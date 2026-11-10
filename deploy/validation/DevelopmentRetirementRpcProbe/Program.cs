using System.Globalization;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using WhaleDeck.Agent.Services;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;


if (args is ["--installed-plan"]) return await InstalledPlanProbe.RunAsync();

// Root-only QA bridge. The caller first supplies a PRIVATE mount namespace
// binding the fixed helper entrypoint to the isolated native probe copy.
// Production code, services, sockets, keys and configurations are untouched.
if (!OperatingSystem.IsLinux() || args.Length != 1 || !Directory.Exists(args[0])) return 2;
var home = args[0];
var key = RandomNumberGenerator.GetBytes(32);
var keyFile = Path.Combine(home, "ephemeral-capability.key");
var socket = Path.Combine(home, "rpc.sock");
await File.WriteAllBytesAsync(keyFile, key);
File.SetUnixFileMode(keyFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
try
{
    using var budget = new CancellationTokenSource(TimeSpan.FromMinutes(5));
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
    builder.Logging.ClearProviders(); builder.Configuration["Agent:CapabilityKeyFile"] = keyFile;
    builder.WebHost.ConfigureKestrel(o => o.ListenUnixSocket(socket, l => l.Protocols = HttpProtocols.Http2));
    builder.Services.AddGrpc(o => { o.Interceptors.Add<CapabilityInterceptor>(); o.Interceptors.Add<AgentErrorInterceptor>(); });
    builder.Services.AddSingleton<CapabilityValidator>(); builder.Services.AddSingleton<PeerCredentialPolicy>();
    builder.Services.AddSingleton<BoundedProcessRunner>();
    await using var server = builder.Build();
    server.UseMiddleware<PeerCredentialGuard>(); server.MapGrpcService<DevelopmentRetirementGrpcService>();
    await server.StartAsync(budget.Token);
    using var channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions { HttpHandler = new SocketsHttpHandler
    {
        UseProxy = false, ConnectCallback = async (_, token) =>
        {
            var connection = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            await connection.ConnectAsync(new UnixDomainSocketEndPoint(socket), token); return new NetworkStream(connection, ownsSocket: true);
        }
    } });
    var client = new DevelopmentRetirementService.DevelopmentRetirementServiceClient(channel);
    await Console.Out.WriteLineAsync("{\"ready\":true}");
    var refusals = false;
    while (await Console.In.ReadLineAsync(budget.Token) is { } line)
    {
        if (line.Length > 8192) throw new InvalidDataException();
        using var document = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 5 });
        var action = document.RootElement.GetProperty("action").GetString();
        var value = document.RootElement.GetProperty("request"); var prerequisite = value.GetProperty("prerequisite"); var storage = prerequisite.GetProperty("storage");
        var request = new WorkspaceRetirementRequest
        {
            TaskId = value.GetProperty("taskId").GetString(), RetirementId = value.GetProperty("retirementId").GetString(), ArchiveId = prerequisite.GetProperty("archiveId").GetString(),
            Phase = value.GetProperty("phase").GetString(), ExpectedEpoch = prerequisite.GetProperty("expectedEpoch").GetUInt64(),
            ArchivePlanSha256 = prerequisite.GetProperty("archivePlanSha256").GetString(), CaptureReceiptSha256 = prerequisite.GetProperty("captureReceiptSha256").GetString(),
            RetirementPlanSha256 = value.GetProperty("retirementPlanSha256").GetString(),
            Storage = new() { WorkspaceId = storage.GetProperty("workspaceId").GetString(), StorageRootId = storage.GetProperty("rootId").GetString(),
                EnvironmentSha256 = storage.GetProperty("environmentSha256").GetString(), DiskMib = storage.GetProperty("diskMiB").GetUInt32(),
                ActorSubject = "native-retirement-rpc-fixture", TraceId = "native-retirement-rpc-fixture" }
        };
        var method = action switch { "start" => DevelopmentRetirementBinding.Start, "observe" => DevelopmentRetirementBinding.Observe,
            "cancel" => DevelopmentRetirementBinding.Cancel, _ => throw new InvalidDataException() };
        Metadata Sign(string target, bool legacy = false, bool archiveDomain = false)
        {
            var expiry = DateTimeOffset.UtcNow.AddSeconds(45).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            var nonce = Guid.NewGuid().ToString("N");
            var payload = legacy ? $"{expiry}.{nonce}.{target}" : archiveDomain
                ? DevelopmentArchiveBinding.Payload(expiry, nonce, target, DevelopmentRetirementBinding.Hash(request))
                : DevelopmentRetirementBinding.Payload(expiry, nonce, target, DevelopmentRetirementBinding.Hash(request));
            return new() { { "x-whaledeck-capability", $"{expiry}.{nonce}.{Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload)))}" } };
        }
        async Task Denied(WorkspaceRetirementRequest body, Metadata headers)
        {
            try { await client.StartAsync(body, headers, DateTime.UtcNow.AddSeconds(5), budget.Token); }
            catch (RpcException error) when (error.StatusCode == StatusCode.PermissionDenied) { return; }
            throw new InvalidDataException("NATIVE_RETIREMENT_RPC_UNAUTHORIZED_ACCEPTED");
        }
        var headers = Sign(method);
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(25);
            var result = action switch
            {
                "start" => await client.StartAsync(request, headers, deadline, budget.Token),
                "observe" => await client.ObserveAsync(request, headers, deadline, budget.Token),
                "cancel" => await client.CancelAsync(request, headers, deadline, budget.Token),
                _ => throw new InvalidDataException()
            };
            if (!DevelopmentRetirementBinding.Matches(request, result)) throw new InvalidDataException();
            if (!refusals)
            {
                await Denied(request, new()); await Denied(request, headers);
                await Denied(request, Sign(DevelopmentRetirementBinding.Start, legacy: true));
                await Denied(request, Sign(DevelopmentRetirementBinding.Start, archiveDomain: true));
                await Denied(request, Sign(DevelopmentRetirementBinding.Cancel));
                var duplicate = Sign(DevelopmentRetirementBinding.Start); duplicate.Add("x-whaledeck-capability", duplicate[0].Value);
                await Denied(request, duplicate);
                var changed = request.Clone(); changed.Storage.ActorSubject = "different-fixture-actor";
                await Denied(changed, Sign(DevelopmentRetirementBinding.Start)); refusals = true;
            }
            using var payload = result.ResultJson.IsEmpty ? null : JsonDocument.Parse(result.ResultJson.Memory);
            var receipt = JsonSerializer.Serialize(new { schemaVersion = 1, taskId = result.TaskId, requestSha256 = result.RequestSha256,
                status = result.Status, code = result.Code.Length == 0 ? null : result.Code, result = payload?.RootElement });
            await Console.Out.WriteLineAsync(JsonSerializer.Serialize(new { rpcStatus = "OK", stdout = receipt, stderr = "", signatureRefusalsPassed = refusals }));
        }
        catch (RpcException error)
        {
            var code = error.Status.Detail;
            if (code.Length > 128 || code.Any(c => !char.IsAsciiLetterUpper(c) && c != '_')) code = "WORKSPACE_RETIREMENT_RPC_UNCONFIRMED";
            await Console.Out.WriteLineAsync(JsonSerializer.Serialize(new { rpcStatus = error.StatusCode.ToString(), stdout = "", stderr = code, signatureRefusalsPassed = refusals }));
        }
    }
    await server.StopAsync(CancellationToken.None);
    return 0;
}
catch (Exception error)
{
    await Console.Error.WriteLineAsync(JsonSerializer.Serialize(new { failed = true, failureType = error.GetType().Name })); return 1;
}
finally
{
    CryptographicOperations.ZeroMemory(key); File.Delete(keyFile); File.Delete(socket);
}
