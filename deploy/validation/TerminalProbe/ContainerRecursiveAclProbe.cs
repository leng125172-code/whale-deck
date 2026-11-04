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

/// <summary>Opt-in signed UDS probe. Every response is physically verified by its private parent.</summary>
internal static class ContainerRecursiveAclProbe
{
    public static string Phase { get; private set; } = "startup";
    internal static ContainerAclEntry[] Entries(uint mask = 4, bool root = false, bool directory = false) =>
        [new() { Tag="user_obj", Permissions=directory?7U:6U },new() { Tag="user",Id=root?0U:2201U,Permissions=root?0U:directory?7U:6U },
         new() { Tag="group_obj",Permissions=0 },new() { Tag="mask",Permissions=mask },new() { Tag="other",Permissions=0 }];
    internal static ContainerRecursiveAclCommand Command(string id,string instance,string kind,string profile,string version)
    {
        Require(kind is "access" or "default");Require(kind=="access"?profile is "named" or "mask" or "root":profile is "named" or "clear");
        return new() { Location=new() {ContainerId=id,InstanceId=instance,Path="work/recursive-tree"},AclType=kind,ExpectedVersion=version,
            FileEntries=kind=="default"?null:new() {Entries={Entries(profile=="mask"?6U:4U,profile=="root")}},
            DirectoryEntries=new() {Entries={kind=="default"?(profile=="clear"?[]:Entries(6)):Entries(7,profile=="root",true)}} };
    }
    public static async Task RunAsync(bool paging = false)
    {
        using var lifetime=new CancellationTokenSource(TimeSpan.FromSeconds(360));
        var text=await Console.In.ReadLineAsync(lifetime.Token);Require(text is {Length:>0 and <=8192});
        using var header=JsonDocument.Parse(text!);var value=header.RootElement;Fields(value,"revision","probeRevision","containerId","instanceId","coreId");
        var revision=value.GetProperty("revision").GetString()!;var probeRevision=value.GetProperty("probeRevision").GetString()!;
        var id=value.GetProperty("containerId").GetString()!;var instance=value.GetProperty("instanceId").GetString()!;var core=value.GetProperty("coreId").GetString()!;
        Require(revision.Length==40 && revision.All(char.IsAsciiHexDigitLower) && probeRevision.Length==40 && probeRevision.All(char.IsAsciiHexDigitLower));
        Require(id.Length==64 && id.All(char.IsAsciiHexDigitLower) && core.Length==64 && core.All(char.IsAsciiHexDigitLower));
        Require(Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.EndsWith("+"+probeRevision,StringComparison.Ordinal));
        var key=await File.ReadAllBytesAsync("/etc/whaledeck/agent-capability.key",lifetime.Token);
        try
        {
            Require(key.Length==32);
            using var handler=new SocketsHttpHandler {UseProxy=false,ConnectCallback=async (_,token)=> {
                var socket=new Socket(AddressFamily.Unix,SocketType.Stream,ProtocolType.Unspecified);
                try {await socket.ConnectAsync(new UnixDomainSocketEndPoint("/run/whaledeck/agent.sock"),token);return new NetworkStream(socket,true);}
                catch {socket.Dispose();throw;}
            }};
            using var channel=GrpcChannel.ForAddress("http://localhost",new() {HttpHandler=handler});
            var client=new ContainerRecursiveAclService.ContainerRecursiveAclServiceClient(channel);
            var formatter=new JsonFormatter(new JsonFormatter.Settings(formatDefaultValues:true));
            var operations=new Dictionary<string,ContainerRecursiveAclRequest>(StringComparer.Ordinal);
            DateTime Until()=>DateTime.UtcNow.AddSeconds(40);
            Metadata Headers(string action,IMessage request,string variant="")
            {
                var hash=ContainerRecursiveAclBinding.Hash(request);var method=ContainerRecursiveAclBinding.Service+action;
                var expires=DateTimeOffset.UtcNow.AddSeconds(variant=="expired"?-60:variant=="future"?180:45).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
                var nonce=Guid.NewGuid().ToString("N");
                var payload=variant=="domain"?ContainerAclBinding.Payload(expires,nonce,method,hash):variant=="owner-domain"?ContainerRecursiveOwnershipBinding.Payload(expires,nonce,method,hash):ContainerRecursiveAclBinding.Payload(expires,nonce,method,hash);
                return new() {{"x-whaledeck-capability",expires+"."+nonce+"."+Convert.ToHexString(HMACSHA256.HashData(key,Encoding.UTF8.GetBytes(payload)))},{ContainerRecursiveAclBinding.Header,hash}};
            }
            async Task Emit(object response) {await Console.Out.WriteLineAsync(JsonSerializer.Serialize(response));await Console.Out.FlushAsync();}
            Phase="capabilities";var caps=await new AgentService.AgentServiceClient(channel).GetCapabilitiesAsync(new(),deadline:Until(),cancellationToken:lifetime.Token);
            Require(caps.ProtocolMajor==1 && caps.ProtocolMinor>=53 && caps.Capabilities.Contains("docker.files.recursive-acl-intents"));
            await Emit(new {kind="ready",revision,probeRevision,protocolMinor=caps.ProtocolMinor});
            for(var count=0;count<64;count++)
            {
                var line=await Console.In.ReadLineAsync(lifetime.Token);Require(line is {Length:>0 and <=65536});
                using var json=JsonDocument.Parse(line!);var input=json.RootElement;var action=input.GetProperty("action").GetString()!;Phase=action;
                if(paging) Require(action is "plan" or "apply" or "quit");
                if(action=="quit") {Fields(input,"action");await Emit(new {kind="done"});return;}
                if(action=="plan")
                {
                    Fields(input,"action","aclType","profile","version","boundaries");
                    if(paging) Require(input.GetProperty("aclType").GetString()=="access" && input.GetProperty("profile").GetString()=="named" && !input.GetProperty("boundaries").GetBoolean());
                    var request=new ContainerRecursiveAclPlanRequest {Command=Command(id,instance,input.GetProperty("aclType").GetString()!,input.GetProperty("profile").GetString()!,input.GetProperty("version").GetString()!),
                        Context=new() {ActorSubject="installed-recursive-acl-protocol-probe",TraceId="owned-recursive-acl-validation"}};
                    var refusals=0;async Task Reject(Func<Task> call,StatusCode code) {await Denied(call,code);refusals++;}
                    if(input.GetProperty("boundaries").GetBoolean())
                    {
                        Require(request.Command.AclType=="access" && input.GetProperty("profile").GetString()=="named");
                        await Reject(async()=>await client.PlanAsync(request,deadline:Until(),cancellationToken:lifetime.Token),StatusCode.PermissionDenied);
                        foreach(var variant in new[]{"domain","owner-domain","expired","future"}) await Reject(async()=>await client.PlanAsync(request,Headers("Plan",request,variant),Until(),lifetime.Token),StatusCode.PermissionDenied);
                        await Reject(async()=>await client.PlanAsync(request,Headers("Apply",request),Until(),lifetime.Token),StatusCode.PermissionDenied);
                        var changed=request.Clone();changed.Command.FileEntries.Entries[1].Id++;
                        await Reject(async()=>await client.PlanAsync(changed,Headers("Plan",request),Until(),lifetime.Token),StatusCode.PermissionDenied);
                        changed=request.Clone();changed.Command.DirectoryEntries.Entries[0].Permissions=6;
                        await Reject(async()=>await client.PlanAsync(changed,Headers("Plan",request),Until(),lifetime.Token),StatusCode.PermissionDenied);
                        var missing=request.Clone();missing.Command.FileEntries=null;
                        await Reject(async()=>await client.PlanAsync(missing,Headers("Plan",missing),Until(),lifetime.Token),StatusCode.InvalidArgument);
                        missing=request.Clone();missing.Command.DirectoryEntries=null;
                        await Reject(async()=>await client.PlanAsync(missing,Headers("Plan",missing),Until(),lifetime.Token),StatusCode.InvalidArgument);
                        missing=request.Clone();missing.Command.FileEntries.Entries[1].ClearId();
                        await Reject(async()=>await client.PlanAsync(missing,Headers("Plan",missing),Until(),lifetime.Token),StatusCode.InvalidArgument);
                        missing=request.Clone();missing.Command.FileEntries.Entries[1].ClearPermissions();
                        await Reject(async()=>await client.PlanAsync(missing,Headers("Plan",missing),Until(),lifetime.Token),StatusCode.InvalidArgument);
                        var mask=request.Clone();mask.Command.FileEntries.Entries.RemoveAt(3);
                        await Reject(async()=>await client.PlanAsync(mask,Headers("Plan",mask),Until(),lifetime.Token),StatusCode.InvalidArgument);
                        var duplicate=request.Clone();duplicate.Command.DirectoryEntries.Entries.Add(duplicate.Command.DirectoryEntries.Entries[^1].Clone());
                        await Reject(async()=>await client.PlanAsync(duplicate,Headers("Plan",duplicate),Until(),lifetime.Token),StatusCode.InvalidArgument);
                        var defaultFile=request.Clone();defaultFile.Command.AclType="default";
                        await Reject(async()=>await client.PlanAsync(defaultFile,Headers("Plan",defaultFile),Until(),lifetime.Token),StatusCode.InvalidArgument);
                        var protectedTarget=request.Clone();protectedTarget.Command.Location.ContainerId=core;
                        await Reject(async()=>await client.PlanAsync(protectedTarget,Headers("Plan",protectedTarget),Until(),lifetime.Token),StatusCode.PermissionDenied);
                        var readOnly=request.Clone();readOnly.Command.Location.Path="tmp/recursive-readonly";
                        await Reject(async()=>await client.PlanAsync(readOnly,Headers("Plan",readOnly),Until(),lifetime.Token),StatusCode.PermissionDenied);
                    }
                    var authority=Headers("Plan",request);var plan=await client.PlanAsync(request,authority,Until(),lifetime.Token);
                    if(input.GetProperty("boundaries").GetBoolean()) await Reject(async()=>await client.PlanAsync(request,authority,Until(),lifetime.Token),StatusCode.PermissionDenied);
                    Require(plan.Command.Equals(request.Command) && plan.OwnerUid==1000 && plan.OwnerGid==1000 &&
                        plan.EntryCount==(paging?26U:4U) && plan.FileCount==(paging?25U:2U) && plan.DirectoryCount==(paging?1U:2U));
                    await Emit(new {kind="plan",plan=formatter.Format(plan),boundaryRefusals=refusals});continue;
                }
                Require(action is "apply" or "inspect" or "discard" or "reject");Fields(input,"action","operationId","plan");
                var operationId=input.GetProperty("operationId").GetString()!;Require(Guid.TryParseExact(operationId,"N",out var parsed) && parsed!=Guid.Empty && parsed.ToString("N")==operationId);
                var supplied=JsonParser.Default.Parse<ContainerRecursiveAclPlan>(input.GetProperty("plan").GetRawText());
                Require(supplied.Command.Location.ContainerId==id && supplied.Command.Location.InstanceId==instance && supplied.Command.Location.Path=="work/recursive-tree");
                if(!operations.TryGetValue(operationId,out var operation))
                {
                    operation=new() {OperationId=operationId,Owner=new('B',64),Plan=supplied,Context=new() {ActorSubject="installed-recursive-acl-protocol-probe",TraceId="owned-recursive-acl-validation",JobId=operationId,IdempotencyKey=Guid.NewGuid().ToString("N")}};
                    operations.Add(operationId,operation);
                }
                Require(operation.Plan.Equals(supplied));
                if(action=="reject")
                {
                    Require(operation.Plan.Command.AclType=="access");var refusals=0;async Task Reject(Func<Task> call,StatusCode code) {await Denied(call,code);refusals++;}
                    var changed=operation.Clone();changed.Plan.ManifestDigest=new('F',64);
                    await Reject(async()=>await client.ApplyAsync(changed,Headers("Apply",operation),Until(),lifetime.Token),StatusCode.PermissionDenied);
                    changed=operation.Clone();changed.Plan.Command.FileEntries.Entries[1].Id++;
                    await Reject(async()=>await client.ApplyAsync(changed,Headers("Apply",operation),Until(),lifetime.Token),StatusCode.PermissionDenied);
                    changed=operation.Clone();changed.Plan.Command.DirectoryEntries.Entries[0].Permissions=6;
                    await Reject(async()=>await client.ApplyAsync(changed,Headers("Apply",operation),Until(),lifetime.Token),StatusCode.PermissionDenied);
                    await Reject(async()=>await client.ApplyAsync(operation,Headers("Apply",operation,"domain"),Until(),lifetime.Token),StatusCode.PermissionDenied);
                    await Reject(async()=>await client.ApplyAsync(operation,Headers("Inspect",operation),Until(),lifetime.Token),StatusCode.PermissionDenied);
                    var missing=operation.Clone();missing.Context.JobId="";
                    await Reject(async()=>await client.ApplyAsync(missing,Headers("Apply",missing),Until(),lifetime.Token),StatusCode.InvalidArgument);
                    changed=operation.Clone();changed.Context.ActorSubject="other-valid-subject";
                    await Reject(async()=>await client.ApplyAsync(changed,Headers("Apply",operation),Until(),lifetime.Token),StatusCode.PermissionDenied);
                    await Reject(async()=>await client.InspectAsync(operation,Headers("Inspect",operation),Until(),lifetime.Token),StatusCode.NotFound);
                    await Emit(new {kind="rejected",operationId,refusals});continue;
                }
                var result=action switch {
                    "apply"=>await client.ApplyAsync(operation,Headers("Apply",operation),Until(),lifetime.Token),
                    "inspect"=>await client.InspectAsync(operation,Headers("Inspect",operation),Until(),lifetime.Token),
                    _=>await client.DiscardAsync(operation,Headers("Discard",operation),Until(),lifetime.Token)};
                Require(result.OperationId==operationId && result.Fingerprint==supplied.Fingerprint && result.TargetProof==supplied.TargetProof && result.AclType==supplied.Command.AclType);
                await Emit(new {kind="receipt",operationId,result=formatter.Format(result)});
            }
            throw new InvalidOperationException("Recursive ACL probe command budget exceeded");
        }
        finally {CryptographicOperations.ZeroMemory(key);}
    }
    private static void Fields(JsonElement value,params string[] fields)
    {
        Require(value.ValueKind==JsonValueKind.Object);var names=value.EnumerateObject().Select(p=>p.Name).ToArray();
        Require(names.Length==fields.Length && names.Distinct(StringComparer.Ordinal).Count()==fields.Length && names.All(n=>fields.Contains(n,StringComparer.Ordinal)));
    }
    private static void Require(bool condition) {if(!condition)throw new InvalidOperationException("Recursive ACL probe invariant failed");}
    private static async Task Denied(Func<Task> call,StatusCode code)
    {try {await call();}catch(RpcException error) when(error.StatusCode==code) {return;}throw new InvalidOperationException("Recursive ACL probe expected refusal");}
}
