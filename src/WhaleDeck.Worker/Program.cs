using Serilog;
using Serilog.Events;
using Serilog.Formatting.Json;
using WhaleDeck.Infrastructure;
using WhaleDeck.Worker;

if (args.Contains("--health-check", StringComparer.Ordinal))
{
    return WorkerHeartbeat.IsFresh(TimeSpan.FromSeconds(45)) ? 0 : 1;
}

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSerilog((services, configuration) => configuration
    .ReadFrom.Services(services)
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("System.Net.Http.HttpClient.Xuanyuan", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(new JsonFormatter()));
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService<MonitoringAlertWorker>();

var host = builder.Build();
await host.RunAsync();
return 0;
