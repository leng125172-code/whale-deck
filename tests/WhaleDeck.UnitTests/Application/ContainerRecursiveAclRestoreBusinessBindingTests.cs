using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Infrastructure.Agent;
using static WhaleDeck.UnitTests.Application.ContainerRecursiveAclRestoreWorkflowFixture;

namespace WhaleDeck.UnitTests.Application;

public sealed class ContainerRecursiveAclRestoreBusinessBindingTests
{
    [Theory][InlineData("access")][InlineData("default")]
    public void CompleteSourceFrameAndHistoricalCountsSurviveCanonicalRoundTrip(string kind)
    {
        using var f = new Fixture(); var plan=Plan(kind:kind); var binding=new ContainerRecursiveAclRestorePlanBinding(f.Request(plan.Command),plan,f.Now.AddMinutes(5));
        var raw=ContainerRecursiveAclRestoreWorkflowValidation.Serialize(binding);
        var read=JsonSerializer.Deserialize<ContainerRecursiveAclRestorePlanBinding>(raw,ContainerRecursiveAclRestoreWorkflowValidation.Json)!;
        Assert.Equal(raw,ContainerRecursiveAclRestoreWorkflowValidation.Serialize(read));
        Assert.Equal(ulong.MaxValue,read.Plan.RestoreSourceFrame.Device); Assert.Equal(-1,read.Plan.RestoreSourceFrame.ModifiedAtUnixNs); Assert.Equal(0,read.Plan.RestoreSourceFrame.ChangedAtUnixNs);
        Assert.Equal(0U,read.Plan.RestoreSourceFrame.OwnerUid); Assert.Equal(0,read.Plan.RestoreSource.CreatedAtUnixMs);
        Assert.Equal(f.Protector.FingerprintRecursiveAclRestorePlan(binding),f.Protector.FingerprintRecursiveAclRestorePlan(read));
        Assert.DoesNotContain("Entries",JsonSerializer.Serialize(read.Request.Command),StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("CaptureId")][InlineData("HistoryDigest")][InlineData("CreatedAtUnixMs")][InlineData("SourceInstanceId")][InlineData("ChangedAclType")]
    [InlineData("ManifestDigest")][InlineData("EntryCount")][InlineData("FileCount")][InlineData("DirectoryCount")][InlineData("ChangeCount")]
    public void EveryHistoricalSummaryFieldIsCoveredByThePlanAuthority(string field)
    {
        using var f=new Fixture();var plan=Plan();var original=new ContainerRecursiveAclRestorePlanBinding(f.Request(plan.Command),plan,f.Now.AddMinutes(5));var source=plan.RestoreSource;
        source=field switch {
            "CaptureId"=>source with {CaptureId=new('e',32)},"HistoryDigest"=>source with {HistoryDigest=new('6',64)},
            "CreatedAtUnixMs"=>source with {CreatedAtUnixMs=1},"SourceInstanceId"=>source with {SourceInstanceId="888:2026-11-03T00:00:00Z"},
            "ChangedAclType"=>source with {ChangedAclType="default"},"ManifestDigest"=>source with {ManifestDigest=new('6',64)},
            "EntryCount"=>source with {EntryCount=5,DirectoryCount=3},"FileCount"=>source with {FileCount=1,DirectoryCount=3},
            "DirectoryCount"=>source with {FileCount=3,DirectoryCount=1},_=>source with {ChangeCount=1} };
        var command=plan.Command with {CaptureId=source.CaptureId,ExpectedHistoryDigest=source.HistoryDigest};
        var changed=plan with {Command=command,RestoreSource=source,EntryCount=source.EntryCount,FileCount=source.FileCount,DirectoryCount=source.DirectoryCount,ChangeCount=source.ChangedAclType=="default"?2U:plan.ChangeCount};
        var request=original.Request with {Command=command};
        Assert.NotEqual(f.Protector.FingerprintRecursiveAclRestorePlan(original),f.Protector.FingerprintRecursiveAclRestorePlan(new(request,changed,original.ReadyExpiresAtUtc)));
    }

    [Theory]
    [InlineData("Device")][InlineData("Inode")][InlineData("Mode")][InlineData("OwnerUid")][InlineData("OwnerGid")]
    [InlineData("LinkCount")][InlineData("SizeBytes")][InlineData("ModifiedAtUnixNs")][InlineData("ChangedAtUnixNs")]
    public void EverySourceFileFrameFieldIsBoundOrRejectedBeforeSigning(string field)
    {
        using var f=new Fixture();var plan=Plan();var original=new ContainerRecursiveAclRestorePlanBinding(f.Request(plan.Command),plan,f.Now.AddMinutes(5));var frame=plan.RestoreSourceFrame;
        frame=field switch {"Device"=>frame with {Device=frame.Device-1},"Inode"=>frame with {Inode=frame.Inode+1},"Mode"=>frame with {Mode=frame.Mode+1},
            "OwnerUid"=>frame with {OwnerUid=1},"OwnerGid"=>frame with {OwnerGid=1},"LinkCount"=>frame with {LinkCount=2},"SizeBytes"=>frame with {SizeBytes=frame.SizeBytes+1},
            "ModifiedAtUnixNs"=>frame with {ModifiedAtUnixNs=0},_=>frame with {ChangedAtUnixNs=1}};
        var changed=original with {Plan=plan with {RestoreSourceFrame=frame}};
        if(field is "Mode" or "OwnerUid" or "OwnerGid" or "LinkCount")Assert.Throws<ArgumentException>(()=>f.Protector.FingerprintRecursiveAclRestorePlan(changed));
        else Assert.NotEqual(f.Protector.FingerprintRecursiveAclRestorePlan(original),f.Protector.FingerprintRecursiveAclRestorePlan(changed));
    }

    [Theory][InlineData("domain")][InlineData("duplicate")][InlineData("extra")][InlineData("whitespace")][InlineData("casing")][InlineData("zero-id")][InlineData("encoding")]
    public void CorrectMacCannotAuthorizeNoncanonicalOrOldDomainConfirmation(string change)
    {
        using var f=new Fixture();var request=f.Request();var claims=new ContainerRecursiveAclRestoreConfirmation(request.Id,request.Actor,request.LoginBinding,new('B',64),f.Now.AddMinutes(5));
        var raw=ContainerRecursiveAclRestoreWorkflowValidation.Serialize(claims);
        raw=change switch {"duplicate"=>raw.Replace("{","{\"Id\":\""+request.Id+"\",",StringComparison.Ordinal),"extra"=>raw[..^1]+",\"Extra\":true}",
            "whitespace"=>" "+raw,"casing"=>raw.Replace("\"Actor\"","\"actor\"",StringComparison.Ordinal),"zero-id"=>ContainerRecursiveAclRestoreWorkflowValidation.Serialize(claims with {Id=Guid.Empty}),_=>raw};
        var payload=Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));if(change=="encoding")payload=" "+payload;
        var domain=change=="domain"?"whaledeck-container-recursive-acl-business-confirmation-v1":"whaledeck-container-recursive-acl-restore-business-confirmation-v1";
        var signature=Convert.ToHexString(HMACSHA256.HashData(f.Key,Encoding.UTF8.GetBytes(domain+"\n"+payload)));
        Assert.Throws<ArgumentException>(()=>f.Protector.VerifyRecursiveAclRestoreConfirmation(payload+"."+signature));
    }

    [Fact]
    public void RestoreAndOrdinaryRecursiveAclIdentitiesOwnersAndConfirmationTokensAreIndependent()
    {
        using var f=new Fixture();var r=f.Request();var claims=new ContainerRecursiveAclRestoreConfirmation(r.Id,r.Actor,r.LoginBinding,new('B',64),f.Now.AddMinutes(5));
        var token=f.Protector.SignRecursiveAclRestoreConfirmation(claims);Assert.Equal(claims,f.Protector.VerifyRecursiveAclRestoreConfirmation(token));
        Assert.Throws<ArgumentException>(()=>f.Protector.VerifyRecursiveAclConfirmation(token));
        Assert.Throws<ArgumentException>(()=>f.Protector.VerifyRecursiveAclRestoreConfirmation(f.Protector.SignRecursiveAclConfirmation(new(r.Id,r.Actor,r.LoginBinding,new('B',64),claims.ExpiresAtUtc))));
        Assert.NotEqual(r.Id,ContainerRecursiveAclWorkflowValidation.IntentId(r.Actor,r.LoginBinding,r.IdempotencyKey));
        Assert.NotEqual(r.Owner,ContainerRecursiveAclWorkflowValidation.Owner(r.Actor,r.LoginBinding));
        Assert.NotEqual(ContainerRecursiveAclRestoreWorkflowValidation.ExecutionId(r.Id),ContainerRecursiveAclWorkflowValidation.ExecutionId(r.Id));
    }

    [Theory][InlineData("RestoreSource")][InlineData("RestoreSourceFrame")][InlineData("RestoreTargetsDigest")]
    public void MissingOrInventedSourceDataCannotBeReadAsCanonicalSavedPlan(string field)
    {
        using var f=new Fixture();var plan=Plan();var r=f.Request(plan.Command);
        var row=new WhaleDeck.Domain.Entities.ContainerRecursiveAclRestoreIntent {Id=r.Id,IdempotencyKey=r.IdempotencyKey,ActorSubject=r.Actor,LoginBinding=r.LoginBinding,TraceId=r.TraceId,
            OwnerHash=r.Owner,ContainerId=r.Command.Location.ContainerId,InstanceId=r.Command.Location.InstanceId,RelativePath=r.Command.Location.Path,
            CommandJson=ContainerRecursiveAclRestoreWorkflowValidation.Serialize(r.Command),RequestHash=f.Protector.FingerprintRecursiveAclRestoreRequest(r),CreatedAtUtc=r.CreatedAtUtc,PlanningExpiresAtUtc=r.PlanningExpiresAtUtc};
        var node=JsonSerializer.SerializeToNode(plan)!.AsObject();node.Remove(field);row.PlanJson=node.ToJsonString();
        Assert.False(ContainerRecursiveAclRestoreWorkflowValidation.HasValidBinding(row,f.Protector));
    }

    [Fact]
    public void TargetProjectionDigestIsRequiredAndChangesTheConfirmationAuthority()
    {
        using var f=new Fixture();var plan=Plan();var binding=new ContainerRecursiveAclRestorePlanBinding(f.Request(plan.Command),plan,f.Now.AddMinutes(5));
        Assert.NotEqual(f.Protector.FingerprintRecursiveAclRestorePlan(binding),f.Protector.FingerprintRecursiveAclRestorePlan(binding with {Plan=plan with {RestoreTargetsDigest=new('6',64)}}));
        Assert.Throws<ArgumentException>(()=>f.Protector.FingerprintRecursiveAclRestorePlan(binding with {Plan=plan with {RestoreTargetsDigest=""}}));
    }

    private sealed class Fixture:IDisposable
    {
        private readonly string path=Path.GetTempFileName();public byte[] Key{get;}=RandomNumberGenerator.GetBytes(32);
        public DateTimeOffset Now{get;}=new(2026,11,5,1,2,3,TimeSpan.Zero);public FilePlanProtector Protector{get;}
        public Fixture(){File.WriteAllBytes(path,Key);Protector=new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Agent:CapabilityKeyFile"]=path}).Build());}
        public ContainerRecursiveAclRestoreRequestBinding Request(ContainerRecursiveAclRestoreCommandDto? command=null)
        {const string actor="synthetic-restore-admin";string login=new('A',64);var key=Guid.NewGuid();return new(ContainerRecursiveAclRestoreWorkflowValidation.IntentId(actor,login,key),key,actor,login,"synthetic-trace",ContainerRecursiveAclRestoreWorkflowValidation.Owner(actor,login),command??Plan().Command,Now,Now.AddMinutes(15));}
        public void Dispose(){CryptographicOperations.ZeroMemory(Key);File.Delete(path);}
    }
}
