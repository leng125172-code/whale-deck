using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Services;

public static partial class ContainerFileValidation
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    [GeneratedRegex(@"\A([1-9][0-9]{0,9}):([0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2})(?:\.([0-9]{1,9}))?Z\z", RegexOptions.CultureInvariant)]
    private static partial Regex InstancePattern();

    public static void Id(string value)
    {
        if (value is not { Length: 64 } || value.Any(c => !(char.IsAsciiDigit(c) || c is >= 'a' and <= 'f')))
            throw new ArgumentException("CONTAINER_FILES_ID_INVALID");
    }
    public static void Location(ContainerFileLocationDto value, bool empty = false)
    {
        if (value is null) throw new ArgumentException("CONTAINER_FILES_LOCATION_REQUIRED");
        Id(value.ContainerId);
        var match = InstancePattern().Match(value.InstanceId ?? "");
        if (!match.Success || !int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var pid) || pid < 2)
            throw new ArgumentException("CONTAINER_FILES_INSTANCE_INVALID");
        var fraction = match.Groups[3].Value;
        var date = match.Groups[2].Value + (fraction.Length == 0 ? "" : "." + fraction[..Math.Min(7, fraction.Length)]) + "Z";
        if (!DateTimeOffset.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out _))
            throw new ArgumentException("CONTAINER_FILES_INSTANCE_INVALID");
        Path(value.Path, empty);
    }
    public static void Path(string value, bool empty)
    {
        try
        {
            if (value is null || Utf8.GetByteCount(value) > 4096 || value.Any(char.IsControl) || value.Contains('\\', StringComparison.Ordinal) ||
                !(empty && value.Length == 0) && value.Split('/').Any(p => p is "" or "." or ".." || Utf8.GetByteCount(p) > 255))
                throw new ArgumentException("CONTAINER_FILES_PATH_INVALID");
        }
        catch (EncoderFallbackException) { throw new ArgumentException("CONTAINER_FILES_PATH_INVALID"); }
    }
    public static void Cursor(string value)
    {
        Path(value, true);
        if (value.Contains('/', StringComparison.Ordinal)) throw new ArgumentException("CONTAINER_FILES_CURSOR_INVALID");
    }
    public static void Hash(string value)
    { if (!FileManagementService.Hex(value)) throw new ArgumentException("CONTAINER_FILES_VERSION_INVALID"); }

    public static void Entry(FileEntryDto value, bool empty = false)
    {
        Cursor(value.Name); Hash(value.Version);
        if (!empty && value.Name.Length == 0 || value.Kind is not ("File" or "Directory" or "Symlink" or "Special") ||
            value.Mode.Length != 4 || value.Mode.Any(c => c is < '0' or > '7') || value.SizeBytes < 0 ||
            (value.Kind is "Symlink" or "Special") && !value.IsProtected) throw InvalidOutput();
    }
    public static void Page(FilePageDto page, string after, int take)
    {
        Hash(page.Version); Cursor(page.NextCursor);
        if (page.Entries.Count > take) throw InvalidOutput();
        var previous = after;
        foreach (var entry in page.Entries)
        {
            Entry(entry);
            if (Utf8.GetBytes(previous).AsSpan().SequenceCompareTo(Utf8.GetBytes(entry.Name)) >= 0) throw InvalidOutput();
            previous = entry.Name;
        }
        if (page.NextCursor.Length > 0 && (page.Entries.Count == 0 || page.NextCursor != previous)) throw InvalidOutput();
    }
    public static InvalidOperationException InvalidOutput() => new("CONTAINER_FILES_OUTPUT_INVALID");
}
