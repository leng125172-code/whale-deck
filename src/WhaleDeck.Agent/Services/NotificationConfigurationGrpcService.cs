using System.Text.Json;
using System.Text;
using Grpc.Core;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

/// <summary>Capability/peer-protected fixed helper; no private data goes into OperationStore.</summary>
public sealed class NotificationConfigurationGrpcService(BoundedProcessRunner processes)
    : NotificationConfigurationService.NotificationConfigurationServiceBase
{
    private static readonly HashSet<int> HelperExitCodes = [2, 3];
    public override Task<NotificationConfigurationMetadata> Inspect(NotificationConfigurationLookup request, ServerCallContext context)
    {
        Identity(request.ChannelId);
        return Run("inspect", JsonSerializer.Serialize(new { channelId = request.ChannelId }), context.CancellationToken);
    }

    public override Task<NotificationConfigurationMetadata> Apply(NotificationConfigurationRequest request, ServerCallContext context) =>
        Run(request.Action, BuildInput(request), context.CancellationToken);

    internal static string BuildInput(NotificationConfigurationRequest request)
    {
        Identity(request.ChannelId); Identity(request.OperationId);
        if (request.Action is not ("apply" or "remove") || !Revision(request.ExpectedRevision) || request.PrivateJson.Length is < 2 or > 65536 ||
            request.Context is null || string.IsNullOrWhiteSpace(request.Context.ActorSubject) || request.Context.ActorSubject.Length > 256)
            throw new ArgumentException("NOTIFICATION_PRIVATE_INVALID");
        try
        {
            // Duplicate private fields are rejected by the root parser; do not
            // normalize them away via a general JsonNode parse before checking.
            using var document = JsonDocument.Parse(request.PrivateJson.Memory, new JsonDocumentOptions { MaxDepth = 8 });
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new ArgumentException("NOTIFICATION_PRIVATE_INVALID");
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in document.RootElement.EnumerateObject())
                if (!names.Add(item.Name) || item.Name is not ("target" or "credential" or "credentialMode")) throw new ArgumentException("NOTIFICATION_PRIVATE_INVALID");
            if (request.Action == "remove" && names.Count != 0) throw new ArgumentException("NOTIFICATION_PRIVATE_INVALID");
            // Embed the original validated object without reserializing nested
            // content, preserving duplicate rejection at the privileged boundary.
            var privateObject = document.RootElement.GetRawText().Trim();
            var prefix = JsonSerializer.Serialize(new { channelId = request.ChannelId, operationId = request.OperationId, expectedRevision = request.ExpectedRevision });
            var input = prefix[..^1] + (names.Count == 0 ? "}" : "," + privateObject[1..]);
            if (Encoding.UTF8.GetByteCount(input) > 65536) throw new ArgumentException("NOTIFICATION_PRIVATE_INVALID");
            return input;
        }
        catch (JsonException) { throw new ArgumentException("NOTIFICATION_PRIVATE_INVALID"); }
    }

    private async Task<NotificationConfigurationMetadata> Run(string action, string input, CancellationToken cancellationToken)
    {
        var result = await processes.RunAsync("/usr/bin/sudo", ["-n", "/usr/local/libexec/whaledeck-privileged", "notification-config", action],
            input, TimeSpan.FromSeconds(10), "NOTIFICATION_PRIVATE_HELPER_FAILED", cancellationToken, acceptedExitCodes: HelperExitCodes);
        if (result.ExitCode != 0)
        {
            string? code = null;
            try { using var error = JsonDocument.Parse(result.StandardError); code = error.RootElement.GetProperty("errorCode").GetString(); }
            catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException) { /* Fixed fallback only. */ }
            var status = code switch
            {
                "VERSION_CONFLICT" or "IDEMPOTENCY_CONFLICT" => StatusCode.Aborted,
                "CONFIG_BUSY" => StatusCode.Unavailable,
                "CONFIG_INVALID" or "NETWORK_DENIED" => StatusCode.InvalidArgument,
                _ => StatusCode.FailedPrecondition
            };
            throw new RpcException(new Status(status, "NOTIFICATION_PRIVATE_REJECTED"));
        }
        try
        {
            using var document = JsonDocument.Parse(result.StandardOutput, new JsonDocumentOptions { MaxDepth = 4 });
            var root = document.RootElement;
            string Value(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : string.Empty;
            bool Flag(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
            var revision = Value("revision");
            if (!Revision(revision)) throw new JsonException();
            return new() { Revision = revision, DestinationReference = Value("destinationReference"), CredentialReference = Value("credentialReference"),
                HasTarget = Flag("hasTarget"), HasCredential = Flag("hasCredential"), Applied = Flag("applied"), Replayed = Flag("replayed"), OperationId = Value("operationId") };
        }
        catch (JsonException) { throw new RpcException(new Status(StatusCode.DataLoss, "NOTIFICATION_PRIVATE_RESULT_INVALID")); }
    }

    private static bool Revision(string value) => value == "EMPTY" || value.Length == 64 && value.All(c => c is >= 'A' and <= 'F' or >= '0' and <= '9');
    private static void Identity(string value)
    { if (!Guid.TryParseExact(value, "D", out var id) || id == Guid.Empty || id.ToString("D") != value) throw new ArgumentException("NOTIFICATION_PRIVATE_INVALID"); }
}
