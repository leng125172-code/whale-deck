using System.ComponentModel;
using System.Text.Json;
using Grpc.Core;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

public sealed class DevelopmentArchiveGrpcService(BoundedProcessRunner processes)
    : DevelopmentArchiveService.DevelopmentArchiveServiceBase
{
    private static readonly HashSet<int> ExitCodes = [2];
    // Independent control slots: a Start flood cannot prevent exact cancellation.
    private static readonly SemaphoreSlim Starts = new(2, 2);
    private static readonly SemaphoreSlim Observations = new(4, 4);
    private static readonly SemaphoreSlim Cancellations = new(2, 2);

    public override Task<WorkspaceArchiveReceipt> Start(WorkspaceArchiveRequest request, ServerCallContext context) =>
        RunAsync(request, "start", Starts, context.CancellationToken);
    public override Task<WorkspaceArchiveReceipt> Observe(WorkspaceArchiveRequest request, ServerCallContext context) =>
        RunAsync(request, "observe", Observations, context.CancellationToken);
    public override Task<WorkspaceArchiveReceipt> Cancel(WorkspaceArchiveRequest request, ServerCallContext context) =>
        RunAsync(request, "cancel", Cancellations, context.CancellationToken);

    private async Task<WorkspaceArchiveReceipt> RunAsync(WorkspaceArchiveRequest request, string action, SemaphoreSlim slots, CancellationToken token)
    {
        if (!DevelopmentArchiveBinding.IsValid(request)) throw Error(StatusCode.InvalidArgument, "WORKSPACE_ARCHIVE_REQUEST_INVALID");
        token.ThrowIfCancellationRequested();
        if (!await slots.WaitAsync(0, token)) throw Error(StatusCode.ResourceExhausted, "WORKSPACE_ARCHIVE_BUSY");
        try
        {
            // The fixed helper owns a durable dispatch fence and an independent
            // long-running unit. A disconnected RPC never kills archive copying.
            var result = await processes.RunAsync("/usr/bin/sudo", ["-n", "/usr/local/libexec/whaledeck-privileged",
                "development-archive-" + action, Guid.NewGuid().ToString("N")],
                DevelopmentArchiveBinding.HostInput(request), TimeSpan.FromSeconds(20), "WORKSPACE_ARCHIVE_HELPER_UNAVAILABLE",
                action == "observe" ? token : CancellationToken.None, acceptedExitCodes: ExitCodes);
            if (result.ExitCode != 0)
            {
                var busy = result.StandardError.Split('\n', StringSplitOptions.TrimEntries).Contains("WORKSPACE_STORAGE_ARCHIVE_TASK_BUSY");
                throw Error(busy ? StatusCode.Unavailable : StatusCode.FailedPrecondition,
                    busy ? "WORKSPACE_ARCHIVE_HOST_BUSY" : "WORKSPACE_ARCHIVE_HOST_UNCONFIRMED");
            }
            return Parse(request, result.StandardOutput);
        }
        catch (OperationCanceledException) { throw Error(StatusCode.DeadlineExceeded, "WORKSPACE_ARCHIVE_TIMEOUT"); }
        catch (Exception error) when (error is IOException or InvalidOperationException or Win32Exception)
        { throw Error(StatusCode.Unavailable, "WORKSPACE_ARCHIVE_HELPER_UNAVAILABLE"); }
        finally { slots.Release(); }
    }

    internal static WorkspaceArchiveReceipt Parse(WorkspaceArchiveRequest request, string output)
    {
        try { return DevelopmentArchiveBinding.Parse(request, output); }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or
            FormatException or ArgumentException or OverflowException)
        { throw Error(StatusCode.DataLoss, "WORKSPACE_ARCHIVE_RECEIPT_INVALID"); }
    }
    private static RpcException Error(StatusCode status, string code) => new(new Status(status, code));
}
