using System.Text.Json;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Agent.Services;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

public sealed class ApplicationBackupTests
{
    private static RegisteredActionRequest Request(string action = "backup") => new()
    {
        Action = action,
        Resource = new ResourceReference { ResourceType = "applications", ResourceId = "application:demo" }
    };

    [Fact]
    public void DefaultsUseQuiescedFixedTargetAndBoundedCapacity()
    {
        var options = ApplicationBackupManager.Parse(Request());
        Assert.Equal(new ApplicationBackupOptions("demo", "quiesced", null, 90), options);
    }

    [Theory]
    [InlineData("source", "/etc")]
    [InlineData("environment", "SYNTHETIC_PRIVATE")]
    [InlineData("consistency", "shell")]
    [InlineData("capacityCriticalPercent", "100")]
    [InlineData("capacityCriticalPercent", "50")]
    [InlineData("capacityCriticalPercent", "90;true")]
    [InlineData("slug", "other")]
    [InlineData("backupId", "11111111111111111111111111111111")]
    public void UnexpectedOrUnsafeParametersAreRejectedWithoutEcho(string key, string value)
    {
        var request = Request();
        request.Parameters[key] = value;
        var exception = Assert.Throws<InvalidOperationException>(() => ApplicationBackupManager.Parse(request));
        Assert.DoesNotContain(value, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("../archive")]
    [InlineData("ffffffff-ffff-ffff-ffff-ffffffffffff")]
    public void VerificationRequiresNormalizedArchiveJobId(string id)
    {
        var request = Request("verify-backup");
        request.Parameters["backupId"] = id;
        Assert.Throws<InvalidOperationException>(() => ApplicationBackupManager.Parse(request));
    }

    [Fact]
    public void VerificationAcceptsOneConcreteArchiveAndDoesNotAcceptAnyPath()
    {
        var request = Request("verify-backup");
        request.Parameters["backupId"] = new string('a', 32);
        Assert.Equal(new string('a', 32), ApplicationBackupManager.Parse(request).BackupId);
    }

    [Theory]
    [InlineData("application:../other")]
    [InlineData("database-platform.postgres")]
    [InlineData("compose:whaledeck")]
    public void ResourceMustBeCanonicalRegisteredApplication(string resource)
    {
        var request = Request();
        request.Resource.ResourceId = resource;
        Assert.Throws<InvalidOperationException>(() => ApplicationBackupManager.Parse(request));
    }

    [Fact]
    public void RetentionUsesOnlyBoundedDaysAndCountForTheSameApplication()
    {
        var request = Request("cleanup-backups");
        request.Parameters["retentionDays"] = "3650";
        request.Parameters["retentionCount"] = "365";
        var options = ApplicationBackupManager.Parse(request);
        Assert.Equal("demo", options.Slug);
        Assert.Equal(3650, options.RetentionDays);
        Assert.Equal(365, options.RetentionCount);
    }

    [Theory]
    [InlineData("retentionDays", "0")]
    [InlineData("retentionDays", "3651")]
    [InlineData("retentionDays", "14;true")]
    [InlineData("retentionCount", "0")]
    [InlineData("retentionCount", "366")]
    [InlineData("source", "/etc")]
    [InlineData("backupId", "11111111111111111111111111111111")]
    public void RetentionRejectsUnapprovedParametersWithoutEcho(string key, string value)
    {
        var request = Request("cleanup-backups");
        request.Parameters[key] = value;
        var exception = Assert.Throws<InvalidOperationException>(() => ApplicationBackupManager.Parse(request));
        Assert.DoesNotContain(value, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AgentRestartRetainsOnlyValidBackupRecovery(bool corrupted)
    {
        var root = Path.Combine(Path.GetTempPath(), "whaledeck-backup-recovery-" + Guid.NewGuid().ToString("N"));
        try
        {
            var directory = Path.Combine(root, "operations");
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Agent:OperationDirectory"] = directory, ["Agent:MaintenanceStatusPath"] = Path.Combine(root, "status.json") }).Build();
            var store = new OperationStore(configuration);
            var operation = store.Create("ApplicationBackup:backup");
            operation.State = OperationState.Running;
            operation.Phase = "Executing";
            store.Save(operation);
            var backups = Path.Combine(directory, "application-backups");
            Directory.CreateDirectory(backups);
            File.WriteAllText(Path.Combine(backups, operation.OperationId + ".json"), corrupted ? "invalid-json" :
                JsonSerializer.Serialize(new ApplicationBackupRecovery(operation.OperationId, "demo", new string('a', 32))));
            var recovered = new OperationStore(configuration).Get(operation.OperationId)!;
            Assert.Equal(corrupted ? OperationState.Failed : OperationState.Running, recovered.State);
            if (corrupted) Assert.Equal("AGENT_OPERATION_INTERRUPTED", recovered.ErrorCode);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
}
