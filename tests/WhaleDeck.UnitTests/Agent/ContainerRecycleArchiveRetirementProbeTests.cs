using System.Text;
using Google.Protobuf;
using WhaleDeck.Agent.Services;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

public sealed class ContainerRecycleArchiveRetirementProbeTests
{
    private static ContainerRecycleArchiveRetirementPlan Plan()
    {
        var p = ContainerRecycleArchiveRetirementContractTests.Request().Plan;
        p.PublishedIdentity.Device = p.Command.Archive.Plan.ParentIdentity.Device;
        p.Fingerprint = ContainerRecycleArchiveRetirementWire.Fingerprint(p); return p;
    }
    [Theory]
    [InlineData("apply", "Reserved", false, true)]
    [InlineData("apply", "Reserved", true, false)]
    [InlineData("apply", "Completed", false, false)]
    [InlineData("apply", "Unknown", false, false)]
    [InlineData("apply", "Aborted", false, false)]
    [InlineData("apply", "unreserved", false, false)]
    [InlineData("reserve", "unreserved", false, true)]
    [InlineData("reserve", "Reserved", false, false)]
    [InlineData("reserve", "Aborted", false, false)]
    [InlineData("inspect", "Unknown", true, true)]
    [InlineData("discard", "Unknown", true, true)]
    [InlineData("restore", "Reserved", false, false)]
    public void ExplicitStepsNeverRetryOrLateActivate(string action, string state, bool applied, bool allowed)
    {
        if (allowed) ContainerRecycleArchiveRetirementProbe.RequireAction(action, state, applied);
        else Assert.Throws<InvalidOperationException>(() => ContainerRecycleArchiveRetirementProbe.RequireAction(action, state, applied));
    }
    [Theory][InlineData("archive")][InlineData("purge")][InlineData("source")][InlineData("zero")]
    public void AncestorIdsCannotBecomeRetirementExecution(string ancestor)
    {
        var p = Plan(); var a = p.Command.Archive; var purge = a.Plan.Command.Purge;
        var id = ancestor switch { "archive" => a.OperationId, "purge" => purge.OperationId, "source" => purge.Plan.Command.Source.OperationId, _ => new string('0', 32) };
        Assert.Throws<InvalidOperationException>(() => ContainerRecycleArchiveRetirementProbe.ValidateNewId(id, p));
        ContainerRecycleArchiveRetirementProbe.ValidateNewId(Guid.NewGuid().ToString("N"), p);
    }
    [Fact]
    public void ReturnedYoungSchedulingPlanIsNotPromotedToExpiry()
    {
        var p = Plan(); var q = new ContainerRecycleArchiveRetirementPlanRequest { Command = p.Command.Clone() };
        ContainerRecycleArchiveRetirementProbe.ValidatePlan(q, p); Assert.False(p.RetirementEligible);
    }
    [Theory][InlineData("due")][InlineData("storage")][InlineData("device")][InlineData("inode")][InlineData("history")][InlineData("diagnostics")][InlineData("command")]
    public void RehashingWrongFactsStillRejectsReturnedPlan(string field)
    {
        var p = Plan(); var q = new ContainerRecycleArchiveRetirementPlanRequest { Command = p.Command.Clone() };
        switch (field) {
            case "due": p.NotBeforeUnixMs++; break;
            case "storage": p.StorageHash = new('E', 64); break;
            case "device": p.PublishedIdentity.Device++; break;
            case "inode": p.PublishedIdentity.Inode = p.ArchiveIdentity.Inode; p.PublishedIdentity.Device = p.ArchiveIdentity.Device; break;
            case "history": p.HistoryMetadataDigest = new('E', 64); break;
            case "diagnostics": p.RetirementEligible = true; break;
            default: p.Command.RetainDays++; break; }
        p.Fingerprint = ContainerRecycleArchiveRetirementWire.Fingerprint(p);
        Assert.Throws<InvalidOperationException>(() => ContainerRecycleArchiveRetirementProbe.ValidatePlan(q, p));
    }
    [Fact]
    public void UnknownFieldsDoNotHideBehindFingerprint()
    {
        var p = Plan(); var q = new ContainerRecycleArchiveRetirementPlanRequest { Command = p.Command.Clone() };
        var changed = ContainerRecycleArchiveRetirementPlan.Parser.ParseFrom(p.ToByteArray().Concat(new byte[] { 248, 7, 1 }).ToArray());
        Assert.Throws<ArgumentException>(() => ContainerRecycleArchiveRetirementProbe.ValidatePlan(q, changed));
    }
    [Theory][InlineData("path")][InlineData("container")][InlineData("instance")][InlineData("size")][InlineData("uid")]
    public void OwnedScopeCannotRedirectToAnotherResource(string field)
    {
        var archive = ContainerRecycleArchivePolicyTests.Request(); var source = archive.Plan.Command.Purge.Plan.Command.Source.Plan;
        source.Command.Location.Path = "work/recycle-rpc.bin";
        source.OwnerUid = source.OwnerGid = source.SourceIdentity.OwnerUid = source.SourceIdentity.OwnerGid = 1000;
        var purge = archive.Plan.Command.Purge.Plan;
        purge.OwnerUid = purge.OwnerGid = purge.RetainedIdentity.OwnerUid = purge.RetainedIdentity.OwnerGid = 1000;
        archive.Plan.OwnerUid = archive.Plan.OwnerGid = archive.Plan.DeletedIdentity.OwnerUid = archive.Plan.DeletedIdentity.OwnerGid = 1000;
        purge.Fingerprint = ContainerRecyclePurgeWire.Fingerprint(purge); archive.Plan.Fingerprint = ContainerRecycleArchiveWire.Fingerprint(archive.Plan);
        var id = source.Command.Location.ContainerId; var instance = source.Command.Location.InstanceId;
        ContainerRecycleArchiveRetirementProbe.ValidateScope(archive, id, instance);
        // Rehash is unnecessary for scope refusals, which precede digest checks.
        switch (field) {
            case "path": source.Command.Location.Path = "work/other.bin"; break;
            case "container": source.Command.Location.ContainerId = new('f', 64); break;
            case "instance": source.Command.Location.InstanceId += "-other"; break;
            case "size": source.SourceIdentity.SizeBytes = 16777217; break;
            default: source.SourceIdentity.OwnerUid = 0; break; }
        Assert.Throws<InvalidOperationException>(() => ContainerRecycleArchiveRetirementProbe.ValidateScope(archive, id, instance));
    }
    [Fact]
    public async Task InputLimitsApplyWhileReadingAndDoNotConsumeTheNextCommand()
    {
        using var source = new MemoryStream(Encoding.UTF8.GetBytes("{\"x\":\"中\"}\n{}\n"));
        using var first = await ContainerRecycleArchiveRetirementProbe.ReadAsync(source, 11, default);
        Assert.Equal("中", first.RootElement.GetProperty("x").GetString());
        using var second = await ContainerRecycleArchiveRetirementProbe.ReadAsync(source, 2, default); Assert.Empty(second.RootElement.EnumerateObject());
    }
    [Theory][InlineData("{}", 2)][InlineData("{}\r\n", 3)][InlineData("{\"x\":1}\n", 6)]
    public async Task TruncatedOrOversizedFramesStopBeforeParsing(string raw, int maximum)
    {
        using var source = new MemoryStream(Encoding.UTF8.GetBytes(raw));
        await Assert.ThrowsAsync<InvalidOperationException>(() => ContainerRecycleArchiveRetirementProbe.ReadAsync(source, maximum, default));
    }
    [Fact]
    public async Task PreCanceledInputDoesNotReadAnything()
    {
        using var source = new MemoryStream(Encoding.UTF8.GetBytes("{}\n")); using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ContainerRecycleArchiveRetirementProbe.ReadAsync(source, 2, canceled.Token)); Assert.Equal(0, source.Position);
    }
}
