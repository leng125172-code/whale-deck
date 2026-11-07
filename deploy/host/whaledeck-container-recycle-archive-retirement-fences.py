"""Internal scheduling-only HDD retirement reservation; no deletion or install.

Lock order: retirement journal -> archive -> purge -> original source -> HDD.
The original archive restore claim never changes. A separate source marker
and active-journal scan serialize retirement reservations, including recovery
from a lost marker. Reserved may precede expiry; it is never unlink authority.
"""
import contextlib
import hashlib
import json
import os
import re
import stat


def encode(value):
    return json.dumps(value,sort_keys=True,separators=(',',':'),ensure_ascii=False,allow_nan=False).encode()


def retirement_fences_type(directory_type):
    class ContainerRecycleArchiveRetirementFences(directory_type):
        RECORD={'schemaVersion','operationId','owner','plan','state'}
        STATES=('Preparing','Reserved','Aborted','Unknown')
        MARKER={'schemaVersion','archive','retirementOperationId','retirementOwner','retirementFingerprint','state'}
        LIMIT=65536

        def __init__(self,preflight,state_path):
            self.preflight=preflight;self.copies=preflight.copies;self.source=preflight.source
            super().__init__(self.source.u,self.source.p,state_path)
            self.claims_path=self.state_path/'claims'
            others=(self.source.state_path,self.copies.state_path,self.copies.restore.state_path,self.copies.restore.claims_path,self.copies.preflight.purges.state_path)
            self._other_states=others
            for other in others:
                left,right=self.state_path.resolve(),other.resolve()
                if left==right or left in right.parents or right in left.parents or self.state_path.exists() and other.exists() and os.path.samestat(self.state_path.stat(),other.stat()):raise ValueError('RETIREMENT_STATE_MUST_BE_INDEPENDENT')
                if self.claims_path.resolve()==right or self.claims_path.exists() and other.exists() and os.path.samestat(self.claims_path.stat(),other.stat()):raise ValueError('RETIREMENT_CLAIMS_MUST_BE_INDEPENDENT')

        def reject(self,suffix):raise self.r.Rejected('CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_'+suffix)

        def validate_plan(self,value):self.preflight.validate_plan(value)

        @staticmethod
        def archive(request):return request['plan']['command']['archive']

        def ref(self,request):return self.copies.ref(self.archive(request))

        def request(self,value):
            super().request(value);archive=self.archive(value);purge=archive['plan']['command']['purge']
            if value['operationId'] in (archive['operationId'],purge['operationId'],self.ref(value)['operationId']):self.reject('NEW_OPERATION_REQUIRED')

        def validate_record(self,value,operation):
            if type(value) is not dict or type(value.get('schemaVersion')) is not int:self.reject('RECEIPT_INVALID')
            if value['schemaVersion']==1:
                if set(value)!=self.RECORD or value['state'] not in self.STATES:self.reject('RECEIPT_INVALID')
            elif value['schemaVersion']==2:
                if set(value)!=self.RECORD|{'completion'} or value['state'] not in ('Completed','Unknown'):self.reject('RECEIPT_INVALID')
                if value['completion'] is None:
                    if value['state']!='Unknown':self.reject('RECEIPT_INVALID')
                else:self.validate_completion(value['completion'],{k:value[k] for k in self.REQUEST})
            else:self.reject('RECEIPT_INVALID')
            self.request({k:value[k] for k in self.REQUEST})
            if value['operationId']!=operation:self.reject('RECEIPT_INVALID')

        @staticmethod
        def completion_digest(value):
            return hashlib.sha256(b'whaledeck-container-recycle-archive-retirement-completion-v1\n'+encode({k:v for k,v in value.items() if k!='proofDigest'})).hexdigest().upper()

        def validate_completion(self,value,request):
            # A future executor may persist this certificate only after its
            # own authorized unlink/fsync. No method here creates one.
            if type(value) is not dict or set(value)!={'executionPlan','unlinkConfirmed','unlinkedIdentity','completedAtUnixMs','proofDigest'} or value['unlinkConfirmed'] is not True:self.reject('COMPLETION_INVALID')
            p=value['executionPlan'];self.preflight.validate_plan(p)
            if p['retirementEligible'] is not True or p['bindingCurrent'] is not True or p['retentionElapsed'] is not True:self.reject('COMPLETION_INVALID')
            if encode(value['unlinkedIdentity'])!=encode(p['archiveIdentity']):self.reject('COMPLETION_INVALID')
            stable=lambda x:{k:v for k,v in x.items() if k not in ('fingerprint','retentionElapsed','retirementEligible')}
            if encode(stable(p))!=encode(stable(request['plan'])):self.reject('COMPLETION_INVALID')
            if type(value['completedAtUnixMs']) is not int or not p['notBeforeUnixMs']<=value['completedAtUnixMs']<=self.preflight.MAX_TIME_MS or type(value['proofDigest']) is not str or not re.fullmatch('[A-F0-9]{64}',value['proofDigest']) or value['proofDigest']!=self.completion_digest(value):self.reject('COMPLETION_INVALID')

        @staticmethod
        def new_record(request,state):return dict(schemaVersion=1,**json.loads(encode(request)),state=state)

        def orphan_record(self,request,marker):
            if marker['state']=='Retired':return dict(schemaVersion=2,**json.loads(encode(request)),state='Unknown',completion=None)
            return self.new_record(request,'Aborted' if marker['state']=='Released' else 'Unknown')

        @contextlib.contextmanager
        def locked(self,request):
            with super().locked(request) as (state,record):
                if record is not None and encode(record['plan'])!=encode(request['plan']):self.reject('IDEMPOTENCY_CONFLICT')
                yield state,record

        @contextlib.contextmanager
        def records(self,archive):
            with self.copies.preflight.existing_record(self.copies,archive) as copied,self.copies.source_records(archive) as (deleted,original):
                yield copied,deleted,original

        @contextlib.contextmanager
        def claims(self):
            path=self.claims_path
            if not path.is_absolute() or path!=path.resolve(strict=True):self.reject('STATE_UNSAFE')
            directory=self.f.trusted_directory(path)
            try:
                info=os.fstat(directory)
                if (info.st_uid,info.st_gid,stat.S_IMODE(info.st_mode))!=(0,0,0o700):self.reject('STATE_UNSAFE')
                if any(os.path.samestat(info,p.stat()) for p in (self.state_path,*self._other_states)):self.reject('STATE_UNSAFE')
                yield directory
            finally:os.close(directory)

        def marker(self,request,state):
            return dict(schemaVersion=1,archive=self.archive(request),retirementOperationId=request['operationId'],retirementOwner=request['owner'],retirementFingerprint=request['plan']['fingerprint'],state=state)

        def validate_marker(self,value,source_id):
            if type(value) is not dict or set(value)!=self.MARKER or type(value['schemaVersion']) is not int or value['schemaVersion']!=1 or value['state'] not in ('Reserved','Unknown','Released','Retired'):self.reject('MARKER_INVALID')
            self.copies.request(value['archive']);self.f.identity(value['retirementOperationId'])
            if self.copies.ref(value['archive'])['operationId']!=source_id or any(type(value[k]) is not str or not re.fullmatch('[A-F0-9]{64}',value[k]) for k in ('retirementOwner','retirementFingerprint')):self.reject('MARKER_INVALID')

        def read_marker(self,source_id):
            self.f.identity(source_id)
            with self.claims() as directory:
                try:fd=os.open(source_id+'.json',os.O_RDONLY|os.O_NOFOLLOW|os.O_NONBLOCK|os.O_CLOEXEC,dir_fd=directory)
                except FileNotFoundError:return None
                try:
                    i=os.fstat(fd)
                    if not stat.S_ISREG(i.st_mode) or (i.st_uid,i.st_gid,stat.S_IMODE(i.st_mode),i.st_nlink)!=(0,0,0o600,1) or not 0<i.st_size<=self.LIMIT:self.reject('MARKER_INVALID')
                    raw=os.read(fd,self.LIMIT+1)
                    if len(raw)!=i.st_size:self.reject('MARKER_INVALID')
                    marker=json.loads(raw,object_pairs_hook=self.f.unique_object);self.validate_marker(marker,source_id);return marker
                finally:os.close(fd)

        def matches(self,marker,request):
            return marker is not None and encode(marker)==encode(self.marker(request,marker['state']))

        def write_marker(self,request,state):
            source_id=self.ref(request)['operationId'];value=self.marker(request,state);self.validate_marker(value,source_id)
            if len(encode(value))>self.LIMIT:self.reject('RECEIPT_LIMIT')
            with self.claims() as directory:
                if len(os.listdir(directory))>=20000 and not self.present(directory,source_id+'.json'):self.reject('RECEIPT_LIMIT')
                self.f.save_receipt(directory,source_id,value)
            if encode(self.read_marker(source_id))!=encode(value):self.reject('FENCE_CHANGED')

        def active(self,state,source_id):
            # Caller holds this source's existing lock. Never take a sibling
            # journal lock here: that reverses journal -> source lock order.
            names=os.listdir(state)
            if len(names)>20000:self.reject('RECEIPT_LIMIT')
            result=[]
            for name in names:
                if not name.endswith('.json'):continue
                if not re.fullmatch(r'[a-f0-9]{32}\.json',name):self.reject('STATE_UNSAFE')
                record=self.read(state,name[:-5])
                if record is None:self.reject('STATE_UNSAFE')
                if self.ref(record)['operationId']==source_id and record['state'] in ('Preparing','Reserved','Unknown','Completed'):result.append(record['operationId'])
            return result

        def checkpoint(self,phase):
            """Trusted test cut only; never request data."""

        def reconcile(self,state,record,marker):
            same=self.matches(marker,record)
            if record['state']=='Aborted':
                if same and marker['state']=='Reserved':self.write_marker(record,'Released')
                elif same and marker['state'] in ('Unknown','Retired'):self.reject('FENCE_CHANGED')
                return
            if record['schemaVersion']==2 and record['state']=='Completed' and same and marker['state']=='Retired':return
            if record['state']=='Preparing' and (marker is None or marker['state']=='Released'):
                record['state']='Aborted';self.save(state,record);return
            if record['state']=='Reserved' and same and marker['state']=='Reserved':return
            if record['schemaVersion']==1 and marker is not None and marker['state']=='Retired':
                record.update(schemaVersion=2,state='Unknown',completion=None);self.save(state,record)
            elif record['state']!='Unknown':record['state']='Unknown';self.save(state,record)
            # No marker repair, payload replay or overwrite of another owner.
            # The active journal scan retains exclusion when the index is lost.

        def public(self,state,record,marker):
            active=self.active(state,self.ref(record)['operationId'])
            applied=False if record['schemaVersion']==1 else True if record['state']=='Completed' else None
            return dict(operationId=record['operationId'],sourceOperationId=self.ref(record)['operationId'],archiveOperationId=self.archive(record)['operationId'],fingerprint=record['plan']['fingerprint'],state=record['state'],sourceFenced=bool(active or marker and marker['state']!='Released'),ownedFence=bool(self.matches(marker,record) and marker['state']!='Released'),retirementApplied=applied,notBeforeUnixMs=record['plan']['notBeforeUnixMs'])

        def reserve(self,request,session):
            with self.locked(request) as (state,record),self.records(self.archive(request)) as records:
                source_id=self.ref(request)['operationId'];marker=self.read_marker(source_id)
                if record is not None:
                    self.reconcile(state,record,marker);return self.public(state,record,self.read_marker(source_id))
                if self.matches(marker,request):
                    record=self.orphan_record(request,marker);self.save(state,record)
                    return self.public(state,record,marker)
                if marker is not None and marker['state']!='Released' or self.active(state,source_id):self.reject('SOURCE_BUSY')
                fresh=self.preflight.plan_locked(request['plan']['command'],session,*records)
                if encode(fresh)!=encode(request['plan']):self.reject('PLAN_CHANGED')
                if not fresh['bindingCurrent']:self.reject('COPY_UNAVAILABLE')
                record=self.new_record(request,'Preparing');self.save(state,record);self.checkpoint('Preparing')
                self.write_marker(request,'Reserved');self.checkpoint('SourceFenced')
                record['state']='Reserved';self.save(state,record);self.checkpoint('Reserved')
                return self.public(state,record,self.read_marker(source_id))

        def observe(self,request,session,discard):
            with self.locked(request) as (state,record),self.records(self.archive(request)):
                source_id=self.ref(request)['operationId'];marker=self.read_marker(source_id)
                if record is None:
                    same=self.matches(marker,request)
                    if not discard and not same:self.reject('NOT_FOUND')
                    record=self.orphan_record(request,marker) if same else self.new_record(request,'Aborted');self.save(state,record)
                self.reconcile(state,record,marker);marker=self.read_marker(source_id)
                if discard and record['state']=='Reserved':
                    if session is None:self.reject('SESSION_REQUIRED')
                    archive=self.archive(request);ref=self.ref(request)
                    # Cancel releases only scheduling metadata, even after the
                    # deadline or live copy loss. It never releases archive data
                    # or the original restore claim and cannot cancel Unknown.
                    self.source.guard(ref['plan']['command'],session,archive['plan']['targetProof'])
                    record['state']='Aborted';self.save(state,record);self.checkpoint('AbortedSaved')
                    self.write_marker(request,'Released');self.checkpoint('Released');marker=self.read_marker(source_id)
                return self.public(state,record,marker)

        def inspect(self,request,session=None):return self.observe(request,session,False)
        def discard(self,request,session=None):return self.observe(request,session,True)

        def plan(self,command,session):
            self.preflight.command(command)
            with self.records(command['archive']) as records:
                if not self.state_path.is_absolute() or self.state_path!=self.state_path.resolve(strict=True):self.reject('STATE_UNSAFE')
                state=self.f.trusted_directory(self.state_path)
                try:
                    if stat.S_IMODE(os.fstat(state).st_mode)!=0o700:self.reject('STATE_UNSAFE')
                    source_id=self.copies.ref(command['archive'])['operationId'];marker=self.read_marker(source_id)
                    if marker is not None and marker['state']!='Released' or self.active(state,source_id):self.reject('SOURCE_BUSY')
                    return self.preflight.plan_locked(command,session,*records)
                finally:os.close(state)

        def apply(self,*args,**kwargs):self.reject('RESERVATION_ONLY')
        def recover(self,*args,**kwargs):self.reject('RESERVATION_ONLY')
        def private(self,*args,**kwargs):self.reject('RESERVATION_ONLY')

    return ContainerRecycleArchiveRetirementFences
