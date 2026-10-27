using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Agent;
using WhaleDeck.Infrastructure.Persistence;

namespace WhaleDeck.IntegrationTests;

public sealed partial class NativeNotificationDispatchTests
{
    // Real migrations/catalog/MAC/transactions; no synthetic receipt below is
    // presented as actual Agent/HDD or browser restoration evidence.
    private static async Task ContainerRestorationBoundaries(DbContextOptions<PlatformDbContext> options)
    {
        await using var db=new PlatformDbContext(options);
        var key=Path.GetTempFileName();await File.WriteAllBytesAsync(key,RandomNumberGenerator.GetBytes(32));
        if(!OperatingSystem.IsWindows())File.SetUnixFileMode(key,UnixFileMode.UserRead|UnixFileMode.UserWrite);
        var protector=new FilePlanProtector(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Agent:CapabilityKeyFile"]=key }).Build());
        var catalog=new ContainerFileHistoryRepository(db,TimeProvider.System);var repository=new ContainerFileUploadRepository(db,protector);
        var actor=new TerminalActor("native-container-restore-admin",null,[],new('A',64),"native-container-restore-trace",null);
        var location=new ContainerFileLocationDto(new('c',64),"999:2026-10-27T01:02:03.123456789Z","work/恢复版本.txt");
        var source=new ContainerHistoryEntryDto(Guid.NewGuid().ToString("N"),location.ContainerId,"888:2026-10-26T01:02:03.123456789Z",location.Path,
            new('B',64),3,new('D',64),DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeMilliseconds(),false,"Present",new('E',64));
        var page=new ContainerHistoryPageDto(location,new('B',64),[source],new('F',64),"",1,0,true);
        var destination = location with { Path = "recovered/另一份恢复.txt" };
        var ids=new List<Guid>();
        ContainerFileUploadOperation Row(bool attributes = false)
        {
            var id=Guid.NewGuid();ids.Add(id);var now=ContainerFileUploadIntentValidation.CanonicalTime(DateTimeOffset.UtcNow);
            var sourceJson=ContainerFileRestoreValidation.Snapshot(source);
            var binding=new ContainerFileUploadBinding(id,Guid.NewGuid(),actor.Subject,actor.LoginBinding,actor.TraceId,
                new(destination,new('B',64),new('C',64),"ABSENT",0),source.SizeBytes,source.Sha256,new('F',64),now,now.AddMinutes(15),sourceJson,attributes);
            return new() {Id=id,IdempotencyKey=binding.IdempotencyKey,ActorSubject=actor.Subject,LoginBinding=actor.LoginBinding,TraceId=actor.TraceId,
                OwnerHash=ContainerFileUploadIntentValidation.Owner(actor.Subject,actor.LoginBinding),ContainerId=location.ContainerId,InstanceId=location.InstanceId,
                RelativePath=destination.Path,TargetProof=binding.Target.TargetProof,ExpectedParentVersion=binding.Target.ExpectedParentVersion,ExpectedVersion="ABSENT",
                PlanHash=binding.PlanHash,RequestHash=protector.FingerprintContainerUpload(binding),Sha256=source.Sha256,SizeBytes=source.SizeBytes,
                HistorySourceJson=sourceJson,RestoreAttributes=attributes,CreatedAtUtc=now,ExpiresAtUtc=binding.ExpiresAtUtc,NextAttemptAtUtc=now};
        }
        try
        {
            await catalog.ObserveAsync(actor,page,default);await catalog.ObserveAsync(actor,page,default);
            Assert.Equal(source,await catalog.FindAsync(location.ContainerId,source.CaptureId,default));
            Assert.Null(await catalog.FindAsync(new('d',64),source.CaptureId,default));
            var changed=page with {Entries=[source with {Sha256=new('A',64)}]};
            await Assert.ThrowsAsync<InvalidOperationException>(()=>catalog.ObserveAsync(actor,changed,default));
            Assert.Equal(source,await catalog.FindAsync(location.ContainerId,source.CaptureId,default));

            var row=Row(true);await repository.EnqueueAsync(row,default);
            Assert.Equal("containers.files.restore",(await db.OperationJobs.AsNoTracking().SingleAsync(item=>item.Id==row.Id)).JobType);
            var loaded=(await repository.OwnedAsync(actor,row.Id,default))!;
            Assert.True(loaded.RestoreAttributes); Assert.Equal(destination.Path, loaded.RelativePath);
            Assert.Equal(source.Path, ContainerFileRestoreValidation.Read(loaded.HistorySourceJson!).Path);
            Assert.Equal(row.HistorySourceJson,loaded.HistorySourceJson);Assert.True(ContainerFileUploadService.HasValidBinding(loaded,protector));
            await Assert.ThrowsAsync<InvalidOperationException>(()=>repository.BeginReceiveAsync(actor,row.Id,DateTimeOffset.UtcNow,default));
            Assert.False(await repository.BeginRestoreReceiveAsync(row,DateTimeOffset.UtcNow,default));
            var claim=await repository.ClaimAsync(DateTimeOffset.UtcNow,default);Assert.NotNull(claim);Assert.Equal(row.Id,claim.Id);
            Assert.True(await repository.BeginRestoreReceiveAsync(claim,DateTimeOffset.UtcNow,default));Assert.Equal("Receiving",claim.State);Assert.NotNull(claim.ReceiveToken);
            Assert.False(await repository.BeginRestoreReceiveAsync(claim,DateTimeOffset.UtcNow,default));
            Assert.True(await repository.MayReceiveAsync(claim.Id,claim.ReceiveToken!.Value,DateTimeOffset.UtcNow,default));
            Assert.True(await repository.ReadyAsync(claim,default));
            Assert.Equal(ContainerFileUploadStartResult.Started,await repository.StartedAsync(claim,DateTimeOffset.UtcNow,default));
            var receipt=new ContainerFileUploadReceiptDto(claim.ContainerId,claim.InstanceId,claim.Id,claim.TargetProof,
                new("Published",claim.SizeBytes,claim.Sha256,new('F',64),false,true,false),false);
            Assert.True(await repository.FinishAsync(claim,"Succeeded","CONTAINER_UPLOAD_PUBLISHED",receipt,default));
            Assert.False((await repository.OwnedAsync(actor,row.Id,default))!.PublishBarrier);
            Assert.True(await db.AuditEvents.AnyAsync(item=>item.JobId==row.Id&&item.Action=="containers.files.restore.result"));
            var pendingMessages=await db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM outbox_messages WHERE payload_json->>'jobId'={row.Id.ToString("D")} AND processed_at_utc IS NULL").SingleAsync();
            Assert.Equal(0,pendingMessages);

            var canceled=Row();await repository.EnqueueAsync(canceled,default);var pending=await repository.ClaimAsync(DateTimeOffset.UtcNow,default);
            Assert.NotNull(pending);Assert.Equal(canceled.Id,pending.Id);Assert.True(await repository.BeginRestoreReceiveAsync(pending,DateTimeOffset.UtcNow,default));
            await repository.CancelAsync(actor,pending.Id,default);Assert.False(await repository.ReadyAsync(pending,default));
            var discard=new ContainerFileUploadReceiptDto(pending.ContainerId,pending.InstanceId,pending.Id,pending.TargetProof,
                new("Discarded",pending.SizeBytes,pending.Sha256,"",false,false,false),false);
            Assert.True(await repository.FinishAsync(pending,"Canceled","CONTAINER_UPLOAD_CANCELED",discard,default));
            Console.WriteLine("INBOX_PHASE=container-restore-permanent-catalog-canonical-source-lease-ready-barrier-cancel");
        }
        finally
        {
            db.ChangeTracker.Clear();await db.ContainerFileUploadOperations.Where(item=>ids.Contains(item.Id)).ExecuteDeleteAsync();
            var capture=Guid.ParseExact(source.CaptureId,"N");await db.ContainerFileVersions.Where(item=>item.Id==capture).ExecuteDeleteAsync();File.Delete(key);
        }
    }
}
