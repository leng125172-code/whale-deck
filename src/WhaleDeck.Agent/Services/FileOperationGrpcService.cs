using System.Text.Json;
using Grpc.Core;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

/// <summary>Explicit registered-file mutation protocol. Never an arbitrary shell/path RPC.</summary>
public sealed class FileOperationGrpcService(FileTransferRunner runner) : FileOperationService.FileOperationServiceBase
{
    public override async Task<FileOperationPlan> Plan(FileOperationPlanRequest request, ServerCallContext context)
    {
        FileOperationPolicy.Actor(request.Context);
        var command = FileOperationPolicy.CommandJson(request.Command);
        using var result = await runner.OnceAsync("operation-plan", command, context.CancellationToken);
        return FileOperationPolicy.Plan(result.RootElement, request.Command);
    }
    public override async Task<FileOperationReceipt> Apply(FileOperationApplyRequest request, ServerCallContext context)
    {
        FileOperationPolicy.Actor(request.Context);
        FileTransferPolicy.Identity(request.OperationId); FileTransferPolicy.Hash(request.Owner);
        var plan = FileOperationPolicy.PlanInput(request.Plan);
        using var result = await runner.OnceAsync("operation-apply", JsonSerializer.Serialize(new { operationId = request.OperationId, owner = request.Owner, plan }), context.CancellationToken);
        return FileOperationPolicy.Receipt(result.RootElement, request.OperationId, request.Plan);
    }
    public override async Task<FileOperationReceipt> Inspect(FileOperationLookup request, ServerCallContext context)
    {
        FileTransferPolicy.Identity(request.OperationId); FileTransferPolicy.Hash(request.Owner);
        using var result = await runner.OnceAsync("operation-status", JsonSerializer.Serialize(new { operationId = request.OperationId, owner = request.Owner }), context.CancellationToken);
        return FileOperationPolicy.Receipt(result.RootElement, request.OperationId);
    }
    public override async Task<FileOperationReceipt> Discard(FileOperationApplyRequest request, ServerCallContext context)
    {
        FileOperationPolicy.Actor(request.Context);
        FileTransferPolicy.Identity(request.OperationId); FileTransferPolicy.Hash(request.Owner);
        var plan = FileOperationPolicy.PlanInput(request.Plan);
        using var result = await runner.OnceAsync("operation-discard", JsonSerializer.Serialize(new { operationId = request.OperationId, owner = request.Owner, plan }), context.CancellationToken);
        return FileOperationPolicy.Receipt(result.RootElement, request.OperationId, request.Plan);
    }
}

internal static class FileOperationPolicy
{
    internal sealed record OperationCommand(string Kind, string SourceRootId, string SourcePath, string TargetRootId, string TargetPath, string ExpectedVersion, string Mode);
    internal sealed record OperationPlan(OperationCommand Command, object[]? SourceIdentity, ulong[]? SourceParent, ulong[]? TargetParent, long SizeBytes, string Sha256, string Fingerprint);
    private static readonly JsonSerializerOptions InputOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly HashSet<string> PlanFields = ["command", "sourceIdentity", "sourceParent", "targetParent", "sizeBytes", "sha256", "fingerprint"];
    private static readonly HashSet<string> CommandFields = ["kind", "sourceRootId", "sourcePath", "targetRootId", "targetPath", "expectedVersion", "mode"];
    private static readonly HashSet<string> ReceiptFields = ["operationId", "kind", "state", "fingerprint", "version", "sizeBytes", "sha256", "sourceRetained", "bindingCurrent"];
    public static string CommandJson(FileOperationCommand? value) => JsonSerializer.Serialize(Command(value), InputOptions);

