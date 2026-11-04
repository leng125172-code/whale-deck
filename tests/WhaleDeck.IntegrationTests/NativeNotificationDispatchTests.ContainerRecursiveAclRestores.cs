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
    private static readonly string[] RecursiveAclRestoreFenceTables = ["container_file_upload_operations", "container_directory_operations", "container_copy_intents", "container_move_intents", "container_permission_intents", "container_ownership_intents", "container_recursive_permission_intents", "container_recursive_ownership_intents", "container_acl_intents", "container_recursive_acl_intents"];
    private static readonly JsonSerializerOptions RestoreJobResultJson = new(JsonSerializerDefaults.Web);
    /// <summary>Real isolated PostgreSQL/MAC/transactions/triggers. Receipts are synthetic, not Root or UI evidence.</summary>
    private static async Task ContainerRecursiveAclRestoreBoundaries(DbContextOptions<PlatformDbContext> options)
    {
        await using var db = new PlatformDbContext(options); await using var second = new PlatformDbContext(options);
        var keyPath = Path.GetTempFileName(); await File.WriteAllBytesAsync(keyPath, RandomNumberGenerator.GetBytes(32));
        var protector = new FilePlanProtector(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:CapabilityKeyFile"] = keyPath }).Build());
        var clock = new RecursiveAclRestoreClock(); var repo = new ContainerRecursiveAclRestoreRepository(db, protector, clock); var other = new ContainerRecursiveAclRestoreRepository(second, protector, clock);
        var actor = new TerminalActor("native-recursive-acl-restore-admin", null, [], new('A', 64), "native-recursive-acl-restore-trace", null);
        var ids = new List<Guid>(); var uploadIds = new List<Guid>(); var directoryIds = new List<Guid>();
        var sourceTypes = new Dictionary<Guid, string>();
        ContainerRecursiveAclRestoreIntent Row(string? path = null, string? subject = null, bool defaults = false)
        {
            var owner = subject ?? actor.Subject; var key = Guid.NewGuid(); var id = ContainerRecursiveAclRestoreWorkflowValidation.IntentId(owner, actor.LoginBinding, key); ids.Add(id); sourceTypes[id] = defaults ? "default" : "access";
            var now = ContainerRecursiveAclRestoreWorkflowValidation.Time(clock.GetUtcNow());
            var command = new ContainerRecursiveAclRestoreCommandDto(new(new('a', 64), "777:2026-10-30T01:02:03.123456789Z", path ?? "work/acl-" + id.ToString("N")), Guid.NewGuid().ToString("N"), new('7', 64), new('B', 64));
            var binding = new ContainerRecursiveAclRestoreRequestBinding(id, key, owner, actor.LoginBinding, actor.TraceId, ContainerRecursiveAclRestoreWorkflowValidation.Owner(owner, actor.LoginBinding), command, now, now.AddMinutes(15));
            return new() { Id = id, IdempotencyKey = key, ActorSubject = owner, LoginBinding = actor.LoginBinding, TraceId = actor.TraceId, OwnerHash = binding.Owner,
                ContainerId = command.Location.ContainerId, InstanceId = command.Location.InstanceId, RelativePath = command.Location.Path,
                CommandJson = ContainerRecursiveAclRestoreWorkflowValidation.Serialize(command), RequestHash = protector.FingerprintRecursiveAclRestoreRequest(binding), CreatedAtUtc = now,
                PlanningExpiresAtUtc = binding.PlanningExpiresAtUtc, NextAttemptAtUtc = now };
        }
        ContainerRecursiveAclRestorePlanDto Plan(ContainerRecursiveAclRestoreIntent row) => new(1, ContainerRecursiveAclRestoreWorkflowValidation.ReadCommand(row), new('C', 64), new(ulong.MaxValue, 33), new('D', 64),
            5100, 5101, new('E', 64), new(ulong.MaxValue, 101, 16872, 1000, 1001, 2, 4096, 1700000000123456789), 1700000000987654321, new('F', 64), 4, 2, 2, 2,
            new(ContainerRecursiveAclRestoreWorkflowValidation.ReadCommand(row).CaptureId, new('7',64), 1700000000000, row.InstanceId, sourceTypes[row.Id], new('6',64), 4, 2, 2, 1),
            new(ulong.MaxValue, 222, 0x8180, 0, 0, 1, 1024, long.MinValue, long.MaxValue), new('5',64));
        ContainerRecursiveAclRestoreConfirmation Claims(ContainerRecursiveAclRestoreIntent row) => new(row.Id, row.ActorSubject, row.LoginBinding, row.PlanHash!, row.ReadyExpiresAtUtc!.Value);
        ContainerRecursiveAclRestoreReceiptDto Receipt(ContainerRecursiveAclRestoreIntent row, string state, bool current = true) => new(row.ExecutionJobId!.Value, row.ContainerId, row.InstanceId,
            new('E', 64), new('C', 64), state, state == "Completed" && current, false,
            state == "Completed" ? 4U : state == "Partial" ? 1U : 0U, state == "Completed" ? 2U : state == "Partial" ? 1U : 0U, 0,
            state == "Aborted" ? 4U : state == "Partial" ? 3U : 0U, state == "Unknown" ? 4U : 0U,
            state != "Aborted", state is "Completed" or "Partial", state is "Completed" or "Partial" ? new('8', 64) : "",
            state is "Completed" or "Partial", state is "Completed" or "Partial" ? new('9', 64) : "", sourceTypes[row.Id]);
        async Task<ContainerRecursiveAclRestoreJobResults.Summary> JobFacts(ContainerRecursiveAclRestoreIntent row, bool execution, string state)
        {
            var jobId = execution ? row.ExecutionJobId!.Value : row.Id;
            var job = await second.OperationJobs.AsNoTracking().SingleAsync(j => j.Id == jobId);
            Assert.NotNull(job.ResultJson);
            var projected = await new OperationService(new JobRepository(second)).FindAsync(jobId, default);
            Assert.NotNull(projected!.ResultJson); Assert.Equal(ContainerRecursiveAclRestoreJobResults.ForJob(job), projected.ResultJson);
            var value = JsonSerializer.Deserialize<ContainerRecursiveAclRestoreJobResults.Summary>(projected.ResultJson, RestoreJobResultJson)!;
            Assert.Equal(row.Id,value.IntentId);Assert.Equal(jobId,value.JobId);Assert.Equal(state,value.State);Assert.Equal(execution?"execution":"planning",value.Stage);
            using var document=JsonDocument.Parse(projected.ResultJson);Assert.Equal(26,document.RootElement.EnumerateObject().Count());
            foreach(var secret in new[]{row.ActorSubject,row.LoginBinding,row.TraceId,row.ContainerId,row.InstanceId,row.RelativePath,row.OwnerHash,row.RequestHash,row.PlanHash,row.ExecutionHash,row.ReceiptHash}) if(secret is not null)Assert.DoesNotContain(secret,projected.ResultJson,StringComparison.Ordinal);
            return value;
        }
        async Task<ContainerRecursiveAclRestoreIntent> Ready(ContainerRecursiveAclRestoreIntent row)
        {
            await repo.EnqueueAsync(row, default); var claimed = Assert.IsType<ContainerRecursiveAclRestoreIntent>(await repo.ClaimAsync(clock.GetUtcNow(), default)); Assert.Equal(row.Id, claimed.Id);
            Assert.True(await repo.MarkPlanningAsync(claimed, default)); await repo.FinishPlanningAsync(claimed, Plan(row), "CONTAINER_RECURSIVE_ACL_RESTORE_READY", default);
            var ready = (await repo.FindAsync(row.Id, default))!;
            var facts=await JobFacts(ready,false,"Ready");Assert.Null(facts.CompletedCount);Assert.Equal(1U,facts.SourceChangedCount);Assert.Equal(2U,facts.ChangeCount);
            return ready;
        }
        async Task<ContainerRecursiveAclRestoreIntent> Pending(ContainerRecursiveAclRestoreIntent row)
        {
            await repo.ConfirmAsync(actor, Claims(row), row.Version, default);
            var claimed = Assert.IsType<ContainerRecursiveAclRestoreIntent>(await repo.ClaimAsync(clock.GetUtcNow(), default)); Assert.Equal(row.Id, claimed.Id); return claimed;
        }
        async Task<ContainerRecursiveAclRestoreIntent> Applying(ContainerRecursiveAclRestoreIntent row)
        { var claimed = await Pending(row); Assert.Equal(ContainerFileUploadStartResult.Started, await repo.StartedAsync(claimed, clock.GetUtcNow(), default)); return claimed; }
        async Task Rejected(FormattableString sql, string message)
        { var failure = await Assert.ThrowsAsync<PostgresException>(() => second.Database.ExecuteSqlInterpolatedAsync(sql)); Assert.Equal(message, failure.MessageText); }
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
        ContainerRecursivePermissionIntent RecursivePermissionPeer(string path)
        {
            const string subject = "native-recursive-chmod-reservation";
            var key = Guid.NewGuid(); var now = ContainerRecursivePermissionWorkflowValidation.Time(clock.GetUtcNow());
            var id = ContainerRecursivePermissionWorkflowValidation.IntentId(subject, actor.LoginBinding, key);
            var command = new ContainerRecursivePermissionCommandDto(new(new('a',64), "777:2026-10-30T01:02:03.123456789Z",path), "0600","0750",new('B',64));
            var binding = new ContainerRecursivePermissionRequestBinding(id,key,subject,actor.LoginBinding,actor.TraceId,
                ContainerRecursivePermissionWorkflowValidation.Owner(subject,actor.LoginBinding),command,now,now.AddMinutes(15));
            return new() { Id=id,IdempotencyKey=key,ActorSubject=subject,LoginBinding=actor.LoginBinding,TraceId=actor.TraceId,OwnerHash=binding.Owner,
                ContainerId=command.Location.ContainerId,InstanceId=command.Location.InstanceId,RelativePath=path,
                CommandJson=ContainerRecursivePermissionWorkflowValidation.Serialize(command),RequestHash=protector.FingerprintRecursivePermissionRequest(binding),
                CreatedAtUtc=now,PlanningExpiresAtUtc=binding.PlanningExpiresAtUtc,NextAttemptAtUtc=now };
        }
        ContainerRecursiveAclIntent OriginalAclPeer(string path)
        {
            const string subject="native-original-recursive-acl-peer";var key=Guid.NewGuid();var now=ContainerRecursiveAclWorkflowValidation.Time(clock.GetUtcNow());
            var id=ContainerRecursiveAclWorkflowValidation.IntentId(subject,actor.LoginBinding,key);
            ContainerAclEntryDto[] entries=[new("user_obj",null,6),new("group_obj",null,0),new("other",null,0)];
            var command=new ContainerRecursiveAclCommandDto(new(new('a',64),"777:2026-10-30T01:02:03.123456789Z",path),"access",entries,entries,new('B',64));
            var binding=new ContainerRecursiveAclRequestBinding(id,key,subject,actor.LoginBinding,actor.TraceId,ContainerRecursiveAclWorkflowValidation.Owner(subject,actor.LoginBinding),command,now,now.AddMinutes(15));
            return new(){Id=id,IdempotencyKey=key,ActorSubject=subject,LoginBinding=actor.LoginBinding,TraceId=actor.TraceId,OwnerHash=binding.Owner,
                ContainerId=command.Location.ContainerId,InstanceId=command.Location.InstanceId,RelativePath=path,CommandJson=ContainerRecursiveAclWorkflowValidation.Serialize(command),
                RequestHash=protector.FingerprintRecursiveAclRequest(binding),CreatedAtUtc=now,PlanningExpiresAtUtc=binding.PlanningExpiresAtUtc,NextAttemptAtUtc=now};
        }
        // These raw clones exercise ONLY SQL exclusion, not application MAC acceptance.
        // Every clone lives in an explicit rolled-back transaction with its own jobs.
        async Task ClonePeer(string table, Guid seed)
        {
            Assert.Contains(table, RecursiveAclRestoreFenceTables);
            var id = Guid.NewGuid(); var execution = Guid.NewGuid(); var key = Guid.NewGuid(); var now = clock.GetUtcNow();
            second.OperationJobs.AddRange(new OperationJob { Id = id, ActorSubject = "native-recursive-fence", IdempotencyKey = key.ToString("D"), JobType = "native.permission.fence" },
                new OperationJob { Id = execution, ActorSubject = "native-recursive-fence", IdempotencyKey = execution.ToString("D"), JobType = "native.permission.fence" });
            await second.SaveChangesAsync();
            // Table identifiers are from the fixed local whitelist above; all values are parameters.
            var executionPatch = table is "container_copy_intents" or "container_move_intents" or "container_permission_intents" or "container_ownership_intents" or "container_recursive_permission_intents" or "container_recursive_ownership_intents" or "container_acl_intents" or "container_recursive_acl_intents" ? " || jsonb_build_object('execution_job_id',@execution)" : "";
            if (table is "container_recursive_permission_intents" or "container_recursive_ownership_intents" or "container_acl_intents" or "container_recursive_acl_intents") executionPatch += " || jsonb_build_object('receipt_json',NULL,'receipt_hash',NULL,'acl_confirmed',NULL,'root_state',NULL,'completed_count',NULL,'changed_count',NULL,'aborted_count',NULL,'not_started_count',NULL,'unknown_count',NULL,'outcome_digest',NULL,'binding_current',NULL,'history_available',NULL,'history_may_exist',FALSE,'history_verified',FALSE,'history_digest',NULL)";
            var sql = "INSERT INTO " + table + " SELECT (jsonb_populate_record(NULL::" + table + ",to_jsonb(t) || jsonb_build_object('id',@id,'idempotency_key',@key,'completed_at_utc',NULL,'lease_token',NULL,'lease_expires_at_utc',NULL,'publish_barrier',TRUE,'root_started_at_utc',@now,'state',@state,'published_version',NULL,'acknowledged_by',NULL,'acknowledgement_json',NULL)" + executionPatch + ")).* FROM " + table + " t WHERE id=@seed";
            Assert.Equal(1, await second.Database.ExecuteSqlRawAsync(sql, new NpgsqlParameter("id", id), new NpgsqlParameter("key", key), new NpgsqlParameter("now", now), new NpgsqlParameter("execution", execution), new NpgsqlParameter("seed", seed), new NpgsqlParameter("state", table == "container_file_upload_operations" ? "Ready" : "Pending")));
        }
        try
        {
            Assert.False(db.Database.HasPendingModelChanges());
            var migrations=db.GetService<IMigrationsAssembly>().Migrations.Keys.ToArray();var latest=migrations.Single(v=>v.EndsWith("_ContainerRecursiveAclRestoreIntents",StringComparison.Ordinal));
            var previous=migrations[Array.IndexOf(migrations,latest)-1];
            await db.GetService<IMigrator>().MigrateAsync(previous);await db.Database.MigrateAsync();
            Assert.Equal(18,await db.Database.SqlQueryRaw<int>("SELECT count(*)::integer AS \"Value\" FROM pg_constraint WHERE conrelid='container_recursive_acl_restore_intents'::regclass AND contype='c'").SingleAsync());

            Assert.False(await db.FilePathLocks.AnyAsync());
            Assert.False(await db.ContainerFileUploadOperations.AnyAsync(r => r.PublishBarrier));
            Assert.False(await db.ContainerDirectoryOperations.AnyAsync(r => r.PublishBarrier));
            Assert.False(await db.ContainerCopyIntents.AnyAsync(r => r.PublishBarrier));
            Assert.False(await db.ContainerMoveIntents.AnyAsync(r => r.PublishBarrier));
            Assert.False(await db.ContainerRecursiveAclRestoreIntents.AnyAsync(r => r.PublishBarrier));
            // No root call: actual repositories must reserve ancestor/descendant paths in both directions.
            foreach (var relation in new[] { "same", "ancestor", "descendant" })
            {
                var prefix = "work/ownership-reservation-" + Guid.NewGuid().ToString("N"); var path = prefix + "/item";
                var peerPath = relation == "ancestor" ? prefix : relation == "descendant" ? path + "/child" : path;
                var owner = Row(path); await repo.EnqueueAsync(owner, default);
                var permission = PermissionPeer(peerPath); var modes = new ContainerPermissionRepository(second, protector, clock);
                var denied = await Assert.ThrowsAsync<InvalidOperationException>(() => modes.EnqueueAsync(permission, default));
                Assert.Equal("CONTAINER_PERMISSION_TARGET_BUSY", denied.Message);
                await repo.CancelAsync(actor, owner.Id, default);
                await modes.EnqueueAsync(permission, default);
                var inverseOwner = Row(path);
                denied = await Assert.ThrowsAsync<InvalidOperationException>(() => repo.EnqueueAsync(inverseOwner, default));
                Assert.Equal("CONTAINER_RECURSIVE_ACL_RESTORE_TARGET_BUSY", denied.Message);
                var modeActor = actor with { Subject = permission.ActorSubject };
                await modes.CancelAsync(modeActor, permission.Id, default);
                // The two recursive families reserve overlapping trees in BOTH directions.
                var ownerTree = Row(path); await repo.EnqueueAsync(ownerTree,default);
                var recursiveMode = RecursivePermissionPeer(peerPath); var trees = new ContainerRecursivePermissionRepository(second,protector,clock);
                denied = await Assert.ThrowsAsync<InvalidOperationException>(()=>trees.EnqueueAsync(recursiveMode,default));
                Assert.Equal("CONTAINER_RECURSIVE_PERMISSION_TARGET_BUSY",denied.Message);
                await repo.CancelAsync(actor,ownerTree.Id,default); await trees.EnqueueAsync(recursiveMode,default);
                denied = await Assert.ThrowsAsync<InvalidOperationException>(()=>repo.EnqueueAsync(Row(path),default));
                Assert.Equal("CONTAINER_RECURSIVE_ACL_RESTORE_TARGET_BUSY",denied.Message);
                await trees.CancelAsync(actor with {Subject=recursiveMode.ActorSubject},recursiveMode.Id,default);
                var restoring=Row(path);await repo.EnqueueAsync(restoring,default);var ordinary=OriginalAclPeer(peerPath);
                var originals=new ContainerRecursiveAclRepository(second,protector,clock);
                denied=await Assert.ThrowsAsync<InvalidOperationException>(()=>originals.EnqueueAsync(ordinary,default));Assert.Equal("CONTAINER_RECURSIVE_ACL_TARGET_BUSY",denied.Message);
                await repo.CancelAsync(actor,restoring.Id,default);await originals.EnqueueAsync(ordinary,default);
                denied=await Assert.ThrowsAsync<InvalidOperationException>(()=>repo.EnqueueAsync(Row(path),default));Assert.Equal("CONTAINER_RECURSIVE_ACL_RESTORE_TARGET_BUSY",denied.Message);
                await originals.CancelAsync(actor with {Subject=ordinary.ActorSubject},ordinary.Id,default);
            }
            // The restore request has no ordinary ACL target fields; missing/extra sources fail closed.
            var shapeSeed=Row();await repo.EnqueueAsync(shapeSeed,default);
            foreach(var field in new[]{"CaptureId","ExpectedHistoryDigest","ExpectedVersion"})
            foreach(var omitted in new[]{true,false})
            {
                await using var transaction=await second.Database.BeginTransactionAsync();var badId=Guid.NewGuid();var badKey=Guid.NewGuid();
                second.OperationJobs.Add(new OperationJob{Id=badId,ActorSubject="native-restore-shape",IdempotencyKey=badKey.ToString("D"),JobType="native.restore.shape"});await second.SaveChangesAsync();
                var failure=await Assert.ThrowsAsync<PostgresException>(()=>second.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO container_recursive_acl_restore_intents SELECT (jsonb_populate_record(NULL::container_recursive_acl_restore_intents,to_jsonb(t) || jsonb_build_object('id',{badId},'idempotency_key',{badKey},'command_json',(CASE WHEN {omitted} THEN t.command_json::jsonb - {field} ELSE jsonb_set(t.command_json::jsonb,ARRAY[{field}],'null'::jsonb) END)::text))).* FROM container_recursive_acl_restore_intents t WHERE id={shapeSeed.Id}"));
                Assert.Equal(PostgresErrorCodes.CheckViolation,failure.SqlState);Assert.Equal("ck_container_recursive_acl_restore_requested_acl",failure.ConstraintName);await transaction.RollbackAsync();second.ChangeTracker.Clear();
            }
            await repo.CancelAsync(actor,shapeSeed.Id,default);
            foreach(var defaults in new[]{false,true})
            {
                var completed=await Applying(await Ready(Row(defaults:defaults)));Assert.True(await repo.FinishAsync(completed,"Succeeded","CONTAINER_RECURSIVE_ACL_RESTORE_COMPLETED",Receipt(completed,"Completed"),default));
                var stored=(await other.FindAsync(completed.Id,default))!;Assert.True(ContainerRecursiveAclRestoreWorkflowValidation.HasValidBinding(stored,protector));
                Assert.Equal(defaults?"default":"access",ContainerRecursiveAclRestoreWorkflowValidation.ReadPlan(stored).RestoreSource.ChangedAclType);Assert.NotNull(stored.ReceiptHash);
                var facts=await JobFacts(stored,true,"Succeeded");Assert.Equal(4,facts.CompletedCount);Assert.Equal(2,facts.ChangedCount);Assert.False(facts.PublishBarrier);
            }
            // PostgreSQL itself rejects malformed source/frame/targets, independently of the application validator.
            var validPlan=Plan(Row());var validJson=ContainerRecursiveAclRestoreWorkflowValidation.Serialize(validPlan);
            var commandJson=ContainerRecursiveAclRestoreWorkflowValidation.Serialize(validPlan.Command);
            Assert.True(await db.Database.SqlQuery<bool>($"SELECT whaledeck_recursive_acl_restore_plan_valid({validJson}::jsonb,{commandJson}::jsonb) AS \"Value\"").SingleAsync());
            foreach(var field in new[]{"RestoreSource","RestoreSourceFrame","RestoreTargetsDigest"})
            {
                var bad=System.Text.Json.Nodes.JsonNode.Parse(validJson)!;bad.AsObject().Remove(field);
                Assert.False(await db.Database.SqlQuery<bool>($"SELECT whaledeck_recursive_acl_restore_plan_valid({bad.ToJsonString()}::jsonb,{commandJson}::jsonb) AS \"Value\"").SingleAsync());
            }
            var invalidSources=new (string Field,System.Text.Json.Nodes.JsonNode? Value)[]{("CaptureId",System.Text.Json.Nodes.JsonValue.Create(Guid.NewGuid().ToString("N"))),("HistoryDigest",System.Text.Json.Nodes.JsonValue.Create(new string('0',64))),("ChangedAclType",System.Text.Json.Nodes.JsonValue.Create("other")),("EntryCount",System.Text.Json.Nodes.JsonValue.Create(3)),("CreatedAtUnixMs",System.Text.Json.Nodes.JsonValue.Create(-1))};
            foreach(var (field,value) in invalidSources)
            {
                var bad=System.Text.Json.Nodes.JsonNode.Parse(validJson)!;bad["RestoreSource"]![field]=value;
                Assert.False(await db.Database.SqlQuery<bool>($"SELECT whaledeck_recursive_acl_restore_plan_valid({bad.ToJsonString()}::jsonb,{commandJson}::jsonb) AS \"Value\"").SingleAsync());
            }
            foreach(var invalidInstance in new[]{"1:2026-10-30T01:02:03Z","2147483648:2026-10-30T01:02:03Z","777:2026-02-30T01:02:03Z","777:2026-10-30T24:02:03Z","777:0000-10-30T01:02:03Z"})
            {
                var bad=System.Text.Json.Nodes.JsonNode.Parse(validJson)!;bad["RestoreSource"]!["SourceInstanceId"]=invalidInstance;
                Assert.False(await db.Database.SqlQuery<bool>($"SELECT whaledeck_recursive_acl_restore_plan_valid({bad.ToJsonString()}::jsonb,{commandJson}::jsonb) AS \"Value\"").SingleAsync());
            }
            foreach(var (field,value) in new (string,long)[]{("Mode",33188),("OwnerUid",1),("OwnerGid",1),("LinkCount",2),("SizeBytes",0),("SizeBytes",16777217),("Inode",0)})
            {
                var bad=System.Text.Json.Nodes.JsonNode.Parse(validJson)!;bad["RestoreSourceFrame"]![field]=value;
                Assert.False(await db.Database.SqlQuery<bool>($"SELECT whaledeck_recursive_acl_restore_plan_valid({bad.ToJsonString()}::jsonb,{commandJson}::jsonb) AS \"Value\"").SingleAsync());
            }
            await PeerSeeds();
            var original = Row(); var ready = await Ready(original);
            await Rejected($"UPDATE container_recursive_acl_restore_intents SET relative_path='elsewhere' WHERE id={ready.Id}", "container_recursive_acl_restore_request_immutable");
            await Rejected($"UPDATE container_recursive_acl_restore_intents SET plan_hash={new string('A', 64)} WHERE id={ready.Id}", "container_recursive_acl_restore_plan_immutable");
            var confirmed = await Task.WhenAll(repo.ConfirmAsync(actor, Claims(ready), ready.Version, default), other.ConfirmAsync(actor, Claims(ready), ready.Version, default));
            Assert.Equal(confirmed[0].ExecutionJobId, confirmed[1].ExecutionJobId); Assert.Equal(2, await db.OperationJobs.CountAsync(j => j.Id == ready.Id || j.Id == confirmed[0].ExecutionJobId));
            var apply = Assert.IsType<ContainerRecursiveAclRestoreIntent>(await repo.ClaimAsync(clock.GetUtcNow(), default));
            Assert.Equal(ContainerFileUploadStartResult.Started, await repo.StartedAsync(apply, clock.GetUtcNow(), default));
            await Rejected($"UPDATE container_recursive_acl_restore_intents SET root_started_at_utc=NULL WHERE id={apply.Id}", "container_recursive_acl_restore_started_immutable");
            var missingProof = await Assert.ThrowsAsync<PostgresException>(() => second.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE container_recursive_acl_restore_intents SET state='Succeeded',completed_at_utc={clock.GetUtcNow()},publish_barrier=false WHERE id={apply.Id}"));
            Assert.Equal(PostgresErrorCodes.CheckViolation, missingProof.SqlState);
            Assert.Equal("ck_container_recursive_acl_restore_result", missingProof.ConstraintName);
            Assert.True(await repo.FinishAsync(apply, "Succeeded", "CONTAINER_RECURSIVE_ACL_RESTORE_COMPLETED", Receipt(apply, "Completed"), default));
            var complete = (await other.FindAsync(ready.Id, default))!; Assert.Equal("Completed", complete.RootState); Assert.Equal(4, complete.CompletedCount); Assert.Equal(2, complete.ChangedCount); Assert.True(complete.HistoryMayExist); Assert.True(complete.HistoryVerified); Assert.Equal(new string('8', 64), complete.HistoryDigest);
            await Rejected($"UPDATE container_recursive_acl_restore_intents SET root_state='Aborted' WHERE id={ready.Id}", "container_recursive_acl_restore_fact_immutable");
            await Rejected($"UPDATE container_recursive_acl_restore_intents SET history_verified=false WHERE id={ready.Id}", "container_recursive_acl_restore_fact_immutable");
            await Rejected($"UPDATE container_recursive_acl_restore_intents SET history_digest=NULL WHERE id={ready.Id}", "container_recursive_acl_restore_fact_immutable");
            await Rejected($"UPDATE container_recursive_acl_restore_intents SET version=version+1 WHERE id={ready.Id}", "container_recursive_acl_restore_terminal_immutable");
            await Rejected($"DELETE FROM container_recursive_acl_restore_intents WHERE id={ready.Id}", "container_recursive_acl_restore_permanent_intent");
            // Ten-way SQL gates: no raw write bypasses the application checks.
            var seeds = new Dictionary<string, Guid> { ["container_file_upload_operations"] = uploadIds.Single(), ["container_directory_operations"] = directoryIds.Single(),
                ["container_copy_intents"] = await db.ContainerCopyIntents.Where(r => r.ExecutionJobId != null).Select(r => r.Id).FirstAsync(),
                ["container_move_intents"] = await db.ContainerMoveIntents.Where(r => r.ExecutionJobId != null).Select(r => r.Id).FirstAsync(),
                ["container_permission_intents"] = await db.ContainerPermissionIntents.Where(r => r.ExecutionJobId != null).Select(r => r.Id).FirstAsync(),
                ["container_ownership_intents"] = await db.ContainerOwnershipIntents.Where(r => r.ExecutionJobId != null).Select(r => r.Id).FirstAsync(),
                ["container_recursive_acl_intents"] = await db.ContainerRecursiveAclIntents.Where(r => r.ExecutionJobId != null).Select(r => r.Id).FirstAsync(),
                ["container_acl_intents"] = await db.ContainerAclIntents.Where(r => r.ExecutionJobId != null).Select(r => r.Id).FirstAsync(),
                ["container_recursive_ownership_intents"] = await db.ContainerRecursiveOwnershipIntents.Where(r => r.ExecutionJobId != null).Select(r => r.Id).FirstAsync(),
                ["container_recursive_permission_intents"] = await db.ContainerRecursivePermissionIntents.Where(r => r.ExecutionJobId != null).Select(r => r.Id).FirstAsync() };
            var blocked = await Applying(await Ready(Row())); var pathHash = FileManagementService.Hash("native-recursive-host:" + blocked.Id);
            await Rejected($"INSERT INTO file_path_locks(path_hash,operation_id,scope_kind) VALUES ({pathHash},{blocked.Id},'Legacy')", "FILES_TARGET_BUSY");
            foreach (var pair in seeds)
            {
                await using var transaction = await second.Database.BeginTransactionAsync();
                var error = await Record.ExceptionAsync(() => ClonePeer(pair.Key, pair.Value));
                Assert.NotNull(error); Assert.Equal(PostgresErrorCodes.ObjectNotInPrerequisiteState, Assert.IsType<PostgresException>(error.GetBaseException()).SqlState);
                await transaction.RollbackAsync(); second.ChangeTracker.Clear();
            }
            var uncertain = Receipt(blocked, "Unknown") with { HistoryVerified = true, HistoryDigest = new('7', 64) };
            Assert.True(await repo.FinishAsync(blocked, "Unknown", "CONTAINER_RECURSIVE_ACL_RESTORE_RESULT_UNKNOWN", uncertain, default));
            var unknown = (await other.FindAsync(blocked.Id, default))!; Assert.True(unknown.PublishBarrier); Assert.Equal("Unknown", unknown.RootState); Assert.Equal(4, unknown.UnknownCount); Assert.True(unknown.HistoryVerified);
            var unknownFacts=await JobFacts(unknown,true,"Unknown");Assert.Equal(4,unknownFacts.UnknownCount);Assert.True(unknownFacts.PublishBarrier);
            Assert.Null(await repo.ClaimAsync(clock.GetUtcNow(), default));
            await Rejected($"UPDATE container_recursive_acl_restore_intents SET history_may_exist=false WHERE id={unknown.Id}", "container_recursive_acl_restore_fact_immutable");
            var admin = actor with { Subject = "native-current-permission-admin", LoginBinding = new('F', 64) };
            await other.AcknowledgeAsync(admin, unknown.Id, unknown.Version, uncertain, default);
            var ack = (await repo.FindAsync(unknown.Id, default))!; Assert.Equal("Acknowledged", ack.State); Assert.False(ack.PublishBarrier); Assert.Equal("Unknown", ack.RootState);
            Assert.Equal(4, ack.UnknownCount); Assert.True(ack.HistoryVerified); Assert.Equal(admin.Subject, ack.AcknowledgedBy);
            var ackFacts=await JobFacts(ack,true,"Acknowledged");Assert.Equal("Unknown",ackFacts.ReceiptState);Assert.Equal(4,ackFacts.UnknownCount);Assert.False(ackFacts.PublishBarrier);
            Assert.Equal("Failed", (await db.OperationJobs.AsNoTracking().SingleAsync(j => j.Id == ack.ExecutionJobId)).State);
            var partial = await Applying(await Ready(Row())); var partialReceipt = Receipt(partial, "Partial");
            Assert.True(await repo.FinishAsync(partial, "Partial", "CONTAINER_RECURSIVE_ACL_RESTORE_PARTIAL_REQUIRES_ACKNOWLEDGEMENT", partialReceipt, default));
            var paused = (await other.FindAsync(partial.Id, default))!;
            var partialFacts=await JobFacts(paused,true,"Partial");Assert.Equal(1,partialFacts.ChangedCount);Assert.True(partialFacts.PublishBarrier);
            Assert.Equal("Partial", paused.State); Assert.Null(paused.CompletedAtUtc); Assert.True(paused.PublishBarrier);
            Assert.Equal(1, paused.ChangedCount); Assert.Equal(3, paused.NotStartedCount);
            Assert.Equal(partialReceipt, ContainerRecursiveAclRestoreWorkflowValidation.ReadReceipt(paused));
            Assert.NotNull(paused.ReceiptHash);Assert.True(ContainerRecursiveAclRestoreWorkflowValidation.HasValidBinding(paused,protector));
            await Rejected($"UPDATE container_recursive_acl_restore_intents SET receipt_hash={new string('7',64)} WHERE id={paused.Id}","container_recursive_acl_restore_receipt_authority_required");
            var observation=partialReceipt with {Replayed=true};var observedJson=ContainerRecursiveAclRestoreWorkflowValidation.Serialize(observation);
            await Rejected($"UPDATE container_recursive_acl_restore_intents SET receipt_json={observedJson} WHERE id={paused.Id}","container_recursive_acl_restore_receipt_authority_required");
            await using(var transaction=await second.Database.BeginTransactionAsync())
            {
                await second.Database.ExecuteSqlInterpolatedAsync($"UPDATE container_recursive_acl_restore_intents SET receipt_json={observedJson},receipt_hash={new string('7',64)} WHERE id={paused.Id}");
                var forged=(await other.FindAsync(paused.Id,default))!;Assert.NotNull(ContainerRecursiveAclRestoreWorkflowValidation.ReadReceipt(forged));Assert.False(ContainerRecursiveAclRestoreWorkflowValidation.HasValidBinding(forged,protector));
                await transaction.RollbackAsync();second.ChangeTracker.Clear();
            }

            Assert.Equal("Running", (await second.OperationJobs.AsNoTracking().SingleAsync(j => j.Id == paused.ExecutionJobId)).State);
            Assert.Null(await repo.ClaimAsync(clock.GetUtcNow(), default));
            await Rejected($"UPDATE container_recursive_acl_restore_intents SET changed_count=0 WHERE id={paused.Id}", "container_recursive_acl_restore_fact_immutable");
            await Rejected($"UPDATE container_recursive_acl_restore_intents SET state='Pending' WHERE id={paused.Id}", "container_recursive_acl_restore_resolution_required");
            await Assert.ThrowsAsync<InvalidOperationException>(() => other.AcknowledgeAsync(admin, paused.Id, paused.Version, Receipt(partial, "Aborted"), default));
            await new JobRepository(second).RequestCancellationAsync(paused.ExecutionJobId!.Value, actor.Subject, default);
            Assert.True(await db.Database.SqlQuery<bool>($"SELECT EXISTS (SELECT 1 FROM outbox_messages WHERE message_type='ContainerRecursiveAclRestoreCancellationRequested' AND payload_json->>'jobId'={paused.ExecutionJobId.Value.ToString("D")}) AS \"Value\"").SingleAsync());
            await other.AcknowledgeAsync(admin, paused.Id, paused.Version, partialReceipt with { HistoryAvailable = false, Replayed = true }, default);
            var resolvedPartial = (await repo.FindAsync(paused.Id, default))!;
            var resolvedFacts=await JobFacts(resolvedPartial,true,"Acknowledged");Assert.Equal("Partial",resolvedFacts.ReceiptState);Assert.Equal(1,resolvedFacts.ChangedCount);Assert.False(resolvedFacts.PublishBarrier);
            Assert.Equal("Acknowledged", resolvedPartial.State); Assert.Equal("Partial", resolvedPartial.RootState); Assert.Equal(1, resolvedPartial.ChangedCount);
            Assert.True(resolvedPartial.HistoryVerified); Assert.False(resolvedPartial.HistoryAvailable); Assert.False(resolvedPartial.PublishBarrier);
            Assert.Equal("Failed", (await db.OperationJobs.AsNoTracking().SingleAsync(j => j.Id == resolvedPartial.ExecutionJobId)).State);
            // Original natural lease expires on the synthetic test clock, not SQL edits.
            // An unreturned Root result is not a row of invented zeroes.
            var unproved=await Applying(await Ready(Row()));
            Assert.True(await repo.FinishAsync(unproved,"Unknown","CONTAINER_RECURSIVE_ACL_RESTORE_RESULT_UNKNOWN",null,default));
            var unprovedRow=(await other.FindAsync(unproved.Id,default))!;
            var unprovedFacts=await JobFacts(unprovedRow,true,"Unknown");Assert.Null(unprovedFacts.CompletedCount);Assert.Null(unprovedFacts.UnknownCount);Assert.Null(unprovedFacts.ReceiptState);Assert.True(unprovedFacts.PublishBarrier);
            await other.AcknowledgeAsync(admin,unprovedRow.Id,unprovedRow.Version,Receipt(unproved,"Unknown"),default);
            await JobFacts((await repo.FindAsync(unproved.Id,default))!,true,"Acknowledged");
            var planningFailure=Row();await repo.EnqueueAsync(planningFailure,default);var failedClaim=Assert.IsType<ContainerRecursiveAclRestoreIntent>(await repo.ClaimAsync(clock.GetUtcNow(),default));Assert.Equal(planningFailure.Id,failedClaim.Id);
            await repo.FinishPlanningAsync(failedClaim,null,"CONTAINER_RECURSIVE_ACL_RESTORE_SOURCE_NOT_FOUND",default);
            var failedFacts=await JobFacts((await repo.FindAsync(planningFailure.Id,default))!,false,"Failed");Assert.Null(failedFacts.EntryCount);Assert.Null(failedFacts.AclType);Assert.Null(failedFacts.ChangedCount);
            var queuedCancel=Row();await repo.EnqueueAsync(queuedCancel,default);await repo.CancelAsync(actor,queuedCancel.Id,default);
            await JobFacts((await repo.FindAsync(queuedCancel.Id,default))!,false,"Canceled");
            var abandoned = await Applying(await Ready(Row())); var oldToken = abandoned.LeaseToken;
            clock.Offset = TimeSpan.FromSeconds(91); var reclaimed = Assert.IsType<ContainerRecursiveAclRestoreIntent>(await other.ClaimAsync(clock.GetUtcNow(), default));
            Assert.Equal(abandoned.Id, reclaimed.Id); Assert.NotEqual(oldToken, reclaimed.LeaseToken); Assert.NotNull(reclaimed.RootStartedAtUtc); Assert.True(reclaimed.PublishBarrier);
            Assert.False(await repo.RenewAsync(abandoned, clock.GetUtcNow(), default));
            Assert.Equal(ContainerFileUploadStartResult.LeaseLost, await other.StartedAsync(reclaimed, clock.GetUtcNow(), default));
            Assert.True(await other.FinishAsync(reclaimed, "Succeeded", "CONTAINER_RECURSIVE_ACL_RESTORE_COMPLETED", Receipt(reclaimed, "Completed"), default));
            clock.Offset = TimeSpan.Zero;
            var inverse = await Pending(await Ready(Row()));
            foreach (var pair in seeds)
            {
                await using var transaction = await second.Database.BeginTransactionAsync(); await ClonePeer(pair.Key, pair.Value);
                var error = await Assert.ThrowsAsync<PostgresException>(() => second.Database.ExecuteSqlInterpolatedAsync($"UPDATE container_recursive_acl_restore_intents SET state='Applying',root_started_at_utc={clock.GetUtcNow()},publish_barrier=true WHERE id={inverse.Id}"));
                Assert.Equal("CONTAINER_RECURSIVE_ACL_RESTORE_TARGET_BUSY", error.MessageText); await transaction.RollbackAsync(); second.ChangeTracker.Clear();
            }
            await second.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO file_path_locks(path_hash,operation_id,scope_kind) VALUES ({pathHash},{inverse.Id},'Legacy')");
            Assert.Equal(ContainerFileUploadStartResult.Busy, await repo.StartedAsync(inverse, clock.GetUtcNow(), default));
            await Rejected($"UPDATE container_recursive_acl_restore_intents SET state='Applying',root_started_at_utc={clock.GetUtcNow()},publish_barrier=true WHERE id={inverse.Id}", "CONTAINER_RECURSIVE_ACL_RESTORE_TARGET_BUSY");
            await second.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM file_path_locks WHERE operation_id={inverse.Id}");
            await repo.CancelAsync(actor, inverse.Id, default); Assert.True(await repo.FinishAsync(inverse, "Canceled", "CONTAINER_RECURSIVE_ACL_RESTORE_CANCELED", Receipt(inverse, "Aborted"), default));
            var canceled = (await other.FindAsync(inverse.Id, default))!; Assert.Equal("Aborted", canceled.RootState); Assert.Equal(0, canceled.ChangedCount); Assert.False(canceled.HistoryMayExist); Assert.Null(canceled.HistoryDigest);
            var canceledFacts=await JobFacts(canceled,true,"Canceled");Assert.Equal(0,canceledFacts.ChangedCount);Assert.Equal(4,canceledFacts.NotStartedCount);
            var expiredReady = await Ready(Row()); clock.Offset = TimeSpan.FromMinutes(6);
            await Assert.ThrowsAsync<InvalidOperationException>(() => repo.ConfirmAsync(actor, Claims(expiredReady), expiredReady.Version, default));
            await repo.CancelAsync(actor, expiredReady.Id, default); await JobFacts(expiredReady,false,"Ready"); clock.Offset = TimeSpan.Zero;
            // Audit failure is part of the same SQL transaction as the intent/jobs/outbox.
            var rejected = Row();var rejectedFinish=await Applying(await Ready(Row()));
            await second.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION wdv_recursive_acl_restore_audit_reject() RETURNS trigger LANGUAGE plpgsql AS $f$
                BEGIN RAISE EXCEPTION 'wdv_recursive_acl_restore_audit_failed' USING ERRCODE='23514'; END $f$;
                CREATE TRIGGER wdv_recursive_acl_restore_audit_reject BEFORE INSERT ON audit_events
                  FOR EACH ROW WHEN (NEW.action LIKE 'containers.files.recursive-acl-restore.%') EXECUTE FUNCTION wdv_recursive_acl_restore_audit_reject();
                """);
            try
            {
                await Assert.ThrowsAsync<DbUpdateException>(() => repo.EnqueueAsync(rejected, default));
                await Assert.ThrowsAsync<DbUpdateException>(() => repo.FinishAsync(rejectedFinish,"Failed","CONTAINER_RECURSIVE_ACL_RESTORE_INTERRUPTED",Receipt(rejectedFinish,"Aborted"),default));
                Assert.Equal("Applying",(await other.FindAsync(rejectedFinish.Id,default))!.State);
                var rolledBack=await second.OperationJobs.AsNoTracking().SingleAsync(j=>j.Id==rejectedFinish.ExecutionJobId);Assert.Equal("Running",rolledBack.State);Assert.Null(rolledBack.ResultJson);
            }
            finally { await second.Database.ExecuteSqlRawAsync("DROP TRIGGER wdv_recursive_acl_restore_audit_reject ON audit_events; DROP FUNCTION wdv_recursive_acl_restore_audit_reject();"); }
            Assert.True(await repo.FinishAsync(rejectedFinish,"Failed","CONTAINER_RECURSIVE_ACL_RESTORE_INTERRUPTED",Receipt(rejectedFinish,"Aborted"),default));
            await JobFacts((await repo.FindAsync(rejectedFinish.Id,default))!,true,"Failed");
            Assert.False(await second.ContainerRecursiveAclRestoreIntents.AnyAsync(r => r.Id == rejected.Id)); Assert.False(await second.OperationJobs.AnyAsync(j => j.Id == rejected.Id));
            var waiting = new List<ContainerRecursiveAclRestoreIntent>();
            for (var n = 0; n < 4; n++) { var row = Row(); await repo.EnqueueAsync(row, default); waiting.Add(row); }
            var over = Row(); await Assert.ThrowsAsync<InvalidOperationException>(() => repo.EnqueueAsync(over, default));
            var peerBudget = PermissionPeer("work/nonoverlap-budget-" + Guid.NewGuid().ToString("N"), actor.Subject);
            var budgetError = await Assert.ThrowsAsync<InvalidOperationException>(() => new ContainerPermissionRepository(second, protector, clock).EnqueueAsync(peerBudget, default));
            Assert.Equal("FILES_OPERATION_BUDGET_EXHAUSTED", budgetError.Message);

            foreach (var row in waiting) await repo.CancelAsync(actor, row.Id, default);
            var lockRejected = Row();
            await using (var holding = await second.Database.BeginTransactionAsync())
            {
                await second.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(41307341)");
                var timeout = await Record.ExceptionAsync(() => repo.EnqueueAsync(lockRejected, default));
                Assert.NotNull(timeout); Assert.Equal(PostgresErrorCodes.LockNotAvailable, Assert.IsType<PostgresException>(timeout.GetBaseException()).SqlState);
                await holding.RollbackAsync(); second.ChangeTracker.Clear();
            }
            Assert.False(await db.ContainerRecursiveAclRestoreIntents.AnyAsync(r => r.Id == lockRejected.Id));
            Assert.False(await db.OperationJobs.AnyAsync(j => j.Id == lockRejected.Id));
            var downgrade = await Assert.ThrowsAsync<PostgresException>(() => db.GetService<IMigrator>().MigrateAsync(previous));
            Assert.Equal("container_recursive_acl_restore_downgrade_requires_empty_table", downgrade.MessageText); await db.Database.MigrateAsync();
            Assert.False(await db.ContainerRecursiveAclRestoreIntents.AnyAsync(r => r.CompletedAtUtc == null || r.PublishBarrier || r.LeaseToken != null));

            Assert.DoesNotContain(await db.OperationJobs.Where(j => ids.Contains(j.Id) || db.ContainerRecursiveAclRestoreIntents.Any(r => r.ExecutionJobId == j.Id && ids.Contains(r.Id))).ToArrayAsync(), j => j.State is "Queued" or "Running");
            Console.WriteLine("INBOX_PHASE=container-recursive-acl-restore-persistent-manifest-partial-unknown-immutable-both-fences-audited");
        }
        finally
        {
            // Only exact synthetic legacy fixtures are deleted; permanent new rows remain until isolated DB removal.
            await second.ContainerFileUploadOperations.Where(r => uploadIds.Contains(r.Id)).ExecuteDeleteAsync();
            await second.ContainerDirectoryOperations.Where(r => directoryIds.Contains(r.Id)).ExecuteDeleteAsync(); File.Delete(keyPath);
        }
    }
    private sealed class RecursiveAclRestoreClock : TimeProvider { public TimeSpan Offset { get; set; } public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow.Add(Offset); }
}
