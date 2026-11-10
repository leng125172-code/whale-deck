using System.Text.Json;
using Google.Protobuf;
using Microsoft.EntityFrameworkCore;
using WhaleDeck.Application.Development;
using WhaleDeck.Application.Models;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Agent;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed class WorkspaceRetirementPreparationRepository(PlatformDbContext db, TimeProvider clock,
    IWorkspaceLifecycleStore lifecycle) : IWorkspaceRetirementPreparationStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };

    public Task<WorkspaceRetirementPreparationView> BeginAsync(TerminalActor actor, WorkspaceDefinition workspace,
        Guid archiveId, Guid key, CancellationToken token) => Transaction(async () =>
    {
        if (key == Guid.Empty || archiveId == Guid.Empty) throw new ArgumentException("WORKSPACE_RETIREMENT_REQUEST_INVALID");
        var current = await LockWorkspace(workspace.Id, token);
        var request = JsonSerializer.Serialize(new { workspaceId = workspace.Id, archiveId, expectedEpoch = workspace.LifecycleVersion }, Json);
        var replay = await db.OperationJobs.AsNoTracking().SingleOrDefaultAsync(row => row.ActorSubject == actor.Subject && row.IdempotencyKey == key.ToString("D"), token);
        if (replay is not null)
        {
            if (replay.JobType != WorkspaceRetirementPreparations.JobType || replay.ResourceId != workspace.Id ||
                !JobRepository.JsonRequestsEqual(request, replay.RequestJson)) throw new InvalidOperationException("WORKSPACE_RETIREMENT_KEY_CONFLICT");
            return View(await db.WorkspaceRetirementIntents.AsNoTracking().SingleAsync(row => row.Id == replay.Id, token), replay);
        }
        if (current.State != WorkspaceStorageAllocation.Allocated || current.LifecycleState != WorkspaceLifecycle.Stopped ||
            workspace.LifecycleVersion < 2 || workspace.LifecycleVersion % 2 != 0 ||
            JsonSerializer.Serialize(DevelopmentWorkspaceRegistry.Map(current), Json) != JsonSerializer.Serialize(workspace, Json))
            throw new InvalidOperationException("WORKSPACE_RETIREMENT_STATE_CHANGED");
        await lifecycle.RequireIdleAsync(workspace.Id, token);
        if (await db.WorkspaceLifecycleIntents.AnyAsync(row => row.WorkspaceId == workspace.Id && row.FinishedAtUtc == null, token) ||
            await db.WorkspaceRetirementIntents.AnyAsync(row => row.WorkspaceId == workspace.Id && row.FinishedAtUtc == null, token))
            throw new InvalidOperationException("WORKSPACE_RETIREMENT_ALREADY_PENDING");
        _ = await Evidence(workspace, archiveId, token);
        var now = Now();
        var job = new OperationJob { JobType = WorkspaceRetirementPreparations.JobType, ActorSubject = actor.Subject,
            ResourceId = workspace.Id, IdempotencyKey = key.ToString("D"), RequestJson = request, CreatedAtUtc = now, Phase = "Plan" };
        var intent = new WorkspaceRetirementIntent { Id = job.Id, WorkspaceId = workspace.Id, ArchiveId = archiveId,
            ExpectedEpoch = workspace.LifecycleVersion, SnapshotJson = JsonSerializer.Serialize(workspace, Json),
            ExecutionSubject = actor.Subject, LoginBinding = actor.LoginBinding, AvailableAtUtc = now };
        current.LifecycleState = WorkspaceRetirementPreparations.Retiring;
        db.WorkspaceRetirementIntents.Add(intent); db.OperationJobs.Add(job);
        Event(job, "WORKSPACE_RETIREMENT_PREPARATION_QUEUED", now); Audit(actor, intent, "request", now);
        return View(intent, job);
    }, token);

    public async Task<WorkspaceRetirementPreparationView?> GetAsync(Guid workspaceId, Guid retirementId, CancellationToken token)
    {
        var pair = await (from row in db.WorkspaceRetirementIntents.AsNoTracking()
                          join job in db.OperationJobs.AsNoTracking() on row.Id equals job.Id
                          where row.WorkspaceId == workspaceId && row.Id == retirementId
                          select new { Row = row, Job = job }).SingleOrDefaultAsync(token);
        return pair is null ? null : View(pair.Row, pair.Job);
    }

    public Task<WorkspaceRetirementPreparationClaim?> ClaimAsync(CancellationToken token) => Transaction<WorkspaceRetirementPreparationClaim?>(async () =>
    {
        var now = Now();
        var selected = await db.WorkspaceRetirementIntents.AsNoTracking().Where(row => row.FinishedAtUtc == null &&
            row.AvailableAtUtc <= now && (row.LeaseUntilUtc == null || row.LeaseUntilUtc <= now) &&
            (row.CancelRequested || row.PlanJson == null)).OrderBy(row => row.AvailableAtUtc).ThenBy(row => row.Id).FirstOrDefaultAsync(token);
        if (selected is null) return null;
        var workspace = await LockWorkspace(selected.WorkspaceId, token);
        var row = await LockIntent(selected.WorkspaceId, selected.Id, token); var job = await Job(row, token);
        if (row.FinishedAtUtc is not null || row.LeaseUntilUtc > now || row.AvailableAtUtc > now ||
            !row.CancelRequested && row.PlanJson is not null) return null;
        var snapshot = RequireFence(workspace, row);
        if (row.CancelRequested) { Cancel(workspace, row, job, new(row.ExecutionSubject, null, [], row.LoginBinding, row.Id.ToString("N"), null), now); return null; }
        _ = await Evidence(snapshot, row.ArchiveId, token);
        var lease = Guid.NewGuid(); row.Lease = lease; row.LeaseUntilUtc = now.AddSeconds(90);
        job.StartedAtUtc ??= now; job.State = "Running";
        return new(View(row, job), snapshot, new(row.ExecutionSubject, null, [], row.LoginBinding, row.Id.ToString("N"), null), lease);
    }, token);

    public Task RecordPlanAsync(WorkspaceRetirementPreparationClaim claim, string hostPlanJson, CancellationToken token) => Transaction(async () =>
    {
        var workspace = await LockWorkspace(claim.Workspace.Id, token);
        var row = await LockIntent(claim.Workspace.Id, claim.View.RetirementId, token);
        if (row.FinishedAtUtc is not null || row.Lease != claim.Lease || row.LeaseUntilUtc <= Now()) return false;
        var snapshot = RequireFence(workspace, row); var job = await Job(row, token); var now = Now();
        if (row.CancelRequested) { Cancel(workspace, row, job, claim.Actor, now); return true; }
        var evidence = await Evidence(snapshot, row.ArchiveId, token);
        if (!DevelopmentRetirementPlanBinding.Matches(evidence.Request, evidence.Receipt, hostPlanJson))
            throw new InvalidDataException("WORKSPACE_RETIREMENT_PLAN_INVALID");
        row.PlanJson = hostPlanJson; row.PlanExpiresAtUtc = now.AddMinutes(10); row.Lease = null; row.LeaseUntilUtc = null;
        job.Phase = "AwaitingConfirmation"; job.Version++;
        Event(job, "WORKSPACE_RETIREMENT_PREPARATION_READY", now); Audit(claim.Actor, row, "prepared", now);
        return true;
    }, token);

    public Task<WorkspaceRetirementPreparationView> AcknowledgeAsync(TerminalActor actor, Guid workspaceId, Guid retirementId,
        string planSha256, CancellationToken token) => Transaction(async () =>
    {
        var workspace = await LockWorkspace(workspaceId, token); var row = await LockIntent(workspaceId, retirementId, token);
        var job = await Job(row, token);
        if (row.FinishedAtUtc is not null || row.CancelRequested || row.PlanJson is null || row.PlanExpiresAtUtc <= Now() ||
            !DevelopmentLifecycleBinding.Digest(planSha256) || Digest(row.PlanJson) != planSha256)
            throw new InvalidOperationException("WORKSPACE_RETIREMENT_PLAN_CHANGED");
        _ = RequireFence(workspace, row); _ = await Evidence(ReadSnapshot(row), row.ArchiveId, token);
        if (row.LeaseUntilUtc > Now()) throw new InvalidOperationException("WORKSPACE_RETIREMENT_WORKER_BUSY");
        if (row.ConfirmedAtUtc is not null) return View(row, job);
        // This acknowledges the preparation only. It cannot start a host task,
        // grant a capability, change source, or release budget. A future
        // execution workflow must reauthorize and revalidate all live facts.
        row.ConfirmedAtUtc = Now(); row.ExecutionSubject = actor.Subject; row.LoginBinding = actor.LoginBinding;
        job.Phase = "Prepared"; job.Version++;
        Event(job, "WORKSPACE_RETIREMENT_PREPARATION_ACKNOWLEDGED", Now()); Audit(actor, row, "acknowledge", Now());
        return View(row, job);
    }, token);

    public Task<WorkspaceRetirementPreparationView> CancelAsync(TerminalActor actor, Guid workspaceId, Guid retirementId, CancellationToken token) => Transaction(async () =>
    {
        var workspace = await LockWorkspace(workspaceId, token); var row = await LockIntent(workspaceId, retirementId, token);
        var job = await Job(row, token);
        if (row.FinishedAtUtc is not null) return View(row, job);
        _ = RequireFence(workspace, row);
        if (!row.CancelRequested)
        {
            row.CancelRequested = true; row.AvailableAtUtc = Now(); job.CancelRequestedAtUtc = Now();
            job.Version++; Event(job, "WORKSPACE_RETIREMENT_PREPARATION_CANCEL_REQUESTED", Now()); Audit(actor, row, "cancel-request", Now());
        }
        if (row.LeaseUntilUtc is null || row.LeaseUntilUtc <= Now()) Cancel(workspace, row, job, actor, Now());
        return View(row, job);
    }, token);

    private async Task<(WorkspaceArchiveRequest Request, WorkspaceArchiveReceipt Receipt)> Evidence(WorkspaceDefinition workspace, Guid archiveId, CancellationToken token)
    {
        var archive = await db.WorkspaceArchiveIntents.AsNoTracking().SingleOrDefaultAsync(row => row.Id == archiveId && row.WorkspaceId == workspace.Id, token);
        var job = await db.OperationJobs.AsNoTracking().SingleOrDefaultAsync(row => row.Id == archiveId && row.JobType == WorkspaceArchives.JobType, token);
        if (archive is null || job?.State != "Succeeded" || job.CompletedAtUtc is null || archive.FinishedAtUtc is null ||
            archive.Lease is not null || archive.LeaseUntilUtc is not null || archive.Phase != "verify" || archive.CancelRequested ||
            archive.ExpectedEpoch != workspace.LifecycleVersion || archive.PlanJson is null || archive.CaptureJson is null || archive.VerifyJson is null ||
            !JobRepository.JsonRequestsEqual(archive.SnapshotJson, JsonSerializer.Serialize(workspace, Json)))
            throw new InvalidOperationException("WORKSPACE_RETIREMENT_ARCHIVE_NOT_VERIFIED");
        var steps = await db.WorkspaceArchiveSteps.AsNoTracking().Where(row => row.ArchiveId == archiveId).ToArrayAsync(token);
        if (steps.Length != 3) throw new InvalidDataException("WORKSPACE_RETIREMENT_ARCHIVE_PROOF_INVALID");
        WorkspaceArchiveRequest? verifiedRequest = null; WorkspaceArchiveReceipt? verifiedReceipt = null;
        foreach (var phase in new[] { "plan", "capture", "verify" })
        {
            var step = steps.SingleOrDefault(row => row.Phase == phase);
            if (step?.DispatchedAtUtc is null || step.SettledAtUtc is null || step.ProofJson is null)
                throw new InvalidDataException("WORKSPACE_RETIREMENT_ARCHIVE_PROOF_INVALID");
            var task = new WorkspaceArchiveTask(step.Id, archiveId, phase, archive.ExpectedEpoch,
                phase == "plan" ? "" : Property(archive.PlanJson, "planSha256"),
                phase == "verify" ? Property(archive.CaptureJson, "receiptSha256") : "");
            var request = AgentGateway.BuildArchiveRequest(new(workspace.OwnerSubject, null, [], new string('a', 64), "retirement-prerequisite", null), workspace, task);
            var proof = JsonSerializer.Deserialize<WorkspaceArchiveTaskProof>(step.ProofJson, Json) ?? throw new InvalidDataException("WORKSPACE_RETIREMENT_ARCHIVE_PROOF_INVALID");
            var receipt = new WorkspaceArchiveReceipt { TaskId = proof.TaskId.ToString("D"), RequestSha256 = proof.RequestSha256,
                Status = proof.Status, Code = proof.Code ?? "", ResultJson = proof.ResultJson is null ? ByteString.Empty : ByteString.CopyFromUtf8(proof.ResultJson) };
            var saved = phase == "plan" ? archive.PlanJson : phase == "capture" ? archive.CaptureJson : archive.VerifyJson;
            if (step.RequestJson != DevelopmentArchiveBinding.HostInput(request) || proof.Status != "Succeeded" ||
                proof.ResultJson is null || !JobRepository.JsonRequestsEqual(saved, proof.ResultJson) || !DevelopmentArchiveBinding.Matches(request, receipt))
                throw new InvalidDataException("WORKSPACE_RETIREMENT_ARCHIVE_PROOF_INVALID");
            if (phase == "verify") { verifiedRequest = request; verifiedReceipt = receipt; }
        }
        if (!JobRepository.JsonRequestsEqual(archive.CaptureJson, archive.VerifyJson)) throw new InvalidDataException("WORKSPACE_RETIREMENT_ARCHIVE_PROOF_INVALID");
        return (verifiedRequest!, verifiedReceipt!);
    }
    private static string Property(string json, string name) { using var value = JsonDocument.Parse(json); return value.RootElement.GetProperty(name).GetString()!; }
    private static string Digest(string json) => Property(json, "retirementPlanSha256");
    private static WorkspaceDefinition ReadSnapshot(WorkspaceRetirementIntent row) => JsonSerializer.Deserialize<WorkspaceDefinition>(row.SnapshotJson, Json)
        ?? throw new InvalidDataException("WORKSPACE_RETIREMENT_SNAPSHOT_INVALID");
    private static WorkspaceDefinition RequireFence(DevelopmentWorkspaceRecord current, WorkspaceRetirementIntent row)
    {
        var snapshot = ReadSnapshot(row);
        var live = DevelopmentWorkspaceRegistry.Map(current) with { LifecycleState = WorkspaceLifecycle.Stopped };
        if (current.LifecycleState != WorkspaceRetirementPreparations.Retiring || snapshot.Id != row.WorkspaceId ||
            snapshot.LifecycleVersion != row.ExpectedEpoch || snapshot.LifecycleState != WorkspaceLifecycle.Stopped ||
            !JobRepository.JsonRequestsEqual(row.SnapshotJson, JsonSerializer.Serialize(live, Json)))
            throw new InvalidOperationException("WORKSPACE_RETIREMENT_FENCE_CHANGED");
        return snapshot;
    }
    private void Cancel(DevelopmentWorkspaceRecord workspace, WorkspaceRetirementIntent row, OperationJob job, TerminalActor actor, DateTimeOffset now)
    {
        _ = RequireFence(workspace, row); workspace.LifecycleState = WorkspaceLifecycle.Stopped;
        row.FinishedAtUtc = job.CompletedAtUtc = now; row.Lease = null; row.LeaseUntilUtc = null;
        job.State = "Canceled"; job.Phase = "PreparationCanceled"; job.Version++;
        job.ResultJson = JsonSerializer.Serialize(new { retirementId = row.Id, retainsSource = true, reservesBudget = true, physicalExecution = false }, Json);
        Event(job, "WORKSPACE_RETIREMENT_PREPARATION_CANCELED", now); Audit(actor, row, "canceled", now);
    }
    private async Task<DevelopmentWorkspaceRecord> LockWorkspace(Guid id, CancellationToken token) =>
        (await db.DevelopmentWorkspaces.FromSqlInterpolated($"SELECT * FROM development_workspaces WHERE id = {id} FOR UPDATE").ToArrayAsync(token))
            .SingleOrDefault() ?? throw new KeyNotFoundException("WORKSPACE_NOT_FOUND");
    private async Task<WorkspaceRetirementIntent> LockIntent(Guid workspaceId, Guid id, CancellationToken token) =>
        (await db.WorkspaceRetirementIntents.FromSqlInterpolated($"SELECT * FROM development_workspace_retirements WHERE id = {id} AND workspace_id = {workspaceId} FOR UPDATE").ToArrayAsync(token))
            .SingleOrDefault() ?? throw new KeyNotFoundException("WORKSPACE_RETIREMENT_NOT_FOUND");
    private Task<OperationJob> Job(WorkspaceRetirementIntent row, CancellationToken token) => db.OperationJobs.SingleAsync(job => job.Id == row.Id && job.JobType == WorkspaceRetirementPreparations.JobType, token);
    private static WorkspaceRetirementPreparationView View(WorkspaceRetirementIntent row, OperationJob job) => new(row.Id, row.WorkspaceId, row.ArchiveId,
        job.State, job.Phase, job.ErrorCode, row.PlanJson, row.PlanExpiresAtUtc, row.ConfirmedAtUtc, row.CancelRequested, row.FinishedAtUtc);
    private DateTimeOffset Now()
    {
        // PostgreSQL stores timestamp values at microsecond precision. Return
        // exactly the value a replay will read, including concurrent ACKs.
        var now = clock.GetUtcNow();
        return new(now.UtcTicks - now.UtcTicks % TimeSpan.TicksPerMicrosecond, TimeSpan.Zero);
    }
    private void Event(OperationJob job, string code, DateTimeOffset now) => db.OperationJobEvents.Add(new()
    { JobId = job.Id, Sequence = job.Version + 1, State = job.State, Phase = job.Phase, MessageCode = code, OccurredAtUtc = now });
    private void Audit(TerminalActor actor, WorkspaceRetirementIntent row, string action, DateTimeOffset now) => db.AuditEvents.Add(new()
    { ActorSubject = actor.Subject, Action = "development.workspace.retirement.prepare." + action, TargetType = "workspace", TargetId = row.WorkspaceId.ToString("D"),
        JobId = row.Id, TraceId = actor.TraceId, SourceIp = actor.SourceIp, OccurredAtUtc = now });
    private Task<T> Transaction<T>(Func<Task<T>> action, CancellationToken token) => db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
    {
        db.ChangeTracker.Clear(); await using var transaction = await db.Database.BeginTransactionAsync(token);
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='3s'; SELECT pg_advisory_xact_lock(41307420)", token);
        var value = await action(); await db.SaveChangesAsync(token); await transaction.CommitAsync(token); return value;
    });
}
