using Microsoft.Net.Http.Headers;
using WhaleDeck.Api.Security;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Api.Files;

public sealed class ContainerFileHttpTransport(ContainerFileReadService files, TerminalCookieIdentity identity)
{
    public async Task DownloadAsync(HttpContext context, TerminalLogin login, ContainerFileLocationDto location, string version)
    {
        if (context.Request.Headers.ContainsKey(HeaderNames.Range)) throw new ArgumentException("FILES_RANGE_UNSUPPORTED");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        lifetime.CancelAfter(TimeSpan.FromSeconds(310));
        var monitor = WatchLogin();
        try
        {
            await FileHttpTransport.CopyVerifiedAsync(files.DownloadAsync(login.Actor, location, version, ValidateLogin, lifetime.Token),
                context.Response, version, lifetime.Token);
        }
        catch { if (context.Response.HasStarted) context.Abort(); throw; }
        finally
        {
            lifetime.Cancel();
            try { await monitor; } catch (OperationCanceledException) { }
        }
        async Task ValidateLogin(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = await identity.ReadAsync(context, login.Key);
            if (current.Actor.Subject != login.Actor.Subject || current.Actor.LoginBinding != login.Actor.LoginBinding)
                throw new UnauthorizedAccessException("CONTAINER_FILES_LOGIN_CHANGED");
        }
        async Task WatchLogin()
        {
            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(6));
                while (await timer.WaitForNextTickAsync(lifetime.Token)) await ValidateLogin(lifetime.Token);
            }
            catch { lifetime.Cancel(); throw; }
        }
    }
}
