using System.Text;
using Microsoft.EntityFrameworkCore;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed partial class PlatformDbContext(DbContextOptions<PlatformDbContext> options) : DbContext(options)
{
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<TerminalSession> TerminalSessions => Set<TerminalSession>();
    public DbSet<FileUploadOperation> FileUploadOperations => Set<FileUploadOperation>();
    public DbSet<FileMutationOperation> FileMutationOperations => Set<FileMutationOperation>();
    public DbSet<FilePathLock> FilePathLocks => Set<FilePathLock>();
    public DbSet<FileVersion> FileVersions => Set<FileVersion>();
    public DbSet<FileArchiveOperation> FileArchiveOperations => Set<FileArchiveOperation>();
    public DbSet<FileTreeCopyOperation> FileTreeCopyOperations => Set<FileTreeCopyOperation>();
    public DbSet<FileTreePackingOperation> FileTreePackingOperations => Set<FileTreePackingOperation>();
    public DbSet<FileTreeUnpackingOperation> FileTreeUnpackingOperations => Set<FileTreeUnpackingOperation>();
    public DbSet<FileTreeRecyclingOperation> FileTreeRecyclingOperations => Set<FileTreeRecyclingOperation>();
    public DbSet<FileTreeCapture> FileTreeCaptures => Set<FileTreeCapture>();
    public DbSet<FileTreeMoveOperation> FileTreeMoveOperations => Set<FileTreeMoveOperation>();
    public DbSet<FileRecoveryOperation> FileRecoveryOperations => Set<FileRecoveryOperation>();
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
    public DbSet<NotificationEvent> NotificationEvents => Set<NotificationEvent>();
    public DbSet<NotificationReadState> NotificationReadStates => Set<NotificationReadState>();
    public DbSet<NotificationReadSnapshot> NotificationReadSnapshots => Set<NotificationReadSnapshot>();
    public DbSet<NotificationReadSnapshotItem> NotificationReadSnapshotItems => Set<NotificationReadSnapshotItem>();
    public DbSet<NotificationChannel> NotificationChannels => Set<NotificationChannel>();
    public DbSet<NotificationRoute> NotificationRoutes => Set<NotificationRoute>();
    public DbSet<NotificationDispatchReceipt> NotificationDispatchReceipts => Set<NotificationDispatchReceipt>();
    public DbSet<NotificationDelivery> NotificationDeliveries => Set<NotificationDelivery>();
    public DbSet<NotificationDeliveryAttempt> NotificationDeliveryAttempts => Set<NotificationDeliveryAttempt>();
    public DbSet<NotificationTargetRateGate> NotificationTargetRateGates => Set<NotificationTargetRateGate>();
    public DbSet<NotificationPrivateOperation> NotificationPrivateOperations => Set<NotificationPrivateOperation>();

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StageNotifications(); return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StageNotifications(); return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    private void StageNotifications()
    {
        ChangeTracker.DetectChanges();
        if (ChangeTracker.Entries<NotificationEvent>().Any(item => item.State == EntityState.Modified))
            throw new InvalidOperationException("Notification facts are immutable.");
        var staged = ChangeTracker.Entries<NotificationEvent>().Select(item => item.Entity.SourceKey).ToHashSet(StringComparer.Ordinal);
        void Add(NotificationEvent? item) { if (item is not null && staged.Add(item.SourceKey)) NotificationEvents.Add(item); }
        foreach (var history in ChangeTracker.Entries<AlertEventHistory>().Where(item => item.State == EntityState.Added).Select(item => item.Entity).ToArray())
        {
            var source = ChangeTracker.Entries<AlertEvent>().SingleOrDefault(item => item.Entity.Id == history.AlertEventId);
            if (source is null) throw new InvalidOperationException("Notification alert history requires its tracked source event.");
            var wasSilenced = source.OriginalValues.GetValue<string>(nameof(AlertEvent.State)) == "Silenced" &&
                source.OriginalValues.GetValue<DateTimeOffset?>(nameof(AlertEvent.SilencedUntilUtc)) > history.OccurredAtUtc;
            Add(NotificationSources.Alert(source.Entity, history, wasSilenced));
        }
        foreach (var job in ChangeTracker.Entries<OperationJob>().Where(item => item.State == EntityState.Added ||
            item.State == EntityState.Modified && item.Property(nameof(OperationJob.State)).IsModified).ToArray()) Add(NotificationSources.Job(job.Entity));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureFileArchives(modelBuilder);
        ConfigureTreeCopies(modelBuilder);
        ConfigureTreePackings(modelBuilder);
        ConfigureTreeUnpackings(modelBuilder);
        ConfigureTreeRecyclings(modelBuilder);
        ConfigureTreeMoves(modelBuilder);
        ConfigureFileRecovery(modelBuilder);
        ConfigureAuditAndSettings(modelBuilder);
        ConfigureIdentityAndPortals(modelBuilder);
        ConfigureResourcesAndApplications(modelBuilder);
        ConfigureJobsAndMetrics(modelBuilder);
        ConfigureBackupsAlertsAndConfiguration(modelBuilder);
        ConfigureNotifications(modelBuilder);
        ConfigureNotificationDispatch(modelBuilder);

        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
            }
        }
    }

    private static void ConfigureNotificationDispatch(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NotificationChannel>(entity =>
        {
            entity.ToTable("notification_channels", table =>
            {
                table.HasCheckConstraint("ck_notification_channel_provider", "provider IN ('Email','Webhook','DingTalk','WeCom','Feishu')");
                table.HasCheckConstraint("ck_notification_channel_refs", "destination_reference ~ '^[a-z][a-z0-9-]{0,63}$' AND (credential_reference IS NULL OR credential_reference ~ '^[a-z][a-z0-9-]{0,63}$')");
                table.HasCheckConstraint("ck_notification_channel_version", "version >= 0");
            });
            entity.HasKey(item => item.Id); entity.Property(item => item.Name).HasMaxLength(80);
            entity.Property(item => item.Provider).HasMaxLength(16);
            entity.Property(item => item.DestinationReference).HasMaxLength(64);
            entity.Property(item => item.CredentialReference).HasMaxLength(64);
            entity.Property(item => item.Version).IsConcurrencyToken();
        });
        modelBuilder.Entity<NotificationRoute>(entity =>
        {
            entity.ToTable("notification_routes", table =>
            {
                table.HasCheckConstraint("ck_notification_route_kind", "source_kind IN ('all','Alert','Job')");
                table.HasCheckConstraint("ck_notification_route_severity", "minimum_severity IN ('Info','Warning','Error','Critical')");
                table.HasCheckConstraint("ck_notification_route_version", "version >= 0");
            });
            entity.HasKey(item => item.Id); entity.Property(item => item.SourceKind).HasMaxLength(16);
            entity.Property(item => item.MinimumSeverity).HasMaxLength(16); entity.Property(item => item.Version).IsConcurrencyToken();
            entity.HasOne<NotificationChannel>().WithMany().HasForeignKey(item => item.ChannelId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<NotificationDispatchReceipt>(entity =>
        {
            entity.ToTable("notification_dispatch_receipts", table => table.HasCheckConstraint("ck_notification_receipt",
                "outcome IN ('Matched','NoRoute','Personal') AND delivery_count >= 0 AND delivery_count <= 32"));
            entity.HasKey(item => item.NotificationId); entity.Property(item => item.Outcome).HasMaxLength(16);
            entity.HasOne<NotificationEvent>().WithMany().HasForeignKey(item => item.NotificationId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<NotificationDelivery>(entity =>
        {
            entity.ToTable("notification_deliveries", table =>
            {
                table.HasCheckConstraint("ck_notification_delivery_state", "state IN ('Pending','Leased','Sending','RetryWait','Succeeded','Failed','Unknown','Canceled')");
                table.HasCheckConstraint("ck_notification_delivery_configuration", "configuration_revision IS NULL OR configuration_revision ~ '^[A-F0-9]{64}$'");
                table.HasCheckConstraint("ck_notification_delivery_intent", "version >= 0 AND ((intent_kind='Automatic' AND requested_by_subject IS NULL AND origin_delivery_id IS NULL) OR (intent_kind='Test' AND requested_by_subject IS NOT NULL AND length(requested_by_subject)>0 AND origin_delivery_id IS NULL) OR (intent_kind='ManualRetry' AND requested_by_subject IS NOT NULL AND length(requested_by_subject)>0 AND origin_delivery_id IS NOT NULL AND origin_delivery_id<>id))");
                table.HasCheckConstraint("ck_notification_delivery_attempts", "attempt_count >= 0 AND attempt_count <= 5 AND channel_version >= 0");
                table.HasCheckConstraint("ck_notification_delivery_lease", "(state IN ('Leased','Sending') AND lease_token IS NOT NULL AND lease_expires_at_utc IS NOT NULL) OR (state NOT IN ('Leased','Sending') AND lease_token IS NULL AND lease_expires_at_utc IS NULL)");
            });
            entity.HasKey(item => item.Id); entity.Property(item => item.Provider).HasMaxLength(16);
            entity.Property(item => item.DestinationReference).HasMaxLength(64); entity.Property(item => item.CredentialReference).HasMaxLength(64);
            entity.Property(item => item.State).HasMaxLength(16); entity.Property(item => item.LastResultCode).HasMaxLength(32);
            entity.Property(item => item.ConfigurationRevision).HasMaxLength(64);
            entity.Property(item => item.IntentKind).HasMaxLength(16).HasDefaultValue("Automatic");
            entity.Property(item => item.RequestedBySubject).HasMaxLength(256); entity.Property(item => item.Version).IsConcurrencyToken();
            entity.HasIndex(item => new { item.NotificationId, item.ChannelId }).IsUnique().HasFilter("intent_kind <> 'ManualRetry'");
            entity.HasIndex(item => item.OriginDeliveryId).IsUnique().HasFilter("origin_delivery_id IS NOT NULL");
            entity.HasOne<NotificationDelivery>().WithMany().HasForeignKey(item => item.OriginDeliveryId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(item => new { item.State, item.NextAttemptAtUtc }); entity.HasIndex(item => item.LeaseExpiresAtUtc);
            entity.HasIndex(item => item.LeaseToken).IsUnique().HasFilter("lease_token IS NOT NULL");
            entity.HasOne<NotificationEvent>().WithMany().HasForeignKey(item => item.NotificationId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<NotificationChannel>().WithMany().HasForeignKey(item => item.ChannelId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<NotificationDeliveryAttempt>(entity =>
        {
            entity.ToTable("notification_delivery_attempts", table =>
            {
                table.HasCheckConstraint("ck_notification_attempt_number", "attempt_number BETWEEN 1 AND 5");
                table.HasCheckConstraint("ck_notification_attempt_result", "(finished_at_utc IS NULL AND result_code IS NULL) OR (finished_at_utc IS NOT NULL AND result_code IS NOT NULL AND finished_at_utc >= started_at_utc AND result_code IN ('Accepted','RetryableNotSent','Rejected','Unknown','LeaseExpiredAfterSend'))");
            });
            entity.HasKey(item => new { item.DeliveryId, item.AttemptNumber });
            entity.Property(item => item.ResultCode).HasMaxLength(32);
            entity.HasOne<NotificationDelivery>().WithMany().HasForeignKey(item => item.DeliveryId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<NotificationTargetRateGate>(entity =>
        {
            entity.ToTable("notification_target_rate_gates", table => table.HasCheckConstraint("ck_notification_target_identity", "id ~ '^[A-F0-9]{64}$'"));
            entity.HasKey(item => item.Id); entity.Property(item => item.Id).HasMaxLength(64); entity.HasIndex(item => item.NextAtUtc);
        });
    }

    private static void ConfigureNotifications(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NotificationReadSnapshot>(entity =>
        {
            entity.ToTable("notification_read_snapshots", table => table.HasCheckConstraint("ck_notification_read_snapshot_budget", "item_count >= 0"));
            entity.HasKey(item => item.Id); entity.Property(item => item.Subject).HasMaxLength(256);
            entity.Property(item => item.IsCurrent).HasDefaultValue(true);
            entity.HasIndex(item => item.Subject).IsUnique().HasFilter("is_current"); entity.HasIndex(item => item.ExpiresAtUtc);
        });
        modelBuilder.Entity<NotificationReadSnapshotItem>(entity =>
        {
            entity.ToTable("notification_read_snapshot_items"); entity.HasKey(item => new { item.SnapshotId, item.NotificationId });
            entity.HasOne<NotificationReadSnapshot>().WithMany().HasForeignKey(item => item.SnapshotId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<NotificationEvent>().WithMany().HasForeignKey(item => item.NotificationId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(item => new { item.SnapshotId, item.IsProcessed });
        });
        modelBuilder.Entity<NotificationEvent>(entity =>
        {
            entity.ToTable("notification_events", table => table.HasCheckConstraint("ck_notification_audience",
                "(audience = 'Administrator' AND recipient_subject IS NULL) OR (audience = 'Subject' AND recipient_subject IS NOT NULL AND length(recipient_subject) > 0)"));
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Sequence).UseIdentityAlwaysColumn();
            entity.HasIndex(item => item.Sequence).IsUnique();
            entity.Property(item => item.SourceKey).HasMaxLength(128); entity.HasIndex(item => item.SourceKey).IsUnique();
            entity.Property(item => item.SourceKind).HasMaxLength(16); entity.Property(item => item.Audience).HasMaxLength(24);
            entity.Property(item => item.RecipientSubject).HasMaxLength(256);
            entity.Property(item => item.MessageCode).HasMaxLength(64); entity.Property(item => item.Severity).HasMaxLength(16);
            entity.HasIndex(item => new { item.Audience, item.RecipientSubject, item.Sequence });
            entity.HasIndex(item => item.ExpiresAtUtc);
        });
        modelBuilder.Entity<NotificationReadState>(entity =>
        {
            entity.ToTable("notification_read_states"); entity.HasKey(item => new { item.Subject, item.NotificationId });
            entity.Property(item => item.Subject).HasMaxLength(256);
            entity.HasOne<NotificationEvent>().WithMany().HasForeignKey(item => item.NotificationId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureAuditAndSettings(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TerminalSession>(entity =>
        {
            entity.ToTable("terminal_sessions", table =>
            {
                table.HasCheckConstraint("ck_terminal_state", "state IN ('Issued','Connecting','Active','Closed') AND version >= 0");
                table.HasCheckConstraint("ck_terminal_hashes", "login_binding ~ '^[A-F0-9]{64}$' AND grant_hash ~ '^[A-F0-9]{64}$'");
                table.HasCheckConstraint("ck_terminal_target", "(target_kind = 'Host' AND target_id = 'whaledeck.host' AND instance_id IS NULL AND shell IS NULL) OR (target_kind = 'Container' AND target_id ~ '^[a-f0-9]{64}$' AND instance_id IS NOT NULL AND length(instance_id) BETWEEN 1 AND 80 AND shell IS NOT NULL AND shell IN ('sh','bash','ash'))");
                table.HasCheckConstraint("ck_terminal_expiry", "grant_expires_at_utc > created_at_utc AND grant_expires_at_utc <= created_at_utc + INTERVAL '30 seconds' AND expires_at_utc > created_at_utc AND expires_at_utc <= created_at_utc + INTERVAL '10 minutes'");
            });
            entity.HasKey(item => item.Id);
            entity.Property(item => item.ActorSubject).HasMaxLength(256);
            entity.Property(item => item.LoginBinding).HasMaxLength(64);
            entity.Property(item => item.IdempotencyKey).HasMaxLength(36);
            entity.Property(item => item.GrantHash).HasMaxLength(64);
            entity.Property(item => item.TargetId).HasMaxLength(64);
            entity.Property(item => item.TargetKind).HasMaxLength(16).HasDefaultValue("Host");
            entity.Property(item => item.InstanceId).HasMaxLength(80);
            entity.Property(item => item.Shell).HasMaxLength(8);
            entity.Property(item => item.State).HasMaxLength(16);
            entity.Property(item => item.CloseCode).HasMaxLength(32);
            entity.Property(item => item.Version).IsConcurrencyToken();
            entity.HasIndex(item => new { item.ActorSubject, item.LoginBinding, item.IdempotencyKey }).IsUnique();
            entity.HasIndex(item => new { item.State, item.LeaseExpiresAtUtc });
            entity.HasIndex(item => item.CreatedAtUtc);
        });
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
        modelBuilder.Entity<FilePathLock>(entity =>
        {
            entity.ToTable("file_path_locks", table =>
            {
                table.HasCheckConstraint("ck_file_path_hash", "path_hash ~ '^[A-F0-9]{64}$'");
                table.HasCheckConstraint("ck_file_path_scope", "(scope_kind IN ('Legacy','Token') AND root_id IS NULL AND relative_path IS NULL) OR (scope_kind='Path' AND root_id IS NOT NULL AND relative_path IS NOT NULL AND root_id ~ '^[a-z0-9.-]{1,64}$' AND octet_length(relative_path) BETWEEN 1 AND 4096)");
            });
            entity.HasKey(item => item.PathHash); entity.Property(item => item.PathHash).HasMaxLength(64);
            entity.Property(item => item.ScopeKind).HasMaxLength(8).HasDefaultValue("Legacy");
            entity.Property(item => item.RootId).HasMaxLength(64); entity.Property(item => item.RelativePath).HasMaxLength(4096);
            entity.HasIndex(item => item.OperationId);
            entity.HasOne<OperationJob>().WithMany().HasForeignKey(item => item.OperationId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<FileMutationOperation>(entity =>
        {
            entity.ToTable("file_mutation_operations", table =>
            {
                table.HasCheckConstraint("ck_file_mutation_state", "state IN ('Pending','Running','Unknown','Succeeded','Superseded','Canceled','Failed','Acknowledged')");
                table.HasCheckConstraint("ck_file_mutation_limits", "version >= 0 AND expires_at_utc > created_at_utc AND octet_length(plan_json::text) <= 32768");
                table.HasCheckConstraint("ck_file_mutation_hashes", "login_binding ~ '^[A-F0-9]{64}$' AND owner_hash ~ '^[A-F0-9]{64}$' AND request_hash ~ '^[A-F0-9]{64}$'");
                table.HasCheckConstraint("ck_file_mutation_terminal", "(completed_at_utc IS NULL) = (state IN ('Pending','Running','Unknown'))");
            });
            entity.HasKey(item => item.Id); entity.Property(item => item.ActorSubject).HasMaxLength(256);
            entity.Property(item => item.PlanJson).HasColumnType("jsonb");
            entity.Property(item => item.State).HasMaxLength(16); entity.Property(item => item.ResultCode).HasMaxLength(64);
            foreach (var name in new[] { nameof(FileMutationOperation.LoginBinding), nameof(FileMutationOperation.OwnerHash), nameof(FileMutationOperation.RequestHash), nameof(FileMutationOperation.PublishedVersion) }) entity.Property(name).HasMaxLength(64);
            entity.Property(item => item.Version).IsConcurrencyToken();
            entity.HasIndex(item => item.LeaseToken).IsUnique().HasFilter("lease_token IS NOT NULL");
            entity.HasIndex(item => new { item.NextAttemptAtUtc, item.Id }).HasFilter("completed_at_utc IS NULL");
            entity.HasIndex(item => new { item.ActorSubject, item.CreatedAtUtc });
            entity.HasOne<OperationJob>().WithOne().HasForeignKey<FileMutationOperation>(item => item.Id).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<FileUploadOperation>(entity =>
        {
            entity.ToTable("file_upload_operations", table =>
            {
                table.HasCheckConstraint("ck_file_upload_state", "state IN ('AwaitingUpload','Receiving','Ready','Publishing','Unknown','Succeeded','Superseded','Canceled','Failed')");
                table.HasCheckConstraint("ck_file_upload_limits", "size_bytes BETWEEN 0 AND 1073741824 AND version >= 0 AND expires_at_utc > created_at_utc");
                table.HasCheckConstraint("ck_file_upload_hashes", "login_binding ~ '^[A-F0-9]{64}$' AND owner_hash ~ '^[A-F0-9]{64}$' AND target_hash ~ '^[A-F0-9]{64}$' AND request_hash ~ '^[A-F0-9]{64}$' AND sha256 ~ '^[A-F0-9]{64}$' AND (expected_version = 'ABSENT' OR expected_version ~ '^[A-F0-9]{64}$')");
            });
            entity.HasKey(item => item.Id); entity.Property(item => item.ActorSubject).HasMaxLength(256);
            entity.Property(item => item.RootId).HasMaxLength(64); entity.Property(item => item.RelativePath).HasMaxLength(4096);
            entity.Property(item => item.State).HasMaxLength(16); entity.Property(item => item.ResultCode).HasMaxLength(64);
            foreach (var name in new[] { nameof(FileUploadOperation.LoginBinding), nameof(FileUploadOperation.OwnerHash), nameof(FileUploadOperation.TargetHash), nameof(FileUploadOperation.RequestHash), nameof(FileUploadOperation.Sha256), nameof(FileUploadOperation.ExpectedVersion), nameof(FileUploadOperation.PublishedVersion) }) entity.Property(name).HasMaxLength(64);
            entity.Property(item => item.Version).IsConcurrencyToken();
            entity.HasIndex(item => item.TargetHash).IsUnique().HasFilter("completed_at_utc IS NULL");
            entity.HasIndex(item => item.LeaseToken).IsUnique().HasFilter("lease_token IS NOT NULL");
            entity.HasIndex(item => new { item.NextAttemptAtUtc, item.Id }).HasFilter("completed_at_utc IS NULL");
            entity.HasOne<OperationJob>().WithOne().HasForeignKey<FileUploadOperation>(item => item.Id).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<NotificationPrivateOperation>(entity =>
        {
            entity.ToTable("notification_private_operations", table =>
            {
                table.HasCheckConstraint("ck_private_state", "state IN ('Pending','Running','Unknown','Succeeded','Superseded','Canceled','Failed')");
                table.HasCheckConstraint("ck_private_action", "action IN ('apply','remove') AND channel_version >= 0 AND version >= 0");
                table.HasCheckConstraint("ck_private_fingerprints", "request_hash ~ '^[A-F0-9]{64}$' AND input_fingerprint ~ '^[A-F0-9]{64}$'");
            });
            entity.HasKey(item => item.Id); entity.Property(item => item.ActorSubject).HasMaxLength(256); entity.Property(item => item.State).HasMaxLength(16);
            entity.Property(item => item.Action).HasMaxLength(16); entity.Property(item => item.Provider).HasMaxLength(16);
            entity.Property(item => item.RequestHash).HasMaxLength(64); entity.Property(item => item.InputFingerprint).HasMaxLength(64); entity.Property(item => item.RegistryRevision).HasMaxLength(64);
            entity.Property(item => item.ResultCode).HasMaxLength(64); entity.Property(item => item.Version).IsConcurrencyToken();
            entity.HasIndex(item => item.ChannelId).IsUnique().HasFilter("state IN ('Pending','Running','Unknown')");
            entity.HasIndex(item => item.LeaseToken).IsUnique().HasFilter("lease_token IS NOT NULL");
            entity.HasOne<OperationJob>().WithOne().HasForeignKey<NotificationPrivateOperation>(item => item.Id).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<NotificationChannel>().WithMany().HasForeignKey(item => item.ChannelId).OnDelete(DeleteBehavior.Restrict);
        });
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
