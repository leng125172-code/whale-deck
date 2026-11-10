using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using WhaleDeck.Api.Security;
using WhaleDeck.Application.Development;

namespace WhaleDeck.Api.Controllers;

[ApiController, Authorize, Route("api/v1/development/workspaces/{workspaceId:guid}/source-imports")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class WorkspaceSourceDownloadsController(WorkspaceSourceDownloads downloads, TerminalCookieIdentity identity) : ControllerBase
{
    [HttpGet("{importId:guid}/previous")]
    public async Task Previous(Guid workspaceId, Guid importId, CancellationToken token)
    {
        if (Request.Headers.ContainsKey(HeaderNames.Range))
            throw new ArgumentException("WORKSPACE_SOURCE_PREVIOUS_RANGE_UNSUPPORTED");
        var login = await identity.ReadAsync(HttpContext);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        lifetime.CancelAfter(TimeSpan.FromSeconds(125));
        var monitor = WatchLoginAsync();
        try
        {
            try
            {
                await downloads.ExportPreviousAsync(login.Actor, workspaceId, importId,
                    async () => (await identity.ReadAsync(HttpContext, login.Key)).Actor, WriteAsync, lifetime.Token);
            }
            finally
            {
                await lifetime.CancelAsync();
                try { await monitor; } catch (OperationCanceledException) { }
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested && !Response.HasStarted)
        { throw new WorkspaceSourceException(WorkspaceSourceFailure.Retryable); }
        catch
        {
            if (Response.HasStarted) HttpContext.Abort();
            throw;
        }

        async Task WriteAsync(ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
        {
            if (!Response.HasStarted)
            {
                Response.ContentType = "application/x-tar";
                Response.Headers.ContentDisposition = $"attachment; filename=workspace-previous-{importId:N}.tar";
                Response.Headers.CacheControl = "no-store";
                Response.Headers.XContentTypeOptions = "nosniff";
                Response.Headers["X-Accel-Buffering"] = "no";
            }
            using var write = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            write.CancelAfter(TimeSpan.FromSeconds(10));
            await Response.Body.WriteAsync(content, write.Token);
        }

        async Task WatchLoginAsync()
        {
            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(6));
                while (await timer.WaitForNextTickAsync(lifetime.Token)) await identity.ReadAsync(HttpContext, login.Key);
            }
            catch
            {
                await lifetime.CancelAsync();
                throw;
            }
        }
    }
}
