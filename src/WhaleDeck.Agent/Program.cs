using Microsoft.AspNetCore.Server.Kestrel.Core;
using Serilog;
using Serilog.Formatting.Json;
using WhaleDeck.Agent.Services;

if (WhaleDeck.Contracts.BuildIdentity.TryPrint(args, typeof(Program).Assembly)) return;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console(new JsonFormatter()));

var socketPath = builder.Configuration["Agent:SocketPath"] ?? "/run/whaledeck/agent.sock";
var socketDirectory = Path.GetDirectoryName(socketPath)
    ?? throw new InvalidOperationException("Agent socket path must have a parent directory.");
Directory.CreateDirectory(socketDirectory);
if (File.Exists(socketPath))
{
    File.Delete(socketPath);
}

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenUnixSocket(socketPath, listen => listen.Protocols = HttpProtocols.Http2);
});

builder.Services.AddGrpc(options =>
{
    options.Interceptors.Add<CapabilityInterceptor>();
    options.Interceptors.Add<AgentErrorInterceptor>();
});
builder.Services.AddSingleton<CapabilityValidator>();
builder.Services.AddSingleton<PeerCredentialPolicy>();
builder.Services.AddSingleton<DockerEngine>();
builder.Services.AddSingleton<IContainerUsageSource>(services => services.GetRequiredService<DockerEngine>());
builder.Services.AddSingleton<ContainerUsageStore>();
builder.Services.AddHostedService<ContainerUsageSampler>();
builder.Services.AddSingleton<DockerEventBuffer>();
builder.Services.AddSingleton<OperationStore>();
builder.Services.AddSingleton<OperationCoordinator>();
builder.Services.AddSingleton<BoundedProcessRunner>();
builder.Services.AddSingleton<HostTerminalRunner>();
builder.Services.AddSingleton<FileTransferRunner>();
builder.Services.AddSingleton<ManagedActionExecutor>();
builder.Services.AddSingleton<PlanStore>();
builder.Services.AddSingleton<ManagedResourcePlanner>();
builder.Services.AddSingleton<ComposeProjectManager>();
builder.Services.AddSingleton<ApplicationConfigurationManager>();
builder.Services.AddSingleton<ApplicationBackupManager>();
builder.Services.AddHostedService<ApplicationBackupRecoveryService>();
builder.Services.AddSingleton<HostReader>();
builder.Services.AddHostedService<HostMetricCollector>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IHostProcessSource, HostProcessSource>();
builder.Services.AddSingleton<HostProcessStore>();
builder.Services.AddHostedService<HostProcessSampler>();
builder.Services.AddSingleton<ResourceRegistry>();
builder.Services.AddHostedService<SocketPermissionService>();
builder.Services.AddHostedService<DockerEventCollector>();

var app = builder.Build();
app.UseMiddleware<PeerCredentialGuard>();
app.MapGrpcService<AgentGrpcService>();
app.MapGrpcService<HostGrpcService>();
app.MapGrpcService<DockerGrpcService>();
app.MapGrpcService<OperationGrpcService>();
app.MapGrpcService<ManagedResourceGrpcService>();
app.MapGrpcService<NotificationConfigurationGrpcService>();
app.MapGrpcService<HostTerminalGrpcService>();
app.MapGrpcService<ContainerTerminalGrpcService>();
app.MapGrpcService<FileTransferGrpcService>();
app.MapGrpcService<FileOperationGrpcService>();
app.MapGrpcService<FileArchiveGrpcService>();
app.MapGrpcService<FileTreeCopyGrpcService>();
app.MapGrpcService<FileTreeMoveGrpcService>();
app.MapGrpcService<FileTreePackingGrpcService>();
app.MapGrpcService<FileTreeUnpackingGrpcService>();
app.MapGrpcService<FileTreeRecyclingGrpcService>();
app.MapGrpcService<FileTreeRestoringGrpcService>();
await app.RunAsync();
