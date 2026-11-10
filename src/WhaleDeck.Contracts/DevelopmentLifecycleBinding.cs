using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Contracts;

public static class DevelopmentLifecycleBinding
{
    public const uint ProtocolMinor = 76;
    public const string Capability = "development.lifecycle.idle";
    public const string Service = "/whaledeck.agent.v1.DevelopmentLifecycleService/";
    public const string Plan = Service + "Plan";
    public const string Apply = Service + "Apply";

    public static bool IsSupported(CapabilitiesResponse value) => value.ProtocolMajor == 1 &&
        value.ProtocolMinor >= ProtocolMinor && value.Capabilities.Contains(Capability);

    public static bool Digest(string? value) => value is { Length: 64 } && value.All(c => c is >= 'a' and <= 'f' or >= '0' and <= '9');
    public static string Hash(WorkspaceLifecycleRequest value) => Convert.ToHexString(SHA256.HashData(value.ToByteArray()));
    public static string Payload(string expires, string nonce, string method, string hash) =>
        $"whaledeck-development-lifecycle-capability-v1\n{expires}.{nonce}.{method}.{hash}";

    public static bool IsValid(WorkspaceLifecycleRequest value, bool apply) => value.CalculateSize() <= 4096 &&
        value.Storage is not null && DevelopmentStorageBinding.IsValid(value.Storage) &&
        value.Action is "stop" or "resume" && value.ExpectedEpoch is >= 1 and < 0x7ffffffffffffffd &&
        (apply ? DevelopmentStorageBinding.IsUuid(value.OperationId) && Digest(value.PlanSha256) && Digest(value.StorageProofSha256) :
            value.OperationId.Length == 0 && value.PlanSha256.Length == 0 && value.StorageProofSha256.Length == 0);

    // Sorted ASCII-only keys agree byte-for-byte with the host canonical JSON.
    public static string HostInput(WorkspaceLifecycleRequest value) => JsonSerializer.Serialize(new
    {
        action = value.Action, expectedEpoch = value.ExpectedEpoch, operationId = value.OperationId,
        planSha256 = value.PlanSha256,
        storage = new { diskMiB = value.Storage.DiskMib, environmentSha256 = value.Storage.EnvironmentSha256,
            rootId = value.Storage.StorageRootId, schemaVersion = 1, workspaceId = value.Storage.WorkspaceId },
        storageProofSha256 = value.StorageProofSha256
    });

    public static bool Matches(WorkspaceLifecycleRequest request, WorkspaceLifecycleReceipt receipt, bool apply) =>
        receipt.WorkspaceId == request.Storage.WorkspaceId && receipt.Action == request.Action &&
        receipt.Epoch == request.ExpectedEpoch + (apply ? 1u : 0u) &&
        receipt.State == ((request.Action == "stop") == apply ? "Stopped" : "Active") &&
        Digest(receipt.PlanSha256) && Digest(receipt.StorageProofSha256) &&
        (!apply || receipt.PlanSha256 == request.PlanSha256 && receipt.StorageProofSha256 == request.StorageProofSha256) &&
        receipt.RequestSha256 == Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(HostInput(request))));
}
