using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Npgsql;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Agent;
using WhaleDeck.Infrastructure.Persistence;

namespace WhaleDeck.IntegrationTests;

public sealed partial class NativeNotificationDispatchTests
{
    private static readonly System.Text.Json.JsonSerializerOptions AclResultJson = new(System.Text.Json.JsonSerializerDefaults.Web);
    private static readonly string[] AclFenceTables = ["container_file_upload_operations", "container_directory_operations", "container_copy_intents", "container_move_intents", "container_permission_intents", "container_ownership_intents", "container_recursive_permission_intents", "container_recursive_ownership_intents"];
    /// <summary>Real isolated PostgreSQL/MAC/transactions/triggers. Receipts are synthetic, not Root or UI evidence.</summary>
    private static async Task ContainerAclBoundaries(DbContextOptions<PlatformDbContext> options)
    {
        await using var db = new PlatformDbContext(options); await using var second = new PlatformDbContext(options);
        var keyPath = Path.GetTempFileName(); await File.WriteAllBytesAsync(keyPath, RandomNumberGenerator.GetBytes(32));
        var protector = new FilePlanProtector(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:CapabilityKeyFile"] = keyPath }).Build());
        var clock = new AclClock(); var repo = new ContainerAclRepository(db, protector, clock); var other = new ContainerAclRepository(second, protector, clock);
        var actor = new TerminalActor("native-acl-admin", null, [], new('A', 64), "native-acl-trace", null);
        var ids = new List<Guid>(); var uploadIds = new List<Guid>(); var directoryIds = new List<Guid>();
        ContainerAclIntent Row(string? path = null, string? subject = null, uint namedId = 2200, bool directory = false, bool clear = false)
        {
            var owner = subject ?? actor.Subject; var key = Guid.NewGuid(); var id = ContainerAclWorkflowValidation.IntentId(owner, actor.LoginBinding, key); ids.Add(id);
            var now = ContainerAclWorkflowValidation.Time(clock.GetUtcNow());
            var command = new ContainerAclCommandDto(new(new('a', 64), "777:2026-10-30T01:02:03.123456789Z", path ?? "work/acl-" + id.ToString("N")), directory ? "default" : "access", clear ? [] : Named(namedId), new('B', 64));
            var binding = new ContainerAclRequestBinding(id, key, owner, actor.LoginBinding, actor.TraceId, ContainerAclWorkflowValidation.Owner(owner, actor.LoginBinding), command, now, now.AddMinutes(15));
            return new() { Id = id, IdempotencyKey = key, ActorSubject = owner, LoginBinding = actor.LoginBinding, TraceId = actor.TraceId, OwnerHash = binding.Owner,
                ContainerId = command.Location.ContainerId, InstanceId = command.Location.InstanceId, RelativePath = command.Location.Path,
                CommandJson = ContainerAclWorkflowValidation.Serialize(command), RequestHash = protector.FingerprintAclRequest(binding), CreatedAtUtc = now,
                PlanningExpiresAtUtc = binding.PlanningExpiresAtUtc, NextAttemptAtUtc = now };
        }
        ContainerAclEntryDto[] BaseAcl() => [new("user_obj",null,6),new("group_obj",null,4),new("other",null,0)];
        ContainerAclEntryDto[] Named(uint id = 2200) => [new("user_obj",null,6),new("user",id,0),new("group_obj",null,4),new("mask",null,6),new("other",null,0)];
        ContainerAclPlanDto Plan(ContainerAclIntent row)
        {
            var command=ContainerAclWorkflowValidation.ReadCommand(row);var directory=command.AclType=="default";
            return new(1,command,new('C',64),new(ulong.MaxValue,33),new('D',64),1000,1001,new('E',64),directory?"Directory":"File",
                new(ulong.MaxValue,101,directory?16800U:33184U,1000,1001,directory?2U:1U,10737418247,1700000000123456789),
                1700000000987654321,new('F',64),false,BaseAcl(),command.Entries.Count==0?Named():[],command.AclType=="access"?432U:416U);
        }
        ContainerAclConfirmation Claims(ContainerAclIntent row) => new(row.Id,row.ActorSubject,row.LoginBinding,row.PlanHash!,row.ReadyExpiresAtUtc!.Value);
        ContainerAclReceiptDto Receipt(ContainerAclIntent row,string state,bool current=true)
        {
            var plan=ContainerAclWorkflowValidation.ReadPlan(row);
            return new(row.ExecutionJobId!.Value,row.ContainerId,row.InstanceId,new('E',64),new('C',64),state,state=="Completed"?new('9',64):"",
                state=="Completed"&&current,false,plan.Kind,state=="Unknown"?null:state=="Completed",false,state!="Aborted",state=="Completed",
                state=="Completed"?new('8',64):"",plan.Command.AclType,plan.TargetMode);
        }
        async Task<ContainerAclJobResults.Summary> JobFacts(ContainerAclIntent row, bool execution, string state)
        {
            var jobId = execution ? row.ExecutionJobId!.Value : row.Id;
            var job = await second.OperationJobs.AsNoTracking().SingleAsync(j => j.Id == jobId);
            Assert.NotNull(job.ResultJson);
            var projected = await new OperationService(new JobRepository(second)).FindAsync(jobId, default);
            Assert.Equal(ContainerAclJobResults.ForJob(job), projected!.ResultJson);
            Assert.NotNull(projected.ResultJson);
            var value = System.Text.Json.JsonSerializer.Deserialize<ContainerAclJobResults.Summary>(job.ResultJson,
                AclResultJson)!;
            Assert.Equal(row.Id, value.IntentId); Assert.Equal(state, value.State);
            Assert.Equal(execution ? "execution" : "planning", value.Stage);
            Assert.Equal(ContainerAclWorkflowValidation.ReadCommand(row).AclType, value.AclType);
            using var document = System.Text.Json.JsonDocument.Parse(job.ResultJson);
            Assert.Equal(15, document.RootElement.EnumerateObject().Count());
            foreach (var secret in new[] { row.RelativePath, row.InstanceId, row.ActorSubject, row.LoginBinding, row.OwnerHash, row.RequestHash, row.PlanHash, row.ExecutionHash, row.ReceiptHash })
                if (secret is not null) Assert.DoesNotContain(secret, job.ResultJson, StringComparison.Ordinal);
            return value;
        }
        async Task<ContainerAclIntent> Ready(ContainerAclIntent row)
        {
            await repo.EnqueueAsync(row, default); var claimed = Assert.IsType<ContainerAclIntent>(await repo.ClaimAsync(clock.GetUtcNow(), default)); Assert.Equal(row.Id, claimed.Id);
            Assert.True(await repo.MarkPlanningAsync(claimed, default)); await repo.FinishPlanningAsync(claimed, Plan(row), "CONTAINER_ACL_READY", default);
            var ready = (await repo.FindAsync(row.Id, default))!;
            var facts = await JobFacts(ready, false, "Ready");
            Assert.Null(facts.AclConfirmed); Assert.Null(facts.ReceiptState); Assert.False(facts.HasPublishedVersion);
            return ready;
        }
        async Task<ContainerAclIntent> Pending(ContainerAclIntent row)
        {
            await repo.ConfirmAsync(actor, Claims(row), row.Version, default);
            var claimed = Assert.IsType<ContainerAclIntent>(await repo.ClaimAsync(clock.GetUtcNow(), default)); Assert.Equal(row.Id, claimed.Id); return claimed;
        }
        async Task<ContainerAclIntent> Applying(ContainerAclIntent row)
        { var claimed = await Pending(row); Assert.Equal(ContainerFileUploadStartResult.Started, await repo.StartedAsync(claimed, clock.GetUtcNow(), default)); return claimed; }
        async Task Rejected(FormattableString sql, string message)
        { var failure = await Assert.ThrowsAsync<PostgresException>(() => second.Database.ExecuteSqlInterpolatedAsync(sql)); Assert.Equal(message, failure.MessageText); }
        async Task PeerSeeds()
        {
            var now = ContainerFileUploadIntentValidation.CanonicalTime(clock.GetUtcNow()); var id = Guid.NewGuid(); uploadIds.Add(id); const string subject = "native-acl-upload-peer";
            var target = new ContainerFileUploadTargetDto(new(new('d', 64), "888:2026-10-30T01:02:03Z", "work/upload-" + id.ToString("N")), new('B', 64), new('C', 64), "ABSENT", 0);
            var binding = new ContainerFileUploadBinding(id, Guid.NewGuid(), subject, actor.LoginBinding, actor.TraceId, target, 0, Convert.ToHexString(SHA256.HashData([])), new('F', 64), now, now.AddMinutes(15));
            var upload = new ContainerFileUploadOperation { Id = id, IdempotencyKey = binding.IdempotencyKey, ActorSubject = subject, LoginBinding = actor.LoginBinding, TraceId = actor.TraceId,
                OwnerHash = ContainerFileUploadIntentValidation.Owner(subject, actor.LoginBinding), ContainerId = target.Location.ContainerId, InstanceId = target.Location.InstanceId,
                RelativePath = target.Location.Path, TargetProof = target.TargetProof, ExpectedParentVersion = target.ExpectedParentVersion, ExpectedVersion = "ABSENT", SizeBytes = 0,
                Sha256 = binding.Sha256, PlanHash = binding.PlanHash, RequestHash = protector.FingerprintContainerUpload(binding), CreatedAtUtc = now, ExpiresAtUtc = binding.ExpiresAtUtc, NextAttemptAtUtc = now };
            await new ContainerFileUploadRepository(second, protector).EnqueueAsync(upload, default);
            id = Guid.NewGuid(); directoryIds.Add(id); const string directorySubject = "native-acl-directory-peer";
            var plan = new ContainerDirectoryPlanDto(1, new(new(new('e', 64), "999:2026-10-30T01:02:03Z", "work/directory-" + id.ToString("N")), "0750"), new('B', 64), new(1, 2), new('C', 64), 1000, 1001, new('D', 64));
            var frozen = new ContainerDirectoryBindingDto(id, Guid.NewGuid(), directorySubject, actor.LoginBinding, actor.TraceId, ContainerDirectoryWorkflowValidation.Owner(directorySubject, actor.LoginBinding), plan, new('E', 64), now, now.AddMinutes(15));
            var directory = new ContainerDirectoryOperation { Id = id, IdempotencyKey = frozen.IdempotencyKey, ActorSubject = directorySubject, LoginBinding = actor.LoginBinding, TraceId = actor.TraceId,
                OwnerHash = frozen.Owner, ContainerId = plan.Command.Location.ContainerId, InstanceId = plan.Command.Location.InstanceId, RelativePath = plan.Command.Location.Path,
                PlanJson = System.Text.Json.JsonSerializer.Serialize(plan, ContainerDirectoryWorkflowValidation.Json), PlanHash = frozen.PlanHash, RequestHash = protector.FingerprintDirectory(frozen),
                CreatedAtUtc = now, ExpiresAtUtc = frozen.ExpiresAtUtc, NextAttemptAtUtc = now };
            await new ContainerDirectoryRepository(second, protector).EnqueueAsync(directory, now.AddMinutes(5), default);
        }
        ContainerPermissionIntent PermissionPeer(string path)
        {
            const string subject = "native-acl-chmod-reservation"; var key = Guid.NewGuid();
            var id = ContainerPermissionWorkflowValidation.IntentId(subject, actor.LoginBinding, key);
            var now = ContainerPermissionWorkflowValidation.Time(clock.GetUtcNow());
            var command = new ContainerPermissionCommandDto(new(new('a', 64), "777:2026-10-30T01:02:03.123456789Z", path), "0600", new('B', 64));
            var binding = new ContainerPermissionRequestBinding(id, key, subject, actor.LoginBinding, actor.TraceId,
                ContainerPermissionWorkflowValidation.Owner(subject, actor.LoginBinding), command, now, now.AddMinutes(15));
            return new() { Id = id, IdempotencyKey = key, ActorSubject = subject, LoginBinding = actor.LoginBinding,
                TraceId = actor.TraceId, OwnerHash = binding.Owner, ContainerId = command.Location.ContainerId, InstanceId = command.Location.InstanceId,
                RelativePath = path, CommandJson = ContainerPermissionWorkflowValidation.Serialize(command), RequestHash = protector.FingerprintPermissionRequest(binding),
                CreatedAtUtc = now, PlanningExpiresAtUtc = binding.PlanningExpiresAtUtc, NextAttemptAtUtc = now };
        }
        // These raw clones exercise ONLY SQL exclusion, not application MAC acceptance.
        // Every clone lives in an explicit rolled-back transaction with its own jobs.
        async Task ClonePeer(string table, Guid seed)
        {
            Assert.Contains(table, AclFenceTables);
            var id = Guid.NewGuid(); var execution = Guid.NewGuid(); var key = Guid.NewGuid(); var now = clock.GetUtcNow();
            second.OperationJobs.AddRange(new OperationJob { Id = id, ActorSubject = "native-acl-fence", IdempotencyKey = key.ToString("D"), JobType = "native.permission.fence" },
                new OperationJob { Id = execution, ActorSubject = "native-acl-fence", IdempotencyKey = execution.ToString("D"), JobType = "native.permission.fence" });
            await second.SaveChangesAsync();
            // Table identifiers are from the fixed local whitelist above; all values are parameters.
            var executionPatch = table is "container_copy_intents" or "container_move_intents" or "container_permission_intents" or "container_ownership_intents" or "container_recursive_permission_intents" or "container_recursive_ownership_intents" ? " || jsonb_build_object('execution_job_id',@execution)" : "";
            if (table is "container_recursive_permission_intents" or "container_recursive_ownership_intents")
                executionPatch += " || jsonb_build_object('receipt_json',NULL,'root_state',NULL,'completed_count',NULL,'changed_count',NULL,'aborted_count',NULL,'not_started_count',NULL,'unknown_count',NULL,'outcome_digest',NULL,'binding_current',NULL,'history_available',NULL,'history_may_exist',FALSE,'history_verified',FALSE,'history_digest',NULL)";
            var sql = "INSERT INTO " + table + " SELECT (jsonb_populate_record(NULL::" + table + ",to_jsonb(t) || jsonb_build_object('id',@id,'idempotency_key',@key,'completed_at_utc',NULL,'lease_token',NULL,'lease_expires_at_utc',NULL,'publish_barrier',TRUE,'root_started_at_utc',@now,'state',@state,'published_version',NULL,'acknowledged_by',NULL,'acknowledgement_json',NULL)" + executionPatch + ")).* FROM " + table + " t WHERE id=@seed";
            Assert.Equal(1, await second.Database.ExecuteSqlRawAsync(sql, new NpgsqlParameter("id", id), new NpgsqlParameter("key", key), new NpgsqlParameter("now", now), new NpgsqlParameter("execution", execution), new NpgsqlParameter("seed", seed), new NpgsqlParameter("state", table == "container_file_upload_operations" ? "Ready" : "Pending")));
        }
        try
        {
            Assert.False(db.Database.HasPendingModelChanges());
            Assert.False(await db.FilePathLocks.AnyAsync());
            Assert.False(await db.ContainerFileUploadOperations.AnyAsync(r => r.PublishBarrier));
            Assert.False(await db.ContainerDirectoryOperations.AnyAsync(r => r.PublishBarrier));
            Assert.False(await db.ContainerCopyIntents.AnyAsync(r => r.PublishBarrier));
            Assert.False(await db.ContainerMoveIntents.AnyAsync(r => r.PublishBarrier));
            Assert.False(await db.ContainerAclIntents.AnyAsync(r => r.PublishBarrier));
            // No root call: actual repositories must reserve ancestor/descendant paths in both directions.
            foreach (var relation in new[] { "same", "ancestor", "descendant" })
            {
                var prefix = "work/acl-reservation-" + Guid.NewGuid().ToString("N"); var path = prefix + "/item";
                var peerPath = relation == "ancestor" ? prefix : relation == "descendant" ? path + "/child" : path;
                var owner = Row(path); await repo.EnqueueAsync(owner, default);
                var permission = PermissionPeer(peerPath); var modes = new ContainerPermissionRepository(second, protector, clock);
                var denied = await Assert.ThrowsAsync<InvalidOperationException>(() => modes.EnqueueAsync(permission, default));
                Assert.Equal("CONTAINER_PERMISSION_TARGET_BUSY", denied.Message);
                await repo.CancelAsync(actor, owner.Id, default);
                await modes.EnqueueAsync(permission, default);
                var inverseOwner = Row(path);
                denied = await Assert.ThrowsAsync<InvalidOperationException>(() => repo.EnqueueAsync(inverseOwner, default));
                Assert.Equal("CONTAINER_ACL_TARGET_BUSY", denied.Message);
                var modeActor = actor with { Subject = permission.ActorSubject };
                await modes.CancelAsync(modeActor, permission.Id, default);
            }
            foreach (var allowEmpty in new[] {false,true})
                Assert.Equal(allowEmpty,await db.Database.SqlQuery<bool>($"SELECT whaledeck_acl_entries_valid('[]'::jsonb,{allowEmpty}) AS \"Value\"").SingleAsync());
            var namedJson=ContainerAclWorkflowValidation.Serialize(Named(0));
            Assert.True(await db.Database.SqlQuery<bool>($"SELECT whaledeck_acl_entries_valid({namedJson}::jsonb,FALSE) AS \"Value\"").SingleAsync());
            Assert.Equal(432,await db.Database.SqlQuery<int>($"SELECT whaledeck_acl_mode({namedJson}::jsonb) AS \"Value\"").SingleAsync());
            foreach (var invalid in new[] {
                namedJson.Replace("\"Id\":0","\"Id\":null"), namedJson.Replace("\"Permissions\":0","\"Permissions\":false"),
                namedJson.Replace("\"Id\":0","\"Id\":4294967295"), ContainerAclWorkflowValidation.Serialize(Named().Where(e=>e.Tag!="mask").ToArray()),
                ContainerAclWorkflowValidation.Serialize(Named().Reverse().ToArray()) })
                Assert.False(await db.Database.SqlQuery<bool>($"SELECT whaledeck_acl_entries_valid({invalid}::jsonb,FALSE) AS \"Value\"").SingleAsync());
            await PeerSeeds();
            var original = Row(); var ready = await Ready(original);
            await Rejected($"UPDATE container_acl_intents SET relative_path='elsewhere' WHERE id={ready.Id}", "container_acl_request_immutable");
            await Rejected($"UPDATE container_acl_intents SET plan_hash={new string('A', 64)} WHERE id={ready.Id}", "container_acl_plan_immutable");
            var confirmed = await Task.WhenAll(repo.ConfirmAsync(actor, Claims(ready), ready.Version, default), other.ConfirmAsync(actor, Claims(ready), ready.Version, default));
            Assert.Equal(confirmed[0].ExecutionJobId, confirmed[1].ExecutionJobId); Assert.Equal(2, await db.OperationJobs.CountAsync(j => j.Id == ready.Id || j.Id == confirmed[0].ExecutionJobId));
            var apply = Assert.IsType<ContainerAclIntent>(await repo.ClaimAsync(clock.GetUtcNow(), default));
            Assert.Equal(ContainerFileUploadStartResult.Started, await repo.StartedAsync(apply, clock.GetUtcNow(), default));
            await Rejected($"UPDATE container_acl_intents SET root_started_at_utc=NULL WHERE id={apply.Id}", "container_acl_started_immutable");
            Assert.True(await repo.FinishAsync(apply, "Succeeded", "CONTAINER_ACL_COMPLETED", Receipt(apply, "Completed"), default));
            var complete = (await other.FindAsync(ready.Id, default))!; Assert.True(complete.AclConfirmed); Assert.True(complete.HistoryMayExist); Assert.True(complete.HistoryVerified); Assert.Equal(new string('8', 64), complete.HistoryDigest);
            var completedFacts = await JobFacts(complete, true, "Succeeded");
            Assert.True(completedFacts.AclConfirmed); Assert.True(completedFacts.HistoryVerified); Assert.True(completedFacts.HasPublishedVersion);
            Assert.Equal("Completed", completedFacts.ReceiptState);
            Assert.Equal("Ready", (await JobFacts(complete, false, "Ready")).State); // Original plan snapshot never becomes an execution claim.
            await Rejected($"UPDATE container_acl_intents SET acl_confirmed=false WHERE id={ready.Id}", "container_acl_fact_immutable");
            await Rejected($"UPDATE container_acl_intents SET history_verified=false WHERE id={ready.Id}", "container_acl_fact_immutable");
            await Rejected($"UPDATE container_acl_intents SET history_digest=NULL WHERE id={ready.Id}", "container_acl_fact_immutable");
            await Rejected($"UPDATE container_acl_intents SET version=version+1 WHERE id={ready.Id}", "container_acl_terminal_immutable");
            await Rejected($"DELETE FROM container_acl_intents WHERE id={ready.Id}", "container_acl_permanent_intent");
            Assert.True(ContainerAclWorkflowValidation.HasValidBinding(complete,protector)); Assert.NotNull(complete.ReceiptHash);
            await Rejected($"UPDATE container_acl_intents SET receipt_hash={new string('A',64)} WHERE id={ready.Id}","container_acl_fact_immutable");
            await Rejected($"UPDATE container_acl_intents SET receipt_json='{{}}' WHERE id={ready.Id}","container_acl_fact_immutable");
            var rootTarget = await Applying(await Ready(Row(namedId: 0)));
            Assert.True(await repo.FinishAsync(rootTarget, "Succeeded", "CONTAINER_ACL_COMPLETED", Receipt(rootTarget, "Completed"), default));
            var rootSaved = (await other.FindAsync(rootTarget.Id, default))!;
            Assert.Equal(0U, ContainerAclWorkflowValidation.ReadCommand(rootSaved).Entries[1].Id);
            Assert.Equal(0U, ContainerAclWorkflowValidation.ReadCommand(rootSaved).Entries[1].Permissions);
            foreach (var clear in new[] {false,true}) {
                var defaults=await Applying(await Ready(Row(directory:true,clear:clear)));
                Assert.True(await repo.FinishAsync(defaults,"Succeeded","CONTAINER_ACL_COMPLETED",Receipt(defaults,"Completed"),default));
                var saved=(await other.FindAsync(defaults.Id,default))!;
                Assert.Equal(416U,ContainerAclWorkflowValidation.ReadReceipt(saved)!.TargetMode); Assert.True(ContainerAclWorkflowValidation.HasValidBinding(saved,protector));
            }
            // Ten-way SQL gates: no raw write bypasses the application checks.
            var seeds = new Dictionary<string, Guid> { ["container_file_upload_operations"] = uploadIds.Single(), ["container_directory_operations"] = directoryIds.Single(),
                ["container_copy_intents"] = await db.ContainerCopyIntents.Where(r => r.ExecutionJobId != null).Select(r => r.Id).FirstAsync(),
                ["container_move_intents"] = await db.ContainerMoveIntents.Where(r => r.ExecutionJobId != null).Select(r => r.Id).FirstAsync(),
                ["container_permission_intents"] = await db.ContainerPermissionIntents.Where(r => r.ExecutionJobId != null).Select(r => r.Id).FirstAsync(),
                ["container_ownership_intents"] = await db.ContainerOwnershipIntents.Where(r => r.ExecutionJobId != null).Select(r => r.Id).FirstAsync(),
                ["container_recursive_permission_intents"] = await db.ContainerRecursivePermissionIntents.Where(r => r.ExecutionJobId != null).Select(r => r.Id).FirstAsync(),
                ["container_recursive_ownership_intents"] = await db.ContainerRecursiveOwnershipIntents.Where(r => r.ExecutionJobId != null).Select(r => r.Id).FirstAsync() };
            var blocked = await Applying(await Ready(Row())); var pathHash = FileManagementService.Hash("native-acl-host:" + blocked.Id);
            await Rejected($"INSERT INTO file_path_locks(path_hash,operation_id,scope_kind) VALUES ({pathHash},{blocked.Id},'Legacy')", "FILES_TARGET_BUSY");
            foreach (var pair in seeds)
            {
                await using var transaction = await second.Database.BeginTransactionAsync();
                var error = await Record.ExceptionAsync(() => ClonePeer(pair.Key, pair.Value));
                Assert.NotNull(error); Assert.Equal(PostgresErrorCodes.ObjectNotInPrerequisiteState, Assert.IsType<PostgresException>(error.GetBaseException()).SqlState);
                await transaction.RollbackAsync(); second.ChangeTracker.Clear();
            }
            var uncertain = Receipt(blocked, "Unknown") with { HistoryVerified = true, HistoryDigest = new('7', 64) };
            Assert.True(await repo.FinishAsync(blocked, "Unknown", "CONTAINER_ACL_RESULT_UNKNOWN", uncertain, default));
            var unknown = (await other.FindAsync(blocked.Id, default))!; Assert.True(unknown.PublishBarrier); Assert.Null(unknown.AclConfirmed); Assert.True(unknown.HistoryVerified);
            var unknownFacts = await JobFacts(unknown, true, "Unknown");
            Assert.Null(unknownFacts.AclConfirmed); Assert.Equal("Unknown", unknownFacts.ReceiptState); Assert.False(unknownFacts.HasPublishedVersion);
            Assert.Null(await repo.ClaimAsync(clock.GetUtcNow(), default));
            await Rejected($"UPDATE container_acl_intents SET history_may_exist=false WHERE id={unknown.Id}", "container_acl_fact_immutable");
            var admin = actor with { Subject = "native-current-permission-admin", LoginBinding = new('F', 64) };
            await other.AcknowledgeAsync(admin, unknown.Id, unknown.Version, uncertain, default);
            var ack = (await repo.FindAsync(unknown.Id, default))!; Assert.Equal("Acknowledged", ack.State); Assert.False(ack.PublishBarrier); Assert.Null(ack.PublishedVersion);
            Assert.Null(ack.AclConfirmed); Assert.True(ack.HistoryVerified); Assert.Equal(admin.Subject, ack.AcknowledgedBy);
            var ackFacts = await JobFacts(ack, true, "Acknowledged");
            Assert.Null(ackFacts.AclConfirmed); Assert.Equal("Unknown", ackFacts.ReceiptState); Assert.True(ackFacts.HistoryVerified);
            Assert.Equal(unknown.ReceiptHash,ack.ReceiptHash); Assert.Equal(unknown.ReceiptJson,ack.ReceiptJson);
            Assert.True(ContainerAclWorkflowValidation.HasValidBinding(ack,protector));
            Assert.Equal("Failed", (await db.OperationJobs.AsNoTracking().SingleAsync(j => j.Id == ack.ExecutionJobId)).State);
            var inverse = await Pending(await Ready(Row()));
            foreach (var pair in seeds)
            {
                await using var transaction = await second.Database.BeginTransactionAsync(); await ClonePeer(pair.Key, pair.Value);
                var error = await Assert.ThrowsAsync<PostgresException>(() => second.Database.ExecuteSqlInterpolatedAsync($"UPDATE container_acl_intents SET state='Applying',root_started_at_utc={clock.GetUtcNow()},publish_barrier=true WHERE id={inverse.Id}"));
                Assert.Equal("CONTAINER_ACL_TARGET_BUSY", error.MessageText); await transaction.RollbackAsync(); second.ChangeTracker.Clear();
            }
            await second.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO file_path_locks(path_hash,operation_id,scope_kind) VALUES ({pathHash},{inverse.Id},'Legacy')");
            Assert.Equal(ContainerFileUploadStartResult.Busy, await repo.StartedAsync(inverse, clock.GetUtcNow(), default));
            await Rejected($"UPDATE container_acl_intents SET state='Applying',root_started_at_utc={clock.GetUtcNow()},publish_barrier=true WHERE id={inverse.Id}", "CONTAINER_ACL_TARGET_BUSY");
            await second.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM file_path_locks WHERE operation_id={inverse.Id}");
            await repo.CancelAsync(actor, inverse.Id, default); Assert.True(await repo.FinishAsync(inverse, "Canceled", "CONTAINER_ACL_CANCELED", Receipt(inverse, "Aborted"), default));
            var canceled = (await other.FindAsync(inverse.Id, default))!; Assert.False(canceled.AclConfirmed); Assert.False(canceled.HistoryMayExist); Assert.Null(canceled.HistoryDigest);
            var canceledFacts = await JobFacts(canceled, true, "Canceled");
            Assert.False(canceledFacts.AclConfirmed); Assert.False(canceledFacts.HistoryVerified); Assert.Equal("Aborted", canceledFacts.ReceiptState);
            var leased = await Applying(await Ready(Row()));
            Assert.Null(await other.ClaimAsync(clock.GetUtcNow(),default));
            clock.Offset=TimeSpan.FromSeconds(91);
            var reclaimed=Assert.IsType<ContainerAclIntent>(await other.ClaimAsync(clock.GetUtcNow(),default));
            Assert.Equal(leased.Id,reclaimed.Id); Assert.NotEqual(leased.LeaseToken,reclaimed.LeaseToken);
            Assert.False(await repo.RenewAsync(leased,clock.GetUtcNow(),default));
            Assert.False(await repo.FinishAsync(leased,"Succeeded","CONTAINER_ACL_COMPLETED",Receipt(leased,"Completed"),default));
            Assert.Equal(ContainerFileUploadStartResult.LeaseLost,await other.StartedAsync(reclaimed,clock.GetUtcNow(),default));
            Assert.True(await other.FinishAsync(reclaimed,"Unknown","CONTAINER_ACL_RESULT_UNKNOWN",null,default));
            var missing=(await other.FindAsync(reclaimed.Id,default))!;
            Assert.True(missing.PublishBarrier); Assert.Null(missing.ReceiptJson); Assert.True(missing.HistoryMayExist);
            var missingFacts = await JobFacts(missing, true, "Unknown");
            Assert.Null(missingFacts.AclConfirmed); Assert.Null(missingFacts.ReceiptState); Assert.True(missingFacts.HistoryMayExist);
            await repo.AcknowledgeAsync(admin,missing.Id,missing.Version,Receipt(missing,"Aborted"),default);
            var resolved=(await other.FindAsync(missing.Id,default))!; Assert.False(resolved.PublishBarrier);
            Assert.False(resolved.AclConfirmed); Assert.True(resolved.HistoryMayExist); Assert.True(ContainerAclWorkflowValidation.HasValidBinding(resolved,protector));
            clock.Offset=TimeSpan.Zero;
            var expiredReady = await Ready(Row()); clock.Offset = TimeSpan.FromMinutes(6);
            await Assert.ThrowsAsync<InvalidOperationException>(() => repo.ConfirmAsync(actor, Claims(expiredReady), expiredReady.Version, default));
            await repo.CancelAsync(actor, expiredReady.Id, default); clock.Offset = TimeSpan.Zero;
            // Audit failure is part of the same SQL transaction as the intent/jobs/outbox.
            var rejected = Row();
            await second.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION wdv_acl_audit_reject() RETURNS trigger LANGUAGE plpgsql AS $f$
                BEGIN RAISE EXCEPTION 'wdv_acl_audit_failed' USING ERRCODE='23514'; END $f$;
                CREATE TRIGGER wdv_acl_audit_reject BEFORE INSERT ON audit_events
                  FOR EACH ROW WHEN (NEW.action LIKE 'containers.files.acl.%') EXECUTE FUNCTION wdv_acl_audit_reject();
                """);
            try { await Assert.ThrowsAsync<DbUpdateException>(() => repo.EnqueueAsync(rejected, default)); }
            finally { await second.Database.ExecuteSqlRawAsync("DROP TRIGGER wdv_acl_audit_reject ON audit_events; DROP FUNCTION wdv_acl_audit_reject();"); }
            Assert.False(await second.ContainerAclIntents.AnyAsync(r => r.Id == rejected.Id)); Assert.False(await second.OperationJobs.AnyAsync(j => j.Id == rejected.Id));
            var waiting = new List<ContainerAclIntent>();
            for (var n = 0; n < 4; n++) { var row = Row(); await repo.EnqueueAsync(row, default); waiting.Add(row); }
            var over = Row(); await Assert.ThrowsAsync<InvalidOperationException>(() => repo.EnqueueAsync(over, default));
            foreach (var row in waiting) await repo.CancelAsync(actor, row.Id, default);
            var downgrade = await Assert.ThrowsAsync<PostgresException>(() => db.GetService<IMigrator>().MigrateAsync("20261102131222_ContainerRecursiveOwnershipIntents"));
            Assert.Equal("container_acl_downgrade_requires_empty_table", downgrade.MessageText); await db.Database.MigrateAsync();
            Assert.False(await db.ContainerAclIntents.AnyAsync(r => r.CompletedAtUtc == null || r.PublishBarrier || r.LeaseToken != null));
            Assert.DoesNotContain(await db.OperationJobs.Where(j => ids.Contains(j.Id) || db.ContainerAclIntents.Any(r => r.ExecutionJobId == j.Id && ids.Contains(r.Id))).ToArrayAsync(), j => j.State is "Queued" or "Running");
            Console.WriteLine("INBOX_PHASE=container-acl-persistent-jobs-sealed-receipts-and-ten-way-fences");
        }
        finally
        {
            // Only exact synthetic legacy fixtures are deleted; permanent new rows remain until isolated DB removal.
            await second.ContainerFileUploadOperations.Where(r => uploadIds.Contains(r.Id)).ExecuteDeleteAsync();
            await second.ContainerDirectoryOperations.Where(r => directoryIds.Contains(r.Id)).ExecuteDeleteAsync(); File.Delete(keyPath);
        }
    }
    private sealed class AclClock : TimeProvider { public TimeSpan Offset { get; set; } public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow.Add(Offset); }
}
