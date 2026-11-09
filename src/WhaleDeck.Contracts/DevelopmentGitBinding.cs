using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Google.Protobuf;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Contracts;

public static partial class DevelopmentGitBinding
{
    public const uint ProtocolMinor = 70;
    public const string Capability = "development.source.git";
    public const string Service = "/whaledeck.agent.v1.DevelopmentGitService/";
    public const string Acquire = Service + "Acquire";
    public const int MaximumHeaderBytes = 2048;

    public static string Hash(WorkspaceGitRequest request) =>
        Convert.ToHexString(SHA256.HashData(request.ToByteArray()));

    public static string Payload(string expires, string nonce, string method, string hash) =>
        $"whaledeck-development-git-capability-v1\n{expires}.{nonce}.{method}.{hash}";

    public static bool IsSupported(CapabilitiesResponse response) =>
        response.ProtocolMajor == 1 && response.ProtocolMinor >= ProtocolMinor && response.Capabilities.Contains(Capability);

    public static bool IsValid(WorkspaceGitRequest request) =>
        request.CalculateSize() <= MaximumHeaderBytes &&
        request.Storage is not null && DevelopmentStorageBinding.IsValid(request.Storage) &&
        Guid.TryParseExact(request.ImportId, "N", out var id) && id != Guid.Empty && id.ToString("N") == request.ImportId &&
        IsIdentifier(request.RepositoryId) && (request.CredentialRef.Length == 0 || IsIdentifier(request.CredentialRef)) &&
        request.Ref.Length is > 0 and <= 255 &&
        (request.Ref == "HEAD" || IsObjectId(request.Ref) || RefName().IsMatch(request.Ref));

    public static bool Matches(WorkspaceGitRequest request, WorkspaceGitSnapshot snapshot) =>
        snapshot.WorkspaceId == request.Storage.WorkspaceId && snapshot.ImportId == request.ImportId &&
        snapshot.RepositoryId == request.RepositoryId && snapshot.RequestedRef == request.Ref &&
        IsObjectId(snapshot.ResolvedObject) && IsObjectId(snapshot.Commit) && snapshot.Commit.Length == snapshot.ResolvedObject.Length &&
        (!IsObjectId(request.Ref) || snapshot.ResolvedObject == request.Ref) &&
        snapshot.ArchiveBytes is > 0 and <= DevelopmentSourceBinding.MaximumArchiveBytes &&
        snapshot.ArchiveSha256.Length == 64 && IsHex(snapshot.ArchiveSha256) &&
        snapshot.FileCount is > 0 and < 10000 && snapshot.TotalBytes <= snapshot.ArchiveBytes;

    public static byte[] HostInput(WorkspaceGitRequest request) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        repositoryId = request.RepositoryId,
        @ref = request.Ref,
        credentialRef = request.CredentialRef.Length == 0 ? null : request.CredentialRef
    });

    private static bool IsIdentifier(string value) => value.Length is > 0 and <= 48 && Identifier().IsMatch(value);
    private static bool IsObjectId(string value) => value.Length is 40 or 64 && IsHex(value) && value.Any(c => c != '0');
    private static bool IsHex(string value) => value.All(c => c is >= 'a' and <= 'f' or >= '0' and <= '9');

    [GeneratedRegex(@"\A[a-z0-9]+(?:-[a-z0-9]+)*\z", RegexOptions.CultureInvariant)]
    private static partial Regex Identifier();

    [GeneratedRegex(@"\Arefs/[A-Za-z0-9_./-]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex RefName();
}
