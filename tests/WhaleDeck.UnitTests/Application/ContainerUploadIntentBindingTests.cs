using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Agent;

namespace WhaleDeck.UnitTests.Application;

public sealed class ContainerUploadIntentBindingTests : IDisposable
{
    private static readonly DateTimeOffset At = new(2026, 10, 27, 1, 2, 3, TimeSpan.Zero);
    private static readonly byte[] Key = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
    private readonly string _keyFile = Path.GetTempFileName();
    private readonly FilePlanProtector _protector;
    public ContainerUploadIntentBindingTests()
    {
        File.WriteAllBytes(_keyFile, Key);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(_keyFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        _protector = new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:CapabilityKeyFile"] = _keyFile }).Build());
    }
    public void Dispose() => File.Delete(_keyFile);
    private static ContainerFileUploadTargetDto Target() => new(new(new('a', 64), "777:2026-10-27T01:02:03.123456789Z", "资料/file.bin"),
        new('A', 64), new('B', 64), "ABSENT", 0);
    private static ContainerFileUploadPlanClaims Claims() => new("container-test-admin", new('C', 64), Target(), 7, new('D', 64), At, At.AddMinutes(5));
    private static ContainerFileUploadBinding Binding() => new(Guid.NewGuid(), Guid.NewGuid(), "container-test-admin", new('C', 64), "test-container-upload",
        Target(), 7, new('D', 64), new('E', 64), At, At.AddMinutes(15));
    private static string SignedJson(string value, string domain = "whaledeck-container-upload-plan-v1")
    {
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        return payload + "." + Convert.ToHexString(HMACSHA256.HashData(Key, Encoding.UTF8.GetBytes(domain + "\n" + payload)));
    }
    [Fact]
    public void PlanRoundTripKeepsRawNanosecondsUnicodeAndMicrosecondTime()
    {
        var claims = Claims() with { CreatedAtUtc = At.AddTicks(1234560), ExpiresAtUtc = At.AddMinutes(5).AddTicks(1234560) };
        var token = _protector.SignContainerUpload(claims);
        Assert.Equal(claims, _protector.VerifyContainerUpload(token));
        Assert.Equal("777:2026-10-27T01:02:03.123456789Z", _protector.VerifyContainerUpload(token).Target.Location.InstanceId);
        Assert.Equal(nameof(ContainerFileUploadPlanClaims), claims.ToString());
    }
    [Fact]
    public void SignedPlanCannotBeForgedOrReusedAcrossHostPlanDomain()
    {
        var json = JsonSerializer.Serialize(Claims());
        Assert.Throws<ArgumentException>(() => _protector.VerifyContainerUpload(SignedJson(json, "whaledeck-file-plan-v1")));
        Assert.Throws<ArgumentException>(() => ((IFilePlanProtector)_protector).Verify(_protector.SignContainerUpload(Claims())));
        var token = _protector.SignContainerUpload(Claims());
        Assert.Throws<ArgumentException>(() => _protector.VerifyContainerUpload(token[..^1] + (token[^1] == '0' ? "1" : "0")));
        var parts = token.Split('.');
        parts[0] = Convert.ToBase64String(Encoding.UTF8.GetBytes(json.Replace("container-test-admin", "different-admin", StringComparison.Ordinal)));
        Assert.Throws<ArgumentException>(() => _protector.VerifyContainerUpload(string.Join('.', parts)));
    }
    [Fact]
    public void CorrectMacDoesNotPermitDuplicateUnknownMissingOrWrongTypedFields()
    {
        var json = JsonSerializer.Serialize(Claims());
        foreach (var malformed in new[] {
            json.Insert(1, "\"Actor\":\"container-test-admin\","), json.Insert(1, "\"Extra\":true,"),
            json.Replace("\"Target\":{", "\"Target\":{\"Unknown\":true,", StringComparison.Ordinal),
            json.Replace("\"Location\":{", "\"Location\":{\"Path\":\"資料/file.bin\",", StringComparison.Ordinal),
            json.Replace("\"SizeBytes\":7", "\"SizeBytes\":\"7\"", StringComparison.Ordinal), "null", "[]", "{}" })
            Assert.Throws<ArgumentException>(() => _protector.VerifyContainerUpload(SignedJson(malformed)));
    }
    [Fact]
    public void EveryImmutableIntentFieldIsBoundAndContainerOwnerIsSeparateFromHost()
    {
        var first = Binding(); var target = first.Target; var location = target.Location;
        var altered = new[] { first with { Id = Guid.NewGuid() }, first with { IdempotencyKey = Guid.NewGuid() },
            first with { Actor = "another-admin" }, first with { LoginBinding = new('F',64) }, first with { TraceId = "another-trace" },
            first with { Target = target with { Location = location with { ContainerId = new('b',64) } } },
            first with { Target = target with { Location = location with { InstanceId = "777:2026-10-27T01:02:03.123456788Z" } } },
            first with { Target = target with { Location = location with { Path = "资料/other.bin" } } },
            first with { Target = target with { TargetProof = new('F',64) } },
            first with { Target = target with { ExpectedParentVersion = new('F',64) } },
            first with { Target = target with { ExpectedVersion = new('F',64) } },
            first with { Target = target with { ExpectedVersion = new('F',64), CurrentSizeBytes = 3 } },
            first with { SizeBytes = 8 }, first with { Sha256 = new('F',64) }, first with { PlanHash = new('F',64) },
            first with { CreatedAtUtc = At.AddTicks(10), ExpiresAtUtc = At.AddMinutes(15).AddTicks(10) } };
        var signature = _protector.FingerprintContainerUpload(first);
        var values = altered.Select(_protector.FingerprintContainerUpload).ToArray();
        Assert.All(values, value => Assert.NotEqual(signature, value)); Assert.Equal(values.Length, values.Distinct().Count());
        Assert.NotEqual(FileManagementService.Hash("file-owner-v1:" + first.Actor + ":" + first.LoginBinding), ContainerFileUploadIntentValidation.Owner(first.Actor, first.LoginBinding));
        Assert.Equal(nameof(ContainerFileUploadBinding), first.ToString());
    }
    [Fact]
    public void ImpreciseOffsetOrWrongLifetimeCannotProduceIntentMac()
    {
        var first = Binding();
        foreach (var bad in new[] { first with { CreatedAtUtc = At.AddTicks(1) }, first with { ExpiresAtUtc = first.ExpiresAtUtc.AddTicks(1) },
            first with { CreatedAtUtc = At.ToOffset(TimeSpan.FromHours(8)) }, first with { ExpiresAtUtc = At.AddMinutes(16) }, first with { Id = Guid.Empty },
            first with { IdempotencyKey = Guid.Empty }, first with { TraceId = "trace\ncommand" }, first with { TraceId = "bad\ud800" } })
            Assert.Throws<ArgumentException>(() => _protector.FingerprintContainerUpload(bad));
        Assert.Equal(At.AddTicks(10), ContainerFileUploadIntentValidation.CanonicalTime(At.AddTicks(19)));
    }
    [Theory]
    [InlineData("../escape")]
    [InlineData("/absolute")]
    [InlineData("empty//segment")]
    [InlineData("bad\\separator")]
    [InlineData("")]
    public void SignedPlanRejectsInvalidContainerPath(string path)
    {
        var first = Claims(); var changed = first with { Target = first.Target with { Location = first.Target.Location with { Path = path } } };
        Assert.Throws<ArgumentException>(() => _protector.SignContainerUpload(changed));
    }
    [Fact]
    public void PermanentRowAndRootContextCannotChangeOwnerOrLoseOriginalBinding()
    {
        var value = Binding(); var location = value.Target.Location;
        ContainerFileUploadOperation Row(string owner) => new() { Id = value.Id, IdempotencyKey = value.IdempotencyKey,
            ActorSubject = value.Actor, LoginBinding = value.LoginBinding, TraceId = value.TraceId, OwnerHash = owner,
            ContainerId = location.ContainerId, InstanceId = location.InstanceId, RelativePath = location.Path,
            TargetProof = value.Target.TargetProof, ExpectedParentVersion = value.Target.ExpectedParentVersion, ExpectedVersion = value.Target.ExpectedVersion,
            PreviousSizeBytes = value.Target.CurrentSizeBytes, PlanHash = value.PlanHash, RequestHash = new('A',64), Sha256 = value.Sha256,
            SizeBytes = value.SizeBytes, CreatedAtUtc = value.CreatedAtUtc, ExpiresAtUtc = value.ExpiresAtUtc };
        var row = Row(ContainerFileUploadIntentValidation.Owner(value.Actor, value.LoginBinding));
        Assert.Equal(value, ContainerFileUploadIntentValidation.ReadBinding(row));
        var stage = ContainerFileUploadIntentValidation.Stage(row); ContainerFileUploadValidation.Stage(stage);
        var reference = ContainerFileUploadIntentValidation.Reference(row); ContainerFileUploadValidation.Reference(reference);
        Assert.Equal(value.Id, stage.Context.JobId); Assert.Equal(value.IdempotencyKey, stage.Context.IdempotencyKey);
        Assert.Equal(stage.Context, reference.Context); Assert.Equal(location, stage.Location);
        Assert.Throws<ArgumentException>(() => ContainerFileUploadIntentValidation.Stage(Row(new('F',64))));
    }
}
