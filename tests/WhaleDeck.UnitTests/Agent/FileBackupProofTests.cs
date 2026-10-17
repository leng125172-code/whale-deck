using System.Text.Json;
using WhaleDeck.Agent.Services;
using Xunit;

namespace WhaleDeck.UnitTests.Agent;

public sealed class FileBackupProofTests
{
    private static JsonElement Receipt(string operation = "11111111111111111111111111111111", long size = 7, string storage = "database-platform.backup.hdd", long created = 1760000000000) => JsonSerializer.SerializeToElement(new
    {
        operationId = "11111111111111111111111111111111", state = "Completed", sizeBytes = 3, sha256 = new string('A', 64), version = new string('B', 64),
        hddBackup = new { operationId = operation, storageId = storage, sourceVersion = new string('C', 64), sizeBytes = size,
            sha256 = new string('D', 64), createdAtUnixMs = created, differentSourceDevice = false }
    });
    [Fact]
    public void TypedProofPreservesOldSizeChecksumVersionAndLogicalVsPhysicalDistinction()
    {
        var value = FileTransferPolicy.Receipt(Receipt());
        Assert.Equal(7, value.HddBackup.SizeBytes); Assert.Equal(new string('C', 64), value.HddBackup.SourceVersion);
        Assert.Equal(new string('D', 64), value.HddBackup.Sha256); Assert.False(value.HddBackup.DifferentSourceDevice);
    }
    [Theory]
    [InlineData("22222222222222222222222222222222", 7, "database-platform.backup.hdd", 1760000000000)]
    [InlineData("11111111111111111111111111111111", -1, "database-platform.backup.hdd", 1760000000000)]
    [InlineData("11111111111111111111111111111111", 1073741825, "database-platform.backup.hdd", 1760000000000)]
    [InlineData("11111111111111111111111111111111", 7, "other", 1760000000000)]
    [InlineData("11111111111111111111111111111111", 7, "database-platform.backup.hdd", -1)]
    public void InvalidOrForeignProofIsNotAdopted(string operation, long size, string storage, long created) =>
        Assert.Throws<InvalidOperationException>(() => FileTransferPolicy.Receipt(Receipt(operation, size, storage, created)));
}
