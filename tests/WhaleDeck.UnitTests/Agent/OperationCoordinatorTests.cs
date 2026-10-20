using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using WhaleDeck.Agent.Services;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

public sealed class OperationCoordinatorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfirmedArchiveCompletionCanWinALateCancellationWithoutChangingOtherOperations(bool completionWins)
    {
        var root = Path.Combine(Path.GetTempPath(), "whaledeck-coordinator-" + Guid.NewGuid().ToString("N"));
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Agent:OperationDirectory"] = Path.Combine(root, "operations"), ["Agent:MaintenanceStatusPath"] = Path.Combine(root, "status.json") }).Build();
            var store = new OperationStore(configuration);
            var coordinator = new OperationCoordinator(store, NullLogger<OperationCoordinator>.Instance);
            var operation = store.Create("Executing");
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var completed = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            coordinator.Start(operation, "TEST_FAILED", _ => { entered.SetResult(); return completed.Task; },
                completionWinsCancellation: completionWins);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(coordinator.RequestCancellation(operation.OperationId));
            completed.SetResult("{\"archiveCompleted\":true}");
            var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
            while (operation.State is OperationState.Queued or OperationState.Running && DateTimeOffset.UtcNow < deadline)
                await Task.Delay(10);
            Assert.Equal(completionWins ? OperationState.Succeeded : OperationState.Canceled, operation.State);
            var persisted = Path.Combine(root, "operations", operation.OperationId + ".json");
            var expected = completionWins ? OperationState.Succeeded : OperationState.Canceled;
            while (ReadPersisted(persisted).State != expected && DateTimeOffset.UtcNow < deadline)
                await Task.Delay(10);
            Assert.Equal(expected, ReadPersisted(persisted).State);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
    private static OperationHandle ReadPersisted(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return JsonSerializer.Deserialize<OperationHandle>(stream)!;
    }
}
