using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using Grpc.Core;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

/// <summary>Ordinary-file Trash/retained-inode restore protocol. Business authorization and permanent intents belong to Application.</summary>
public sealed class ContainerRecycleGrpcService(FileTransferRunner runner) : ContainerRecycleService.ContainerRecycleServiceBase
{
    public override async Task<ContainerRecyclePlan> Plan(ContainerRecyclePlanRequest request, ServerCallContext context)
    {
        ContainerRecyclePolicy.Command(request.Command); ContainerRecyclePolicy.Context(request.Context, planning: true);
        ContainerRecyclePolicy.RequireBinding(request, context);
        using var result = await runner.ContainerRecycleOnceAsync("plan", JsonSerializer.Serialize(ContainerRecyclePolicy.CommandInput(request.Command)), context.CancellationToken);
        return ContainerRecyclePolicy.PlanOutput(result.RootElement, request.Command);
    }
    public override Task<ContainerRecycleReceipt> Apply(ContainerRecycleRequest request, ServerCallContext context) => Run("apply", request, context);
    public override Task<ContainerRecycleReceipt> Inspect(ContainerRecycleRequest request, ServerCallContext context) => Run("inspect", request, context);
    public override Task<ContainerRecycleReceipt> Discard(ContainerRecycleRequest request, ServerCallContext context) => Run("discard", request, context);
    public override Task<ContainerRecycleReceipt> Restore(ContainerRecycleRequest request, ServerCallContext context) => Run("restore", request, context);
    private async Task<ContainerRecycleReceipt> Run(string action, ContainerRecycleRequest request, ServerCallContext context)
    {
        ContainerRecyclePolicy.Request(request, restoring: action == "restore"); ContainerRecyclePolicy.RequireBinding(request, context);
        using var result = await runner.ContainerRecycleOnceAsync(action, JsonSerializer.Serialize(new
            { operationId = request.OperationId, owner = request.Owner, plan = ContainerRecyclePolicy.PlanInput(request.Plan) }), context.CancellationToken);
        return ContainerRecyclePolicy.Receipt(result.RootElement, request);
    }
}

