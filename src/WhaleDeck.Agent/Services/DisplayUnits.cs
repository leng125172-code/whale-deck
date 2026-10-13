using System.Globalization;

namespace WhaleDeck.Agent.Services;

internal static class DisplayUnits
{
    private static readonly string[] BinaryUnits = ["B", "KiB", "MiB", "GiB", "TiB", "PiB", "EiB"];

    public static string Bytes(long bytes)
    {
        if (bytes < 0) return "容量未知";
        decimal size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < BinaryUnits.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return $"{size.ToString(unit == 0 ? "0" : "0.0#", CultureInfo.InvariantCulture)} {BinaryUnits[unit]}";
    }
}
