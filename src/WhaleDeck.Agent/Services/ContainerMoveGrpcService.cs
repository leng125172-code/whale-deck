using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using Grpc.Core;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

/// <summary>Independent container move authority. Human intents remain in Application.</summary>
public sealed class ContainerMoveGrpcService(FileTransferRunner runner) : ContainerMoveService.ContainerMoveServiceBase
{
    public override async Task<ContainerMovePlan> Plan(ContainerMovePlanRequest request, ServerCallContext context)
    {
        ContainerMovePolicy.Command(request.Command); ContainerMovePolicy.Context(request.Context, planning: true);
        ContainerMovePolicy.RequireBinding(request, context);
        using var result = await runner.ContainerMoveOnceAsync("plan", JsonSerializer.Serialize(ContainerMovePolicy.CommandInput(request.Command)), context.CancellationToken);
        return ContainerMovePolicy.PlanOutput(result.RootElement, request.Command);
    }
    public override Task<ContainerMoveReceipt> Apply(ContainerMoveRequest request, ServerCallContext context) => Run("apply", request, context);
    public override Task<ContainerMoveReceipt> Inspect(ContainerMoveRequest request, ServerCallContext context) => Run("inspect", request, context);
    public override Task<ContainerMoveReceipt> Discard(ContainerMoveRequest request, ServerCallContext context) => Run("discard", request, context);
    private async Task<ContainerMoveReceipt> Run(string action, ContainerMoveRequest request, ServerCallContext context)
    {
        ContainerMovePolicy.Request(request); ContainerMovePolicy.RequireBinding(request, context);
        using var result = await runner.ContainerMoveOnceAsync(action, JsonSerializer.Serialize(new
            { operationId = request.OperationId, owner = request.Owner, plan = ContainerMovePolicy.PlanInput(request.Plan) }), context.CancellationToken);
        return ContainerMovePolicy.Receipt(result.RootElement, request);
    }
}

