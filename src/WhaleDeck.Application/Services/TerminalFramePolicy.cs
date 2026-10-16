using System.Text.Json;
using WhaleDeck.Application.Abstractions;

namespace WhaleDeck.Application.Services;

public static class TerminalFramePolicy
{
    public static string Grant(ReadOnlyMemory<byte> bytes)
    {
        using var document = Parse(bytes, 256);
        var root = document.RootElement;
        if (root.EnumerateObject().Count() != 1 || !root.TryGetProperty("token", out var token) || token.ValueKind != JsonValueKind.String)
            throw new ArgumentException("TERMINAL_TICKET_REQUIRED");
        var value = token.GetString()!;
        if (value.Length != 64 || !value.All(char.IsAsciiHexDigit) || value.Any(char.IsAsciiLetterLower)) throw new ArgumentException("TERMINAL_TICKET_INVALID");
        return value;
    }
    public static TerminalInput Input(ReadOnlyMemory<byte> bytes)
    {
        using var document = Parse(bytes, 8192); var root = document.RootElement;
        if (!root.TryGetProperty("kind", out var kind) || kind.ValueKind != JsonValueKind.String) throw new ArgumentException("TERMINAL_FRAME_INVALID");
        var count = root.EnumerateObject().Count();
        switch (kind.GetString())
        {
            case "input" when count == 2 && root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.String:
                if (!data.TryGetBytesFromBase64(out var decoded) || decoded.Length is < 1 or > 4096) throw new ArgumentException("TERMINAL_INPUT_INVALID");
                return new("input", decoded);
            case "resize" when count == 3 && root.TryGetProperty("columns", out var columns) && columns.ValueKind == JsonValueKind.Number &&
                root.TryGetProperty("rows", out var rows) && rows.ValueKind == JsonValueKind.Number && columns.TryGetUInt32(out var width) && rows.TryGetUInt32(out var height) &&
                width is >= 2 and <= 400 && height is >= 2 and <= 400:
                return new("resize", [], width, height);
            case "close" when count == 1: return new("close", []);
            default: throw new ArgumentException("TERMINAL_FRAME_INVALID");
        }
    }
    private static JsonDocument Parse(ReadOnlyMemory<byte> bytes, int maximum)
    {
        if (bytes.Length == 0 || bytes.Length > maximum) throw new ArgumentException("TERMINAL_FRAME_INVALID");
        JsonDocument document;
        try { document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 4 }); }
        catch (JsonException) { throw new ArgumentException("TERMINAL_FRAME_INVALID"); }
        if (document.RootElement.ValueKind != JsonValueKind.Object || document.RootElement.EnumerateObject().Select(item => item.Name).Distinct(StringComparer.Ordinal).Count() != document.RootElement.EnumerateObject().Count())
        { document.Dispose(); throw new ArgumentException("TERMINAL_FRAME_INVALID"); }
        return document;
    }
}
