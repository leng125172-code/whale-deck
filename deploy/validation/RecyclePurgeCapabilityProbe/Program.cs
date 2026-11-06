using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Application.Models;
using WhaleDeck.Infrastructure.Agent;
using WhaleDeck.Contracts.Agent.V1;
using WhaleDeck.Contracts;

// Read-only current-host negative probe. It never reads a capability key and
// its HTTP handler refuses to forward any operation, even on a newer Agent.
if(BuildIdentity.TryPrint(args,Assembly.GetExecutingAssembly()))return 0;
if(args is not ["old-host-refusal-only"])return 2;
var missing=Path.Combine(Path.GetTempPath(),"wd-purge-no-key-"+Guid.NewGuid().ToString("N"));
using var gateway=new AgentGateway(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Agent:CapabilityKeyFile"]=missing}).Build());
using var forward=new SocketsHttpHandler{UseProxy=false,ConnectCallback=async(_,token)=>{var socket=new Socket(AddressFamily.Unix,SocketType.Stream,ProtocolType.Unspecified);try{await socket.ConnectAsync(new UnixDomainSocketEndPoint("/run/whaledeck/agent.sock"),token);return new NetworkStream(socket,true);}catch{socket.Dispose();throw;}}};
using var guard=new ReadOnlyHandler(forward);using var channel=GrpcChannel.ForAddress("http://localhost",new(){HttpHandler=guard,DisposeHttpClient=false});
var field=typeof(AgentGateway).GetField("_channel",BindingFlags.Instance|BindingFlags.NonPublic)!;((GrpcChannel)field.GetValue(gateway)!).Dispose();field.SetValue(gateway,channel);typeof(AgentGateway).GetField("_agent",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(gateway,new AgentService.AgentServiceClient(channel));
var phase="capabilities";
try
{
    var capabilities=await gateway.GetCapabilitiesAsync(default);
    if(capabilities.ProtocolMajor!=1||capabilities.ProtocolMinor!=57||capabilities.Capabilities.Contains("docker.files.recycle.purge"))throw new InvalidOperationException("CURRENT_BASELINE_IS_NOT_OLD_1_57");
    var location=new ContainerFileLocationDto(new('a',64),"777:2026-10-27T01:02:03.123456789Z","work/synthetic.bin");
    var source=new ContainerRecyclePlanDto(1,new(location,new('9',64)),new('A',64),new(1,2),new('B',64),1000,1000,new('C',64),new(3,4,0x81A0,1000,1000,1,7,10),11,new('8',64),[]);
    var command=new ContainerRecyclePurgeCommandDto(new(Guid.ParseExact("aa0a8865343e49bdb01a72680131bf39","N"),new('D',64),source),new('7',64),"Originals");
    phase="gate";
    try{await gateway.PlanContainerRecyclePurgeAsync(new("synthetic-admin",null,[],new('F',64),"synthetic-trace",null),command,default);throw new InvalidOperationException("OLD_HOST_ACCEPTED_NEW_PURGE");}
    catch(RpcException error)when(error.StatusCode==StatusCode.Unimplemented&&error.Status.Detail=="CONTAINER_RECYCLE_PURGE_CAPABILITY_UNAVAILABLE"){}
    if(guard.CapabilityFrames!=2||guard.BlockedOperationFrames!=0)throw new InvalidOperationException("UNEXPECTED_FRAME_COUNT");
    Console.WriteLine(JsonSerializer.Serialize(new{actualAgentMajor=capabilities.ProtocolMajor,actualAgentMinor=capabilities.ProtocolMinor,newPurgeRefused=true,capabilityFrames=guard.CapabilityFrames,operationFrames=0,capabilityKeyRead=false,probeRevision=BuildIdentity.FromAssembly(Assembly.GetExecutingAssembly()).SourceRevision,sdkRevision=BuildIdentity.FromAssembly(typeof(AgentGateway).Assembly).SourceRevision,contractRevision=BuildIdentity.FromAssembly(typeof(ContainerRecyclePurgeBinding).Assembly).SourceRevision}));return 0;
}
catch(Exception error)
{
    var rpc=error as RpcException;var detail=rpc?.Status.Detail;
    var safeCode=detail is not null&&System.Text.RegularExpressions.Regex.IsMatch(detail,"^[A-Z0-9_]{1,100}$")?detail:null;
    Console.Error.WriteLine(JsonSerializer.Serialize(new{oldHostPurgeProbe="failed",phase,type=error.GetType().Name,rpcStatus=rpc?.StatusCode.ToString(),code=safeCode,capabilityFrames=guard.CapabilityFrames,blockedOperationFrames=guard.BlockedOperationFrames}));return 1;
}

sealed class ReadOnlyHandler(HttpMessageHandler inner):DelegatingHandler(inner)
{
    public int CapabilityFrames{get;private set;}
    public int BlockedOperationFrames{get;private set;}
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
    {
        if(request.RequestUri!.AbsolutePath!="/whaledeck.agent.v1.AgentService/GetCapabilities"){BlockedOperationFrames++;throw new InvalidOperationException("OPERATION_FORWARDING_DISABLED");}
        CapabilityFrames++;return base.SendAsync(request,cancellationToken);
    }
}
