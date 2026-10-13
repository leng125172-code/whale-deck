namespace WhaleDeck.Agent.Services;

public static class SystemdStatus
{
    public static IReadOnlyDictionary<string, string> Parse(string text)
    {
        if (text.Length > 4096) return new Dictionary<string, string>();
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = line.Trim().Split('=', 2);
            if (pair.Length == 2 && pair[0] is "ActiveState" or "SubState" or "UnitFileState" or "LoadState" &&
                pair[1].Length <= 128 && !pair[1].Any(char.IsControl)) result[pair[0]] = pair[1];
        }
        return result;
    }
}
