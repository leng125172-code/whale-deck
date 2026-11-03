using System.Security.Cryptography;
using System.Text;
using Google.Protobuf;

namespace WhaleDeck.Contracts;

/// <summary>Independent full-request authority for read-only ACL history; never interchangeable with ACL writes.</summary>
public static class ContainerAclHistoryBinding
{
    public const string Service = "/whaledeck.agent.v1.ContainerAclHistoryService/";
    public const string Header = "x-whaledeck-request-sha256";
    public static string Hash(IMessage request)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes("whaledeck-container-acl-history-request-v1\n" + request.Descriptor.FullName + "\n"));
        hash.AppendData(request.ToByteArray());
        return Convert.ToHexString(hash.GetHashAndReset());
    }
    public static string Payload(string expires, string nonce, string method, string requestHash) =>
        $"whaledeck-container-acl-history-capability-v1\n{expires}.{nonce}.{method}.{requestHash}";
}
