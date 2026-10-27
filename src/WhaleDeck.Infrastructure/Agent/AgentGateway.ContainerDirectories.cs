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

public sealed partial class AgentGateway : IContainerDirectoryGateway
{
    public async Task RequireContainerDirectoryWorkflowAsync(CancellationToken cancellationToken) => _ = await DirectoryClient(cancellationToken);
    private async Task<ContainerDirectoryService.ContainerDirectoryServiceClient> DirectoryClient(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var caps = await GetCapabilitiesAsync(timeout.Token);
        if (caps.ProtocolMajor != 1 || caps.ProtocolMinor < 44 || !caps.Capabilities.Contains("docker.files.directory-intents"))
            throw new RpcException(new(StatusCode.Unimplemented, "CONTAINER_DIRECTORY_CAPABILITY_UNAVAILABLE"));
        return new(_channel);
    }
    private Metadata DirectoryHeaders(string action, IMessage request)
    {
        if (_capabilityKey is not { Length: >= 32 }) throw new RpcException(new(StatusCode.PermissionDenied, "CONTAINER_DIRECTORY_CAPABILITY_UNAVAILABLE"));
        var method = ContainerDirectoryBinding.Service + action; var hash = ContainerDirectoryBinding.Hash(request);
        var expires = DateTimeOffset.UtcNow.AddSeconds(45).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture); var nonce = Guid.NewGuid().ToString("N");
        var signature = HMACSHA256.HashData(_capabilityKey, Encoding.UTF8.GetBytes(ContainerDirectoryBinding.Payload(expires, nonce, method, hash)));
        return new() { { "x-whaledeck-capability", expires + "." + nonce + "." + Convert.ToHexString(signature) }, { ContainerDirectoryBinding.Header, hash } };
    }
    public async Task<ContainerDirectoryPlanDto> PlanContainerDirectoryAsync(TerminalActor actor, ContainerDirectoryCommandDto command, CancellationToken cancellationToken)
    {
        ContainerDirectoryValidation.Command(command);
        if (string.IsNullOrWhiteSpace(actor.Subject) || actor.Subject.Length > 256 || actor.Subject.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(actor.TraceId) || actor.TraceId.Length > 128 || actor.TraceId.Any(char.IsControl)) throw new ArgumentException("CONTAINER_DIRECTORY_CONTEXT_INVALID");
        var request = new ContainerDirectoryPlanRequest { Command = DirectoryCommand(command), Context = new() { ActorSubject = actor.Subject, TraceId = actor.TraceId } };
        var client = await DirectoryClient(cancellationToken);
        var response = await client.PlanAsync(request, DirectoryHeaders("Plan", request), deadline: DateTime.UtcNow.AddSeconds(30), cancellationToken: cancellationToken);
        var plan = DirectoryPlan(response);
        if (plan.Command != command) throw ContainerDirectoryValidation.Invalid();
        return plan;
    }
    public Task<ContainerDirectoryReceiptDto> ApplyContainerDirectoryAsync(ContainerDirectoryOperationDto operation, CancellationToken cancellationToken) => DirectoryAction("Apply", operation, cancellationToken);
    public Task<ContainerDirectoryReceiptDto> DiscardContainerDirectoryAsync(ContainerDirectoryOperationDto operation, CancellationToken cancellationToken) => DirectoryAction("Discard", operation, cancellationToken);
    public async Task<ContainerDirectoryReceiptDto?> InspectContainerDirectoryAsync(ContainerDirectoryOperationDto operation, CancellationToken cancellationToken)
    {
        try { return await DirectoryAction("Inspect", operation, cancellationToken); }
        catch (RpcException error) when (error.StatusCode == StatusCode.NotFound && error.Status.Detail == "CONTAINER_DIRECTORY_NOT_FOUND") { return null; }
    }
    private async Task<ContainerDirectoryReceiptDto> DirectoryAction(string action, ContainerDirectoryOperationDto operation, CancellationToken cancellationToken)
    {
        ContainerDirectoryValidation.Operation(operation);
        var request = new ContainerDirectoryRequest { OperationId = operation.Id.ToString("N"), Owner = operation.Owner,
            Plan = DirectoryPlan(operation.Plan), Context = UploadContext(operation.Context) };
        var client = await DirectoryClient(cancellationToken);
        var headers = DirectoryHeaders(action, request); var deadline = DateTime.UtcNow.AddSeconds(30);
        var response = action switch
        {
            "Apply" => await client.ApplyAsync(request, headers, deadline, cancellationToken),
            "Inspect" => await client.InspectAsync(request, headers, deadline, cancellationToken),
            "Discard" => await client.DiscardAsync(request, headers, deadline, cancellationToken),
            _ => throw new ArgumentException("CONTAINER_DIRECTORY_ACTION_INVALID")
        };
        if (response.OperationId != request.OperationId) throw ContainerDirectoryValidation.Invalid();
        var receipt = new ContainerDirectoryReceiptDto(operation.Id, response.ContainerId, response.InstanceId, response.Fingerprint,
            response.TargetProof, response.State, response.Version, response.BindingCurrent, response.Replayed);
        ContainerDirectoryValidation.Receipt(operation, receipt); return receipt;
    }
    private static ContainerDirectoryCommand DirectoryCommand(ContainerDirectoryCommandDto command) => new() { Location = ContainerLocation(command.Location), Mode = command.Mode };
    private static ContainerDirectoryPlan DirectoryPlan(ContainerDirectoryPlanDto plan) => new()
    {
        SchemaVersion = plan.SchemaVersion, Command = DirectoryCommand(plan.Command), TargetProof = plan.TargetProof,
        ParentIdentity = new() { Device = plan.ParentIdentity.Device, Inode = plan.ParentIdentity.Inode }, ParentVersion = plan.ParentVersion,
        OwnerUid = plan.OwnerUid, OwnerGid = plan.OwnerGid, Fingerprint = plan.Fingerprint
    };
    private static ContainerDirectoryPlanDto DirectoryPlan(ContainerDirectoryPlan response)
    {
        if (response.Command?.Location is not { } location || response.ParentIdentity is null) throw ContainerDirectoryValidation.Invalid();
        var plan = new ContainerDirectoryPlanDto(response.SchemaVersion, new(new(location.ContainerId, location.InstanceId, location.Path), response.Command.Mode),
            response.TargetProof, new(response.ParentIdentity.Device, response.ParentIdentity.Inode), response.ParentVersion, response.OwnerUid, response.OwnerGid, response.Fingerprint);
        try { ContainerDirectoryValidation.Plan(plan); } catch (ArgumentException) { throw ContainerDirectoryValidation.Invalid(); }
        return plan;
    }
}
