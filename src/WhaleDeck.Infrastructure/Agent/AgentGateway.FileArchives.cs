using Grpc.Core;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Services;
using WhaleDeck.Contracts.Agent.V1;
using WhaleDeck.Domain.Entities;
using App = WhaleDeck.Application.Models;
using ArchiveRpc = WhaleDeck.Contracts.Agent.V1.FileArchiveService;

namespace WhaleDeck.Infrastructure.Agent;

public sealed partial class AgentGateway : IFileArchiveGateway
{
    private const string ArchivesMethod = "/whaledeck.agent.v1.FileArchiveService/";
    private async Task<ArchiveRpc.FileArchiveServiceClient> Archives(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var capabilities = await GetCapabilitiesAsync(timeout.Token);
        if (capabilities.ProtocolMajor != 1 || capabilities.ProtocolMinor < 26 || !capabilities.Capabilities.Contains("files.archive-intents"))
            throw new RpcException(new(StatusCode.Unimplemented, "FILES_ARCHIVE_CAPABILITY_UNAVAILABLE"));
        return new(_channel);
    }
    public async Task<App.FileHistoryPage> ListHistoryAsync(App.FileHistoryCommand command, CancellationToken cancellationToken)
    {
        FileArchiveValidation.HistoryCommand(command); var client = await Archives(cancellationToken);
        var value = await client.ListAsync(new() { Location = new() { RootId = command.RootId, Path = command.Path }, After = command.After, Take = (uint)command.Take, ExpectedVersion = command.ExpectedVersion },
            Headers(ArchivesMethod + "List"), deadline: DateTime.UtcNow.AddSeconds(30), cancellationToken: cancellationToken);
        if (value.Entries.Count > command.Take || value.Scanned > 500 || value.InvalidRecords > value.Scanned) throw new App.FileTransferRejectedException();
        var result = new App.FileHistoryPage(value.Entries.Select(item => new App.FileVersionDto(ArchiveId(item.VersionId), item.Location.RootId, item.Location.Path, item.SourceVersion,
            item.SizeBytes, item.Sha256, DateTimeOffset.FromUnixTimeMilliseconds(item.CreatedAtUnixMs), item.CaptureKind, item.CaptureState, item.Availability, item.DifferentSourceDevice)).ToArray(),
            value.NextCursor, value.Version, (int)value.Scanned, (int)value.InvalidRecords, value.Complete);
        FileArchiveValidation.History(command, result); return result;
    }
    public async Task<App.FileArchivePlan> PlanArchiveAsync(App.TerminalActor actor, App.FileArchiveCommand command, CancellationToken cancellationToken)
    {
        var client = await Archives(cancellationToken); var expected = ArchiveCommand(command);
        var value = await client.PlanAsync(new() { Command = expected, Context = new() { ActorSubject = actor.Subject, TraceId = actor.TraceId } },
            Headers(ArchivesMethod + "Plan"), deadline: DateTime.UtcNow.AddMinutes(10), cancellationToken: cancellationToken);
        if (!expected.Equals(value.Command)) throw new InvalidOperationException("FILES_ARCHIVE_TARGET_MISMATCH");
        var result = new App.FileArchivePlan(command, Parent(value.ParentIdentity), ArchiveSource(value.SourceIdentity), value.SourceSha256,
            value.ArchiveIdentity is null ? null : new(value.ArchiveIdentity.MetadataHash, ArchiveSource(value.ArchiveIdentity.DataIdentity)!),
            ArchiveSource(value.RestoreMetadata), value.SizeBytes, value.Sha256, value.Fingerprint);
        FileArchiveValidation.Plan(result); return result;
    }
    public async Task<App.FileArchiveReceipt?> InspectArchiveAsync(FileArchiveOperation operation, CancellationToken cancellationToken)
    {
        var client = await Archives(cancellationToken);
        try { return ArchiveReceipt(await client.InspectAsync(new() { OperationId = operation.Id.ToString("N"), Owner = operation.OwnerHash },
            Headers(ArchivesMethod + "Inspect"), deadline: DateTime.UtcNow.AddMinutes(10), cancellationToken: cancellationToken), operation); }
        catch (RpcException error) when (error.StatusCode == StatusCode.NotFound) { return null; }
    }
    public Task<App.FileArchiveReceipt> ApplyArchiveAsync(FileArchiveOperation operation, CancellationToken cancellationToken) => ArchiveAction(operation, false, cancellationToken);
    public Task<App.FileArchiveReceipt> DiscardArchiveAsync(FileArchiveOperation operation, CancellationToken cancellationToken) => ArchiveAction(operation, true, cancellationToken);
    private async Task<App.FileArchiveReceipt> ArchiveAction(FileArchiveOperation operation, bool discard, CancellationToken cancellationToken)
    {
        var client = await Archives(cancellationToken); var plan = FileArchiveValidation.ReadPlan(operation); FileArchiveValidation.Plan(plan);
        var request = new FileArchiveApplyRequest { OperationId = operation.Id.ToString("N"), Owner = operation.OwnerHash,
            Context = new() { ActorSubject = operation.ActorSubject, TraceId = operation.Id.ToString("D") }, Plan = new() {
                Command = ArchiveCommand(plan.Command), ParentIdentity = Parent(plan.ParentIdentity), SourceIdentity = ArchiveSource(plan.SourceIdentity), SourceSha256 = plan.SourceSha256,
                ArchiveIdentity = plan.ArchiveIdentity is null ? null : new() { MetadataHash = plan.ArchiveIdentity.MetadataHash, DataIdentity = ArchiveSource(plan.ArchiveIdentity.DataIdentity) },
                RestoreMetadata = ArchiveSource(plan.RestoreMetadata), SizeBytes = plan.SizeBytes, Sha256 = plan.Sha256, Fingerprint = plan.Fingerprint } };
        using var call = discard ? client.DiscardAsync(request, Headers(ArchivesMethod + "Discard"), deadline: DateTime.UtcNow.AddMinutes(10), cancellationToken: cancellationToken)
            : client.ApplyAsync(request, Headers(ArchivesMethod + "Apply"), deadline: DateTime.UtcNow.AddMinutes(10), cancellationToken: cancellationToken);
        return ArchiveReceipt(await call, operation);
    }
    private static FileArchiveCommand ArchiveCommand(App.FileArchiveCommand command)
    {
        FileArchiveValidation.Command(command);
        return new() { Kind = command.Kind switch { "trash" => FileArchiveKind.Trash, "restore" => FileArchiveKind.Restore, "purge" => FileArchiveKind.Purge, _ => throw new ArgumentException("FILES_ARCHIVE_INVALID") },
            Location = new() { RootId = command.Location.RootId, Path = command.Location.Path }, VersionId = command.VersionId?.ToString("N") ?? "", ExpectedVersion = command.ExpectedVersion };
    }
    private static App.FileObjectIdentityDto? ArchiveSource(FileObjectIdentity? value) => value is null ? null : new(value.Device, value.Inode, value.Mode, value.OwnerUid, value.OwnerGid, value.LinkCount, value.SizeBytes, value.ModifiedAtUnixNs);
    private static FileObjectIdentity? ArchiveSource(App.FileObjectIdentityDto? value) => value is null ? null : new() { Device = value.Device, Inode = value.Inode, Mode = value.Mode, OwnerUid = value.OwnerUid, OwnerGid = value.OwnerGid,
        LinkCount = value.LinkCount, SizeBytes = value.SizeBytes, ModifiedAtUnixNs = value.ModifiedAtUnixNs };
    private static Guid ArchiveId(string value) => Guid.TryParseExact(value, "N", out var id) && id != Guid.Empty && id.ToString("N") == value ? id : throw new App.FileTransferRejectedException();
    private static App.FileArchiveReceipt ArchiveReceipt(FileArchiveReceipt value, FileArchiveOperation operation)
    {
        var plan = FileArchiveValidation.ReadPlan(operation);
        if (value.OperationId != operation.Id.ToString("N") || value.Kind != ArchiveCommand(plan.Command).Kind) throw new App.FileTransferRejectedException();
        var result = new App.FileArchiveReceipt(plan.Command.Kind, value.State, value.Fingerprint, value.Version, value.SizeBytes, value.Sha256,
            value.SourceRetained, value.BindingCurrent, ArchiveId(value.VersionId), value.HddBackup is null ? null : new(value.HddBackup.OperationId, value.HddBackup.StorageId,
                value.HddBackup.SourceVersion, value.HddBackup.SizeBytes, value.HddBackup.Sha256, value.HddBackup.CreatedAtUnixMs, value.HddBackup.DifferentSourceDevice));
        FileArchiveValidation.Receipt(operation.Id, plan, result); return result;
    }
}
