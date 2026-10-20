using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Agent;

namespace WhaleDeck.UnitTests.Application;

/// <summary>In-memory catalogue/signature compatibility only, without persistence or privileged I/O.</summary>
public sealed class FileTreeCaptureKindTests : IDisposable
{
    private readonly string _keyPath = Path.Combine(Path.GetTempPath(), "wd-capture-kind-" + Guid.NewGuid().ToString("N"));
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    private readonly FilePlanProtector _protector;

    public FileTreeCaptureKindTests()
    {
        File.WriteAllBytes(_keyPath, _key);
        _protector = new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Agent:CapabilityKeyFile"] = _keyPath }).Build());
    }

    public void Dispose()
    {
        File.Delete(_keyPath);
        CryptographicOperations.ZeroMemory(_key);
    }

    private static FileTreeCapture Clone(FileTreeCapture row, Action<JsonObject> change)
    {
        var value = JsonSerializer.SerializeToNode(row)!.AsObject(); change(value);
        return value.Deserialize<FileTreeCapture>()!;
    }

    private FileTreeCapture Capture(string kind)
    {
        var command = new FileTreeRecyclingCommand(new("test.tree", "目录/独立捕获"), new('A', 64));
        var intent = Guid.Parse("cd79a630-733c-4208-a97a-b92f4e01c32f");
        var row = new FileTreeCapture {
            Id = kind == "Trash" ? FileTreeRecyclingValidation.ExecutionId(intent) : FileTreeOverwriteRestoringValidation.ExecutionId(intent),
            IntentId = intent, OwnerHash = new('B', 64), SourceHash = new FilePathScope(command.Source).Hash,
            SourceJson = JsonSerializer.Serialize(command), ManifestSha256 = new('C', 64),
            EntryCount = 3, TotalBytes = 1073741831, ArchiveBytes = 1073756160, ArchiveSha256 = new('D', 64),
            ProofHash = new('0', 64), CreatedAtUtc = DateTimeOffset.Parse("2026-10-20T12:34:56.123456Z", System.Globalization.CultureInfo.InvariantCulture) };
        // Leave the historical constructor/default Kind untouched for legacy captures.
        if (kind == "Overwrite") row = Clone(row, value => { value["Kind"] = kind; value["ContentSha256"] = new string('E', 64); });
        return Sign(row);
    }

    private FileTreeCapture Sign(FileTreeCapture row)
    {
        var proof = row.Kind == "Trash" ? _protector.FingerprintCapture(FileTreeRecyclingValidation.CaptureBinding(row))
            : _protector.FingerprintCapture(FileTreeCaptureValidation.OverwrittenBinding(row));
        return Clone(row, value => value["ProofHash"] = proof);
    }

    private FileTreeCaptureDto Verify(FileTreeCapture row) => FileTreeCaptureValidation.Verify(row, _protector, _protector);
    private string Mac<T>(T payload, string domain) => Convert.ToHexString(HMACSHA256.HashData(_key,
        Encoding.UTF8.GetBytes(domain + "\n" + JsonSerializer.Serialize(payload))));

    [Fact]
    public void HistoricalTrashDefaultsAndExactLegacySigningPayloadRemainCompatible()
    {
        var row = Capture("Trash");
        Assert.Equal("Trash", row.Kind); Assert.Null(row.ContentSha256);
        var binding = FileTreeRecyclingValidation.CaptureBinding(row);
        var legacy = new {
            binding.Id, binding.IntentId, binding.OwnerHash, binding.SourceHash, binding.Command,
            binding.ManifestSha256, binding.EntryCount, binding.TotalBytes, binding.ArchiveBytes, binding.ArchiveSha256, binding.CreatedAtUtc };
        Assert.Equal(JsonSerializer.Serialize(legacy), JsonSerializer.Serialize(binding));
        Assert.Equal(Mac(legacy, "whaledeck-tree-trash-capture-v1"), row.ProofHash);
        Assert.DoesNotContain("\"Kind\"", JsonSerializer.Serialize(binding), StringComparison.Ordinal);
        Assert.DoesNotContain("\"ContentSha256\"", JsonSerializer.Serialize(binding), StringComparison.Ordinal);
        FileTreeRecyclingValidation.Capture(row, _protector);
        var dto = Verify(row);
        Assert.Equal("Trash", dto.Kind); Assert.Null(dto.ContentSha256);
        var legacyRow = Clone(row, value => { value.Remove("Kind"); value.Remove("ContentSha256"); });
        Assert.Equal("Trash", legacyRow.Kind); Assert.Null(legacyRow.ContentSha256);
        Assert.Equal(dto, Verify(legacyRow));
        var legacyDto = new FileTreeCaptureDto(row.Id, row.IntentId, binding.Command, row.ManifestSha256, row.EntryCount,
            row.TotalBytes, row.ArchiveBytes, row.ArchiveSha256, row.CreatedAtUtc);
        Assert.Equal(dto, legacyDto);
    }

