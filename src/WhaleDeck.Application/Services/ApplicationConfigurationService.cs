using System.Text.Json;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Services;

/// <summary>Loads owner-bound transient input for preflight only; it never enters persisted jobs.</summary>
public sealed class ApplicationConfigurationService(IAgentGateway agent, IOneTimeSecretStore secrets)
{
    public Task<ApplicationConfigurationDto> ReadAsync(string resourceId, CancellationToken cancellationToken) =>
        agent.GetApplicationConfigurationAsync(resourceId, cancellationToken);

    public async Task<PlanDto> PlanAsync(string subject, string resourceId, string expectedFingerprint, string inputTicket,
        CancellationToken cancellationToken)
    {
        if (!resourceId.StartsWith("application:", StringComparison.Ordinal) || expectedFingerprint.Length != 64 ||
            !expectedFingerprint.All(Uri.IsHexDigit)) throw new ArgumentException("A current application configuration fingerprint is required.");
        var input = await secrets.PeekAsync(subject, inputTicket, cancellationToken)
            ?? throw new InvalidOperationException("The configuration input is unavailable or expired. Enter it again.");
        JsonDocument document;
        try { document = JsonDocument.Parse(input); }
        catch (JsonException) { throw new ArgumentException("The configuration input is invalid."); }
        using var validatedInput = document;
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new ArgumentException("The configuration input must be an object.");
        return await agent.PlanAsync("applications", "configure", resourceId, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["slug"] = resourceId["application:".Length..], ["expectedFingerprint"] = expectedFingerprint,
            ["inputKind"] = "environment", ["environment"] = input
        }, cancellationToken);
    }
}
