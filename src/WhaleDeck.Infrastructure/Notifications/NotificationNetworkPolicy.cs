using System.Net;
using System.Net.Sockets;
using WhaleDeck.Application.Abstractions;

namespace WhaleDeck.Infrastructure.Notifications;

/// <summary>Pin approved IPs at socket connection, not merely at an earlier DNS lookup.</summary>
public sealed class NotificationNetworkPolicy(bool allowTestLoopback = false, string? testCertificateThumbprint = null)
{
    internal bool HasTestCertificate(NotificationTarget target) => allowTestLoopback && testCertificateThumbprint is not null && target.AllowPrivateNetwork &&
        target.AllowedAddresses.Length > 0 && target.AllowedAddresses.All(value => IPAddress.IsLoopback(IPAddress.Parse(value)));
    internal bool TestCertificateMatches(System.Security.Cryptography.X509Certificates.X509Certificate? certificate, NotificationTarget target) =>
        HasTestCertificate(target) && certificate?.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256) == testCertificateThumbprint;
    public async Task<Socket> ConnectAsync(string host, int port, NotificationTarget target, CancellationToken cancellationToken)
    {
        var approved = target.AllowedAddresses.Select(IPAddress.Parse).Select(Normalize).ToHashSet();
        IPAddress[] resolved;
        try { resolved = IPAddress.TryParse(host, out var literal) ? [literal] : await Dns.GetHostAddressesAsync(host, cancellationToken); }
        catch (SocketException) { throw new NotificationNotSentException(); }
        var addresses = resolved.Select(Normalize).Where(value => Allowed(value, target.AllowPrivateNetwork) &&
            (Private(value) || IPAddress.IsLoopback(value) ? approved.Contains(value) && target.AllowPrivateNetwork : approved.Count == 0 || approved.Contains(value)) &&
            (target.Url is null || !target.Url.StartsWith("http:", StringComparison.Ordinal) || Private(value) || allowTestLoopback && IPAddress.IsLoopback(value))).Take(8).ToArray();
        if (addresses.Length == 0) throw new NotificationConfigurationException("NetworkDenied");
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, port), cancellationToken);
                if (socket.RemoteEndPoint is not IPEndPoint endpoint || !Normalize(endpoint.Address).Equals(address)) throw new NotificationConfigurationException("NetworkDenied");
                return socket;
            }
            catch (SocketException) { socket.Dispose(); }
            catch { socket.Dispose(); throw; }
        }
        throw new NotificationNotSentException();
    }
    public bool Allowed(IPAddress address, bool allowPrivate)
    {
        address = Normalize(address);
        if (IPAddress.IsLoopback(address)) return allowTestLoopback;
        if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) || address.IsIPv6LinkLocal || address.IsIPv6Multicast || address.IsIPv6SiteLocal) return false;
        var bytes = address.GetAddressBytes();
        if (bytes.Length == 4)
        {
            if (bytes[0] is 0 or 127 or >= 224 || bytes[0] == 169 && bytes[1] == 254 || address.Equals(IPAddress.Broadcast)) return false;
            var privateAddress = bytes[0] == 10 || bytes[0] == 172 && bytes[1] is >= 16 and <= 31 || bytes[0] == 192 && bytes[1] == 168 || bytes[0] == 100 && bytes[1] is >= 64 and <= 127;
            return allowPrivate || !privateAddress;
        }
        if (bytes.Length != 16 || bytes.Take(12).All(value => value == 0)) return false;
        if (bytes[0] == 0x00 && bytes[1] == 0x64 || bytes[0] == 0x20 && bytes[1] == 0x02 || bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0 && bytes[3] == 0) return false;
        var uniqueLocal = (bytes[0] & 0xfe) == 0xfc;
        if (!uniqueLocal && (bytes[0] & 0xe0) != 0x20) return false;
        return allowPrivate || !uniqueLocal;
    }
    private static bool Private(IPAddress address)
    {
        var bytes = Normalize(address).GetAddressBytes();
        return bytes.Length == 4 ? bytes[0] == 10 || bytes[0] == 172 && bytes[1] is >= 16 and <= 31 || bytes[0] == 192 && bytes[1] == 168 || bytes[0] == 100 && bytes[1] is >= 64 and <= 127
            : (bytes[0] & 0xfe) == 0xfc;
    }
    private static IPAddress Normalize(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
}
public sealed class NotificationNotSentException() : Exception("The notification connection was not established.");
