using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

/// <summary>Historical journal observations support acknowledgment only, never fabricated success.</summary>
public static class ContainerUploadRecoveryValidation
{
    private static readonly HashSet<string> States = new(StringComparer.Ordinal)
    { "Missing", "Receiving", "Verified", "Staged", "Interrupted", "Aborted", "Discarded", "Prevented", "Unknown", "PublishPreparing", "PublishApplying", "PublishInterrupted", "Published", "PublishDiscarding", "PublishDiscarded" };
    private static readonly HashSet<string> Fields = new(StringComparer.Ordinal)
    { "ContainerId", "InstanceId", "OperationId", "TargetProof", "RootState", "OriginalRecordHash", "Sealed", "SealId", "MayHavePublished", "RetainedObjects", "ObservationHash" };

    public static void Observation(ContainerFileUploadOperation original, ContainerUploadRecoveryObservationDto observation)
    {
        _ = ContainerFileUploadIntentValidation.ReadBinding(original);
        if (observation.ContainerId != original.ContainerId || observation.InstanceId != original.InstanceId || observation.OperationId != original.Id ||
            observation.TargetProof != original.TargetProof || !States.Contains(observation.RootState) ||
            observation.Sealed != observation.SealId.HasValue || observation.SealId == Guid.Empty || observation.SealId == original.Id ||
            observation.RetainedObjects != (observation.RootState is not ("Missing" or "Prevented")) ||
            observation.RootState is "Missing" or "Prevented" && observation.MayHavePublished ||
            observation.RootState is "Published" or "Unknown" && !observation.MayHavePublished ||
            !FileManagementService.Hex(observation.OriginalRecordHash) || !FileManagementService.Hex(observation.ObservationHash) ||
            observation.ObservationHash != Digest(original.OwnerHash, observation)) throw Invalid();
        if (observation.RootState == "Missing" && observation.OriginalRecordHash != "B73B451CAC7D3B4D6CD095A4CB10A7F062D17ACA014FD693F2C99DD5F542441A") throw Invalid();
    }
    public static FileRecoveryObservation Wrap(FileRecoverySnapshot snapshot, ContainerFileUploadOperation original, ContainerUploadRecoveryObservationDto observation)
    {
        if (snapshot.Kind != FileRecoveryKinds.ContainerUpload || snapshot.Binding.IntentId != original.Id || snapshot.Binding.ExecutionJobId != original.Id) throw Invalid();
        Observation(original, observation);
        var json = JsonSerializer.Serialize(observation);
        return new("Unknown", FileRecoveryValidation.ReceiptHash(snapshot.Kind, original.Id, "Unknown", json), false) { ReceiptJson = json };
    }
    public static ContainerUploadRecoveryObservationDto Decode(FileRecoverySnapshot snapshot, FileRecoveryObservation observation)
    {
        if (snapshot.Kind != FileRecoveryKinds.ContainerUpload || observation.State != "Unknown" || observation.BindingCurrent ||
            Encoding.UTF8.GetByteCount(observation.ReceiptJson) > 16384) throw Invalid();
        try
        {
            using var document = JsonDocument.Parse(observation.ReceiptJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw Invalid();
            var names = document.RootElement.EnumerateObject().Select(x => x.Name).ToArray();
            if (names.Length != Fields.Count || !Fields.SetEquals(names)) throw Invalid();
            var original = JsonSerializer.Deserialize<ContainerFileUploadOperation>(snapshot.OriginalJson, FileTreeCopyValidation.JsonOptions) ?? throw Invalid();
            var value = JsonSerializer.Deserialize<ContainerUploadRecoveryObservationDto>(observation.ReceiptJson, FileTreeCopyValidation.JsonOptions) ?? throw Invalid();
            var canonical = Wrap(snapshot, original, value);
            if (canonical.ReceiptHash != observation.ReceiptHash) throw Invalid();
            return value;
        }
        catch (Exception error) when (error is JsonException or ArgumentException or KeyNotFoundException)
        { throw Invalid(); }
    }
    public static bool MatchesApprovedOrSealed(FileRecoverySnapshot snapshot, FileRecoveryObservation current)
    {
        if (snapshot.RecoveryExecutionId is not { } execution || snapshot.ApprovedObservation is null) return false;
        var approved = Decode(snapshot, snapshot.ApprovedObservation); var received = Decode(snapshot, current);
        if (!received.Sealed) return false;
        if (approved.Sealed) return approved == received;
        // Only this recovery may account for a lost seal reply. Every original
        // journal fact must still equal the human-approved observation.
        return received.SealId == execution && (received with { Sealed = false, SealId = null, ObservationHash = approved.ObservationHash }) == approved;
    }
    private static string Digest(string owner, ContainerUploadRecoveryObservationDto observation)
    {
        // Independent verification at the durable business boundary, after
        // the transport has performed its own protobuf/Root checks.
        var facts = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["containerId"] = observation.ContainerId, ["instanceId"] = observation.InstanceId, ["operationId"] = observation.OperationId.ToString("N"),
            ["targetProof"] = observation.TargetProof, ["rootState"] = observation.RootState, ["originalRecordHash"] = observation.OriginalRecordHash,
            ["sealed"] = observation.Sealed, ["sealId"] = observation.SealId?.ToString("N"), ["bindingCurrent"] = false,
            ["mayHavePublished"] = observation.MayHavePublished, ["retainedObjects"] = observation.RetainedObjects, ["owner"] = owner
        };
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("whaledeck-container-upload-recovery-v1\nobservation\n" + JsonSerializer.Serialize(facts))));
    }
    private static InvalidOperationException Invalid() => new("CONTAINER_UPLOAD_RECOVERY_OBSERVATION_INVALID");
}
