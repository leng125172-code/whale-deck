using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using Grpc.Core;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

/// <summary>Non-recursive explicit access/default ACL protocol. Business authorization and permanent intents belong to Application.</summary>
public sealed class ContainerAclGrpcService(FileTransferRunner runner) : ContainerAclService.ContainerAclServiceBase
{
    public override async Task<ContainerAclPlan> Plan(ContainerAclPlanRequest request, ServerCallContext context)
    {
        ContainerAclPolicy.Command(request.Command); ContainerAclPolicy.Context(request.Context, planning: true);
        ContainerAclPolicy.RequireBinding(request, context);
        using var result = await runner.ContainerAclOnceAsync("plan", JsonSerializer.Serialize(ContainerAclPolicy.CommandInput(request.Command)), context.CancellationToken);
        return ContainerAclPolicy.PlanOutput(result.RootElement, request.Command);
    }
    public override Task<ContainerAclReceipt> Apply(ContainerAclRequest request, ServerCallContext context) => Run("apply", request, context);
    public override Task<ContainerAclReceipt> Inspect(ContainerAclRequest request, ServerCallContext context) => Run("inspect", request, context);
    public override Task<ContainerAclReceipt> Discard(ContainerAclRequest request, ServerCallContext context) => Run("discard", request, context);
    private async Task<ContainerAclReceipt> Run(string action, ContainerAclRequest request, ServerCallContext context)
    {
        ContainerAclPolicy.Request(request); ContainerAclPolicy.RequireBinding(request, context);
        using var result = await runner.ContainerAclOnceAsync(action, JsonSerializer.Serialize(new
            { operationId = request.OperationId, owner = request.Owner, plan = ContainerAclPolicy.PlanInput(request.Plan) }), context.CancellationToken);
        return ContainerAclPolicy.Receipt(result.RootElement, request);
    }
}

