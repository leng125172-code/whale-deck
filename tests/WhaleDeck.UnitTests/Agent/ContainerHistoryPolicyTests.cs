using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using WhaleDeck.Agent.Services;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

public sealed class ContainerHistoryPolicyTests
{
    private static readonly string ContainerId = new('a', 64);
    private const string InstanceId = "777:2026-10-22T01:02:03.123456789Z";
    private const string CaptureId = "11111111111111111111111111111111";
    private const string NextCaptureId = "22222222222222222222222222222222";
    private static readonly string CaptureHash = new('A', 64);
    private static readonly string Version = new('B', 64);
    private static readonly string TargetProof = new('C', 64);

    [Theory]
    [InlineData("take-zero")]
    [InlineData("take-large")]
    [InlineData("invalid-cursor")]
    [InlineData("cursor-without-version")]
    public async Task InvalidListInputFailsBeforeContextOrHelperAccess(string variant)
    {
        var request = ListRequest();
        switch (variant)
        {
            case "take-zero": request.Take = 0; break;
            case "take-large": request.Take = 51; break;
            case "invalid-cursor": request.After = "not-an-identity"; request.ExpectedVersion = Version; break;
            case "cursor-without-version": request.After = CaptureId; break;
        }

        var service = new ContainerHistoryGrpcService(null!);
        await Assert.ThrowsAsync<ArgumentException>(() => service.List(request, null!));
    }

    [Theory]
    [InlineData("container")]
    [InlineData("instance")]
    [InlineData("path")]
    [InlineData("capture")]
    [InlineData("hash")]
    public void InvalidSourceInputFailsBeforeContextOrHelperAccess(string variant)
    {
        var request = SourceRequest();
        switch (variant)
        {
            case "container": request.Location.ContainerId = new('A', 64); break;
            case "instance": request.Location.InstanceId = "777:2026-10-22T01:02:03.1234567890Z"; break;
            case "path": request.Location.Path = "../escape"; break;
            case "capture": request.CaptureId = Guid.Empty.ToString("N"); break;
            case "hash": request.ExpectedCaptureHash = new('a', 64); break;
        }

        var service = new ContainerHistoryGrpcService(null!);
        Assert.Throws<ArgumentException>(() => { _ = service.ReadSource(request, null!, null!); });
    }

    [Fact]
    public void SourceValidationPreservesTheRawNineDigitInstanceIdentity()
    {
        var request = SourceRequest();
        ContainerHistoryPolicy.Source(request);
        Assert.Equal(InstanceId, request.Location.InstanceId);
    }

    [Fact]
    public void ValidBoundPageIsAcceptedWithoutChangingItsLocation()
    {
        var request = ListRequest();
        var page = Page(request);
        ContainerHistoryPolicy.Page(page, request);
        Assert.Equal(request.Location, page.Location);
    }

    [Theory]
    [InlineData("page-container")]
    [InlineData("page-instance")]
    [InlineData("page-path")]
    [InlineData("entry-container")]
    [InlineData("entry-path")]
    [InlineData("old-version")]
    [InlineData("stale-cursor")]
    [InlineData("duplicate-capture")]
    [InlineData("too-many-scanned")]
    [InlineData("impossible-counts")]
    [InlineData("incomplete-without-cursor")]
    [InlineData("complete-with-cursor")]
    public void PageRejectsCrossScopeStaleOrImpossibleOutput(string variant)
    {
        var request = ListRequest();
        var page = Page(request);
        switch (variant)
        {
            case "page-container": page.Location.ContainerId = new('b', 64); break;
            case "page-instance": page.Location.InstanceId = "778:2026-10-22T01:02:03.123456789Z"; break;
            case "page-path": page.Location.Path = "work/other.txt"; break;
            case "entry-container": page.Entries[0].ContainerId = new('b', 64); break;
            case "entry-path": page.Entries[0].Path = "work/other.txt"; break;
            case "old-version": request.ExpectedVersion = new('D', 64); break;
            case "stale-cursor":
                request.After = CaptureId; request.ExpectedVersion = Version;
                page.Entries.Clear(); page.Scanned = 0; page.Complete = false; page.NextCursor = CaptureId;
                break;
            case "duplicate-capture": page.Entries.Add(page.Entries[0].Clone()); page.Scanned = 2; break;
            case "too-many-scanned": page.Scanned = 501; break;
            case "impossible-counts": page.InvalidRecords = 1; break;
            case "incomplete-without-cursor": page.Complete = false; break;
            case "complete-with-cursor": page.NextCursor = NextCaptureId; break;
        }

        AssertOutputInvalid(() => ContainerHistoryPolicy.Page(page, request));
    }

