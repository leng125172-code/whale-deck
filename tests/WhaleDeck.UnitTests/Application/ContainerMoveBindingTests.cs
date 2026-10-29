using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Infrastructure.Agent;
using WhaleDeck.UnitTests.Agent;

namespace WhaleDeck.UnitTests.Application;

public sealed class ContainerMoveBindingTests
{
    [Theory][InlineData(false)][InlineData(true)]
    public void RequestPlanExecutionAndConfirmationUseIndependentDomainsAndRoundTripExactValues(bool directory)
    {
        using var f = new Fixture(); var plan = ContainerMoveGatewayTests.Plan(directory); var request = f.Request(plan.Command);
        var planned = new ContainerMovePlanBinding(request, plan, f.Now.AddMinutes(5));
        var hash = f.Protector.FingerprintContainerMovePlan(planned);
        var execution = new ContainerMoveExecutionBinding(request.Id, request.Actor, request.LoginBinding, hash,
            ContainerMoveWorkflowValidation.ExecutionId(request.Id), f.Now, f.Now.AddMinutes(15));
        var claims = new ContainerMoveConfirmation(request.Id, request.Actor, request.LoginBinding, hash, planned.ReadyExpiresAtUtc);
        var values = new[] { f.Protector.FingerprintContainerMoveRequest(request), hash, f.Protector.FingerprintContainerMoveExecution(execution) };
        Assert.Equal(3, values.Distinct().Count());
        Assert.Equal(claims, f.Protector.VerifyContainerMoveConfirmation(f.Protector.SignContainerMoveConfirmation(claims)));
        var json = ContainerMoveWorkflowValidation.Serialize(planned);
        var restored = JsonSerializer.Deserialize<ContainerMovePlanBinding>(json, ContainerMoveWorkflowValidation.Json)!;
        Assert.Equal(planned, restored); Assert.Equal(ulong.MaxValue, restored.Plan.SourceIdentity.Device);
        Assert.Equal(1700000000987654321, restored.Plan.SourceChangedAtUnixNs);
        Assert.Equal(hash, f.Protector.FingerprintContainerMovePlan(restored));
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
            _ => original with { Command = original.Command with { TargetPath = "work/another" } } };
        if (change is "trace" or "command") Assert.NotEqual(f.Protector.FingerprintContainerMoveRequest(original), f.Protector.FingerprintContainerMoveRequest(changed));
        else Assert.Throws<ArgumentException>(() => f.Protector.FingerprintContainerMoveRequest(changed));
    }
    [Theory][InlineData("different-domain")][InlineData("duplicate")][InlineData("extra")][InlineData("whitespace")]
    [InlineData("camelCase")][InlineData("zero-id")][InlineData("bad-login")][InlineData("no-expiry")]
    public void EvenCorrectlyMaccedMalformedOrCrossDomainConfirmationIsRejected(string change)
    {
        using var f = new Fixture(); var request = f.Request();
        var claims = new ContainerMoveConfirmation(request.Id, request.Actor, request.LoginBinding, new('B', 64), f.Now.AddMinutes(5));
        var raw = ContainerMoveWorkflowValidation.Serialize(claims);
        raw = change switch {
            "duplicate" => raw.Replace("{", "{\"Id\":\"" + request.Id + "\",", StringComparison.Ordinal),
            "extra" => raw[..^1] + ",\"Command\":\"move\"}", "whitespace" => " " + raw,
            "camelCase" => raw.Replace("\"Actor\"", "\"actor\"", StringComparison.Ordinal),
            "zero-id" => ContainerMoveWorkflowValidation.Serialize(claims with { Id = Guid.Empty }),
            "bad-login" => ContainerMoveWorkflowValidation.Serialize(claims with { LoginBinding = "" }),
            "no-expiry" => raw.Replace("\"ExpiresAtUtc\":\"2026-10-28T01:07:03+00:00\"", "\"ExpiresAtUtc\":null", StringComparison.Ordinal),
            _ => raw };
        // Use the known ephemeral test key to exercise parsing independently of MAC rejection.
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
        var domain = change == "different-domain" ? "whaledeck-container-directory-business-plan-v1" : "whaledeck-container-move-business-confirmation-v1";
        var mac = Convert.ToHexString(HMACSHA256.HashData(f.Key, Encoding.UTF8.GetBytes(domain + "\n" + payload)));
        Assert.Throws<ArgumentException>(() => f.Protector.VerifyContainerMoveConfirmation(payload + "." + mac));
    }
    [Theory][InlineData("id")][InlineData("actor")][InlineData("login")][InlineData("hash")][InlineData("job")]
    [InlineData("confirmed")][InlineData("expiry")]
    public void ConfirmedExecutionHasItsOwnBoundIdentityAndDeadline(string change)
    {
        using var f = new Fixture(); var r = f.Request(); var id = ContainerMoveWorkflowValidation.ExecutionId(r.Id);
        var original = new ContainerMoveExecutionBinding(r.Id, r.Actor, r.LoginBinding, new('B', 64), id, f.Now, f.Now.AddMinutes(15));
        var changed = change switch {
            "id" => original with { Id = Guid.NewGuid() }, "actor" => original with { Actor = "another-admin" },
            "login" => original with { LoginBinding = new('E', 64) }, "hash" => original with { PlanHash = new('F', 64) },
            "job" => original with { ExecutionJobId = Guid.NewGuid() },
            "confirmed" => original with { ConfirmedAtUtc = f.Now.AddTicks(1) }, _ => original with { ExecutionExpiresAtUtc = f.Now.AddHours(1) } };
        if (change is "actor" or "login" or "hash") Assert.NotEqual(f.Protector.FingerprintContainerMoveExecution(original), f.Protector.FingerprintContainerMoveExecution(changed));
        else Assert.Throws<ArgumentException>(() => f.Protector.FingerprintContainerMoveExecution(changed));
    }
    [Theory][InlineData("")][InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("A26533E0-029E-48D8-88B8-8273058B370D")][InlineData("a26533e0029e48d888b88273058b370d")]
    public void IdempotencyKeyRequiresCanonicalNonemptyGuid(string value) => Assert.Throws<ArgumentException>(() => ContainerMoveWorkflowValidation.Key(value));
    [Fact]
    public void DomainsSeparateLoginOwnersRequestIdsAndExecutionIds()
    {
        using var f = new Fixture(); var r = f.Request();
        var id = ContainerMoveWorkflowValidation.IntentId(r.Actor, r.LoginBinding, r.IdempotencyKey);
        Assert.NotEqual(id, ContainerMoveWorkflowValidation.ExecutionId(id));
        Assert.NotEqual(id, ContainerMoveWorkflowValidation.IntentId(r.Actor, new('F', 64), r.IdempotencyKey));
        Assert.NotEqual(id, ContainerMoveWorkflowValidation.IntentId("another", r.LoginBinding, r.IdempotencyKey));
        Assert.NotEqual(r.Owner, ContainerDirectoryWorkflowValidation.Owner(r.Actor, r.LoginBinding));
        Assert.NotEqual(r.Owner, ContainerMoveWorkflowValidation.Owner(r.Actor, new('F', 64)));
        Assert.DoesNotContain(r.Actor, r.ToString()); Assert.DoesNotContain(r.Command.Source.Path, r.ToString());
    }
    [Fact]
    public void CopyAndHostMoveDomainsCannotAuthorizeContainerMoveBusinessIntent()
    {
        using var f = new Fixture(); var r = f.Request();
        var copyId = ContainerCopyWorkflowValidation.IntentId(r.Actor, r.LoginBinding, r.IdempotencyKey);
        Assert.NotEqual(copyId, r.Id);
        Assert.NotEqual(ContainerCopyWorkflowValidation.ExecutionId(r.Id), ContainerMoveWorkflowValidation.ExecutionId(r.Id));
        Assert.NotEqual(ContainerCopyWorkflowValidation.Owner(r.Actor, r.LoginBinding), r.Owner);
        var claims = new ContainerMoveConfirmation(r.Id, r.Actor, r.LoginBinding, new('B', 64), f.Now.AddMinutes(5));
        var copy = new ContainerCopyConfirmation(claims.Id, claims.Actor, claims.LoginBinding, claims.PlanHash, claims.ExpiresAtUtc);
        Assert.Throws<ArgumentException>(() => f.Protector.VerifyContainerMoveConfirmation(f.Protector.SignCopyConfirmation(copy)));
        Assert.Throws<ArgumentException>(() => f.Protector.VerifyCopyConfirmation(f.Protector.SignContainerMoveConfirmation(claims)));
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void IndependentPlanMacBindsFullOriginalOuterAndTypedInnerPlan(bool directory)
    {
        using var f = new Fixture(); var p = ContainerMoveGatewayTests.Plan(directory); var r = f.Request(p.Command);
        var original = new ContainerMovePlanBinding(r, p, f.Now.AddMinutes(5));
        var hash = f.Protector.FingerprintContainerMovePlan(original);
        Assert.NotEqual(hash, f.Protector.FingerprintContainerMovePlan(original with { Plan = p with { Fingerprint = new('F', 64) } }));
        Assert.NotEqual(hash, f.Protector.FingerprintContainerMovePlan(original with { Plan = p with { OwnerUid = p.OwnerUid + 1 } }));
        Assert.NotEqual(hash, f.Protector.FingerprintContainerMovePlan(original with { ReadyExpiresAtUtc = original.ReadyExpiresAtUtc.AddSeconds(1) }));
        if (directory)
        {
            var changed = p with { TreePlan = p.TreePlan! with { Fingerprint = new('D', 64) } };
            Assert.NotEqual(hash, f.Protector.FingerprintContainerMovePlan(original with { Plan = changed }));
        }
        else
        {
            var changed = p with { FilePlan = p.FilePlan! with { Sha256 = new('D', 64) } };
            Assert.NotEqual(hash, f.Protector.FingerprintContainerMovePlan(original with { Plan = changed }));
        }
        Assert.Throws<ArgumentException>(() => f.Protector.FingerprintContainerMovePlan(original with { Plan = p with { OperationKind = "Copy" } }));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _path = Path.GetTempFileName(); public byte[] Key { get; } = RandomNumberGenerator.GetBytes(32);
        public DateTimeOffset Now { get; } = new(2026, 10, 28, 1, 2, 3, TimeSpan.Zero); public FilePlanProtector Protector { get; }
        public Fixture() { File.WriteAllBytes(_path, Key); Protector = new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:CapabilityKeyFile"] = _path }).Build()); }
        public ContainerMoveRequestBinding Request(ContainerMoveCommandDto? command = null)
        {
            const string actor = "synthetic-move-admin"; string login = new('A', 64); var key = Guid.NewGuid();
            return new(ContainerMoveWorkflowValidation.IntentId(actor, login, key), key, actor, login, "synthetic-trace",
                ContainerMoveWorkflowValidation.Owner(actor, login), command ?? ContainerMoveGatewayTests.Plan().Command, Now, Now.AddMinutes(15));
        }
        public void Dispose() { CryptographicOperations.ZeroMemory(Key); File.Delete(_path); }
    }
}
