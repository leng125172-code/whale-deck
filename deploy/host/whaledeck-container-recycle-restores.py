"""Internal independent retained-inode restoration journal and source claim.

No CLI, RPC, installation or user-selected paths. The future authenticated C#
workflow must pin this complete plan and acquire shared publication exclusion.
Every restore uses a new operation ID; it never replays the original Trash.
"""
import contextlib
import hashlib
import json
import os
import re
import stat


def encode(value):
    return json.dumps(value, sort_keys=True, ensure_ascii=False, separators=(',', ':'), allow_nan=False).encode()


def restores_type(directory_type):
    class ContainerRecycleRestores(directory_type):
        COMMAND = {'source', 'historyDigest'}
        PLAN = {'schemaVersion', 'command', 'targetProof', 'parentIdentity', 'parentVersion', 'uid', 'gid', 'privateIdentity', 'retainedIdentity', 'fingerprint'}
        RECORD = {'schemaVersion', 'operationId', 'owner', 'plan', 'state', 'claimed', 'historyVerified', 'historyDigest', 'sourceOutcome'}
        TERMINAL = ('Completed', 'Aborted', 'Unknown')
        LIMIT = 65536

        def __init__(self, source, state_path):
            super().__init__(source.u, source.p, state_path)
            if self.state_path.resolve()==source.state_path.resolve() or self.state_path.exists() and source.state_path.exists() and os.path.samestat(self.state_path.stat(),source.state_path.stat()): raise ValueError('RESTORE_STATE_MUST_BE_INDEPENDENT')
            self.source = source
            self.claims_path = self.state_path/'claims'
            if source.restore_claims_path is not None and source.restore_claims_path != self.claims_path:
                raise ValueError('RESTORE_CLAIMS_MUST_BE_SHARED')
            source.restore_claims_path = self.claims_path

        def reject(self, suffix): raise self.r.Rejected('CONTAINER_RECYCLE_RESTORE_'+suffix)

        @staticmethod
        def digest(value):
            return hashlib.sha256(b'whaledeck-container-recycle-restore-plan-v1\n'+encode({k:v for k,v in value.items() if k!='fingerprint'})).hexdigest().upper()

        def command(self, value):
            if type(value) is not dict or set(value)!=self.COMMAND: self.reject('REQUEST_INVALID')
            self.source.request(value['source'])
            if type(value['historyDigest']) is not str or not re.fullmatch('[A-F0-9]{64}', value['historyDigest']): self.reject('SOURCE_INVALID')

        def validate_plan(self, value):
            if type(value) is not dict or set(value)!=self.PLAN or type(value['schemaVersion']) is not int or value['schemaVersion']!=1: self.reject('PLAN_INVALID')
            self.command(value['command']); original=value['command']['source']['plan']
            if any(type(value[k]) is not int or not 0<=value[k]<2**32-1 for k in ('uid','gid')):self.reject('PLAN_INVALID')
            parent=value['parentIdentity']
            if type(parent) is not list or len(parent)!=2 or any(type(n) is not int or not 0<=n<=2**64-1 for n in parent) or parent[1]==0:self.reject('PLAN_INVALID')
            if value['parentIdentity']!=original['parentIdentity'] or value['targetProof']!=original['targetProof'] or (value['uid'],value['gid'])!=(original['uid'],original['gid']): self.reject('PLAN_INVALID')
            private=value['privateIdentity']; retained=value['retainedIdentity']
            if type(private) is not list or len(private)!=2 or any(type(n) is not int or not 0<=n<=2**64-1 for n in private) or private[1]==0: self.reject('PLAN_INVALID')
            if type(retained) is not list or len(retained)!=9 or any(type(n) is not int for n in retained) or retained[:8]!=original['sourceIdentity'][:8] or not original['sourceIdentity'][8]<=retained[8]<2**63: self.reject('PLAN_INVALID')
            if type(value['parentVersion']) is not str or not re.fullmatch('[A-F0-9]{64}',value['parentVersion']) or value['fingerprint']!=self.digest(value): self.reject('PLAN_INVALID')

        def request(self, value):
            super().request(value)
            if value['operationId']==value['plan']['command']['source']['operationId']: self.reject('NEW_OPERATION_REQUIRED')

        def validate_record(self, value, operation):
            if type(value) is not dict or set(value)!=self.RECORD or type(value['schemaVersion']) is not int or value['schemaVersion']!=1: self.reject('RECEIPT_INVALID')
            self.request({k:value[k] for k in self.REQUEST})
            if value['operationId']!=operation or value['state'] not in (*self.TERMINAL,'Preparing','Applying'): self.reject('RECEIPT_INVALID')
            if type(value['claimed']) is not bool or type(value['historyVerified']) is not bool or value['sourceOutcome'] not in (None,'Trashed','Restored','Unknown','Aborted'): self.reject('RECEIPT_INVALID')
            if value['historyDigest']!=(value['plan']['command']['historyDigest'] if value['historyVerified'] else ''): self.reject('RECEIPT_INVALID')
            if value['claimed']!=value['historyVerified']: self.reject('RECEIPT_INVALID')
            if value['state'] in ('Applying','Completed') and not value['claimed']: self.reject('RECEIPT_INVALID')
            if value['state']=='Completed' and value['sourceOutcome']!='Restored': self.reject('RECEIPT_INVALID')
            if value['state'] in ('Preparing','Applying') and value['sourceOutcome'] is not None:self.reject('RECEIPT_INVALID')
            if value['state']=='Preparing' and value['claimed']:self.reject('RECEIPT_INVALID')
            if value['state']=='Aborted' and value['sourceOutcome']!=('Trashed' if value['claimed'] else None):self.reject('RECEIPT_INVALID')
            if value['state']=='Unknown' and not value['claimed']:self.reject('RECEIPT_INVALID')

        def new_record(self, request, phase):
            return dict(schemaVersion=1,**json.loads(encode(request)),state=phase,claimed=False,historyVerified=False,historyDigest='',sourceOutcome=None)

        @contextlib.contextmanager
        def claims(self):
            path=self.claims_path
            if not path.is_absolute() or path!=path.resolve(strict=True): self.reject('STATE_UNSAFE')
            fd=self.f.trusted_directory(path)
            try:
                if stat.S_IMODE(os.fstat(fd).st_mode)!=0o700: self.reject('STATE_UNSAFE')
                yield fd
            finally: os.close(fd)

        @staticmethod
        def claim_matches(claim, request):
            return claim is not None and claim['restoreOperationId']==request['operationId'] and claim['restoreOwner']==request['owner'] and claim['restoreFingerprint']==request['plan']['fingerprint']

        def claim(self, request):
            return dict(schemaVersion=1,source=request['plan']['command']['source'],restoreOperationId=request['operationId'],restoreOwner=request['owner'],restoreFingerprint=request['plan']['fingerprint'])

        def write_claim(self, request):
            self.source.check_purge_fence(request['plan']['command']['source'])
            value=self.claim(request)
            if len(json.dumps(value,separators=(',',':')).encode())>65536:self.reject('RECEIPT_LIMIT')
            with self.claims() as directory:
                name=request['plan']['command']['source']['operationId']
                if len(os.listdir(directory))>=20000 and not self.present(directory,name+'.json'):self.reject('RECEIPT_LIMIT')
                self.f.save_receipt(directory,name,value)

        def clear_claim(self, request):
            original=request['plan']['command']['source']; claim=self.source.read_restore_claim(original)
            if claim is None: return
            if not self.claim_matches(claim,request): self.reject('SOURCE_BUSY')
            with self.claims() as directory:
                os.unlink(original['operationId']+'.json',dir_fd=directory);os.fsync(directory)

        def source_plan(self, command, record, session, purge_id=None):
            original=command['source']; plan=original['plan']
            self.source.check_purge_fence(original,purge_id)
            if record is None or record['state']!='Trashed' or not record['historyVerified'] or record['historyDigest']!=command['historyDigest']: self.reject('SOURCE_CHANGED')
            if self.source.read_restore_claim(original) is not None: self.reject('SOURCE_BUSY')
            if session is None: self.reject('SESSION_REQUIRED')
            with self.source.parent(plan,session) as parent:
                leaf=plan['command']['path'].rsplit('/',1)[-1]
                if self.source.present(parent,leaf): self.reject('TARGET_EXISTS')
                private=self.source.private(parent,record)
                try:
                    retained=self.source.probe_file(private,record['operationId']+'.trash',plan,session)
                    if retained is None or retained!=record['actualIdentity']: self.reject('SOURCE_CHANGED')
                    self.source.history.verify(original,command['historyDigest'])
                    info=os.fstat(parent)
                    result=dict(schemaVersion=1,command=json.loads(encode(command)),targetProof=plan['targetProof'],parentIdentity=[info.st_dev,info.st_ino],
                        parentVersion=self.r.version(plan['command'],plan['command']['path'].rpartition('/')[0],info),uid=plan['uid'],gid=plan['gid'],privateIdentity=list(record['privateIdentity']),retainedIdentity=retained)
                    self.source.guard(plan['command'],session,plan['targetProof'])
                    if self.source.probe_file(private,record['operationId']+'.trash',plan,session)!=retained or self.source.present(parent,leaf): self.reject('SOURCE_CHANGED')
                finally: os.close(private)
            result['fingerprint']=self.digest(result);self.validate_plan(result);return result

        def plan(self, command, session):
            self.command(command)
            with self.source.locked(command['source']) as (_,record): return self.source_plan(command,record,session)

        def checkpoint(self, phase):
            """Trusted test cut only; never request data."""

        def recover_locked(self, state, record, source_state, original, session):
            request={k:record[k] for k in self.REQUEST}; ref=record['plan']['command']['source']; claim=self.source.read_restore_claim(ref)
            if claim is None and record['claimed'] and record['state']!='Aborted':
                # Re-establish this known durable source fence if its index is lost;
                # never infer an unobserved result or allow a legacy late rename.
                self.write_claim(request);claim=self.source.read_restore_claim(ref)
                if record['state'] not in self.TERMINAL:
                    record.update(state='Unknown',sourceOutcome=None);self.save(state,record);return
            if record['state'] in self.TERMINAL:
                if record['state']=='Aborted' and self.claim_matches(claim,request): self.clear_claim(request)
                return
            if claim is None:
                record.update(state='Aborted' if not record['claimed'] else 'Unknown',sourceOutcome=None)
                self.save(state,record);return
            if not self.claim_matches(claim,request):
                record.update(state='Aborted' if not record['claimed'] else 'Unknown',sourceOutcome=None);self.save(state,record);return
            record.update(claimed=True,historyVerified=True,historyDigest=record['plan']['command']['historyDigest'])
            if original is not None: self.source.recover(source_state,original,session)
            outcome=None if original is None else original['state']; record['sourceOutcome']=outcome
            # The source claim is fsynced before Applying and before any rename.
            # Only this operation can mutate the claimed original source.
            if record['state']=='Applying' and outcome=='Restored': record['state']='Completed'
            elif outcome=='Trashed' and original['actualIdentity']==record['plan']['retainedIdentity']: record['state']='Aborted'
            else: record['state']='Unknown'
            self.save(state,record)
            if record['state']=='Aborted':
                self.checkpoint('AbortedSaved');self.clear_claim(request)

        def apply(self, request, session):
            with self.locked(request) as (state,record):
                original_ref=request['plan']['command']['source']
                with self.source.locked(original_ref) as (source_state,original):
                    claim=self.source.read_restore_claim(original_ref)
                    if record is not None:
                        self.recover_locked(state,record,source_state,original,session)
                        return self.public(record,original,session,True)
                    record=self.new_record(request,'Preparing');self.save(state,record);self.checkpoint('Preparing')
                    if claim is not None:
                        if self.claim_matches(claim,request):
                            record.update(state='Unknown',claimed=True,historyVerified=True,historyDigest=request['plan']['command']['historyDigest'])
                        else: record['state']='Aborted'
                        self.save(state,record);return self.public(record,original,session,False)
                    try:
                        fresh=self.source_plan(request['plan']['command'],original,session)
                        if fresh!=request['plan']: self.reject('PLAN_CHANGED')
                        self.write_claim(request)
                        self.checkpoint('SourceClaimed')
                        record.update(state='Applying',claimed=True,historyVerified=True,historyDigest=request['plan']['command']['historyDigest']);self.save(state,record);self.checkpoint('Applying')
                        self.source.restore_locked(original_ref,source_state,original,session,restore_id=request['operationId'])
                        self.checkpoint('SourceReturned')
                        self.recover_locked(state,record,source_state,original,session)
                        return self.public(record,original,session,False)
                    except BaseException:
                        # Preserve the original error; observation never resumes a rename.
                        with contextlib.suppress(OSError,self.r.Rejected,self.f.Rejected):self.recover_locked(state,record,source_state,original,session)
                        raise

        def observe(self, request, session, discard):
            with self.locked(request) as (state,record):
                ref=request['plan']['command']['source']
                with self.source.locked(ref) as (source_state,original):
                    claim=self.source.read_restore_claim(ref)
                    if record is None:
                        if not discard and not self.claim_matches(claim,request):self.reject('NOT_FOUND')
                        orphan=self.claim_matches(claim,request)
                        record=self.new_record(request,'Unknown' if orphan else 'Aborted')
                        if orphan: record.update(claimed=True,historyVerified=True,historyDigest=request['plan']['command']['historyDigest'])
                        self.save(state,record)
                    else:self.recover_locked(state,record,source_state,original,session)
                    return self.public(record,original,session,True)

        def inspect(self, request, session=None): return self.observe(request,session,False)
        def discard(self, request, session=None): return self.observe(request,session,True)

        def public(self, record, original, session, replayed):
            current=None
            if original is not None:
                current=self.source.public(original,session,True)
            outcome=record['sourceOutcome']
            available=bool(current and current['historyAvailable'] and record['historyVerified'] and current['historyDigest']==record['historyDigest'])
            return dict(operationId=record['operationId'],sourceOperationId=record['plan']['command']['source']['operationId'],fingerprint=record['plan']['fingerprint'],targetProof=record['plan']['targetProof'],
                state=record['state'],bindingCurrent=bool(record['state']=='Completed' and available and current['state']=='Restored' and current['bindingCurrent']),replayed=replayed,
                historyVerified=record['historyVerified'],historyDigest=record['historyDigest'],historyAvailable=available,sourceState=outcome,
                sourceRetained=None if record['state']=='Unknown' or outcome is None or outcome=='Unknown' else outcome=='Trashed',sourceRestored=None if record['state']=='Unknown' or outcome is None or outcome=='Unknown' else outcome=='Restored',
                restoredByThisOperation=True if record['state']=='Completed' else False if record['state']=='Aborted' else None)

    return ContainerRecycleRestores
