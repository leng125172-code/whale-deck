using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Google.Protobuf;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Contracts;

public static partial class DevelopmentStorageBinding
{
    public const uint MinimumProtocolMinor = 62;
    public const string Capability = "development.storage.allocate";
    public const string Method = "/whaledeck.agent.v1.DevelopmentStorageService/Allocate";

    public static bool IsSupported(CapabilitiesResponse value) => value.ProtocolMajor == 1 &&
        value.ProtocolMinor >= MinimumProtocolMinor && value.Capabilities.Contains(Capability);

    public static string Hash(WorkspaceAllocationRequest request) => Convert.ToHexString(SHA256.HashData(request.ToByteArray()));

    public static string Payload(string expires, string nonce, string method, string hash) =>
        $"whaledeck-development-storage-capability-v1\n{expires}.{nonce}.{method}.{hash}";

    public static bool IsValid(WorkspaceAllocationRequest request) =>
        IsUuid(request.WorkspaceId) && request.StorageRootId.Length is > 0 and <= 48 && RootId().IsMatch(request.StorageRootId) &&
        request.EnvironmentSha256.Length == 64 && request.EnvironmentSha256.All(c => c is >= 'a' and <= 'f' or >= '0' and <= '9') &&
        request.DiskMib is >= 256 and <= 1048576 &&
        IsContext(request.ActorSubject, 256) && IsContext(request.TraceId, 128);

    public static bool IsUuid(string? value) => Guid.TryParseExact(value, "D", out var id) && id != Guid.Empty && id.ToString("D") == value;

    public static bool Matches(WorkspaceAllocationRequest request, WorkspaceAllocationReceipt receipt) =>
        receipt.WorkspaceId == request.WorkspaceId && receipt.StorageRootId == request.StorageRootId &&
        receipt.EnvironmentSha256 == request.EnvironmentSha256 && receipt.DiskMib == request.DiskMib &&
        receipt.ProjectQuotaVerified;

    private static bool IsContext(string value, int maximum) =>
        value.Length <= maximum && !string.IsNullOrWhiteSpace(value) && !value.Any(char.IsControl);

    [GeneratedRegex(@"\A[a-z0-9]+(?:-[a-z0-9]+)*\z", RegexOptions.CultureInvariant)]
    private static partial Regex RootId();
}
