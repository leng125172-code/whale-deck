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

public sealed partial class AgentGateway : IContainerRecursiveOwnershipGateway
{
    public async Task RequireContainerRecursiveOwnershipWorkflowAsync(CancellationToken cancellationToken) => _ = await RecursiveOwnershipClient(cancellationToken);
    private async Task<ContainerRecursiveOwnershipService.ContainerRecursiveOwnershipServiceClient> RecursiveOwnershipClient(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var caps = await GetCapabilitiesAsync(timeout.Token);
        if (caps.ProtocolMajor != 1 || caps.ProtocolMinor < 50 || !caps.Capabilities.Contains("docker.files.recursive-ownership-intents"))
            throw new RpcException(new(StatusCode.Unimplemented, "CONTAINER_RECURSIVE_OWNERSHIP_CAPABILITY_UNAVAILABLE"));
        return new(_channel);
    }
    private Metadata RecursiveOwnershipHeaders(string action, IMessage request)
    {
        if (_capabilityKey is not { Length: >= 32 }) throw new RpcException(new(StatusCode.PermissionDenied, "CONTAINER_RECURSIVE_OWNERSHIP_CAPABILITY_UNAVAILABLE"));
        var method = ContainerRecursiveOwnershipBinding.Service + action; var hash = ContainerRecursiveOwnershipBinding.Hash(request);
        var expires = DateTimeOffset.UtcNow.AddSeconds(45).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture); var nonce = Guid.NewGuid().ToString("N");
        var signature = HMACSHA256.HashData(_capabilityKey, Encoding.UTF8.GetBytes(ContainerRecursiveOwnershipBinding.Payload(expires, nonce, method, hash)));
        return new() { { "x-whaledeck-capability", expires + "." + nonce + "." + Convert.ToHexString(signature) }, { ContainerRecursiveOwnershipBinding.Header, hash } };
    }
    public async Task<ContainerRecursiveOwnershipPlanDto> PlanContainerRecursiveOwnershipAsync(TerminalActor actor, ContainerRecursiveOwnershipCommandDto command, CancellationToken cancellationToken)
    {
        ContainerRecursiveOwnershipValidation.Command(command);
        if (string.IsNullOrWhiteSpace(actor.Subject) || actor.Subject.Length > 256 || actor.Subject.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(actor.TraceId) || actor.TraceId.Length > 128 || actor.TraceId.Any(char.IsControl)) throw new ArgumentException("CONTAINER_RECURSIVE_OWNERSHIP_CONTEXT_INVALID");
        var request = new ContainerRecursiveOwnershipPlanRequest { Command = RecursiveOwnershipCommand(command), Context = new() { ActorSubject = actor.Subject, TraceId = actor.TraceId } };
        var client = await RecursiveOwnershipClient(cancellationToken);
        try
        {
            var response = await client.PlanAsync(request, RecursiveOwnershipHeaders("Plan", request), deadline: DateTime.UtcNow.AddSeconds(330), cancellationToken: cancellationToken);
            var plan = RecursiveOwnershipPlan(response);
            if (plan.Command != command) throw ContainerRecursiveOwnershipValidation.Invalid();
            return plan;
        }
        catch (RpcException error) when (RecursiveOwnershipPreflightCode(error) is { } code)
        { throw new ContainerRecursiveOwnershipPreflightRejectedException(code); }
    }
    private static string? RecursiveOwnershipPreflightCode(RpcException error) => (error.StatusCode, error.Status.Detail) switch
    {
        (StatusCode.Aborted, "CONTAINER_RECURSIVE_OWNERSHIP_VERSION_CONFLICT" or "CONTAINER_RECURSIVE_OWNERSHIP_TREE_CHANGED") => "CONTAINER_RECURSIVE_OWNERSHIP_VERSION_CONFLICT",
        (StatusCode.FailedPrecondition, "CONTAINER_RECURSIVE_OWNERSHIP_SOURCE_UNSUPPORTED") => "CONTAINER_RECURSIVE_OWNERSHIP_SOURCE_UNSUPPORTED",
        (StatusCode.FailedPrecondition, "CONTAINER_RECURSIVE_OWNERSHIP_EXTENDED_METADATA_REQUIRED") => "CONTAINER_RECURSIVE_OWNERSHIP_EXTENDED_METADATA_REQUIRED",
        (StatusCode.ResourceExhausted, "CONTAINER_RECURSIVE_OWNERSHIP_ATTRIBUTES_LIMIT") => "CONTAINER_RECURSIVE_OWNERSHIP_ATTRIBUTES_LIMIT",
        (StatusCode.ResourceExhausted, "CONTAINER_RECURSIVE_OWNERSHIP_TREE_LIMIT") => "CONTAINER_RECURSIVE_OWNERSHIP_TREE_LIMIT",
        (StatusCode.ResourceExhausted, "CONTAINER_RECURSIVE_OWNERSHIP_MANIFEST_LIMIT") => "CONTAINER_RECURSIVE_OWNERSHIP_MANIFEST_LIMIT",
        (StatusCode.PermissionDenied, "CONTAINER_RECURSIVE_OWNERSHIP_PROTECTED" or "CONTAINER_PROTECTED" or "CONTAINER_UNSAFE_ISOLATION" or "CONTAINER_UNSAFE_MOUNT" or
            "CONTAINER_FILES_SHARED_CORE_MOUNT" or "CONTAINER_UPLOAD_PUBLISH_READ_ONLY" or "CONTAINER_UPLOAD_PUBLISH_MOUNT_ROOT") => "CONTAINER_RECURSIVE_OWNERSHIP_PROTECTED",
        (StatusCode.FailedPrecondition, "CONTAINER_RECURSIVE_OWNERSHIP_ROOT_CHANGED" or "CONTAINER_FILES_TARGET_MISSING" or "CONTAINER_FILES_INSTANCE_CHANGED" or
            "CONTAINER_FILES_TARGET_CHANGED" or "CONTAINER_FILES_ROOT_CHANGED" or "CONTAINER_NOT_RUNNING") => "CONTAINER_RECURSIVE_OWNERSHIP_TARGET_CHANGED",
        _ => null
    };
    public Task<ContainerRecursiveOwnershipReceiptDto> ApplyContainerRecursiveOwnershipAsync(ContainerRecursiveOwnershipOperationDto operation, CancellationToken cancellationToken) => RecursiveOwnershipAction("Apply", operation, cancellationToken);
    public Task<ContainerRecursiveOwnershipReceiptDto> DiscardContainerRecursiveOwnershipAsync(ContainerRecursiveOwnershipOperationDto operation, CancellationToken cancellationToken) => RecursiveOwnershipAction("Discard", operation, cancellationToken);
    public async Task<ContainerRecursiveOwnershipReceiptDto?> InspectContainerRecursiveOwnershipAsync(ContainerRecursiveOwnershipOperationDto operation, CancellationToken cancellationToken)
    {
        try { return await RecursiveOwnershipAction("Inspect", operation, cancellationToken); }
        catch (RpcException error) when (error.StatusCode == StatusCode.NotFound && error.Status.Detail == "CONTAINER_RECURSIVE_OWNERSHIP_NOT_FOUND") { return null; }
    }
    private async Task<ContainerRecursiveOwnershipReceiptDto> RecursiveOwnershipAction(string action, ContainerRecursiveOwnershipOperationDto operation, CancellationToken cancellationToken)
    {
        ContainerRecursiveOwnershipValidation.Operation(operation);
        var request = new ContainerRecursiveOwnershipRequest { OperationId = operation.Id.ToString("N"), Owner = operation.Owner,
            Plan = RecursiveOwnershipPlan(operation.Plan), Context = UploadContext(operation.Context) };
        var client = await RecursiveOwnershipClient(cancellationToken);
        var headers = RecursiveOwnershipHeaders(action, request); var deadline = DateTime.UtcNow.AddSeconds(330);
        var response = action switch
        {
            "Apply" => await client.ApplyAsync(request, headers, deadline, cancellationToken),
            "Inspect" => await client.InspectAsync(request, headers, deadline, cancellationToken),
            "Discard" => await client.DiscardAsync(request, headers, deadline, cancellationToken),
            _ => throw new ArgumentException("CONTAINER_RECURSIVE_OWNERSHIP_ACTION_INVALID")
        };
        if (response.OperationId != request.OperationId) throw ContainerRecursiveOwnershipValidation.Invalid();
        var receipt = new ContainerRecursiveOwnershipReceiptDto(operation.Id, response.ContainerId, response.InstanceId, response.Fingerprint,
            response.TargetProof, response.State, response.BindingCurrent, response.Replayed, response.CompletedCount, response.ChangedCount,
            response.AbortedCount, response.NotStartedCount, response.UnknownCount, response.HistoryMayExist, response.HistoryVerified,
            response.HistoryDigest, response.HistoryAvailable, response.OutcomeDigest);
        ContainerRecursiveOwnershipValidation.Receipt(operation, receipt); return receipt;
    }
    private static ContainerRecursiveOwnershipCommand RecursiveOwnershipCommand(ContainerRecursiveOwnershipCommandDto command) => new()
    { Location = ContainerLocation(command.Location), Uid = command.Uid, Gid = command.Gid, ExpectedVersion = command.ExpectedVersion };
    private static ContainerRecursiveOwnershipPlan RecursiveOwnershipPlan(ContainerRecursiveOwnershipPlanDto plan) => new()
    {
        SchemaVersion = plan.SchemaVersion, Command = RecursiveOwnershipCommand(plan.Command), TargetProof = plan.TargetProof,
        ParentIdentity = new() { Device = plan.ParentIdentity.Device, Inode = plan.ParentIdentity.Inode }, ParentVersion = plan.ParentVersion,
        OwnerUid = plan.OwnerUid, OwnerGid = plan.OwnerGid, Fingerprint = plan.Fingerprint,
        RootIdentity = new() { Device = plan.RootIdentity.Device, Inode = plan.RootIdentity.Inode, Mode = plan.RootIdentity.Mode,
            OwnerUid = plan.RootIdentity.OwnerUid, OwnerGid = plan.RootIdentity.OwnerGid, LinkCount = plan.RootIdentity.LinkCount,
            SizeBytes = plan.RootIdentity.SizeBytes, ModifiedAtUnixNs = plan.RootIdentity.ModifiedAtUnixNs },
        RootChangedAtUnixNs = plan.RootChangedAtUnixNs, ManifestDigest = plan.ManifestDigest, EntryCount = plan.EntryCount, FileCount = plan.FileCount,
        DirectoryCount = plan.DirectoryCount, ChangeCount = plan.ChangeCount
    };
    private static ContainerRecursiveOwnershipPlanDto RecursiveOwnershipPlan(ContainerRecursiveOwnershipPlan response)
    {
        if (response.Command?.Location is not { } location || !response.Command.HasUid || !response.Command.HasGid || response.ParentIdentity is null || response.RootIdentity is null) throw ContainerRecursiveOwnershipValidation.Invalid();
        var plan = new ContainerRecursiveOwnershipPlanDto(response.SchemaVersion,
            new(new(location.ContainerId, location.InstanceId, location.Path), response.Command.Uid, response.Command.Gid, response.Command.ExpectedVersion),
            response.TargetProof, new(response.ParentIdentity.Device, response.ParentIdentity.Inode), response.ParentVersion, response.OwnerUid, response.OwnerGid, response.Fingerprint,
            new(response.RootIdentity.Device, response.RootIdentity.Inode, response.RootIdentity.Mode, response.RootIdentity.OwnerUid,
                response.RootIdentity.OwnerGid, response.RootIdentity.LinkCount, response.RootIdentity.SizeBytes, response.RootIdentity.ModifiedAtUnixNs),
            response.RootChangedAtUnixNs, response.ManifestDigest, response.EntryCount, response.FileCount, response.DirectoryCount, response.ChangeCount);
        try { ContainerRecursiveOwnershipValidation.Plan(plan); } catch (ArgumentException) { throw ContainerRecursiveOwnershipValidation.Invalid(); }
        return plan;
    }
}
