using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Services;
using WhaleDeck.Infrastructure.Agent;
using WhaleDeck.Infrastructure.Catalog;
using WhaleDeck.Infrastructure.Identity;
using WhaleDeck.Infrastructure.Notifications;
using WhaleDeck.Infrastructure.Persistence;
using WhaleDeck.Infrastructure.Secrets;

namespace WhaleDeck.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("WhaleDeck")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:WhaleDeck is required. Supply it through a secret-backed environment variable.");

        services.AddDbContextPool<PlatformDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure(3)));

        var valkeyEndpoint = configuration["Valkey:Endpoint"];
        if (string.IsNullOrWhiteSpace(valkeyEndpoint))
        {
            services.AddDistributedMemoryCache();
            services.AddSingleton<IOneTimeSecretStore, MemoryOneTimeSecretStore>();
        }
        else
        {
            var user = configuration["Valkey:User"];
            var password = configuration["Valkey:Password"];
            services.AddStackExchangeRedisCache(options =>
            {
                options.InstanceName = "whaledeck:";
                options.Configuration = $"{valkeyEndpoint},user={user},password={password},abortConnect=false";
            });
            services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(new ConfigurationOptions
            {
                EndPoints = { valkeyEndpoint },
                User = user,
                Password = password,
                AbortOnConnectFail = false
            }));
            services.AddSingleton<IOneTimeSecretStore, ValkeyOneTimeSecretStore>();
        }

        services.AddHttpClient("Authentik", client =>
        {
            client.BaseAddress = new Uri(configuration["Authentication:Authentik:ApiBaseUrl"] ?? "http://authentik:9000/api/v3/");
            client.Timeout = TimeSpan.FromSeconds(15);
        });
        services.AddHttpClient("Xuanyuan", client =>
        {
            client.BaseAddress = new Uri(configuration["Catalog:Xuanyuan:BaseUrl"] ?? "https://xuanyuan.cloud/");
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("WhaleDeck/0.1 (+internal-workstation)");
        });
        services.AddHttpClient("ServiceHealth", client => client.Timeout = TimeSpan.FromSeconds(5));

        services.AddScoped<IPortalRepository, PortalRepository>();
        services.AddScoped<IJobRepository, JobRepository>();
        services.AddScoped<ITerminalSessionRepository, TerminalSessionRepository>();
        services.AddScoped<IFileUploadRepository, FileUploadRepository>();
        services.AddSingleton<IFilePlanProtector, FilePlanProtector>();
        services.AddSingleton<IFileGateway>(provider => provider.GetRequiredService<AgentGateway>());
        services.AddScoped<FileManagementService>();
        services.AddScoped<FileUploadDispatcher>();
        services.AddSingleton<ITerminalGrantSigner, TerminalGrantSigner>();
        services.AddScoped<TerminalSessionService>();
        services.AddScoped<IGovernanceRepository, GovernanceRepository>();
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<NotificationService>();
        services.AddScoped<INotificationManagementRepository, NotificationManagementRepository>();
        services.AddScoped<NotificationManagementService>();
        services.AddScoped<INotificationPrivateRepository, NotificationPrivateRepository>();
        services.AddSingleton<INotificationPrivateInputStore, NotificationPrivateInputStore>();
        services.AddSingleton<INotificationPrivateProtector, NotificationPrivateProtector>();
        services.AddSingleton<INotificationPrivateValidator, NotificationPrivateValidator>();
        services.AddScoped<NotificationPrivateWorkflow>();
        services.AddScoped<NotificationPrivateDispatcher>();
        services.AddScoped<INotificationDispatchRepository, NotificationDispatchStore>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(new PrivateNotificationConfiguration("/run/secrets/notifications/registry.json"));
        services.AddSingleton(new NotificationNetworkPolicy());
        services.AddSingleton<INotificationTransport, NotificationTransport>();
        services.AddScoped<NotificationSender>();
        services.AddScoped<INotificationRequestAuthorizer, NotificationRequestAuthorizer>();
        services.AddScoped<IApplicationRepository, ApplicationRepository>();
        services.AddScoped<IContainerUpdateRepository, ContainerUpdateRepository>();
        services.AddScoped<IDockerEventRepository, DockerEventRepository>();
        services.AddScoped<IGlobalSearchRepository, GlobalSearchRepository>();
        services.AddScoped<IRoleMappingRepository, RoleMappingRepository>();
        services.AddSingleton<IAdministratorGroupConfiguration, AdministratorGroupConfiguration>();
        services.AddScoped<IMetricsQuery, MetricsQuery>();
        services.AddScoped<ResourceLeaseManager>();
        services.AddScoped<IIdentityDirectory, AuthentikIdentityDirectory>();
        services.AddScoped<IIdentityManager>(provider => provider.GetRequiredService<IIdentityDirectory>() as IIdentityManager
            ?? throw new InvalidOperationException("The identity directory does not support management operations."));
        services.AddScoped<ICatalogProvider, XuanyuanCatalogProvider>();
        services.AddSingleton<AgentGateway>();
        services.AddSingleton<IAgentGateway>(provider => provider.GetRequiredService<AgentGateway>());
        services.AddSingleton<IHostProcessGateway>(provider => provider.GetRequiredService<AgentGateway>());
        services.AddSingleton<IHostTerminalGateway>(provider => provider.GetRequiredService<AgentGateway>());
        services.AddSingleton<IContainerTerminalGateway>(provider => provider.GetRequiredService<AgentGateway>());
        services.AddSingleton<INotificationPrivateGateway>(provider => provider.GetRequiredService<AgentGateway>());
        services.AddSingleton<IManagementQuery>(provider => provider.GetRequiredService<AgentGateway>());
        services.AddScoped<PortalService>();
        services.AddScoped<OverviewService>();
        services.AddScoped<OperationService>();
        services.AddScoped<JobRetryService>();
        services.AddScoped<HostProcessService>();
        services.AddScoped<GovernanceService>();
        services.AddScoped<ApplicationService>();
        services.AddScoped<ApplicationConfigurationService>();
        services.AddScoped<ContainerUpdateService>();
        services.AddScoped<ContainerDiagnosticService>();
        services.AddScoped<DockerEventService>();
        services.AddScoped<GlobalSearchService>();
        services.AddScoped<RoleMappingService>();

        return services;
    }
}
