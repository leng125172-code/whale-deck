using WhaleDeck.Application.Models;
namespace WhaleDeck.Application.Services;

public static class ContainerAclHistoryValidation
{
    public static void Actor(TerminalActor actor)
    {
        if(actor is null || string.IsNullOrWhiteSpace(actor.Subject) || actor.Subject.Length>256 || actor.Subject.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(actor.TraceId) || actor.TraceId.Length>128 || actor.TraceId.Any(char.IsControl)) throw new ArgumentException("CONTAINER_ACL_HISTORY_CONTEXT_INVALID");
    }
    public static void List(ContainerFileLocationDto location,string after,int take,string version)
    {
        ContainerFileValidation.Location(location);
        if(after is null || version is null || take is <1 or >50) throw new ArgumentException("CONTAINER_ACL_HISTORY_REQUEST_INVALID");
        if(after.Length!=0) Identity(after); if(version.Length!=0) ContainerFileValidation.Hash(version);
        if(after.Length!=0 && version.Length==0) throw new ArgumentException("CONTAINER_ACL_HISTORY_VERSION_REQUIRED");
    }
    public static void Read(ContainerFileLocationDto location,string capture,string digest)
    { ContainerFileValidation.Location(location); Identity(capture); ContainerFileValidation.Hash(digest); }
    private static void Identity(string id)
    { if(id is null || id.Length!=32 || !Guid.TryParseExact(id,"N",out var value) || value==Guid.Empty || id!=value.ToString("N"))throw new ArgumentException("CONTAINER_ACL_HISTORY_ID_INVALID"); }
    public static void Entry(ContainerAclHistoryEntryDto entry,ContainerFileLocationDto location)
    {
        if(entry is null)throw Invalid(); Identity(entry.CaptureId); ContainerFileValidation.Hash(entry.HistoryDigest);
        ContainerFileValidation.Location(location with {InstanceId=entry.SourceInstanceId});
        if(entry.CreatedAtUnixMs is <0 or >253402300799999L || entry.ChangedAclType is not ("access" or "default") || entry.Kind is not ("File" or "Directory") ||
            entry.OriginalMode>511 || entry.AccessEntryCount is <3 or >64 || entry.DefaultEntryCount>64 || entry.DefaultEntryCount is 1 or 2 ||
            entry.Kind=="File" && (entry.DefaultEntryCount!=0 || entry.ChangedAclType!="access"))throw Invalid();
    }
    public static void Page(ContainerAclHistoryPageDto page,ContainerFileLocationDto location,string after,int take,string version)
    {
        if(page is null || page.Location!=location || page.Entries is null || page.NextAfter is null)throw Invalid(); ContainerFileValidation.Hash(page.Version);
        if(page.NextAfter.Length!=0)Identity(page.NextAfter);
        if(version.Length!=0 && version!=page.Version || page.Entries.Count>take || page.ScannedCount is <0 or >200 || page.InvalidCount<0 || page.InvalidCount>page.ScannedCount ||
            page.Entries.Count+page.InvalidCount>page.ScannedCount || page.Complete!=(page.NextAfter.Length==0) || !page.Complete && string.CompareOrdinal(page.NextAfter,after)<=0)throw Invalid();
        var previous=after;
        foreach(var entry in page.Entries){Entry(entry,location);if(string.CompareOrdinal(entry.CaptureId,previous)<=0 || page.NextAfter.Length!=0 && string.CompareOrdinal(entry.CaptureId,page.NextAfter)>0)throw Invalid();previous=entry.CaptureId;}
    }
    public static void Snapshot(ContainerAclHistorySnapshotDto value,ContainerFileLocationDto location,string capture,string digest)
    {
        if(value is null || value.Location!=location || value.Entry is null || value.Entry.CaptureId!=capture || value.Entry.HistoryDigest!=digest)throw Invalid();
        Entry(value.Entry,location);ContainerAclValidation.Entries(value.AccessAcl);ContainerAclValidation.Entries(value.DefaultAcl,true);
        if(value.AccessAcl.Count!=value.Entry.AccessEntryCount || value.DefaultAcl.Count!=value.Entry.DefaultEntryCount || ContainerAclValidation.Mode(value.AccessAcl)!=value.Entry.OriginalMode)throw Invalid();
    }
    public static InvalidOperationException Invalid()=>new("CONTAINER_ACL_HISTORY_OUTPUT_INVALID");
}
