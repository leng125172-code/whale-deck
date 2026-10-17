using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;
using Xunit;

namespace WhaleDeck.UnitTests.Application;

public sealed class FileHddBackupTests
{
    private static FileUploadOperation Operation(string version = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA") => new()
    {
        Id = Guid.Parse("11111111-1111-1111-1111-111111111111"), ActorSubject = "admin", LoginBinding = new('A', 64), OwnerHash = new('B', 64),
        RootId = "files.shared", RelativePath = "sample.txt", TargetHash = new('C', 64), RequestHash = new('D', 64), ExpectedVersion = version, Sha256 = new('E', 64)
    };
    private static FileHddBackupDto Proof(FileUploadOperation operation) => new(operation.Id.ToString("N"), "database-platform.backup.hdd",
        operation.ExpectedVersion, 7, new('F', 64), 1760000000000, false);
    [Fact]
    public void CreatesHaveNoPreviousFileAndCannotClaimAnInventedBackup()
    {
        var operation = Operation("ABSENT"); FileManagementService.ValidateBackup(operation, null);
        Assert.Throws<FileTransferRejectedException>(() => FileManagementService.ValidateBackup(operation, Proof(operation)));
    }
    [Fact]
    public void OverwriteRequiresTheExactOperationStorageAndSourceVersionWithBoundedProof()
    {
        var operation = Operation(); var proof = Proof(operation);
        FileManagementService.ValidateBackup(operation, proof);
        FileManagementService.ValidateBackup(operation, proof with { DifferentSourceDevice = true });
        Assert.Throws<FileTransferRejectedException>(() => FileManagementService.ValidateBackup(operation, null));
        foreach (var invalid in new[] { proof with { OperationId = Guid.NewGuid().ToString("N") }, proof with { StorageId = "other" },
            proof with { SourceVersion = new('B', 64) }, proof with { Sha256 = "private-body" }, proof with { SizeBytes = -1 },
            proof with { SizeBytes = FileManagementService.MaximumBytes + 1 }, proof with { CreatedAtUnixMs = -1 }, proof with { CreatedAtUnixMs = 253402300800000 } })
            Assert.Throws<FileTransferRejectedException>(() => FileManagementService.ValidateBackup(operation, invalid));
    }
}
