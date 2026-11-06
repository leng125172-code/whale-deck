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

/// <summary>Parent-paced, fixed-resource probe. Each reply must be independently checked before the next mutation.</summary>
internal static class ContainerRecycleArchiveProbe
{
    public static string Phase { get; private set; } = "startup";
    public static string FailureCode { get; private set; } = "";
    public static object? CapabilityObservation { get; private set; }
    public static async Task RunAsync()
    {
        Require(OperatingSystem.IsLinux());
        using var lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(12));
        var line = await Console.In.ReadLineAsync(lifetime.Token);
        Require(line is { Length: > 0 } && Encoding.UTF8.GetByteCount(line) <= 8192);
        using var header = JsonDocument.Parse(line!); var input = header.RootElement;
        Fields(input, "revision", "probeRevision", "containerId", "instanceId", "coreId");
        var revision = Text(input, "revision"); var probeRevision = Text(input, "probeRevision");
        var id = Text(input, "containerId"); var instance = Text(input, "instanceId"); var core = Text(input, "coreId");
        Require(Lower(revision, 40) && Lower(probeRevision, 40) && Lower(id, 64) && Lower(core, 64) && id != core && instance.Length is > 20 and < 64);
        Require(Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.EndsWith("+" + probeRevision, StringComparison.Ordinal));
        using var handler = new SocketsHttpHandler { UseProxy = false, ConnectCallback = async (_, token) =>
        {
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try { await socket.ConnectAsync(new UnixDomainSocketEndPoint("/run/whaledeck/agent.sock"), token); return new NetworkStream(socket, true); }
            catch { socket.Dispose(); throw; }
        }};
        using var channel = GrpcChannel.ForAddress("http://localhost", new() { HttpHandler = handler });
        Phase = "capabilities";
        var caps = await new AgentService.AgentServiceClient(channel).GetCapabilitiesAsync(new(), deadline: DateTime.UtcNow.AddSeconds(10), cancellationToken: lifetime.Token);
        CapabilityObservation = new { protocolMajor = caps.ProtocolMajor, protocolMinor = caps.ProtocolMinor,
            sourceCapability = caps.Capabilities.Contains("docker.files.recycle"), restoreCapability = caps.Capabilities.Contains("docker.files.recycle.restore"), purgeCapability = caps.Capabilities.Contains("docker.files.recycle.purge"), archiveCapability = caps.Capabilities.Contains("docker.files.recycle.archive") };
        // An older installation must refuse before reading the signing key or sending any operation.
        if (!(caps.ProtocolMajor == 1 && caps.ProtocolMinor >= 59 && caps.Capabilities.Contains("docker.files.recycle.archive") && caps.Capabilities.Contains("docker.files.recycle.purge") && caps.Capabilities.Contains("docker.files.recycle.restore") && caps.Capabilities.Contains("docker.files.recycle")))
        {
            FailureCode = "CONTAINER_RECYCLE_ARCHIVE_CAPABILITY_UNAVAILABLE";
            throw new InvalidOperationException();
        }
        var key = await File.ReadAllBytesAsync("/etc/whaledeck/agent-capability.key", lifetime.Token);
        try
        {
            Require(key.Length == 32);
            var sourceClient = new ContainerRecycleService.ContainerRecycleServiceClient(channel);
            var client = new ContainerRecyclePurgeService.ContainerRecyclePurgeServiceClient(channel);
            var archiveClient = new ContainerRecycleArchiveService.ContainerRecycleArchiveServiceClient(channel);
            var archivePlans = new Dictionary<string, ContainerRecycleArchivePlan>(StringComparer.Ordinal);
            var archiveOperations = new Dictionary<string, ContainerRecycleArchiveRequest>(StringComparer.Ordinal);
            var archiveStates = new Dictionary<string,string>(StringComparer.Ordinal);
            var formatter = new JsonFormatter(new JsonFormatter.Settings(formatDefaultValues: true));
            var sourcePlans = new Dictionary<string, ContainerRecyclePlan>(StringComparer.Ordinal);
            var sources = new Dictionary<string, ContainerRecycleRequest>(StringComparer.Ordinal);
            var plans = new Dictionary<string, ContainerRecyclePurgePlan>(StringComparer.Ordinal);
            var operations = new Dictionary<string, ContainerRecyclePurgeRequest>(StringComparer.Ordinal);
            DateTime Until() => DateTime.UtcNow.AddSeconds(120);
            RequestContext Context() => new() { ActorSubject = "installed-recycle-archive-probe", TraceId = "owned-recycle-archive-validation" };
            Metadata Headers(string action, IMessage request, bool source = false, string variant = "", bool archive = false)
            {
                var hash = archive ? ContainerRecycleArchiveBinding.Hash(request) : source ? ContainerRecycleBinding.Hash(request) : ContainerRecyclePurgeBinding.Hash(request);
                var method = (archive ? ContainerRecycleArchiveBinding.Service : source ? ContainerRecycleBinding.Service : ContainerRecyclePurgeBinding.Service) + action;
                var expires = DateTimeOffset.UtcNow.AddSeconds(variant == "expired" ? -60 : variant == "future" ? 180 : 45).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
                var nonce = Guid.NewGuid().ToString("N");
                var payload = variant switch {
                    "recycle-domain" => ContainerRecycleBinding.Payload(expires, nonce, method, hash),
                    "purge-domain" => ContainerRecyclePurgeBinding.Payload(expires, nonce, method, hash),
                    "archive-domain" => ContainerRecycleArchiveBinding.Payload(expires, nonce, method, hash),
                    "restore-domain" => ContainerRecycleRestoreBinding.Payload(expires, nonce, method, hash),
                    "permission-domain" => ContainerPermissionBinding.Payload(expires, nonce, method, hash),
                    "generic-domain" => $"{expires}.{nonce}.{method}",
                    _ => archive ? ContainerRecycleArchiveBinding.Payload(expires, nonce, method, hash) : source ? ContainerRecycleBinding.Payload(expires, nonce, method, hash) : ContainerRecyclePurgeBinding.Payload(expires, nonce, method, hash) };
                return new() { { "x-whaledeck-capability", expires + "." + nonce + "." + Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload))) },
                    { archive ? ContainerRecycleArchiveBinding.Header : source ? ContainerRecycleBinding.Header : ContainerRecyclePurgeBinding.Header, hash } };
            }
            async Task Emit(object value) { await Console.Out.WriteLineAsync(JsonSerializer.Serialize(value)); await Console.Out.FlushAsync(); }
            await Emit(new { kind = "ready", revision, probeRevision, protocolMinor = caps.ProtocolMinor });
            for (var count = 0; count < 64; count++)
            {
                line = await Console.In.ReadLineAsync(lifetime.Token);
                Require(line is { Length: > 0 } && Encoding.UTF8.GetByteCount(line) <= 65536);
                using var packet = JsonDocument.Parse(line!); input = packet.RootElement; var action = Text(input, "action");
                Require(action is "quit" or "source-plan" or "source-apply" or "source-inspect" or "plan" or "reserve" or "apply" or "inspect" or "discard" or "reject" or "archive-plan" or "archive-reserve" or "archive-apply" or "archive-inspect" or "archive-discard" or "archive-reject");Phase = action;
                if (action == "quit") { Fields(input, "action"); await Emit(new { kind = "done" }); return; }
                var refusals = 0;
                async Task Reject(Func<Task> call, StatusCode expected)
                { Phase = action + "-refusal-" + (refusals + 1).ToString(CultureInfo.InvariantCulture); await Denied(call, expected); refusals++; }
                if (action == "source-plan")
                {
                    Fields(input, "action", "version"); Require(Upper(Text(input, "version")));
                    var request = new ContainerRecyclePlanRequest { Command = new() { Location = new() { ContainerId = id, InstanceId = instance, Path = "work/recycle-rpc.bin" }, ExpectedVersion = Text(input, "version") }, Context = Context() };
                    var plan = await sourceClient.PlanAsync(request, Headers("Plan", request, source: true), Until(), lifetime.Token);
                    ContainerRecyclePurgeWire.RequireKnown(plan);
                    Require(plan.Command.Equals(request.Command) && plan.SchemaVersion == 1 && plan.OwnerUid == 1000 && plan.OwnerGid == 1000 && Upper(plan.Fingerprint) && Upper(plan.Sha256));
                    sourcePlans[plan.Fingerprint] = plan.Clone();
                    await Emit(new { kind = "source-plan", plan = formatter.Format(plan) }); continue;
                }
                if (action is "source-apply" or "source-inspect")
                {
                    Fields(input, "action", "operationId", "plan"); var sourceId = Text(input, "operationId"); Identity(sourceId);
                    var supplied = JsonParser.Default.Parse<ContainerRecyclePlan>(Text(input, "plan"));
                    Require(sourcePlans.TryGetValue(supplied.Fingerprint, out var approved) && supplied.Equals(approved));
                    if (!sources.TryGetValue(sourceId, out var request))
                    {
                        Require(action == "source-apply"); var context = Context(); context.JobId = sourceId; context.IdempotencyKey = Guid.NewGuid().ToString("N");
                        request = new() { OperationId = sourceId, Owner = new('B', 64), Plan = supplied.Clone(), Context = context }; sources.Add(sourceId, request);
                    }
                    Require(request.Plan.Equals(supplied));
                    var result = action == "source-apply" ? await sourceClient.ApplyAsync(request, Headers("Apply", request, source: true), Until(), lifetime.Token) :
                        await sourceClient.InspectAsync(request, Headers("Inspect", request, source: true), Until(), lifetime.Token);
                    ContainerRecyclePurgeWire.RequireKnown(result);
                    Require(result.OperationId == sourceId && result.ContainerId == id && result.InstanceId == instance && result.Fingerprint == supplied.Fingerprint && result.TargetProof == supplied.TargetProof);
                    Require(result.State is "Trashed" or "Restored" or "Aborted" or "Unknown" or "Purged");
                    await Emit(new { kind = "source-receipt", action, operationId = sourceId, jobId = request.Context.JobId, result = formatter.Format(result) }); continue;
                }
                if (action == "archive-plan")
                {
                    Fields(input,"action","purgeOperationId","boundaries");Require(operations.TryGetValue(Text(input,"purgeOperationId"),out var purged));
                    var observedPurge=await client.InspectAsync(purged!,Headers("Inspect",purged!),Until(),lifetime.Token);ContainerRecyclePurgeWire.Receipt(purged!,observedPurge);
                    Require(observedPurge.State=="Completed"&&observedPurge.HasPurgeApplied&&observedPurge.PurgeApplied&&observedPurge.OwnedFence);
                    Require(sources.TryGetValue(purged!.Plan.Command.Source.OperationId,out var original)&&original.Plan.Equals(purged.Plan.Command.Source.Plan));
                    var observed=await sourceClient.InspectAsync(original!,Headers("Inspect",original!,source:true),Until(),lifetime.Token);
                    Require(observed.State=="Purged"&&observed.OperationId==original!.OperationId&&observed.Fingerprint==original.Plan.Fingerprint&&observed.TargetProof==original.Plan.TargetProof&&observed.ContainerId==id&&observed.InstanceId==instance&&observed.HistoryVerified&&observed.HistoryAvailable&&observed.HistoryDigest==purged.Plan.Command.HistoryDigest);
                    var request=new ContainerRecycleArchivePlanRequest{Command=new(){Purge=new(){OperationId=purged.OperationId,Owner=purged.Owner,Plan=purged.Plan.Clone()},HistoryDigest=observed.HistoryDigest,Mode="ArchiveCopy"},Context=Context()};
                    if(input.GetProperty("boundaries").GetBoolean())
                    {
                        await Reject(async()=>await archiveClient.PlanAsync(request,deadline:Until(),cancellationToken:lifetime.Token),StatusCode.PermissionDenied);
                        foreach(var variant in new[]{"recycle-domain","purge-domain","restore-domain","generic-domain","expired","future"})await Reject(async()=>await archiveClient.PlanAsync(request,Headers("Plan",request,variant:variant,archive:true),Until(),lifetime.Token),StatusCode.PermissionDenied);
                        await Reject(async()=>await archiveClient.PlanAsync(request,Headers("Reserve",request,archive:true),Until(),lifetime.Token),StatusCode.PermissionDenied);
                        foreach(var field in new[]{"purge-id","purge-owner","source-path","source-bytes","source-attribute","archive","store","history","actor","trace"})
                        {
                            var changed=request.Clone();switch(field){case "purge-id":changed.Command.Purge.OperationId=Guid.NewGuid().ToString("N");break;case "purge-owner":changed.Command.Purge.Owner=new('E',64);break;case "source-path":changed.Command.Purge.Plan.Command.Source.Plan.Command.Location.Path="work/other.bin";break;case "source-bytes":changed.Command.Purge.Plan.Command.Source.Plan.Sha256=new('E',64);break;case "source-attribute":changed.Command.Purge.Plan.Command.Source.Plan.Attributes[0].Value=ByteString.CopyFrom([1,2,3]);break;case "archive":changed.Command.Purge.Plan.ArchiveIdentity.Inode++;break;case "store":changed.Command.Purge.Plan.StoreIdentity.Inode++;break;case "history":changed.Command.HistoryDigest=new('E',64);break;case "actor":changed.Context.ActorSubject+="-changed";break;default:changed.Context.TraceId+="-changed";break;}
                            Require(ContainerRecycleArchiveBinding.Hash(changed)!=ContainerRecycleArchiveBinding.Hash(request));await Reject(async()=>await archiveClient.PlanAsync(changed,Headers("Plan",request,archive:true),Until(),lifetime.Token),StatusCode.PermissionDenied);
                        }
                        var bad=request.Clone();bad.Command.Mode="All";await Reject(async()=>await archiveClient.PlanAsync(bad,Headers("Plan",bad,archive:true),Until(),lifetime.Token),StatusCode.InvalidArgument);
                        bad=request.Clone();bad.Command.Purge.OperationId=original!.OperationId;await Reject(async()=>await archiveClient.PlanAsync(bad,Headers("Plan",bad,archive:true),Until(),lifetime.Token),StatusCode.InvalidArgument);
                        bad=request.Clone();bad.Command.HistoryDigest="";await Reject(async()=>await archiveClient.PlanAsync(bad,Headers("Plan",bad,archive:true),Until(),lifetime.Token),StatusCode.InvalidArgument);
                    }
                    Phase="archive-plan-value";var authority=Headers("Plan",request,archive:true);var plan=await archiveClient.PlanAsync(request,authority,Until(),lifetime.Token);ContainerRecycleArchiveWire.RequireKnown(plan);
                    Require(plan.Command.Equals(request.Command)&&plan.SchemaVersion==1&&plan.Fingerprint==ContainerRecycleArchiveWire.Fingerprint(plan)&&plan.TargetProof==purged.Plan.TargetProof&&plan.ParentIdentity.Equals(purged.Plan.ParentIdentity)&&plan.OwnerUid==1000&&plan.OwnerGid==1000);
                    Require((plan.PrivateIdentity is null||plan.PrivateIdentity.Equals(purged.Plan.PrivateIdentity))&&plan.DeletedIdentity.Equals(purged.Plan.RetainedIdentity)&&plan.DeletedChangedAtUnixNs==purged.Plan.RetainedChangedAtUnixNs&&plan.ArchiveIdentity.Equals(purged.Plan.ArchiveIdentity)&&plan.ArchiveChangedAtUnixNs==purged.Plan.ArchiveChangedAtUnixNs&&plan.ArchiveBytes==purged.Plan.ArchiveBytes&&plan.ArchiveSha256==purged.Plan.ArchiveSha256&&plan.StoreIdentity.Equals(purged.Plan.StoreIdentity)&&plan.StorageHash==purged.Plan.StorageHash);
                    foreach(var hash in new[]{plan.ParentVersion,plan.SourceRecordDigest,plan.PurgeRecordDigest,plan.PurgeMarkerDigest,plan.HistoryMetadataDigest})Require(Upper(hash));archivePlans[plan.Fingerprint]=plan.Clone();
                    if(input.GetProperty("boundaries").GetBoolean())await Reject(async()=>await archiveClient.PlanAsync(request,authority,Until(),lifetime.Token),StatusCode.PermissionDenied);
                    await Emit(new{kind="archive-plan",plan=formatter.Format(plan),boundaryRefusals=refusals});continue;
                }
                if(action.StartsWith("archive-",StringComparison.Ordinal))
                {
                    Require(action is "archive-reserve" or "archive-apply" or "archive-inspect" or "archive-discard" or "archive-reject");Fields(input,"action","operationId","plan");var archiveId=Text(input,"operationId");Identity(archiveId);
                    var supplied=JsonParser.Default.Parse<ContainerRecycleArchivePlan>(Text(input,"plan"));Require(archivePlans.TryGetValue(supplied.Fingerprint,out var approvedArchive)&&supplied.Equals(approvedArchive)&&archiveId!=supplied.Command.Purge.OperationId&&archiveId!=supplied.Command.Purge.Plan.Command.Source.OperationId);
                    if(!archiveOperations.TryGetValue(archiveId,out var archiveOperation))
                    {
                        Require(action is "archive-reserve" or "archive-discard" or "archive-reject");var context=Context();context.JobId=archiveId;context.IdempotencyKey=Guid.NewGuid().ToString("N");archiveOperation=new(){OperationId=archiveId,Owner=new('D',64),Plan=supplied.Clone(),Context=context};archiveOperations.Add(archiveId,archiveOperation);archiveStates.Add(archiveId,"unreserved");
                    }
                    Require(archiveOperation.Plan.Equals(supplied));
                    if(action=="archive-reject")
                    {
                        Require(archiveStates[archiveId]=="unreserved");
                        foreach(var field in new[]{"operation-job","owner","purge-id","purge-owner","source-owner","source-attribute","private","deleted-time","parent-version","source-record","purge-record","purge-marker","history-meta","storage","store","archive","archive-time","actor","trace","key"})
                        {
                            var changed=archiveOperation.Clone();var old=changed.Plan.Command.Purge.Plan;
                            switch(field){case "operation-job":changed.OperationId=changed.Context.JobId=Guid.NewGuid().ToString("N");break;case "owner":changed.Owner=new('E',64);break;case "purge-id":changed.Plan.Command.Purge.OperationId=Guid.NewGuid().ToString("N");break;case "purge-owner":changed.Plan.Command.Purge.Owner=new('E',64);break;case "source-owner":old.Command.Source.Owner=new('E',64);break;case "source-attribute":old.Command.Source.Plan.Attributes[0].Value=ByteString.CopyFrom([1,2,3]);break;case "private":changed.Plan.PrivateIdentity=changed.Plan.PrivateIdentity is null?old.PrivateIdentity.Clone():null;break;case "deleted-time":old.RetainedChangedAtUnixNs++;changed.Plan.DeletedChangedAtUnixNs=old.RetainedChangedAtUnixNs;break;case "parent-version":changed.Plan.ParentVersion=new('E',64);break;case "source-record":changed.Plan.SourceRecordDigest=new('E',64);break;case "purge-record":changed.Plan.PurgeRecordDigest=new('E',64);break;case "purge-marker":changed.Plan.PurgeMarkerDigest=new('E',64);break;case "history-meta":changed.Plan.HistoryMetadataDigest=new('E',64);break;case "storage":old.StorageHash=new('E',64);changed.Plan.StorageHash=old.StorageHash;break;case "store":old.StoreIdentity.Inode++;changed.Plan.StoreIdentity=old.StoreIdentity.Clone();break;case "archive":old.ArchiveIdentity.Inode++;changed.Plan.ArchiveIdentity=old.ArchiveIdentity.Clone();break;case "archive-time":old.ArchiveChangedAtUnixNs++;changed.Plan.ArchiveChangedAtUnixNs=old.ArchiveChangedAtUnixNs;break;case "actor":changed.Context.ActorSubject+="-changed";break;case "trace":changed.Context.TraceId+="-changed";break;default:changed.Context.IdempotencyKey=Guid.NewGuid().ToString("N");break;}
                            old.Fingerprint=ContainerRecyclePurgeWire.Fingerprint(old);changed.Plan.Fingerprint=ContainerRecycleArchiveWire.Fingerprint(changed.Plan);Require(ContainerRecycleArchiveBinding.Hash(changed)!=ContainerRecycleArchiveBinding.Hash(archiveOperation));
                            await Reject(async()=>await archiveClient.ApplyAsync(changed,Headers("Apply",archiveOperation,archive:true),Until(),lifetime.Token),StatusCode.PermissionDenied);
                        }
                        foreach(var variant in new[]{"recycle-domain","purge-domain","restore-domain","generic-domain"})await Reject(async()=>await archiveClient.ApplyAsync(archiveOperation,Headers("Apply",archiveOperation,variant:variant,archive:true),Until(),lifetime.Token),StatusCode.PermissionDenied);
                        await Reject(async()=>await archiveClient.ApplyAsync(archiveOperation,Headers("Discard",archiveOperation,archive:true),Until(),lifetime.Token),StatusCode.PermissionDenied);
                        foreach(var oldId in new[]{archiveOperation.Plan.Command.Purge.OperationId,archiveOperation.Plan.Command.Purge.Plan.Command.Source.OperationId}){var bad=archiveOperation.Clone();bad.OperationId=bad.Context.JobId=oldId;await Reject(async()=>await archiveClient.ApplyAsync(bad,Headers("Apply",bad,archive:true),Until(),lifetime.Token),StatusCode.InvalidArgument);}
                        var wrong=archiveOperation.Clone();wrong.Context.JobId=wrong.Plan.Command.Purge.OperationId;await Reject(async()=>await archiveClient.ApplyAsync(wrong,Headers("Apply",wrong,archive:true),Until(),lifetime.Token),StatusCode.InvalidArgument);
                        await Reject(async()=>await archiveClient.InspectAsync(archiveOperation,Headers("Inspect",archiveOperation,archive:true),Until(),lifetime.Token),StatusCode.NotFound);
                        await Reject(async()=>await archiveClient.ApplyAsync(archiveOperation,Headers("Apply",archiveOperation,archive:true),Until(),lifetime.Token),StatusCode.FailedPrecondition);
                        await Reject(async()=>await archiveClient.ApplyAsync(archiveOperation,deadline:Until(),cancellationToken:lifetime.Token),StatusCode.PermissionDenied);
                        await Emit(new{kind="archive-rejected",operationId=archiveId,refusals});continue;
                    }
                    if(action=="archive-apply")Require(archiveStates[archiveId] is "Reserved" or "Completed" or "Aborted" or "Unknown");
                    Phase=action+"-value";var method=action[8..];var archiveReceipt=method switch {"reserve"=>await archiveClient.ReserveAsync(archiveOperation,Headers("Reserve",archiveOperation,archive:true),Until(),lifetime.Token),"apply"=>await archiveClient.ApplyAsync(archiveOperation,Headers("Apply",archiveOperation,archive:true),Until(),lifetime.Token),"inspect"=>await archiveClient.InspectAsync(archiveOperation,Headers("Inspect",archiveOperation,archive:true),Until(),lifetime.Token),_=>await archiveClient.DiscardAsync(archiveOperation,Headers("Discard",archiveOperation,archive:true),Until(),lifetime.Token)};
                    ContainerRecycleArchiveWire.Receipt(archiveOperation,archiveReceipt);archiveStates[archiveId]=archiveReceipt.State;await Emit(new{kind="archive-receipt",action,operationId=archiveId,jobId=archiveOperation.Context.JobId,result=formatter.Format(archiveReceipt)});continue;
                }
                if (action == "plan")
                {
                    Fields(input, "action", "sourceOperationId", "boundaries");
                    Require(sources.TryGetValue(Text(input, "sourceOperationId"), out var original));
                    var observed = await sourceClient.InspectAsync(original, Headers("Inspect", original!, source: true), Until(), lifetime.Token);
                    Require(observed.OperationId == original!.OperationId && observed.Fingerprint == original.Plan.Fingerprint && observed.TargetProof == original.Plan.TargetProof && observed.ContainerId == id && observed.InstanceId == instance);
                    Require(observed.State == "Trashed" && observed.BindingCurrent && observed.HistoryVerified && observed.HistoryAvailable && observed.HasSourceRetained && observed.SourceRetained && observed.HasSourceRestored && !observed.SourceRestored && Upper(observed.HistoryDigest));
                    var request = new ContainerRecyclePurgePlanRequest { Command = new() { Source = new() { OperationId = original.OperationId, Owner = original.Owner, Plan = original.Plan.Clone() }, HistoryDigest = observed.HistoryDigest, Scope = "Originals" }, Context = Context() };
                    if (input.GetProperty("boundaries").GetBoolean())
                    {
                        await Reject(async () => await client.PlanAsync(request, deadline: Until(), cancellationToken: lifetime.Token), StatusCode.PermissionDenied);
                        foreach (var variant in new[] { "recycle-domain", "restore-domain", "permission-domain", "generic-domain", "expired", "future" })
                            await Reject(async () => await client.PlanAsync(request, Headers("Plan", request, variant: variant), Until(), lifetime.Token), StatusCode.PermissionDenied);
                        await Reject(async () => await client.PlanAsync(request, Headers("Reserve", request), Until(), lifetime.Token), StatusCode.PermissionDenied);
                        foreach (var field in new[] { "source", "owner", "history", "path", "bytes", "actor", "trace" })
                        {
                            var changed = request.Clone();
                            switch (field) { case "source": changed.Command.Source.OperationId = Guid.NewGuid().ToString("N"); break; case "owner": changed.Command.Source.Owner = new('E', 64); break;
                                case "history": changed.Command.HistoryDigest = new('E', 64); break; case "path": changed.Command.Source.Plan.Command.Location.Path = "work/other.bin"; break;
                                case "bytes": changed.Command.Source.Plan.Sha256 = new('E', 64); break; case "actor": changed.Context.ActorSubject += "-changed"; break; default: changed.Context.TraceId += "-changed"; break; }
                            Require(ContainerRecyclePurgeBinding.Hash(changed) != ContainerRecyclePurgeBinding.Hash(request));
                            await Reject(async () => await client.PlanAsync(changed, Headers("Plan", request), Until(), lifetime.Token), StatusCode.PermissionDenied);
                        }
                        var bad = request.Clone(); bad.Command.Scope = "All";
                        await Reject(async () => await client.PlanAsync(bad, Headers("Plan", bad), Until(), lifetime.Token), StatusCode.InvalidArgument);
                        bad = request.Clone(); bad.Command.HistoryDigest = "";
                        await Reject(async () => await client.PlanAsync(bad, Headers("Plan", bad), Until(), lifetime.Token), StatusCode.InvalidArgument);
                    }
                    Phase = "plan-value"; var authority = Headers("Plan", request);
                    var plan = await client.PlanAsync(request, authority, Until(), lifetime.Token);
                    ContainerRecyclePurgeWire.RequireKnown(plan);
                    Require(plan.Command.Equals(request.Command) && plan.SchemaVersion == 1 && plan.OwnerUid == 1000 && plan.OwnerGid == 1000 && plan.TargetProof == original.Plan.TargetProof && plan.ParentIdentity.Equals(original.Plan.ParentIdentity));
                    Require(plan.RetainedIdentity.Equals(original.Plan.SourceIdentity) && plan.RetainedChangedAtUnixNs >= original.Plan.SourceChangedAtUnixNs && plan.PrivateIdentity is { Inode: > 0 } && plan.Fingerprint == ContainerRecyclePurgeWire.Fingerprint(plan));
                    Require(plan.Command.Scope == "Originals" && Upper(plan.SourceRecordDigest) && Upper(plan.HistoryMetadataDigest) && Upper(plan.StorageHash));
                    Require(plan.StoreIdentity is { Inode: > 0 } && plan.ArchiveIdentity is { Inode: > 0, Mode: 33152, OwnerUid: 0, OwnerGid: 0, LinkCount: 1 } && plan.ArchiveBytes == original.Plan.SourceIdentity.SizeBytes && plan.ArchiveIdentity.SizeBytes == plan.ArchiveBytes && plan.ArchiveSha256 == original.Plan.Sha256 && plan.ArchiveChangedAtUnixNs >= 0 && plan.HistoryCreatedAtUnixMs is >= 0 and <= 253402300799999);
                    plans[plan.Fingerprint] = plan.Clone();
                    if (input.GetProperty("boundaries").GetBoolean()) await Reject(async () => await client.PlanAsync(request, authority, Until(), lifetime.Token), StatusCode.PermissionDenied);
                    await Emit(new { kind = "plan", plan = formatter.Format(plan), boundaryRefusals = refusals }); continue;
                }
                Require(action is "reserve" or "apply" or "inspect" or "discard" or "reject"); Fields(input, "action", "operationId", "plan");
                var operationId = Text(input, "operationId"); Identity(operationId);
                var purgePlan = JsonParser.Default.Parse<ContainerRecyclePurgePlan>(Text(input, "plan"));
                Require(plans.TryGetValue(purgePlan.Fingerprint, out var approvedPurge) && purgePlan.Equals(approvedPurge) && operationId != purgePlan.Command.Source.OperationId);
                if (!operations.TryGetValue(operationId, out var operation))
                {
                    Require(action is "reserve" or "discard" or "reject"); var context = Context(); context.JobId = operationId; context.IdempotencyKey = Guid.NewGuid().ToString("N");
                    operation = new() { OperationId = operationId, Owner = new('C', 64), Plan = purgePlan.Clone(), Context = context }; operations.Add(operationId, operation);
                }
                Require(operation.Plan.Equals(purgePlan));
                if (action == "reject")
                {
                    foreach (var field in new[] { "source", "history", "private", "ctime", "fingerprint", "archive", "archive-time", "store", "source-record", "history-meta", "storage", "created", "owner", "actor", "trace", "key" })
                    {
                        var changed = operation.Clone();
                        switch (field) { case "source": changed.Plan.Command.Source.Owner = new('E', 64); break; case "history": changed.Plan.Command.HistoryDigest = new('E', 64); break;
                            case "private": changed.Plan.PrivateIdentity.Inode++; break; case "ctime": changed.Plan.RetainedChangedAtUnixNs++; break; case "fingerprint": changed.Plan.ParentVersion = new('E', 64); break;
                            case "archive": changed.Plan.ArchiveIdentity.Inode++; break; case "archive-time": changed.Plan.ArchiveChangedAtUnixNs++; break;
                            case "store": changed.Plan.StoreIdentity.Inode++; break; case "source-record": changed.Plan.SourceRecordDigest = new('E', 64); break;
                            case "history-meta": changed.Plan.HistoryMetadataDigest = new('E', 64); break; case "storage": changed.Plan.StorageHash = new('E', 64); break; case "created": changed.Plan.HistoryCreatedAtUnixMs++; break;
                            case "owner": changed.Owner = new('E', 64); break; case "actor": changed.Context.ActorSubject += "-changed"; break; case "trace": changed.Context.TraceId += "-changed"; break; default: changed.Context.IdempotencyKey = Guid.NewGuid().ToString("N"); break; }
                        // Keep the body structurally valid so this tests signed
                        // binding, rather than failing the earlier plan parser.
                        changed.Plan.Fingerprint = ContainerRecyclePurgeWire.Fingerprint(changed.Plan);
                        Require(ContainerRecyclePurgeBinding.Hash(changed) != ContainerRecyclePurgeBinding.Hash(operation));
                        await Reject(async () => await client.ApplyAsync(changed, Headers("Apply", operation), Until(), lifetime.Token), StatusCode.PermissionDenied);
                    }
                    foreach (var variant in new[] { "recycle-domain", "restore-domain", "permission-domain", "generic-domain" })
                        await Reject(async () => await client.ApplyAsync(operation, Headers("Apply", operation, variant: variant), Until(), lifetime.Token), StatusCode.PermissionDenied);
                    await Reject(async () => await client.ApplyAsync(operation, Headers("Discard", operation), Until(), lifetime.Token), StatusCode.PermissionDenied);
                    var bad = operation.Clone(); bad.OperationId = bad.Context.JobId = bad.Plan.Command.Source.OperationId;
                    await Reject(async () => await client.ApplyAsync(bad, Headers("Apply", bad), Until(), lifetime.Token), StatusCode.InvalidArgument);
                    bad = operation.Clone(); bad.Context.JobId = bad.Plan.Command.Source.OperationId;
                    await Reject(async () => await client.ApplyAsync(bad, Headers("Apply", bad), Until(), lifetime.Token), StatusCode.InvalidArgument);
                    await Reject(async () => await client.InspectAsync(operation, Headers("Inspect", operation), Until(), lifetime.Token), StatusCode.NotFound);
                    await Reject(async () => await client.ApplyAsync(operation, Headers("Apply", operation), Until(), lifetime.Token), StatusCode.FailedPrecondition);
                    await Reject(async () => await client.ApplyAsync(operation, deadline: Until(), cancellationToken: lifetime.Token), StatusCode.PermissionDenied);
                    await Emit(new { kind = "rejected", operationId, refusals }); continue;
                }
                Phase = action + "-value";
                var receipt = action switch {
                    "reserve" => await client.ReserveAsync(operation, Headers("Reserve", operation), Until(), lifetime.Token),
                    "apply" => await client.ApplyAsync(operation, Headers("Apply", operation), Until(), lifetime.Token),
                    "inspect" => await client.InspectAsync(operation, Headers("Inspect", operation), Until(), lifetime.Token),
                    _ => await client.DiscardAsync(operation, Headers("Discard", operation), Until(), lifetime.Token) };
                ContainerRecyclePurgeWire.Receipt(operation, receipt);
                await Emit(new { kind = "receipt", action, operationId, jobId = operation.Context.JobId, result = formatter.Format(receipt) });
            }
            throw new InvalidOperationException();
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
    private static void Fields(JsonElement value, params string[] names) { Require(value.ValueKind == JsonValueKind.Object); var fields = value.EnumerateObject().Select(p => p.Name).ToArray(); Require(fields.Length == names.Length && fields.ToHashSet(StringComparer.Ordinal).SetEquals(names)); }
    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString() ?? throw new InvalidOperationException();
    private static bool Lower(string value, int length) => value.Length == length && value.All(char.IsAsciiHexDigitLower);
    private static bool Upper(string value) => value.Length == 64 && value.All(c => char.IsAsciiDigit(c) || c is >= 'A' and <= 'F');
    private static void Identity(string value) => Require(Lower(value, 32) && Guid.ParseExact(value, "N") != Guid.Empty);
    private static void Require(bool condition) { if (!condition) throw new InvalidOperationException(); }
    private static async Task Denied(Func<Task> call, StatusCode expected) { try { await call(); } catch (RpcException e) when (e.StatusCode == expected) { return; } throw new InvalidOperationException(); }
}
