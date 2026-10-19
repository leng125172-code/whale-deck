using System.Text;
using System.Text.Json;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Agent;

public sealed partial class AgentGateway : IFileRecoveryGateway
{
    public Task<FileRecoveryObservation> InspectForRecoveryAsync(FileRecoverySnapshot original, TerminalActor currentActor, CancellationToken cancellationToken) =>
        RecoveryAction(original, currentActor, false, cancellationToken);
    public Task<FileRecoveryObservation> FenceForRecoveryAsync(FileRecoverySnapshot original, TerminalActor currentActor, CancellationToken cancellationToken) =>
        RecoveryAction(original, currentActor, true, cancellationToken);
    private async Task<FileRecoveryObservation> RecoveryAction(FileRecoverySnapshot original, TerminalActor currentActor, bool fence, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(currentActor.Subject) || currentActor.Subject.Length > 256 || currentActor.Subject.Any(char.IsControl) ||
            !FileManagementService.Hex(currentActor.LoginBinding) || string.IsNullOrWhiteSpace(currentActor.TraceId) || currentActor.TraceId.Length > 64 || currentActor.TraceId.Any(char.IsControl))
            throw new ArgumentException("FILES_RECOVERY_CURRENT_ACTOR_INVALID");
        switch (original.Kind)
        {
            case FileRecoveryKinds.Upload:
            {
                var row = _recoveryMaterial.Read<FileUploadOperation>(original);
                // Upload lookups carry no human identity; the old opaque owner
                // is only the original resource binding. Audit uses currentActor.
                var receipt = fence ? await DiscardFileAsync(row, cancellationToken) : await UploadReceiptAsync(row, cancellationToken);
                if (receipt?.IsInProgress == true || receipt?.State is "Staged" or "Applying") throw new FileRecoveryBusyException();
                if (receipt?.State == "Completed")
                {
                    if (receipt.SizeBytes != row.SizeBytes || receipt.Sha256 != row.Sha256 || !FileManagementService.Hex(receipt.Version)) throw new FileTransferRejectedException();
                    FileManagementService.ValidateBackup(row, receipt.HddBackup);
                }
                return RecoveryObservation(original, receipt?.State, receipt, receipt?.BindingCurrent == true);
            }
            case FileRecoveryKinds.Mutation:
            {
                var row = _recoveryMaterial.Read<FileMutationOperation>(original);
                var receipt = fence ? await MutationAction(row, true, cancellationToken, currentActor) : await InspectMutationAsync(row, cancellationToken);
                return RecoveryObservation(original, receipt?.State, receipt, receipt?.BindingCurrent == true);
            }
            case FileRecoveryKinds.Archive:
            {
                var row = _recoveryMaterial.Read<FileArchiveOperation>(original);
                var receipt = fence ? await ArchiveAction(row, true, cancellationToken, currentActor) : await InspectArchiveAsync(row, cancellationToken);
                return RecoveryObservation(original, receipt?.State, receipt, receipt?.BindingCurrent == true);
            }
            case FileRecoveryKinds.TreeCopy:
            {
                var row = _recoveryMaterial.Read<FileTreeCopyOperation>(original);
                var receipt = fence ? await TreeAction(row, true, cancellationToken, currentActor) : await InspectTreeCopyAsync(row, cancellationToken);
                return RecoveryObservation(original, receipt?.State, receipt, receipt?.State == "Completed");
            }
            case FileRecoveryKinds.TreeMove:
            {
                var row = _recoveryMaterial.Read<FileTreeMoveOperation>(original);
                var receipt = fence ? await MoveTreeAction(row, true, cancellationToken, currentActor) : await InspectTreeMoveAsync(row, cancellationToken);
                return RecoveryObservation(original, receipt?.State, receipt, receipt?.State == "Completed");
            }
            case FileRecoveryKinds.TreePacking:
            {
                var row = _recoveryMaterial.Read<FileTreePackingOperation>(original);
                var receipt = fence ? await PackingAction(row, true, cancellationToken, currentActor) : await InspectTreePackingAsync(row, cancellationToken);
                return RecoveryObservation(original, receipt?.State, receipt, receipt?.State == "Completed");
            }
            case FileRecoveryKinds.TreeUnpacking:
            {
                var row = _recoveryMaterial.Read<FileTreeUnpackingOperation>(original);
                var receipt = fence ? await UnpackingAction(row, true, cancellationToken, currentActor) : await InspectTreeUnpackingAsync(row, cancellationToken);
                return RecoveryObservation(original, receipt?.State, receipt, receipt?.State == "Completed");
            }
            default: throw new ArgumentException("FILES_RECOVERY_KIND_INVALID");
        }
    }
    internal static FileRecoveryObservation RecoveryObservation<T>(FileRecoverySnapshot original, string? state, T? receipt, bool current)
    {
        state ??= "Missing";
        if (state is not ("Missing" or "Unknown" or "Aborted" or "Completed")) throw new FileTransferRejectedException();
        var json = receipt is null ? "null" : JsonSerializer.Serialize(receipt);
        if (Encoding.UTF8.GetByteCount(json) > 16384 || state == "Missing" && json != "null") throw new FileTransferRejectedException();
        var hash = FileRecoveryValidation.ReceiptHash(original.Kind, original.Binding.ExecutionJobId, state, json);
        return new(state, hash, current) { ReceiptJson = json };
    }
}