    public static void Actor(RequestContext? context)
    {
        if (context is null || string.IsNullOrWhiteSpace(context.ActorSubject) || context.ActorSubject.Length > 256 || context.ActorSubject.Any(char.IsControl))
            throw new ArgumentException("FILES_ACTOR_REQUIRED");
    }
    public static OperationCommand Command(FileOperationCommand? value)
    {
        if (value is null) throw new ArgumentException("FILES_OPERATION_REQUIRED");
        var kind = Kind(value.Kind);
        var source = value.Source; var target = value.Target;
        if (value.Kind == FileOperationKind.CreateDirectory)
        {
            if (source is not null || value.ExpectedVersion != "ABSENT") throw new ArgumentException("FILES_OPERATION_INVALID");
        }
        else { FileTransferPolicy.Location(source); FileTransferPolicy.Hash(value.ExpectedVersion); }
        if (value.Kind == FileOperationKind.SetPermissions)
        {
            if (target is not null) throw new ArgumentException("FILES_OPERATION_INVALID");
        }
        else FileTransferPolicy.Location(target);
        if (value.Kind is FileOperationKind.CreateDirectory or FileOperationKind.SetPermissions)
        {
            if (value.Mode.Length != 4 || value.Mode[0] != '0' || value.Mode.Skip(1).Any(c => c is < '0' or > '7')) throw new ArgumentException("FILES_MODE_INVALID");
        }
        else if (value.Mode.Length != 0) throw new ArgumentException("FILES_MODE_INVALID");
        return new(kind, source?.RootId ?? "", source?.Path ?? "", target?.RootId ?? "", target?.Path ?? "", value.ExpectedVersion, value.Mode);
    }
    public static string Kind(FileOperationKind kind) => kind switch
    {
        FileOperationKind.CreateDirectory => "mkdir", FileOperationKind.CopyFile => "copy", FileOperationKind.MoveFile => "move",
        FileOperationKind.SetPermissions => "chmod", _ => throw new ArgumentException("FILES_KIND_INVALID")
    };
    private static FileOperationKind Kind(string kind) => kind switch
    {
        "mkdir" => FileOperationKind.CreateDirectory, "copy" => FileOperationKind.CopyFile, "move" => FileOperationKind.MoveFile,
        "chmod" => FileOperationKind.SetPermissions, _ => throw new InvalidOperationException("FILES_KIND_INVALID")
    };
    public static JsonElement PlanInput(FileOperationPlan? value)
    {
        if (value is null) throw new ArgumentException("FILES_PLAN_REQUIRED");
        var command = Command(value.Command); FileTransferPolicy.Hash(value.Fingerprint);
        if (value.SizeBytes is < 0 or > FileTransferPolicy.MaximumBytes) throw new ArgumentException("FILES_SIZE_LIMIT");
        if (value.Command.Kind == FileOperationKind.CreateDirectory)
        {
            if (value.SourceIdentity is not null || value.SourceParent is not null || value.Sha256.Length != 0 || value.SizeBytes != 0) throw new ArgumentException("FILES_PLAN_INVALID");
        }
        else
        {
            FileTransferPolicy.Hash(value.Sha256);
            var identity = value.SourceIdentity ?? throw new ArgumentException("FILES_PLAN_INVALID");
            if (identity.LinkCount != 1 || identity.SizeBytes != value.SizeBytes || identity.Inode == 0 || identity.Mode > ushort.MaxValue || (identity.Mode & 0xF000) != 0x8000)
                throw new ArgumentException("FILES_IDENTITY_INVALID");
            Directory(value.SourceParent);
        }
        if (value.Command.Kind == FileOperationKind.SetPermissions)
        { if (value.TargetParent is not null) throw new ArgumentException("FILES_PLAN_INVALID"); }
        else Directory(value.TargetParent);
        var source = value.SourceIdentity;
        var plan = new OperationPlan(command, source is null ? null : [source.Device, source.Inode, source.Mode, source.OwnerUid, source.OwnerGid, source.LinkCount, source.SizeBytes, source.ModifiedAtUnixNs],
            value.SourceParent is null ? null : [value.SourceParent.Device, value.SourceParent.Inode], value.TargetParent is null ? null : [value.TargetParent.Device, value.TargetParent.Inode],
            value.SizeBytes, value.Sha256, value.Fingerprint);
        return JsonSerializer.SerializeToElement(plan, InputOptions);
    }
    private static void Directory(FileDirectoryIdentity? value)
    { if (value is null || value.Inode == 0) throw new ArgumentException("FILES_PARENT_INVALID"); }

