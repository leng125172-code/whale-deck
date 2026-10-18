using System.Text.Json;
using WhaleDeck.Agent.Services;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

public sealed class FileContentSearchPolicyTests
{
    [Theory]
    [InlineData("")][InlineData(" ")][InlineData("a\n")][InlineData("a\u0085")]
    public void InvalidUnicodeAndControlsAreRejectedByApplicationAndRpc(string query)
    {
        Assert.ThrowsAny<ArgumentException>(() => FileBrowserService.ValidateContent(new("files.shared", "", query)));
        Assert.ThrowsAny<ArgumentException>(() => FileTransferPolicy.ContentSearch(Request(query)));
    }
    [Fact]
    public void LiteralPathsAndRegexCharactersRemainPlainText()
    {
        var query = "Api/V1\\[test]*中文";
        FileBrowserService.ValidateContent(new("files.shared", "", query)); FileTransferPolicy.ContentSearch(Request(query));
        Assert.Throws<ArgumentException>(() => FileBrowserService.ValidateContent(new("files.shared", "", new('中', 86))));
    }
    [Fact]
    public void UnpairedSurrogateCannotBeReplacedSilentlyDuringTransport()
    {
        var query = new string((char)0xd800, 1);
        Assert.ThrowsAny<ArgumentException>(() => FileBrowserService.ValidateContent(new("files.shared", "", query)));
        Assert.ThrowsAny<ArgumentException>(() => FileTransferPolicy.ContentSearch(Request(query)));
    }
    [Theory]
    [InlineData(0, 1)][InlineData(101, 1)][InlineData(50, 0)][InlineData(50, 16777217)]
    public void ByteAndPageBoundsAgree(int take, int bytes)
    {
        Assert.Throws<ArgumentException>(() => FileBrowserService.ValidateContent(new("files.shared", "", "needle", Take: take, MaxFileBytes: bytes)));
        var request = Request(); request.Take = (uint)take; request.MaxFileBytes = (uint)bytes;
        Assert.Throws<ArgumentException>(() => FileTransferPolicy.ContentSearch(request));
    }
    [Theory]
    [InlineData("../cursor")][InlineData("bad/opaque")]
    public void CursorCannotBecomeAHostPath(string cursor)
    {
        Assert.Throws<ArgumentException>(() => FileBrowserService.ValidateContent(new("files.shared", "", "needle", Cursor: cursor)));
        var request = Request(); request.Cursor = cursor;
        Assert.Throws<ArgumentException>(() => FileTransferPolicy.ContentSearch(request));
    }
    [Fact]
    public void TrustedRootOutputStillRequiresBoundedPrivateContentShape()
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(new { entries = new[] { new {
            path = "file.txt", entry = new { name = "file.txt", kind = "File", sizeBytes = 10, modifiedAtUnixMs = 1,
                version = new string('A', 64), mode = "0644", isProtected = false },
            lineNumber = 2, excerpt = "needle <script>", excerptTruncated = false } }, nextCursor = "", scannedEntries = 1,
            excludedEntries = 0, complete = true, stopReason = "", depthLimited = false, scannedFiles = 1,
            readBytes = 10, skippedNonTextFiles = 0, skippedOversizedFiles = 0 }));
        var result = FileTransferPolicy.ContentResponse(json.RootElement, Request());
        Assert.Equal("needle <script>", Assert.Single(result.Entries).Excerpt);
        var scoped = Request(); scoped.Location.Path = "other";
        Assert.Throws<InvalidOperationException>(() => FileTransferPolicy.ContentResponse(json.RootElement, scoped));
        var tiny = Request(); tiny.MaxFileBytes = 1;
        Assert.Throws<InvalidOperationException>(() => FileTransferPolicy.ContentResponse(json.RootElement, tiny));
    }
    private static FileContentSearchRequest Request(string query = "needle") => new()
    { Location = new() { RootId = "files.shared", Path = "" }, Query = query, Take = 50, MaxFileBytes = 16777216 };
}
