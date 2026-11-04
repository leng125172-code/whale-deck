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

/// <summary>Installed UDS metadata reads only. No write, discard, restore or arbitrary host path.</summary>
internal static class ContainerRecursiveAclHistoryProbe
{
    public static string Phase { get; private set; } = "startup";
    public static async Task RunAsync()
    {
        Require(OperatingSystem.IsLinux());
        using var lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(8));
        var text = await Console.In.ReadLineAsync(lifetime.Token); Require(text is { Length: > 0 and <= 8192 });
        using var header = JsonDocument.Parse(text!); var input = header.RootElement;
        Fields(input, "revision", "probeRevision", "containerId", "instanceId", "coreId");
        var revision = Text(input, "revision"); var probeRevision = Text(input, "probeRevision");
        var id = Text(input, "containerId"); var core = Text(input, "coreId"); var instance = Text(input, "instanceId");
        Require(Lower(revision,40) && Lower(probeRevision,40) && Lower(id,64) && Lower(core,64) && instance.Length is > 20 and < 64);
        Require(Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion.EndsWith("+" + probeRevision, StringComparison.Ordinal));
        var key = await File.ReadAllBytesAsync("/etc/whaledeck/agent-capability.key", lifetime.Token);
        try
        {
            Require(key.Length == 32);
            using var handler = new SocketsHttpHandler { UseProxy=false, ConnectCallback=async (_, token)=>
            {
                var socket=new Socket(AddressFamily.Unix,SocketType.Stream,ProtocolType.Unspecified);
                try { await socket.ConnectAsync(new UnixDomainSocketEndPoint("/run/whaledeck/agent.sock"),token); return new NetworkStream(socket,true); }
                catch { socket.Dispose(); throw; }
            }};
            using var channel=GrpcChannel.ForAddress("http://localhost",new(){HttpHandler=handler});
            var client=new ContainerRecursiveAclHistoryService.ContainerRecursiveAclHistoryServiceClient(channel);
            var formatter=new JsonFormatter(new JsonFormatter.Settings(formatDefaultValues:true));
            DateTime Until()=>DateTime.UtcNow.AddSeconds(35);
            RequestContext Context()=>new(){ActorSubject="installed-recursive-acl-history-probe",TraceId="owned-recursive-acl-history-validation"};
            ContainerFileLocation Location(string path)=>new(){ContainerId=id,InstanceId=instance,Path=path};
            Metadata Headers(string action,IMessage request,string variant="")
            {
                var hash=ContainerRecursiveAclHistoryBinding.Hash(request);
                var expires=DateTimeOffset.UtcNow.AddSeconds(variant=="expired"?-60:variant=="future"?180:45).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
                var nonce=Guid.NewGuid().ToString("N"); var method=ContainerRecursiveAclHistoryBinding.Service+action;
                var payload=variant=="acl-domain"?ContainerAclBinding.Payload(expires,nonce,method,hash):
                    variant=="content-domain"?ContainerHistoryBinding.Payload(expires,nonce,method,hash):
                    variant=="recursive-domain"?ContainerRecursiveAclBinding.Payload(expires,nonce,method,hash):
                    variant=="single-history-domain"?ContainerAclHistoryBinding.Payload(expires,nonce,method,hash):ContainerRecursiveAclHistoryBinding.Payload(expires,nonce,method,hash);
                return new(){{"x-whaledeck-capability",expires+"."+nonce+"."+Convert.ToHexString(HMACSHA256.HashData(key,Encoding.UTF8.GetBytes(payload)))},
                    {ContainerRecursiveAclHistoryBinding.Header,hash}};
            }
            async Task Emit(object response) { await Console.Out.WriteLineAsync(JsonSerializer.Serialize(response)); await Console.Out.FlushAsync(); }
            Phase="capabilities";
            var caps=await new AgentService.AgentServiceClient(channel).GetCapabilitiesAsync(new(),deadline:Until(),cancellationToken:lifetime.Token);
            Require(caps.ProtocolMajor==1 && caps.ProtocolMinor>=54 && caps.Capabilities.Contains("docker.files.recursive-acl-history"));
            await Emit(new {kind="ready",revision,probeRevision,protocolMinor=caps.ProtocolMinor});
            for(var count=0;count<200;count++)
            {
                text=await Console.In.ReadLineAsync(lifetime.Token); Require(text is {Length:>0 and <=8192});
                using var line=JsonDocument.Parse(text!); input=line.RootElement; var action=Text(input,"action"); Phase=action;
                if(action=="quit") { Fields(input,"action"); await Emit(new{kind="done"}); return; }
                var path=Text(input,"path"); Require(path is "work/recursive-tree" or "work/original.bin" or "work/history-missing");
                var refusals=0;
                async Task Reject(Func<Task> call,StatusCode status) { await Denied(call,status); refusals++; }
                if(action=="list")
                {
                    Fields(input,"action","path","after","take","expectedVersion","boundaries");
                    var request=new ContainerRecursiveAclHistoryListRequest{Location=Location(path),After=Text(input,"after"),Take=input.GetProperty("take").GetUInt32(),
                        ExpectedVersion=Text(input,"expectedVersion"),Context=Context()};
                    if(input.GetProperty("boundaries").GetBoolean())
                    {
                        await Reject(async()=>await client.ListAsync(request,deadline:Until(),cancellationToken:lifetime.Token),StatusCode.PermissionDenied);
                        foreach(var variant in new[]{"acl-domain","content-domain","recursive-domain","single-history-domain","expired","future"})
                            await Reject(async()=>await client.ListAsync(request,Headers("List",request,variant),Until(),lifetime.Token),StatusCode.PermissionDenied);
                        await Reject(async()=>await client.ListAsync(request,Headers("Read",request),Until(),lifetime.Token),StatusCode.PermissionDenied);
                        var changes=new List<ContainerRecursiveAclHistoryListRequest>();
                        var changed=request.Clone();changed.Take=request.Take==1?2U:1U;changes.Add(changed);
                        changed=request.Clone();changed.Location.Path="work/original.bin";changes.Add(changed);
                        changed=request.Clone();changed.Context.ActorSubject+="-changed";changes.Add(changed);
                        changed=request.Clone();changed.Context.TraceId+="-changed";changes.Add(changed);
                        changed=request.Clone();changed.After=Guid.NewGuid().ToString("N");changed.ExpectedVersion=new('A',64);changes.Add(changed);
                        changed=request.Clone();changed.ExpectedVersion=new('A',64);changes.Add(changed);
                        foreach(var altered in changes)
                            await Reject(async()=>await client.ListAsync(altered,Headers("List",request),Until(),lifetime.Token),StatusCode.PermissionDenied);
                        changed=request.Clone();changed.Take=0;
                        await Reject(async()=>await client.ListAsync(changed,Headers("List",changed),Until(),lifetime.Token),StatusCode.InvalidArgument);
                        changed=request.Clone();changed.After=Guid.NewGuid().ToString("N");changed.ExpectedVersion="";
                        await Reject(async()=>await client.ListAsync(changed,Headers("List",changed),Until(),lifetime.Token),StatusCode.InvalidArgument);
                        changed=request.Clone();changed.Context.ActorSubject="";
                        await Reject(async()=>await client.ListAsync(changed,Headers("List",changed),Until(),lifetime.Token),StatusCode.InvalidArgument);
                        changed=request.Clone();changed.Context.JobId=Guid.NewGuid().ToString("N");
                        await Reject(async()=>await client.ListAsync(changed,Headers("List",changed),Until(),lifetime.Token),StatusCode.InvalidArgument);
                        changed=request.Clone();changed.Location.ContainerId=core;
                        await Reject(async()=>await client.ListAsync(changed,Headers("List",changed),Until(),lifetime.Token),StatusCode.PermissionDenied);
                        changed=request.Clone();changed.ExpectedVersion=new('0',64);
                        await Reject(async()=>await client.ListAsync(changed,Headers("List",changed),Until(),lifetime.Token),StatusCode.Aborted);
                    }
                    var authority=Headers("List",request);
                    var page=await client.ListAsync(request,authority,Until(),lifetime.Token);
                    Require(page.Location.Equals(request.Location) && page.HasComplete && page.HasScannedCount && page.HasInvalidCount &&
                        page.Entries.Count<=request.Take && page.ScannedCount<=200 && page.InvalidCount<=page.ScannedCount &&
                        Upper(page.Version) && (request.ExpectedVersion.Length==0 || page.Version==request.ExpectedVersion) && page.Complete==(page.NextAfter.Length==0));
                    var previous=request.After;
                    foreach(var entry in page.Entries)
                    {
                        Summary(entry);Require(string.CompareOrdinal(entry.CaptureId,previous)>0);previous=entry.CaptureId;
                    }
                    if(!page.Complete) Require(string.CompareOrdinal(page.NextAfter,request.After)>0 && (page.Entries.Count==0 || string.CompareOrdinal(page.NextAfter,previous)>=0));
                    if(input.GetProperty("boundaries").GetBoolean())
                        await Reject(async()=>await client.ListAsync(request,authority,Until(),lifetime.Token),StatusCode.PermissionDenied);
                    await Emit(new{kind="page",page=formatter.Format(page),boundaryRefusals=refusals});continue;
                }
                if(action=="read")
                {
                    Fields(input,"action","path","captureId","historyDigest","offset","take","boundaries");
                    var request=new ContainerRecursiveAclHistoryReadRequest{Location=Location(path),CaptureId=Text(input,"captureId"),ExpectedHistoryDigest=Text(input,"historyDigest"),Offset=input.GetProperty("offset").GetUInt32(),Take=input.GetProperty("take").GetUInt32(),Context=Context()};
                    if(input.GetProperty("boundaries").GetBoolean())
                    {
                        await Reject(async()=>await client.ReadAsync(request,deadline:Until(),cancellationToken:lifetime.Token),StatusCode.PermissionDenied);
                        await Reject(async()=>await client.ReadAsync(request,Headers("Read",request,"acl-domain"),Until(),lifetime.Token),StatusCode.PermissionDenied);
                        await Reject(async()=>await client.ReadAsync(request,Headers("List",request),Until(),lifetime.Token),StatusCode.PermissionDenied);
                        var changed=request.Clone();changed.CaptureId=Guid.NewGuid().ToString("N");
                        await Reject(async()=>await client.ReadAsync(changed,Headers("Read",request),Until(),lifetime.Token),StatusCode.PermissionDenied);
                        changed=request.Clone();changed.ExpectedHistoryDigest=new('0',64);
                        await Reject(async()=>await client.ReadAsync(changed,Headers("Read",request),Until(),lifetime.Token),StatusCode.PermissionDenied);
                        await Reject(async()=>await client.ReadAsync(changed,Headers("Read",changed),Until(),lifetime.Token),StatusCode.Aborted);
                        changed=request.Clone();changed.CaptureId=Guid.NewGuid().ToString("N");
                        await Reject(async()=>await client.ReadAsync(changed,Headers("Read",changed),Until(),lifetime.Token),StatusCode.NotFound);
                        changed=request.Clone();changed.Offset++;
                        await Reject(async()=>await client.ReadAsync(changed,Headers("Read",request),Until(),lifetime.Token),StatusCode.PermissionDenied);
                        changed=request.Clone();changed.Take=request.Take==1?2U:1U;
                        await Reject(async()=>await client.ReadAsync(changed,Headers("Read",request),Until(),lifetime.Token),StatusCode.PermissionDenied);
                        changed=request.Clone();changed.ClearOffset();
                        await Reject(async()=>await client.ReadAsync(changed,Headers("Read",request),Until(),lifetime.Token),StatusCode.PermissionDenied);
                        changed=request.Clone();changed.ClearOffset();
                        await Reject(async()=>await client.ReadAsync(changed,Headers("Read",changed),Until(),lifetime.Token),StatusCode.InvalidArgument);
                        changed=request.Clone();changed.Take=0;
                        await Reject(async()=>await client.ReadAsync(changed,Headers("Read",changed),Until(),lifetime.Token),StatusCode.InvalidArgument);
                        changed=request.Clone();changed.Take=51;
                        await Reject(async()=>await client.ReadAsync(changed,Headers("Read",changed),Until(),lifetime.Token),StatusCode.InvalidArgument);
                        changed=request.Clone();changed.Offset=10000;
                        await Reject(async()=>await client.ReadAsync(changed,Headers("Read",changed),Until(),lifetime.Token),StatusCode.InvalidArgument);
                        changed=request.Clone();changed.Location.Path="work/original.bin";
                        await Reject(async()=>await client.ReadAsync(changed,Headers("Read",changed),Until(),lifetime.Token),StatusCode.PermissionDenied);
                        changed=request.Clone();changed.Location.Path="work/history-missing";
                        await Reject(async()=>await client.ReadAsync(changed,Headers("Read",changed),Until(),lifetime.Token),StatusCode.NotFound);
                        foreach(var variant in new[]{"recursive-domain","single-history-domain"})
                            await Reject(async()=>await client.ReadAsync(request,Headers("Read",request,variant),Until(),lifetime.Token),StatusCode.PermissionDenied);
                    }
                    var authority=Headers("Read",request);var snapshot=await client.ReadAsync(request,authority,Until(),lifetime.Token);
                    Require(snapshot.Location.Equals(request.Location) && snapshot.Summary is not null && snapshot.Summary.CaptureId==request.CaptureId && snapshot.Summary.HistoryDigest==request.ExpectedHistoryDigest);
                    Summary(snapshot.Summary!);
                    Require(snapshot.HasOffset && snapshot.Offset==request.Offset && snapshot.HasComplete && snapshot.Entries.Count>0 && snapshot.Entries.Count<=request.Take && snapshot.CalculateSize()<=128*1024);
                    var end=(long)snapshot.Offset+snapshot.Entries.Count;
                    Require(end<=snapshot.Summary!.EntryCount && snapshot.Complete==(end==snapshot.Summary.EntryCount) && snapshot.HasNextOffset!=snapshot.Complete && (!snapshot.HasNextOffset || snapshot.NextOffset==end));
                    foreach(var entry in snapshot.Entries)
                    {
                        Require(entry.HasOriginalMode && entry.OriginalMode<=511 && entry.Kind is "File" or "Directory" && entry.AccessAcl.Count is >=3 and <=64 && entry.DefaultAcl.Count<=64);
                        foreach(var acl in entry.AccessAcl.Concat(entry.DefaultAcl)) Require(acl.HasPermissions && acl.Permissions<=7 && (acl.Tag is "user" or "group")==acl.HasId);
                    }
                    if(input.GetProperty("boundaries").GetBoolean())
                        await Reject(async()=>await client.ReadAsync(request,authority,Until(),lifetime.Token),StatusCode.PermissionDenied);
                    await Emit(new{kind="snapshot",snapshot=formatter.Format(snapshot),boundaryRefusals=refusals});continue;
                }
                throw new InvalidOperationException();
            }
            throw new InvalidOperationException();
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
    private static void Summary(ContainerRecursiveAclHistorySummary entry)=>Require(Lower(entry.CaptureId,32) && Upper(entry.HistoryDigest) && Upper(entry.ManifestDigest) &&
        entry.HasCreatedAtUnixMs && entry.CreatedAtUnixMs>=0 && entry.HasEntryCount && entry.HasFileCount && entry.HasDirectoryCount && entry.HasChangeCount &&
        entry.EntryCount is >=1 and <=10000 && entry.DirectoryCount is >=1 and <=10000 && entry.FileCount<=10000 && entry.EntryCount==entry.FileCount+entry.DirectoryCount &&
        entry.ChangeCount<=entry.EntryCount && entry.ChangedAclType is "access" or "default" && (entry.ChangedAclType!="default" || entry.ChangeCount<=entry.DirectoryCount));
    private static void Fields(JsonElement value,params string[] names)
    {Require(value.ValueKind==JsonValueKind.Object);var actual=value.EnumerateObject().Select(p=>p.Name).ToArray();Require(actual.Length==names.Length && actual.ToHashSet(StringComparer.Ordinal).SetEquals(names));}
    private static string Text(JsonElement value,string name)=>value.GetProperty(name).GetString()??throw new InvalidOperationException();
    private static bool Lower(string value,int size)=>value.Length==size && value.All(char.IsAsciiHexDigitLower);
    private static bool Upper(string value)=>value.Length==64 && value.All(c=>char.IsAsciiDigit(c)||c is >= 'A' and <= 'F');
    private static void Require(bool condition) {if(!condition)throw new InvalidOperationException();}
    private static async Task Denied(Func<Task> call,StatusCode expected)
    {try{await call();}catch(RpcException error)when(error.StatusCode==expected){return;}throw new InvalidOperationException();}
}
