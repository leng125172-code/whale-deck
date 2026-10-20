using Microsoft.EntityFrameworkCore;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed partial class FileTreeRestoringRepository
{
    private async Task<FileTreeCaptureBinding> Capture(Guid id, CancellationToken cancellationToken)
    {
        var row = await db.FileTreeCaptures.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("FILES_CAPTURE_NOT_FOUND");
        FileTreeRecyclingValidation.Capture(row, captureProtector);
        return FileTreeRecyclingValidation.CaptureBinding(row);
    }
    private async Task<FileTreeRestoringCommand> CaptureCommand(CreateFileTreeRestoringCommand request, CancellationToken cancellationToken)
    {
        var capture = await Capture(request.CaptureId, cancellationToken);
        // Existing catalogue entries attest directory Trash captures. A future
        // overwrite workflow must add its own signed kind, never accept HTTP's.
        var command = new FileTreeRestoringCommand(capture.Command.Source, capture.Command.ExpectedVersion, request.Target,
            "tar.gz", capture.Id, capture.OwnerHash, "Trash", capture.ManifestSha256);
        FileTreeRestoringValidation.Command(command); return command;
    }
    private async Task RequireCapture(FileTreeRestoringPlan plan, CancellationToken cancellationToken)
    {
        var capture = await Capture(plan.Command.CaptureId, cancellationToken);
        if (plan.Command != await CaptureCommand(new(capture.Id, plan.Command.Target), cancellationToken) || plan.EntryCount != capture.EntryCount ||
            plan.TotalBytes != capture.TotalBytes || plan.ArchiveBytes != capture.ArchiveBytes || plan.ArchiveSha256 != capture.ArchiveSha256)
            throw new InvalidOperationException("FILES_CAPTURE_PROOF_CHANGED");
    }
}
