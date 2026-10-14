using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MailKit;
using MailKit.Security;
using MimeKit;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Notifications;

public sealed class NotificationTransport(PrivateNotificationConfiguration configuration, NotificationNetworkPolicy network, TimeProvider clock) : INotificationTransport
{
    public async Task<INotificationPreparedSend> PrepareAsync(string provider, string destinationReference, string? credentialReference, CancellationToken cancellationToken) =>
        new Prepared(await configuration.ResolveAsync(provider, destinationReference, credentialReference, cancellationToken), network, clock);

    private sealed class Prepared(NotificationPrivateBinding binding, NotificationNetworkPolicy network, TimeProvider clock) : INotificationPreparedSend
    {
        public string ConfigurationRevision => binding.Revision;
        public string TargetIdentity => binding.TargetIdentity;
        public int MinimumIntervalSeconds => binding.Target.MinimumIntervalSeconds;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task<NotificationTransportResult> SendAsync(Guid deliveryId, NotificationEvent message, CancellationToken cancellationToken)
        {
            if (message.Audience != "Administrator" || message.RecipientSubject is not null || message.SourceKind is not ("Alert" or "Job" or "NotificationTest") ||
                message.Severity is not ("Info" or "Warning" or "Error" or "Critical") || string.IsNullOrEmpty(message.MessageCode) || message.MessageCode.Length > 64 ||
                message.MessageCode.Any(value => value is not (>= 'A' and <= 'Z' or >= '0' and <= '9' or '_')))
                return Task.FromResult(NotificationTransportResult.Rejected);
            return binding.Target.Provider == "Email" ? Email(deliveryId, message, cancellationToken) : Http(deliveryId, message, cancellationToken);
        }

        private async Task<NotificationTransportResult> Http(Guid deliveryId, NotificationEvent message, CancellationToken cancellationToken)
        {
            var target = binding.Target; var uri = new Uri(target.Url!); var timestamp = clock.GetUtcNow().ToUnixTimeSeconds();
            var content = NotificationPayload.Serialize(target.Provider, deliveryId, message, timestamp, binding.Credential?.SigningSecret);
            if (target.Provider == "DingTalk")
            {
                var milliseconds = checked(timestamp * 1000).ToString(System.Globalization.CultureInfo.InvariantCulture);
                var sign = NotificationPayload.DingTalkSignature(milliseconds, binding.Credential!.SigningSecret!);
                uri = new Uri(uri.AbsoluteUri + "&timestamp=" + milliseconds + "&sign=" + Uri.EscapeDataString(sign));
            }
            using var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false, UseProxy = false, UseCookies = false, MaxResponseHeadersLength = 16,
                ConnectTimeout = TimeSpan.FromSeconds(5), AutomaticDecompression = DecompressionMethods.None,
                ConnectCallback = async (context, token) =>
                {
                    if (!string.Equals(context.DnsEndPoint.Host, uri.IdnHost, StringComparison.OrdinalIgnoreCase) || context.DnsEndPoint.Port != uri.Port)
                        throw new NotificationConfigurationException("NetworkDenied");
                    return new System.Net.Sockets.NetworkStream(await network.ConnectAsync(uri.IdnHost, uri.Port, target, token), true);
                }
            };
            using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            using var request = new HttpRequestMessage(HttpMethod.Post, uri)
            { Version = HttpVersion.Version11, VersionPolicy = HttpVersionPolicy.RequestVersionExact, Content = new ByteArrayContent(content) };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            request.Headers.Add("X-WhaleDeck-Delivery", deliveryId.ToString("D"));
            if (target.Provider == "Webhook")
            {
                if (!string.IsNullOrEmpty(binding.Credential?.BearerToken)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", binding.Credential.BearerToken);
                if (!string.IsNullOrEmpty(binding.Credential?.SigningSecret))
                {
                    var time = timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    request.Headers.Add("X-WhaleDeck-Timestamp", time);
                    request.Headers.Add("X-WhaleDeck-Signature", NotificationPayload.WebhookSignature(time, deliveryId, content, binding.Credential.SigningSecret));
                }
            }
            try
            {
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if ((int)response.StatusCode >= 500) return NotificationTransportResult.Unknown;
                if (!response.IsSuccessStatusCode) return NotificationTransportResult.Rejected; // Generic 429 does not prove an earlier send was unprocessed.
                if (target.Provider == "Webhook") return NotificationTransportResult.Accepted;
                if (response.Content.Headers.ContentLength > 8192) return NotificationTransportResult.Unknown;
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                var bytes = new byte[8193]; var read = 0;
                while (read < bytes.Length)
                { var count = await stream.ReadAsync(bytes.AsMemory(read), cancellationToken); if (count == 0) break; read += count; }
                if (read == bytes.Length) return NotificationTransportResult.Unknown;
                return NotificationPayload.Result(target.Provider, bytes.AsMemory(0, read));
            }
            catch (HttpRequestException error) when (HasNotSent(error)) { return NotificationTransportResult.RetryableNotSent; }
            catch (HttpRequestException error) when (error.HttpRequestError == HttpRequestError.SecureConnectionError) { return NotificationTransportResult.Rejected; }
            catch (NotificationNotSentException) { return NotificationTransportResult.RetryableNotSent; }
            catch (NotificationConfigurationException) { return NotificationTransportResult.Rejected; }
            catch (Exception) { return NotificationTransportResult.Unknown; }
        }

