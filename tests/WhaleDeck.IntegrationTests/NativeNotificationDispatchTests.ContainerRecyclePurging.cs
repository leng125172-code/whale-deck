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
        var uploadIds=new List<Guid>();var directoryIds=new List<Guid>();
        async Task PeerSeeds()
        {
            var now = ContainerFileUploadIntentValidation.CanonicalTime(clock.GetUtcNow()); var id = Guid.NewGuid(); uploadIds.Add(id); const string subject = "native-recursive-upload-peer";
            var target = new ContainerFileUploadTargetDto(new(new('d', 64), "888:2026-10-30T01:02:03Z", "work/upload-" + id.ToString("N")), new('B', 64), new('C', 64), "ABSENT", 0);
            var binding = new ContainerFileUploadBinding(id, Guid.NewGuid(), subject, actor.LoginBinding, actor.TraceId, target, 0, Convert.ToHexString(SHA256.HashData([])), new('F', 64), now, now.AddMinutes(15));
            var upload = new ContainerFileUploadOperation { Id = id, IdempotencyKey = binding.IdempotencyKey, ActorSubject = subject, LoginBinding = actor.LoginBinding, TraceId = actor.TraceId,
                OwnerHash = ContainerFileUploadIntentValidation.Owner(subject, actor.LoginBinding), ContainerId = target.Location.ContainerId, InstanceId = target.Location.InstanceId,
                RelativePath = target.Location.Path, TargetProof = target.TargetProof, ExpectedParentVersion = target.ExpectedParentVersion, ExpectedVersion = "ABSENT", SizeBytes = 0,
                Sha256 = binding.Sha256, PlanHash = binding.PlanHash, RequestHash = protector.FingerprintContainerUpload(binding), CreatedAtUtc = now, ExpiresAtUtc = binding.ExpiresAtUtc, NextAttemptAtUtc = now };
            await new ContainerFileUploadRepository(second, protector).EnqueueAsync(upload, default);
            id = Guid.NewGuid(); directoryIds.Add(id); const string directorySubject = "native-recursive-directory-peer";
            var plan = new ContainerDirectoryPlanDto(1, new(new(new('e', 64), "999:2026-10-30T01:02:03Z", "work/directory-" + id.ToString("N")), "0750"), new('B', 64), new(1, 2), new('C', 64), 1000, 1001, new('D', 64));
            var frozen = new ContainerDirectoryBindingDto(id, Guid.NewGuid(), directorySubject, actor.LoginBinding, actor.TraceId, ContainerDirectoryWorkflowValidation.Owner(directorySubject, actor.LoginBinding), plan, new('E', 64), now, now.AddMinutes(15));
            var directory = new ContainerDirectoryOperation { Id = id, IdempotencyKey = frozen.IdempotencyKey, ActorSubject = directorySubject, LoginBinding = actor.LoginBinding, TraceId = actor.TraceId,
                OwnerHash = frozen.Owner, ContainerId = plan.Command.Location.ContainerId, InstanceId = plan.Command.Location.InstanceId, RelativePath = plan.Command.Location.Path,
                PlanJson = System.Text.Json.JsonSerializer.Serialize(plan, ContainerDirectoryWorkflowValidation.Json), PlanHash = frozen.PlanHash, RequestHash = protector.FingerprintDirectory(frozen),
                CreatedAtUtc = now, ExpiresAtUtc = frozen.ExpiresAtUtc, NextAttemptAtUtc = now };
            await new ContainerDirectoryRepository(second, protector).EnqueueAsync(directory, now.AddMinutes(5), default);
        }
        var peers=RecycleFenceTables.Concat(["container_recycle_intents","container_recycle_restore_intents"]).ToArray();
        async Task ClonePeer(string table)
        {
            Assert.Contains(table,peers);var seedSql = table switch {
                "container_file_upload_operations" => "SELECT id AS \"Value\" FROM container_file_upload_operations LIMIT 1",
                "container_directory_operations" => "SELECT id AS \"Value\" FROM container_directory_operations LIMIT 1",
                "container_copy_intents" => "SELECT id AS \"Value\" FROM container_copy_intents WHERE completed_at_utc IS NOT NULL AND execution_job_id IS NOT NULL AND plan_json IS NOT NULL LIMIT 1",
                "container_move_intents" => "SELECT id AS \"Value\" FROM container_move_intents WHERE completed_at_utc IS NOT NULL AND execution_job_id IS NOT NULL AND plan_json IS NOT NULL LIMIT 1",
                "container_permission_intents" => "SELECT id AS \"Value\" FROM container_permission_intents WHERE completed_at_utc IS NOT NULL AND execution_job_id IS NOT NULL AND plan_json IS NOT NULL LIMIT 1",
                "container_ownership_intents" => "SELECT id AS \"Value\" FROM container_ownership_intents WHERE completed_at_utc IS NOT NULL AND execution_job_id IS NOT NULL AND plan_json IS NOT NULL LIMIT 1",
                "container_recursive_permission_intents" => "SELECT id AS \"Value\" FROM container_recursive_permission_intents WHERE completed_at_utc IS NOT NULL AND execution_job_id IS NOT NULL AND plan_json IS NOT NULL LIMIT 1",
                "container_recursive_ownership_intents" => "SELECT id AS \"Value\" FROM container_recursive_ownership_intents WHERE completed_at_utc IS NOT NULL AND execution_job_id IS NOT NULL AND plan_json IS NOT NULL LIMIT 1",
                "container_acl_intents" => "SELECT id AS \"Value\" FROM container_acl_intents WHERE completed_at_utc IS NOT NULL AND execution_job_id IS NOT NULL AND plan_json IS NOT NULL LIMIT 1",
                "container_recursive_acl_intents" => "SELECT id AS \"Value\" FROM container_recursive_acl_intents WHERE completed_at_utc IS NOT NULL AND execution_job_id IS NOT NULL AND plan_json IS NOT NULL LIMIT 1",
                "container_recursive_acl_restore_intents" => "SELECT id AS \"Value\" FROM container_recursive_acl_restore_intents WHERE completed_at_utc IS NOT NULL AND execution_job_id IS NOT NULL AND plan_json IS NOT NULL LIMIT 1",
                "container_recycle_intents" => "SELECT id AS \"Value\" FROM container_recycle_intents WHERE completed_at_utc IS NOT NULL AND execution_job_id IS NOT NULL AND plan_json IS NOT NULL LIMIT 1",
                "container_recycle_restore_intents" => "SELECT id AS \"Value\" FROM container_recycle_restore_intents WHERE completed_at_utc IS NOT NULL AND execution_job_id IS NOT NULL AND plan_json IS NOT NULL LIMIT 1",
                _ => throw new InvalidOperationException("unknown fixed fence table") };
            var seed=await second.Database.SqlQueryRaw<Guid>(seedSql).SingleAsync();
            var id=Guid.NewGuid();var execution=Guid.NewGuid();var key=Guid.NewGuid();var now=clock.GetUtcNow();
            second.OperationJobs.AddRange(new OperationJob{Id=id,ActorSubject="native-restore-fence",IdempotencyKey=key.ToString("D"),JobType="native.restore.fence"},new OperationJob{Id=execution,ActorSubject="native-restore-fence",IdempotencyKey=execution.ToString("D"),JobType="native.restore.fence"});await second.SaveChangesAsync();
            var patch=" || jsonb_build_object('source_state',NULL,'restored_by_this_operation',NULL,'receipt_json',NULL,'receipt_hash',NULL,'acl_confirmed',NULL,'root_state',NULL,'source_retained',NULL,'source_restored',NULL,'destination_published',NULL,'source_removed',NULL,'completed_count',NULL,'changed_count',NULL,'aborted_count',NULL,'not_started_count',NULL,'unknown_count',NULL,'outcome_digest',NULL,'binding_current',NULL,'history_available',NULL,'history_may_exist',FALSE,'history_verified',FALSE,'history_digest',NULL)";
            if(table!="container_file_upload_operations" && table!="container_directory_operations")patch+=" || jsonb_build_object('execution_job_id',@execution)";
            var sql="INSERT INTO "+table+" SELECT (jsonb_populate_record(NULL::"+table+",to_jsonb(t) || jsonb_build_object('id',@id,'idempotency_key',@key,'completed_at_utc',NULL,'lease_token',NULL,'lease_expires_at_utc',NULL,'publish_barrier',TRUE,'root_started_at_utc',@now,'state',@state,'published_version',NULL,'acknowledged_by',NULL,'acknowledgement_json',NULL)"+patch+")).* FROM "+table+" t WHERE id=@seed";
            Assert.Equal(1,await second.Database.ExecuteSqlRawAsync(sql,new NpgsqlParameter("id",id),new NpgsqlParameter("key",key),new NpgsqlParameter("execution",execution),new NpgsqlParameter("now",now),new NpgsqlParameter("state",table=="container_file_upload_operations"?"Ready":"Pending"),new NpgsqlParameter("seed",seed)));
        }
        try
        {
            Assert.Equal(21,await db.Database.SqlQueryRaw<int>("SELECT count(*)::integer AS \"Value\" FROM pg_constraint WHERE conrelid='container_recycle_purge_intents'::regclass AND contype='c'").SingleAsync());
            Assert.Equal(15,await db.Database.SqlQueryRaw<int>("SELECT count(*)::integer AS \"Value\" FROM pg_trigger t JOIN pg_proc p ON p.oid=t.tgfoid WHERE p.proname='whaledeck_container_recycle_purge_fence' AND NOT t.tgisinternal").SingleAsync());
            await PeerSeeds();
            var src=await Source();var original=JsonSerializer.Serialize(src);var ready=await Ready(src);Assert.Null(await other.ClaimAsync(clock.GetUtcNow(),default));
            await Assert.ThrowsAsync<InvalidOperationException>(()=>repo.ConfirmAsync(actor,Claims(ready),ready.Version+1,default));
            await using(var tx=await second.Database.BeginTransactionAsync())
            {await second.Database.ExecuteSqlInterpolatedAsync($"SELECT * FROM container_recycle_intents WHERE id={src.Id} FOR UPDATE");var ex=await Assert.ThrowsAnyAsync<Exception>(()=>repo.ConfirmAsync(actor,Claims(ready),ready.Version,default));Assert.Equal(PostgresErrorCodes.LockNotAvailable,Assert.IsType<PostgresException>(ex.GetBaseException()).SqlState);await tx.RollbackAsync();}
            var pending=await Pending(ready);
            Assert.Equal(ContainerFileUploadStartResult.LeaseLost,await repo.ApplyingAsync(pending,clock.GetUtcNow(),default));
            foreach(var table in peers)
            {
                await using var tx=await second.Database.BeginTransactionAsync();await ClonePeer(table);
                var blocked=await Assert.ThrowsAsync<PostgresException>(()=>second.Database.ExecuteSqlInterpolatedAsync($"UPDATE container_recycle_purge_intents SET publish_barrier=true,root_started_at_utc={clock.GetUtcNow()},state='Reserving' WHERE id={pending.Id}"));Assert.Equal(PostgresErrorCodes.ObjectNotInPrerequisiteState,blocked.SqlState);await tx.RollbackAsync();second.ChangeTracker.Clear();
            }
            Assert.Equal(ContainerFileUploadStartResult.Started,await repo.StartedAsync(pending,clock.GetUtcNow(),default));
            foreach(var table in peers)
            {await using var tx=await second.Database.BeginTransactionAsync();var blocked=await Assert.ThrowsAsync<PostgresException>(()=>ClonePeer(table));Assert.Equal(PostgresErrorCodes.ObjectNotInPrerequisiteState,blocked.SqlState);await tx.RollbackAsync();second.ChangeTracker.Clear();}

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
            var rollback=Row(await Source());var failedFinish=await Pending(await Ready(await Source()));Assert.Equal(ContainerFileUploadStartResult.Started,await repo.StartedAsync(failedFinish,clock.GetUtcNow(),default));Assert.True(await repo.ReservedAsync(failedFinish,Receipt(failedFinish,"Reserved"),default));Assert.Equal(ContainerFileUploadStartResult.Started,await repo.ApplyingAsync(failedFinish,clock.GetUtcNow(),default));await second.Database.ExecuteSqlRawAsync("CREATE FUNCTION wdv_purge_audit_reject() RETURNS trigger LANGUAGE plpgsql AS $f$ BEGIN IF NEW.action LIKE 'containers.files.recycle-purge.%' THEN RAISE EXCEPTION 'test audit fault'; END IF; RETURN NEW; END $f$; CREATE TRIGGER wdv_purge_audit_reject BEFORE INSERT ON audit_events FOR EACH ROW EXECUTE FUNCTION wdv_purge_audit_reject();");
            try{await Assert.ThrowsAsync<DbUpdateException>(()=>repo.EnqueueAsync(rollback,default));Assert.False(await second.OperationJobs.AnyAsync(j=>j.Id==rollback.Id));Assert.False(await second.ContainerRecyclePurgeIntents.AnyAsync(j=>j.Id==rollback.Id));await Assert.ThrowsAsync<DbUpdateException>(()=>repo.FinishAsync(failedFinish,"Succeeded","CONTAINER_RECYCLE_PURGE_COMPLETED",Receipt(failedFinish,"Completed"),default));var stillApplying=(await other.FindAsync(failedFinish.Id,default))!;Assert.Equal("Applying",stillApplying.State);Assert.True(stillApplying.PublishBarrier);Assert.Null(stillApplying.ReceiptJson);}
            finally{await second.Database.ExecuteSqlRawAsync("DROP TRIGGER wdv_purge_audit_reject ON audit_events; DROP FUNCTION wdv_purge_audit_reject();");}
            Assert.True(await repo.FinishAsync(failedFinish,"Succeeded","CONTAINER_RECYCLE_PURGE_COMPLETED",Receipt(failedFinish,"Completed"),default));
            var migrations=db.Database.GetMigrations().ToArray();Assert.EndsWith("_ContainerRecyclePurgeIntents",migrations[^1],StringComparison.Ordinal);var down=await Assert.ThrowsAsync<PostgresException>(()=>db.GetService<IMigrator>().MigrateAsync(migrations[^2]));Assert.Equal("container_recycle_purge_downgrade_requires_empty_table",down.MessageText);
            Assert.False(await db.ContainerRecyclePurgeIntents.AnyAsync(r=>r.CompletedAtUtc==null||r.PublishBarrier||r.LeaseToken!=null));Assert.False(await db.OutboxMessages.AnyAsync(m=>(m.MessageType=="ContainerRecyclePurgeRequested"||m.MessageType=="ContainerRecyclePurgeCancellationRequested")&&m.ProcessedAtUtc==null));
            Console.WriteLine("INBOX_PHASE=container-recycle-purge-two-phase-source-locked-fifteen-fences-nullable-audited");
        }
        finally{await second.ContainerFileUploadOperations.Where(x=>uploadIds.Contains(x.Id)).ExecuteDeleteAsync();await second.ContainerDirectoryOperations.Where(x=>directoryIds.Contains(x.Id)).ExecuteDeleteAsync();File.Delete(keyPath);}
    }
}
