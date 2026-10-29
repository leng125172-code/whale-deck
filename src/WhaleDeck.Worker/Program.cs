using Serilog;
using Serilog.Events;
using Serilog.Formatting.Json;
using WhaleDeck.Infrastructure;
using WhaleDeck.Worker;

if (WhaleDeck.Contracts.BuildIdentity.TryPrint(args, typeof(Program).Assembly)) return 0;

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
builder.Services.AddHostedService<HostMetricIngestionWorker>();
builder.Services.AddHostedService<MonitoringAlertWorker>();
builder.Services.AddHostedService<NotificationRetentionWorker>();
builder.Services.AddHostedService<NotificationSenderWorker>();
builder.Services.AddHostedService<NotificationPrivateWorker>();
builder.Services.AddHostedService<FileUploadWorker>();
builder.Services.AddHostedService<ContainerFileUploadWorker>();
builder.Services.AddHostedService<ContainerDirectoryWorker>();
builder.Services.AddHostedService<ContainerCopyWorker>();
builder.Services.AddHostedService<ContainerMoveWorker>();
builder.Services.AddHostedService<FileMutationWorker>();
builder.Services.AddHostedService<FileArchiveWorker>();
builder.Services.AddHostedService<FileTreeCopyWorker>();
builder.Services.AddHostedService<FileTreePackingWorker>();
builder.Services.AddHostedService<FileTreeUnpackingWorker>();
builder.Services.AddHostedService<FileTreeRestoringWorker>();
builder.Services.AddHostedService<FileTreeOverwriteRestoringWorker>();
builder.Services.AddHostedService<FileTreePurgingWorker>();
builder.Services.AddHostedService<FileTreeRecyclingWorker>();
builder.Services.AddHostedService<FileTreeMoveWorker>();
builder.Services.AddHostedService<FileRecoveryWorker>();

var host = builder.Build();
await host.RunAsync();
return 0;
