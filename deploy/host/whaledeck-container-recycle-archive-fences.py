"""Independent archive-copy reservation journal; no payload copy or publish.

Lock order: own archive journal -> completed purge journal -> original source
-> existing HDD gate during preflight. The shared restore claim schema is a
source mutex only: this new operation and archive-plan fingerprint identify an
archive reservation, never a retained-inode restore result. Original purge,
source, marker and HDD lifecycle remain unchanged.
"""
import contextlib
import json
import os


def encode(value):
    return json.dumps(value,sort_keys=True,separators=(',',':'),ensure_ascii=False,allow_nan=False).encode()


def archive_fences_type(directory_type):
    class ContainerRecycleArchiveFences(directory_type):
        RECORD={'schemaVersion','operationId','owner','plan','state'}
        STATES=('Preparing','Reserved','Aborted','Unknown')
        LIMIT=65536

        def __init__(self,preflight,state_path):
            self.preflight=preflight;self.source=preflight.source
            self.restore=preflight.purges.preflight._restore
            super().__init__(self.source.u,self.source.p,state_path)
            for other in (self.source.state_path,preflight.purges.state_path,self.restore.state_path,self.restore.claims_path):
                left,right=self.state_path.resolve(),other.resolve()
                if left==right or left in right.parents or right in left.parents or self.state_path.exists() and other.exists() and os.path.samestat(self.state_path.stat(),other.stat()):raise ValueError('ARCHIVE_STATE_MUST_BE_INDEPENDENT')

        def reject(self,suffix):raise self.r.Rejected('CONTAINER_RECYCLE_ARCHIVE_'+suffix)

        def validate_plan(self,value):self.preflight.validate_plan(value)

        def plan(self,command,session):return self.preflight.plan(command,session)

        def apply(self,request,session):
            # The directory base's Apply must never become an archive entry.
            self.reject('RESERVATION_ONLY')

        def request(self,value):
            super().request(value)
            purge=value['plan']['command']['purge'];ref=purge['plan']['command']['source']
            if value['operationId'] in (purge['operationId'],ref['operationId']):self.reject('NEW_OPERATION_REQUIRED')

        def validate_record(self,value,operation):
            if type(value) is not dict or set(value)!=self.RECORD or type(value['schemaVersion']) is not int or value['schemaVersion']!=1:self.reject('RECEIPT_INVALID')
            self.request({k:value[k] for k in self.REQUEST})
            if value['operationId']!=operation or value['state'] not in self.STATES:self.reject('RECEIPT_INVALID')

        @staticmethod
        def new_record(request,state):return dict(schemaVersion=1,**json.loads(encode(request)),state=state)

        @staticmethod
        def ref(request):return request['plan']['command']['purge']['plan']['command']['source']

        def claim(self,request):
            return dict(schemaVersion=1,source=self.ref(request),restoreOperationId=request['operationId'],restoreOwner=request['owner'],restoreFingerprint=request['plan']['fingerprint'])

        def matches(self,claim,request):return claim is not None and encode(claim)==encode(self.claim(request))

        def write_claim(self,request):
            current=self.source.read_restore_claim(self.ref(request))
            if current is not None:
                if not self.matches(current,request):self.reject('SOURCE_BUSY')
                return
            value=self.claim(request)
            if len(encode(value))>65536:self.reject('RECEIPT_LIMIT')
            with self.restore.claims() as directory:
                if len(os.listdir(directory))>=20000:self.reject('RECEIPT_LIMIT')
                self.f.save_receipt(directory,self.ref(request)['operationId'],value)
            if not self.matches(self.source.read_restore_claim(self.ref(request)),request):self.reject('FENCE_CHANGED')

        def clear_claim(self,request):
            current=self.source.read_restore_claim(self.ref(request))
            if current is None:return
            if not self.matches(current,request):self.reject('SOURCE_BUSY')
            with self.restore.claims() as directory:
                os.unlink(self.ref(request)['operationId']+'.json',dir_fd=directory);os.fsync(directory)

        @contextlib.contextmanager
        def source_records(self,request):
            purge=request['plan']['command']['purge']
            with self.preflight.existing_record(self.preflight.purges,purge) as deleted,self.preflight.existing_record(self.source,self.ref(request)) as original:
                yield deleted,original

        def fresh(self,request,session,deleted,original,claimed=False):
            value=self.preflight.plan_locked(request['plan']['command'],session,deleted,original,self.claim(request) if claimed else None)
            if encode(value)!=encode(request['plan']):self.reject('PLAN_CHANGED')

        def checkpoint(self,phase):
            """Trusted test cut only; never request data."""

        def reconcile(self,state,record):
            request={k:record[k] for k in self.REQUEST};claim=self.source.read_restore_claim(self.ref(request));same=self.matches(claim,request)
            if record['state']=='Aborted':
                if same:self.clear_claim(request)
                return
            if record['state']=='Preparing':
                record['state']='Unknown' if same else 'Aborted';self.save(state,record)
                return
            if record['state']=='Reserved' and same:return
            if record['state']!='Unknown':record['state']='Unknown';self.save(state,record)
            # A durable reservation cannot be silently reused after its claim
            # index disappears. Restore the known protection, never a payload.
            if claim is None:self.write_claim(request)

        def public(self,record):
            claim=self.source.read_restore_claim(self.ref(record));purge=record['plan']['command']['purge']
            return dict(operationId=record['operationId'],sourceOperationId=self.ref(record)['operationId'],purgeOperationId=purge['operationId'],fingerprint=record['plan']['fingerprint'],state=record['state'],sourceFenced=claim is not None,ownedFence=self.matches(claim,record),archiveCopied=False)

        def reserve(self,request,session):
            with self.locked(request) as (state,record),self.source_records(request) as (deleted,original):
                if record is not None:
                    self.reconcile(state,record);return self.public(record)
                current=self.source.read_restore_claim(self.ref(request))
                if current is not None:
                    if not self.matches(current,request):self.reject('SOURCE_BUSY')
                    record=self.new_record(request,'Unknown');self.save(state,record);return self.public(record)
                self.fresh(request,session,deleted,original)
                record=self.new_record(request,'Preparing');self.save(state,record);self.checkpoint('Preparing')
                self.write_claim(request);self.checkpoint('SourceClaimed')
                record['state']='Reserved';self.save(state,record);self.checkpoint('Reserved')
                return self.public(record)

        def observe(self,request,session,discard):
            with self.locked(request) as (state,record),self.source_records(request) as (deleted,original):
                if record is None:
                    claim=self.source.read_restore_claim(self.ref(request));same=self.matches(claim,request)
                    if not discard and not same:self.reject('NOT_FOUND')
                    record=self.new_record(request,'Unknown' if same else 'Aborted');self.save(state,record)
                self.reconcile(state,record)
                if discard and record['state']=='Reserved':
                    self.fresh(request,session,deleted,original,True)
                    record['state']='Aborted';self.save(state,record);self.checkpoint('AbortedSaved')
                    self.clear_claim(request);self.checkpoint('Released')
                return self.public(record)

        def inspect(self,request,session=None):return self.observe(request,session,False)
        def discard(self,request,session=None):return self.observe(request,session,True)

    return ContainerRecycleArchiveFences
