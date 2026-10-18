using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.UnitTests.Application;

public sealed class FileContentSearchServiceTests
{
    private static readonly TerminalActor Actor = new("admin", null, [], new('A', 64), "content-trace", null);
    private static readonly FileContentSearchCommand Command = new("files.shared", "中文", "Api/V1\\[test]*", true, "opaque.cursor", 50, 4 * 1024 * 1024);
    [Fact]
    public async Task CurrentAdministratorAndLiteralQueryArePreservedAcrossBothAuthorityChecks()
    {
        var identity = new Identity(); var gateway = new Gateway(); var service = Create(identity, gateway);
        using var cancellation = new CancellationTokenSource();
        Assert.Same(gateway.Page, await service.SearchContentAsync(Actor, Command, cancellation.Token));
        Assert.Equal(2, identity.Calls); Assert.Equal(1, gateway.Calls);
        Assert.Equal(Command, gateway.Command); Assert.Equal(cancellation.Token, gateway.Cancellation);
    }
    [Theory]
    [InlineData(false, false, false)][InlineData(true, true, false)][InlineData(true, false, true)]
    public async Task OrdinaryWrongSubjectOrExpiredAuthorityCannotReadContent(bool admin, bool wrong, bool expired)
    {
        var identity = new Identity { Administrator = admin, WrongSubject = wrong, Expired = expired }; var gateway = new Gateway();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Create(identity, gateway).SearchContentAsync(Actor, Command, default));
        Assert.Equal(0, gateway.Calls);
    }
    [Fact]
    public async Task DowngradeAfterAgentScanDoesNotReturnSnippets()
    {
        var identity = new Identity { DropAfterFirst = true }; var gateway = new Gateway();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Create(identity, gateway).SearchContentAsync(Actor, Command, default));
        Assert.Equal(1, gateway.Calls); Assert.Equal(2, identity.Calls);
    }
    [Fact]
    public async Task CancellationAfterTransportDoesNotReturnLateContent()
    {
        using var cancellation = new CancellationTokenSource();
        var gateway = new Gateway { BeforeReturn = cancellation.Cancel };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create(new Identity(), gateway).SearchContentAsync(Actor, Command, cancellation.Token));
    }
    private static FileBrowserService Create(Identity identity, Gateway gateway) =>
        new(new FileManagementService(null!, null!, null!, identity, TimeProvider.System), gateway);
    private sealed class Identity : IIdentityDirectory
    {
        public int Calls { get; private set; }
        public bool Administrator { get; init; } = true;
        public bool WrongSubject { get; init; }
        public bool Expired { get; init; }
        public bool DropAfterFirst { get; init; }
        public Task<CurrentUserDto> ResolveCurrentAsync(string subject, string? name, IReadOnlyCollection<string> groups, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new CurrentUserDto(WrongSubject ? "other" : subject, name, groups,
                Administrator && (!DropAfterFirst || Calls == 1), DateTimeOffset.UtcNow.AddHours(Expired ? -1 : 2)));
        }
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListUsersAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListGroupsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListSsoApplicationsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed class Gateway : IFileBrowserGateway
    {
        public int Calls { get; private set; }
        public FileContentSearchCommand? Command { get; private set; }
        public CancellationToken Cancellation { get; private set; }
        public Action? BeforeReturn { get; init; }
        public FileContentSearchPage Page { get; } = new([], "cursor", 10, 0, false, "ContentByteBudget", false, 2, 12, 0, 0);
        public Task<FileContentSearchPage> SearchContentAsync(FileContentSearchCommand command, CancellationToken cancellationToken)
        { Calls++; Command = command; Cancellation = cancellationToken; BeforeReturn?.Invoke(); return Task.FromResult(Page); }
        public Task<FilePageDto> DirectoriesAsync(FileLocationDto location, string after, int take, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FileSearchPage> SearchAsync(FileSearchCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
