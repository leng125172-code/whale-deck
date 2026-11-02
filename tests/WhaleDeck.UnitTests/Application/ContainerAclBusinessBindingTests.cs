using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Infrastructure.Agent;
using WhaleDeck.UnitTests.Agent;

namespace WhaleDeck.UnitTests.Application;

public sealed class ContainerAclBusinessBindingTests
{
    [Theory][InlineData(false)][InlineData(true)]
    public void RequestPlanExecutionAndConfirmationUseIndependentDomainsAndRoundTripExactValues(bool directory)
    {
        using var f = new Fixture(); var plan = ContainerAclWorkflowFixture.Plan(directory); var request = f.Request(plan.Command);
        var planned = new ContainerAclPlanBinding(request, plan, f.Now.AddMinutes(5));
        var hash = f.Protector.FingerprintAclPlan(planned);
        var execution = new ContainerAclExecutionBinding(request.Id, request.Actor, request.LoginBinding, hash,
            ContainerAclWorkflowValidation.ExecutionId(request.Id), f.Now, f.Now.AddMinutes(15));
        var claims = new ContainerAclConfirmation(request.Id, request.Actor, request.LoginBinding, hash, planned.ReadyExpiresAtUtc);
        var operation = new ContainerAclOperationDto(execution.ExecutionJobId, request.Owner, plan,
            new(request.Actor, request.TraceId, execution.ExecutionJobId, request.IdempotencyKey));
        var receipt = new ContainerAclReceiptDto(operation.Id, plan.Command.Location.ContainerId, plan.Command.Location.InstanceId,
            plan.Fingerprint, plan.TargetProof, "Unknown", "", false, false, plan.Kind, null, plan.NoChange, true, false, "", plan.Command.AclType, plan.TargetMode);
        var values = new[] { f.Protector.FingerprintAclRequest(request), hash, f.Protector.FingerprintAclExecution(execution),
            f.Protector.FingerprintAclReceipt(new(operation, receipt)) };
        Assert.Equal(4, values.Distinct().Count());
        Assert.Equal(claims, f.Protector.VerifyAclConfirmation(f.Protector.SignAclConfirmation(claims)));
        var json = ContainerAclWorkflowValidation.Serialize(planned);
        var restored = JsonSerializer.Deserialize<ContainerAclPlanBinding>(json, ContainerAclWorkflowValidation.Json)!;
        Assert.Equal(json, ContainerAclWorkflowValidation.Serialize(restored)); Assert.Equal(ulong.MaxValue, restored.Plan.SourceIdentity.Device);
        Assert.Equal(1700000000987654321, restored.Plan.SourceChangedAtUnixNs);
        Assert.Equal(hash, f.Protector.FingerprintAclPlan(restored));
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
            _ => original with { Command = original.Command with { Entries = ContainerAclWorkflowFixture.Named(2300) } } };
        if (change is "trace" or "command") Assert.NotEqual(f.Protector.FingerprintAclRequest(original), f.Protector.FingerprintAclRequest(changed));
        else Assert.Throws<ArgumentException>(() => f.Protector.FingerprintAclRequest(changed));
    }
    [Theory][InlineData("different-domain")][InlineData("duplicate")][InlineData("extra")][InlineData("whitespace")]
    [InlineData("camelCase")][InlineData("zero-id")][InlineData("bad-login")][InlineData("no-expiry")]
    public void EvenCorrectlyMaccedMalformedOrCrossDomainConfirmationIsRejected(string change)
    {
        using var f = new Fixture(); var request = f.Request();
        var claims = new ContainerAclConfirmation(request.Id, request.Actor, request.LoginBinding, new('B', 64), f.Now.AddMinutes(5));
        var raw = ContainerAclWorkflowValidation.Serialize(claims);
        raw = change switch {
            "duplicate" => raw.Replace("{", "{\"Id\":\"" + request.Id + "\",", StringComparison.Ordinal),
            "extra" => raw[..^1] + ",\"Command\":\"copy\"}", "whitespace" => " " + raw,
            "camelCase" => raw.Replace("\"Actor\"", "\"actor\"", StringComparison.Ordinal),
            "zero-id" => ContainerAclWorkflowValidation.Serialize(claims with { Id = Guid.Empty }),
            "bad-login" => ContainerAclWorkflowValidation.Serialize(claims with { LoginBinding = "" }),
            "no-expiry" => raw.Replace("\"ExpiresAtUtc\":\"2026-10-28T01:07:03+00:00\"", "\"ExpiresAtUtc\":null", StringComparison.Ordinal),
            _ => raw };
        // Use the known ephemeral test key to exercise parsing independently of MAC rejection.
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
        var domain = change == "different-domain" ? "whaledeck-container-directory-business-plan-v1" : "whaledeck-container-acl-business-confirmation-v1";
        var mac = Convert.ToHexString(HMACSHA256.HashData(f.Key, Encoding.UTF8.GetBytes(domain + "\n" + payload)));
        Assert.Throws<ArgumentException>(() => f.Protector.VerifyAclConfirmation(payload + "." + mac));
    }
    [Theory][InlineData("id")][InlineData("actor")][InlineData("login")][InlineData("hash")][InlineData("job")]
    [InlineData("confirmed")][InlineData("expiry")]
    public void ConfirmedExecutionHasItsOwnBoundIdentityAndDeadline(string change)
    {
        using var f = new Fixture(); var r = f.Request(); var id = ContainerAclWorkflowValidation.ExecutionId(r.Id);
        var original = new ContainerAclExecutionBinding(r.Id, r.Actor, r.LoginBinding, new('B', 64), id, f.Now, f.Now.AddMinutes(15));
        var changed = change switch {
            "id" => original with { Id = Guid.NewGuid() }, "actor" => original with { Actor = "another-admin" },
            "login" => original with { LoginBinding = new('E', 64) }, "hash" => original with { PlanHash = new('F', 64) },
            "job" => original with { ExecutionJobId = Guid.NewGuid() },
            "confirmed" => original with { ConfirmedAtUtc = f.Now.AddTicks(1) }, _ => original with { ExecutionExpiresAtUtc = f.Now.AddHours(1) } };
        if (change is "actor" or "login" or "hash") Assert.NotEqual(f.Protector.FingerprintAclExecution(original), f.Protector.FingerprintAclExecution(changed));
        else Assert.Throws<ArgumentException>(() => f.Protector.FingerprintAclExecution(changed));
    }
    [Theory][InlineData("")][InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("A26533E0-029E-48D8-88B8-8273058B370D")][InlineData("a26533e0029e48d888b88273058b370d")]
    public void IdempotencyKeyRequiresCanonicalNonemptyGuid(string value) => Assert.Throws<ArgumentException>(() => ContainerAclWorkflowValidation.Key(value));
    [Fact]
    public void DomainsSeparateLoginOwnersRequestIdsAndExecutionIds()
    {
        using var f = new Fixture(); var r = f.Request();
        var id = ContainerAclWorkflowValidation.IntentId(r.Actor, r.LoginBinding, r.IdempotencyKey);
        Assert.NotEqual(id, ContainerAclWorkflowValidation.ExecutionId(id));
        Assert.NotEqual(id, ContainerAclWorkflowValidation.IntentId(r.Actor, new('F', 64), r.IdempotencyKey));
        Assert.NotEqual(id, ContainerAclWorkflowValidation.IntentId("another", r.LoginBinding, r.IdempotencyKey));
        Assert.NotEqual(r.Owner, ContainerDirectoryWorkflowValidation.Owner(r.Actor, r.LoginBinding));
        Assert.NotEqual(r.Owner, ContainerAclWorkflowValidation.Owner(r.Actor, new('F', 64)));
        Assert.DoesNotContain(r.Actor, r.ToString()); Assert.DoesNotContain(r.Command.Location.Path, r.ToString());
    }
    [Fact]
    public void AclBusinessAuthorityCannotBeReusedAsOrdinaryChmodAuthority()
    {
        using var f = new Fixture(); var r = f.Request();
        Assert.NotEqual(r.Id, ContainerPermissionWorkflowValidation.IntentId(r.Actor, r.LoginBinding, r.IdempotencyKey));
        Assert.NotEqual(r.Owner, ContainerPermissionWorkflowValidation.Owner(r.Actor, r.LoginBinding));
        var own = new ContainerAclConfirmation(r.Id, r.Actor, r.LoginBinding, new('B', 64), f.Now.AddMinutes(5));
        var mode = new ContainerPermissionConfirmation(r.Id, r.Actor, r.LoginBinding, new('B', 64), f.Now.AddMinutes(5));
        Assert.Throws<ArgumentException>(() => f.Protector.VerifyPermissionConfirmation(f.Protector.SignAclConfirmation(own)));
        Assert.Throws<ArgumentException>(() => f.Protector.VerifyAclConfirmation(f.Protector.SignPermissionConfirmation(mode)));
    }
    [Theory][InlineData("id")][InlineData("permission")][InlineData("mask")][InlineData("type")][InlineData("version")]
    public void EveryAclRequestFieldChangesAuthority(string field)
    {
        using var f = new Fixture(); var r = f.Request();
        var c = r.Command;
        var changed = field switch {
            "id" => c with { Entries = ContainerAclWorkflowFixture.Named(0) },
            "permission" => c with { Entries = ContainerAclWorkflowFixture.Named(2200, 0) },
            "mask" => c with { Entries = ContainerAclWorkflowFixture.Named(2200, 4, 0) },
            "type" => c with { AclType = "default" }, _ => c with { ExpectedVersion = new('D', 64) } };
        Assert.NotEqual(f.Protector.FingerprintAclRequest(r), f.Protector.FingerprintAclRequest(r with { Command = changed }));
    }
    [Theory][InlineData("other-acl")][InlineData("ctime")][InlineData("target-mode")][InlineData("no-change")]
    public void PlanBindsOriginalOtherAclSourceTimeAndDerivedTarget(string field)
    {
        using var f = new Fixture(); var p = ContainerAclWorkflowFixture.Plan(true); var r = f.Request(p.Command);
        var original = new ContainerAclPlanBinding(r, p, f.Now.AddMinutes(5));
        var changed = p with { DefaultAcl = ContainerAclWorkflowFixture.Named(0) };
        if (field == "ctime") changed = p with { SourceChangedAtUnixNs = p.SourceChangedAtUnixNs + 1 };
        if (field == "target-mode") changed = p with { TargetMode = 511 };
        if (field == "no-change") changed = p with { NoChange = true };
        if (field is "target-mode" or "no-change") Assert.Throws<ArgumentException>(() => f.Protector.FingerprintAclPlan(original with { Plan = changed }));
        else Assert.NotEqual(f.Protector.FingerprintAclPlan(original), f.Protector.FingerprintAclPlan(original with { Plan = changed }));
    }
    [Fact]
    public void AclBusinessAuthorityCannotBeReusedForOwnershipOrRecursiveOwnership()
    {
        using var f = new Fixture(); var r = f.Request();
        Assert.NotEqual(r.Id, ContainerOwnershipWorkflowValidation.IntentId(r.Actor, r.LoginBinding, r.IdempotencyKey));
        Assert.NotEqual(r.Owner, ContainerRecursiveOwnershipWorkflowValidation.Owner(r.Actor, r.LoginBinding));
        var acl = new ContainerAclConfirmation(r.Id, r.Actor, r.LoginBinding, new('B', 64), f.Now.AddMinutes(5));
        var owner = new ContainerOwnershipConfirmation(r.Id, r.Actor, r.LoginBinding, new('B', 64), f.Now.AddMinutes(5));
        Assert.Throws<ArgumentException>(() => f.Protector.VerifyOwnershipConfirmation(f.Protector.SignAclConfirmation(acl)));
        Assert.Throws<ArgumentException>(() => f.Protector.VerifyAclConfirmation(f.Protector.SignOwnershipConfirmation(owner)));
    }
    private sealed class Fixture : IDisposable
    {
        private readonly string _path = Path.GetTempFileName(); public byte[] Key { get; } = RandomNumberGenerator.GetBytes(32);
        public DateTimeOffset Now { get; } = new(2026, 10, 28, 1, 2, 3, TimeSpan.Zero); public FilePlanProtector Protector { get; }
        public Fixture() { File.WriteAllBytes(_path, Key); Protector = new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:CapabilityKeyFile"] = _path }).Build()); }
        public ContainerAclRequestBinding Request(ContainerAclCommandDto? command = null)
        {
            const string actor = "synthetic-copy-admin"; string login = new('A', 64); var key = Guid.NewGuid();
            return new(ContainerAclWorkflowValidation.IntentId(actor, login, key), key, actor, login, "synthetic-trace",
                ContainerAclWorkflowValidation.Owner(actor, login), command ?? ContainerAclWorkflowFixture.Plan().Command, Now, Now.AddMinutes(15));
        }
        public void Dispose() { CryptographicOperations.ZeroMemory(Key); File.Delete(_path); }
    }
}
