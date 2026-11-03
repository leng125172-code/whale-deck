using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using Grpc.Core;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

/// <summary>Verified metadata only. Restoration and human authorization belong to Application.</summary>
public sealed class ContainerAclHistoryGrpcService(FileTransferRunner runner) : ContainerAclHistoryService.ContainerAclHistoryServiceBase
{
    public override async Task<ContainerAclHistoryPage> List(ContainerAclHistoryListRequest request, ServerCallContext context)
    {
        ContainerAclHistoryPolicy.List(request); ContainerAclHistoryPolicy.Binding(request, context);
        using var value = await runner.ContainerAclHistoryOnceAsync("list", JsonSerializer.Serialize(new {
            containerId=request.Location.ContainerId, instanceId=request.Location.InstanceId, path=request.Location.Path,
            after=request.After, take=request.Take, expectedVersion=request.ExpectedVersion }), context.CancellationToken);
        return ContainerAclHistoryPolicy.Page(value.RootElement, request);
    }
    public override async Task<ContainerAclHistorySnapshot> Read(ContainerAclHistoryReadRequest request, ServerCallContext context)
    {
        ContainerAclHistoryPolicy.Read(request); ContainerAclHistoryPolicy.Binding(request, context);
        using var value = await runner.ContainerAclHistoryOnceAsync("read", JsonSerializer.Serialize(new {
            containerId=request.Location.ContainerId, instanceId=request.Location.InstanceId, path=request.Location.Path,
            captureId=request.CaptureId, expectedHistoryDigest=request.ExpectedHistoryDigest }), context.CancellationToken);
        return ContainerAclHistoryPolicy.Snapshot(value.RootElement, request);
    }
}

