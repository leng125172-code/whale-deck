using System.Text;
using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Services;

public static class ContainerRecycleValidation
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    public static void Command(ContainerRecycleCommandDto? command)
    {
        if (command is null) throw new ArgumentException("CONTAINER_RECYCLE_COMMAND_REQUIRED");
        ContainerFileValidation.Location(command.Location); ContainerFileValidation.Hash(command.ExpectedVersion);
    }
    public static void Plan(ContainerRecyclePlanDto? plan)
    {
        if (plan is null || plan.SchemaVersion != 1 || plan.ParentIdentity is not { Inode: > 0 } || plan.OwnerUid == uint.MaxValue || plan.OwnerGid == uint.MaxValue)
            throw new ArgumentException("CONTAINER_RECYCLE_PLAN_INVALID");
        Command(plan.Command);
        if (plan.SourceIdentity is not { Inode: > 0, LinkCount: 1 } source || source.Mode > ushort.MaxValue || (source.Mode & 0xF000) != 0x8000 ||
            (source.Mode & 0xE00) != 0 || source.OwnerUid == uint.MaxValue || source.OwnerGid == uint.MaxValue || source.SizeBytes is < 0 or > 1073741824)
            throw new ArgumentException("CONTAINER_RECYCLE_PLAN_INVALID");
        ContainerFileValidation.Hash(plan.TargetProof); ContainerFileValidation.Hash(plan.ParentVersion);
        ContainerFileValidation.Hash(plan.Fingerprint); ContainerFileValidation.Hash(plan.Sha256);
        if (plan.Attributes is null || plan.Attributes.Count > 32) throw new ArgumentException("CONTAINER_RECYCLE_ATTRIBUTES_LIMIT");
        var total = 0; byte[]? previous = null;
        foreach (var item in plan.Attributes)
        {
            if (item is null || item.Name is null || !item.Name.StartsWith("user.", StringComparison.Ordinal) || item.Name.Contains('\0', StringComparison.Ordinal) || item.Value is null)
                throw new ArgumentException("CONTAINER_RECYCLE_PLAN_INVALID");
            byte[] name;
            try { name = Utf8.GetBytes(item.Name); } catch (EncoderFallbackException) { throw new ArgumentException("CONTAINER_RECYCLE_PLAN_INVALID"); }
            if (name.Length > 255 || previous is not null && previous.AsSpan().SequenceCompareTo(name) >= 0) throw new ArgumentException("CONTAINER_RECYCLE_PLAN_INVALID");
            total = checked(total + name.Length + item.Value.Length); previous = name;
            if (total > 4096) throw new ArgumentException("CONTAINER_RECYCLE_ATTRIBUTES_LIMIT");
        }
    }
    public static void Operation(ContainerRecycleOperationDto? operation, bool restoring = false)
    {
        if (operation is null || operation.Id == Guid.Empty || operation.Context is null || operation.Context.JobId == Guid.Empty ||
            (restoring ? operation.Context.JobId == operation.Id : operation.Context.JobId != operation.Id) || operation.Context.IdempotencyKey == Guid.Empty ||
            string.IsNullOrWhiteSpace(operation.Context.ActorSubject) || operation.Context.ActorSubject.Length > 256 || operation.Context.ActorSubject.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(operation.Context.TraceId) || operation.Context.TraceId.Length > 128 || operation.Context.TraceId.Any(char.IsControl))
            throw new ArgumentException("CONTAINER_RECYCLE_CONTEXT_INVALID");
        Plan(operation.Plan); ContainerFileValidation.Hash(operation.Owner);
    }
    public static void Receipt(ContainerRecycleOperationDto operation, ContainerRecycleReceiptDto receipt)
    {
        var location = operation.Plan.Command.Location;
        if (receipt is null || receipt.Id != operation.Id || receipt.ContainerId != location.ContainerId || receipt.InstanceId != location.InstanceId ||
            receipt.Fingerprint != operation.Plan.Fingerprint || receipt.TargetProof != operation.Plan.TargetProof || receipt.State is not ("Trashed" or "Restored" or "Aborted" or "Unknown") ||
            receipt.HistoryVerified && !receipt.HistoryMayExist || receipt.HistoryAvailable && !receipt.HistoryVerified ||
            receipt.HistoryVerified != (receipt.HistoryDigest is { Length: 64 }) || !receipt.HistoryVerified && receipt.HistoryDigest != "" ||
            receipt.BindingCurrent && (!receipt.HistoryAvailable || receipt.State is not ("Trashed" or "Restored")) ||
            receipt.State is "Trashed" or "Restored" && !receipt.HistoryVerified ||
            receipt.State == "Trashed" && (receipt.SourceRetained != true || receipt.SourceRestored != false) ||
            receipt.State == "Restored" && (receipt.SourceRetained != false || receipt.SourceRestored != true) ||
            receipt.State == "Aborted" && (receipt.SourceRetained != false || receipt.SourceRestored != false) ||
            receipt.State == "Unknown" && (receipt.SourceRetained is not null || receipt.SourceRestored is not null)) throw Invalid();
        if (receipt.HistoryVerified) try { ContainerFileValidation.Hash(receipt.HistoryDigest); } catch (ArgumentException) { throw Invalid(); }
    }
    public static InvalidOperationException Invalid() => new("CONTAINER_RECYCLE_OUTPUT_INVALID");
}
