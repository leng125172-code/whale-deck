using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

public static class ContainerAclRestoreValidation
{
    public static void Command(ContainerAclRestoreCommand value)
    {
        ArgumentNullException.ThrowIfNull(value);
        ContainerAclHistoryValidation.Read(value.Location, value.CaptureId, value.ExpectedHistoryDigest);
        ContainerFileValidation.Hash(value.ExpectedVersion);
    }
    public static ContainerAclHistorySnapshotDto Snapshot(ContainerAclHistorySnapshotDto value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var frozen = System.Text.Json.JsonSerializer.Deserialize<ContainerAclHistorySnapshotDto>(
            ContainerAclWorkflowValidation.Serialize(value), ContainerAclWorkflowValidation.Json) ?? throw ContainerAclWorkflowValidation.Invalid();
        if (frozen.Entry is null) throw ContainerAclWorkflowValidation.Invalid();
        ContainerAclHistoryValidation.Snapshot(frozen, frozen.Location, frozen.Entry.CaptureId, frozen.Entry.HistoryDigest);
        return frozen;
    }
    public static void Source(ContainerAclHistorySnapshotDto source, ContainerAclCommandDto command)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(command);
        if (source.Entry is null) throw ContainerAclWorkflowValidation.Invalid();
        ContainerAclHistoryValidation.Snapshot(source, command.Location, source.Entry.CaptureId, source.Entry.HistoryDigest);
        if (source.Entry.ChangedAclType != command.AclType || command.AclType == "default" && source.Entry.Kind != "Directory" ||
            !command.Entries.SequenceEqual(command.AclType == "access" ? source.AccessAcl : source.DefaultAcl))
            throw ContainerAclWorkflowValidation.Invalid();
    }
    public static ContainerAclHistorySnapshotDto? Read(ContainerAclIntent row)
    {
        if (row.RestoreSourceJson is null) return null;
        var value = System.Text.Json.JsonSerializer.Deserialize<ContainerAclHistorySnapshotDto>(row.RestoreSourceJson, ContainerAclWorkflowValidation.Json)
            ?? throw ContainerAclWorkflowValidation.Invalid();
        if (ContainerAclWorkflowValidation.Serialize(value) != row.RestoreSourceJson) throw ContainerAclWorkflowValidation.Invalid();
        Source(value, ContainerAclWorkflowValidation.ReadCommand(row)); return value;
    }
    public static bool Same(ContainerAclHistorySnapshotDto expected, ContainerAclHistorySnapshotDto observed) =>
        ContainerAclWorkflowValidation.Serialize(expected) == ContainerAclWorkflowValidation.Serialize(observed);
}
