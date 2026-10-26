using System.Security.Cryptography;
using System.Text.Json;
using Google.Protobuf;
using Grpc.Core;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

/// <summary>Content-bound, separate write transport. Business authorization stays in Application.</summary>
public sealed class ContainerFileWriteGrpcService(FileTransferRunner runner)
    : ContainerFileWriteService.ContainerFileWriteServiceBase
{
    public override async Task<ContainerUploadTarget> InspectTarget(ContainerFileLocation request, ServerCallContext context)
    {
        ContainerFilePolicy.Location(request);
        ContainerUploadPolicy.RequireBinding(request, context);
        using var result = await runner.ContainerWriteOnceAsync("target", JsonSerializer.Serialize(new
            { containerId = request.ContainerId, instanceId = request.InstanceId, path = request.Path }), context.CancellationToken);
        return ContainerUploadPolicy.Target(result.RootElement, request);
    }

    public override async Task<ContainerUploadReceipt> StageUpload(IAsyncStreamReader<ContainerUploadFrame> requestStream, ServerCallContext context)
    {
        using var first = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        first.CancelAfter(TimeSpan.FromSeconds(5));
        if (!await requestStream.MoveNext(first.Token)) throw new ArgumentException("CONTAINER_UPLOAD_HEADER_REQUIRED");
        var frame = requestStream.Current;
        var input = ContainerUploadPolicy.Open(frame);
        ContainerUploadPolicy.RequireBinding(frame.Header, context);
        return await runner.ContainerStageAsync(input, frame.Header, requestStream, context.CancellationToken);
    }

    public override Task<ContainerUploadReceipt> Publish(ContainerUploadReference request, ServerCallContext context) => Action("publish", request, context);
    public override Task<ContainerUploadReceipt> Inspect(ContainerUploadReference request, ServerCallContext context) => Action("inspect", request, context);
    public override Task<ContainerUploadReceipt> Discard(ContainerUploadReference request, ServerCallContext context) => Action("discard", request, context);

    private async Task<ContainerUploadReceipt> Action(string action, ContainerUploadReference request, ServerCallContext context)
    {
        ContainerUploadPolicy.Reference(request);
        ContainerUploadPolicy.RequireBinding(request, context);
        using var result = await runner.ContainerWriteOnceAsync(action, JsonSerializer.Serialize(new
            { containerId = request.ContainerId, instanceId = request.InstanceId, operationId = request.OperationId,
                owner = request.Owner, expectedTargetProof = request.ExpectedTargetProof }), context.CancellationToken);
        return ContainerUploadPolicy.Receipt(result.RootElement, request);
    }
}

