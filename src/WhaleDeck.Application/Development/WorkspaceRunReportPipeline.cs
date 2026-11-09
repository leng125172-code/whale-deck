using System.Security.Cryptography;
using System.Text;

namespace WhaleDeck.Application.Development;

public sealed record WorkspaceRunReportClaim(WorkspaceRunClaim Execution, string Stage, WorkspaceRunnerReceipt? Receipt);

public interface IWorkspaceRunReportWork
{
    Task<WorkspaceRunReportClaim?> ClaimReportAsync(CancellationToken token);
    Task ReleaseReportAsync(WorkspaceRunReportClaim claim, string? terminalStage, CancellationToken token);
}

public interface IWorkspaceCollectedReportStore
{
    Task<WorkspaceReportWriteState> SaveCollectedAsync(WorkspaceRunReportClaim claim, WorkspaceRunReport report, CancellationToken token);
}

public sealed class WorkspaceRunReportPipeline(IWorkspaceRunReportWork work, IWorkspaceCollectedReportStore reports,
    IWorkspaceTestArtifactGateway gateway, TimeProvider clock)
{
    public async Task TickAsync(CancellationToken token)
    {
        using var tick = CancellationTokenSource.CreateLinkedTokenSource(token);
        tick.CancelAfter(TimeSpan.FromSeconds(35));
        var claim = await work.ClaimReportAsync(tick.Token);
        if (claim is null) return;
        var execution = claim.Execution;
        try
        {
            if (claim.Stage == "Collect")
            {
                if (execution.Run.CompletedAtUtc is { } completed && completed.AddDays(Retention(execution.Command)) <= clock.GetUtcNow())
                {
                    await work.ReleaseReportAsync(claim, "Attention", tick.Token);
                    return;
                }
                var report = await BuildAsync(claim, tick.Token);
                if (await reports.SaveCollectedAsync(claim, report, tick.Token) == WorkspaceReportWriteState.Expired)
                    await work.ReleaseReportAsync(claim, "Attention", tick.Token);
                return;
            }
            if (claim.Stage != "Retire" || claim.Receipt is not { TestReport.State: "Present" } receipt)
                throw new InvalidDataException("WORKSPACE_REPORT_STAGE_INVALID");
            await gateway.RetireTestArtifactAsync(execution.Actor, execution.Workspace, execution.Command, receipt, tick.Token);
            await work.ReleaseReportAsync(claim, "Complete", tick.Token);
        }
        catch (WorkspaceRunnerException error) when (claim.Stage == "Retire" &&
            error.Failure is WorkspaceRunnerFailure.NotFound or WorkspaceRunnerFailure.Integrity)
        {
            await work.ReleaseReportAsync(claim, "Attention", token);
        }
        catch (Exception error) when (!token.IsCancellationRequested && error is WorkspaceRunnerException or OperationCanceledException)
        {
            await work.ReleaseReportAsync(claim, null, token);
        }
    }

    public static WorkspaceRunOutcome Outcome(WorkspaceRunView run) => (run.State, run.Phase) switch
    {
        ("Succeeded", "Completed") => WorkspaceRunOutcome.Succeeded,
        ("Canceled", _) => WorkspaceRunOutcome.Canceled,
        ("Failed", "TimedOut") => WorkspaceRunOutcome.TimedOut,
        ("Failed", "Interrupted") => WorkspaceRunOutcome.Interrupted,
        ("Failed", "ResultUnknown") => WorkspaceRunOutcome.Unknown,
        ("Failed", _) => WorkspaceRunOutcome.Failed,
        _ => throw new InvalidDataException("WORKSPACE_REPORT_RUN_NOT_FINAL")
    };

    private async Task<WorkspaceRunReport> BuildAsync(WorkspaceRunReportClaim claim, CancellationToken token)
    {
        var (execution, _, receipt) = claim;
        var (run, workspace, actor, _, command, dispatched) = execution;
        var outcome = Outcome(run);
        var completed = run.CompletedAtUtc ?? throw new InvalidDataException("WORKSPACE_REPORT_RUN_NOT_FINAL");
        var stepId = ScopedId(run.RunId, "step");
        var artifactId = ScopedId(run.RunId, "test");
        var format = run.Task == "test" ? run.Runtime switch
        {
            "node" => WorkspaceTestFormat.JUnit,
            "dotnet" => WorkspaceTestFormat.Trx,
            _ => throw new InvalidDataException("WORKSPACE_REPORT_FORMAT_INVALID")
        } : (WorkspaceTestFormat?)null;
        WorkspaceRegisteredTestArtifact? artifact = null;
        WorkspaceReportTestSlot? slot = null;
        byte[]? content = null;
        if (format is { } testFormat)
        {
            var collection = WorkspaceArtifactCollectionState.Unavailable;
            if (receipt?.TestReport is { } manifest)
            {
                if (manifest.Format != testFormat || !Enum.TryParse(manifest.State, out collection) || !Enum.IsDefined(collection))
                    throw new InvalidDataException("WORKSPACE_REPORT_MANIFEST_INVALID");
                if (collection == WorkspaceArtifactCollectionState.Present)
                {
                    try
                    {
                        content = await gateway.ReadTestArtifactAsync(actor, workspace, command, receipt, token);
                        artifact = new(run.WorkspaceId, run.RunId, stepId, artifactId, testFormat, manifest.Sha256);
                    }
                    catch (WorkspaceRunnerException error) when (error.Failure is WorkspaceRunnerFailure.Integrity or WorkspaceRunnerFailure.NotFound)
                    {
                        collection = error.Failure == WorkspaceRunnerFailure.Integrity
                            ? WorkspaceArtifactCollectionState.IntegrityFailure : WorkspaceArtifactCollectionState.Missing;
                    }
                }
            }
            slot = new(artifactId, testFormat, collection);
        }
        var started = dispatched ?? run.CreatedAtUtc;
        var hasHostResult = receipt is not null;
        var step = new WorkspaceReportStepEvidence(stepId, run.Runtime + "-" + run.Task,
            hasHostResult ? outcome : WorkspaceRunOutcome.Skipped, hasHostResult ? dispatched : null,
            hasHostResult ? completed : null, receipt?.RuntimeExitCode, slot is null ? [] : [slot]);
        var environment = new WorkspaceReportEnvironment(workspace.EnvironmentSha256, workspace.Version,
            [.. workspace.Environment.Runtimes.Select(runtime => new WorkspaceReportTool(runtime.Key, runtime.Version))]);
        var input = new WorkspaceRunReportEvidence(run.WorkspaceId, run.RunId, run.RunId, actor.TraceId, actor.Subject,
            started, completed, completed.AddDays(Retention(command)), outcome, receipt?.RuntimeExitCode,
            new(null, null, null, null), environment, [step]);
        return await new WorkspaceRunReports(new SingleArtifact(artifact, content)).BuildAsync(input,
            artifact is null ? [] : [artifact], token);
    }

    private static Guid ScopedId(Guid run, string slot) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes($"workspace-report:{run:N}:{slot}")).AsSpan(0, 16));

    private static int Retention(WorkspaceRunCommand command) => command.ReportRetentionDays
        ?? throw new InvalidDataException("WORKSPACE_REPORT_RETENTION_MISSING");

    private sealed class SingleArtifact(WorkspaceRegisteredTestArtifact? expected, byte[]? content) : IWorkspaceReportArtifactReader
    {
        public ValueTask<Stream?> OpenAsync(WorkspaceRegisteredTestArtifact artifact, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<Stream?>(artifact == expected && content is not null ? new MemoryStream(content, writable: false) : null);
        }
    }
}
