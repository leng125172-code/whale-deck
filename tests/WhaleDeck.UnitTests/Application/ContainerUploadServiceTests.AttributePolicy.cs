using System.Text.Json;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.UnitTests.Application;

public sealed partial class ContainerUploadServiceTests
{
    [Fact]
    public async Task AttributePolicyIsSealedInPlanIntentResponseAndSignedStageSource()
    {
        using var fixture = new Fixture(); var source = Source(fixture); var service = Restores(fixture, new() { Entry = source });
        fixture.Gateway.RecordStageReceipt = true;
        var plan = await service.PlanAsync(fixture.Actor, RestoreCommand(fixture, source) with { RestoreAttributes = true }, default);
        Assert.True(plan.RestoreAttributes); Assert.Contains("同时恢复历史", plan.Warning, StringComparison.Ordinal);
        var claims = fixture.Protector.VerifyContainerRestore(plan.PlanHash); Assert.True(claims.RestoreAttributes);
        var created = await service.SubmitAsync(fixture.Actor, source.ContainerId, new(plan.PlanHash, true, Guid.NewGuid().ToString("D")), default);
        Assert.True(created.RestoreAttributes); Assert.True(fixture.Repository.Row!.RestoreAttributes);
        Assert.True(await RestoreDispatcher(fixture).TickAsync(default));
        Assert.Equal("Succeeded", Assert.Single(fixture.Repository.Results).State);
        Assert.Equal(new ContainerFileRestoreSourceDto(source.CaptureId, source.CaptureHash, source.Path), fixture.Gateway.StageSource);
        typeof(ContainerFileUploadOperation).GetProperty(nameof(ContainerFileUploadOperation.RestoreAttributes))!.SetValue(fixture.Repository.Row, false);
        Assert.False(ContainerFileUploadService.HasValidBinding(fixture.Repository.Row, fixture.Protector));
    }

