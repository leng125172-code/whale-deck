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

/// <summary>Fixed new-resource signed RPC probe; parent verifies each disk boundary before sending another action.</summary>
internal static class ContainerRecycleProbe
{
    public static string Phase { get; private set; } = "startup";
    public static string FailureCode { get; private set; } = "";
    public static async Task RunAsync()
    {
        Require(OperatingSystem.IsLinux());
        using var lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(12));
        var line = await Console.In.ReadLineAsync(lifetime.Token); Require(line is { Length: > 0 } && Encoding.UTF8.GetByteCount(line) <= 8192);
        using var header = JsonDocument.Parse(line!); var input = header.RootElement;
        Fields(input, "revision", "probeRevision", "containerId", "instanceId", "coreId");
        var revision=Text(input,"revision"); var probeRevision=Text(input,"probeRevision");
        var id=Text(input,"containerId"); var instance=Text(input,"instanceId"); var core=Text(input,"coreId");
        Require(Lower(revision,40) && Lower(probeRevision,40) && Lower(id,64) && Lower(core,64) && id!=core && instance.Length is > 20 and < 64);
        Require(Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.EndsWith("+"+probeRevision,StringComparison.Ordinal));
        using var handler = new SocketsHttpHandler { UseProxy=false, ConnectCallback=async (_,token)=>
        {
            var socket=new Socket(AddressFamily.Unix,SocketType.Stream,ProtocolType.Unspecified);
            try { await socket.ConnectAsync(new UnixDomainSocketEndPoint("/run/whaledeck/agent.sock"),token); return new NetworkStream(socket,true); }
            catch { socket.Dispose();throw; }
        }};
        using var channel=GrpcChannel.ForAddress("http://localhost",new(){HttpHandler=handler});
        Phase="capabilities";
        var caps=await new AgentService.AgentServiceClient(channel).GetCapabilitiesAsync(new(),deadline:DateTime.UtcNow.AddSeconds(10),cancellationToken:lifetime.Token);
        // No key or operation is read until the installed protocol explicitly supports this domain.
        if (!(caps.ProtocolMajor==1 && caps.ProtocolMinor>=56 && caps.Capabilities.Contains("docker.files.recycle")))
        {
            FailureCode="CONTAINER_RECYCLE_CAPABILITY_UNAVAILABLE";
            throw new InvalidOperationException();
        }
        var key=await File.ReadAllBytesAsync("/etc/whaledeck/agent-capability.key",lifetime.Token);
        try
        {
            Require(key.Length==32);
            var client=new ContainerRecycleService.ContainerRecycleServiceClient(channel);
            var formatter=new JsonFormatter(new JsonFormatter.Settings(formatDefaultValues:true));
            var plans=new Dictionary<string,ContainerRecyclePlan>(StringComparer.Ordinal);
            var operations=new Dictionary<string,ContainerRecycleRequest>(StringComparer.Ordinal);
            var restores=new Dictionary<string,ContainerRecycleRequest>(StringComparer.Ordinal);
            DateTime Until()=>DateTime.UtcNow.AddSeconds(120);
            RequestContext Context()=>new(){ActorSubject="installed-recycle-probe",TraceId="owned-recycle-validation"};
            Metadata Headers(string action,IMessage request,string variant="")
            {
                var hash=ContainerRecycleBinding.Hash(request); var method=ContainerRecycleBinding.Service+action;
                var expires=DateTimeOffset.UtcNow.AddSeconds(variant=="expired"?-60:variant=="future"?180:45).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
                var nonce=Guid.NewGuid().ToString("N");
                var payload=variant switch {"permission-domain"=>ContainerPermissionBinding.Payload(expires,nonce,method,hash),
                    "history-domain"=>ContainerHistoryBinding.Payload(expires,nonce,method,hash),"generic-domain"=>$"{expires}.{nonce}.{method}",
                    _=>ContainerRecycleBinding.Payload(expires,nonce,method,hash)};
                return new(){{"x-whaledeck-capability",expires+"."+nonce+"."+Convert.ToHexString(HMACSHA256.HashData(key,Encoding.UTF8.GetBytes(payload)))},{ContainerRecycleBinding.Header,hash}};
            }
            async Task Emit(object value){await Console.Out.WriteLineAsync(JsonSerializer.Serialize(value));await Console.Out.FlushAsync();}
            await Emit(new {kind="ready",revision,probeRevision,protocolMinor=caps.ProtocolMinor});
            for(var count=0;count<64;count++)
            {
                line=await Console.In.ReadLineAsync(lifetime.Token); Require(line is {Length:>0} && Encoding.UTF8.GetByteCount(line)<=65536);
                using var packet=JsonDocument.Parse(line!);input=packet.RootElement;var action=Text(input,"action");Phase=action;
                if(action=="quit"){Fields(input,"action");await Emit(new {kind="done"});return;}
                var refusals=0;
                async Task Reject(Func<Task> call,StatusCode expected){Phase=action+"-refusal-"+(refusals+1).ToString(CultureInfo.InvariantCulture);await Denied(call,expected);refusals++;}
                if(action=="plan")
                {
                    Fields(input,"action","version","boundaries");Require(Upper(Text(input,"version")));
                    var request=new ContainerRecyclePlanRequest {Command=new(){Location=new(){ContainerId=id,InstanceId=instance,Path="work/recycle-rpc.bin"},ExpectedVersion=Text(input,"version")},Context=Context()};
                    if(input.GetProperty("boundaries").GetBoolean())
                    {
                        await Reject(async()=>await client.PlanAsync(request,deadline:Until(),cancellationToken:lifetime.Token),StatusCode.PermissionDenied);
                        foreach(var variant in new[]{"permission-domain","history-domain","generic-domain","expired","future"})
                            await Reject(async()=>await client.PlanAsync(request,Headers("Plan",request,variant),Until(),lifetime.Token),StatusCode.PermissionDenied);
                        await Reject(async()=>await client.PlanAsync(request,Headers("Apply",request),Until(),lifetime.Token),StatusCode.PermissionDenied);
                        foreach(var field in new[]{"path","version","actor","trace"})
                        {
                            var changed=request.Clone();
                            switch(field){case "path":changed.Command.Location.Path="work/other.bin";break;case "version":changed.Command.ExpectedVersion=new('F',64);break;case "actor":changed.Context.ActorSubject+="-changed";break;default:changed.Context.TraceId+="-changed";break;}
                            await Reject(async()=>await client.PlanAsync(changed,Headers("Plan",request),Until(),lifetime.Token),StatusCode.PermissionDenied);
                        }
                        var bad=request.Clone();bad.Command.ExpectedVersion="";
                        await Reject(async()=>await client.PlanAsync(bad,Headers("Plan",bad),Until(),lifetime.Token),StatusCode.InvalidArgument);
                        bad=request.Clone();bad.Command.Location.ContainerId=core;
                        await Reject(async()=>await client.PlanAsync(bad,Headers("Plan",bad),Until(),lifetime.Token),StatusCode.PermissionDenied);
                        bad=request.Clone();bad.Command.Location.Path="tmp/recycle-rpc.bin";
                        await Reject(async()=>await client.PlanAsync(bad,Headers("Plan",bad),Until(),lifetime.Token),StatusCode.PermissionDenied);
                        bad=request.Clone();bad.Command.Location.Path="work/.env";
                        await Reject(async()=>await client.PlanAsync(bad,Headers("Plan",bad),Until(),lifetime.Token),StatusCode.PermissionDenied);
                        bad=request.Clone();bad.Command.ExpectedVersion=new('0',64);
                        await Reject(async()=>await client.PlanAsync(bad,Headers("Plan",bad),Until(),lifetime.Token),StatusCode.Aborted);
                    }
                    Phase="plan-value";var authority=Headers("Plan",request);var plan=await client.PlanAsync(request,authority,Until(),lifetime.Token);
                    Require(plan.Command.Equals(request.Command) && plan.SchemaVersion==1 && plan.OwnerUid==1000 && plan.OwnerGid==1000 && plan.SourceIdentity is {Inode:>0,LinkCount:1});
                    Require(Upper(plan.Fingerprint) && Upper(plan.Sha256));plans[plan.Fingerprint]=plan.Clone();
                    if(input.GetProperty("boundaries").GetBoolean())await Reject(async()=>await client.PlanAsync(request,authority,Until(),lifetime.Token),StatusCode.PermissionDenied);
                    await Emit(new {kind="plan",plan=formatter.Format(plan),boundaryRefusals=refusals});continue;
                }
                Require(action is "apply" or "inspect" or "discard" or "restore" or "reject");Fields(input,"action","operationId","plan");
                var operationId=Text(input,"operationId");Require(Lower(operationId,32) && Guid.ParseExact(operationId,"N")!=Guid.Empty);
                var supplied=JsonParser.Default.Parse<ContainerRecyclePlan>(Text(input,"plan"));
                Require(plans.TryGetValue(supplied.Fingerprint,out var approved) && supplied.Equals(approved));
                if(!operations.TryGetValue(operationId,out var operation))
                {
                    Require(action is "apply" or "discard" or "reject");
                    var context=Context();context.JobId=operationId;context.IdempotencyKey=Guid.NewGuid().ToString("N");
                    operation=new(){OperationId=operationId,Owner=new('B',64),Plan=supplied.Clone(),Context=context};operations.Add(operationId,operation);
                }
                Require(operation.Plan.Equals(supplied));
                if(action=="reject")
                {
                    foreach(var field in new[]{"sha","ctime","inode","owner","actor","trace","key"})
                    {
                        var changed=operation.Clone();switch(field){case "sha":changed.Plan.Sha256=new('E',64);break;case "ctime":changed.Plan.SourceChangedAtUnixNs++;break;case "inode":changed.Plan.SourceIdentity.Inode++;break;case "owner":changed.Owner=new('E',64);break;case "actor":changed.Context.ActorSubject+="-changed";break;case "trace":changed.Context.TraceId+="-changed";break;default:changed.Context.IdempotencyKey=Guid.NewGuid().ToString("N");break;}
                        await Reject(async()=>await client.ApplyAsync(changed,Headers("Apply",operation),Until(),lifetime.Token),StatusCode.PermissionDenied);
                    }
                    foreach(var variant in new[]{"permission-domain","history-domain","generic-domain"})await Reject(async()=>await client.ApplyAsync(operation,Headers("Apply",operation,variant),Until(),lifetime.Token),StatusCode.PermissionDenied);
                    await Reject(async()=>await client.ApplyAsync(operation,Headers("Restore",operation),Until(),lifetime.Token),StatusCode.PermissionDenied);
                    await Reject(async()=>await client.RestoreAsync(operation,Headers("Restore",operation),Until(),lifetime.Token),StatusCode.InvalidArgument);
                    var bad=operation.Clone();bad.Context.JobId=Guid.NewGuid().ToString("N");
                    await Reject(async()=>await client.ApplyAsync(bad,Headers("Apply",bad),Until(),lifetime.Token),StatusCode.InvalidArgument);
                    await Reject(async()=>await client.InspectAsync(operation,Headers("Inspect",operation),Until(),lifetime.Token),StatusCode.NotFound);
                    await Emit(new {kind="rejected",operationId,refusals});continue;
                }
                var current=operation;
                if(action=="restore")
                {
                    if(!restores.TryGetValue(operationId,out current))
                    {
                        current=operation.Clone();current.Context.JobId=Guid.NewGuid().ToString("N");current.Context.IdempotencyKey=current.Context.JobId;
                        Require(current.Context.JobId!=operationId);restores.Add(operationId,current);
                    }
                }
                Phase=action+"-value";
                var result=action switch {
                    "apply"=>await client.ApplyAsync(current,Headers("Apply",current),Until(),lifetime.Token),
                    "inspect"=>await client.InspectAsync(current,Headers("Inspect",current),Until(),lifetime.Token),
                    "discard"=>await client.DiscardAsync(current,Headers("Discard",current),Until(),lifetime.Token),
                    _=>await client.RestoreAsync(current,Headers("Restore",current),Until(),lifetime.Token)};
                Require(result.OperationId==operationId && result.ContainerId==id && result.InstanceId==instance && result.Fingerprint==supplied.Fingerprint && result.TargetProof==supplied.TargetProof);
                Require(result.State is "Trashed" or "Restored" or "Aborted" or "Unknown");
                Require(result.State=="Unknown" ? !result.HasSourceRetained && !result.HasSourceRestored : result.HasSourceRetained && result.HasSourceRestored);
                await Emit(new {kind="receipt",action,operationId,jobId=current.Context.JobId,result=formatter.Format(result)});
            }
            throw new InvalidOperationException();
        }
        finally {CryptographicOperations.ZeroMemory(key);}
    }
    private static void Fields(JsonElement value,params string[] names){Require(value.ValueKind==JsonValueKind.Object);var fields=value.EnumerateObject().Select(p=>p.Name).ToArray();Require(fields.Length==names.Length && fields.ToHashSet(StringComparer.Ordinal).SetEquals(names));}
    private static string Text(JsonElement value,string name)=>value.GetProperty(name).GetString()??throw new InvalidOperationException();
    private static bool Lower(string value,int length)=>value.Length==length && value.All(char.IsAsciiHexDigitLower);
    private static bool Upper(string value)=>value.Length==64 && value.All(c=>char.IsAsciiDigit(c)||c is >= 'A' and <= 'F');
    private static void Require(bool condition){if(!condition)throw new InvalidOperationException();}
    private static async Task Denied(Func<Task> call,StatusCode expected){try{await call();}catch(RpcException e)when(e.StatusCode==expected){return;}throw new InvalidOperationException();}
}
