using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed partial class FileTreePurgingRepository
{
    private async Task<FileTreeCapture> Capture(Guid id, CancellationToken cancellationToken) =>
        await db.FileTreeCaptures.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
        ?? throw new KeyNotFoundException("FILES_CAPTURE_NOT_FOUND");
    private async Task<FileTreePurgingCommand> CaptureCommand(CreateFileTreePurgingCommand request, CancellationToken cancellationToken)
    {
        var capture = await Capture(request.CaptureId, cancellationToken);
        await Available(request, cancellationToken);
        return FileTreePurgingValidation.CaptureCommand(request, capture, captureProtector, overwriteProtector);
    }
    private async Task Available(CreateFileTreePurgingCommand request, CancellationToken cancellationToken, Guid? ignoreIntent = null)
    {
        var state = await FileTreeCaptureStates.Read(db, request.CaptureId, protector, cancellationToken, ignoreIntent);
        // Acknowledged uncertainty is not invented as retained. A fresh root
        // plan may prove a still-present payload for an explicit new operation;
        // positive permanent deletion facts can never be cleared or repeated.
        if (state.CleanupActive || FileTreePurgingValidation.Selects(request.Scope, "Archive") && state.Archive == "Purged" ||
            FileTreePurgingValidation.Selects(request.Scope, "Originals") && state.Originals == "Purged") throw new InvalidOperationException("FILES_CAPTURE_COMPONENT_UNAVAILABLE");
    }
    private async Task RequireCapture(FileTreePurgingPlan plan, CancellationToken cancellationToken, Guid? ignoreIntent = null)
    {
        FileTreePurgingValidation.Capture(plan, await Capture(plan.Command.CaptureId, cancellationToken), captureProtector, overwriteProtector);
        await Available(new(plan.Command.CaptureId, plan.Command.Scope), cancellationToken, ignoreIntent);
    }
    private void StoreReceipt(FileTreePurgingOperation row, FileTreePurgingReceipt receipt, DateTimeOffset now)
    {
        FileTreePurgingValidation.Receipt(row.ExecutionJobId!.Value, FileTreePurgingValidation.ReadPlan(row), receipt);
        if (row.ReceiptJson is not null) FileTreePurgingValidation.MonotonicReceipt(FileTreePurgingValidation.ReadReceipt(row), receipt);
        row.ReceiptObservedAtUtc ??= now; row.ReceiptJson = JsonSerializer.Serialize(receipt);
        row.ReceiptHash = protector.FingerprintReceipt(FileTreePurgingValidation.ReceiptBinding(row));
        row.DeletionStarted = receipt.DeletionStarted; row.ArchiveRemoved = receipt.ArchiveRemoved; row.OriginalsRemoved = receipt.OriginalsRemoved;
    }
}
