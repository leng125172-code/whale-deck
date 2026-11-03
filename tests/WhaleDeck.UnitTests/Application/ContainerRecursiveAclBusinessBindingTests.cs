using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Infrastructure.Agent;
using static WhaleDeck.UnitTests.Application.ContainerRecursiveAclWorkflowFixture;

namespace WhaleDeck.UnitTests.Application;

public sealed class ContainerRecursiveAclBusinessBindingTests
{
    [Theory][InlineData("access")][InlineData("default")][InlineData("clear")]
    public void CanonicalRoundTripKeepsListValuesNullAndEmptyAndKernelPrecision(string kind)
    {
        using var f=new Fixture();var plan=Plan();
        if(kind!="access")plan=plan with { Command=plan.Command with { AclType="default",FileEntries=null,DirectoryEntries=kind=="clear"?[]:Entries() },ChangeCount=2 };
        var bound=new ContainerRecursiveAclPlanBinding(f.Request(plan.Command),plan,f.Now.AddMinutes(5));
        var raw=ContainerRecursiveAclWorkflowValidation.Serialize(bound);
        var read=JsonSerializer.Deserialize<ContainerRecursiveAclPlanBinding>(raw,ContainerRecursiveAclWorkflowValidation.Json)!;
        Assert.True(ContainerRecursiveAclValidation.SameCommand(bound.Request.Command,read.Request.Command));
        Assert.Equal(raw,ContainerRecursiveAclWorkflowValidation.Serialize(read));
        Assert.Equal(ulong.MaxValue,read.Plan.RootIdentity.Device);Assert.Equal(1700000000987654321,read.Plan.RootChangedAtUnixNs);
        Assert.Equal(f.Protector.FingerprintRecursiveAclPlan(bound),f.Protector.FingerprintRecursiveAclPlan(read));
        if(kind=="clear"){Assert.Null(read.Plan.Command.FileEntries);Assert.Empty(read.Plan.Command.DirectoryEntries);}
    }
    [Theory][InlineData("id")][InlineData("key")][InlineData("actor")][InlineData("login")][InlineData("trace")]
    [InlineData("owner")][InlineData("created")][InlineData("expiry")][InlineData("files")][InlineData("directories")][InlineData("kind")]
    public void RequestAuthorityBindsEveryIdentityDeadlineAndBothAclTargets(string change)
    {
        using var f=new Fixture();var r=f.Request();
        var changed=change switch {
            "id"=>r with {Id=Guid.NewGuid()},"key"=>r with {IdempotencyKey=Guid.NewGuid()},"actor"=>r with {Actor="different"},
            "login"=>r with {LoginBinding=new('F',64)},"owner"=>r with {Owner=new('F',64)},"trace"=>r with {TraceId="different"},
            "created"=>r with {CreatedAtUtc=r.CreatedAtUtc.AddTicks(1)},"expiry"=>r with {PlanningExpiresAtUtc=r.PlanningExpiresAtUtc.AddSeconds(1)},
            "files"=>r with {Command=r.Command with {FileEntries=Entries(4,0,0)}},
            "directories"=>r with {Command=r.Command with {DirectoryEntries=Entries(5)}},
            _=>r with {Command=r.Command with {AclType="default",FileEntries=null}} };
        if(change is "trace" or "files" or "directories" or "kind")Assert.NotEqual(f.Protector.FingerprintRecursiveAclRequest(r),f.Protector.FingerprintRecursiveAclRequest(changed));
        else Assert.Throws<ArgumentException>(()=>f.Protector.FingerprintRecursiveAclRequest(changed));
    }
    [Theory][InlineData("domain")][InlineData("duplicate")][InlineData("extra")][InlineData("whitespace")][InlineData("casing")]
    [InlineData("empty-id")][InlineData("login")][InlineData("expiry")][InlineData("encoding")]
    public void CorrectMacDoesNotAuthorizeNoncanonicalOrCrossDomainConfirmation(string change)
    {
        using var f=new Fixture();var request=f.Request();var claims=new ContainerRecursiveAclConfirmation(request.Id,request.Actor,request.LoginBinding,new('B',64),f.Now.AddMinutes(5));
        var raw=ContainerRecursiveAclWorkflowValidation.Serialize(claims);
        raw=change switch {"duplicate"=>raw.Replace("{","{\"Id\":\""+request.Id+"\",",StringComparison.Ordinal),"extra"=>raw[..^1]+",\"Extra\":true}",
            "whitespace"=>" "+raw,"casing"=>raw.Replace("\"Actor\"","\"actor\"",StringComparison.Ordinal),
            "empty-id"=>ContainerRecursiveAclWorkflowValidation.Serialize(claims with {Id=Guid.Empty}),
            "login"=>ContainerRecursiveAclWorkflowValidation.Serialize(claims with {LoginBinding=""}),
            "expiry"=>ContainerRecursiveAclWorkflowValidation.Serialize(claims with {ExpiresAtUtc=DateTimeOffset.UnixEpoch}),_=>raw};
        var payload=Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));if(change=="encoding")payload=" "+payload;
        var domain=change=="domain"?"whaledeck-container-acl-business-confirmation-v1":"whaledeck-container-recursive-acl-business-confirmation-v1";
        var mac=Convert.ToHexString(HMACSHA256.HashData(f.Key,Encoding.UTF8.GetBytes(domain+"\n"+payload)));
        Assert.Throws<ArgumentException>(()=>f.Protector.VerifyRecursiveAclConfirmation(payload+"."+mac));
    }
    [Fact]
    public void OrdinaryAclAndRecursiveOwnershipAndModeAuthoritiesCannotBeReused()
    {
        using var f=new Fixture();var r=f.Request();var claims=new ContainerRecursiveAclConfirmation(r.Id,r.Actor,r.LoginBinding,new('B',64),f.Now.AddMinutes(5));
        var token=f.Protector.SignRecursiveAclConfirmation(claims);Assert.Equal(claims,f.Protector.VerifyRecursiveAclConfirmation(token));
        Assert.Throws<ArgumentException>(()=>f.Protector.VerifyAclConfirmation(token));Assert.Throws<ArgumentException>(()=>f.Protector.VerifyRecursiveOwnershipConfirmation(token));
        Assert.Throws<ArgumentException>(()=>f.Protector.VerifyRecursivePermissionConfirmation(token));
        Assert.Throws<ArgumentException>(()=>f.Protector.VerifyRecursiveAclConfirmation(f.Protector.SignAclConfirmation(new(r.Id,r.Actor,r.LoginBinding,new('B',64),claims.ExpiresAtUtc))));
        Assert.Throws<ArgumentException>(()=>f.Protector.VerifyRecursiveAclConfirmation(f.Protector.SignRecursiveOwnershipConfirmation(new(r.Id,r.Actor,r.LoginBinding,new('B',64),claims.ExpiresAtUtc))));
        Assert.Throws<ArgumentException>(()=>f.Protector.VerifyRecursiveAclConfirmation(f.Protector.SignRecursivePermissionConfirmation(new(r.Id,r.Actor,r.LoginBinding,new('B',64),claims.ExpiresAtUtc))));
        Assert.NotEqual(r.Id,ContainerAclWorkflowValidation.IntentId(r.Actor,r.LoginBinding,r.IdempotencyKey));
        Assert.NotEqual(r.Id,ContainerRecursiveOwnershipWorkflowValidation.IntentId(r.Actor,r.LoginBinding,r.IdempotencyKey));
        Assert.NotEqual(r.Id,ContainerRecursivePermissionWorkflowValidation.IntentId(r.Actor,r.LoginBinding,r.IdempotencyKey));
        Assert.NotEqual(r.Owner,ContainerAclWorkflowValidation.Owner(r.Actor,r.LoginBinding));
        Assert.NotEqual(r.Owner,ContainerRecursiveOwnershipWorkflowValidation.Owner(r.Actor,r.LoginBinding));
        Assert.NotEqual(r.Owner,ContainerRecursivePermissionWorkflowValidation.Owner(r.Actor,r.LoginBinding));
    }
    [Fact]
    public void BusinessRequestPlanExecutionAndReceiptHaveSeparateMacDomains()
    {
        using var f=new Fixture();var p=Plan();var request=f.Request(p.Command);var bound=new ContainerRecursiveAclPlanBinding(request,p,f.Now.AddMinutes(5));var hash=f.Protector.FingerprintRecursiveAclPlan(bound);
        var execution=new ContainerRecursiveAclExecutionBinding(request.Id,request.Actor,request.LoginBinding,hash,ContainerRecursiveAclWorkflowValidation.ExecutionId(request.Id),f.Now,f.Now.AddMinutes(15));
        var op=new ContainerRecursiveAclOperationDto(execution.ExecutionJobId,request.Owner,p,new(request.Actor,request.TraceId,execution.ExecutionJobId,request.IdempotencyKey));
        var receipt=new ContainerRecursiveAclReceiptDto(op.Id,p.Command.Location.ContainerId,p.Command.Location.InstanceId,p.Fingerprint,p.TargetProof,"Unknown",false,false,0,0,0,0,4,true,false,"",false,"",p.Command.AclType);
        var values=new[]{f.Protector.FingerprintRecursiveAclRequest(request),hash,f.Protector.FingerprintRecursiveAclExecution(execution),f.Protector.FingerprintRecursiveAclReceipt(new(op,receipt))};
        Assert.Equal(4,values.Distinct().Count());
        Assert.NotEqual(values[3],f.Protector.FingerprintRecursiveAclReceipt(new(op,receipt with {Replayed=true})));
        Assert.Throws<InvalidOperationException>(()=>f.Protector.FingerprintRecursiveAclReceipt(new(op,receipt with {AclType="default"})));
    }
    [Theory][InlineData("FileEntries")][InlineData("DirectoryEntries")]
    public void MissingRequiredTargetCannotDeserializeAsDefaultOrClear(string field)
    {
        var command=Plan().Command with {AclType="default",FileEntries=null,DirectoryEntries=[]};var raw=ContainerRecursiveAclWorkflowValidation.Serialize(command);
        var node=JsonNode.Parse(raw)!.AsObject();node.Remove(field);
        Assert.Throws<JsonException>(()=>JsonSerializer.Deserialize<ContainerRecursiveAclCommandDto>(node.ToJsonString(),ContainerRecursiveAclWorkflowValidation.Json));
    }
    [Theory][InlineData("job")][InlineData("created")][InlineData("expiry")][InlineData("actor")][InlineData("login")][InlineData("plan")]
    public void ExecutionBindsOriginalJobActorLoginPlanAndDeadline(string change)
    {
        using var f=new Fixture();var r=f.Request();var bound=new ContainerRecursiveAclExecutionBinding(r.Id,r.Actor,r.LoginBinding,new('B',64),ContainerRecursiveAclWorkflowValidation.ExecutionId(r.Id),f.Now,f.Now.AddMinutes(15));
        var changed=change switch {"job"=>bound with {ExecutionJobId=Guid.NewGuid()},"created"=>bound with {ConfirmedAtUtc=f.Now.AddTicks(1)},
            "expiry"=>bound with {ExecutionExpiresAtUtc=f.Now.AddHours(1)},"actor"=>bound with {Actor="different"},"login"=>bound with {LoginBinding=new('F',64)},_=>bound with {PlanHash=new('E',64)}};
        if(change is "actor" or "login" or "plan")Assert.NotEqual(f.Protector.FingerprintRecursiveAclExecution(bound),f.Protector.FingerprintRecursiveAclExecution(changed));
        else Assert.Throws<ArgumentException>(()=>f.Protector.FingerprintRecursiveAclExecution(changed));
    }
    [Theory][InlineData("")][InlineData("00000000-0000-0000-0000-000000000000")][InlineData("A26533E0-029E-48D8-88B8-8273058B370D")][InlineData("a26533e0029e48d888b88273058b370d")]
    public void IdempotencyKeyMustBeCanonicalNonemptyGuid(string key)=>Assert.Throws<ArgumentException>(()=>ContainerRecursiveAclWorkflowValidation.Key(key));
    private sealed class Fixture:IDisposable
    {
        private readonly string _path=Path.GetTempFileName();public byte[] Key{get;}=RandomNumberGenerator.GetBytes(32);
        public DateTimeOffset Now{get;}=new(2026,11,4,1,2,3,TimeSpan.Zero);public FilePlanProtector Protector{get;}
        public Fixture(){File.WriteAllBytes(_path,Key);Protector=new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Agent:CapabilityKeyFile"]=_path}).Build());}
        public ContainerRecursiveAclRequestBinding Request(ContainerRecursiveAclCommandDto? command=null)
        {const string actor="synthetic-acl-admin";string login=new('A',64);var key=Guid.NewGuid();return new(ContainerRecursiveAclWorkflowValidation.IntentId(actor,login,key),key,actor,login,"synthetic-trace",ContainerRecursiveAclWorkflowValidation.Owner(actor,login),command??Plan().Command,Now,Now.AddMinutes(15));}
        public void Dispose(){CryptographicOperations.ZeroMemory(Key);File.Delete(_path);}
    }
}
