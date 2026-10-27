using System.Security.Cryptography;
using System.Text;
using Google.Protobuf;

namespace WhaleDeck.Contracts;

/// <summary>Separate content-bound authority for historical container bytes, never host-root aliases.</summary>
public static class ContainerHistoryBinding
{
    public const string Service = "/whaledeck.agent.v1.ContainerHistoryService/";
    public const string Header = "x-whaledeck-request-sha256";

    public static string Hash(IMessage request)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes("whaledeck-container-history-request-v1\n" + request.Descriptor.FullName + "\n"));
        hash.AppendData(request.ToByteArray());
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    public static string Payload(string expires, string nonce, string method, string requestHash) =>
        $"whaledeck-container-history-capability-v1\n{expires}.{nonce}.{method}.{requestHash}";
}
