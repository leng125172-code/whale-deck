using Google.Protobuf;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

internal static partial class ContainerRecycleArchiveProbe
{
    private static string DifferentHash(string value)=>value==new string('E',64)?new('F',64):new('E',64);
    internal static ContainerRecycleArchivePlanRequest MutateArchivePlan(ContainerRecycleArchivePlanRequest request,string field)
    {
        var changed=request.Clone();var purge=changed.Command.Purge.Plan;switch(field){case "purge-id":changed.Command.Purge.OperationId=Guid.NewGuid().ToString("N");break;case "purge-owner":changed.Command.Purge.Owner=DifferentHash(changed.Command.Purge.Owner);break;case "source-path":changed.Command.Purge.Plan.Command.Source.Plan.Command.Location.Path="work/other.bin";break;case "source-bytes":purge.Command.Source.Plan.Sha256=new('E',64);purge.ArchiveSha256=purge.Command.Source.Plan.Sha256;break;case "source-attribute":changed.Command.Purge.Plan.Command.Source.Plan.Attributes[0].Value=ByteString.CopyFrom([1,2,3]);break;case "archive":changed.Command.Purge.Plan.ArchiveIdentity.Inode++;break;case "store":changed.Command.Purge.Plan.StoreIdentity.Inode++;break;case "history":changed.Command.HistoryDigest=new('E',64);purge.Command.HistoryDigest=changed.Command.HistoryDigest;break;case "actor":changed.Context.ActorSubject+="-changed";break;case "trace":changed.Context.TraceId+="-changed";break;default:throw new ArgumentException("UNKNOWN_PROBE_MUTATION");}
        purge.Fingerprint=ContainerRecyclePurgeWire.Fingerprint(purge);return changed;
    }
    internal static ContainerRecycleArchiveRequest MutateArchiveOperation(ContainerRecycleArchiveRequest archiveOperation,string field)
    {
        var changed=archiveOperation.Clone();var old=changed.Plan.Command.Purge.Plan;
                            switch(field){case "operation-job":changed.OperationId=changed.Context.JobId=Guid.NewGuid().ToString("N");break;case "owner":changed.Owner=new('E',64);break;case "purge-id":changed.Plan.Command.Purge.OperationId=Guid.NewGuid().ToString("N");break;case "purge-owner":changed.Plan.Command.Purge.Owner=DifferentHash(changed.Plan.Command.Purge.Owner);break;case "source-owner":old.Command.Source.Owner=new('E',64);break;case "source-attribute":old.Command.Source.Plan.Attributes[0].Value=ByteString.CopyFrom([1,2,3]);break;case "private":changed.Plan.PrivateIdentity=changed.Plan.PrivateIdentity is null?old.PrivateIdentity.Clone():null;break;case "deleted-time":old.RetainedChangedAtUnixNs++;changed.Plan.DeletedChangedAtUnixNs=old.RetainedChangedAtUnixNs;break;case "parent-version":changed.Plan.ParentVersion=new('E',64);break;case "source-record":changed.Plan.SourceRecordDigest=new('E',64);break;case "purge-record":changed.Plan.PurgeRecordDigest=new('E',64);break;case "purge-marker":changed.Plan.PurgeMarkerDigest=new('E',64);break;case "history-meta":changed.Plan.HistoryMetadataDigest=new('E',64);break;case "storage":old.StorageHash=new('E',64);changed.Plan.StorageHash=old.StorageHash;break;case "store":old.StoreIdentity.Inode++;changed.Plan.StoreIdentity=old.StoreIdentity.Clone();break;case "archive":old.ArchiveIdentity.Inode++;changed.Plan.ArchiveIdentity=old.ArchiveIdentity.Clone();break;case "archive-time":old.ArchiveChangedAtUnixNs++;changed.Plan.ArchiveChangedAtUnixNs=old.ArchiveChangedAtUnixNs;break;case "actor":changed.Context.ActorSubject+="-changed";break;case "trace":changed.Context.TraceId+="-changed";break;case "key":changed.Context.IdempotencyKey=Guid.NewGuid().ToString("N");break;default:throw new ArgumentException("UNKNOWN_PROBE_MUTATION");}
                            old.Fingerprint=ContainerRecyclePurgeWire.Fingerprint(old);changed.Plan.Fingerprint=ContainerRecycleArchiveWire.Fingerprint(changed.Plan);return changed;
    }
}
