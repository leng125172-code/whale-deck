using WhaleDeck.Application.Models;
namespace WhaleDeck.UnitTests.Application;
internal static class ContainerAclWorkflowFixture
{
    internal static ContainerAclEntryDto[] Base(uint owner = 6, uint group = 4, uint other = 0) =>
        [new("user_obj", null, owner), new("group_obj", null, group), new("other", null, other)];
    internal static ContainerAclEntryDto[] Named(uint id = 2200, uint permissions = 4, uint mask = 6) =>
        [new("user_obj", null, 6), new("user", id, permissions), new("group_obj", null, 4), new("mask", null, mask), new("other", null, 0)];
    internal static ContainerAclPlanDto Plan(bool directory = false)
    {
        var source = ContainerOwnershipWorkflowFixture.Plan(directory);
        return new(1, new(source.Command.Location, "access", Named(), source.Command.ExpectedVersion), source.TargetProof,
            source.ParentIdentity, source.ParentVersion, source.OwnerUid, source.OwnerGid, source.Fingerprint, source.Kind,
            source.SourceIdentity, source.SourceChangedAtUnixNs, source.AttributesDigest, false,
            directory ? Base(7, 5) : Base(), [], 432);
    }
}
