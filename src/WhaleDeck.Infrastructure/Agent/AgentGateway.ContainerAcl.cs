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

public sealed partial class AgentGateway : IContainerAclGateway
{
    public async Task RequireContainerAclWorkflowAsync(CancellationToken cancellationToken) => _ = await AclClient(cancellationToken);
    private async Task<ContainerAclService.ContainerAclServiceClient> AclClient(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var caps = await GetCapabilitiesAsync(timeout.Token);
        if (caps.ProtocolMajor != 1 || caps.ProtocolMinor < 51 || !caps.Capabilities.Contains("docker.files.acl-intents"))
            throw new RpcException(new(StatusCode.Unimplemented, "CONTAINER_ACL_CAPABILITY_UNAVAILABLE"));
        return new(_channel);
    }
    private Metadata AclHeaders(string action, IMessage request)
    {
        if (_capabilityKey is not { Length: >= 32 }) throw new RpcException(new(StatusCode.PermissionDenied, "CONTAINER_ACL_CAPABILITY_UNAVAILABLE"));
        var method = ContainerAclBinding.Service + action; var hash = ContainerAclBinding.Hash(request);
        var expires = DateTimeOffset.UtcNow.AddSeconds(45).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture); var nonce = Guid.NewGuid().ToString("N");
        var signature = HMACSHA256.HashData(_capabilityKey, Encoding.UTF8.GetBytes(ContainerAclBinding.Payload(expires, nonce, method, hash)));
        return new() { { "x-whaledeck-capability", expires + "." + nonce + "." + Convert.ToHexString(signature) }, { ContainerAclBinding.Header, hash } };
    }
    public async Task<ContainerAclPlanDto> PlanContainerAclAsync(TerminalActor actor, ContainerAclCommandDto command, CancellationToken cancellationToken)
    {
        ContainerAclValidation.Command(command);
        if (string.IsNullOrWhiteSpace(actor.Subject) || actor.Subject.Length > 256 || actor.Subject.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(actor.TraceId) || actor.TraceId.Length > 128 || actor.TraceId.Any(char.IsControl)) throw new ArgumentException("CONTAINER_ACL_CONTEXT_INVALID");
        var request = new ContainerAclPlanRequest { Command = AclCommand(command), Context = new() { ActorSubject = actor.Subject, TraceId = actor.TraceId } };
        var client = await AclClient(cancellationToken);
        try
        {
            var response = await client.PlanAsync(request, AclHeaders("Plan", request), deadline: DateTime.UtcNow.AddSeconds(60), cancellationToken: cancellationToken);
            var plan = AclPlan(response);
            if (!ContainerAclValidation.SameCommand(plan.Command, command)) throw ContainerAclValidation.Invalid();
            return plan;
        }
        catch (RpcException error) when (AclPreflightCode(error) is { } code)
        { throw new ContainerAclPreflightRejectedException(code); }
    }
    private static string? AclPreflightCode(RpcException error) => (error.StatusCode, error.Status.Detail) switch
    {
        (StatusCode.Aborted, "CONTAINER_ACL_VERSION_CONFLICT") => "CONTAINER_ACL_VERSION_CONFLICT",
        (StatusCode.FailedPrecondition, "CONTAINER_ACL_SOURCE_UNSUPPORTED") => "CONTAINER_ACL_SOURCE_UNSUPPORTED",
        (StatusCode.FailedPrecondition, "CONTAINER_ACL_EXTENDED_METADATA_REQUIRED") => "CONTAINER_ACL_EXTENDED_METADATA_REQUIRED",
        (StatusCode.FailedPrecondition, "CONTAINER_ACL_UNSUPPORTED") => "CONTAINER_ACL_UNSUPPORTED",
        (StatusCode.FailedPrecondition, "CONTAINER_ACL_MAPPING_UNPROVEN") => "CONTAINER_ACL_MAPPING_UNPROVEN",
        (StatusCode.FailedPrecondition, "CONTAINER_ACL_DEFAULT_REQUIRES_DIRECTORY") => "CONTAINER_ACL_DEFAULT_REQUIRES_DIRECTORY",
        (StatusCode.ResourceExhausted, "CONTAINER_ACL_ATTRIBUTES_LIMIT") => "CONTAINER_ACL_ATTRIBUTES_LIMIT",
        (StatusCode.PermissionDenied, "CONTAINER_ACL_PROTECTED" or "CONTAINER_PROTECTED" or "CONTAINER_UNSAFE_ISOLATION" or "CONTAINER_UNSAFE_MOUNT" or
            "CONTAINER_FILES_SHARED_CORE_MOUNT" or "CONTAINER_UPLOAD_PUBLISH_READ_ONLY" or "CONTAINER_UPLOAD_PUBLISH_MOUNT_ROOT") => "CONTAINER_ACL_PROTECTED",
        (StatusCode.FailedPrecondition, "CONTAINER_FILES_TARGET_MISSING" or "CONTAINER_FILES_INSTANCE_CHANGED" or "CONTAINER_FILES_TARGET_CHANGED" or "CONTAINER_FILES_ROOT_CHANGED" or
            "CONTAINER_NOT_RUNNING") => "CONTAINER_ACL_TARGET_CHANGED",
        _ => null
    };
    public Task<ContainerAclReceiptDto> ApplyContainerAclAsync(ContainerAclOperationDto operation, CancellationToken cancellationToken) => AclAction("Apply", operation, cancellationToken);
    public Task<ContainerAclReceiptDto> DiscardContainerAclAsync(ContainerAclOperationDto operation, CancellationToken cancellationToken) => AclAction("Discard", operation, cancellationToken);
    public async Task<ContainerAclReceiptDto?> InspectContainerAclAsync(ContainerAclOperationDto operation, CancellationToken cancellationToken)
    {
        try { return await AclAction("Inspect", operation, cancellationToken); }
        catch (RpcException error) when (error.StatusCode == StatusCode.NotFound && error.Status.Detail == "CONTAINER_ACL_NOT_FOUND") { return null; }
    }
    private async Task<ContainerAclReceiptDto> AclAction(string action, ContainerAclOperationDto operation, CancellationToken cancellationToken)
    {
        ContainerAclValidation.Operation(operation);
        var request = new ContainerAclRequest { OperationId = operation.Id.ToString("N"), Owner = operation.Owner,
            Plan = AclPlan(operation.Plan), Context = UploadContext(operation.Context) };
        var client = await AclClient(cancellationToken);
        var headers = AclHeaders(action, request); var deadline = DateTime.UtcNow.AddSeconds(60);
        var response = action switch
        {
            "Apply" => await client.ApplyAsync(request, headers, deadline, cancellationToken),
            "Inspect" => await client.InspectAsync(request, headers, deadline, cancellationToken),
            "Discard" => await client.DiscardAsync(request, headers, deadline, cancellationToken),
            _ => throw new ArgumentException("CONTAINER_ACL_ACTION_INVALID")
        };
        if (response.OperationId != request.OperationId) throw ContainerAclValidation.Invalid();
        var receipt = new ContainerAclReceiptDto(operation.Id, response.ContainerId, response.InstanceId, response.Fingerprint,
            response.TargetProof, response.State, response.Version, response.BindingCurrent, response.Replayed, response.Kind,
            response.HasAclConfirmed ? response.AclConfirmed : null, response.NoChange, response.HistoryMayExist, response.HistoryVerified, response.HistoryDigest, response.AclType, response.TargetMode);
        ContainerAclValidation.Receipt(operation, receipt); return receipt;
    }
    private static ContainerAclCommand AclCommand(ContainerAclCommandDto command) => new() { Location = ContainerLocation(command.Location), AclType = command.AclType, Entries = { command.Entries.Select(AclEntry) }, ExpectedVersion = command.ExpectedVersion };
    private static ContainerAclPlan AclPlan(ContainerAclPlanDto plan) => new()
    {
        SchemaVersion = plan.SchemaVersion, Command = AclCommand(plan.Command), TargetProof = plan.TargetProof,
        ParentIdentity = new() { Device = plan.ParentIdentity.Device, Inode = plan.ParentIdentity.Inode }, ParentVersion = plan.ParentVersion,
        OwnerUid = plan.OwnerUid, OwnerGid = plan.OwnerGid, Fingerprint = plan.Fingerprint,
        Kind = plan.Kind, SourceIdentity = new() { Device = plan.SourceIdentity.Device, Inode = plan.SourceIdentity.Inode, Mode = plan.SourceIdentity.Mode,
            OwnerUid = plan.SourceIdentity.OwnerUid, OwnerGid = plan.SourceIdentity.OwnerGid, LinkCount = plan.SourceIdentity.LinkCount,
            SizeBytes = plan.SourceIdentity.SizeBytes, ModifiedAtUnixNs = plan.SourceIdentity.ModifiedAtUnixNs },
        SourceChangedAtUnixNs = plan.SourceChangedAtUnixNs, AttributesDigest = plan.AttributesDigest, NoChange = plan.NoChange,
        AccessAcl = { plan.AccessAcl.Select(AclEntry) }, DefaultAcl = { plan.DefaultAcl.Select(AclEntry) }, TargetMode = plan.TargetMode
    };
    private static ContainerAclPlanDto AclPlan(ContainerAclPlan response)
    {
        if (response.Command?.Location is not { } location || response.ParentIdentity is null || response.SourceIdentity is null) throw ContainerAclValidation.Invalid();
        var plan = new ContainerAclPlanDto(response.SchemaVersion, new(new(location.ContainerId, location.InstanceId, location.Path), response.Command.AclType, response.Command.Entries.Select(AclEntry).ToArray(), response.Command.ExpectedVersion),
            response.TargetProof, new(response.ParentIdentity.Device, response.ParentIdentity.Inode), response.ParentVersion, response.OwnerUid, response.OwnerGid, response.Fingerprint,
            response.Kind, new(response.SourceIdentity.Device, response.SourceIdentity.Inode, response.SourceIdentity.Mode, response.SourceIdentity.OwnerUid,
                response.SourceIdentity.OwnerGid, response.SourceIdentity.LinkCount, response.SourceIdentity.SizeBytes, response.SourceIdentity.ModifiedAtUnixNs),
            response.SourceChangedAtUnixNs, response.AttributesDigest, response.NoChange,
            response.AccessAcl.Select(AclEntry).ToArray(), response.DefaultAcl.Select(AclEntry).ToArray(), response.TargetMode);
        try { ContainerAclValidation.Plan(plan); } catch (ArgumentException) { throw ContainerAclValidation.Invalid(); }
        return plan;
    }
    private static ContainerAclEntry AclEntry(ContainerAclEntryDto entry)
    {
        var value = new ContainerAclEntry { Tag = entry.Tag, Permissions = entry.Permissions };
        if (entry.Id is { } id) value.Id = id;
        return value;
    }
    private static ContainerAclEntryDto AclEntry(ContainerAclEntry entry)
    {
        if (!entry.HasPermissions) throw ContainerAclValidation.Invalid();
        return new(entry.Tag, entry.HasId ? entry.Id : null, entry.Permissions);
    }

}
