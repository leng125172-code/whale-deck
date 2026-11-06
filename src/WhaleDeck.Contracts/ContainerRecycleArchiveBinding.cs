using System.Security.Cryptography;
using System.Text;
using Google.Protobuf;

namespace WhaleDeck.Contracts;

public static class ContainerRecycleArchiveBinding
{
    public const string Service = "/whaledeck.agent.v1.ContainerRecycleArchiveService/";
    public const string Header = "x-whaledeck-request-sha256";
    public static string Hash(IMessage request)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes("whaledeck-container-recycle-archive-request-v1\n" + request.Descriptor.FullName + "\n"));
        hash.AppendData(request.ToByteArray()); return Convert.ToHexString(hash.GetHashAndReset());
    }
    public static string Payload(string expires, string nonce, string method, string requestHash) =>
        $"whaledeck-container-recycle-archive-capability-v1\n{expires}.{nonce}.{method}.{requestHash}";
}
