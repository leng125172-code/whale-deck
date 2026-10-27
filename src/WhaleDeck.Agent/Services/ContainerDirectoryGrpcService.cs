using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using Grpc.Core;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

/// <summary>Fixed mkdir protocol. Business authorization and permanent intents belong to Application.</summary>
public sealed class ContainerDirectoryGrpcService(FileTransferRunner runner) : ContainerDirectoryService.ContainerDirectoryServiceBase
{
    public override async Task<ContainerDirectoryPlan> Plan(ContainerDirectoryPlanRequest request, ServerCallContext context)
    {
        ContainerDirectoryPolicy.Command(request.Command); ContainerDirectoryPolicy.Context(request.Context, planning: true);
        ContainerDirectoryPolicy.RequireBinding(request, context);
        using var result = await runner.ContainerDirectoryOnceAsync("plan", JsonSerializer.Serialize(ContainerDirectoryPolicy.CommandInput(request.Command)), context.CancellationToken);
        return ContainerDirectoryPolicy.PlanOutput(result.RootElement, request.Command);
    }
    public override Task<ContainerDirectoryReceipt> Apply(ContainerDirectoryRequest request, ServerCallContext context) => Run("apply", request, context);
    public override Task<ContainerDirectoryReceipt> Inspect(ContainerDirectoryRequest request, ServerCallContext context) => Run("inspect", request, context);
    public override Task<ContainerDirectoryReceipt> Discard(ContainerDirectoryRequest request, ServerCallContext context) => Run("discard", request, context);
    private async Task<ContainerDirectoryReceipt> Run(string action, ContainerDirectoryRequest request, ServerCallContext context)
    {
        ContainerDirectoryPolicy.Request(request); ContainerDirectoryPolicy.RequireBinding(request, context);
        using var result = await runner.ContainerDirectoryOnceAsync(action, JsonSerializer.Serialize(new
            { operationId = request.OperationId, owner = request.Owner, plan = ContainerDirectoryPolicy.PlanInput(request.Plan) }), context.CancellationToken);
        return ContainerDirectoryPolicy.Receipt(result.RootElement, request);
    }
}

