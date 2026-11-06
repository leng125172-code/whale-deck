using System.Text.Json;
using Grpc.Core;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Infrastructure.Agent;

// Read-only compatibility proof. Never calls any archive Plan/write/read RPC
// and never accepts an existing credential file. Positive probes are separate.
try
{
    if(args.Length!=1||!Path.IsPathFullyQualified(args[0])||Path.GetFileName(args[0])!="missing-capability.key"||File.Exists(args[0])||Directory.Exists(args[0]))throw new ArgumentException("INVALID_MISSING_KEY_FIXTURE");
    var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Agent:SocketPath"]="/run/whaledeck/agent.sock",["Agent:CapabilityKeyFile"]=args[0]}).Build();
    using var gateway=new AgentGateway(config);using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));
    var caps=await gateway.GetCapabilitiesAsync(timeout.Token);
    if(caps.ProtocolMajor!=1||caps.ProtocolMinor>=59||caps.Capabilities.Contains("docker.files.recycle.archive"))throw new InvalidOperationException("EXPECTED_OLD_AGENT_ONLY");
    try{await gateway.RequireContainerRecycleArchiveAsync(timeout.Token);throw new InvalidOperationException("OLD_AGENT_ACCEPTED_ARCHIVE");}
    catch(RpcException e)when(e.StatusCode==StatusCode.Unimplemented&&e.Status.Detail=="CONTAINER_RECYCLE_ARCHIVE_CAPABILITY_UNAVAILABLE"){}
    if(File.Exists(args[0]))throw new InvalidOperationException("MISSING_KEY_FIXTURE_CHANGED");
    Console.WriteLine(JsonSerializer.Serialize(new{oldProtocolRejected=true,protocolMajor=caps.ProtocolMajor,protocolMinor=caps.ProtocolMinor,archiveCapabilityAbsent=true,sdkRequireRejected=true,realCredentialRead=false,archiveOperationMethodsInvoked=0}));return 0;
}
catch(Exception e){Console.WriteLine(JsonSerializer.Serialize(new{oldProtocolRejected=false,errorType=e.GetType().Name}));return 1;}
