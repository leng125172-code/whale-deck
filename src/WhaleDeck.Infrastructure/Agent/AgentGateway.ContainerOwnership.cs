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

public sealed partial class AgentGateway : IContainerOwnershipGateway
{
    public async Task RequireContainerOwnershipWorkflowAsync(CancellationToken cancellationToken) => _ = await OwnershipClient(cancellationToken);
    private async Task<ContainerOwnershipService.ContainerOwnershipServiceClient> OwnershipClient(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var caps = await GetCapabilitiesAsync(timeout.Token);
        if (caps.ProtocolMajor != 1 || caps.ProtocolMinor < 48 || !caps.Capabilities.Contains("docker.files.ownership-intents"))
            throw new RpcException(new(StatusCode.Unimplemented, "CONTAINER_OWNERSHIP_CAPABILITY_UNAVAILABLE"));
        return new(_channel);
    }
    private Metadata OwnershipHeaders(string action, IMessage request)
    {
        if (_capabilityKey is not { Length: >= 32 }) throw new RpcException(new(StatusCode.PermissionDenied, "CONTAINER_OWNERSHIP_CAPABILITY_UNAVAILABLE"));
        var method = ContainerOwnershipBinding.Service + action; var hash = ContainerOwnershipBinding.Hash(request);
        var expires = DateTimeOffset.UtcNow.AddSeconds(45).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture); var nonce = Guid.NewGuid().ToString("N");
        var signature = HMACSHA256.HashData(_capabilityKey, Encoding.UTF8.GetBytes(ContainerOwnershipBinding.Payload(expires, nonce, method, hash)));
        return new() { { "x-whaledeck-capability", expires + "." + nonce + "." + Convert.ToHexString(signature) }, { ContainerOwnershipBinding.Header, hash } };
    }
    public async Task<ContainerOwnershipPlanDto> PlanContainerOwnershipAsync(TerminalActor actor, ContainerOwnershipCommandDto command, CancellationToken cancellationToken)
    {
        ContainerOwnershipValidation.Command(command);
        if (string.IsNullOrWhiteSpace(actor.Subject) || actor.Subject.Length > 256 || actor.Subject.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(actor.TraceId) || actor.TraceId.Length > 128 || actor.TraceId.Any(char.IsControl)) throw new ArgumentException("CONTAINER_OWNERSHIP_CONTEXT_INVALID");
        var request = new ContainerOwnershipPlanRequest { Command = OwnershipCommand(command), Context = new() { ActorSubject = actor.Subject, TraceId = actor.TraceId } };
        var client = await OwnershipClient(cancellationToken);
        try
        {
            var response = await client.PlanAsync(request, OwnershipHeaders("Plan", request), deadline: DateTime.UtcNow.AddSeconds(60), cancellationToken: cancellationToken);
            var plan = OwnershipPlan(response);
            if (plan.Command != command) throw ContainerOwnershipValidation.Invalid();
            return plan;
        }
        catch (RpcException error) when (OwnershipPreflightCode(error) is { } code)
        { throw new ContainerOwnershipPreflightRejectedException(code); }
    }
    private static string? OwnershipPreflightCode(RpcException error) => (error.StatusCode, error.Status.Detail) switch
    {
        (StatusCode.Aborted, "CONTAINER_OWNERSHIP_VERSION_CONFLICT") => "CONTAINER_OWNERSHIP_VERSION_CONFLICT",
        (StatusCode.FailedPrecondition, "CONTAINER_OWNERSHIP_SOURCE_UNSUPPORTED") => "CONTAINER_OWNERSHIP_SOURCE_UNSUPPORTED",
        (StatusCode.FailedPrecondition, "CONTAINER_OWNERSHIP_EXTENDED_METADATA_REQUIRED") => "CONTAINER_OWNERSHIP_EXTENDED_METADATA_REQUIRED",
        (StatusCode.ResourceExhausted, "CONTAINER_OWNERSHIP_ATTRIBUTES_LIMIT") => "CONTAINER_OWNERSHIP_ATTRIBUTES_LIMIT",
        (StatusCode.PermissionDenied, "CONTAINER_OWNERSHIP_PROTECTED" or "CONTAINER_PROTECTED" or "CONTAINER_UNSAFE_ISOLATION" or "CONTAINER_UNSAFE_MOUNT" or
            "CONTAINER_FILES_SHARED_CORE_MOUNT" or "CONTAINER_UPLOAD_PUBLISH_READ_ONLY" or "CONTAINER_UPLOAD_PUBLISH_MOUNT_ROOT") => "CONTAINER_OWNERSHIP_PROTECTED",
        (StatusCode.FailedPrecondition, "CONTAINER_FILES_TARGET_MISSING" or "CONTAINER_FILES_INSTANCE_CHANGED" or "CONTAINER_FILES_TARGET_CHANGED" or "CONTAINER_FILES_ROOT_CHANGED" or
            "CONTAINER_NOT_RUNNING") => "CONTAINER_OWNERSHIP_TARGET_CHANGED",
        _ => null
    };
    public Task<ContainerOwnershipReceiptDto> ApplyContainerOwnershipAsync(ContainerOwnershipOperationDto operation, CancellationToken cancellationToken) => OwnershipAction("Apply", operation, cancellationToken);
    public Task<ContainerOwnershipReceiptDto> DiscardContainerOwnershipAsync(ContainerOwnershipOperationDto operation, CancellationToken cancellationToken) => OwnershipAction("Discard", operation, cancellationToken);
    public async Task<ContainerOwnershipReceiptDto?> InspectContainerOwnershipAsync(ContainerOwnershipOperationDto operation, CancellationToken cancellationToken)
    {
        try { return await OwnershipAction("Inspect", operation, cancellationToken); }
        catch (RpcException error) when (error.StatusCode == StatusCode.NotFound && error.Status.Detail == "CONTAINER_OWNERSHIP_NOT_FOUND") { return null; }
    }
    private async Task<ContainerOwnershipReceiptDto> OwnershipAction(string action, ContainerOwnershipOperationDto operation, CancellationToken cancellationToken)
    {
        ContainerOwnershipValidation.Operation(operation);
        var request = new ContainerOwnershipRequest { OperationId = operation.Id.ToString("N"), Owner = operation.Owner,
            Plan = OwnershipPlan(operation.Plan), Context = UploadContext(operation.Context) };
        var client = await OwnershipClient(cancellationToken);
        var headers = OwnershipHeaders(action, request); var deadline = DateTime.UtcNow.AddSeconds(60);
        var response = action switch
        {
            "Apply" => await client.ApplyAsync(request, headers, deadline, cancellationToken),
            "Inspect" => await client.InspectAsync(request, headers, deadline, cancellationToken),
            "Discard" => await client.DiscardAsync(request, headers, deadline, cancellationToken),
            _ => throw new ArgumentException("CONTAINER_OWNERSHIP_ACTION_INVALID")
        };
        if (response.OperationId != request.OperationId) throw ContainerOwnershipValidation.Invalid();
        var receipt = new ContainerOwnershipReceiptDto(operation.Id, response.ContainerId, response.InstanceId, response.Fingerprint,
            response.TargetProof, response.State, response.Version, response.BindingCurrent, response.Replayed, response.Kind,
            response.HasOwnerConfirmed ? response.OwnerConfirmed : null, response.NoChange, response.HistoryMayExist, response.HistoryVerified, response.HistoryDigest);
        ContainerOwnershipValidation.Receipt(operation, receipt); return receipt;
    }
    private static ContainerOwnershipCommand OwnershipCommand(ContainerOwnershipCommandDto command) => new() { Location = ContainerLocation(command.Location), Uid = command.Uid, Gid = command.Gid, ExpectedVersion = command.ExpectedVersion };
    private static ContainerOwnershipPlan OwnershipPlan(ContainerOwnershipPlanDto plan) => new()
    {
        SchemaVersion = plan.SchemaVersion, Command = OwnershipCommand(plan.Command), TargetProof = plan.TargetProof,
        ParentIdentity = new() { Device = plan.ParentIdentity.Device, Inode = plan.ParentIdentity.Inode }, ParentVersion = plan.ParentVersion,
        OwnerUid = plan.OwnerUid, OwnerGid = plan.OwnerGid, Fingerprint = plan.Fingerprint,
        Kind = plan.Kind, SourceIdentity = new() { Device = plan.SourceIdentity.Device, Inode = plan.SourceIdentity.Inode, Mode = plan.SourceIdentity.Mode,
            OwnerUid = plan.SourceIdentity.OwnerUid, OwnerGid = plan.SourceIdentity.OwnerGid, LinkCount = plan.SourceIdentity.LinkCount,
            SizeBytes = plan.SourceIdentity.SizeBytes, ModifiedAtUnixNs = plan.SourceIdentity.ModifiedAtUnixNs },
        SourceChangedAtUnixNs = plan.SourceChangedAtUnixNs, AttributesDigest = plan.AttributesDigest, NoChange = plan.NoChange
    };
    private static ContainerOwnershipPlanDto OwnershipPlan(ContainerOwnershipPlan response)
    {
        if (response.Command?.Location is not { } location || !response.Command.HasUid || !response.Command.HasGid || response.ParentIdentity is null || response.SourceIdentity is null) throw ContainerOwnershipValidation.Invalid();
        var plan = new ContainerOwnershipPlanDto(response.SchemaVersion, new(new(location.ContainerId, location.InstanceId, location.Path), response.Command.Uid, response.Command.Gid, response.Command.ExpectedVersion),
            response.TargetProof, new(response.ParentIdentity.Device, response.ParentIdentity.Inode), response.ParentVersion, response.OwnerUid, response.OwnerGid, response.Fingerprint,
            response.Kind, new(response.SourceIdentity.Device, response.SourceIdentity.Inode, response.SourceIdentity.Mode, response.SourceIdentity.OwnerUid,
                response.SourceIdentity.OwnerGid, response.SourceIdentity.LinkCount, response.SourceIdentity.SizeBytes, response.SourceIdentity.ModifiedAtUnixNs),
            response.SourceChangedAtUnixNs, response.AttributesDigest, response.NoChange);
        try { ContainerOwnershipValidation.Plan(plan); } catch (ArgumentException) { throw ContainerOwnershipValidation.Invalid(); }
        return plan;
    }
}
