using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using Grpc.Core;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

/// <summary>Bounded recursive ordinary-mode protocol. Business authorization and permanent intents belong to Application.</summary>
public sealed class ContainerRecursivePermissionGrpcService(FileTransferRunner runner) : ContainerRecursivePermissionService.ContainerRecursivePermissionServiceBase
{
    public override async Task<ContainerRecursivePermissionPlan> Plan(ContainerRecursivePermissionPlanRequest request, ServerCallContext context)
    {
        ContainerRecursivePermissionPolicy.Command(request.Command); ContainerRecursivePermissionPolicy.Context(request.Context, planning: true);
        ContainerRecursivePermissionPolicy.RequireBinding(request, context);
        using var result = await runner.ContainerRecursivePermissionOnceAsync("plan", JsonSerializer.Serialize(ContainerRecursivePermissionPolicy.CommandInput(request.Command)), context.CancellationToken);
        return ContainerRecursivePermissionPolicy.PlanOutput(result.RootElement, request.Command);
    }
    public override Task<ContainerRecursivePermissionReceipt> Apply(ContainerRecursivePermissionRequest request, ServerCallContext context) => Run("apply", request, context);
    public override Task<ContainerRecursivePermissionReceipt> Inspect(ContainerRecursivePermissionRequest request, ServerCallContext context) => Run("inspect", request, context);
    public override Task<ContainerRecursivePermissionReceipt> Discard(ContainerRecursivePermissionRequest request, ServerCallContext context) => Run("discard", request, context);
    private async Task<ContainerRecursivePermissionReceipt> Run(string action, ContainerRecursivePermissionRequest request, ServerCallContext context)
    {
        ContainerRecursivePermissionPolicy.Request(request); ContainerRecursivePermissionPolicy.RequireBinding(request, context);
        using var result = await runner.ContainerRecursivePermissionOnceAsync(action, JsonSerializer.Serialize(new
            { operationId = request.OperationId, owner = request.Owner, plan = ContainerRecursivePermissionPolicy.PlanInput(request.Plan) }), context.CancellationToken);
        return ContainerRecursivePermissionPolicy.Receipt(result.RootElement, request);
    }
}

