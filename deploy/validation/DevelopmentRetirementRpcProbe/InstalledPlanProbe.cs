using System.Globalization;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

// Explicit trusted QA only. Fixed installed socket/key; plan/observe/cancel
// only. Never captures HDD bytes, deletes source, releases quota or starts a
// substitute server. Business identity/authorization is outside this probe.
internal static class InstalledPlanProbe
{
    internal static async Task<int> RunAsync()
    {
        byte[]? key = null;
        string? taskId = null;
        var cleanup = new List<object>();
        try
        {
            if (!OperatingSystem.IsLinux() || Environment.GetEnvironmentVariable("WHALEDECK_INSTALLED_RETIREMENT_PLAN_PROBE") != "1")
                throw new InvalidOperationException("INSTALLED_RETIREMENT_PROBE_DISABLED");
            var uid = (await File.ReadAllLinesAsync("/proc/self/status")).Single(x => x.StartsWith("Uid:", StringComparison.Ordinal));
            if (uid.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Skip(1).Any(x => x != "0"))
                throw new InvalidOperationException("INSTALLED_RETIREMENT_PROBE_ROOT_REQUIRED");
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            var input = new char[4097];
            var length = 0;
            while (length < input.Length)
            {
                var count = await Console.In.ReadAsync(input.AsMemory(length), budget.Token);
                if (count == 0) break;
                length += count;
            }
            if (length is 0 or > 4096) throw new InvalidDataException();
            using var document = JsonDocument.Parse(new string(input, 0, length), new JsonDocumentOptions { MaxDepth = 4 });
            var value = document.RootElement;
            RequireKeys(value, ["taskId", "retirementId", "archiveId", "storage", "expectedEpoch", "archivePlanSha256", "captureReceiptSha256", "expectedStatus"]);
            var storage = value.GetProperty("storage");
            RequireKeys(storage, ["workspaceId", "rootId", "environmentSha256", "diskMiB"]);
            var expected = value.GetProperty("expectedStatus").GetString();
            if (expected is not ("Succeeded" or "Rejected")) throw new InvalidDataException();
            var request = new WorkspaceRetirementRequest
            {
                TaskId = value.GetProperty("taskId").GetString(), RetirementId = value.GetProperty("retirementId").GetString(), ArchiveId = value.GetProperty("archiveId").GetString(),
                ExpectedEpoch = value.GetProperty("expectedEpoch").GetUInt64(), Phase = "plan",
                ArchivePlanSha256 = value.GetProperty("archivePlanSha256").GetString(), CaptureReceiptSha256 = value.GetProperty("captureReceiptSha256").GetString(),
                Storage = new() { WorkspaceId = storage.GetProperty("workspaceId").GetString(),
                    StorageRootId = storage.GetProperty("rootId").GetString(),
                    EnvironmentSha256 = storage.GetProperty("environmentSha256").GetString(),
                    DiskMib = storage.GetProperty("diskMiB").GetUInt32(),
                    ActorSubject = "installed-retirement-plan-acceptance", TraceId = Guid.NewGuid().ToString("D") }
            };
            if (!DevelopmentRetirementBinding.IsValid(request) ||
                request.Storage.WorkspaceId != "c23526c5-fc43-4944-9f27-75ac540f6df0" ||
                request.Storage.StorageRootId != "nvme-main" || request.Storage.DiskMib != 8192) throw new InvalidDataException();
            taskId = request.TaskId;
            const string keyFile = "/etc/whaledeck/agent-capability.key";
            if (new FileInfo(keyFile).LinkTarget is not null || new FileInfo(keyFile).Length is < 32 or > 4096)
                throw new InvalidDataException();
            key = await File.ReadAllBytesAsync(keyFile, budget.Token);
            using var channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions { HttpHandler = new SocketsHttpHandler
            {
                UseProxy = false, ConnectCallback = async (_, token) =>
                {
                    var connection = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    try
                    {
                        await connection.ConnectAsync(new UnixDomainSocketEndPoint("/run/whaledeck/agent.sock"), token);
                        return new NetworkStream(connection, ownsSocket: true);
                    }
                    catch { connection.Dispose(); throw; }
                }
            }, MaxReceiveMessageSize = DevelopmentRetirementBinding.MaximumReceiptBytes });
            var capabilities = await new AgentService.AgentServiceClient(channel).GetCapabilitiesAsync(new Empty(),
                deadline: DateTime.UtcNow.AddSeconds(10), cancellationToken: budget.Token);
            if (!DevelopmentRetirementBinding.IsSupported(capabilities)) throw new InvalidDataException();
            var client = new DevelopmentRetirementService.DevelopmentRetirementServiceClient(channel);
            var cleanupRequests = new List<WorkspaceRetirementRequest> { request };
            Metadata Sign(string method, WorkspaceRetirementRequest body, bool legacy = false, bool archiveDomain = false)
            {
                var expires = DateTimeOffset.UtcNow.AddSeconds(45).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
                var nonce = Guid.NewGuid().ToString("N");
                var payload = legacy ? $"{expires}.{nonce}.{method}" : archiveDomain
                    ? DevelopmentArchiveBinding.Payload(expires, nonce, method, DevelopmentRetirementBinding.Hash(body))
                    : DevelopmentRetirementBinding.Payload(expires, nonce, method, DevelopmentRetirementBinding.Hash(body));
                return new() { { "x-whaledeck-capability", $"{expires}.{nonce}.{Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload)))}" } };
            }
            async Task Denied(WorkspaceRetirementRequest body, Metadata headers)
            {
                try { await client.StartAsync(body, headers, DateTime.UtcNow.AddSeconds(10), budget.Token); }
                catch (RpcException error) when (error.StatusCode == StatusCode.PermissionDenied) { return; }
                throw new InvalidDataException("INSTALLED_RETIREMENT_UNAUTHORIZED_ACCEPTED");
            }
            try
            {
            await Denied(request, new());
            await Denied(request, Sign(DevelopmentRetirementBinding.Start, request, legacy: true));
            await Denied(request, Sign(DevelopmentRetirementBinding.Start, request, archiveDomain: true));
            await Denied(request, Sign(DevelopmentRetirementBinding.Cancel, request));
            var duplicate = Sign(DevelopmentRetirementBinding.Start, request);
            duplicate.Add("x-whaledeck-capability", duplicate[0].Value);
            await Denied(request, duplicate);
            var changed = request.Clone(); changed.Storage.ActorSubject += "-tampered";
            await Denied(changed, Sign(DevelopmentRetirementBinding.Start, request));
            var headers = Sign(DevelopmentRetirementBinding.Start, request);
            var result = await client.StartAsync(request, headers, DateTime.UtcNow.AddSeconds(25), budget.Token);
            await Denied(request, headers);
            var observations = 0;
            while (result.Status is "Pending" or "Running" or "Finishing")
            {
                if (!DevelopmentRetirementBinding.Matches(request, result)) throw new InvalidDataException();
                await Task.Delay(500, budget.Token);
                result = await client.ObserveAsync(request, Sign(DevelopmentRetirementBinding.Observe, request),
                    DateTime.UtcNow.AddSeconds(25), budget.Token);
                observations++;
            }
            if (!DevelopmentRetirementBinding.Matches(request, result) || result.Status != expected ||
                (expected == "Rejected" && result.Code != "WORKSPACE_STORAGE_ARCHIVE_NOT_STOPPED")) throw new InvalidDataException();
            var replay = await client.StartAsync(request, Sign(DevelopmentRetirementBinding.Start, request),
                DateTime.UtcNow.AddSeconds(25), budget.Token);
            if (!replay.Equals(result)) throw new InvalidDataException();
            var cancelled = await client.CancelAsync(request, Sign(DevelopmentRetirementBinding.Cancel, request),
                DateTime.UtcNow.AddSeconds(25), budget.Token);
            if (!cancelled.Equals(result)) throw new InvalidDataException();
            var preCancelled = request.Clone(); preCancelled.TaskId = Guid.NewGuid().ToString("D");
            cleanupRequests.Add(preCancelled);
            var stopped = await client.CancelAsync(preCancelled, Sign(DevelopmentRetirementBinding.Cancel, preCancelled),
                DateTime.UtcNow.AddSeconds(25), budget.Token);
            var lateStart = await client.StartAsync(preCancelled, Sign(DevelopmentRetirementBinding.Start, preCancelled),
                DateTime.UtcNow.AddSeconds(25), budget.Token);
            var observed = await client.ObserveAsync(preCancelled, Sign(DevelopmentRetirementBinding.Observe, preCancelled),
                DateTime.UtcNow.AddSeconds(25), budget.Token);
            if (!DevelopmentRetirementBinding.Matches(preCancelled, stopped) || stopped.Status != "Stopped" ||
                !stopped.Equals(lateStart) || !stopped.Equals(observed)) throw new InvalidDataException();
            using var payload = result.ResultJson.IsEmpty ? null : JsonDocument.Parse(result.ResultJson.Memory);
            Console.WriteLine(JsonSerializer.Serialize(new { passed = true, installedAgent = true,
                protocolMajor = capabilities.ProtocolMajor, protocolMinor = capabilities.ProtocolMinor,
                taskId, preCancelledTaskId = preCancelled.TaskId, requestSha256 = result.RequestSha256,
                signatureRefusals = 7, observations, result.Status, result.Code, result = payload?.RootElement,
                duplicateStartPreserved = true, lateCancelPreserved = true, preCancelledStartStopped = true,
                scope = "Installed trusted QA retirement plan RPC; no source deletion, quota/budget release or user retirement workflow" }));
            return 0;
            }
            catch
            {
                // A lost Start reply may already own a real execution. Use the
                // original request/ID and a fresh signature, never a new task.
                using var cleanupBudget = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                foreach (var original in cleanupRequests)
                {
                    try
                    {
                        var terminal = await client.CancelAsync(original, Sign(DevelopmentRetirementBinding.Cancel, original),
                            DateTime.UtcNow.AddSeconds(25), cleanupBudget.Token);
                        while (terminal.Status is "Pending" or "Running" or "Finishing")
                        {
                            await Task.Delay(500, cleanupBudget.Token);
                            terminal = await client.ObserveAsync(original, Sign(DevelopmentRetirementBinding.Observe, original),
                                DateTime.UtcNow.AddSeconds(25), cleanupBudget.Token);
                        }
                        cleanup.Add(new { taskId = original.TaskId, settled = DevelopmentRetirementBinding.Matches(original, terminal) &&
                            terminal.Status is "Succeeded" or "Rejected" or "Stopped" or "Interrupted", terminal.Status });
                    }
                    catch (Exception error)
                    {
                        cleanup.Add(new { taskId = original.TaskId, settled = false, failureType = error.GetType().Name });
                    }
                }
                throw;
            }
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(JsonSerializer.Serialize(new { passed = false, taskId,
                failureType = error.GetType().Name, rpcStatus = (error as RpcException)?.StatusCode.ToString(), cleanup }));
            return 1;
        }
        finally { if (key is not null) CryptographicOperations.ZeroMemory(key); }
    }

    private static void RequireKeys(JsonElement value, string[] keys)
    {
        if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Count() != keys.Length ||
            !value.EnumerateObject().Select(x => x.Name).Order().SequenceEqual(keys.Order())) throw new InvalidDataException();
    }
}
