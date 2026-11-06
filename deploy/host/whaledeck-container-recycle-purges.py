"""Internal Originals-only purge kernel; no CLI, installation or RPC.

Explicit reservation is required. HDD data/metadata are retained. Recovery is
observation only: no rename/unlink is resumed after an interrupted Apply.
Deletion authority, authorization, policy, jobs/audit and versioned installation
must be supplied by the future authenticated workflow before exposing this.
"""
import contextlib
import hashlib
import json
import os


def encode(value):
    return json.dumps(value,sort_keys=True,separators=(',',':'),ensure_ascii=False,allow_nan=False).encode()


def purges_type(fence_type):
    class ContainerRecyclePurges(fence_type):
        RECORD=fence_type.RECORD|{'pendingIdentity','unlinkConfirmed'}
        STATES=(*fence_type.STATES,'Applying','Quarantined','Removing','Removed','Completed')

        def new_record(self,request,state):return dict(**super().new_record(request,state),pendingIdentity=None,unlinkConfirmed=False)

        def validate_record(self,value,operation):
            if type(value) is not dict or set(value)!=self.RECORD or type(value['schemaVersion']) is not int or value['schemaVersion']!=1:self.reject('RECEIPT_INVALID')
            self.request({k:value[k] for k in self.REQUEST})
            if value['operationId']!=operation or value['state'] not in self.STATES or type(value['unlinkConfirmed']) is not bool:self.reject('RECEIPT_INVALID')
            identity=value['pendingIdentity'];expected=value['plan']['retainedIdentity']
            if identity is not None and (type(identity) is not list or len(identity)!=9 or any(type(n) is not int for n in identity) or identity[:8]!=expected[:8] or identity[8]<expected[8]):self.reject('RECEIPT_INVALID')
            if value['state'] in ('Preparing','Reserved','Aborted') and (identity is not None or value['unlinkConfirmed']):self.reject('RECEIPT_INVALID')
            if value['state'] in ('Quarantined','Removing','Removed','Completed') and identity is None:self.reject('RECEIPT_INVALID')
            if value['state'] in ('Removed','Completed') and not value['unlinkConfirmed']:self.reject('RECEIPT_INVALID')
            if value['unlinkConfirmed'] and (identity is None or value['state'] not in ('Removed','Completed','Unknown')):self.reject('RECEIPT_INVALID')

        def public(self,record,marker):
            result=super().public(record,marker)
            result['purgeApplied']=True if record['state']=='Completed' else None if record['state']=='Unknown' else False
            return result

        def reconcile(self,state,record,source_state,marker):
            if record['state'] in ('Preparing','Reserved','Aborted') and (not marker or marker['state'] not in ('Deleting','Purged')):
                return super().reconcile(state,record,source_state,marker)
            request={k:record[k] for k in self.REQUEST};same=self.matches(marker,request)
            if record['state']=='Completed' and same and marker['state']=='Purged':return
            if record['state']!='Unknown':record['state']='Unknown';self.save(state,record)
            if marker is None or same or marker['state']=='Released':
                if marker is None or marker['state']!='Unknown':self.write_marker(source_state,request,'Unknown')
            # Do not call source.recover here: observe never renames or deletes.

        @contextlib.contextmanager
        def archive(self,plan,session):
            source=self.source;ref=plan['command']['source'];history=source.history
            with history.target(False) as (configuration,directory),history.gate(directory):
                value=history.read(directory,ref)
                if value['captureState']!='Trashed' or value['historyDigest']!=plan['command']['historyDigest'] or hashlib.sha256(b'whaledeck-container-recycle-purge-history-v1\n'+encode(value)).hexdigest().upper()!=plan['historyMetadataDigest']:self.reject('HISTORY_CHANGED')
                if hashlib.sha256(encode(configuration)).hexdigest().upper()!=plan['storageHash'] or [os.fstat(directory).st_dev,os.fstat(directory).st_ino]!=plan['storeIdentity']:self.reject('HISTORY_CHANGED')
                fd=self.f.open_at(directory,ref['operationId']+'.data',os.O_RDONLY|os.O_NOFOLLOW|os.O_NONBLOCK)
                try:
                    def attest():
                        history.storage.attest(configuration,directory)
                        if source.metadata(os.fstat(fd))!=plan['archiveIdentity'] or source.metadata(os.stat(ref['operationId']+'.data',dir_fd=directory,follow_symlinks=False))!=plan['archiveIdentity']:self.reject('HISTORY_CHANGED')
                    attest();checksum=hashlib.sha256();count=0
                    while chunk:=os.read(fd,self.f.CHUNK):
                        session.revalidate();count+=len(chunk);checksum.update(chunk)
                        if count>plan['archiveBytes']:self.reject('HISTORY_CHANGED')
                    if count!=plan['archiveBytes'] or checksum.hexdigest().upper()!=plan['archiveSha256']:self.reject('HISTORY_CHANGED')
                    attest();yield directory,value,attest;attest()
                finally:os.close(fd)

        def apply(self,request,session):
            with self.locked(request) as (state,record):
                ref=request['plan']['command']['source'];plan=request['plan'];source=self.source
                with source.locked(ref) as (source_state,original):
                    marker=source.read_purge_fence(ref)
                    if record is None:
                        if not self.matches(marker,request):self.reject('NOT_RESERVED')
                        record=self.new_record(request,'Unknown');self.save(state,record)
                    if record['state']!='Reserved':
                        self.reconcile(state,record,source_state,marker);return self.public(record,source.read_purge_fence(ref))
                    self.reconcile(state,record,source_state,marker)
                    if record['state']!='Reserved':return self.public(record,source.read_purge_fence(ref))
                    fresh=self.preflight.plan_locked(plan['command'],original,session,request['operationId'])
                    if encode(fresh)!=encode(plan):self.reject('PLAN_CHANGED')
                    with self.archive(plan,session) as (archive,value,attest),source.parent(ref['plan'],session) as parent:
                        private=source.private(parent,original)
                        try:
                            name=ref['operationId']+'.trash';pending=request['operationId']+'.purging';leaf=ref['plan']['command']['path'].rsplit('/',1)[-1]
                            def guard(name,identity):
                                source.guard(ref['plan']['command'],session,plan['targetProof']);attest()
                                with source.parent(ref['plan'],session):pass
                                checked=source.private(parent,original)
                                try:
                                    if not os.path.samestat(os.fstat(checked),os.fstat(private)):self.reject('SOURCE_CHANGED')
                                    if source.probe_file(checked,name,ref['plan'],session)!=identity or source.present(parent,leaf):self.reject('SOURCE_CHANGED')
                                finally:os.close(checked)
                            guard(name,plan['retainedIdentity'])
                            record['state']='Applying';self.save(state,record);self.checkpoint('Applying')
                            self.write_marker(source_state,request,'Deleting');self.checkpoint('DeletingFenced')
                            original.update(state='Purging',actualIdentity=None);source.save(source_state,original);self.checkpoint('SourcePurging')
                            guard(name,plan['retainedIdentity'])
                            self.f.exchange(private,name,private,pending,1);os.fsync(private);self.checkpoint('QuarantinedNamespace')
                            identity=source.probe_file(private,pending,ref['plan'],session)
                            if identity is None or identity[:8]!=plan['retainedIdentity'][:8] or identity[8]<plan['retainedIdentity'][8]:self.reject('SOURCE_CHANGED')
                            record.update(state='Quarantined',pendingIdentity=identity);self.save(state,record);self.checkpoint('Quarantined')
                            guard(pending,identity)
                            record['state']='Removing';self.save(state,record);self.checkpoint('Removing')
                            guard(pending,identity)
                            removed=self.f.open_at(private,pending,os.O_RDONLY|os.O_NOFOLLOW|os.O_NONBLOCK,cross_mount=True)
                            try:
                                if source.metadata(os.fstat(removed))!=identity:self.reject('SOURCE_CHANGED')
                                guard(pending,identity)
                                os.unlink(pending,dir_fd=private);os.fsync(private);self.checkpoint('Unlinked')
                                after=source.metadata(os.fstat(removed))
                                if after[:5]!=identity[:5] or after[5]!=0 or after[6:8]!=identity[6:8] or after[8]<identity[8]:self.reject('SOURCE_CHANGED')
                                checksum=hashlib.sha256();count=0
                                while chunk:=os.read(removed,self.f.CHUNK):
                                    session.revalidate();count+=len(chunk);checksum.update(chunk)
                                    if count>plan['archiveBytes']:self.reject('SOURCE_CHANGED')
                                if count!=plan['archiveBytes'] or checksum.hexdigest().upper()!=ref['plan']['sha256'] or source.attributes(removed)!=ref['plan']['attributes'] or source.metadata(os.fstat(removed))!=after:self.reject('SOURCE_CHANGED')
                            finally:os.close(removed)
                            if source.present(private,name) or source.present(private,pending) or source.present(parent,leaf):self.reject('SOURCE_CHANGED')
                            record.update(state='Removed',unlinkConfirmed=True);self.save(state,record);self.checkpoint('Removed')
                            attest();source.guard(ref['plan']['command'],session,plan['targetProof'])
                            value['captureState']='Purged';self.f.save_receipt(archive,ref['operationId'],value);self.checkpoint('HistoryPurged')
                            attest();original.update(state='Purged',actualIdentity=None);source.save(source_state,original);self.checkpoint('SourcePurged')
                            self.write_marker(source_state,request,'Purged');self.checkpoint('PurgedFenced')
                            # Exit contexts/attest before committing success.
                        except BaseException:
                            with contextlib.suppress(OSError,self.r.Rejected,self.f.Rejected):self.reconcile(state,record,source_state,source.read_purge_fence(ref))
                            raise
                        finally:os.close(private)
                    record['state']='Completed';self.save(state,record);self.checkpoint('Completed')
                    return self.public(record,source.read_purge_fence(ref))

    return ContainerRecyclePurges
