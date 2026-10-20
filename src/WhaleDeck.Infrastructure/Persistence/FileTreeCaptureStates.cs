using Microsoft.EntityFrameworkCore;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Infrastructure.Persistence;

/// <summary>Permanent signed cleanup rows overlay the immutable catalogue; unknown never means retained.</summary>
internal sealed record FileTreeCaptureState(string Archive = "Retained", string Originals = "Retained", bool CleanupActive = false);

internal static class FileTreeCaptureStates
{
    internal static async Task<Dictionary<Guid, FileTreeCaptureState>> Read(PlatformDbContext db, IReadOnlyCollection<Guid> captures,
        IFileTreePurgingProtector? protector, CancellationToken cancellationToken, Guid? ignoreIntent = null)
    {
        var ids = captures.Distinct().ToArray(); var result = ids.ToDictionary(id => id, _ => new FileTreeCaptureState());
        if (ids.Length == 0) return result;
        // Stream history rather than truncate it: old positive removal proofs
        // must not disappear just because a capture has many canceled intents.
        await foreach (var row in db.FileTreePurgingOperations.AsNoTracking().Where(item => ids.Contains(item.CaptureId) && item.ExecutionJobId != null &&
                (!ignoreIntent.HasValue || item.Id != ignoreIntent.Value)).OrderBy(item => item.CaptureId).ThenBy(item => item.Id).AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            if (protector is null || !FileTreePurgingValidation.HasValidBinding(row, protector)) throw new InvalidOperationException("FILES_CAPTURE_STATE_UNVERIFIABLE");
            var plan = FileTreePurgingValidation.ReadPlan(row); var receipt = row.ReceiptJson is null ? null : FileTreePurgingValidation.ReadReceipt(row);
            var current = result[row.CaptureId]; var uncertain = row.RootStartedAtUtc is not null && (receipt is null || receipt.State == "Unknown");
            string Component(string name, string previous, bool removed) => removed || previous == "Purged" ? "Purged" :
                previous == "Uncertain" || uncertain && FileTreePurgingValidation.Selects(plan.Command.Scope, name) ? "Uncertain" : "Retained";
            result[row.CaptureId] = new(Component("Archive", current.Archive, row.ArchiveRemoved), Component("Originals", current.Originals, row.OriginalsRemoved),
                current.CleanupActive || row.CompletedAtUtc is null);
        }
        return result;
    }
    internal static async Task<FileTreeCaptureState> Read(PlatformDbContext db, Guid capture, IFileTreePurgingProtector? protector,
        CancellationToken cancellationToken, Guid? ignoreIntent = null) => (await Read(db, [capture], protector, cancellationToken, ignoreIntent))[capture];
    internal static async Task RequireRestore(PlatformDbContext db, Guid capture, IFileTreePurgingProtector? protector, CancellationToken cancellationToken)
    {
        var state = await Read(db, capture, protector, cancellationToken);
        if (state.Archive != "Retained" || state.CleanupActive) throw new InvalidOperationException("FILES_CAPTURE_ARCHIVE_UNAVAILABLE");
    }
}