    [Fact]
    public void OverwriteHasIndependentExecutionContentProofAndHmacAndPreservesDtoFields()
    {
        var row = Capture("Overwrite");
        Assert.NotEqual(FileTreeRecyclingValidation.ExecutionId(row.IntentId), row.Id);
        Assert.Equal(FileTreeOverwriteRestoringValidation.ExecutionId(row.IntentId), row.Id);
        var binding = FileTreeCaptureValidation.OverwrittenBinding(row);
        Assert.Equal(row.ContentSha256, binding.ContentSha256);
        Assert.Equal(Mac(binding, "whaledeck-tree-overwrite-capture-v1"), row.ProofHash);
        Assert.NotEqual(Mac(binding, "whaledeck-tree-trash-capture-v1"), row.ProofHash);
        var dto = Verify(row);
        Assert.Equal("Overwrite", dto.Kind); Assert.Equal(row.ContentSha256, dto.ContentSha256);
        Assert.Equal(row.Id, dto.Id); Assert.Equal(row.IntentId, dto.IntentId);
        Assert.Equal(binding.Command, dto.Source); Assert.Equal(row.ManifestSha256, dto.ManifestSha256);
        Assert.Equal(row.ArchiveSha256, dto.ArchiveSha256); Assert.Equal(row.CreatedAtUtc, dto.CreatedAtUtc);
        Assert.Throws<FileTransferRejectedException>(() => FileTreeRecyclingValidation.Capture(row, _protector));
    }

    [Theory]
    [InlineData("Trash", "Overwrite")][InlineData("Overwrite", "Trash")]
    [InlineData("Trash", null)][InlineData("Overwrite", null)]
    [InlineData("Trash", "trash")][InlineData("Overwrite", "overwrite")]
    [InlineData("Trash", "Unknown")][InlineData("Overwrite", "")]
    public void SwitchingNullOrUnknownKindCannotReuseAValidCapture(string original, string? changed)
    {
        var row = Clone(Capture(original), value => value["Kind"] = changed);
        Assert.Throws<FileTransferRejectedException>(() => Verify(row));
    }

    [Fact]
    public void NullCaptureAndWrongContentProofPresenceAreRejected()
    {
        Assert.Throws<FileTransferRejectedException>(() => Verify(null!));
        foreach (var content in new string?[] { "", new('E', 64) })
        {
            var trash = Clone(Capture("Trash"), value => value["ContentSha256"] = content);
            Assert.Throws<FileTransferRejectedException>(() => Verify(trash));
            Assert.Throws<FileTransferRejectedException>(() => FileTreeRecyclingValidation.Capture(trash, _protector));
        }
        foreach (var content in new string?[] { null, "", "bad" })
        {
            var overwrite = Clone(Capture("Overwrite"), value => value["ContentSha256"] = content);
            Assert.Throws<FileTransferRejectedException>(() => Verify(overwrite));
        }
    }

