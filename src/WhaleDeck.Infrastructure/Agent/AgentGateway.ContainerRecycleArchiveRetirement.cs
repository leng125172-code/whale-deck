using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Contracts;
using Rpc = WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Infrastructure.Agent;

// No workflow/DI registration or Agent capability promotion in this candidate.
public sealed partial class AgentGateway : IContainerRecycleArchiveRetirementGateway
{
    public async Task RequireContainerRecycleArchiveRetirementAsync(CancellationToken cancellationToken) => _ = await RetirementClient(cancellationToken);
    private async Task<Rpc.ContainerRecycleArchiveRetirementService.ContainerRecycleArchiveRetirementServiceClient> RetirementClient(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var caps = await _agent.GetCapabilitiesAsync(new Empty(), cancellationToken: timeout.Token);
        if (!ContainerRecycleArchiveRetirementBinding.IsSupported(caps)) throw new RpcException(new(StatusCode.Unimplemented, "CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_CAPABILITY_UNAVAILABLE"));
        return new(_channel);
    }
    private Metadata RetirementHeaders(string action, IMessage request)
    {
        if (_capabilityKey is not { Length: >= 32 }) throw new RpcException(new(StatusCode.PermissionDenied, "CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_CAPABILITY_UNAVAILABLE"));
        var hash = ContainerRecycleArchiveRetirementBinding.Hash(request);
        var expires = DateTimeOffset.UtcNow.AddSeconds(45).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture); var nonce = Guid.NewGuid().ToString("N");
        var signature = HMACSHA256.HashData(_capabilityKey, Encoding.UTF8.GetBytes(ContainerRecycleArchiveRetirementBinding.Payload(expires, nonce, ContainerRecycleArchiveRetirementBinding.Service + action, hash)));
        return new() { { "x-whaledeck-capability", expires + "." + nonce + "." + Convert.ToHexString(signature) }, { ContainerRecycleArchiveRetirementBinding.Header, hash } };
    }
    public async Task<ContainerRecycleArchiveRetirementPlanDto> PlanContainerRecycleArchiveRetirementAsync(TerminalActor actor, ContainerRecycleArchiveRetirementCommandDto command, CancellationToken cancellationToken)
    {
        ContainerRecycleArchiveRetirementValidation.Command(command);
        if (actor is null || string.IsNullOrWhiteSpace(actor.Subject) || actor.Subject.Length > 256 || actor.Subject.Any(char.IsControl) || string.IsNullOrWhiteSpace(actor.TraceId) || actor.TraceId.Length > 128 || actor.TraceId.Any(char.IsControl)) throw new ArgumentException("CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_CONTEXT_INVALID");
        // Protobuf owns copies of every nested byte array before the first await.
        var request = new Rpc.ContainerRecycleArchiveRetirementPlanRequest { Command = RetirementCommand(command), Context = new() { ActorSubject = actor.Subject, TraceId = actor.TraceId } };
        VerifyRetirementCommand(request.Command);
        var client = await RetirementClient(cancellationToken);
        var result = await client.PlanAsync(request, RetirementHeaders("Plan", request), deadline: DateTime.UtcNow.AddSeconds(120), cancellationToken: cancellationToken);
        try
        {
            ContainerRecycleArchiveRetirementWire.RequireKnown(result);
            if (result.Command is null || !result.Command.Equals(request.Command)) throw ContainerRecycleArchiveRetirementValidation.Invalid();
            VerifyRetirementPlan(result); return RetirementPlan(result);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or FormatException or NullReferenceException or OverflowException) { throw ContainerRecycleArchiveRetirementValidation.Invalid(); }
    }
    public Task<ContainerRecycleArchiveRetirementReceiptDto> ReserveContainerRecycleArchiveRetirementAsync(ContainerRecycleArchiveRetirementOperationDto operation, CancellationToken cancellationToken) => RetirementAction("Reserve", operation, cancellationToken);
    public Task<ContainerRecycleArchiveRetirementReceiptDto> ApplyContainerRecycleArchiveRetirementAsync(ContainerRecycleArchiveRetirementOperationDto operation, CancellationToken cancellationToken) => RetirementAction("Apply", operation, cancellationToken);
    public Task<ContainerRecycleArchiveRetirementReceiptDto> DiscardContainerRecycleArchiveRetirementAsync(ContainerRecycleArchiveRetirementOperationDto operation, CancellationToken cancellationToken) => RetirementAction("Discard", operation, cancellationToken);
    public async Task<ContainerRecycleArchiveRetirementReceiptDto?> InspectContainerRecycleArchiveRetirementAsync(ContainerRecycleArchiveRetirementOperationDto operation, CancellationToken cancellationToken)
    {
        try { return await RetirementAction("Inspect", operation, cancellationToken); }
        catch (RpcException error) when (error.StatusCode == StatusCode.NotFound && error.Status.Detail == "CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_NOT_FOUND") { return null; }
    }
    private async Task<ContainerRecycleArchiveRetirementReceiptDto> RetirementAction(string action, ContainerRecycleArchiveRetirementOperationDto operation, CancellationToken cancellationToken)
    {
        ContainerRecycleArchiveRetirementValidation.Operation(operation);
        var request = new Rpc.ContainerRecycleArchiveRetirementRequest { OperationId = operation.Id.ToString("N"), Owner = operation.Owner, Plan = RetirementPlan(operation.Plan), Context = UploadContext(operation.Context) };
        VerifyRetirementPlan(request.Plan);
        var client = await RetirementClient(cancellationToken); var headers = RetirementHeaders(action, request); var deadline = DateTime.UtcNow.AddSeconds(120);
        var result = action switch
        {
            "Reserve" => await client.ReserveAsync(request, headers, deadline, cancellationToken),
            "Apply" => await client.ApplyAsync(request, headers, deadline, cancellationToken),
            "Inspect" => await client.InspectAsync(request, headers, deadline, cancellationToken),
            "Discard" => await client.DiscardAsync(request, headers, deadline, cancellationToken),
            _ => throw new ArgumentException("CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_ACTION_INVALID")
        };
        ContainerRecycleArchiveRetirementWire.Receipt(request, result);
        var value = new ContainerRecycleArchiveRetirementReceiptDto(operation.Id, Guid.ParseExact(result.SourceOperationId, "N"), Guid.ParseExact(result.ArchiveOperationId, "N"), result.Fingerprint, result.State, result.SourceFenced, result.OwnedFence, result.HasRetirementApplied ? result.RetirementApplied : null, result.NotBeforeUnixMs);
        ContainerRecycleArchiveRetirementValidation.Receipt(operation, value); return value;
    }
    private static void VerifyRetirementCommand(Rpc.ContainerRecycleArchiveRetirementCommand c)
    {
        ContainerRecycleArchiveRetirementWire.RequireKnown(c);
        var archive = c.Archive.Plan;
        VerifyArchiveCommand(archive.Command);
        if (archive.Fingerprint != ContainerRecycleArchiveWire.Fingerprint(archive)) throw new ArgumentException("CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_PLAN_INVALID");
    }
    private static void VerifyRetirementPlan(Rpc.ContainerRecycleArchiveRetirementPlan p)
    {
        VerifyRetirementCommand(p.Command);
        if (p.Fingerprint != ContainerRecycleArchiveRetirementWire.Fingerprint(p)) throw new ArgumentException("CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_PLAN_INVALID");
    }
    private static Rpc.ContainerRecycleArchiveRetirementCommand RetirementCommand(ContainerRecycleArchiveRetirementCommandDto c) => new() { Archive = new() { OperationId = c.Archive.OperationId.ToString("N"), Owner = c.Archive.Owner, Plan = ArchivePlan(c.Archive.Plan) }, RetainDays = c.RetainDays, Mode = c.Mode };
    private static ContainerRecycleArchiveRetirementCommandDto RetirementCommand(Rpc.ContainerRecycleArchiveRetirementCommand c) => new(new(Guid.ParseExact(c.Archive.OperationId, "N"), c.Archive.Owner, ArchivePlan(c.Archive.Plan)), c.RetainDays, c.Mode);
    private static Rpc.ContainerRecycleArchiveRetirementPlan RetirementPlan(ContainerRecycleArchiveRetirementPlanDto p) => new()
    {
        SchemaVersion = p.SchemaVersion, Command = RetirementCommand(p.Command), ArchiveRecordDigest = p.ArchiveRecordDigest, SourceRecordDigest = p.SourceRecordDigest,
        PurgeRecordDigest = p.PurgeRecordDigest, PurgeMarkerDigest = p.PurgeMarkerDigest, ClaimDigest = p.ClaimDigest, HistoryMetadataDigest = p.HistoryMetadataDigest,
        HistoryCreatedAtUnixMs = p.HistoryCreatedAtUnixMs, NotBeforeUnixMs = p.NotBeforeUnixMs, RetentionElapsed = p.RetentionElapsed, BindingCurrent = p.BindingCurrent, RetirementEligible = p.RetirementEligible,
        StorageHash = p.StorageHash, StoreIdentity = new() { Device = p.StoreIdentity.Device, Inode = p.StoreIdentity.Inode }, ArchiveIdentity = PurgeIdentity(p.ArchiveIdentity), ArchiveChangedAtUnixNs = p.ArchiveChangedAtUnixNs,
        ArchiveBytes = p.ArchiveBytes, ArchiveSha256 = p.ArchiveSha256, PublishedIdentity = PurgeIdentity(p.PublishedIdentity), PublishedChangedAtUnixNs = p.PublishedChangedAtUnixNs, Fingerprint = p.Fingerprint
    };
    private static ContainerRecycleArchiveRetirementPlanDto RetirementPlan(Rpc.ContainerRecycleArchiveRetirementPlan p)
    {
        if (p.Command?.Archive?.Plan is null || p.StoreIdentity is null || p.ArchiveIdentity is null || p.PublishedIdentity is null) throw ContainerRecycleArchiveRetirementValidation.Invalid();
        var value = new ContainerRecycleArchiveRetirementPlanDto(p.SchemaVersion, RetirementCommand(p.Command), p.ArchiveRecordDigest, p.SourceRecordDigest, p.PurgeRecordDigest, p.PurgeMarkerDigest, p.ClaimDigest, p.HistoryMetadataDigest,
            p.HistoryCreatedAtUnixMs, p.NotBeforeUnixMs, p.RetentionElapsed, p.BindingCurrent, p.RetirementEligible, p.StorageHash, new(p.StoreIdentity.Device, p.StoreIdentity.Inode), PurgeIdentity(p.ArchiveIdentity), p.ArchiveChangedAtUnixNs,
            p.ArchiveBytes, p.ArchiveSha256, PurgeIdentity(p.PublishedIdentity), p.PublishedChangedAtUnixNs, p.Fingerprint);
        ContainerRecycleArchiveRetirementValidation.Plan(value); return value;
    }
}
