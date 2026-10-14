using System.Text.Json;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.UnitTests.Application;

public sealed class NotificationManagementTests
{
    private static NotificationContext Admin => new("admin", true, "trace", "127.0.0.1", Guid.NewGuid().ToString());
    private static SaveNotificationChannelCommand Channel => new("Channel", "Webhook", "approved-target", Confirmed: true);

    [Fact]
    public async Task AllAdministrationRequiresRoleBeforeValidationOrPersistence()
    {
        var repository = new Spy(); var service = new NotificationManagementService(repository);
        var ordinary = Admin with { IsAdministrator = false }; var id = Guid.NewGuid();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ListChannelsAsync(ordinary, new(), default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ListRoutesAsync(ordinary, new(), id, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ListDeliveriesAsync(ordinary, new(), id, "invalid", default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SaveChannelAsync(ordinary, null, Channel, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SaveRouteAsync(ordinary, null, new(id, Confirmed: true), default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.DeleteChannelAsync(ordinary, id, new(0, true), default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.DeleteRouteAsync(ordinary, id, new(0, true), default));
        Assert.Equal(0, repository.Calls);
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("not-a-uuid")] [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task WritesRequireAnIdempotencyKey(string? key)
    {
        var repository = new Spy(); var service = new NotificationManagementService(repository); var context = Admin with { IdempotencyKey = key };
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveChannelAsync(context, null, Channel, default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveRouteAsync(context, null, new(Guid.NewGuid(), Confirmed: true), default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.DeleteChannelAsync(context, Guid.NewGuid(), new(0, true), default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.DeleteRouteAsync(context, Guid.NewGuid(), new(0, true), default));
        Assert.Equal(0, repository.Calls);
    }

    [Fact]
    public async Task ConfirmationVersionsAndCredentialIntentAreValidated()
    {
        var repository = new Spy(); var service = new NotificationManagementService(repository); var id = Guid.NewGuid();
        foreach (var command in new[] { Channel with { Confirmed = false }, Channel with { ExpectedVersion = 0 }, Channel with { KeepCredential = true } })
            await Assert.ThrowsAsync<ArgumentException>(() => service.SaveChannelAsync(Admin, null, command, default));
        foreach (var command in new[] { Channel, Channel with { ExpectedVersion = -1 }, Channel with { ExpectedVersion = long.MaxValue },
                     Channel with { ExpectedVersion = 0, KeepCredential = true, CredentialReference = "private-reference" } })
            await Assert.ThrowsAsync<ArgumentException>(() => service.SaveChannelAsync(Admin, id, command, default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveChannelAsync(Admin, Guid.Empty, Channel, default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.DeleteChannelAsync(Admin, id, new(0), default));
        Assert.Equal(0, repository.Calls);
        await service.SaveChannelAsync(Admin, id, Channel with { Name = "  Channel  ", ExpectedVersion = 0, KeepCredential = true }, default);
        Assert.Equal("Channel", repository.Command!.Name);
        Assert.True(repository.Command.KeepCredential);
    }

    [Theory]
    [InlineData("Email")] [InlineData("Webhook")] [InlineData("DingTalk")] [InlineData("WeCom")] [InlineData("Feishu")]
    public async Task SupportedProvidersAcceptBoundedReferences(string provider)
    {
        var repository = new Spy();
        await new NotificationManagementService(repository).SaveChannelAsync(Admin, null,
            Channel with { Provider = provider, DestinationReference = new string('a', 64), CredentialReference = "private-reference" }, default);
        Assert.Equal(1, repository.Calls);
    }

    [Theory]
    [InlineData("approved-target\n")] [InlineData("approved-target\r")] [InlineData("https://example.test")]
    [InlineData("../secret")] [InlineData("UPPER")] [InlineData("")]
    public async Task InvalidReferenceNeverReachesPersistence(string reference)
    {
        var repository = new Spy(); var service = new NotificationManagementService(repository);
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveChannelAsync(Admin, null, Channel with { DestinationReference = reference }, default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveChannelAsync(Admin, null, Channel with { CredentialReference = reference }, default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveChannelAsync(Admin, null, Channel with { DestinationReference = new string('a', 65) }, default));
        Assert.Equal(0, repository.Calls);
    }

    [Theory]
    [InlineData(-1, 1)] [InlineData(101, 100)] [InlineData(25, 25)]
    public async Task PageBudgetsAreClamped(int take, int expected)
    {
        var repository = new Spy(); var service = new NotificationManagementService(repository);
        await service.ListChannelsAsync(Admin, new(Take: take), default);
        Assert.Equal(expected, repository.Query!.Take);
        await service.ListRoutesAsync(Admin, new(Take: take), null, default);
        Assert.Equal(expected, repository.Query!.Take);
        await service.ListDeliveriesAsync(Admin, new(Take: take), null, null, default);
        Assert.Equal(expected, repository.Query!.Take);
    }

    [Fact]
    public async Task InvalidFiltersAndOverflowAreRejected()
    {
        var repository = new Spy(); var service = new NotificationManagementService(repository);
        await Assert.ThrowsAsync<ArgumentException>(() => service.ListChannelsAsync(Admin, new(Page: 0), default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ListChannelsAsync(Admin, new(Page: int.MaxValue), default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ListRoutesAsync(Admin, new(), Guid.Empty, default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ListDeliveriesAsync(Admin, new(), null, "arbitrary", default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveRouteAsync(Admin, null, new(Guid.Empty, Confirmed: true), default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveRouteAsync(Admin, null, new(Guid.NewGuid(), SourceKind: "Personal", Confirmed: true), default));
        Assert.Equal(0, repository.Calls);
    }

    [Fact]
    public void PublicProjectionsDoNotExposeCredentialsOrLeases()
    {
        var names = typeof(NotificationChannelDto).GetProperties().Select(item => item.Name).ToArray();
        Assert.Contains("HasCredential", names); Assert.DoesNotContain("CredentialReference", names);
        var serialized = JsonSerializer.Serialize(new NotificationDeliveryDto(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0, "Webhook", "Pending", 0,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null, null));
        Assert.DoesNotContain("Reference", serialized); Assert.DoesNotContain("Token", serialized); Assert.DoesNotContain("Message", serialized);
    }

    private sealed class Spy : INotificationManagementRepository
    {
        public int Calls { get; private set; }
        public NotificationManagementQuery? Query { get; private set; }
        public SaveNotificationChannelCommand? Command { get; private set; }
        private Task<NotificationManagementPage<T>> List<T>(NotificationManagementQuery query) { Calls++; Query = query; return Task.FromResult(new NotificationManagementPage<T>([], false, query.Page, query.Take)); }
        public Task<NotificationManagementPage<NotificationChannelDto>> ListChannelsAsync(NotificationManagementQuery query, CancellationToken cancellationToken) => List<NotificationChannelDto>(query);
        public Task<NotificationManagementPage<NotificationRouteDto>> ListRoutesAsync(NotificationManagementQuery query, Guid? channelId, CancellationToken cancellationToken) => List<NotificationRouteDto>(query);
        public Task<NotificationManagementPage<NotificationDeliveryDto>> ListDeliveriesAsync(NotificationManagementQuery query, Guid? channelId, string? state, CancellationToken cancellationToken) => List<NotificationDeliveryDto>(query);
        public Task<NotificationChannelDto> SaveChannelAsync(NotificationContext context, Guid? id, SaveNotificationChannelCommand command, CancellationToken cancellationToken)
        { Calls++; Command = command; return Task.FromResult(new NotificationChannelDto(id ?? Guid.NewGuid(), command.Name, command.Provider, command.DestinationReference, command.CredentialReference is not null, command.IsEnabled, 0, DateTimeOffset.UtcNow)); }
        public Task<NotificationRouteDto> SaveRouteAsync(NotificationContext context, Guid? id, SaveNotificationRouteCommand command, CancellationToken cancellationToken)
        { Calls++; return Task.FromResult(new NotificationRouteDto(id ?? Guid.NewGuid(), command.ChannelId, command.SourceKind, command.MinimumSeverity, command.IncludeRecovery, command.IsEnabled, 0, DateTimeOffset.UtcNow)); }
        public Task DeleteChannelAsync(NotificationContext context, Guid id, long expectedVersion, CancellationToken cancellationToken) { Calls++; return Task.CompletedTask; }
        public Task DeleteRouteAsync(NotificationContext context, Guid id, long expectedVersion, CancellationToken cancellationToken) { Calls++; return Task.CompletedTask; }
    }
}
