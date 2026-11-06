"""Internal read-only archive-copy preflight after a proven completed purge.

No CLI, installation, copy, reservation or publication authority. Unknown purge
sources deliberately remain ineligible. Future archive restoration must use a
new operation/claim/journal and publish a new inode without rewriting the old
purge, source or HDD lifecycle as a retained-inode restoration.
"""
import contextlib
import fcntl
import hashlib
import json
import os
import stat


def encode(value):
    return json.dumps(value,sort_keys=True,separators=(',',':'),ensure_ascii=False,allow_nan=False).encode()


class ContainerRecycleArchivePreflight:
    COMMAND={'purge','historyDigest','mode'}
    PLAN={'schemaVersion','command','targetProof','parentIdentity','parentVersion','uid','gid','privateIdentity','deletedIdentity','sourceRecordDigest','purgeRecordDigest','purgeMarkerDigest','historyMetadataDigest','storageHash','storeIdentity','archiveIdentity','archiveBytes','archiveSha256','fingerprint'}

    def __init__(self,purges):
        self.purges=purges;self.source=purges.source

    def reject(self,suffix):raise self.source.r.Rejected('CONTAINER_RECYCLE_ARCHIVE_'+suffix)

    @staticmethod
    def digest(value):
        return hashlib.sha256(b'whaledeck-container-recycle-archive-plan-v1\n'+encode({k:v for k,v in value.items() if k!='fingerprint'})).hexdigest().upper()

    @staticmethod
    def record_digest(domain,value):return hashlib.sha256(domain+encode(value)).hexdigest().upper()

    def command(self,value):
        if type(value) is not dict or set(value)!=self.COMMAND or value['mode']!='ArchiveCopy':self.reject('REQUEST_INVALID')
        self.purges.request(value['purge'])
        if value['historyDigest']!=value['purge']['plan']['command']['historyDigest']:self.reject('HISTORY_CHANGED')
        restore=self.purges.preflight._restore
        if self.source.restore_claims_path is None or self.source.restore_claims_path!=restore.claims_path:self.reject('STATE_UNSAFE')

    @contextlib.contextmanager
    def existing_lock(self,directory,name):
        # Never create missing lock files while observing a historical source.
        fd=os.open(name,os.O_RDONLY|os.O_NOFOLLOW|os.O_NONBLOCK|os.O_CLOEXEC,dir_fd=directory)
        try:
            i=os.fstat(fd)
            if not stat.S_ISREG(i.st_mode) or (i.st_uid,i.st_gid,stat.S_IMODE(i.st_mode),i.st_nlink,i.st_size)!=(0,0,0o600,1,0):self.reject('STATE_UNSAFE')
            try:fcntl.flock(fd,fcntl.LOCK_EX|fcntl.LOCK_NB)
            except BlockingIOError:self.reject('BUSY')
            yield
        finally:os.close(fd)

    @contextlib.contextmanager
    def existing_record(self,engine,request):
        engine.request(request);path=engine.state_path
        if not path.is_absolute() or path!=path.resolve(strict=True):self.reject('STATE_UNSAFE')
        directory=engine.f.trusted_directory(path)
        try:
            if stat.S_IMODE(os.fstat(directory).st_mode)!=0o700:self.reject('STATE_UNSAFE')
            with self.existing_lock(directory,request['operationId']+'.lock'):
                record=engine.read(directory,request['operationId'])
                if record is None:self.reject('SOURCE_NOT_FOUND')
                if record['owner']!=request['owner'] or encode(record['plan'])!=encode(request['plan']):self.reject('SOURCE_CHANGED')
                yield record
        finally:os.close(directory)

    def namespace(self,ref,original,purge,session):
        source=self.source;plan=ref['plan']
        with source.parent(plan,session) as parent:
            if source.present(parent,plan['command']['path'].rsplit('/',1)[-1]):self.reject('TARGET_EXISTS')
            private_identity=None
            if source.present(parent,source.PRIVATE):
                private=source.private(parent,original)
                try:
                    private_identity=list(original['privateIdentity'])
                    if any(source.present(private,name) for name in (ref['operationId']+'.trash',purge['operationId']+'.purging')):self.reject('SOURCE_CHANGED')
                finally:os.close(private)
            info=os.fstat(parent)
            source.guard(plan['command'],session,plan['targetProof'])
            return dict(targetProof=plan['targetProof'],parentIdentity=[info.st_dev,info.st_ino],parentVersion=source.r.version(plan['command'],plan['command']['path'].rpartition('/')[0],info),uid=plan['uid'],gid=plan['gid'],privateIdentity=private_identity)

    def plan(self,command,session):
        self.command(command)
        if session is None:self.reject('SESSION_REQUIRED')
        purge=command['purge'];p=purge['plan'];ref=p['command']['source'];source=self.source
        # Same lock order as purge; no call to inspect/reconcile (those write).
        with self.existing_record(self.purges,purge) as deleted,self.existing_record(source,ref) as original:
            return self.plan_locked(command,session,deleted,original)

    def check_claim(self,ref,expected_claim):
        actual=self.source.read_restore_claim(ref)
        if encode(actual)!=encode(expected_claim):self.reject('SOURCE_BUSY')

    def plan_locked(self,command,session,deleted,original,expected_claim=None):
        """Internal: caller holds both purge and original source locks.

        A reservation may revalidate only its exact shared source claim. Public
        planning continues to reject every claim, including the caller's own.
        """
        self.command(command)
        if session is None:self.reject('SESSION_REQUIRED')
        purge=command['purge'];p=purge['plan'];ref=p['command']['source'];source=self.source
        marker=source.read_purge_fence(ref)
        if deleted['state']!='Completed' or not deleted['unlinkConfirmed'] or not self.purges.matches(marker,purge) or marker['state']!='Purged':self.reject('PURGE_UNCONFIRMED')
        if original['state']!='Purged' or original['actualIdentity'] is not None or not original['historyVerified'] or original['historyDigest']!=command['historyDigest']:self.reject('SOURCE_CHANGED')
        historical={**original,'state':'Trashed','actualIdentity':p['retainedIdentity']}
        if self.record_digest(b'whaledeck-container-recycle-purge-source-v1\n',historical)!=p['sourceRecordDigest']:self.reject('SOURCE_CHANGED')
        self.check_claim(ref,expected_claim)
        selected=self.namespace(ref,original,purge,session)
        with source.history.target(False) as (configuration,directory),self.existing_lock(directory,'history.lock'):
            history=source.history.read(directory,ref)
            if history['captureState']!='Purged' or history['historyDigest']!=command['historyDigest'] or self.record_digest(b'whaledeck-container-recycle-purge-history-v1\n',{**history,'captureState':'Trashed'})!=p['historyMetadataDigest']:self.reject('HISTORY_CHANGED')
            def attest():
                source.history.storage.attest(configuration,directory,read_only=True)
                if self.record_digest(b'',configuration)!=p['storageHash'] or [os.fstat(directory).st_dev,os.fstat(directory).st_ino]!=p['storeIdentity']:self.reject('HISTORY_CHANGED')
            attest();name=ref['operationId']+'.data';fd=source.f.open_at(directory,name,os.O_RDONLY|os.O_NOFOLLOW|os.O_NONBLOCK)
            try:
                identity=source.metadata(os.fstat(fd))
                def data_current():
                    if source.metadata(os.fstat(fd))!=identity or source.metadata(os.stat(name,dir_fd=directory,follow_symlinks=False))!=identity:self.reject('HISTORY_CHANGED')
                if identity!=p['archiveIdentity'] or not stat.S_ISREG(identity[2]) or (identity[2]&0o7777,identity[3],identity[4],identity[5])!=(0o600,0,0,1):self.reject('HISTORY_CHANGED')
                checksum=hashlib.sha256();count=0
                while chunk:=os.read(fd,source.f.CHUNK):
                    session.revalidate();count+=len(chunk);checksum.update(chunk)
                    if count>p['archiveBytes']:self.reject('HISTORY_CHANGED')
                if count!=p['archiveBytes'] or checksum.hexdigest().upper()!=p['archiveSha256']:self.reject('HISTORY_CHANGED')
                data_current();attest()
                result=dict(schemaVersion=1,command=json.loads(encode(command)),**selected,deletedIdentity=list(p['retainedIdentity']),sourceRecordDigest=self.record_digest(b'archive-source-v1\n',original),purgeRecordDigest=self.record_digest(b'archive-purge-v1\n',deleted),purgeMarkerDigest=self.record_digest(b'archive-marker-v1\n',marker),historyMetadataDigest=self.record_digest(b'archive-history-v1\n',history),storageHash=p['storageHash'],storeIdentity=list(p['storeIdentity']),archiveIdentity=identity,archiveBytes=count,archiveSha256=p['archiveSha256'])
            finally:os.close(fd)
        if encode(selected)!=encode(self.namespace(ref,original,purge,session)):self.reject('TARGET_CHANGED')
        self.check_claim(ref,expected_claim)
        session.revalidate()
        result['fingerprint']=self.digest(result);return result

    def validate_plan(self,value):
        if type(value) is not dict or set(value)!=self.PLAN or type(value['schemaVersion']) is not int or value['schemaVersion']!=1 or value['fingerprint']!=self.digest(value):self.reject('PLAN_INVALID')
        self.command(value['command'])

    def revalidate(self,value,session):
        self.validate_plan(value);fresh=self.plan(value['command'],session)
        if encode(value)!=encode(fresh):self.reject('PLAN_CHANGED')
        return fresh