    [Theory]
    [InlineData("missing-field")]
    [InlineData("owner")]
    [InlineData("current-false")]
    public void EntryRequiresTheExactPublicShape(string variant)
    {
        var value = EntryNode(Entry(7, new('D', 64)));
        switch (variant)
        {
            case "missing-field": value.Remove("sha256"); break;
            case "owner": value["owner"] = new string('E', 64); break;
            case "current-false": value["current"] = false; break;
        }

        AssertOutputInvalid(() => ContainerHistoryPolicy.Entry(Element(value)));
    }

    [Fact]
    public void DecoderCompletesTwoMiBPlusSevenOnlyAfterAllBoundFramesAndEnd()
    {
        var bytes = Enumerable.Range(0, 2 * 1024 * 1024 + 7).Select(index => (byte)(index % 251)).ToArray();
        using var decoder = new ContainerHistorySourceDecoder(SourceRequest());
        var metadata = decoder.Read(Metadata(bytes));
        Assert.Equal("metadata", metadata.Kind);
        Assert.False(decoder.Complete);

        var frames = 0;
        for (var offset = 0; offset < bytes.Length; offset += 32768)
        {
            var chunk = bytes.AsSpan(offset, Math.Min(32768, bytes.Length - offset)).ToArray();
            var frame = decoder.Read(Chunk(offset, chunk));
            Assert.Equal("chunk", frame.Kind);
            Assert.Equal(offset, frame.Offset);
            Assert.Equal(chunk, frame.Data.ToByteArray());
            frames++;
        }

        Assert.Equal(65, frames);
        Assert.False(decoder.Complete);
        var end = decoder.Read(End(bytes));
        Assert.Equal("end", end.Kind);
        Assert.Equal(bytes.Length, end.SizeBytes);
        Assert.Equal(Sha(bytes), end.Sha256);
        Assert.True(decoder.Complete);
    }

    [Fact]
    public void EmptySourceCompletesOnlyWithTheRealEmptyDigestAndBoundEnd()
    {
        var bytes = Array.Empty<byte>();
        using (var changed = new ContainerHistorySourceDecoder(SourceRequest()))
        {
            changed.Read(Metadata(bytes));
            AssertOutputInvalid(() => changed.Read(End(bytes, sha256: new string('F', 64))));
            Assert.False(changed.Complete);
        }

        using var decoder = new ContainerHistorySourceDecoder(SourceRequest());
        decoder.Read(Metadata(bytes));
        Assert.False(decoder.Complete);
        var end = decoder.Read(End(bytes));
        Assert.Equal(Sha(bytes), end.Sha256);
        Assert.True(decoder.Complete);
    }

    [Theory]
    [InlineData("capture")]
    [InlineData("container")]
    [InlineData("path")]
    [InlineData("capture-hash")]
    [InlineData("availability")]
    public void MetadataMustRemainBoundToTheRequestedPresentCapture(string variant)
    {
        var node = MetadataNode([42]);
        var entry = node["entry"]!.AsObject();
        switch (variant)
        {
            case "capture": entry["captureId"] = NextCaptureId; break;
            case "container": entry["containerId"] = new string('b', 64); break;
            case "path": entry["path"] = "work/other.txt"; break;
            case "capture-hash": entry["captureHash"] = new string('D', 64); break;
            case "availability": entry["availability"] = "Missing"; break;
        }

        using var decoder = new ContainerHistorySourceDecoder(SourceRequest());
        AssertOutputInvalid(() => decoder.Read(Element(node)));
        Assert.False(decoder.Complete);
    }

    [Theory]
    [InlineData("wrong-offset")]
    [InlineData("oversized-frame")]
    [InlineData("exceeds-declared-size")]
    [InlineData("early-end")]
    [InlineData("wrong-hash")]
    [InlineData("wrong-capture-hash")]
    [InlineData("wrong-proof")]
    [InlineData("duplicate-metadata")]
    public void ChangedTruncatedOrMisorderedFramesNeverComplete(string variant)
    {
        var content = variant == "oversized-frame" ? new byte[32769] : new byte[] { 42 };
        using var decoder = new ContainerHistorySourceDecoder(SourceRequest());
        var metadata = Metadata(content);
        decoder.Read(metadata);

        switch (variant)
        {
            case "wrong-offset":
                AssertOutputInvalid(() => decoder.Read(Chunk(1, content)));
                break;
            case "oversized-frame":
                AssertOutputInvalid(() => decoder.Read(Chunk(0, content)));
                break;
            case "exceeds-declared-size":
                AssertOutputInvalid(() => decoder.Read(Chunk(0, [42, 43])));
                break;
            case "early-end":
                AssertOutputInvalid(() => decoder.Read(End(content)));
                break;
            case "duplicate-metadata":
                AssertOutputInvalid(() => decoder.Read(metadata));
                break;
            case "wrong-hash":
                decoder.Read(Chunk(0, content));
                AssertOutputInvalid(() => decoder.Read(End(content, sha256: new string('F', 64))));
                break;
            case "wrong-capture-hash":
                decoder.Read(Chunk(0, content));
                AssertOutputInvalid(() => decoder.Read(End(content, captureHash: new string('D', 64))));
                break;
            case "wrong-proof":
                decoder.Read(Chunk(0, content));
                AssertOutputInvalid(() => decoder.Read(End(content, targetProof: new string('D', 64))));
                break;
        }

        Assert.False(decoder.Complete);
    }

