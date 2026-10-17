using System.Text.Json;
using Grpc.Core;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

/// <summary>Explicit directory-copy capability, distinct from regular-file mutation plans.</summary>
public sealed class FileTreeCopyGrpcService(FileTransferRunner runner) : FileTreeCopyService.FileTreeCopyServiceBase
{
    public override async Task<FileTreeCopyPlan> Plan(FileTreeCopyPlanRequest request, ServerCallContext context)
    {
        var input = Input(() => { FileOperationPolicy.Actor(request.Context); return FileTreeCopyPolicy.CommandJson(request.Command); });
        using var response = await runner.OnceAsync("tree-copy-plan", input, context.CancellationToken);
        return FileTreeCopyPolicy.Plan(response.RootElement, request.Command);
    }
    public override Task<FileTreeCopyReceipt> Apply(FileTreeCopyApplyRequest request, ServerCallContext context) => Write(request, context, false);
    public override Task<FileTreeCopyReceipt> Discard(FileTreeCopyApplyRequest request, ServerCallContext context) => Write(request, context, true);
    private async Task<FileTreeCopyReceipt> Write(FileTreeCopyApplyRequest request, ServerCallContext context, bool discard)
    {
        var plan = Input(() => { FileOperationPolicy.Actor(request.Context); FileTransferPolicy.Identity(request.OperationId); FileTransferPolicy.Hash(request.Owner);
            return FileTreeCopyPolicy.PlanInput(request.Plan); });
        using var response = await runner.OnceAsync(discard ? "tree-copy-discard" : "tree-copy-apply",
            JsonSerializer.Serialize(new { operationId = request.OperationId, owner = request.Owner, plan }), context.CancellationToken);
        return FileTreeCopyPolicy.Receipt(response.RootElement, request.OperationId, request.Plan);
    }
    public override async Task<FileTreeCopyReceipt> Inspect(FileTreeCopyLookup request, ServerCallContext context)
    {
        var input = Input(() => { FileTransferPolicy.Identity(request.OperationId); FileTransferPolicy.Hash(request.Owner);
            return JsonSerializer.Serialize(new { operationId = request.OperationId, owner = request.Owner }); });
        using var response = await runner.OnceAsync("tree-copy-status", input, context.CancellationToken);
        return FileTreeCopyPolicy.Receipt(response.RootElement, request.OperationId);
    }
    private static T Input<T>(Func<T> validate)
    {
        try { return validate(); }
        catch (ArgumentException error)
        {
            var code = error.Message;
            if (code.Length > 80 || !code.StartsWith("FILES_", StringComparison.Ordinal) || code.Any(character => !(char.IsAsciiLetterUpper(character) || character == '_')))
                code = "FILES_TREE_INPUT_INVALID";
            throw new RpcException(new(StatusCode.InvalidArgument, code));
        }
    }
}

internal static class FileTreeCopyPolicy
{
    public const long MaximumBytes = 16L * 1024 * 1024 * 1024;
    public const uint MaximumEntries = 10000;
    private static readonly JsonSerializerOptions InputOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly HashSet<string> CommandFields = ["source", "target"];
    private static readonly HashSet<string> SourceFields = ["rootId", "path", "expectedVersion"];
    private static readonly HashSet<string> TargetFields = ["rootId", "path"];
    private static readonly HashSet<string> PlanFields = ["command", "manifestSha256", "entryCount", "totalBytes", "targetParent", "sourcePolicyHash", "targetPolicyHash", "fingerprint"];
    private static readonly HashSet<string> ReceiptFields = ["operationId", "state", "fingerprint", "entryCount", "totalBytes", "version", "partialRetained"];
    internal sealed record RootSource(string RootId, string Path, string ExpectedVersion);
    internal sealed record RootLocation(string RootId, string Path);
    internal sealed record RootCommand(RootSource Source, RootLocation Target);

