using System.Security.Cryptography;
using System.Text.Json;
using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Services;

public static class ContainerCopyValidation
{
    public const string SourceRoot = "container-copy-source", TargetRoot = "container-copy-target";
    public static void Command(ContainerCopyCommandDto? command)
    {
        if (command is null) throw new ArgumentException("CONTAINER_COPY_COMMAND_REQUIRED");
        ContainerFileValidation.Location(command.Source);
        ContainerFileValidation.Location(command.Source with { Path = command.TargetPath }); ContainerFileValidation.Hash(command.ExpectedVersion);
        var source = command.Source.Path; var target = command.TargetPath;
        if (source == target || source.StartsWith(target + "/", StringComparison.Ordinal) || target.StartsWith(source + "/", StringComparison.Ordinal))
            throw new ArgumentException("CONTAINER_COPY_OVERLAP");
    }
    public static string HostVersion(ContainerCopyPlanDto plan)
    {
        var value = plan.SourceIdentity;
        object[] identity = [value.Device, value.Inode, value.Mode, value.OwnerUid, value.OwnerGid, value.LinkCount,
            value.SizeBytes, value.ModifiedAtUnixNs, plan.SourceChangedAtUnixNs];
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(identity)));
    }
    public static void Plan(ContainerCopyPlanDto? plan)
    {
        if (plan is null || plan.SchemaVersion != 1 || plan.Kind is not ("File" or "Directory") || plan.SourceIdentity is not { Inode: > 0, LinkCount: > 0 } source ||
            source.Mode > ushort.MaxValue || (source.Mode & 0xE00) != 0 || source.OwnerUid == uint.MaxValue || source.OwnerGid == uint.MaxValue || source.SizeBytes < 0 ||
            plan.OwnerUid == uint.MaxValue || plan.OwnerGid == uint.MaxValue || plan.SourceParent is not { Inode: > 0 } || plan.TargetParent is not { Inode: > 0 })
            throw new ArgumentException("CONTAINER_COPY_PLAN_INVALID");
        Command(plan.Command); ContainerFileValidation.Hash(plan.TargetProof); ContainerFileValidation.Hash(plan.TargetParentVersion); ContainerFileValidation.Hash(plan.Fingerprint);
        var expected = HostVersion(plan);
        if (plan.Kind == "File")
        {
            if ((source.Mode & 0xF000) != 0x8000 || source.LinkCount != 1 || source.SizeBytes > FileManagementService.MaximumBytes || plan.FilePlan is null || plan.TreePlan is not null)
                throw new ArgumentException("CONTAINER_COPY_PLAN_INVALID");
            var inner = plan.FilePlan; FileMutationService.Validate(inner);
            if (inner.Command.Kind != "copy" || inner.Command.Source != new FileLocationDto(SourceRoot, plan.Command.Source.Path) ||
                inner.Command.Target != new FileLocationDto(TargetRoot, plan.Command.TargetPath) || inner.Command.ExpectedVersion != expected ||
                inner.SourceIdentity != source || inner.SourceParent != plan.SourceParent || inner.TargetParent != plan.TargetParent) throw new ArgumentException("CONTAINER_COPY_PLAN_INVALID");
        }
        else
        {
            if ((source.Mode & 0xF000) != 0x4000 || plan.TreePlan is null || plan.FilePlan is not null) throw new ArgumentException("CONTAINER_COPY_PLAN_INVALID");
            var inner = plan.TreePlan; FileTreeCopyValidation.Plan(inner);
            if (inner.Command.Source != new FileLocationDto(SourceRoot, plan.Command.Source.Path) || inner.Command.Target != new FileLocationDto(TargetRoot, plan.Command.TargetPath) ||
                inner.Command.ExpectedVersion != expected || inner.TargetParent != plan.TargetParent) throw new ArgumentException("CONTAINER_COPY_PLAN_INVALID");
        }
    }
    public static void Operation(ContainerCopyOperationDto operation)
    {
        if (operation is null || operation.Id == Guid.Empty || operation.Context is null || operation.Context.JobId != operation.Id || operation.Context.IdempotencyKey == Guid.Empty ||
            string.IsNullOrWhiteSpace(operation.Context.ActorSubject) || operation.Context.ActorSubject.Length > 256 || operation.Context.ActorSubject.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(operation.Context.TraceId) || operation.Context.TraceId.Length > 128 || operation.Context.TraceId.Any(char.IsControl))
            throw new ArgumentException("CONTAINER_COPY_CONTEXT_INVALID");
        Plan(operation.Plan); ContainerFileValidation.Hash(operation.Owner);
    }
    public static void Receipt(ContainerCopyOperationDto operation, ContainerCopyReceiptDto receipt)
    {
        var plan = operation.Plan;
        if (receipt is null || receipt.Id != operation.Id || receipt.ContainerId != plan.Command.Source.ContainerId || receipt.InstanceId != plan.Command.Source.InstanceId || receipt.Kind != plan.Kind ||
            receipt.Fingerprint != plan.Fingerprint || receipt.TargetProof != plan.TargetProof || receipt.State is not ("Completed" or "Aborted" or "Unknown") ||
            receipt.State != "Completed" && (receipt.Version != "" || receipt.BindingCurrent) || receipt.State == "Completed" && receipt.PrivateMayExist || receipt.State == "Unknown" && !receipt.PrivateMayExist)
            throw Invalid();
        if (receipt.State == "Completed") try { ContainerFileValidation.Hash(receipt.Version); } catch (ArgumentException) { throw Invalid(); }
    }
    public static InvalidOperationException Invalid() => new("CONTAINER_COPY_OUTPUT_INVALID");
}
