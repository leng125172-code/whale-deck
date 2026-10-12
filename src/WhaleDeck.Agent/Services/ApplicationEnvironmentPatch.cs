using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace WhaleDeck.Agent.Services;

/// <summary>Preserves existing dotenv entries without revealing or decoding their values.</summary>
public static partial class ApplicationEnvironmentPatch
{
    public static IReadOnlyCollection<string> Names(string source) => Entries(source).Select(item => item.Name)
        .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    public static string Apply(string source, JsonObject? patch)
    {
        if (patch is null) return source;
        if (patch.Any(item => item.Key is not ("set" or "remove"))) throw Invalid();
        if (patch.ContainsKey("set") && patch["set"] is not JsonObject ||
            patch.ContainsKey("remove") && patch["remove"] is not JsonArray) throw Invalid();
        var set = patch["set"] as JsonObject ?? new JsonObject();
        var remove = patch["remove"] as JsonArray ?? new JsonArray();
        if (set.Count + remove.Count > 256) throw Invalid();
        var removals = remove.Select(item => item?.GetValue<string>() ?? throw Invalid()).ToHashSet(StringComparer.Ordinal);
        foreach (var name in removals.Concat(set.Select(item => item.Key))) if (!NamePattern().IsMatch(name)) throw Invalid();
        if (removals.Overlaps(set.Select(item => item.Key))) throw Invalid();
        var entries = Entries(source);
        var lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var replace = entries.Where(entry => removals.Contains(entry.Name) || set.ContainsKey(entry.Name))
            .SelectMany(entry => Enumerable.Range(entry.Start, entry.End - entry.Start + 1)).ToHashSet();
        var output = new StringBuilder();
        for (var index = 0; index < lines.Length; index++)
            if (!replace.Contains(index) && (index < lines.Length - 1 || lines[index].Length > 0)) output.Append(lines[index]).Append('\n');
        foreach (var (name, node) in set.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var value = node?.GetValue<string>() ?? throw Invalid();
            if (value.Contains('\0') || value.Contains('\r') || value.Length > 32_768) throw Invalid();
            // Verified against actual Docker container Config.Env, not the dollar-escaped `compose config` output.
            var encoded = value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)
                .Replace("$", "\\$", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal)
                .Replace("\t", "\\t", StringComparison.Ordinal);
            output.Append(name).Append("=\"").Append(encoded).Append("\"\n");
        }
        var result = output.ToString();
        if (Encoding.UTF8.GetByteCount(result) > 65_536) throw Invalid();
        return result;
    }

    private static List<Entry> Entries(string source)
    {
        if (Encoding.UTF8.GetByteCount(source) > 65_536 || source.Contains('\0')) throw Invalid();
        var lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var entries = new List<Entry>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].TrimStart();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (line.StartsWith("export ", StringComparison.Ordinal)) line = line[7..].TrimStart();
            var separator = line.IndexOf('=');
            if (separator < 1) throw Invalid();
            var name = line[..separator].Trim();
            if (!NamePattern().IsMatch(name) || !names.Add(name) || names.Count > 256) throw Invalid();
            var start = index;
            var value = line[(separator + 1)..].TrimStart();
            if (value.Length > 0 && value[0] is '\'' or '"')
            {
                var quote = value[0];
                while (!Closed(value, quote))
                {
                    if (++index >= lines.Length) throw Invalid();
                    value += "\n" + lines[index];
                }
            }
            entries.Add(new(name, start, index));
        }
        return entries;
    }

    private static bool Closed(string value, char quote)
    {
        for (var index = 1; index < value.Length; index++)
        {
            if (value[index] != quote) continue;
            // dotenv recognizes an escaped quote, even inside otherwise literal single quotes.
            var slashes = 0;
            for (var previous = index - 1; previous >= 0 && value[previous] == '\\'; previous--) slashes++;
            if (slashes % 2 != 0) continue;
            var trailing = value[(index + 1)..].TrimStart();
            if (trailing.Length > 0 && !trailing.StartsWith('#')) throw Invalid();
            return true;
        }
        return false;
    }

    private static InvalidOperationException Invalid() => new("Application environment input is invalid or exceeds its bounds.");
    private sealed record Entry(string Name, int Start, int End);
    [GeneratedRegex("\\A[A-Za-z_][A-Za-z0-9_]{0,127}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();
}