internal static class ContainerRecyclePolicy
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    public static void RequireBinding(IMessage request, ServerCallContext context)
    {
        var expected = context.RequestHeaders.GetValue(ContainerRecycleBinding.Header);
        var actual = ContainerRecycleBinding.Hash(request);
        if (expected is not { Length: 64 } || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(actual), Encoding.ASCII.GetBytes(expected)))
            throw new RpcException(new(StatusCode.PermissionDenied, "CONTAINER_RECYCLE_BINDING_INVALID"));
    }
    public static void Context(RequestContext? context, bool planning = false)
    {
        if (context is null || context.Capability.Length != 0 || string.IsNullOrWhiteSpace(context.ActorSubject) || context.ActorSubject.Length > 256 ||
            context.ActorSubject.Any(char.IsControl) || string.IsNullOrWhiteSpace(context.TraceId) || context.TraceId.Length > 128 || context.TraceId.Any(char.IsControl))
            throw new ArgumentException("CONTAINER_RECYCLE_CONTEXT_INVALID");
        if (planning)
        {
            if (context.JobId.Length != 0 || context.IdempotencyKey.Length != 0) throw new ArgumentException("CONTAINER_RECYCLE_CONTEXT_INVALID");
        }
        else
        { FileTransferPolicy.Identity(context.JobId); FileTransferPolicy.Identity(context.IdempotencyKey); }
    }
    public static void Command(ContainerRecycleCommand? command)
    {
        if (command is null) throw new ArgumentException("CONTAINER_RECYCLE_COMMAND_REQUIRED");
        ContainerFilePolicy.Location(command.Location); FileTransferPolicy.Hash(command.ExpectedVersion);

    }
    public static object CommandInput(ContainerRecycleCommand command) => new
        { containerId = command.Location.ContainerId, instanceId = command.Location.InstanceId, path = command.Location.Path, expectedVersion = command.ExpectedVersion };
    public static void Plan(ContainerRecyclePlan? plan)
    {
        if (plan is null || plan.SchemaVersion != 1 || plan.ParentIdentity is not { Inode: > 0 } || plan.OwnerUid == uint.MaxValue || plan.OwnerGid == uint.MaxValue)
            throw new ArgumentException("CONTAINER_RECYCLE_PLAN_INVALID");
        Command(plan.Command);
        if (plan.SourceIdentity is not { Inode: > 0, LinkCount: 1 } source || source.Mode > ushort.MaxValue || (source.Mode & 0xF000) != 0x8000 ||
            (source.Mode & 0xE00) != 0 || source.OwnerUid == uint.MaxValue || source.OwnerGid == uint.MaxValue || source.SizeBytes is < 0 or > 1073741824)
            throw new ArgumentException("CONTAINER_RECYCLE_PLAN_INVALID");
        FileTransferPolicy.Hash(plan.TargetProof); FileTransferPolicy.Hash(plan.ParentVersion);
        FileTransferPolicy.Hash(plan.Fingerprint); FileTransferPolicy.Hash(plan.Sha256);
        if (plan.Attributes.Count > 32) throw new ArgumentException("CONTAINER_RECYCLE_ATTRIBUTES_LIMIT");
        var total = 0; byte[]? previous = null;
        foreach (var item in plan.Attributes)
        {
            if (item is null || item.Name is null || !item.Name.StartsWith("user.", StringComparison.Ordinal) || item.Name.Contains('\0', StringComparison.Ordinal) || item.Value is null)
                throw new ArgumentException("CONTAINER_RECYCLE_PLAN_INVALID");
            byte[] name;
            try { name = Utf8.GetBytes(item.Name); } catch (EncoderFallbackException) { throw new ArgumentException("CONTAINER_RECYCLE_PLAN_INVALID"); }
            if (name.Length > 255 || previous is not null && previous.AsSpan().SequenceCompareTo(name) >= 0) throw new ArgumentException("CONTAINER_RECYCLE_PLAN_INVALID");
            total = checked(total + name.Length + item.Value.Length); previous = name;
            if (total > 4096) throw new ArgumentException("CONTAINER_RECYCLE_ATTRIBUTES_LIMIT");
        }
    }
    public static object PlanInput(ContainerRecyclePlan plan)
    {
        Plan(plan);
        return new { schemaVersion = plan.SchemaVersion, command = CommandInput(plan.Command), targetProof = plan.TargetProof,
            parentIdentity = new[] { plan.ParentIdentity.Device, plan.ParentIdentity.Inode }, parentVersion = plan.ParentVersion,
            uid = plan.OwnerUid, gid = plan.OwnerGid, fingerprint = plan.Fingerprint,
            sourceIdentity = Identity(plan), sha256 = plan.Sha256, attributes = plan.Attributes.Select(a => new[] { a.Name, Convert.ToBase64String(a.Value.Span) }).ToArray() };
    }
    private static object[] Identity(ContainerRecyclePlan plan)
    {
        var value = plan.SourceIdentity;
        return [value.Device, value.Inode, value.Mode, value.OwnerUid, value.OwnerGid, value.LinkCount,
            value.SizeBytes, value.ModifiedAtUnixNs, plan.SourceChangedAtUnixNs];
    }
    public static void Request(ContainerRecycleRequest request, bool restoring = false)
    {
        FileTransferPolicy.Identity(request.OperationId); FileTransferPolicy.Hash(request.Owner); Plan(request.Plan); Context(request.Context);
        if (restoring ? request.Context.JobId == request.OperationId : request.Context.JobId != request.OperationId) throw new ArgumentException("CONTAINER_RECYCLE_CONTEXT_INVALID");
    }
    public static ContainerRecyclePlan PlanOutput(JsonElement value, ContainerRecycleCommand expected)
    {
        Fields(value, "schemaVersion", "command", "targetProof", "parentIdentity", "parentVersion", "uid", "gid", "fingerprint", "sourceIdentity", "sha256", "attributes");
        var command = value.GetProperty("command"); Fields(command, "containerId", "instanceId", "path", "expectedVersion");
        var parent = value.GetProperty("parentIdentity"); var source = value.GetProperty("sourceIdentity");
        if (parent.ValueKind != JsonValueKind.Array || parent.GetArrayLength() != 2 || source.ValueKind != JsonValueKind.Array || source.GetArrayLength() != 9) throw Invalid();
        var result = new ContainerRecyclePlan { SchemaVersion = value.GetProperty("schemaVersion").GetUInt32(),
            Command = new() { Location = new() { ContainerId = Text(command, "containerId"), InstanceId = Text(command, "instanceId"), Path = Text(command, "path") }, ExpectedVersion = Text(command, "expectedVersion") },
            TargetProof = Text(value, "targetProof"), ParentIdentity = new() { Device = parent[0].GetUInt64(), Inode = parent[1].GetUInt64() },
            ParentVersion = Text(value, "parentVersion"), OwnerUid = value.GetProperty("uid").GetUInt32(), OwnerGid = value.GetProperty("gid").GetUInt32(), Fingerprint = Text(value, "fingerprint"),
            SourceIdentity = new() { Device = source[0].GetUInt64(), Inode = source[1].GetUInt64(), Mode = source[2].GetUInt32(),
                OwnerUid = source[3].GetUInt32(), OwnerGid = source[4].GetUInt32(), LinkCount = source[5].GetUInt64(),
                SizeBytes = source[6].GetInt64(), ModifiedAtUnixNs = source[7].GetInt64() }, SourceChangedAtUnixNs = source[8].GetInt64(),
            Sha256 = Text(value, "sha256") };
        var attributes = value.GetProperty("attributes");
        if (attributes.ValueKind != JsonValueKind.Array || attributes.GetArrayLength() > 32) throw Invalid();
        foreach (var item in attributes.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Array || item.GetArrayLength() != 2 || item[0].ValueKind != JsonValueKind.String || item[1].ValueKind != JsonValueKind.String) throw Invalid();
            var encoded = item[1].GetString()!; byte[] bytes;
            try { bytes = Convert.FromBase64String(encoded); } catch (FormatException) { throw Invalid(); }
            if (Convert.ToBase64String(bytes) != encoded) throw Invalid();
            result.Attributes.Add(new ContainerRecycleAttribute { Name = item[0].GetString()!, Value = ByteString.CopyFrom(bytes) });
        }
        try { Plan(result); } catch (ArgumentException) { throw Invalid(); }
        if (!result.Command.Equals(expected)) throw Invalid();
        return result;
    }
    public static ContainerRecycleReceipt Receipt(JsonElement value, ContainerRecycleRequest expected)
    {
        Fields(value, "operationId", "containerId", "instanceId", "fingerprint", "targetProof", "state", "bindingCurrent", "replayed", "historyMayExist", "historyVerified", "historyDigest", "historyAvailable", "sourceRetained", "sourceRestored");
        var result = new ContainerRecycleReceipt { OperationId = Text(value, "operationId"), ContainerId = Text(value, "containerId"), InstanceId = Text(value, "instanceId"),
            Fingerprint = Text(value, "fingerprint"), TargetProof = Text(value, "targetProof"), State = Text(value, "state"),
            BindingCurrent = value.GetProperty("bindingCurrent").GetBoolean(), Replayed = value.GetProperty("replayed").GetBoolean(),
            HistoryMayExist = value.GetProperty("historyMayExist").GetBoolean(), HistoryVerified = value.GetProperty("historyVerified").GetBoolean(),
            HistoryDigest = Text(value, "historyDigest"), HistoryAvailable = value.GetProperty("historyAvailable").GetBoolean() };
        if (value.GetProperty("sourceRetained").ValueKind != JsonValueKind.Null) result.SourceRetained = value.GetProperty("sourceRetained").GetBoolean();
        if (value.GetProperty("sourceRestored").ValueKind != JsonValueKind.Null) result.SourceRestored = value.GetProperty("sourceRestored").GetBoolean();
        if (result.OperationId != expected.OperationId || result.ContainerId != expected.Plan.Command.Location.ContainerId || result.InstanceId != expected.Plan.Command.Location.InstanceId ||
            result.Fingerprint != expected.Plan.Fingerprint || result.TargetProof != expected.Plan.TargetProof || result.State is not ("Trashed" or "Restored" or "Aborted" or "Unknown") ||
            result.HistoryVerified && !result.HistoryMayExist || result.HistoryAvailable && !result.HistoryVerified || result.HistoryVerified != (result.HistoryDigest.Length == 64) ||
            !result.HistoryVerified && result.HistoryDigest.Length != 0 || result.BindingCurrent && (!result.HistoryAvailable || result.State is not ("Trashed" or "Restored")) ||
            result.State is "Trashed" or "Restored" && !result.HistoryVerified ||
            result.State == "Unknown" && (result.HasSourceRetained || result.HasSourceRestored) ||
            result.State != "Unknown" && (!result.HasSourceRetained || !result.HasSourceRestored || result.SourceRetained != (result.State == "Trashed") || result.SourceRestored != (result.State == "Restored"))) throw Invalid();
        if (result.HistoryVerified) try { FileTransferPolicy.Hash(result.HistoryDigest); } catch (ArgumentException) { throw Invalid(); }
        return result;
    }
    public static RpcException Failure(string? code)
    {
        var status = code switch
        {
        "CONTAINER_RECYCLE_NOT_FOUND" => StatusCode.NotFound,
        "CONTAINER_RECYCLE_BUSY" or "CONTAINER_RECYCLE_RECEIPT_LIMIT" or "FILES_BACKUP_CAPACITY_REQUIRED" or "FILES_BACKUP_HISTORY_LIMIT" or "CONTAINER_RECYCLE_SOURCE_CAPACITY" or "CONTAINER_RECYCLE_CAPACITY_LOW" or "CONTAINER_RECYCLE_HISTORY_LIMIT" or "CONTAINER_RECYCLE_HISTORY_CAPACITY" or "CONTAINER_RECYCLE_ATTRIBUTES_LIMIT" => StatusCode.ResourceExhausted,
        "CONTAINER_RECYCLE_REQUEST_INVALID" or "CONTAINER_RECYCLE_PLAN_INVALID" or "CONTAINER_RECYCLE_VERSION_INVALID" or "CONTAINER_FILES_PATH_INVALID" => StatusCode.InvalidArgument,
        "CONTAINER_RECYCLE_TARGET_EXISTS" or "CONTAINER_RECYCLE_SOURCE_CHANGED" or "CONTAINER_RECYCLE_IDEMPOTENCY_CONFLICT" => StatusCode.Aborted,
        "CONTAINER_RECYCLE_TIMEOUT" or "FILES_INPUT_TIMEOUT" => StatusCode.DeadlineExceeded,
        "CONTAINER_RECYCLE_PROTECTED" or "CONTAINER_PROTECTED" or "CONTAINER_UNSAFE_ISOLATION" or "CONTAINER_UNSAFE_MOUNT" or
            "CONTAINER_FILES_SHARED_CORE_MOUNT" or "CONTAINER_UPLOAD_PUBLISH_READ_ONLY" or "CONTAINER_UPLOAD_PUBLISH_MOUNT_ROOT" => StatusCode.PermissionDenied,
        "CONTAINER_RECYCLE_STATE_UNSAFE" or "CONTAINER_RECYCLE_MODULE_UNSAFE" or "CONTAINER_RECYCLE_RECEIPT_INVALID" or "CONTAINER_RECYCLE_PRIVATE_UNSAFE" or
            "CONTAINER_RECYCLE_PRIVATE_CHANGED" or "CONTAINER_RECYCLE_PARENT_CHANGED" or "CONTAINER_RECYCLE_OWNER_CHANGED" or "CONTAINER_RECYCLE_TARGET_CHANGED" or "CONTAINER_RECYCLE_RESTORE_UNPROVEN" or "CONTAINER_RECYCLE_HISTORY_STATE_CONFLICT" or "CONTAINER_RECYCLE_HISTORY_INCOMPLETE" or "CONTAINER_RECYCLE_SESSION_REQUIRED" or "CONTAINER_RECYCLE_SOURCE_UNSUPPORTED" or "CONTAINER_RECYCLE_EXTENDED_METADATA_REQUIRED" or "CONTAINER_RECYCLE_HISTORY_UNSAFE" or "CONTAINER_RECYCLE_HISTORY_CHANGED" or "FILES_BACKUP_TARGET_CHANGED" or "FILES_BACKUP_NOT_HDD" or
            "CONTAINER_FILES_TARGET_MISSING" or "CONTAINER_FILES_INSTANCE_CHANGED" or "CONTAINER_FILES_TARGET_CHANGED" or "CONTAINER_FILES_ROOT_CHANGED" or
            "CONTAINER_NOT_RUNNING" => StatusCode.FailedPrecondition,
        "FILES_BACKUP_CONFIGURATION_INVALID" or "FILES_BACKUP_CONTENT_INVALID" or "FILES_BACKUP_DEVICE_METADATA_UNSAFE" or "FILES_BACKUP_DEVICE_MISMATCH" or
            "FILES_BACKUP_DIRECTORY_UNSAFE" or "FILES_BACKUP_FILESYSTEM_UNSUPPORTED" or "FILES_BACKUP_HDD_REQUIRED" or "FILES_BACKUP_IDEMPOTENCY_CONFLICT" or
            "FILES_BACKUP_INCOMPLETE" or "FILES_BACKUP_METADATA_UNSAFE" or "FILES_BACKUP_MOUNT_MISMATCH" or "FILES_BACKUP_READ_ONLY" => StatusCode.FailedPrecondition,
        _ => StatusCode.Unavailable
        };
        return new(new(status, status == StatusCode.Unavailable ? "CONTAINER_RECYCLE_UNAVAILABLE" : code!));
    }
    private static void Fields(JsonElement value, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Invalid();
        var fields = value.EnumerateObject().Select(p => p.Name).ToArray();
        if (fields.Length != names.Length || fields.Distinct(StringComparer.Ordinal).Count() != names.Length || fields.Any(p => !names.Contains(p, StringComparer.Ordinal))) throw Invalid();
    }
    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString() ?? throw Invalid();
    private static InvalidOperationException Invalid() => new("CONTAINER_RECYCLE_OUTPUT_INVALID");
}