    [Theory]
    [InlineData("Trash")][InlineData("Overwrite")]
    public void CaptureRejectsEveryPersistedIdentityMetadataAndArchiveTamper(string kind)
    {
        var original = Capture(kind);
        foreach (var field in new[] { "Id", "IntentId", "OwnerHash", "SourceHash", "SourceJson", "ManifestSha256",
            "EntryCount", "TotalBytes", "ArchiveBytes", "ArchiveSha256", "CreatedAtUtc", "ProofHash" })
        {
            var changed = Clone(original, value => {
                switch (field)
                {
                    case "Id": case "IntentId": value[field] = Guid.NewGuid(); break;
                    case "SourceJson": value[field] = JsonSerializer.Serialize(new FileTreeRecyclingCommand(new("test.tree", "different"), new('A', 64))); break;
                    case "EntryCount": value[field] = original.EntryCount + 1; break;
                    case "TotalBytes": value[field] = original.TotalBytes + 1; break;
                    case "ArchiveBytes": value[field] = original.ArchiveBytes + 1; break;
                    case "CreatedAtUtc": value[field] = original.CreatedAtUtc.AddMicroseconds(1); break;
                    default: value[field] = new string('F', 64); break;
                }
            });
            Assert.Throws<FileTransferRejectedException>(() => Verify(changed));
            if (kind == "Trash") Assert.Throws<FileTransferRejectedException>(() => FileTreeRecyclingValidation.Capture(changed, _protector));
        }
    }

    [Fact]
    public void IndependentOverwriteContentProofCannotBeTamperedWithoutResigning()
    {
        var row = Clone(Capture("Overwrite"), value => value["ContentSha256"] = new string('F', 64));
        Assert.Throws<FileTransferRejectedException>(() => Verify(row));
        Assert.Equal(new string('F', 64), Verify(Sign(row)).ContentSha256);
    }

    [Theory]
    [InlineData("Trash")][InlineData("Overwrite")]
    public void ValidHmacStillCannotUseExecutionIdFromTheOtherPurpose(string kind)
    {
        var row = Capture(kind);
        var otherExecution = kind == "Trash" ? FileTreeOverwriteRestoringValidation.ExecutionId(row.IntentId)
            : FileTreeRecyclingValidation.ExecutionId(row.IntentId);
        row = Sign(Clone(row, value => value["Id"] = otherExecution));
        Assert.Throws<FileTransferRejectedException>(() => Verify(row));
    }

    [Theory]
    [InlineData("Trash")][InlineData("Overwrite")]
    public void ExactPayloadSignedForTheOtherCapturePurposeCannotAuthorizeCatalogueRow(string kind)
    {
        var row = Capture(kind);
        var wrongProof = kind == "Trash"
            ? Mac(FileTreeRecyclingValidation.CaptureBinding(row), "whaledeck-tree-overwrite-capture-v1")
            : Mac(FileTreeCaptureValidation.OverwrittenBinding(row), "whaledeck-tree-trash-capture-v1");
        row = Clone(row, value => value["ProofHash"] = wrongProof);
        Assert.Throws<FileTransferRejectedException>(() => Verify(row));
    }

    [Theory]
    [InlineData("Trash")][InlineData("Overwrite")]
    public void CorrectlySignedSourceHashStillMustMatchTheRegisteredSource(string kind)
    {
        var changed = Sign(Clone(Capture(kind), value => value["SourceHash"] = new string('F', 64)));
        Assert.Throws<FileTransferRejectedException>(() => Verify(changed));
    }

    [Theory]
    [InlineData("Trash")][InlineData("Overwrite")]
    public void SignedCountsBytesAndArchiveProofCannotExceedTheirIndependentLimits(string kind)
    {
        var row = Capture(kind);
        var edge = Sign(Clone(row, value => {
            value["EntryCount"] = 10000; value["TotalBytes"] = FileTreeOverwriteRestoringValidation.MaximumBytes;
            value["ArchiveBytes"] = FileTreeOverwriteRestoringValidation.MaximumArchiveBytes;
        }));
        Verify(edge);
        foreach (var field in new[] { "EntryCount", "TotalBytes", "ArchiveBytes" })
        {
            var invalid = Sign(Clone(edge, value => {
                value[field] = field switch {
                    "EntryCount" => 10001L, "TotalBytes" => FileTreeOverwriteRestoringValidation.MaximumBytes + 1,
                    _ => FileTreeOverwriteRestoringValidation.MaximumArchiveBytes + 1 };
            }));
            Assert.Throws<FileTransferRejectedException>(() => Verify(invalid));
        }
    }
}
