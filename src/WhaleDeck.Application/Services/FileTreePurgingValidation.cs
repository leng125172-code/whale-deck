using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

public static class FileTreePurgingValidation
{
    public const long MaximumBytes = 16L * 1024 * 1024 * 1024;
    public const long MaximumArchiveBytes = 17L * 1024 * 1024 * 1024;
    public const int MaximumEntries = 10000;
    public static readonly JsonSerializerOptions JsonOptions = new() { MaxDepth = 12, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public static DateTimeOffset Timestamp(DateTimeOffset value) => value.AddTicks(-(value.Ticks % 10));
    public static bool Scope(string? scope) => scope is "Archive" or "Originals" or "All";
    public static bool Selects(string scope, string component) => (component is "Archive" or "Originals") && (scope == component || scope == "All");
    public static void Request(CreateFileTreePurgingCommand command)
    {
        if (command is null || command.CaptureId == Guid.Empty) throw new ArgumentException("FILES_CAPTURE_ID_REQUIRED");
        if (!Scope(command.Scope)) throw new ArgumentException("FILES_TREE_PURGE_SCOPE_INVALID");
    }
    public static void Command(FileTreePurgingCommand command)
    {
        if (command is null) throw new ArgumentException("FILES_TREE_COMMAND_REQUIRED");
        Request(new(command.CaptureId, command.Scope)); FileManagementService.Location(command.Source);
        if (!FileManagementService.Hex(command.ExpectedVersion) || !FileManagementService.Hex(command.CaptureOwner) ||
            !FileManagementService.Hex(command.ManifestSha256) || !FileManagementService.Hex(command.CatalogueProofHash) ||
            command.CaptureKind is not ("Trash" or "Overwrite")) throw new ArgumentException("FILES_TREE_PURGE_INVALID");
    }
    public static FileTreePurgingCommand CaptureCommand(CreateFileTreePurgingCommand request, FileTreeCapture catalogue,
        IFileTreeRecyclingProtector trash, IFileTreeOverwriteRestoringProtector overwrite)
    {
        Request(request); var verified = FileTreeCaptureValidation.Verify(catalogue, trash, overwrite);
        if (verified.Id != request.CaptureId) throw new InvalidOperationException("FILES_CAPTURE_PROOF_CHANGED");
        var command = new FileTreePurgingCommand(verified.Source.Source, verified.Source.ExpectedVersion, verified.Id, catalogue.OwnerHash,
            verified.Kind, verified.ManifestSha256, request.Scope, catalogue.ProofHash);
        Command(command); return command;
    }
    public static void Capture(FileTreePurgingPlan plan, FileTreeCapture catalogue, IFileTreeRecyclingProtector trash, IFileTreeOverwriteRestoringProtector overwrite)
    {
        Plan(plan);
        if (plan.Command != CaptureCommand(new(plan.Command.CaptureId, plan.Command.Scope), catalogue, trash, overwrite) ||
            plan.EntryCount != catalogue.EntryCount || plan.TotalBytes != catalogue.TotalBytes || plan.ArchiveBytes != catalogue.ArchiveBytes ||
            plan.ArchiveSha256 != catalogue.ArchiveSha256 || catalogue.ContentSha256 is not null && plan.ContentSha256 != catalogue.ContentSha256)
            throw new InvalidOperationException("FILES_CAPTURE_PROOF_CHANGED");
    }
    public static void Plan(FileTreePurgingPlan plan)
    {
        if (plan is null) throw new ArgumentException("FILES_TREE_PLAN_REQUIRED"); Command(plan.Command);
        if (new[] { plan.SourcePolicyHash, plan.StorageHash, plan.CaptureMetadataHash, plan.OriginReceiptHash,
            plan.ManifestFileSha256, plan.ArchiveSha256, plan.ContentSha256, plan.Fingerprint }.Any(hash => !FileManagementService.Hex(hash)) ||
            plan.StoreIdentity is not { Inode: > 0 } || plan.ArchiveIdentity is not { Inode: > 0 } ||
            plan.EntryCount is < 1 or > MaximumEntries || plan.TotalBytes is < 0 or > MaximumBytes || plan.ArchiveBytes is < 1 or > MaximumArchiveBytes)
            throw new ArgumentException("FILES_TREE_PURGE_PLAN_INVALID");
        if (Selects(plan.Command.Scope, "Originals"))
        {
            if (plan.SourceParent is not { Inode: > 0 } || plan.RetainedIdentity is not { Inode: > 0 } || plan.StagingIdentity is not { Inode: > 0 } ||
                !FileManagementService.Hex(plan.RetainedTreeSha256)) throw new ArgumentException("FILES_TREE_PURGE_PLAN_INVALID");
        }
        else if (plan.SourceParent is not null || plan.RetainedIdentity is not null || plan.StagingIdentity is not null || plan.RetainedTreeSha256 is not null)
            throw new ArgumentException("FILES_TREE_PURGE_PLAN_INVALID");
    }
    public static void Receipt(Guid operation, FileTreePurgingPlan plan, FileTreePurgingReceipt receipt)
    {
        Plan(plan);
        if (operation == Guid.Empty || operation == plan.Command.CaptureId || receipt is null || receipt.OperationId != operation || receipt.Kind != "DirectoryPurge" ||
            receipt.CaptureId != plan.Command.CaptureId || receipt.Scope != plan.Command.Scope || receipt.Fingerprint != plan.Fingerprint ||
            receipt.EntryCount != plan.EntryCount || receipt.TotalBytes != plan.TotalBytes || receipt.ArchiveBytes != plan.ArchiveBytes ||
            receipt.State is not ("Completed" or "Aborted" or "Unknown") ||
            receipt.ArchiveRemoved && !Selects(receipt.Scope, "Archive") || receipt.OriginalsRemoved && !Selects(receipt.Scope, "Originals") ||
            (receipt.ArchiveRemoved || receipt.OriginalsRemoved) && !receipt.DeletionStarted ||
            receipt.State == "Aborted" && receipt.DeletionStarted || receipt.State != "Aborted" && !receipt.DeletionStarted ||
            receipt.State == "Completed" && (Selects(receipt.Scope, "Archive") && !receipt.ArchiveRemoved || Selects(receipt.Scope, "Originals") && !receipt.OriginalsRemoved))
            throw new FileTransferRejectedException();
    }
    public static void MonotonicReceipt(FileTreePurgingReceipt previous, FileTreePurgingReceipt next)
    {
        // The fixed root engine has terminal journals. Observing an already
        // observed terminal receipt may not invent a new outcome or side effect.
        if (previous is null || next is null || previous != next) throw new InvalidOperationException("FILES_TREE_PURGE_RECEIPT_CHANGED");
    }
    public static FileTreePurgingCommand ReadCommand(FileTreePurgingOperation row) => Read<FileTreePurgingCommand>(row.CommandJson);
    public static FileTreePurgingPlan ReadPlan(FileTreePurgingOperation row) => Read<FileTreePurgingPlan>(row.PlanJson);
    public static FileTreePurgingReceipt ReadReceipt(FileTreePurgingOperation row) => Read<FileTreePurgingReceipt>(row.ReceiptJson);
    public static FileRecoveryPurgeDto RecoverySummary(FileTreePurgingOperation row, IFileTreePurgingProtector protector,
        FileTreePurgingOperation? original = null)
    {
        if (!HasValidBinding(row, protector)) throw new InvalidOperationException("FILES_INTENT_BINDING_CHANGED");
        if (original is not null)
        {
            // Current facts may advance after disposition, but cannot be from a
            // different capture, owner, plan, execution or older observation.
            if (!HasValidBinding(original, protector) || row.Id != original.Id || row.CaptureId != original.CaptureId ||
                row.RequestHash != original.RequestHash || row.PlanHash != original.PlanHash || row.OwnerHash != original.OwnerHash ||
                row.ExecutionJobId != original.ExecutionJobId || row.ExecutionExpiresAtUtc != original.ExecutionExpiresAtUtc ||
                ReadCommand(row) != ReadCommand(original) || row.Version < original.Version ||
                row.Version == original.Version && (row.State != original.State || row.RootStartedAtUtc != original.RootStartedAtUtc ||
                    row.ReceiptHash != original.ReceiptHash || row.ReceiptObservedAtUtc != original.ReceiptObservedAtUtc) ||
                original.RootStartedAtUtc is not null && row.RootStartedAtUtc != original.RootStartedAtUtc ||
                original.ReceiptJson is not null && (row.ReceiptJson is null || row.ReceiptHash != original.ReceiptHash || row.ReceiptObservedAtUtc != original.ReceiptObservedAtUtc))
                throw new InvalidOperationException("FILES_RECOVERY_ORIGINAL_CHANGED");
            if (original.ReceiptJson is not null) MonotonicReceipt(ReadReceipt(original), ReadReceipt(row));
        }
        var command = ReadCommand(row); var receipt = row.ReceiptJson is null ? null : ReadReceipt(row);
        var noDeletion = row.RootStartedAtUtc is null && receipt is null || receipt?.State == "Aborted";
        bool? Removed(string component, bool established) => established ? true : noDeletion || !Selects(command.Scope, component) ? false : null;
        return new(command.CaptureId, command.CaptureKind, command.Scope, row.Version, row.State,
            row.DeletionStarted ? true : noDeletion ? false : null, Removed("Archive", row.ArchiveRemoved), Removed("Originals", row.OriginalsRemoved));
    }
    private static T Read<T>(string? json)
    {
        if (json is null || Encoding.UTF8.GetByteCount(json) > 32768) throw new ArgumentException("FILES_TREE_PURGE_MATERIAL_INVALID");
        using var document = JsonDocument.Parse(json, new() { MaxDepth = 12 });
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new ArgumentException("FILES_TREE_PURGE_MATERIAL_INVALID");
        Unique(document.RootElement);
        return JsonSerializer.Deserialize<T>(json, JsonOptions) ?? throw new ArgumentException("FILES_TREE_PURGE_MATERIAL_INVALID");
    }
    private static void Unique(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            var fields = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in node.EnumerateObject())
            { if (!fields.Add(property.Name)) throw new ArgumentException("FILES_TREE_PURGE_MATERIAL_INVALID"); Unique(property.Value); }
        }
        else if (node.ValueKind == JsonValueKind.Array) foreach (var value in node.EnumerateArray()) Unique(value);
    }
    public static Guid IntentId(TerminalActor actor, string key)
    {
        if (string.IsNullOrWhiteSpace(actor.Subject) || actor.Subject.Length > 256 || actor.Subject.Any(char.IsControl) || !FileManagementService.Hex(actor.LoginBinding))
            throw new ArgumentException("FILES_ACTOR_INVALID");
        if (!Guid.TryParseExact(key, "D", out var value) || value == Guid.Empty || value.ToString("D") != key) throw new ArgumentException("FILES_IDEMPOTENCY_KEY_REQUIRED");
        return new(SHA256.HashData(Encoding.UTF8.GetBytes("files-tree-purge-plan:" + actor.Subject + ":" + actor.LoginBinding + ":" + key)).AsSpan(0, 16));
    }
    public static Guid ExecutionId(Guid id)
    {
        if (id == Guid.Empty) throw new ArgumentException("FILES_OPERATION_ID_REQUIRED");
        return new(SHA256.HashData(Encoding.UTF8.GetBytes("files-tree-purge-execution:" + id.ToString("N"))).AsSpan(0, 16));
    }
    public static string CaptureToken(Guid id) => FileTreeRestoringValidation.CaptureToken(id);
    public static FileTreePurgingRequestBinding RequestBinding(FileTreePurgingOperation row) => new(row.Id, row.ActorSubject, row.LoginBinding,
        ReadCommand(row), row.CreatedAtUtc, row.PlanningExpiresAtUtc);
    public static FileTreePurgingPlanBinding PlanBinding(FileTreePurgingOperation row) => new(row.Id, row.ActorSubject, row.LoginBinding,
        ReadPlan(row), row.ReadyExpiresAtUtc ?? throw new ArgumentException("FILES_TREE_PLAN_REQUIRED"));
    public static FileTreePurgingReceiptBinding ReceiptBinding(FileTreePurgingOperation row) => new(row.Id, row.ActorSubject, row.LoginBinding,
        ReadReceipt(row), row.ReceiptObservedAtUtc ?? throw new ArgumentException("FILES_TREE_PURGE_RECEIPT_REQUIRED"));
    public static bool HasValidBinding(FileTreePurgingOperation row, IFileTreePurgingProtector protector)
    {
        try
        {
            var command = ReadCommand(row); Command(command);
            if (row.Id != IntentId(new(row.ActorSubject, null, [], row.LoginBinding, "", null), row.IdempotencyKey) || row.CaptureId != command.CaptureId ||
                row.OwnerHash != FileManagementService.Hash("file-owner-v1:" + row.ActorSubject + ":" + row.LoginBinding) ||
                row.SourceHash != new FilePathScope(command.Source).Hash || row.PlanningExpiresAtUtc != row.CreatedAtUtc.AddMinutes(15) ||
                Timestamp(row.CreatedAtUtc) != row.CreatedAtUtc || !Equal(row.RequestHash, protector.FingerprintRequest(RequestBinding(row))) || row.Version < 0 ||
                row.State is not ("Queued" or "Planning" or "Ready" or "PurgingQueued" or "Purging" or "Unknown" or "Succeeded" or "Failed" or "Canceled" or "Expired" or "Acknowledged")) return false;
            if (row.PlanJson is null) return row.PlanHash is null && row.ReadyExpiresAtUtc is null && row.ExecutionJobId is null && row.ExecutionExpiresAtUtc is null &&
                row.RootStartedAtUtc is null && row.ReceiptJson is null && row.ReceiptHash is null && row.ReceiptObservedAtUtc is null &&
                !row.DeletionStarted && !row.ArchiveRemoved && !row.OriginalsRemoved && row.State is "Queued" or "Planning" or "Failed" or "Canceled" or "Expired";
            var plan = ReadPlan(row); Plan(plan);
            if (plan.Command != command || row.ReadyExpiresAtUtc is null || row.ReadyExpiresAtUtc <= row.CreatedAtUtc || row.ReadyExpiresAtUtc > row.PlanningExpiresAtUtc.AddMinutes(5) ||
                Timestamp(row.ReadyExpiresAtUtc.Value) != row.ReadyExpiresAtUtc || !Equal(row.PlanHash, protector.FingerprintPlan(PlanBinding(row)))) return false;
            if (row.ExecutionJobId is null)
                return row.ExecutionExpiresAtUtc is null && row.RootStartedAtUtc is null && row.ReceiptJson is null && row.ReceiptHash is null && row.ReceiptObservedAtUtc is null &&
                    !row.DeletionStarted && !row.ArchiveRemoved && !row.OriginalsRemoved && row.State is "Ready" or "Failed" or "Canceled" or "Expired";
            if (row.ExecutionJobId != ExecutionId(row.Id) || row.ExecutionJobId == row.CaptureId || row.ExecutionJobId == row.Id || row.ExecutionExpiresAtUtc is null ||
                row.ExecutionExpiresAtUtc <= row.CreatedAtUtc || row.ExecutionExpiresAtUtc > row.ReadyExpiresAtUtc.Value.AddMinutes(15) ||
                Timestamp(row.ExecutionExpiresAtUtc.Value) != row.ExecutionExpiresAtUtc ||
                row.State is "Queued" or "Planning" or "Ready" || (row.State is "Purging" or "Unknown") && row.RootStartedAtUtc is null) return false;
            if (row.ReceiptJson is null) return row.ReceiptHash is null && row.ReceiptObservedAtUtc is null && !row.DeletionStarted && !row.ArchiveRemoved && !row.OriginalsRemoved &&
                (row.RootStartedAtUtc is null ? row.State is not ("Succeeded" or "Acknowledged") : row.State is "Purging" or "Unknown");
            var receipt = ReadReceipt(row); Receipt(row.ExecutionJobId.Value, plan, receipt);
            return row.ReceiptObservedAtUtc is not null && row.ReceiptObservedAtUtc >= row.CreatedAtUtc && Timestamp(row.ReceiptObservedAtUtc.Value) == row.ReceiptObservedAtUtc &&
                Equal(row.ReceiptHash, protector.FingerprintReceipt(ReceiptBinding(row))) && row.DeletionStarted == receipt.DeletionStarted &&
                row.ArchiveRemoved == receipt.ArchiveRemoved && row.OriginalsRemoved == receipt.OriginalsRemoved &&
                (!receipt.DeletionStarted || row.RootStartedAtUtc is not null) &&
                (receipt.State switch { "Completed" => row.State == "Succeeded", "Unknown" => row.State is "Unknown" or "Acknowledged",
                    "Aborted" => row.State is "Failed" or "Canceled" or "Acknowledged", _ => false });
        }
        catch (Exception error) when (error is ArgumentException or JsonException or InvalidOperationException or OverflowException) { return false; }
    }
    private static bool Equal(string? left, string right) => FileManagementService.Hex(left) && FileManagementService.Hex(right) &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(left!), Encoding.ASCII.GetBytes(right));
}
