using System.ComponentModel;
using System.Text.Json;
using Grpc.Core;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

public sealed class DevelopmentLifecycleGrpcService(BoundedProcessRunner processes)
    : DevelopmentLifecycleService.DevelopmentLifecycleServiceBase
{
    private static readonly HashSet<int> ExitCodes = [2];
    private static readonly SemaphoreSlim Helpers = new(4, 4);

    public override Task<WorkspaceLifecycleReceipt> Plan(WorkspaceLifecycleRequest request, ServerCallContext context) =>
        RunAsync(request, false, context.CancellationToken);
    public override Task<WorkspaceLifecycleReceipt> Apply(WorkspaceLifecycleRequest request, ServerCallContext context) =>
        RunAsync(request, true, context.CancellationToken);

    private async Task<WorkspaceLifecycleReceipt> RunAsync(WorkspaceLifecycleRequest request, bool apply, CancellationToken token)
    {
        if (!DevelopmentLifecycleBinding.IsValid(request, apply)) throw Error(StatusCode.InvalidArgument, "WORKSPACE_LIFECYCLE_REQUEST_INVALID");
        token.ThrowIfCancellationRequested();
        if (!await Helpers.WaitAsync(0, token)) throw Error(StatusCode.ResourceExhausted, "WORKSPACE_LIFECYCLE_BUSY");
        try
        {
            var result = await processes.RunAsync("/usr/bin/sudo", ["-n", "/usr/local/libexec/whaledeck-privileged",
                apply ? "development-lifecycle-apply" : "development-lifecycle-plan", Guid.NewGuid().ToString("N")],
                DevelopmentLifecycleBinding.HostInput(request), TimeSpan.FromSeconds(25), "WORKSPACE_LIFECYCLE_HELPER_UNAVAILABLE",
                apply ? CancellationToken.None : token, acceptedExitCodes: ExitCodes);
            if (result.ExitCode != 0)
            {
                var busy = result.StandardError.Split('\n', StringSplitOptions.TrimEntries).Contains("WORKSPACE_STORAGE_BUSY");
                throw Error(busy ? StatusCode.Unavailable : StatusCode.FailedPrecondition,
                    busy ? "WORKSPACE_LIFECYCLE_HOST_BUSY" : "WORKSPACE_LIFECYCLE_HOST_UNCONFIRMED");
            }
            return Parse(request, result.StandardOutput, apply);
        }
        catch (OperationCanceledException) { throw Error(StatusCode.DeadlineExceeded, "WORKSPACE_LIFECYCLE_TIMEOUT"); }
        catch (Exception error) when (error is IOException or InvalidOperationException or Win32Exception)
        { throw Error(StatusCode.Unavailable, "WORKSPACE_LIFECYCLE_HELPER_UNAVAILABLE"); }
        finally { Helpers.Release(); }
    }

    internal static WorkspaceLifecycleReceipt Parse(WorkspaceLifecycleRequest request, string output, bool apply)
    {
        try
        {
            if (output.Length > 2048) throw new JsonException();
            using var document = JsonDocument.Parse(output, new JsonDocumentOptions { MaxDepth = 2 });
            var root = document.RootElement;
            if (root.EnumerateObject().Count() != 8 || root.GetProperty("schemaVersion").GetInt32() != 1) throw new JsonException();
            var receipt = new WorkspaceLifecycleReceipt
            {
                WorkspaceId = root.GetProperty("workspaceId").GetString()!, Action = root.GetProperty("action").GetString()!,
                Epoch = root.GetProperty("epoch").GetUInt64(), State = root.GetProperty("state").GetString()!,
                PlanSha256 = root.GetProperty("planSha256").GetString()!, StorageProofSha256 = root.GetProperty("storageProofSha256").GetString()!,
                RequestSha256 = root.GetProperty("requestSha256").GetString()!
            };
            if (!DevelopmentLifecycleBinding.Matches(request, receipt, apply)) throw new JsonException();
            return receipt;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or ArgumentException)
        { throw Error(StatusCode.DataLoss, "WORKSPACE_LIFECYCLE_RECEIPT_INVALID"); }
    }

    private static RpcException Error(StatusCode status, string code) => new(new Status(status, code));
}
