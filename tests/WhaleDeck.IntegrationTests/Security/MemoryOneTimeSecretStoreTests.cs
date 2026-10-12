using WhaleDeck.Infrastructure.Secrets;

namespace WhaleDeck.IntegrationTests.Secrets;

public sealed class MemoryOneTimeSecretStoreTests
{
    [Fact]
    public async Task PreflightDoesNotRenewLifetimeAndExpiryFailsClosed()
    {
        var clock = new ManualClock();
        var store = new MemoryOneTimeSecretStore(clock);
        var ticket = await store.StoreAsync("owner", "candidate-config", CancellationToken.None);
        clock.Now = clock.Now.AddMinutes(4);
        Assert.Equal("candidate-config", await store.PeekAsync("owner", ticket.Token, default));
        clock.Now = ticket.ExpiresAtUtc;
        Assert.Null(await store.PeekAsync("owner", ticket.Token, default));
        Assert.Null(await store.ConsumeAsync("owner", ticket.Token, default));
    }

    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 13, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task OwnerPreflightDoesNotConsumeTheTicketOrPermitAnotherOwner()
    {
        var store = new MemoryOneTimeSecretStore();
        var ticket = await store.StoreAsync("owner-a", "candidate-config", CancellationToken.None);
        Assert.Null(await store.PeekAsync("owner-b", ticket.Token, CancellationToken.None));
        Assert.Equal("candidate-config", await store.PeekAsync("owner-a", ticket.Token, CancellationToken.None));
        Assert.Equal("candidate-config", await store.PeekAsync("owner-a", ticket.Token, CancellationToken.None));
        Assert.Equal("candidate-config", await store.ConsumeAsync("owner-a", ticket.Token, CancellationToken.None));
        Assert.Null(await store.PeekAsync("owner-a", ticket.Token, CancellationToken.None));
    }

    [Fact]
    public async Task SecretCanOnlyBeConsumedOnceByItsOwner()
    {
        var store = new MemoryOneTimeSecretStore();
        var ticket = await store.StoreAsync("owner-a", "top-secret", CancellationToken.None);

        Assert.Null(await store.ConsumeAsync("owner-b", ticket.Token, CancellationToken.None));
        Assert.Equal("top-secret", await store.ConsumeAsync("owner-a", ticket.Token, CancellationToken.None));
        Assert.Null(await store.ConsumeAsync("owner-a", ticket.Token, CancellationToken.None));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-token")]
    [InlineData("../../etc/passwd")]
    public async Task InvalidTokensAreRejected(string token)
    {
        var store = new MemoryOneTimeSecretStore();

        Assert.Null(await store.ConsumeAsync("owner", token, CancellationToken.None));
        Assert.Null(await store.PeekAsync("owner", token, CancellationToken.None));
    }
}