internal static class ContainerUploadPolicy
{
    public static void RequireBinding(IMessage request, ServerCallContext context)
    {
        var expected = context.RequestHeaders.GetValue(ContainerFileWriteBinding.Header);
        var actual = ContainerFileWriteBinding.Hash(request);
        if (expected is not { Length: 64 } || !CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.ASCII.GetBytes(actual), System.Text.Encoding.ASCII.GetBytes(expected)))
            throw new RpcException(new(StatusCode.PermissionDenied, "CONTAINER_UPLOAD_BINDING_INVALID"));
    }

    public static void Context(RequestContext? context)
    {
        if (context is null || context.Capability.Length != 0 || string.IsNullOrWhiteSpace(context.ActorSubject) ||
            context.ActorSubject.Length > 256 || context.ActorSubject.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(context.TraceId) || context.TraceId.Length > 128 || context.TraceId.Any(char.IsControl))
            throw new ArgumentException("CONTAINER_UPLOAD_CONTEXT_INVALID");
        FileTransferPolicy.Identity(context.JobId);
        FileTransferPolicy.Identity(context.IdempotencyKey);
    }

    public static void Reference(ContainerUploadReference request)
    {
        ContainerFilePolicy.Location(new() { ContainerId = request.ContainerId, InstanceId = request.InstanceId, Path = "" }, allowEmpty: true);
        FileTransferPolicy.Identity(request.OperationId); FileTransferPolicy.Hash(request.Owner);
        FileTransferPolicy.Hash(request.ExpectedTargetProof); Context(request.Context);
    }

    public static string Open(ContainerUploadFrame frame)
    {
        var h = frame.Header;
        if (h is null || frame.Data.Length != 0 || frame.Offset != 0 || frame.Commit || h.SizeBytes is < 0 or > FileTransferPolicy.MaximumBytes)
            throw new ArgumentException("CONTAINER_UPLOAD_HEADER_INVALID");
        ContainerFilePolicy.Location(h.Location); FileTransferPolicy.Identity(h.OperationId); FileTransferPolicy.Hash(h.Owner);
        FileTransferPolicy.Hash(h.Sha256); FileTransferPolicy.Hash(h.ExpectedParentVersion); FileTransferPolicy.Hash(h.ExpectedTargetProof);
        if (h.ExpectedVersion != "ABSENT") FileTransferPolicy.Hash(h.ExpectedVersion);
        Context(h.Context);
        return JsonSerializer.Serialize(new { containerId = h.Location.ContainerId, instanceId = h.Location.InstanceId, path = h.Location.Path,
            operationId = h.OperationId, owner = h.Owner, sizeBytes = h.SizeBytes, sha256 = h.Sha256, expectedVersion = h.ExpectedVersion,
            expectedParentVersion = h.ExpectedParentVersion, expectedTargetProof = h.ExpectedTargetProof });
    }

    public static string Input(ContainerUploadFrame frame, ref long offset, long size)
    {
        if (frame.Header is not null) throw new ArgumentException("CONTAINER_UPLOAD_TARGET_CHANGED");
        if (frame.Commit && frame.Data.Length == 0 && frame.Offset == 0 && offset == size) return "{\"type\":\"commit\"}";
        if (frame.Commit || frame.Data.Length is < 1 or > 32768 || frame.Offset != offset || offset + frame.Data.Length > size)
            throw new ArgumentException("CONTAINER_UPLOAD_CHUNK_INVALID");
        var value = JsonSerializer.Serialize(new { type = "chunk", offset, data = frame.Data.ToBase64() });
        offset += frame.Data.Length; return value;
    }

    public static ContainerUploadTarget Target(JsonElement value, ContainerFileLocation location)
    {
        if (value.GetProperty("containerId").GetString() != location.ContainerId || value.GetProperty("instanceId").GetString() != location.InstanceId ||
            value.GetProperty("path").GetString() != location.Path) throw Invalid();
        var result = new ContainerUploadTarget { Location = location.Clone(), TargetProof = Text(value, "targetProof"),
            ExpectedParentVersion = Text(value, "expectedParentVersion"), ExpectedVersion = Text(value, "expectedVersion"),
            CurrentSizeBytes = value.GetProperty("currentSizeBytes").GetInt64() };
        OutputHash(result.TargetProof); OutputHash(result.ExpectedParentVersion);
        if (result.ExpectedVersion != "ABSENT") OutputHash(result.ExpectedVersion);
        if (result.CurrentSizeBytes is < 0 or > FileTransferPolicy.MaximumBytes || result.ExpectedVersion == "ABSENT" && result.CurrentSizeBytes != 0) throw Invalid();
        return result;
    }

    public static ContainerUploadReceipt Receipt(JsonElement value, ContainerUploadReference reference, ContainerUploadHeader? expected = null)
    {
        if (value.GetProperty("containerId").GetString() != reference.ContainerId || value.GetProperty("instanceId").GetString() != reference.InstanceId ||
            value.GetProperty("operationId").GetString() != reference.OperationId || value.GetProperty("targetProof").GetString() != reference.ExpectedTargetProof) throw Invalid();
        var receipt = new FileUploadReceipt { OperationId = reference.OperationId, State = Text(value, "state"),
            SizeBytes = value.GetProperty("sizeBytes").GetInt64(), Sha256 = Text(value, "sha256"),
            Version = value.TryGetProperty("version", out var version) ? version.GetString() ?? "" : "",
            PreviousRetained = value.TryGetProperty("previousRetained", out var previous) && previous.GetBoolean(),
            BindingCurrent = value.TryGetProperty("bindingCurrent", out var binding) && binding.GetBoolean(),
            Replayed = value.GetProperty("replayed").GetBoolean() };
        if (receipt.State is not ("Receiving" or "Verified" or "Staged" or "Interrupted" or "Aborted" or "Discarded" or "Unknown" or
                "PublishPreparing" or "PublishApplying" or "PublishInterrupted" or "Published" or "PublishDiscarding" or "PublishDiscarded") ||
            receipt.SizeBytes is < 0 or > FileTransferPolicy.MaximumBytes || receipt.State != "Published" && (receipt.BindingCurrent || receipt.PreviousRetained || receipt.Version.Length != 0)) throw Invalid();
        OutputHash(receipt.Sha256);
        if (receipt.State == "Published") OutputHash(receipt.Version);
        if (expected is not null && (receipt.State != "Staged" || receipt.SizeBytes != expected.SizeBytes || receipt.Sha256 != expected.Sha256)) throw Invalid();
        var result = new ContainerUploadReceipt { ContainerId = reference.ContainerId, InstanceId = reference.InstanceId,
            TargetProof = reference.ExpectedTargetProof, Upload = receipt };
        if (value.TryGetProperty("backupCapture", out var backup) && backup.ValueKind != JsonValueKind.Null)
        {
            var proof = new FileHddBackupProof { OperationId = Text(backup, "operationId"), StorageId = Text(backup, "storageId"),
                SourceVersion = Text(backup, "sourceVersion"), SizeBytes = backup.GetProperty("sizeBytes").GetInt64(),
                Sha256 = Text(backup, "sha256"), CreatedAtUnixMs = backup.GetProperty("createdAtUnixMs").GetInt64(),
                DifferentSourceDevice = backup.GetProperty("differentSourceDevice").GetBoolean() };
            if (backup.GetProperty("resourceType").GetString() != "ContainerFile" || backup.GetProperty("containerId").GetString() != reference.ContainerId ||
                backup.GetProperty("instanceId").GetString() != reference.InstanceId || backup.GetProperty("targetProof").GetString() != reference.ExpectedTargetProof ||
                proof.OperationId != reference.OperationId || proof.StorageId != "database-platform.backup.hdd" ||
                proof.SizeBytes is < 0 or > FileTransferPolicy.MaximumBytes || proof.CreatedAtUnixMs is < 0 or > 253402300799999L) throw Invalid();
            OutputHash(proof.SourceVersion); OutputHash(proof.Sha256);
            if (expected is not null && (expected.ExpectedVersion == "ABSENT" || proof.SourceVersion != expected.ExpectedVersion)) throw Invalid();
            result.Backup = new() { Proof = proof, ContainerId = reference.ContainerId, InstanceId = reference.InstanceId, TargetProof = reference.ExpectedTargetProof };
        }
        if (receipt.PreviousRetained && result.Backup is null) throw Invalid();
        return result;
    }

    public static RpcException Failure(string? code)
    {
        var status = code switch
        {
            "CONTAINER_UPLOAD_PROTECTED" or "CONTAINER_PROTECTED" or "CONTAINER_UNSAFE_ISOLATION" or "CONTAINER_UNSAFE_MOUNT" or
                "CONTAINER_FILES_PROTECTED" or "CONTAINER_FILES_SHARED_CORE_MOUNT" or "CONTAINER_UPLOAD_SCOPE_MISMATCH" or
                "CONTAINER_UPLOAD_PUBLISH_READ_ONLY" or "CONTAINER_UPLOAD_PUBLISH_MOUNT_ROOT" or "FILES_REGULAR_SINGLE_LINK_REQUIRED" => StatusCode.PermissionDenied,
            "CONTAINER_UPLOAD_VERSION_CONFLICT" or "CONTAINER_UPLOAD_PARENT_VERSION_CONFLICT" or "FILES_VERSION_CONFLICT" or
                "CONTAINER_UPLOAD_IDEMPOTENCY_CONFLICT" => StatusCode.Aborted,
            "CONTAINER_UPLOAD_BUSY" or "CONTAINER_UPLOAD_CAPACITY_LOW" or "CONTAINER_UPLOAD_RECEIPT_LIMIT" or "CONTAINER_UPLOAD_SIZE_LIMIT" or
                "FILES_BACKUP_CAPACITY_REQUIRED" or "FILES_BACKUP_HISTORY_LIMIT" => StatusCode.ResourceExhausted,
            "CONTAINER_UPLOAD_TIMEOUT" or "CONTAINER_UPLOAD_PUBLISH_TIMEOUT" or "FILES_INPUT_TIMEOUT" => StatusCode.DeadlineExceeded,
            "CONTAINER_UPLOAD_NOT_FOUND" or "CONTAINER_UPLOAD_PATH_NOT_FOUND" => StatusCode.NotFound,
            "CONTAINER_UPLOAD_TARGET_CHANGED" or "CONTAINER_FILES_INSTANCE_CHANGED" or "CONTAINER_FILES_ROOT_CHANGED" or "CONTAINER_FILES_TARGET_CHANGED" or
                "CONTAINER_NOT_RUNNING" or "CONTAINER_UPLOAD_PREVIOUS_OPERATION_INCOMPLETE" or "CONTAINER_UPLOAD_ALREADY_PUBLISHED" or
                "CONTAINER_UPLOAD_STAGED_REQUIRED" or "CONTAINER_UPLOAD_PAYLOAD_CHANGED" or "CONTAINER_UPLOAD_BACKUP_BINDING_CHANGED" or
                "CONTAINER_UPLOAD_PUBLISH_PARENT_CHANGED" or "CONTAINER_UPLOAD_PUBLISH_OBJECT_CHANGED" or "CONTAINER_UPLOAD_PUBLISH_PRIVATE_CHANGED" or
                "CONTAINER_UPLOAD_PUBLISH_OWNER_CHANGED" or "FILES_BACKUP_INCOMPLETE" or "FILES_BACKUP_CONTENT_INVALID" => StatusCode.FailedPrecondition,
            "FILES_BACKUP_METADATA_UNSAFE" or "FILES_BACKUP_CONFIGURATION_INVALID" or "FILES_BACKUP_MOUNT_MISMATCH" or
                "FILES_BACKUP_FILESYSTEM_UNSUPPORTED" or "FILES_BACKUP_DEVICE_METADATA_UNSAFE" or "FILES_BACKUP_DEVICE_MISMATCH" or
                "FILES_BACKUP_HDD_REQUIRED" or "FILES_BACKUP_READ_ONLY" or "FILES_BACKUP_DIRECTORY_UNSAFE" or "FILES_BACKUP_IDEMPOTENCY_CONFLICT" or
                "CONTAINER_UPLOAD_STATE_UNSAFE" or "CONTAINER_UPLOAD_MODULE_UNSAFE" or "CONTAINER_UPLOAD_RECEIPT_INVALID" => StatusCode.FailedPrecondition,
            _ => StatusCode.Unavailable
        };
        return new(new(status, status == StatusCode.Unavailable ? "CONTAINER_UPLOAD_UNAVAILABLE" : code!));
    }
    private static InvalidOperationException Invalid() => new("CONTAINER_UPLOAD_OUTPUT_INVALID");
    private static string Text(JsonElement value, string key) => value.GetProperty(key).GetString() ?? throw Invalid();
    private static void OutputHash(string value)
    {
        try { FileTransferPolicy.Hash(value); }
        catch (ArgumentException) { throw Invalid(); }
    }
}
