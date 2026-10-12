using System.Reflection;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Infrastructure.Secrets;

namespace WhaleDeck.UnitTests.Application;

public sealed class ApplicationConfigurationServiceTests
{
    [Fact]
    public async Task PreflightReadsOnlyTheActorsTicketAndDoesNotConsumeIt()
    {
        var store = new MemoryOneTimeSecretStore();
        var ticket = await store.StoreAsync("owner", "{\"environment\":{\"set\":{\"KEY\":\"private\"}}}", default);
        var gateway = DispatchProxy.Create<IAgentGateway, Gateway>();
        var service = new ApplicationConfigurationService(gateway, store);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PlanAsync("other", "application:demo", new string('a', 64), ticket.Token, default));
        var plan = await service.PlanAsync("owner", "application:demo", new string('a', 64), ticket.Token, default);
        Assert.Equal("public-plan", plan.PlanHash);
        Assert.NotNull(await store.ConsumeAsync("owner", ticket.Token, default));
        Assert.Equal("demo", ((Gateway)(object)gateway).Parameters!["slug"]);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("malformed-private-data")]
    public async Task InvalidCandidateIsRejectedWithoutEchoingItsValue(string input)
    {
        var store = new MemoryOneTimeSecretStore();
        var ticket = await store.StoreAsync("owner", input, default);
        var error = await Assert.ThrowsAsync<ArgumentException>(() => new ApplicationConfigurationService(null!, store)
            .PlanAsync("owner", "application:demo", new string('a', 64), ticket.Token, default));
        Assert.DoesNotContain(input, error.Message);
    }

    public class Gateway : DispatchProxy
    {
        public IReadOnlyDictionary<string, string>? Parameters { get; private set; }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Assert.Equal(nameof(IAgentGateway.PlanAsync), targetMethod?.Name);
            Parameters = (IReadOnlyDictionary<string, string>)args![3]!;
            Assert.Equal("environment", Parameters["inputKind"]);
            return Task.FromResult(new PlanDto("public-plan", DateTimeOffset.UtcNow.AddMinutes(5), ["redacted"], []));
        }
    }
}