internal static class ContainerDirectoryPolicy
{
    public static void RequireBinding(IMessage request, ServerCallContext context)
    {
        var expected = context.RequestHeaders.GetValue(ContainerDirectoryBinding.Header);
        var actual = ContainerDirectoryBinding.Hash(request);
        if (expected is not { Length: 64 } || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(actual), Encoding.ASCII.GetBytes(expected)))
            throw new RpcException(new(StatusCode.PermissionDenied, "CONTAINER_DIRECTORY_BINDING_INVALID"));
    }
    public static void Context(RequestContext? context, bool planning = false)
    {
        if (context is null || context.Capability.Length != 0 || string.IsNullOrWhiteSpace(context.ActorSubject) || context.ActorSubject.Length > 256 ||
            context.ActorSubject.Any(char.IsControl) || string.IsNullOrWhiteSpace(context.TraceId) || context.TraceId.Length > 128 || context.TraceId.Any(char.IsControl))
            throw new ArgumentException("CONTAINER_DIRECTORY_CONTEXT_INVALID");
        if (planning)
        {
            if (context.JobId.Length != 0 || context.IdempotencyKey.Length != 0) throw new ArgumentException("CONTAINER_DIRECTORY_CONTEXT_INVALID");
        }
        else
        { FileTransferPolicy.Identity(context.JobId); FileTransferPolicy.Identity(context.IdempotencyKey); }
    }
    public static void Command(ContainerDirectoryCommand? command)
    {
        if (command is null) throw new ArgumentException("CONTAINER_DIRECTORY_COMMAND_REQUIRED");
        ContainerFilePolicy.Location(command.Location);
        if (command.Mode.Length != 4 || command.Mode[0] != '0' || command.Mode.Skip(1).Any(c => c is < '0' or > '7'))
            throw new ArgumentException("CONTAINER_DIRECTORY_MODE_INVALID");
    }
    public static object CommandInput(ContainerDirectoryCommand command) => new
        { containerId = command.Location.ContainerId, instanceId = command.Location.InstanceId, path = command.Location.Path, mode = command.Mode };
    public static void Plan(ContainerDirectoryPlan? plan)
    {
        if (plan is null || plan.SchemaVersion != 1 || plan.ParentIdentity is not { Inode: > 0 } || plan.OwnerUid == uint.MaxValue || plan.OwnerGid == uint.MaxValue)
            throw new ArgumentException("CONTAINER_DIRECTORY_PLAN_INVALID");
        Command(plan.Command); FileTransferPolicy.Hash(plan.TargetProof); FileTransferPolicy.Hash(plan.ParentVersion); FileTransferPolicy.Hash(plan.Fingerprint);
    }
    public static object PlanInput(ContainerDirectoryPlan plan)
    {
        Plan(plan);
        return new { schemaVersion = plan.SchemaVersion, command = CommandInput(plan.Command), targetProof = plan.TargetProof,
            parentIdentity = new[] { plan.ParentIdentity.Device, plan.ParentIdentity.Inode }, parentVersion = plan.ParentVersion,
            uid = plan.OwnerUid, gid = plan.OwnerGid, fingerprint = plan.Fingerprint };
    }
    public static void Request(ContainerDirectoryRequest request)
    {
        FileTransferPolicy.Identity(request.OperationId); FileTransferPolicy.Hash(request.Owner); Plan(request.Plan); Context(request.Context);
        if (request.Context.JobId != request.OperationId) throw new ArgumentException("CONTAINER_DIRECTORY_CONTEXT_INVALID");
    }
    public static ContainerDirectoryPlan PlanOutput(JsonElement value, ContainerDirectoryCommand expected)
    {
        Fields(value, "schemaVersion", "command", "targetProof", "parentIdentity", "parentVersion", "uid", "gid", "fingerprint");
        var command = value.GetProperty("command"); Fields(command, "containerId", "instanceId", "path", "mode");
        var parent = value.GetProperty("parentIdentity");
        if (parent.ValueKind != JsonValueKind.Array || parent.GetArrayLength() != 2) throw Invalid();
        var result = new ContainerDirectoryPlan { SchemaVersion = value.GetProperty("schemaVersion").GetUInt32(),
            Command = new() { Location = new() { ContainerId = Text(command, "containerId"), InstanceId = Text(command, "instanceId"), Path = Text(command, "path") }, Mode = Text(command, "mode") },
            TargetProof = Text(value, "targetProof"), ParentIdentity = new() { Device = parent[0].GetUInt64(), Inode = parent[1].GetUInt64() },
            ParentVersion = Text(value, "parentVersion"), OwnerUid = value.GetProperty("uid").GetUInt32(), OwnerGid = value.GetProperty("gid").GetUInt32(), Fingerprint = Text(value, "fingerprint") };
        try { Plan(result); } catch (ArgumentException) { throw Invalid(); }
        if (!result.Command.Equals(expected)) throw Invalid();
        return result;
    }
    public static ContainerDirectoryReceipt Receipt(JsonElement value, ContainerDirectoryRequest expected)
    {
        Fields(value, "operationId", "containerId", "instanceId", "fingerprint", "targetProof", "state", "version", "bindingCurrent", "replayed");
        var result = new ContainerDirectoryReceipt { OperationId = Text(value, "operationId"), ContainerId = Text(value, "containerId"), InstanceId = Text(value, "instanceId"),
            Fingerprint = Text(value, "fingerprint"), TargetProof = Text(value, "targetProof"), State = Text(value, "state"), Version = Text(value, "version"),
            BindingCurrent = value.GetProperty("bindingCurrent").GetBoolean(), Replayed = value.GetProperty("replayed").GetBoolean() };
        if (result.OperationId != expected.OperationId || result.ContainerId != expected.Plan.Command.Location.ContainerId ||
            result.InstanceId != expected.Plan.Command.Location.InstanceId || result.Fingerprint != expected.Plan.Fingerprint || result.TargetProof != expected.Plan.TargetProof ||
            result.State is not ("Completed" or "Aborted" or "Unknown") || result.State != "Completed" && (result.Version.Length != 0 || result.BindingCurrent)) throw Invalid();
        if (result.State == "Completed") try { FileTransferPolicy.Hash(result.Version); } catch (ArgumentException) { throw Invalid(); }
        return result;
    }
    public static RpcException Failure(string? code)
    {
        var status = code switch
        {
        "CONTAINER_DIRECTORY_NOT_FOUND" => StatusCode.NotFound,
        "CONTAINER_DIRECTORY_BUSY" or "CONTAINER_DIRECTORY_RECEIPT_LIMIT" or "CONTAINER_DIRECTORY_CAPACITY_LOW" => StatusCode.ResourceExhausted,
        "CONTAINER_DIRECTORY_REQUEST_INVALID" or "CONTAINER_DIRECTORY_MODE_INVALID" or "CONTAINER_DIRECTORY_PLAN_INVALID" or "CONTAINER_FILES_PATH_INVALID" => StatusCode.InvalidArgument,
        "CONTAINER_DIRECTORY_TARGET_EXISTS" or "CONTAINER_DIRECTORY_VERSION_CONFLICT" or "CONTAINER_DIRECTORY_IDEMPOTENCY_CONFLICT" => StatusCode.Aborted,
        "CONTAINER_DIRECTORY_TIMEOUT" or "FILES_INPUT_TIMEOUT" => StatusCode.DeadlineExceeded,
        "CONTAINER_DIRECTORY_PROTECTED" or "CONTAINER_PROTECTED" or "CONTAINER_UNSAFE_ISOLATION" or "CONTAINER_UNSAFE_MOUNT" or
            "CONTAINER_FILES_SHARED_CORE_MOUNT" or "CONTAINER_UPLOAD_PUBLISH_READ_ONLY" or "CONTAINER_UPLOAD_PUBLISH_MOUNT_ROOT" => StatusCode.PermissionDenied,
        "CONTAINER_DIRECTORY_STATE_UNSAFE" or "CONTAINER_DIRECTORY_MODULE_UNSAFE" or "CONTAINER_DIRECTORY_RECEIPT_INVALID" or "CONTAINER_DIRECTORY_PRIVATE_UNSAFE" or
            "CONTAINER_DIRECTORY_PRIVATE_CHANGED" or "CONTAINER_DIRECTORY_PARENT_CHANGED" or "CONTAINER_DIRECTORY_OWNER_CHANGED" or "CONTAINER_DIRECTORY_TARGET_CHANGED" or
            "CONTAINER_FILES_TARGET_MISSING" or "CONTAINER_FILES_INSTANCE_CHANGED" or "CONTAINER_FILES_TARGET_CHANGED" or "CONTAINER_FILES_ROOT_CHANGED" or
            "CONTAINER_NOT_RUNNING" => StatusCode.FailedPrecondition,
        _ => StatusCode.Unavailable
        };
        return new(new(status, status == StatusCode.Unavailable ? "CONTAINER_DIRECTORY_UNAVAILABLE" : code!));
    }
    private static void Fields(JsonElement value, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Invalid();
        var fields = value.EnumerateObject().Select(p => p.Name).ToArray();
        if (fields.Length != names.Length || fields.Distinct(StringComparer.Ordinal).Count() != names.Length || fields.Any(p => !names.Contains(p, StringComparer.Ordinal))) throw Invalid();
    }
    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString() ?? throw Invalid();
    private static InvalidOperationException Invalid() => new("CONTAINER_DIRECTORY_OUTPUT_INVALID");
}
