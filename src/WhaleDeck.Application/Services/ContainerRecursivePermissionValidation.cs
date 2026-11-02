using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Services;

public static class ContainerRecursivePermissionValidation
{
    public static void Command(ContainerRecursivePermissionCommandDto? command)
    {
        if (command is null) throw new ArgumentException("CONTAINER_RECURSIVE_PERMISSION_COMMAND_REQUIRED");
        ContainerFileValidation.Location(command.Location); ContainerFileValidation.Hash(command.ExpectedVersion);
        foreach (var mode in new[] { command.FileMode, command.DirectoryMode })
            if (mode is not { Length: 4 } || mode[0] != '0' || mode.Skip(1).Any(c => c is < '0' or > '7'))
                throw new ArgumentException("CONTAINER_RECURSIVE_PERMISSION_MODE_INVALID");
    }
    public static void Plan(ContainerRecursivePermissionPlanDto? plan)
    {
        if (plan is null || plan.SchemaVersion != 1 || plan.ParentIdentity is not { Inode: > 0 } || plan.OwnerUid == uint.MaxValue || plan.OwnerGid == uint.MaxValue)
            throw new ArgumentException("CONTAINER_RECURSIVE_PERMISSION_PLAN_INVALID");
        Command(plan.Command);
        if (plan.RootIdentity is not { Inode: > 0, LinkCount: > 0 } source || source.Mode > ushort.MaxValue || (source.Mode & 0xE00) != 0 ||
            (source.Mode & 0xF000) != 0x4000U || source.OwnerUid == uint.MaxValue || source.OwnerGid == uint.MaxValue || source.SizeBytes < 0 ||
            plan.EntryCount is < 1 or > 10000 || plan.FileCount >= plan.EntryCount || plan.DirectoryCount != plan.EntryCount - plan.FileCount || plan.ChangeCount > plan.EntryCount)
            throw new ArgumentException("CONTAINER_RECURSIVE_PERMISSION_PLAN_INVALID");
        ContainerFileValidation.Hash(plan.ManifestDigest); ContainerFileValidation.Hash(plan.TargetProof); ContainerFileValidation.Hash(plan.ParentVersion); ContainerFileValidation.Hash(plan.Fingerprint);
    }
    public static void Operation(ContainerRecursivePermissionOperationDto operation)
    {
        if (operation is null || operation.Id == Guid.Empty || operation.Context is null || operation.Context.JobId != operation.Id ||
            operation.Context.IdempotencyKey == Guid.Empty || string.IsNullOrWhiteSpace(operation.Context.ActorSubject) || operation.Context.ActorSubject.Length > 256 ||
            operation.Context.ActorSubject.Any(char.IsControl) || string.IsNullOrWhiteSpace(operation.Context.TraceId) || operation.Context.TraceId.Length > 128 || operation.Context.TraceId.Any(char.IsControl))
            throw new ArgumentException("CONTAINER_RECURSIVE_PERMISSION_CONTEXT_INVALID");
        Plan(operation.Plan); ContainerFileValidation.Hash(operation.Owner);
    }
    public static void Receipt(ContainerRecursivePermissionOperationDto operation, ContainerRecursivePermissionReceiptDto receipt)
    {
        var location = operation.Plan.Command.Location;
        var n = operation.Plan.EntryCount;
        if (receipt is null || receipt.Id != operation.Id || receipt.ContainerId != location.ContainerId || receipt.InstanceId != location.InstanceId ||
            receipt.TargetProof != operation.Plan.TargetProof || receipt.Fingerprint != operation.Plan.Fingerprint ||
            receipt.HistoryVerified && !receipt.HistoryMayExist || receipt.HistoryAvailable && !receipt.HistoryVerified ||
            receipt.HistoryVerified != (receipt.HistoryDigest is { Length: 64 }) || !receipt.HistoryVerified && receipt.HistoryDigest != "" ||
            receipt.State is not ("Completed" or "Partial" or "Aborted" or "Unknown") ||
            receipt.CompletedCount > n || receipt.AbortedCount > n || receipt.NotStartedCount > n || receipt.UnknownCount > n ||
            (ulong)receipt.CompletedCount + receipt.AbortedCount + receipt.NotStartedCount + receipt.UnknownCount != n ||
            receipt.ChangedCount > receipt.CompletedCount || receipt.ChangedCount > operation.Plan.ChangeCount ||
            receipt.State == "Completed" && (receipt.CompletedCount != n || receipt.ChangedCount != operation.Plan.ChangeCount || !receipt.HistoryVerified) ||
            receipt.State == "Partial" && (receipt.ChangedCount == 0 || receipt.CompletedCount == n || receipt.UnknownCount != 0 || !receipt.HistoryVerified) ||
            receipt.State == "Unknown" && receipt.UnknownCount == 0 ||
            receipt.State == "Aborted" && (receipt.ChangedCount != 0 || receipt.UnknownCount != 0) ||
            receipt.BindingCurrent && (receipt.State != "Completed" || !receipt.HistoryAvailable)) throw Invalid();
        try
        {
            if (receipt.HistoryVerified) ContainerFileValidation.Hash(receipt.HistoryDigest);
            if (receipt.State is "Completed" or "Partial" || receipt.OutcomeDigest != "") ContainerFileValidation.Hash(receipt.OutcomeDigest);
        }
        catch (ArgumentException) { throw Invalid(); }
    }
    public static InvalidOperationException Invalid() => new("CONTAINER_RECURSIVE_PERMISSION_OUTPUT_INVALID");
}
