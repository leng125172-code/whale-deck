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

public sealed partial class AgentGateway : IContainerFileWriteGateway
{
    public async Task RequireContainerUploadWorkflowAsync(CancellationToken cancellationToken) =>
        _ = await ContainerRecoveryClient(cancellationToken);

    public async Task RequireContainerRestorationWorkflowAsync(CancellationToken cancellationToken)
    {
        await RequireContainerUploadWorkflowAsync(cancellationToken);
        _ = await ContainerWrites(cancellationToken, cancellationFence: true, restoration: true);
    }

    private async Task<ContainerFileWriteService.ContainerFileWriteServiceClient> ContainerWrites(CancellationToken cancellationToken, bool cancellationFence = false, bool restoration = false)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var caps = await GetCapabilitiesAsync(timeout.Token);
        if (caps.ProtocolMajor != 1 || caps.ProtocolMinor < 39 || !caps.Capabilities.Contains("docker.files.bound-uploads") ||
            cancellationFence && (caps.ProtocolMinor < 40 || !caps.Capabilities.Contains("docker.files.upload-cancel-fence")))
            throw new RpcException(new(StatusCode.Unimplemented, "CONTAINER_UPLOAD_CAPABILITY_UNAVAILABLE"));
        if (restoration && (caps.ProtocolMinor < 43 || !caps.Capabilities.Contains("docker.files.restore-attributes") ||
            !caps.Capabilities.Contains("docker.files.history") || !caps.Capabilities.Contains("docker.files.history-source")))
            throw new RpcException(new(StatusCode.Unimplemented, "CONTAINER_RESTORE_CAPABILITY_UNAVAILABLE"));
        return new(_channel);
    }

    private Metadata ContainerWriteHeaders(string action, IMessage request)
    {
        if (_capabilityKey is not { Length: >= 32 }) throw new RpcException(new(StatusCode.PermissionDenied, "CONTAINER_UPLOAD_CAPABILITY_UNAVAILABLE"));
        var method = ContainerFileWriteBinding.Service + action;
        var hash = ContainerFileWriteBinding.Hash(request);
        var expires = DateTimeOffset.UtcNow.AddSeconds(45).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var nonce = Guid.NewGuid().ToString("N");
        var mac = HMACSHA256.HashData(_capabilityKey, Encoding.UTF8.GetBytes(ContainerFileWriteBinding.Payload(expires, nonce, method, hash)));
        return new() { { "x-whaledeck-capability", expires + "." + nonce + "." + Convert.ToHexString(mac) }, { ContainerFileWriteBinding.Header, hash } };
    }

    public async Task<ContainerFileUploadTargetDto> InspectContainerUploadTargetAsync(ContainerFileLocationDto location, CancellationToken cancellationToken)
    {
        ContainerFileValidation.Location(location);
        var request = ContainerLocation(location); var client = await ContainerWrites(cancellationToken);
        var result = await client.InspectTargetAsync(request, ContainerWriteHeaders("InspectTarget", request),
            deadline: DateTime.UtcNow.AddSeconds(30), cancellationToken: cancellationToken);
        if (result.Location is null || result.Location.ContainerId != location.ContainerId || result.Location.InstanceId != location.InstanceId || result.Location.Path != location.Path ||
            result.CurrentSizeBytes is < 0 or > 1073741824L || result.ExpectedVersion == "ABSENT" && result.CurrentSizeBytes != 0) throw UploadOutput();
        UploadOutputHash(result.TargetProof); UploadOutputHash(result.ExpectedParentVersion);
        if (result.ExpectedVersion != "ABSENT") UploadOutputHash(result.ExpectedVersion);
        return new(location, result.TargetProof, result.ExpectedParentVersion, result.ExpectedVersion, result.CurrentSizeBytes);
    }

    public async Task<ContainerFileUploadReceiptDto> StageContainerFileAsync(ContainerFileStageDto request,
        IAsyncEnumerable<ReadOnlyMemory<byte>> bytes, CancellationToken cancellationToken)
    {
        ContainerFileUploadValidation.Stage(request);
        var header = new ContainerUploadHeader { Location = ContainerLocation(request.Location), OperationId = request.OperationId.ToString("N"),
            Owner = request.Owner, SizeBytes = request.SizeBytes, Sha256 = request.Sha256, ExpectedVersion = request.ExpectedVersion,
            ExpectedParentVersion = request.ExpectedParentVersion, ExpectedTargetProof = request.ExpectedTargetProof, Context = UploadContext(request.Context) };
        if (request.RestoreSource is { } source)
            header.RestoreSource = new() { CaptureId = source.CaptureId, CaptureHash = source.CaptureHash, Path = source.Path };
        var client = await ContainerWrites(cancellationToken, cancellationFence: true, restoration: request.RestoreSource is not null);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); lifetime.CancelAfter(TimeSpan.FromSeconds(325));
        using var call = client.StageUpload(ContainerWriteHeaders("StageUpload", header), deadline: DateTime.UtcNow.AddSeconds(330), cancellationToken: lifetime.Token);
        try
        {
            await call.RequestStream.WriteAsync(new() { Header = header }, lifetime.Token);
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long offset = 0;
            await foreach (var part in bytes.WithCancellation(lifetime.Token))
            {
                for (var start = 0; start < part.Length; start += 32768)
                {
                    var chunk = part.Slice(start, Math.Min(32768, part.Length - start));
                    if (offset + chunk.Length > request.SizeBytes) throw new ArgumentException("CONTAINER_UPLOAD_SIZE_MISMATCH");
                    digest.AppendData(chunk.Span);
                    await call.RequestStream.WriteAsync(new() { Data = ByteString.CopyFrom(chunk.Span), Offset = offset }, lifetime.Token);
                    offset += chunk.Length;
                }
            }
            if (offset != request.SizeBytes || Convert.ToHexString(digest.GetHashAndReset()) != request.Sha256)
                throw new ArgumentException("CONTAINER_UPLOAD_CHECKSUM_MISMATCH");
            await call.RequestStream.WriteAsync(new() { Commit = true }, lifetime.Token);
            await call.RequestStream.CompleteAsync();
        }
        catch (RpcException) when (call.ResponseAsync.IsCompleted)
        {
            _ = await call.ResponseAsync; // Prefer a stable early rejection, never premature success.
            throw UploadOutput();
        }
        var response = await call.ResponseAsync;
        var reference = new ContainerFileUploadReferenceDto(request.Location.ContainerId, request.Location.InstanceId,
            request.OperationId, request.Owner, request.ExpectedTargetProof, request.Context);
        var receipt = UploadReceipt(response, reference);
        if (receipt.Upload.State != "Staged" || receipt.Upload.SizeBytes != request.SizeBytes || receipt.Upload.Sha256 != request.Sha256 ||
            receipt.Upload.HddBackup is { } backup && (request.ExpectedVersion == "ABSENT" || backup.SourceVersion != request.ExpectedVersion)) throw UploadOutput();
        return receipt;
    }

    public Task<ContainerFileUploadReceiptDto> PublishContainerFileAsync(ContainerFileUploadReferenceDto request, CancellationToken cancellationToken) => ContainerUploadAction("Publish", request, cancellationToken, cancellationFence: true);
    public Task<ContainerFileUploadReceiptDto> DiscardContainerUploadAsync(ContainerFileUploadReferenceDto request, CancellationToken cancellationToken) => ContainerUploadAction("Discard", request, cancellationToken);
    public Task<ContainerFileUploadReceiptDto> PreventContainerUploadAsync(ContainerFileUploadReferenceDto request, CancellationToken cancellationToken) => ContainerUploadAction("Discard", request, cancellationToken, cancellationFence: true);
    public async Task<ContainerFileUploadReceiptDto?> InspectContainerUploadAsync(ContainerFileUploadReferenceDto request, CancellationToken cancellationToken)
    {
        try { return await ContainerUploadAction("Inspect", request, cancellationToken); }
        catch (RpcException error) when (error.StatusCode == StatusCode.NotFound && error.Status.Detail == "CONTAINER_UPLOAD_NOT_FOUND") { return null; }
    }
    private async Task<ContainerFileUploadReceiptDto> ContainerUploadAction(string action, ContainerFileUploadReferenceDto request, CancellationToken cancellationToken, bool cancellationFence = false)
    {
        ContainerFileUploadValidation.Reference(request);
        var client = await ContainerWrites(cancellationToken, cancellationFence);
        var message = new ContainerUploadReference { ContainerId = request.ContainerId, InstanceId = request.InstanceId,
            OperationId = request.OperationId.ToString("N"), Owner = request.Owner, ExpectedTargetProof = request.ExpectedTargetProof, Context = UploadContext(request.Context) };
        using var call = action switch
        {
            "Publish" => client.PublishAsync(message, ContainerWriteHeaders(action, message), deadline: DateTime.UtcNow.AddSeconds(650), cancellationToken: cancellationToken),
            "Inspect" => client.InspectAsync(message, ContainerWriteHeaders(action, message), deadline: DateTime.UtcNow.AddSeconds(650), cancellationToken: cancellationToken),
            "Discard" => client.DiscardAsync(message, ContainerWriteHeaders(action, message), deadline: DateTime.UtcNow.AddSeconds(650), cancellationToken: cancellationToken),
            _ => throw new ArgumentException("CONTAINER_UPLOAD_ACTION_INVALID")
        };
        ContainerFileUploadReceiptDto result;
        try { result = UploadReceipt(await call, request); }
        catch (RpcException error) when (error.StatusCode == StatusCode.FailedPrecondition && error.Status.Detail is
            "CONTAINER_NOT_RUNNING" or "CONTAINER_FILES_INSTANCE_CHANGED" or "CONTAINER_FILES_ROOT_CHANGED" or
            "CONTAINER_FILES_TARGET_CHANGED" or "CONTAINER_UPLOAD_TARGET_CHANGED" or "CONTAINER_UPLOAD_PUBLISH_PARENT_CHANGED")
        { throw new ContainerFileUploadTargetUnavailableException(); }
        if (action == "Discard" && cancellationFence && result.Upload.State is not ("Prevented" or "Discarded" or "PublishDiscarded")) throw UploadOutput();
        return result;
    }

    private static RequestContext UploadContext(ContainerFileWriteContextDto value) => new()
        { ActorSubject = value.ActorSubject, TraceId = value.TraceId, JobId = value.JobId.ToString("N"), IdempotencyKey = value.IdempotencyKey.ToString("N") };

    private static ContainerFileUploadReceiptDto UploadReceipt(ContainerUploadReceipt value, ContainerFileUploadReferenceDto request)
    {
        var receipt = value.Upload;
        if (value.ContainerId != request.ContainerId || value.InstanceId != request.InstanceId || value.TargetProof != request.ExpectedTargetProof ||
            receipt is null || receipt.OperationId != request.OperationId.ToString("N") || receipt.HddBackup is not null || receipt.IsInProgress || receipt.ResultCode.Length > 0 ||
            receipt.SizeBytes is < 0 or > 1073741824L || receipt.State is not ("Receiving" or "Verified" or "Staged" or "Interrupted" or "Aborted" or "Discarded" or "Prevented" or "Unknown" or
                "PublishPreparing" or "PublishApplying" or "PublishInterrupted" or "Published" or "PublishDiscarding" or "PublishDiscarded") ||
            receipt.State != "Published" && (receipt.Version.Length != 0 || receipt.BindingCurrent || receipt.PreviousRetained)) throw UploadOutput();
        UploadOutputHash(receipt.Sha256);
        if (receipt.State == "Prevented" && (receipt.SizeBytes != 0 || receipt.Sha256 != Convert.ToHexString(SHA256.HashData([])) || value.Backup is not null)) throw UploadOutput();
        if (receipt.State == "Published") UploadOutputHash(receipt.Version);
        FileHddBackupDto? backup = null;
        if (value.Backup is { } container)
        {
            var proof = container.Proof;
            if (proof is null || container.ContainerId != request.ContainerId || container.InstanceId != request.InstanceId || container.TargetProof != request.ExpectedTargetProof ||
                proof.OperationId != request.OperationId.ToString("N") || proof.StorageId != "database-platform.backup.hdd" ||
                proof.SizeBytes is < 0 or > 1073741824L || proof.CreatedAtUnixMs is < 0 or > 253402300799999L) throw UploadOutput();
            UploadOutputHash(proof.SourceVersion); UploadOutputHash(proof.Sha256);
            backup = new(proof.OperationId, proof.StorageId, proof.SourceVersion, proof.SizeBytes, proof.Sha256, proof.CreatedAtUnixMs, proof.DifferentSourceDevice);
        }
        if (receipt.PreviousRetained && backup is null) throw UploadOutput();
        return new(value.ContainerId, value.InstanceId, request.OperationId, value.TargetProof,
            new(receipt.State, receipt.SizeBytes, receipt.Sha256, receipt.Version, receipt.PreviousRetained, receipt.BindingCurrent, false, backup), receipt.Replayed);
    }
    private static InvalidOperationException UploadOutput() => new("CONTAINER_UPLOAD_OUTPUT_INVALID");
    private static void UploadOutputHash(string value)
    {
        try { ContainerFileValidation.Hash(value); }
        catch (ArgumentException) { throw UploadOutput(); }
    }
}
