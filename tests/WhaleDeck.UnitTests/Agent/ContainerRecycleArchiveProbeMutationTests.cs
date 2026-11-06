using WhaleDeck.Agent.Services;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

public sealed class ContainerRecycleArchiveProbeMutationTests
{
    [Theory]
    [InlineData("purge-id")][InlineData("purge-owner")][InlineData("source-path")][InlineData("source-bytes")][InlineData("source-attribute")][InlineData("archive")][InlineData("store")][InlineData("history")][InlineData("actor")][InlineData("trace")]
    public void ActualProbePlanBindingNegativesRemainStructurallyValid(string field)
    {
        var original=new ContainerRecycleArchivePlanRequest{Command=ContainerRecycleArchivePolicyTests.Plan().Command.Clone(),Context=new(){ActorSubject="synthetic-admin",TraceId="synthetic-trace"}};
        var changed=ContainerRecycleArchiveProbe.MutateArchivePlan(original,field);ContainerRecycleArchivePolicy.Command(changed.Command);ContainerRecyclePolicy.Context(changed.Context,planning:true);
        Assert.NotEqual(ContainerRecycleArchiveBinding.Hash(original),ContainerRecycleArchiveBinding.Hash(changed));Assert.Equal(original.Command.Purge.Plan.Fingerprint,ContainerRecyclePurgeWire.Fingerprint(original.Command.Purge.Plan));
    }
    [Theory]
    [InlineData("operation-job")][InlineData("owner")][InlineData("purge-id")][InlineData("purge-owner")][InlineData("source-owner")][InlineData("source-attribute")][InlineData("private")][InlineData("deleted-time")][InlineData("parent-version")][InlineData("source-record")][InlineData("purge-record")][InlineData("purge-marker")][InlineData("history-meta")][InlineData("storage")][InlineData("store")][InlineData("archive")][InlineData("archive-time")][InlineData("actor")][InlineData("trace")][InlineData("key")]
    public void ActualProbeOperationNegativesHitSignedBindingAfterFullValidation(string field)
    {
        var original=ContainerRecycleArchivePolicyTests.Request();var changed=ContainerRecycleArchiveProbe.MutateArchiveOperation(original,field);ContainerRecycleArchivePolicy.Request(changed);
        Assert.NotEqual(ContainerRecycleArchiveBinding.Hash(original),ContainerRecycleArchiveBinding.Hash(changed));Assert.Equal(original.Plan.Fingerprint,ContainerRecycleArchiveWire.Fingerprint(original.Plan));
    }
}
