using System.ComponentModel;
using System.Text.Json;
using Grpc.Core;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

public sealed class DevelopmentRetirementGrpcService(BoundedProcessRunner processes)
    : DevelopmentRetirementService.DevelopmentRetirementServiceBase
{
    private static readonly HashSet<int> ExitCodes = [2];
    // Independent control slots: a Start flood cannot prevent exact cancellation.
    private static readonly SemaphoreSlim Starts = new(2, 2);
    private static readonly SemaphoreSlim Observations = new(4, 4);
    private static readonly SemaphoreSlim Cancellations = new(2, 2);

    public override Task<WorkspaceRetirementReceipt> Start(WorkspaceRetirementRequest request, ServerCallContext context) =>
        RunAsync(request, "start", Starts, context.CancellationToken);
    public override Task<WorkspaceRetirementReceipt> Observe(WorkspaceRetirementRequest request, ServerCallContext context) =>
        RunAsync(request, "observe", Observations, context.CancellationToken);
    public override Task<WorkspaceRetirementReceipt> Cancel(WorkspaceRetirementRequest request, ServerCallContext context) =>
        RunAsync(request, "cancel", Cancellations, context.CancellationToken);

    private async Task<WorkspaceRetirementReceipt> RunAsync(WorkspaceRetirementRequest request, string action, SemaphoreSlim slots, CancellationToken token)
    {
        if (!DevelopmentRetirementBinding.IsValid(request)) throw Error(StatusCode.InvalidArgument, "WORKSPACE_RETIREMENT_REQUEST_INVALID");
        token.ThrowIfCancellationRequested();
        if (!await slots.WaitAsync(0, token)) throw Error(StatusCode.ResourceExhausted, "WORKSPACE_RETIREMENT_BUSY");
        try
        {
            // The fixed helper owns a durable dispatch fence and an independent
            // long-running unit. A disconnected RPC never kills source retirement.
            var result = await processes.RunAsync("/usr/bin/sudo", ["-n", "/usr/local/libexec/whaledeck-privileged",
                "development-retirement-" + action, Guid.NewGuid().ToString("N")],
                DevelopmentRetirementBinding.HostInput(request), TimeSpan.FromSeconds(20), "WORKSPACE_RETIREMENT_HELPER_UNAVAILABLE",
                action == "observe" ? token : CancellationToken.None, acceptedExitCodes: ExitCodes);
            if (result.ExitCode != 0)
            {
                var busy = result.StandardError.Split('\n', StringSplitOptions.TrimEntries).Contains("WORKSPACE_STORAGE_RETIREMENT_TASK_BUSY");
                throw Error(busy ? StatusCode.Unavailable : StatusCode.FailedPrecondition,
                    busy ? "WORKSPACE_RETIREMENT_HOST_BUSY" : "WORKSPACE_RETIREMENT_HOST_UNCONFIRMED");
            }
            return Parse(request, result.StandardOutput);
        }
        catch (OperationCanceledException) { throw Error(StatusCode.DeadlineExceeded, "WORKSPACE_RETIREMENT_TIMEOUT"); }
        catch (Exception error) when (error is IOException or InvalidOperationException or Win32Exception)
        { throw Error(StatusCode.Unavailable, "WORKSPACE_RETIREMENT_HELPER_UNAVAILABLE"); }
        finally { slots.Release(); }
    }

    internal static WorkspaceRetirementReceipt Parse(WorkspaceRetirementRequest request, string output)
    {
        try { return DevelopmentRetirementBinding.Parse(request, output); }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or
            FormatException or ArgumentException or OverflowException)
        { throw Error(StatusCode.DataLoss, "WORKSPACE_RETIREMENT_RECEIPT_INVALID"); }
    }
    private static RpcException Error(StatusCode status, string code) => new(new Status(status, code));
}
