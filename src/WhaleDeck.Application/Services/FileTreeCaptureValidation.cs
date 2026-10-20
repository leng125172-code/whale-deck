using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

/// <summary>Preserves the legacy Trash signing payload; Overwrite has a distinct purpose and content proof.</summary>
public static class FileTreeCaptureValidation
{
    public static FileTreeCaptureDto Verify(FileTreeCapture row, IFileTreeRecyclingProtector trash, IFileTreeOverwriteRestoringProtector overwrite)
    {
        if (row is null) throw new FileTransferRejectedException();
        FileTreeRecyclingCommand source;
        if (row.Kind == "Trash")
        {
            FileTreeRecyclingValidation.Capture(row, trash);
            source = FileTreeRecyclingValidation.CaptureBinding(row).Command;
        }
        else if (row.Kind == "Overwrite")
        {
            var binding = OverwrittenBinding(row); source = binding.Command;
            FileTreeRecyclingValidation.Command(source);
            if (row.Id == Guid.Empty || row.IntentId == Guid.Empty || row.Id != FileTreeOverwriteRestoringValidation.ExecutionId(row.IntentId) ||
                !FileManagementService.Hex(row.OwnerHash) || new FilePathScope(source.Source).Hash != row.SourceHash ||
                !FileManagementService.Hex(row.ManifestSha256) || !FileManagementService.Hex(row.ContentSha256) ||
                row.EntryCount is < 1 or > FileTreeOverwriteRestoringValidation.MaximumEntries || row.TotalBytes is < 0 or > FileTreeOverwriteRestoringValidation.MaximumBytes ||
                row.ArchiveBytes is < 1 or > FileTreeOverwriteRestoringValidation.MaximumArchiveBytes || !FileManagementService.Hex(row.ArchiveSha256) ||
                !Equal(row.ProofHash, overwrite.FingerprintCapture(binding))) throw new FileTransferRejectedException();
        }
        else throw new FileTransferRejectedException();
        return new(row.Id, row.IntentId, source, row.ManifestSha256, row.EntryCount, row.TotalBytes, row.ArchiveBytes, row.ArchiveSha256, row.CreatedAtUtc, row.Kind, row.ContentSha256);
    }

    public static FileTreeOverwrittenCaptureBinding OverwrittenBinding(FileTreeCapture row)
    {
        if (row.Kind != "Overwrite" || row.ContentSha256 is null) throw new FileTransferRejectedException();
        return new(row.Id, row.IntentId, row.OwnerHash, row.SourceHash,
            JsonSerializer.Deserialize<FileTreeRecyclingCommand>(row.SourceJson, FileTreeRecyclingValidation.JsonOptions) ?? throw new FileTransferRejectedException(),
            row.ManifestSha256, row.ContentSha256, row.EntryCount, row.TotalBytes, row.ArchiveBytes, row.ArchiveSha256, row.CreatedAtUtc);
    }

    private static bool Equal(string left, string right) => FileManagementService.Hex(left) && FileManagementService.Hex(right) &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(left), Encoding.ASCII.GetBytes(right));
}
