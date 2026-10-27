using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;

namespace WhaleDeck.Infrastructure.Agent;

public sealed partial class FilePlanProtector(IConfiguration configuration) : IFilePlanProtector, IFileMutationProtector, IFileArchiveProtector, IFileTreeCopyProtector, IFileTreeMoveProtector, IFileRecoveryProtector, IFileTreePackingProtector, IFileTreeUnpackingProtector, IFileTreeRecyclingProtector, IFileTreeRestoringProtector, IContainerFileUploadProtector
{
    private static readonly JsonSerializerOptions JsonOptions = new() { MaxDepth = 8 };
    private string Mac(string payload, string domain = "whaledeck-file-plan-v1")
    {
        var key = File.ReadAllBytes(configuration["Agent:CapabilityKeyFile"] ?? "/run/secrets/agent-capability.key");
        try
        {
            if (key.Length != 32) throw new InvalidOperationException("FILES_SIGNING_UNAVAILABLE");
            return Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(domain + "\n" + payload)));
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
    public string Sign(FileUploadPlanClaims claims)
    {
        var payload = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(claims));
        return payload + "." + Mac(payload);
    }
    public string Fingerprint(FileUploadBinding binding) => Mac(JsonSerializer.Serialize(binding), "whaledeck-file-intent-v1");
    string IFileArchiveProtector.Fingerprint(FileArchiveBinding binding) => Mac(JsonSerializer.Serialize(binding), "whaledeck-file-archive-intent-v1");
    public string FingerprintRegistration(FileArchiveBinding binding) => Mac(JsonSerializer.Serialize(binding), "whaledeck-file-archive-registration-v1");
    string IFileArchiveProtector.Sign(FileArchivePlanClaims claims)
    {
        var payload = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(claims));
        return payload + "." + Mac(payload, "whaledeck-file-archive-plan-v1");
    }
    public FileArchivePlanClaims VerifyArchive(string plan)
    {
        if (string.IsNullOrWhiteSpace(plan) || plan.Length > 65536) throw new ArgumentException("FILES_PLAN_INVALID");
        var parts = plan.Split('.');
        if (parts.Length != 2 || parts[1].Length != 64 || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(parts[1]), Encoding.ASCII.GetBytes(Mac(parts[0], "whaledeck-file-archive-plan-v1"))))
            throw new ArgumentException("FILES_PLAN_SIGNATURE_INVALID");
        try { return JsonSerializer.Deserialize<FileArchivePlanClaims>(Convert.FromBase64String(parts[0]), WhaleDeck.Application.Services.FileArchiveValidation.JsonOptions) ?? throw new ArgumentException("FILES_PLAN_INVALID"); }
        catch (Exception error) when (error is JsonException or FormatException) { throw new ArgumentException("FILES_PLAN_INVALID"); }
    }
    string IFileMutationProtector.Fingerprint(FileMutationBinding binding) => Mac(JsonSerializer.Serialize(binding), "whaledeck-file-mutation-intent-v1");
    string IFileMutationProtector.Sign(FileMutationPlanClaims claims)
    {
        var payload = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(claims));
        return payload + "." + Mac(payload, "whaledeck-file-mutation-plan-v1");
    }
    public FileMutationPlanClaims VerifyMutation(string plan)
    {
        // Two 4096-byte Unicode paths can expand past 32 KiB after JSON
        // escaping and Base64. Keep a finite bound without rejecting a plan
        // produced by this server for otherwise valid registered locations.
        if (string.IsNullOrWhiteSpace(plan) || plan.Length > 65536) throw new ArgumentException("FILES_PLAN_INVALID");
        var parts = plan.Split('.');
        if (parts.Length != 2 || parts[1].Length != 64 || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(parts[1]), Encoding.ASCII.GetBytes(Mac(parts[0], "whaledeck-file-mutation-plan-v1"))))
            throw new ArgumentException("FILES_PLAN_SIGNATURE_INVALID");
        try { return JsonSerializer.Deserialize<FileMutationPlanClaims>(Convert.FromBase64String(parts[0]), JsonOptions) ?? throw new ArgumentException("FILES_PLAN_INVALID"); }
        catch (Exception error) when (error is JsonException or FormatException) { throw new ArgumentException("FILES_PLAN_INVALID"); }
    }
    public FileUploadPlanClaims Verify(string plan)
    {
        if (string.IsNullOrWhiteSpace(plan) || plan.Length > 32768) throw new ArgumentException("FILES_PLAN_INVALID");
        var parts = plan.Split('.');
        if (parts.Length != 2 || parts[1].Length != 64 || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(parts[1]), Encoding.ASCII.GetBytes(Mac(parts[0]))))
            throw new ArgumentException("FILES_PLAN_SIGNATURE_INVALID");
        try { return JsonSerializer.Deserialize<FileUploadPlanClaims>(Convert.FromBase64String(parts[0]), JsonOptions) ?? throw new ArgumentException("FILES_PLAN_INVALID"); }
        catch (Exception error) when (error is JsonException or FormatException) { throw new ArgumentException("FILES_PLAN_INVALID"); }
    }
}
