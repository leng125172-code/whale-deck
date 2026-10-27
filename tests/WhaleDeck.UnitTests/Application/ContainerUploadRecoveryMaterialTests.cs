using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Agent;
using WhaleDeck.Contracts;
using Rpc = WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Application;

/// <summary>Signed material with synthetic Root metadata, not physical container or authority evidence.</summary>
public sealed partial class ContainerUploadRecoveryMaterialTests : IDisposable
{
    private readonly string _key = Path.GetTempFileName();
    private readonly FilePlanProtector _protector;
    private FileRecoveryMaterial Material => new(_protector,_protector,_protector,_protector,_protector,_protector,_protector,_protector,_protector,_protector,_protector,_protector);
    public ContainerUploadRecoveryMaterialTests()
    {
        File.WriteAllBytes(_key,RandomNumberGenerator.GetBytes(32));
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(_key,UnixFileMode.UserRead|UnixFileMode.UserWrite);
        _protector = new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Agent:CapabilityKeyFile"] = _key }).Build());
    }
    public void Dispose() => File.Delete(_key);
    private ContainerFileUploadOperation Original()
    {
        var now = ContainerFileUploadIntentValidation.CanonicalTime(DateTimeOffset.UtcNow); var id = Guid.NewGuid(); var key = Guid.NewGuid();
        var target = new ContainerFileUploadTargetDto(new(new('a',64),"777:2026-10-22T01:02:03.123456789Z","private/file.bin"),new('B',64),new('C',64),"ABSENT",0);
        var binding = new ContainerFileUploadBinding(id,key,"old-container-admin",new('A',64),"original-trace",target,17,new('D',64),new('E',64),now,now.AddMinutes(15));
        return new() { Id = id,IdempotencyKey = key,ActorSubject = binding.Actor,LoginBinding = binding.LoginBinding,TraceId = binding.TraceId,
            OwnerHash = ContainerFileUploadIntentValidation.Owner(binding.Actor,binding.LoginBinding),ContainerId = target.Location.ContainerId,InstanceId = target.Location.InstanceId,
            RelativePath = target.Location.Path,TargetProof = target.TargetProof,ExpectedParentVersion = target.ExpectedParentVersion,ExpectedVersion = target.ExpectedVersion,
            PlanHash = binding.PlanHash,RequestHash = _protector.FingerprintContainerUpload(binding),Sha256 = binding.Sha256,SizeBytes = binding.SizeBytes,
            CreatedAtUtc = now,ExpiresAtUtc = binding.ExpiresAtUtc,State = "Unknown",RootStartedAtUtc = now,PublishBarrier = true,Version = 7 };
    }
    private static ContainerUploadRecoveryObservationDto Facts(ContainerFileUploadOperation original, bool sealedValue = false, Guid? seal = null, string rootState = "Published")
    {
        var wire = new Rpc.ContainerUploadRecoveryObservation { ContainerId = original.ContainerId,InstanceId = original.InstanceId,OperationId = original.Id.ToString("N"),
            TargetProof = original.TargetProof,RootState = rootState,OriginalRecordHash = new('F',64),Sealed = sealedValue,SealId = seal?.ToString("N") ?? "",
            MayHavePublished = true,RetainedObjects = true };
        wire.ObservationHash = ContainerUploadRecoveryBinding.ObservationHash(new() { Owner = original.OwnerHash },wire);
        return new(wire.ContainerId,wire.InstanceId,original.Id,wire.TargetProof,wire.RootState,wire.OriginalRecordHash,wire.Sealed,seal,wire.MayHavePublished,wire.RetainedObjects,wire.ObservationHash);
    }
    [Fact]
    public void DistinctContainerKindFreezesOriginalContextAndHistoricalPublishedCannotBecomeCompleted()
    {
        var original = Original(); var snapshot = Material.Capture(original);
        Assert.Equal(FileRecoveryKinds.ContainerUpload,snapshot.Kind); Assert.Equal(11,FileRecoveryValidation.Rank(snapshot.Kind));
        Assert.Equal(original.Id,snapshot.Binding.ExecutionJobId); Assert.Equal(original.TraceId,Material.Read<ContainerFileUploadOperation>(snapshot).TraceId);
        var observation = ContainerUploadRecoveryValidation.Wrap(snapshot,original,Facts(original));
        Assert.Equal("Unknown",observation.State); Assert.False(observation.BindingCurrent);
        Assert.Equal("Published",Assert.IsType<ContainerUploadRecoveryObservationDto>(Material.ValidateObservation(snapshot,observation)).RootState);
        Assert.Throws<ArgumentException>(() => FileRecoveryValidation.Command(new(snapshot.Kind,original.Id,original.Version,"ReconcileCompleted","IncidentReview")));
        FileRecoveryValidation.Command(new(snapshot.Kind,original.Id,original.Version,"AcknowledgeUnknown","IncidentReview"));
        Assert.DoesNotContain(original.LoginBinding,snapshot.ToString(),StringComparison.Ordinal);
    }
    [Theory]
    [InlineData("owner")][InlineData("path")][InlineData("context")][InlineData("kind")][InlineData("mutable-snapshot")]
    public void ChangedOriginalCannotBorrowTheSignedSnapshot(string field)
    {
        var original = Original(); var snapshot = Material.Capture(original); var node = JsonSerializer.SerializeToNode(original)!;
        if (field == "owner") node[nameof(original.OwnerHash)] = new string('F',64);
        if (field == "path") node[nameof(original.RelativePath)] = "another/file.bin";
        if (field == "context") node[nameof(original.TraceId)] = "another-trace";
        if (field == "mutable-snapshot") node[nameof(original.PublishBarrier)] = false;
        var changed = new FileRecoverySnapshot(field == "kind" ? FileRecoveryKinds.Upload : snapshot.Kind,node.ToJsonString(),snapshot.Binding);
        Assert.Throws<InvalidOperationException>(() => Material.Read<ContainerFileUploadOperation>(changed));
    }
    [Theory]
    [InlineData("container")][InlineData("instance")][InlineData("operation")][InlineData("proof")][InlineData("hash")][InlineData("seal")][InlineData("state")]
    public void HistoricalFactsMustBindTheOriginalAndTheirIndependentDigest(string field)
    {
        var original = Original(); var facts = Facts(original);
        facts = field switch
        {
            "container" => facts with { ContainerId = new('b',64) }, "instance" => facts with { InstanceId = facts.InstanceId.Replace("777:","778:",StringComparison.Ordinal) },
            "operation" => facts with { OperationId = Guid.NewGuid() }, "proof" => facts with { TargetProof = new('A',64) },
            "hash" => facts with { ObservationHash = new('A',64) }, "seal" => facts with { Sealed = true }, _ => facts with { RootState = "Completed" }
        };
        Assert.Throws<InvalidOperationException>(() => ContainerUploadRecoveryValidation.Wrap(Material.Capture(original),original,facts));
    }
    [Theory]
    [InlineData("extra")][InlineData("duplicate")][InlineData("receipt-hash")][InlineData("success")][InlineData("current")]
    public void UnboundOrAmbiguousSqlReceiptJsonCannotSupportRecovery(string field)
    {
        var original = Original(); var snapshot = Material.Capture(original); var observation = ContainerUploadRecoveryValidation.Wrap(snapshot,original,Facts(original));
        observation = field switch
        {
            "extra" => observation with { ReceiptJson = observation.ReceiptJson[..^1]+",\"Private\":true}" },
            "duplicate" => observation with { ReceiptJson = observation.ReceiptJson[..^1]+",\"Sealed\":false}" },
            "receipt-hash" => observation with { ReceiptHash = new('A',64) }, "success" => observation with { State = "Completed" }, _ => observation with { BindingCurrent = true }
        };
        Assert.Throws<InvalidOperationException>(() => Material.ValidateObservation(snapshot,observation));
    }
    [Fact]
    public void LostSealReplyOnlyAcceptsThisRecoveryAndAllHumanApprovedOriginalFacts()
    {
        var original = Original(); var raw = Material.Capture(original); var id = Guid.NewGuid();
        var approved = ContainerUploadRecoveryValidation.Wrap(raw,original,Facts(original));
        var snapshot = new FileRecoverySnapshot(raw.Kind,raw.OriginalJson,raw.Binding) { RecoveryExecutionId = id,ApprovedObservation = approved };
        var sealedReceipt = ContainerUploadRecoveryValidation.Wrap(raw,original,Facts(original,true,id));
        Assert.True(ContainerUploadRecoveryValidation.MatchesApprovedOrSealed(snapshot,sealedReceipt));
        Assert.False(ContainerUploadRecoveryValidation.MatchesApprovedOrSealed(snapshot,approved));
        var another = ContainerUploadRecoveryValidation.Wrap(raw,original,Facts(original,true,Guid.NewGuid()));
        Assert.False(ContainerUploadRecoveryValidation.MatchesApprovedOrSealed(snapshot,another));
        // A later administrator may explicitly approve an already sealed
        // history, without impersonating or rewriting the previous sealer.
        var handover = new FileRecoverySnapshot(raw.Kind,raw.OriginalJson,raw.Binding) { RecoveryExecutionId = Guid.NewGuid(),ApprovedObservation = sealedReceipt };
        Assert.True(ContainerUploadRecoveryValidation.MatchesApprovedOrSealed(handover,sealedReceipt));
    }
}
