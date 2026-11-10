using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Contracts;

public static class DevelopmentSourceDiffBinding
{
    public const uint MaximumEntries = 20000;
    public const uint MaximumPageSize = 50;
    public const int MaximumHostPageBytes = 49 * 1024;

    public static string Hash(WorkspaceSourceDiffRequest request) =>
        Convert.ToHexString(SHA256.HashData(request.ToByteArray()));

    public static bool IsValid(WorkspaceSourceDiffRequest request) => request.Source is not null &&
        request.CalculateSize() <= DevelopmentSourceBinding.MaximumHeaderBytes &&
        DevelopmentSourceBinding.IsValid(request.Source, DevelopmentSourceBinding.ReadDiff) &&
        request.Offset < MaximumEntries && request.Limit is > 0 and <= MaximumPageSize;

    public static byte[] HostInput(WorkspaceSourceDiffRequest request)
    {
        var fields = DevelopmentSourceBinding.HostFields(request.Source);
        fields.Add("offset", request.Offset);
        fields.Add("limit", request.Limit);
        return JsonSerializer.SerializeToUtf8Bytes(fields);
    }

    public static bool Matches(WorkspaceSourceDiffRequest request, WorkspaceSourceDiffPage page)
    {
        if (page.WorkspaceId != request.Source.Storage.WorkspaceId || page.ImportId != request.Source.ImportId ||
            page.PreviousImportId != request.Source.PreviousImportId || page.PlanSha256 != request.Source.PlanSha256 ||
            page.Offset != request.Offset || page.VisibleEntries >= MaximumEntries ||
            page.RedactedEntries >= MaximumEntries - page.VisibleEntries || page.Offset > page.VisibleEntries ||
            page.Entries.Count > request.Limit || page.Entries.Count > page.VisibleEntries - page.Offset)
            return false;
        var end = page.Offset + (uint)page.Entries.Count;
        if (end < page.VisibleEntries ? !page.HasNextOffset || page.NextOffset != end || end == page.Offset : page.HasNextOffset)
            return false;
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in page.Entries)
        {
            if (!ValidPath(entry.Path) || !paths.Add(entry.Path) ||
                entry.Before is not null && !ValidNode(entry.Before) ||
                entry.After is not null && !ValidNode(entry.After))
                return false;
            if (entry.Change switch
                {
                    WorkspaceSourceDiffChange.Added => entry.Before is not null || entry.After is null,
                    WorkspaceSourceDiffChange.Removed => entry.Before is null || entry.After is not null,
                    WorkspaceSourceDiffChange.Changed => entry.Before is null || entry.After is null,
                    _ => true
                })
                return false;
        }
        return true;
    }

    private static bool ValidNode(WorkspaceSourceDiffNode node) => node.Kind switch
    {
        WorkspaceSourceDiffKind.Directory => node.SizeBytes == 0 && !node.Executable,
        WorkspaceSourceDiffKind.File => node.SizeBytes <= 1024UL * 1024 * 1024,
        _ => false
    };

    private static bool ValidPath(string path)
    {
        if (path.Length == 0 || Encoding.UTF8.GetByteCount(path) > 4096 || path.Contains('\\') || path.Contains(':'))
            return false;
        var parts = path.Split('/');
        return parts.Length <= 32 && parts.All(part => part is not ("" or "." or "..") &&
            Encoding.UTF8.GetByteCount(part) <= 255) &&
            path.EnumerateRunes().All(rune => Rune.GetUnicodeCategory(rune) is not
                (UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator));
    }
}
