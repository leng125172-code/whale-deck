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
    // Real PostgreSQL/MAC/locks/transactions, explicit synthetic identity/plans/receipts. No actual Root or UI.
    private static async Task ContainerRecyclePurgeBoundaries(DbContextOptions<PlatformDbContext> options)
    {
        await using var db=new PlatformDbContext(options);await using var second=new PlatformDbContext(options);
        var keyPath=Path.GetTempFileName();await File.WriteAllBytesAsync(keyPath,RandomNumberGenerator.GetBytes(32));
        var protector=new FilePlanProtector(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Agent:CapabilityKeyFile"]=keyPath}).Build());
        var clock=new RecycleClock();var sourceRepo=new ContainerRecycleRepository(db,protector,clock);var repo=new ContainerRecyclePurgeRepository(db,protector,clock,protector);var other=new ContainerRecyclePurgeRepository(second,protector,clock,protector);
        var actor=new TerminalActor("native-independent-purge-admin",null,[],new('A',64),"native-independent-purge-trace",null);
        async Task<ContainerRecycleIntent> Source()
        {
            var key=Guid.NewGuid();var now=ContainerRecycleWorkflowValidation.Time(clock.GetUtcNow());var id=ContainerRecycleWorkflowValidation.IntentId(actor.Subject,actor.LoginBinding,key);
            var command=new ContainerRecycleCommandDto(new(new('a',64),"777:2026-11-05T01:02:03.123456789Z","work/independent-purge-"+id.ToString("N")),new('B',64));
            var binding=new ContainerRecycleRequestBinding(id,key,actor.Subject,actor.LoginBinding,actor.TraceId,ContainerRecycleWorkflowValidation.Owner(actor.Subject,actor.LoginBinding),command,now,now.AddMinutes(15));
            var row=new ContainerRecycleIntent{Id=id,IdempotencyKey=key,ActorSubject=actor.Subject,LoginBinding=actor.LoginBinding,TraceId=actor.TraceId,OwnerHash=binding.Owner,ContainerId=command.Location.ContainerId,InstanceId=command.Location.InstanceId,RelativePath=command.Location.Path,
                CommandJson=ContainerRecycleWorkflowValidation.Serialize(command),RequestHash=protector.FingerprintRecycleRequest(binding),CreatedAtUtc=now,PlanningExpiresAtUtc=binding.PlanningExpiresAtUtc,NextAttemptAtUtc=now};
            await sourceRepo.EnqueueAsync(row,default);var claimed=(await sourceRepo.ClaimAsync(clock.GetUtcNow(),default))!;Assert.Equal(id,claimed.Id);Assert.True(await sourceRepo.MarkPlanningAsync(claimed,default));
            var plan=new ContainerRecyclePlanDto(1,command,new('C',64),new(ulong.MaxValue,33),new('D',64),5100,5101,new('E',64),new(ulong.MaxValue,101,33184,1000,1001,1,7,1700000000123456789),1700000000987654321,new('F',64),[new("user.note",[0,255,7])]);
            await sourceRepo.FinishPlanningAsync(claimed,plan,"CONTAINER_RECYCLE_READY",default);var ready=(await sourceRepo.FindAsync(id,default))!;
            await sourceRepo.ConfirmAsync(actor,new(id,actor.Subject,actor.LoginBinding,ready.PlanHash!,ready.ReadyExpiresAtUtc!.Value),ready.Version,default);
            claimed=(await sourceRepo.ClaimAsync(clock.GetUtcNow(),default))!;Assert.Equal(ContainerFileUploadStartResult.Started,await sourceRepo.StartedAsync(claimed,clock.GetUtcNow(),default));
            var receipt=new ContainerRecycleReceiptDto(claimed.ExecutionJobId!.Value,claimed.ContainerId,claimed.InstanceId,plan.Fingerprint,plan.TargetProof,"Trashed",true,false,true,true,new('8',64),true,true,false);
            Assert.True(await sourceRepo.FinishAsync(claimed,"Succeeded","CONTAINER_RECYCLE_TRASHED",receipt,default));return (await sourceRepo.FindAsync(id,default))!;
        }
        ContainerRecyclePurgeIntent Row(ContainerRecycleIntent original,string? sourceProof=null)
        {
            var src=ContainerRecyclePurgeSourceResolver.FromRow(original,protector,protector);var key=Guid.NewGuid();var now=ContainerRecyclePurgeWorkflowValidation.Time(clock.GetUtcNow());var id=ContainerRecyclePurgeWorkflowValidation.IntentId(actor.Subject,actor.LoginBinding,key);
            var b=new ContainerRecyclePurgeRequestBinding(id,key,actor.Subject,actor.LoginBinding,actor.TraceId,ContainerRecyclePurgeWorkflowValidation.Owner(actor.Subject,actor.LoginBinding),src.IntentId,sourceProof??src.ProofHash,src.Command,now,now.AddMinutes(15));
            return new(){Id=id,SourceIntentId=src.IntentId,SourceProofHash=b.SourceProofHash,IdempotencyKey=key,ActorSubject=actor.Subject,LoginBinding=actor.LoginBinding,TraceId=actor.TraceId,OwnerHash=b.Owner,ContainerId=src.Command.Source.Plan.Command.Location.ContainerId,InstanceId=src.Command.Source.Plan.Command.Location.InstanceId,RelativePath=src.Command.Source.Plan.Command.Location.Path,CommandJson=ContainerRecyclePurgeWorkflowValidation.Serialize(src.Command),RequestHash=protector.FingerprintRecyclePurgeRequest(b),CreatedAtUtc=now,PlanningExpiresAtUtc=b.PlanningExpiresAtUtc,NextAttemptAtUtc=now};
        }
        ContainerRecyclePurgePlanDto Plan(ContainerRecyclePurgeIntent r)
        {var c=ContainerRecyclePurgeWorkflowValidation.ReadCommand(r);var p=c.Source.Plan;return new(1,c,p.TargetProof,p.ParentIdentity,p.ParentVersion,p.OwnerUid,p.OwnerGid,new(2049,66),p.SourceIdentity,p.SourceChangedAtUnixNs+100,new('1',64),new('2',64),1,new('3',64),new(2049,67),new(2049,68,33152,0,0,1,p.SourceIdentity.SizeBytes,10),11,p.SourceIdentity.SizeBytes,p.Sha256,new('9',64));}
        ContainerRecyclePurgeReceiptDto Receipt(ContainerRecyclePurgeIntent r,string state)=>new(r.ExecutionJobId!.Value,ContainerRecyclePurgeWorkflowValidation.ReadCommand(r).Source.OperationId,ContainerRecyclePurgeWorkflowValidation.ReadPlan(r).Fingerprint,state,state!="Aborted",state!="Aborted",state=="Unknown"?null:state=="Completed");
        ContainerRecyclePurgeConfirmation Claims(ContainerRecyclePurgeIntent r)=>new(r.Id,r.ActorSubject,r.LoginBinding,r.PlanHash!,r.ReadyExpiresAtUtc!.Value);
        async Task<ContainerRecyclePurgeIntent> Ready(ContainerRecycleIntent src)
        {var row=Row(src);await repo.EnqueueAsync(row,default);var claim=(await repo.ClaimAsync(clock.GetUtcNow(),default))!;Assert.Equal(row.Id,claim.Id);Assert.True(await repo.MarkPlanningAsync(claim,default));await repo.FinishPlanningAsync(claim,Plan(row),"CONTAINER_RECYCLE_PURGE_READY",default);return (await repo.FindAsync(row.Id,default))!;}
        async Task<ContainerRecyclePurgeIntent> Pending(ContainerRecyclePurgeIntent r)
        {await repo.ConfirmAsync(actor,Claims(r),r.Version,default);return (await repo.ClaimAsync(clock.GetUtcNow(),default))!;}
        try
        {
            Assert.Equal(21,await db.Database.SqlQueryRaw<int>("SELECT count(*)::integer AS \"Value\" FROM pg_constraint WHERE conrelid='container_recycle_purge_intents'::regclass AND contype='c'").SingleAsync());
            Assert.Equal(15,await db.Database.SqlQueryRaw<int>("SELECT count(*)::integer AS \"Value\" FROM pg_trigger t JOIN pg_proc p ON p.oid=t.tgfoid WHERE p.proname='whaledeck_container_recycle_purge_fence' AND NOT t.tgisinternal").SingleAsync());
            var src=await Source();var original=JsonSerializer.Serialize(src);var ready=await Ready(src);Assert.Null(await other.ClaimAsync(clock.GetUtcNow(),default));
            await Assert.ThrowsAsync<InvalidOperationException>(()=>repo.ConfirmAsync(actor,Claims(ready),ready.Version+1,default));
            await using(var tx=await second.Database.BeginTransactionAsync())
            {await second.Database.ExecuteSqlInterpolatedAsync($"SELECT * FROM container_recycle_intents WHERE id={src.Id} FOR UPDATE");var ex=await Assert.ThrowsAnyAsync<Exception>(()=>repo.ConfirmAsync(actor,Claims(ready),ready.Version,default));Assert.Equal(PostgresErrorCodes.LockNotAvailable,Assert.IsType<PostgresException>(ex.GetBaseException()).SqlState);await tx.RollbackAsync();}
            var pending=await Pending(ready);
            Assert.Equal(ContainerFileUploadStartResult.LeaseLost,await repo.ApplyingAsync(pending,clock.GetUtcNow(),default));
            Assert.Equal(ContainerFileUploadStartResult.Started,await repo.StartedAsync(pending,clock.GetUtcNow(),default));
            var hostHash=FileManagementService.Hash("purge-host-lock:"+pending.Id);var lockError=await Assert.ThrowsAsync<PostgresException>(()=>second.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO file_path_locks(path_hash,operation_id,scope_kind) VALUES ({hostHash},{pending.Id},'Legacy')"));Assert.Equal(PostgresErrorCodes.ObjectNotInPrerequisiteState,lockError.SqlState);
            Assert.True(await repo.ReservedAsync(pending,Receipt(pending,"Reserved"),default));
            await using(var tx=await second.Database.BeginTransactionAsync())
            {await second.Database.ExecuteSqlInterpolatedAsync($"SELECT * FROM container_recycle_intents WHERE id={src.Id} FOR UPDATE");var ex=await Assert.ThrowsAnyAsync<Exception>(()=>repo.ApplyingAsync(pending,clock.GetUtcNow(),default));Assert.Equal(PostgresErrorCodes.LockNotAvailable,Assert.IsType<PostgresException>(ex.GetBaseException()).SqlState);await tx.RollbackAsync();}
            Assert.Null((await other.FindAsync(pending.Id,default))!.ApplyStartedAtUtc);Assert.Equal(ContainerFileUploadStartResult.Started,await repo.ApplyingAsync(pending,clock.GetUtcNow(),default));
            Assert.True(await repo.FinishAsync(pending,"Unknown","CONTAINER_RECYCLE_PURGE_RESULT_UNKNOWN",Receipt(pending,"Unknown"),default));var unknown=(await other.FindAsync(pending.Id,default))!;Assert.Null(unknown.PurgeApplied);Assert.True(unknown.PublishBarrier);Assert.Null(await repo.ClaimAsync(clock.GetUtcNow(),default));
            var immutable=await Assert.ThrowsAsync<PostgresException>(()=>second.Database.ExecuteSqlInterpolatedAsync($"UPDATE container_recycle_purge_intents SET source_proof_hash={new string('0',64)} WHERE id={unknown.Id}"));Assert.Equal("container_recycle_purge_request_immutable",immutable.MessageText);
            await Assert.ThrowsAsync<PostgresException>(()=>second.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM container_recycle_purge_intents WHERE id={unknown.Id}"));
            var admin=actor with{Subject="another-native-purge-admin",LoginBinding=new('9',64)};await repo.AcknowledgeAsync(admin,unknown.Id,unknown.Version,Receipt(unknown,"Unknown"),default);var ack=(await other.FindAsync(unknown.Id,default))!;Assert.Null(ack.PurgeApplied);Assert.False(ack.PublishBarrier);Assert.True(ContainerRecyclePurgeWorkflowValidation.HasValidBinding(ack,protector));
            await Assert.ThrowsAnyAsync<Exception>(()=>repo.EnqueueAsync(Row(src),default));
            var corrupted=Row(await Source(),new('0',64));await Assert.ThrowsAsync<InvalidOperationException>(()=>repo.EnqueueAsync(corrupted,default));Assert.False(await second.OperationJobs.AnyAsync(x=>x.Id==corrupted.Id));
            var cancel=await Pending(await Ready(await Source()));await repo.CancelAsync(actor,cancel.Id,default);Assert.True(await repo.FinishAsync(cancel,"Canceled","CONTAINER_RECYCLE_PURGE_CANCELED",Receipt(cancel,"Aborted"),default));
            var normalSource=await Source();var normalSourceBefore=JsonSerializer.Serialize(normalSource);var normal=await Pending(await Ready(normalSource));Assert.Equal(ContainerFileUploadStartResult.Started,await repo.StartedAsync(normal,clock.GetUtcNow(),default));Assert.True(await repo.ReservedAsync(normal,Receipt(normal,"Reserved"),default));await repo.DeferAsync(normal,default);clock.Offset+=TimeSpan.FromSeconds(7);var reclaimed=(await other.ClaimAsync(clock.GetUtcNow(),default))!;Assert.Equal(normal.Id,reclaimed.Id);Assert.False(await repo.RenewAsync(normal,clock.GetUtcNow(),default));
            Assert.Equal(ContainerFileUploadStartResult.Started,await other.ApplyingAsync(reclaimed,clock.GetUtcNow(),default));Assert.False(await repo.FinishAsync(normal,"Succeeded","CONTAINER_RECYCLE_PURGE_COMPLETED",Receipt(normal,"Completed"),default));Assert.True(await other.FinishAsync(reclaimed,"Succeeded","CONTAINER_RECYCLE_PURGE_COMPLETED",Receipt(reclaimed,"Completed"),default));
            var done=(await repo.FindAsync(normal.Id,default))!;Assert.True(done.PurgeApplied);Assert.True(ContainerRecyclePurgeWorkflowValidation.HasValidBinding(done,protector));Assert.Equal(normalSourceBefore,JsonSerializer.Serialize(await sourceRepo.FindAsync(normalSource.Id,default)));Assert.Equal(original,JsonSerializer.Serialize(await sourceRepo.FindAsync(src.Id,default)));await Assert.ThrowsAnyAsync<Exception>(()=>repo.EnqueueAsync(Row(normalSource),default));
            var rollback=Row(await Source());await second.Database.ExecuteSqlRawAsync("CREATE FUNCTION wdv_purge_audit_reject() RETURNS trigger LANGUAGE plpgsql AS $f$ BEGIN IF NEW.action LIKE 'containers.files.recycle-purge.%' THEN RAISE EXCEPTION 'test audit fault'; END IF; RETURN NEW; END $f$; CREATE TRIGGER wdv_purge_audit_reject BEFORE INSERT ON audit_events FOR EACH ROW EXECUTE FUNCTION wdv_purge_audit_reject();");
            try{await Assert.ThrowsAsync<DbUpdateException>(()=>repo.EnqueueAsync(rollback,default));Assert.False(await second.OperationJobs.AnyAsync(j=>j.Id==rollback.Id));Assert.False(await second.ContainerRecyclePurgeIntents.AnyAsync(j=>j.Id==rollback.Id));}
            finally{await second.Database.ExecuteSqlRawAsync("DROP TRIGGER wdv_purge_audit_reject ON audit_events; DROP FUNCTION wdv_purge_audit_reject();");}
            var migrations=db.Database.GetMigrations().ToArray();Assert.EndsWith("_ContainerRecyclePurgeIntents",migrations[^1],StringComparison.Ordinal);var down=await Assert.ThrowsAsync<PostgresException>(()=>db.GetService<IMigrator>().MigrateAsync(migrations[^2]));Assert.Equal("container_recycle_purge_downgrade_requires_empty_table",down.MessageText);
            Assert.False(await db.ContainerRecyclePurgeIntents.AnyAsync(r=>r.CompletedAtUtc==null||r.PublishBarrier||r.LeaseToken!=null));Assert.False(await db.OutboxMessages.AnyAsync(m=>(m.MessageType=="ContainerRecyclePurgeRequested"||m.MessageType=="ContainerRecyclePurgeCancellationRequested")&&m.ProcessedAtUtc==null));
            Console.WriteLine("INBOX_PHASE=container-recycle-purge-two-phase-source-locked-fifteen-fences-nullable-audited");
        }
        finally{File.Delete(keyPath);}
    }
}
