using System.Text;
using WhaleDeck.Application.Models;
namespace WhaleDeck.Application.Services;

public static class ContainerRecursiveAclHistoryValidation
{
    public static void List(ContainerFileLocationDto location,string after,int take,string version)=>ContainerAclHistoryValidation.List(location,after,take,version);
    public static void Read(ContainerFileLocationDto location,string capture,string digest,int offset,int take)
    {
        ContainerAclHistoryValidation.Read(location,capture,digest);
        if(offset is <0 or >=10000 || take is <1 or >50)throw new ArgumentException("CONTAINER_RECURSIVE_ACL_HISTORY_REQUEST_INVALID");
    }
    public static void Summary(ContainerRecursiveAclHistorySummaryDto entry,ContainerFileLocationDto location)
    {
        if(entry is null)throw Invalid();ContainerAclHistoryValidation.Read(location,entry.CaptureId,entry.HistoryDigest);ContainerFileValidation.Hash(entry.ManifestDigest);
        ContainerFileValidation.Location(location with{InstanceId=entry.SourceInstanceId});
        if(entry.CreatedAtUnixMs<0 || entry.ChangedAclType is not ("access" or "default") || entry.EntryCount is <1 or >10000 || entry.DirectoryCount is <1 or >10000 ||
            entry.FileCount>10000 || entry.EntryCount!=entry.FileCount+entry.DirectoryCount || entry.ChangeCount>entry.EntryCount ||
            entry.ChangedAclType=="default" && entry.ChangeCount>entry.DirectoryCount)throw Invalid();
    }
    public static void Page(ContainerRecursiveAclHistoryPageDto page,ContainerFileLocationDto location,string after,int take,string version)
    {
        if(page is null || page.Location!=location || page.Entries is null || page.NextAfter is null)throw Invalid();ContainerFileValidation.Hash(page.Version);
        if(page.NextAfter.Length!=0)ContainerAclHistoryValidation.Read(location,page.NextAfter,page.Version);
        if(version.Length!=0 && version!=page.Version || page.Entries.Count>take || page.ScannedCount is <0 or >200 || page.InvalidCount<0 || page.InvalidCount>page.ScannedCount ||
            page.Entries.Count+page.InvalidCount>page.ScannedCount || page.Complete!=(page.NextAfter.Length==0) || !page.Complete && string.CompareOrdinal(page.NextAfter,after)<=0)throw Invalid();
        var previous=after;
        foreach(var entry in page.Entries){Summary(entry,location);if(string.CompareOrdinal(entry.CaptureId,previous)<=0 || page.NextAfter.Length!=0 && string.CompareOrdinal(entry.CaptureId,page.NextAfter)>0)throw Invalid();previous=entry.CaptureId;}
    }
    public static void Snapshot(ContainerRecursiveAclHistorySnapshotDto value,ContainerFileLocationDto location,string capture,string digest,int offset,int take)
    {
        if(value is null || value.Location!=location || value.Summary is null || value.Summary.CaptureId!=capture || value.Summary.HistoryDigest!=digest ||
            value.Offset!=offset || value.Entries is null || value.Entries.Count<1 || value.Entries.Count>take || value.Entries.Count>50)throw Invalid();
        Summary(value.Summary,location);var end=(long)value.Offset+value.Entries.Count;
        if(end>value.Summary.EntryCount || value.Complete!=(end==value.Summary.EntryCount) || value.NextOffset.HasValue==value.Complete || value.NextOffset is {} next && next!=end)throw Invalid();
        string? previous=null;
        for(var index=0;index<value.Entries.Count;index++)
        {
            var entry=value.Entries[index];if(entry is null)throw Invalid();ContainerFileValidation.Path(entry.RelativePath,true);
            ContainerFileValidation.Path(location.Path+(entry.RelativePath.Length==0?"":"/"+entry.RelativePath),false);
            if(entry.RelativePath.Length!=0 && entry.RelativePath.Count(c=>c=='/')>=32 || entry.Kind is not ("File" or "Directory") || entry.OriginalMode>511 ||
                entry.Kind=="File" && entry.DefaultAcl is not {Count:0} || (entry.RelativePath.Length==0)!=(value.Offset+index+1==value.Summary.EntryCount) || entry.RelativePath.Length==0 && entry.Kind!="Directory")throw Invalid();
            ContainerAclValidation.Entries(entry.AccessAcl);ContainerAclValidation.Entries(entry.DefaultAcl,true);
            if(ContainerAclValidation.Mode(entry.AccessAcl)!=entry.OriginalMode || previous is not null && Compare(previous,entry.RelativePath)>=0)throw Invalid();previous=entry.RelativePath;
        }
    }
    private static int Compare(string left,string right)
    {
        static int Depth(string path)=>path.Length==0?0:path.Count(c=>c=='/')+1;
        var depth=Depth(right).CompareTo(Depth(left));return depth!=0?depth:Encoding.UTF8.GetBytes(left).AsSpan().SequenceCompareTo(Encoding.UTF8.GetBytes(right));
    }
    public static InvalidOperationException Invalid()=>new("CONTAINER_RECURSIVE_ACL_HISTORY_OUTPUT_INVALID");
}
