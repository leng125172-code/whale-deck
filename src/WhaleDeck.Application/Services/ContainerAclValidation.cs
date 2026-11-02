using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Services;

public static class ContainerAclValidation
{
    public static void Command(ContainerAclCommandDto? command)
    {
        if (command is null) throw new ArgumentException("CONTAINER_ACL_COMMAND_REQUIRED");
        ContainerFileValidation.Location(command.Location); ContainerFileValidation.Hash(command.ExpectedVersion);
        if (command.AclType is not ("access" or "default")) throw new ArgumentException("CONTAINER_ACL_TYPE_INVALID");
        Entries(command.Entries, command.AclType == "default");
    }
    public static void Plan(ContainerAclPlanDto? plan)
    {
        if (plan is null || plan.SchemaVersion != 1 || plan.ParentIdentity is not { Inode: > 0 } || plan.OwnerUid == uint.MaxValue || plan.OwnerGid == uint.MaxValue)
            throw new ArgumentException("CONTAINER_ACL_PLAN_INVALID");
        Command(plan.Command);
        if (plan.Kind is not ("File" or "Directory") || plan.SourceIdentity is not { Inode: > 0, LinkCount: > 0 } source ||
            source.Mode > ushort.MaxValue || (source.Mode & 0xE00) != 0 || source.OwnerUid == uint.MaxValue || source.OwnerGid == uint.MaxValue || source.SizeBytes < 0 ||
            (source.Mode & 0xF000) != (plan.Kind == "File" ? 0x8000U : 0x4000U) || plan.Kind == "File" && source.LinkCount != 1 ||
            plan.Kind == "File" && (plan.Command.AclType == "default" || plan.DefaultAcl is not { Count: 0 })) throw new ArgumentException("CONTAINER_ACL_PLAN_INVALID");
        Entries(plan.AccessAcl); Entries(plan.DefaultAcl, true);
        if (Mode(plan.AccessAcl) != (source.Mode & 0x1FF) || plan.TargetMode != (plan.Command.AclType == "access" ? Mode(plan.Command.Entries) : source.Mode & 0x1FF) ||
            plan.NoChange != plan.Command.Entries.SequenceEqual(plan.Command.AclType == "access" ? plan.AccessAcl : plan.DefaultAcl)) throw new ArgumentException("CONTAINER_ACL_PLAN_INVALID");
        ContainerFileValidation.Hash(plan.AttributesDigest);
        ContainerFileValidation.Hash(plan.TargetProof); ContainerFileValidation.Hash(plan.ParentVersion); ContainerFileValidation.Hash(plan.Fingerprint);
    }
    public static void Operation(ContainerAclOperationDto operation)
    {
        if (operation is null || operation.Id == Guid.Empty || operation.Context is null || operation.Context.JobId != operation.Id ||
            operation.Context.IdempotencyKey == Guid.Empty || string.IsNullOrWhiteSpace(operation.Context.ActorSubject) || operation.Context.ActorSubject.Length > 256 ||
            operation.Context.ActorSubject.Any(char.IsControl) || string.IsNullOrWhiteSpace(operation.Context.TraceId) || operation.Context.TraceId.Length > 128 || operation.Context.TraceId.Any(char.IsControl))
            throw new ArgumentException("CONTAINER_ACL_CONTEXT_INVALID");
        Plan(operation.Plan); ContainerFileValidation.Hash(operation.Owner);
    }
    public static void Receipt(ContainerAclOperationDto operation, ContainerAclReceiptDto receipt)
    {
        var location = operation.Plan.Command.Location;
        if (receipt is null || receipt.Id != operation.Id || receipt.ContainerId != location.ContainerId || receipt.InstanceId != location.InstanceId ||
            receipt.TargetProof != operation.Plan.TargetProof || receipt.Fingerprint != operation.Plan.Fingerprint || receipt.Kind != operation.Plan.Kind || receipt.NoChange != operation.Plan.NoChange || receipt.AclType != operation.Plan.Command.AclType || receipt.TargetMode != operation.Plan.TargetMode ||
            receipt.HistoryVerified && !receipt.HistoryMayExist || receipt.HistoryVerified != (receipt.HistoryDigest is { Length: 64 }) ||
            !receipt.HistoryVerified && receipt.HistoryDigest != "" ||
            receipt.State is not ("Completed" or "Aborted" or "Unknown") ||
            receipt.State == "Completed" && (receipt.AclConfirmed != true || !receipt.HistoryVerified) ||
            receipt.State == "Aborted" && receipt.AclConfirmed != false || receipt.State == "Unknown" && receipt.AclConfirmed is not null ||
            receipt.State != "Completed" && (receipt.Version != "" || receipt.BindingCurrent)) throw Invalid();
        if (receipt.HistoryVerified) try { ContainerFileValidation.Hash(receipt.HistoryDigest); } catch (ArgumentException) { throw Invalid(); }
        if (receipt.State == "Completed") try { ContainerFileValidation.Hash(receipt.Version); } catch (ArgumentException) { throw Invalid(); }
    }
    public static void Entries(IReadOnlyList<ContainerAclEntryDto>? entries, bool empty = false)
    {
        if (entries is null || entries.Count > 64 || entries.Count == 0 && !empty) throw new ArgumentException("CONTAINER_ACL_ENTRIES_INVALID");
        if (entries.Count == 0) return;
        var seen = new HashSet<(int, uint?)>(); (int Tag, long Id) previous = (0, -1);
        foreach (var entry in entries)
        {
            if (entry is null) throw new ArgumentException("CONTAINER_ACL_ENTRIES_INVALID");
            var tag = entry.Tag switch { "user_obj" => 1, "user" => 2, "group_obj" => 4, "group" => 8, "mask" => 16, "other" => 32, _ => 0 };
            if (tag == 0 || entry.Permissions > 7 || (tag is 2 or 8 ? entry.Id is null or uint.MaxValue : entry.Id is not null) ||
                !seen.Add((tag, entry.Id))) throw new ArgumentException("CONTAINER_ACL_ENTRIES_INVALID");
            var key = (tag, entry.Id is { } id ? (long)id : -1L);
            if (key.CompareTo(previous) <= 0) throw new ArgumentException("CONTAINER_ACL_ENTRIES_INVALID");
            previous = key;
        }
        if (!seen.Contains((1, null)) || !seen.Contains((4, null)) || !seen.Contains((32, null)) ||
            entries.Any(e => e.Tag is "user" or "group") && !seen.Contains((16, null))) throw new ArgumentException("CONTAINER_ACL_ENTRIES_INVALID");
    }
    public static uint Mode(IReadOnlyList<ContainerAclEntryDto> entries)
    {
        Entries(entries);
        return entries.Single(e => e.Tag == "user_obj").Permissions << 6 |
            (entries.FirstOrDefault(e => e.Tag == "mask") ?? entries.Single(e => e.Tag == "group_obj")).Permissions << 3 |
            entries.Single(e => e.Tag == "other").Permissions;
    }
    public static bool SameCommand(ContainerAclCommandDto a, ContainerAclCommandDto b) =>
        a.Location == b.Location && a.AclType == b.AclType && a.ExpectedVersion == b.ExpectedVersion && a.Entries.SequenceEqual(b.Entries);
    public static InvalidOperationException Invalid() => new("CONTAINER_ACL_OUTPUT_INVALID");
}
