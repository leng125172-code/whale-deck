using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.UnitTests.Application;

public sealed partial class ContainerRecycleArchiveWorkflowTests
{
    [Fact]
    public async Task RetirementSourceUsesDurableExecutionIdentityAndExplicitRetention()
    {
        using var f = new Fixture();
        await f.Create();
        await f.Dispatcher.TickAsync(default);
        var row = f.Repository.Row!;
        var source = await ResolveRetirement(f);
        var receipt = ContainerRecycleArchiveWorkflowValidation.ReadReceipt(row)!;

        Assert.Equal(row.Id, source.Binding.ArchiveIntentId);
        Assert.Equal(row.ExecutionJobId, source.Binding.Command.Archive.OperationId);
        Assert.Equal(row.OwnerHash, source.Binding.Command.Archive.Owner);
        Assert.Equal(receipt.PublishedIdentity, source.Binding.PublishedIdentity);
        Assert.Equal(receipt.PublishedChangedAtUnixNs, source.Binding.PublishedChangedAtUnixNs);
        Assert.Equal(30U, source.Binding.Command.RetainDays);
        Assert.Equal(source.ProofHash, (await ResolveRetirement(f)).ProofHash);
        Assert.NotEqual(source.ProofHash, (await ResolveRetirement(f, retainDays: 31)).ProofHash);
        Assert.NotEqual(row.SourceProofHash, source.ProofHash);
    }

    [Theory]
    [InlineData("archive-receipt")]
    [InlineData("archive-reservation")]
    [InlineData("purge-receipt")]
    [InlineData("original-receipt")]
    public async Task RetirementRejectsBrokenAncestorOrArchiveMac(string component)
    {
        using var f = new Fixture();
        await f.Create();
        await f.Dispatcher.TickAsync(default);
        var invalid = new string('0', 64);
        switch (component)
        {
            case "archive-receipt": f.Repository.Row!.ReceiptHash = invalid; break;
            case "archive-reservation": f.Repository.Row!.ReservationHash = invalid; break;
            case "purge-receipt": f.Source.Row.ReceiptHash = invalid; break;
            default: f.Original.Row.ReceiptHash = invalid; break;
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => ResolveRetirement(f));
    }

    [Fact]
    public async Task RetirementRefusesCrossResourceAndRevokedLoginDuringResolution()
    {
        using var f = new Fixture();
        await f.Create();
        await f.Dispatcher.TickAsync(default);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => ResolveRetirement(f, containerId: new string('b', 64)));
        f.Original.OnFind = () => f.Logins.Active = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ResolveRetirement(f));
    }

    [Fact]
    public async Task RetirementRejectsProvenCopyWithoutOwnedCurrentBinding()
    {
        using var f = new Fixture();
        await f.Create();
        f.Gateway.ApplyHandler = op => Gateway.ReceiptFor(op, "Completed") with { OwnedFence = false, BindingCurrent = false };
        await f.Dispatcher.TickAsync(default);
        Assert.Equal("Succeeded", f.Repository.Row!.State);
        await Assert.ThrowsAsync<InvalidOperationException>(() => ResolveRetirement(f));
    }

    [Fact]
    public async Task RetirementPlanMustMatchAuthorizedPolicyAndExactPublishedFile()
    {
        using var f = new Fixture();
        await f.Create();
        await f.Dispatcher.TickAsync(default);
        var source = await ResolveRetirement(f);
        var command = source.Binding.Command;
        var archive = command.Archive.Plan;
        var created = archive.Command.Purge.Plan.HistoryCreatedAtUnixMs;
        var plan = new ContainerRecycleArchiveRetirementPlanDto(1, command, new('E', 64), archive.SourceRecordDigest, archive.PurgeRecordDigest,
            archive.PurgeMarkerDigest, new('C', 64), archive.HistoryMetadataDigest, created, created + 30L * 86400000, false, true, false,
            archive.StorageHash, archive.StoreIdentity, archive.ArchiveIdentity, archive.ArchiveChangedAtUnixNs, archive.ArchiveBytes, archive.ArchiveSha256,
            source.Binding.PublishedIdentity, source.Binding.PublishedChangedAtUnixNs, new('D', 64));

        ContainerRecycleArchiveRetirementSourceResolver.RequireCurrentPlan(source, plan);
        await Assert.ThrowsAsync<ArgumentException>(() => ResolveRetirement(f, retainDays: 0));
        Assert.Throws<InvalidOperationException>(() => ContainerRecycleArchiveRetirementSourceResolver.RequireCurrentPlan(source,
            plan with { Command = command with { RetainDays = 31 }, NotBeforeUnixMs = created + 31L * 86400000 }));
        Assert.Throws<InvalidOperationException>(() => ContainerRecycleArchiveRetirementSourceResolver.RequireCurrentPlan(source,
            plan with { PublishedIdentity = plan.PublishedIdentity with { Inode = plan.PublishedIdentity.Inode + 1 } }));
        Assert.Throws<InvalidOperationException>(() => ContainerRecycleArchiveRetirementSourceResolver.RequireCurrentPlan(source,
            plan with { PublishedChangedAtUnixNs = plan.PublishedChangedAtUnixNs + 1 }));
        Assert.Throws<InvalidOperationException>(() => ContainerRecycleArchiveRetirementSourceResolver.RequireCurrentPlan(source,
            plan with { BindingCurrent = false }));
    }

    private static Task<ResolvedContainerRecycleArchiveRetirementSource> ResolveRetirement(Fixture f, uint retainDays = 30, string? containerId = null) =>
        new ContainerRecycleArchiveRetirementSourceResolver(f.Repository, f.Protector, f.Protector, f.Resolver, f.Service)
            .ResolveAsync(f.Actor, containerId ?? f.Repository.Row!.ContainerId, f.Repository.Row!.Id, retainDays, default);
}
