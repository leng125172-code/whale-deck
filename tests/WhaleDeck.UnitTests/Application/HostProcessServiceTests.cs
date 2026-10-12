using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.UnitTests.Application;

public sealed class HostProcessServiceTests
{
    [Theory]
    [InlineData(0, 50, "cpu")]
    [InlineData(1, 501, "cpu")]
    [InlineData(10001, 50, "cpu")]
    [InlineData(1, 50, "arguments")]
    public async Task InvalidInputsNeverReachAgent(int page, int size, string sort)
    {
        var gateway = new FakeGateway();
        await Assert.ThrowsAsync<ArgumentException>(() => new HostProcessService(gateway).ListAsync(new(page, size, sort), default));
        Assert.Equal(0, gateway.Calls);
    }

    [Fact]
    public async Task FiltersAreTrimmedAndControlCharactersRejected()
    {
        var gateway = new FakeGateway();
        var service = new HostProcessService(gateway);
        await service.ListAsync(new(Filter: "  postgres  "), default);
        Assert.Equal("postgres", gateway.Query?.Filter);
        await Assert.ThrowsAsync<ArgumentException>(() => service.ListAsync(new(Filter: "bad\nfilter"), default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ListAsync(new(Filter: new string('x', 129)), default));
        Assert.Equal(1, gateway.Calls);
    }

    [Theory]
    [InlineData(-1, "-1:42")]
    [InlineData(12, "13:42")]
    [InlineData(12, "12:../../secret")]
    [InlineData(12, "")]
    [InlineData(12, "12:+42")]
    [InlineData(12, "12: 42")]
    [InlineData(12, "12:42\n")]
    public async Task InvalidIdentityCannotSelectArbitraryProcessOrPath(int pid, string identity)
    {
        var gateway = new FakeGateway();
        await Assert.ThrowsAsync<ArgumentException>(() => new HostProcessService(gateway).GetAsync(pid, identity, default));
        Assert.Equal(0, gateway.Calls);
    }

    private sealed class FakeGateway : IHostProcessGateway
    {
        public int Calls { get; private set; }
        public HostProcessQuery? Query { get; private set; }
        public Task<HostProcessPageDto> ListProcessesAsync(HostProcessQuery query, CancellationToken cancellationToken)
        {
            Calls++; Query = query;
            return Task.FromResult(new HostProcessPageDto([], 0, query.Page, query.PageSize, DateTimeOffset.UtcNow, 6, false, false, 0));
        }
        public Task<HostProcessDto> GetProcessAsync(int pid, string instanceId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