        private async Task<NotificationTransportResult> Email(Guid deliveryId, NotificationEvent item, CancellationToken cancellationToken)
        {
            var target = binding.Target;
            if (!MailboxAddress.TryParse(target.From, out var from) || from is null || !string.IsNullOrEmpty(from.Name)) return NotificationTransportResult.Rejected;
            using var message = new MimeMessage { Subject = "Whale Deck · " + item.Severity, MessageId = deliveryId.ToString("N") + "@whaledeck.invalid",
                Body = new TextPart("plain") { Text = NotificationPayload.Text(item) } };
            message.From.Add(from);
            foreach (var address in target.To)
            { if (!MailboxAddress.TryParse(address, out var recipient) || recipient is null || !string.IsNullOrEmpty(recipient.Name)) return NotificationTransportResult.Rejected; message.To.Add(recipient); }
            var started = false;
            using var client = new MailKit.Net.Smtp.SmtpClient { Timeout = 15000 };
            // Isolated loopback test fixture only. Production DI never supplies either test option.
            if (network.HasTestCertificate(target)) client.ServerCertificateValidationCallback = (_, certificate, _, _) => network.TestCertificateMatches(certificate, target);
            try
            {
                using var socket = await network.ConnectAsync(target.SmtpHost!, target.SmtpPort, target, cancellationToken);
                await client.ConnectAsync(socket, target.SmtpHost!, target.SmtpPort, target.Tls == "SslOnConnect" ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls, cancellationToken);
                if (!client.IsSecure) return NotificationTransportResult.Rejected;
                if (binding.Credential?.Username is { } username) await client.AuthenticateAsync(username, binding.Credential.Password!, cancellationToken);
                started = true;
                await client.SendAsync(message, cancellationToken);
                return NotificationTransportResult.Accepted; // SMTP acceptance is not recipient delivery.
            }
            catch (NotificationNotSentException) { return NotificationTransportResult.RetryableNotSent; }
            catch (MailKit.Net.Smtp.SmtpCommandException error)
            { return (int)error.StatusCode is >= 400 and <= 499 ? NotificationTransportResult.RetryableNotSent : NotificationTransportResult.Rejected; }
            catch (Exception) { return started ? NotificationTransportResult.Unknown : NotificationTransportResult.Rejected; }
        }
        private static bool HasNotSent(Exception? error)
        { for (var depth = 0; error is not null && depth < 8; depth++, error = error.InnerException) if (error is NotificationNotSentException) return true; return false; }
    }
}

public static class NotificationPayload
{
    public static string Text(NotificationEvent item) => $"Whale Deck | {item.Severity}\n{item.MessageCode}\n{item.SourceKind}: {item.SourceId:D}\nUTC: {item.OccurredAtUtc:O}";
    public static byte[] Serialize(string provider, Guid id, NotificationEvent item, long timestamp, string? secret) => provider switch
    {
        "Webhook" => JsonSerializer.SerializeToUtf8Bytes(new { deliveryId = id, eventId = item.Id, sourceKind = item.SourceKind, sourceId = item.SourceId, severity = item.Severity, code = item.MessageCode, occurredAtUtc = item.OccurredAtUtc }),
        "DingTalk" or "WeCom" => JsonSerializer.SerializeToUtf8Bytes(new { msgtype = "text", text = new { content = Text(item) } }),
        "Feishu" => JsonSerializer.SerializeToUtf8Bytes(new { msg_type = "text", content = new { text = Text(item) }, timestamp = timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture), sign = FeishuSignature(timestamp, secret!) }),
        _ => throw new ArgumentException("Unsupported notification payload type.")
    };
    public static string DingTalkSignature(string timestamp, string secret) => Sign(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(timestamp + "\n" + secret));
    public static string FeishuSignature(long timestamp, string secret) => Sign(Encoding.UTF8.GetBytes(timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n" + secret), []);
    public static string WebhookSignature(string timestamp, Guid id, byte[] content, string secret) => Sign(Encoding.UTF8.GetBytes(secret), [.. Encoding.UTF8.GetBytes(timestamp + "\n" + id.ToString("D") + "\n"), .. content]);
    private static string Sign(byte[] key, byte[] value)
    { try { return Convert.ToBase64String(HMACSHA256.HashData(key, value)); } finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(value); } }
    public static NotificationTransportResult Result(string provider, ReadOnlyMemory<byte> body)
    {
        try
        {
            using var json = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 8 });
            var field = provider == "Feishu" ? "code" : "errcode";
            return json.RootElement.TryGetProperty(field, out var value) && value.TryGetInt32(out var code)
                ? code == 0 ? NotificationTransportResult.Accepted : NotificationTransportResult.Rejected : NotificationTransportResult.Unknown;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException) { return NotificationTransportResult.Unknown; }
    }
}