internal static class ContainerMovePolicy
{
    internal const string SourceRoot = "container-move-source", TargetRoot = "container-move-target";
    public static void RequireBinding(IMessage request, ServerCallContext context)
    {
        var expected = context.RequestHeaders.GetValue(ContainerMoveBinding.Header); var actual = ContainerMoveBinding.Hash(request);
        if (request.CalculateSize() > 65536 || expected is not { Length: 64 } ||
            !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(actual), Encoding.ASCII.GetBytes(expected)))
            throw new RpcException(new(StatusCode.PermissionDenied, "CONTAINER_MOVE_BINDING_INVALID"));
    }
    public static void Context(RequestContext? context, bool planning = false)
    {
        if (context is null || context.Capability.Length != 0 || string.IsNullOrWhiteSpace(context.ActorSubject) || context.ActorSubject.Length > 256 ||
            context.ActorSubject.Any(char.IsControl) || string.IsNullOrWhiteSpace(context.TraceId) || context.TraceId.Length > 128 || context.TraceId.Any(char.IsControl))
            throw new ArgumentException("CONTAINER_MOVE_CONTEXT_INVALID");
        if (planning)
        {
            if (context.JobId.Length != 0 || context.IdempotencyKey.Length != 0) throw new ArgumentException("CONTAINER_MOVE_CONTEXT_INVALID");
        }
        else { FileTransferPolicy.Identity(context.JobId); FileTransferPolicy.Identity(context.IdempotencyKey); }
    }
    public static void Command(ContainerMoveCommand? command)
    {
        if (command is null) throw new ArgumentException("CONTAINER_MOVE_COMMAND_REQUIRED");
        ContainerFilePolicy.Location(command.Source);
        ContainerFilePolicy.Location(new() { ContainerId = command.Source.ContainerId, InstanceId = command.Source.InstanceId, Path = command.TargetPath });
        FileTransferPolicy.Hash(command.ExpectedVersion);
        var source = command.Source.Path; var target = command.TargetPath;
        if (source == target || source.StartsWith(target + "/", StringComparison.Ordinal) || target.StartsWith(source + "/", StringComparison.Ordinal))
            throw new ArgumentException("CONTAINER_MOVE_OVERLAP");
    }
    public static object CommandInput(ContainerMoveCommand command) => new
        { containerId = command.Source.ContainerId, instanceId = command.Source.InstanceId, sourcePath = command.Source.Path,
          targetPath = command.TargetPath, expectedVersion = command.ExpectedVersion };
    private static object[] Identity(ContainerMovePlan plan)
    {
        var value = plan.SourceIdentity;
        return [value.Device, value.Inode, value.Mode, value.OwnerUid, value.OwnerGid, value.LinkCount,
            value.SizeBytes, value.ModifiedAtUnixNs, plan.SourceChangedAtUnixNs];
    }
    internal static string HostVersion(ContainerMovePlan plan) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(Identity(plan))));
    private static void Parent(FileDirectoryIdentity? value)
    { if (value is not { Inode: > 0 }) throw new ArgumentException("CONTAINER_MOVE_PLAN_INVALID"); }
    public static void Plan(ContainerMovePlan? plan)
    {
        if (plan is null || plan.SchemaVersion != 1 || plan.OperationKind != "Move" || plan.Kind is not ("File" or "Directory") || plan.SourceIdentity is not { Inode: > 0, LinkCount: > 0 } source ||
            source.Mode > ushort.MaxValue || (source.Mode & 0xE00) != 0 || source.OwnerUid == uint.MaxValue || source.OwnerGid == uint.MaxValue ||
            source.SizeBytes < 0 || plan.OwnerUid == uint.MaxValue || plan.OwnerGid == uint.MaxValue) throw new ArgumentException("CONTAINER_MOVE_PLAN_INVALID");
        Command(plan.Command); Parent(plan.SourceParent); Parent(plan.TargetParent);
        FileTransferPolicy.Hash(plan.TargetProof); FileTransferPolicy.Hash(plan.TargetParentVersion); FileTransferPolicy.Hash(plan.Fingerprint);
        var expected = HostVersion(plan);
        if (plan.Kind == "File")
        {
            if ((source.Mode & 0xF000) != 0x8000 || source.LinkCount != 1 || source.SizeBytes > FileTransferPolicy.MaximumBytes ||
                plan.InnerPlanCase != ContainerMovePlan.InnerPlanOneofCase.FilePlan) throw new ArgumentException("CONTAINER_MOVE_PLAN_INVALID");
            var inner = plan.FilePlan; _ = FileOperationPolicy.PlanInput(inner);
            if (inner.Command.Kind != FileOperationKind.MoveFile || inner.Command.Source.RootId != SourceRoot || inner.Command.Target.RootId != TargetRoot ||
                inner.Command.Source.Path != plan.Command.Source.Path || inner.Command.Target.Path != plan.Command.TargetPath ||
                inner.Command.ExpectedVersion != expected || !inner.SourceIdentity.Equals(source) || !inner.SourceParent.Equals(plan.SourceParent) ||
                !inner.TargetParent.Equals(plan.TargetParent)) throw new ArgumentException("CONTAINER_MOVE_PLAN_INVALID");
        }
        else
        {
            if ((source.Mode & 0xF000) != 0x4000 || plan.InnerPlanCase != ContainerMovePlan.InnerPlanOneofCase.TreePlan) throw new ArgumentException("CONTAINER_MOVE_PLAN_INVALID");
            _ = FileTreeMovePolicy.PlanInput(plan.TreePlan);
            if (!plan.TreePlan.SourceParent.Equals(plan.SourceParent)) throw new ArgumentException("CONTAINER_MOVE_PLAN_INVALID");
            var inner = plan.TreePlan.CopyPlan;
            if (inner.Command.Source.RootId != SourceRoot || inner.Command.Target.RootId != TargetRoot || inner.Command.Source.Path != plan.Command.Source.Path ||
                inner.Command.Target.Path != plan.Command.TargetPath || inner.Command.ExpectedVersion != expected || !inner.TargetParent.Equals(plan.TargetParent))
                throw new ArgumentException("CONTAINER_MOVE_PLAN_INVALID");
        }
    }
    public static JsonElement PlanInput(ContainerMovePlan plan)
    {
        Plan(plan);
        var result = JsonSerializer.SerializeToElement(new { schemaVersion = plan.SchemaVersion, operationKind = plan.OperationKind, command = CommandInput(plan.Command), kind = plan.Kind,
            targetProof = plan.TargetProof, sourceIdentity = Identity(plan), sourceParent = new[] { plan.SourceParent.Device, plan.SourceParent.Inode },
            targetParent = new[] { plan.TargetParent.Device, plan.TargetParent.Inode }, targetParentVersion = plan.TargetParentVersion,
            uid = plan.OwnerUid, gid = plan.OwnerGid, innerPlan = plan.Kind == "File" ? FileOperationPolicy.PlanInput(plan.FilePlan) : FileTreeMovePolicy.PlanInput(plan.TreePlan),
            fingerprint = plan.Fingerprint });
        if (Encoding.UTF8.GetByteCount(result.GetRawText()) > 32768) throw new ArgumentException("CONTAINER_MOVE_PLAN_INVALID");
        return result;
    }
    public static void Request(ContainerMoveRequest request)
    {
        FileTransferPolicy.Identity(request.OperationId); FileTransferPolicy.Hash(request.Owner); _ = PlanInput(request.Plan); Context(request.Context);
        if (request.Context.JobId != request.OperationId) throw new ArgumentException("CONTAINER_MOVE_CONTEXT_INVALID");
    }
    public static ContainerMovePlan PlanOutput(JsonElement value, ContainerMoveCommand expected)
    {
        Fields(value, "schemaVersion", "operationKind", "command", "kind", "targetProof", "sourceIdentity", "sourceParent", "targetParent", "targetParentVersion", "uid", "gid", "innerPlan", "fingerprint");
        var command = value.GetProperty("command"); Fields(command, "containerId", "instanceId", "sourcePath", "targetPath", "expectedVersion");
        var identity = value.GetProperty("sourceIdentity");
        if (identity.ValueKind != JsonValueKind.Array || identity.GetArrayLength() != 9) throw Invalid();
        var plan = new ContainerMovePlan { SchemaVersion = value.GetProperty("schemaVersion").GetUInt32(),
            Command = new() { Source = new() { ContainerId = Text(command, "containerId"), InstanceId = Text(command, "instanceId"), Path = Text(command, "sourcePath") },
                TargetPath = Text(command, "targetPath"), ExpectedVersion = Text(command, "expectedVersion") },
            OperationKind = Text(value, "operationKind"), Kind = Text(value, "kind"), TargetProof = Text(value, "targetProof"), TargetParentVersion = Text(value, "targetParentVersion"), Fingerprint = Text(value, "fingerprint"),
            SourceIdentity = new() { Device = identity[0].GetUInt64(), Inode = identity[1].GetUInt64(), Mode = identity[2].GetUInt32(), OwnerUid = identity[3].GetUInt32(),
                OwnerGid = identity[4].GetUInt32(), LinkCount = identity[5].GetUInt64(), SizeBytes = identity[6].GetInt64(), ModifiedAtUnixNs = identity[7].GetInt64() },
            SourceChangedAtUnixNs = identity[8].GetInt64(), SourceParent = ReadParent(value.GetProperty("sourceParent")), TargetParent = ReadParent(value.GetProperty("targetParent")),
            OwnerUid = value.GetProperty("uid").GetUInt32(), OwnerGid = value.GetProperty("gid").GetUInt32() };
        if (!plan.Command.Equals(expected)) throw Invalid();
        if (plan.Kind == "File") plan.FilePlan = FileOperationPolicy.Plan(value.GetProperty("innerPlan"), new()
            { Kind = FileOperationKind.MoveFile, Source = new() { RootId = SourceRoot, Path = expected.Source.Path },
                Target = new() { RootId = TargetRoot, Path = expected.TargetPath }, ExpectedVersion = HostVersion(plan) });
        else if (plan.Kind == "Directory") plan.TreePlan = FileTreeMovePolicy.Plan(value.GetProperty("innerPlan"), new()
            { Source = new() { RootId = SourceRoot, Path = expected.Source.Path }, Target = new() { RootId = TargetRoot, Path = expected.TargetPath }, ExpectedVersion = HostVersion(plan) });
        else throw Invalid();
        try { _ = PlanInput(plan); } catch (ArgumentException) { throw Invalid(); }
        return plan;
    }
    public static ContainerMoveReceipt Receipt(JsonElement value, ContainerMoveRequest expected)
    {
        Fields(value, "operationId", "operationKind", "containerId", "instanceId", "kind", "fingerprint", "targetProof", "state", "version", "bindingCurrent", "privateMayExist", "replayed", "destinationPublished", "sourceRemoved", "sourceRetained");
        var result = new ContainerMoveReceipt { OperationId = Text(value, "operationId"), ContainerId = Text(value, "containerId"), InstanceId = Text(value, "instanceId"),
            OperationKind = Text(value, "operationKind"), Kind = Text(value, "kind"), Fingerprint = Text(value, "fingerprint"), TargetProof = Text(value, "targetProof"), State = Text(value, "state"), Version = Text(value, "version"),
            BindingCurrent = value.GetProperty("bindingCurrent").GetBoolean(), PrivateMayExist = value.GetProperty("privateMayExist").GetBoolean(), Replayed = value.GetProperty("replayed").GetBoolean() };
        SetFact(value, "destinationPublished", x => result.DestinationPublished = x);
        SetFact(value, "sourceRemoved", x => result.SourceRemoved = x);
        SetFact(value, "sourceRetained", x => result.SourceRetained = x);
        if (result.OperationId != expected.OperationId || result.OperationKind != "Move" || result.ContainerId != expected.Plan.Command.Source.ContainerId || result.InstanceId != expected.Plan.Command.Source.InstanceId ||
            result.Kind != expected.Plan.Kind || result.Fingerprint != expected.Plan.Fingerprint || result.TargetProof != expected.Plan.TargetProof || result.State is not ("Completed" or "Aborted" or "Unknown") ||
            result.State != "Completed" && (result.Version.Length != 0 || result.BindingCurrent) || result.State == "Completed" && result.PrivateMayExist ||
            result.State == "Unknown" && !result.PrivateMayExist ||
            result.State == "Completed" && (!result.HasDestinationPublished || !result.DestinationPublished || !result.HasSourceRemoved || !result.SourceRemoved || !result.HasSourceRetained || !result.SourceRetained) ||
            result.State == "Aborted" && (!result.HasDestinationPublished || result.DestinationPublished || !result.HasSourceRemoved || result.SourceRemoved || !result.HasSourceRetained || result.SourceRetained)) throw Invalid();
        if (result.State == "Completed") try { FileTransferPolicy.Hash(result.Version); } catch (ArgumentException) { throw Invalid(); }
        return result;
    }
    private static void SetFact(JsonElement value, string field, Action<bool> set)
    {
        var fact = value.GetProperty(field);
        if (fact.ValueKind == JsonValueKind.Null) return;
        if (fact.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw Invalid();
        set(fact.GetBoolean());
    }
    public static RpcException Failure(string? code)
    {
        var status = code switch
        {
            "CONTAINER_MOVE_NOT_FOUND" => StatusCode.NotFound,
            "CONTAINER_MOVE_BUSY" or "CONTAINER_MOVE_RECEIPT_LIMIT" or "FILES_OPERATION_BUSY" or "FILES_CAPACITY_REQUIRED" or
                "FILES_SIZE_LIMIT" or "FILES_TREE_ENTRY_LIMIT" or "FILES_TREE_SIZE_LIMIT" or "FILES_TREE_DEPTH_LIMIT" or "FILES_TREE_MANIFEST_LIMIT" => StatusCode.ResourceExhausted,
            "CONTAINER_MOVE_REQUEST_INVALID" or "CONTAINER_MOVE_PLAN_INVALID" or "CONTAINER_MOVE_VERSION_INVALID" or "CONTAINER_FILES_PATH_INVALID" or
                "FILES_PLAN_INVALID" or "FILES_OPERATION_INVALID" or "FILES_TREE_COMMAND_INVALID" => StatusCode.InvalidArgument,
            "CONTAINER_MOVE_VERSION_CONFLICT" or "CONTAINER_MOVE_IDEMPOTENCY_CONFLICT" or "FILES_VERSION_CONFLICT" or "FILES_READ_CHANGED" or "FILES_TREE_CHANGED" or "FILES_IDEMPOTENCY_CONFLICT" => StatusCode.Aborted,
            "CONTAINER_MOVE_TIMEOUT" or "FILES_TREE_DEADLINE" or "FILES_INPUT_TIMEOUT" => StatusCode.DeadlineExceeded,
            "FILES_TREE_CANCELED" => StatusCode.Cancelled,
            "CONTAINER_MOVE_PROTECTED" or "CONTAINER_MOVE_OVERLAP" or "CONTAINER_PROTECTED" or "CONTAINER_UNSAFE_ISOLATION" or "CONTAINER_UNSAFE_MOUNT" or
                "CONTAINER_FILES_SHARED_CORE_MOUNT" or "CONTAINER_UPLOAD_PUBLISH_READ_ONLY" or "CONTAINER_UPLOAD_PUBLISH_MOUNT_ROOT" or "FILES_TREE_PROTECTED" or
                "FILES_TREE_OVERLAP" or "FILES_REGULAR_SINGLE_LINK_REQUIRED" or "CONTAINER_MOVE_SPECIAL_MODE_UNSUPPORTED" or "FILES_ROOT_FORBIDDEN" => StatusCode.PermissionDenied,
            "CONTAINER_MOVE_STATE_UNSAFE" or "CONTAINER_MOVE_MODULE_UNSAFE" or "CONTAINER_MOVE_RECEIPT_INVALID" or "CONTAINER_MOVE_PRIVATE_UNSAFE" or
                "CONTAINER_MOVE_PARENT_CHANGED" or "CONTAINER_MOVE_TARGET_CHANGED" or "CONTAINER_MOVE_OWNER_CHANGED" or "CONTAINER_MOVE_TARGET_UNAVAILABLE" or
                "CONTAINER_FILES_TARGET_MISSING" or "CONTAINER_FILES_INSTANCE_CHANGED" or "CONTAINER_FILES_TARGET_CHANGED" or "CONTAINER_FILES_ROOT_CHANGED" or
                "CONTAINER_NOT_RUNNING" or "FILES_PARENT_CHANGED" or "FILES_RECEIPT_UNSAFE" or "CONTAINER_MOVE_SOURCE_PARENT_CHANGED" or "CONTAINER_MOVE_SOURCE_CHANGED" or
                "CONTAINER_MOVE_RETENTION_LOCATION_INVALID" or "CONTAINER_MOVE_OPERATION_REQUIRED" or "CONTAINER_MOVE_OPERATION_MISMATCH" or "CONTAINER_MOVE_TARGET_UNCONFIRMED" => StatusCode.FailedPrecondition,
            _ => StatusCode.Unavailable
        };
        return new(new(status, status == StatusCode.Unavailable ? "CONTAINER_MOVE_UNAVAILABLE" : code!));
    }
    private static FileDirectoryIdentity ReadParent(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 2) throw Invalid();
        return new() { Device = value[0].GetUInt64(), Inode = value[1].GetUInt64() };
    }
    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString() ?? throw Invalid();
    private static void Fields(JsonElement value, params string[] fields)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Invalid();
        var names = value.EnumerateObject().Select(p => p.Name).ToArray();
        if (names.Length != fields.Length || names.Distinct(StringComparer.Ordinal).Count() != fields.Length || names.Any(p => !fields.Contains(p, StringComparer.Ordinal))) throw Invalid();
    }
    private static InvalidOperationException Invalid() => new("CONTAINER_MOVE_OUTPUT_INVALID");
}