    [Fact]
    public void DecoderRejectsEveryFrameAfterACompleteEnd()
    {
        var bytes = new byte[] { 42 };
        using var decoder = new ContainerHistorySourceDecoder(SourceRequest());
        decoder.Read(Metadata(bytes));
        decoder.Read(Chunk(0, bytes));
        decoder.Read(End(bytes));
        Assert.True(decoder.Complete);
        AssertOutputInvalid(() => decoder.Read(Chunk(bytes.Length, [43])));
    }

    [Fact]
    public void FullyReceivedBytesWithoutEndRemainIncomplete()
    {
        var bytes = new byte[] { 42 };
        using var decoder = new ContainerHistorySourceDecoder(SourceRequest());
        decoder.Read(Metadata(bytes));
        decoder.Read(Chunk(0, bytes));
        Assert.False(decoder.Complete);
    }

    private static ContainerHistoryListRequest ListRequest() => new()
    {
        Location = Location("work"),
        After = "",
        Take = 10,
        ExpectedVersion = ""
    };

    private static ContainerHistorySourceRequest SourceRequest() => new()
    {
        Location = Location("work/中文.txt"),
        CaptureId = CaptureId,
        ExpectedCaptureHash = CaptureHash
    };

    private static ContainerFileLocation Location(string path) => new()
    {
        ContainerId = ContainerId,
        InstanceId = InstanceId,
        Path = path
    };

    private static ContainerHistoryEntry Entry(long sizeBytes, string sha256, string path = "work") => new()
    {
        CaptureId = CaptureId,
        ContainerId = ContainerId,
        OriginalInstanceId = InstanceId,
        Path = path,
        SourceVersion = Version,
        SizeBytes = sizeBytes,
        Sha256 = sha256,
        CreatedAtUnixMs = 1760000000000L,
        DifferentSourceDevice = true,
        Availability = "Present",
        CaptureHash = CaptureHash
    };

    private static ContainerHistoryPage Page(ContainerHistoryListRequest request)
    {
        var page = new ContainerHistoryPage
        {
            Location = request.Location.Clone(),
            TargetProof = TargetProof,
            Version = Version,
            NextCursor = "",
            Scanned = 1,
            InvalidRecords = 0,
            Complete = true
        };
        page.Entries.Add(Entry(7, new('D', 64), request.Location.Path));
        return page;
    }

    private static JsonObject EntryNode(ContainerHistoryEntry entry) => new()
    {
        ["captureId"] = entry.CaptureId,
        ["containerId"] = entry.ContainerId,
        ["originalInstanceId"] = entry.OriginalInstanceId,
        ["path"] = entry.Path,
        ["sourceVersion"] = entry.SourceVersion,
        ["sizeBytes"] = entry.SizeBytes,
        ["sha256"] = entry.Sha256,
        ["createdAtUnixMs"] = entry.CreatedAtUnixMs,
        ["differentSourceDevice"] = entry.DifferentSourceDevice,
        ["availability"] = entry.Availability,
        ["captureHash"] = entry.CaptureHash
    };

    private static JsonObject MetadataNode(byte[] bytes) => new()
    {
        ["type"] = "metadata",
        ["entry"] = EntryNode(Entry(bytes.LongLength, Sha(bytes), SourceRequest().Location.Path)),
        ["targetProof"] = TargetProof
    };

    private static JsonElement Metadata(byte[] bytes) => Element(MetadataNode(bytes));

    private static JsonElement Chunk(long offset, byte[] bytes) => JsonSerializer.SerializeToElement(new
    {
        type = "chunk",
        offset,
        data = Convert.ToBase64String(bytes)
    });

    private static JsonElement End(byte[] bytes, string? sha256 = null, string? captureHash = null, string? targetProof = null) =>
        JsonSerializer.SerializeToElement(new
        {
            type = "end",
            sizeBytes = bytes.LongLength,
            sha256 = sha256 ?? Sha(bytes),
            captureHash = captureHash ?? CaptureHash,
            targetProof = targetProof ?? TargetProof
        });

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static JsonElement Element(JsonNode value)
    {
        using var document = JsonDocument.Parse(value.ToJsonString());
        return document.RootElement.Clone();
    }

    private static void AssertOutputInvalid(Action action)
    {
        var error = Assert.Throws<InvalidOperationException>(action);
        Assert.Equal("CONTAINER_HISTORY_OUTPUT_INVALID", error.Message);
    }
}
