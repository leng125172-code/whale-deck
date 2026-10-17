using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

public static class FileTreeMoveValidation
{
    public static readonly JsonSerializerOptions JsonOptions = FileTreeCopyValidation.JsonOptions;
    public static DateTimeOffset Timestamp(DateTimeOffset value) => FileTreeCopyValidation.Timestamp(value);
    public static void Command(FileTreeCopyCommand command) => FileTreeCopyValidation.Command(command);
    public static Guid IntentId(TerminalActor actor, string key)
    {
        _ = FileTreeCopyValidation.IntentId(actor, key); // Preserve the canonical public idempotency-key rules.
        return new(SHA256.HashData(Encoding.UTF8.GetBytes("files-tree-move-plan:" + actor.Subject + ":" + actor.LoginBinding + ":" + key)).AsSpan(0, 16));
    }
    public static Guid ExecutionId(Guid id) => new(SHA256.HashData(Encoding.UTF8.GetBytes("files-tree-move-execution:" + id.ToString("N"))).AsSpan(0, 16));
    public static void Plan(FileTreeMovePlan plan)
    {
        if (plan is null) throw new ArgumentException("FILES_TREE_PLAN_REQUIRED");
        FileTreeCopyValidation.Plan(plan.CopyPlan);
        if (plan.SourceParent is not { Inode: > 0 } || !FileManagementService.Hex(plan.Fingerprint)) throw new ArgumentException("FILES_TREE_PLAN_INVALID");
    }
    public static void Receipt(Guid id, FileTreeMovePlan plan, FileTreeMoveReceipt receipt)
    {
        Plan(plan);
        if (receipt is null) throw new FileTransferRejectedException();
        var completed = receipt.State == "Completed";
        if (id == Guid.Empty || receipt.OperationId != id || receipt.Fingerprint != plan.Fingerprint || receipt.EntryCount != plan.CopyPlan.EntryCount || receipt.TotalBytes != plan.CopyPlan.TotalBytes ||
            receipt.State is not ("Completed" or "Aborted" or "Unknown") ||
            completed && (!FileManagementService.Hex(receipt.Version) || !receipt.DestinationPublished || !receipt.SourceRemoved || !receipt.SourceRetained || receipt.PartialRetained) ||
            !completed && (receipt.Version != "" || receipt.SourceRemoved || receipt.SourceRetained) ||
            receipt.State == "Aborted" && receipt.DestinationPublished || !completed && receipt.DestinationPublished && !receipt.PartialRetained)
            throw new FileTransferRejectedException();
    }
    public static FileTreeCopyCommand ReadCommand(FileTreeMoveOperation row) => JsonSerializer.Deserialize<FileTreeCopyCommand>(row.CommandJson, JsonOptions) ?? throw new ArgumentException("FILES_TREE_COMMAND_REQUIRED");
    public static FileTreeMovePlan ReadPlan(FileTreeMoveOperation row) => JsonSerializer.Deserialize<FileTreeMovePlan>(row.PlanJson ?? "null", JsonOptions) ?? throw new ArgumentException("FILES_TREE_PLAN_REQUIRED");
    public static FileTreeMoveRequestBinding RequestBinding(FileTreeMoveOperation row) => new(row.Id, row.ActorSubject, row.LoginBinding, ReadCommand(row), row.PlanningExpiresAtUtc);
    public static FileTreeMovePlanBinding PlanBinding(FileTreeMoveOperation row) => new(row.Id, row.ActorSubject, row.LoginBinding, ReadPlan(row), row.ReadyExpiresAtUtc ?? throw new ArgumentException("FILES_TREE_PLAN_REQUIRED"));
    public static bool HasValidBinding(FileTreeMoveOperation row, IFileTreeMoveProtector protector)
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
