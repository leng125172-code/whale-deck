using System.Text;
using System.Text.Json;
using Google.Protobuf;
using WhaleDeck.Agent.Services;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

public sealed class FileTransferPolicyTests
{
    private static FileUploadFrame Open() => new() { Header = new() { Location = new() { RootId = "whaledeck.files.shared", Path = "中文/readme.txt" },
        OperationId = "073e1d38e73f450a822640746147f35c", Owner = new('A', 64), SizeBytes = 3, Sha256 = new('B', 64), ExpectedVersion = "ABSENT", Context = new() { ActorSubject = "administrator" } } };
    [Theory]
    [InlineData("/etc/passwd")][InlineData("../escape")][InlineData("a/../escape")][InlineData("a//b")]
    [InlineData("a/./b")][InlineData("C:\\Windows\\x")][InlineData("a\nb")][InlineData("")]
    public void AbsoluteTraversalAndAmbiguousPathsRejected(string path) => Assert.Throws<ArgumentException>(() => FileTransferPolicy.Location(new() { RootId = "whaledeck.files.shared", Path = path }));
    [Fact]
    public void DirectoryRootAndUnicodeAreSupportedButByteBudgetsAreEnforced()
    {
        FileTransferPolicy.Location(new() { RootId = "whaledeck.files.shared", Path = "" }, true);
        FileTransferPolicy.Location(new() { RootId = "whaledeck.files.shared", Path = "资料/中文.txt" });
        Assert.Throws<ArgumentException>(() => FileTransferPolicy.Location(new() { RootId = "../root", Path = "file" }));
        Assert.Throws<ArgumentException>(() => FileTransferPolicy.Location(new() { RootId = "root", Path = new('中', 86) }));
    }
    [Fact]
    public void HeaderOnlyCarriesApprovedMetadataNotHumanProfileOrRawLogin()
    {
        using var input = JsonDocument.Parse(FileTransferPolicy.Open(Open()));
        Assert.Equal(7, input.RootElement.EnumerateObject().Count());
        Assert.Equal("中文/readme.txt", input.RootElement.GetProperty("path").GetString());
        Assert.DoesNotContain("administrator", input.RootElement.GetRawText(), StringComparison.Ordinal);
    }
    [Theory]
    [InlineData("root")][InlineData("00000000000000000000000000000000")][InlineData("073E1D38E73F450A822640746147F35C")]
    public void UploadIdentityMustBeCanonicalAndNonzero(string id)
    {
        var request = Open(); request.Header.OperationId = id;
        Assert.Throws<ArgumentException>(() => FileTransferPolicy.Open(request));
    }
    [Fact]
    public void UploadCannotExceedBudgetChangeContextOrMixHeaderWithData()
    {
        var request = Open(); request.Data = ByteString.CopyFromUtf8("a");
        Assert.Throws<ArgumentException>(() => FileTransferPolicy.Open(request));
        request = Open(); request.Header.SizeBytes = FileTransferPolicy.MaximumBytes + 1;
        Assert.Throws<ArgumentException>(() => FileTransferPolicy.Open(request));
        request = Open(); request.Header.Owner = "raw-login";
        Assert.Throws<ArgumentException>(() => FileTransferPolicy.Open(request));
        request = Open(); request.Header.Context.ActorSubject = "admin\nother";
        Assert.Throws<ArgumentException>(() => FileTransferPolicy.Open(request));
    }
    [Fact]
    public void DataIsSequentialBoundedAndRequiresExactFinalSize()
    {
        long offset = 0;
        Assert.Throws<ArgumentException>(() => FileTransferPolicy.Input(new() { Commit = true }, ref offset, 3));
        Assert.Throws<ArgumentException>(() => FileTransferPolicy.Input(new() { Data = ByteString.CopyFromUtf8("a"), Offset = 1 }, ref offset, 3));
        Assert.Throws<ArgumentException>(() => FileTransferPolicy.Input(Open(), ref offset, 3));
        var json = FileTransferPolicy.Input(new() { Data = ByteString.CopyFromUtf8("abc") }, ref offset, 3);
        Assert.Equal(3, offset); Assert.Contains(Convert.ToBase64String(Encoding.UTF8.GetBytes("abc")), json, StringComparison.Ordinal);
        Assert.Equal("{\"type\":\"commit\"}", FileTransferPolicy.Input(new() { Commit = true }, ref offset, 3));
        Assert.Throws<ArgumentException>(() => FileTransferPolicy.Input(new() { Data = ByteString.CopyFrom(new byte[32769]), Offset = offset }, ref offset, 100000));
    }
    [Fact]
    public void RejectedHelperCleanupDropsBufferedInputAndPreservesOriginalFailure()
    {
        var stream = new RejectingPipe();
        var writer = new StreamWriter(stream);
        writer.Write("buffered-private-body");
        FileTransferPipe.AbortInput(writer);
        Assert.True(stream.Disposed);
        Assert.Equal(0, stream.WritesWhileOpen);
        FileTransferPipe.AbortInput(writer);
    }
    [Theory]
    [InlineData("upload", true)][InlineData("stage", true)]
    [InlineData("roots", false)][InlineData("list", false)][InlineData("stat", false)]
    [InlineData("download", false)][InlineData("status", false)][InlineData("publish", false)][InlineData("discard", false)]
    [InlineData("operation-plan", false)][InlineData("operation-apply", false)][InlineData("operation-status", false)]
    [InlineData("operation-discard", false)]
    public void ReceiveModesKeepBodyPipeOpenAfterReadyButUnaryActionsSendEof(string action, bool streaming)
    {
        using var pipe = new MemoryStream();
        using var input = new StreamWriter(pipe);
        input.WriteLine("opening-request"); input.Flush();
        FileTransferPipe.CompleteOpeningInput(input, action);
        Assert.Equal(streaming, pipe.CanWrite);
        if (streaming)
        {
            input.WriteLine("body-then-commit"); input.Flush();
            Assert.Contains("body-then-commit", Encoding.UTF8.GetString(pipe.ToArray()), StringComparison.Ordinal);
            input.Close(); Assert.False(pipe.CanWrite);
        }
        else Assert.Throws<ObjectDisposedException>(() => input.WriteLine("unexpected-body"));
    }
    private sealed class RejectingPipe : MemoryStream
    {
        public bool Disposed { get; private set; }
        public int WritesWhileOpen { get; private set; }
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (Disposed) throw new IOException("private rejected input");
            WritesWhileOpen++;
            base.Write(buffer, offset, count);
        }
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (Disposed) throw new IOException("private rejected input");
            WritesWhileOpen++;
            base.Write(buffer);
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
}
