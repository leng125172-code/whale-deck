using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

public static class FileTreePackingValidation
{
    public const long MaximumBytes = 16L * 1024 * 1024 * 1024;
    public const long MaximumArchiveBytes = 17L * 1024 * 1024 * 1024;
    public const int MaximumEntries = 10000;
    public static readonly JsonSerializerOptions JsonOptions = new() { MaxDepth = 12, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public static DateTimeOffset Timestamp(DateTimeOffset value) => value.AddTicks(-(value.Ticks % 10));
    public static void Command(FileTreePackingCommand command)
    {
        if (command is null) throw new ArgumentException("FILES_TREE_COMMAND_REQUIRED");
        FileManagementService.Location(command.Source); FileManagementService.Location(command.Target);
        if (!FileManagementService.Hex(command.ExpectedVersion)) throw new ArgumentException("FILES_VERSION_CONFLICT");
        if (command.Format is not ("tar" or "tar.gz")) throw new ArgumentException("FILES_PACK_FORMAT_INVALID");
        if (!command.Target.Path.EndsWith("." + command.Format, StringComparison.Ordinal)) throw new ArgumentException("FILES_PACK_FORMAT_INVALID");
        if (new FilePathScope(command.Source).Conflicts(new(command.Target))) throw new ArgumentException("FILES_TREE_OVERLAP");
    }
    public static void Plan(FileTreePackingPlan plan)
    {
        if (plan is null) throw new ArgumentException("FILES_TREE_PLAN_REQUIRED"); Command(plan.Command);
        if (!FileManagementService.Hex(plan.ManifestSha256) || !FileManagementService.Hex(plan.SourcePolicyHash) || !FileManagementService.Hex(plan.TargetPolicyHash) ||
            !FileManagementService.Hex(plan.Fingerprint) || plan.EntryCount is < 1 or > MaximumEntries || plan.TotalBytes is < 0 or > MaximumBytes || plan.TargetParent is not { Inode: > 0 })
            throw new ArgumentException("FILES_TREE_PLAN_INVALID");
    }
    public static void Receipt(Guid id, FileTreePackingPlan plan, FileTreePackingReceipt receipt)
    {
        Plan(plan);
        if (id == Guid.Empty || receipt is null || receipt.OperationId != id || receipt.Fingerprint != plan.Fingerprint || receipt.Format != plan.Command.Format || receipt.EntryCount != plan.EntryCount || receipt.TotalBytes != plan.TotalBytes ||
            receipt.State is not ("Completed" or "Aborted" or "Unknown") || receipt.State == "Completed" && (!FileManagementService.Hex(receipt.Version) || receipt.PartialRetained) ||
            receipt.State == "Completed" && (receipt.ArchiveBytes is < 1 or > MaximumArchiveBytes || !FileManagementService.Hex(receipt.ArchiveSha256)) ||
            receipt.State != "Completed" && (receipt.Version != "" || receipt.ArchiveSha256 != "" || receipt.ArchiveBytes != 0)) throw new FileTransferRejectedException();
    }
    public static FileTreePackingCommand ReadCommand(FileTreePackingOperation row) => JsonSerializer.Deserialize<FileTreePackingCommand>(row.CommandJson, JsonOptions) ?? throw new ArgumentException("FILES_TREE_COMMAND_REQUIRED");
    public static FileTreePackingPlan ReadPlan(FileTreePackingOperation row) => JsonSerializer.Deserialize<FileTreePackingPlan>(row.PlanJson ?? "null", JsonOptions) ?? throw new ArgumentException("FILES_TREE_PLAN_REQUIRED");
    public static Guid IntentId(TerminalActor actor, string key)
    {
        if (!Guid.TryParseExact(key, "D", out var value) || value == Guid.Empty || value.ToString("D") != key) throw new ArgumentException("FILES_IDEMPOTENCY_KEY_REQUIRED");
        return new(SHA256.HashData(Encoding.UTF8.GetBytes("files-tree-pack-plan:" + actor.Subject + ":" + actor.LoginBinding + ":" + key)).AsSpan(0, 16));
    }
    public static Guid ExecutionId(Guid id) => new(SHA256.HashData(Encoding.UTF8.GetBytes("files-tree-pack-execution:" + id.ToString("N"))).AsSpan(0, 16));
    public static FileTreePackingRequestBinding RequestBinding(FileTreePackingOperation row) => new(row.Id, row.ActorSubject, row.LoginBinding, ReadCommand(row), row.PlanningExpiresAtUtc);
    public static FileTreePackingPlanBinding PlanBinding(FileTreePackingOperation row) => new(row.Id, row.ActorSubject, row.LoginBinding, ReadPlan(row), row.ReadyExpiresAtUtc ?? throw new ArgumentException("FILES_TREE_PLAN_REQUIRED"));
    public static bool HasValidBinding(FileTreePackingOperation row, IFileTreePackingProtector protector)
    {
        try
        {
            var command = ReadCommand(row); Command(command);
            if (row.Id != IntentId(new(row.ActorSubject, null, [], row.LoginBinding, "", null), row.IdempotencyKey) || !FileManagementService.Hex(row.LoginBinding) ||
                row.OwnerHash != FileManagementService.Hash("file-owner-v1:" + row.ActorSubject + ":" + row.LoginBinding) || row.SourceHash != new FilePathScope(command.Source).Hash ||
                row.TargetHash != new FilePathScope(command.Target).Hash || !Equal(row.RequestHash, protector.FingerprintRequest(RequestBinding(row)))) return false;
            if (row.PlanJson is null) return row.PlanHash is null && row.ReadyExpiresAtUtc is null && row.ExecutionJobId is null;
            var plan = ReadPlan(row); Plan(plan);
            return plan.Command == command && row.ReadyExpiresAtUtc is not null && Equal(row.PlanHash, protector.FingerprintPlan(PlanBinding(row))) &&
                (row.ExecutionJobId is null && row.ExecutionExpiresAtUtc is null || row.ExecutionJobId == ExecutionId(row.Id) && row.ExecutionExpiresAtUtc is not null);
        }
        catch (Exception error) when (error is ArgumentException or JsonException or InvalidOperationException) { return false; }
    }
    private static bool Equal(string? left, string right) => left is not null && FileManagementService.Hex(left) && FileManagementService.Hex(right) &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(left!), Encoding.ASCII.GetBytes(right));
}
