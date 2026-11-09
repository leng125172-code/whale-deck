using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Google.Protobuf;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Contracts;

public static partial class DevelopmentRunnerBinding
{
    public const uint ProtocolMinor = 64;
    public const string Capability = "development.run";
    public const string Service = "/whaledeck.agent.v1.DevelopmentRunnerService/";
    public const string Start = Service + "Start";
    public const string Observe = Service + "Observe";
    public const int MaximumHostInputBytes = 32768;
    public const int MaximumWireBytes = MaximumHostInputBytes + 2048;
    public const int PreparationSeconds = 300;
    public const int ObserveSeconds = 20;
    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static bool IsSupported(CapabilitiesResponse value) => value.ProtocolMajor == 1 &&
        value.ProtocolMinor >= ProtocolMinor && value.Capabilities.Contains(Capability);

    public static string Hash(WorkspaceRunRequest request) => Convert.ToHexString(SHA256.HashData(request.ToByteArray()));

    public static string Payload(string expires, string nonce, string method, string hash) =>
        $"whaledeck-development-run-capability-v1\n{expires}.{nonce}.{method}.{hash}";

    public static bool IsValid(WorkspaceRunRequest request)
    {
        if (request.Storage is null || !DevelopmentStorageBinding.IsValid(request.Storage) ||
            !Guid.TryParseExact(request.RunId, "N", out var id) || id == Guid.Empty || id.ToString("N") != request.RunId ||
            request.EnvironmentJson.Length is < 1 or > 4096 || request.EnvironmentJson.Any(c => !char.IsAscii(c)) ||
            request.Runtime.Length is < 1 or > 48 || !RuntimeKey().IsMatch(request.Runtime) ||
            request.CpuMillis is < 100 or > 64000 || request.MemoryMib is < 128 or > 1048576 ||
            request.DeadlineSeconds is < 1 or > 3600 || request.Argv.Count is < 1 or > 64 ||
            request.Argv.Any(arg => arg.Length > 4096 || arg.Contains('\0') || Encoding.UTF8.GetByteCount(arg) > 4096) ||
            request.Argv.Sum(arg => Encoding.UTF8.GetByteCount(arg) + 1) > 16384)
            return false;
        var executable = request.Argv[0];
        return executable.StartsWith('/') && !executable.Split('/').Skip(1).Any(part => part is "" or "." or "..") &&
            Digest(request.EnvironmentJson) == request.Storage.EnvironmentSha256 &&
            Encoding.UTF8.GetByteCount(HostInput(request)) <= MaximumHostInputBytes;
    }

    public static string HostInput(WorkspaceRunRequest request) => JsonSerializer.Serialize(new
    {
        runId = request.RunId,
        storage = new
        {
            schemaVersion = 1, rootId = request.Storage.StorageRootId, workspaceId = request.Storage.WorkspaceId,
            environmentSha256 = request.Storage.EnvironmentSha256, diskMiB = request.Storage.DiskMib
        },
        environment = request.EnvironmentJson, runtime = request.Runtime, argv = request.Argv,
        cpuMillis = request.CpuMillis, memoryMiB = request.MemoryMib, deadlineSeconds = request.DeadlineSeconds
    }, Json);

    public static string Digest(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public static bool Matches(WorkspaceRunRequest request, WorkspaceRunReceipt receipt) =>
        receipt.RunId == request.RunId && receipt.InputSha256 == Digest(HostInput(request)) &&
        receipt.RequestSha256.Length == 64 && receipt.RequestSha256.All(c => c is >= 'a' and <= 'f' or >= '0' and <= '9') &&
        (receipt.Status switch
        {
            "Succeeded" => receipt.HasRuntimeExitCode && receipt.RuntimeExitCode == 0,
            "Failed" => receipt.HasRuntimeExitCode && receipt.RuntimeExitCode is >= 1 and <= 255,
            "TimedOut" => !receipt.HasRuntimeExitCode,
            _ => false
        });

    [GeneratedRegex(@"\A[a-z0-9]+(?:-[a-z0-9]+)*\z", RegexOptions.CultureInvariant)]
    private static partial Regex RuntimeKey();
}
