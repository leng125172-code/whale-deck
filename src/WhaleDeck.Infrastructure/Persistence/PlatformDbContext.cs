using System.Text;
using Microsoft.EntityFrameworkCore;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options) : DbContext(options)
{
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<PlatformSetting> PlatformSettings => Set<PlatformSetting>();
    public DbSet<PortalItem> PortalItems => Set<PortalItem>();
    public DbSet<PublicPortalPreference> PublicPortalPreferences => Set<PublicPortalPreference>();
    public DbSet<RoleMapping> RoleMappings => Set<RoleMapping>();
    public DbSet<ManagedResource> ManagedResources => Set<ManagedResource>();
    public DbSet<ApplicationInstallation> ApplicationInstallations => Set<ApplicationInstallation>();
    public DbSet<ApplicationResource> ApplicationResources => Set<ApplicationResource>();
    public DbSet<ApplicationUpdateRun> ApplicationUpdateRuns => Set<ApplicationUpdateRun>();
    public DbSet<ContainerUpdateRun> ContainerUpdateRuns => Set<ContainerUpdateRun>();
    public DbSet<DockerEventRecord> DockerEvents => Set<DockerEventRecord>();
    public DbSet<OperationJob> OperationJobs => Set<OperationJob>();
    public DbSet<OperationJobEvent> OperationJobEvents => Set<OperationJobEvent>();
    public DbSet<ScheduledTask> ScheduledTasks => Set<ScheduledTask>();
    public DbSet<ScheduledTaskRun> ScheduledTaskRuns => Set<ScheduledTaskRun>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ResourceOperationLease> ResourceOperationLeases => Set<ResourceOperationLease>();
    public DbSet<MetricSeries> MetricSeries => Set<MetricSeries>();
    public DbSet<MetricSample> MetricSamples => Set<MetricSample>();
    public DbSet<MetricRollup> MetricRollups => Set<MetricRollup>();
    public DbSet<MetricAggregationPending> MetricAggregationPending => Set<MetricAggregationPending>();
    public DbSet<MetricAggregationState> MetricAggregationStates => Set<MetricAggregationState>();
    public DbSet<MetricAggregationWindow> MetricAggregationWindows => Set<MetricAggregationWindow>();
    public DbSet<BackupPolicy> BackupPolicies => Set<BackupPolicy>();
    public DbSet<BackupRecord> BackupRecords => Set<BackupRecord>();
    public DbSet<AlertRule> AlertRules => Set<AlertRule>();
    public DbSet<AlertEvent> AlertEvents => Set<AlertEvent>();
    public DbSet<AlertEventHistory> AlertEventHistory => Set<AlertEventHistory>();
    public DbSet<ConfigSnapshot> ConfigSnapshots => Set<ConfigSnapshot>();
    public DbSet<ConfigSyncRun> ConfigSyncRuns => Set<ConfigSyncRun>();
    public DbSet<CatalogSnapshot> CatalogSnapshots => Set<CatalogSnapshot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureAuditAndSettings(modelBuilder);
        ConfigureIdentityAndPortals(modelBuilder);
        ConfigureResourcesAndApplications(modelBuilder);
        ConfigureJobsAndMetrics(modelBuilder);
        ConfigureBackupsAlertsAndConfiguration(modelBuilder);

        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
            }
        }
    }

    private static void ConfigureAuditAndSettings(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditEvent>(entity =>
        {
            entity.ToTable("audit_events");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.ActorSubject).HasMaxLength(256);
            entity.Property(item => item.Action).HasMaxLength(128);
            entity.Property(item => item.TargetType).HasMaxLength(64);
            entity.Property(item => item.TargetId).HasMaxLength(512);
            entity.Property(item => item.Result).HasMaxLength(32);
            entity.Property(item => item.TraceId).HasMaxLength(64);
            entity.Property(item => item.DetailJson).HasColumnType("jsonb");
            entity.HasIndex(item => item.OccurredAtUtc);
            entity.HasIndex(item => new { item.ActorSubject, item.OccurredAtUtc });
            entity.HasIndex(item => new { item.TargetType, item.TargetId, item.OccurredAtUtc });
        });
        modelBuilder.Entity<PlatformSetting>(entity =>
        {
            entity.ToTable("platform_settings");
            entity.HasKey(item => item.Key);
            entity.Property(item => item.Key).HasMaxLength(256);
            entity.Property(item => item.ValueJson).HasColumnType("jsonb");
            entity.Property(item => item.Version).IsConcurrencyToken();
        });
    }

    private static void ConfigureIdentityAndPortals(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PortalItem>(entity =>
        {
            entity.ToTable("portal_items", table => table.HasCheckConstraint("ck_portal_scope_owner", "(scope = 'Personal' AND owner_subject IS NOT NULL) OR (scope = 'Public' AND owner_subject IS NULL)"));
            entity.HasKey(item => item.Id);
            entity.Property(item => item.OwnerSubject).HasMaxLength(256);
            entity.Property(item => item.Name).HasMaxLength(80);
            entity.Property(item => item.Description).HasMaxLength(240);
            entity.Property(item => item.Url).HasMaxLength(2048);
            entity.Property(item => item.IconValue).HasMaxLength(2048);
            entity.Property(item => item.Version).IsConcurrencyToken();
            entity.HasIndex(item => new { item.OwnerSubject, item.IsEnabled, item.SortOrder });
            entity.HasIndex(item => new { item.Scope, item.IsEnabled, item.SortOrder });
        });
        modelBuilder.Entity<PublicPortalPreference>(entity =>
        {
            entity.ToTable("public_portal_preferences");
            entity.HasKey(item => new { item.Subject, item.PortalItemId });
        });
        modelBuilder.Entity<RoleMapping>(entity =>
        {
            entity.ToTable("role_mappings");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => item.AuthentikGroupId).IsUnique();
            entity.Property(item => item.AuthentikGroupId).HasMaxLength(128);
            entity.Property(item => item.AuthentikGroupNameSnapshot).HasMaxLength(150);
            entity.Property(item => item.Role).HasMaxLength(32);
            entity.Property(item => item.Version).IsConcurrencyToken();
        });
    }

    private static void ConfigureResourcesAndApplications(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ManagedResource>(entity =>
        {
            entity.ToTable("managed_resources");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.LabelsJson).HasColumnType("jsonb");
            entity.Property(item => item.Version).IsConcurrencyToken();
            entity.HasIndex(item => new { item.ResourceType, item.ExternalId }).IsUnique();
            entity.HasIndex(item => item.ProtectionLevel);
        });
        modelBuilder.Entity<ApplicationInstallation>(entity =>
        {
            entity.ToTable("application_installations");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.ConfigSummaryJson).HasColumnType("jsonb");
            entity.Property(item => item.Version).IsConcurrencyToken();
            entity.HasIndex(item => item.CatalogAppId).IsUnique();
        });
        modelBuilder.Entity<ApplicationResource>(entity =>
        {
            entity.ToTable("application_resources");
            entity.HasKey(item => new { item.ApplicationId, item.ResourceId, item.Role });
        });
        modelBuilder.Entity<ApplicationUpdateRun>(entity =>
        {
            entity.ToTable("application_update_runs");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new { item.ApplicationId, item.StartedAtUtc });
            entity.HasIndex(item => item.JobId).IsUnique();
        });
        modelBuilder.Entity<ContainerUpdateRun>(entity =>
        {
            entity.ToTable("container_update_runs");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.ExternalContainerId).HasMaxLength(128);
            entity.Property(item => item.ContainerName).HasMaxLength(128);
            entity.Property(item => item.Image).HasMaxLength(512);
            entity.HasIndex(item => new { item.ContainerName, item.StartedAtUtc });
            entity.HasIndex(item => item.JobId).IsUnique();
        });
        modelBuilder.Entity<DockerEventRecord>(entity =>
        {
            entity.ToTable("docker_events");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Fingerprint).HasMaxLength(64);
            entity.Property(item => item.EventType).HasMaxLength(32);
            entity.Property(item => item.Action).HasMaxLength(64);
            entity.Property(item => item.ResourceId).HasMaxLength(128);
            entity.Property(item => item.ResourceName).HasMaxLength(128);
            entity.Property(item => item.Image).HasMaxLength(256);
            entity.Property(item => item.AttributesJson).HasColumnType("jsonb");
            entity.HasIndex(item => item.Fingerprint).IsUnique();
            entity.HasIndex(item => item.OccurredAtUtc);
            entity.HasIndex(item => new { item.ResourceId, item.OccurredAtUtc });
            entity.HasIndex(item => new { item.ResourceName, item.OccurredAtUtc });
        });
    }

    private static void ConfigureJobsAndMetrics(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OperationJob>(entity =>
        {
            entity.ToTable("operation_jobs");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.RequestJson).HasColumnType("jsonb");
            entity.Property(item => item.ResultJson).HasColumnType("jsonb");
            entity.Property(item => item.Version).IsConcurrencyToken();
            entity.HasIndex(item => new { item.ActorSubject, item.IdempotencyKey }).IsUnique();
            entity.HasIndex(item => new { item.State, item.CreatedAtUtc });
            entity.HasIndex(item => new { item.ResourceId, item.CreatedAtUtc });
        });
        modelBuilder.Entity<OperationJobEvent>(entity =>
        {
            entity.ToTable("operation_job_events");
            entity.HasKey(item => new { item.JobId, item.Sequence });
            entity.Property(item => item.DetailJson).HasColumnType("jsonb");
        });
        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("outbox_messages");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.PayloadJson).HasColumnType("jsonb");
            entity.HasIndex(item => new { item.ProcessedAtUtc, item.AvailableAtUtc });
        });
        modelBuilder.Entity<ResourceOperationLease>(entity =>
        {
            entity.ToTable("resource_operation_leases");
            entity.HasKey(item => item.LockKey);
            entity.Property(item => item.LockKey).HasMaxLength(192);
            entity.Property(item => item.OwnerId).HasMaxLength(192);
            entity.Property(item => item.Version).IsConcurrencyToken();
            entity.HasIndex(item => item.JobId).IsUnique();
            entity.HasIndex(item => item.LeaseExpiresAtUtc);
        });
        modelBuilder.Entity<ScheduledTask>(entity =>
        {
            entity.ToTable("scheduled_tasks");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.ParametersJson).HasColumnType("jsonb");
            entity.Property(item => item.Version).IsConcurrencyToken();
        });
        modelBuilder.Entity<ScheduledTaskRun>(entity =>
        {
            entity.ToTable("scheduled_task_runs");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new { item.ScheduleId, item.ScheduledForUtc });
        });
        modelBuilder.Entity<MetricSeries>(entity =>
        {
            entity.ToTable("metric_series");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.DimensionsJson).HasColumnType("jsonb");
            entity.HasIndex(item => new { item.ResourceId, item.MetricKind, item.DimensionsJson }).IsUnique();
        });
        modelBuilder.Entity<MetricSample>(entity =>
        {
            entity.ToTable("metric_samples");
            entity.HasKey(item => new { item.SeriesId, item.SampledAtUtc });
            entity.HasIndex(item => new { item.SeriesId, item.SampledAtUtc });
            entity.HasIndex(item => item.SampledAtUtc).HasMethod("brin").HasDatabaseName("IX_metric_samples_sampled_at_utc_brin");
        });
        modelBuilder.Entity<MetricRollup>(entity =>
        {
            entity.ToTable("metric_rollups");
            entity.HasKey(item => new { item.SeriesId, item.WindowStartUtc, item.Resolution });
        });
        modelBuilder.Entity<MetricAggregationPending>(entity => { entity.ToTable("metric_aggregation_pending"); entity.HasKey(item => item.WindowStartUtc); });
        modelBuilder.Entity<MetricAggregationState>(entity => { entity.ToTable("metric_aggregation_state"); entity.HasKey(item => item.Key); entity.Property(item => item.Key).HasMaxLength(64); });
        modelBuilder.Entity<MetricAggregationWindow>(entity => { entity.ToTable("metric_aggregation_windows"); entity.HasKey(item => item.WindowStartUtc); });
    }

    private static void ConfigureBackupsAlertsAndConfiguration(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BackupPolicy>(entity => { entity.ToTable("backup_policies"); entity.HasKey(item => item.Id); entity.Property(item => item.Version).IsConcurrencyToken(); entity.Property(item => item.Consistency).HasMaxLength(16).HasDefaultValue("quiesced"); });
        modelBuilder.Entity<BackupRecord>(entity => { entity.ToTable("backup_records"); entity.HasKey(item => item.Id); entity.HasIndex(item => new { item.InstanceResourceId, item.StartedAtUtc }); });
        modelBuilder.Entity<AlertRule>(entity =>
        {
            entity.ToTable("alert_rules"); entity.HasKey(item => item.Id);
            entity.Property(item => item.ResourceSelectorJson).HasColumnType("jsonb");
            entity.Property(item => item.ThresholdJson).HasColumnType("jsonb");
            entity.Property(item => item.Version).IsConcurrencyToken();
        });
        modelBuilder.Entity<AlertEvent>(entity =>
        {
            entity.ToTable("alert_events"); entity.HasKey(item => item.Id);
            entity.Property(item => item.DetailJson).HasColumnType("jsonb");
            entity.HasIndex(item => new { item.Fingerprint, item.State });
            entity.HasIndex(item => item.Fingerprint).IsUnique().HasFilter("state <> 'Recovered'");
            entity.Property(item => item.Version).IsConcurrencyToken();
        });
        modelBuilder.Entity<AlertEventHistory>(entity => { entity.ToTable("alert_event_history"); entity.HasKey(item => item.Id); entity.HasIndex(item => new { item.AlertEventId, item.OccurredAtUtc }); });
        modelBuilder.Entity<ConfigSnapshot>(entity =>
        {
            entity.ToTable("config_snapshots"); entity.HasKey(item => item.Id);
            entity.Property(item => item.FileManifestJson).HasColumnType("jsonb");
            entity.Property(item => item.DiffSummaryJson).HasColumnType("jsonb");
        });
        modelBuilder.Entity<ConfigSyncRun>(entity => { entity.ToTable("config_sync_runs"); entity.HasKey(item => item.Id); });
        modelBuilder.Entity<CatalogSnapshot>(entity =>
        {
            entity.ToTable("catalog_snapshots"); entity.HasKey(item => item.Id);
            entity.Property(item => item.PayloadJson).HasColumnType("jsonb");
            entity.HasIndex(item => new { item.Provider, item.FetchedAtUtc });
        });
    }

    private static string ToSnakeCase(string value)
    {
        var result = new StringBuilder(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (char.IsUpper(character) && index > 0) result.Append('_');
            result.Append(char.ToLowerInvariant(character));
        }
        return result.ToString();
    }
}
