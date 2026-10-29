using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Grpc.Core;
using WhaleDeck.Agent.Services;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

public sealed class ContainerFilePolicyTests
{
    private static readonly string Id = new('a', 64);
    private static readonly string Version = new('A', 64);
    private const string Instance = "777:2026-10-21T01:02:03.123456789Z";
    private static ContainerFileLocation Location(string path = "目录/中文.bin") => new() { ContainerId = Id, InstanceId = Instance, Path = path };
    private static FileEntry Entry(string name) => new() { Name = name, Kind = "File", Version = Version, Mode = "0640", SizeBytes = 1 };

    [Theory]
    [InlineData("ordinary")][InlineData("abc123")][InlineData("")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void OnlyFullImmutableContainerIdsAreAccepted(string id)
    {
        var location = Location(); location.ContainerId = id;
        Assert.Throws<ArgumentException>(() => ContainerFilePolicy.Location(location));
    }

    [Theory]
    [InlineData("1:2026-10-21T01:02:03Z")][InlineData("0777:2026-10-21T01:02:03Z")]
    [InlineData("2147483648:2026-10-21T01:02:03Z")][InlineData("777:2026-02-30T01:02:03Z")]
    [InlineData("777:2026-10-21T25:02:03Z")][InlineData("777:2026-10-21T01:02:03.1234567890Z")]
    [InlineData("777:2026-10-21T01:02:03Z\n")][InlineData("777:invalid")]
    public void StartupBindingRejectsInvalidPidCalendarOrAmbiguousTimestamp(string instance)
    {
        var location = Location(); location.InstanceId = instance;
        Assert.Throws<ArgumentException>(() => ContainerFilePolicy.Location(location));
    }

    [Fact]
    public void NanosecondIdentityAndUnicodePathsArePreservedWithoutNormalization()
    {
        var location = Location("目录/😀.txt"); ContainerFilePolicy.Location(location);
        Assert.Equal(Instance, location.InstanceId); Assert.Equal("目录/😀.txt", location.Path);
        ContainerFilePolicy.Location(Location(""), allowEmpty: true);
        Assert.Throws<ArgumentException>(() => ContainerFilePolicy.Location(Location("")));
        Assert.Throws<ArgumentException>(() => ContainerFilePolicy.Path("\ud800", false));
        Assert.Throws<ArgumentException>(() => ContainerFilePolicy.Path(new string('中', 86), false));
    }

    [Theory]
    [InlineData("/etc/passwd")][InlineData("../escape")][InlineData("a/../b")][InlineData("a/./b")]
    [InlineData("a//b")][InlineData("a/")][InlineData("a\\b")][InlineData("a\nb")]
    public void AmbiguousAndAbsolutePathsAreNeverHelperInputs(string path) =>
        Assert.Throws<ArgumentException>(() => ContainerFilePolicy.Location(Location(path)));

    [Fact]
    public void PaginationUsesScalarOrderAndRejectsDuplicateNamesOrInventedCursor()
    {
        var page = new FileListResponse { Version = Version, NextCursor = "😀" };
        page.Entries.Add(Entry("\ue000")); page.Entries.Add(Entry("😀"));
        ContainerFilePolicy.Page(page, "", 2);
        page.Entries.Add(Entry("😀"));
        Assert.Throws<InvalidOperationException>(() => ContainerFilePolicy.Page(page, "", 3));
        page.Entries.RemoveAt(2); page.NextCursor = "not-returned";
        Assert.Throws<InvalidOperationException>(() => ContainerFilePolicy.Page(page, "", 2));
        Assert.Throws<ArgumentException>(() => ContainerFilePolicy.Cursor("a/b"));
    }

    [Fact]
    public async Task InvalidRpcInputFailsBeforeAnyPlatformOrHelperAccess()
    {
        var service = new ContainerFileGrpcService(null!, null!, new());
        await Assert.ThrowsAsync<ArgumentException>(() => service.List(new() { Location = Location(), Take = 101 }, null!));
        await Assert.ThrowsAsync<ArgumentException>(() => service.Inspect(Location("../bad"), null!));
        Assert.Throws<ArgumentException>(() => { _ = service.Download(new() { Location = Location(), ExpectedVersion = "ABSENT" }, null!, null!); });
        var capabilities = await new AgentGrpcService(null!).GetCapabilities(new(), null!);
        Assert.Equal(46U, capabilities.ProtocolMinor); Assert.Contains("docker.files.read", capabilities.Capabilities);
        Assert.Contains("docker.files.copy-intents", capabilities.Capabilities);
        Assert.Contains("docker.files.move-intents", capabilities.Capabilities);
        Assert.Equal("whaledeck.agent.v1.ContainerFileService", ContainerFileService.Descriptor.FullName);
        Assert.Equal(4, ContainerFileService.Descriptor.Methods.Count);
        Assert.DoesNotContain(ContainerFileService.Descriptor.Methods, method => method.Name is "Upload" or "Exec" or "Run");
    }

    [Theory]
    [InlineData("CONTAINER_PROTECTED", StatusCode.PermissionDenied)]
    [InlineData("CONTAINER_FILES_PATH_REJECTED", StatusCode.PermissionDenied)]
    [InlineData("CONTAINER_FILES_VERSION_CONFLICT", StatusCode.Aborted)]
    [InlineData("CONTAINER_FILES_BUSY", StatusCode.ResourceExhausted)]
    [InlineData("CONTAINER_FILES_NOT_FOUND", StatusCode.NotFound)]
    [InlineData("CONTAINER_FILES_INSTANCE_CHANGED", StatusCode.FailedPrecondition)]
    [InlineData("CONTAINER_FILES_TIMEOUT", StatusCode.DeadlineExceeded)]
    [InlineData("CONTAINER_FILES_CLIENT_DISCONNECTED", StatusCode.Cancelled)]
    public void FixedErrorCodesKeepTheirSemantics(string code, StatusCode expected) =>
        Assert.Equal(expected, ContainerFilePolicy.Failure(code).StatusCode);

    [Fact]
    public void UnknownHelperExceptionsNeverEchoPrivateDiagnostics()
    {
        var error = ContainerFilePolicy.Failure("private password /etc/private-path");
        Assert.Equal(StatusCode.Unavailable, error.StatusCode);
        Assert.Equal("CONTAINER_FILES_UNAVAILABLE", error.Status.Detail);
    }

    [Theory]
    [InlineData(false, false)][InlineData(false, true)][InlineData(true, false)][InlineData(true, true)]
    public void HelperFamiliesAndExactInvocationStopsRemainSeparate(bool container, bool stop)
    {
        var method = typeof(FileTransferRunner).GetMethod("Start", BindingFlags.NonPublic | BindingFlags.Static)!;
        var start = Assert.IsType<ProcessStartInfo>(method.Invoke(null, [container ? "container:download" : "download", new string('a', 32), stop ? new string('b', 32) : null]));
        Assert.Equal("/usr/bin/sudo", start.FileName); Assert.False(start.UseShellExecute);
        var verb = container ? "container-file-transport" : "file-transport";
        if (stop) verb += "-stop";
        var expected = new List<string> { "-n", "/usr/local/libexec/whaledeck-privileged", verb };
        if (!stop) expected.Add("download");
        expected.Add(new('a', 32)); if (stop) expected.Add(new('b', 32));
        Assert.Equal(expected, start.ArgumentList);
        Assert.Equal(2, start.Environment.Count); Assert.Contains("LANG", start.Environment.Keys); Assert.Contains("PATH", start.Environment.Keys);
    }

    [Fact]
    public void DecoderChecksAllBytesAndOnlyCompletesAfterBoundHashAndVersion()
    {
        var bytes = Enumerable.Range(0, 65_543).Select(value => (byte)(value % 251)).ToArray();
        using var decoder = new ContainerFileDownloadDecoder("中文.bin", Version);
        Assert.Equal("metadata", decoder.Read(Metadata(bytes.Length)).Kind);
        for (var offset = 0; offset < bytes.Length; offset += 32768)
        {
            var chunk = bytes.AsSpan(offset, Math.Min(32768, bytes.Length-offset)).ToArray();
            var frame = decoder.Read(JsonSerializer.SerializeToElement(new { type = "chunk", offset, data = Convert.ToBase64String(chunk) }));
            Assert.Equal(offset, frame.Offset); Assert.Equal(chunk, frame.Data.ToByteArray());
        }
        Assert.False(decoder.Complete);
        var end = End(bytes);
        Assert.Equal("end", decoder.Read(end).Kind); Assert.True(decoder.Complete);
        Assert.Throws<InvalidOperationException>(() => decoder.Read(end));
    }

    [Fact]
    public void EmptyFileHasARealEmptyDigestNotAPlaceholder()
    {
        using var decoder = new ContainerFileDownloadDecoder("中文.bin", Version);
        decoder.Read(Metadata(0)); decoder.Read(End([])); Assert.True(decoder.Complete);
    }

    [Theory]
    [InlineData("wrong-name")][InlineData("wrong-version")][InlineData("protected")]
    [InlineData("negative-size")][InlineData("oversized")][InlineData("directory")]
    public void WrongOrUnsafeMetadataCannotBeginDownload(string variant)
    {
        var metadata = Metadata(variant == "negative-size" ? -1 : variant == "oversized" ? FileTransferPolicy.MaximumBytes+1 : 1,
            variant == "wrong-name" ? "other.bin" : "中文.bin", variant == "wrong-version" ? new('B',64) : Version,
            variant == "protected", variant == "directory" ? "Directory" : "File");
        using var decoder = new ContainerFileDownloadDecoder("中文.bin", Version);
        Assert.Throws<InvalidOperationException>(() => decoder.Read(metadata)); Assert.False(decoder.Complete);
    }

    [Theory]
    [InlineData("early-end")][InlineData("wrong-offset")][InlineData("wrong-hash")][InlineData("wrong-version")][InlineData("duplicate-metadata")]
    public void TruncatedChangedOrMisorderedStreamsNeverProduceSuccess(string variant)
    {
        using var decoder = new ContainerFileDownloadDecoder("中文.bin", Version);
        var metadata = Metadata(1); decoder.Read(metadata);
        if (variant is "wrong-hash" or "wrong-version")
            decoder.Read(JsonSerializer.SerializeToElement(new { type = "chunk", offset = 0, data = Convert.ToBase64String([42]) }));
        var bad = variant switch
        {
            "duplicate-metadata" => metadata,
            "wrong-offset" => JsonSerializer.SerializeToElement(new { type = "chunk", offset = 1, data = Convert.ToBase64String([42]) }),
            "wrong-hash" => JsonSerializer.SerializeToElement(new { type = "end", sizeBytes = 1, sha256 = new string('F',64), version = Version }),
            "wrong-version" => End([42], new('B',64)),
            _ => End([42])
        };
        Assert.Throws<InvalidOperationException>(() => decoder.Read(bad)); Assert.False(decoder.Complete);
    }

    private static JsonElement Metadata(long bytes, string name = "中文.bin", string? version = null, bool isProtected = false, string kind = "File") =>
        JsonSerializer.SerializeToElement(new { type = "metadata", name, kind, sizeBytes = bytes, modifiedAtUnixMs = 0, mode = "0640", version = version ?? Version, isProtected });
    private static JsonElement End(byte[] bytes, string? version = null) =>
        JsonSerializer.SerializeToElement(new { type = "end", sizeBytes = bytes.Length, sha256 = Convert.ToHexString(SHA256.HashData(bytes)), version = version ?? Version });
}
