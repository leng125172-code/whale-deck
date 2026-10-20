using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

public static class FileTreeRecyclingValidation
{
    public const long MaximumBytes = 16L * 1024 * 1024 * 1024;
    public const long MaximumArchiveBytes = 17L * 1024 * 1024 * 1024;
    public const int MaximumEntries = 10000;
    public static readonly JsonSerializerOptions JsonOptions = new() { MaxDepth = 12, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public static DateTimeOffset Timestamp(DateTimeOffset value) => value.AddTicks(-(value.Ticks % 10));
    /// <summary>A null hash means a read-only catalogue query for an entire registered root, never a mutation scope.</summary>
    public static string? CaptureSourceHash(FileLocationDto location)
    {
        FileManagementService.Location(location, true);
        return location.Path.Length == 0 ? null : new FilePathScope(location).Hash;
    }
    public static void Command(FileTreeRecyclingCommand command)
    {
        if (command is null) throw new ArgumentException("FILES_TREE_COMMAND_REQUIRED");
        FileManagementService.Location(command.Source);
        if (!FileManagementService.Hex(command.ExpectedVersion)) throw new ArgumentException("FILES_VERSION_CONFLICT");
    }
    public static void Plan(FileTreeRecyclingPlan plan)
    {
        if (plan is null) throw new ArgumentException("FILES_TREE_PLAN_REQUIRED"); Command(plan.Command);
        if (plan.SourceParent is not { Inode: > 0 } || !FileManagementService.Hex(plan.SourcePolicyHash) || !FileManagementService.Hex(plan.ManifestSha256) ||
            !FileManagementService.Hex(plan.Fingerprint) || plan.EntryCount is < 1 or > MaximumEntries || plan.TotalBytes is < 0 or > MaximumBytes)
            throw new ArgumentException("FILES_TREE_PLAN_INVALID");
    }
    public static void Receipt(Guid id, FileTreeRecyclingPlan plan, FileTreeRecyclingReceipt receipt)
    {
        Plan(plan);
        if (id == Guid.Empty || receipt is null || receipt.OperationId != id || receipt.CaptureId != id || receipt.Fingerprint != plan.Fingerprint ||
            receipt.ManifestSha256 != plan.ManifestSha256 || receipt.EntryCount != plan.EntryCount || receipt.TotalBytes != plan.TotalBytes ||
            receipt.State is not ("Completed" or "Aborted" or "Unknown") ||
            receipt.CaptureVerified && (!receipt.CaptureMayBeRetained || receipt.ArchiveBytes is < 1 or > MaximumArchiveBytes || !FileManagementService.Hex(receipt.ArchiveSha256)) ||
            !receipt.CaptureVerified && (receipt.ArchiveBytes != 0 || receipt.ArchiveSha256 != "") ||
            receipt.SourceMayHaveChanged && !receipt.CaptureVerified || receipt.State == "Aborted" && receipt.SourceMayHaveChanged ||
            receipt.State == "Completed" && !(receipt.CaptureVerified && receipt.SourceMayHaveChanged && receipt.SourceRemoved && receipt.OriginalsRetained) ||
            receipt.State != "Completed" && (receipt.SourceRemoved || receipt.OriginalsRetained)) throw new FileTransferRejectedException();
    }
    public static FileTreeRecyclingCommand ReadCommand(FileTreeRecyclingOperation row) => JsonSerializer.Deserialize<FileTreeRecyclingCommand>(row.CommandJson, JsonOptions) ?? throw new ArgumentException("FILES_TREE_COMMAND_REQUIRED");
    public static FileTreeRecyclingPlan ReadPlan(FileTreeRecyclingOperation row) => JsonSerializer.Deserialize<FileTreeRecyclingPlan>(row.PlanJson ?? "null", JsonOptions) ?? throw new ArgumentException("FILES_TREE_PLAN_REQUIRED");
    public static Guid IntentId(TerminalActor actor, string key)
    {
        if (!Guid.TryParseExact(key, "D", out var value) || value == Guid.Empty || value.ToString("D") != key) throw new ArgumentException("FILES_IDEMPOTENCY_KEY_REQUIRED");
        return new(SHA256.HashData(Encoding.UTF8.GetBytes("files-tree-trash-plan:" + actor.Subject + ":" + actor.LoginBinding + ":" + key)).AsSpan(0, 16));
    }
    public static Guid ExecutionId(Guid id) => new(SHA256.HashData(Encoding.UTF8.GetBytes("files-tree-trash-execution:" + id.ToString("N"))).AsSpan(0, 16));
    public static FileTreeRecyclingRequestBinding RequestBinding(FileTreeRecyclingOperation row) => new(row.Id, row.ActorSubject, row.LoginBinding, ReadCommand(row), row.PlanningExpiresAtUtc);
    public static FileTreeRecyclingPlanBinding PlanBinding(FileTreeRecyclingOperation row) => new(row.Id, row.ActorSubject, row.LoginBinding, ReadPlan(row), row.ReadyExpiresAtUtc ?? throw new ArgumentException("FILES_TREE_PLAN_REQUIRED"));
    public static FileTreeCaptureBinding CaptureBinding(FileTreeCapture capture) => new(capture.Id, capture.IntentId, capture.OwnerHash, capture.SourceHash,
        JsonSerializer.Deserialize<FileTreeRecyclingCommand>(capture.SourceJson, JsonOptions) ?? throw new FileTransferRejectedException(), capture.ManifestSha256,
        capture.EntryCount, capture.TotalBytes, capture.ArchiveBytes, capture.ArchiveSha256, capture.CreatedAtUtc);
    public static void Capture(FileTreeCapture capture, IFileTreeRecyclingProtector protector)
    {
        if (capture.Kind != "Trash" || capture.ContentSha256 is not null) throw new FileTransferRejectedException();
        var binding = CaptureBinding(capture); Command(binding.Command);
        if (capture.Id == Guid.Empty || capture.IntentId == Guid.Empty || capture.Id != ExecutionId(capture.IntentId) || !FileManagementService.Hex(capture.OwnerHash) ||
            new FilePathScope(binding.Command.Source).Hash != capture.SourceHash || !FileManagementService.Hex(capture.ManifestSha256) ||
            capture.EntryCount is < 1 or > MaximumEntries || capture.TotalBytes is < 0 or > MaximumBytes || capture.ArchiveBytes is < 1 or > MaximumArchiveBytes ||
            !FileManagementService.Hex(capture.ArchiveSha256) || !Equal(capture.ProofHash, protector.FingerprintCapture(binding))) throw new FileTransferRejectedException();
    }
    public static bool HasValidBinding(FileTreeRecyclingOperation row, IFileTreeRecyclingProtector protector)
    {
        try
        {
            var command = ReadCommand(row); Command(command);
            if (row.Id != IntentId(new(row.ActorSubject, null, [], row.LoginBinding, "", null), row.IdempotencyKey) || !FileManagementService.Hex(row.LoginBinding) ||
                row.OwnerHash != FileManagementService.Hash("file-owner-v1:" + row.ActorSubject + ":" + row.LoginBinding) || row.SourceHash != new FilePathScope(command.Source).Hash ||
                !Equal(row.RequestHash, protector.FingerprintRequest(RequestBinding(row)))) return false;
            if (row.PlanJson is null) return row.PlanHash is null && row.ReadyExpiresAtUtc is null && row.ExecutionJobId is null && !row.CaptureVerified && row.ArchiveBytes is null && row.ArchiveSha256 is null && row.CaptureId is null && row.CaptureHash is null && row.CaptureCreatedAtUtc is null;
            var plan = ReadPlan(row); Plan(plan);
            return plan.Command == command && row.ReadyExpiresAtUtc is not null && Equal(row.PlanHash, protector.FingerprintPlan(PlanBinding(row))) &&
                (row.ExecutionJobId is null && row.ExecutionExpiresAtUtc is null || row.ExecutionJobId == ExecutionId(row.Id) && row.ExecutionExpiresAtUtc is not null) &&
                (row.CaptureVerified ? row.CaptureId == row.ExecutionJobId && row.ArchiveBytes is >= 1 and <= MaximumArchiveBytes && FileManagementService.Hex(row.ArchiveSha256) && row.CaptureCreatedAtUtc is not null &&
                    Equal(row.CaptureHash, protector.FingerprintCapture(new(row.CaptureId!.Value, row.Id, row.OwnerHash, row.SourceHash, command, plan.ManifestSha256, plan.EntryCount,
                        plan.TotalBytes, row.ArchiveBytes!.Value, row.ArchiveSha256!, row.CaptureCreatedAtUtc.Value))) :
                    row.CaptureId is null && row.CaptureHash is null && row.ArchiveBytes is null && row.ArchiveSha256 is null && row.CaptureCreatedAtUtc is null);
        }
        catch (Exception error) when (error is ArgumentException or JsonException or InvalidOperationException) { return false; }
    }
    private static bool Equal(string? left, string right) => left is not null && FileManagementService.Hex(left) && FileManagementService.Hex(right) &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(left!), Encoding.ASCII.GetBytes(right));
}
