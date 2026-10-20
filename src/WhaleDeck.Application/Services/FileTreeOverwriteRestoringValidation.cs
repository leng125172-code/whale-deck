using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

public static class FileTreeOverwriteRestoringValidation
{
    public const long MaximumBytes = 16L * 1024 * 1024 * 1024;
    public const long MaximumArchiveBytes = 17L * 1024 * 1024 * 1024;
    public const int MaximumEntries = 10000;
    public static readonly JsonSerializerOptions JsonOptions = new() { MaxDepth = 12, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public static DateTimeOffset Timestamp(DateTimeOffset value) => value.AddTicks(-(value.Ticks % 10));
    public static void Request(CreateFileTreeOverwriteRestoringCommand command)
    {
        if (command is null || command.CaptureId == Guid.Empty) throw new ArgumentException("FILES_CAPTURE_ID_REQUIRED");
        FileManagementService.Location(command.Target);
        if (!FileManagementService.Hex(command.TargetExpectedVersion)) throw new ArgumentException("FILES_VERSION_CONFLICT");
    }
    public static void Command(FileTreeOverwriteRestoringCommand command)
    {
        if (command is null) throw new ArgumentException("FILES_TREE_COMMAND_REQUIRED");
        FileManagementService.Location(command.Source); FileManagementService.Location(command.Target);
        if (!FileManagementService.Hex(command.ExpectedVersion) || !FileManagementService.Hex(command.TargetExpectedVersion))
            throw new ArgumentException("FILES_VERSION_CONFLICT");
        if (command.Format != "tar.gz" || command.CaptureId == Guid.Empty || !FileManagementService.Hex(command.CaptureOwner) ||
            !FileManagementService.Hex(command.ManifestSha256) || command.CaptureKind is not ("Trash" or "Overwrite"))
            throw new ArgumentException("FILES_TREE_OVERWRITE_RESTORE_INVALID");
    }
    public static void Plan(FileTreeOverwriteRestoringPlan plan)
    {
        if (plan is null) throw new ArgumentException("FILES_TREE_PLAN_REQUIRED"); Command(plan.Command);
        if (!FileManagementService.Hex(plan.ContentSha256) || !FileManagementService.Hex(plan.ArchiveSha256) || plan.ArchiveBytes is < 1 or > MaximumArchiveBytes ||
            !FileManagementService.Hex(plan.SourcePolicyHash) || !FileManagementService.Hex(plan.TargetPolicyHash) || !FileManagementService.Hex(plan.Fingerprint) ||
            plan.EntryCount is < 1 or > MaximumEntries || plan.TotalBytes is < 0 or > MaximumBytes || plan.TargetParent is not { Inode: > 0 } ||
            !FileManagementService.Hex(plan.TargetManifestSha256) || !FileManagementService.Hex(plan.TargetTreeSha256) ||
            plan.TargetEntryCount is < 1 or > MaximumEntries || plan.TargetTotalBytes is < 0 or > MaximumBytes || plan.TargetIdentity is not { Inode: > 0 })
            throw new ArgumentException("FILES_TREE_OVERWRITE_RESTORE_PLAN_INVALID");
    }
    public static void Receipt(Guid id, FileTreeOverwriteRestoringPlan plan, FileTreeOverwriteRestoringReceipt receipt)
    {
        Plan(plan);
        if (id == Guid.Empty || receipt is null || receipt.OperationId != id || receipt.Fingerprint != plan.Fingerprint || receipt.Format != plan.Command.Format ||
            receipt.CaptureId != plan.Command.CaptureId || receipt.Kind != "DirectoryOverwriteRestore" || receipt.EntryCount != plan.EntryCount ||
            receipt.TotalBytes != plan.TotalBytes || receipt.ArchiveBytes != plan.ArchiveBytes || receipt.ArchiveSha256 != plan.ArchiveSha256 ||
            receipt.State is not ("Completed" or "Aborted" or "Unknown") ||
            receipt.State == "Completed" && (!FileManagementService.Hex(receipt.Version) || receipt.PartialRetained) ||
            receipt.State != "Completed" && receipt.Version != "" ||
            receipt.BackupVerified && (!receipt.BackupMayBeRetained || receipt.OverwrittenCaptureId != id || receipt.BackupArchiveBytes is < 1 or > MaximumArchiveBytes || !FileManagementService.Hex(receipt.BackupArchiveSha256)) ||
            !receipt.BackupVerified && (receipt.OverwrittenCaptureId is not null || receipt.BackupArchiveBytes != 0 || receipt.BackupArchiveSha256 != "") ||
            receipt.TargetMayHaveChanged && !receipt.BackupVerified || receipt.OriginalsRetained && !receipt.TargetMayHaveChanged ||
            receipt.State == "Completed" && !(receipt.BackupVerified && receipt.OriginalsRetained && receipt.TargetMayHaveChanged) ||
            receipt.State == "Aborted" && receipt.TargetMayHaveChanged) throw new FileTransferRejectedException();
    }
    public static FileTreeOverwriteRestoringCommand ReadCommand(FileTreeOverwriteRestoringOperation row) =>
        JsonSerializer.Deserialize<FileTreeOverwriteRestoringCommand>(row.CommandJson, JsonOptions) ?? throw new ArgumentException("FILES_TREE_COMMAND_REQUIRED");
    public static FileTreeOverwriteRestoringPlan ReadPlan(FileTreeOverwriteRestoringOperation row) =>
        JsonSerializer.Deserialize<FileTreeOverwriteRestoringPlan>(row.PlanJson ?? "null", JsonOptions) ?? throw new ArgumentException("FILES_TREE_PLAN_REQUIRED");
    public static Guid IntentId(TerminalActor actor, string key)
    {
        if (!Guid.TryParseExact(key, "D", out var value) || value == Guid.Empty || value.ToString("D") != key) throw new ArgumentException("FILES_IDEMPOTENCY_KEY_REQUIRED");
        return new(SHA256.HashData(Encoding.UTF8.GetBytes("files-tree-overwrite-restore-plan:" + actor.Subject + ":" + actor.LoginBinding + ":" + key)).AsSpan(0, 16));
    }
    public static Guid ExecutionId(Guid id) => new(SHA256.HashData(Encoding.UTF8.GetBytes("files-tree-overwrite-restore-execution:" + id.ToString("N"))).AsSpan(0, 16));
    // This resource is intentionally shared with recycling/absent-target restore.
    public static string CaptureToken(Guid id) => FileTreeRestoringValidation.CaptureToken(id);
    public static FileTreeOverwriteRestoringRequestBinding RequestBinding(FileTreeOverwriteRestoringOperation row) =>
        new(row.Id, row.ActorSubject, row.LoginBinding, ReadCommand(row), row.PlanningExpiresAtUtc);
    public static FileTreeOverwriteRestoringPlanBinding PlanBinding(FileTreeOverwriteRestoringOperation row) =>
        new(row.Id, row.ActorSubject, row.LoginBinding, ReadPlan(row), row.ReadyExpiresAtUtc ?? throw new ArgumentException("FILES_TREE_PLAN_REQUIRED"));
    public static FileTreeOverwrittenCaptureBinding BackupBinding(FileTreeOverwriteRestoringOperation row)
    {
        var plan = ReadPlan(row);
        return new(row.OverwrittenCaptureId ?? throw new ArgumentException("FILES_CAPTURE_PROOF_REQUIRED"), row.Id, row.OwnerHash, row.TargetHash,
            new(plan.Command.Target, plan.Command.TargetExpectedVersion), plan.TargetManifestSha256, plan.TargetTreeSha256, plan.TargetEntryCount, plan.TargetTotalBytes,
            row.BackupArchiveBytes ?? throw new ArgumentException("FILES_CAPTURE_PROOF_REQUIRED"), row.BackupArchiveSha256 ?? throw new ArgumentException("FILES_CAPTURE_PROOF_REQUIRED"),
            row.BackupCapturedAtUtc ?? throw new ArgumentException("FILES_CAPTURE_PROOF_REQUIRED"));
    }
    public static bool HasValidBinding(FileTreeOverwriteRestoringOperation row, IFileTreeOverwriteRestoringProtector protector)
    {
        try
        {
            var command = ReadCommand(row); Command(command);
            if (row.CaptureId != command.CaptureId || row.Id != IntentId(new(row.ActorSubject, null, [], row.LoginBinding, "", null), row.IdempotencyKey) || !FileManagementService.Hex(row.LoginBinding) ||
                row.OwnerHash != FileManagementService.Hash("file-owner-v1:" + row.ActorSubject + ":" + row.LoginBinding) ||
                row.SourceHash != new FilePathScope(command.Source).Hash || row.TargetHash != new FilePathScope(command.Target).Hash ||
                !Equal(row.RequestHash, protector.FingerprintRequest(RequestBinding(row)))) return false;
            if (row.TargetMayHaveChanged && !row.BackupVerified || row.OriginalsRetained && !row.TargetMayHaveChanged) return false;
            FileTreeOverwriteRestoringPlan? plan = null;
            if (row.PlanJson is not null) { plan = ReadPlan(row); Plan(plan); }
            var backup = row.BackupVerified
                ? row.ExecutionJobId is not null && row.OverwrittenCaptureId == row.ExecutionJobId && row.BackupMayBeRetained &&
                    row.BackupArchiveBytes is >= 1 and <= MaximumArchiveBytes && FileManagementService.Hex(row.BackupArchiveSha256) &&
                    row.BackupCapturedAtUtc is not null && Equal(row.BackupProofHash, protector.FingerprintCapture(BackupBinding(row)))
                : row.OverwrittenCaptureId is null && row.BackupArchiveBytes is null && row.BackupArchiveSha256 is null && row.BackupProofHash is null && row.BackupCapturedAtUtc is null;
            if (!backup) return false;
            if (row.PlanJson is null) return row.PlanHash is null && row.ReadyExpiresAtUtc is null && row.ExecutionJobId is null && row.ExecutionExpiresAtUtc is null &&
                row.ArchiveBytes is null && row.ArchiveSha256 is null && !row.BackupMayBeRetained && !row.BackupVerified && !row.OriginalsRetained && !row.TargetMayHaveChanged;
            return plan!.Command == command && row.ArchiveBytes == plan.ArchiveBytes && row.ArchiveSha256 == plan.ArchiveSha256 &&
                row.ReadyExpiresAtUtc is not null && Equal(row.PlanHash, protector.FingerprintPlan(PlanBinding(row))) &&
                (row.ExecutionJobId is null && row.ExecutionExpiresAtUtc is null || row.ExecutionJobId == ExecutionId(row.Id) && row.ExecutionExpiresAtUtc is not null);
        }
        catch (Exception error) when (error is ArgumentException or JsonException or InvalidOperationException) { return false; }
    }
    private static bool Equal(string? left, string right) => left is not null && FileManagementService.Hex(left) && FileManagementService.Hex(right) &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(left!), Encoding.ASCII.GetBytes(right));
}
