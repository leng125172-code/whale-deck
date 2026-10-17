using System.Security.Cryptography;
using System.Text;
using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Services;

public static class FileTreeMoveValidation
{
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
}
