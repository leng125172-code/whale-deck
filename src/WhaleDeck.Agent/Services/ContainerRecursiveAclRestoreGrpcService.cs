using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using Grpc.Core;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

/// <summary>Heterogeneous recursive ACL restore protocol. Business authorization and permanent intents belong to Application.</summary>
public sealed class ContainerRecursiveAclRestoreGrpcService(FileTransferRunner runner) : ContainerRecursiveAclRestoreService.ContainerRecursiveAclRestoreServiceBase
{
    public override async Task<ContainerRecursiveAclRestorePlan> Plan(ContainerRecursiveAclRestorePlanRequest request, ServerCallContext context)
    {
        ContainerRecursiveAclRestorePolicy.Command(request.Command); ContainerRecursiveAclRestorePolicy.Context(request.Context, planning: true);
        ContainerRecursiveAclRestorePolicy.RequireBinding(request, context);
        using var result = await runner.ContainerRecursiveAclRestoreOnceAsync("plan", JsonSerializer.Serialize(ContainerRecursiveAclRestorePolicy.CommandInput(request.Command)), context.CancellationToken);
        return ContainerRecursiveAclRestorePolicy.PlanOutput(result.RootElement, request.Command);
    }
    public override Task<ContainerRecursiveAclRestoreReceipt> Apply(ContainerRecursiveAclRestoreRequest request, ServerCallContext context) => Run("apply", request, context);
    public override Task<ContainerRecursiveAclRestoreReceipt> Inspect(ContainerRecursiveAclRestoreRequest request, ServerCallContext context) => Run("inspect", request, context);
    public override Task<ContainerRecursiveAclRestoreReceipt> Discard(ContainerRecursiveAclRestoreRequest request, ServerCallContext context) => Run("discard", request, context);
    private async Task<ContainerRecursiveAclRestoreReceipt> Run(string action, ContainerRecursiveAclRestoreRequest request, ServerCallContext context)
    {
        ContainerRecursiveAclRestorePolicy.Request(request); ContainerRecursiveAclRestorePolicy.RequireBinding(request, context);
        using var result = await runner.ContainerRecursiveAclRestoreOnceAsync(action, JsonSerializer.Serialize(new
            { operationId = request.OperationId, owner = request.Owner, plan = ContainerRecursiveAclRestorePolicy.PlanInput(request.Plan) }), context.CancellationToken);
        return ContainerRecursiveAclRestorePolicy.Receipt(result.RootElement, request);
    }
}

