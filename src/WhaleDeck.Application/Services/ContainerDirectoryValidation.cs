using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Services;

public static class ContainerDirectoryValidation
{
    public static void Command(ContainerDirectoryCommandDto? command)
    {
        if (command is null) throw new ArgumentException("CONTAINER_DIRECTORY_COMMAND_REQUIRED");
        ContainerFileValidation.Location(command.Location);
        if (command.Mode is not { Length: 4 } || command.Mode[0] != '0' || command.Mode.Skip(1).Any(c => c is < '0' or > '7'))
            throw new ArgumentException("CONTAINER_DIRECTORY_MODE_INVALID");
    }
    public static void Plan(ContainerDirectoryPlanDto? plan)
    {
        if (plan is null || plan.SchemaVersion != 1 || plan.ParentIdentity is not { Inode: > 0 } || plan.OwnerUid == uint.MaxValue || plan.OwnerGid == uint.MaxValue)
            throw new ArgumentException("CONTAINER_DIRECTORY_PLAN_INVALID");
        Command(plan.Command);
        ContainerFileValidation.Hash(plan.TargetProof); ContainerFileValidation.Hash(plan.ParentVersion); ContainerFileValidation.Hash(plan.Fingerprint);
    }
    public static void Operation(ContainerDirectoryOperationDto operation)
    {
        if (operation is null || operation.Id == Guid.Empty || operation.Context is null || operation.Context.JobId != operation.Id ||
            operation.Context.IdempotencyKey == Guid.Empty || string.IsNullOrWhiteSpace(operation.Context.ActorSubject) || operation.Context.ActorSubject.Length > 256 ||
            operation.Context.ActorSubject.Any(char.IsControl) || string.IsNullOrWhiteSpace(operation.Context.TraceId) || operation.Context.TraceId.Length > 128 || operation.Context.TraceId.Any(char.IsControl))
            throw new ArgumentException("CONTAINER_DIRECTORY_CONTEXT_INVALID");
        Plan(operation.Plan); ContainerFileValidation.Hash(operation.Owner);
    }
    public static void Receipt(ContainerDirectoryOperationDto operation, ContainerDirectoryReceiptDto receipt)
    {
        var location = operation.Plan.Command.Location;
        if (receipt is null || receipt.Id != operation.Id || receipt.ContainerId != location.ContainerId || receipt.InstanceId != location.InstanceId ||
            receipt.TargetProof != operation.Plan.TargetProof || receipt.Fingerprint != operation.Plan.Fingerprint || receipt.State is not ("Completed" or "Aborted" or "Unknown") ||
            receipt.State != "Completed" && (receipt.Version != "" || receipt.BindingCurrent)) throw Invalid();
        if (receipt.State == "Completed") try { ContainerFileValidation.Hash(receipt.Version); } catch (ArgumentException) { throw Invalid(); }
    }
    public static InvalidOperationException Invalid() => new("CONTAINER_DIRECTORY_OUTPUT_INVALID");
}
