"""Internal read-only ordinary-container Originals purge preflight.

No CLI, installation, RPC, apply/delete, scheduling or automatic retention.
This plan is not deletion authority: a future workflow must add confirmation,
per-source purge fencing, immutable jobs/audit and observation-only recovery.
"""
import hashlib,json,os,re


def encode(value):
    return json.dumps(value,sort_keys=True,ensure_ascii=False,separators=(',',':'),allow_nan=False).encode()


class ContainerRecyclePurgePreflight:
    COMMAND={'source','historyDigest','scope'}
    PLAN={'schemaVersion','command','targetProof','parentIdentity','parentVersion','uid','gid','privateIdentity','retainedIdentity','sourceRecordDigest','historyMetadataDigest','historyCreatedAtUnixMs','storageHash','storeIdentity','archiveIdentity','archiveBytes','archiveSha256','fingerprint'}

    def __init__(self,restore_planner):
        self._restore=restore_planner;self._source=restore_planner.source
        if self._source.restore_claims_path!=restore_planner.claims_path:raise ValueError('PURGE_SHARED_RESTORE_CLAIMS_REQUIRED')

    def reject(self,suffix):raise self._source.r.Rejected('CONTAINER_RECYCLE_PURGE_'+suffix)

    @staticmethod
    def digest(value):return hashlib.sha256(b'whaledeck-container-recycle-purge-plan-v1\n'+encode({k:v for k,v in value.items() if k!='fingerprint'})).hexdigest().upper()

    def command(self,value):
        if type(value) is not dict or set(value)!=self.COMMAND or value['scope']!='Originals':self.reject('REQUEST_INVALID')
        self._restore.command({k:value[k] for k in ('source','historyDigest')})
        if self._source.restore_claims_path is None or self._source.restore_claims_path!=self._restore.claims_path:self.reject('STATE_UNSAFE')

    def plan(self,command,session):
        self.command(command)
        if session is None:self.reject('SESSION_REQUIRED')
        ref=command['source'];source=self._source
        with source.locked(ref) as (_,record):
            return self.plan_locked(command,record,session)

    def plan_locked(self,command,record,session,purge_id=None):
        """Internal: caller holds the original source lock throughout use."""
        self.command(command)
        if session is None:self.reject('SESSION_REQUIRED')
        ref=command['source'];source=self._source
        # This rejects non-Trashed sources, missing/unsafe/any active restore
        # claim, changed namespace/private inode/content/attributes and HDD.
        selected=self._restore.source_plan({k:command[k] for k in ('source','historyDigest')},record,session,purge_id)
        with source.history.target(False) as (configuration,directory),source.history.gate(directory):
            history=source.history.read(directory,ref)
            if history['historyDigest']!=command['historyDigest'] or history['captureState']!='Trashed':self.reject('HISTORY_CHANGED')
            info=os.stat(ref['operationId']+'.data',dir_fd=directory,follow_symlinks=False)
            fd=source.f.open_at(directory,ref['operationId']+'.data',os.O_RDONLY|os.O_NOFOLLOW|os.O_NONBLOCK)
            try:
                if not os.path.samestat(info,os.fstat(fd)):self.reject('HISTORY_CHANGED')
                archive=source.metadata(info)
                checksum=hashlib.sha256();count=0
                while chunk:=os.read(fd,source.f.CHUNK):
                    session.revalidate();count+=len(chunk)
                    if count>history['sizeBytes']:self.reject('HISTORY_CHANGED')
                    checksum.update(chunk)
                if count!=history['sizeBytes'] or checksum.hexdigest().upper()!=history['sha256']:self.reject('HISTORY_CHANGED')
                if archive[3:6]!=[0,0,1] or archive[2]&0o7777!=0o600:self.reject('HISTORY_CHANGED')
                source.history.storage.attest(configuration,directory)
                result=dict(schemaVersion=1,command=json.loads(encode(command)),targetProof=selected['targetProof'],parentIdentity=selected['parentIdentity'],parentVersion=selected['parentVersion'],uid=selected['uid'],gid=selected['gid'],privateIdentity=selected['privateIdentity'],retainedIdentity=selected['retainedIdentity'],
                    sourceRecordDigest=hashlib.sha256(b'whaledeck-container-recycle-purge-source-v1\n'+encode(record)).hexdigest().upper(),historyMetadataDigest=hashlib.sha256(b'whaledeck-container-recycle-purge-history-v1\n'+encode(history)).hexdigest().upper(),historyCreatedAtUnixMs=history['createdAtUnixMs'],storageHash=hashlib.sha256(encode(configuration)).hexdigest().upper(),storeIdentity=[os.fstat(directory).st_dev,os.fstat(directory).st_ino],archiveIdentity=archive,archiveBytes=history['sizeBytes'],archiveSha256=history['sha256'])
                # The attestation and exact data inode must still be current.
                if source.metadata(os.fstat(fd))!=archive or source.metadata(os.stat(ref['operationId']+'.data',dir_fd=directory,follow_symlinks=False))!=archive:self.reject('HISTORY_CHANGED')
            finally:os.close(fd)
        source.guard(ref['plan']['command'],session,selected['targetProof'])
        source.check_purge_fence(ref,purge_id)
        if source.read_restore_claim(ref) is not None:self.reject('SOURCE_BUSY')
        result['fingerprint']=self.digest(result)
        return result

    def validate_plan(self,value):
        if type(value) is not dict or set(value)!=self.PLAN or type(value['schemaVersion']) is not int or value['schemaVersion']!=1 or type(value['fingerprint']) is not str or not re.fullmatch('[A-F0-9]{64}',value['fingerprint']) or value['fingerprint']!=self.digest(value):self.reject('PLAN_INVALID')
        self.command(value['command'])

    def revalidate(self,value,session):
        self.validate_plan(value);current=self.plan(value['command'],session)
        # Canonical bytes distinguish bool/float from integral identities and
        # never drop unknown fields while comparing the fresh native view.
        if encode(current)!=encode(value):self.reject('PLAN_CHANGED')
        return current