internal static class ContainerAclHistoryPolicy
{
    public static void List(ContainerAclHistoryListRequest request)
    {
        ContainerFilePolicy.Location(request.Location); ContainerAclPolicy.Context(request.Context, planning:true);
        if(request.Take is <1 or >50) throw new ArgumentException("CONTAINER_ACL_HISTORY_REQUEST_INVALID");
        if(request.After.Length!=0) FileTransferPolicy.Identity(request.After);
        if(request.ExpectedVersion.Length!=0) FileTransferPolicy.Hash(request.ExpectedVersion);
        if(request.After.Length!=0 && request.ExpectedVersion.Length==0) throw new ArgumentException("CONTAINER_ACL_HISTORY_VERSION_REQUIRED");
    }
    public static void Read(ContainerAclHistoryReadRequest request)
    {
        ContainerFilePolicy.Location(request.Location); ContainerAclPolicy.Context(request.Context, planning:true);
        FileTransferPolicy.Identity(request.CaptureId); FileTransferPolicy.Hash(request.ExpectedHistoryDigest);
    }
    public static void Binding(IMessage request, ServerCallContext context)
    {
        var expected=context.RequestHeaders.GetValue(ContainerAclHistoryBinding.Header); var actual=ContainerAclHistoryBinding.Hash(request);
        if(expected is not {Length:64} || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected),Encoding.ASCII.GetBytes(actual)))
            throw new RpcException(new(StatusCode.PermissionDenied,"CONTAINER_ACL_HISTORY_BINDING_INVALID"));
    }
    public static void Entry(ContainerAclHistoryEntry entry, ContainerFileLocation location)
    {
        FileTransferPolicy.Identity(entry.CaptureId); FileTransferPolicy.Hash(entry.HistoryDigest);
        ContainerFilePolicy.Location(new(){ContainerId=location.ContainerId,InstanceId=entry.SourceInstanceId,Path=location.Path});
        if(!entry.HasCreatedAtUnixMs || !entry.HasAccessEntryCount || !entry.HasDefaultEntryCount || entry.CreatedAtUnixMs is <0 or >253402300799999L || entry.ChangedAclType is not ("access" or "default") || entry.Kind is not ("File" or "Directory") ||
            !entry.HasOriginalMode || entry.OriginalMode>511 || entry.AccessEntryCount is <3 or >64 || entry.DefaultEntryCount>64 || entry.DefaultEntryCount is 1 or 2 ||
            entry.Kind=="File" && (entry.ChangedAclType!="access" || entry.DefaultEntryCount!=0)) throw Invalid();
    }
    public static void Page(ContainerAclHistoryPage page, ContainerAclHistoryListRequest request)
    {
        if(!Equals(page.Location,request.Location) || !page.HasScannedCount || !page.HasInvalidCount || !page.HasComplete) throw Invalid();
        FileTransferPolicy.Hash(page.Version);
        if(page.NextAfter.Length!=0) FileTransferPolicy.Identity(page.NextAfter);
        if(request.ExpectedVersion.Length!=0 && page.Version!=request.ExpectedVersion || page.Entries.Count>request.Take ||
            page.ScannedCount>200 || page.InvalidCount>page.ScannedCount || page.Entries.Count+page.InvalidCount>page.ScannedCount ||
            page.Complete!=(page.NextAfter.Length==0) || !page.Complete && string.CompareOrdinal(page.NextAfter,request.After)<=0 || page.CalculateSize()>60*1024) throw Invalid();
        var previous=request.After;
        foreach(var entry in page.Entries)
        {
            Entry(entry,request.Location);
            if(string.CompareOrdinal(entry.CaptureId,previous)<=0 || page.NextAfter.Length!=0 && string.CompareOrdinal(entry.CaptureId,page.NextAfter)>0) throw Invalid();
            previous=entry.CaptureId;
        }
    }
    public static void Snapshot(ContainerAclHistorySnapshot snapshot, ContainerAclHistoryReadRequest request)
    {
        if(!Equals(snapshot.Location,request.Location) || snapshot.Entry is null || snapshot.Entry.CaptureId!=request.CaptureId || snapshot.Entry.HistoryDigest!=request.ExpectedHistoryDigest) throw Invalid();
        Entry(snapshot.Entry,request.Location); ContainerAclPolicy.Entries(snapshot.AccessAcl); ContainerAclPolicy.Entries(snapshot.DefaultAcl,true);
        if(snapshot.AccessAcl.Count!=snapshot.Entry.AccessEntryCount || snapshot.DefaultAcl.Count!=snapshot.Entry.DefaultEntryCount || Mode(snapshot.AccessAcl)!=snapshot.Entry.OriginalMode || snapshot.CalculateSize()>24*1024) throw Invalid();
    }
    private static uint Mode(IReadOnlyList<ContainerAclEntry> entries) => entries.Single(e=>e.Tag=="user_obj").Permissions<<6 |
        (entries.FirstOrDefault(e=>e.Tag=="mask")??entries.Single(e=>e.Tag=="group_obj")).Permissions<<3 | entries.Single(e=>e.Tag=="other").Permissions;
    private static readonly string[] EntryFields=["captureId","historyDigest","createdAtUnixMs","sourceInstanceId","changedAclType","kind","originalMode","accessEntryCount","defaultEntryCount"];
    private static ContainerAclHistoryEntry ParseEntry(JsonElement v) => new() {CaptureId=Text(v,"captureId"),HistoryDigest=Text(v,"historyDigest"),CreatedAtUnixMs=v.GetProperty("createdAtUnixMs").GetInt64(),
        SourceInstanceId=Text(v,"sourceInstanceId"),ChangedAclType=Text(v,"changedAclType"),Kind=Text(v,"kind"),OriginalMode=v.GetProperty("originalMode").GetUInt32(),AccessEntryCount=v.GetProperty("accessEntryCount").GetUInt32(),DefaultEntryCount=v.GetProperty("defaultEntryCount").GetUInt32()};
    private static void Location(JsonElement v,ContainerFileLocation expected)
    { if(Text(v,"containerId")!=expected.ContainerId || Text(v,"instanceId")!=expected.InstanceId || Text(v,"path")!=expected.Path) throw Invalid(); }
    public static ContainerAclHistoryPage Page(JsonElement v,ContainerAclHistoryListRequest request)
    {
        ContainerHistoryPolicy.Fields(v,"containerId","instanceId","path","version","entries","scannedCount","invalidCount","complete","nextAfter"); Location(v,request.Location);
        var rows=v.GetProperty("entries");if(rows.ValueKind!=JsonValueKind.Array || rows.GetArrayLength()>50)throw Invalid();
        var next=v.GetProperty("nextAfter");if(next.ValueKind is not (JsonValueKind.Null or JsonValueKind.String))throw Invalid();
        var page=new ContainerAclHistoryPage{Location=request.Location.Clone(),Version=Text(v,"version"),NextAfter=next.GetString()??"",ScannedCount=v.GetProperty("scannedCount").GetUInt32(),InvalidCount=v.GetProperty("invalidCount").GetUInt32(),Complete=v.GetProperty("complete").GetBoolean()};
        foreach(var row in rows.EnumerateArray()){ContainerHistoryPolicy.Fields(row,EntryFields);page.Entries.Add(ParseEntry(row));}
        Page(page,request);return page;
    }
    public static ContainerAclHistorySnapshot Snapshot(JsonElement v,ContainerAclHistoryReadRequest request)
    {
        ContainerHistoryPolicy.Fields(v,[..EntryFields,"containerId","instanceId","path","accessAcl","defaultAcl"]);Location(v,request.Location);
        var snapshot=new ContainerAclHistorySnapshot{Location=request.Location.Clone(),Entry=ParseEntry(v)};
        snapshot.AccessAcl.Add(Entries(v.GetProperty("accessAcl")));snapshot.DefaultAcl.Add(Entries(v.GetProperty("defaultAcl")));Snapshot(snapshot,request);return snapshot;
    }
    private static ContainerAclEntry[] Entries(JsonElement rows)
    {
        if(rows.ValueKind!=JsonValueKind.Array || rows.GetArrayLength()>64)throw Invalid();
        return rows.EnumerateArray().Select(v=>{ContainerHistoryPolicy.Fields(v,"tag","id","permissions");var e=new ContainerAclEntry{Tag=Text(v,"tag"),Permissions=v.GetProperty("permissions").GetUInt32()};if(v.GetProperty("id").ValueKind!=JsonValueKind.Null)e.Id=v.GetProperty("id").GetUInt32();return e;}).ToArray();
    }
    private static string Text(JsonElement v,string key)=>v.GetProperty(key).GetString()??throw Invalid();
    public static InvalidOperationException Invalid()=>new("CONTAINER_ACL_HISTORY_OUTPUT_INVALID");
    public static RpcException Failure(string? code)
    {
        if(code?.StartsWith("CONTAINER_ACL_HISTORY_",StringComparison.Ordinal)!=true)
            return code?.StartsWith("CONTAINER_FILES_",StringComparison.Ordinal)==true?ContainerFilePolicy.Failure(code):ContainerUploadPolicy.Failure(code);
        var status=code switch {
            "CONTAINER_ACL_HISTORY_REQUEST_INVALID" or "CONTAINER_ACL_HISTORY_VERSION_REQUIRED"=>StatusCode.InvalidArgument,
            "CONTAINER_ACL_HISTORY_PROTECTED"=>StatusCode.PermissionDenied,
            "CONTAINER_ACL_HISTORY_NOT_FOUND" or "CONTAINER_ACL_HISTORY_TARGET_MISSING"=>StatusCode.NotFound,
            "CONTAINER_ACL_HISTORY_VERSION_CONFLICT"=>StatusCode.Aborted,
            "CONTAINER_ACL_HISTORY_TIMEOUT"=>StatusCode.DeadlineExceeded,
            "CONTAINER_ACL_HISTORY_LIMIT" or "CONTAINER_ACL_HISTORY_RESPONSE_LIMIT"=>StatusCode.ResourceExhausted,
            "CONTAINER_ACL_HISTORY_ENGINE_MISMATCH" or "CONTAINER_ACL_HISTORY_INVALID" or "CONTAINER_ACL_HISTORY_UNSAFE" or "CONTAINER_ACL_HISTORY_MAPPING_UNPROVEN" or "CONTAINER_ACL_HISTORY_MODULE_UNSAFE" or "CONTAINER_ACL_HISTORY_INVOCATION_INVALID"=>StatusCode.FailedPrecondition,
            _=>StatusCode.Unavailable};
        return new(new(status,status==StatusCode.Unavailable?"CONTAINER_ACL_HISTORY_UNAVAILABLE":code!));
    }
}
