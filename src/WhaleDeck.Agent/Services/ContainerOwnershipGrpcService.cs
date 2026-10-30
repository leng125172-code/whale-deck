using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using Grpc.Core;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

/// <summary>Non-recursive explicit UID/GID ownership protocol. Business authorization and permanent intents belong to Application.</summary>
public sealed class ContainerOwnershipGrpcService(FileTransferRunner runner) : ContainerOwnershipService.ContainerOwnershipServiceBase
{
    public override async Task<ContainerOwnershipPlan> Plan(ContainerOwnershipPlanRequest request, ServerCallContext context)
    {
        ContainerOwnershipPolicy.Command(request.Command); ContainerOwnershipPolicy.Context(request.Context, planning: true);
        ContainerOwnershipPolicy.RequireBinding(request, context);
        using var result = await runner.ContainerOwnershipOnceAsync("plan", JsonSerializer.Serialize(ContainerOwnershipPolicy.CommandInput(request.Command)), context.CancellationToken);
        return ContainerOwnershipPolicy.PlanOutput(result.RootElement, request.Command);
    }
    public override Task<ContainerOwnershipReceipt> Apply(ContainerOwnershipRequest request, ServerCallContext context) => Run("apply", request, context);
    public override Task<ContainerOwnershipReceipt> Inspect(ContainerOwnershipRequest request, ServerCallContext context) => Run("inspect", request, context);
    public override Task<ContainerOwnershipReceipt> Discard(ContainerOwnershipRequest request, ServerCallContext context) => Run("discard", request, context);
    private async Task<ContainerOwnershipReceipt> Run(string action, ContainerOwnershipRequest request, ServerCallContext context)
    {
        ContainerOwnershipPolicy.Request(request); ContainerOwnershipPolicy.RequireBinding(request, context);
        using var result = await runner.ContainerOwnershipOnceAsync(action, JsonSerializer.Serialize(new
            { operationId = request.OperationId, owner = request.Owner, plan = ContainerOwnershipPolicy.PlanInput(request.Plan) }), context.CancellationToken);
        return ContainerOwnershipPolicy.Receipt(result.RootElement, request);
    }
}

