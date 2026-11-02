using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Google.Protobuf;
using Grpc.Core;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Infrastructure.Agent;

public sealed partial class AgentGateway : IContainerRecursivePermissionGateway
{
    public async Task RequireContainerRecursivePermissionWorkflowAsync(CancellationToken cancellationToken) => _ = await RecursivePermissionClient(cancellationToken);
    private async Task<ContainerRecursivePermissionService.ContainerRecursivePermissionServiceClient> RecursivePermissionClient(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var caps = await GetCapabilitiesAsync(timeout.Token);
        if (caps.ProtocolMajor != 1 || caps.ProtocolMinor < 49 || !caps.Capabilities.Contains("docker.files.recursive-permission-intents"))
            throw new RpcException(new(StatusCode.Unimplemented, "CONTAINER_RECURSIVE_PERMISSION_CAPABILITY_UNAVAILABLE"));
        return new(_channel);
    }
    private Metadata RecursivePermissionHeaders(string action, IMessage request)
    {
        if (_capabilityKey is not { Length: >= 32 }) throw new RpcException(new(StatusCode.PermissionDenied, "CONTAINER_RECURSIVE_PERMISSION_CAPABILITY_UNAVAILABLE"));
        var method = ContainerRecursivePermissionBinding.Service + action; var hash = ContainerRecursivePermissionBinding.Hash(request);
        var expires = DateTimeOffset.UtcNow.AddSeconds(45).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture); var nonce = Guid.NewGuid().ToString("N");
        var signature = HMACSHA256.HashData(_capabilityKey, Encoding.UTF8.GetBytes(ContainerRecursivePermissionBinding.Payload(expires, nonce, method, hash)));
        return new() { { "x-whaledeck-capability", expires + "." + nonce + "." + Convert.ToHexString(signature) }, { ContainerRecursivePermissionBinding.Header, hash } };
    }
    public async Task<ContainerRecursivePermissionPlanDto> PlanContainerRecursivePermissionAsync(TerminalActor actor, ContainerRecursivePermissionCommandDto command, CancellationToken cancellationToken)
    {
        ContainerRecursivePermissionValidation.Command(command);
        if (string.IsNullOrWhiteSpace(actor.Subject) || actor.Subject.Length > 256 || actor.Subject.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(actor.TraceId) || actor.TraceId.Length > 128 || actor.TraceId.Any(char.IsControl)) throw new ArgumentException("CONTAINER_RECURSIVE_PERMISSION_CONTEXT_INVALID");
        var request = new ContainerRecursivePermissionPlanRequest { Command = RecursivePermissionCommand(command), Context = new() { ActorSubject = actor.Subject, TraceId = actor.TraceId } };
        var client = await RecursivePermissionClient(cancellationToken);
        try
        {
            var response = await client.PlanAsync(request, RecursivePermissionHeaders("Plan", request), deadline: DateTime.UtcNow.AddSeconds(330), cancellationToken: cancellationToken);
            var plan = RecursivePermissionPlan(response);
            if (plan.Command != command) throw ContainerRecursivePermissionValidation.Invalid();
            return plan;
        }
        catch (RpcException error) when (RecursivePermissionPreflightCode(error) is { } code)
        { throw new ContainerRecursivePermissionPreflightRejectedException(code); }
    }
    private static string? RecursivePermissionPreflightCode(RpcException error) => (error.StatusCode, error.Status.Detail) switch
    {
        (StatusCode.Aborted, "CONTAINER_RECURSIVE_PERMISSION_VERSION_CONFLICT" or "CONTAINER_RECURSIVE_PERMISSION_TREE_CHANGED") => "CONTAINER_RECURSIVE_PERMISSION_VERSION_CONFLICT",
        (StatusCode.FailedPrecondition, "CONTAINER_RECURSIVE_PERMISSION_SOURCE_UNSUPPORTED") => "CONTAINER_RECURSIVE_PERMISSION_SOURCE_UNSUPPORTED",
        (StatusCode.FailedPrecondition, "CONTAINER_RECURSIVE_PERMISSION_EXTENDED_METADATA_REQUIRED") => "CONTAINER_RECURSIVE_PERMISSION_EXTENDED_METADATA_REQUIRED",
        (StatusCode.ResourceExhausted, "CONTAINER_RECURSIVE_PERMISSION_ATTRIBUTES_LIMIT") => "CONTAINER_RECURSIVE_PERMISSION_ATTRIBUTES_LIMIT",
        (StatusCode.ResourceExhausted, "CONTAINER_RECURSIVE_PERMISSION_TREE_LIMIT") => "CONTAINER_RECURSIVE_PERMISSION_TREE_LIMIT",
        (StatusCode.ResourceExhausted, "CONTAINER_RECURSIVE_PERMISSION_MANIFEST_LIMIT") => "CONTAINER_RECURSIVE_PERMISSION_MANIFEST_LIMIT",
        (StatusCode.PermissionDenied, "CONTAINER_RECURSIVE_PERMISSION_PROTECTED" or "CONTAINER_PROTECTED" or "CONTAINER_UNSAFE_ISOLATION" or "CONTAINER_UNSAFE_MOUNT" or
            "CONTAINER_FILES_SHARED_CORE_MOUNT" or "CONTAINER_UPLOAD_PUBLISH_READ_ONLY" or "CONTAINER_UPLOAD_PUBLISH_MOUNT_ROOT") => "CONTAINER_RECURSIVE_PERMISSION_PROTECTED",
        (StatusCode.FailedPrecondition, "CONTAINER_RECURSIVE_PERMISSION_ROOT_CHANGED" or "CONTAINER_FILES_TARGET_MISSING" or "CONTAINER_FILES_INSTANCE_CHANGED" or
            "CONTAINER_FILES_TARGET_CHANGED" or "CONTAINER_FILES_ROOT_CHANGED" or "CONTAINER_NOT_RUNNING") => "CONTAINER_RECURSIVE_PERMISSION_TARGET_CHANGED",
        _ => null
    };
    public Task<ContainerRecursivePermissionReceiptDto> ApplyContainerRecursivePermissionAsync(ContainerRecursivePermissionOperationDto operation, CancellationToken cancellationToken) => RecursivePermissionAction("Apply", operation, cancellationToken);
    public Task<ContainerRecursivePermissionReceiptDto> DiscardContainerRecursivePermissionAsync(ContainerRecursivePermissionOperationDto operation, CancellationToken cancellationToken) => RecursivePermissionAction("Discard", operation, cancellationToken);
    public async Task<ContainerRecursivePermissionReceiptDto?> InspectContainerRecursivePermissionAsync(ContainerRecursivePermissionOperationDto operation, CancellationToken cancellationToken)
    {
        try { return await RecursivePermissionAction("Inspect", operation, cancellationToken); }
        catch (RpcException error) when (error.StatusCode == StatusCode.NotFound && error.Status.Detail == "CONTAINER_RECURSIVE_PERMISSION_NOT_FOUND") { return null; }
    }
    private async Task<ContainerRecursivePermissionReceiptDto> RecursivePermissionAction(string action, ContainerRecursivePermissionOperationDto operation, CancellationToken cancellationToken)
    {
        ContainerRecursivePermissionValidation.Operation(operation);
        var request = new ContainerRecursivePermissionRequest { OperationId = operation.Id.ToString("N"), Owner = operation.Owner,
            Plan = RecursivePermissionPlan(operation.Plan), Context = UploadContext(operation.Context) };
        var client = await RecursivePermissionClient(cancellationToken);
        var headers = RecursivePermissionHeaders(action, request); var deadline = DateTime.UtcNow.AddSeconds(330);
        var response = action switch
        {
            "Apply" => await client.ApplyAsync(request, headers, deadline, cancellationToken),
            "Inspect" => await client.InspectAsync(request, headers, deadline, cancellationToken),
            "Discard" => await client.DiscardAsync(request, headers, deadline, cancellationToken),
            _ => throw new ArgumentException("CONTAINER_RECURSIVE_PERMISSION_ACTION_INVALID")
        };
        if (response.OperationId != request.OperationId) throw ContainerRecursivePermissionValidation.Invalid();
        var receipt = new ContainerRecursivePermissionReceiptDto(operation.Id, response.ContainerId, response.InstanceId, response.Fingerprint,
            response.TargetProof, response.State, response.BindingCurrent, response.Replayed, response.CompletedCount, response.ChangedCount,
            response.AbortedCount, response.NotStartedCount, response.UnknownCount, response.HistoryMayExist, response.HistoryVerified,
            response.HistoryDigest, response.HistoryAvailable, response.OutcomeDigest);
        ContainerRecursivePermissionValidation.Receipt(operation, receipt); return receipt;
    }
    private static ContainerRecursivePermissionCommand RecursivePermissionCommand(ContainerRecursivePermissionCommandDto command) => new()
    { Location = ContainerLocation(command.Location), FileMode = command.FileMode, DirectoryMode = command.DirectoryMode, ExpectedVersion = command.ExpectedVersion };
    private static ContainerRecursivePermissionPlan RecursivePermissionPlan(ContainerRecursivePermissionPlanDto plan) => new()
    {
        SchemaVersion = plan.SchemaVersion, Command = RecursivePermissionCommand(plan.Command), TargetProof = plan.TargetProof,
        ParentIdentity = new() { Device = plan.ParentIdentity.Device, Inode = plan.ParentIdentity.Inode }, ParentVersion = plan.ParentVersion,
        OwnerUid = plan.OwnerUid, OwnerGid = plan.OwnerGid, Fingerprint = plan.Fingerprint,
        RootIdentity = new() { Device = plan.RootIdentity.Device, Inode = plan.RootIdentity.Inode, Mode = plan.RootIdentity.Mode,
            OwnerUid = plan.RootIdentity.OwnerUid, OwnerGid = plan.RootIdentity.OwnerGid, LinkCount = plan.RootIdentity.LinkCount,
            SizeBytes = plan.RootIdentity.SizeBytes, ModifiedAtUnixNs = plan.RootIdentity.ModifiedAtUnixNs },
        RootChangedAtUnixNs = plan.RootChangedAtUnixNs, ManifestDigest = plan.ManifestDigest, EntryCount = plan.EntryCount, FileCount = plan.FileCount,
        DirectoryCount = plan.DirectoryCount, ChangeCount = plan.ChangeCount
    };
    private static ContainerRecursivePermissionPlanDto RecursivePermissionPlan(ContainerRecursivePermissionPlan response)
    {
        if (response.Command?.Location is not { } location || response.ParentIdentity is null || response.RootIdentity is null) throw ContainerRecursivePermissionValidation.Invalid();
        var plan = new ContainerRecursivePermissionPlanDto(response.SchemaVersion,
            new(new(location.ContainerId, location.InstanceId, location.Path), response.Command.FileMode, response.Command.DirectoryMode, response.Command.ExpectedVersion),
            response.TargetProof, new(response.ParentIdentity.Device, response.ParentIdentity.Inode), response.ParentVersion, response.OwnerUid, response.OwnerGid, response.Fingerprint,
            new(response.RootIdentity.Device, response.RootIdentity.Inode, response.RootIdentity.Mode, response.RootIdentity.OwnerUid,
                response.RootIdentity.OwnerGid, response.RootIdentity.LinkCount, response.RootIdentity.SizeBytes, response.RootIdentity.ModifiedAtUnixNs),
            response.RootChangedAtUnixNs, response.ManifestDigest, response.EntryCount, response.FileCount, response.DirectoryCount, response.ChangeCount);
        try { ContainerRecursivePermissionValidation.Plan(plan); } catch (ArgumentException) { throw ContainerRecursivePermissionValidation.Invalid(); }
        return plan;
    }
}
