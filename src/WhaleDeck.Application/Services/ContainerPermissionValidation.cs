using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Services;

public static class ContainerPermissionValidation
{
    public static void Command(ContainerPermissionCommandDto? command)
    {
        if (command is null) throw new ArgumentException("CONTAINER_PERMISSION_COMMAND_REQUIRED");
        ContainerFileValidation.Location(command.Location); ContainerFileValidation.Hash(command.ExpectedVersion);
        if (command.Mode is not { Length: 4 } || command.Mode[0] != '0' || command.Mode.Skip(1).Any(c => c is < '0' or > '7'))
            throw new ArgumentException("CONTAINER_PERMISSION_MODE_INVALID");
    }
    public static void Plan(ContainerPermissionPlanDto? plan)
    {
        if (plan is null || plan.SchemaVersion != 1 || plan.ParentIdentity is not { Inode: > 0 } || plan.OwnerUid == uint.MaxValue || plan.OwnerGid == uint.MaxValue)
            throw new ArgumentException("CONTAINER_PERMISSION_PLAN_INVALID");
        Command(plan.Command);
        if (plan.Kind is not ("File" or "Directory") || plan.SourceIdentity is not { Inode: > 0, LinkCount: > 0 } source ||
            source.Mode > ushort.MaxValue || (source.Mode & 0xE00) != 0 || source.OwnerUid == uint.MaxValue || source.OwnerGid == uint.MaxValue || source.SizeBytes < 0 ||
            (source.Mode & 0xF000) != (plan.Kind == "File" ? 0x8000U : 0x4000U) || plan.Kind == "File" && source.LinkCount != 1 ||
            plan.NoChange != ((source.Mode & 0x1FF) == Convert.ToUInt32(plan.Command.Mode, 8))) throw new ArgumentException("CONTAINER_PERMISSION_PLAN_INVALID");
        ContainerFileValidation.Hash(plan.AttributesDigest);
        ContainerFileValidation.Hash(plan.TargetProof); ContainerFileValidation.Hash(plan.ParentVersion); ContainerFileValidation.Hash(plan.Fingerprint);
    }
    public static void Operation(ContainerPermissionOperationDto operation)
    {
        if (operation is null || operation.Id == Guid.Empty || operation.Context is null || operation.Context.JobId != operation.Id ||
            operation.Context.IdempotencyKey == Guid.Empty || string.IsNullOrWhiteSpace(operation.Context.ActorSubject) || operation.Context.ActorSubject.Length > 256 ||
            operation.Context.ActorSubject.Any(char.IsControl) || string.IsNullOrWhiteSpace(operation.Context.TraceId) || operation.Context.TraceId.Length > 128 || operation.Context.TraceId.Any(char.IsControl))
            throw new ArgumentException("CONTAINER_PERMISSION_CONTEXT_INVALID");
        Plan(operation.Plan); ContainerFileValidation.Hash(operation.Owner);
    }
    public static void Receipt(ContainerPermissionOperationDto operation, ContainerPermissionReceiptDto receipt)
    {
        var location = operation.Plan.Command.Location;
        if (receipt is null || receipt.Id != operation.Id || receipt.ContainerId != location.ContainerId || receipt.InstanceId != location.InstanceId ||
            receipt.TargetProof != operation.Plan.TargetProof || receipt.Fingerprint != operation.Plan.Fingerprint || receipt.Kind != operation.Plan.Kind || receipt.NoChange != operation.Plan.NoChange ||
            receipt.HistoryVerified && !receipt.HistoryMayExist || receipt.HistoryVerified != (receipt.HistoryDigest is { Length: 64 }) ||
            !receipt.HistoryVerified && receipt.HistoryDigest != "" ||
            receipt.State is not ("Completed" or "Aborted" or "Unknown") ||
            receipt.State == "Completed" && (receipt.ModeConfirmed != true || !receipt.HistoryVerified) ||
            receipt.State == "Aborted" && receipt.ModeConfirmed != false || receipt.State == "Unknown" && receipt.ModeConfirmed is not null ||
            receipt.State != "Completed" && (receipt.Version != "" || receipt.BindingCurrent)) throw Invalid();
        if (receipt.HistoryVerified) try { ContainerFileValidation.Hash(receipt.HistoryDigest); } catch (ArgumentException) { throw Invalid(); }
        if (receipt.State == "Completed") try { ContainerFileValidation.Hash(receipt.Version); } catch (ArgumentException) { throw Invalid(); }
    }
    public static InvalidOperationException Invalid() => new("CONTAINER_PERMISSION_OUTPUT_INVALID");
}
