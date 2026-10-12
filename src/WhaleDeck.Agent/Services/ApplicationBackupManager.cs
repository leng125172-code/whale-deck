using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

public sealed record ApplicationBackupOptions(string Slug, string Consistency, string? BackupId, int CriticalPercent, int RetentionDays = 14, int RetentionCount = 14);
public sealed record ApplicationBackupPreview(ApplicationBackupOptions Options, string Fingerprint, string[] Changes, string Warning);
public sealed record ApplicationBackupRecovery(string OperationId, string Slug, string JobId);

/// <summary>Fixed root helper adapter. Independent services survive Agent restart; no paths or secrets cross RPC.</summary>
public sealed class ApplicationBackupManager(ResourceRegistry registry, BoundedProcessRunner processes,
    OperationStore operations, IConfiguration configuration)
{
    private const string Helper = "/usr/local/libexec/whaledeck-privileged";
    private readonly string _directory = Path.Combine(configuration["Agent:OperationDirectory"] ??
        "/var/lib/whaledeck-agent/operations", "application-backups");

    public async Task<ApplicationBackupPreview> PreviewAsync(RegisteredActionRequest request, CancellationToken cancellationToken)
    {
        registry.Require("whaledeck.apps", request.Action);
        var options = Parse(request);
        var output = request.Action == "cleanup-backups"
            ? await RunHelperAsync(["application-backup-cleanup-plan", options.Slug,
                options.RetentionDays.ToString(CultureInfo.InvariantCulture), options.RetentionCount.ToString(CultureInfo.InvariantCulture)], TimeSpan.FromMinutes(2), cancellationToken)
            : request.Action == "backup"
            ? await RunHelperAsync(["application-backup-plan", options.Slug, options.Consistency,
                options.CriticalPercent.ToString(CultureInfo.InvariantCulture)], TimeSpan.FromMinutes(2), cancellationToken)
            : await RunHelperAsync(["application-backup-verify-plan", options.Slug, options.BackupId!], TimeSpan.FromMinutes(2), cancellationToken);
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        var fingerprint = root.GetProperty("fingerprint").GetString() ?? throw Invalid();
        if (fingerprint.Length != 64 || !fingerprint.All(Uri.IsHexDigit)) throw Invalid();
        var changes = request.Action == "cleanup-backups"
            ? new[] { "Remove expired private archives: " + root.GetProperty("removeBackupIds").GetArrayLength(),
                "Retain at least the newest archive; application runtime data is not touched" }
            : request.Action == "backup"
            ? new[] { "Archive compose.yml, .env, manifest.json and application data", "Named volumes: " +
                string.Join(", ", root.GetProperty("namedVolumes").EnumerateArray().Select(item => item.GetString())),
                "Consistency: " + options.Consistency, "HDD target; private gzip archive; SHA256 verification" }
            : new[] { "Verify existing application archive " + options.BackupId, "No restore or extraction" };
        return new(options, fingerprint, changes, root.GetProperty("warning").GetString() ?? "");
    }

    public async Task<string?> ExecuteAsync(RegisteredActionRequest request, ApplicationBackupPreview approved,
        OperationHandle operation, CancellationToken cancellationToken)
    {
        var current = await PreviewAsync(request, cancellationToken);
        if (current.Fingerprint != approved.Fingerprint || current.Options != approved.Options)
            throw new InvalidOperationException("Application backup source changed. Confirm a fresh plan.");
        if (request.Action == "verify-backup")
            return await RunHelperAsync(["application-backup-verify", approved.Options.Slug,
                approved.Options.BackupId!, approved.Fingerprint], TimeSpan.FromHours(2), cancellationToken);
        if (!Guid.TryParse(request.Context.JobId, out var jobId)) throw Invalid();
        var recovery = new ApplicationBackupRecovery(operation.OperationId, approved.Options.Slug, jobId.ToString("N"));
        SaveRecovery(recovery);
        try
        {
            try
            {
                var arguments = request.Action == "cleanup-backups"
                    ? new[] { "application-backup-cleanup-run", recovery.Slug, recovery.JobId,
                        approved.Options.RetentionDays.ToString(CultureInfo.InvariantCulture), approved.Options.RetentionCount.ToString(CultureInfo.InvariantCulture), approved.Fingerprint }
                    : new[] { "application-backup-run", recovery.Slug, recovery.JobId, approved.Options.Consistency,
                        approved.Fingerprint, approved.Options.CriticalPercent.ToString(CultureInfo.InvariantCulture) };
                await RunHelperAsync(arguments, TimeSpan.FromMinutes(2), CancellationToken.None);
            }
            catch (InvalidOperationException) { /* Inspect the same independent unit; never start a replacement. */ }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
            return await ObserveAsync(recovery, operation, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var completed = await CancelAndWaitAsync(recovery, operation);
            DeleteRecovery(recovery.OperationId);
            if (completed is not null) return completed;
            throw;
        }
    }

    public async Task<string?> ResumeAsync(ApplicationBackupRecovery recovery, OperationHandle operation, CancellationToken cancellationToken)
    {
        try { return await ObserveAsync(recovery, operation, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var completed = await CancelAndWaitAsync(recovery, operation);
            DeleteRecovery(recovery.OperationId);
            if (completed is not null) return completed;
            throw;
        }
    }

    private async Task<string?> ObserveAsync(ApplicationBackupRecovery recovery, OperationHandle operation, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            JsonObject? status = null;
            try { status = JsonNode.Parse(await RunHelperAsync(["application-backup-status", recovery.Slug, recovery.JobId],
                TimeSpan.FromSeconds(30), cancellationToken))?.AsObject(); }
            catch (InvalidOperationException) { /* An observation failure is not a stopped service or permission to restart it. */ }
            catch (JsonException) { }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
            if (status is not null)
            {
                var state = status["state"]?.ToString();
                if (state == "Succeeded")
                {
                    DeleteRecovery(recovery.OperationId);
                    return status["result"]?.ToJsonString() ?? throw Invalid();
                }
                if (state is "Failed" or "Canceled")
                {
                    DeleteRecovery(recovery.OperationId);
                    throw new RecordedOperationFailureException(status["result"]?.ToJsonString() ?? "{}");
                }
                var phase = status["phase"]?.ToString() ?? "Executing";
                if (phase != operation.Phase)
                {
                    operation.Phase = phase;
                    operations.Save(operation);
                }
            }
            await Task.Delay(TimeSpan.FromSeconds(6), cancellationToken);
        }
    }

    private async Task<string?> CancelAndWaitAsync(ApplicationBackupRecovery recovery, OperationHandle operation)
    {
        operation.Phase = "WaitingForApplicationResume";
        operations.Save(operation);
        try { await RunHelperAsync(["application-backup-cancel", recovery.JobId], TimeSpan.FromMinutes(6), CancellationToken.None); }
        catch (InvalidOperationException) { /* The same unit may already be stopped; verify its state instead. */ }
        catch (OperationCanceledException) { }
        while (true)
        {
            try
            {
                using var status = JsonDocument.Parse(await RunHelperAsync(["application-backup-status", recovery.Slug, recovery.JobId],
                    TimeSpan.FromSeconds(30), CancellationToken.None));
                var state = status.RootElement.GetProperty("state").GetString();
                if (state == "Succeeded") return status.RootElement.GetProperty("result").GetRawText();
                if (state is "Failed" or "Canceled")
                {
                    operation.ResultJson = status.RootElement.TryGetProperty("result", out var result) ? result.GetRawText() : "{}";
                    return null;
                }
            }
            catch (InvalidOperationException) { }
            catch (JsonException) { }
            catch (OperationCanceledException) { }
            await Task.Delay(TimeSpan.FromSeconds(6), CancellationToken.None);
        }
    }

    public IReadOnlyCollection<ApplicationBackupRecovery> Recoveries()
    {
        if (!Directory.Exists(_directory)) return [];
        var result = new List<ApplicationBackupRecovery>();
        foreach (var file in Directory.EnumerateFiles(_directory, "*.json").Take(1024))
        {
            var value = ReadRecovery(file);
            if (value is not null) result.Add(value);
        }
        return result;
    }

    internal static ApplicationBackupRecovery? ReadRecovery(string file)
    {
        try
        {
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0 || new FileInfo(file).Length > 4096) return null;
            var value = JsonSerializer.Deserialize<ApplicationBackupRecovery>(File.ReadAllText(file));
            if (value is null || !Guid.TryParse(value.OperationId, out _) || !Guid.TryParseExact(value.JobId, "N", out _) ||
                Path.GetFileName(file) != value.OperationId + ".json") return null;
            _ = ComposeProjectManager.ParseResourceId("application:" + value.Slug);
            return value;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException) { return null; }
    }

    private void SaveRecovery(ApplicationBackupRecovery recovery)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, recovery.OperationId + ".json");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (OperatingSystem.IsLinux()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        try
        {
            using (var stream = new FileStream(temporary, options))
            {
                JsonSerializer.Serialize(stream, recovery);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }

    private void DeleteRecovery(string operationId) => File.Delete(Path.Combine(_directory, operationId + ".json"));

    internal static ApplicationBackupOptions Parse(RegisteredActionRequest request)
    {
        if (request.Resource.ResourceType != "applications" || request.Action is not ("backup" or "verify-backup" or "cleanup-backups")) throw Invalid();
        var allowed = request.Action switch
        {
            "backup" => new[] { "slug", "consistency", "capacityCriticalPercent" },
            "verify-backup" => new[] { "slug", "backupId" },
            _ => new[] { "slug", "retentionDays", "retentionCount" }
        };
        if (request.Parameters.Any(item => !allowed.Contains(item.Key, StringComparer.Ordinal))) throw Invalid();
        var slug = ComposeProjectManager.ParseResourceId(request.Resource.ResourceId);
        if (request.Parameters.TryGetValue("slug", out var provided) && provided != slug) throw Invalid();
        var consistency = request.Parameters.GetValueOrDefault("consistency", "quiesced");
        if (consistency is not ("live" or "quiesced")) throw Invalid();
        var critical = 90;
        if (request.Parameters.TryGetValue("capacityCriticalPercent", out var limit) &&
            (!int.TryParse(limit, NumberStyles.None, CultureInfo.InvariantCulture, out critical) || critical is < 51 or > 99)) throw Invalid();
        var backupId = request.Parameters.GetValueOrDefault("backupId");
        if (request.Action == "verify-backup" && (backupId is null || !Guid.TryParseExact(backupId, "N", out _)) ||
            request.Action == "backup" && backupId is not null) throw Invalid();
        var retentionDays = ParseRetention(request, "retentionDays", 14, 3650);
        var retentionCount = ParseRetention(request, "retentionCount", 14, 365);
        return new(slug, consistency, backupId, critical, retentionDays, retentionCount);
    }

    private static int ParseRetention(RegisteredActionRequest request, string key, int defaultValue, int maximum) =>
        !request.Parameters.TryGetValue(key, out var value) ? defaultValue :
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number >= 1 && number <= maximum ? number : throw Invalid();

    private async Task<string> RunHelperAsync(string[] arguments, TimeSpan timeout, CancellationToken cancellationToken) =>
        (await processes.RunAsync("/usr/bin/sudo", [Helper, .. arguments], null, timeout,
            "APPLICATION_BACKUP_HELPER_FAILED", cancellationToken)).StandardOutput.Trim();

    private static InvalidOperationException Invalid() => new("Application backup request is outside its approved scope.");
}

public sealed class RecordedOperationFailureException(string resultJson) : Exception("Independent operation failed; only approved partial results are recorded.")
{
    public string ResultJson { get; } = resultJson;
}

public sealed class ApplicationBackupRecoveryService(ApplicationBackupManager backups, OperationStore operations,
    OperationCoordinator coordinator) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var recovery in backups.Recoveries())
        {
            var operation = operations.Get(recovery.OperationId);
            if (operation is not null && operation.State is OperationState.Queued or OperationState.Running or OperationState.WaitingForHealth)
                coordinator.Start(operation, "APPLICATION_BACKUP_FAILED", token => backups.ResumeAsync(recovery, operation, token),
                    completionWinsCancellation: true);
        }
        return Task.CompletedTask;
    }
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
