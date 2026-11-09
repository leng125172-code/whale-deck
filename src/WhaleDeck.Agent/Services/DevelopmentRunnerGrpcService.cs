using System.ComponentModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Google.Protobuf;
using Grpc.Core;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

public sealed class DevelopmentRunnerGrpcService(BoundedProcessRunner processes) : DevelopmentRunnerService.DevelopmentRunnerServiceBase
{
    private static readonly HashSet<int> HelperExitCodes = [1, 2];
    private static readonly SemaphoreSlim ArtifactReaders = new(2, 2);

    public override Task<WorkspaceRunReceipt> Start(WorkspaceRunRequest request, ServerCallContext context) =>
        ExecuteAsync(request, "start", context.CancellationToken);

    public override Task<WorkspaceRunReceipt> Observe(WorkspaceRunRequest request, ServerCallContext context) =>
        ExecuteAsync(request, "observe", context.CancellationToken);

    public override Task<WorkspaceRunReceipt> Cancel(WorkspaceRunRequest request, ServerCallContext context) =>
        ExecuteAsync(request, "cancel", context.CancellationToken);

    public override Task<WorkspaceRunReceipt> Reclaim(WorkspaceRunRequest request, ServerCallContext context) =>
        ExecuteAsync(request, "reclaim", context.CancellationToken);

    public override async Task ReadTestArtifact(WorkspaceTestArtifactRequest request,
        IServerStreamWriter<WorkspaceTestArtifactChunk> responseStream, ServerCallContext context)
    {
        if (!DevelopmentRunnerBinding.IsValid(request))
            throw Error(StatusCode.InvalidArgument, "WORKSPACE_RUN_REQUEST_INVALID");
        if (!await ArtifactReaders.WaitAsync(0, context.CancellationToken))
            throw Error(StatusCode.ResourceExhausted, "WORKSPACE_RUN_REPORT_BUSY");
        try
        {
            var result = await BoundedProcessRunner.RunBinaryAsync("/usr/bin/sudo", ArtifactArguments("readtest"),
                DevelopmentRunnerBinding.HostInput(request), (int)request.Size, TimeSpan.FromSeconds(20), context.CancellationToken);
            if (result.ExitCode != 0) throw Rejected(result.Error);
            if (Convert.ToHexStringLower(SHA256.HashData(result.Content)) != request.ArtifactSha256)
                throw Error(StatusCode.DataLoss, "WORKSPACE_RUN_REPORT_CHANGED");
            for (var offset = 0; offset < result.Content.Length; offset += DevelopmentRunnerBinding.ArtifactChunkBytes)
            {
                var size = Math.Min(DevelopmentRunnerBinding.ArtifactChunkBytes, result.Content.Length - offset);
                await responseStream.WriteAsync(new WorkspaceTestArtifactChunk
                    { Content = ByteString.CopyFrom(result.Content, offset, size) }, context.CancellationToken);
            }
        }
        catch (InvalidDataException) { throw Error(StatusCode.DataLoss, "WORKSPACE_RUN_REPORT_CHANGED"); }
        catch (OperationCanceledException) { throw Error(StatusCode.DeadlineExceeded, "WORKSPACE_RUN_HELPER_TIMEOUT"); }
        catch (Exception error) when (error is IOException or InvalidOperationException or Win32Exception)
        { throw Error(StatusCode.Unavailable, "WORKSPACE_RUN_HELPER_UNAVAILABLE"); }
        finally { ArtifactReaders.Release(); }
    }

    public override async Task<WorkspaceTestArtifactRetirement> RetireTestArtifact(WorkspaceTestArtifactRequest request,
        ServerCallContext context)
    {
        if (!DevelopmentRunnerBinding.IsValid(request))
            throw Error(StatusCode.InvalidArgument, "WORKSPACE_RUN_REQUEST_INVALID");
        context.CancellationToken.ThrowIfCancellationRequested();
        ProcessResult result;
        try
        {
            result = await processes.RunAsync("/usr/bin/sudo", ArtifactArguments("retiretest"),
                DevelopmentRunnerBinding.HostInput(request), TimeSpan.FromSeconds(20), "WORKSPACE_RUN_HELPER_UNAVAILABLE",
                CancellationToken.None, acceptedExitCodes: HelperExitCodes);
        }
        catch (OperationCanceledException) { throw Error(StatusCode.DeadlineExceeded, "WORKSPACE_RUN_HELPER_TIMEOUT"); }
        catch (Exception error) when (error is IOException or InvalidOperationException or Win32Exception)
        { throw Error(StatusCode.Unavailable, "WORKSPACE_RUN_HELPER_UNAVAILABLE"); }
        if (result.ExitCode != 0) throw Rejected(result.StandardError);
        return ParseRetirement(request, result.StandardOutput);
    }

    private static string[] ArtifactArguments(string action) =>
        ["-n", "/usr/local/libexec/whaledeck-privileged", "development-run-" + action, Guid.NewGuid().ToString("N")];

