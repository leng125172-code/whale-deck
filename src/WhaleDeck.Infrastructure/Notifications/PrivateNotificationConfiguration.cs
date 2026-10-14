using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using WhaleDeck.Application.Abstractions;

namespace WhaleDeck.Infrastructure.Notifications;

public sealed class NotificationTarget
{
    public required string Provider { get; init; }
    public string? Url { get; init; }
    public string? SmtpHost { get; init; }
    public int SmtpPort { get; init; } = 587;
    public string Tls { get; init; } = "StartTls";
    public string? From { get; init; }
    public string[] To { get; init; } = [];
    public required string[] AllowedAddresses { get; init; }
    public bool AllowPrivateNetwork { get; init; }
    public bool AllowHttp { get; init; }
    public int MinimumIntervalSeconds { get; init; } = 5;
    public override string ToString() => "NotificationTarget [private]";
}

public sealed class NotificationCredential
{
    public string? BearerToken { get; init; }
    public string? SigningSecret { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }
    public override string ToString() => "NotificationCredential [private]";
}

public sealed class NotificationPrivateBinding(NotificationTarget target, NotificationCredential? credential, string revision, string targetIdentity)
{
    public NotificationTarget Target { get; } = target;
    public NotificationCredential? Credential { get; } = credential;
    public string Revision { get; } = revision;
    public string TargetIdentity { get; } = targetIdentity;
    public override string ToString() => "NotificationPrivateBinding [private]";
}

