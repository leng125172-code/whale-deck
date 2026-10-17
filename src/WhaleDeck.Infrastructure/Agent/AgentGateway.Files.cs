using System.Runtime.CompilerServices;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Infrastructure.Agent;

public sealed partial class AgentGateway : IFileGateway
{
    private const string FilesMethod = "/whaledeck.agent.v1.FileTransferService/";
    private async Task<FileTransferService.FileTransferServiceClient> Files(CancellationToken cancellationToken, bool requireHdd = false)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var capabilities = await GetCapabilitiesAsync(timeout.Token);
        if (capabilities.ProtocolMajor != 1 || !capabilities.Capabilities.Contains("files.staged-publication")) throw new RpcException(new(StatusCode.Unimplemented, "FILES_CAPABILITY_UNAVAILABLE"));
        if (requireHdd && !capabilities.Capabilities.Contains("files.hdd-versions")) throw new RpcException(new(StatusCode.Unimplemented, "FILES_HDD_CAPABILITY_UNAVAILABLE"));
        return new(_channel);
    }
    public async Task<IReadOnlyCollection<FileRootDto>> RootsAsync(CancellationToken cancellationToken)
    {
        var client = await Files(cancellationToken);
        var result = await client.ListRootsAsync(new Empty(), Headers(FilesMethod + "ListRoots"), deadline: DateTime.UtcNow.AddSeconds(15), cancellationToken: cancellationToken);
        return result.Roots.Select(item => new FileRootDto(item.Id, item.Name, item.Actions.ToArray())).ToArray();
    }
    public async Task<FilePageDto> ListFilesAsync(FileLocationDto location, string after, int take, CancellationToken cancellationToken)
    {
        var client = await Files(cancellationToken);
        var result = await client.ListAsync(new() { Location = new() { RootId = location.RootId, Path = location.Path }, After = after, Take = checked((uint)take) },
            Headers(FilesMethod + "List"), deadline: DateTime.UtcNow.AddSeconds(15), cancellationToken: cancellationToken);
        return new(result.Entries.Select(MapFile).ToArray(), result.NextCursor, result.Version);
    }
    public async Task<FileEntryDto?> InspectFileAsync(FileLocationDto location, CancellationToken cancellationToken)
    {
        var client = await Files(cancellationToken);
        try { return MapFile(await client.InspectAsync(new() { RootId = location.RootId, Path = location.Path }, Headers(FilesMethod + "Inspect"), deadline: DateTime.UtcNow.AddSeconds(15), cancellationToken: cancellationToken)); }
        catch (RpcException error) when (error.StatusCode == StatusCode.NotFound) { return null; }
    }
    public async IAsyncEnumerable<FileDownloadPart> DownloadFileAsync(FileLocationDto location, string expectedVersion, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var client = await Files(cancellationToken);
        using var call = client.Download(new() { Location = new() { RootId = location.RootId, Path = location.Path }, ExpectedVersion = expectedVersion },
            Headers(FilesMethod + "Download"), deadline: DateTime.UtcNow.AddMinutes(10), cancellationToken: cancellationToken);
        while (await call.ResponseStream.MoveNext(cancellationToken))
        {
            var item = call.ResponseStream.Current;
            yield return new(item.Kind, item.Metadata is null ? null : MapFile(item.Metadata), item.Data.Memory, item.Offset, item.Sha256, item.Version, item.SizeBytes);
        }
    }
    public async Task<FileUploadReceiptDto> StageFileAsync(FileUploadOperation operation, IAsyncEnumerable<ReadOnlyMemory<byte>> bytes, CancellationToken cancellationToken)
    {
        var client = await Files(cancellationToken, requireHdd: true);
        using var call = client.StageUpload(Headers(FilesMethod + "StageUpload"), deadline: DateTime.UtcNow.AddMinutes(10), cancellationToken: cancellationToken);
        try
        {
            await call.RequestStream.WriteAsync(new() { Header = new() { Location = new() { RootId = operation.RootId, Path = operation.RelativePath },
                OperationId = operation.Id.ToString("N"), Owner = operation.OwnerHash, SizeBytes = operation.SizeBytes, Sha256 = operation.Sha256,
                ExpectedVersion = operation.ExpectedVersion, Context = new() { ActorSubject = operation.ActorSubject } } }, cancellationToken);
            long offset = 0;
            await foreach (var part in bytes.WithCancellation(cancellationToken))
            {
                await call.RequestStream.WriteAsync(new() { Data = ByteString.CopyFrom(part.Span), Offset = offset }, cancellationToken); offset += part.Length;
            }
            await call.RequestStream.WriteAsync(new() { Commit = true }, cancellationToken); await call.RequestStream.CompleteAsync();
        }
        catch (RpcException) when (call.ResponseAsync.IsCompleted)
        {
            // Prefer the server's stable early rejection over a local write race.
            return MapUpload(await call.ResponseAsync, operation);
        }
        return MapUpload(await call.ResponseAsync, operation);
    }
    public async Task<FileUploadReceiptDto?> UploadReceiptAsync(FileUploadOperation operation, CancellationToken cancellationToken)
    {
        var client = await Files(cancellationToken);
        try { return MapUpload(await client.GetUploadStatusAsync(Lookup(operation), Headers(FilesMethod + "GetUploadStatus"), deadline: DateTime.UtcNow.AddSeconds(60), cancellationToken: cancellationToken), operation); }
        catch (RpcException error) when (error.StatusCode == StatusCode.NotFound) { return null; }
    }
    public Task<FileUploadReceiptDto> PublishFileAsync(FileUploadOperation operation, CancellationToken cancellationToken) => StagedAction(operation, true, cancellationToken);
    public Task<FileUploadReceiptDto> DiscardFileAsync(FileUploadOperation operation, CancellationToken cancellationToken) => StagedAction(operation, false, cancellationToken);
    private async Task<FileUploadReceiptDto> StagedAction(FileUploadOperation operation, bool publish, CancellationToken cancellationToken)
    {
        var client = await Files(cancellationToken, requireHdd: publish);
        try
        {
            var call = publish ? client.PublishUploadAsync(Lookup(operation), Headers(FilesMethod + "PublishUpload"), deadline: DateTime.UtcNow.AddMinutes(10), cancellationToken: cancellationToken)
                : client.DiscardUploadAsync(Lookup(operation), Headers(FilesMethod + "DiscardUpload"), deadline: DateTime.UtcNow.AddSeconds(15), cancellationToken: cancellationToken);
            using (call) return MapUpload(await call, operation);
        }
        catch (RpcException error) when (error.StatusCode is StatusCode.Aborted or StatusCode.PermissionDenied or StatusCode.FailedPrecondition) { throw new FileTransferRejectedException(); }
    }
    private static FileUploadLookup Lookup(FileUploadOperation operation) => new() { OperationId = operation.Id.ToString("N"), Owner = operation.OwnerHash };
    private static FileEntryDto MapFile(FileEntry item) => new(item.Name, item.Kind, item.SizeBytes, item.ModifiedAtUnixMs, item.Version, item.Mode, item.IsProtected);
    private static FileUploadReceiptDto MapUpload(FileUploadReceipt item, FileUploadOperation operation)
    {
        if (item.OperationId != operation.Id.ToString("N")) throw new InvalidOperationException("FILES_RECEIPT_TARGET_MISMATCH");
        var backup = item.HddBackup is not { } proof ? null : new FileHddBackupDto(proof.OperationId, proof.StorageId, proof.SourceVersion,
            proof.SizeBytes, proof.Sha256, proof.CreatedAtUnixMs, proof.DifferentSourceDevice);
        if (backup is not null) FileManagementService.ValidateBackup(operation, backup);
        if (item.ResultCode.Length > 0 && !FileManagementService.IsBackupFailureCode(item.ResultCode)) throw new FileTransferRejectedException();
        return new(item.State, item.SizeBytes, item.Sha256, item.Version, item.PreviousRetained, item.BindingCurrent, item.IsInProgress, backup, item.ResultCode.Length == 0 ? null : item.ResultCode);
    }
}
