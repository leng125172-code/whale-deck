using System.Net;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace WhaleDeck.MaintenanceHost;

/// <summary>Translate a gateway HTML failure before YARP starts forwarding its body.</summary>
public static class MaintenanceUnavailableResponse
{
    public static void Configure(TransformBuilderContext context, IConfiguration configuration)
    {
        if (context.Route.ClusterId is not ("gateway" or "authentik")) return;
        var statusPath = configuration["Maintenance:StatusPath"] ?? "/run/whaledeck/maintenance/status.json";
        context.AddResponseTransform(async transform =>
        {
            var upstream = transform.ProxyResponse;
            // Application problem JSON and dedicated streaming routes retain
            // their existing semantics. Never retry a forwarded request.
            if (upstream?.StatusCode is not (HttpStatusCode.BadGateway or HttpStatusCode.GatewayTimeout) ||
                !string.Equals(upstream.Content.Headers.ContentType?.MediaType, "text/html", StringComparison.OrdinalIgnoreCase)) return;
            transform.SuppressResponseBody = true;
            var response = transform.HttpContext.Response;
            response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            response.ContentLength = null;
            response.Headers.Remove("Content-Encoding");
            response.Headers.Remove("Content-Range");
            response.Headers.Remove("ETag");
            response.Headers.Remove("Last-Modified");
            response.ContentType = "text/html; charset=utf-8";
            response.Headers.CacheControl = "no-store";
            if (!HttpMethods.IsHead(transform.HttpContext.Request.Method))
            {
                var status = await transform.HttpContext.RequestServices.GetRequiredService<MaintenanceStatusReader>()
                    .ReadAsync(statusPath, transform.HttpContext.RequestAborted);
                await response.WriteAsync(Page(status.Body.Message), transform.HttpContext.RequestAborted);
            }
        });
    }

    public static string Page(string status) => $$"""
        <!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <meta http-equiv="refresh" content="6"><title>Whale Deck 维护中</title>
        <style>body{margin:0;min-height:100vh;display:grid;place-items:center;background:#0f172a;color:#dbeafe;font:14px ui-monospace,monospace}.card{padding:32px;border:1px solid #1d4ed8;border-radius:16px;background:#111827;box-shadow:0 20px 60px #02061780}h1{font-size:20px;color:#60a5fa}</style></head>
        <body><main class="card"><h1>Whale Deck</h1><p>{{WebUtility.HtmlEncode(status)}}</p><p>页面将在 6 秒后自动重试。</p></main></body></html>
        """;
}