public sealed partial class PrivateNotificationConfiguration(string path)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 8 };

    public async Task<NotificationPrivateBinding> ResolveAsync(string provider, string destinationReference, string? credentialReference, CancellationToken cancellationToken)
    {
        byte[]? bytes = null;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) throw new NotificationConfigurationException("ConfigurationMissing");
            if (info.LinkTarget is not null || (info.Attributes & FileAttributes.ReparsePoint) != 0 || info.Length is <= 0 or > 65536)
                throw new NotificationConfigurationException("ConfigurationInvalid");
            if (!OperatingSystem.IsWindows())
            {
                var mode = File.GetUnixFileMode(path);
                if ((mode & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0)
                    throw new NotificationConfigurationException("ConfigurationInvalid");
            }
            await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 4096, true);
            if (file.Length is <= 0 or > 65536) throw new NotificationConfigurationException("ConfigurationInvalid");
            bytes = new byte[file.Length]; await file.ReadExactlyAsync(bytes, cancellationToken);
            using (var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 })) NoDuplicates(document.RootElement);
            var registry = JsonSerializer.Deserialize<Registry>(bytes, JsonOptions) ?? throw new NotificationConfigurationException("ConfigurationInvalid");
            if (registry.Version != 1 || registry.Targets is null || registry.Credentials is null || registry.Targets.Count > 32 || registry.Credentials.Count > 32 ||
                registry.Targets.Keys.Concat(registry.Credentials.Keys).Any(key => !Reference().IsMatch(key))) throw new NotificationConfigurationException("ConfigurationInvalid");
            if (!Reference().IsMatch(destinationReference) || !registry.Targets.TryGetValue(destinationReference, out var target) || target is null || target.Provider != provider)
                throw new NotificationConfigurationException("BindingMissing");
            NotificationCredential? credential = null;
            if (credentialReference is not null && (!Reference().IsMatch(credentialReference) || !registry.Credentials.TryGetValue(credentialReference, out credential) || credential is null))
                throw new NotificationConfigurationException("BindingMissing");
            Validate(target, credential);
            var canonical = JsonSerializer.SerializeToUtf8Bytes(new { target, credential }, JsonOptions);
            var endpoint = JsonSerializer.SerializeToUtf8Bytes(new { target.Provider, Url = target.Url is null ? null : new Uri(target.Url).AbsoluteUri,
                Host = target.SmtpHost?.ToLowerInvariant().TrimEnd('.'), Port = target.Provider == "Email" ? target.SmtpPort : 0 });
            try { return new(target, credential, Convert.ToHexString(SHA256.HashData(canonical)), Convert.ToHexString(SHA256.HashData(endpoint))); }
            finally { CryptographicOperations.ZeroMemory(canonical); CryptographicOperations.ZeroMemory(endpoint); }
        }
        catch (NotificationConfigurationException) { throw; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { throw new NotificationConfigurationException("ConfigurationInvalid"); }
        finally { if (bytes is not null) CryptographicOperations.ZeroMemory(bytes); }
    }

    private static void Validate(NotificationTarget target, NotificationCredential? credential)
    {
        if (target.Provider is not ("Email" or "Webhook" or "DingTalk" or "WeCom" or "Feishu") || target.MinimumIntervalSeconds is < 5 or > 300 ||
            target.AllowedAddresses is null || target.AllowedAddresses.Length > 8 || target.AllowPrivateNetwork && target.AllowedAddresses.Length == 0 || target.AllowedAddresses.Any(value => !IPAddress.TryParse(value, out _)) ||
            target.SmtpPort is < 1 or > 65535 || target.Tls is not ("StartTls" or "SslOnConnect")) throw new NotificationConfigurationException("ConfigurationInvalid");
        foreach (var value in new[] { target.Url, target.SmtpHost, target.From, credential?.BearerToken, credential?.SigningSecret, credential?.Username, credential?.Password })
            if (value is { } text && (text.Length > 4096 || text.Any(char.IsControl))) throw new NotificationConfigurationException("ConfigurationInvalid");
        if (target.Provider == "Email")
        {
            if (string.IsNullOrWhiteSpace(target.SmtpHost) || Uri.CheckHostName(target.SmtpHost) == UriHostNameType.Unknown || string.IsNullOrWhiteSpace(target.From) ||
                target.To is null || target.To.Length is < 1 or > 10 || target.To.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 254 || value.Any(char.IsControl)) ||
                target.Url is not null || target.AllowHttp || credential?.BearerToken is not null || credential?.SigningSecret is not null ||
                (credential?.Username is null) != (credential?.Password is null)) throw new NotificationConfigurationException("ConfigurationInvalid");
        }
        else
        {
            if (!Uri.TryCreate(target.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 ||
                uri.Port is < 1 or > 65535 || uri.Scheme == "http" && (!target.AllowHttp || !target.AllowPrivateNetwork) ||
                target.SmtpHost is not null || target.From is not null || target.To is null || target.To.Length != 0 || credential?.Username is not null || credential?.Password is not null)
                throw new NotificationConfigurationException("ConfigurationInvalid");
            if (target.Provider != "Webhook" && (uri.Scheme != "https" || credential?.BearerToken is not null)) throw new NotificationConfigurationException("ConfigurationInvalid");
            if (target.Provider is "DingTalk" or "Feishu" && string.IsNullOrEmpty(credential?.SigningSecret)) throw new NotificationConfigurationException("ConfigurationInvalid");
            if (target.Provider == "WeCom" && credential is not null) throw new NotificationConfigurationException("ConfigurationInvalid");
            var legalBot = target.Provider switch
            {
                "DingTalk" => uri.Host == "oapi.dingtalk.com" && uri.AbsolutePath == "/robot/send" && uri.Query.StartsWith("?access_token=", StringComparison.Ordinal),
                "WeCom" => uri.Host == "qyapi.weixin.qq.com" && uri.AbsolutePath == "/cgi-bin/webhook/send" && uri.Query.StartsWith("?key=", StringComparison.Ordinal),
                "Feishu" => uri.Host == "open.feishu.cn" && uri.AbsolutePath.StartsWith("/open-apis/bot/v2/hook/", StringComparison.Ordinal) && uri.Query.Length == 0,
                _ => true
            };
            if (!legalBot) throw new NotificationConfigurationException("ConfigurationInvalid");
        }
    }
    private static void NoDuplicates(JsonElement item)
    {
        if (item.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in item.EnumerateObject())
            { if (!keys.Add(property.Name)) throw new NotificationConfigurationException("ConfigurationInvalid"); NoDuplicates(property.Value); }
        }
        else if (item.ValueKind == JsonValueKind.Array) foreach (var child in item.EnumerateArray()) NoDuplicates(child);
    }
    [GeneratedRegex("\\A[a-z][a-z0-9-]{0,63}\\z", RegexOptions.CultureInvariant)] private static partial Regex Reference();
    private sealed class Registry
    {
        public Registry() { }
        public int Version { get; init; }
        public Dictionary<string, NotificationTarget> Targets { get; init; } = [];
        public Dictionary<string, NotificationCredential> Credentials { get; init; } = [];
    }
}