    private static RootCommand Command(FileTreeCopyCommand? command)
    {
        if (command is null) throw new ArgumentException("FILES_TREE_COMMAND_REQUIRED");
        FileTransferPolicy.Location(command.Source); FileTransferPolicy.Location(command.Target); FileTransferPolicy.Hash(command.ExpectedVersion);
        if (command.Source.RootId == command.Target.RootId && (command.Source.Path == command.Target.Path ||
            command.Source.Path.StartsWith(command.Target.Path + "/", StringComparison.Ordinal) || command.Target.Path.StartsWith(command.Source.Path + "/", StringComparison.Ordinal)))
            throw new ArgumentException("FILES_TREE_OVERLAP");
        return new(new(command.Source.RootId, command.Source.Path, command.ExpectedVersion), new(command.Target.RootId, command.Target.Path));
    }
    public static string CommandJson(FileTreeCopyCommand? command) => JsonSerializer.Serialize(Command(command), InputOptions);
    public static JsonElement PlanInput(FileTreeCopyPlan? plan)
    {
        if (plan is null) throw new ArgumentException("FILES_TREE_PLAN_REQUIRED");
        var command = Command(plan.Command);
        FileTransferPolicy.Hash(plan.ManifestSha256); FileTransferPolicy.Hash(plan.SourcePolicyHash);
        FileTransferPolicy.Hash(plan.TargetPolicyHash); FileTransferPolicy.Hash(plan.Fingerprint);
        if (plan.EntryCount is < 1 or > MaximumEntries || plan.TotalBytes is < 0 or > MaximumBytes || plan.TargetParent is not { Inode: > 0 })
            throw new ArgumentException("FILES_TREE_PLAN_INVALID");
        return JsonSerializer.SerializeToElement(new { command, manifestSha256 = plan.ManifestSha256,
            entryCount = plan.EntryCount, totalBytes = plan.TotalBytes, targetParent = new[] { plan.TargetParent.Device, plan.TargetParent.Inode },
            sourcePolicyHash = plan.SourcePolicyHash, targetPolicyHash = plan.TargetPolicyHash, fingerprint = plan.Fingerprint }, InputOptions);
    }
    public static FileTreeCopyPlan Plan(JsonElement value, FileTreeCopyCommand expected)
    {
        Fields(value, PlanFields); var command = value.GetProperty("command"); Fields(command, CommandFields);
        var source = command.GetProperty("source"); var target = command.GetProperty("target"); Fields(source, SourceFields); Fields(target, TargetFields);
        var parent = value.GetProperty("targetParent");
        if (parent.ValueKind != JsonValueKind.Array || parent.GetArrayLength() != 2) throw new InvalidOperationException("FILES_TREE_PARENT_INVALID");
        var result = new FileTreeCopyPlan
        {
            Command = new() { Source = new() { RootId = Text(source, "rootId"), Path = Text(source, "path") }, ExpectedVersion = Text(source, "expectedVersion"),
                Target = new() { RootId = Text(target, "rootId"), Path = Text(target, "path") } },
            ManifestSha256 = Text(value, "manifestSha256"), EntryCount = value.GetProperty("entryCount").GetUInt32(), TotalBytes = value.GetProperty("totalBytes").GetInt64(),
            TargetParent = new() { Device = parent[0].GetUInt64(), Inode = parent[1].GetUInt64() },
            SourcePolicyHash = Text(value, "sourcePolicyHash"), TargetPolicyHash = Text(value, "targetPolicyHash"), Fingerprint = Text(value, "fingerprint")
        };
        if (!result.Command.Equals(expected)) throw new InvalidOperationException("FILES_PLAN_TARGET_MISMATCH");
        _ = PlanInput(result); return result;
    }
    public static FileTreeCopyReceipt Receipt(JsonElement value, string operation, FileTreeCopyPlan? plan = null)
    {
        Fields(value, ReceiptFields);
        var result = new FileTreeCopyReceipt { OperationId = Text(value, "operationId"), State = Text(value, "state"), Fingerprint = Text(value, "fingerprint"),
            EntryCount = value.GetProperty("entryCount").GetUInt32(), TotalBytes = value.GetProperty("totalBytes").GetInt64(), Version = Text(value, "version"),
            PartialRetained = value.GetProperty("partialRetained").GetBoolean() };
        FileTransferPolicy.Identity(result.OperationId); FileTransferPolicy.Hash(result.Fingerprint);
        if (result.OperationId != operation || result.State is not ("Completed" or "Aborted" or "Unknown") || result.EntryCount is < 1 or > MaximumEntries ||
            result.TotalBytes is < 0 or > MaximumBytes || result.State == "Completed" && result.PartialRetained || result.State != "Completed" && result.Version.Length != 0)
            throw new InvalidOperationException("FILES_TREE_RECEIPT_INVALID");
        if (result.State == "Completed") FileTransferPolicy.Hash(result.Version);
        if (plan is not null && (result.Fingerprint != plan.Fingerprint || result.EntryCount != plan.EntryCount || result.TotalBytes != plan.TotalBytes))
            throw new InvalidOperationException("FILES_RECEIPT_TARGET_MISMATCH");
        return result;
    }
    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString() ?? throw new InvalidOperationException("FILES_OUTPUT_INVALID");
    private static void Fields(JsonElement value, HashSet<string> fields)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("FILES_OUTPUT_INVALID");
        var names = value.EnumerateObject().Select(item => item.Name).ToArray();
        if (names.Length != fields.Count || names.Distinct(StringComparer.Ordinal).Count() != fields.Count || !names.All(fields.Contains))
            throw new InvalidOperationException("FILES_OUTPUT_INVALID");
    }
}
