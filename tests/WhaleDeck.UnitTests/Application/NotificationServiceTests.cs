using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.UnitTests.Application;

public sealed class NotificationServiceTests
{
    private static readonly NotificationContext Valid = new("actor", false, "trace");

    [Theory]
    [InlineData("invalid", "all", null)]
    [InlineData("all", "unknown", null)]
    [InlineData("all", "all", 0L)]
    [InlineData("all", "all", -1L)]
    public async Task InvalidFilterNeverTouchesPersistence(string status, string kind, long? before)
    {
        var repository = new SpyRepository();
        await Assert.ThrowsAsync<ArgumentException>(() => new NotificationService(repository).ListAsync(Valid, new(Status: status, Kind: kind, Before: before), default));
        Assert.Equal(0, repository.Calls);
    }

    [Theory]
    [InlineData("", "trace")]
    [InlineData("actor\n", "trace")]
    [InlineData("actor", "")]
    [InlineData("actor", "trace\r")]
    public async Task InvalidActorOrTraceNeverTouchesPersistence(string subject, string trace)
    {
        var repository = new SpyRepository();
        await Assert.ThrowsAsync<ArgumentException>(() => new NotificationService(repository).SummaryAsync(new(subject, false, trace), default));
        Assert.Equal(0, repository.Calls);
    }

    [Theory]
    [InlineData(-100, 1)]
    [InlineData(1000, 100)]
    public async Task PageBudgetIsBounded(int requested, int expected)
    {
        var repository = new SpyRepository();
        await new NotificationService(repository).ListAsync(Valid, new(Take: requested), default);
        Assert.Equal(expected, repository.Query!.Take);
    }

    [Fact]
    public async Task InvalidWriteTargetsNeverTouchPersistence()
    {
        var repository = new SpyRepository();
        var service = new NotificationService(repository);
        await Assert.ThrowsAsync<ArgumentException>(() => service.SetReadAsync(Valid, Guid.Empty, true, default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.MarkAllReadAsync(Valid, Guid.Empty, default));
        Assert.Equal(0, repository.Calls);
    }

    private sealed class SpyRepository : INotificationRepository
    {
        public int Calls { get; private set; }
        public NotificationQuery? Query { get; private set; }
        public Task<NotificationPageDto> ListAsync(NotificationContext context, NotificationQuery query, CancellationToken cancellationToken)
        { Calls++; Query = query; return Task.FromResult(new NotificationPageDto([], null, 0)); }
        public Task<NotificationSummaryDto> SummaryAsync(NotificationContext context, CancellationToken cancellationToken)
        { Calls++; return Task.FromResult(new NotificationSummaryDto(0, 0, 0)); }
        public Task<NotificationReadDto> SetReadAsync(NotificationContext context, Guid id, bool isRead, CancellationToken cancellationToken)
        { Calls++; return Task.FromResult(new NotificationReadDto(id, isRead, null)); }
        public Task<PrepareNotificationsReadDto> PrepareReadAllAsync(NotificationContext context, CancellationToken cancellationToken)
        { Calls++; return Task.FromResult(new PrepareNotificationsReadDto(Guid.NewGuid(), 0, DateTimeOffset.UtcNow.AddMinutes(5))); }
        public Task<MarkNotificationsReadDto> MarkAllReadAsync(NotificationContext context, Guid snapshotId, CancellationToken cancellationToken)
        { Calls++; return Task.FromResult(new MarkNotificationsReadDto(0)); }
    }
}
