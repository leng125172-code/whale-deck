using System.Security.Cryptography;
using System.Text;
using Google.Protobuf;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Contracts;

/// <summary>Prepared client binding only; neither registers nor advertises a service.</summary>
public static class ContainerRecycleArchiveRetirementBinding
{
    public const uint MinimumProtocolMinor = 60;
    public const string Capability = "docker.files.recycle.archive.retire";
    public const string Service = "/whaledeck.agent.v1.ContainerRecycleArchiveRetirementService/";
    public const string Header = "x-whaledeck-request-sha256";

    public static bool IsSupported(CapabilitiesResponse? value) => value is not null &&
        value.ProtocolMajor == 1 && value.ProtocolMinor >= MinimumProtocolMinor && value.Capabilities.Contains(Capability);

    public static string Hash(IMessage request)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes("whaledeck-container-recycle-archive-retirement-request-v1\n" + request.Descriptor.FullName + "\n"));
        hash.AppendData(request.ToByteArray());
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    public static string Payload(string expires, string nonce, string method, string requestHash) =>
        $"whaledeck-container-recycle-archive-retirement-capability-v1\n{expires}.{nonce}.{method}.{requestHash}";
}
