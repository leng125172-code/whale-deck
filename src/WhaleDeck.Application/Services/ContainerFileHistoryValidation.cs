using System.Text;
using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Services;

public static class ContainerFileHistoryValidation
{
    public static void CaptureId(string value)
    {
        if (!Guid.TryParseExact(value, "N", out var id) || id == Guid.Empty || id.ToString("N") != value)
            throw new ArgumentException("CONTAINER_HISTORY_REQUEST_INVALID");
    }

    public static void List(ContainerFileLocationDto location, string after, int take, string expectedVersion)
    {
        ContainerFileValidation.Location(location, empty: true);
        if (take is < 1 or > 50) throw new ArgumentException("CONTAINER_HISTORY_REQUEST_INVALID");
        if (after.Length != 0) CaptureId(after);
        if (expectedVersion.Length != 0) ContainerFileValidation.Hash(expectedVersion);
        if (after.Length != 0 && expectedVersion.Length == 0) throw new ArgumentException("CONTAINER_HISTORY_VERSION_REQUIRED");
    }

    public static void Entry(ContainerHistoryEntryDto entry)
    {
        CaptureId(entry.CaptureId);
        ContainerFileValidation.Location(new(entry.ContainerId, entry.OriginalInstanceId, entry.Path));
        ContainerFileValidation.Hash(entry.SourceVersion); ContainerFileValidation.Hash(entry.Sha256); ContainerFileValidation.Hash(entry.CaptureHash);
        if (entry.SizeBytes is < 0 or > 1073741824L || entry.CreatedAtUnixMs is < 0 or > 253402300799999L ||
            entry.Availability is not ("Present" or "Incomplete" or "Missing" or "Invalid")) throw InvalidOutput();
    }

    public static void Source(ContainerFileLocationDto location, ContainerHistoryEntryDto expected)
    {
        ContainerFileValidation.Location(location); Entry(expected);
        if (expected.ContainerId != location.ContainerId || expected.Path != location.Path || expected.Availability != "Present")
            throw new ArgumentException("CONTAINER_HISTORY_SOURCE_INVALID");
    }

    public static void Page(ContainerHistoryPageDto page, ContainerFileLocationDto location, string after, int take, string expectedVersion)
    {
        if (page.Location != location) throw InvalidOutput();
        ContainerFileValidation.Hash(page.TargetProof); ContainerFileValidation.Hash(page.Version);
        if (page.NextCursor.Length != 0) CaptureId(page.NextCursor);
        if (page.Entries.Count > take || page.Scanned is < 0 or > 500 || page.InvalidRecords < 0 || page.InvalidRecords > page.Scanned ||
            page.Entries.Count + page.InvalidRecords > page.Scanned || page.Complete != (page.NextCursor.Length == 0) ||
            expectedVersion.Length != 0 && expectedVersion != page.Version || !page.Complete && string.CompareOrdinal(page.NextCursor, after) <= 0) throw InvalidOutput();
        var previous = after; var bytes = 0;
        foreach (var entry in page.Entries)
        {
            Entry(entry);
            if (entry.ContainerId != location.ContainerId || location.Path.Length != 0 && entry.Path != location.Path ||
                string.CompareOrdinal(entry.CaptureId, previous) <= 0 || page.NextCursor.Length != 0 && string.CompareOrdinal(entry.CaptureId, page.NextCursor) > 0) throw InvalidOutput();
            bytes += Encoding.UTF8.GetByteCount(entry.Path) + entry.ContainerId.Length + entry.CaptureId.Length + entry.OriginalInstanceId.Length +
                entry.SourceVersion.Length + entry.Sha256.Length + entry.CaptureHash.Length;
            if (bytes > 48 * 1024) throw InvalidOutput();
            previous = entry.CaptureId;
        }
    }

    public static InvalidOperationException InvalidOutput() => new("CONTAINER_HISTORY_OUTPUT_INVALID");
}
