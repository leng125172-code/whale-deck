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

public sealed partial class AgentGateway : IContainerRecycleGateway
{
    public async Task RequireContainerRecycleAsync(CancellationToken cancellationToken) => _ = await RecycleClient(cancellationToken);
    private async Task<ContainerRecycleService.ContainerRecycleServiceClient> RecycleClient(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var caps = await GetCapabilitiesAsync(timeout.Token);
        if (caps.ProtocolMajor != 1 || caps.ProtocolMinor < 56 || !caps.Capabilities.Contains("docker.files.recycle"))
            throw new RpcException(new(StatusCode.Unimplemented, "CONTAINER_RECYCLE_CAPABILITY_UNAVAILABLE"));
        return new(_channel);
    }
    private Metadata RecycleHeaders(string action, IMessage request)
    {
        if (_capabilityKey is not { Length: >= 32 }) throw new RpcException(new(StatusCode.PermissionDenied, "CONTAINER_RECYCLE_CAPABILITY_UNAVAILABLE"));
        var method = ContainerRecycleBinding.Service + action; var hash = ContainerRecycleBinding.Hash(request);
        var expires = DateTimeOffset.UtcNow.AddSeconds(45).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture); var nonce = Guid.NewGuid().ToString("N");
        var signature = HMACSHA256.HashData(_capabilityKey, Encoding.UTF8.GetBytes(ContainerRecycleBinding.Payload(expires, nonce, method, hash)));
        return new() { { "x-whaledeck-capability", expires + "." + nonce + "." + Convert.ToHexString(signature) }, { ContainerRecycleBinding.Header, hash } };
    }
    public async Task<ContainerRecyclePlanDto> PlanContainerRecycleAsync(TerminalActor actor, ContainerRecycleCommandDto command, CancellationToken cancellationToken)
    {
        ContainerRecycleValidation.Command(command);
        if (string.IsNullOrWhiteSpace(actor.Subject) || actor.Subject.Length > 256 || actor.Subject.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(actor.TraceId) || actor.TraceId.Length > 128 || actor.TraceId.Any(char.IsControl)) throw new ArgumentException("CONTAINER_RECYCLE_CONTEXT_INVALID");
        var request = new ContainerRecyclePlanRequest { Command = RecycleCommand(command), Context = new() { ActorSubject = actor.Subject, TraceId = actor.TraceId } };
        var client = await RecycleClient(cancellationToken);
        try
        {
            var response = await client.PlanAsync(request, RecycleHeaders("Plan", request), deadline: DateTime.UtcNow.AddSeconds(120), cancellationToken: cancellationToken);
            var plan = RecyclePlan(response);
            if (plan.Command != command) throw ContainerRecycleValidation.Invalid();
            return plan;
        }
        catch (RpcException error) when (RecyclePreflightCode(error) is { } code)
        { throw new ContainerRecyclePreflightRejectedException(code); }
    }
    private static string? RecyclePreflightCode(RpcException error) => (error.StatusCode, error.Status.Detail) switch
    {
        (StatusCode.Aborted, "CONTAINER_RECYCLE_SOURCE_CHANGED") => "CONTAINER_RECYCLE_SOURCE_CHANGED",
        (StatusCode.FailedPrecondition, "CONTAINER_RECYCLE_SOURCE_UNSUPPORTED") => "CONTAINER_RECYCLE_SOURCE_UNSUPPORTED",
        (StatusCode.FailedPrecondition, "CONTAINER_RECYCLE_EXTENDED_METADATA_REQUIRED") => "CONTAINER_RECYCLE_EXTENDED_METADATA_REQUIRED",
        (StatusCode.ResourceExhausted, "CONTAINER_RECYCLE_ATTRIBUTES_LIMIT") => "CONTAINER_RECYCLE_ATTRIBUTES_LIMIT",
        (StatusCode.PermissionDenied, "CONTAINER_RECYCLE_PROTECTED" or "CONTAINER_PROTECTED" or "CONTAINER_UNSAFE_ISOLATION" or "CONTAINER_UNSAFE_MOUNT" or
            "CONTAINER_FILES_SHARED_CORE_MOUNT" or "CONTAINER_UPLOAD_PUBLISH_READ_ONLY" or "CONTAINER_UPLOAD_PUBLISH_MOUNT_ROOT") => "CONTAINER_RECYCLE_PROTECTED",
        (StatusCode.FailedPrecondition, "CONTAINER_RECYCLE_TARGET_CHANGED" or "CONTAINER_FILES_TARGET_MISSING" or "CONTAINER_FILES_INSTANCE_CHANGED" or
            "CONTAINER_FILES_TARGET_CHANGED" or "CONTAINER_FILES_ROOT_CHANGED" or "CONTAINER_NOT_RUNNING") => "CONTAINER_RECYCLE_TARGET_CHANGED",
        _ => null
    };
    public Task<ContainerRecycleReceiptDto> ApplyContainerRecycleAsync(ContainerRecycleOperationDto operation, CancellationToken cancellationToken) => RecycleAction("Apply", operation, cancellationToken);
    public Task<ContainerRecycleReceiptDto> DiscardContainerRecycleAsync(ContainerRecycleOperationDto operation, CancellationToken cancellationToken) => RecycleAction("Discard", operation, cancellationToken);
    public async Task<ContainerRecycleReceiptDto?> InspectContainerRecycleAsync(ContainerRecycleOperationDto operation, CancellationToken cancellationToken)
    {
        try { return await RecycleAction("Inspect", operation, cancellationToken); }
        catch (RpcException error) when (error.StatusCode == StatusCode.NotFound && error.Status.Detail == "CONTAINER_RECYCLE_NOT_FOUND") { return null; }
    }
    public Task<ContainerRecycleReceiptDto> RestoreContainerRecycleAsync(ContainerRecycleOperationDto operation, CancellationToken cancellationToken) => RecycleAction("Restore", operation, cancellationToken);
    private async Task<ContainerRecycleReceiptDto> RecycleAction(string action, ContainerRecycleOperationDto operation, CancellationToken cancellationToken)
    {
        ContainerRecycleValidation.Operation(operation, restoring: action == "Restore");
        var request = new ContainerRecycleRequest { OperationId = operation.Id.ToString("N"), Owner = operation.Owner,
            Plan = RecyclePlan(operation.Plan), Context = UploadContext(operation.Context) };
        var client = await RecycleClient(cancellationToken);
        var headers = RecycleHeaders(action, request); var deadline = DateTime.UtcNow.AddSeconds(120);
        var response = action switch
        {
            "Apply" => await client.ApplyAsync(request, headers, deadline, cancellationToken),
            "Inspect" => await client.InspectAsync(request, headers, deadline, cancellationToken),
            "Discard" => await client.DiscardAsync(request, headers, deadline, cancellationToken),
            "Restore" => await client.RestoreAsync(request, headers, deadline, cancellationToken),
            _ => throw new ArgumentException("CONTAINER_RECYCLE_ACTION_INVALID")
        };
        if (response.OperationId != request.OperationId) throw ContainerRecycleValidation.Invalid();
        var receipt = new ContainerRecycleReceiptDto(operation.Id, response.ContainerId, response.InstanceId, response.Fingerprint,
            response.TargetProof, response.State, response.BindingCurrent, response.Replayed, response.HistoryMayExist, response.HistoryVerified, response.HistoryDigest, response.HistoryAvailable,
            response.HasSourceRetained ? response.SourceRetained : null, response.HasSourceRestored ? response.SourceRestored : null);
        ContainerRecycleValidation.Receipt(operation, receipt); return receipt;
    }
    private static ContainerRecycleCommand RecycleCommand(ContainerRecycleCommandDto command) => new() { Location = ContainerLocation(command.Location), ExpectedVersion = command.ExpectedVersion };
    private static ContainerRecyclePlan RecyclePlan(ContainerRecyclePlanDto plan) => new()
    {
        SchemaVersion = plan.SchemaVersion, Command = RecycleCommand(plan.Command), TargetProof = plan.TargetProof,
        ParentIdentity = new() { Device = plan.ParentIdentity.Device, Inode = plan.ParentIdentity.Inode }, ParentVersion = plan.ParentVersion,
        OwnerUid = plan.OwnerUid, OwnerGid = plan.OwnerGid, Fingerprint = plan.Fingerprint,
        SourceIdentity = new() { Device = plan.SourceIdentity.Device, Inode = plan.SourceIdentity.Inode, Mode = plan.SourceIdentity.Mode,
            OwnerUid = plan.SourceIdentity.OwnerUid, OwnerGid = plan.SourceIdentity.OwnerGid, LinkCount = plan.SourceIdentity.LinkCount,
            SizeBytes = plan.SourceIdentity.SizeBytes, ModifiedAtUnixNs = plan.SourceIdentity.ModifiedAtUnixNs },
        SourceChangedAtUnixNs = plan.SourceChangedAtUnixNs, Sha256 = plan.Sha256,
        Attributes = { plan.Attributes.Select(a => new ContainerRecycleAttribute { Name = a.Name, Value = ByteString.CopyFrom(a.Value) }) }
    };
    private static ContainerRecyclePlanDto RecyclePlan(ContainerRecyclePlan response)
    {
        if (response.Command?.Location is not { } location || response.ParentIdentity is null || response.SourceIdentity is null) throw ContainerRecycleValidation.Invalid();
        var plan = new ContainerRecyclePlanDto(response.SchemaVersion, new(new(location.ContainerId, location.InstanceId, location.Path), response.Command.ExpectedVersion),
            response.TargetProof, new(response.ParentIdentity.Device, response.ParentIdentity.Inode), response.ParentVersion, response.OwnerUid, response.OwnerGid, response.Fingerprint,
            new(response.SourceIdentity.Device, response.SourceIdentity.Inode, response.SourceIdentity.Mode, response.SourceIdentity.OwnerUid,
                response.SourceIdentity.OwnerGid, response.SourceIdentity.LinkCount, response.SourceIdentity.SizeBytes, response.SourceIdentity.ModifiedAtUnixNs),
            response.SourceChangedAtUnixNs, response.Sha256, response.Attributes.Select(a => new ContainerRecycleAttributeDto(a.Name, a.Value.ToByteArray())).ToArray());
        try { ContainerRecycleValidation.Plan(plan); } catch (ArgumentException) { throw ContainerRecycleValidation.Invalid(); }
        return plan;
    }
}
