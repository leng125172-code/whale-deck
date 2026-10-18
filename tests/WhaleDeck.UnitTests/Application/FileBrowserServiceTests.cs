using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.UnitTests.Application;

public sealed class FileBrowserServiceTests
{
    private static readonly TerminalActor Actor = new("admin", null, [], new('A', 64), "trace", null);
    [Theory]
    [InlineData(false, false)][InlineData(true, true)]
    public async Task BothReadsReauthorizeBeforeCallingAgent(bool administrator, bool wrongSubject)
    {
        var gateway = new Gateway(); var service = Service(gateway, administrator, wrongSubject);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.DirectoriesAsync(Actor, new("files.shared", ""), "", 100, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SearchAsync(Actor, new("files.shared", "", "readme"), default));
        Assert.Equal(0, gateway.Calls);
    }
    [Fact]
    public async Task AuthorizedQueriesKeepScopeCursorAndCancellationWithoutWritingAJob()
    {
        var gateway = new Gateway(); var service = Service(gateway, true, false); using var cancellation = new CancellationTokenSource();
        await service.DirectoriesAsync(Actor, new("files.shared", "中文"), "上一页", 50, cancellation.Token);
        var command = new FileSearchCommand("files.shared", "中文", "test[1]", "Directory", true, "opaque.cursor", 99);
        Assert.Same(gateway.Page, await service.SearchAsync(Actor, command, cancellation.Token));
        Assert.Equal(command, gateway.Command); Assert.Equal(cancellation.Token, gateway.Cancellation); Assert.Equal(2, gateway.Calls);
    }
    [Theory]
    [InlineData("../bad")][InlineData("a/b")][InlineData("a\\b")][InlineData("..")][InlineData("a\n")]
    public async Task DirectoryCursorCannotBecomeAPath(string cursor)
    {
        var gateway = new Gateway();
        await Assert.ThrowsAsync<ArgumentException>(() => Service(gateway, true, false).DirectoriesAsync(Actor, new("files.shared", ""), cursor, 100, default));
        Assert.Equal(0, gateway.Calls);
    }
    private static FileBrowserService Service(Gateway gateway, bool admin, bool wrong) => new(
        new FileManagementService(null!, null!, null!, new Identity(admin, wrong), TimeProvider.System), gateway);
    private sealed class Identity(bool admin, bool wrong) : IIdentityDirectory
    {
        public Task<CurrentUserDto> ResolveCurrentAsync(string subject, string? name, IReadOnlyCollection<string> groups, CancellationToken cancellationToken) =>
            Task.FromResult(new CurrentUserDto(wrong ? "other" : subject, name, groups, admin, DateTimeOffset.UtcNow.AddHours(2)));
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListUsersAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListGroupsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListSsoApplicationsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed class Gateway : IFileBrowserGateway
    {
        public int Calls { get; private set; }
        public FileSearchCommand? Command { get; private set; }
        public CancellationToken Cancellation { get; private set; }
        public FileSearchPage Page { get; } = new([], "cursor", 10000, 0, false, "ScanBudget", false);
        public Task<FilePageDto> DirectoriesAsync(FileLocationDto location, string after, int take, CancellationToken cancellationToken)
        { Calls++; return Task.FromResult(new FilePageDto([], "", new('A', 64))); }
        public Task<FileSearchPage> SearchAsync(FileSearchCommand command, CancellationToken cancellationToken)
        { Calls++; Command = command; Cancellation = cancellationToken; return Task.FromResult(Page); }
        public Task<FileContentSearchPage> SearchContentAsync(FileContentSearchCommand command, CancellationToken cancellationToken)
        { Calls++; return Task.FromResult(new FileContentSearchPage([], "", 0, 0, true, "", false, 0, 0, 0, 0)); }
    }
}
