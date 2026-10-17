using System.Text.Json;
using Grpc.Core;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

/// <summary>Distinct move capability: verified destination plus retained source leaves, never arbitrary recursive deletion.</summary>
public sealed class FileTreeMoveGrpcService(FileTransferRunner runner) : FileTreeMoveService.FileTreeMoveServiceBase
{
    public override async Task<FileTreeMovePlan> Plan(FileTreeMovePlanRequest request, ServerCallContext context)
    {
        var input = Input(() => { FileOperationPolicy.Actor(request.Context); return FileTreeCopyPolicy.CommandJson(request.Command); });
        using var response = await runner.OnceAsync("tree-move-plan", input, context.CancellationToken);
        return FileTreeMovePolicy.Plan(response.RootElement, request.Command);
    }
    public override Task<FileTreeMoveReceipt> Apply(FileTreeMoveApplyRequest request, ServerCallContext context) => Write(request, context, false);
    public override Task<FileTreeMoveReceipt> Discard(FileTreeMoveApplyRequest request, ServerCallContext context) => Write(request, context, true);
    private async Task<FileTreeMoveReceipt> Write(FileTreeMoveApplyRequest request, ServerCallContext context, bool discard)
    {
        var plan = Input(() => { FileOperationPolicy.Actor(request.Context); FileTransferPolicy.Identity(request.OperationId); FileTransferPolicy.Hash(request.Owner);
            return FileTreeMovePolicy.PlanInput(request.Plan); });
        using var response = await runner.OnceAsync(discard ? "tree-move-discard" : "tree-move-apply",
            JsonSerializer.Serialize(new { operationId = request.OperationId, owner = request.Owner, plan }), context.CancellationToken);
        return FileTreeMovePolicy.Receipt(response.RootElement, request.OperationId, request.Plan);
    }
    public override async Task<FileTreeMoveReceipt> Inspect(FileTreeMoveLookup request, ServerCallContext context)
    {
        var input = Input(() => { FileTransferPolicy.Identity(request.OperationId); FileTransferPolicy.Hash(request.Owner);
            return JsonSerializer.Serialize(new { operationId = request.OperationId, owner = request.Owner }); });
        using var response = await runner.OnceAsync("tree-move-status", input, context.CancellationToken);
        return FileTreeMovePolicy.Receipt(response.RootElement, request.OperationId);
    }
    private static T Input<T>(Func<T> validate)
    {
        try { return validate(); }
        catch (ArgumentException error)
        {
            var code = error.Message;
            if (code.Length > 80 || !code.StartsWith("FILES_", StringComparison.Ordinal) || code.Any(character => !(char.IsAsciiLetterUpper(character) || character == '_')))
                code = "FILES_TREE_MOVE_INPUT_INVALID";
            throw new RpcException(new(StatusCode.InvalidArgument, code));
        }
    }
}

internal static class FileTreeMovePolicy
{
    private static readonly HashSet<string> PlanFields = ["copyPlan", "sourceParent", "fingerprint"];
    private static readonly HashSet<string> ReceiptFields = ["operationId", "state", "fingerprint", "entryCount", "totalBytes", "version", "destinationPublished", "sourceRemoved", "sourceRetained", "partialRetained"];

    public static JsonElement PlanInput(FileTreeMovePlan? plan)
    {
        if (plan is null) throw new ArgumentException("FILES_TREE_MOVE_PLAN_REQUIRED");
        var copyPlan = FileTreeCopyPolicy.PlanInput(plan.CopyPlan); FileTransferPolicy.Hash(plan.Fingerprint);
        if (plan.SourceParent is not { Inode: > 0 }) throw new ArgumentException("FILES_TREE_MOVE_PLAN_INVALID");
        return JsonSerializer.SerializeToElement(new { copyPlan, sourceParent = new[] { plan.SourceParent.Device, plan.SourceParent.Inode }, fingerprint = plan.Fingerprint });
    }
    public static FileTreeMovePlan Plan(JsonElement value, FileTreeCopyCommand expected)
    {
        Fields(value, PlanFields); var parent = value.GetProperty("sourceParent");
        if (parent.ValueKind != JsonValueKind.Array || parent.GetArrayLength() != 2) throw new InvalidOperationException("FILES_TREE_PARENT_INVALID");
        var result = new FileTreeMovePlan { CopyPlan = FileTreeCopyPolicy.Plan(value.GetProperty("copyPlan"), expected),
            SourceParent = new() { Device = parent[0].GetUInt64(), Inode = parent[1].GetUInt64() }, Fingerprint = Text(value, "fingerprint") };
        _ = PlanInput(result); return result;
    }
    public static FileTreeMoveReceipt Receipt(JsonElement value, string operation, FileTreeMovePlan? plan = null)
    {
        Fields(value, ReceiptFields);
        var result = new FileTreeMoveReceipt { OperationId = Text(value, "operationId"), State = Text(value, "state"), Fingerprint = Text(value, "fingerprint"),
            EntryCount = value.GetProperty("entryCount").GetUInt32(), TotalBytes = value.GetProperty("totalBytes").GetInt64(), Version = Text(value, "version"),
            DestinationPublished = value.GetProperty("destinationPublished").GetBoolean(), SourceRemoved = value.GetProperty("sourceRemoved").GetBoolean(),
            SourceRetained = value.GetProperty("sourceRetained").GetBoolean(), PartialRetained = value.GetProperty("partialRetained").GetBoolean() };
        FileTransferPolicy.Identity(result.OperationId); FileTransferPolicy.Hash(result.Fingerprint);
        var completed = result.State == "Completed";
        if (result.OperationId != operation || result.State is not ("Completed" or "Aborted" or "Unknown") || result.EntryCount is < 1 or > FileTreeCopyPolicy.MaximumEntries ||
            result.TotalBytes is < 0 or > FileTreeCopyPolicy.MaximumBytes ||
            completed && (!result.DestinationPublished || !result.SourceRemoved || !result.SourceRetained || result.PartialRetained) ||
            !completed && (result.Version.Length != 0 || result.SourceRemoved || result.SourceRetained) ||
            result.State == "Aborted" && result.DestinationPublished || !completed && result.DestinationPublished && !result.PartialRetained)
            throw new InvalidOperationException("FILES_TREE_MOVE_RECEIPT_INVALID");
        if (completed) FileTransferPolicy.Hash(result.Version);
        if (plan is not null && (result.Fingerprint != plan.Fingerprint || result.EntryCount != plan.CopyPlan.EntryCount || result.TotalBytes != plan.CopyPlan.TotalBytes))
            throw new InvalidOperationException("FILES_RECEIPT_TARGET_MISMATCH");
        return result;
    }
    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString() ?? throw new InvalidOperationException("FILES_OUTPUT_INVALID");
    private static void Fields(JsonElement value, HashSet<string> fields)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("FILES_OUTPUT_INVALID");
        var names = value.EnumerateObject().Select(item => item.Name).ToArray();
        if (names.Length != fields.Count || names.Distinct(StringComparer.Ordinal).Count() != fields.Count || !names.All(fields.Contains)) throw new InvalidOperationException("FILES_OUTPUT_INVALID");
    }
}