    [Fact]
    public async Task AttributeCapabilityIsRequiredAtPlanAndRegistrationWhileContentOnlyRemainsCompatible()
    {
        using var fixture = new Fixture(); var source = Source(fixture); var service = Restores(fixture, new() { Entry = source });
        var command = RestoreCommand(fixture, source) with { RestoreAttributes = true };
        fixture.Gateway.RestorationUnavailable = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PlanAsync(fixture.Actor, command, default));
        Assert.Equal(0, fixture.Gateway.Inspections);
        var legacy = await service.PlanAsync(fixture.Actor, RestoreCommand(fixture, source), default);
        Assert.False(legacy.RestoreAttributes);
        fixture.Gateway.RestorationUnavailable = false; var plan = await service.PlanAsync(fixture.Actor, command, default);
        fixture.Gateway.RestorationUnavailable = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SubmitAsync(fixture.Actor, source.ContainerId,
            new(plan.PlanHash, true, Guid.NewGuid().ToString("D")), default));
        Assert.Equal(0, fixture.Repository.Enqueued); Assert.Equal(0, fixture.History.Reads);
    }

    [Fact]
    public async Task WorkerDowngradeDoesNotReadSourceOrSilentlyStageContentOnly()
    {
        using var fixture = new Fixture(); var source = Source(fixture); var service = Restores(fixture, new() { Entry = source });
        var plan = await service.PlanAsync(fixture.Actor, RestoreCommand(fixture, source) with { RestoreAttributes = true }, default);
        await service.SubmitAsync(fixture.Actor, source.ContainerId, new(plan.PlanHash, true, Guid.NewGuid().ToString("D")), default);
        fixture.Gateway.RestorationUnavailable = true;
        Assert.True(await RestoreDispatcher(fixture).TickAsync(default));
        Assert.True(fixture.Repository.Row!.RestoreAttributes); Assert.Equal(0, fixture.History.Reads);
        Assert.Equal(0, fixture.Gateway.Stages); Assert.Equal(0, fixture.Gateway.Publications);
    }

    [Fact]
    public async Task ExactAttributeRetryReusesOriginalPolicyAndLifetimeEvenAfterCapabilityLoss()
    {
        using var fixture = new Fixture(); var source = Source(fixture); var service = Restores(fixture, new() { Entry = source });
        var plan = await service.PlanAsync(fixture.Actor, RestoreCommand(fixture, source) with { RestoreAttributes = true }, default);
        var submit = new SubmitFileUploadCommand(plan.PlanHash, true, Guid.NewGuid().ToString("D"));
        var first = await service.SubmitAsync(fixture.Actor, source.ContainerId, submit, default);
        fixture.Clock.Now = fixture.Clock.Now.AddMinutes(6); fixture.Gateway.RestorationUnavailable = true;
        Assert.Equal(first, await service.SubmitAsync(fixture.Actor, source.ContainerId, submit, default)); Assert.Equal(1, fixture.Repository.Enqueued);
        Assert.True(first.RestoreAttributes);
    }

    [Fact]
    public async Task DefaultFalsePreservesLegacyPlanBindingAndFrozenRowSerializationExactly()
    {
        using var fixture = new Fixture(); var source = Source(fixture); var service = Restores(fixture, new() { Entry = source });
        var plan = await service.PlanAsync(fixture.Actor, RestoreCommand(fixture, source), default);
        var claims = fixture.Protector.VerifyContainerRestore(plan.PlanHash);
        var legacyClaims = JsonSerializer.Serialize(new { claims.Actor, claims.LoginBinding, claims.Target, claims.Source, claims.CreatedAtUtc, claims.ExpiresAtUtc }, ContainerFileRestoreValidation.Json);
        Assert.Equal(legacyClaims, JsonSerializer.Serialize(claims, ContainerFileRestoreValidation.Json));
        await service.SubmitAsync(fixture.Actor, source.ContainerId, new(plan.PlanHash, true, Guid.NewGuid().ToString("D")), default);
        var row = fixture.Repository.Row!; var binding = ContainerFileUploadIntentValidation.ReadBinding(row);
        var legacy = JsonSerializer.Serialize(new { binding.Id, binding.IdempotencyKey, binding.Actor, binding.LoginBinding, binding.TraceId, binding.Target,
            binding.SizeBytes, binding.Sha256, binding.PlanHash, binding.CreatedAtUtc, binding.ExpiresAtUtc, binding.HistorySourceJson });
        Assert.Equal(legacy, JsonSerializer.Serialize(binding));
        Assert.DoesNotContain("RestoreAttributes", JsonSerializer.Serialize(row), StringComparison.Ordinal);
        Assert.DoesNotContain("RestoreAttributes", JsonSerializer.Serialize(ContainerFileUploadService.Map(row)), StringComparison.Ordinal);
        Assert.Null(ContainerFileUploadIntentValidation.Stage(row).RestoreSource);
    }

    [Fact]
    public async Task ChangedAttributePolicyCannotReuseAnotherPlanKeyOrBeAttachedToOrdinaryUpload()
    {
        using var fixture = new Fixture(); var source = Source(fixture); var service = Restores(fixture, new() { Entry = source });
        var old = await service.PlanAsync(fixture.Actor, RestoreCommand(fixture, source), default);
        var attributes = await service.PlanAsync(fixture.Actor, RestoreCommand(fixture, source) with { RestoreAttributes = true }, default);
        Assert.NotEqual(old.PlanHash, attributes.PlanHash); var key = Guid.NewGuid().ToString("D");
        await service.SubmitAsync(fixture.Actor, source.ContainerId, new(old.PlanHash, true, key), default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SubmitAsync(fixture.Actor, source.ContainerId, new(attributes.PlanHash, true, key), default));
        using var upload = new Fixture(); await upload.Create(); var binding = ContainerFileUploadIntentValidation.ReadBinding(upload.Repository.Row!);
        Assert.Throws<ArgumentException>(() => ContainerFileUploadIntentValidation.Binding(binding with { RestoreAttributes = true }));
    }
}
