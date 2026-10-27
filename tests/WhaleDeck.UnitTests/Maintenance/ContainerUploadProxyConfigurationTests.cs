using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WhaleDeck.MaintenanceHost;
using Yarp.ReverseProxy.Configuration;

namespace WhaleDeck.UnitTests.Maintenance;

public sealed class ContainerUploadProxyConfigurationTests
{
    [Fact]
    public async Task OnlyCanonicalPutContentGetsTheStreamingRouteAndPreservedHost()
    {
        var builder = Builder(); await using var upstream = builder.Build();
        var reached = 0;
        upstream.MapPut("/{**path}",async (Microsoft.AspNetCore.Http.HttpContext context) =>
        {
            Interlocked.Increment(ref reached); await Task.Delay(TimeSpan.FromSeconds(6),context.RequestAborted);
            using var digest=System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
            var bytes=new byte[32768]; int count; long length=0;
            while ((count=await context.Request.Body.ReadAsync(bytes,context.RequestAborted))!=0) { digest.AppendData(bytes.AsSpan(0,count)); length+=count; }
            return new { length,sha256=Convert.ToHexString(digest.GetHashAndReset()),host=context.Request.Host.ToString() };
        });
        await upstream.StartAsync();
        var route=ContainerUploadProxyConfiguration.CreateRoute(); var cluster=ContainerUploadProxyConfiguration.CreateCluster() with
        { Destinations=new Dictionary<string,DestinationConfig> { ["owned"]=new() { Address=Assert.Single(upstream.Urls) } } };
        Assert.Equal(FileProxyConfiguration.MaximumBytes,route.MaxRequestBodySize); Assert.Equal("PUT",Assert.Single(route.Match.Methods!));
        Assert.Equal(TimeSpan.FromSeconds(660),cluster.HttpRequest!.ActivityTimeout); Assert.Equal(-50,route.Order);
        Assert.Equal("true",Assert.Single(route.Transforms!)["RequestHeaderOriginalHost"]);
        var proxyBuilder=Builder(); proxyBuilder.Services.AddReverseProxy().LoadFromMemory([route],[cluster]);
        await using var proxy=proxyBuilder.Build(); proxy.MapReverseProxy(); await proxy.StartAsync();
        using var client=new HttpClient(new SocketsHttpHandler { UseProxy=false }) { BaseAddress=new Uri(Assert.Single(proxy.Urls)),Timeout=TimeSpan.FromSeconds(15) };
        var id="aa0a8865-343e-49bd-b01a-72680131bf39"; var cid=new string('a',64); var path=$"/api/v1/containers/{cid}/files/uploads/{id}/content";
        var data=System.Security.Cryptography.RandomNumberGenerator.GetBytes(2*1024*1024+7);
        using var message=new HttpRequestMessage(HttpMethod.Put,path) { Content=new ByteArrayContent(data) }; message.Headers.Host="owned-origin.invalid:8080";
        using var response=await client.SendAsync(message); Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var json=System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync()); using (json)
        {
            Assert.Equal(data.LongLength,json.RootElement.GetProperty("length").GetInt64());
            Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data)),json.RootElement.GetProperty("sha256").GetString());
            Assert.Equal("owned-origin.invalid:8080",json.RootElement.GetProperty("host").GetString());
        }
        foreach (var other in new[] { path.Replace(cid,new string('A',64),StringComparison.Ordinal),path.Replace(cid,"ordinary",StringComparison.Ordinal),
            path.Replace(id,id.ToUpperInvariant(),StringComparison.Ordinal),path.Replace(id,Guid.Parse(id).ToString("N"),StringComparison.Ordinal),
            path+"/extra",path.Replace(cid,new string('a',63)+"%0A",StringComparison.Ordinal),path.Replace("/content","/cancel",StringComparison.Ordinal),$"/api/v1/containers/{cid}/files/uploads/plan","/api/v1/metrics" })
        { using var rejected=await client.PutAsync(other,null); Assert.Equal(HttpStatusCode.NotFound,rejected.StatusCode); }
        using var get=await client.GetAsync(path); Assert.Equal(HttpStatusCode.MethodNotAllowed,get.StatusCode);
        Assert.Equal(1,reached); Assert.Equal(TimeSpan.FromSeconds(60),FileProxyConfiguration.CreateCluster().HttpRequest!.ActivityTimeout);
    }
    private static WebApplicationBuilder Builder()
    {
        var builder=WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName="Test" });
        builder.Logging.ClearProviders(); builder.WebHost.ConfigureKestrel(options=>options.Listen(IPAddress.Loopback,0)); return builder;
    }
}