internal static class ContainerOwnershipPolicy
{
    public static void RequireBinding(IMessage request, ServerCallContext context)
    {
        var expected = context.RequestHeaders.GetValue(ContainerOwnershipBinding.Header);
        var actual = ContainerOwnershipBinding.Hash(request);
        if (expected is not { Length: 64 } || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(actual), Encoding.ASCII.GetBytes(expected)))
            throw new RpcException(new(StatusCode.PermissionDenied, "CONTAINER_OWNERSHIP_BINDING_INVALID"));
    }
    public static void Context(RequestContext? context, bool planning = false)
    {
        if (context is null || context.Capability.Length != 0 || string.IsNullOrWhiteSpace(context.ActorSubject) || context.ActorSubject.Length > 256 ||
            context.ActorSubject.Any(char.IsControl) || string.IsNullOrWhiteSpace(context.TraceId) || context.TraceId.Length > 128 || context.TraceId.Any(char.IsControl))
            throw new ArgumentException("CONTAINER_OWNERSHIP_CONTEXT_INVALID");
        if (planning)
        {
            if (context.JobId.Length != 0 || context.IdempotencyKey.Length != 0) throw new ArgumentException("CONTAINER_OWNERSHIP_CONTEXT_INVALID");
        }
        else
        { FileTransferPolicy.Identity(context.JobId); FileTransferPolicy.Identity(context.IdempotencyKey); }
    }
    public static void Command(ContainerOwnershipCommand? command)
    {
        if (command is null) throw new ArgumentException("CONTAINER_OWNERSHIP_COMMAND_REQUIRED");
        ContainerFilePolicy.Location(command.Location); FileTransferPolicy.Hash(command.ExpectedVersion);
        if (!command.HasUid || !command.HasGid || command.Uid == uint.MaxValue || command.Gid == uint.MaxValue)
            throw new ArgumentException("CONTAINER_OWNERSHIP_UID_GID_INVALID");
    }
    public static object CommandInput(ContainerOwnershipCommand command) => new
        { containerId = command.Location.ContainerId, instanceId = command.Location.InstanceId, path = command.Location.Path, uid = command.Uid, gid = command.Gid, expectedVersion = command.ExpectedVersion };
    public static void Plan(ContainerOwnershipPlan? plan)
    {
        if (plan is null || plan.SchemaVersion != 1 || plan.ParentIdentity is not { Inode: > 0 } || plan.OwnerUid == uint.MaxValue || plan.OwnerGid == uint.MaxValue)
            throw new ArgumentException("CONTAINER_OWNERSHIP_PLAN_INVALID");
        Command(plan.Command);
        if (plan.Kind is not ("File" or "Directory") || plan.SourceIdentity is not { Inode: > 0, LinkCount: > 0 } source ||
            source.Mode > ushort.MaxValue || (source.Mode & 0xE00) != 0 || source.OwnerUid == uint.MaxValue || source.OwnerGid == uint.MaxValue || source.SizeBytes < 0 ||
            (source.Mode & 0xF000) != (plan.Kind == "File" ? 0x8000U : 0x4000U) || plan.Kind == "File" && source.LinkCount != 1 ||
            plan.NoChange != (source.OwnerUid == plan.Command.Uid && source.OwnerGid == plan.Command.Gid)) throw new ArgumentException("CONTAINER_OWNERSHIP_PLAN_INVALID");
        FileTransferPolicy.Hash(plan.AttributesDigest); FileTransferPolicy.Hash(plan.TargetProof); FileTransferPolicy.Hash(plan.ParentVersion); FileTransferPolicy.Hash(plan.Fingerprint);
    }
    public static object PlanInput(ContainerOwnershipPlan plan)
    {
        Plan(plan);
        return new { schemaVersion = plan.SchemaVersion, command = CommandInput(plan.Command), targetProof = plan.TargetProof,
            parentIdentity = new[] { plan.ParentIdentity.Device, plan.ParentIdentity.Inode }, parentVersion = plan.ParentVersion,
            uid = plan.OwnerUid, gid = plan.OwnerGid, fingerprint = plan.Fingerprint,
            kind = plan.Kind, sourceIdentity = Identity(plan), attributesDigest = plan.AttributesDigest, noChange = plan.NoChange };
    }
    private static object[] Identity(ContainerOwnershipPlan plan)
    {
        var value = plan.SourceIdentity;
        return [value.Device, value.Inode, value.Mode, value.OwnerUid, value.OwnerGid, value.LinkCount,
            value.SizeBytes, value.ModifiedAtUnixNs, plan.SourceChangedAtUnixNs];
    }
    public static void Request(ContainerOwnershipRequest request)
    {
        FileTransferPolicy.Identity(request.OperationId); FileTransferPolicy.Hash(request.Owner); Plan(request.Plan); Context(request.Context);
        if (request.Context.JobId != request.OperationId) throw new ArgumentException("CONTAINER_OWNERSHIP_CONTEXT_INVALID");
    }
    public static ContainerOwnershipPlan PlanOutput(JsonElement value, ContainerOwnershipCommand expected)
    {
        Fields(value, "schemaVersion", "command", "targetProof", "parentIdentity", "parentVersion", "uid", "gid", "fingerprint", "kind", "sourceIdentity", "attributesDigest", "noChange");
        var command = value.GetProperty("command"); Fields(command, "containerId", "instanceId", "path", "uid", "gid", "expectedVersion");
        var parent = value.GetProperty("parentIdentity"); var source = value.GetProperty("sourceIdentity");
        if (parent.ValueKind != JsonValueKind.Array || parent.GetArrayLength() != 2 || source.ValueKind != JsonValueKind.Array || source.GetArrayLength() != 9) throw Invalid();
        var result = new ContainerOwnershipPlan { SchemaVersion = value.GetProperty("schemaVersion").GetUInt32(),
            Command = new() { Location = new() { ContainerId = Text(command, "containerId"), InstanceId = Text(command, "instanceId"), Path = Text(command, "path") }, Uid = OwnerId(command, "uid"), Gid = OwnerId(command, "gid"), ExpectedVersion = Text(command, "expectedVersion") },
            TargetProof = Text(value, "targetProof"), ParentIdentity = new() { Device = parent[0].GetUInt64(), Inode = parent[1].GetUInt64() },
            ParentVersion = Text(value, "parentVersion"), OwnerUid = value.GetProperty("uid").GetUInt32(), OwnerGid = value.GetProperty("gid").GetUInt32(), Fingerprint = Text(value, "fingerprint"),
            Kind = Text(value, "kind"), SourceIdentity = new() { Device = source[0].GetUInt64(), Inode = source[1].GetUInt64(), Mode = source[2].GetUInt32(),
                OwnerUid = source[3].GetUInt32(), OwnerGid = source[4].GetUInt32(), LinkCount = source[5].GetUInt64(),
                SizeBytes = source[6].GetInt64(), ModifiedAtUnixNs = source[7].GetInt64() }, SourceChangedAtUnixNs = source[8].GetInt64(),
            AttributesDigest = Text(value, "attributesDigest"), NoChange = value.GetProperty("noChange").GetBoolean() };
        try { Plan(result); } catch (ArgumentException) { throw Invalid(); }
        if (!result.Command.Equals(expected)) throw Invalid();
        return result;
    }
    public static ContainerOwnershipReceipt Receipt(JsonElement value, ContainerOwnershipRequest expected)
    {
        Fields(value, "operationId", "containerId", "instanceId", "fingerprint", "targetProof", "state", "version", "bindingCurrent", "replayed", "kind", "ownerConfirmed", "noChange", "historyMayExist", "historyVerified", "historyDigest");
        var result = new ContainerOwnershipReceipt { OperationId = Text(value, "operationId"), ContainerId = Text(value, "containerId"), InstanceId = Text(value, "instanceId"),
            Fingerprint = Text(value, "fingerprint"), TargetProof = Text(value, "targetProof"), State = Text(value, "state"), Version = Text(value, "version"),
            BindingCurrent = value.GetProperty("bindingCurrent").GetBoolean(), Replayed = value.GetProperty("replayed").GetBoolean(),
            Kind = Text(value, "kind"), NoChange = value.GetProperty("noChange").GetBoolean(), HistoryMayExist = value.GetProperty("historyMayExist").GetBoolean(),
            HistoryVerified = value.GetProperty("historyVerified").GetBoolean(), HistoryDigest = Text(value, "historyDigest") };
        if (value.GetProperty("ownerConfirmed").ValueKind != JsonValueKind.Null) result.OwnerConfirmed = value.GetProperty("ownerConfirmed").GetBoolean();
        if (result.OperationId != expected.OperationId || result.ContainerId != expected.Plan.Command.Location.ContainerId ||
            result.InstanceId != expected.Plan.Command.Location.InstanceId || result.Fingerprint != expected.Plan.Fingerprint || result.TargetProof != expected.Plan.TargetProof ||
            result.Kind != expected.Plan.Kind || result.NoChange != expected.Plan.NoChange ||
            result.HistoryVerified && !result.HistoryMayExist || result.HistoryVerified != (result.HistoryDigest.Length == 64) ||
            !result.HistoryVerified && result.HistoryDigest.Length != 0 ||
            result.State is not ("Completed" or "Aborted" or "Unknown") ||
            result.State == "Completed" && (!result.HasOwnerConfirmed || !result.OwnerConfirmed || !result.HistoryVerified) ||
            result.State == "Aborted" && (!result.HasOwnerConfirmed || result.OwnerConfirmed) || result.State == "Unknown" && result.HasOwnerConfirmed || result.State != "Completed" && (result.Version.Length != 0 || result.BindingCurrent)) throw Invalid();
        if (result.HistoryVerified) try { FileTransferPolicy.Hash(result.HistoryDigest); } catch (ArgumentException) { throw Invalid(); }
        if (result.State == "Completed") try { FileTransferPolicy.Hash(result.Version); } catch (ArgumentException) { throw Invalid(); }
        return result;
    }
    public static RpcException Failure(string? code)
    {
        var status = code switch
        {
        "CONTAINER_OWNERSHIP_NOT_FOUND" => StatusCode.NotFound,
        "CONTAINER_OWNERSHIP_BUSY" or "CONTAINER_OWNERSHIP_RECEIPT_LIMIT" or "CONTAINER_OWNERSHIP_CAPACITY_LOW" or "CONTAINER_OWNERSHIP_HISTORY_LIMIT" or "CONTAINER_OWNERSHIP_HISTORY_CAPACITY" or "CONTAINER_OWNERSHIP_ATTRIBUTES_LIMIT" => StatusCode.ResourceExhausted,
        "CONTAINER_OWNERSHIP_REQUEST_INVALID" or "CONTAINER_OWNERSHIP_UID_GID_INVALID" or "CONTAINER_OWNERSHIP_PLAN_INVALID" or "CONTAINER_OWNERSHIP_VERSION_INVALID" or "CONTAINER_FILES_PATH_INVALID" => StatusCode.InvalidArgument,
        "CONTAINER_OWNERSHIP_TARGET_EXISTS" or "CONTAINER_OWNERSHIP_VERSION_CONFLICT" or "CONTAINER_OWNERSHIP_IDEMPOTENCY_CONFLICT" => StatusCode.Aborted,
        "CONTAINER_OWNERSHIP_TIMEOUT" or "FILES_INPUT_TIMEOUT" => StatusCode.DeadlineExceeded,
        "CONTAINER_OWNERSHIP_PROTECTED" or "CONTAINER_PROTECTED" or "CONTAINER_UNSAFE_ISOLATION" or "CONTAINER_UNSAFE_MOUNT" or
            "CONTAINER_FILES_SHARED_CORE_MOUNT" or "CONTAINER_UPLOAD_PUBLISH_READ_ONLY" or "CONTAINER_UPLOAD_PUBLISH_MOUNT_ROOT" => StatusCode.PermissionDenied,
        "CONTAINER_OWNERSHIP_STATE_UNSAFE" or "CONTAINER_OWNERSHIP_MODULE_UNSAFE" or "CONTAINER_OWNERSHIP_RECEIPT_INVALID" or "CONTAINER_OWNERSHIP_PRIVATE_UNSAFE" or
            "CONTAINER_OWNERSHIP_PRIVATE_CHANGED" or "CONTAINER_OWNERSHIP_PARENT_CHANGED" or "CONTAINER_OWNERSHIP_OWNER_CHANGED" or "CONTAINER_OWNERSHIP_TARGET_CHANGED" or "CONTAINER_OWNERSHIP_SESSION_REQUIRED" or "CONTAINER_OWNERSHIP_SOURCE_UNSUPPORTED" or "CONTAINER_OWNERSHIP_EXTENDED_METADATA_REQUIRED" or "CONTAINER_OWNERSHIP_HISTORY_UNSAFE" or "CONTAINER_OWNERSHIP_HISTORY_CHANGED" or "FILES_BACKUP_TARGET_CHANGED" or "FILES_BACKUP_NOT_HDD" or
            "CONTAINER_FILES_TARGET_MISSING" or "CONTAINER_FILES_INSTANCE_CHANGED" or "CONTAINER_FILES_TARGET_CHANGED" or "CONTAINER_FILES_ROOT_CHANGED" or
            "CONTAINER_NOT_RUNNING" => StatusCode.FailedPrecondition,
        _ => StatusCode.Unavailable
        };
        return new(new(status, status == StatusCode.Unavailable ? "CONTAINER_OWNERSHIP_UNAVAILABLE" : code!));
    }
    private static void Fields(JsonElement value, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Invalid();
        var fields = value.EnumerateObject().Select(p => p.Name).ToArray();
        if (fields.Length != names.Length || fields.Distinct(StringComparer.Ordinal).Count() != names.Length || fields.Any(p => !names.Contains(p, StringComparer.Ordinal))) throw Invalid();
    }
    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString() ?? throw Invalid();
    private static uint OwnerId(JsonElement value, string name)
    {
        var item = value.GetProperty(name);
        return item.ValueKind == JsonValueKind.Number && item.TryGetUInt32(out var id) && id != uint.MaxValue ? id : throw Invalid();
    }
    private static InvalidOperationException Invalid() => new("CONTAINER_OWNERSHIP_OUTPUT_INVALID");
}
