using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WhaleDeck.Contracts;

/// <summary>Public build provenance read from the binary, never runtime configuration.</summary>
public sealed record BuildIdentity(
    [property: JsonPropertyName("component")] string Component,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("sourceRevision")] string? SourceRevision)
{
    public static BuildIdentity FromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return Parse(assembly.GetName().Name ?? "unknown",
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
    }

    public static BuildIdentity Parse(string component, string? informationalVersion)
    {
        var parts = informationalVersion?.Split('+', 2);
        var version = parts is { Length: > 0 } && System.Version.TryParse(parts[0], out _)
            ? parts[0] : "unknown";
        var revision = parts is { Length: 2 } && parts[1].Length == 40 &&
            parts[1].All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f')
                ? parts[1] : null;
        return new(component, version, revision);
    }

    /// <summary>Inspection exits before DI, configuration, sockets or dependency access.</summary>
    public static bool TryPrint(string[] arguments, Assembly assembly)
    {
        if (arguments.Length != 1 || arguments[0] != "--build-info") return false;
        Console.WriteLine(JsonSerializer.Serialize(FromAssembly(assembly)));
        return true;
    }
}