internal static class ContainerAclPolicy
{
    public static void RequireBinding(IMessage request, ServerCallContext context)
    {
        var expected = context.RequestHeaders.GetValue(ContainerAclBinding.Header);
        var actual = ContainerAclBinding.Hash(request);
        if (expected is not { Length: 64 } || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(actual), Encoding.ASCII.GetBytes(expected)))
            throw new RpcException(new(StatusCode.PermissionDenied, "CONTAINER_ACL_BINDING_INVALID"));
    }
    public static void Context(RequestContext? context, bool planning = false)
    {
        if (context is null || context.Capability.Length != 0 || string.IsNullOrWhiteSpace(context.ActorSubject) || context.ActorSubject.Length > 256 ||
            context.ActorSubject.Any(char.IsControl) || string.IsNullOrWhiteSpace(context.TraceId) || context.TraceId.Length > 128 || context.TraceId.Any(char.IsControl))
            throw new ArgumentException("CONTAINER_ACL_CONTEXT_INVALID");
        if (planning)
        {
            if (context.JobId.Length != 0 || context.IdempotencyKey.Length != 0) throw new ArgumentException("CONTAINER_ACL_CONTEXT_INVALID");
        }
        else
        { FileTransferPolicy.Identity(context.JobId); FileTransferPolicy.Identity(context.IdempotencyKey); }
    }
    public static void Command(ContainerAclCommand? command)
    {
        if (command is null) throw new ArgumentException("CONTAINER_ACL_COMMAND_REQUIRED");
        ContainerFilePolicy.Location(command.Location); FileTransferPolicy.Hash(command.ExpectedVersion);
        if (command.AclType is not ("access" or "default")) throw new ArgumentException("CONTAINER_ACL_TYPE_INVALID");
        Entries(command.Entries, command.AclType == "default");
    }
    public static object CommandInput(ContainerAclCommand command) => new
        { containerId = command.Location.ContainerId, instanceId = command.Location.InstanceId, path = command.Location.Path, aclType = command.AclType, entries = EntriesInput(command.Entries), expectedVersion = command.ExpectedVersion };
    public static void Plan(ContainerAclPlan? plan)
    {
        if (plan is null || plan.SchemaVersion != 1 || plan.ParentIdentity is not { Inode: > 0 } || plan.OwnerUid == uint.MaxValue || plan.OwnerGid == uint.MaxValue)
            throw new ArgumentException("CONTAINER_ACL_PLAN_INVALID");
        Command(plan.Command);
        if (plan.Kind is not ("File" or "Directory") || plan.SourceIdentity is not { Inode: > 0, LinkCount: > 0 } source ||
            source.Mode > ushort.MaxValue || (source.Mode & 0xE00) != 0 || source.OwnerUid == uint.MaxValue || source.OwnerGid == uint.MaxValue || source.SizeBytes < 0 ||
            (source.Mode & 0xF000) != (plan.Kind == "File" ? 0x8000U : 0x4000U) || plan.Kind == "File" && source.LinkCount != 1 ||
            plan.Kind == "File" && (plan.Command.AclType == "default" || plan.DefaultAcl.Count != 0)) throw new ArgumentException("CONTAINER_ACL_PLAN_INVALID");
        Entries(plan.AccessAcl); Entries(plan.DefaultAcl, true);
        if (Mode(plan.AccessAcl) != (source.Mode & 0x1FF) || plan.TargetMode != (plan.Command.AclType == "access" ? Mode(plan.Command.Entries) : source.Mode & 0x1FF) ||
            plan.NoChange != plan.Command.Entries.SequenceEqual(plan.Command.AclType == "access" ? plan.AccessAcl : plan.DefaultAcl)) throw new ArgumentException("CONTAINER_ACL_PLAN_INVALID");
        FileTransferPolicy.Hash(plan.AttributesDigest); FileTransferPolicy.Hash(plan.TargetProof); FileTransferPolicy.Hash(plan.ParentVersion); FileTransferPolicy.Hash(plan.Fingerprint);
    }
    public static object PlanInput(ContainerAclPlan plan)
    {
        Plan(plan);
        return new { schemaVersion = plan.SchemaVersion, command = CommandInput(plan.Command), targetProof = plan.TargetProof,
            parentIdentity = new[] { plan.ParentIdentity.Device, plan.ParentIdentity.Inode }, parentVersion = plan.ParentVersion,
            uid = plan.OwnerUid, gid = plan.OwnerGid, fingerprint = plan.Fingerprint,
            kind = plan.Kind, sourceIdentity = Identity(plan), attributesDigest = plan.AttributesDigest, noChange = plan.NoChange, accessAcl = EntriesInput(plan.AccessAcl), defaultAcl = EntriesInput(plan.DefaultAcl), targetMode = plan.TargetMode };
    }
    private static object[] Identity(ContainerAclPlan plan)
    {
        var value = plan.SourceIdentity;
        return [value.Device, value.Inode, value.Mode, value.OwnerUid, value.OwnerGid, value.LinkCount,
            value.SizeBytes, value.ModifiedAtUnixNs, plan.SourceChangedAtUnixNs];
    }
    public static void Request(ContainerAclRequest request)
    {
        FileTransferPolicy.Identity(request.OperationId); FileTransferPolicy.Hash(request.Owner); Plan(request.Plan); Context(request.Context);
        if (request.Context.JobId != request.OperationId) throw new ArgumentException("CONTAINER_ACL_CONTEXT_INVALID");
    }
    public static ContainerAclPlan PlanOutput(JsonElement value, ContainerAclCommand expected)
    {
        Fields(value, "schemaVersion", "command", "targetProof", "parentIdentity", "parentVersion", "uid", "gid", "fingerprint", "kind", "sourceIdentity", "attributesDigest", "noChange", "accessAcl", "defaultAcl", "targetMode");
        var command = value.GetProperty("command"); Fields(command, "containerId", "instanceId", "path", "aclType", "entries", "expectedVersion");
        var parent = value.GetProperty("parentIdentity"); var source = value.GetProperty("sourceIdentity");
        if (parent.ValueKind != JsonValueKind.Array || parent.GetArrayLength() != 2 || source.ValueKind != JsonValueKind.Array || source.GetArrayLength() != 9) throw Invalid();
        var result = new ContainerAclPlan { SchemaVersion = value.GetProperty("schemaVersion").GetUInt32(),
            Command = new() { Location = new() { ContainerId = Text(command, "containerId"), InstanceId = Text(command, "instanceId"), Path = Text(command, "path") }, AclType = Text(command, "aclType"), Entries = { EntriesOutput(command.GetProperty("entries")) }, ExpectedVersion = Text(command, "expectedVersion") },
            TargetProof = Text(value, "targetProof"), ParentIdentity = new() { Device = parent[0].GetUInt64(), Inode = parent[1].GetUInt64() },
            ParentVersion = Text(value, "parentVersion"), OwnerUid = value.GetProperty("uid").GetUInt32(), OwnerGid = value.GetProperty("gid").GetUInt32(), Fingerprint = Text(value, "fingerprint"),
            Kind = Text(value, "kind"), SourceIdentity = new() { Device = source[0].GetUInt64(), Inode = source[1].GetUInt64(), Mode = source[2].GetUInt32(),
                OwnerUid = source[3].GetUInt32(), OwnerGid = source[4].GetUInt32(), LinkCount = source[5].GetUInt64(),
                SizeBytes = source[6].GetInt64(), ModifiedAtUnixNs = source[7].GetInt64() }, SourceChangedAtUnixNs = source[8].GetInt64(),
            AttributesDigest = Text(value, "attributesDigest"), NoChange = value.GetProperty("noChange").GetBoolean(),
            AccessAcl = { EntriesOutput(value.GetProperty("accessAcl")) }, DefaultAcl = { EntriesOutput(value.GetProperty("defaultAcl")) }, TargetMode = value.GetProperty("targetMode").GetUInt32() };
        try { Plan(result); } catch (ArgumentException) { throw Invalid(); }
        if (!result.Command.Equals(expected)) throw Invalid();
        return result;
    }
    public static ContainerAclReceipt Receipt(JsonElement value, ContainerAclRequest expected)
    {
        Fields(value, "operationId", "containerId", "instanceId", "fingerprint", "targetProof", "state", "version", "bindingCurrent", "replayed", "kind", "aclConfirmed", "noChange", "historyMayExist", "historyVerified", "historyDigest", "aclType", "targetMode");
        var result = new ContainerAclReceipt { OperationId = Text(value, "operationId"), ContainerId = Text(value, "containerId"), InstanceId = Text(value, "instanceId"),
            Fingerprint = Text(value, "fingerprint"), TargetProof = Text(value, "targetProof"), State = Text(value, "state"), Version = Text(value, "version"),
            BindingCurrent = value.GetProperty("bindingCurrent").GetBoolean(), Replayed = value.GetProperty("replayed").GetBoolean(),
            Kind = Text(value, "kind"), NoChange = value.GetProperty("noChange").GetBoolean(), HistoryMayExist = value.GetProperty("historyMayExist").GetBoolean(),
            HistoryVerified = value.GetProperty("historyVerified").GetBoolean(), HistoryDigest = Text(value, "historyDigest"), AclType = Text(value, "aclType"), TargetMode = value.GetProperty("targetMode").GetUInt32() };
        if (value.GetProperty("aclConfirmed").ValueKind != JsonValueKind.Null) result.AclConfirmed = value.GetProperty("aclConfirmed").GetBoolean();
        if (result.OperationId != expected.OperationId || result.ContainerId != expected.Plan.Command.Location.ContainerId ||
            result.InstanceId != expected.Plan.Command.Location.InstanceId || result.Fingerprint != expected.Plan.Fingerprint || result.TargetProof != expected.Plan.TargetProof ||
            result.Kind != expected.Plan.Kind || result.NoChange != expected.Plan.NoChange || result.AclType != expected.Plan.Command.AclType || result.TargetMode != expected.Plan.TargetMode ||
            result.HistoryVerified && !result.HistoryMayExist || result.HistoryVerified != (result.HistoryDigest.Length == 64) ||
            !result.HistoryVerified && result.HistoryDigest.Length != 0 ||
            result.State is not ("Completed" or "Aborted" or "Unknown") ||
            result.State == "Completed" && (!result.HasAclConfirmed || !result.AclConfirmed || !result.HistoryVerified) ||
            result.State == "Aborted" && (!result.HasAclConfirmed || result.AclConfirmed) || result.State == "Unknown" && result.HasAclConfirmed || result.State != "Completed" && (result.Version.Length != 0 || result.BindingCurrent)) throw Invalid();
        if (result.HistoryVerified) try { FileTransferPolicy.Hash(result.HistoryDigest); } catch (ArgumentException) { throw Invalid(); }
        if (result.State == "Completed") try { FileTransferPolicy.Hash(result.Version); } catch (ArgumentException) { throw Invalid(); }
        return result;
    }
    public static RpcException Failure(string? code)
    {
        var status = code switch
        {
        "CONTAINER_ACL_NOT_FOUND" => StatusCode.NotFound,
        "CONTAINER_ACL_BUSY" or "CONTAINER_ACL_RECEIPT_LIMIT" or "CONTAINER_ACL_CAPACITY_LOW" or "CONTAINER_ACL_HISTORY_LIMIT" or "CONTAINER_ACL_HISTORY_CAPACITY" or "CONTAINER_ACL_ATTRIBUTES_LIMIT" => StatusCode.ResourceExhausted,
        "CONTAINER_ACL_REQUEST_INVALID" or "CONTAINER_ACL_ENTRIES_INVALID" or "CONTAINER_ACL_TYPE_INVALID" or "CONTAINER_ACL_PLAN_INVALID" or "CONTAINER_ACL_VERSION_INVALID" or "CONTAINER_FILES_PATH_INVALID" => StatusCode.InvalidArgument,
        "CONTAINER_ACL_TARGET_EXISTS" or "CONTAINER_ACL_VERSION_CONFLICT" or "CONTAINER_ACL_IDEMPOTENCY_CONFLICT" => StatusCode.Aborted,
        "CONTAINER_ACL_TIMEOUT" or "FILES_INPUT_TIMEOUT" => StatusCode.DeadlineExceeded,
        "CONTAINER_ACL_PROTECTED" or "CONTAINER_PROTECTED" or "CONTAINER_UNSAFE_ISOLATION" or "CONTAINER_UNSAFE_MOUNT" or
            "CONTAINER_FILES_SHARED_CORE_MOUNT" or "CONTAINER_UPLOAD_PUBLISH_READ_ONLY" or "CONTAINER_UPLOAD_PUBLISH_MOUNT_ROOT" => StatusCode.PermissionDenied,
        "CONTAINER_ACL_STATE_UNSAFE" or "CONTAINER_ACL_MODULE_UNSAFE" or "CONTAINER_ACL_RECEIPT_INVALID" or "CONTAINER_ACL_PRIVATE_UNSAFE" or
            "CONTAINER_ACL_PRIVATE_CHANGED" or "CONTAINER_ACL_PARENT_CHANGED" or "CONTAINER_ACL_OWNER_CHANGED" or "CONTAINER_ACL_TARGET_CHANGED" or "CONTAINER_ACL_SESSION_REQUIRED" or "CONTAINER_ACL_UNSUPPORTED" or "CONTAINER_ACL_MAPPING_UNPROVEN" or "CONTAINER_ACL_DEFAULT_REQUIRES_DIRECTORY" or "CONTAINER_ACL_ACL_INVALID" or "CONTAINER_ACL_SOURCE_UNSUPPORTED" or "CONTAINER_ACL_EXTENDED_METADATA_REQUIRED" or "CONTAINER_ACL_HISTORY_UNSAFE" or "CONTAINER_ACL_HISTORY_CHANGED" or "FILES_BACKUP_TARGET_CHANGED" or "FILES_BACKUP_NOT_HDD" or
            "CONTAINER_FILES_TARGET_MISSING" or "CONTAINER_FILES_INSTANCE_CHANGED" or "CONTAINER_FILES_TARGET_CHANGED" or "CONTAINER_FILES_ROOT_CHANGED" or
            "CONTAINER_NOT_RUNNING" => StatusCode.FailedPrecondition,
        _ => StatusCode.Unavailable
        };
        return new(new(status, status == StatusCode.Unavailable ? "CONTAINER_ACL_UNAVAILABLE" : code!));
    }
    public static void Entries(IReadOnlyList<ContainerAclEntry> entries, bool empty = false)
    {
        if (entries.Count > 64 || entries.Count == 0 && !empty) throw new ArgumentException("CONTAINER_ACL_ENTRIES_INVALID");
        if (entries.Count == 0) return;
        var seen = new HashSet<(int, uint?)>(); (int Tag, long Id) previous = (0, -1);
        foreach (var entry in entries)
        {
            var tag = entry.Tag switch { "user_obj" => 1, "user" => 2, "group_obj" => 4, "group" => 8, "mask" => 16, "other" => 32, _ => 0 };
            uint? id = entry.HasId ? entry.Id : null;
            if (tag == 0 || !entry.HasPermissions || entry.Permissions > 7 || (tag is 2 or 8 ? id is null or uint.MaxValue : id is not null) ||
                !seen.Add((tag, id))) throw new ArgumentException("CONTAINER_ACL_ENTRIES_INVALID");
            var key = (tag, id is { } value ? (long)value : -1L);
            if (key.CompareTo(previous) <= 0) throw new ArgumentException("CONTAINER_ACL_ENTRIES_INVALID"); previous = key;
        }
        if (!seen.Contains((1, null)) || !seen.Contains((4, null)) || !seen.Contains((32, null)) ||
            entries.Any(e => e.Tag is "user" or "group") && !seen.Contains((16, null))) throw new ArgumentException("CONTAINER_ACL_ENTRIES_INVALID");
    }
    private static uint Mode(IReadOnlyList<ContainerAclEntry> entries)
    {
        Entries(entries);
        return entries.Single(e => e.Tag == "user_obj").Permissions << 6 |
            (entries.FirstOrDefault(e => e.Tag == "mask") ?? entries.Single(e => e.Tag == "group_obj")).Permissions << 3 |
            entries.Single(e => e.Tag == "other").Permissions;
    }
    private static object[] EntriesInput(IEnumerable<ContainerAclEntry> entries) =>
        entries.Select(e => (object)new { tag = e.Tag, id = e.HasId ? (uint?)e.Id : null, permissions = e.Permissions }).ToArray();
    private static ContainerAclEntry[] EntriesOutput(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > 64) throw Invalid();
        return value.EnumerateArray().Select(e =>
        {
            Fields(e, "tag", "id", "permissions"); var entry = new ContainerAclEntry { Tag = Text(e, "tag"), Permissions = OwnerId(e, "permissions") };
            if (e.GetProperty("id").ValueKind != JsonValueKind.Null) entry.Id = OwnerId(e, "id");
            return entry;
        }).ToArray();
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
    private static InvalidOperationException Invalid() => new("CONTAINER_ACL_OUTPUT_INVALID");
}
