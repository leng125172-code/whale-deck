using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    private static readonly string[] RecycleFenceTables = ["container_file_upload_operations", "container_directory_operations", "container_copy_intents", "container_move_intents", "container_permission_intents", "container_ownership_intents", "container_recursive_permission_intents", "container_recursive_ownership_intents", "container_acl_intents", "container_recursive_acl_intents", "container_recursive_acl_restore_intents"];
    // Real isolated PostgreSQL; all identity, plan and Root-receipt values are explicit fixtures.
    private static async Task ContainerRecycleBoundaries(DbContextOptions<PlatformDbContext> options)
    {
        await using var db = new PlatformDbContext(options); await using var second = new PlatformDbContext(options);
        var keyPath=Path.GetTempFileName();await File.WriteAllBytesAsync(keyPath,RandomNumberGenerator.GetBytes(32));
        var protector=new FilePlanProtector(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Agent:CapabilityKeyFile"]=keyPath}).Build());
        var clock=new RecycleClock();var repo=new ContainerRecycleRepository(db,protector,clock);var other=new ContainerRecycleRepository(second,protector,clock);
        var actor=new TerminalActor("native-recycle-admin",null,[],new('A',64),"native-recycle-trace",null);
        var ids=new List<Guid>();var uploadIds=new List<Guid>();var directoryIds=new List<Guid>();
        ContainerRecycleIntent Row(string? path=null)
        {
            var key=Guid.NewGuid();var id=ContainerRecycleWorkflowValidation.IntentId(actor.Subject,actor.LoginBinding,key);ids.Add(id);
            var now=ContainerRecycleWorkflowValidation.Time(clock.GetUtcNow());var command=new ContainerRecycleCommandDto(new(new('a',64),"777:2026-11-05T01:02:03.123456789Z",path??"work/recycle-"+id.ToString("N")),new('B',64));
            var binding=new ContainerRecycleRequestBinding(id,key,actor.Subject,actor.LoginBinding,actor.TraceId,ContainerRecycleWorkflowValidation.Owner(actor.Subject,actor.LoginBinding),command,now,now.AddMinutes(15));
            return new(){Id=id,IdempotencyKey=key,ActorSubject=actor.Subject,LoginBinding=actor.LoginBinding,TraceId=actor.TraceId,OwnerHash=binding.Owner,
                ContainerId=command.Location.ContainerId,InstanceId=command.Location.InstanceId,RelativePath=command.Location.Path,CommandJson=ContainerRecycleWorkflowValidation.Serialize(command),
                RequestHash=protector.FingerprintRecycleRequest(binding),CreatedAtUtc=now,PlanningExpiresAtUtc=binding.PlanningExpiresAtUtc,NextAttemptAtUtc=now};
        }
        ContainerRecyclePlanDto Plan(ContainerRecycleIntent row)=>new(1,ContainerRecycleWorkflowValidation.ReadCommand(row),new('C',64),new(ulong.MaxValue,33),new('D',64),5100,5101,new('E',64),
            new(ulong.MaxValue,101,33184,1000,1001,1,7,1700000000123456789),1700000000987654321,new('F',64),[new("user.note",[0,255,7])]);
        ContainerRecycleConfirmation Claims(ContainerRecycleIntent row)=>new(row.Id,row.ActorSubject,row.LoginBinding,row.PlanHash!,row.ReadyExpiresAtUtc!.Value);
        ContainerRecycleReceiptDto Receipt(ContainerRecycleIntent row,string state)=>new(row.ExecutionJobId!.Value,row.ContainerId,row.InstanceId,new('E',64),new('C',64),state,
            state is "Trashed" or "Restored",false,state!="Aborted",state is "Trashed" or "Restored",state is "Trashed" or "Restored"?new('8',64):"",state is "Trashed" or "Restored",
            state=="Unknown"?null:state=="Trashed",state=="Unknown"?null:state=="Restored");
        async Task<ContainerRecycleIntent> Ready(ContainerRecycleIntent row)
        {
            await repo.EnqueueAsync(row,default);var claimed=Assert.IsType<ContainerRecycleIntent>(await repo.ClaimAsync(clock.GetUtcNow(),default));Assert.Equal(row.Id,claimed.Id);
            Assert.True(await repo.MarkPlanningAsync(claimed,default));await repo.FinishPlanningAsync(claimed,Plan(row),"CONTAINER_RECYCLE_READY",default);return (await repo.FindAsync(row.Id,default))!;
        }
        async Task<ContainerRecycleIntent> Pending(ContainerRecycleIntent row)
        {await repo.ConfirmAsync(actor,Claims(row),row.Version,default);var claimed=Assert.IsType<ContainerRecycleIntent>(await repo.ClaimAsync(clock.GetUtcNow(),default));Assert.Equal(row.Id,claimed.Id);return claimed;}
        async Task<ContainerRecycleIntent> Applying(ContainerRecycleIntent row)
        {var claimed=await Pending(row);Assert.Equal(ContainerFileUploadStartResult.Started,await repo.StartedAsync(claimed,clock.GetUtcNow(),default));return claimed;}
        async Task Rejected(FormattableString sql,string code)
        {var error=await Assert.ThrowsAsync<PostgresException>(()=>second.Database.ExecuteSqlInterpolatedAsync(sql));Assert.Equal(code,error.MessageText);}
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
        ContainerPermissionIntent PermissionPeer(string path, string? subjectOverride = null)
        {
            var subject = subjectOverride ?? "native-recursive-chmod-reservation"; var key = Guid.NewGuid();
            var id = ContainerPermissionWorkflowValidation.IntentId(subject, actor.LoginBinding, key);
            var now = ContainerPermissionWorkflowValidation.Time(clock.GetUtcNow());
            var command = new ContainerPermissionCommandDto(new(new('a', 64), "777:2026-10-30T01:02:03.123456789Z", path), "0600", new('B', 64));
            var binding = new ContainerPermissionRequestBinding(id, key, subject, actor.LoginBinding, actor.TraceId,
                ContainerPermissionWorkflowValidation.Owner(subject, actor.LoginBinding), command, now, now.AddMinutes(15));
            return new() { Id = id, IdempotencyKey = key, ActorSubject = subject, LoginBinding = actor.LoginBinding,
                TraceId = actor.TraceId, OwnerHash = binding.Owner, ContainerId = command.Location.ContainerId, InstanceId = command.Location.InstanceId,
                RelativePath = path, CommandJson = ContainerPermissionWorkflowValidation.Serialize(command), RequestHash = protector.FingerprintPermissionRequest(binding),
                CreatedAtUtc = now, PlanningExpiresAtUtc = binding.PlanningExpiresAtUtc, NextAttemptAtUtc = now };
        }
        async Task ClonePeer(string table, Guid seed)
        {
            Assert.Contains(table, RecycleFenceTables);
            var id = Guid.NewGuid(); var execution = Guid.NewGuid(); var key = Guid.NewGuid(); var now = clock.GetUtcNow();
            second.OperationJobs.AddRange(new OperationJob { Id = id, ActorSubject = "native-recursive-fence", IdempotencyKey = key.ToString("D"), JobType = "native.permission.fence" },
                new OperationJob { Id = execution, ActorSubject = "native-recursive-fence", IdempotencyKey = execution.ToString("D"), JobType = "native.permission.fence" });
            await second.SaveChangesAsync();
            // Table identifiers are from the fixed local whitelist above; all values are parameters.
            var executionPatch = table is "container_copy_intents" or "container_move_intents" or "container_permission_intents" or "container_ownership_intents" or "container_recursive_permission_intents" or "container_recursive_ownership_intents" or "container_acl_intents" or "container_recursive_acl_intents" or "container_recursive_acl_restore_intents" ? " || jsonb_build_object('execution_job_id',@execution)" : "";
            if (table is "container_recursive_permission_intents" or "container_recursive_ownership_intents" or "container_acl_intents" or "container_recursive_acl_intents" or "container_recursive_acl_restore_intents") executionPatch += " || jsonb_build_object('receipt_json',NULL,'receipt_hash',NULL,'acl_confirmed',NULL,'root_state',NULL,'completed_count',NULL,'changed_count',NULL,'aborted_count',NULL,'not_started_count',NULL,'unknown_count',NULL,'outcome_digest',NULL,'binding_current',NULL,'history_available',NULL,'history_may_exist',FALSE,'history_verified',FALSE,'history_digest',NULL)";
            var sql = "INSERT INTO " + table + " SELECT (jsonb_populate_record(NULL::" + table + ",to_jsonb(t) || jsonb_build_object('id',@id,'idempotency_key',@key,'completed_at_utc',NULL,'lease_token',NULL,'lease_expires_at_utc',NULL,'publish_barrier',TRUE,'root_started_at_utc',@now,'state',@state,'published_version',NULL,'acknowledged_by',NULL,'acknowledgement_json',NULL)" + executionPatch + ")).* FROM " + table + " t WHERE id=@seed";
            Assert.Equal(1, await second.Database.ExecuteSqlRawAsync(sql, new NpgsqlParameter("id", id), new NpgsqlParameter("key", key), new NpgsqlParameter("now", now), new NpgsqlParameter("execution", execution), new NpgsqlParameter("seed", seed), new NpgsqlParameter("state", table == "container_file_upload_operations" ? "Ready" : "Pending")));
        }
        try
        {
            Assert.False(db.Database.HasPendingModelChanges());var migrations=db.GetService<IMigrationsAssembly>().Migrations.Keys.ToArray();var migration=migrations.Single(v=>v.EndsWith("_ContainerRecycleIntents",StringComparison.Ordinal));
            var previous=migrations[Array.IndexOf(migrations,migration)-1];await db.GetService<IMigrator>().MigrateAsync(previous);await db.Database.MigrateAsync();
            Assert.Equal(18,await db.Database.SqlQueryRaw<int>("SELECT count(*)::integer AS \"Value\" FROM pg_constraint WHERE conrelid='container_recycle_intents'::regclass AND contype='c'").SingleAsync());
            Assert.Equal(13,await db.Database.SqlQueryRaw<int>("SELECT count(*)::integer AS \"Value\" FROM pg_trigger t JOIN pg_proc p ON p.oid=t.tgfoid WHERE p.proname='whaledeck_container_recycle_fence' AND NOT t.tgisinternal").SingleAsync());
            foreach(var relation in new[]{"same","ancestor","descendant"})
            {
                var path="work/reservation-"+Guid.NewGuid().ToString("N")+"/leaf";var peer=PermissionPeer(relation=="ancestor"?path[..path.LastIndexOf('/')]:relation=="descendant"?path+"/child":path);
                var held=Row(path);await repo.EnqueueAsync(held,default);var modes=new ContainerPermissionRepository(second,protector,clock);
                Assert.Equal("CONTAINER_PERMISSION_TARGET_BUSY",(await Assert.ThrowsAsync<InvalidOperationException>(()=>modes.EnqueueAsync(peer,default))).Message);
                await repo.CancelAsync(actor,held.Id,default);await modes.EnqueueAsync(peer,default);
                Assert.Equal("CONTAINER_RECYCLE_TARGET_BUSY",(await Assert.ThrowsAsync<InvalidOperationException>(()=>repo.EnqueueAsync(Row(path),default))).Message);
                await modes.CancelAsync(actor with{Subject=peer.ActorSubject},peer.Id,default);
            }
            var ready=await Ready(Row());Assert.Null(await other.ClaimAsync(clock.GetUtcNow(),default));
            Assert.Null(await repo.OwnedAsync(actor with{LoginBinding=new('9',64)},ready.Id,default));
            Assert.Equal(ready.Id,(await repo.EnqueueAsync(ready,default)).Id);
            await Assert.ThrowsAsync<InvalidOperationException>(()=>other.ConfirmAsync(actor,Claims(ready),ready.Version-1,default));
            await Rejected($"UPDATE container_recycle_intents SET relative_path='elsewhere' WHERE id={ready.Id}","container_recycle_request_immutable");
            await Rejected($"UPDATE container_recycle_intents SET plan_hash={new string('9',64)} WHERE id={ready.Id}","container_recycle_plan_immutable");
            var applied=await Applying(ready);Assert.Null(await other.ClaimAsync(clock.GetUtcNow(),default));
            await Rejected($"UPDATE container_recycle_intents SET execution_job_id={Guid.NewGuid()} WHERE id={ready.Id}","container_recycle_execution_immutable");
            Assert.True(await repo.FinishAsync(applied,"Succeeded","CONTAINER_RECYCLE_TRASHED",Receipt(applied,"Trashed"),default));
            var finished=(await other.FindAsync(ready.Id,default))!;Assert.True(ContainerRecycleWorkflowValidation.HasValidBinding(finished,protector));Assert.True(finished.SourceRetained);Assert.False(finished.SourceRestored);
            Assert.Equal("Succeeded",(await second.OperationJobs.AsNoTracking().SingleAsync(j=>j.Id==finished.ExecutionJobId)).State);
            await Rejected($"UPDATE container_recycle_intents SET version=version+1 WHERE id={ready.Id}","container_recycle_terminal_immutable");
            await Rejected($"DELETE FROM container_recycle_intents WHERE id={ready.Id}","container_recycle_permanent_intent");
            var plan=Plan(Row());var pj=ContainerRecycleWorkflowValidation.Serialize(plan);var cj=ContainerRecycleWorkflowValidation.Serialize(plan.Command);
            Assert.True(await db.Database.SqlQuery<bool>($"SELECT whaledeck_recycle_plan_valid({pj}::jsonb,{cj}::jsonb) AS \"Value\"").SingleAsync());
            foreach(var kind in new[]{"missing","type","links","size","setid","attribute-domain","base64","order","limits"})
            {
                var bad=JsonNode.Parse(pj)!;
                switch(kind){case "missing":bad.AsObject().Remove("Attributes");break;case "type":bad["SourceIdentity"]!["Inode"]="101";break;
                    case "links":bad["SourceIdentity"]!["LinkCount"]=2;break;case "size":bad["SourceIdentity"]!["SizeBytes"]=1073741825;break;
                    case "setid":bad["SourceIdentity"]!["Mode"]=0x89A0;break;case "attribute-domain":bad["Attributes"]![0]!["Name"]="security.capability";break;
                    case "base64":bad["Attributes"]![0]!["Value"]="AP8H\n";break;case "order":bad["Attributes"]!.AsArray().Add(bad["Attributes"]![0]!.DeepClone());break;
                    default:bad["Attributes"]![0]!["Value"]=Convert.ToBase64String(new byte[4096]);break;}
                Assert.False(await db.Database.SqlQuery<bool>($"SELECT whaledeck_recycle_plan_valid({bad.ToJsonString()}::jsonb,{cj}::jsonb) AS \"Value\"").SingleAsync());
            }
            foreach(var invalid in new[]{"../escape","/absolute","x//y","x/../y","x/","x\\y",new string('x',256)})
            {var bad=plan.Command with{Location=plan.Command.Location with{Path=invalid}};Assert.False(await db.Database.SqlQuery<bool>($"SELECT whaledeck_recycle_command_valid({ContainerRecycleWorkflowValidation.Serialize(bad)}::jsonb,{bad.Location.ContainerId},{bad.Location.InstanceId},{invalid}) AS \"Value\"").SingleAsync());}
            await PeerSeeds();
            var seeds=new Dictionary<string,Guid>{["container_file_upload_operations"]=uploadIds.Single(),["container_directory_operations"]=directoryIds.Single(),
                ["container_copy_intents"]=await db.ContainerCopyIntents.Where(r=>r.ExecutionJobId!=null).Select(r=>r.Id).FirstAsync(),
                ["container_move_intents"]=await db.ContainerMoveIntents.Where(r=>r.ExecutionJobId!=null).Select(r=>r.Id).FirstAsync(),
                ["container_permission_intents"]=await db.ContainerPermissionIntents.Where(r=>r.ExecutionJobId!=null).Select(r=>r.Id).FirstAsync(),
                ["container_ownership_intents"]=await db.ContainerOwnershipIntents.Where(r=>r.ExecutionJobId!=null).Select(r=>r.Id).FirstAsync(),
                ["container_recursive_permission_intents"]=await db.ContainerRecursivePermissionIntents.Where(r=>r.ExecutionJobId!=null).Select(r=>r.Id).FirstAsync(),
                ["container_recursive_ownership_intents"]=await db.ContainerRecursiveOwnershipIntents.Where(r=>r.ExecutionJobId!=null).Select(r=>r.Id).FirstAsync(),
                ["container_acl_intents"]=await db.ContainerAclIntents.Where(r=>r.ExecutionJobId!=null).Select(r=>r.Id).FirstAsync(),
                ["container_recursive_acl_intents"]=await db.ContainerRecursiveAclIntents.Where(r=>r.ExecutionJobId!=null).Select(r=>r.Id).FirstAsync(),
                ["container_recursive_acl_restore_intents"]=await db.ContainerRecursiveAclRestoreIntents.Where(r=>r.ExecutionJobId!=null).Select(r=>r.Id).FirstAsync()};
            var blocked=await Applying(await Ready(Row()));
            await Rejected($"INSERT INTO file_path_locks(path_hash,operation_id,scope_kind) VALUES ({FileManagementService.Hash("recycle-host:"+blocked.Id)},{blocked.Id},'Legacy')","FILES_TARGET_BUSY");
            foreach(var pair in seeds)
            {
                await using var tx=await second.Database.BeginTransactionAsync();var error=await Record.ExceptionAsync(()=>ClonePeer(pair.Key,pair.Value));
                Assert.NotNull(error);Assert.Equal(PostgresErrorCodes.ObjectNotInPrerequisiteState,Assert.IsType<PostgresException>(error.GetBaseException()).SqlState);await tx.RollbackAsync();second.ChangeTracker.Clear();
            }
            var unknownReceipt=Receipt(blocked,"Unknown") with{HistoryVerified=true,HistoryDigest=new('8',64),HistoryAvailable=true};
            Assert.True(await repo.FinishAsync(blocked,"Unknown","CONTAINER_RECYCLE_RESULT_UNKNOWN",unknownReceipt,default));
            var unknown=(await other.FindAsync(blocked.Id,default))!;Assert.Null(unknown.SourceRetained);Assert.Null(unknown.SourceRestored);Assert.True(unknown.PublishBarrier);Assert.Null(await repo.ClaimAsync(clock.GetUtcNow(),default));
            await Rejected($"UPDATE container_recycle_intents SET source_retained=false WHERE id={unknown.Id}","container_recycle_fact_immutable");
            var wrong=unknownReceipt with{State="Aborted",SourceRetained=false,SourceRestored=false};await Assert.ThrowsAsync<InvalidOperationException>(()=>other.AcknowledgeAsync(actor,unknown.Id,unknown.Version,wrong,default));
            var receiptJson=JsonNode.Parse(ContainerRecycleWorkflowValidation.Serialize(unknownReceipt))!;receiptJson["SourceRetained"]=false;
            Assert.False(await db.Database.SqlQuery<bool>($"SELECT whaledeck_recycle_receipt_valid({receiptJson.ToJsonString()}::jsonb,{unknown.PlanJson}::jsonb) AS \"Value\"").SingleAsync());
            var admin=actor with{Subject="native-other-recycle-admin",LoginBinding=new('F',64)};
            await other.AcknowledgeAsync(admin,unknown.Id,unknown.Version,unknownReceipt with{HistoryAvailable=false,Replayed=true},default);
            var ack=(await repo.FindAsync(unknown.Id,default))!;Assert.Equal("Acknowledged",ack.State);Assert.Equal("Unknown",ack.RootState);Assert.False(ack.PublishBarrier);Assert.False(ack.HistoryAvailable);Assert.True(ack.HistoryVerified);Assert.True(ContainerRecycleWorkflowValidation.HasValidBinding(ack,protector));
            // Inverse SQL direction: every legacy publisher blocks a raw recycle start.
            var inverse=await Pending(await Ready(Row()));
            foreach(var pair in seeds)
            {
                await using var tx=await second.Database.BeginTransactionAsync();await ClonePeer(pair.Key,pair.Value);
                // Same connection owns the SQL transaction; direct new-table mutation must fail before any write.
                var error=await Assert.ThrowsAsync<PostgresException>(()=>second.Database.ExecuteSqlInterpolatedAsync($"UPDATE container_recycle_intents SET publish_barrier=true,root_started_at_utc={clock.GetUtcNow()},state='Applying' WHERE id={inverse.Id}"));
                Assert.Equal("CONTAINER_RECYCLE_TARGET_BUSY",error.MessageText);await tx.RollbackAsync();second.ChangeTracker.Clear();
            }
            await using(var tx=await second.Database.BeginTransactionAsync())
            {
                await second.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO file_path_locks(path_hash,operation_id,scope_kind) VALUES ({FileManagementService.Hash("recycle-inverse:"+inverse.Id)},{inverse.Id},'Legacy')");
                var error=await Assert.ThrowsAsync<PostgresException>(()=>second.Database.ExecuteSqlInterpolatedAsync($"UPDATE container_recycle_intents SET publish_barrier=true,root_started_at_utc={clock.GetUtcNow()},state='Applying' WHERE id={inverse.Id}"));Assert.Equal("CONTAINER_RECYCLE_TARGET_BUSY",error.MessageText);await tx.RollbackAsync();second.ChangeTracker.Clear();
            }
            var heldHash=FileManagementService.Hash("recycle-repository-peer:"+inverse.Id);
            await second.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO file_path_locks(path_hash,operation_id,scope_kind) VALUES ({heldHash},{inverse.Id},'Legacy')");
            try {Assert.Equal(ContainerFileUploadStartResult.Busy,await repo.StartedAsync(inverse,clock.GetUtcNow(),default));Assert.Null((await other.FindAsync(inverse.Id,default))!.RootStartedAtUtc);}
            finally {Assert.Equal(1,await second.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM file_path_locks WHERE path_hash={heldHash} AND operation_id={inverse.Id}"));}
            Assert.Equal(ContainerFileUploadStartResult.Started,await repo.StartedAsync(inverse,clock.GetUtcNow(),default));
            Assert.True(await repo.FinishAsync(inverse,"Failed","CONTAINER_RECYCLE_INTERRUPTED",Receipt(inverse,"Aborted"),default));
            var leased=await Applying(await Ready(Row()));await repo.DeferAsync(leased,default);clock.Offset=TimeSpan.FromSeconds(7);
            var reclaimed=Assert.IsType<ContainerRecycleIntent>(await other.ClaimAsync(clock.GetUtcNow(),default));Assert.Equal(leased.Id,reclaimed.Id);Assert.NotEqual(leased.LeaseToken,reclaimed.LeaseToken);Assert.NotNull(reclaimed.RootStartedAtUtc);Assert.True(reclaimed.PublishBarrier);
            Assert.False(await repo.RenewAsync(leased,clock.GetUtcNow(),default));Assert.False(await repo.FinishAsync(leased,"Failed","CONTAINER_RECYCLE_INTERRUPTED",Receipt(leased,"Aborted"),default));
            Assert.True(await other.FinishAsync(reclaimed,"Failed","CONTAINER_RECYCLE_INTERRUPTED",Receipt(reclaimed,"Aborted"),default));
            var expiring=await Ready(Row());clock.Offset+=TimeSpan.FromMinutes(6);var expired=Assert.IsType<ContainerRecycleIntent>(await repo.ClaimAsync(clock.GetUtcNow(),default));Assert.Equal(expiring.Id,expired.Id);await repo.FinishPlanningAsync(expired,null,"CONTAINER_RECYCLE_PLAN_EXPIRED",default);
            var rejected=Row();var finalizing=await Applying(await Ready(Row()));
            await second.Database.ExecuteSqlRawAsync("CREATE FUNCTION wdv_recycle_audit_reject() RETURNS trigger LANGUAGE plpgsql AS $f$ BEGIN RAISE EXCEPTION 'wdv_recycle_audit_failed' USING ERRCODE='23514'; END $f$; CREATE TRIGGER wdv_recycle_audit_reject BEFORE INSERT ON audit_events FOR EACH ROW WHEN (NEW.action LIKE 'containers.files.recycle.%') EXECUTE FUNCTION wdv_recycle_audit_reject();");
            try {await Assert.ThrowsAsync<DbUpdateException>(()=>repo.EnqueueAsync(rejected,default));await Assert.ThrowsAsync<DbUpdateException>(()=>repo.FinishAsync(finalizing,"Succeeded","CONTAINER_RECYCLE_TRASHED",Receipt(finalizing,"Trashed"),default));Assert.Equal("Applying",(await other.FindAsync(finalizing.Id,default))!.State);}
            finally{await second.Database.ExecuteSqlRawAsync("DROP TRIGGER wdv_recycle_audit_reject ON audit_events; DROP FUNCTION wdv_recycle_audit_reject();");}
            Assert.False(await second.ContainerRecycleIntents.AnyAsync(r=>r.Id==rejected.Id));Assert.False(await second.OperationJobs.AnyAsync(j=>j.Id==rejected.Id));
            Assert.True(await repo.FinishAsync(finalizing,"Succeeded","CONTAINER_RECYCLE_TRASHED",Receipt(finalizing,"Trashed"),default));
            var waiting=new List<ContainerRecycleIntent>();for(var n=0;n<4;n++){var row=Row();await repo.EnqueueAsync(row,default);waiting.Add(row);}
            Assert.Equal("FILES_OPERATION_BUDGET_EXHAUSTED",(await Assert.ThrowsAsync<InvalidOperationException>(()=>repo.EnqueueAsync(Row(),default))).Message);
            var mode=new ContainerPermissionRepository(second,protector,clock);Assert.Equal("FILES_OPERATION_BUDGET_EXHAUSTED",(await Assert.ThrowsAsync<InvalidOperationException>(()=>mode.EnqueueAsync(PermissionPeer("work/budget-other",actor.Subject),default))).Message);
            foreach(var row in waiting)await repo.CancelAsync(actor,row.Id,default);
            var lockRejected=Row();await using(var tx=await second.Database.BeginTransactionAsync())
            {await second.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(41307341)");var error=await Record.ExceptionAsync(()=>repo.EnqueueAsync(lockRejected,default));Assert.NotNull(error);Assert.Equal(PostgresErrorCodes.LockNotAvailable,Assert.IsType<PostgresException>(error.GetBaseException()).SqlState);await tx.RollbackAsync();}
            Assert.False(await db.OperationJobs.AnyAsync(j=>j.Id==lockRejected.Id));
            var downgrade=await Assert.ThrowsAsync<PostgresException>(()=>db.GetService<IMigrator>().MigrateAsync(previous));Assert.Equal("container_recycle_downgrade_requires_empty_table",downgrade.MessageText);await db.Database.MigrateAsync();
            Assert.False(await db.ContainerRecycleIntents.AnyAsync(r=>r.CompletedAtUtc==null||r.PublishBarrier||r.LeaseToken!=null));
            Assert.False(await db.OperationJobs.AnyAsync(j=>ids.Contains(j.Id)&&j.CompletedAtUtc==null));
            Assert.False(await db.OutboxMessages.AnyAsync(m=>(m.MessageType=="ContainerRecycleRequested"||m.MessageType=="ContainerRecycleCancellationRequested")&&m.ProcessedAtUtc==null));
            Assert.True(await db.AuditEvents.AnyAsync(a=>a.Action=="containers.files.recycle.unknown-acknowledged"&&a.ActorSubject==admin.Subject));
            Console.WriteLine("INBOX_PHASE=container-recycle-persistent-original-facts-nullable-unknown-twelve-peer-fences-audited");
        }
        finally
        {await second.ContainerFileUploadOperations.Where(r=>uploadIds.Contains(r.Id)).ExecuteDeleteAsync();await second.ContainerDirectoryOperations.Where(r=>directoryIds.Contains(r.Id)).ExecuteDeleteAsync();File.Delete(keyPath);}
    }
    private sealed class RecycleClock:TimeProvider{public TimeSpan Offset{get;set;}public override DateTimeOffset GetUtcNow()=>DateTimeOffset.UtcNow.Add(Offset);}
}
