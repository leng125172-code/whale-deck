using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using WhaleDeck.Api.Security;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Services;
using WhaleDeck.Infrastructure.Identity;

namespace WhaleDeck.IntegrationTests.Security;

/// <summary>API ticket protection and atomic in-process semantics; real Valkey has a separate native gate.</summary>
public sealed class BffSessionLifecycleTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    internal static AuthenticationTicket Ticket(string subject, DateTimeOffset expires) => new(
        new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", subject)], "Cookies")), new() { ExpiresUtc = expires }, "Cookies");
    private static string Binding() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    [Theory]
    [InlineData(true)][InlineData(false)]
    public async Task EitherRenewLogoutOrderingLeavesTheLoginRevokedAcrossStoreInstances(bool removeFirst)
    {
        var clock = new Clock(); var storage = new MemoryBffSessionStorage(clock); var protection = new EphemeralDataProtectionProvider();
        var first = new ServerSideTicketStore(storage, protection, clock); var second = new ServerSideTicketStore(storage, protection, clock);
        var key = await first.StoreAsync(Ticket("subject", clock.Now.AddMinutes(10)));
        if (removeFirst) await first.RemoveAsync(key);
        await second.RenewAsync(key, Ticket("subject", clock.Now.AddMinutes(20)));
        if (!removeFirst) await first.RemoveAsync(key);
        Assert.Null(await second.RetrieveAsync(key)); Assert.False(await storage.IsActiveAsync("subject", BffSessionValidation.Binding(key), default));
    }
    [Fact]
    public async Task ConcurrentRenewersCannotRecreateTheDeletedReference()
    {
        var clock = new Clock(); var storage = new MemoryBffSessionStorage(clock); var protection = new EphemeralDataProtectionProvider();
        var first = new ServerSideTicketStore(storage, protection, clock); var second = new ServerSideTicketStore(storage, protection, clock);
        for (var run = 0; run < 64; run++)
        {
            var key = await first.StoreAsync(Ticket("subject", clock.Now.AddMinutes(10)));
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var renew = Task.Run(async () => { await ready.Task; await second.RenewAsync(key, Ticket("subject", clock.Now.AddMinutes(20))); });
            var remove = Task.Run(async () => { await ready.Task; await first.RemoveAsync(key); });
            ready.SetResult(); await Task.WhenAll(renew, remove);
            await second.RenewAsync(key, Ticket("subject", clock.Now.AddMinutes(20)));
            Assert.Null(await first.RetrieveAsync(key)); Assert.False(await storage.IsActiveAsync("subject", BffSessionValidation.Binding(key), default));
        }
    }
    [Fact]
    public async Task OtherLoginAndOtherSubjectAreNotAliasesOfTheOriginalSession()
    {
        var clock = new Clock(); var storage = new MemoryBffSessionStorage(clock); var api = new ServerSideTicketStore(storage, new EphemeralDataProtectionProvider(), clock);
        var one = await api.StoreAsync(Ticket("subject", clock.Now.AddMinutes(10)));
        var two = await api.StoreAsync(Ticket("subject", clock.Now.AddMinutes(10)));
        await api.RenewAsync(one, Ticket("another-subject", clock.Now.AddMinutes(20)));
        Assert.True(await storage.IsActiveAsync("subject", BffSessionValidation.Binding(one), default));
        Assert.False(await storage.IsActiveAsync("another-subject", BffSessionValidation.Binding(one), default));
        Assert.False(await storage.IsActiveAsync("subject", one, default));
        await api.RemoveAsync(one);
        Assert.Null(await api.RetrieveAsync(one)); Assert.NotNull(await api.RetrieveAsync(two));
        Assert.True(await storage.IsActiveAsync("subject", BffSessionValidation.Binding(two), default));
    }
    [Fact]
    public async Task ExpiryIsARevocationBoundaryAndAStaleOrExpiredRenewalDoesNotResurrectIt()
    {
        var clock = new Clock(); var storage = new MemoryBffSessionStorage(clock); var api = new ServerSideTicketStore(storage, new EphemeralDataProtectionProvider(), clock);
        var key = await api.StoreAsync(Ticket("subject", clock.Now.AddMinutes(10)));
        clock.Now = clock.Now.AddMinutes(11);
        Assert.False(await storage.IsActiveAsync("subject", BffSessionValidation.Binding(key), default));
        await api.RenewAsync(key, Ticket("subject", clock.Now.AddMinutes(10)));
        Assert.Null(await api.RetrieveAsync(key));
    }
    [Fact]
    public async Task AStaleShorterRenewalCannotReplaceANewerTicket()
    {
        var clock = new Clock(); var storage = new MemoryBffSessionStorage(clock); var api = new ServerSideTicketStore(storage, new EphemeralDataProtectionProvider(), clock);
        var key = await api.StoreAsync(Ticket("subject", clock.Now.AddMinutes(20)));
        var before = await storage.ReadAsync(BffSessionValidation.Binding(key), default);
        await api.RenewAsync(key, Ticket("subject", clock.Now.AddMinutes(10)));
        var after = await storage.ReadAsync(BffSessionValidation.Binding(key), default);
        Assert.Equal(before!.Revision, after!.Revision); Assert.Equal(before.ExpiresAt, after.ExpiresAt);
        Assert.Equal(before.ProtectedTicket.ToArray(), after.ProtectedTicket.ToArray());
    }
    [Fact]
    public async Task CorruptOldReadCannotDeleteAConcurrentValidRenewal()
    {
        var clock = new Clock(); var inner = new MemoryBffSessionStorage(clock); var storage = new Interleaved(inner);
        var api = new ServerSideTicketStore(storage, new EphemeralDataProtectionProvider(), clock);
        var key = await api.StoreAsync(Ticket("subject", clock.Now.AddMinutes(10))); var binding = BffSessionValidation.Binding(key);
        Assert.True(await inner.RenewAsync(binding, "subject", new byte[] { 1, 2, 3 }, clock.Now.AddMinutes(20), default));
        storage.AfterRead = () => api.RenewAsync(key, Ticket("subject", clock.Now.AddMinutes(30)));
        Assert.Null(await api.RetrieveAsync(key));
        Assert.NotNull(await api.RetrieveAsync(key)); Assert.True(await inner.IsActiveAsync("subject", binding, default));
    }
    [Fact]
    public async Task ProtectedTicketSubjectMustMatchTheStorageMetadata()
    {
        var clock = new Clock(); var storage = new MemoryBffSessionStorage(clock); var api = new ServerSideTicketStore(storage, new EphemeralDataProtectionProvider(), clock);
        var key = await api.StoreAsync(Ticket("alice", clock.Now.AddMinutes(10))); var binding = BffSessionValidation.Binding(key);
        var prior = (await storage.ReadAsync(binding, default))!;
        await storage.RemoveAsync(binding, null, default);
        Assert.True(await storage.CreateAsync(binding, "bob", prior.ProtectedTicket, prior.ExpiresAt, default));
        Assert.Null(await api.RetrieveAsync(key)); Assert.False(await storage.IsActiveAsync("bob", binding, default));
    }
    [Theory]
    [InlineData("too-large")][InlineData("too-long")][InlineData("no-subject")][InlineData("invalid-binding")][InlineData("invalid-unicode")]
    public async Task InvalidStorageInputsCannotCreateSessionRecords(string variant)
    {
        var clock = new Clock(); var storage = new MemoryBffSessionStorage(clock); var binding = Binding();
        await Assert.ThrowsAsync<ArgumentException>(() => storage.CreateAsync(variant == "invalid-binding" ? "../x" : binding,
            variant == "no-subject" ? "\n" : variant == "invalid-unicode" ? "\ud800" : "subject", new byte[variant == "too-large" ? BffSessionValidation.MaximumTicketBytes + 1 : 1],
            clock.Now.AddHours(variant == "too-long" ? 9 : 1), default));
        Assert.Null(await storage.ReadAsync(binding, default));
    }
    [Fact]
    public async Task MemoryPayloadsAreCopiedAndTheirDiagnosticTextDoesNotDumpTicketContents()
    {
        var clock = new Clock(); var storage = new MemoryBffSessionStorage(clock); var binding = Binding(); var bytes = new byte[] { 7, 8, 9 };
        Assert.True(await storage.CreateAsync(binding, "subject", bytes, clock.Now.AddMinutes(10), default)); bytes[0] = 0;
        var one = (await storage.ReadAsync(binding, default))!;
        var copy = one.ProtectedTicket.ToArray(); copy[0] = 1;
        Assert.Equal(7, (await storage.ReadAsync(binding, default))!.ProtectedTicket.Span[0]); Assert.Equal(nameof(BffSessionSnapshot), one.ToString());
    }

    private sealed class Interleaved(IBffSessionStorage inner) : IBffSessionStorage
    {
        public Func<Task>? AfterRead { get; set; }
        public async Task<BffSessionSnapshot?> ReadAsync(string binding, CancellationToken cancellationToken)
        {
            var result = await inner.ReadAsync(binding, cancellationToken); var action = AfterRead; AfterRead = null;
            if (action is not null) await action(); return result;
        }
        public Task<bool> IsActiveAsync(string subject, string loginBinding, CancellationToken cancellationToken) => inner.IsActiveAsync(subject, loginBinding, cancellationToken);
        public Task<bool> CreateAsync(string binding, string subject, ReadOnlyMemory<byte> protectedTicket, DateTimeOffset expiresAt, CancellationToken cancellationToken) => inner.CreateAsync(binding, subject, protectedTicket, expiresAt, cancellationToken);
        public Task<bool> RenewAsync(string binding, string subject, ReadOnlyMemory<byte> protectedTicket, DateTimeOffset expiresAt, CancellationToken cancellationToken) => inner.RenewAsync(binding, subject, protectedTicket, expiresAt, cancellationToken);
        public Task RemoveAsync(string binding, long? expectedRevision, CancellationToken cancellationToken) => inner.RemoveAsync(binding, expectedRevision, cancellationToken);
    }
}
