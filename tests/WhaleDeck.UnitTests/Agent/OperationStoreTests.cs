using Microsoft.Extensions.Configuration;
using WhaleDeck.Agent.Services;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

public sealed class OperationStoreTests
{
    [Fact]
    public void StableBackupHandleSurvivesALostIndexAndRequiresIdempotency()
    {
        var root = Path.Combine(Path.GetTempPath(), $"whaledeck-operation-store-{Guid.NewGuid():N}");
        try
        {
            var configuration = BuildConfiguration(root);
            var store = new OperationStore(configuration);
            var jobId = Guid.NewGuid();
            Assert.Throws<InvalidOperationException>(() => store.GetOrCreate("", "ApplicationBackup", out _, jobId));
            var operation = store.GetOrCreate("backup-job", "ApplicationBackup", out var created, jobId);
            Assert.True(created);
            Assert.Equal(jobId.ToString("D"), operation.OperationId);
            operation.State = OperationState.Succeeded;
            store.Save(operation);
            foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "operations", "idempotency"))) File.Delete(file);
            var restarted = new OperationStore(configuration);
            var recovered = restarted.GetOrCreate("backup-job", "ApplicationBackup", out var recreated, jobId);
            Assert.False(recreated);
            Assert.Equal(operation.OperationId, recovered.OperationId);
            Assert.Equal(OperationState.Succeeded, recovered.State);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void ReusesIdempotentOperationAcrossProcessRestart()
    {
        var root = Path.Combine(Path.GetTempPath(), $"whaledeck-operation-store-{Guid.NewGuid():N}");
        try
        {
            var configuration = BuildConfiguration(root);
            var firstStore = new OperationStore(configuration);
            var first = firstStore.GetOrCreate("job:request:resource", "ContainerStart", out var firstCreated);
            first.State = OperationState.Succeeded;
            firstStore.Save(first);

            var repeated = firstStore.GetOrCreate("job:request:resource", "ContainerStart", out var repeatedCreated);
            var restartedStore = new OperationStore(configuration);
            var afterRestart = restartedStore.GetOrCreate("job:request:resource", "ContainerStart", out var restartCreated);

            Assert.True(firstCreated);
            Assert.False(repeatedCreated);
            Assert.False(restartCreated);
            Assert.Equal(first.OperationId, repeated.OperationId);
            Assert.Equal(first.OperationId, afterRestart.OperationId);
            Assert.Equal(OperationState.Succeeded, afterRestart.State);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DifferentIdempotencyKeysCreateDifferentOperations()
    {
        var root = Path.Combine(Path.GetTempPath(), $"whaledeck-operation-store-{Guid.NewGuid():N}");
        try
        {
            var store = new OperationStore(BuildConfiguration(root));
            var first = store.GetOrCreate("job-one", "ContainerStart", out _);
            var second = store.GetOrCreate("job-two", "ContainerStart", out _);

            Assert.NotEqual(first.OperationId, second.OperationId);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MarksAnInterruptedOperationFailedAfterAgentRestart()
    {
        var root = Path.Combine(Path.GetTempPath(), $"whaledeck-operation-store-{Guid.NewGuid():N}");
        try
        {
            var configuration = BuildConfiguration(root);
            var firstStore = new OperationStore(configuration);
            var operation = firstStore.Create("Executing");
            operation.State = OperationState.Running;
            operation.ProgressPercent = 40;
            firstStore.Save(operation);

            var restartedStore = new OperationStore(configuration);
            var recovered = restartedStore.Get(operation.OperationId);

            Assert.NotNull(recovered);
            Assert.Equal(OperationState.Failed, recovered.State);
            Assert.Equal("InterruptedByAgentRestart", recovered.Phase);
            Assert.Equal("AGENT_OPERATION_INTERRUPTED", recovered.ErrorCode);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static IConfiguration BuildConfiguration(string root) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Agent:OperationDirectory"] = Path.Combine(root, "operations"),
            ["Agent:MaintenanceStatusPath"] = Path.Combine(root, "run", "status.json")
        }).Build();
}
