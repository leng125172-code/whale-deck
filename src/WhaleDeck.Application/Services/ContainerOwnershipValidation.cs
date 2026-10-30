using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Services;

public static class ContainerOwnershipValidation
{
    public static void Command(ContainerOwnershipCommandDto? command)
    {
        if (command is null) throw new ArgumentException("CONTAINER_OWNERSHIP_COMMAND_REQUIRED");
        ContainerFileValidation.Location(command.Location); ContainerFileValidation.Hash(command.ExpectedVersion);
        if (command.Uid == uint.MaxValue || command.Gid == uint.MaxValue)
            throw new ArgumentException("CONTAINER_OWNERSHIP_UID_GID_INVALID");
    }
    public static void Plan(ContainerOwnershipPlanDto? plan)
    {
        if (plan is null || plan.SchemaVersion != 1 || plan.ParentIdentity is not { Inode: > 0 } || plan.OwnerUid == uint.MaxValue || plan.OwnerGid == uint.MaxValue)
            throw new ArgumentException("CONTAINER_OWNERSHIP_PLAN_INVALID");
        Command(plan.Command);
        if (plan.Kind is not ("File" or "Directory") || plan.SourceIdentity is not { Inode: > 0, LinkCount: > 0 } source ||
            source.Mode > ushort.MaxValue || (source.Mode & 0xE00) != 0 || source.OwnerUid == uint.MaxValue || source.OwnerGid == uint.MaxValue || source.SizeBytes < 0 ||
            (source.Mode & 0xF000) != (plan.Kind == "File" ? 0x8000U : 0x4000U) || plan.Kind == "File" && source.LinkCount != 1 ||
            plan.NoChange != (source.OwnerUid == plan.Command.Uid && source.OwnerGid == plan.Command.Gid)) throw new ArgumentException("CONTAINER_OWNERSHIP_PLAN_INVALID");
        ContainerFileValidation.Hash(plan.AttributesDigest);
        ContainerFileValidation.Hash(plan.TargetProof); ContainerFileValidation.Hash(plan.ParentVersion); ContainerFileValidation.Hash(plan.Fingerprint);
    }
    public static void Operation(ContainerOwnershipOperationDto operation)
    {
        if (operation is null || operation.Id == Guid.Empty || operation.Context is null || operation.Context.JobId != operation.Id ||
            operation.Context.IdempotencyKey == Guid.Empty || string.IsNullOrWhiteSpace(operation.Context.ActorSubject) || operation.Context.ActorSubject.Length > 256 ||
            operation.Context.ActorSubject.Any(char.IsControl) || string.IsNullOrWhiteSpace(operation.Context.TraceId) || operation.Context.TraceId.Length > 128 || operation.Context.TraceId.Any(char.IsControl))
            throw new ArgumentException("CONTAINER_OWNERSHIP_CONTEXT_INVALID");
        Plan(operation.Plan); ContainerFileValidation.Hash(operation.Owner);
    }
    public static void Receipt(ContainerOwnershipOperationDto operation, ContainerOwnershipReceiptDto receipt)
    {
        var location = operation.Plan.Command.Location;
        if (receipt is null || receipt.Id != operation.Id || receipt.ContainerId != location.ContainerId || receipt.InstanceId != location.InstanceId ||
            receipt.TargetProof != operation.Plan.TargetProof || receipt.Fingerprint != operation.Plan.Fingerprint || receipt.Kind != operation.Plan.Kind || receipt.NoChange != operation.Plan.NoChange ||
            receipt.HistoryVerified && !receipt.HistoryMayExist || receipt.HistoryVerified != (receipt.HistoryDigest is { Length: 64 }) ||
            !receipt.HistoryVerified && receipt.HistoryDigest != "" ||
            receipt.State is not ("Completed" or "Aborted" or "Unknown") ||
            receipt.State == "Completed" && (receipt.OwnerConfirmed != true || !receipt.HistoryVerified) ||
            receipt.State == "Aborted" && receipt.OwnerConfirmed != false || receipt.State == "Unknown" && receipt.OwnerConfirmed is not null ||
            receipt.State != "Completed" && (receipt.Version != "" || receipt.BindingCurrent)) throw Invalid();
        if (receipt.HistoryVerified) try { ContainerFileValidation.Hash(receipt.HistoryDigest); } catch (ArgumentException) { throw Invalid(); }
        if (receipt.State == "Completed") try { ContainerFileValidation.Hash(receipt.Version); } catch (ArgumentException) { throw Invalid(); }
    }
    public static InvalidOperationException Invalid() => new("CONTAINER_OWNERSHIP_OUTPUT_INVALID");
}
