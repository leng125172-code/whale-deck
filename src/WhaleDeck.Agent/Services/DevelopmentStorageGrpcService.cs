using System.ComponentModel;
using System.Text.Json;
using Grpc.Core;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

public sealed class DevelopmentStorageGrpcService(BoundedProcessRunner processes) : DevelopmentStorageService.DevelopmentStorageServiceBase
{
    private static readonly HashSet<int> HelperExitCodes = [1, 2];

    public override Task<WorkspaceAllocationReceipt> Allocate(WorkspaceAllocationRequest request, ServerCallContext context) =>
        RunAsync(request, attest: false, context.CancellationToken);

    public override Task<WorkspaceAllocationReceipt> Attest(WorkspaceAllocationRequest request, ServerCallContext context) =>
        RunAsync(request, attest: true, context.CancellationToken);

    private async Task<WorkspaceAllocationReceipt> RunAsync(WorkspaceAllocationRequest request, bool attest, CancellationToken token)
    {
        if (!DevelopmentStorageBinding.IsValid(request))
            throw Error(StatusCode.InvalidArgument, "WORKSPACE_STORAGE_REQUEST_INVALID");

        var input = JsonSerializer.Serialize(new
        {
            schemaVersion = 1, rootId = request.StorageRootId, workspaceId = request.WorkspaceId,
            environmentSha256 = request.EnvironmentSha256, diskMiB = request.DiskMib
        });
        token.ThrowIfCancellationRequested();
        ProcessResult result;
        try
        {
            // Allocation can publish before the RPC returns. Its sealed receipt
            // makes a fresh signed retry safe after client disconnect.
            result = await processes.RunAsync("/usr/bin/sudo",
                ["-n", "/usr/local/libexec/whaledeck-privileged",
                    attest ? "development-storage-attest" : "development-storage-allocate", Guid.NewGuid().ToString("N")],
                input, TimeSpan.FromSeconds(25), "WORKSPACE_STORAGE_HELPER_UNAVAILABLE", attest ? token : CancellationToken.None,
                acceptedExitCodes: HelperExitCodes);
        }
        catch (OperationCanceledException) { throw Error(StatusCode.DeadlineExceeded, "WORKSPACE_STORAGE_TIMEOUT"); }
        catch (Exception e) when (e is IOException or InvalidOperationException or Win32Exception)
        { throw Error(StatusCode.Unavailable, "WORKSPACE_STORAGE_HELPER_UNAVAILABLE"); }
        if (result.ExitCode != 0) throw Rejected(result.StandardError);
        return ParseReceipt(request, result.StandardOutput);
    }

    internal static WorkspaceAllocationReceipt ParseReceipt(WorkspaceAllocationRequest request, string output)
    {
        try
        {
            if (output.Length > 1024) throw new JsonException();
            using var document = JsonDocument.Parse(output, new JsonDocumentOptions { MaxDepth = 2 });
            var root = document.RootElement;
            if (root.EnumerateObject().Count() != 10 || root.GetProperty("schemaVersion").GetInt32() != 1 ||
                !DevelopmentStorageBinding.IsUuid(root.GetProperty("filesystemUuid").GetString()) ||
                root.GetProperty("rootInode").GetUInt64() == 0 || root.GetProperty("directoryInode").GetUInt64() == 0 ||
                root.GetProperty("payloadInode").GetUInt64() == 0 || root.GetProperty("projectId").GetInt32() <= 0)
                throw new JsonException();
            var receipt = new WorkspaceAllocationReceipt
            {
                WorkspaceId = root.GetProperty("workspaceId").GetString()!,
                StorageRootId = root.GetProperty("rootId").GetString()!,
                EnvironmentSha256 = root.GetProperty("environmentSha256").GetString()!,
                DiskMib = root.GetProperty("diskMiB").GetUInt32(), ProjectQuotaVerified = true
            };
            return DevelopmentStorageBinding.Matches(request, receipt) ? receipt : throw new JsonException();
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException or KeyNotFoundException or ArgumentNullException)
        { throw Error(StatusCode.DataLoss, "WORKSPACE_STORAGE_RECEIPT_INVALID"); }
    }

    private static RpcException Rejected(string stderr)
    {
        foreach (var line in stderr.Split('\n', StringSplitOptions.TrimEntries))
        {
            var status = line switch
            {
                "WORKSPACE_STORAGE_BUSY" => StatusCode.Unavailable,
                "WORKSPACE_STORAGE_CAPACITY_REQUIRED" => StatusCode.ResourceExhausted,
                "WORKSPACE_STORAGE_QUOTA_PROJECT_IDS_BUSY" => StatusCode.ResourceExhausted,
                "WORKSPACE_STORAGE_REQUEST_INVALID" => StatusCode.InvalidArgument,
                "WORKSPACE_STORAGE_DIRECTORY_CONFLICT" or "WORKSPACE_STORAGE_CONFIGURATION_CHANGED" or
                    "WORKSPACE_STORAGE_QUOTA_CLAIM_CONFLICT" or "WORKSPACE_STORAGE_QUOTA_ID_IN_USE" or
                    "WORKSPACE_STORAGE_ROOT_CHANGED" => StatusCode.Aborted,
                "WORKSPACE_STORAGE_NOT_CONFIGURED" or "WORKSPACE_STORAGE_ROOT_NOT_APPROVED" or
                    "WORKSPACE_STORAGE_MOUNT_MISMATCH" or "WORKSPACE_STORAGE_DEVICE_MISMATCH" or
                    "WORKSPACE_STORAGE_NVME_REQUIRED" or "WORKSPACE_STORAGE_FILESYSTEM_UNSUPPORTED" or
                    "WORKSPACE_STORAGE_READ_ONLY" or "WORKSPACE_STORAGE_DIRECTORY_UNSAFE" or
                    "WORKSPACE_STORAGE_METADATA_UNSAFE" or "WORKSPACE_STORAGE_METADATA_INVALID" or
                    "WORKSPACE_STORAGE_CONFIGURATION_INVALID" or "WORKSPACE_STORAGE_STAGING_UNKNOWN" or
                    "WORKSPACE_STORAGE_PUBLICATION_UNKNOWN" or "WORKSPACE_STORAGE_STATE_UNSAFE" or
                    "WORKSPACE_STORAGE_QUOTA_RANGES_OVERLAP" or "WORKSPACE_STORAGE_QUOTA_PROOF_MISSING" or "WORKSPACE_STORAGE_NOT_PUBLISHED" or
                    "WORKSPACE_STORAGE_QUOTA_UNAVAILABLE" or "WORKSPACE_STORAGE_QUOTA_UNSUPPORTED" or
                    "WORKSPACE_STORAGE_QUOTA_NOT_ENFORCING" or "WORKSPACE_STORAGE_QUOTA_ATTRIBUTES_UNAVAILABLE" or
                    "WORKSPACE_STORAGE_QUOTA_BINDING_CHANGED" or "WORKSPACE_STORAGE_QUOTA_DIRECTORY_UNSAFE" or
                    "WORKSPACE_STORAGE_QUOTA_ATTACH_FAILED" => StatusCode.FailedPrecondition,
                _ => StatusCode.OK
            };
            if (status != StatusCode.OK) return Error(status, line);
        }
        return Error(StatusCode.Unavailable, "WORKSPACE_STORAGE_HELPER_UNAVAILABLE");
    }

    private static RpcException Error(StatusCode status, string code) => new(new Status(status, code));
}