internal static class ContainerRecursiveAclRestorePolicy
{
    public static void RequireBinding(IMessage request, ServerCallContext context)
    {
        var expected = context.RequestHeaders.GetValue(ContainerRecursiveAclRestoreBinding.Header);
        var actual = ContainerRecursiveAclRestoreBinding.Hash(request);
        if (expected is not { Length: 64 } || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(actual), Encoding.ASCII.GetBytes(expected)))
            throw new RpcException(new(StatusCode.PermissionDenied, "CONTAINER_RECURSIVE_ACL_RESTORE_BINDING_INVALID"));
    }
    public static void Context(RequestContext? context, bool planning = false)
    {
        if (context is null || context.Capability.Length != 0 || string.IsNullOrWhiteSpace(context.ActorSubject) || context.ActorSubject.Length > 256 ||
            context.ActorSubject.Any(char.IsControl) || string.IsNullOrWhiteSpace(context.TraceId) || context.TraceId.Length > 128 || context.TraceId.Any(char.IsControl))
            throw new ArgumentException("CONTAINER_RECURSIVE_ACL_RESTORE_CONTEXT_INVALID");
        if (planning)
        {
            if (context.JobId.Length != 0 || context.IdempotencyKey.Length != 0) throw new ArgumentException("CONTAINER_RECURSIVE_ACL_RESTORE_CONTEXT_INVALID");
        }
        else
        { FileTransferPolicy.Identity(context.JobId); FileTransferPolicy.Identity(context.IdempotencyKey); }
    }
    public static void Command(ContainerRecursiveAclRestoreCommand? command)
    {
        if (command is null) throw new ArgumentException("CONTAINER_RECURSIVE_ACL_RESTORE_COMMAND_REQUIRED");
        ContainerFilePolicy.Location(command.Location); FileTransferPolicy.Hash(command.ExpectedVersion);
        FileTransferPolicy.Identity(command.CaptureId); FileTransferPolicy.Hash(command.ExpectedHistoryDigest);
    }
    public static object CommandInput(ContainerRecursiveAclRestoreCommand command) => new
        { containerId = command.Location.ContainerId, instanceId = command.Location.InstanceId, path = command.Location.Path,
          captureId = command.CaptureId, expectedHistoryDigest = command.ExpectedHistoryDigest, expectedVersion = command.ExpectedVersion };
    public static void Plan(ContainerRecursiveAclRestorePlan? plan)
    {
        if (plan is null || plan.SchemaVersion != 1 || plan.ParentIdentity is not { Inode: > 0 } || plan.OwnerUid == uint.MaxValue || plan.OwnerGid == uint.MaxValue)
            throw new ArgumentException("CONTAINER_RECURSIVE_ACL_RESTORE_PLAN_INVALID");
        Command(plan.Command);
        if (plan.RestoreSource is null) throw new ArgumentException("CONTAINER_RECURSIVE_ACL_RESTORE_SOURCE_INVALID");
        try { ContainerRecursiveAclHistoryPolicy.Summary(plan.RestoreSource, plan.Command.Location); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        { throw new ArgumentException("CONTAINER_RECURSIVE_ACL_RESTORE_SOURCE_INVALID"); }
        var summary = plan.RestoreSource;
        if (summary.CaptureId != plan.Command.CaptureId || summary.HistoryDigest != plan.Command.ExpectedHistoryDigest ||
            summary.EntryCount != plan.EntryCount || summary.FileCount != plan.FileCount || summary.DirectoryCount != plan.DirectoryCount ||
            plan.RestoreSourceFrame is not { HasDevice: true, HasInode: true, HasMode: true, HasOwnerUid: true, HasOwnerGid: true, HasLinkCount: true, HasSizeBytes: true, HasModifiedAtUnixNs: true, HasChangedAtUnixNs: true,
                Inode: > 0, Mode: 0x8180, OwnerUid: 0, OwnerGid: 0, LinkCount: 1, SizeBytes: > 0 and <= 16777216 })
            throw new ArgumentException("CONTAINER_RECURSIVE_ACL_RESTORE_SOURCE_INVALID");
        FileTransferPolicy.Hash(plan.RestoreTargetsDigest);
        if (plan.RootIdentity is not { Inode: > 0, LinkCount: > 0 } source || source.Mode > ushort.MaxValue || (source.Mode & 0xE00) != 0 ||
            (source.Mode & 0xF000) != 0x4000U || source.OwnerUid == uint.MaxValue || source.OwnerGid == uint.MaxValue || source.SizeBytes < 0 ||
            plan.EntryCount is < 1 or > 10000 || plan.FileCount >= plan.EntryCount || plan.DirectoryCount != plan.EntryCount - plan.FileCount || plan.ChangeCount > plan.EntryCount || plan.RestoreSource.ChangedAclType == "default" && plan.ChangeCount > plan.DirectoryCount)
            throw new ArgumentException("CONTAINER_RECURSIVE_ACL_RESTORE_PLAN_INVALID");
        FileTransferPolicy.Hash(plan.ManifestDigest); FileTransferPolicy.Hash(plan.TargetProof); FileTransferPolicy.Hash(plan.ParentVersion); FileTransferPolicy.Hash(plan.Fingerprint);
    }
    public static object PlanInput(ContainerRecursiveAclRestorePlan plan)
    {
        Plan(plan);
        return new { schemaVersion = plan.SchemaVersion, command = CommandInput(plan.Command), targetProof = plan.TargetProof,
            parentIdentity = new[] { plan.ParentIdentity.Device, plan.ParentIdentity.Inode }, parentVersion = plan.ParentVersion,
            uid = plan.OwnerUid, gid = plan.OwnerGid, fingerprint = plan.Fingerprint,
            rootIdentity = Identity(plan), manifestDigest = plan.ManifestDigest, entryCount = plan.EntryCount, fileCount = plan.FileCount, directoryCount = plan.DirectoryCount, changeCount = plan.ChangeCount,
            restoreSource = SummaryInput(plan.RestoreSource), restoreSourceFrame = FrameInput(plan.RestoreSourceFrame), restoreTargetsDigest = plan.RestoreTargetsDigest };
    }
    private static object[] Identity(ContainerRecursiveAclRestorePlan plan)
    {
        var value = plan.RootIdentity;
        return [value.Device, value.Inode, value.Mode, value.OwnerUid, value.OwnerGid, value.LinkCount,
            value.SizeBytes, value.ModifiedAtUnixNs, plan.RootChangedAtUnixNs];
    }
    public static void Request(ContainerRecursiveAclRestoreRequest request)
    {
        FileTransferPolicy.Identity(request.OperationId); FileTransferPolicy.Hash(request.Owner); Plan(request.Plan); Context(request.Context);
        if (request.Context.JobId != request.OperationId) throw new ArgumentException("CONTAINER_RECURSIVE_ACL_RESTORE_CONTEXT_INVALID");
    }
    public static ContainerRecursiveAclRestorePlan PlanOutput(JsonElement value, ContainerRecursiveAclRestoreCommand expected)
    {
        Fields(value, "schemaVersion", "command", "targetProof", "parentIdentity", "parentVersion", "uid", "gid", "fingerprint", "rootIdentity", "manifestDigest", "entryCount", "fileCount", "directoryCount", "changeCount", "restoreSource", "restoreSourceFrame", "restoreTargetsDigest");
        var command = value.GetProperty("command"); Fields(command, "containerId", "instanceId", "path", "captureId", "expectedHistoryDigest", "expectedVersion");
        var parent = value.GetProperty("parentIdentity"); var source = value.GetProperty("rootIdentity");
        if (parent.ValueKind != JsonValueKind.Array || parent.GetArrayLength() != 2 || source.ValueKind != JsonValueKind.Array || source.GetArrayLength() != 9) throw Invalid();
        var result = new ContainerRecursiveAclRestorePlan { SchemaVersion = value.GetProperty("schemaVersion").GetUInt32(),
            Command = new() { Location = new() { ContainerId = Text(command, "containerId"), InstanceId = Text(command, "instanceId"), Path = Text(command, "path") },
                CaptureId = Text(command, "captureId"), ExpectedHistoryDigest = Text(command, "expectedHistoryDigest"), ExpectedVersion = Text(command, "expectedVersion") },
            TargetProof = Text(value, "targetProof"), ParentIdentity = new() { Device = parent[0].GetUInt64(), Inode = parent[1].GetUInt64() },
            ParentVersion = Text(value, "parentVersion"), OwnerUid = value.GetProperty("uid").GetUInt32(), OwnerGid = value.GetProperty("gid").GetUInt32(), Fingerprint = Text(value, "fingerprint"),
            RootIdentity = new() { Device = source[0].GetUInt64(), Inode = source[1].GetUInt64(), Mode = source[2].GetUInt32(),
                OwnerUid = source[3].GetUInt32(), OwnerGid = source[4].GetUInt32(), LinkCount = source[5].GetUInt64(),
                SizeBytes = source[6].GetInt64(), ModifiedAtUnixNs = source[7].GetInt64() }, RootChangedAtUnixNs = source[8].GetInt64(),
            ManifestDigest = Text(value, "manifestDigest"), EntryCount = value.GetProperty("entryCount").GetUInt32(), FileCount = value.GetProperty("fileCount").GetUInt32(),
            DirectoryCount = value.GetProperty("directoryCount").GetUInt32(), ChangeCount = value.GetProperty("changeCount").GetUInt32(),
            RestoreSource = SummaryOutput(value.GetProperty("restoreSource")), RestoreSourceFrame = FrameOutput(value.GetProperty("restoreSourceFrame")), RestoreTargetsDigest = Text(value, "restoreTargetsDigest") };
        try { Plan(result); } catch (ArgumentException) { throw Invalid(); }
        if (!result.Command.Equals(expected)) throw Invalid();
        return result;
    }
    public static ContainerRecursiveAclRestoreReceipt Receipt(JsonElement value, ContainerRecursiveAclRestoreRequest expected)
    {
        Fields(value, "operationId", "containerId", "instanceId", "fingerprint", "targetProof", "state", "bindingCurrent", "replayed", "completedCount", "changedCount", "abortedCount", "notStartedCount", "unknownCount", "historyMayExist", "historyVerified", "historyDigest", "historyAvailable", "outcomeDigest", "aclType");
        var receipt = new ContainerRecursiveAclRestoreReceipt { OperationId = Text(value, "operationId"), ContainerId = Text(value, "containerId"), InstanceId = Text(value, "instanceId"),
            Fingerprint = Text(value, "fingerprint"), TargetProof = Text(value, "targetProof"), State = Text(value, "state"),
            BindingCurrent = value.GetProperty("bindingCurrent").GetBoolean(), Replayed = value.GetProperty("replayed").GetBoolean(),
            CompletedCount = value.GetProperty("completedCount").GetUInt32(), ChangedCount = value.GetProperty("changedCount").GetUInt32(),
            AbortedCount = value.GetProperty("abortedCount").GetUInt32(), NotStartedCount = value.GetProperty("notStartedCount").GetUInt32(), UnknownCount = value.GetProperty("unknownCount").GetUInt32(),
            HistoryMayExist = value.GetProperty("historyMayExist").GetBoolean(), HistoryVerified = value.GetProperty("historyVerified").GetBoolean(),
            HistoryAvailable = value.GetProperty("historyAvailable").GetBoolean(), HistoryDigest = Text(value, "historyDigest"), OutcomeDigest = Text(value, "outcomeDigest"), AclType = Text(value, "aclType") };
        var n = expected.Plan.EntryCount;
        if (receipt.OperationId != expected.OperationId || receipt.ContainerId != expected.Plan.Command.Location.ContainerId ||
            receipt.AclType != expected.Plan.RestoreSource.ChangedAclType || receipt.InstanceId != expected.Plan.Command.Location.InstanceId || receipt.Fingerprint != expected.Plan.Fingerprint || receipt.TargetProof != expected.Plan.TargetProof ||
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
        "CONTAINER_RECURSIVE_ACL_RESTORE_NOT_FOUND" => StatusCode.NotFound,
        "CONTAINER_RECURSIVE_ACL_RESTORE_TREE_LIMIT" or "CONTAINER_RECURSIVE_ACL_RESTORE_MANIFEST_LIMIT" or "CONTAINER_RECURSIVE_ACL_RESTORE_BUSY" or "CONTAINER_RECURSIVE_ACL_RESTORE_RECEIPT_LIMIT" or "CONTAINER_RECURSIVE_ACL_RESTORE_CAPACITY_LOW" or "CONTAINER_RECURSIVE_ACL_RESTORE_HISTORY_LIMIT" or "CONTAINER_RECURSIVE_ACL_RESTORE_HISTORY_CAPACITY" or "CONTAINER_RECURSIVE_ACL_RESTORE_ATTRIBUTES_LIMIT" => StatusCode.ResourceExhausted,
        "CONTAINER_RECURSIVE_ACL_RESTORE_REQUEST_INVALID" or "CONTAINER_RECURSIVE_ACL_RESTORE_TYPE_INVALID" or "CONTAINER_RECURSIVE_ACL_RESTORE_ENTRIES_INVALID" or "CONTAINER_RECURSIVE_ACL_RESTORE_DEFAULT_FILE_ENTRIES_INVALID" or "CONTAINER_RECURSIVE_ACL_RESTORE_PLAN_INVALID" or "CONTAINER_RECURSIVE_ACL_RESTORE_VERSION_INVALID" or "CONTAINER_FILES_PATH_INVALID" => StatusCode.InvalidArgument,
        "CONTAINER_RECURSIVE_ACL_RESTORE_SOURCE_CHANGED" or "CONTAINER_RECURSIVE_ACL_RESTORE_SOURCE_TREE_MISMATCH" or "CONTAINER_RECURSIVE_ACL_RESTORE_TREE_CHANGED" or "CONTAINER_RECURSIVE_ACL_RESTORE_TARGET_EXISTS" or "CONTAINER_RECURSIVE_ACL_RESTORE_VERSION_CONFLICT" or "CONTAINER_RECURSIVE_ACL_RESTORE_IDEMPOTENCY_CONFLICT" => StatusCode.Aborted,
        "CONTAINER_RECURSIVE_ACL_RESTORE_CANCELED" => StatusCode.Cancelled,
        "CONTAINER_RECURSIVE_ACL_RESTORE_TIMEOUT" or "FILES_INPUT_TIMEOUT" => StatusCode.DeadlineExceeded,
        "CONTAINER_RECURSIVE_ACL_RESTORE_PROTECTED" or "CONTAINER_PROTECTED" or "CONTAINER_UNSAFE_ISOLATION" or "CONTAINER_UNSAFE_MOUNT" or
            "CONTAINER_FILES_SHARED_CORE_MOUNT" or "CONTAINER_UPLOAD_PUBLISH_READ_ONLY" or "CONTAINER_UPLOAD_PUBLISH_MOUNT_ROOT" => StatusCode.PermissionDenied,
        "CONTAINER_RECURSIVE_ACL_RESTORE_SOURCE_NOT_FOUND" or "CONTAINER_RECURSIVE_ACL_RESTORE_SOURCE_INVALID" or "CONTAINER_RECURSIVE_ACL_RESTORE_LIMITS_INVALID" or "CONTAINER_RECURSIVE_ACL_RESTORE_HISTORY_DOMAIN" or "CONTAINER_RECURSIVE_ACL_RESTORE_ROOT_CHANGED" or "CONTAINER_RECURSIVE_ACL_RESTORE_TREE_ALIAS_OR_MOUNT" or "CONTAINER_RECURSIVE_ACL_RESTORE_ENTRY_SCOPE_INVALID" or "CONTAINER_RECURSIVE_ACL_RESTORE_ENTRY_RESULT_UNKNOWN" or "CONTAINER_RECURSIVE_ACL_RESTORE_MAPPING_UNPROVEN" or "CONTAINER_RECURSIVE_ACL_RESTORE_UNSUPPORTED" or "CONTAINER_RECURSIVE_ACL_RESTORE_ACL_INVALID" or "CONTAINER_RECURSIVE_ACL_RESTORE_STATE_UNSAFE" or "CONTAINER_RECURSIVE_ACL_RESTORE_MODULE_UNSAFE" or "CONTAINER_RECURSIVE_ACL_RESTORE_RECEIPT_INVALID" or "CONTAINER_RECURSIVE_ACL_RESTORE_PRIVATE_UNSAFE" or
            "CONTAINER_RECURSIVE_ACL_RESTORE_PRIVATE_CHANGED" or "CONTAINER_RECURSIVE_ACL_RESTORE_PARENT_CHANGED" or "CONTAINER_RECURSIVE_ACL_RESTORE_OWNER_CHANGED" or "CONTAINER_RECURSIVE_ACL_RESTORE_TARGET_CHANGED" or "CONTAINER_RECURSIVE_ACL_RESTORE_SESSION_REQUIRED" or "CONTAINER_RECURSIVE_ACL_RESTORE_SOURCE_UNSUPPORTED" or "CONTAINER_RECURSIVE_ACL_RESTORE_EXTENDED_METADATA_REQUIRED" or "CONTAINER_RECURSIVE_ACL_RESTORE_HISTORY_UNSAFE" or "CONTAINER_RECURSIVE_ACL_RESTORE_HISTORY_CHANGED" or "FILES_BACKUP_TARGET_CHANGED" or "FILES_BACKUP_NOT_HDD" or
            "CONTAINER_FILES_TARGET_MISSING" or "CONTAINER_FILES_INSTANCE_CHANGED" or "CONTAINER_FILES_TARGET_CHANGED" or "CONTAINER_FILES_ROOT_CHANGED" or
            "CONTAINER_NOT_RUNNING" => StatusCode.FailedPrecondition,
        _ => StatusCode.Unavailable
        };
        return new(new(status, status == StatusCode.Unavailable ? "CONTAINER_RECURSIVE_ACL_RESTORE_UNAVAILABLE" : code!));
    }
    private static object SummaryInput(ContainerRecursiveAclHistorySummary s) => new {
        captureId = s.CaptureId, historyDigest = s.HistoryDigest, createdAtUnixMs = s.CreatedAtUnixMs, sourceInstanceId = s.SourceInstanceId,
        changedAclType = s.ChangedAclType, manifestDigest = s.ManifestDigest, entryCount = s.EntryCount, fileCount = s.FileCount, directoryCount = s.DirectoryCount, changeCount = s.ChangeCount };
    private static ContainerRecursiveAclHistorySummary SummaryOutput(JsonElement v)
    {
        Fields(v, "captureId", "historyDigest", "createdAtUnixMs", "sourceInstanceId", "changedAclType", "manifestDigest", "entryCount", "fileCount", "directoryCount", "changeCount");
        return new() { CaptureId = Text(v, "captureId"), HistoryDigest = Text(v, "historyDigest"), CreatedAtUnixMs = v.GetProperty("createdAtUnixMs").GetInt64(),
            SourceInstanceId = Text(v, "sourceInstanceId"), ChangedAclType = Text(v, "changedAclType"), ManifestDigest = Text(v, "manifestDigest"),
            EntryCount = v.GetProperty("entryCount").GetUInt32(), FileCount = v.GetProperty("fileCount").GetUInt32(), DirectoryCount = v.GetProperty("directoryCount").GetUInt32(), ChangeCount = v.GetProperty("changeCount").GetUInt32() };
    }
    private static object[] FrameInput(ContainerRecursiveAclRestoreFrame f) => [f.Device, f.Inode, f.Mode, f.OwnerUid, f.OwnerGid, f.LinkCount, f.SizeBytes, f.ModifiedAtUnixNs, f.ChangedAtUnixNs];
    private static ContainerRecursiveAclRestoreFrame FrameOutput(JsonElement f)
    {
        if (f.ValueKind != JsonValueKind.Array || f.GetArrayLength() != 9) throw Invalid();
        return new() { Device = f[0].GetUInt64(), Inode = f[1].GetUInt64(), Mode = f[2].GetUInt32(), OwnerUid = f[3].GetUInt32(), OwnerGid = f[4].GetUInt32(),
            LinkCount = f[5].GetUInt64(), SizeBytes = f[6].GetInt64(), ModifiedAtUnixNs = f[7].GetInt64(), ChangedAtUnixNs = f[8].GetInt64() };
    }
    private static void Fields(JsonElement value, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Invalid();
        var fields = value.EnumerateObject().Select(p => p.Name).ToArray();
        if (fields.Length != names.Length || fields.Distinct(StringComparer.Ordinal).Count() != names.Length || fields.Any(p => !names.Contains(p, StringComparer.Ordinal))) throw Invalid();
    }
    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString() ?? throw Invalid();
    private static InvalidOperationException Invalid() => new("CONTAINER_RECURSIVE_ACL_RESTORE_OUTPUT_INVALID");
}
