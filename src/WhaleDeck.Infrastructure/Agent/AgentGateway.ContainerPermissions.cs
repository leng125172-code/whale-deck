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

public sealed partial class AgentGateway : IContainerPermissionGateway
{
    public async Task RequireContainerPermissionWorkflowAsync(CancellationToken cancellationToken) => _ = await PermissionClient(cancellationToken);
    private async Task<ContainerPermissionService.ContainerPermissionServiceClient> PermissionClient(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var caps = await GetCapabilitiesAsync(timeout.Token);
        if (caps.ProtocolMajor != 1 || caps.ProtocolMinor < 47 || !caps.Capabilities.Contains("docker.files.permission-intents"))
            throw new RpcException(new(StatusCode.Unimplemented, "CONTAINER_PERMISSION_CAPABILITY_UNAVAILABLE"));
        return new(_channel);
    }
    private Metadata PermissionHeaders(string action, IMessage request)
    {
        if (_capabilityKey is not { Length: >= 32 }) throw new RpcException(new(StatusCode.PermissionDenied, "CONTAINER_PERMISSION_CAPABILITY_UNAVAILABLE"));
        var method = ContainerPermissionBinding.Service + action; var hash = ContainerPermissionBinding.Hash(request);
        var expires = DateTimeOffset.UtcNow.AddSeconds(45).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture); var nonce = Guid.NewGuid().ToString("N");
        var signature = HMACSHA256.HashData(_capabilityKey, Encoding.UTF8.GetBytes(ContainerPermissionBinding.Payload(expires, nonce, method, hash)));
        return new() { { "x-whaledeck-capability", expires + "." + nonce + "." + Convert.ToHexString(signature) }, { ContainerPermissionBinding.Header, hash } };
    }
    public async Task<ContainerPermissionPlanDto> PlanContainerPermissionAsync(TerminalActor actor, ContainerPermissionCommandDto command, CancellationToken cancellationToken)
    {
        ContainerPermissionValidation.Command(command);
        if (string.IsNullOrWhiteSpace(actor.Subject) || actor.Subject.Length > 256 || actor.Subject.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(actor.TraceId) || actor.TraceId.Length > 128 || actor.TraceId.Any(char.IsControl)) throw new ArgumentException("CONTAINER_PERMISSION_CONTEXT_INVALID");
        var request = new ContainerPermissionPlanRequest { Command = PermissionCommand(command), Context = new() { ActorSubject = actor.Subject, TraceId = actor.TraceId } };
        var client = await PermissionClient(cancellationToken);
        var response = await client.PlanAsync(request, PermissionHeaders("Plan", request), deadline: DateTime.UtcNow.AddSeconds(60), cancellationToken: cancellationToken);
        var plan = PermissionPlan(response);
        if (plan.Command != command) throw ContainerPermissionValidation.Invalid();
        return plan;
    }
    public Task<ContainerPermissionReceiptDto> ApplyContainerPermissionAsync(ContainerPermissionOperationDto operation, CancellationToken cancellationToken) => PermissionAction("Apply", operation, cancellationToken);
    public Task<ContainerPermissionReceiptDto> DiscardContainerPermissionAsync(ContainerPermissionOperationDto operation, CancellationToken cancellationToken) => PermissionAction("Discard", operation, cancellationToken);
    public async Task<ContainerPermissionReceiptDto?> InspectContainerPermissionAsync(ContainerPermissionOperationDto operation, CancellationToken cancellationToken)
    {
        try { return await PermissionAction("Inspect", operation, cancellationToken); }
        catch (RpcException error) when (error.StatusCode == StatusCode.NotFound && error.Status.Detail == "CONTAINER_PERMISSION_NOT_FOUND") { return null; }
    }
    private async Task<ContainerPermissionReceiptDto> PermissionAction(string action, ContainerPermissionOperationDto operation, CancellationToken cancellationToken)
    {
        ContainerPermissionValidation.Operation(operation);
        var request = new ContainerPermissionRequest { OperationId = operation.Id.ToString("N"), Owner = operation.Owner,
            Plan = PermissionPlan(operation.Plan), Context = UploadContext(operation.Context) };
        var client = await PermissionClient(cancellationToken);
        var headers = PermissionHeaders(action, request); var deadline = DateTime.UtcNow.AddSeconds(60);
        var response = action switch
        {
            "Apply" => await client.ApplyAsync(request, headers, deadline, cancellationToken),
            "Inspect" => await client.InspectAsync(request, headers, deadline, cancellationToken),
            "Discard" => await client.DiscardAsync(request, headers, deadline, cancellationToken),
            _ => throw new ArgumentException("CONTAINER_PERMISSION_ACTION_INVALID")
        };
        if (response.OperationId != request.OperationId) throw ContainerPermissionValidation.Invalid();
        var receipt = new ContainerPermissionReceiptDto(operation.Id, response.ContainerId, response.InstanceId, response.Fingerprint,
            response.TargetProof, response.State, response.Version, response.BindingCurrent, response.Replayed, response.Kind,
            response.HasModeConfirmed ? response.ModeConfirmed : null, response.NoChange, response.HistoryMayExist, response.HistoryVerified, response.HistoryDigest);
        ContainerPermissionValidation.Receipt(operation, receipt); return receipt;
    }
    private static ContainerPermissionCommand PermissionCommand(ContainerPermissionCommandDto command) => new() { Location = ContainerLocation(command.Location), Mode = command.Mode, ExpectedVersion = command.ExpectedVersion };
    private static ContainerPermissionPlan PermissionPlan(ContainerPermissionPlanDto plan) => new()
    {
        SchemaVersion = plan.SchemaVersion, Command = PermissionCommand(plan.Command), TargetProof = plan.TargetProof,
        ParentIdentity = new() { Device = plan.ParentIdentity.Device, Inode = plan.ParentIdentity.Inode }, ParentVersion = plan.ParentVersion,
        OwnerUid = plan.OwnerUid, OwnerGid = plan.OwnerGid, Fingerprint = plan.Fingerprint,
        Kind = plan.Kind, SourceIdentity = new() { Device = plan.SourceIdentity.Device, Inode = plan.SourceIdentity.Inode, Mode = plan.SourceIdentity.Mode,
            OwnerUid = plan.SourceIdentity.OwnerUid, OwnerGid = plan.SourceIdentity.OwnerGid, LinkCount = plan.SourceIdentity.LinkCount,
            SizeBytes = plan.SourceIdentity.SizeBytes, ModifiedAtUnixNs = plan.SourceIdentity.ModifiedAtUnixNs },
        SourceChangedAtUnixNs = plan.SourceChangedAtUnixNs, AttributesDigest = plan.AttributesDigest, NoChange = plan.NoChange
    };
    private static ContainerPermissionPlanDto PermissionPlan(ContainerPermissionPlan response)
    {
        if (response.Command?.Location is not { } location || response.ParentIdentity is null || response.SourceIdentity is null) throw ContainerPermissionValidation.Invalid();
        var plan = new ContainerPermissionPlanDto(response.SchemaVersion, new(new(location.ContainerId, location.InstanceId, location.Path), response.Command.Mode, response.Command.ExpectedVersion),
            response.TargetProof, new(response.ParentIdentity.Device, response.ParentIdentity.Inode), response.ParentVersion, response.OwnerUid, response.OwnerGid, response.Fingerprint,
            response.Kind, new(response.SourceIdentity.Device, response.SourceIdentity.Inode, response.SourceIdentity.Mode, response.SourceIdentity.OwnerUid,
                response.SourceIdentity.OwnerGid, response.SourceIdentity.LinkCount, response.SourceIdentity.SizeBytes, response.SourceIdentity.ModifiedAtUnixNs),
            response.SourceChangedAtUnixNs, response.AttributesDigest, response.NoChange);
        try { ContainerPermissionValidation.Plan(plan); } catch (ArgumentException) { throw ContainerPermissionValidation.Invalid(); }
        return plan;
    }
}
