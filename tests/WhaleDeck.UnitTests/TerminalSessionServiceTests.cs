using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.UnitTests;

public sealed class TerminalSessionServiceTests
{
    private static readonly TerminalActor Actor = new("admin", "Admin", [], new('A', 64), "trace", null);
    [Fact]
    public async Task GrantRetryIsSameUnconsumedTokenAndNoPlaintextStored()
    {
        var repository = new Repository(); var clock = new Clock(); var service = Service(repository, clock);
        var command = new CreateHostTerminalCommand(true, Guid.NewGuid().ToString("D"));
        var first = await service.IssueHostAsync(Actor, command, default); var second = await service.IssueHostAsync(Actor, command, default);
        Assert.Equal(first, second); Assert.Equal(64, first.ConnectToken.Length);
        Assert.NotEqual(repository.Row!.GrantHash, first.ConnectToken);
        Assert.DoesNotContain(first.ConnectToken, JsonSerializer.Serialize(repository.Row));
        Assert.Equal(clock.Now.AddSeconds(30), first.GrantExpiresAtUtc); Assert.Equal(clock.Now.AddMinutes(10), first.ExpiresAtUtc);
    }
    [Theory]
    [InlineData(false, "f8d838ea-a2ba-4688-b16d-aed2b375fe4d")]
    [InlineData(true, "bad")]
    [InlineData(true, "00000000-0000-0000-0000-000000000000")]
    [InlineData(true, "F8D838EA-A2BA-4688-B16D-AED2B375FE4D")]
    public async Task ConfirmationAndCanonicalIdempotencyRequired(bool confirmed, string key)
    {
        var repository = new Repository();
        await Assert.ThrowsAsync<ArgumentException>(() => Service(repository, new()).IssueHostAsync(Actor, new(confirmed, key), default));
        Assert.Null(repository.Row);
    }
    [Theory]
    [InlineData("issue")][InlineData("claim")][InlineData("get")][InlineData("renew")]
    public async Task OrdinaryUserCannotIssueClaimReadOrRenew(string action)
    {
        var service = Service(new(), new(), false);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
        {
            if (action == "issue") await service.IssueHostAsync(Actor, new(true, Guid.NewGuid().ToString("D")), default);
            if (action == "claim") await service.ClaimAsync(Actor, Guid.NewGuid(), new('B', 64), default);
            if (action == "get") await service.GetAsync(Actor, Guid.NewGuid(), default);
            if (action == "renew") await service.RenewAsync(Actor, Guid.NewGuid(), default);
        });
    }
    [Fact]
    public async Task ConsumedOrExpiredGrantCannotBeReissuedAndStatusDoesNotLeakHashes()
    {
        var repository = new Repository(); var clock = new Clock(); var service = Service(repository, clock);
        var command = new CreateHostTerminalCommand(true, Guid.NewGuid().ToString("D")); var grant = await service.IssueHostAsync(Actor, command, default);
        clock.Now = clock.Now.AddSeconds(31);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.IssueHostAsync(Actor, command, default));
        var status = await service.GetAsync(Actor, grant.SessionId, default);
        Assert.Equal("Expired", status.State); Assert.DoesNotContain(repository.Row!.GrantHash, JsonSerializer.Serialize(status));
        Assert.DoesNotContain(Actor.LoginBinding, JsonSerializer.Serialize(status));
    }
    [Fact]
    public async Task CleanupRemainsPossibleAfterDemotionButDoesNotReauthorize()
    {
        var repository = new Repository(); var clock = new Clock(); var service = Service(repository, clock);
        var grant = await service.IssueHostAsync(Actor, new(true, Guid.NewGuid().ToString("D")), default);
        var demoted = Service(repository, clock, false);
        await demoted.CloseAsync(Actor, grant.SessionId, "TERMINAL_FORBIDDEN", null, default);
        Assert.Equal("Closed", repository.Row!.State);
    }
    [Fact]
    public async Task InvalidLoginBindingRejectedBeforeDirectoryAndStorage()
    {
        var repository = new Repository();
        await Assert.ThrowsAsync<ArgumentException>(() => Service(repository, new()).IssueHostAsync(Actor with { LoginBinding = "raw-session-reference" }, new(true, Guid.NewGuid().ToString("D")), default));
        Assert.Null(repository.Row);
    }
    private static TerminalSessionService Service(Repository repository, Clock clock, bool administrator = true) => new(repository, new Signer(), new Identity(administrator), clock);
    private sealed class Clock : TimeProvider { public DateTimeOffset Now { get; set; } = new(2026, 10, 16, 0, 0, 0, TimeSpan.Zero); public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Signer : ITerminalGrantSigner
    { public string Sign(Guid id, string subject, string binding) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id + subject + binding))); }
    private sealed class Identity(bool administrator) : IIdentityDirectory
    {
        public Task<CurrentUserDto> ResolveCurrentAsync(string subject, string? name, IReadOnlyCollection<string> groups, CancellationToken cancellationToken) => Task.FromResult(new CurrentUserDto(subject, name, groups, administrator, DateTimeOffset.UtcNow.AddHours(2)));
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListUsersAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListGroupsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListSsoApplicationsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed class Repository : ITerminalSessionRepository
    {
        public TerminalSession? Row { get; private set; }
        public Task<TerminalSession> IssueAsync(TerminalActor actor, TerminalSession proposed, DateTimeOffset now, CancellationToken cancellationToken) { Row ??= proposed; return Task.FromResult(Row); }
        public Task<TerminalSession> ClaimAsync(TerminalActor actor, Guid id, string grantHash, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<TerminalSession?> FindOwnedAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken) => Task.FromResult(Row);
        public Task<bool> RenewAsync(TerminalActor actor, Guid id, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CloseAsync(TerminalActor actor, Guid id, string code, int? exitCode, DateTimeOffset now, CancellationToken cancellationToken) { Row!.State = "Closed"; return Task.CompletedTask; }
    }
}
