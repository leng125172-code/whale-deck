using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Npgsql;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Agent;
using WhaleDeck.Infrastructure.Persistence;

namespace WhaleDeck.IntegrationTests;

public sealed partial class NativeNotificationDispatchTests
{
    /// <summary>Real isolated SQL/old MAC/constraints/atomicity; history and Root receipts remain synthetic.</summary>
    private static async Task ContainerAclRestoreBoundaries(DbContextOptions<PlatformDbContext> options)
    {
        await using var db=new PlatformDbContext(options);await using var peer=new PlatformDbContext(options);
        var key=Path.GetTempFileName();await File.WriteAllBytesAsync(key,RandomNumberGenerator.GetBytes(32));
        try
        {
            var protector=new FilePlanProtector(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Agent:CapabilityKeyFile",key}}).Build());
            var clock=new AclClock();var repo=new ContainerAclRepository(db,protector,clock);var other=new ContainerAclRepository(peer,protector,clock);
            var actor=new TerminalActor("native-acl-restore",null,[],new('A',64),"native-acl-restore-trace",null);
            ContainerAclEntryDto[] Basic()=>[new("user_obj",null,6),new("group_obj",null,4),new("other",null,0)];
            ContainerAclEntryDto[] Desired()=>[new("user_obj",null,6),new("user",0,0),new("group_obj",null,4),new("mask",null,6),new("other",null,0)];
            ContainerAclIntent Row(bool restore=true,string? path=null)
            {
                var idempotency=Guid.NewGuid();var id=ContainerAclWorkflowValidation.IntentId(actor.Subject,actor.LoginBinding,idempotency);var now=ContainerAclWorkflowValidation.Time(clock.GetUtcNow());
                var command=new ContainerAclCommandDto(new(new('f',64),"777:2026-11-03T01:02:03Z",path??"work/restore-"+id.ToString("N")),"access",Desired(),new('B',64));
                var source=restore?new ContainerAclHistorySnapshotDto(command.Location,new(Guid.NewGuid().ToString("N"),new('C',64),0,"666:2026-11-02T01:02:03Z","access","File",432,5,0),Desired(),[]):null;
                var binding=new ContainerAclRequestBinding(id,idempotency,actor.Subject,actor.LoginBinding,actor.TraceId,ContainerAclWorkflowValidation.Owner(actor.Subject,actor.LoginBinding),command,now,now.AddMinutes(15),source);
                return new(){Id=id,IdempotencyKey=idempotency,ActorSubject=actor.Subject,LoginBinding=actor.LoginBinding,TraceId=actor.TraceId,OwnerHash=binding.Owner,
                    ContainerId=command.Location.ContainerId,InstanceId=command.Location.InstanceId,RelativePath=command.Location.Path,CommandJson=ContainerAclWorkflowValidation.Serialize(command),
                    RestoreSourceJson=source is null?null:ContainerAclWorkflowValidation.Serialize(source),RequestHash=protector.FingerprintAclRequest(binding),CreatedAtUtc=now,PlanningExpiresAtUtc=binding.PlanningExpiresAtUtc,NextAttemptAtUtc=now};
            }
            async Task Rejected(FormattableString sql,string message)
            {
                var e=await Assert.ThrowsAsync<PostgresException>(()=>peer.Database.ExecuteSqlInterpolatedAsync(sql));Assert.Equal("23514",e.SqlState);
                Assert.Equal(message,e.MessageText);peer.ChangeTracker.Clear();
            }
            var row=Row();await repo.EnqueueAsync(row,default);var saved=(await other.FindAsync(row.Id,default))!;
            Assert.Equal(row.RestoreSourceJson,saved.RestoreSourceJson);Assert.True(ContainerAclWorkflowValidation.HasValidBinding(saved,protector));
            var audit=await peer.AuditEvents.AsNoTracking().SingleAsync(a=>a.JobId==row.Id&&a.Action=="containers.files.acl.restore.plan-accepted");
            using(var doc=JsonDocument.Parse(audit.DetailJson!))
            {Assert.True(doc.RootElement.GetProperty("historyRestore").GetBoolean());Assert.Equal(FileManagementService.Hash(row.RestoreSourceJson!),doc.RootElement.GetProperty("restoreSourceHash").GetString());Assert.False(doc.RootElement.TryGetProperty("source",out _));}
            Assert.DoesNotContain("CaptureId",(await peer.OperationJobs.AsNoTracking().SingleAsync(j=>j.Id==row.Id)).RequestJson,StringComparison.Ordinal);
            await Rejected($"UPDATE container_acl_intents SET restore_source_json=NULL WHERE id={row.Id}","container_acl_restore_source_immutable");
            var duplicateSource=ContainerAclRestoreValidation.Read(row)!;var altered=ContainerAclWorkflowValidation.Serialize(duplicateSource with{Entry=duplicateSource.Entry with{CaptureId=Guid.NewGuid().ToString("N")}});
            await Rejected($"UPDATE container_acl_intents SET restore_source_json={altered} WHERE id={row.Id}","container_acl_restore_source_immutable");
            foreach(var invalid in new[]{"{}","[]",row.RestoreSourceJson!.Replace("\"OriginalMode\":432","\"OriginalMode\":false",StringComparison.Ordinal),row.RestoreSourceJson.Replace("\"Id\":0","\"Id\":1",StringComparison.Ordinal),row.RestoreSourceJson.Replace("\"ChangedAclType\":\"access\"","\"ChangedAclType\":\"default\"",StringComparison.Ordinal)})
            {
                var valid=await peer.Database.SqlQueryRaw<bool>("SELECT whaledeck_acl_restore_source_valid({0},{1},{2},{3},{4}) AS \"Value\"",invalid,row.CommandJson,row.ContainerId,row.InstanceId,row.RelativePath).SingleAsync();Assert.False(valid);
            }
            var claimed=(await repo.ClaimAsync(clock.GetUtcNow(),default))!;Assert.Equal(row.Id,claimed.Id);Assert.True(await repo.MarkPlanningAsync(claimed,default));
            var command=ContainerAclWorkflowValidation.ReadCommand(row);
            var plan=new ContainerAclPlanDto(1,command,new('D',64),new(1,9),new('E',64),1000,1001,new('F',64),"File",new(1,10,33184,1000,1001,1,3,1700000000000000000),1700000000000000001,new('9',64),false,Basic(),[],432);
            await repo.FinishPlanningAsync(claimed,plan,"CONTAINER_ACL_READY",default);var ready=(await other.FindAsync(row.Id,default))!;
            Assert.True(ContainerAclWorkflowValidation.HasValidBinding(ready,protector));Assert.Equal(row.RestoreSourceJson,ready.RestoreSourceJson);
            await repo.ConfirmAsync(actor,new(row.Id,actor.Subject,actor.LoginBinding,ready.PlanHash!,ready.ReadyExpiresAtUtc!.Value),ready.Version,default);
            var executing=(await repo.ClaimAsync(clock.GetUtcNow(),default))!;Assert.Equal(ContainerFileUploadStartResult.Started,await repo.StartedAsync(executing,clock.GetUtcNow(),default));
            var ordinary=Row(false,row.RelativePath);await Assert.ThrowsAsync<InvalidOperationException>(()=>other.EnqueueAsync(ordinary,default));
            var op=ContainerAclWorkflowValidation.Reference(executing);
            var receipt=new ContainerAclReceiptDto(op.Id,row.ContainerId,row.InstanceId,plan.Fingerprint,plan.TargetProof,"Completed",new('8',64),true,false,"File",true,false,true,true,new('7',64),"access",432);
            Assert.True(await repo.FinishAsync(executing,"Succeeded","CONTAINER_ACL_COMPLETED",receipt,default));
            var done=(await other.FindAsync(row.Id,default))!;Assert.Equal(row.RestoreSourceJson,done.RestoreSourceJson);Assert.NotEqual(duplicateSource.Entry.HistoryDigest,done.HistoryDigest);Assert.True(ContainerAclWorkflowValidation.HasValidBinding(done,protector));
            Assert.Equal(2,await peer.OperationJobs.CountAsync(j=>j.Id==row.Id||j.Id==done.ExecutionJobId));Assert.Equal(1,await peer.AuditEvents.CountAsync(a=>a.JobId==done.ExecutionJobId&&a.Action=="containers.files.acl.restore.result"));
            await peer.Database.ExecuteSqlRawAsync("""
              CREATE FUNCTION wdv_acl_restore_audit_fail() RETURNS trigger LANGUAGE plpgsql AS $f$
              BEGIN RAISE EXCEPTION 'wdv_acl_restore_audit_fail' USING ERRCODE='23514'; END $f$;
              CREATE TRIGGER wdv_acl_restore_audit_fail BEFORE INSERT ON audit_events
                FOR EACH ROW WHEN (NEW.action LIKE 'containers.files.acl.restore.%') EXECUTE FUNCTION wdv_acl_restore_audit_fail();
              """);
            var rejected=Row();try{await Assert.ThrowsAsync<DbUpdateException>(()=>repo.EnqueueAsync(rejected,default));}
            finally{await peer.Database.ExecuteSqlRawAsync("DROP TRIGGER wdv_acl_restore_audit_fail ON audit_events;DROP FUNCTION wdv_acl_restore_audit_fail();");}
            Assert.False(await peer.ContainerAclIntents.AnyAsync(r=>r.Id==rejected.Id));Assert.False(await peer.OperationJobs.AnyAsync(j=>j.Id==rejected.Id));
            var old=Row(false);await repo.EnqueueAsync(old,default);Assert.True(ContainerAclWorkflowValidation.HasValidBinding((await other.FindAsync(old.Id,default))!,protector));await repo.CancelAsync(actor,old.Id,default);
            var down=await Assert.ThrowsAsync<PostgresException>(()=>db.GetService<IMigrator>().MigrateAsync("20261102180208_ContainerAclIntents"));Assert.Equal("container_acl_restore_downgrade_requires_no_restore_intents",down.MessageText);
            await db.Database.MigrateAsync();Assert.False(await db.ContainerAclIntents.AnyAsync(r=>r.PublishBarrier||r.CompletedAtUtc==null||r.LeaseToken!=null));
            Console.WriteLine("INBOX_PHASE=container-acl-restore-source-mac-immutable-audited-new-jobs");
        }
        finally{File.Delete(key);}
    }
}
