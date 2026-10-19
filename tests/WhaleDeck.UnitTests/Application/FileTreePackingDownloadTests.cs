using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.UnitTests.Application;

// Synthetic bounded streams. Installed root policy and actual large bytes require separate acceptance.
public sealed class FileTreePackingDownloadTests
{
    private static readonly string[] PartKinds = ["metadata", "chunk", "end"];
    private static readonly string[] SuccessfulAudits = ["Started", "Validated"];
    [Theory]
    [InlineData("valid")][InlineData("role")][InlineData("digest")][InlineData("version")][InlineData("name")]
    [InlineData("size")][InlineData("missing-end")][InlineData("after-end")][InlineData("audit")]
    public async Task NewLoginUsesCurrentAuthorityAndExactCompletedArtifactWithFinalAudit(string variant)
    {
        using var fixture = new FileTreePackingTests();
        var row = fixture.Operation(); row.State = "Succeeded"; row.CompletedAtUtc = DateTimeOffset.UtcNow;
        row.PublishedVersion = new('F', 64); row.ArchiveBytes = 3;
        row.ArchiveSha256 = Convert.ToHexString(SHA256.HashData(new byte[] { 1, 2, 3 }));
        var actor = new TerminalActor("different-current-admin", null, [], new('9', 64), "pack-download-test", null);
        var repository = new Repository(row) { FailValidatedAudit = variant == "audit" };
        var identity = new Identity(); var gateway = new Gateway(row, variant, () => identity.Admin = false);
        var service = new FileTreePackingService(new(null!, null!, null!, identity, TimeProvider.System), repository, fixture.Protector, gateway, TimeProvider.System);
        var parts = new List<FileDownloadPart>();
        async Task Read() { await foreach (var part in service.DownloadAsync(actor, row.Id, row.PublishedVersion, default)) parts.Add(part); }
        if (variant == "valid")
        {
            await Read(); Assert.Equal(PartKinds, parts.Select(part => part.Kind));
            Assert.Equal(SuccessfulAudits, repository.Audits);
        }
        else
        {
            await Assert.ThrowsAnyAsync<Exception>(Read);
            Assert.Equal(variant == "audit" ? "Validated" : "Incomplete", repository.Audits.Last());
        }
        Assert.Equal(actor.Subject, gateway.LastActor?.Subject);
        Assert.Equal(actor.LoginBinding, gateway.LastActor?.LoginBinding);
        Assert.NotEqual(row.ActorSubject, gateway.LastActor?.Subject);
        Assert.Equal(row.OwnerHash, gateway.OriginalOwner);
        Assert.All(repository.AuditActors, item => Assert.Equal(actor.Subject, item));
        Assert.Equal(0, repository.OwnedCalls);
    }
    [Fact]
    public async Task OrdinaryUserIsDeniedBeforeAnyArtifactLookupOrCapability()
    {
        using var fixture = new FileTreePackingTests(); var row = fixture.Operation();
        var repository = new Repository(row); var gateway = new Gateway(row, "valid", () => { });
        var service = new FileTreePackingService(new(null!, null!, null!, new Identity { Admin = false }, TimeProvider.System), repository, fixture.Protector, gateway, TimeProvider.System);
        async Task Read() { await foreach (var _ in service.DownloadAsync(new("user", null, [], new('A', 64), "denied-test", null), row.Id, new('F', 64), default)) { } }
        await Assert.ThrowsAsync<UnauthorizedAccessException>(Read);
        Assert.Equal(0, repository.Lookups); Assert.Null(gateway.LastActor); Assert.Empty(repository.Audits);
    }
    private sealed class Identity : IIdentityDirectory
    {
        public bool Admin { get; set; } = true;
        public Task<CurrentUserDto> ResolveCurrentAsync(string subject, string? name, IReadOnlyCollection<string> groups, CancellationToken cancellationToken) =>
            Task.FromResult(new CurrentUserDto(subject, name, groups, Admin, DateTimeOffset.UtcNow.AddHours(2)));
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListUsersAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListGroupsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListSsoApplicationsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed class Gateway(FileTreePackingOperation row, string variant, Action downgrade) : IFileTreePackingGateway
    {
        public TerminalActor? LastActor { get; private set; } public string? OriginalOwner { get; private set; }
        public async IAsyncEnumerable<FileDownloadPart> DownloadTreePackingAsync(FileTreePackingOperation operation, TerminalActor actor, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Assert.Same(row, operation); LastActor = actor; OriginalOwner = operation.OwnerHash;
            await Task.Yield(); cancellationToken.ThrowIfCancellationRequested();
            yield return new("metadata", new(variant == "name" ? "other.tar" : "target.tar", "File", variant == "size" ? 4 : 3, 0,
                variant == "version" ? new('B', 64) : row.PublishedVersion!, "0640", false), default, 0, "", "", 0);
            yield return new("chunk", null, new byte[] { 1, 2, 3 }, 0, "", "", 0);
            if (variant == "missing-end") yield break;
            yield return new("end", null, default, 0, variant == "digest" ? new('D', 64) : row.ArchiveSha256!, row.PublishedVersion!, 3);
            if (variant == "after-end") throw new IOException("test-only lost RPC completion");
            if (variant == "role") downgrade();
        }
        public Task<FileTreePackingPlan> PlanTreePackingAsync(TerminalActor actor, FileTreePackingCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FileTreePackingReceipt?> InspectTreePackingAsync(FileTreePackingOperation operation, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FileTreePackingReceipt> ApplyTreePackingAsync(FileTreePackingOperation operation, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FileTreePackingReceipt> DiscardTreePackingAsync(FileTreePackingOperation operation, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed class Repository(FileTreePackingOperation row) : IFileTreePackingRepository
    {
        public List<string> Audits { get; } = []; public List<string> AuditActors { get; } = [];
        public int Lookups { get; private set; } public int OwnedCalls { get; private set; } public bool FailValidatedAudit { get; set; }
        public Task<FileTreePackingOperation?> ArtifactAsync(Guid id, CancellationToken cancellationToken) { Lookups++; return Task.FromResult<FileTreePackingOperation?>(row); }
        public Task AuditDownloadAsync(TerminalActor actor, FileTreePackingOperation operation, string result, CancellationToken cancellationToken)
        { Audits.Add(result); AuditActors.Add(actor.Subject); return result == "Validated" && FailValidatedAudit ? Task.FromException(new InvalidOperationException("test-only audit unavailable")) : Task.CompletedTask; }
        public Task<FileTreePackingOperation?> OwnedAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken) { OwnedCalls++; throw new NotSupportedException(); }
        public Task<FileTreePackingOperation> QueueAsync(TerminalActor actor, string key, FileTreePackingCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FileTreePackingOperation> ConfirmAsync(TerminalActor actor, FileTreePackingConfirmation claims, long version, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CancelAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FileTreePackingOperation?> ClaimAsync(DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> CancellationRequestedAsync(FileTreePackingOperation claimed, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> MarkPlanningAsync(FileTreePackingOperation claimed, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> FinishPlanningAsync(FileTreePackingOperation claimed, FileTreePackingPlan? plan, string code, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FileUploadStartResult> StartedAsync(FileTreePackingOperation claimed, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> RenewAsync(FileTreePackingOperation claimed, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> FinishAsync(FileTreePackingOperation claimed, string state, string code, FileTreePackingReceipt? receipt, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeferAsync(FileTreePackingOperation claimed, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AcknowledgeAsync(TerminalActor actor, Guid id, long version, FileTreePackingReceipt receipt, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
