using System.Security.Cryptography;
using Microsoft.Net.Http.Headers;
using WhaleDeck.Api.Security;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Api.Files;

/// <summary>Bounded HTTP transport. The final bytes are withheld until the entire RPC/digest/audit succeeds.</summary>
public sealed class FileHttpTransport(FileManagementService files, TerminalCookieIdentity identity)
{
    public async Task DownloadAsync(HttpContext context, TerminalLogin login, FileLocationDto location, string version)
    {
        if (context.Request.Headers.ContainsKey(HeaderNames.Range)) throw new ArgumentException("FILES_RANGE_UNSUPPORTED");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        lifetime.CancelAfter(TimeSpan.FromMinutes(10));
        var monitor = WatchLogin();
        try
        {
            await CopyVerifiedAsync(files.DownloadAsync(login.Actor, location, version, lifetime.Token), context.Response, version, lifetime.Token);
        }
        catch
        {
            if (context.Response.HasStarted) context.Abort();
            throw;
        }
        finally
        {
            lifetime.Cancel();
            try { await monitor; } catch (OperationCanceledException) { }
        }
        async Task WatchLogin()
        {
            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(6));
                while (await timer.WaitForNextTickAsync(lifetime.Token)) await identity.ReadAsync(context, login.Key);
            }
            catch { lifetime.Cancel(); throw; }
        }
    }
    internal static async Task CopyVerifiedAsync(IAsyncEnumerable<FileDownloadPart> parts, HttpResponse response, string version, CancellationToken cancellationToken)
        => await CopyCoreAsync(parts, response, version, FileManagementService.MaximumBytes, cancellationToken);

    internal static async Task CopyArchiveVerifiedAsync(IAsyncEnumerable<FileDownloadPart> parts, HttpResponse response, string version, CancellationToken cancellationToken)
        => await CopyCoreAsync(parts, response, version, FileTreePackingValidation.MaximumArchiveBytes, cancellationToken);

    private static async Task CopyCoreAsync(IAsyncEnumerable<FileDownloadPart> parts, HttpResponse response, string version, long maximumBytes, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        FileEntryDto? metadata = null; byte[]? pending = null; long count = 0; var ended = false;
        await foreach (var part in parts.WithCancellation(cancellationToken))
        {
            if (part.Kind == "metadata" && metadata is null && count == 0 && !ended)
            {
                metadata = part.Metadata;
                if (metadata is null || metadata.Kind != "File" || metadata.IsProtected || metadata.SizeBytes < 0 || metadata.SizeBytes > maximumBytes || metadata.Version != version)
                    throw new InvalidOperationException("FILES_DOWNLOAD_METADATA_INVALID");
                response.ContentType = "application/octet-stream"; response.ContentLength = metadata.SizeBytes;
                response.Headers.CacheControl = "no-store"; response.Headers.XContentTypeOptions = "nosniff";
                var disposition = new ContentDispositionHeaderValue("attachment"); disposition.SetHttpFileName(metadata.Name);
                response.Headers.ContentDisposition = disposition.ToString();
            }
            else if (part.Kind == "chunk" && metadata is not null && !ended && part.Offset == count && part.Data.Length is >= 1 and <= 32768 && count + part.Data.Length <= metadata.SizeBytes)
            {
                if (pending is not null) await Write(pending);
                hash.AppendData(part.Data.Span); count += part.Data.Length; pending = part.Data.ToArray();
            }
            else if (part.Kind == "end" && metadata is not null && !ended && count == metadata.SizeBytes && part.SizeBytes == count && part.Version == version &&
                FileManagementService.Hex(part.Sha256) && Convert.ToHexString(hash.GetHashAndReset()) == part.Sha256) ended = true;
            else throw new InvalidOperationException("FILES_DOWNLOAD_INCOMPLETE");
        }
        // Even after end, MoveNext must complete successfully. A subsequent
        // RPC/audit failure cannot become a successful Content-Length response.
        if (!ended) throw new InvalidOperationException("FILES_DOWNLOAD_INCOMPLETE");
        if (pending is not null) await Write(pending);
        async Task Write(ReadOnlyMemory<byte> data)
        {
            using var write = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); write.CancelAfter(TimeSpan.FromSeconds(10));
            await response.Body.WriteAsync(data, write.Token);
        }
    }
}