    internal static WorkspaceTestArtifactRetirement ParseRetirement(WorkspaceTestArtifactRequest request, string output)
    {
        try
        {
            if (output.Length > 1024) throw new JsonException();
            using var document = JsonDocument.Parse(output, new JsonDocumentOptions { MaxDepth = 3 });
            var root = document.RootElement;
            var report = root.GetProperty("testReport");
            if (root.EnumerateObject().Count() != 4 || report.EnumerateObject().Count() != 4 ||
                report.GetProperty("state").GetString() != "Present" ||
                report.GetProperty("format").GetString() != DevelopmentRunnerBinding.ReportFormat(request.Run))
                throw new JsonException();
            var receipt = new WorkspaceTestArtifactRetirement
            {
                RunId = root.GetProperty("runId").GetString()!,
                RequestSha256 = root.GetProperty("requestSha256").GetString()!,
                InputSha256 = root.GetProperty("inputSha256").GetString()!,
                Size = report.GetProperty("size").GetUInt32(),
                ArtifactSha256 = report.GetProperty("sha256").GetString()!
            };
            return DevelopmentRunnerBinding.Matches(request, receipt) ? receipt : throw new JsonException();
        }
        catch (Exception error) when (error is JsonException or ArgumentException or KeyNotFoundException or InvalidOperationException or FormatException)
        { throw Error(StatusCode.DataLoss, "WORKSPACE_RUN_RECEIPT_INVALID"); }
    }

    private async Task<WorkspaceRunReceipt> ExecuteAsync(WorkspaceRunRequest request, string action, CancellationToken token)
    {
        if (!DevelopmentRunnerBinding.IsValid(request))
            throw Error(StatusCode.InvalidArgument, "WORKSPACE_RUN_REQUEST_INVALID");
        token.ThrowIfCancellationRequested();
        var seconds = action switch
        {
            "start" => DevelopmentRunnerBinding.PreparationSeconds + (int)request.DeadlineSeconds,
            "observe" => DevelopmentRunnerBinding.ObserveSeconds,
            _ => DevelopmentRunnerBinding.ControlSeconds
        };
        var arguments = new List<string>
            { "-n", "/usr/local/libexec/whaledeck-privileged", "development-run-" + action, Guid.NewGuid().ToString("N") };
        if (action == "start") arguments.Add((seconds - 10).ToString(CultureInfo.InvariantCulture));
        ProcessResult result;
        try
        {
            // Dispatch outlives a dropped RPC. Observe with a fresh signature
            // instead of retrying under a different run identifier.
            result = await processes.RunAsync("/usr/bin/sudo", arguments,
                DevelopmentRunnerBinding.HostInput(request), TimeSpan.FromSeconds(seconds),
                "WORKSPACE_RUN_HELPER_UNAVAILABLE", action == "observe" ? token : CancellationToken.None, acceptedExitCodes: HelperExitCodes);
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
            if (output.Length > 2048) throw new JsonException();
            using var document = JsonDocument.Parse(output, new JsonDocumentOptions { MaxDepth = 3 });
            var root = document.RootElement;
            var hasReport = root.TryGetProperty("testReport", out var manifest);
            if (root.EnumerateObject().Count() != (hasReport ? 6 : 5)) throw new JsonException();
            var receipt = new WorkspaceRunReceipt
            {
                RunId = root.GetProperty("runId").GetString()!,
                RequestSha256 = root.GetProperty("requestSha256").GetString()!,
                InputSha256 = root.GetProperty("inputSha256").GetString()!,
                Status = root.GetProperty("status").GetString()!
            };
            var exitCode = root.GetProperty("runtimeExitCode");
            if (exitCode.ValueKind != JsonValueKind.Null) receipt.RuntimeExitCode = exitCode.GetInt32();
            if (hasReport)
            {
                var state = manifest.GetProperty("state").GetString()!;
                var present = state == "Present";
                if (manifest.EnumerateObject().Count() != (present ? 4 : 2)) throw new JsonException();
                receipt.TestReport = new()
                {
                    Format = manifest.GetProperty("format").GetString()!,
                    State = state,
                    Size = present ? manifest.GetProperty("size").GetUInt32() : 0,
                    Sha256 = present ? manifest.GetProperty("sha256").GetString()! : ""
                };
            }
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
                "WORKSPACE_RUN_NOT_FOUND" or "WORKSPACE_RUN_REPORT_RETIRED" => StatusCode.NotFound,
                "WORKSPACE_RUN_REQUEST_CONFLICT" or "WORKSPACE_RUN_REPORT_CONFLICT" => StatusCode.Aborted,
                "WORKSPACE_RUN_BUSY" or "WORKSPACE_STORAGE_BUSY" => StatusCode.Unavailable,
                "WORKSPACE_RUN_CAPACITY" => StatusCode.ResourceExhausted,
                "WORKSPACE_RUN_ENVIRONMENT_CHANGED" or "WORKSPACE_RUN_RUNTIME_NOT_REGISTERED" or
                    "WORKSPACE_RUN_WORK_IDENTITY_CHANGED" or "WORKSPACE_STORAGE_NOT_PUBLISHED" or
                    "WORKSPACE_ROOTFS_IMAGE_NOT_FOUND" or "WORKSPACE_UNPACK_FAILED" => StatusCode.FailedPrecondition,
                "WORKSPACE_RUN_INTERRUPTED" or "WORKSPACE_RUN_CLEANUP_UNCONFIRMED" or
                    "WORKSPACE_UNPACK_CLEANUP_UNCONFIRMED" or "WORKSPACE_RUN_RECEIPT_INVALID" or
                    "WORKSPACE_RUN_EXECUTION_PROOF_MISSING" or "WORKSPACE_RUN_EXECUTION_PROOF_INVALID" or
                "WORKSPACE_RUN_EXECUTION_PROOF_CHANGED" or "WORKSPACE_RUN_REPORT_CHANGED" or
                    "WORKSPACE_STORAGE_METADATA_UNSAFE" => StatusCode.DataLoss,
                _ => StatusCode.OK
            };
            if (status != StatusCode.OK) return Error(status, line);
        }
        return Error(StatusCode.Unavailable, "WORKSPACE_RUN_HELPER_UNAVAILABLE");
    }

    private static RpcException Error(StatusCode status, string code) => new(new Status(status, code));
}
