using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Services;

/// <summary>Current administrators may discover orphaned Unknown metadata; no file operation or lock is changed.</summary>
public sealed class FileRecoveryInventoryService(FileManagementService authorization, IFileRecoveryInventory inventory)
{
    public async Task<FileRecoveryPageDto> ListAsync(TerminalActor actor, string? after, int take, CancellationToken cancellationToken)
    {
        await authorization.AuthorizeAsync(actor, cancellationToken);
        if (take is < 1 or > FileRecoveryValidation.MaximumPageSize) throw new ArgumentException("FILES_RECOVERY_PAGE_INVALID");
        var page = await inventory.ListAsync(FileRecoveryValidation.DecodeCursor(after), take, cancellationToken);
        await authorization.AuthorizeAsync(actor, cancellationToken); return page;
    }
    public async Task<FileRecoveryCandidateDto> GetAsync(TerminalActor actor, string kind, Guid id, CancellationToken cancellationToken)
    {
        await authorization.AuthorizeAsync(actor, cancellationToken); FileRecoveryValidation.Kind(kind);
        if (id == Guid.Empty) throw new ArgumentException("FILES_RECOVERY_ID_INVALID");
        var result = await inventory.GetAsync(kind, id, cancellationToken) ?? throw new KeyNotFoundException("FILES_RECOVERY_NOT_FOUND");
        await authorization.AuthorizeAsync(actor, cancellationToken); return result;
    }
}
