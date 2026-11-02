using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Infrastructure.Agent;
using WhaleDeck.UnitTests.Agent;

namespace WhaleDeck.UnitTests.Application;

public sealed class ContainerRecursiveOwnershipBusinessBindingTests
{
    [Theory][InlineData(false)][InlineData(true)]
    public void RequestPlanExecutionAndConfirmationUseIndependentDomainsAndRoundTripExactValues(bool directory)
    {
        using var f = new Fixture(); var plan = ContainerRecursiveOwnershipWorkflowFixture.Plan(directory); var request = f.Request(plan.Command);
        var planned = new ContainerRecursiveOwnershipPlanBinding(request, plan, f.Now.AddMinutes(5));
        var hash = f.Protector.FingerprintRecursiveOwnershipPlan(planned);
        var execution = new ContainerRecursiveOwnershipExecutionBinding(request.Id, request.Actor, request.LoginBinding, hash,
            ContainerRecursiveOwnershipWorkflowValidation.ExecutionId(request.Id), f.Now, f.Now.AddMinutes(15));
        var claims = new ContainerRecursiveOwnershipConfirmation(request.Id, request.Actor, request.LoginBinding, hash, planned.ReadyExpiresAtUtc);
        var values = new[] { f.Protector.FingerprintRecursiveOwnershipRequest(request), hash, f.Protector.FingerprintRecursiveOwnershipExecution(execution) };
        Assert.Equal(3, values.Distinct().Count());
        Assert.Equal(claims, f.Protector.VerifyRecursiveOwnershipConfirmation(f.Protector.SignRecursiveOwnershipConfirmation(claims)));
        var json = ContainerRecursiveOwnershipWorkflowValidation.Serialize(planned);
        var restored = JsonSerializer.Deserialize<ContainerRecursiveOwnershipPlanBinding>(json, ContainerRecursiveOwnershipWorkflowValidation.Json)!;
        Assert.Equal(planned, restored); Assert.Equal(ulong.MaxValue, restored.Plan.RootIdentity.Device);
        Assert.Equal(1700000000987654321, restored.Plan.RootChangedAtUnixNs);
        Assert.Equal(hash, f.Protector.FingerprintRecursiveOwnershipPlan(restored));
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
            _ => original with { Command = original.Command with { Uid = 2300 } } };
        if (change is "trace" or "command") Assert.NotEqual(f.Protector.FingerprintRecursiveOwnershipRequest(original), f.Protector.FingerprintRecursiveOwnershipRequest(changed));
        else Assert.Throws<ArgumentException>(() => f.Protector.FingerprintRecursiveOwnershipRequest(changed));
    }
    [Theory][InlineData("different-domain")][InlineData("duplicate")][InlineData("extra")][InlineData("whitespace")]
    [InlineData("camelCase")][InlineData("zero-id")][InlineData("bad-login")][InlineData("no-expiry")]
    public void EvenCorrectlyMaccedMalformedOrCrossDomainConfirmationIsRejected(string change)
    {
        using var f = new Fixture(); var request = f.Request();
        var claims = new ContainerRecursiveOwnershipConfirmation(request.Id, request.Actor, request.LoginBinding, new('B', 64), f.Now.AddMinutes(5));
        var raw = ContainerRecursiveOwnershipWorkflowValidation.Serialize(claims);
        raw = change switch {
            "duplicate" => raw.Replace("{", "{\"Id\":\"" + request.Id + "\",", StringComparison.Ordinal),
            "extra" => raw[..^1] + ",\"Command\":\"copy\"}", "whitespace" => " " + raw,
            "camelCase" => raw.Replace("\"Actor\"", "\"actor\"", StringComparison.Ordinal),
            "zero-id" => ContainerRecursiveOwnershipWorkflowValidation.Serialize(claims with { Id = Guid.Empty }),
            "bad-login" => ContainerRecursiveOwnershipWorkflowValidation.Serialize(claims with { LoginBinding = "" }),
            "no-expiry" => raw.Replace("\"ExpiresAtUtc\":\"2026-10-28T01:07:03+00:00\"", "\"ExpiresAtUtc\":null", StringComparison.Ordinal),
            _ => raw };
        // Use the known ephemeral test key to exercise parsing independently of MAC rejection.
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
        var domain = change == "different-domain" ? "whaledeck-container-directory-business-plan-v1" : "whaledeck-container-recursive-ownership-business-confirmation-v1";
        var mac = Convert.ToHexString(HMACSHA256.HashData(f.Key, Encoding.UTF8.GetBytes(domain + "\n" + payload)));
        Assert.Throws<ArgumentException>(() => f.Protector.VerifyRecursiveOwnershipConfirmation(payload + "." + mac));
    }
    [Theory][InlineData("id")][InlineData("actor")][InlineData("login")][InlineData("hash")][InlineData("job")]
    [InlineData("confirmed")][InlineData("expiry")]
    public void ConfirmedExecutionHasItsOwnBoundIdentityAndDeadline(string change)
    {
        using var f = new Fixture(); var r = f.Request(); var id = ContainerRecursiveOwnershipWorkflowValidation.ExecutionId(r.Id);
        var original = new ContainerRecursiveOwnershipExecutionBinding(r.Id, r.Actor, r.LoginBinding, new('B', 64), id, f.Now, f.Now.AddMinutes(15));
        var changed = change switch {
            "id" => original with { Id = Guid.NewGuid() }, "actor" => original with { Actor = "another-admin" },
            "login" => original with { LoginBinding = new('E', 64) }, "hash" => original with { PlanHash = new('F', 64) },
            "job" => original with { ExecutionJobId = Guid.NewGuid() },
            "confirmed" => original with { ConfirmedAtUtc = f.Now.AddTicks(1) }, _ => original with { ExecutionExpiresAtUtc = f.Now.AddHours(1) } };
        if (change is "actor" or "login" or "hash") Assert.NotEqual(f.Protector.FingerprintRecursiveOwnershipExecution(original), f.Protector.FingerprintRecursiveOwnershipExecution(changed));
        else Assert.Throws<ArgumentException>(() => f.Protector.FingerprintRecursiveOwnershipExecution(changed));
    }
    [Theory][InlineData("")][InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("A26533E0-029E-48D8-88B8-8273058B370D")][InlineData("a26533e0029e48d888b88273058b370d")]
    public void IdempotencyKeyRequiresCanonicalNonemptyGuid(string value) => Assert.Throws<ArgumentException>(() => ContainerRecursiveOwnershipWorkflowValidation.Key(value));
    [Fact]
    public void DomainsSeparateLoginOwnersRequestIdsAndExecutionIds()
    {
        using var f = new Fixture(); var r = f.Request();
        var id = ContainerRecursiveOwnershipWorkflowValidation.IntentId(r.Actor, r.LoginBinding, r.IdempotencyKey);
        Assert.NotEqual(id, ContainerRecursiveOwnershipWorkflowValidation.ExecutionId(id));
        Assert.NotEqual(id, ContainerRecursiveOwnershipWorkflowValidation.IntentId(r.Actor, new('F', 64), r.IdempotencyKey));
        Assert.NotEqual(id, ContainerRecursiveOwnershipWorkflowValidation.IntentId("another", r.LoginBinding, r.IdempotencyKey));
        Assert.NotEqual(r.Owner, ContainerDirectoryWorkflowValidation.Owner(r.Actor, r.LoginBinding));
        Assert.NotEqual(r.Owner, ContainerRecursiveOwnershipWorkflowValidation.Owner(r.Actor, new('F', 64)));
        Assert.DoesNotContain(r.Actor, r.ToString()); Assert.DoesNotContain(r.Command.Location.Path, r.ToString());
    }
    [Fact]
    public void RecursiveOwnershipBusinessAuthorityCannotBeReusedAsOrdinaryChmodAuthority()
    {
        using var f = new Fixture(); var r = f.Request();
        Assert.NotEqual(r.Id, ContainerPermissionWorkflowValidation.IntentId(r.Actor, r.LoginBinding, r.IdempotencyKey));
        Assert.NotEqual(r.Owner, ContainerPermissionWorkflowValidation.Owner(r.Actor, r.LoginBinding));
        var own = new ContainerRecursiveOwnershipConfirmation(r.Id, r.Actor, r.LoginBinding, new('B', 64), f.Now.AddMinutes(5));
        var mode = new ContainerPermissionConfirmation(r.Id, r.Actor, r.LoginBinding, new('B', 64), f.Now.AddMinutes(5));
        Assert.Throws<ArgumentException>(() => f.Protector.VerifyPermissionConfirmation(f.Protector.SignRecursiveOwnershipConfirmation(own)));
        Assert.Throws<ArgumentException>(() => f.Protector.VerifyRecursiveOwnershipConfirmation(f.Protector.SignPermissionConfirmation(mode)));
    }
    [Theory][InlineData(true)][InlineData(false)]
    public void BothRequestedOwnersAreBoundIntoRequestAuthority(bool file)
    {
        using var f = new Fixture(); var r = f.Request();
        var changed = r with { Command = file ? r.Command with { Uid = 0 } : r.Command with { Gid = 0 } };
        Assert.NotEqual(f.Protector.FingerprintRecursiveOwnershipRequest(r), f.Protector.FingerprintRecursiveOwnershipRequest(changed));
    }
    [Fact]
    public void RecursiveChownAuthorityCannotBeReusedForSingleChownOrRecursiveChmod()
    {
        using var f = new Fixture(); var r = f.Request();
        var own = new ContainerRecursiveOwnershipConfirmation(r.Id, r.Actor, r.LoginBinding, new('B',64), f.Now.AddMinutes(5));
        var single = new ContainerOwnershipConfirmation(r.Id, r.Actor, r.LoginBinding, new('B',64), f.Now.AddMinutes(5));
        var recursiveMode = new ContainerRecursivePermissionConfirmation(r.Id, r.Actor, r.LoginBinding, new('B',64), f.Now.AddMinutes(5));
        var token = f.Protector.SignRecursiveOwnershipConfirmation(own);
        Assert.Throws<ArgumentException>(() => f.Protector.VerifyOwnershipConfirmation(token));
        Assert.Throws<ArgumentException>(() => f.Protector.VerifyRecursivePermissionConfirmation(token));
        Assert.Throws<ArgumentException>(() => f.Protector.VerifyRecursiveOwnershipConfirmation(f.Protector.SignOwnershipConfirmation(single)));
        Assert.Throws<ArgumentException>(() => f.Protector.VerifyRecursiveOwnershipConfirmation(f.Protector.SignRecursivePermissionConfirmation(recursiveMode)));
        Assert.NotEqual(r.Id,ContainerOwnershipWorkflowValidation.IntentId(r.Actor,r.LoginBinding,r.IdempotencyKey));
        Assert.NotEqual(r.Id,ContainerRecursivePermissionWorkflowValidation.IntentId(r.Actor,r.LoginBinding,r.IdempotencyKey));
        Assert.NotEqual(r.Owner,ContainerOwnershipWorkflowValidation.Owner(r.Actor,r.LoginBinding));
        Assert.NotEqual(r.Owner,ContainerRecursivePermissionWorkflowValidation.Owner(r.Actor,r.LoginBinding));
    }
    [Theory][InlineData("Uid")][InlineData("Gid")]
    public void MissingJsonOwnerCannotDeserializeAsRoot(string field)
    {
        var command = ContainerRecursiveOwnershipWorkflowFixture.Plan().Command with { Uid = 0, Gid = 0 };
        var raw = ContainerRecursiveOwnershipWorkflowValidation.Serialize(command);
        var restored = JsonSerializer.Deserialize<ContainerRecursiveOwnershipCommandDto>(raw,ContainerRecursiveOwnershipWorkflowValidation.Json)!;
        Assert.Equal(command,restored);
        var node = System.Text.Json.Nodes.JsonNode.Parse(raw)!.AsObject();node.Remove(field);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ContainerRecursiveOwnershipCommandDto>(node.ToJsonString(),ContainerRecursiveOwnershipWorkflowValidation.Json));
    }
    private sealed class Fixture : IDisposable
    {
        private readonly string _path = Path.GetTempFileName(); public byte[] Key { get; } = RandomNumberGenerator.GetBytes(32);
        public DateTimeOffset Now { get; } = new(2026, 10, 28, 1, 2, 3, TimeSpan.Zero); public FilePlanProtector Protector { get; }
        public Fixture() { File.WriteAllBytes(_path, Key); Protector = new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:CapabilityKeyFile"] = _path }).Build()); }
        public ContainerRecursiveOwnershipRequestBinding Request(ContainerRecursiveOwnershipCommandDto? command = null)
        {
            const string actor = "synthetic-copy-admin"; string login = new('A', 64); var key = Guid.NewGuid();
            return new(ContainerRecursiveOwnershipWorkflowValidation.IntentId(actor, login, key), key, actor, login, "synthetic-trace",
                ContainerRecursiveOwnershipWorkflowValidation.Owner(actor, login), command ?? ContainerRecursiveOwnershipWorkflowFixture.Plan().Command, Now, Now.AddMinutes(15));
        }
        public void Dispose() { CryptographicOperations.ZeroMemory(Key); File.Delete(_path); }
    }
}
