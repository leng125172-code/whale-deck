using Microsoft.EntityFrameworkCore;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed partial class FileTreeRestoringRepository
{
    private async Task<(FileTreeCaptureDto Capture, string Owner)> Capture(Guid id, CancellationToken cancellationToken)
    {
        var row = await db.FileTreeCaptures.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("FILES_CAPTURE_NOT_FOUND");
        return (FileTreeCaptureValidation.Verify(row, captureProtector, overwriteProtector), row.OwnerHash);
    }
    private async Task<FileTreeRestoringCommand> CaptureCommand(CreateFileTreeRestoringCommand request, CancellationToken cancellationToken)
    {
        var (capture, owner) = await Capture(request.CaptureId, cancellationToken);
        var command = new FileTreeRestoringCommand(capture.Source.Source, capture.Source.ExpectedVersion, request.Target,
            "tar.gz", capture.Id, owner, capture.Kind, capture.ManifestSha256);
        FileTreeRestoringValidation.Command(command); return command;
    }
    private async Task RequireCapture(FileTreeRestoringPlan plan, CancellationToken cancellationToken)
    {
        var (capture, _) = await Capture(plan.Command.CaptureId, cancellationToken);
        if (plan.Command != await CaptureCommand(new(capture.Id, plan.Command.Target), cancellationToken) || plan.EntryCount != capture.EntryCount ||
            plan.TotalBytes != capture.TotalBytes || plan.ArchiveBytes != capture.ArchiveBytes || plan.ArchiveSha256 != capture.ArchiveSha256 ||
            capture.ContentSha256 is not null && plan.ContentSha256 != capture.ContentSha256)
            throw new InvalidOperationException("FILES_CAPTURE_PROOF_CHANGED");
    }
}
