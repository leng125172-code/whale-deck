using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Services;

/// <summary>Current human authority around every uncached Agent read and its durable audit.</summary>
public sealed class ContainerRecursiveAclHistoryService(FileManagementService authorization, ILoginSessionRegistry logins,
    IContainerRecursiveAclHistoryGateway gateway, IContainerRecursiveAclHistoryAudit audit)
{
    private async Task AuthorizeAsync(TerminalActor actor,CancellationToken cancellationToken)
    {
        await authorization.AuthorizeAsync(actor,cancellationToken);
        if(!await logins.IsActiveAsync(actor.Subject,actor.LoginBinding,cancellationToken))
            throw new UnauthorizedAccessException("CONTAINER_RECURSIVE_ACL_HISTORY_LOGIN_REQUIRED");
        cancellationToken.ThrowIfCancellationRequested();
    }
    public async Task<ContainerRecursiveAclHistoryPageDto> ListAsync(TerminalActor actor,ContainerFileLocationDto location,
        string after,int take,string expectedVersion,CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor,cancellationToken);
        ContainerRecursiveAclHistoryValidation.List(location,after,take,expectedVersion);
        await gateway.RequireContainerRecursiveAclHistoryAsync(cancellationToken);
        var page=await gateway.ListContainerRecursiveAclHistoryAsync(actor,location,after,take,expectedVersion,cancellationToken);
        ContainerRecursiveAclHistoryValidation.Page(page,location,after,take,expectedVersion);
        var result=page with{Entries=Array.AsReadOnly(page.Entries.ToArray())};
        await AuthorizeAsync(actor,cancellationToken);
        await audit.ListAsync(actor,location,after,take,expectedVersion,result,cancellationToken);
        await AuthorizeAsync(actor,cancellationToken);
        return result;
    }
    public async Task<ContainerRecursiveAclHistorySnapshotDto> ReadAsync(TerminalActor actor,ContainerFileLocationDto location,
        string captureId,string expectedHistoryDigest,int offset,int take,CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor,cancellationToken);
        ContainerRecursiveAclHistoryValidation.Read(location,captureId,expectedHistoryDigest,offset,take);
        await gateway.RequireContainerRecursiveAclHistoryAsync(cancellationToken);
        var page=await gateway.ReadContainerRecursiveAclHistoryAsync(actor,location,captureId,expectedHistoryDigest,offset,take,cancellationToken);
        ContainerRecursiveAclHistoryValidation.Snapshot(page,location,captureId,expectedHistoryDigest,offset,take);
        var result=page with{Entries=Array.AsReadOnly(page.Entries.Select(entry=>entry with{
            AccessAcl=Array.AsReadOnly(entry.AccessAcl.ToArray()),DefaultAcl=Array.AsReadOnly(entry.DefaultAcl.ToArray())}).ToArray())};
        await AuthorizeAsync(actor,cancellationToken);
        await audit.ReadAsync(actor,location,captureId,expectedHistoryDigest,offset,take,result,cancellationToken);
        await AuthorizeAsync(actor,cancellationToken);
        return result;
    }
}
