using System.Text.Json;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed class ContainerRecursiveAclHistoryAudit(PlatformDbContext db) : IContainerRecursiveAclHistoryAudit
{
    public Task ListAsync(TerminalActor actor,ContainerFileLocationDto location,string after,int take,string expectedVersion,
        ContainerRecursiveAclHistoryPageDto result,CancellationToken cancellationToken)
    {
        ContainerRecursiveAclHistoryValidation.List(location,after,take,expectedVersion);
        ContainerRecursiveAclHistoryValidation.Page(result,location,after,take,expectedVersion);
        return SaveAsync(actor,location,"list",new{
            pathHash=FileManagementService.Hash(location.Path),instanceHash=FileManagementService.Hash(location.InstanceId),
            afterHash=after.Length==0?null:FileManagementService.Hash(after),take,inventoryVersion=result.Version,
            returnedCount=result.Entries.Count,scannedCount=result.ScannedCount,invalidCount=result.InvalidCount,complete=result.Complete},cancellationToken);
    }
    public Task ReadAsync(TerminalActor actor,ContainerFileLocationDto location,string captureId,string digest,int offset,int take,
        ContainerRecursiveAclHistorySnapshotDto result,CancellationToken cancellationToken)
    {
        ContainerRecursiveAclHistoryValidation.Read(location,captureId,digest,offset,take);
        ContainerRecursiveAclHistoryValidation.Snapshot(result,location,captureId,digest,offset,take);
        return SaveAsync(actor,location,"read",new{
            pathHash=FileManagementService.Hash(location.Path),instanceHash=FileManagementService.Hash(location.InstanceId),
            captureHash=FileManagementService.Hash(captureId),historyDigest=digest,offset,take,returnedCount=result.Entries.Count,
            complete=result.Complete},cancellationToken);
    }
    private async Task SaveAsync(TerminalActor actor,ContainerFileLocationDto location,string action,object detail,CancellationToken token)
    {
        ContainerAclHistoryValidation.Actor(actor);
        db.AuditEvents.Add(new AuditEvent{ActorSubject=actor.Subject,Action="containers.files.recursive-acl-history-"+action,
            TargetType="container",TargetId=location.ContainerId,TraceId=actor.TraceId,SourceIp=actor.SourceIp,
            Result="Validated",DetailJson=JsonSerializer.Serialize(detail)});
        await db.SaveChangesAsync(token);
    }
}
