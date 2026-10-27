using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Contracts;

/// <summary>Canonical historical facts, including the opaque owner only inside the digest.</summary>
public static class ContainerUploadRecoveryBinding
{
    public static string ObservationHash(ContainerUploadReference original, ContainerUploadRecoveryObservation observation)
    {
        var facts = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["containerId"] = observation.ContainerId, ["instanceId"] = observation.InstanceId, ["operationId"] = observation.OperationId,
            ["targetProof"] = observation.TargetProof, ["rootState"] = observation.RootState, ["originalRecordHash"] = observation.OriginalRecordHash,
            ["sealed"] = observation.Sealed, ["sealId"] = observation.SealId.Length == 0 ? null : observation.SealId,
            ["bindingCurrent"] = observation.BindingCurrent, ["mayHavePublished"] = observation.MayHavePublished,
            ["retainedObjects"] = observation.RetainedObjects, ["owner"] = original.Owner
        };
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            "whaledeck-container-upload-recovery-v1\nobservation\n" + JsonSerializer.Serialize(facts))));
    }
}
