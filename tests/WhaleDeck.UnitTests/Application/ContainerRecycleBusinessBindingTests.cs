using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Infrastructure.Agent;
using WhaleDeck.UnitTests.Agent;

namespace WhaleDeck.UnitTests.Application;

public sealed class ContainerRecycleBusinessBindingTests
{
    [Fact]
    public void RequestPlanExecutionAndConfirmationUseIndependentDomainsAndRoundTripExactValues()
    {
        using var f = new Fixture(); var plan = ContainerRecycleWorkflowFixture.Plan(); var request = f.Request(plan.Command);
        var planned = new ContainerRecyclePlanBinding(request, plan, f.Now.AddMinutes(5));
        var hash = f.Protector.FingerprintRecyclePlan(planned);
        var execution = new ContainerRecycleExecutionBinding(request.Id, request.Actor, request.LoginBinding, hash,
            ContainerRecycleWorkflowValidation.ExecutionId(request.Id), f.Now, f.Now.AddMinutes(15));
        var claims = new ContainerRecycleConfirmation(request.Id, request.Actor, request.LoginBinding, hash, planned.ReadyExpiresAtUtc);
        var values = new[] { f.Protector.FingerprintRecycleRequest(request), hash, f.Protector.FingerprintRecycleExecution(execution) };
        Assert.Equal(3, values.Distinct().Count());
        Assert.Equal(claims, f.Protector.VerifyRecycleConfirmation(f.Protector.SignRecycleConfirmation(claims)));
        var json = ContainerRecycleWorkflowValidation.Serialize(planned);
        var restored = JsonSerializer.Deserialize<ContainerRecyclePlanBinding>(json, ContainerRecycleWorkflowValidation.Json)!;
        Assert.Equal(json, ContainerRecycleWorkflowValidation.Serialize(restored)); Assert.Equal(ulong.MaxValue, restored.Plan.SourceIdentity.Device);
        Assert.Equal(1700000000987654321, restored.Plan.SourceChangedAtUnixNs);
        Assert.Equal(hash, f.Protector.FingerprintRecyclePlan(restored));
    }
    [Theory][InlineData("id")][InlineData("key")][InlineData("actor")][InlineData("login")][InlineData("trace")]
    [InlineData("owner")][InlineData("created")][InlineData("expiry")][InlineData("command")]
    public void RequestCannotChangeIdentityLoginTraceOwnerOrLifetime(string change)
    {
        using var f = new Fixture(); var original = f.Request();
        var changed = change switch {
            "id" => original with { Id = Guid.NewGuid() }, "key" => original with { IdempotencyKey = Guid.NewGuid() },
            "actor" => original with { Actor = "another" }, "login" => original with { LoginBinding = new('F', 64) },
            "trace" => original with { TraceId = "different-trace" }, "owner" => original with { Owner = new('E', 64) },
            "created" => original with { CreatedAtUtc = original.CreatedAtUtc.AddTicks(1) },
            "expiry" => original with { PlanningExpiresAtUtc = original.PlanningExpiresAtUtc.AddSeconds(1) },
            _ => original with { Command = original.Command with { ExpectedVersion = new('9',64) } } };
        if (change is "trace" or "command") Assert.NotEqual(f.Protector.FingerprintRecycleRequest(original), f.Protector.FingerprintRecycleRequest(changed));
        else Assert.Throws<ArgumentException>(() => f.Protector.FingerprintRecycleRequest(changed));
    }
    [Theory][InlineData("different-domain")][InlineData("duplicate")][InlineData("extra")][InlineData("whitespace")]
    [InlineData("camelCase")][InlineData("zero-id")][InlineData("bad-login")][InlineData("no-expiry")]
    public void EvenCorrectlyMaccedMalformedOrCrossDomainConfirmationIsRejected(string change)
    {
        using var f = new Fixture(); var request = f.Request();
        var claims = new ContainerRecycleConfirmation(request.Id, request.Actor, request.LoginBinding, new('B', 64), f.Now.AddMinutes(5));
        var raw = ContainerRecycleWorkflowValidation.Serialize(claims);
        raw = change switch {
            "duplicate" => raw.Replace("{", "{\"Id\":\"" + request.Id + "\",", StringComparison.Ordinal),
            "extra" => raw[..^1] + ",\"Command\":\"copy\"}", "whitespace" => " " + raw,
            "camelCase" => raw.Replace("\"Actor\"", "\"actor\"", StringComparison.Ordinal),
            "zero-id" => ContainerRecycleWorkflowValidation.Serialize(claims with { Id = Guid.Empty }),
            "bad-login" => ContainerRecycleWorkflowValidation.Serialize(claims with { LoginBinding = "" }),
            "no-expiry" => raw.Replace("\"ExpiresAtUtc\":\"2026-10-28T01:07:03+00:00\"", "\"ExpiresAtUtc\":null", StringComparison.Ordinal),
            _ => raw };
        // Use the known ephemeral test key to exercise parsing independently of MAC rejection.
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
        var domain = change == "different-domain" ? "whaledeck-container-directory-business-plan-v1" : "whaledeck-container-recycle-business-confirmation-v1";
        var mac = Convert.ToHexString(HMACSHA256.HashData(f.Key, Encoding.UTF8.GetBytes(domain + "\n" + payload)));
        Assert.Throws<ArgumentException>(() => f.Protector.VerifyRecycleConfirmation(payload + "." + mac));
    }
    [Theory][InlineData("id")][InlineData("actor")][InlineData("login")][InlineData("hash")][InlineData("job")]
    [InlineData("confirmed")][InlineData("expiry")]
    public void ConfirmedExecutionHasItsOwnBoundIdentityAndDeadline(string change)
    {
        using var f = new Fixture(); var r = f.Request(); var id = ContainerRecycleWorkflowValidation.ExecutionId(r.Id);
        var original = new ContainerRecycleExecutionBinding(r.Id, r.Actor, r.LoginBinding, new('B', 64), id, f.Now, f.Now.AddMinutes(15));
        var changed = change switch {
            "id" => original with { Id = Guid.NewGuid() }, "actor" => original with { Actor = "another-admin" },
            "login" => original with { LoginBinding = new('E', 64) }, "hash" => original with { PlanHash = new('F', 64) },
            "job" => original with { ExecutionJobId = Guid.NewGuid() },
            "confirmed" => original with { ConfirmedAtUtc = f.Now.AddTicks(1) }, _ => original with { ExecutionExpiresAtUtc = f.Now.AddHours(1) } };
        if (change is "actor" or "login" or "hash") Assert.NotEqual(f.Protector.FingerprintRecycleExecution(original), f.Protector.FingerprintRecycleExecution(changed));
        else Assert.Throws<ArgumentException>(() => f.Protector.FingerprintRecycleExecution(changed));
    }
    [Theory][InlineData("")][InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("A26533E0-029E-48D8-88B8-8273058B370D")][InlineData("a26533e0029e48d888b88273058b370d")]
    public void IdempotencyKeyRequiresCanonicalNonemptyGuid(string value) => Assert.Throws<ArgumentException>(() => ContainerRecycleWorkflowValidation.Key(value));
    [Fact]
    public void DomainsSeparateLoginOwnersRequestIdsAndExecutionIds()
    {
        using var f = new Fixture(); var r = f.Request();
        var id = ContainerRecycleWorkflowValidation.IntentId(r.Actor, r.LoginBinding, r.IdempotencyKey);
        Assert.NotEqual(id, ContainerRecycleWorkflowValidation.ExecutionId(id));
        Assert.NotEqual(id, ContainerRecycleWorkflowValidation.IntentId(r.Actor, new('F', 64), r.IdempotencyKey));
        Assert.NotEqual(id, ContainerRecycleWorkflowValidation.IntentId("another", r.LoginBinding, r.IdempotencyKey));
        Assert.NotEqual(r.Owner, ContainerDirectoryWorkflowValidation.Owner(r.Actor, r.LoginBinding));
        Assert.NotEqual(r.Owner, ContainerRecycleWorkflowValidation.Owner(r.Actor, new('F', 64)));
        Assert.DoesNotContain(r.Actor, r.ToString()); Assert.DoesNotContain(r.Command.Location.Path, r.ToString());
    }
    [Fact]
    public void RecycleBusinessAuthorityCannotBeReusedAsOrdinaryChmodAuthority()
    {
        using var f = new Fixture(); var r = f.Request();
        Assert.NotEqual(r.Id, ContainerPermissionWorkflowValidation.IntentId(r.Actor, r.LoginBinding, r.IdempotencyKey));
        Assert.NotEqual(r.Owner, ContainerPermissionWorkflowValidation.Owner(r.Actor, r.LoginBinding));
        var own = new ContainerRecycleConfirmation(r.Id, r.Actor, r.LoginBinding, new('B', 64), f.Now.AddMinutes(5));
        var mode = new ContainerPermissionConfirmation(r.Id, r.Actor, r.LoginBinding, new('B', 64), f.Now.AddMinutes(5));
        Assert.Throws<ArgumentException>(() => f.Protector.VerifyPermissionConfirmation(f.Protector.SignRecycleConfirmation(own)));
        Assert.Throws<ArgumentException>(() => f.Protector.VerifyRecycleConfirmation(f.Protector.SignPermissionConfirmation(mode)));
    }
    private sealed class Fixture : IDisposable
    {
        private readonly string _path = Path.GetTempFileName(); public byte[] Key { get; } = RandomNumberGenerator.GetBytes(32);
        public DateTimeOffset Now { get; } = new(2026, 10, 28, 1, 2, 3, TimeSpan.Zero); public FilePlanProtector Protector { get; }
        public Fixture() { File.WriteAllBytes(_path, Key); Protector = new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:CapabilityKeyFile"] = _path }).Build()); }
        public ContainerRecycleRequestBinding Request(ContainerRecycleCommandDto? command = null)
        {
            const string actor = "synthetic-copy-admin"; string login = new('A', 64); var key = Guid.NewGuid();
            return new(ContainerRecycleWorkflowValidation.IntentId(actor, login, key), key, actor, login, "synthetic-trace",
                ContainerRecycleWorkflowValidation.Owner(actor, login), command ?? ContainerRecycleWorkflowFixture.Plan().Command, Now, Now.AddMinutes(15));
        }
        public void Dispose() { CryptographicOperations.ZeroMemory(Key); File.Delete(_path); }
    }
}
