using System.Security.Cryptography;
using System.Text;
using Google.Protobuf;

namespace WhaleDeck.Contracts;

/// <summary>Canonical request binding shared by the trusted caller and Agent.</summary>
public static class ContainerFileWriteBinding
{
    public const string Service = "/whaledeck.agent.v1.ContainerFileWriteService/";
    public const string Header = "x-whaledeck-request-sha256";

    public static string Hash(IMessage message)
    {
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        digest.AppendData(Encoding.UTF8.GetBytes("whaledeck-container-file-request-v1\n" + message.Descriptor.FullName + "\n"));
        digest.AppendData(message.ToByteArray());
        return Convert.ToHexString(digest.GetHashAndReset());
    }

    public static string Payload(string expires, string nonce, string method, string requestHash) =>
        $"whaledeck-container-file-capability-v1\n{expires}.{nonce}.{method}.{requestHash}";
}
