using System.Security.Cryptography;
using System.Text;
using Google.Protobuf;

namespace WhaleDeck.Contracts;

/// <summary>Independent method, message and complete-plan authority for bounded recursive container ownership.</summary>
public static class ContainerRecursiveOwnershipBinding
{
    public const string Service = "/whaledeck.agent.v1.ContainerRecursiveOwnershipService/";
    public const string Header = "x-whaledeck-request-sha256";
    public static string Hash(IMessage request)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes("whaledeck-container-recursive-ownership-request-v1\n" + request.Descriptor.FullName + "\n"));
        hash.AppendData(request.ToByteArray());
        return Convert.ToHexString(hash.GetHashAndReset());
    }
    public static string Payload(string expires, string nonce, string method, string requestHash) =>
        $"whaledeck-container-recursive-ownership-capability-v1\n{expires}.{nonce}.{method}.{requestHash}";
}
