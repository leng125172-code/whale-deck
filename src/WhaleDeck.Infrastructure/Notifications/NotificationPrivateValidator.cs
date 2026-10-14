using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WhaleDeck.Application.Abstractions;

namespace WhaleDeck.Infrastructure.Notifications;

public sealed class NotificationPrivateValidator : INotificationPrivateValidator
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 8 };
    public void Validate(string provider, string action, string input, bool hasCredential)
    {
        if (Encoding.UTF8.GetByteCount(input) > 64000) throw new ArgumentException("Private input is too large.");
        try
        {
            using var document = JsonDocument.Parse(input, new JsonDocumentOptions { MaxDepth = 8 });
            Duplicates(document.RootElement);
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new ArgumentException("Private input must be an object.");
            if (action == "remove") { if (document.RootElement.EnumerateObject().Any()) throw new ArgumentException("Removal does not accept private values."); return; }
            if (action != "apply" || document.RootElement.EnumerateObject().Any(item => item.Name is not ("target" or "credential" or "credentialMode"))) throw new ArgumentException("Invalid private input fields.");
            var target = document.RootElement.GetProperty("target").Deserialize<NotificationTarget>(Options) ?? throw new ArgumentException("Private target is required.");
            var mode = document.RootElement.GetProperty("credentialMode").GetString();
            if (target.Provider != provider || mode is not ("keep" or "replace" or "remove")) throw new ArgumentException("Invalid private target or credential policy.");
            NotificationCredential? credential = document.RootElement.TryGetProperty("credential", out var value) && value.ValueKind != JsonValueKind.Null ? value.Deserialize<NotificationCredential>(Options) : null;
            if (mode == "replace" && credential is null || mode != "replace" && credential is not null) throw new ArgumentException("Explicit credential policy is required.");
            // Kept private values are never read by API. Type-level preflight only;
            // the root writer validates the actual existing pair before mutation.
            if (mode == "keep" && hasCredential) credential = provider == "Email" ? new() { Username = "existing", Password = "existing" } : provider is "DingTalk" or "Feishu" ? new() { SigningSecret = "existing" } : null;
            PrivateNotificationConfiguration.Validate(target, credential);
            var policy = new NotificationNetworkPolicy();
            if (target.AllowedAddresses.Any(address => !policy.Allowed(IPAddress.Parse(address), target.AllowPrivateNetwork))) throw new ArgumentException("The approved network address is denied.");
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or NotificationConfigurationException or FormatException)
        { throw new ArgumentException("Private configuration shape is invalid or incompatible."); }
    }
    private static void Duplicates(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        { var names = new HashSet<string>(StringComparer.Ordinal); foreach (var item in value.EnumerateObject()) { if (!names.Add(item.Name)) throw new ArgumentException("Duplicate private field."); Duplicates(item.Value); } }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) Duplicates(item);
    }
}
