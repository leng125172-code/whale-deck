using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

public sealed class OperationStore
{
    private readonly ConcurrentDictionary<string, OperationHandle> _operations = new(StringComparer.Ordinal);
    private readonly object _createLock = new();
    private readonly object _writeLock = new();
    private readonly string _directory;
    private readonly string _idempotencyDirectory;
    private readonly string _maintenancePath;

    public OperationStore(IConfiguration configuration)
    {
        _directory = configuration["Agent:OperationDirectory"] ?? "/var/lib/whaledeck-agent/operations";
        _idempotencyDirectory = Path.Combine(_directory, "idempotency");
        _maintenancePath = configuration["Agent:MaintenanceStatusPath"] ?? "/run/whaledeck/maintenance/status.json";
        Directory.CreateDirectory(_directory);
        Directory.CreateDirectory(_idempotencyDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(_maintenancePath)!);
        foreach (var file in Directory.EnumerateFiles(_directory, "*.json"))
        {
            try
            {
                var operation = JsonSerializer.Deserialize<OperationHandle>(ReadSnapshot(file));
                if (operation is not null)
                {
                    var recoverableBackup = Guid.TryParse(operation.OperationId, out _) &&
                        ApplicationBackupManager.ReadRecovery(Path.Combine(_directory, "application-backups", operation.OperationId + ".json")) is not null;
                    if (!recoverableBackup && (operation.State is OperationState.Queued or OperationState.Running or OperationState.WaitingForHealth or OperationState.RollingBack))
                    {
                        operation.State = OperationState.Failed;
                        operation.Phase = "InterruptedByAgentRestart";
                        operation.ErrorCode = "AGENT_OPERATION_INTERRUPTED";
                        operation.ProgressPercent = 0;
                        AtomicWrite(file, JsonSerializer.Serialize(operation));
                    }
                    _operations[operation.OperationId] = operation;
                }
            }
            catch (JsonException)
            {
                // A corrupt operation is ignored here and remains available for diagnostics.
            }
        }
    }

    public OperationHandle Create(string phase, Guid? stableId = null)
    {
        var operation = new OperationHandle
        {
            OperationId = (stableId ?? Guid.NewGuid()).ToString("D"),
            State = OperationState.Queued,
            Phase = phase,
            ProgressPercent = 0
        };
        Save(operation);
        return operation;
    }

    public OperationHandle GetOrCreate(string idempotencyKey, string phase, out bool created, Guid? stableId = null)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            if (stableId is not null) throw new InvalidOperationException("A stable operation requires a job idempotency key.");
            created = true;
            return Create(phase);
        }

        var indexPath = GetIdempotencyPath(idempotencyKey);
        lock (_createLock)
        {
            var existing = ReadIdempotentOperation(indexPath);
            if (existing is not null)
            {
                created = false;
                return existing;
            }

            // Application backup IDs are persisted by Worker before RPC dispatch.
            // Repair the index after a crash without creating another root service.
            if (stableId is { } identity && Get(identity.ToString("D")) is { } recovered)
            {
                AtomicWrite(indexPath, recovered.OperationId);
                created = false;
                return recovered;
            }
            var operation = Create(phase, stableId);
            AtomicWrite(indexPath, operation.OperationId);
            created = true;
            return operation;
        }
    }

    public OperationHandle? GetByIdempotencyKey(string idempotencyKey) =>
        string.IsNullOrWhiteSpace(idempotencyKey) ? null : ReadIdempotentOperation(GetIdempotencyPath(idempotencyKey));

    public OperationHandle? Get(string id) => _operations.GetValueOrDefault(id);

    public OperationHandle GetMaintenance()
    {
        // File.Replace can expose a transient missing name to a Windows opener
        // even though existing shared-delete readers retain their old inode.
        // Serialize this store's existence/open pair with its own publication;
        // never turn that window into a false Ready result or weaken readers.
        lock (_writeLock) return ReadMaintenance();
    }

    private OperationHandle ReadMaintenance()
    {
        if (!File.Exists(_maintenancePath))
        {
            return new OperationHandle { State = OperationState.Succeeded, Phase = "Ready", ProgressPercent = 100 };
        }

        try
        {
            using var document = JsonDocument.Parse(ReadSnapshot(_maintenancePath));
            var root = document.RootElement;
            return new OperationHandle
            {
                OperationId = root.TryGetProperty("operationId", out var id) ? id.GetString() ?? string.Empty : string.Empty,
                Phase = root.TryGetProperty("phase", out var phase) ? phase.GetString() ?? "Unknown" : "Unknown",
                State = root.TryGetProperty("state", out var state) && Enum.TryParse<OperationState>(state.GetString(), true, out var parsed)
                    ? parsed
                    : OperationState.Running
            };
        }
        catch (JsonException)
        {
            return new OperationHandle { State = OperationState.Failed, Phase = "InvalidMaintenanceState", ErrorCode = "AGENT_STATE_INVALID" };
        }
    }

    public void Save(OperationHandle operation, string? maintenanceMessage = null)
    {
        lock (_writeLock)
        {
            _operations[operation.OperationId] = operation;
            AtomicWrite(Path.Combine(_directory, $"{operation.OperationId}.json"), JsonSerializer.Serialize(operation));
            if (maintenanceMessage is not null)
            {
                AtomicWrite(_maintenancePath, JsonSerializer.Serialize(new
                {
                    operationId = operation.OperationId,
                    state = operation.State.ToString(),
                    phase = operation.Phase,
                    message = maintenanceMessage,
                    observedAtUtc = DateTimeOffset.UtcNow
                }));
            }
        }
    }

    private static void AtomicWrite(string path, string content)
    {
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var bytes = Encoding.UTF8.GetBytes(content); stream.Write(bytes); stream.Flush(flushToDisk: true);
            }
            // Replace an existing snapshot instead of MoveFileEx's overwrite
            // path, which can reject a still-open Windows reader even when it
            // shares deletion. Initial publication stays a no-replace rename.
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static string ReadSnapshot(string path)
    {
        // Writers replace directory entries, never mutate a published inode.
        // On Windows a reader must explicitly share DELETE or a concurrent
        // atomic rename can fail (or the reader itself sees a sharing error).
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private string GetIdempotencyPath(string key)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        return Path.Combine(_idempotencyDirectory, $"{hash}.key");
    }

    private OperationHandle? ReadIdempotentOperation(string indexPath)
    {
        if (!File.Exists(indexPath)) return null;
        var operationId = ReadSnapshot(indexPath).Trim();
        return _operations.GetValueOrDefault(operationId);
    }
}
