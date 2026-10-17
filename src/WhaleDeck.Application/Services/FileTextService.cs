using System.Security.Cryptography;
using System.Text;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Services;

/// <summary>Bounded strict UTF-8 preview; saves reuse the sealed upload/Worker/HDD publication workflow.</summary>
public sealed class FileTextService(FileManagementService files, IFileGateway gateway, IFileUploadRepository repository, TimeProvider clock)
{
    public const int MaximumBytes = 1024 * 1024;
    public async Task<FileTextDto> ReadAsync(TerminalActor actor, FileLocationDto location, string expectedVersion, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await files.AuthorizeAsync(actor, cancellationToken); FileManagementService.Location(location);
        if (!FileManagementService.Hex(expectedVersion)) throw new ArgumentException("FILES_HASH_INVALID");
        await repository.AuditReadAsync(actor, location, "Started", FileReadPurpose.TextPreview, cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var body = new MemoryStream(); using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var validated = false; var ended = false; long size = -1; string? checksum = null; var nextAuthorization = clock.GetUtcNow().AddSeconds(6);
        try
        {
            await foreach (var frame in gateway.DownloadFileAsync(location, expectedVersion, timeout.Token))
            {
                timeout.Token.ThrowIfCancellationRequested();
                if (clock.GetUtcNow() >= nextAuthorization)
                { await files.AuthorizeAsync(actor, timeout.Token); nextAuthorization = clock.GetUtcNow().AddSeconds(6); }
                if (ended) throw new InvalidOperationException("FILES_TEXT_STREAM_INVALID");
                if (frame.Kind == "metadata" && size == -1 && frame.Metadata is { } entry && entry.Kind == "File" && !entry.IsProtected &&
                    entry.Version == expectedVersion && entry.Name == location.Path[(location.Path.LastIndexOf('/') + 1)..])
                {
                    if (entry.SizeBytes is < 0 or > MaximumBytes) throw new ArgumentException("FILES_TEXT_SIZE_LIMIT");
                    size = entry.SizeBytes;
                }
                else if (frame.Kind == "chunk" && size >= 0 && frame.Offset == body.Length && frame.Data.Length is > 0 and <= 32768 && body.Length + frame.Data.Length <= size)
                { hash.AppendData(frame.Data.Span); body.Write(frame.Data.Span); }
                else if (frame.Kind == "end" && size >= 0 && frame.SizeBytes == size && body.Length == size && frame.Version == expectedVersion && FileManagementService.Hex(frame.Sha256))
                {
                    checksum = Convert.ToHexString(hash.GetHashAndReset());
                    if (checksum != frame.Sha256) throw new InvalidOperationException("FILES_TEXT_CHECKSUM_MISMATCH");
                    ended = true;
                }
                else throw new InvalidOperationException("FILES_TEXT_STREAM_INVALID");
            }
            if (!ended || checksum is null) throw new InvalidOperationException("FILES_TEXT_STREAM_INCOMPLETE");
            var bytes = body.GetBuffer().AsSpan(0, checked((int)body.Length));
            var bom = bytes.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF });
            string text;
            try { text = new UTF8Encoding(false, true).GetString(bytes[(bom ? 3 : 0)..]); }
            catch (DecoderFallbackException) { throw new ArgumentException("FILES_TEXT_UTF8_REQUIRED"); }
            if (text.Contains('\0', StringComparison.Ordinal)) throw new ArgumentException("FILES_TEXT_BINARY_REJECTED");
            var withoutPairs = text.Replace("\r\n", string.Empty, StringComparison.Ordinal);
            var crlf = text.Contains("\r\n", StringComparison.Ordinal); var cr = withoutPairs.Contains('\r', StringComparison.Ordinal); var lf = withoutPairs.Contains('\n', StringComparison.Ordinal);
            var kinds = (crlf ? 1 : 0) + (cr ? 1 : 0) + (lf ? 1 : 0);
            var ending = kinds > 1 ? "Mixed" : crlf ? "CRLF" : cr ? "CR" : lf ? "LF" : "None";
            var result = new FileTextDto(location.RootId, location.Path, expectedVersion, size, checksum, text, bom, ending);
            // Never return content until its successful audit is durable.
            await repository.AuditReadAsync(actor, location, "Validated", FileReadPurpose.TextPreview, timeout.Token);
            validated = true; return result;
        }
        finally
        {
            if (!validated)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await repository.AuditReadAsync(actor, location, "Incomplete", FileReadPurpose.TextPreview, cleanup.Token);
            }
        }
    }
}
