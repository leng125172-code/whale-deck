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
    // Real isolated PostgreSQL transactions and signed synthetic provenance, without Agent/Root or UI calls.
    private static async Task ContainerRecycleArchiveBoundaries(DbContextOptions<PlatformDbContext> options)
    {
        await using var db=new PlatformDbContext(options);await using var second=new PlatformDbContext(options);
        var keyPath=Path.GetTempFileName();await File.WriteAllBytesAsync(keyPath,RandomNumberGenerator.GetBytes(32));
        var protector=new FilePlanProtector(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Agent:CapabilityKeyFile"]=keyPath}).Build());
        var clock=new RecycleClock();var sourceRepo=new ContainerRecycleRepository(db,protector,clock);var purgeRepo=new ContainerRecyclePurgeRepository(db,protector,clock,protector);
        var repo=new ContainerRecycleArchiveRepository(db,protector,clock,protector,protector);var other=new ContainerRecycleArchiveRepository(second,protector,clock,protector,protector);
        var actor=new TerminalActor("native-archive-admin",null,[],new('A',64),"native-archive-trace",null);
        async Task<ContainerRecycleIntent> Original(string? containerId = null)
        {
            var key=Guid.NewGuid();var now=ContainerRecycleWorkflowValidation.Time(clock.GetUtcNow());var id=ContainerRecycleWorkflowValidation.IntentId(actor.Subject,actor.LoginBinding,key);
            var command=new ContainerRecycleCommandDto(new(containerId ?? new('a',64),"777:2026-11-05T01:02:03.123456789Z","work/independent-purge-"+id.ToString("N")),new('B',64));
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
        ContainerRecyclePurgeIntent PurgeRow(ContainerRecycleIntent original,string? sourceProof=null)
        {
            var src=ContainerRecyclePurgeSourceResolver.FromRow(original,protector,protector);var key=Guid.NewGuid();var now=ContainerRecyclePurgeWorkflowValidation.Time(clock.GetUtcNow());var id=ContainerRecyclePurgeWorkflowValidation.IntentId(actor.Subject,actor.LoginBinding,key);
            var b=new ContainerRecyclePurgeRequestBinding(id,key,actor.Subject,actor.LoginBinding,actor.TraceId,ContainerRecyclePurgeWorkflowValidation.Owner(actor.Subject,actor.LoginBinding),src.IntentId,sourceProof??src.ProofHash,src.Command,now,now.AddMinutes(15));
            return new(){Id=id,SourceIntentId=src.IntentId,SourceProofHash=b.SourceProofHash,IdempotencyKey=key,ActorSubject=actor.Subject,LoginBinding=actor.LoginBinding,TraceId=actor.TraceId,OwnerHash=b.Owner,ContainerId=src.Command.Source.Plan.Command.Location.ContainerId,InstanceId=src.Command.Source.Plan.Command.Location.InstanceId,RelativePath=src.Command.Source.Plan.Command.Location.Path,CommandJson=ContainerRecyclePurgeWorkflowValidation.Serialize(src.Command),RequestHash=protector.FingerprintRecyclePurgeRequest(b),CreatedAtUtc=now,PlanningExpiresAtUtc=b.PlanningExpiresAtUtc,NextAttemptAtUtc=now};
        }
        ContainerRecyclePurgePlanDto PurgePlan(ContainerRecyclePurgeIntent r, long capturedAt)
        {var c=ContainerRecyclePurgeWorkflowValidation.ReadCommand(r);var p=c.Source.Plan;return new(1,c,p.TargetProof,p.ParentIdentity,p.ParentVersion,p.OwnerUid,p.OwnerGid,new(2049,66),p.SourceIdentity,p.SourceChangedAtUnixNs+100,new('1',64),new('2',64),capturedAt,new('3',64),new(2049,67),new(2049,68,33152,0,0,1,p.SourceIdentity.SizeBytes,10),11,p.SourceIdentity.SizeBytes,p.Sha256,new('9',64));}
        ContainerRecyclePurgeReceiptDto PurgeReceipt(ContainerRecyclePurgeIntent r,string state)=>new(r.ExecutionJobId!.Value,ContainerRecyclePurgeWorkflowValidation.ReadCommand(r).Source.OperationId,ContainerRecyclePurgeWorkflowValidation.ReadPlan(r).Fingerprint,state,state!="Aborted",state!="Aborted",state=="Unknown"?null:state=="Completed");
        ContainerRecyclePurgeConfirmation PurgeClaims(ContainerRecyclePurgeIntent r)=>new(r.Id,r.ActorSubject,r.LoginBinding,r.PlanHash!,r.ReadyExpiresAtUtc!.Value);

        async Task<(ContainerRecyclePurgeIntent Purge,ContainerRecycleIntent Original)> Source(string? containerId=null, long capturedAt=1)
        {
            var original=await Original(containerId);var row=PurgeRow(original);await purgeRepo.EnqueueAsync(row,default);
            var claimed=(await purgeRepo.ClaimAsync(clock.GetUtcNow(),default))!;Assert.Equal(row.Id,claimed.Id);Assert.True(await purgeRepo.MarkPlanningAsync(claimed,default));
            await purgeRepo.FinishPlanningAsync(claimed,PurgePlan(row,capturedAt),"CONTAINER_RECYCLE_PURGE_READY",default);row=(await purgeRepo.FindAsync(row.Id,default))!;
            await purgeRepo.ConfirmAsync(actor,PurgeClaims(row),row.Version,default);claimed=(await purgeRepo.ClaimAsync(clock.GetUtcNow(),default))!;
            Assert.Equal(ContainerFileUploadStartResult.Started,await purgeRepo.StartedAsync(claimed,clock.GetUtcNow(),default));
            Assert.True(await purgeRepo.ReservedAsync(claimed,PurgeReceipt(claimed,"Reserved"),default));Assert.Equal(ContainerFileUploadStartResult.Started,await purgeRepo.ApplyingAsync(claimed,clock.GetUtcNow(),default));
            Assert.True(await purgeRepo.FinishAsync(claimed,"Succeeded","CONTAINER_RECYCLE_PURGE_COMPLETED",PurgeReceipt(claimed,"Completed"),default));
            return ((await purgeRepo.FindAsync(row.Id,default))!,original);
        }
        ContainerRecycleArchiveIntent Row((ContainerRecyclePurgeIntent Purge,ContainerRecycleIntent Original) src,string? proof=null)
        {
            var resolved=ContainerRecycleArchiveSourceResolver.FromRows(src.Purge,src.Original,protector,protector,protector);var key=Guid.NewGuid();var now=ContainerRecycleArchiveWorkflowValidation.Time(clock.GetUtcNow());
            var id=ContainerRecycleArchiveWorkflowValidation.IntentId(actor.Subject,actor.LoginBinding,key);
            var binding=new ContainerRecycleArchiveRequestBinding(id,key,actor.Subject,actor.LoginBinding,actor.TraceId,ContainerRecycleArchiveWorkflowValidation.Owner(actor.Subject,actor.LoginBinding),resolved.IntentId,proof??resolved.ProofHash,resolved.Command,now,now.AddMinutes(15));
            return new(){Id=id,SourceIntentId=resolved.IntentId,SourceProofHash=binding.SourceProofHash,IdempotencyKey=key,ActorSubject=actor.Subject,LoginBinding=actor.LoginBinding,TraceId=actor.TraceId,OwnerHash=binding.Owner,ContainerId=src.Purge.ContainerId,InstanceId=src.Purge.InstanceId,RelativePath=src.Purge.RelativePath,
                CommandJson=ContainerRecycleArchiveWorkflowValidation.Serialize(binding.Command),RequestHash=protector.FingerprintRecycleArchiveRequest(binding),CreatedAtUtc=now,PlanningExpiresAtUtc=binding.PlanningExpiresAtUtc,NextAttemptAtUtc=now};
        }
        ContainerRecycleArchivePlanDto Plan(ContainerRecycleArchiveIntent row)
        {
            var c=ContainerRecycleArchiveWorkflowValidation.ReadCommand(row);var p=c.Purge.Plan;
            return new(1,c,p.TargetProof,p.ParentIdentity,new('A',64),p.OwnerUid,p.OwnerGid,null,p.RetainedIdentity,p.RetainedChangedAtUnixNs,new('1',64),new('2',64),new('3',64),new('4',64),p.StorageHash,p.StoreIdentity,p.ArchiveIdentity,p.ArchiveChangedAtUnixNs,p.ArchiveBytes,p.ArchiveSha256,new('5',64));
        }
        ContainerRecycleArchiveReceiptDto Receipt(ContainerRecycleArchiveIntent row,string state,bool? current=null)
        {
            var op=ContainerRecycleArchiveWorkflowValidation.Reference(row);var p=op.Plan;var i=p.Command.Purge.Plan.Command.Source.Plan.SourceIdentity;
            return new(op.Id,p.Command.Purge.Plan.Command.Source.OperationId,p.Command.Purge.OperationId,p.Fingerprint,state,state!="Aborted",state!="Aborted",state=="Unknown"?null:state=="Completed",state=="Completed"?current??true:null,state=="Completed"?i with{Inode=999}:null,state=="Completed"?99L:null);
        }
        ContainerRecycleArchiveConfirmation Claims(ContainerRecycleArchiveIntent row)=>new(row.Id,row.ActorSubject,row.LoginBinding,row.PlanHash!,row.ReadyExpiresAtUtc!.Value);
        var planningSnapshots=new Dictionary<Guid,string>();
        async Task JobFacts(ContainerRecycleArchiveIntent row,bool execution)
        {
            var id=execution?row.ExecutionJobId!.Value:row.Id;var job=await second.OperationJobs.AsNoTracking().SingleAsync(x=>x.Id==id);
            var projected=(await new OperationService(new JobRepository(second)).FindAsync(id,default))!;
            Assert.NotNull(projected.ResultJson);Assert.Equal(ContainerRecycleArchiveJobResults.ForJob(job),projected.ResultJson);
            using var doc=JsonDocument.Parse(projected.ResultJson);Assert.Equal(19,doc.RootElement.EnumerateObject().Count());
            var value=JsonSerializer.Deserialize<ContainerRecycleArchiveJobResults.Summary>(projected.ResultJson,JsonSerializerOptions.Web)!;
            var purge=(await purgeRepo.FindAsync(row.SourceIntentId,default))!;var command=ContainerRecycleArchiveWorkflowValidation.ReadCommand(row);
            Assert.Equal(row.Id,value.IntentId);Assert.Equal(row.SourceIntentId,value.SourceIntentId);Assert.Equal(purge.SourceIntentId,value.OriginalIntentId);
            Assert.Equal(command.Purge.OperationId,value.PurgeOperationId);Assert.Equal(command.Purge.Plan.Command.Source.OperationId,value.SourceOperationId);
            Assert.Equal(row.State,value.State);Assert.Equal(row.RootState,value.ReceiptState);Assert.Equal(row.ArchiveCopied,value.ArchiveCopied);Assert.Equal(row.BindingCurrent,value.BindingCurrent);
            Assert.Equal(row.SourceFenced,value.SourceFenced);Assert.Equal(row.OwnedFence,value.OwnedFence);Assert.Equal(row.PublishBarrier,value.PublishBarrier);
            Assert.Equal(row.RootStartedAtUtc is not null,value.ReservationStarted);Assert.Equal(row.ReservationJson is not null,value.ReservationRecorded);Assert.Equal(row.ApplyStartedAtUtc is not null,value.ApplyStarted);
            foreach(var secret in new[]{row.ActorSubject,row.LoginBinding,row.TraceId,row.OwnerHash,row.ContainerId,row.InstanceId,row.RelativePath,row.SourceProofHash,row.RequestHash,row.PlanHash,row.ExecutionHash,row.ReservationHash,row.ReceiptHash,command.HistoryDigest,command.Purge.Plan.ArchiveSha256})if(secret is not null)Assert.DoesNotContain(secret,projected.ResultJson,StringComparison.Ordinal);
            if(execution)Assert.Equal(planningSnapshots[row.Id],(await second.OperationJobs.AsNoTracking().SingleAsync(x=>x.Id==row.Id)).ResultJson);
        }
        async Task<ContainerRecycleArchiveIntent> Ready((ContainerRecyclePurgeIntent Purge,ContainerRecycleIntent Original) src)
        {
            var row=Row(src);await repo.EnqueueAsync(row,default);var claimed=(await repo.ClaimAsync(clock.GetUtcNow(),default))!;Assert.Equal(row.Id,claimed.Id);
            Assert.True(await repo.MarkPlanningAsync(claimed,default));await repo.FinishPlanningAsync(claimed,Plan(row),"CONTAINER_RECYCLE_ARCHIVE_READY",default);var ready=(await other.FindAsync(row.Id,default))!;await JobFacts(ready,false);planningSnapshots[row.Id]=(await second.OperationJobs.AsNoTracking().SingleAsync(j=>j.Id==row.Id)).ResultJson!;return ready;
        }
        async Task<ContainerRecycleArchiveIntent> Pending(ContainerRecycleArchiveIntent row)
        {await repo.ConfirmAsync(actor,Claims(row),row.Version,default);return (await repo.ClaimAsync(clock.GetUtcNow(),default))!;}
        async Task ReserveAndApply(ContainerRecycleArchiveIntent row)
        {
            Assert.Equal(ContainerFileUploadStartResult.Started,await repo.StartedAsync(row,clock.GetUtcNow(),default));await JobFacts((await other.FindAsync(row.Id,default))!,true);Assert.True(await repo.ReservedAsync(row,Receipt(row,"Reserved"),default));
            await JobFacts((await other.FindAsync(row.Id,default))!,true);Assert.Equal(ContainerFileUploadStartResult.Started,await repo.ApplyingAsync(row,clock.GetUtcNow(),default));await JobFacts((await other.FindAsync(row.Id,default))!,true);
        }
        var uploadIds=new List<Guid>();var directoryIds=new List<Guid>(); Guid purgePeerSourceId=Guid.Empty;
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
        var peers=RecycleFenceTables.Concat(["container_recycle_intents","container_recycle_restore_intents","container_recycle_purge_intents"]).ToArray();
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
                "container_recycle_purge_intents" => "SELECT id AS \"Value\" FROM container_recycle_purge_intents WHERE completed_at_utc IS NOT NULL AND root_state='Completed' LIMIT 1",
                "container_recycle_restore_intents" => "SELECT id AS \"Value\" FROM container_recycle_restore_intents WHERE completed_at_utc IS NOT NULL AND execution_job_id IS NOT NULL AND plan_json IS NOT NULL LIMIT 1",
                _ => throw new InvalidOperationException("unknown fixed fence table") };
            var seed=await second.Database.SqlQueryRaw<Guid>(seedSql).SingleAsync();
            var id=Guid.NewGuid();var execution=Guid.NewGuid();var key=Guid.NewGuid();var now=clock.GetUtcNow();
            second.OperationJobs.AddRange(new OperationJob{Id=id,ActorSubject="native-restore-fence",IdempotencyKey=key.ToString("D"),JobType="native.restore.fence"},new OperationJob{Id=execution,ActorSubject="native-restore-fence",IdempotencyKey=execution.ToString("D"),JobType="native.restore.fence"});await second.SaveChangesAsync();
            var patch=" || jsonb_build_object('source_state',NULL,'restored_by_this_operation',NULL,'receipt_json',NULL,'receipt_hash',NULL,'acl_confirmed',NULL,'root_state',NULL,'source_retained',NULL,'source_restored',NULL,'destination_published',NULL,'source_removed',NULL,'completed_count',NULL,'changed_count',NULL,'aborted_count',NULL,'not_started_count',NULL,'unknown_count',NULL,'outcome_digest',NULL,'binding_current',NULL,'history_available',NULL,'history_may_exist',FALSE,'history_verified',FALSE,'history_digest',NULL,'purge_applied',NULL,'source_fenced',NULL,'owned_fence',NULL,'reservation_json',NULL,'reservation_hash',NULL,'apply_started_at_utc',NULL)";
            if(table=="container_recycle_purge_intents")patch+=" || jsonb_build_object('source_intent_id',@purgeSource)";
            if(table!="container_file_upload_operations" && table!="container_directory_operations")patch+=" || jsonb_build_object('execution_job_id',@execution)";
            var sql="INSERT INTO "+table+" SELECT (jsonb_populate_record(NULL::"+table+",to_jsonb(t) || jsonb_build_object('id',@id,'idempotency_key',@key,'completed_at_utc',NULL,'lease_token',NULL,'lease_expires_at_utc',NULL,'publish_barrier',TRUE,'root_started_at_utc',@now,'state',@state,'published_version',NULL,'acknowledged_by',NULL,'acknowledgement_json',NULL)"+patch+")).* FROM "+table+" t WHERE id=@seed";
            Assert.Equal(1,await second.Database.ExecuteSqlRawAsync(sql,new NpgsqlParameter("purgeSource",purgePeerSourceId),new NpgsqlParameter("id",id),new NpgsqlParameter("key",key),new NpgsqlParameter("execution",execution),new NpgsqlParameter("now",now),new NpgsqlParameter("state",table=="container_file_upload_operations"?"Ready":table=="container_recycle_purge_intents"?"Reserving":"Pending"),new NpgsqlParameter("seed",seed)));
        }

        try
        {
            var catalog=new ContainerRecycleArchiveSourceCatalog(second);var catalogCid=new string('c',64);
            var catalogSources=new[]{await Source(catalogCid),await Source(catalogCid),await Source(catalogCid)};
            var page1=await catalog.ListAsync(catalogCid,2,null,default);Assert.Equal(2,page1.Count);
            var page2=await catalog.ListAsync(catalogCid,2,page1[^1].Purge.Id,default);Assert.Single(page2);
            Assert.Equal(3,page1.Concat(page2).Select(x=>x.Purge.Id).Distinct().Count());
            foreach(var item in page1.Concat(page2))Assert.NotNull(ContainerRecycleArchiveSourceResolver.FromRows(item.Purge,item.Original,protector,protector,protector));
            var sourceReady=await Ready(catalogSources[0]);Assert.DoesNotContain(await catalog.ListAsync(catalogCid,4,null,default),x=>x.Purge.Id==sourceReady.SourceIntentId);
            await repo.CancelAsync(actor,sourceReady.Id,default);Assert.Equal(3,(await catalog.ListAsync(catalogCid,4,null,default)).Count);
            Console.WriteLine("INBOX_PHASE=container-recycle-archive-source-catalog-uuid-pagination-active-exclusion-cancel-release");
            await PeerSeeds();purgePeerSourceId=(await Original(new('f',64))).Id;var src=await Source();var original=JsonSerializer.Serialize(src.Original);var purge=JsonSerializer.Serialize(src.Purge);
            var ready=await Ready(src);Assert.Null(await other.ClaimAsync(clock.GetUtcNow(),default));
            await Assert.ThrowsAsync<InvalidOperationException>(()=>repo.ConfirmAsync(actor,Claims(ready),ready.Version+1,default));
            foreach(var sourceTable in new[]{"container_recycle_intents","container_recycle_purge_intents"})
            {
                await using var tx=await second.Database.BeginTransactionAsync();var id=sourceTable=="container_recycle_intents"?src.Original.Id:src.Purge.Id;
                var sql=sourceTable=="container_recycle_intents"?"SELECT * FROM container_recycle_intents WHERE id=@id FOR UPDATE":"SELECT * FROM container_recycle_purge_intents WHERE id=@id FOR UPDATE";await second.Database.ExecuteSqlRawAsync(sql,new NpgsqlParameter("id",id));
                var error=await Assert.ThrowsAnyAsync<Exception>(()=>repo.ConfirmAsync(actor,Claims(ready),ready.Version,default));Assert.Equal(PostgresErrorCodes.LockNotAvailable,Assert.IsType<PostgresException>(error.GetBaseException()).SqlState);await tx.RollbackAsync();
            }
            var pending=await Pending(ready);Assert.Equal(ContainerFileUploadStartResult.LeaseLost,await repo.ApplyingAsync(pending,clock.GetUtcNow(),default));
            foreach(var table in peers)
            {
                await using var tx=await second.Database.BeginTransactionAsync();await ClonePeer(table);
                var error=await Assert.ThrowsAsync<PostgresException>(()=>second.Database.ExecuteSqlInterpolatedAsync($"UPDATE container_recycle_archive_intents SET publish_barrier=true,root_started_at_utc={clock.GetUtcNow()},state='Reserving' WHERE id={pending.Id}"));Assert.Equal(PostgresErrorCodes.ObjectNotInPrerequisiteState,error.SqlState);await tx.RollbackAsync();second.ChangeTracker.Clear();
            }
            Assert.Equal(ContainerFileUploadStartResult.Started,await repo.StartedAsync(pending,clock.GetUtcNow(),default));
            foreach(var table in peers)
            {await using var tx=await second.Database.BeginTransactionAsync();var error=await Assert.ThrowsAsync<PostgresException>(()=>ClonePeer(table));Assert.Equal(PostgresErrorCodes.ObjectNotInPrerequisiteState,error.SqlState);await tx.RollbackAsync();second.ChangeTracker.Clear();}
            var hostHash=FileManagementService.Hash("archive-host-lock:"+pending.Id);var hostError=await Assert.ThrowsAsync<PostgresException>(()=>second.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO file_path_locks(path_hash,operation_id,scope_kind) VALUES ({hostHash},{pending.Id},'Legacy')"));Assert.Equal(PostgresErrorCodes.ObjectNotInPrerequisiteState,hostError.SqlState);
            Assert.True(await repo.ReservedAsync(pending,Receipt(pending,"Reserved"),default));
            foreach(var sourceTable in new[]{"container_recycle_intents","container_recycle_purge_intents"})
            {
                await using var tx=await second.Database.BeginTransactionAsync();var id=sourceTable=="container_recycle_intents"?src.Original.Id:src.Purge.Id;
                var sql=sourceTable=="container_recycle_intents"?"SELECT * FROM container_recycle_intents WHERE id=@id FOR UPDATE":"SELECT * FROM container_recycle_purge_intents WHERE id=@id FOR UPDATE";await second.Database.ExecuteSqlRawAsync(sql,new NpgsqlParameter("id",id));
                var error=await Assert.ThrowsAnyAsync<Exception>(()=>repo.ApplyingAsync(pending,clock.GetUtcNow(),default));Assert.Equal(PostgresErrorCodes.LockNotAvailable,Assert.IsType<PostgresException>(error.GetBaseException()).SqlState);await tx.RollbackAsync();
            }
            Assert.Null((await other.FindAsync(pending.Id,default))!.ApplyStartedAtUtc);Assert.Equal(ContainerFileUploadStartResult.Started,await repo.ApplyingAsync(pending,clock.GetUtcNow(),default));
            Assert.True(await repo.FinishAsync(pending,"Unknown","CONTAINER_RECYCLE_ARCHIVE_RESULT_UNKNOWN",Receipt(pending,"Unknown"),default));var unknown=(await other.FindAsync(pending.Id,default))!;await JobFacts(unknown,true);Assert.Null(unknown.ArchiveCopied);Assert.True(unknown.PublishBarrier);Assert.Null(await repo.ClaimAsync(clock.GetUtcNow(),default));
            var immutable=await Assert.ThrowsAsync<PostgresException>(()=>second.Database.ExecuteSqlInterpolatedAsync($"UPDATE container_recycle_archive_intents SET source_proof_hash={new string('0',64)} WHERE id={unknown.Id}"));Assert.Equal("container_recycle_archive_request_immutable",immutable.MessageText);
            await Assert.ThrowsAsync<PostgresException>(()=>second.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM container_recycle_archive_intents WHERE id={unknown.Id}"));
            var admin=actor with{Subject="another-native-archive-admin",LoginBinding=new('9',64)};await repo.AcknowledgeAsync(admin,unknown.Id,unknown.Version,Receipt(unknown,"Unknown"),default);var ack=(await other.FindAsync(unknown.Id,default))!;await JobFacts(ack,true);Assert.Null(ack.ArchiveCopied);Assert.False(ack.PublishBarrier);Assert.True(ContainerRecycleArchiveWorkflowValidation.HasValidBinding(ack,protector));
            await Assert.ThrowsAnyAsync<Exception>(()=>repo.EnqueueAsync(Row(src),default));
            var corrupt=Row(await Source(),new('0',64));await Assert.ThrowsAsync<InvalidOperationException>(()=>repo.EnqueueAsync(corrupt,default));Assert.False(await second.OperationJobs.AnyAsync(x=>x.Id==corrupt.Id));
            var cancel=await Pending(await Ready(await Source()));await new JobRepository(second).RequestCancellationAsync(cancel.ExecutionJobId!.Value,actor.Subject,default);Assert.True(await repo.CancellationRequestedAsync(cancel.Id,default));Assert.True(await second.OutboxMessages.AnyAsync(m=>m.MessageType=="ContainerRecycleArchiveCancellationRequested"&&m.ProcessedAtUtc==null));Assert.True(await repo.FinishAsync(cancel,"Canceled","CONTAINER_RECYCLE_ARCHIVE_CANCELED",Receipt(cancel,"Aborted"),default));
            var normalSource=catalogSources[1];var normal=await Pending(await Ready(normalSource));await ReserveAndApply(normal);await repo.DeferAsync(normal,default);clock.Offset+=TimeSpan.FromSeconds(7);var reclaimed=(await other.ClaimAsync(clock.GetUtcNow(),default))!;Assert.Equal(normal.Id,reclaimed.Id);Assert.False(await repo.RenewAsync(normal,clock.GetUtcNow(),default));
            Assert.False(await repo.FinishAsync(normal,"Succeeded","CONTAINER_RECYCLE_ARCHIVE_COMPLETED",Receipt(normal,"Completed"),default));Assert.True(await other.FinishAsync(reclaimed,"Succeeded","CONTAINER_RECYCLE_ARCHIVE_COMPLETED",Receipt(reclaimed,"Completed",false),default));var done=(await repo.FindAsync(normal.Id,default))!;await JobFacts(done,true);Assert.DoesNotContain(await catalog.ListAsync(catalogCid,4,null,default),x=>x.Purge.Id==done.SourceIntentId);Assert.True(done.ArchiveCopied);Assert.False(done.BindingCurrent);Assert.True(ContainerRecycleArchiveWorkflowValidation.HasValidBinding(done,protector));await Assert.ThrowsAnyAsync<Exception>(()=>repo.EnqueueAsync(Row(normalSource),default));
            Assert.Equal(original,JsonSerializer.Serialize(await sourceRepo.FindAsync(src.Original.Id,default)));Assert.Equal(purge,JsonSerializer.Serialize(await purgeRepo.FindAsync(src.Purge.Id,default)));
            var rollback=Row(await Source());var failed=await Pending(await Ready(await Source()));await ReserveAndApply(failed);
            await second.Database.ExecuteSqlRawAsync("CREATE FUNCTION wdv_archive_audit_reject() RETURNS trigger LANGUAGE plpgsql AS $f$ BEGIN IF NEW.action LIKE 'containers.files.recycle-archive.%' THEN RAISE EXCEPTION 'test audit fault'; END IF; RETURN NEW; END $f$; CREATE TRIGGER wdv_archive_audit_reject BEFORE INSERT ON audit_events FOR EACH ROW EXECUTE FUNCTION wdv_archive_audit_reject();");
            try
            {
                await Assert.ThrowsAsync<DbUpdateException>(()=>repo.EnqueueAsync(rollback,default));Assert.False(await second.OperationJobs.AnyAsync(j=>j.Id==rollback.Id));Assert.False(await second.ContainerRecycleArchiveIntents.AnyAsync(j=>j.Id==rollback.Id));
                await Assert.ThrowsAsync<DbUpdateException>(()=>repo.FinishAsync(failed,"Succeeded","CONTAINER_RECYCLE_ARCHIVE_COMPLETED",Receipt(failed,"Completed"),default));var still=(await other.FindAsync(failed.Id,default))!;Assert.Equal("Applying",still.State);Assert.True(still.PublishBarrier);Assert.Null(still.ReceiptJson);await JobFacts(still,true);
            }
            finally{await second.Database.ExecuteSqlRawAsync("DROP TRIGGER wdv_archive_audit_reject ON audit_events; DROP FUNCTION wdv_archive_audit_reject();");}
            Assert.True(await repo.FinishAsync(failed,"Succeeded","CONTAINER_RECYCLE_ARCHIVE_COMPLETED",Receipt(failed,"Completed"),default));
            var late=await Pending(await Ready(await Source()));await ReserveAndApply(late);Assert.True(await repo.FinishAsync(late,"Unknown","CONTAINER_RECYCLE_ARCHIVE_RESULT_UNKNOWN",null,default));var noReply=(await other.FindAsync(late.Id,default))!;await JobFacts(noReply,true);
            await repo.AcknowledgeAsync(admin,noReply.Id,noReply.Version,Receipt(noReply,"Completed",false),default);var lateProof=(await other.FindAsync(late.Id,default))!;await JobFacts(lateProof,true);Assert.True(lateProof.ArchiveCopied);Assert.False(lateProof.BindingCurrent);Assert.True(ContainerRecycleArchiveWorkflowValidation.HasValidBinding(lateProof,protector));
            var planningFail=Row(await Source());await repo.EnqueueAsync(planningFail,default);var failClaim=(await repo.ClaimAsync(clock.GetUtcNow(),default))!;
            Assert.True(await repo.MarkPlanningAsync(failClaim,default));await repo.FinishPlanningAsync(failClaim,null,"CONTAINER_RECYCLE_ARCHIVE_SOURCE_CHANGED",default);await JobFacts((await other.FindAsync(planningFail.Id,default))!,false);
            Console.WriteLine("INBOX_PHASE=container-recycle-archive-job-nineteen-safe-fields-frozen-planning-nullable-binding-audit-projection");
            var migrations=db.Database.GetMigrations().ToArray();
            var archiveMigration=Assert.Single(migrations,name=>name.EndsWith("_ContainerRecycleArchiveIntents",StringComparison.Ordinal));
            var down=await Assert.ThrowsAsync<PostgresException>(()=>db.GetService<IMigrator>().MigrateAsync(migrations[Array.IndexOf(migrations,archiveMigration)-1]));
            Assert.Equal("container_recycle_archive_downgrade_requires_empty_table",down.MessageText);
            await db.Database.MigrateAsync();
            var retirementSource=await Source(capturedAt:clock.GetUtcNow().AddDays(-30).AddMinutes(2).ToUnixTimeMilliseconds());
            var retirementCopy=await Pending(await Ready(retirementSource));await ReserveAndApply(retirementCopy);
            Assert.True(await repo.FinishAsync(retirementCopy,"Succeeded","CONTAINER_RECYCLE_ARCHIVE_COMPLETED",Receipt(retirementCopy,"Completed"),default));
            await RetirementSchemaBoundaries(second, db, (await repo.FindAsync(retirementCopy.Id,default))!, done, clock.GetUtcNow(), archiveMigration, ClonePeer);
            Assert.False(await db.ContainerRecycleArchiveIntents.AnyAsync(r=>r.CompletedAtUtc==null||r.PublishBarrier||r.LeaseToken!=null));Assert.False(await db.OutboxMessages.AnyAsync(m=>(m.MessageType=="ContainerRecycleArchiveRequested"||m.MessageType=="ContainerRecycleArchiveCancellationRequested")&&m.ProcessedAtUtc==null));
            Assert.Contains(await second.AuditEvents.AsNoTracking().ToArrayAsync(),e=>e.Action=="containers.files.recycle-archive.unknown-acknowledged"&&e.ActorSubject==admin.Subject);
            Console.WriteLine("INBOX_PHASE=container-recycle-archive-two-phase-dual-source-lock-sixteen-fences-nullable-audit-rollback");
        }
        finally{await second.ContainerFileUploadOperations.Where(x=>uploadIds.Contains(x.Id)).ExecuteDeleteAsync();await second.ContainerDirectoryOperations.Where(x=>directoryIds.Contains(x.Id)).ExecuteDeleteAsync();File.Delete(keyPath);}
    }
}
