using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Agent;

namespace WhaleDeck.UnitTests.Application;

public sealed class FileMutationTests : IDisposable
{
    private readonly string _key = Path.Combine(Path.GetTempPath(), "wd-mutation-test-" + Guid.NewGuid().ToString("N"));
    private readonly IFileMutationProtector _protector;
    private static readonly TerminalActor Actor = new("admin", null, [], new('F', 64), "trace", null);
    public FileMutationTests()
    {
        File.WriteAllBytes(_key, RandomNumberGenerator.GetBytes(32));
        _protector = new FilePlanProtector(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:CapabilityKeyFile"] = _key }).Build());
    }
    public void Dispose() => File.Delete(_key);
    internal static FileMutationPlan Plan(string kind = "copy") => new(new(kind, kind == "mkdir" ? null : new("test.files", "source"), kind == "chmod" ? null : new("test.files", "target"),
        kind == "mkdir" ? "ABSENT" : new('A', 64), kind is "mkdir" or "chmod" ? "0750" : ""),
        kind == "mkdir" ? null : new(ulong.MaxValue, 9007199254740993UL, 0x81A4, uint.MaxValue, 1000, 1, 3, -1),
        kind == "mkdir" ? null : new(1, 2), kind == "chmod" ? null : new(1, 3), kind == "mkdir" ? 0 : 3, kind == "mkdir" ? "" : new('B', 64), new('C', 64));
    [Theory]
    [InlineData("mkdir")][InlineData("copy")][InlineData("move")][InlineData("chmod")]
    public void TypedPlanRoundtripKeepsExactUnsignedIdentityAndSignedNanoseconds(string kind)
    {
        var claims = new FileMutationPlanClaims(Actor.Subject, Actor.LoginBinding, Plan(kind), DateTimeOffset.UtcNow.AddMinutes(5));
        FileMutationService.Validate(claims.Plan);
        var token = _protector.Sign(claims); Assert.Equal(claims, _protector.VerifyMutation(token));
        Assert.Equal(claims.Plan, JsonSerializer.Deserialize<FileMutationPlan>(JsonSerializer.Serialize(claims.Plan), FileMutationService.JsonOptions));
        var other = _protector.Sign(claims with { LoginBinding = new('D', 64) });
        Assert.Throws<ArgumentException>(() => _protector.VerifyMutation(other.Split('.')[0] + "." + token.Split('.')[1]));
    }
    [Fact]
    public void PermanentSealBindsEveryOperationFieldAndCannotReuseUploadDomain()
    {
        var expiry = DateTimeOffset.UtcNow.AddMinutes(15); var plan = Plan(); var id = Guid.NewGuid();
        FileMutationOperation Row(FileMutationBinding binding) => new() { Id = binding.Id, ActorSubject = binding.Actor, LoginBinding = binding.LoginBinding,
            OwnerHash = FileManagementService.Hash("file-owner-v1:" + binding.Actor + ":" + binding.LoginBinding), RequestHash = _protector.Fingerprint(binding),
            PlanJson = JsonSerializer.Serialize(binding.Plan), ExpiresAtUtc = binding.ExpiresAtUtc };
        var binding = new FileMutationBinding(id, Actor.Subject, Actor.LoginBinding, plan, expiry); var row = Row(binding);
        Assert.True(FileMutationService.HasValidBinding(row, _protector));
        foreach (var changed in new[] { binding with { Id = Guid.NewGuid() }, binding with { Actor = "other" }, binding with { LoginBinding = new('E', 64) },
            binding with { ExpiresAtUtc = expiry.AddSeconds(1) }, binding with { Plan = plan with { SourceIdentity = plan.SourceIdentity! with { Inode = 42 } } },
            binding with { Plan = plan with { Command = plan.Command with { Target = new("test.files", "elsewhere") } } }, binding with { Plan = plan with { Fingerprint = new('F', 64) } } })
        { Assert.NotEqual(row.RequestHash, _protector.Fingerprint(changed)); }
        Assert.DoesNotContain(Actor.LoginBinding, JsonSerializer.Serialize(FileMutationService.Map(row)));
        var uploadPlan = ((FilePlanProtector)_protector).Sign(new FileUploadPlanClaims(Actor.Subject, Actor.LoginBinding,
            new("test.files", "source", 3, new('B', 64), "ABSENT"), expiry));
        Assert.Throws<ArgumentException>(() => _protector.VerifyMutation(uploadPlan));
    }
    [Theory]
    [InlineData("4755")][InlineData("1777")][InlineData("777")][InlineData("0999")][InlineData("")]
    public void UnsafeModesAreRejected(string mode) => Assert.Throws<ArgumentException>(() => FileMutationService.Validate(Plan("chmod").Command with { Mode = mode }));
    [Theory]
    [InlineData("/etc/passwd")][InlineData("a/../b")][InlineData("a//b")][InlineData("a\\b")]
    public void MutationLocationsCannotEscapeRegisteredRoot(string target) => Assert.Throws<ArgumentException>(() => FileMutationService.Validate(Plan().Command with { Target = new("test.files", target) }));
    [Fact]
    public void SharedPathHashesIncludeBothSourceAndTargetAndSamePathIsRejected()
    {
        Assert.Equal(2, FileMutationService.PathHashes(Plan()).Length);
        Assert.Contains(FileManagementService.Hash("test.files:source"), FileMutationService.PathHashes(Plan()));
        Assert.Contains(FileManagementService.Hash("test.files:target"), FileMutationService.PathHashes(Plan()));
        Assert.Throws<ArgumentException>(() => FileMutationService.Validate(Plan().Command with { Target = Plan().Command.Source }));
    }
    [Fact]
    public void LongValidUnicodeLocationsDoNotProduceAnUnusableSignedPlan()
    {
        var path = string.Join('/', Enumerable.Repeat(string.Concat(Enumerable.Repeat("😀", 59)), 17));
        var plan = Plan() with { Command = Plan().Command with { Source = new("test.files", path + "a"), Target = new("test.files", path + "b") } };
        FileMutationService.Validate(plan);
        var claims = new FileMutationPlanClaims(Actor.Subject, Actor.LoginBinding, plan, DateTimeOffset.UtcNow.AddMinutes(5));
        var signed = _protector.Sign(claims); Assert.True(signed.Length > 32768); Assert.True(signed.Length <= 65536);
        Assert.Equal(claims, _protector.VerifyMutation(signed));
        Assert.Throws<ArgumentException>(() => _protector.VerifyMutation(new('A', 65537)));
    }
    [Theory]
    [InlineData("kind")][InlineData("digest")][InlineData("size")][InlineData("fingerprint")][InlineData("version")][InlineData("unknown-fields")][InlineData("move-retention")]
    public void ReceiptsCannotInventSuccessOrDetachFromThePlan(string change)
    {
        var plan = Plan(change == "move-retention" ? "move" : "copy");
        var receipt = new FileMutationReceipt(plan.Command.Kind, "Completed", plan.Fingerprint, new('D', 64), plan.SizeBytes, plan.Sha256, plan.Command.Kind == "move", true);
        FileMutationService.ValidateReceipt(plan, receipt);
        var bad = change switch { "kind" => receipt with { Kind = "move" }, "digest" => receipt with { Sha256 = new('E', 64) }, "size" => receipt with { SizeBytes = 4 },
            "fingerprint" => receipt with { Fingerprint = new('F', 64) }, "version" => receipt with { Version = "bad" },
            "unknown-fields" => receipt with { State = "Unknown" }, _ => receipt with { SourceRetained = false } };
        Assert.Throws<FileTransferRejectedException>(() => FileMutationService.ValidateReceipt(plan, bad));
    }
}
