using System.Globalization;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

/// <summary>Private line-framed installed RPC probe. Python independently checks physical inodes after each response.</summary>
internal static class ContainerAclProbe
{
    public static string Phase { get; private set; } = "startup";
    public static async Task RunAsync()
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(360));
        var headerText = await Console.In.ReadLineAsync(lifetime.Token); Require(headerText is { Length: > 0 and <= 8192 });
        using var header = JsonDocument.Parse(headerText!); var value = header.RootElement;
        Require(value.EnumerateObject().Count() == 5);
        var probeRevision = value.GetProperty("probeRevision").GetString()!;
        Require(probeRevision.Length == 40 && probeRevision.All(char.IsAsciiHexDigitLower));
        var revision = value.GetProperty("revision").GetString()!; var id = value.GetProperty("containerId").GetString()!;
        var instance = value.GetProperty("instanceId").GetString()!; var core = value.GetProperty("coreId").GetString()!;
        Require(revision.Length == 40 && revision.All(char.IsAsciiHexDigitLower) && id.Length == 64 && id.All(char.IsAsciiHexDigitLower) && core.Length == 64 && core.All(char.IsAsciiHexDigitLower));
        Require(Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.EndsWith("+" + probeRevision, StringComparison.Ordinal));
        var key = await File.ReadAllBytesAsync("/etc/whaledeck/agent-capability.key", lifetime.Token);
        try
        {
            Require(key.Length == 32);
            using var handler = new SocketsHttpHandler { UseProxy = false, ConnectCallback = async (_, token) =>
            {
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                try { await socket.ConnectAsync(new UnixDomainSocketEndPoint("/run/whaledeck/agent.sock"), token); return new NetworkStream(socket, ownsSocket: true); }
                catch { socket.Dispose(); throw; }
            } };
            using var channel = GrpcChannel.ForAddress("http://localhost", new() { HttpHandler = handler });
            var client = new ContainerAclService.ContainerAclServiceClient(channel);
            var formatter = new JsonFormatter(new JsonFormatter.Settings(formatDefaultValues: true));
            var operations = new Dictionary<string, ContainerAclRequest>(StringComparer.Ordinal);
            DateTime Until() => DateTime.UtcNow.AddSeconds(40);
            Metadata Headers(string action, IMessage request, string variant = "")
            {
                var hash = ContainerAclBinding.Hash(request);
                var expires = DateTimeOffset.UtcNow.AddSeconds(variant == "expired" ? -60 : variant == "future" ? 180 : 45).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
                var nonce = Guid.NewGuid().ToString("N"); var method = ContainerAclBinding.Service + action;
                var payload = variant == "domain" ? ContainerOwnershipBinding.Payload(expires, nonce, method, hash) : ContainerAclBinding.Payload(expires, nonce, method, hash);
                return new() { { "x-whaledeck-capability", expires + "." + nonce + "." + Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload))) }, { ContainerAclBinding.Header, hash } };
            }
            async Task Emit(object response) { await Console.Out.WriteLineAsync(JsonSerializer.Serialize(response)); await Console.Out.FlushAsync(); }
            Phase = "capabilities";
            var caps = await new AgentService.AgentServiceClient(channel).GetCapabilitiesAsync(new(), deadline: Until(), cancellationToken: lifetime.Token);
            Require(caps.ProtocolMajor == 1 && caps.ProtocolMinor >= 51 && caps.Capabilities.Contains("docker.files.acl-intents"));
            await Emit(new { kind = "ready", revision, protocolMinor = caps.ProtocolMinor });
            for (var count = 0; count < 64; count++)
            {
                var text = await Console.In.ReadLineAsync(lifetime.Token); Require(text is { Length: > 0 and <= 65536 });
                using var document = JsonDocument.Parse(text!); var input = document.RootElement; var action = input.GetProperty("action").GetString()!;
                Phase = action;
                if (action == "quit") { Require(input.EnumerateObject().Count() == 1); await Emit(new { kind = "done" }); return; }
                if (action == "plan")
                {
                    Require(input.EnumerateObject().Count() == 6);
                    var path = input.GetProperty("path").GetString()!; var kind = input.GetProperty("aclType").GetString()!;
                    var profile = input.GetProperty("profile").GetString()!;
                    Require(path is "work/acl-file.bin" or "work/acl-directory"); Require(kind is "access" or "default");
                    Require(kind == "access" ? profile is "named" or "mask" or "root" or "base" : path == "work/acl-directory" && profile is "named" or "clear");
                    var entries = profile == "clear" ? Array.Empty<ContainerAclEntry>() : profile == "base" ? new[] {
                        new ContainerAclEntry { Tag="user_obj",Permissions=6 }, new ContainerAclEntry { Tag="group_obj",Permissions=4 }, new ContainerAclEntry { Tag="other",Permissions=0 } } : new[] {
                        new ContainerAclEntry { Tag="user_obj",Permissions=6 }, new ContainerAclEntry { Tag="user",Id=profile=="root"?0U:2201U,Permissions=6 },
                        new ContainerAclEntry { Tag="group_obj",Permissions=0 }, new ContainerAclEntry { Tag="mask",Permissions=profile=="mask"?6U:4U }, new ContainerAclEntry { Tag="other",Permissions=0 } };
                    var request = new ContainerAclPlanRequest {
                        Command = new() { Location = new() { ContainerId=id,InstanceId=instance,Path=path }, AclType=kind,Entries={entries},ExpectedVersion=input.GetProperty("version").GetString()! },
                        Context = new() { ActorSubject="installed-acl-protocol-probe",TraceId="owned-acl-validation" } };
                    var refusals=0;
                    async Task Reject(Func<Task> call, StatusCode status) { await Denied(call,status); refusals++; }
                    if (input.GetProperty("boundaries").GetBoolean())
                    {
                        await Reject(async()=>await client.PlanAsync(request,deadline:Until(),cancellationToken:lifetime.Token),StatusCode.PermissionDenied);
                        await Reject(async()=>await client.PlanAsync(request,Headers("Plan",request,"domain"),Until(),lifetime.Token),StatusCode.PermissionDenied);
                        await Reject(async()=>await client.PlanAsync(request,Headers("Plan",request,"expired"),Until(),lifetime.Token),StatusCode.PermissionDenied);
                        await Reject(async()=>await client.PlanAsync(request,Headers("Plan",request,"future"),Until(),lifetime.Token),StatusCode.PermissionDenied);
                        await Reject(async()=>await client.PlanAsync(request,Headers("Apply",request),Until(),lifetime.Token),StatusCode.PermissionDenied);
                        var changed=request.Clone();changed.Command.Entries[1].Id++;
                        await Reject(async()=>await client.PlanAsync(changed,Headers("Plan",request),Until(),lifetime.Token),StatusCode.PermissionDenied);
                        var missing=request.Clone();missing.Command.Entries[1].ClearId();
                        await Reject(async()=>await client.PlanAsync(missing,Headers("Plan",missing),Until(),lifetime.Token),StatusCode.InvalidArgument);
                        missing=request.Clone();missing.Command.Entries[1].ClearPermissions();
                        await Reject(async()=>await client.PlanAsync(missing,Headers("Plan",missing),Until(),lifetime.Token),StatusCode.InvalidArgument);
                        var mask=request.Clone();mask.Command.Entries.RemoveAt(3);
                        await Reject(async()=>await client.PlanAsync(mask,Headers("Plan",mask),Until(),lifetime.Token),StatusCode.InvalidArgument);
                        var duplicate=request.Clone();duplicate.Command.Entries.Add(duplicate.Command.Entries[^1].Clone());
                        await Reject(async()=>await client.PlanAsync(duplicate,Headers("Plan",duplicate),Until(),lifetime.Token),StatusCode.InvalidArgument);
                        var protectedTarget=request.Clone();protectedTarget.Command.Location.ContainerId=core;
                        await Reject(async()=>await client.PlanAsync(protectedTarget,Headers("Plan",protectedTarget),Until(),lifetime.Token),StatusCode.PermissionDenied);
                        var readOnly=request.Clone();readOnly.Command.Location.Path="tmp/acl-readonly";
                        await Reject(async()=>await client.PlanAsync(readOnly,Headers("Plan",readOnly),Until(),lifetime.Token),StatusCode.PermissionDenied);
                    }
                    var authority=Headers("Plan",request);var plan=await client.PlanAsync(request,authority,Until(),lifetime.Token);
                    if(input.GetProperty("boundaries").GetBoolean()) await Reject(async()=>await client.PlanAsync(request,authority,Until(),lifetime.Token),StatusCode.PermissionDenied);
                    Require(plan.Command.Equals(request.Command) && plan.OwnerUid==1000 && plan.OwnerGid==1000 && plan.Kind==(path.EndsWith(".bin",StringComparison.Ordinal)?"File":"Directory"));
                    await Emit(new {kind="plan",plan=formatter.Format(plan),boundaryRefusals=refusals});continue;
                }
                if (action is "apply" or "discard")
                {
                    Require(input.EnumerateObject().Count()==3);
                    var plan=JsonParser.Default.Parse<ContainerAclPlan>(input.GetProperty("plan").GetString()!);
                    Require(plan.Command.Location.ContainerId==id && plan.Command.Location.InstanceId==instance && plan.Command.Location.Path is "work/acl-file.bin" or "work/acl-directory");
                    var operation=Guid.NewGuid().ToString("N"); var request=new ContainerAclRequest {OperationId=operation,Owner=new('B',64),Plan=plan,
                        Context=new(){ActorSubject="installed-acl-protocol-probe",TraceId="owned-acl-validation",JobId=operation,IdempotencyKey=Guid.NewGuid().ToString("N")}};
                    var refusals=0;
                    if(input.GetProperty("boundaries").GetBoolean())
                    {
                        var changed=request.Clone();changed.Plan.SourceChangedAtUnixNs++;
                        await Denied(async()=>await client.ApplyAsync(changed,Headers("Apply",request),Until(),lifetime.Token),StatusCode.PermissionDenied);refusals++;
                        await Denied(async()=>await client.ApplyAsync(request,Headers("Plan",request),Until(),lifetime.Token),StatusCode.PermissionDenied);refusals++;
                        await Denied(async()=>await client.ApplyAsync(request,Headers("Apply",request,"domain"),Until(),lifetime.Token),StatusCode.PermissionDenied);refusals++;
                        var context=request.Clone();context.Context.JobId=Guid.NewGuid().ToString("N");
                        await Denied(async()=>await client.ApplyAsync(context,Headers("Apply",context),Until(),lifetime.Token),StatusCode.InvalidArgument);refusals++;
                    }
                    var result=action=="apply" ? await client.ApplyAsync(request,Headers("Apply",request),Until(),lifetime.Token) : await client.DiscardAsync(request,Headers("Discard",request),Until(),lifetime.Token);
                    Require(result.State==(action=="apply"?"Completed":"Aborted") && result.OperationId==operation && result.AclType==plan.Command.AclType && result.TargetMode==plan.TargetMode);
                    Require(result.HasAclConfirmed && result.AclConfirmed==(action=="apply") && result.BindingCurrent==(action=="apply"));
                    Require(action=="apply" ? result.HistoryVerified && result.HistoryMayExist && result.HistoryDigest.Length==64 && result.Version.Length==64 : !result.HistoryMayExist && !result.HistoryVerified && result.Version.Length==0);
                    operations.Add(operation,request);await Emit(new {kind="receipt",operationId=operation,result=formatter.Format(result),boundaryRefusals=refusals});continue;
                }
                if(action is "inspect" or "replay" or "discard-old")
                {
                    Require(input.EnumerateObject().Count()==2);var operation=input.GetProperty("operationId").GetString()!;Require(operations.ContainsKey(operation));var request=operations[operation];
                    var method=action=="inspect"?"Inspect":action=="replay"?"Apply":"Discard";
                    var result=method=="Inspect"?await client.InspectAsync(request,Headers(method,request),Until(),lifetime.Token):method=="Apply"?await client.ApplyAsync(request,Headers(method,request),Until(),lifetime.Token):await client.DiscardAsync(request,Headers(method,request),Until(),lifetime.Token);
                    Require(result.Replayed && result.OperationId==operation);await Emit(new {kind="receipt",operationId=operation,result=formatter.Format(result)});continue;
                }
                throw new InvalidOperationException("Unsupported ACL probe action");
            }
            throw new InvalidOperationException("ACL probe budget exceeded");
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
    private static void Require(bool condition) { if (!condition) throw new InvalidOperationException("ACL probe invariant failed"); }
    private static async Task Denied(Func<Task> action, StatusCode expected)
    {
        try { await action(); } catch(RpcException error) when(error.StatusCode==expected) { return; }
        throw new InvalidOperationException("ACL probe expected refusal");
    }
}
