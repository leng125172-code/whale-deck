using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using Grpc.Core;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

/// <summary>Container copy protocol. Human authorization/intents remain in Application.</summary>
public sealed class ContainerCopyGrpcService(FileTransferRunner runner) : ContainerCopyService.ContainerCopyServiceBase
{
    public override async Task<ContainerCopyPlan> Plan(ContainerCopyPlanRequest request, ServerCallContext context)
    {
        ContainerCopyPolicy.Command(request.Command); ContainerCopyPolicy.Context(request.Context, planning: true);
        ContainerCopyPolicy.RequireBinding(request, context);
        using var result = await runner.ContainerCopyOnceAsync("plan", JsonSerializer.Serialize(ContainerCopyPolicy.CommandInput(request.Command)), context.CancellationToken);
        return ContainerCopyPolicy.PlanOutput(result.RootElement, request.Command);
    }
    public override Task<ContainerCopyReceipt> Apply(ContainerCopyRequest request, ServerCallContext context) => Run("apply", request, context);
    public override Task<ContainerCopyReceipt> Inspect(ContainerCopyRequest request, ServerCallContext context) => Run("inspect", request, context);
    public override Task<ContainerCopyReceipt> Discard(ContainerCopyRequest request, ServerCallContext context) => Run("discard", request, context);
    private async Task<ContainerCopyReceipt> Run(string action, ContainerCopyRequest request, ServerCallContext context)
    {
        ContainerCopyPolicy.Request(request); ContainerCopyPolicy.RequireBinding(request, context);
        using var result = await runner.ContainerCopyOnceAsync(action, JsonSerializer.Serialize(new
            { operationId = request.OperationId, owner = request.Owner, plan = ContainerCopyPolicy.PlanInput(request.Plan) }), context.CancellationToken);
        return ContainerCopyPolicy.Receipt(result.RootElement, request);
    }
}

