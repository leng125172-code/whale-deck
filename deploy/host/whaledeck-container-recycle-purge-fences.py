"""Internal durable purge reservation only: no deletion, CLI or installation.

Lock order: own journal -> original source -> HDD gate. Restore uses its own
journal -> the same source lock. Released source markers remain as tombstones.
Future deletion must add its own durable phase before any irreversible change;
this reservation layer must never be treated as authority to unlink a payload.
"""
import json
import os


def encode(value):
    return json.dumps(value,sort_keys=True,separators=(',',':'),ensure_ascii=False,allow_nan=False).encode()


def purge_fences_type(directory_type):
    class ContainerRecyclePurgeFences(directory_type):
        RECORD={'schemaVersion','operationId','owner','plan','state'}
        STATES=('Preparing','Reserved','Aborted','Unknown')
        LIMIT=65536

        def __init__(self,preflight,state_path):
            source=preflight._source
            super().__init__(source.u,source.p,state_path)
            for other in (source.state_path,preflight._restore.state_path):
                if self.state_path.resolve()==other.resolve() or self.state_path.exists() and other.exists() and os.path.samestat(self.state_path.stat(),other.stat()):raise ValueError('PURGE_STATE_MUST_BE_INDEPENDENT')
            self.preflight=preflight;self.source=source

        def reject(self,suffix):raise self.r.Rejected('CONTAINER_RECYCLE_PURGE_'+suffix)

        def validate_plan(self,value):self.preflight.validate_plan(value)

        def request(self,value):
            super().request(value)
            if value['operationId']==value['plan']['command']['source']['operationId']:self.reject('NEW_OPERATION_REQUIRED')

        def validate_record(self,value,operation):
            if type(value) is not dict or set(value)!=self.RECORD or type(value['schemaVersion']) is not int or value['schemaVersion']!=1:self.reject('RECEIPT_INVALID')
            self.request({k:value[k] for k in self.REQUEST})
            if value['operationId']!=operation or value['state'] not in self.STATES:self.reject('RECEIPT_INVALID')

        def new_record(self,request,state):return dict(schemaVersion=1,**json.loads(encode(request)),state=state)

        @staticmethod
        def matches(marker,request):
            return marker is not None and (marker['operationId'],marker['owner'],marker['fingerprint'])==(request['operationId'],request['owner'],request['plan']['fingerprint'])

        def write_marker(self,state,request,phase):
            ref=request['plan']['command']['source']
            marker=dict(schemaVersion=1,source=ref,operationId=request['operationId'],owner=request['owner'],fingerprint=request['plan']['fingerprint'],state=phase)
            self.f.save_receipt(state,ref['operationId']+'.purge',marker)
            current=self.source.read_purge_fence(ref)
            if encode(current)!=encode(marker):self.reject('FENCE_CHANGED')

        def checkpoint(self,phase):
            """Trusted test cut only, never request data."""

        def reconcile(self,state,record,source_state,marker):
            request={k:record[k] for k in self.REQUEST}
            same=self.matches(marker,request)
            if record['state']=='Aborted':
                if same and marker['state']=='Reserved':self.write_marker(source_state,request,'Released')
                elif same and marker['state']=='Unknown':self.reject('FENCE_CHANGED')
                return
            if record['state']=='Preparing' and not same:
                record['state']='Aborted';self.save(state,record);return
            if record['state']=='Reserved' and same and marker['state']=='Reserved':return
            # A missing, conflicting or half-written reservation is not success
            # and never permits automatic replay. Do not overwrite another ID.
            if record['state']!='Unknown':record['state']='Unknown';self.save(state,record)
            if marker is None or same or marker['state']=='Released':
                if marker is None or marker['state']!='Unknown':self.write_marker(source_state,request,'Unknown')

        def public(self,record,marker):
            return dict(operationId=record['operationId'],sourceOperationId=record['plan']['command']['source']['operationId'],fingerprint=record['plan']['fingerprint'],state=record['state'],
                sourceFenced=bool(marker and marker['state']!='Released'),ownedFence=bool(self.matches(marker,record) and marker['state']!='Released'),purgeApplied=False)

        def reserve(self,request,session):
            with self.locked(request) as (state,record):
                ref=request['plan']['command']['source']
                with self.source.locked(ref) as (source_state,original):
                    marker=self.source.read_purge_fence(ref)
                    if record is not None:
                        self.reconcile(state,record,source_state,marker)
                        return self.public(record,self.source.read_purge_fence(ref))
                    if self.matches(marker,request) and marker['state']=='Released':
                        record=self.new_record(request,'Aborted');self.save(state,record)
                        return self.public(record,marker)
                    if marker is not None and marker['state']!='Released':
                        if not self.matches(marker,request):self.reject('SOURCE_BUSY')
                        record=self.new_record(request,'Unknown');self.save(state,record)
                        self.write_marker(source_state,request,'Unknown');return self.public(record,self.source.read_purge_fence(ref))
                    fresh=self.preflight.plan_locked(request['plan']['command'],original,session)
                    if encode(fresh)!=encode(request['plan']):self.reject('PLAN_CHANGED')
                    record=self.new_record(request,'Preparing');self.save(state,record);self.checkpoint('Preparing')
                    self.write_marker(source_state,request,'Reserved');self.checkpoint('SourceFenced')
                    record['state']='Reserved';self.save(state,record);self.checkpoint('Reserved')
                    return self.public(record,self.source.read_purge_fence(ref))

        def observe(self,request,session,discard):
            with self.locked(request) as (state,record):
                ref=request['plan']['command']['source']
                with self.source.locked(ref) as (source_state,original):
                    marker=self.source.read_purge_fence(ref)
                    if record is None:
                        same=self.matches(marker,request)
                        if not discard and not same:self.reject('NOT_FOUND')
                        record=self.new_record(request,'Unknown' if same and marker['state']!='Released' else 'Aborted');self.save(state,record)
                    self.reconcile(state,record,source_state,marker)
                    marker=self.source.read_purge_fence(ref)
                    if discard and record['state']=='Reserved':
                        # This implementation has no Applying/deletion phase.
                        # Verify both copies before releasing an explicit cancel.
                        fresh=self.preflight.plan_locked(request['plan']['command'],original,session,request['operationId'])
                        if encode(fresh)!=encode(request['plan']):self.reject('PLAN_CHANGED')
                        record['state']='Aborted';self.save(state,record);self.checkpoint('AbortedSaved')
                        self.write_marker(source_state,request,'Released');self.checkpoint('Released')
                        marker=self.source.read_purge_fence(ref)
                    return self.public(record,marker)

        def inspect(self,request,session=None):return self.observe(request,session,False)
        def discard(self,request,session=None):return self.observe(request,session,True)

    return ContainerRecyclePurgeFences
