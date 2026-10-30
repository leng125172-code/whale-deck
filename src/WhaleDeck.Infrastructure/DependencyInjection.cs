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
            services.AddSingleton<IBffSessionStorage, MemoryBffSessionStorage>();
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
            services.AddSingleton<IBffSessionStorage, ValkeyBffSessionStorage>();
        }
        services.AddSingleton<ILoginSessionRegistry>(provider => provider.GetRequiredService<IBffSessionStorage>());

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
        services.AddSingleton<IContainerFileUploadProtector, FilePlanProtector>();
        services.AddSingleton<IContainerFileRestoreProtector, FilePlanProtector>();
        services.AddScoped<IContainerFileHistoryRepository, ContainerFileHistoryRepository>();
        services.AddScoped<ContainerFileHistoryService>();
        services.AddScoped<ContainerFileRestoreService>();
        services.AddSingleton<IFileGateway>(provider => provider.GetRequiredService<AgentGateway>());
        services.AddScoped<FileManagementService>();
        services.AddScoped<ContainerFileReadService>();
        services.AddScoped<IContainerFileUploadRepository, ContainerFileUploadRepository>();
        services.AddScoped<ContainerFileUploadService>();
        services.AddScoped<ContainerFileUploadDispatcher>();
        services.AddSingleton<IContainerDirectoryProtector, FilePlanProtector>();
        services.AddScoped<IContainerDirectoryRepository, ContainerDirectoryRepository>();
        services.AddScoped<ContainerDirectoryWorkflowService>();
        services.AddScoped<ContainerDirectoryDispatcher>();
        services.AddSingleton<IContainerCopyProtector, FilePlanProtector>();
        services.AddScoped<IContainerCopyRepository, ContainerCopyRepository>();
        services.AddScoped<ContainerCopyWorkflowService>();
        services.AddScoped<ContainerCopyDispatcher>();
        services.AddSingleton<IContainerPermissionProtector, FilePlanProtector>();
        services.AddScoped<IContainerPermissionRepository, ContainerPermissionRepository>();
        services.AddScoped<ContainerPermissionWorkflowService>();
        services.AddScoped<ContainerPermissionDispatcher>();
        services.AddSingleton<IContainerMoveProtector, FilePlanProtector>();
        services.AddScoped<IContainerMoveRepository, ContainerMoveRepository>();
        services.AddScoped<ContainerMoveWorkflowService>();
        services.AddScoped<ContainerMoveDispatcher>();
        services.AddScoped<IContainerFileReadAudit, ContainerFileReadAudit>();
        services.AddSingleton<IFileBrowserGateway>(provider => provider.GetRequiredService<AgentGateway>());
        services.AddScoped<FileBrowserService>();
        services.AddScoped<FileTextService>();
        services.AddScoped<FileUploadDispatcher>();
        services.AddScoped<IFileMutationRepository, FileMutationRepository>();
        services.AddSingleton<IFileMutationProtector, FilePlanProtector>();
        services.AddSingleton<IFileMutationGateway>(provider => provider.GetRequiredService<AgentGateway>());
        services.AddSingleton<IFileArchiveProtector, FilePlanProtector>();
        services.AddSingleton<IFileArchiveGateway>(provider => provider.GetRequiredService<AgentGateway>());
        services.AddScoped<FileArchiveRepository>();
        services.AddScoped<IFileArchiveRepository>(provider => provider.GetRequiredService<FileArchiveRepository>());
        services.AddScoped<FileArchiveService>();
        services.AddScoped<FileArchiveDispatcher>();
        services.AddSingleton<IFileTreeCopyProtector, FilePlanProtector>();
        services.AddSingleton<IFileTreeCopyGateway>(provider => provider.GetRequiredService<AgentGateway>());
        services.AddScoped<IFileTreeCopyRepository, FileTreeCopyRepository>();
        services.AddScoped<FileTreeCopyService>();
        services.AddScoped<FileTreeCopyDispatcher>();
        services.AddSingleton<IFileTreePackingProtector, FilePlanProtector>();
        services.AddSingleton<IFileTreePackingGateway>(provider => provider.GetRequiredService<AgentGateway>());
        services.AddScoped<IFileTreePackingRepository, FileTreePackingRepository>();
        services.AddScoped<FileTreePackingService>();
        services.AddScoped<FileTreePackingDispatcher>();
        services.AddSingleton<IFileTreeUnpackingProtector, FilePlanProtector>();
        services.AddSingleton<IFileTreeUnpackingGateway>(provider => provider.GetRequiredService<AgentGateway>());
        services.AddScoped<IFileTreeUnpackingRepository, FileTreeUnpackingRepository>();
        services.AddScoped<FileTreeUnpackingService>();
        services.AddScoped<FileTreeUnpackingDispatcher>();
        services.AddSingleton<IFileTreeRestoringProtector, FilePlanProtector>();
        services.AddSingleton<IFileTreeOverwriteRestoringProtector, FilePlanProtector>();
        services.AddSingleton<IFileTreeOverwriteRestoringGateway>(provider => provider.GetRequiredService<AgentGateway>());
        services.AddSingleton<IFileTreeRestoringGateway>(provider => provider.GetRequiredService<AgentGateway>());
        services.AddScoped<IFileTreeRestoringRepository, FileTreeRestoringRepository>();
        services.AddScoped<FileTreeRestoringService>();
        services.AddScoped<FileTreeRestoringDispatcher>();
        services.AddScoped<FileTreeOverwriteRestoringRepository>();
        services.AddScoped<IFileTreeOverwriteRestoringRepository>(provider => provider.GetRequiredService<FileTreeOverwriteRestoringRepository>());
        services.AddScoped<FileTreeOverwriteRestoringService>();
        services.AddScoped<FileTreeOverwriteRestoringDispatcher>();
        services.AddSingleton<IFileTreeRecyclingProtector, FilePlanProtector>();
        services.AddSingleton<IFileTreePurgingProtector, FilePlanProtector>();
        services.AddSingleton<IFileTreePurgingGateway>(provider => provider.GetRequiredService<AgentGateway>());
        services.AddScoped<FileTreePurgingRepository>();
        services.AddScoped<IFileTreePurgingRepository>(provider => provider.GetRequiredService<FileTreePurgingRepository>());
        services.AddScoped<FileTreePurgingService>();
        services.AddScoped<FileTreePurgingDispatcher>();
        services.AddSingleton<IFileTreeRecyclingGateway>(provider => provider.GetRequiredService<AgentGateway>());
        services.AddScoped<FileTreeRecyclingRepository>();
        services.AddScoped<IFileTreeRecyclingRepository>(provider => provider.GetRequiredService<FileTreeRecyclingRepository>());
        services.AddScoped<FileTreeRecyclingService>();
        services.AddScoped<FileTreeRecyclingDispatcher>();
        services.AddSingleton<IFileTreeMoveProtector, FilePlanProtector>();
        services.AddSingleton<IFileTreeMoveGateway>(provider => provider.GetRequiredService<AgentGateway>());
        services.AddScoped<IFileTreeMoveRepository, FileTreeMoveRepository>();
        services.AddScoped<FileTreeMoveService>();
        services.AddScoped<FileTreeMoveDispatcher>();
        services.AddSingleton<IFileRecoveryProtector, FilePlanProtector>();
        services.AddScoped<IFileRecoveryInventory, FileRecoveryInventory>();
        services.AddScoped<FileRecoveryInventoryService>();
        services.AddSingleton<FileRecoveryMaterial>();
        services.AddScoped<FileRecoverySources>();
        services.AddScoped<IFileRecoveryRepository, FileRecoveryRepository>();
        services.AddSingleton<IFileRecoveryGateway>(provider => provider.GetRequiredService<AgentGateway>());
        services.AddScoped<FileRecoveryService>();
        services.AddScoped<FileRecoveryDispatcher>();
        services.AddScoped<FileMutationService>();
        services.AddScoped<FileMutationDispatcher>();
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
        services.AddSingleton<IContainerFileGateway>(provider => provider.GetRequiredService<AgentGateway>());
        services.AddSingleton<IContainerFileWriteGateway>(provider => provider.GetRequiredService<AgentGateway>());
        services.AddSingleton<IContainerFileHistoryGateway>(provider => provider.GetRequiredService<AgentGateway>());
        services.AddSingleton<IContainerDirectoryGateway>(provider => provider.GetRequiredService<AgentGateway>());
        services.AddSingleton<IContainerPermissionGateway>(provider => provider.GetRequiredService<AgentGateway>());
        services.AddSingleton<IContainerOwnershipGateway>(provider => provider.GetRequiredService<AgentGateway>());
        services.AddSingleton<IContainerCopyGateway>(provider => provider.GetRequiredService<AgentGateway>());
        services.AddSingleton<IContainerMoveGateway>(provider => provider.GetRequiredService<AgentGateway>());
        services.AddSingleton<IContainerUploadRecoveryGateway>(provider => provider.GetRequiredService<AgentGateway>());
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
