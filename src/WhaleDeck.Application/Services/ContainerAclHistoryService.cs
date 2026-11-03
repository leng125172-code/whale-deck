using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Services;

/// <summary>Authoritative HDD metadata reads; no cached SQL copy can authorize restoration.</summary>
public sealed class ContainerAclHistoryService(FileManagementService authorization, ILoginSessionRegistry logins,
    IContainerAclHistoryGateway gateway, IContainerFileReadAudit audit)
{
    private async Task AuthorizeAsync(TerminalActor actor, CancellationToken cancellationToken)
    {
        await authorization.AuthorizeAsync(actor, cancellationToken);
        if (!await logins.IsActiveAsync(actor.Subject, actor.LoginBinding, cancellationToken))
            throw new UnauthorizedAccessException("CONTAINER_ACL_HISTORY_LOGIN_REQUIRED");
        cancellationToken.ThrowIfCancellationRequested();
    }

    public async Task<ContainerAclHistoryPageDto> ListAsync(TerminalActor actor, ContainerFileLocationDto location,
        string after, int take, string expectedVersion, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        ContainerAclHistoryValidation.List(location, after, take, expectedVersion);
        await gateway.RequireContainerAclHistoryAsync(cancellationToken);
        var page = await gateway.ListContainerAclHistoryAsync(actor, location, after, take, expectedVersion, cancellationToken);
        ContainerAclHistoryValidation.Page(page, location, after, take, expectedVersion);
        var result = page with { Entries = Array.AsReadOnly(page.Entries.ToArray()) };
        await AuthorizeAsync(actor, cancellationToken);
        await audit.RecordAsync(actor, location, "acl-history-list", "Validated", result.Version, null, null, cancellationToken);
        await AuthorizeAsync(actor, cancellationToken);
        return result;
    }

    public async Task<ContainerAclHistorySnapshotDto> ReadAsync(TerminalActor actor, ContainerFileLocationDto location,
        string captureId, string expectedHistoryDigest, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        ContainerAclHistoryValidation.Read(location, captureId, expectedHistoryDigest);
        await gateway.RequireContainerAclHistoryAsync(cancellationToken);
        var snapshot = await gateway.ReadContainerAclHistoryAsync(actor, location, captureId, expectedHistoryDigest, cancellationToken);
        ContainerAclHistoryValidation.Snapshot(snapshot, location, captureId, expectedHistoryDigest);
        var result = snapshot with { AccessAcl = Array.AsReadOnly(snapshot.AccessAcl.ToArray()), DefaultAcl = Array.AsReadOnly(snapshot.DefaultAcl.ToArray()) };
        await AuthorizeAsync(actor, cancellationToken);
        // Version is the verified ACL metadata digest, never a file-byte hash.
        await audit.RecordAsync(actor, location, "acl-history-read", "Validated", expectedHistoryDigest, null, null, cancellationToken);
        await AuthorizeAsync(actor, cancellationToken);
        return result;
    }
}
