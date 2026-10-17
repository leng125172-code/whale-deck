using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

/// <summary>Independent history protocol invariants. Does not execute writes or bypass authorization.</summary>
public static class FileArchiveValidation
{
    public static readonly JsonSerializerOptions JsonOptions = new() { MaxDepth = 12, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public static void Command(FileArchiveCommand command)
    {
        if (command is null || command.Kind is not ("trash" or "restore" or "purge")) throw new ArgumentException("FILES_ARCHIVE_INVALID");
        FileManagementService.Location(command.Location);
        if (command.Kind == "trash")
        { if (command.VersionId is not null || !FileManagementService.Hex(command.ExpectedVersion)) throw new ArgumentException("FILES_ARCHIVE_INVALID"); }
        else
        {
            if (command.VersionId is not { } id || id == Guid.Empty || command.Kind == "purge" && command.ExpectedVersion != "ABSENT" ||
                command.Kind == "restore" && command.ExpectedVersion != "ABSENT" && !FileManagementService.Hex(command.ExpectedVersion))
                throw new ArgumentException("FILES_ARCHIVE_INVALID");
        }
    }
    public static void HistoryCommand(FileHistoryCommand command)
    {
        if (command is null || command.Take is < 1 or > 50 || command.After is null || command.ExpectedVersion is null) throw new ArgumentException("FILES_ARCHIVE_LIST_INVALID");
        FileManagementService.Location(new(command.RootId, command.Path), true);
        if (command.After != "" && (!Guid.TryParseExact(command.After, "N", out var after) || after == Guid.Empty || after.ToString("N") != command.After) ||
            command.ExpectedVersion != "" && !FileManagementService.Hex(command.ExpectedVersion)) throw new ArgumentException("FILES_ARCHIVE_LIST_INVALID");
    }
    public static void History(FileHistoryCommand command, FileHistoryPage page)
    {
        HistoryCommand(command);
        if (page is null || page.Entries is null || page.Entries.Count > command.Take || page.Scanned is < 0 or > 500 || page.InvalidRecords < 0 || page.InvalidRecords > page.Scanned ||
            page.Entries.Count > page.Scanned || !FileManagementService.Hex(page.Version) || command.ExpectedVersion != "" && command.ExpectedVersion != page.Version || page.Complete != (page.NextCursor == ""))
            throw new FileTransferRejectedException();
        if (!page.Complete && (!Guid.TryParseExact(page.NextCursor, "N", out var cursor) || cursor == Guid.Empty || cursor.ToString("N") != page.NextCursor || string.CompareOrdinal(page.NextCursor, command.After) <= 0))
            throw new FileTransferRejectedException();
        var previous = command.After;
        foreach (var row in page.Entries)
        {
            if (row is null || row.Id == Guid.Empty || row.RootId != command.RootId || command.Path != "" && row.Path != command.Path ||
                !FileManagementService.Hex(row.SourceVersion) || !FileManagementService.Hex(row.Sha256) || row.SizeBytes is < 0 or > FileManagementService.MaximumBytes ||
                row.CapturedAtUtc.ToUnixTimeMilliseconds() is < 0 or > 253402300799999 || row.CaptureKind is not ("Overwrite" or "Trash") ||
                row.CaptureState is not ("Available" or "Pending" or "Trashed" or "Restored" or "Canceled") || row.CaptureKind == "Overwrite" && row.CaptureState != "Available" || row.CaptureKind == "Trash" && row.CaptureState == "Available" ||
                row.Availability is not ("Present" or "Missing" or "Incomplete" or "Invalid") || string.CompareOrdinal(row.Id.ToString("N"), previous) <= 0)
                throw new FileTransferRejectedException();
            FileManagementService.Location(new(row.RootId, row.Path)); previous = row.Id.ToString("N");
            if (!page.Complete && string.CompareOrdinal(previous, page.NextCursor) > 0) throw new FileTransferRejectedException();
        }
    }
    private static bool Identity(FileObjectIdentityDto? value) => value is not null && value.Inode > 0 && value.LinkCount == 1 && value.Mode <= ushort.MaxValue &&
        (value.Mode & 0xF000) == 0x8000 && value.OwnerUid != uint.MaxValue && value.OwnerGid != uint.MaxValue && value.SizeBytes is >= 0 and <= FileManagementService.MaximumBytes;
    public static void Plan(FileArchivePlan plan)
    {
        if (plan is null) throw new ArgumentException("FILES_ARCHIVE_PLAN_INVALID"); Command(plan.Command);
        if (!FileManagementService.Hex(plan.Fingerprint) || !FileManagementService.Hex(plan.Sha256) || plan.SizeBytes is < 0 or > FileManagementService.MaximumBytes)
            throw new ArgumentException("FILES_ARCHIVE_PLAN_INVALID");
        if (plan.Command.Kind == "purge")
        { if (plan.ParentIdentity is not null || plan.SourceIdentity is not null || plan.SourceSha256 != "") throw new ArgumentException("FILES_ARCHIVE_PLAN_INVALID"); }
        else
        {
            if (plan.ParentIdentity is not { Inode: > 0 }) throw new ArgumentException("FILES_ARCHIVE_PLAN_INVALID");
            if (plan.Command.ExpectedVersion == "ABSENT")
            { if (plan.SourceIdentity is not null || plan.SourceSha256 != "") throw new ArgumentException("FILES_ARCHIVE_PLAN_INVALID"); }
            else if (!Identity(plan.SourceIdentity) || !FileManagementService.Hex(plan.SourceSha256)) throw new ArgumentException("FILES_ARCHIVE_PLAN_INVALID");
        }
        if (plan.Command.Kind == "trash")
        {
            if (plan.ArchiveIdentity is not null || plan.RestoreMetadata is not null || plan.SourceIdentity!.SizeBytes != plan.SizeBytes || plan.SourceSha256 != plan.Sha256)
                throw new ArgumentException("FILES_ARCHIVE_PLAN_INVALID");
        }
        else if (plan.ArchiveIdentity is not { } archive || !FileManagementService.Hex(archive.MetadataHash) || !Identity(archive.DataIdentity) || archive.DataIdentity.OwnerUid != 0 ||
            (archive.DataIdentity.Mode & 0xFFF) != 0x180 || archive.DataIdentity.SizeBytes != plan.SizeBytes || !Identity(plan.RestoreMetadata) || plan.RestoreMetadata!.SizeBytes != plan.SizeBytes)
            throw new ArgumentException("FILES_ARCHIVE_PLAN_INVALID");
    }
    public static void Receipt(Guid operationId, FileArchivePlan plan, FileArchiveReceipt receipt)
    {
        Plan(plan);
        if (operationId == Guid.Empty || receipt is null || receipt.Kind != plan.Command.Kind || receipt.Fingerprint != plan.Fingerprint || receipt.SizeBytes != plan.SizeBytes || receipt.Sha256 != plan.Sha256 ||
            receipt.VersionId != (plan.Command.Kind == "trash" ? operationId : plan.Command.VersionId) || receipt.State is not ("Completed" or "Aborted" or "Unknown")) throw new FileTransferRejectedException();
        var retained = plan.Command.Kind == "trash" || plan.Command.Kind == "restore" && plan.SourceIdentity is not null;
        if (receipt.State == "Completed")
        {
            if (!FileManagementService.Hex(receipt.Version) || receipt.SourceRetained != retained || retained && receipt.HddBackup is null) throw new FileTransferRejectedException();
        }
        else if (receipt.Version != "" || receipt.SourceRetained || receipt.BindingCurrent) throw new FileTransferRejectedException();
        if (receipt.HddBackup is { } backup)
        {
            if (!retained || backup.OperationId != operationId.ToString("N") || backup.StorageId != "database-platform.backup.hdd" || backup.SourceVersion != plan.Command.ExpectedVersion ||
                backup.SizeBytes != plan.SourceIdentity!.SizeBytes || backup.Sha256 != plan.SourceSha256 || backup.CreatedAtUnixMs is < 0 or > 253402300799999)
                throw new FileTransferRejectedException();
        }
    }
    public static FileArchivePlan ReadPlan(FileArchiveOperation row) => JsonSerializer.Deserialize<FileArchivePlan>(row.PlanJson, JsonOptions) ?? throw new InvalidOperationException("FILES_ARCHIVE_PLAN_INVALID");
    public static bool HasValidBinding(FileArchiveOperation row, IFileArchiveProtector protector)
    {
        try
        {
            var plan = ReadPlan(row); Plan(plan);
            return row.Kind == plan.Command.Kind && row.RootId == plan.Command.Location.RootId && row.RelativePath == plan.Command.Location.Path && row.ArchiveVersionId == plan.Command.VersionId &&
                row.ExpectedVersion == plan.Command.ExpectedVersion && row.SizeBytes == plan.SizeBytes && row.Sha256 == plan.Sha256 &&
                row.TargetHash == FileManagementService.Hash(row.RootId + ":" + row.RelativePath) && row.OwnerHash == FileManagementService.Hash("file-owner-v1:" + row.ActorSubject + ":" + row.LoginBinding) &&
                FileManagementService.Hex(row.RequestHash) && FileManagementService.Hex(row.LoginBinding) && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(row.RequestHash),
                    Encoding.ASCII.GetBytes(protector.Fingerprint(new(row.Id, row.ActorSubject, row.LoginBinding, plan, row.ExpiresAtUtc))));
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException) { return false; }
    }
}
