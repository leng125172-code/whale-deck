using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Contracts;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Agent;
using WhaleDeck.Infrastructure.Persistence;
using Rpc = WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.IntegrationTests;

public sealed partial class NativeNotificationDispatchTests
{
    // Real isolated SQL, migration, ownership, atomic audit and publication
    // barrier. The Root journal below is an explicit synthetic transport.
    private static async Task ContainerFileUploadRecoveryBoundaries(DbContextOptions<PlatformDbContext> options)
    {
        await using var db = new PlatformDbContext(options);
        var keyFile = Path.GetTempFileName(); await File.WriteAllBytesAsync(keyFile,RandomNumberGenerator.GetBytes(32));
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(keyFile,UnixFileMode.UserRead|UnixFileMode.UserWrite);
        var protector = new FilePlanProtector(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Agent:CapabilityKeyFile"] = keyFile }).Build());
        var material = new FileRecoveryMaterial(protector,protector,protector,protector,protector,protector,protector,protector,protector,protector,protector,protector);
        var clock = new NativeRecoveryClock(); var logins = new NativeContainerRecoveryLogins(); var identity = new NativeRecoveryIdentity();
        var authorization = new FileManagementService(null!,null!,null!,identity,clock);
        FileRecoveryRepository Repository(PlatformDbContext context) => new(context,new(context,material,clock),material,new(context,protector),protector,clock);
        var repository = Repository(db); var service = new FileRecoveryService(authorization,repository,protector,clock,logins);
        var gateway = new NativeContainerRecoveryGateway(material); var worker = new FileRecoveryDispatcher(repository,gateway,identity,protector,clock,logins);
        var actor = new TerminalActor(NativeRecoveryIdentity.CurrentSubject,null,[],new('F',64),"native-container-recovery",null);
        var oldActor = new TerminalActor("native-old-container-owner",null,[],new('A',64),"native-old-container-trace",null);
        var uploads = new ContainerFileUploadRepository(db,protector); var ids = new List<Guid>();
        ContainerFileUploadOperation Row(string path, string cid)
        {
            var now = ContainerFileUploadIntentValidation.CanonicalTime(clock.GetUtcNow()); var id = Guid.NewGuid(); ids.Add(id);
            var target = new ContainerFileUploadTargetDto(new(cid,"777:2026-10-27T01:02:03.123456789Z",path),new('B',64),new('C',64),"ABSENT",0);
            var binding = new ContainerFileUploadBinding(id,Guid.NewGuid(),oldActor.Subject,oldActor.LoginBinding,oldActor.TraceId,target,17,new('D',64),new('E',64),now,now.AddMinutes(15));
            return new() { Id = id,IdempotencyKey = binding.IdempotencyKey,ActorSubject = oldActor.Subject,LoginBinding = oldActor.LoginBinding,TraceId = oldActor.TraceId,
                OwnerHash = ContainerFileUploadIntentValidation.Owner(oldActor.Subject,oldActor.LoginBinding),ContainerId = cid,InstanceId = target.Location.InstanceId,RelativePath = path,
                TargetProof = target.TargetProof,ExpectedParentVersion = target.ExpectedParentVersion,ExpectedVersion = target.ExpectedVersion,PlanHash = binding.PlanHash,
                RequestHash = protector.FingerprintContainerUpload(binding),Sha256 = binding.Sha256,SizeBytes = binding.SizeBytes,CreatedAtUtc = now,ExpiresAtUtc = binding.ExpiresAtUtc,NextAttemptAtUtc = now };
        }
        async Task<ContainerFileUploadOperation> Ready(ContainerFileUploadOperation row)
        {
            await uploads.EnqueueAsync(row,default);
            var receiving = await uploads.BeginReceiveAsync(oldActor,row.Id,clock.GetUtcNow(),default); Assert.True(await uploads.ReadyAsync(receiving,default));
            await db.ContainerFileUploadOperations.Where(item => item.Id==row.Id).ExecuteUpdateAsync(update => update.SetProperty(item => item.NextAttemptAtUtc,clock.GetUtcNow()));
            return Assert.IsType<ContainerFileUploadOperation>(await uploads.ClaimAsync(clock.GetUtcNow(),default));
        }
        async Task<ContainerFileUploadOperation> Original()
        {
            var claim = await Ready(Row("private/recovery.bin",new('a',64)));
            Assert.Equal(ContainerFileUploadStartResult.Started,await uploads.StartedAsync(claim,clock.GetUtcNow(),default));
            Assert.True(await uploads.FinishAsync(claim,"Unknown","CONTAINER_UPLOAD_RESULT_UNKNOWN",null,default));
            return (await uploads.OwnedAsync(oldActor,claim.Id,default))!;
        }
        try
        {
            var original = await Original(); Assert.True(original.PublishBarrier); Assert.Equal("Unknown",original.State);
            var immutable = ContainerFileUploadIntentValidation.ReadBinding(original);
            var inventory = new FileRecoveryInventory(db,protector,protector,protector,protector,protector,protector,protector,protector,protector,protector,protector,protector);
            var candidate = (await inventory.GetAsync(FileRecoveryKinds.ContainerUpload,original.Id,default))!;
            Assert.Equal("Verified",candidate.Integrity); Assert.Null(candidate.Source); Assert.Null(candidate.Target);
            Assert.Equal(new(original.ContainerId,original.InstanceId,original.RelativePath),candidate.Container);
            Assert.Contains((await inventory.ListAsync(null,50,default)).Entries,item => item.Kind==FileRecoveryKinds.ContainerUpload&&item.IntentId==original.Id);
            var command = new QueueFileRecoveryCommand(FileRecoveryKinds.ContainerUpload,original.Id,original.Version,"AcknowledgeUnknown","OperatorHandover");
            logins.Active = false;
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.QueueAsync(actor,command,Guid.NewGuid().ToString("D"),default));
            Assert.False(await db.FileRecoveryOperations.AnyAsync(item => item.OriginalIntentId==original.Id));
            logins.Active = true;
            await Assert.ThrowsAsync<ArgumentException>(() => service.QueueAsync(actor,command with { Disposition="ReconcileCompleted" },Guid.NewGuid().ToString("D"),default));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.QueueAsync(actor,command with { OriginalVersion=original.Version+1 },Guid.NewGuid().ToString("D"),default));
            var key = Guid.NewGuid().ToString("D"); var intent = await service.QueueAsync(actor,command,key,default);
            Assert.Equal(intent,await service.QueueAsync(actor,command,key,default));
            // Even a queued current-admin plan cannot reach Root after logout
            // or when the liveness dependency has not been registered.
            logins.Active = false; Assert.True(await worker.TickAsync(default)); logins.Active = true;
            Assert.Equal("Failed",(await service.GetAsync(actor,intent.Id,default)).State); Assert.Equal(0,gateway.Inspections);
            intent = await service.QueueAsync(actor,command,Guid.NewGuid().ToString("D"),default);
            Assert.True(await new FileRecoveryDispatcher(repository,gateway,identity,protector,clock).TickAsync(default));
            Assert.Equal("Failed",(await service.GetAsync(actor,intent.Id,default)).State); Assert.Equal(0,gateway.Inspections);
            intent = await service.QueueAsync(actor,command,Guid.NewGuid().ToString("D"),default);
            await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetAsync(actor with { LoginBinding=new('E',64) },intent.Id,default));
            Assert.True(await worker.TickAsync(default)); var ready = await service.GetAsync(actor,intent.Id,default);
            Assert.Equal("Ready",ready.State); Assert.Equal("Unknown",ready.RootState); Assert.Equal(1,gateway.Inspections); Assert.Equal(0,gateway.Fences);
            Assert.True((await uploads.OwnedAsync(oldActor,original.Id,default))!.PublishBarrier);
            var disposition = await Assert.ThrowsAsync<PostgresException>(() => db.FileRecoveryOperations.Where(item => item.Id==ready.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(item => item.Disposition,"ReconcileCompleted")));
            Assert.Equal("23514",disposition.SqlState);
            var probeId = Guid.NewGuid();
            var forbiddenInsert = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO file_recovery_operations
                SELECT (jsonb_populate_record(NULL::file_recovery_operations,to_jsonb(f) ||
                  jsonb_build_object('id',{probeId},'idempotency_key',{probeId.ToString("D")},'disposition','ReconcileCompleted'))).*
                FROM file_recovery_operations f WHERE f.id={ready.Id}
                """));
            Assert.Equal("23514",forbiddenInsert.SqlState); Assert.Equal("ck_file_recovery_kind",forbiddenInsert.ConstraintName);

            var competitor = await Ready(Row("private/other.bin",new('b',64)));
            Assert.Equal(ContainerFileUploadStartResult.Busy,await uploads.StartedAsync(competitor,clock.GetUtcNow(),default));
            await uploads.DeferAsync(competitor,default);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ConfirmAsync(actor,ready.Id,new(ready.Confirmation!.PlanHash,true,ready.Version+1),default));
            logins.Active = false;
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ConfirmAsync(actor,ready.Id,new(ready.Confirmation!.PlanHash,true,ready.Version),default));
            Assert.Equal(0,gateway.Fences); logins.Active = true;
            var accepted = await service.ConfirmAsync(actor,ready.Id,new(ready.Confirmation!.PlanHash,true,ready.Version),default);
            gateway.LoseFirstSealReply = true;
            Assert.True(await worker.TickAsync(default));
            var interrupted = await service.GetAsync(actor,accepted.Id,default);
            Assert.Equal("Unknown",interrupted.State); Assert.Equal(1,gateway.Fences);
            Assert.True((await uploads.OwnedAsync(oldActor,original.Id,default))!.PublishBarrier);
            // A subsequent transaction failing its immutable audit must roll
            // back original status, job, Outbox and publication-barrier release.
            await db.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION wd_container_recovery_audit_fail() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN IF NEW.action='files.recovery.original-disposed' THEN RAISE EXCEPTION 'owned recovery audit failure'; END IF; RETURN NEW; END; $$;
                CREATE TRIGGER wd_container_recovery_audit_fail BEFORE INSERT ON audit_events FOR EACH ROW EXECUTE FUNCTION wd_container_recovery_audit_fail();
                """);
            try
            {
                clock.Now = clock.Now.AddSeconds(7);
                var restarted = new FileRecoveryDispatcher(Repository(db),gateway,identity,protector,clock,logins);
                Assert.True(await restarted.TickAsync(default));
                var held = (await uploads.OwnedAsync(oldActor,original.Id,default))!;
                Assert.Equal("Unknown",held.State); Assert.True(held.PublishBarrier); Assert.Null(held.CompletedAtUtc);
                Assert.Equal("Unknown",(await service.GetAsync(actor,accepted.Id,default)).State);
                Assert.False(await db.AuditEvents.AnyAsync(item => item.Action=="files.recovery.original-disposed"&&item.TargetId==accepted.Id.ToString("D")));
                Assert.Null((await db.OperationJobs.AsNoTracking().SingleAsync(item => item.Id==original.Id)).CompletedAtUtc);
            }
            finally
            {
                await db.Database.ExecuteSqlRawAsync("DROP TRIGGER wd_container_recovery_audit_fail ON audit_events; DROP FUNCTION wd_container_recovery_audit_fail();");
            }
            clock.Now = clock.Now.AddSeconds(7); Assert.True(await worker.TickAsync(default));
            var disposed = await service.GetAsync(actor,accepted.Id,default); Assert.Equal("Disposed",disposed.State); Assert.Equal("Failed",disposed.OriginalResolution);
            Assert.Equal(1,gateway.Fences); Assert.Equal(actor.Subject,gateway.LastActor!.Subject); Assert.Equal(original.ActorSubject,gateway.OriginalActor);
            var final = (await uploads.OwnedAsync(oldActor,original.Id,default))!;
            Assert.Equal("Failed",final.State); Assert.False(final.PublishBarrier); Assert.NotNull(final.CompletedAtUtc);
            Assert.Equal("CONTAINER_UPLOAD_ADMIN_ACKNOWLEDGED",final.ResultCode); Assert.Equal(immutable,ContainerFileUploadIntentValidation.ReadBinding(final));
            Assert.True(ContainerFileUploadService.HasValidBinding(final,protector)); Assert.Null(final.PublishedVersion);
            Assert.Equal("Failed",(await db.OperationJobs.AsNoTracking().SingleAsync(item => item.Id==original.Id)).State);
            Assert.Equal("Succeeded",(await db.OperationJobs.AsNoTracking().SingleAsync(item => item.Id==accepted.ExecutionJobId)).State);
            Assert.Equal(1,await db.AuditEvents.CountAsync(item => item.ActorSubject==actor.Subject&&item.Action=="files.recovery.original-disposed"&&item.TargetId==accepted.Id.ToString("D")));
            Assert.False(await db.OutboxMessages.FromSqlInterpolated($"SELECT * FROM outbox_messages WHERE payload_json->>'jobId'={original.Id.ToString("D")}").AnyAsync(item => item.ProcessedAtUtc==null));
            Assert.Null(await inventory.GetAsync(FileRecoveryKinds.ContainerUpload,original.Id,default));
            await db.ContainerFileUploadOperations.Where(item => item.Id==competitor.Id).ExecuteUpdateAsync(update => update.SetProperty(item => item.NextAttemptAtUtc,clock.GetUtcNow()));
            var next = Assert.IsType<ContainerFileUploadOperation>(await uploads.ClaimAsync(clock.GetUtcNow(),default)); Assert.Equal(competitor.Id,next.Id);
            Assert.Equal(ContainerFileUploadStartResult.Started,await uploads.StartedAsync(next,clock.GetUtcNow(),default));
            var discarded = new ContainerFileUploadReceiptDto(next.ContainerId,next.InstanceId,next.Id,next.TargetProof,new("Discarded",next.SizeBytes,next.Sha256,"",false,false,false),false);
            Assert.True(await uploads.FinishAsync(next,"Canceled","CONTAINER_UPLOAD_CANCELED",discarded,default));
            Assert.False(await db.ContainerFileUploadOperations.AnyAsync(item => ids.Contains(item.Id)&&item.PublishBarrier));
            // Loss before Root receives the seal is a different recovery path:
            // repeat only the same idempotent seal, never publish file bytes.
            var unseen = await Original(); gateway.ResetBeforeReceptionLoss();
            var unseenPlan = await service.QueueAsync(actor,new(FileRecoveryKinds.ContainerUpload,unseen.Id,unseen.Version,"AcknowledgeUnknown","IncidentReview"),Guid.NewGuid().ToString("D"),default);
            Assert.True(await worker.TickAsync(default)); var unseenReady = await service.GetAsync(actor,unseenPlan.Id,default);
            var unseenAccepted = await service.ConfirmAsync(actor,unseenReady.Id,new(unseenReady.Confirmation!.PlanHash,true,unseenReady.Version),default);
            var fencesBefore = gateway.Fences; Assert.True(await worker.TickAsync(default));
            Assert.Equal("Unknown",(await service.GetAsync(actor,unseenAccepted.Id,default)).State);
            Assert.True((await uploads.OwnedAsync(oldActor,unseen.Id,default))!.PublishBarrier);
            clock.Now = clock.Now.AddSeconds(7); Assert.True(await worker.TickAsync(default));
            Assert.Equal("Disposed",(await service.GetAsync(actor,unseenAccepted.Id,default)).State); Assert.Equal(fencesBefore+2,gateway.Fences);
            Assert.False((await uploads.OwnedAsync(oldActor,unseen.Id,default))!.PublishBarrier);
            var downgrade = await Assert.ThrowsAsync<PostgresException>(() => db.GetService<IMigrator>().MigrateAsync("20261027001436_ContainerFileUploadIntent"));
            Assert.Contains("container_upload_recovery_downgrade_requires_empty_kind",downgrade.MessageText,StringComparison.Ordinal);
            Assert.False(db.Database.HasPendingModelChanges());
            Assert.Contains("20261027015755_ContainerUploadAdministratorRecovery",await db.Database.GetAppliedMigrationsAsync());
            Console.WriteLine("INBOX_PHASE=container-recovery-current-login-seal-replay-atomic-audit-barrier");
        }
        finally
        {
            // Permanent intent/audit history stays intact, even in this native
            // fixture. The wrapper drops the whole exact disposable database.
            db.ChangeTracker.Clear(); File.Delete(keyFile);
        }
    }
    private sealed class NativeContainerRecoveryLogins : ILoginSessionRegistry
    {
        public bool Active { get; set; } = true;
        public Task<bool> IsActiveAsync(string subject,string loginBinding,CancellationToken cancellationToken) =>
            Task.FromResult(Active&&subject==NativeRecoveryIdentity.CurrentSubject&&loginBinding==new string('F',64));
    }
    private sealed class NativeContainerRecoveryGateway(FileRecoveryMaterial material) : IFileRecoveryGateway
    {
        public int Inspections { get; private set; } public int Fences { get; private set; }
        public bool LoseFirstSealReply { get; set; } public TerminalActor? LastActor { get; private set; } public string? OriginalActor { get; private set; }
        private Guid? _seal; private bool _loseBeforeSeal;
        public void ResetBeforeReceptionLoss() { _seal=null; _loseBeforeSeal=true; }
        public Task<FileRecoveryObservation> InspectForRecoveryAsync(FileRecoverySnapshot original,TerminalActor currentActor,CancellationToken cancellationToken)
        { Inspections++; return Task.FromResult(Observe(original)); }
        public Task<FileRecoveryObservation> FenceForRecoveryAsync(FileRecoverySnapshot original,TerminalActor currentActor,CancellationToken cancellationToken)
        {
            Fences++; LastActor=currentActor;
            if (_loseBeforeSeal) { _loseBeforeSeal=false; return Task.FromException<FileRecoveryObservation>(new IOException("owned loss before Root receipt")); }
            _seal=original.RecoveryExecutionId??throw new InvalidOperationException("missing recovery identity");
            var observed=Observe(original);
            if (LoseFirstSealReply) { LoseFirstSealReply=false; return Task.FromException<FileRecoveryObservation>(new IOException("owned synthetic lost seal reply")); }
            return Task.FromResult(observed);
        }
        private FileRecoveryObservation Observe(FileRecoverySnapshot snapshot)
        {
            var row=material.Read<ContainerFileUploadOperation>(snapshot); OriginalActor=row.ActorSubject;
            var wire=new Rpc.ContainerUploadRecoveryObservation { ContainerId=row.ContainerId,InstanceId=row.InstanceId,OperationId=row.Id.ToString("N"),
                TargetProof=row.TargetProof,RootState="Published",OriginalRecordHash=new('F',64),Sealed=_seal.HasValue,SealId=_seal?.ToString("N")??"",MayHavePublished=true,RetainedObjects=true };
            wire.ObservationHash=ContainerUploadRecoveryBinding.ObservationHash(new() { Owner=row.OwnerHash },wire);
            var facts=new ContainerUploadRecoveryObservationDto(wire.ContainerId,wire.InstanceId,row.Id,wire.TargetProof,wire.RootState,wire.OriginalRecordHash,wire.Sealed,_seal,true,true,wire.ObservationHash);
            return ContainerUploadRecoveryValidation.Wrap(snapshot,row,facts);
        }
    }
}