    public static FileOperationPlan Plan(JsonElement value, FileOperationCommand expected)
    {
        Fields(value, PlanFields);
        var command = value.GetProperty("command");
        Fields(command, CommandFields);
        var result = new FileOperationPlan { Command = new() { Kind = Kind(Text(command, "kind")),
            ExpectedVersion = Text(command, "expectedVersion"), Mode = Text(command, "mode") },
            SizeBytes = value.GetProperty("sizeBytes").GetInt64(), Sha256 = Text(value, "sha256"), Fingerprint = Text(value, "fingerprint") };
        if (result.Command.Kind == FileOperationKind.CreateDirectory && (Text(command, "sourceRootId").Length != 0 || Text(command, "sourcePath").Length != 0) ||
            result.Command.Kind == FileOperationKind.SetPermissions && (Text(command, "targetRootId").Length != 0 || Text(command, "targetPath").Length != 0))
            throw new InvalidOperationException("FILES_PLAN_TARGET_MISMATCH");
        if (result.Command.Kind == FileOperationKind.CreateDirectory && (value.GetProperty("sourceIdentity").ValueKind != JsonValueKind.Null || value.GetProperty("sourceParent").ValueKind != JsonValueKind.Null) ||
            result.Command.Kind == FileOperationKind.SetPermissions && value.GetProperty("targetParent").ValueKind != JsonValueKind.Null)
            throw new InvalidOperationException("FILES_PLAN_INVALID");
        if (result.Command.Kind != FileOperationKind.CreateDirectory)
        {
            result.Command.Source = new() { RootId = Text(command, "sourceRootId"), Path = Text(command, "sourcePath") };
            var identity = value.GetProperty("sourceIdentity");
            if (identity.GetArrayLength() != 8) throw new InvalidOperationException("FILES_IDENTITY_INVALID");
            result.SourceIdentity = new() { Device = identity[0].GetUInt64(), Inode = identity[1].GetUInt64(), Mode = identity[2].GetUInt32(), OwnerUid = identity[3].GetUInt32(),
                OwnerGid = identity[4].GetUInt32(), LinkCount = identity[5].GetUInt64(), SizeBytes = identity[6].GetInt64(), ModifiedAtUnixNs = identity[7].GetInt64() };
            result.SourceParent = Parent(value.GetProperty("sourceParent"));
        }
        if (result.Command.Kind != FileOperationKind.SetPermissions)
        {
            result.Command.Target = new() { RootId = Text(command, "targetRootId"), Path = Text(command, "targetPath") };
            result.TargetParent = Parent(value.GetProperty("targetParent"));
        }
        if (!result.Command.Equals(expected)) throw new InvalidOperationException("FILES_PLAN_TARGET_MISMATCH");
        _ = PlanInput(result); return result;
    }
    private static FileDirectoryIdentity Parent(JsonElement value)
    {
        if (value.GetArrayLength() != 2) throw new InvalidOperationException("FILES_PARENT_INVALID");
        return new() { Device = value[0].GetUInt64(), Inode = value[1].GetUInt64() };
    }
    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString() ?? throw new InvalidOperationException("FILES_OUTPUT_INVALID");
    private static void Fields(JsonElement value, HashSet<string> fields)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("FILES_OUTPUT_INVALID");
        var names = value.EnumerateObject().Select(item => item.Name).ToArray();
        if (names.Length != fields.Count || names.Distinct(StringComparer.Ordinal).Count() != fields.Count || !names.All(fields.Contains)) throw new InvalidOperationException("FILES_OUTPUT_INVALID");
    }
    public static FileOperationReceipt Receipt(JsonElement value, string expected, FileOperationPlan? plan = null)
    {
        Fields(value, ReceiptFields);
        var result = new FileOperationReceipt { OperationId = Text(value, "operationId"), Kind = Kind(Text(value, "kind")),
            State = Text(value, "state"), Fingerprint = Text(value, "fingerprint"), Version = Text(value, "version"),
            SizeBytes = value.GetProperty("sizeBytes").GetInt64(), Sha256 = Text(value, "sha256"), SourceRetained = value.GetProperty("sourceRetained").GetBoolean(),
            BindingCurrent = value.GetProperty("bindingCurrent").GetBoolean() };
        if (result.OperationId != expected || result.State is not ("Completed" or "Aborted" or "Unknown") || result.SizeBytes is < 0 or > FileTransferPolicy.MaximumBytes)
            throw new InvalidOperationException("FILES_RECEIPT_INVALID");
        FileTransferPolicy.Hash(result.Fingerprint);
        if (result.Kind != FileOperationKind.CreateDirectory) FileTransferPolicy.Hash(result.Sha256);
        else if (result.SizeBytes != 0 || result.Sha256.Length != 0) throw new InvalidOperationException("FILES_RECEIPT_INVALID");
        if (result.State == "Completed") FileTransferPolicy.Hash(result.Version);
        else if (result.BindingCurrent || result.SourceRetained || result.Version.Length != 0) throw new InvalidOperationException("FILES_RECEIPT_INVALID");
        if (result.SourceRetained && result.Kind != FileOperationKind.MoveFile || plan is not null && (result.Kind != plan.Command.Kind || result.Fingerprint != plan.Fingerprint || result.SizeBytes != plan.SizeBytes || result.Sha256 != plan.Sha256))
            throw new InvalidOperationException("FILES_RECEIPT_TARGET_MISMATCH");
        return result;
    }
}
