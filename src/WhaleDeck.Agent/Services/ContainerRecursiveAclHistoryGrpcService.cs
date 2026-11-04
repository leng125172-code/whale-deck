using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using Grpc.Core;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

/// <summary>Read-only preimages, not execution outcomes or authority to restore.</summary>
public sealed class ContainerRecursiveAclHistoryGrpcService(FileTransferRunner runner) : ContainerRecursiveAclHistoryService.ContainerRecursiveAclHistoryServiceBase
{
    public override async Task<ContainerRecursiveAclHistoryPage> List(ContainerRecursiveAclHistoryListRequest request, ServerCallContext context)
    {
        ContainerRecursiveAclHistoryPolicy.List(request); ContainerRecursiveAclHistoryPolicy.Binding(request, context);
        using var value = await runner.ContainerRecursiveAclHistoryOnceAsync("list", JsonSerializer.Serialize(new {
            containerId=request.Location.ContainerId, instanceId=request.Location.InstanceId, path=request.Location.Path,
            after=request.After, take=request.Take, expectedVersion=request.ExpectedVersion }), context.CancellationToken);
        return ContainerRecursiveAclHistoryPolicy.Page(value.RootElement, request);
    }
    public override async Task<ContainerRecursiveAclHistorySnapshot> Read(ContainerRecursiveAclHistoryReadRequest request, ServerCallContext context)
    {
        ContainerRecursiveAclHistoryPolicy.Read(request); ContainerRecursiveAclHistoryPolicy.Binding(request, context);
        using var value = await runner.ContainerRecursiveAclHistoryOnceAsync("read", JsonSerializer.Serialize(new {
            containerId=request.Location.ContainerId, instanceId=request.Location.InstanceId, path=request.Location.Path,
            captureId=request.CaptureId, expectedHistoryDigest=request.ExpectedHistoryDigest, offset=request.Offset, take=request.Take }), context.CancellationToken);
        return ContainerRecursiveAclHistoryPolicy.Snapshot(value.RootElement, request);
    }
}

