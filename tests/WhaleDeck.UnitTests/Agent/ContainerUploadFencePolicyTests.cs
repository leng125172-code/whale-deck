using System.Security.Cryptography;
using System.Text.Json;
using WhaleDeck.Agent.Services;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

/// <summary>Prevented receipt codec invariants only; no helper, filesystem fence or installed Agent evidence.</summary>
public sealed class ContainerUploadFencePolicyTests
{
    private static string EmptySha256 => Convert.ToHexString(SHA256.HashData(Array.Empty<byte>()));
    private static ContainerUploadReference Reference() => new()
    {
        ContainerId = new('a', 64), InstanceId = "777:2026-10-22T01:02:03.123456789Z",
        OperationId = "aa0a8865343e49bdb01a72680131bf39", Owner = new('B', 64), ExpectedTargetProof = new('C', 64),
        Context = new()
        {
            ActorSubject = "fixture-administrator", TraceId = "fixture-fence-codec",
            JobId = "bb0a8865343e49bdb01a72680131bf39", IdempotencyKey = "cc0a8865343e49bdb01a72680131bf39"
        }
    };

    private static Dictionary<string, object?> Receipt(bool replayed = false)
    {
        var reference = Reference();
        return new()
        {
            ["containerId"] = reference.ContainerId, ["instanceId"] = reference.InstanceId,
            ["operationId"] = reference.OperationId, ["targetProof"] = reference.ExpectedTargetProof,
            ["state"] = "Prevented", ["sizeBytes"] = 0L, ["sha256"] = EmptySha256, ["replayed"] = replayed
        };
    }

    [Theory]
    [InlineData(false, false)][InlineData(true, false)]
    [InlineData(false, true)][InlineData(true, true)]
    public void PreventedReceiptPreservesExactScopeAndReplayWithoutPublicationFacts(bool replayed, bool explicitNullBackup)
    {
        var reference = Reference();
        ContainerUploadPolicy.Reference(reference);
        var value = Receipt(replayed);
        if (explicitNullBackup) value["backupCapture"] = null;
        var result = ContainerUploadPolicy.Receipt(JsonSerializer.SerializeToElement(value), reference);
        Assert.Equal(reference.ContainerId, result.ContainerId);
        Assert.Equal(reference.InstanceId, result.InstanceId);
        Assert.Equal(reference.ExpectedTargetProof, result.TargetProof);
        Assert.Equal(reference.OperationId, result.Upload.OperationId);
        Assert.Equal("Prevented", result.Upload.State);
        Assert.Equal(0, result.Upload.SizeBytes);
        Assert.Equal(EmptySha256, result.Upload.Sha256);
        Assert.Equal(replayed, result.Upload.Replayed);
        Assert.Empty(result.Upload.Version);
        Assert.False(result.Upload.PreviousRetained);
        Assert.False(result.Upload.BindingCurrent);
        Assert.Null(result.Upload.HddBackup);
        Assert.Null(result.Backup);
    }

    [Theory]
    [InlineData("containerId")][InlineData("instanceId")]
    [InlineData("operationId")][InlineData("targetProof")]
    public void PreventedReceiptCannotReplaceAnyBoundScopeFact(string field)
    {
        var value = Receipt();
        value[field] = field switch
        {
            "containerId" => new string('b', 64),
            "instanceId" => "778:2026-10-22T01:02:03.123456789Z",
            "operationId" => "dd0a8865343e49bdb01a72680131bf39",
            _ => new string('D', 64)
        };
        AssertInvalid(value);
    }

    [Theory]
    [InlineData(-1L)][InlineData(1L)][InlineData(1073741824L)]
    public void PreventedReceiptCannotClaimPayloadBytes(long size)
    {
        var value = Receipt();
        value["sizeBytes"] = size;
        AssertInvalid(value);
    }

    [Theory]
    [InlineData("missing")][InlineData("empty")][InlineData("lowercase")][InlineData("other-content")]
    public void PreventedReceiptRequiresCanonicalSha256OfEmptyContent(string variant)
    {
        var value = Receipt();
        value["sha256"] = variant switch
        {
            "missing" => null,
            "empty" => "",
            "lowercase" => EmptySha256.ToLowerInvariant(),
            _ => Convert.ToHexString(SHA256.HashData("not-empty"u8))
        };
        AssertInvalid(value);
    }

    [Theory]
    [InlineData("empty-object")][InlineData("complete-proof")]
    public void PreventedReceiptCannotAttachAnyBackupCapture(string variant)
    {
        var value = Receipt();
        var reference = Reference();
        value["backupCapture"] = variant == "empty-object" ? new Dictionary<string, object?>() : new Dictionary<string, object?>
        {
            ["operationId"] = reference.OperationId, ["storageId"] = "database-platform.backup.hdd",
            ["resourceType"] = "ContainerFile", ["containerId"] = reference.ContainerId,
            ["instanceId"] = reference.InstanceId, ["targetProof"] = reference.ExpectedTargetProof,
            ["sourceVersion"] = new string('D', 64), ["sizeBytes"] = 0L, ["sha256"] = EmptySha256,
            ["createdAtUnixMs"] = 1792602123000L, ["differentSourceDevice"] = true
        };
        AssertInvalid(value);
    }

    [Theory]
    [InlineData("version")][InlineData("previousRetained")][InlineData("bindingCurrent")]
    public void PreventedReceiptCannotClaimPublicationFacts(string field)
    {
        var value = Receipt();
        value[field] = field == "version" ? new string('E', 64) : true;
        AssertInvalid(value);
    }

    [Fact]
    public void PreventedFenceReceiptCannotBeAcceptedAsSuccessfulStagingEvenForAnEmptyUpload()
    {
        var reference = Reference();
        var expected = new ContainerUploadHeader
        {
            Location = new() { ContainerId = reference.ContainerId, InstanceId = reference.InstanceId, Path = "empty.bin" },
            OperationId = reference.OperationId, Owner = reference.Owner, ExpectedTargetProof = reference.ExpectedTargetProof,
            ExpectedParentVersion = new('D', 64), ExpectedVersion = "ABSENT", SizeBytes = 0, Sha256 = EmptySha256,
            Context = reference.Context.Clone()
        };
        var error = Assert.Throws<InvalidOperationException>(() => ContainerUploadPolicy.Receipt(
            JsonSerializer.SerializeToElement(Receipt()), reference, expected));
        Assert.Equal("CONTAINER_UPLOAD_OUTPUT_INVALID", error.Message);
    }

    private static void AssertInvalid(Dictionary<string, object?> value)
    {
        var error = Assert.Throws<InvalidOperationException>(() => ContainerUploadPolicy.Receipt(
            JsonSerializer.SerializeToElement(value), Reference()));
        Assert.Equal("CONTAINER_UPLOAD_OUTPUT_INVALID", error.Message);
    }
}
