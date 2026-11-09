using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Grpc.Core;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

public sealed class DevelopmentRunnerGrpcService(BoundedProcessRunner processes) : DevelopmentRunnerService.DevelopmentRunnerServiceBase
{
    private static readonly HashSet<int> HelperExitCodes = [1, 2];

    public override Task<WorkspaceRunReceipt> Start(WorkspaceRunRequest request, ServerCallContext context) =>
        ExecuteAsync(request, observe: false, context.CancellationToken);

    public override Task<WorkspaceRunReceipt> Observe(WorkspaceRunRequest request, ServerCallContext context) =>
        ExecuteAsync(request, observe: true, context.CancellationToken);

    private async Task<WorkspaceRunReceipt> ExecuteAsync(WorkspaceRunRequest request, bool observe, CancellationToken token)
    {
        if (!DevelopmentRunnerBinding.IsValid(request))
            throw Error(StatusCode.InvalidArgument, "WORKSPACE_RUN_REQUEST_INVALID");
        token.ThrowIfCancellationRequested();
        var seconds = observe ? DevelopmentRunnerBinding.ObserveSeconds :
            DevelopmentRunnerBinding.PreparationSeconds + request.DeadlineSeconds;
        var nonce = Guid.NewGuid().ToString("N");
        string[] arguments = observe
            ? ["-n", "/usr/local/libexec/whaledeck-privileged", "development-run-observe", nonce]
            : ["-n", "/usr/local/libexec/whaledeck-privileged", "development-run-start", nonce,
                (seconds - 10).ToString(CultureInfo.InvariantCulture)];
        ProcessResult result;
        try
        {
            // Dispatch outlives a dropped RPC. Observe with a fresh signature
            // instead of retrying under a different run identifier.
            result = await processes.RunAsync("/usr/bin/sudo", arguments,
                DevelopmentRunnerBinding.HostInput(request), TimeSpan.FromSeconds(seconds),
                "WORKSPACE_RUN_HELPER_UNAVAILABLE", observe ? token : CancellationToken.None, acceptedExitCodes: HelperExitCodes);
        }
        catch (OperationCanceledException) { throw Error(StatusCode.DeadlineExceeded, "WORKSPACE_RUN_HELPER_TIMEOUT"); }
        catch (Exception error) when (error is IOException or InvalidOperationException or Win32Exception)
        { throw Error(StatusCode.Unavailable, "WORKSPACE_RUN_HELPER_UNAVAILABLE"); }
        return result.ExitCode == 0 ? ParseReceipt(request, result.StandardOutput) : throw Rejected(result.StandardError);
    }

    internal static WorkspaceRunReceipt ParseReceipt(WorkspaceRunRequest request, string output)
    {
        try
        {
            if (output.Length > 1024) throw new JsonException();
            using var document = JsonDocument.Parse(output, new JsonDocumentOptions { MaxDepth = 2 });
            var root = document.RootElement;
            if (root.EnumerateObject().Count() != 5) throw new JsonException();
            var receipt = new WorkspaceRunReceipt
            {
                RunId = root.GetProperty("runId").GetString()!,
                RequestSha256 = root.GetProperty("requestSha256").GetString()!,
                InputSha256 = root.GetProperty("inputSha256").GetString()!,
                Status = root.GetProperty("status").GetString()!
            };
            var exitCode = root.GetProperty("runtimeExitCode");
            if (exitCode.ValueKind != JsonValueKind.Null) receipt.RuntimeExitCode = exitCode.GetInt32();
            return DevelopmentRunnerBinding.Matches(request, receipt) ? receipt : throw new JsonException();
        }
        catch (Exception error) when (error is JsonException or ArgumentException or KeyNotFoundException or InvalidOperationException or FormatException)
        { throw Error(StatusCode.DataLoss, "WORKSPACE_RUN_RECEIPT_INVALID"); }
    }

    private static RpcException Rejected(string stderr)
    {
        foreach (var line in stderr.Split('\n', StringSplitOptions.TrimEntries))
        {
            var status = line switch
            {
                "WORKSPACE_RUN_NOT_FOUND" => StatusCode.NotFound,
                "WORKSPACE_RUN_REQUEST_CONFLICT" => StatusCode.Aborted,
                "WORKSPACE_RUN_BUSY" or "WORKSPACE_STORAGE_BUSY" => StatusCode.Unavailable,
                "WORKSPACE_RUN_CAPACITY" => StatusCode.ResourceExhausted,
                "WORKSPACE_RUN_ENVIRONMENT_CHANGED" or "WORKSPACE_RUN_RUNTIME_NOT_REGISTERED" or
                    "WORKSPACE_RUN_WORK_IDENTITY_CHANGED" or "WORKSPACE_STORAGE_NOT_PUBLISHED" or
                    "WORKSPACE_ROOTFS_IMAGE_NOT_FOUND" or "WORKSPACE_UNPACK_FAILED" => StatusCode.FailedPrecondition,
                "WORKSPACE_RUN_INTERRUPTED" or "WORKSPACE_RUN_CLEANUP_UNCONFIRMED" or
                    "WORKSPACE_UNPACK_CLEANUP_UNCONFIRMED" or "WORKSPACE_RUN_RECEIPT_INVALID" => StatusCode.DataLoss,
                _ => StatusCode.OK
            };
            if (status != StatusCode.OK) return Error(status, line);
        }
        return Error(StatusCode.Unavailable, "WORKSPACE_RUN_HELPER_UNAVAILABLE");
    }

    private static RpcException Error(StatusCode status, string code) => new(new Status(status, code));
}
