using System.Globalization;

namespace WhaleDeck.Agent.Services;

/// <summary>Linux load is runnable/uninterruptible tasks, not CPU busy time.</summary>
public static class HostLoadMetrics
{
    public static IReadOnlyCollection<HostMetric> Parse(string? text, int logicalProcessors)
    {
        var fields = text?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries) ?? [];
        var values = new double[3];
        var valid = logicalProcessors > 0 && fields.Length >= 3;
        for (var index = 0; valid && index < 3; index++)
            valid = double.TryParse(fields[index], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out values[index]) &&
                double.IsFinite(values[index]) && values[index] >= 0;
        var normalized = valid ? values[0] / logicalProcessors * 100 : 0;
        valid &= double.IsFinite(normalized);
        if (!valid) Array.Clear(values);
        var quality = valid ? "Good" : "Unavailable";
        return [new("load.average.1", "host", values[0], "tasks", quality),
            new("load.average.5", "host", values[1], "tasks", quality),
            new("load.average.15", "host", values[2], "tasks", quality),
            new("load.normalized", "host", valid ? normalized : 0, "percent", quality)];
    }
}
