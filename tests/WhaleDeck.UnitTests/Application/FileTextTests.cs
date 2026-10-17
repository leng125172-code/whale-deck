using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;
using Xunit;

namespace WhaleDeck.UnitTests.Application;

public sealed class FileTextTests
{
    private static readonly TerminalActor Actor = new("admin", null, [], new('A', 64), "trace", null);
    private static readonly FileLocationDto Location = new("files.shared", "sample.txt");
    private const string Version = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
    private static readonly string[] ValidRead = ["Started", "Validated"];
    private static readonly string[] IncompleteRead = ["Started", "Incomplete"];
    private static readonly string[] AuditFailure = ["Started", "Validated", "Incomplete"];
    private static List<FileDownloadPart> Frames(byte[] bytes)
    {
        List<FileDownloadPart> frames = [new("metadata", new("sample.txt", "File", bytes.Length, 0, Version, "0644", false), default, 0, "", "", 0)];
        for (var offset = 0; offset < bytes.Length; offset += 32768)
            frames.Add(new("chunk", null, bytes.AsMemory(offset, Math.Min(32768, bytes.Length - offset)), offset, "", "", 0));
        frames.Add(new("end", null, default, 0, Convert.ToHexString(SHA256.HashData(bytes)), Version, bytes.Length));
        return frames;
    }
    private static FileTextService Service(Gateway gateway, Repository repository, bool administrator = true) => new(
        new FileManagementService(gateway, repository, null!, new Identity(administrator), TimeProvider.System), gateway, repository, TimeProvider.System);
    [Theory]
    [InlineData("中英 O0Il\r\nline\r\n", "CRLF")][InlineData("a\nb", "LF")][InlineData("a\rb", "CR")][InlineData("a\r\nb\nc", "Mixed")][InlineData("", "None")]
    public async Task FullUtf8BomAndExactLineEndingsAreReturnedOnlyAfterSuccessfulAudit(string text, string ending)
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(text)).ToArray(); var gateway = new Gateway(Frames(bytes)); var repository = new Repository();
        var result = await Service(gateway, repository).ReadAsync(Actor, Location, Version, default);
        Assert.Equal(text, result.Text); Assert.Equal(ending, result.LineEnding); Assert.True(result.Utf8Bom);
        Assert.Equal(bytes.Length, result.SizeBytes); Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), result.Sha256);
        Assert.Equal(ValidRead, repository.Audits); Assert.Equal(FileReadPurpose.TextPreview, repository.Purpose);
    }
    [Fact]
    public async Task ExactOneMiBLimitIsAcceptedAcrossBoundedFramesWithFullChecksum()
    {
        var bytes = Encoding.UTF8.GetBytes(new string('a', FileTextService.MaximumBytes)); var repository = new Repository(); var gateway = new Gateway(Frames(bytes));
        var result = await Service(gateway, repository).ReadAsync(Actor, Location, Version, default);
        Assert.Equal(FileTextService.MaximumBytes, result.SizeBytes); Assert.Equal(bytes.Length, result.Text.Length);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), result.Sha256); Assert.Equal(34, gateway.Reads); Assert.Equal(ValidRead, repository.Audits);
    }
    [Fact]
    public async Task AlreadyCanceledRequestDoesNotReadOrCreateAnAudit()
    {
        var gateway = new Gateway(Frames([])); var repository = new Repository(); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(gateway, repository).ReadAsync(Actor, Location, Version, cancellation.Token));
        Assert.Equal(0, gateway.Reads); Assert.Empty(repository.Audits);
    }
    [Fact]
    public async Task OrdinaryUserCannotTriggerAReadOrAnAudit()
    {
        var gateway = new Gateway(Frames([])); var repository = new Repository();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service(gateway, repository, false).ReadAsync(Actor, Location, Version, default));
        Assert.Equal(0, gateway.Reads); Assert.Empty(repository.Audits);
    }
    [Fact]
    public async Task OversizedMetadataStopsBeforeADataFrameAndAuditsIncomplete()
    {
        var frames = Frames([]); frames[0] = frames[0] with { Metadata = frames[0].Metadata! with { SizeBytes = FileTextService.MaximumBytes + 1 } };
        var gateway = new Gateway(frames); var repository = new Repository();
        await Assert.ThrowsAsync<ArgumentException>(() => Service(gateway, repository).ReadAsync(Actor, Location, Version, default));
        Assert.Equal(1, gateway.Reads); Assert.Equal(IncompleteRead, repository.Audits);
    }
    [Theory]
    [InlineData("utf8")][InlineData("nul")][InlineData("missing-end")][InlineData("bad-checksum")][InlineData("bad-offset")][InlineData("after-end")][InlineData("wrong-version")]
    public async Task InvalidTextOrStreamNeverReturnsPartialContent(string kind)
    {
        var frames = Frames(kind == "utf8" ? new byte[] { 0xFF } : kind == "nul" ? new byte[] { 0 } : Encoding.UTF8.GetBytes("text"));
        if (kind == "missing-end") frames.RemoveAt(frames.Count - 1);
        if (kind == "bad-checksum") frames[^1] = frames[^1] with { Sha256 = new('C', 64) };
        if (kind == "bad-offset") frames[1] = frames[1] with { Offset = 1 };
        if (kind == "after-end") frames.Add(frames[^1]);
        if (kind == "wrong-version") frames[0] = frames[0] with { Metadata = frames[0].Metadata! with { Version = new('C', 64) } };
        var repository = new Repository();
        var error = await Assert.ThrowsAnyAsync<Exception>(() => Service(new Gateway(frames), repository).ReadAsync(Actor, Location, Version, default));
        var code = kind switch { "utf8" => "FILES_TEXT_UTF8_REQUIRED", "nul" => "FILES_TEXT_BINARY_REJECTED", "missing-end" => "FILES_TEXT_STREAM_INCOMPLETE", "bad-checksum" => "FILES_TEXT_CHECKSUM_MISMATCH", _ => "FILES_TEXT_STREAM_INVALID" };
        Assert.Equal(code, error.Message);
        if (kind is "utf8" or "nul") Assert.IsType<ArgumentException>(error); else Assert.IsType<InvalidOperationException>(error);
        Assert.Equal(IncompleteRead, repository.Audits);
    }
    [Fact]
    public async Task AuditFailurePreventsDeliveryEvenWhenTheTextIsValid()
    {
        var repository = new Repository { FailValidated = true };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(new Gateway(Frames(Encoding.UTF8.GetBytes("not-delivered"))), repository).ReadAsync(Actor, Location, Version, default));
        Assert.Equal(AuditFailure, repository.Audits);
    }
    private sealed class Identity(bool administrator) : IIdentityDirectory
    {
        public Task<CurrentUserDto> ResolveCurrentAsync(string subject, string? name, IReadOnlyCollection<string> groups, CancellationToken cancellationToken) => Task.FromResult(new CurrentUserDto(subject, name, groups, administrator, DateTimeOffset.UtcNow.AddHours(2)));
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListUsersAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListGroupsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListSsoApplicationsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed class Gateway(List<FileDownloadPart> frames) : IFileGateway
    {
        public int Reads { get; private set; }
        public async IAsyncEnumerable<FileDownloadPart> DownloadFileAsync(FileLocationDto location, string expectedVersion, [EnumeratorCancellation] CancellationToken cancellationToken)
        { foreach (var frame in frames) { cancellationToken.ThrowIfCancellationRequested(); Reads++; yield return frame; } await Task.CompletedTask; }
        public Task<IReadOnlyCollection<FileRootDto>> RootsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FilePageDto> ListFilesAsync(FileLocationDto location, string after, int take, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FileEntryDto?> InspectFileAsync(FileLocationDto location, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FileUploadReceiptDto> StageFileAsync(FileUploadOperation operation, IAsyncEnumerable<ReadOnlyMemory<byte>> bytes, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FileUploadReceiptDto?> UploadReceiptAsync(FileUploadOperation operation, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FileUploadReceiptDto> PublishFileAsync(FileUploadOperation operation, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FileUploadReceiptDto> DiscardFileAsync(FileUploadOperation operation, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed class Repository : IFileUploadRepository
    {
        public List<string> Audits { get; } = []; public FileReadPurpose Purpose { get; private set; } public bool FailValidated { get; init; }
        public Task AuditReadAsync(TerminalActor actor, FileLocationDto location, string result, FileReadPurpose purpose, CancellationToken cancellationToken)
        { Audits.Add(result); Purpose = purpose; return result == "Validated" && FailValidated ? Task.FromException(new InvalidOperationException("audit unavailable")) : Task.CompletedTask; }
        public Task<FileUploadOperation?> OwnedAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FileUploadOperation> EnqueueAsync(TerminalActor actor, Guid id, string key, string requestHash, FileUploadPlanClaims claims, DateTimeOffset expires, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FileUploadOperation> BeginReceiveAsync(TerminalActor actor, Guid id, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> MayReceiveAsync(Guid id, Guid token, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ReadyAsync(FileUploadOperation operation, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CancelAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FileUploadOperation?> ClaimAsync(DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> RenewAsync(FileUploadOperation operation, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> CancellationRequestedAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FileUploadStartResult> StartedAsync(FileUploadOperation operation, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> FinishAsync(FileUploadOperation operation, string state, string code, FileUploadReceiptDto? receipt, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeferAsync(FileUploadOperation operation, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
