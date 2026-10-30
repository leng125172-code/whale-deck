using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Infrastructure.Agent;
using WhaleDeck.UnitTests.Agent;

namespace WhaleDeck.UnitTests.Application;

public sealed class ContainerPermissionBindingTests
{
    [Theory][InlineData(false)][InlineData(true)]
    public void RequestPlanExecutionAndConfirmationUseIndependentDomainsAndRoundTripExactValues(bool directory)
    {
        using var f = new Fixture(); var plan = ContainerPermissionWorkflowFixture.Plan(directory); var request = f.Request(plan.Command);
        var planned = new ContainerPermissionPlanBinding(request, plan, f.Now.AddMinutes(5));
        var hash = f.Protector.FingerprintPermissionPlan(planned);
        var execution = new ContainerPermissionExecutionBinding(request.Id, request.Actor, request.LoginBinding, hash,
            ContainerPermissionWorkflowValidation.ExecutionId(request.Id), f.Now, f.Now.AddMinutes(15));
        var claims = new ContainerPermissionConfirmation(request.Id, request.Actor, request.LoginBinding, hash, planned.ReadyExpiresAtUtc);
        var values = new[] { f.Protector.FingerprintPermissionRequest(request), hash, f.Protector.FingerprintPermissionExecution(execution) };
        Assert.Equal(3, values.Distinct().Count());
        Assert.Equal(claims, f.Protector.VerifyPermissionConfirmation(f.Protector.SignPermissionConfirmation(claims)));
        var json = ContainerPermissionWorkflowValidation.Serialize(planned);
        var restored = JsonSerializer.Deserialize<ContainerPermissionPlanBinding>(json, ContainerPermissionWorkflowValidation.Json)!;
        Assert.Equal(planned, restored); Assert.Equal(ulong.MaxValue, restored.Plan.SourceIdentity.Device);
        Assert.Equal(1700000000987654321, restored.Plan.SourceChangedAtUnixNs);
        Assert.Equal(hash, f.Protector.FingerprintPermissionPlan(restored));
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
            _ => original with { Command = original.Command with { Mode = "0777" } } };
        if (change is "trace" or "command") Assert.NotEqual(f.Protector.FingerprintPermissionRequest(original), f.Protector.FingerprintPermissionRequest(changed));
        else Assert.Throws<ArgumentException>(() => f.Protector.FingerprintPermissionRequest(changed));
    }
    [Theory][InlineData("different-domain")][InlineData("duplicate")][InlineData("extra")][InlineData("whitespace")]
    [InlineData("camelCase")][InlineData("zero-id")][InlineData("bad-login")][InlineData("no-expiry")]
    public void EvenCorrectlyMaccedMalformedOrCrossDomainConfirmationIsRejected(string change)
    {
        using var f = new Fixture(); var request = f.Request();
        var claims = new ContainerPermissionConfirmation(request.Id, request.Actor, request.LoginBinding, new('B', 64), f.Now.AddMinutes(5));
        var raw = ContainerPermissionWorkflowValidation.Serialize(claims);
        raw = change switch {
            "duplicate" => raw.Replace("{", "{\"Id\":\"" + request.Id + "\",", StringComparison.Ordinal),
            "extra" => raw[..^1] + ",\"Command\":\"copy\"}", "whitespace" => " " + raw,
            "camelCase" => raw.Replace("\"Actor\"", "\"actor\"", StringComparison.Ordinal),
            "zero-id" => ContainerPermissionWorkflowValidation.Serialize(claims with { Id = Guid.Empty }),
            "bad-login" => ContainerPermissionWorkflowValidation.Serialize(claims with { LoginBinding = "" }),
            "no-expiry" => raw.Replace("\"ExpiresAtUtc\":\"2026-10-28T01:07:03+00:00\"", "\"ExpiresAtUtc\":null", StringComparison.Ordinal),
            _ => raw };
        // Use the known ephemeral test key to exercise parsing independently of MAC rejection.
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
        var domain = change == "different-domain" ? "whaledeck-container-directory-business-plan-v1" : "whaledeck-container-permission-business-confirmation-v1";
        var mac = Convert.ToHexString(HMACSHA256.HashData(f.Key, Encoding.UTF8.GetBytes(domain + "\n" + payload)));
        Assert.Throws<ArgumentException>(() => f.Protector.VerifyPermissionConfirmation(payload + "." + mac));
    }
    [Theory][InlineData("id")][InlineData("actor")][InlineData("login")][InlineData("hash")][InlineData("job")]
    [InlineData("confirmed")][InlineData("expiry")]
    public void ConfirmedExecutionHasItsOwnBoundIdentityAndDeadline(string change)
    {
        using var f = new Fixture(); var r = f.Request(); var id = ContainerPermissionWorkflowValidation.ExecutionId(r.Id);
        var original = new ContainerPermissionExecutionBinding(r.Id, r.Actor, r.LoginBinding, new('B', 64), id, f.Now, f.Now.AddMinutes(15));
        var changed = change switch {
            "id" => original with { Id = Guid.NewGuid() }, "actor" => original with { Actor = "another-admin" },
            "login" => original with { LoginBinding = new('E', 64) }, "hash" => original with { PlanHash = new('F', 64) },
            "job" => original with { ExecutionJobId = Guid.NewGuid() },
            "confirmed" => original with { ConfirmedAtUtc = f.Now.AddTicks(1) }, _ => original with { ExecutionExpiresAtUtc = f.Now.AddHours(1) } };
        if (change is "actor" or "login" or "hash") Assert.NotEqual(f.Protector.FingerprintPermissionExecution(original), f.Protector.FingerprintPermissionExecution(changed));
        else Assert.Throws<ArgumentException>(() => f.Protector.FingerprintPermissionExecution(changed));
    }
    [Theory][InlineData("")][InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("A26533E0-029E-48D8-88B8-8273058B370D")][InlineData("a26533e0029e48d888b88273058b370d")]
    public void IdempotencyKeyRequiresCanonicalNonemptyGuid(string value) => Assert.Throws<ArgumentException>(() => ContainerPermissionWorkflowValidation.Key(value));
    [Fact]
    public void DomainsSeparateLoginOwnersRequestIdsAndExecutionIds()
    {
        using var f = new Fixture(); var r = f.Request();
        var id = ContainerPermissionWorkflowValidation.IntentId(r.Actor, r.LoginBinding, r.IdempotencyKey);
        Assert.NotEqual(id, ContainerPermissionWorkflowValidation.ExecutionId(id));
        Assert.NotEqual(id, ContainerPermissionWorkflowValidation.IntentId(r.Actor, new('F', 64), r.IdempotencyKey));
        Assert.NotEqual(id, ContainerPermissionWorkflowValidation.IntentId("another", r.LoginBinding, r.IdempotencyKey));
        Assert.NotEqual(r.Owner, ContainerDirectoryWorkflowValidation.Owner(r.Actor, r.LoginBinding));
        Assert.NotEqual(r.Owner, ContainerPermissionWorkflowValidation.Owner(r.Actor, new('F', 64)));
        Assert.DoesNotContain(r.Actor, r.ToString()); Assert.DoesNotContain(r.Command.Location.Path, r.ToString());
    }
    private sealed class Fixture : IDisposable
    {
        private readonly string _path = Path.GetTempFileName(); public byte[] Key { get; } = RandomNumberGenerator.GetBytes(32);
        public DateTimeOffset Now { get; } = new(2026, 10, 28, 1, 2, 3, TimeSpan.Zero); public FilePlanProtector Protector { get; }
        public Fixture() { File.WriteAllBytes(_path, Key); Protector = new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:CapabilityKeyFile"] = _path }).Build()); }
        public ContainerPermissionRequestBinding Request(ContainerPermissionCommandDto? command = null)
        {
            const string actor = "synthetic-copy-admin"; string login = new('A', 64); var key = Guid.NewGuid();
            return new(ContainerPermissionWorkflowValidation.IntentId(actor, login, key), key, actor, login, "synthetic-trace",
                ContainerPermissionWorkflowValidation.Owner(actor, login), command ?? ContainerPermissionWorkflowFixture.Plan().Command, Now, Now.AddMinutes(15));
        }
        public void Dispose() { CryptographicOperations.ZeroMemory(Key); File.Delete(_path); }
    }
}
