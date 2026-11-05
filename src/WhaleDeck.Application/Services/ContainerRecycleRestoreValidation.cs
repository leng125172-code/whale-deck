using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Services;

public static class ContainerRecycleRestoreValidation
{
    public static void Command(ContainerRecycleRestoreCommandDto? value)
    {
        if (value?.Source is not { OperationId: var id } source || id == Guid.Empty) throw new ArgumentException("CONTAINER_RECYCLE_RESTORE_COMMAND_REQUIRED");
        ContainerFileValidation.Hash(source.Owner); ContainerRecycleValidation.Plan(source.Plan); ContainerFileValidation.Hash(value.HistoryDigest);
    }
    public static void Plan(ContainerRecycleRestorePlanDto? value)
    {
        if (value is null || value.SchemaVersion != 1) throw new ArgumentException("CONTAINER_RECYCLE_RESTORE_PLAN_INVALID");
        Command(value.Command); var original = value.Command.Source.Plan;
        if (value.ParentIdentity != original.ParentIdentity || value.TargetProof != original.TargetProof ||
            value.OwnerUid != original.OwnerUid || value.OwnerGid != original.OwnerGid || value.PrivateIdentity is not { Inode: > 0 } ||
            value.RetainedIdentity != original.SourceIdentity || value.RetainedChangedAtUnixNs < original.SourceChangedAtUnixNs)
            throw new ArgumentException("CONTAINER_RECYCLE_RESTORE_PLAN_INVALID");
        ContainerFileValidation.Hash(value.ParentVersion); ContainerFileValidation.Hash(value.Fingerprint);
    }
    public static void Operation(ContainerRecycleRestoreOperationDto? value)
    {
        if (value is null) throw new ArgumentException("CONTAINER_RECYCLE_RESTORE_CONTEXT_INVALID");
        Plan(value.Plan);
        if (value.Id == value.Plan.Command.Source.OperationId) throw new ArgumentException("CONTAINER_RECYCLE_RESTORE_NEW_OPERATION_REQUIRED");
        ContainerRecycleValidation.Operation(new(value.Id, value.Owner, value.Plan.Command.Source.Plan, value.Context));
    }
    public static void Receipt(ContainerRecycleRestoreOperationDto operation, ContainerRecycleRestoreReceiptDto value)
    {
        if (value is null || value.Id != operation.Id || value.SourceOperationId != operation.Plan.Command.Source.OperationId ||
            value.Fingerprint != operation.Plan.Fingerprint || value.TargetProof != operation.Plan.TargetProof || value.State is not ("Completed" or "Aborted" or "Unknown") ||
            value.HistoryDigest != (value.HistoryVerified ? operation.Plan.Command.HistoryDigest : "") || value.HistoryAvailable && !value.HistoryVerified ||
            value.BindingCurrent && !(value.State == "Completed" && value.HistoryAvailable && value.HistoryVerified) ||
            value.SourceState is not (null or "Trashed" or "Restored" or "Unknown" or "Aborted")) throw Invalid();
        if (value.State == "Completed" && (!value.HistoryVerified || value.SourceState != "Restored" || value.SourceRetained != false || value.SourceRestored != true || value.RestoredByThisOperation != true) ||
            value.State == "Unknown" && (!value.HistoryVerified || value.SourceRetained is not null || value.SourceRestored is not null || value.RestoredByThisOperation is not null) ||
            value.State == "Aborted" && (value.RestoredByThisOperation != false ||
                (value.HistoryVerified ? value.SourceState != "Trashed" || value.SourceRetained != true || value.SourceRestored != false :
                    value.SourceState is not null || value.SourceRetained is not null || value.SourceRestored is not null))) throw Invalid();
    }
    public static InvalidOperationException Invalid() => new("CONTAINER_RECYCLE_RESTORE_OUTPUT_INVALID");
}
