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

public sealed partial class AgentGateway : IContainerRecursiveAclGateway
{
    public async Task RequireContainerRecursiveAclWorkflowAsync(CancellationToken cancellationToken) => _ = await RecursiveAclClient(cancellationToken);
    private async Task<ContainerRecursiveAclService.ContainerRecursiveAclServiceClient> RecursiveAclClient(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var caps = await GetCapabilitiesAsync(timeout.Token);
        if (caps.ProtocolMajor != 1 || caps.ProtocolMinor < 53 || !caps.Capabilities.Contains("docker.files.recursive-acl-intents"))
            throw new RpcException(new(StatusCode.Unimplemented, "CONTAINER_RECURSIVE_ACL_CAPABILITY_UNAVAILABLE"));
        return new(_channel);
    }
    private Metadata RecursiveAclHeaders(string action, IMessage request)
    {
        if (_capabilityKey is not { Length: >= 32 }) throw new RpcException(new(StatusCode.PermissionDenied, "CONTAINER_RECURSIVE_ACL_CAPABILITY_UNAVAILABLE"));
        var method = ContainerRecursiveAclBinding.Service + action; var hash = ContainerRecursiveAclBinding.Hash(request);
        var expires = DateTimeOffset.UtcNow.AddSeconds(45).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture); var nonce = Guid.NewGuid().ToString("N");
        var signature = HMACSHA256.HashData(_capabilityKey, Encoding.UTF8.GetBytes(ContainerRecursiveAclBinding.Payload(expires, nonce, method, hash)));
        return new() { { "x-whaledeck-capability", expires + "." + nonce + "." + Convert.ToHexString(signature) }, { ContainerRecursiveAclBinding.Header, hash } };
    }
    public async Task<ContainerRecursiveAclPlanDto> PlanContainerRecursiveAclAsync(TerminalActor actor, ContainerRecursiveAclCommandDto command, CancellationToken cancellationToken)
    {
        ContainerRecursiveAclValidation.Command(command);
        if (string.IsNullOrWhiteSpace(actor.Subject) || actor.Subject.Length > 256 || actor.Subject.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(actor.TraceId) || actor.TraceId.Length > 128 || actor.TraceId.Any(char.IsControl)) throw new ArgumentException("CONTAINER_RECURSIVE_ACL_CONTEXT_INVALID");
        var request = new ContainerRecursiveAclPlanRequest { Command = RecursiveAclCommand(command), Context = new() { ActorSubject = actor.Subject, TraceId = actor.TraceId } };
        var client = await RecursiveAclClient(cancellationToken);
        try
        {
            var response = await client.PlanAsync(request, RecursiveAclHeaders("Plan", request), deadline: DateTime.UtcNow.AddSeconds(330), cancellationToken: cancellationToken);
            var plan = RecursiveAclPlan(response);
            if (!ContainerRecursiveAclValidation.SameCommand(plan.Command, command)) throw ContainerRecursiveAclValidation.Invalid();
            return plan;
        }
        catch (RpcException error) when (RecursiveAclPreflightCode(error) is { } code)
        { throw new ContainerRecursiveAclPreflightRejectedException(code); }
    }
    private static string? RecursiveAclPreflightCode(RpcException error) => (error.StatusCode, error.Status.Detail) switch
    {
        (StatusCode.Aborted, "CONTAINER_RECURSIVE_ACL_VERSION_CONFLICT" or "CONTAINER_RECURSIVE_ACL_TREE_CHANGED") => "CONTAINER_RECURSIVE_ACL_VERSION_CONFLICT",
        (StatusCode.FailedPrecondition, "CONTAINER_RECURSIVE_ACL_SOURCE_UNSUPPORTED") => "CONTAINER_RECURSIVE_ACL_SOURCE_UNSUPPORTED",
        (StatusCode.FailedPrecondition, "CONTAINER_RECURSIVE_ACL_EXTENDED_METADATA_REQUIRED") => "CONTAINER_RECURSIVE_ACL_EXTENDED_METADATA_REQUIRED",
        (StatusCode.FailedPrecondition, "CONTAINER_RECURSIVE_ACL_MAPPING_UNPROVEN") => "CONTAINER_RECURSIVE_ACL_MAPPING_UNPROVEN",
        (StatusCode.FailedPrecondition, "CONTAINER_RECURSIVE_ACL_UNSUPPORTED") => "CONTAINER_RECURSIVE_ACL_UNSUPPORTED",
        (StatusCode.ResourceExhausted, "CONTAINER_RECURSIVE_ACL_ATTRIBUTES_LIMIT") => "CONTAINER_RECURSIVE_ACL_ATTRIBUTES_LIMIT",
        (StatusCode.ResourceExhausted, "CONTAINER_RECURSIVE_ACL_TREE_LIMIT") => "CONTAINER_RECURSIVE_ACL_TREE_LIMIT",
        (StatusCode.ResourceExhausted, "CONTAINER_RECURSIVE_ACL_MANIFEST_LIMIT") => "CONTAINER_RECURSIVE_ACL_MANIFEST_LIMIT",
        (StatusCode.PermissionDenied, "CONTAINER_RECURSIVE_ACL_PROTECTED" or "CONTAINER_PROTECTED" or "CONTAINER_UNSAFE_ISOLATION" or "CONTAINER_UNSAFE_MOUNT" or
            "CONTAINER_FILES_SHARED_CORE_MOUNT" or "CONTAINER_UPLOAD_PUBLISH_READ_ONLY" or "CONTAINER_UPLOAD_PUBLISH_MOUNT_ROOT") => "CONTAINER_RECURSIVE_ACL_PROTECTED",
        (StatusCode.FailedPrecondition, "CONTAINER_RECURSIVE_ACL_ROOT_CHANGED" or "CONTAINER_FILES_TARGET_MISSING" or "CONTAINER_FILES_INSTANCE_CHANGED" or
            "CONTAINER_FILES_TARGET_CHANGED" or "CONTAINER_FILES_ROOT_CHANGED" or "CONTAINER_NOT_RUNNING") => "CONTAINER_RECURSIVE_ACL_TARGET_CHANGED",
        _ => null
    };
    public Task<ContainerRecursiveAclReceiptDto> ApplyContainerRecursiveAclAsync(ContainerRecursiveAclOperationDto operation, CancellationToken cancellationToken) => RecursiveAclAction("Apply", operation, cancellationToken);
    public Task<ContainerRecursiveAclReceiptDto> DiscardContainerRecursiveAclAsync(ContainerRecursiveAclOperationDto operation, CancellationToken cancellationToken) => RecursiveAclAction("Discard", operation, cancellationToken);
    public async Task<ContainerRecursiveAclReceiptDto?> InspectContainerRecursiveAclAsync(ContainerRecursiveAclOperationDto operation, CancellationToken cancellationToken)
    {
        try { return await RecursiveAclAction("Inspect", operation, cancellationToken); }
        catch (RpcException error) when (error.StatusCode == StatusCode.NotFound && error.Status.Detail == "CONTAINER_RECURSIVE_ACL_NOT_FOUND") { return null; }
    }
    private async Task<ContainerRecursiveAclReceiptDto> RecursiveAclAction(string action, ContainerRecursiveAclOperationDto operation, CancellationToken cancellationToken)
    {
        ContainerRecursiveAclValidation.Operation(operation);
        var request = new ContainerRecursiveAclRequest { OperationId = operation.Id.ToString("N"), Owner = operation.Owner,
            Plan = RecursiveAclPlan(operation.Plan), Context = UploadContext(operation.Context) };
        var client = await RecursiveAclClient(cancellationToken);
        var headers = RecursiveAclHeaders(action, request); var deadline = DateTime.UtcNow.AddSeconds(330);
        var response = action switch
        {
            "Apply" => await client.ApplyAsync(request, headers, deadline, cancellationToken),
            "Inspect" => await client.InspectAsync(request, headers, deadline, cancellationToken),
            "Discard" => await client.DiscardAsync(request, headers, deadline, cancellationToken),
            _ => throw new ArgumentException("CONTAINER_RECURSIVE_ACL_ACTION_INVALID")
        };
        if (response.OperationId != request.OperationId) throw ContainerRecursiveAclValidation.Invalid();
        var receipt = new ContainerRecursiveAclReceiptDto(operation.Id, response.ContainerId, response.InstanceId, response.Fingerprint,
            response.TargetProof, response.State, response.BindingCurrent, response.Replayed, response.CompletedCount, response.ChangedCount,
            response.AbortedCount, response.NotStartedCount, response.UnknownCount, response.HistoryMayExist, response.HistoryVerified,
            response.HistoryDigest, response.HistoryAvailable, response.OutcomeDigest, response.AclType);
        ContainerRecursiveAclValidation.Receipt(operation, receipt); return receipt;
    }
    private static ContainerRecursiveAclCommand RecursiveAclCommand(ContainerRecursiveAclCommandDto command) => new()
    { Location = ContainerLocation(command.Location), AclType = command.AclType,
      FileEntries = command.FileEntries is null ? null : new() { Entries = { command.FileEntries.Select(RecursiveAclEntry) } },
      DirectoryEntries = new() { Entries = { command.DirectoryEntries.Select(RecursiveAclEntry) } }, ExpectedVersion = command.ExpectedVersion };
    private static ContainerRecursiveAclPlan RecursiveAclPlan(ContainerRecursiveAclPlanDto plan) => new()
    {
        SchemaVersion = plan.SchemaVersion, Command = RecursiveAclCommand(plan.Command), TargetProof = plan.TargetProof,
        ParentIdentity = new() { Device = plan.ParentIdentity.Device, Inode = plan.ParentIdentity.Inode }, ParentVersion = plan.ParentVersion,
        OwnerUid = plan.OwnerUid, OwnerGid = plan.OwnerGid, Fingerprint = plan.Fingerprint,
        RootIdentity = new() { Device = plan.RootIdentity.Device, Inode = plan.RootIdentity.Inode, Mode = plan.RootIdentity.Mode,
            OwnerUid = plan.RootIdentity.OwnerUid, OwnerGid = plan.RootIdentity.OwnerGid, LinkCount = plan.RootIdentity.LinkCount,
            SizeBytes = plan.RootIdentity.SizeBytes, ModifiedAtUnixNs = plan.RootIdentity.ModifiedAtUnixNs },
        RootChangedAtUnixNs = plan.RootChangedAtUnixNs, ManifestDigest = plan.ManifestDigest, EntryCount = plan.EntryCount, FileCount = plan.FileCount,
        DirectoryCount = plan.DirectoryCount, ChangeCount = plan.ChangeCount
    };
    private static ContainerRecursiveAclPlanDto RecursiveAclPlan(ContainerRecursiveAclPlan response)
    {
        if (response.Command?.Location is not { } location || response.Command.DirectoryEntries is null || response.Command.AclType == "access" && response.Command.FileEntries is null || response.ParentIdentity is null || response.RootIdentity is null) throw ContainerRecursiveAclValidation.Invalid();
        var plan = new ContainerRecursiveAclPlanDto(response.SchemaVersion,
            new(new(location.ContainerId, location.InstanceId, location.Path), response.Command.AclType, response.Command.FileEntries?.Entries.Select(RecursiveAclEntry).ToArray(), response.Command.DirectoryEntries.Entries.Select(RecursiveAclEntry).ToArray(), response.Command.ExpectedVersion),
            response.TargetProof, new(response.ParentIdentity.Device, response.ParentIdentity.Inode), response.ParentVersion, response.OwnerUid, response.OwnerGid, response.Fingerprint,
            new(response.RootIdentity.Device, response.RootIdentity.Inode, response.RootIdentity.Mode, response.RootIdentity.OwnerUid,
                response.RootIdentity.OwnerGid, response.RootIdentity.LinkCount, response.RootIdentity.SizeBytes, response.RootIdentity.ModifiedAtUnixNs),
            response.RootChangedAtUnixNs, response.ManifestDigest, response.EntryCount, response.FileCount, response.DirectoryCount, response.ChangeCount);
        try { ContainerRecursiveAclValidation.Plan(plan); } catch (ArgumentException) { throw ContainerRecursiveAclValidation.Invalid(); }
        return plan;
    }
    private static ContainerAclEntry RecursiveAclEntry(ContainerAclEntryDto entry)
    {
        var value = new ContainerAclEntry { Tag = entry.Tag, Permissions = entry.Permissions };
        if (entry.Id is { } id) value.Id = id;
        return value;
    }
    private static ContainerAclEntryDto RecursiveAclEntry(ContainerAclEntry entry)
    {
        if (!entry.HasPermissions) throw ContainerRecursiveAclValidation.Invalid();
        return new(entry.Tag, entry.HasId ? entry.Id : null, entry.Permissions);
    }

}
