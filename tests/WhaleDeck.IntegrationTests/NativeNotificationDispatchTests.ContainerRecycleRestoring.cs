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
    private static async Task ContainerRecycleRestoreBoundaries(DbContextOptions<PlatformDbContext> options)
    {
        await using var db=new PlatformDbContext(options);await using var second=new PlatformDbContext(options);
        var keyPath=Path.GetTempFileName();await File.WriteAllBytesAsync(keyPath,RandomNumberGenerator.GetBytes(32));
        var protector=new FilePlanProtector(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Agent:CapabilityKeyFile"]=keyPath}).Build());
        var clock=new RecycleClock();var sourceRepo=new ContainerRecycleRepository(db,protector,clock);var repo=new ContainerRecycleRestoreRepository(db,protector,clock,protector);var other=new ContainerRecycleRestoreRepository(second,protector,clock,protector);
        var actor=new TerminalActor("native-independent-restore-admin",null,[],new('A',64),"native-independent-restore-trace",null);
        async Task<ContainerRecycleIntent> Source()
        {
            var key=Guid.NewGuid();var now=ContainerRecycleWorkflowValidation.Time(clock.GetUtcNow());var id=ContainerRecycleWorkflowValidation.IntentId(actor.Subject,actor.LoginBinding,key);
            var command=new ContainerRecycleCommandDto(new(new('a',64),"777:2026-11-05T01:02:03.123456789Z","work/independent-restore-"+id.ToString("N")),new('B',64));
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
        ContainerRecycleRestoreIntent Row(ContainerRecycleIntent original, string? proofOverride=null)
        {
            var source=ContainerRecycleRestoreSourceResolver.FromRow(original,protector,protector);var key=Guid.NewGuid();var now=ContainerRecycleRestoreWorkflowValidation.Time(clock.GetUtcNow());var id=ContainerRecycleRestoreWorkflowValidation.IntentId(actor.Subject,actor.LoginBinding,key);
            var binding=new ContainerRecycleRestoreRequestBinding(id,key,actor.Subject,actor.LoginBinding,actor.TraceId,ContainerRecycleRestoreWorkflowValidation.Owner(actor.Subject,actor.LoginBinding),source.IntentId,proofOverride??source.ProofHash,source.Command,now,now.AddMinutes(15));
            return new(){Id=id,IdempotencyKey=key,ActorSubject=actor.Subject,LoginBinding=actor.LoginBinding,TraceId=actor.TraceId,OwnerHash=binding.Owner,SourceIntentId=source.IntentId,SourceProofHash=binding.SourceProofHash,ContainerId=original.ContainerId,InstanceId=original.InstanceId,RelativePath=original.RelativePath,
                CommandJson=ContainerRecycleRestoreWorkflowValidation.Serialize(binding.Command),RequestHash=protector.FingerprintRecycleRestoreRequest(binding),CreatedAtUtc=now,PlanningExpiresAtUtc=binding.PlanningExpiresAtUtc,NextAttemptAtUtc=now};
        }
        ContainerRecycleRestorePlanDto Plan(ContainerRecycleRestoreIntent row)
        {var c=ContainerRecycleRestoreWorkflowValidation.ReadCommand(row);var p=c.Source.Plan;return new(1,c,p.TargetProof,p.ParentIdentity,p.ParentVersion,p.OwnerUid,p.OwnerGid,new(2049,66),p.SourceIdentity,p.SourceChangedAtUnixNs+100,new('9',64));}
        ContainerRecycleRestoreConfirmation Claims(ContainerRecycleRestoreIntent row)=>new(row.Id,row.ActorSubject,row.LoginBinding,row.PlanHash!,row.ReadyExpiresAtUtc!.Value);
        ContainerRecycleRestoreReceiptDto Receipt(ContainerRecycleRestoreIntent row,string state)
        {var p=Plan(row);return new(row.ExecutionJobId!.Value,p.Command.Source.OperationId,p.Fingerprint,p.TargetProof,state,state=="Completed",false,state!="Aborted",state!="Aborted"?p.Command.HistoryDigest:"",state!="Aborted",state=="Completed"?"Restored":null,state=="Completed"?false:null,state=="Completed"?true:null,state=="Unknown"?null:state=="Completed");}
        async Task<ContainerRecycleRestoreIntent> Ready(ContainerRecycleIntent original)
        {
            var row=Row(original);await repo.EnqueueAsync(row,default);var claimed=(await repo.ClaimAsync(clock.GetUtcNow(),default))!;Assert.Equal(row.Id,claimed.Id);
            Assert.True(await repo.MarkPlanningAsync(claimed,default));await repo.FinishPlanningAsync(claimed,Plan(row),"CONTAINER_RECYCLE_RESTORE_READY",default);return (await repo.FindAsync(row.Id,default))!;
        }
        async Task<ContainerRecycleRestoreIntent> Pending(ContainerRecycleRestoreIntent ready)
        {await repo.ConfirmAsync(actor,Claims(ready),ready.Version,default);var claimed=(await repo.ClaimAsync(clock.GetUtcNow(),default))!;Assert.Equal(ready.Id,claimed.Id);return claimed;}
        async Task Rejected(FormattableString sql,string code)
        {var error=await Assert.ThrowsAsync<PostgresException>(()=>second.Database.ExecuteSqlInterpolatedAsync(sql));Assert.Equal(code,error.MessageText);}
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
        var peers=RecycleFenceTables.Concat(["container_recycle_intents"]).ToArray();
        async Task ClonePeer(string table)
        {
            Assert.Contains(table,peers);var seedSql = table switch {
                "container_file_upload_operations" => "SELECT id AS \"Value\" FROM container_file_upload_operations LIMIT 1",
                "container_directory_operations" => "SELECT id AS \"Value\" FROM container_directory_operations LIMIT 1",
                "container_copy_intents" => "SELECT id AS \"Value\" FROM container_copy_intents WHERE completed_at_utc IS NOT NULL LIMIT 1",
                "container_move_intents" => "SELECT id AS \"Value\" FROM container_move_intents WHERE completed_at_utc IS NOT NULL LIMIT 1",
                "container_permission_intents" => "SELECT id AS \"Value\" FROM container_permission_intents WHERE completed_at_utc IS NOT NULL LIMIT 1",
                "container_ownership_intents" => "SELECT id AS \"Value\" FROM container_ownership_intents WHERE completed_at_utc IS NOT NULL LIMIT 1",
                "container_recursive_permission_intents" => "SELECT id AS \"Value\" FROM container_recursive_permission_intents WHERE completed_at_utc IS NOT NULL LIMIT 1",
                "container_recursive_ownership_intents" => "SELECT id AS \"Value\" FROM container_recursive_ownership_intents WHERE completed_at_utc IS NOT NULL LIMIT 1",
                "container_acl_intents" => "SELECT id AS \"Value\" FROM container_acl_intents WHERE completed_at_utc IS NOT NULL LIMIT 1",
                "container_recursive_acl_intents" => "SELECT id AS \"Value\" FROM container_recursive_acl_intents WHERE completed_at_utc IS NOT NULL LIMIT 1",
                "container_recursive_acl_restore_intents" => "SELECT id AS \"Value\" FROM container_recursive_acl_restore_intents WHERE completed_at_utc IS NOT NULL LIMIT 1",
                "container_recycle_intents" => "SELECT id AS \"Value\" FROM container_recycle_intents WHERE completed_at_utc IS NOT NULL LIMIT 1",
                _ => throw new InvalidOperationException("unknown fixed fence table") };
            var seed=await second.Database.SqlQueryRaw<Guid>(seedSql).SingleAsync();
            var id=Guid.NewGuid();var execution=Guid.NewGuid();var key=Guid.NewGuid();var now=clock.GetUtcNow();
            second.OperationJobs.AddRange(new OperationJob{Id=id,ActorSubject="native-restore-fence",IdempotencyKey=key.ToString("D"),JobType="native.restore.fence"},new OperationJob{Id=execution,ActorSubject="native-restore-fence",IdempotencyKey=execution.ToString("D"),JobType="native.restore.fence"});await second.SaveChangesAsync();
            var patch=" || jsonb_build_object('receipt_json',NULL,'receipt_hash',NULL,'acl_confirmed',NULL,'root_state',NULL,'source_retained',NULL,'source_restored',NULL,'destination_published',NULL,'source_removed',NULL,'completed_count',NULL,'changed_count',NULL,'aborted_count',NULL,'not_started_count',NULL,'unknown_count',NULL,'outcome_digest',NULL,'binding_current',NULL,'history_available',NULL,'history_may_exist',FALSE,'history_verified',FALSE,'history_digest',NULL)";
            if(table!="container_file_upload_operations" && table!="container_directory_operations")patch+=" || jsonb_build_object('execution_job_id',@execution)";
            var sql="INSERT INTO "+table+" SELECT (jsonb_populate_record(NULL::"+table+",to_jsonb(t) || jsonb_build_object('id',@id,'idempotency_key',@key,'completed_at_utc',NULL,'lease_token',NULL,'lease_expires_at_utc',NULL,'publish_barrier',TRUE,'root_started_at_utc',@now,'state',@state,'published_version',NULL,'acknowledged_by',NULL,'acknowledgement_json',NULL)"+patch+")).* FROM "+table+" t WHERE id=@seed";
            Assert.Equal(1,await second.Database.ExecuteSqlRawAsync(sql,new NpgsqlParameter("id",id),new NpgsqlParameter("key",key),new NpgsqlParameter("execution",execution),new NpgsqlParameter("now",now),new NpgsqlParameter("state",table=="container_file_upload_operations"?"Ready":"Pending"),new NpgsqlParameter("seed",seed)));
        }
        try
        {
            Assert.Equal(19,await db.Database.SqlQueryRaw<int>("SELECT count(*)::integer AS \"Value\" FROM pg_constraint WHERE conrelid='container_recycle_restore_intents'::regclass AND contype='c'").SingleAsync());
            Assert.Equal(14,await db.Database.SqlQueryRaw<int>("SELECT count(*)::integer AS \"Value\" FROM pg_trigger t JOIN pg_proc p ON p.oid=t.tgfoid WHERE p.proname='whaledeck_container_recycle_restore_fence' AND NOT t.tgisinternal").SingleAsync());
            await PeerSeeds();
            var source=await Source();var sourceBefore=JsonSerializer.Serialize(source);var ready=await Ready(source);Assert.Null(await other.ClaimAsync(clock.GetUtcNow(),default));
            await Assert.ThrowsAsync<InvalidOperationException>(()=>repo.ConfirmAsync(actor,Claims(ready),ready.Version+1,default));
            await using(var tx=await second.Database.BeginTransactionAsync())
            {await second.Database.ExecuteSqlInterpolatedAsync($"SELECT * FROM container_recycle_intents WHERE id={source.Id} FOR UPDATE");var error=await Assert.ThrowsAnyAsync<Exception>(()=>repo.ConfirmAsync(actor,Claims(ready),ready.Version,default));Assert.Equal(PostgresErrorCodes.LockNotAvailable,Assert.IsType<PostgresException>(error.GetBaseException()).SqlState);await tx.RollbackAsync();}
            Assert.Null((await other.FindAsync(ready.Id,default))!.ExecutionJobId);
            var pending=await Pending(ready);
            foreach(var table in peers)
            {
                Console.WriteLine("INBOX_PHASE=restore-peer-inverse-"+table.Replace('_','-'));
                await using var tx=await second.Database.BeginTransactionAsync();await ClonePeer(table);
                await Rejected($"UPDATE container_recycle_restore_intents SET publish_barrier=true,root_started_at_utc={clock.GetUtcNow()},state='Applying' WHERE id={pending.Id}","CONTAINER_RECYCLE_RESTORE_TARGET_BUSY");await tx.RollbackAsync();second.ChangeTracker.Clear();
            }
            var hash=FileManagementService.Hash("independent-restore-host-lock:"+pending.Id);
            await second.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO file_path_locks(path_hash,operation_id,scope_kind) VALUES ({hash},{pending.Id},'Legacy')");
            try{Assert.Equal(ContainerFileUploadStartResult.Busy,await repo.StartedAsync(pending,clock.GetUtcNow(),default));}finally{await second.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM file_path_locks WHERE path_hash={hash}");}
            await using(var tx=await second.Database.BeginTransactionAsync())
            {await second.Database.ExecuteSqlInterpolatedAsync($"SELECT * FROM container_recycle_intents WHERE id={source.Id} FOR UPDATE");var error=await Assert.ThrowsAnyAsync<Exception>(()=>repo.StartedAsync(pending,clock.GetUtcNow(),default));Assert.Equal(PostgresErrorCodes.LockNotAvailable,Assert.IsType<PostgresException>(error.GetBaseException()).SqlState);await tx.RollbackAsync();}
            Assert.Null((await other.FindAsync(pending.Id,default))!.RootStartedAtUtc);Assert.Equal(ContainerFileUploadStartResult.Started,await repo.StartedAsync(pending,clock.GetUtcNow(),default));
            foreach(var table in peers)
            {await using var tx=await second.Database.BeginTransactionAsync();var error=await Assert.ThrowsAsync<PostgresException>(()=>ClonePeer(table));Assert.Equal(PostgresErrorCodes.ObjectNotInPrerequisiteState,error.SqlState);await tx.RollbackAsync();second.ChangeTracker.Clear();}
            await Rejected($"INSERT INTO file_path_locks(path_hash,operation_id,scope_kind) VALUES ({hash},{pending.Id},'Legacy')","FILES_TARGET_BUSY");
            var unknown=Receipt(pending,"Unknown");Assert.True(await repo.FinishAsync(pending,"Unknown","CONTAINER_RECYCLE_RESTORE_RESULT_UNKNOWN",unknown,default));
            var held=(await other.FindAsync(pending.Id,default))!;Assert.True(held.PublishBarrier);Assert.Null(held.SourceRetained);Assert.Null(held.SourceRestored);Assert.Null(held.RestoredByThisOperation);Assert.Null(await repo.ClaimAsync(clock.GetUtcNow(),default));
            await Rejected($"UPDATE container_recycle_restore_intents SET source_proof_hash={new string('F',64)} WHERE id={held.Id}","container_recycle_restore_request_immutable");
            await Rejected($"UPDATE container_recycle_restore_intents SET restored_by_this_operation=false WHERE id={held.Id}","container_recycle_restore_fact_immutable");
            await Rejected($"DELETE FROM container_recycle_restore_intents WHERE id={held.Id}","container_recycle_restore_permanent_intent");
            var admin=actor with{Subject="another-native-restore-admin",LoginBinding=new('9',64)};await repo.AcknowledgeAsync(admin,held.Id,held.Version,unknown,default);
            var acknowledged=(await other.FindAsync(held.Id,default))!;Assert.False(acknowledged.PublishBarrier);Assert.Null(acknowledged.RestoredByThisOperation);Assert.Equal("Unknown",acknowledged.RootState);
            var proofSource=await Source();var forged=Row(proofSource,new('F',64));
            Assert.Equal("CONTAINER_RECYCLE_RESTORE_SOURCE_CHANGED",(await Assert.ThrowsAsync<InvalidOperationException>(()=>repo.EnqueueAsync(forged,default))).Message);
            Assert.False(await second.OperationJobs.AnyAsync(j=>j.Id==forged.Id));
            var canceledReady=await Ready(await Source());await repo.CancelAsync(actor,canceledReady.Id,default);Assert.Null((await other.FindAsync(canceledReady.Id,default))!.ExecutionJobId);
            var canceled=await Pending(await Ready(await Source()));await repo.CancelAsync(actor,canceled.Id,default);
            Assert.True(await repo.FinishAsync(canceled,"Canceled","CONTAINER_RECYCLE_RESTORE_CANCELED",Receipt(canceled,"Aborted"),default));var canceledResult=(await other.FindAsync(canceled.Id,default))!;
            Assert.False(canceledResult.RestoredByThisOperation);Assert.Null(canceledResult.SourceRetained);Assert.False(canceledResult.HistoryVerified);
            var rejectSource=await Source();
            var normal=await Pending(await Ready(await Source()));Assert.Equal(ContainerFileUploadStartResult.Started,await repo.StartedAsync(normal,clock.GetUtcNow(),default));await repo.DeferAsync(normal,default);clock.Offset+=TimeSpan.FromSeconds(7);
            var reclaimed=(await other.ClaimAsync(clock.GetUtcNow(),default))!;Assert.Equal(normal.Id,reclaimed.Id);Assert.NotEqual(normal.LeaseToken,reclaimed.LeaseToken);Assert.False(await repo.RenewAsync(normal,clock.GetUtcNow(),default));
            Assert.False(await repo.FinishAsync(normal,"Succeeded","CONTAINER_RECYCLE_RESTORE_COMPLETED",Receipt(normal,"Completed"),default));
            var rejected=Row(rejectSource);
            await second.Database.ExecuteSqlRawAsync("CREATE FUNCTION wdv_recycle_restore_audit_reject() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.action LIKE 'containers.files.recycle-restore.%' THEN RAISE EXCEPTION 'wdv_recycle_restore_audit_reject'; END IF; RETURN NEW; END $$; CREATE TRIGGER wdv_recycle_restore_audit_reject BEFORE INSERT ON audit_events FOR EACH ROW EXECUTE FUNCTION wdv_recycle_restore_audit_reject();");
            try
            {
                await Assert.ThrowsAsync<DbUpdateException>(()=>repo.EnqueueAsync(rejected,default));
                await Assert.ThrowsAsync<DbUpdateException>(()=>other.FinishAsync(reclaimed,"Succeeded","CONTAINER_RECYCLE_RESTORE_COMPLETED",Receipt(reclaimed,"Completed"),default));
                Assert.False(await db.OperationJobs.AnyAsync(j=>j.Id==rejected.Id));Assert.Equal("Applying",(await repo.FindAsync(reclaimed.Id,default))!.State);
            }
            finally{await second.Database.ExecuteSqlRawAsync("DROP TRIGGER wdv_recycle_restore_audit_reject ON audit_events; DROP FUNCTION wdv_recycle_restore_audit_reject();");}
            Assert.True(await other.FinishAsync(reclaimed,"Succeeded","CONTAINER_RECYCLE_RESTORE_COMPLETED",Receipt(reclaimed,"Completed"),default));
            var finished=(await repo.FindAsync(normal.Id,default))!;Assert.True(finished.RestoredByThisOperation);Assert.Equal("Restored",finished.SourceState);Assert.True(ContainerRecycleRestoreWorkflowValidation.HasValidBinding(finished,protector));
            Assert.Equal(sourceBefore,JsonSerializer.Serialize(await sourceRepo.FindAsync(source.Id,default)));
            var migration=db.Database.GetMigrations().Last();Assert.EndsWith("_ContainerRecycleRestoreIntents",migration,StringComparison.Ordinal);
            var migrations=db.Database.GetMigrations().ToArray();var errorDown=await Assert.ThrowsAsync<PostgresException>(()=>db.GetService<IMigrator>().MigrateAsync(migrations[^2]));Assert.Equal("container_recycle_restore_downgrade_requires_empty_table",errorDown.MessageText);
            Assert.False(await db.ContainerRecycleRestoreIntents.AnyAsync(r=>r.CompletedAtUtc==null||r.PublishBarrier||r.LeaseToken!=null));
            Assert.False(await db.OutboxMessages.AnyAsync(m=>(m.MessageType=="ContainerRecycleRestoreRequested"||m.MessageType=="ContainerRecycleRestoreCancellationRequested")&&m.ProcessedAtUtc==null));
            Assert.True(await db.AuditEvents.AnyAsync(a=>a.Action=="containers.files.recycle-restore.unknown-acknowledged"&&a.ActorSubject==admin.Subject));
            Console.WriteLine("INBOX_PHASE=container-recycle-restore-source-locked-persistent-nullable-unknown-fourteen-fences-audited");
        }
        finally{await second.ContainerFileUploadOperations.Where(r=>uploadIds.Contains(r.Id)).ExecuteDeleteAsync();await second.ContainerDirectoryOperations.Where(r=>directoryIds.Contains(r.Id)).ExecuteDeleteAsync();File.Delete(keyPath);}
    }
}
