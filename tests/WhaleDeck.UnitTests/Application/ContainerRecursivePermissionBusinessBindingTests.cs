using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Infrastructure.Agent;
using WhaleDeck.UnitTests.Agent;

namespace WhaleDeck.UnitTests.Application;

public sealed class ContainerRecursivePermissionBusinessBindingTests
{
    [Theory][InlineData(false)][InlineData(true)]
    public void RequestPlanExecutionAndConfirmationUseIndependentDomainsAndRoundTripExactValues(bool directory)
    {
        using var f = new Fixture(); var plan = ContainerRecursivePermissionWorkflowFixture.Plan(directory); var request = f.Request(plan.Command);
        var planned = new ContainerRecursivePermissionPlanBinding(request, plan, f.Now.AddMinutes(5));
        var hash = f.Protector.FingerprintRecursivePermissionPlan(planned);
        var execution = new ContainerRecursivePermissionExecutionBinding(request.Id, request.Actor, request.LoginBinding, hash,
            ContainerRecursivePermissionWorkflowValidation.ExecutionId(request.Id), f.Now, f.Now.AddMinutes(15));
        var claims = new ContainerRecursivePermissionConfirmation(request.Id, request.Actor, request.LoginBinding, hash, planned.ReadyExpiresAtUtc);
        var values = new[] { f.Protector.FingerprintRecursivePermissionRequest(request), hash, f.Protector.FingerprintRecursivePermissionExecution(execution) };
        Assert.Equal(3, values.Distinct().Count());
        Assert.Equal(claims, f.Protector.VerifyRecursivePermissionConfirmation(f.Protector.SignRecursivePermissionConfirmation(claims)));
        var json = ContainerRecursivePermissionWorkflowValidation.Serialize(planned);
        var restored = JsonSerializer.Deserialize<ContainerRecursivePermissionPlanBinding>(json, ContainerRecursivePermissionWorkflowValidation.Json)!;
        Assert.Equal(planned, restored); Assert.Equal(ulong.MaxValue, restored.Plan.RootIdentity.Device);
        Assert.Equal(1700000000987654321, restored.Plan.RootChangedAtUnixNs);
        Assert.Equal(hash, f.Protector.FingerprintRecursivePermissionPlan(restored));
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
            _ => original with { Command = original.Command with { FileMode = "0777" } } };
        if (change is "trace" or "command") Assert.NotEqual(f.Protector.FingerprintRecursivePermissionRequest(original), f.Protector.FingerprintRecursivePermissionRequest(changed));
        else Assert.Throws<ArgumentException>(() => f.Protector.FingerprintRecursivePermissionRequest(changed));
    }
    [Theory][InlineData("different-domain")][InlineData("duplicate")][InlineData("extra")][InlineData("whitespace")]
    [InlineData("camelCase")][InlineData("zero-id")][InlineData("bad-login")][InlineData("no-expiry")]
    public void EvenCorrectlyMaccedMalformedOrCrossDomainConfirmationIsRejected(string change)
    {
        using var f = new Fixture(); var request = f.Request();
        var claims = new ContainerRecursivePermissionConfirmation(request.Id, request.Actor, request.LoginBinding, new('B', 64), f.Now.AddMinutes(5));
        var raw = ContainerRecursivePermissionWorkflowValidation.Serialize(claims);
        raw = change switch {
            "duplicate" => raw.Replace("{", "{\"Id\":\"" + request.Id + "\",", StringComparison.Ordinal),
            "extra" => raw[..^1] + ",\"Command\":\"copy\"}", "whitespace" => " " + raw,
            "camelCase" => raw.Replace("\"Actor\"", "\"actor\"", StringComparison.Ordinal),
            "zero-id" => ContainerRecursivePermissionWorkflowValidation.Serialize(claims with { Id = Guid.Empty }),
            "bad-login" => ContainerRecursivePermissionWorkflowValidation.Serialize(claims with { LoginBinding = "" }),
            "no-expiry" => raw.Replace("\"ExpiresAtUtc\":\"2026-10-28T01:07:03+00:00\"", "\"ExpiresAtUtc\":null", StringComparison.Ordinal),
            _ => raw };
        // Use the known ephemeral test key to exercise parsing independently of MAC rejection.
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
        var domain = change == "different-domain" ? "whaledeck-container-directory-business-plan-v1" : "whaledeck-container-recursive-permission-business-confirmation-v1";
        var mac = Convert.ToHexString(HMACSHA256.HashData(f.Key, Encoding.UTF8.GetBytes(domain + "\n" + payload)));
        Assert.Throws<ArgumentException>(() => f.Protector.VerifyRecursivePermissionConfirmation(payload + "." + mac));
    }
    [Theory][InlineData("id")][InlineData("actor")][InlineData("login")][InlineData("hash")][InlineData("job")]
    [InlineData("confirmed")][InlineData("expiry")]
    public void ConfirmedExecutionHasItsOwnBoundIdentityAndDeadline(string change)
    {
        using var f = new Fixture(); var r = f.Request(); var id = ContainerRecursivePermissionWorkflowValidation.ExecutionId(r.Id);
        var original = new ContainerRecursivePermissionExecutionBinding(r.Id, r.Actor, r.LoginBinding, new('B', 64), id, f.Now, f.Now.AddMinutes(15));
        var changed = change switch {
            "id" => original with { Id = Guid.NewGuid() }, "actor" => original with { Actor = "another-admin" },
            "login" => original with { LoginBinding = new('E', 64) }, "hash" => original with { PlanHash = new('F', 64) },
            "job" => original with { ExecutionJobId = Guid.NewGuid() },
            "confirmed" => original with { ConfirmedAtUtc = f.Now.AddTicks(1) }, _ => original with { ExecutionExpiresAtUtc = f.Now.AddHours(1) } };
        if (change is "actor" or "login" or "hash") Assert.NotEqual(f.Protector.FingerprintRecursivePermissionExecution(original), f.Protector.FingerprintRecursivePermissionExecution(changed));
        else Assert.Throws<ArgumentException>(() => f.Protector.FingerprintRecursivePermissionExecution(changed));
    }
    [Theory][InlineData("")][InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("A26533E0-029E-48D8-88B8-8273058B370D")][InlineData("a26533e0029e48d888b88273058b370d")]
    public void IdempotencyKeyRequiresCanonicalNonemptyGuid(string value) => Assert.Throws<ArgumentException>(() => ContainerRecursivePermissionWorkflowValidation.Key(value));
    [Fact]
    public void DomainsSeparateLoginOwnersRequestIdsAndExecutionIds()
    {
        using var f = new Fixture(); var r = f.Request();
        var id = ContainerRecursivePermissionWorkflowValidation.IntentId(r.Actor, r.LoginBinding, r.IdempotencyKey);
        Assert.NotEqual(id, ContainerRecursivePermissionWorkflowValidation.ExecutionId(id));
        Assert.NotEqual(id, ContainerRecursivePermissionWorkflowValidation.IntentId(r.Actor, new('F', 64), r.IdempotencyKey));
        Assert.NotEqual(id, ContainerRecursivePermissionWorkflowValidation.IntentId("another", r.LoginBinding, r.IdempotencyKey));
        Assert.NotEqual(r.Owner, ContainerDirectoryWorkflowValidation.Owner(r.Actor, r.LoginBinding));
        Assert.NotEqual(r.Owner, ContainerRecursivePermissionWorkflowValidation.Owner(r.Actor, new('F', 64)));
        Assert.DoesNotContain(r.Actor, r.ToString()); Assert.DoesNotContain(r.Command.Location.Path, r.ToString());
    }
    [Fact]
    public void RecursivePermissionBusinessAuthorityCannotBeReusedAsOrdinaryChmodAuthority()
    {
        using var f = new Fixture(); var r = f.Request();
        Assert.NotEqual(r.Id, ContainerPermissionWorkflowValidation.IntentId(r.Actor, r.LoginBinding, r.IdempotencyKey));
        Assert.NotEqual(r.Owner, ContainerPermissionWorkflowValidation.Owner(r.Actor, r.LoginBinding));
        var own = new ContainerRecursivePermissionConfirmation(r.Id, r.Actor, r.LoginBinding, new('B', 64), f.Now.AddMinutes(5));
        var mode = new ContainerPermissionConfirmation(r.Id, r.Actor, r.LoginBinding, new('B', 64), f.Now.AddMinutes(5));
        Assert.Throws<ArgumentException>(() => f.Protector.VerifyPermissionConfirmation(f.Protector.SignRecursivePermissionConfirmation(own)));
        Assert.Throws<ArgumentException>(() => f.Protector.VerifyRecursivePermissionConfirmation(f.Protector.SignPermissionConfirmation(mode)));
    }
    [Theory][InlineData(true)][InlineData(false)]
    public void BothTargetModesAreBoundIntoRequestAuthority(bool file)
    {
        using var f = new Fixture(); var r = f.Request();
        var changed = r with { Command = file ? r.Command with { FileMode = "0000" } : r.Command with { DirectoryMode = "0000" } };
        Assert.NotEqual(f.Protector.FingerprintRecursivePermissionRequest(r), f.Protector.FingerprintRecursivePermissionRequest(changed));
    }
    private sealed class Fixture : IDisposable
    {
        private readonly string _path = Path.GetTempFileName(); public byte[] Key { get; } = RandomNumberGenerator.GetBytes(32);
        public DateTimeOffset Now { get; } = new(2026, 10, 28, 1, 2, 3, TimeSpan.Zero); public FilePlanProtector Protector { get; }
        public Fixture() { File.WriteAllBytes(_path, Key); Protector = new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:CapabilityKeyFile"] = _path }).Build()); }
        public ContainerRecursivePermissionRequestBinding Request(ContainerRecursivePermissionCommandDto? command = null)
        {
            const string actor = "synthetic-copy-admin"; string login = new('A', 64); var key = Guid.NewGuid();
            return new(ContainerRecursivePermissionWorkflowValidation.IntentId(actor, login, key), key, actor, login, "synthetic-trace",
                ContainerRecursivePermissionWorkflowValidation.Owner(actor, login), command ?? ContainerRecursivePermissionWorkflowFixture.Plan().Command, Now, Now.AddMinutes(15));
        }
        public void Dispose() { CryptographicOperations.ZeroMemory(Key); File.Delete(_path); }
    }
}
