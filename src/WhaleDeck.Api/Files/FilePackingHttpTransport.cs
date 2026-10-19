using Microsoft.Net.Http.Headers;
using WhaleDeck.Api.Security;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Api.Files;

/// <summary>Receipt-only large archive download. Does not enlarge the general file download limit.</summary>
public sealed class FilePackingHttpTransport(FileTreePackingService files, TerminalCookieIdentity identity)
{
    public async Task DownloadAsync(HttpContext context, TerminalLogin login, Guid id, string version)
    {
        if (context.Request.Headers.ContainsKey(HeaderNames.Range)) throw new ArgumentException("FILES_RANGE_UNSUPPORTED");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        lifetime.CancelAfter(TimeSpan.FromMinutes(10)); var monitor = WatchLogin();
        try { await FileHttpTransport.CopyArchiveVerifiedAsync(files.DownloadAsync(login.Actor, id, version, lifetime.Token), context.Response, version, lifetime.Token); }
        catch { if (context.Response.HasStarted) context.Abort(); throw; }
        finally { await lifetime.CancelAsync(); try { await monitor; } catch (OperationCanceledException) { } }
        async Task WatchLogin()
        {
            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(6));
                while (await timer.WaitForNextTickAsync(lifetime.Token)) await identity.ReadAsync(context, login.Key);
            }
            catch { await lifetime.CancelAsync(); throw; }
        }
    }
}