internal static class ContainerCopyPolicy
{
    internal const string SourceRoot = "container-copy-source", TargetRoot = "container-copy-target";
    public static void RequireBinding(IMessage request, ServerCallContext context)
    {
        var expected = context.RequestHeaders.GetValue(ContainerCopyBinding.Header); var actual = ContainerCopyBinding.Hash(request);
        if (request.CalculateSize() > 65536 || expected is not { Length: 64 } ||
            !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(actual), Encoding.ASCII.GetBytes(expected)))
            throw new RpcException(new(StatusCode.PermissionDenied, "CONTAINER_COPY_BINDING_INVALID"));
    }
    public static void Context(RequestContext? context, bool planning = false)
    {
        if (context is null || context.Capability.Length != 0 || string.IsNullOrWhiteSpace(context.ActorSubject) || context.ActorSubject.Length > 256 ||
            context.ActorSubject.Any(char.IsControl) || string.IsNullOrWhiteSpace(context.TraceId) || context.TraceId.Length > 128 || context.TraceId.Any(char.IsControl))
            throw new ArgumentException("CONTAINER_COPY_CONTEXT_INVALID");
        if (planning)
        {
            if (context.JobId.Length != 0 || context.IdempotencyKey.Length != 0) throw new ArgumentException("CONTAINER_COPY_CONTEXT_INVALID");
        }
        else { FileTransferPolicy.Identity(context.JobId); FileTransferPolicy.Identity(context.IdempotencyKey); }
    }
    public static void Command(ContainerCopyCommand? command)
    {
        if (command is null) throw new ArgumentException("CONTAINER_COPY_COMMAND_REQUIRED");
        ContainerFilePolicy.Location(command.Source);
        ContainerFilePolicy.Location(new() { ContainerId = command.Source.ContainerId, InstanceId = command.Source.InstanceId, Path = command.TargetPath });
        FileTransferPolicy.Hash(command.ExpectedVersion);
        var source = command.Source.Path; var target = command.TargetPath;
        if (source == target || source.StartsWith(target + "/", StringComparison.Ordinal) || target.StartsWith(source + "/", StringComparison.Ordinal))
            throw new ArgumentException("CONTAINER_COPY_OVERLAP");
    }
    public static object CommandInput(ContainerCopyCommand command) => new
        { containerId = command.Source.ContainerId, instanceId = command.Source.InstanceId, sourcePath = command.Source.Path,
          targetPath = command.TargetPath, expectedVersion = command.ExpectedVersion };
    private static object[] Identity(ContainerCopyPlan plan)
    {
        var value = plan.SourceIdentity;
        return [value.Device, value.Inode, value.Mode, value.OwnerUid, value.OwnerGid, value.LinkCount,
            value.SizeBytes, value.ModifiedAtUnixNs, plan.SourceChangedAtUnixNs];
    }
    internal static string HostVersion(ContainerCopyPlan plan) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(Identity(plan))));
    private static void Parent(FileDirectoryIdentity? value)
    { if (value is not { Inode: > 0 }) throw new ArgumentException("CONTAINER_COPY_PLAN_INVALID"); }
    public static void Plan(ContainerCopyPlan? plan)
    {
        if (plan is null || plan.SchemaVersion != 1 || plan.Kind is not ("File" or "Directory") || plan.SourceIdentity is not { Inode: > 0, LinkCount: > 0 } source ||
            source.Mode > ushort.MaxValue || (source.Mode & 0xE00) != 0 || source.OwnerUid == uint.MaxValue || source.OwnerGid == uint.MaxValue ||
            source.SizeBytes < 0 || plan.OwnerUid == uint.MaxValue || plan.OwnerGid == uint.MaxValue) throw new ArgumentException("CONTAINER_COPY_PLAN_INVALID");
        Command(plan.Command); Parent(plan.SourceParent); Parent(plan.TargetParent);
        FileTransferPolicy.Hash(plan.TargetProof); FileTransferPolicy.Hash(plan.TargetParentVersion); FileTransferPolicy.Hash(plan.Fingerprint);
        var expected = HostVersion(plan);
        if (plan.Kind == "File")
        {
            if ((source.Mode & 0xF000) != 0x8000 || source.LinkCount != 1 || source.SizeBytes > FileTransferPolicy.MaximumBytes ||
                plan.InnerPlanCase != ContainerCopyPlan.InnerPlanOneofCase.FilePlan) throw new ArgumentException("CONTAINER_COPY_PLAN_INVALID");
            var inner = plan.FilePlan; _ = FileOperationPolicy.PlanInput(inner);
            if (inner.Command.Kind != FileOperationKind.CopyFile || inner.Command.Source.RootId != SourceRoot || inner.Command.Target.RootId != TargetRoot ||
                inner.Command.Source.Path != plan.Command.Source.Path || inner.Command.Target.Path != plan.Command.TargetPath ||
                inner.Command.ExpectedVersion != expected || !inner.SourceIdentity.Equals(source) || !inner.SourceParent.Equals(plan.SourceParent) ||
                !inner.TargetParent.Equals(plan.TargetParent)) throw new ArgumentException("CONTAINER_COPY_PLAN_INVALID");
        }
        else
        {
            if ((source.Mode & 0xF000) != 0x4000 || plan.InnerPlanCase != ContainerCopyPlan.InnerPlanOneofCase.TreePlan) throw new ArgumentException("CONTAINER_COPY_PLAN_INVALID");
            var inner = plan.TreePlan; _ = FileTreeCopyPolicy.PlanInput(inner);
            if (inner.Command.Source.RootId != SourceRoot || inner.Command.Target.RootId != TargetRoot || inner.Command.Source.Path != plan.Command.Source.Path ||
                inner.Command.Target.Path != plan.Command.TargetPath || inner.Command.ExpectedVersion != expected || !inner.TargetParent.Equals(plan.TargetParent))
                throw new ArgumentException("CONTAINER_COPY_PLAN_INVALID");
        }
    }
    public static JsonElement PlanInput(ContainerCopyPlan plan)
    {
        Plan(plan);
        var result = JsonSerializer.SerializeToElement(new { schemaVersion = plan.SchemaVersion, command = CommandInput(plan.Command), kind = plan.Kind,
            targetProof = plan.TargetProof, sourceIdentity = Identity(plan), sourceParent = new[] { plan.SourceParent.Device, plan.SourceParent.Inode },
            targetParent = new[] { plan.TargetParent.Device, plan.TargetParent.Inode }, targetParentVersion = plan.TargetParentVersion,
            uid = plan.OwnerUid, gid = plan.OwnerGid, innerPlan = plan.Kind == "File" ? FileOperationPolicy.PlanInput(plan.FilePlan) : FileTreeCopyPolicy.PlanInput(plan.TreePlan),
            fingerprint = plan.Fingerprint });
        if (Encoding.UTF8.GetByteCount(result.GetRawText()) > 32768) throw new ArgumentException("CONTAINER_COPY_PLAN_INVALID");
        return result;
    }
    public static void Request(ContainerCopyRequest request)
    {
        FileTransferPolicy.Identity(request.OperationId); FileTransferPolicy.Hash(request.Owner); _ = PlanInput(request.Plan); Context(request.Context);
        if (request.Context.JobId != request.OperationId) throw new ArgumentException("CONTAINER_COPY_CONTEXT_INVALID");
    }
    public static ContainerCopyPlan PlanOutput(JsonElement value, ContainerCopyCommand expected)
    {
        Fields(value, "schemaVersion", "command", "kind", "targetProof", "sourceIdentity", "sourceParent", "targetParent", "targetParentVersion", "uid", "gid", "innerPlan", "fingerprint");
        var command = value.GetProperty("command"); Fields(command, "containerId", "instanceId", "sourcePath", "targetPath", "expectedVersion");
        var identity = value.GetProperty("sourceIdentity");
        if (identity.ValueKind != JsonValueKind.Array || identity.GetArrayLength() != 9) throw Invalid();
        var plan = new ContainerCopyPlan { SchemaVersion = value.GetProperty("schemaVersion").GetUInt32(),
            Command = new() { Source = new() { ContainerId = Text(command, "containerId"), InstanceId = Text(command, "instanceId"), Path = Text(command, "sourcePath") },
                TargetPath = Text(command, "targetPath"), ExpectedVersion = Text(command, "expectedVersion") },
            Kind = Text(value, "kind"), TargetProof = Text(value, "targetProof"), TargetParentVersion = Text(value, "targetParentVersion"), Fingerprint = Text(value, "fingerprint"),
            SourceIdentity = new() { Device = identity[0].GetUInt64(), Inode = identity[1].GetUInt64(), Mode = identity[2].GetUInt32(), OwnerUid = identity[3].GetUInt32(),
                OwnerGid = identity[4].GetUInt32(), LinkCount = identity[5].GetUInt64(), SizeBytes = identity[6].GetInt64(), ModifiedAtUnixNs = identity[7].GetInt64() },
            SourceChangedAtUnixNs = identity[8].GetInt64(), SourceParent = ReadParent(value.GetProperty("sourceParent")), TargetParent = ReadParent(value.GetProperty("targetParent")),
            OwnerUid = value.GetProperty("uid").GetUInt32(), OwnerGid = value.GetProperty("gid").GetUInt32() };
        if (!plan.Command.Equals(expected)) throw Invalid();
        if (plan.Kind == "File") plan.FilePlan = FileOperationPolicy.Plan(value.GetProperty("innerPlan"), new()
            { Kind = FileOperationKind.CopyFile, Source = new() { RootId = SourceRoot, Path = expected.Source.Path },
                Target = new() { RootId = TargetRoot, Path = expected.TargetPath }, ExpectedVersion = HostVersion(plan) });
        else if (plan.Kind == "Directory") plan.TreePlan = FileTreeCopyPolicy.Plan(value.GetProperty("innerPlan"), new()
            { Source = new() { RootId = SourceRoot, Path = expected.Source.Path }, Target = new() { RootId = TargetRoot, Path = expected.TargetPath }, ExpectedVersion = HostVersion(plan) });
        else throw Invalid();
        try { _ = PlanInput(plan); } catch (ArgumentException) { throw Invalid(); }
        return plan;
    }
    public static ContainerCopyReceipt Receipt(JsonElement value, ContainerCopyRequest expected)
    {
        Fields(value, "operationId", "containerId", "instanceId", "kind", "fingerprint", "targetProof", "state", "version", "bindingCurrent", "privateMayExist", "replayed");
        var result = new ContainerCopyReceipt { OperationId = Text(value, "operationId"), ContainerId = Text(value, "containerId"), InstanceId = Text(value, "instanceId"),
            Kind = Text(value, "kind"), Fingerprint = Text(value, "fingerprint"), TargetProof = Text(value, "targetProof"), State = Text(value, "state"), Version = Text(value, "version"),
            BindingCurrent = value.GetProperty("bindingCurrent").GetBoolean(), PrivateMayExist = value.GetProperty("privateMayExist").GetBoolean(), Replayed = value.GetProperty("replayed").GetBoolean() };
        if (result.OperationId != expected.OperationId || result.ContainerId != expected.Plan.Command.Source.ContainerId || result.InstanceId != expected.Plan.Command.Source.InstanceId ||
            result.Kind != expected.Plan.Kind || result.Fingerprint != expected.Plan.Fingerprint || result.TargetProof != expected.Plan.TargetProof || result.State is not ("Completed" or "Aborted" or "Unknown") ||
            result.State != "Completed" && (result.Version.Length != 0 || result.BindingCurrent) || result.State == "Completed" && result.PrivateMayExist ||
            result.State == "Unknown" && !result.PrivateMayExist) throw Invalid();
        if (result.State == "Completed") try { FileTransferPolicy.Hash(result.Version); } catch (ArgumentException) { throw Invalid(); }
        return result;
    }
    public static RpcException Failure(string? code)
    {
        var status = code switch
        {
            "CONTAINER_COPY_NOT_FOUND" => StatusCode.NotFound,
            "CONTAINER_COPY_BUSY" or "CONTAINER_COPY_RECEIPT_LIMIT" or "FILES_OPERATION_BUSY" or "FILES_CAPACITY_REQUIRED" or
                "FILES_SIZE_LIMIT" or "FILES_TREE_ENTRY_LIMIT" or "FILES_TREE_SIZE_LIMIT" or "FILES_TREE_DEPTH_LIMIT" or "FILES_TREE_MANIFEST_LIMIT" => StatusCode.ResourceExhausted,
            "CONTAINER_COPY_REQUEST_INVALID" or "CONTAINER_COPY_PLAN_INVALID" or "CONTAINER_COPY_VERSION_INVALID" or "CONTAINER_FILES_PATH_INVALID" => StatusCode.InvalidArgument,
            "CONTAINER_COPY_VERSION_CONFLICT" or "CONTAINER_COPY_IDEMPOTENCY_CONFLICT" or "FILES_VERSION_CONFLICT" or "FILES_READ_CHANGED" or "FILES_TREE_CHANGED" => StatusCode.Aborted,
            "CONTAINER_COPY_TIMEOUT" or "FILES_TREE_DEADLINE" or "FILES_INPUT_TIMEOUT" => StatusCode.DeadlineExceeded,
            "FILES_TREE_CANCELED" => StatusCode.Cancelled,
            "CONTAINER_COPY_PROTECTED" or "CONTAINER_COPY_OVERLAP" or "CONTAINER_PROTECTED" or "CONTAINER_UNSAFE_ISOLATION" or "CONTAINER_UNSAFE_MOUNT" or
                "CONTAINER_FILES_SHARED_CORE_MOUNT" or "CONTAINER_UPLOAD_PUBLISH_READ_ONLY" or "CONTAINER_UPLOAD_PUBLISH_MOUNT_ROOT" or "FILES_TREE_PROTECTED" or
                "FILES_TREE_OVERLAP" or "FILES_REGULAR_SINGLE_LINK_REQUIRED" or "CONTAINER_COPY_SPECIAL_MODE_UNSUPPORTED" => StatusCode.PermissionDenied,
            "CONTAINER_COPY_STATE_UNSAFE" or "CONTAINER_COPY_MODULE_UNSAFE" or "CONTAINER_COPY_RECEIPT_INVALID" or "CONTAINER_COPY_PRIVATE_UNSAFE" or
                "CONTAINER_COPY_PARENT_CHANGED" or "CONTAINER_COPY_TARGET_CHANGED" or "CONTAINER_COPY_OWNER_CHANGED" or "CONTAINER_COPY_TARGET_UNAVAILABLE" or
                "CONTAINER_FILES_TARGET_MISSING" or "CONTAINER_FILES_INSTANCE_CHANGED" or "CONTAINER_FILES_TARGET_CHANGED" or "CONTAINER_FILES_ROOT_CHANGED" or
                "CONTAINER_NOT_RUNNING" or "FILES_PARENT_CHANGED" or "FILES_RECEIPT_UNSAFE" => StatusCode.FailedPrecondition,
            _ => StatusCode.Unavailable
        };
        return new(new(status, status == StatusCode.Unavailable ? "CONTAINER_COPY_UNAVAILABLE" : code!));
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
    private static InvalidOperationException Invalid() => new("CONTAINER_COPY_OUTPUT_INVALID");
}
