using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Services;

public sealed partial class FileTreePackingService
{
    /// <summary>Current administrator read, not original-login authority. The old owner binds only the immutable root receipt.</summary>
    public async IAsyncEnumerable<FileDownloadPart> DownloadAsync(TerminalActor actor, Guid id, string version,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await authorization.AuthorizeAsync(actor, cancellationToken);
        if (id == Guid.Empty || !FileManagementService.Hex(version)) throw new ArgumentException("FILES_PACK_DOWNLOAD_INVALID");
        var row = await repository.ArtifactAsync(id, cancellationToken) ?? throw new KeyNotFoundException("FILES_PACK_ARTIFACT_NOT_FOUND");
        if (!FileTreePackingValidation.HasValidBinding(row, protector) || row.CompletedAtUtc is null || row.State != "Succeeded" ||
            row.PublishedVersion != version || row.ArchiveBytes is not (> 0 and <= FileTreePackingValidation.MaximumArchiveBytes) ||
            !FileManagementService.Hex(row.ArchiveSha256)) throw new InvalidOperationException("FILES_PACK_ARTIFACT_INVALID");
        var target = FileTreePackingValidation.ReadCommand(row).Target;
        await repository.AuditDownloadAsync(actor, row, "Started", cancellationToken);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lifetime.CancelAfter(TimeSpan.FromMinutes(10));
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var metadata = false; var ended = false; var completed = false; long count = 0; var monitor = Monitor();
        try
        {
            // Check immediately before signing the capability, not just before the SQL lookup.
            await authorization.AuthorizeAsync(actor, lifetime.Token);
            await foreach (var part in gateway.DownloadTreePackingAsync(row, actor, lifetime.Token).WithCancellation(lifetime.Token))
            {
                if (part.Kind == "metadata" && !metadata && !ended && part.Metadata is { Kind: "File", IsProtected: false } entry &&
                    entry.SizeBytes == row.ArchiveBytes && entry.Version == version && entry.Name == target.Path.Split('/')[^1]) metadata = true;
                else if (part.Kind == "chunk" && metadata && !ended && part.Offset == count && part.Data.Length is >= 1 and <= 32768 && count + part.Data.Length <= row.ArchiveBytes)
                { hash.AppendData(part.Data.Span); count += part.Data.Length; }
                else if (part.Kind == "end" && metadata && !ended && count == row.ArchiveBytes && part.SizeBytes == count && part.Version == version &&
                    part.Sha256 == row.ArchiveSha256 && Convert.ToHexString(hash.GetHashAndReset()) == row.ArchiveSha256) ended = true;
                else throw new FileTransferRejectedException();
                yield return part;
            }
            if (!ended) throw new FileTransferRejectedException();
            await authorization.AuthorizeAsync(actor, lifetime.Token);
            completed = true;
        }
        finally
        {
            await lifetime.CancelAsync();
            var validated = false;
            try { await monitor; validated = completed; }
            finally
            {
                using var audit = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                // The HTTP transport must await this final audit before releasing its withheld tail.
                await repository.AuditDownloadAsync(actor, row, validated ? "Validated" : "Incomplete", audit.Token);
            }
        }
        async Task Monitor()
        {
            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(6), clock);
                while (await timer.WaitForNextTickAsync(lifetime.Token)) await authorization.AuthorizeAsync(actor, lifetime.Token);
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch { completed = false; await lifetime.CancelAsync(); throw; }
        }
    }
}
