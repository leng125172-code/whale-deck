namespace WhaleDeck.Application.Models;

/// <summary>Only selects an authoritative capture; callers cannot submit historical ACL entries.</summary>
public sealed record ContainerAclRestoreCommand(ContainerFileLocationDto Location, string CaptureId, string ExpectedHistoryDigest, string ExpectedVersion)
{ public override string ToString() => nameof(ContainerAclRestoreCommand); }
public sealed record ContainerAclRestoreIntentDto(ContainerAclIntentDto Intent, ContainerAclHistorySnapshotDto Source)
{ public override string ToString() => nameof(ContainerAclRestoreIntentDto); }
public sealed class ContainerAclRestoreSourceChangedException : InvalidOperationException
{
    public ContainerAclRestoreSourceChangedException() : base("CONTAINER_ACL_RESTORE_SOURCE_CHANGED") { }
}
