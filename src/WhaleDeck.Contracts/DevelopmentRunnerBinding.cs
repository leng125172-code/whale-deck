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
    public const uint ControlProtocolMinor = 65;
    public const uint ReportProtocolMinor = 66;
    public const uint ArtifactTransferProtocolMinor = 67;
    public const uint RestoreProtocolMinor = 75;
    public const string Capability = "development.run";
    public const string ControlCapability = "development.run.control";
    public const string ReportCapability = "development.run.test-artifact";
    public const string ArtifactTransferCapability = "development.run.test-artifact.transfer";
    public const string RestoreCapability = "development.run.dotnet-restore";
    public const int MaximumArtifactBytes = 16 * 1024 * 1024;
    public const string Service = "/whaledeck.agent.v1.DevelopmentRunnerService/";
    public const string Start = Service + "Start";
    public const string Observe = Service + "Observe";
    public const string Cancel = Service + "Cancel";
    public const string Reclaim = Service + "Reclaim";
    public const string ReadTestArtifact = Service + "ReadTestArtifact";
    public const string RetireTestArtifact = Service + "RetireTestArtifact";
    public const int ArtifactChunkBytes = 64 * 1024;
    public const int MaximumHostInputBytes = 32768;
    public const int MaximumWireBytes = MaximumHostInputBytes + 2048;
    public const int PreparationSeconds = 300;
    public const int ObserveSeconds = 20;
    public const int ControlSeconds = 48;
    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly string[] NodeReport =
        ["/usr/local/bin/node", "--test", "--test-reporter=junit", "--test-reporter-destination=/tmp/whaledeck-results/tests.xml"];
    private static readonly string[] DotnetReport =
        ["/usr/share/dotnet/dotnet", "test", "--results-directory", "/tmp/whaledeck-results", "--logger", "trx;LogFileName=tests.trx"];
    private static readonly string[] LegacyDotnetReport =
        ["/usr/share/dotnet/dotnet", "test", "--no-restore", "--results-directory", "/tmp/whaledeck-results", "--logger", "trx;LogFileName=tests.trx"];
    private static readonly string[] DotnetBuild = ["/usr/share/dotnet/dotnet", "build"];

    public static bool RequiresRestoreSupport(WorkspaceRunRequest request) => request.Runtime == "dotnet" &&
        (request.Argv.SequenceEqual(DotnetBuild) || request.Argv.SequenceEqual(DotnetReport));

    public static bool SupportsRestore(CapabilitiesResponse value) => IsSupported(value) &&
        value.ProtocolMinor >= RestoreProtocolMinor && value.Capabilities.Contains(RestoreCapability);

    public static bool IsSupported(CapabilitiesResponse value, string method = Start) => value.ProtocolMajor == 1 &&
        value.ProtocolMinor >= ProtocolMinor && value.Capabilities.Contains(Capability) &&
        (method is Start or Observe || (method is Cancel or Reclaim &&
            value.ProtocolMinor >= ControlProtocolMinor && value.Capabilities.Contains(ControlCapability)));

    public static string Hash(WorkspaceRunRequest request) => Convert.ToHexString(SHA256.HashData(request.ToByteArray()));
    public static string Hash(WorkspaceTestArtifactRequest request) => Convert.ToHexString(SHA256.HashData(request.ToByteArray()));

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

    public static string HostInput(WorkspaceTestArtifactRequest request)
    {
        using var run = JsonDocument.Parse(HostInput(request.Run));
        return JsonSerializer.Serialize(new { run = run.RootElement, requestSha256 = request.RequestSha256,
            artifactSha256 = request.ArtifactSha256, size = request.Size }, Json);
    }

    public static bool IsValid(WorkspaceTestArtifactRequest request) =>
        request.Run is not null && IsValid(request.Run) && ReportFormat(request.Run) is not null &&
        IsDigest(request.RequestSha256) && IsDigest(request.ArtifactSha256) &&
        request.Size is > 0 and <= MaximumArtifactBytes &&
        Encoding.UTF8.GetByteCount(HostInput(request)) <= MaximumHostInputBytes + 512;

    public static bool Matches(WorkspaceTestArtifactRequest request, WorkspaceTestArtifactRetirement receipt) =>
        receipt.RunId == request.Run.RunId && receipt.RequestSha256 == request.RequestSha256 &&
        receipt.ArtifactSha256 == request.ArtifactSha256 && receipt.Size == request.Size &&
        receipt.InputSha256 == Digest(HostInput(request));

    public static string Digest(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public static string? ReportFormat(WorkspaceRunRequest request)
    {
        if (request.Runtime == "node" && request.Argv.SequenceEqual(NodeReport)) return "JUnit";
        if (request.Runtime == "dotnet" && (request.Argv.SequenceEqual(DotnetReport) || request.Argv.SequenceEqual(LegacyDotnetReport))) return "Trx";
        return null;
    }

    public static bool Matches(WorkspaceRunRequest request, WorkspaceRunReceipt receipt) =>
        receipt.RunId == request.RunId && receipt.InputSha256 == Digest(HostInput(request)) &&
        IsDigest(receipt.RequestSha256) && MatchesReport(ReportFormat(request), receipt) &&
        (receipt.Status switch
        {
            "Succeeded" => receipt.HasRuntimeExitCode && receipt.RuntimeExitCode == 0,
            "Failed" => receipt.HasRuntimeExitCode && receipt.RuntimeExitCode is >= 1 and <= 255,
            "TimedOut" or "Cancelled" or "Interrupted" => !receipt.HasRuntimeExitCode,
            _ => false
        });

    private static bool MatchesReport(string? format, WorkspaceRunReceipt receipt)
    {
        var report = receipt.TestReport;
        if (format is null) return report is null;
        if (report is null || report.Format != format) return false;
        var completed = receipt.Status is "Succeeded" or "Failed";
        return report.State == "Present"
            ? completed && report.Size is > 0 and <= MaximumArtifactBytes && IsDigest(report.Sha256)
            : report.Size == 0 && report.Sha256.Length == 0 &&
                (report.State == "Unavailable" || (completed && report.State is "Missing" or "Rejected" or "TooLarge"));
    }

    private static bool IsDigest(string value) =>
        value.Length == 64 && value.All(c => c is >= 'a' and <= 'f' or >= '0' and <= '9');

    [GeneratedRegex(@"\A[a-z0-9]+(?:-[a-z0-9]+)*\z", RegexOptions.CultureInvariant)]
    private static partial Regex RuntimeKey();
}