internal static class ContainerRecursivePermissionPolicy
{
    public static void RequireBinding(IMessage request, ServerCallContext context)
    {
        var expected = context.RequestHeaders.GetValue(ContainerRecursivePermissionBinding.Header);
        var actual = ContainerRecursivePermissionBinding.Hash(request);
        if (expected is not { Length: 64 } || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(actual), Encoding.ASCII.GetBytes(expected)))
            throw new RpcException(new(StatusCode.PermissionDenied, "CONTAINER_RECURSIVE_PERMISSION_BINDING_INVALID"));
    }
    public static void Context(RequestContext? context, bool planning = false)
    {
        if (context is null || context.Capability.Length != 0 || string.IsNullOrWhiteSpace(context.ActorSubject) || context.ActorSubject.Length > 256 ||
            context.ActorSubject.Any(char.IsControl) || string.IsNullOrWhiteSpace(context.TraceId) || context.TraceId.Length > 128 || context.TraceId.Any(char.IsControl))
            throw new ArgumentException("CONTAINER_RECURSIVE_PERMISSION_CONTEXT_INVALID");
        if (planning)
        {
            if (context.JobId.Length != 0 || context.IdempotencyKey.Length != 0) throw new ArgumentException("CONTAINER_RECURSIVE_PERMISSION_CONTEXT_INVALID");
        }
        else
        { FileTransferPolicy.Identity(context.JobId); FileTransferPolicy.Identity(context.IdempotencyKey); }
    }
    public static void Command(ContainerRecursivePermissionCommand? command)
    {
        if (command is null) throw new ArgumentException("CONTAINER_RECURSIVE_PERMISSION_COMMAND_REQUIRED");
        ContainerFilePolicy.Location(command.Location); FileTransferPolicy.Hash(command.ExpectedVersion);
        foreach (var mode in new[] { command.FileMode, command.DirectoryMode })
            if (mode.Length != 4 || mode[0] != '0' || mode.Skip(1).Any(c => c is < '0' or > '7'))
                throw new ArgumentException("CONTAINER_RECURSIVE_PERMISSION_MODE_INVALID");
    }
    public static object CommandInput(ContainerRecursivePermissionCommand command) => new
        { containerId = command.Location.ContainerId, instanceId = command.Location.InstanceId, path = command.Location.Path, fileMode = command.FileMode, directoryMode = command.DirectoryMode, expectedVersion = command.ExpectedVersion };
    public static void Plan(ContainerRecursivePermissionPlan? plan)
    {
        if (plan is null || plan.SchemaVersion != 1 || plan.ParentIdentity is not { Inode: > 0 } || plan.OwnerUid == uint.MaxValue || plan.OwnerGid == uint.MaxValue)
            throw new ArgumentException("CONTAINER_RECURSIVE_PERMISSION_PLAN_INVALID");
        Command(plan.Command);
        if (plan.RootIdentity is not { Inode: > 0, LinkCount: > 0 } source || source.Mode > ushort.MaxValue || (source.Mode & 0xE00) != 0 ||
            (source.Mode & 0xF000) != 0x4000U || source.OwnerUid == uint.MaxValue || source.OwnerGid == uint.MaxValue || source.SizeBytes < 0 ||
            plan.EntryCount is < 1 or > 10000 || plan.FileCount >= plan.EntryCount || plan.DirectoryCount != plan.EntryCount - plan.FileCount || plan.ChangeCount > plan.EntryCount)
            throw new ArgumentException("CONTAINER_RECURSIVE_PERMISSION_PLAN_INVALID");
        FileTransferPolicy.Hash(plan.ManifestDigest); FileTransferPolicy.Hash(plan.TargetProof); FileTransferPolicy.Hash(plan.ParentVersion); FileTransferPolicy.Hash(plan.Fingerprint);
    }
    public static object PlanInput(ContainerRecursivePermissionPlan plan)
    {
        Plan(plan);
        return new { schemaVersion = plan.SchemaVersion, command = CommandInput(plan.Command), targetProof = plan.TargetProof,
            parentIdentity = new[] { plan.ParentIdentity.Device, plan.ParentIdentity.Inode }, parentVersion = plan.ParentVersion,
            uid = plan.OwnerUid, gid = plan.OwnerGid, fingerprint = plan.Fingerprint,
            rootIdentity = Identity(plan), manifestDigest = plan.ManifestDigest, entryCount = plan.EntryCount, fileCount = plan.FileCount, directoryCount = plan.DirectoryCount, changeCount = plan.ChangeCount };
    }
    private static object[] Identity(ContainerRecursivePermissionPlan plan)
    {
        var value = plan.RootIdentity;
        return [value.Device, value.Inode, value.Mode, value.OwnerUid, value.OwnerGid, value.LinkCount,
            value.SizeBytes, value.ModifiedAtUnixNs, plan.RootChangedAtUnixNs];
    }
    public static void Request(ContainerRecursivePermissionRequest request)
    {
        FileTransferPolicy.Identity(request.OperationId); FileTransferPolicy.Hash(request.Owner); Plan(request.Plan); Context(request.Context);
        if (request.Context.JobId != request.OperationId) throw new ArgumentException("CONTAINER_RECURSIVE_PERMISSION_CONTEXT_INVALID");
    }
    public static ContainerRecursivePermissionPlan PlanOutput(JsonElement value, ContainerRecursivePermissionCommand expected)
    {
        Fields(value, "schemaVersion", "command", "targetProof", "parentIdentity", "parentVersion", "uid", "gid", "fingerprint", "rootIdentity", "manifestDigest", "entryCount", "fileCount", "directoryCount", "changeCount");
        var command = value.GetProperty("command"); Fields(command, "containerId", "instanceId", "path", "fileMode", "directoryMode", "expectedVersion");
        var parent = value.GetProperty("parentIdentity"); var source = value.GetProperty("rootIdentity");
        if (parent.ValueKind != JsonValueKind.Array || parent.GetArrayLength() != 2 || source.ValueKind != JsonValueKind.Array || source.GetArrayLength() != 9) throw Invalid();
        var result = new ContainerRecursivePermissionPlan { SchemaVersion = value.GetProperty("schemaVersion").GetUInt32(),
            Command = new() { Location = new() { ContainerId = Text(command, "containerId"), InstanceId = Text(command, "instanceId"), Path = Text(command, "path") },
                FileMode = Text(command, "fileMode"), DirectoryMode = Text(command, "directoryMode"), ExpectedVersion = Text(command, "expectedVersion") },
            TargetProof = Text(value, "targetProof"), ParentIdentity = new() { Device = parent[0].GetUInt64(), Inode = parent[1].GetUInt64() },
            ParentVersion = Text(value, "parentVersion"), OwnerUid = value.GetProperty("uid").GetUInt32(), OwnerGid = value.GetProperty("gid").GetUInt32(), Fingerprint = Text(value, "fingerprint"),
            RootIdentity = new() { Device = source[0].GetUInt64(), Inode = source[1].GetUInt64(), Mode = source[2].GetUInt32(),
                OwnerUid = source[3].GetUInt32(), OwnerGid = source[4].GetUInt32(), LinkCount = source[5].GetUInt64(),
                SizeBytes = source[6].GetInt64(), ModifiedAtUnixNs = source[7].GetInt64() }, RootChangedAtUnixNs = source[8].GetInt64(),
            ManifestDigest = Text(value, "manifestDigest"), EntryCount = value.GetProperty("entryCount").GetUInt32(), FileCount = value.GetProperty("fileCount").GetUInt32(),
            DirectoryCount = value.GetProperty("directoryCount").GetUInt32(), ChangeCount = value.GetProperty("changeCount").GetUInt32() };
        try { Plan(result); } catch (ArgumentException) { throw Invalid(); }
        if (!result.Command.Equals(expected)) throw Invalid();
        return result;
    }
    public static ContainerRecursivePermissionReceipt Receipt(JsonElement value, ContainerRecursivePermissionRequest expected)
    {
        Fields(value, "operationId", "containerId", "instanceId", "fingerprint", "targetProof", "state", "bindingCurrent", "replayed", "completedCount", "changedCount", "abortedCount", "notStartedCount", "unknownCount", "historyMayExist", "historyVerified", "historyDigest", "historyAvailable", "outcomeDigest");
        var receipt = new ContainerRecursivePermissionReceipt { OperationId = Text(value, "operationId"), ContainerId = Text(value, "containerId"), InstanceId = Text(value, "instanceId"),
            Fingerprint = Text(value, "fingerprint"), TargetProof = Text(value, "targetProof"), State = Text(value, "state"),
            BindingCurrent = value.GetProperty("bindingCurrent").GetBoolean(), Replayed = value.GetProperty("replayed").GetBoolean(),
            CompletedCount = value.GetProperty("completedCount").GetUInt32(), ChangedCount = value.GetProperty("changedCount").GetUInt32(),
            AbortedCount = value.GetProperty("abortedCount").GetUInt32(), NotStartedCount = value.GetProperty("notStartedCount").GetUInt32(), UnknownCount = value.GetProperty("unknownCount").GetUInt32(),
            HistoryMayExist = value.GetProperty("historyMayExist").GetBoolean(), HistoryVerified = value.GetProperty("historyVerified").GetBoolean(),
            HistoryAvailable = value.GetProperty("historyAvailable").GetBoolean(), HistoryDigest = Text(value, "historyDigest"), OutcomeDigest = Text(value, "outcomeDigest") };
        var n = expected.Plan.EntryCount;
        if (receipt.OperationId != expected.OperationId || receipt.ContainerId != expected.Plan.Command.Location.ContainerId ||
            receipt.InstanceId != expected.Plan.Command.Location.InstanceId || receipt.Fingerprint != expected.Plan.Fingerprint || receipt.TargetProof != expected.Plan.TargetProof ||
            receipt.HistoryVerified && !receipt.HistoryMayExist || receipt.HistoryAvailable && !receipt.HistoryVerified ||
            receipt.HistoryVerified != (receipt.HistoryDigest is { Length: 64 }) || !receipt.HistoryVerified && receipt.HistoryDigest != "" ||
            receipt.State is not ("Completed" or "Partial" or "Aborted" or "Unknown") ||
            receipt.CompletedCount > n || receipt.AbortedCount > n || receipt.NotStartedCount > n || receipt.UnknownCount > n ||
            (ulong)receipt.CompletedCount + receipt.AbortedCount + receipt.NotStartedCount + receipt.UnknownCount != n ||
            receipt.ChangedCount > receipt.CompletedCount || receipt.ChangedCount > expected.Plan.ChangeCount ||
            receipt.State == "Completed" && (receipt.CompletedCount != n || receipt.ChangedCount != expected.Plan.ChangeCount || !receipt.HistoryVerified) ||
            receipt.State == "Partial" && (receipt.ChangedCount == 0 || receipt.CompletedCount == n || receipt.UnknownCount != 0 || !receipt.HistoryVerified) ||
            receipt.State == "Unknown" && receipt.UnknownCount == 0 ||
            receipt.State == "Aborted" && (receipt.ChangedCount != 0 || receipt.UnknownCount != 0) ||
            receipt.BindingCurrent && (receipt.State != "Completed" || !receipt.HistoryAvailable)) throw Invalid();
        try
        {
            if (receipt.HistoryVerified) FileTransferPolicy.Hash(receipt.HistoryDigest);
            if (receipt.State is "Completed" or "Partial" || receipt.OutcomeDigest != "") FileTransferPolicy.Hash(receipt.OutcomeDigest);
        }
        catch (ArgumentException) { throw Invalid(); }
        return receipt;
    }
    public static RpcException Failure(string? code)
    {
        var status = code switch
        {
        "CONTAINER_RECURSIVE_PERMISSION_NOT_FOUND" => StatusCode.NotFound,
        "CONTAINER_RECURSIVE_PERMISSION_TREE_LIMIT" or "CONTAINER_RECURSIVE_PERMISSION_MANIFEST_LIMIT" or "CONTAINER_RECURSIVE_PERMISSION_BUSY" or "CONTAINER_RECURSIVE_PERMISSION_RECEIPT_LIMIT" or "CONTAINER_RECURSIVE_PERMISSION_CAPACITY_LOW" or "CONTAINER_RECURSIVE_PERMISSION_HISTORY_LIMIT" or "CONTAINER_RECURSIVE_PERMISSION_HISTORY_CAPACITY" or "CONTAINER_RECURSIVE_PERMISSION_ATTRIBUTES_LIMIT" => StatusCode.ResourceExhausted,
        "CONTAINER_RECURSIVE_PERMISSION_REQUEST_INVALID" or "CONTAINER_RECURSIVE_PERMISSION_MODE_INVALID" or "CONTAINER_RECURSIVE_PERMISSION_PLAN_INVALID" or "CONTAINER_RECURSIVE_PERMISSION_VERSION_INVALID" or "CONTAINER_FILES_PATH_INVALID" => StatusCode.InvalidArgument,
        "CONTAINER_RECURSIVE_PERMISSION_TREE_CHANGED" or "CONTAINER_RECURSIVE_PERMISSION_TARGET_EXISTS" or "CONTAINER_RECURSIVE_PERMISSION_VERSION_CONFLICT" or "CONTAINER_RECURSIVE_PERMISSION_IDEMPOTENCY_CONFLICT" => StatusCode.Aborted,
        "CONTAINER_RECURSIVE_PERMISSION_CANCELED" => StatusCode.Cancelled,
        "CONTAINER_RECURSIVE_PERMISSION_TIMEOUT" or "FILES_INPUT_TIMEOUT" => StatusCode.DeadlineExceeded,
        "CONTAINER_RECURSIVE_PERMISSION_PROTECTED" or "CONTAINER_PROTECTED" or "CONTAINER_UNSAFE_ISOLATION" or "CONTAINER_UNSAFE_MOUNT" or
            "CONTAINER_FILES_SHARED_CORE_MOUNT" or "CONTAINER_UPLOAD_PUBLISH_READ_ONLY" or "CONTAINER_UPLOAD_PUBLISH_MOUNT_ROOT" => StatusCode.PermissionDenied,
        "CONTAINER_RECURSIVE_PERMISSION_LIMITS_INVALID" or "CONTAINER_RECURSIVE_PERMISSION_HISTORY_DOMAIN" or "CONTAINER_RECURSIVE_PERMISSION_ROOT_CHANGED" or "CONTAINER_RECURSIVE_PERMISSION_TREE_ALIAS_OR_MOUNT" or "CONTAINER_RECURSIVE_PERMISSION_ENTRY_SCOPE_INVALID" or "CONTAINER_RECURSIVE_PERMISSION_ENTRY_RESULT_UNKNOWN" or "CONTAINER_RECURSIVE_PERMISSION_STATE_UNSAFE" or "CONTAINER_RECURSIVE_PERMISSION_MODULE_UNSAFE" or "CONTAINER_RECURSIVE_PERMISSION_RECEIPT_INVALID" or "CONTAINER_RECURSIVE_PERMISSION_PRIVATE_UNSAFE" or
            "CONTAINER_RECURSIVE_PERMISSION_PRIVATE_CHANGED" or "CONTAINER_RECURSIVE_PERMISSION_PARENT_CHANGED" or "CONTAINER_RECURSIVE_PERMISSION_OWNER_CHANGED" or "CONTAINER_RECURSIVE_PERMISSION_TARGET_CHANGED" or "CONTAINER_RECURSIVE_PERMISSION_SESSION_REQUIRED" or "CONTAINER_RECURSIVE_PERMISSION_SOURCE_UNSUPPORTED" or "CONTAINER_RECURSIVE_PERMISSION_EXTENDED_METADATA_REQUIRED" or "CONTAINER_RECURSIVE_PERMISSION_HISTORY_UNSAFE" or "CONTAINER_RECURSIVE_PERMISSION_HISTORY_CHANGED" or "FILES_BACKUP_TARGET_CHANGED" or "FILES_BACKUP_NOT_HDD" or
            "CONTAINER_FILES_TARGET_MISSING" or "CONTAINER_FILES_INSTANCE_CHANGED" or "CONTAINER_FILES_TARGET_CHANGED" or "CONTAINER_FILES_ROOT_CHANGED" or
            "CONTAINER_NOT_RUNNING" => StatusCode.FailedPrecondition,
        _ => StatusCode.Unavailable
        };
        return new(new(status, status == StatusCode.Unavailable ? "CONTAINER_RECURSIVE_PERMISSION_UNAVAILABLE" : code!));
    }
    private static void Fields(JsonElement value, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Invalid();
        var fields = value.EnumerateObject().Select(p => p.Name).ToArray();
        if (fields.Length != names.Length || fields.Distinct(StringComparer.Ordinal).Count() != names.Length || fields.Any(p => !names.Contains(p, StringComparer.Ordinal))) throw Invalid();
    }
    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString() ?? throw Invalid();
    private static InvalidOperationException Invalid() => new("CONTAINER_RECURSIVE_PERMISSION_OUTPUT_INVALID");
}