internal static class ContainerRecursiveAclHistoryPolicy
{
    public static void List(ContainerRecursiveAclHistoryListRequest request)
    {
        ContainerFilePolicy.Location(request.Location); ContainerRecursiveAclPolicy.Context(request.Context, planning:true);
        if(request.Take is <1 or >50) throw new ArgumentException("CONTAINER_RECURSIVE_ACL_HISTORY_REQUEST_INVALID");
        if(request.After.Length!=0) FileTransferPolicy.Identity(request.After);
        if(request.ExpectedVersion.Length!=0) FileTransferPolicy.Hash(request.ExpectedVersion);
        if(request.After.Length!=0 && request.ExpectedVersion.Length==0) throw new ArgumentException("CONTAINER_RECURSIVE_ACL_HISTORY_VERSION_REQUIRED");
    }
    public static void Read(ContainerRecursiveAclHistoryReadRequest request)
    {
        ContainerFilePolicy.Location(request.Location); ContainerRecursiveAclPolicy.Context(request.Context, planning:true);
        FileTransferPolicy.Identity(request.CaptureId); FileTransferPolicy.Hash(request.ExpectedHistoryDigest);
        if(!request.HasOffset || request.Offset>=10000 || request.Take is <1 or >50) throw new ArgumentException("CONTAINER_RECURSIVE_ACL_HISTORY_REQUEST_INVALID");
    }
    public static void Binding(IMessage request, ServerCallContext context)
    {
        var expected=context.RequestHeaders.GetValue(ContainerRecursiveAclHistoryBinding.Header); var actual=ContainerRecursiveAclHistoryBinding.Hash(request);
        if(expected is not {Length:64} || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected),Encoding.ASCII.GetBytes(actual)))
            throw new RpcException(new(StatusCode.PermissionDenied,"CONTAINER_RECURSIVE_ACL_HISTORY_BINDING_INVALID"));
    }
    public static void Summary(ContainerRecursiveAclHistorySummary entry, ContainerFileLocation location)
    {
        FileTransferPolicy.Identity(entry.CaptureId); FileTransferPolicy.Hash(entry.HistoryDigest); FileTransferPolicy.Hash(entry.ManifestDigest);
        ContainerFilePolicy.Location(new(){ContainerId=location.ContainerId,InstanceId=entry.SourceInstanceId,Path=location.Path});
        if(!entry.HasCreatedAtUnixMs || entry.CreatedAtUnixMs<0 || entry.ChangedAclType is not ("access" or "default") ||
            !entry.HasEntryCount || !entry.HasFileCount || !entry.HasDirectoryCount || !entry.HasChangeCount ||
            entry.EntryCount is <1 or >10000 || entry.DirectoryCount is <1 or >10000 || entry.FileCount>10000 ||
            entry.EntryCount!=entry.FileCount+entry.DirectoryCount || entry.ChangeCount>entry.EntryCount ||
            entry.ChangedAclType=="default" && entry.ChangeCount>entry.DirectoryCount) throw Invalid();
    }
    public static void Page(ContainerRecursiveAclHistoryPage page, ContainerRecursiveAclHistoryListRequest request)
    {
        if(!Equals(page.Location,request.Location) || !page.HasScannedCount || !page.HasInvalidCount || !page.HasComplete) throw Invalid();
        FileTransferPolicy.Hash(page.Version);
        if(page.NextAfter.Length!=0) FileTransferPolicy.Identity(page.NextAfter);
        if(request.ExpectedVersion.Length!=0 && page.Version!=request.ExpectedVersion || page.Entries.Count>request.Take ||
            page.ScannedCount>200 || page.InvalidCount>page.ScannedCount || page.Entries.Count+page.InvalidCount>page.ScannedCount ||
            page.Complete!=(page.NextAfter.Length==0) || !page.Complete && string.CompareOrdinal(page.NextAfter,request.After)<=0 || page.CalculateSize()>128*1024) throw Invalid();
        var previous=request.After;
        foreach(var entry in page.Entries)
        {
            Summary(entry,request.Location);
            if(string.CompareOrdinal(entry.CaptureId,previous)<=0 || page.NextAfter.Length!=0 && string.CompareOrdinal(entry.CaptureId,page.NextAfter)>0) throw Invalid();
            previous=entry.CaptureId;
        }
    }
    public static void Snapshot(ContainerRecursiveAclHistorySnapshot snapshot, ContainerRecursiveAclHistoryReadRequest request)
    {
        if(!Equals(snapshot.Location,request.Location) || snapshot.Summary is null || snapshot.Summary.CaptureId!=request.CaptureId || snapshot.Summary.HistoryDigest!=request.ExpectedHistoryDigest ||
            !snapshot.HasOffset || snapshot.Offset!=request.Offset || !snapshot.HasComplete || snapshot.Entries.Count<1 || snapshot.Entries.Count>request.Take || snapshot.Entries.Count>50) throw Invalid();
        Summary(snapshot.Summary,request.Location);
        var end=(long)snapshot.Offset+snapshot.Entries.Count;
        if(end>snapshot.Summary.EntryCount || snapshot.Complete!=(end==snapshot.Summary.EntryCount) || snapshot.HasNextOffset==snapshot.Complete ||
            snapshot.HasNextOffset && snapshot.NextOffset!=end || snapshot.CalculateSize()>128*1024) throw Invalid();
        string? previous=null;
        for(var index=0;index<snapshot.Entries.Count;index++)
        {
            var entry=snapshot.Entries[index];ContainerFilePolicy.Path(entry.RelativePath,true);
            ContainerFilePolicy.Path(request.Location.Path+(entry.RelativePath.Length==0?"":"/"+entry.RelativePath),false);
            if(entry.RelativePath.Length!=0 && entry.RelativePath.Count(c=>c=='/')>=32 || entry.Kind is not ("File" or "Directory") ||
                !entry.HasOriginalMode || entry.OriginalMode>511 || entry.Kind=="File" && entry.DefaultAcl.Count!=0 ||
                (entry.RelativePath.Length==0)!=(snapshot.Offset+index+1==snapshot.Summary.EntryCount) || entry.RelativePath.Length==0 && entry.Kind!="Directory") throw Invalid();
            ContainerAclPolicy.Entries(entry.AccessAcl);ContainerAclPolicy.Entries(entry.DefaultAcl,true);
            if(Mode(entry.AccessAcl)!=entry.OriginalMode || previous is not null && Compare(previous,entry.RelativePath)>=0) throw Invalid();
            previous=entry.RelativePath;
        }
    }
    private static int Compare(string left,string right)
    {
        static int Depth(string path)=>path.Length==0?0:path.Count(c=>c=='/')+1;
        var depth=Depth(right).CompareTo(Depth(left));return depth!=0?depth:Encoding.UTF8.GetBytes(left).AsSpan().SequenceCompareTo(Encoding.UTF8.GetBytes(right));
    }
    private static uint Mode(IReadOnlyList<ContainerAclEntry> entries)=>entries.Single(e=>e.Tag=="user_obj").Permissions<<6 |
        (entries.FirstOrDefault(e=>e.Tag=="mask")??entries.Single(e=>e.Tag=="group_obj")).Permissions<<3 | entries.Single(e=>e.Tag=="other").Permissions;
    private static readonly string[] SummaryFields=["captureId","historyDigest","createdAtUnixMs","sourceInstanceId","changedAclType","manifestDigest","entryCount","fileCount","directoryCount","changeCount"];
    private static ContainerRecursiveAclHistorySummary ParseSummary(JsonElement v)=>new(){CaptureId=Text(v,"captureId"),HistoryDigest=Text(v,"historyDigest"),CreatedAtUnixMs=v.GetProperty("createdAtUnixMs").GetInt64(),
        SourceInstanceId=Text(v,"sourceInstanceId"),ChangedAclType=Text(v,"changedAclType"),ManifestDigest=Text(v,"manifestDigest"),EntryCount=v.GetProperty("entryCount").GetUInt32(),FileCount=v.GetProperty("fileCount").GetUInt32(),DirectoryCount=v.GetProperty("directoryCount").GetUInt32(),ChangeCount=v.GetProperty("changeCount").GetUInt32()};
    private static void Location(JsonElement v,ContainerFileLocation expected)
    { if(Text(v,"containerId")!=expected.ContainerId || Text(v,"instanceId")!=expected.InstanceId || Text(v,"path")!=expected.Path) throw Invalid(); }
    public static ContainerRecursiveAclHistoryPage Page(JsonElement v,ContainerRecursiveAclHistoryListRequest request)
    {
        ContainerHistoryPolicy.Fields(v,"containerId","instanceId","path","version","entries","scannedCount","invalidCount","complete","nextAfter"); Location(v,request.Location);
        var rows=v.GetProperty("entries");if(rows.ValueKind!=JsonValueKind.Array || rows.GetArrayLength()>50)throw Invalid();
        var next=v.GetProperty("nextAfter");if(next.ValueKind is not (JsonValueKind.Null or JsonValueKind.String))throw Invalid();
        var page=new ContainerRecursiveAclHistoryPage{Location=request.Location.Clone(),Version=Text(v,"version"),NextAfter=next.GetString()??"",ScannedCount=v.GetProperty("scannedCount").GetUInt32(),InvalidCount=v.GetProperty("invalidCount").GetUInt32(),Complete=v.GetProperty("complete").GetBoolean()};
        foreach(var row in rows.EnumerateArray()){ContainerHistoryPolicy.Fields(row,SummaryFields);page.Entries.Add(ParseSummary(row));}
        Page(page,request);return page;
    }
    public static ContainerRecursiveAclHistorySnapshot Snapshot(JsonElement v,ContainerRecursiveAclHistoryReadRequest request)
    {
        ContainerHistoryPolicy.Fields(v,[..SummaryFields,"containerId","instanceId","path","offset","entries","complete","nextOffset"]);Location(v,request.Location);
        var rows=v.GetProperty("entries");if(rows.ValueKind!=JsonValueKind.Array || rows.GetArrayLength()>50)throw Invalid();
        var snapshot=new ContainerRecursiveAclHistorySnapshot{Location=request.Location.Clone(),Summary=ParseSummary(v),Offset=v.GetProperty("offset").GetUInt32(),Complete=v.GetProperty("complete").GetBoolean()};
        var next=v.GetProperty("nextOffset");if(next.ValueKind!=JsonValueKind.Null)snapshot.NextOffset=next.GetUInt32();
        foreach(var row in rows.EnumerateArray())
        {
            ContainerHistoryPolicy.Fields(row,"relativePath","kind","originalMode","accessAcl","defaultAcl");
            var entry=new ContainerRecursiveAclHistoryEntry{RelativePath=Text(row,"relativePath"),Kind=Text(row,"kind"),OriginalMode=row.GetProperty("originalMode").GetUInt32()};
            entry.AccessAcl.Add(Entries(row.GetProperty("accessAcl")));entry.DefaultAcl.Add(Entries(row.GetProperty("defaultAcl")));snapshot.Entries.Add(entry);
        }
        Snapshot(snapshot,request);return snapshot;
    }
    private static ContainerAclEntry[] Entries(JsonElement rows)
    {
        if(rows.ValueKind!=JsonValueKind.Array || rows.GetArrayLength()>64)throw Invalid();
        return rows.EnumerateArray().Select(v=>{ContainerHistoryPolicy.Fields(v,"tag","id","permissions");var e=new ContainerAclEntry{Tag=Text(v,"tag"),Permissions=v.GetProperty("permissions").GetUInt32()};if(v.GetProperty("id").ValueKind!=JsonValueKind.Null)e.Id=v.GetProperty("id").GetUInt32();return e;}).ToArray();
    }
    private static string Text(JsonElement v,string key)=>v.GetProperty(key).GetString()??throw Invalid();
    public static InvalidOperationException Invalid()=>new("CONTAINER_RECURSIVE_ACL_HISTORY_OUTPUT_INVALID");
    public static RpcException Failure(string? code)
    {
        if(code?.StartsWith("CONTAINER_RECURSIVE_ACL_HISTORY_",StringComparison.Ordinal)!=true)
            return code?.StartsWith("CONTAINER_FILES_",StringComparison.Ordinal)==true?ContainerFilePolicy.Failure(code):ContainerUploadPolicy.Failure(code);
        var status=code switch {
            "CONTAINER_RECURSIVE_ACL_HISTORY_REQUEST_INVALID" or "CONTAINER_RECURSIVE_ACL_HISTORY_VERSION_REQUIRED"=>StatusCode.InvalidArgument,
            "CONTAINER_RECURSIVE_ACL_HISTORY_PROTECTED"=>StatusCode.PermissionDenied,
            "CONTAINER_RECURSIVE_ACL_HISTORY_NOT_FOUND" or "CONTAINER_RECURSIVE_ACL_HISTORY_TARGET_MISSING"=>StatusCode.NotFound,
            "CONTAINER_RECURSIVE_ACL_HISTORY_VERSION_CONFLICT"=>StatusCode.Aborted,
            "CONTAINER_RECURSIVE_ACL_HISTORY_TIMEOUT"=>StatusCode.DeadlineExceeded,
            "CONTAINER_RECURSIVE_ACL_HISTORY_LIMIT" or "CONTAINER_RECURSIVE_ACL_HISTORY_RESPONSE_LIMIT"=>StatusCode.ResourceExhausted,
            "CONTAINER_RECURSIVE_ACL_HISTORY_ENGINE_MISMATCH" or "CONTAINER_RECURSIVE_ACL_HISTORY_INVALID" or "CONTAINER_RECURSIVE_ACL_HISTORY_UNSAFE" or "CONTAINER_RECURSIVE_ACL_HISTORY_MAPPING_UNPROVEN" or "CONTAINER_RECURSIVE_ACL_HISTORY_MODULE_UNSAFE" or "CONTAINER_RECURSIVE_ACL_HISTORY_INVOCATION_INVALID"=>StatusCode.FailedPrecondition,
            _=>StatusCode.Unavailable};
        return new(new(status,status==StatusCode.Unavailable?"CONTAINER_RECURSIVE_ACL_HISTORY_UNAVAILABLE":code!));
    }
}
