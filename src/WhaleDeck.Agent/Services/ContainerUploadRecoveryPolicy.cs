using System.Text.Json;
using WhaleDeck.Contracts.Agent.V1;
using WhaleDeck.Contracts;

namespace WhaleDeck.Agent.Services;

/// <summary>Historical receipt metadata never authorizes another publication.</summary>
internal static class ContainerUploadRecoveryPolicy
{
    private static readonly HashSet<string> Fields = new(StringComparer.Ordinal)
    { "containerId", "instanceId", "operationId", "targetProof", "rootState", "originalRecordHash", "sealed", "sealId", "bindingCurrent", "mayHavePublished", "retainedObjects", "observationHash" };
    private static readonly HashSet<string> States = new(StringComparer.Ordinal)
    { "Missing", "Receiving", "Verified", "Staged", "Interrupted", "Aborted", "Discarded", "Prevented", "Unknown", "PublishPreparing", "PublishApplying", "PublishInterrupted", "Published", "PublishDiscarding", "PublishDiscarded" };

    public static void Request(ContainerUploadRecoveryRequest request)
    {
        if (request.Original is null) throw new ArgumentException("CONTAINER_UPLOAD_RECOVERY_REQUEST_INVALID");
        ContainerUploadPolicy.Reference(request.Original); ContainerUploadPolicy.Context(request.Context);
        FileTransferPolicy.Identity(request.RecoveryId); FileTransferPolicy.Hash(request.ExpectedObservationHash);
        if (request.RecoveryId == request.Original.OperationId || request.Context.JobId != request.RecoveryId || request.Context.IdempotencyKey != request.RecoveryId)
            throw new ArgumentException("CONTAINER_UPLOAD_RECOVERY_REQUEST_INVALID");
    }

    public static ContainerUploadRecoveryObservation Observation(JsonElement value, ContainerUploadReference original, string? sealedBy = null)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Invalid();
        var names = value.EnumerateObject().Select(x => x.Name).ToArray();
        if (names.Length != Fields.Count || !Fields.SetEquals(names)) throw Invalid();
        var result = new ContainerUploadRecoveryObservation
        {
            ContainerId = Text(value, "containerId"), InstanceId = Text(value, "instanceId"), OperationId = Text(value, "operationId"),
            TargetProof = Text(value, "targetProof"), RootState = Text(value, "rootState"), OriginalRecordHash = Text(value, "originalRecordHash"),
            Sealed = value.GetProperty("sealed").GetBoolean(), SealId = value.GetProperty("sealId").ValueKind == JsonValueKind.Null ? "" : Text(value, "sealId"),
            BindingCurrent = value.GetProperty("bindingCurrent").GetBoolean(), MayHavePublished = value.GetProperty("mayHavePublished").GetBoolean(),
            RetainedObjects = value.GetProperty("retainedObjects").GetBoolean(), ObservationHash = Text(value, "observationHash")
        };
        if (result.ContainerId != original.ContainerId || result.InstanceId != original.InstanceId || result.OperationId != original.OperationId ||
            result.TargetProof != original.ExpectedTargetProof || !States.Contains(result.RootState) || result.BindingCurrent ||
            result.Sealed != (result.SealId.Length != 0) || result.SealId == original.OperationId ||
            sealedBy is not null && (!result.Sealed || result.SealId != sealedBy) ||
            result.RetainedObjects != (result.RootState is not ("Missing" or "Prevented")) ||
            result.RootState is "Missing" or "Prevented" && result.MayHavePublished ||
            result.RootState is "Published" or "Unknown" && !result.MayHavePublished) throw Invalid();
        try
        {
            FileTransferPolicy.Hash(result.OriginalRecordHash); FileTransferPolicy.Hash(result.ObservationHash);
            if (result.Sealed) FileTransferPolicy.Identity(result.SealId);
        }
        catch (ArgumentException) { throw Invalid(); }
        if (ContainerUploadRecoveryBinding.ObservationHash(original, result) != result.ObservationHash) throw Invalid();
        return result;
    }
    private static string Text(JsonElement value, string key) => value.GetProperty(key).GetString() ?? throw Invalid();
    private static InvalidOperationException Invalid() => new("CONTAINER_UPLOAD_RECOVERY_OUTPUT_INVALID");
}
