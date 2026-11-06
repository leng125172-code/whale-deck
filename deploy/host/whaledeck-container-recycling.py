"""Internal ordinary-file Trash capture/retention/empty-path restore kernel.

No CLI/RPC/installation or host-root alias. A future permanent workflow must
authorize and fence the path before calling this kernel. Originals purge is a
separate candidate module; its Purging/Purged source/history states are read
here, without exposing deletion through these capture/restore methods. No
overwrite, recursive removal or automatic uncertain namespace replay.
"""
import base64
import contextlib
import fcntl
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import time


def encode(value):
    return json.dumps(value, sort_keys=True, ensure_ascii=False, separators=(',', ':'), allow_nan=False).encode()


def history_type(base_history):
    class ContainerRecycleHistory(base_history):
        NAMESPACE = 'container-recycling-v1'
        DOMAIN = b'whaledeck-container-recycle-history-v1\n'

        def reject(self, suffix): raise self.f.Rejected('CONTAINER_RECYCLE_' + suffix)

        def expected(self, request):
            return dict(schemaVersion=1, resourceType='ContainerFileTrash', storageId='database-platform.backup.hdd',
                operationId=request['operationId'], owner=request['owner'], plan=request['plan'],
                sizeBytes=request['plan']['sourceIdentity'][6], sha256=request['plan']['sha256'], captureKind='Trash')

        def digest(self, value):
            return hashlib.sha256(self.DOMAIN + encode({k:v for k,v in value.items() if k not in ('historyDigest','state','captureState')})).hexdigest().upper()

        def has_artifacts(self, request):
            try:
                with self.target(False) as (_,directory):
                    for suffix in ('.json','.data','.partial'):
                        try:os.stat(request['operationId']+suffix,dir_fd=directory,follow_symlinks=False);return True
                        except FileNotFoundError:pass
            except FileNotFoundError:return False
            return False

        @contextlib.contextmanager
        def gate(self, directory):
            fd=os.open('history.lock',os.O_RDWR|os.O_CREAT|os.O_NOFOLLOW|os.O_CLOEXEC,0o600,dir_fd=directory)
            try:
                i=os.fstat(fd)
                if (i.st_uid,i.st_gid,stat.S_IMODE(i.st_mode),i.st_nlink,i.st_size)!=(0,0,0o600,1,0) or not stat.S_ISREG(i.st_mode):self.reject('HISTORY_UNSAFE')
                try:fcntl.flock(fd,fcntl.LOCK_EX|fcntl.LOCK_NB)
                except BlockingIOError:self.reject('BUSY')
                yield
            finally:os.close(fd)

        def read(self, directory, request):
            value=self.storage.read_json(directory,request['operationId']+'.json',131072)
            expected=self.expected(request)
            if set(value)!=set(expected)|{'state','captureState','historyDigest','createdAtUnixMs','differentSourceDevice'} or any(value[k]!=v for k,v in expected.items()):self.reject('HISTORY_CHANGED')
            if value['state']!='Verified' or value['captureState'] not in ('Pending','Trashed','Restored','Canceled','Purged'):self.reject('HISTORY_INCOMPLETE')
            if type(value['createdAtUnixMs']) is not int or not 0<=value['createdAtUnixMs']<=253402300799999 or type(value['differentSourceDevice']) is not bool or value['historyDigest']!=self.digest(value):self.reject('HISTORY_CHANGED')
            self.storage.verify_data(directory,request['operationId'],expected)
            return value

        def capture(self, request, parent, leaf, validate):
            op=self.f.identity(request['operationId'])
            with self.target(True) as (configuration,directory),self.gate(directory):
                try:return self.read(directory,request)['historyDigest']
                except FileNotFoundError:
                    if os.path.exists('/proc/self/fd/'+str(directory)+'/'+op+'.json'):self.reject('HISTORY_INCOMPLETE')
                    if any(os.path.lexists('/proc/self/fd/'+str(directory)+'/'+op+suffix) for suffix in ('.data','.partial')):self.reject('HISTORY_INCOMPLETE')
                if sum(n.endswith('.json') for n in os.listdir(directory))>=configuration['maxVersions']:self.reject('HISTORY_LIMIT')
                self.storage.check_capacity(directory,configuration,request['plan']['sourceIdentity'][6]+131072)
                validate();source=self.f.open_at(parent,leaf,os.O_RDONLY|os.O_NONBLOCK|os.O_NOFOLLOW,cross_mount=True);output=None
                try:
                    before=os.fstat(source)
                    if list(self.f.publication_identity(before))!=request['plan']['sourceIdentity'][:8]:self.reject('SOURCE_CHANGED')
                    try:output=os.open(op+'.partial',os.O_WRONLY|os.O_CREAT|os.O_EXCL|os.O_NOFOLLOW|os.O_CLOEXEC,0o600,dir_fd=directory)
                    except FileExistsError:self.reject('HISTORY_INCOMPLETE')
                    value={**self.expected(request),'state':'Preparing','captureState':'Pending','createdAtUnixMs':time.time_ns()//1000000,'differentSourceDevice':before.st_dev!=os.fstat(directory).st_dev}
                    value['historyDigest']=self.digest(value);self.f.save_receipt(directory,op,value)
                    checksum=hashlib.sha256();count=0
                    while chunk:=os.read(source,self.f.CHUNK):
                        validate();count+=len(chunk)
                        if count>before.st_size:self.reject('SOURCE_CHANGED')
                        checksum.update(chunk);pending=memoryview(chunk)
                        while pending:
                            written=os.write(output,pending)
                            if written<=0:self.reject('HISTORY_WRITE_FAILED')
                            pending=pending[written:]
                    validate()
                    if count!=before.st_size or checksum.hexdigest().upper()!=value['sha256'] or list(self.f.publication_identity(os.fstat(source)))!=request['plan']['sourceIdentity'][:8]:self.reject('SOURCE_CHANGED')
                    os.fsync(output);os.close(output);output=None;self.storage.attest(configuration,directory)
                    self.f.exchange(directory,op+'.partial',directory,op+'.data',1);os.fsync(directory)
                    self.storage.verify_data(directory,op,value);validate();value['state']='Verified';self.f.save_receipt(directory,op,value)
                    return self.read(directory,request)['historyDigest']
                finally:
                    if output is not None:os.close(output)
                    os.close(source)

        def verify(self, request, digest):
            with self.target(False) as (_,directory):
                value=self.read(directory,request)
                if value['historyDigest']!=digest:self.reject('HISTORY_CHANGED')
                return value

        def mark(self, request, digest, state):
            with self.target(True) as (_,directory),self.gate(directory):
                value=self.read(directory,request)
                if value['historyDigest']!=digest:self.reject('HISTORY_CHANGED')
                old=value['captureState']
                if old==state:return
                if (old,state) not in (('Pending','Trashed'),('Pending','Canceled'),('Trashed','Restored')):self.reject('HISTORY_STATE_CONFLICT')
                value['captureState']=state;self.f.save_receipt(directory,request['operationId'],value)
    return ContainerRecycleHistory


def recycling_type(directory_type):
    class ContainerRecycling(directory_type):
        COMMAND={'containerId','instanceId','path','expectedVersion'}
        PLAN=directory_type.PLAN|{'sourceIdentity','sha256','attributes'}
        RECORD={'schemaVersion','operationId','owner','plan','state','privateIdentity','historyMayExist','historyVerified','historyDigest','actualIdentity'}
        TERMINAL=('Trashed','Restored','Aborted','Unknown','Purged')
        PRIVATE='.whaledeck-container-recycling-v1'
        LIMIT=65536

        def __init__(self, uploads, publisher, state_path, history, restore_claims_path=None):
            super().__init__(uploads,publisher,state_path)
            if history.f is not self.f:raise ValueError('CONTAINER_RECYCLE_HISTORY_ENGINE_MISMATCH')
            self.history=history
            self.restore_claims_path=restore_claims_path

        def reject(self, suffix):raise self.r.Rejected('CONTAINER_RECYCLE_'+suffix)

        @staticmethod
        def digest(plan):return hashlib.sha256(b'whaledeck-container-recycle-plan-v1\n'+encode({k:v for k,v in plan.items() if k!='fingerprint'})).hexdigest().upper()

        def command(self, value):
            if type(value) is not dict or set(value)!=self.COMMAND:self.reject('REQUEST_INVALID')
            self.r.target_binding(value['containerId'],value['instanceId']);self.r.canonical_path(value['path'],empty=False)
            if self.r.protected(value['path'],self.f) or any(n.startswith('.whaledeck-') for n in value['path'].split('/')):self.reject('PROTECTED')
            if type(value['expectedVersion']) is not str or not re.fullmatch('[A-F0-9]{64}',value['expectedVersion']):self.reject('VERSION_INVALID')

        @contextlib.contextmanager
        def parent(self, plan, session):
            with super().parent(plan,session) as parent:
                if 'uid' in plan and tuple(self.p.owner(session))!=(plan['uid'],plan['gid']):self.reject('OWNER_CHANGED')
                yield parent

        @staticmethod
        def metadata(i):return [i.st_dev,i.st_ino,i.st_mode,i.st_uid,i.st_gid,i.st_nlink,i.st_size,i.st_mtime_ns,i.st_ctime_ns]

        def attributes(self, fd):
            values=[];size=0
            for name in sorted(os.listxattr(fd)):
                if not name.startswith('user.'):self.reject('EXTENDED_METADATA_REQUIRED')
                raw=os.getxattr(fd,name);size+=len(name.encode())+len(raw)
                if len(values)>=32 or size>4096:self.reject('ATTRIBUTES_LIMIT')
                values.append([name,base64.b64encode(raw).decode('ascii')])
            return values

        def fingerprint(self, fd, session):
            i=os.fstat(fd);identity=self.metadata(i)
            if not stat.S_ISREG(i.st_mode) or i.st_nlink!=1 or i.st_mode&0o7000 or not 0<=i.st_size<=self.r.MAX_BYTES:self.reject('SOURCE_UNSUPPORTED')
            attrs=self.attributes(fd);checksum=hashlib.sha256();count=0;os.lseek(fd,0,os.SEEK_SET)
            while chunk:=os.read(fd,self.f.CHUNK):
                session.revalidate();count+=len(chunk)
                if count>identity[6]:self.reject('SOURCE_CHANGED')
                checksum.update(chunk)
            if count!=identity[6] or self.metadata(os.fstat(fd))!=identity or self.attributes(fd)!=attrs:self.reject('SOURCE_CHANGED')
            return identity,attrs,checksum.hexdigest().upper()

        def validate_plan(self, plan):
            super().validate_plan(plan);i=plan['sourceIdentity']
            if type(i) is not list or len(i)!=9 or any(type(n) is not int for n in i) or not 0<=i[2]<=65535 or not stat.S_ISREG(i[2]) or i[2]&0o7000 or i[5]!=1 or not 0<=i[6]<=self.r.MAX_BYTES:self.reject('PLAN_INVALID')
            if any(not 0<=i[k]<=18446744073709551615 for k in (0,1)) or i[1]==0 or any(not 0<=i[k]<4294967295 for k in (3,4)) or any(not -(2**63)<=i[k]<2**63 for k in (7,8)):self.reject('PLAN_INVALID')
            attrs=plan['attributes']
            if type(attrs) is not list or len(attrs)>32 or len(encode(attrs))>12000:self.reject('PLAN_INVALID')
            names=[];size=0
            for pair in attrs:
                if type(pair) is not list or len(pair)!=2 or any(type(x) is not str for x in pair) or not pair[0].startswith('user.') or '\x00' in pair[0]:self.reject('PLAN_INVALID')
                try:raw=base64.b64decode(pair[1],validate=True)
                except ValueError:self.reject('PLAN_INVALID')
                names.append(pair[0]);size+=len(pair[0].encode())+len(raw)
            if names!=sorted(set(names)) or size>4096 or type(plan['sha256']) is not str or not re.fullmatch('[A-F0-9]{64}',plan['sha256']):self.reject('PLAN_INVALID')
            c=plan['command'];expected=hashlib.sha256(b'whaledeck-container-file-v1\n'+json.dumps([c['containerId'],c['instanceId'],c['path'],*i],ensure_ascii=False,separators=(',',':')).encode()).hexdigest().upper()
            if expected!=c['expectedVersion']:self.reject('PLAN_INVALID')

        def plan(self, command, session):
            self.command(command);proof=self.guard(command,session)
            with self.parent({'command':command,'targetProof':proof},session) as parent:
                fd=self.f.open_at(parent,command['path'].rsplit('/',1)[-1],os.O_RDONLY|os.O_NONBLOCK|os.O_NOFOLLOW,cross_mount=True)
                try:
                    i,attrs,checksum=self.fingerprint(fd,session)
                    if self.r.version(command,command['path'],os.fstat(fd))!=command['expectedVersion'] or self.metadata(os.stat(command['path'].rsplit('/',1)[-1],dir_fd=parent,follow_symlinks=False))!=i:self.reject('SOURCE_CHANGED')
                finally:os.close(fd)
                info=os.fstat(parent);uid,gid=self.p.owner(session)
                result=dict(schemaVersion=1,command=dict(command),targetProof=proof,parentIdentity=[info.st_dev,info.st_ino],parentVersion=self.r.version(command,command['path'].rpartition('/')[0],info),uid=uid,gid=gid,sourceIdentity=i,attributes=attrs,sha256=checksum)
            result['fingerprint']=self.digest(result);self.validate_plan(result);return result

        def new_record(self, request, phase):return dict(schemaVersion=1,**json.loads(encode(request)),state=phase,privateIdentity=None,historyMayExist=False,historyVerified=False,historyDigest='',actualIdentity=None)

        def validate_record(self, record, operation):
            if type(record) is not dict or set(record)!=self.RECORD or type(record['schemaVersion']) is not int or record['schemaVersion']!=1:self.reject('RECEIPT_INVALID')
            self.request({k:record[k] for k in self.REQUEST})
            if record['operationId']!=operation or record['state'] not in (*self.TERMINAL,'Preparing','Retaining','Restoring','Purging'):self.reject('RECEIPT_INVALID')
            if any(type(record[k]) is not bool for k in ('historyMayExist','historyVerified')) or type(record['historyDigest']) is not str:self.reject('RECEIPT_INVALID')
            if record['historyVerified']:
                if not record['historyMayExist'] or not re.fullmatch('[A-F0-9]{64}',record['historyDigest']):self.reject('RECEIPT_INVALID')
            elif record['historyDigest']!='':self.reject('RECEIPT_INVALID')
            private=record['privateIdentity'];actual=record['actualIdentity']
            if private is not None and (type(private) is not list or len(private)!=2 or any(type(n) is not int or not 0<=n<=18446744073709551615 for n in private) or private[1]==0):self.reject('RECEIPT_INVALID')
            if record['state'] in ('Retaining','Restoring','Trashed','Restored') and (not record['historyVerified'] or private is None):self.reject('RECEIPT_INVALID')
            if record['state'] in ('Purging','Purged') and (not record['historyVerified'] or private is None):self.reject('RECEIPT_INVALID')
            if actual is not None and (type(actual) is not list or len(actual)!=9 or any(type(n) is not int for n in actual) or actual[:8]!=record['plan']['sourceIdentity'][:8] or actual[8]<record['plan']['sourceIdentity'][8]):self.reject('RECEIPT_INVALID')
            if (record['state'] in ('Trashed','Restored')) != (actual is not None):self.reject('RECEIPT_INVALID')

        def checkpoint(self, phase):
            """Trusted test process cut only; never command data."""

        def source_unchanged(self, parent, plan, session):
            self.guard(plan['command'],session,plan['targetProof'])
            with self.parent(plan,session) as fresh:
                if (os.fstat(fresh).st_dev,os.fstat(fresh).st_ino)!=(os.fstat(parent).st_dev,os.fstat(parent).st_ino):self.reject('PARENT_CHANGED')
            if tuple(self.p.owner(session))!=(plan['uid'],plan['gid']):self.reject('OWNER_CHANGED')
            fd=self.f.open_at(parent,plan['command']['path'].rsplit('/',1)[-1],os.O_RDONLY|os.O_NONBLOCK|os.O_NOFOLLOW,cross_mount=True)
            try:
                if self.metadata(os.fstat(fd))!=plan['sourceIdentity'] or self.attributes(fd)!=plan['attributes']:self.reject('SOURCE_CHANGED')
            finally:os.close(fd)

        def probe_file(self, parent, name, plan, session):
            try:fd=self.f.open_at(parent,name,os.O_RDONLY|os.O_NONBLOCK|os.O_NOFOLLOW,cross_mount=True)
            except FileNotFoundError:return None
            try:
                i,attrs,checksum=self.fingerprint(fd,session)
                if i[:8]!=plan['sourceIdentity'][:8] or i[8]<plan['sourceIdentity'][8] or attrs!=plan['attributes'] or checksum!=plan['sha256']:self.reject('SOURCE_CHANGED')
                return i
            finally:os.close(fd)

        def apply(self, request, session):
            with self.locked(request) as (state,record):
                if record is not None:self.recover(state,record,session);return self.public(record,session,True)
                if self.history.has_artifacts(request):
                    record=self.new_record(request,'Unknown');record['historyMayExist']=True;self.save(state,record);return self.public(record,session,False)
                if session is None:self.reject('SESSION_REQUIRED')
                if self.plan(request['plan']['command'],session)!=request['plan']:self.reject('SOURCE_CHANGED')
                record=self.new_record(request,'Preparing');self.save(state,record);self.checkpoint('Preparing')
                try:
                    plan=record['plan'];c=plan['command']
                    with self.parent(plan,session) as parent:
                        record['historyMayExist']=True;self.save(state,record)
                        record['historyDigest']=self.history.capture(request,parent,c['path'].rsplit('/',1)[-1],lambda:self.source_unchanged(parent,plan,session));record['historyVerified']=True;self.save(state,record);self.checkpoint('HistoryVerified')
                        if self.r.version(c,c['path'].rpartition('/')[0],os.fstat(parent))!=plan['parentVersion']:self.reject('PARENT_CHANGED')
                        if os.fstatvfs(parent).f_bavail*os.fstatvfs(parent).f_frsize<64*1024**2:self.reject('SOURCE_CAPACITY')
                        private=self.private(parent,record,create=True)
                        try:
                            info=os.fstat(private);record['privateIdentity']=[info.st_dev,info.st_ino];self.save(state,record)
                            self.source_unchanged(parent,plan,session);self.history.verify(request,record['historyDigest'])
                            with self.parent(plan,session):pass
                            record['state']='Retaining';self.save(state,record);self.checkpoint('Retaining')
                            self.source_unchanged(parent,plan,session)
                            self.f.exchange(parent,c['path'].rsplit('/',1)[-1],private,record['operationId']+'.trash',1);os.fsync(parent);os.fsync(private);self.checkpoint('Retained')
                        finally:os.close(private)
                    self.recover(state,record,session)
                except BaseException:
                    with contextlib.suppress(OSError,self.r.Rejected,self.f.Rejected):self.recover(state,record,session)
                    raise
                return self.public(record,session,False)

        def recover(self, state, record, session):
            if record['state'] in self.TERMINAL:return
            phase=record['state'];plan=record['plan'];request={k:record[k] for k in self.REQUEST};record['state']='Unknown';record['actualIdentity']=None
            try:
                with self.parent(plan,session) as parent:
                    current=self.probe_file(parent,plan['command']['path'].rsplit('/',1)[-1],plan,session)
                    retained=None
                    if record['privateIdentity'] is not None:
                        private=self.private(parent,record)
                        try:retained=self.probe_file(private,record['operationId']+'.trash',plan,session)
                        finally:os.close(private)
                    if record['historyVerified']:self.history.verify(request,record['historyDigest'])
                    if phase=='Preparing' and current==plan['sourceIdentity'] and retained is None:
                        record['state']='Aborted'
                        if record['historyVerified']:self.history.mark(request,record['historyDigest'],'Canceled')
                    elif phase in ('Retaining','Restoring') and current is None and retained is not None:
                        self.history.mark(request,record['historyDigest'],'Trashed');record.update(state='Trashed',actualIdentity=retained)
                    elif phase=='Retaining' and current==plan['sourceIdentity'] and retained is None:
                        self.history.mark(request,record['historyDigest'],'Canceled');record['state']='Aborted'
                    elif phase=='Restoring' and current is not None and retained is None:
                        self.history.mark(request,record['historyDigest'],'Restored');record.update(state='Restored',actualIdentity=current)
            except (OSError,self.r.Rejected,self.f.Rejected):pass
            self.save(state,record)

        def read_restore_claim(self, request):
            if self.restore_claims_path is None:return None
            path=Path(self.restore_claims_path)
            if not os.path.lexists(path):self.reject('STATE_UNSAFE')
            if not path.is_absolute() or path!=path.resolve(strict=True):self.reject('STATE_UNSAFE')
            directory=self.f.trusted_directory(path)
            try:
                if stat.S_IMODE(os.fstat(directory).st_mode)!=0o700:self.reject('STATE_UNSAFE')
                try:fd=os.open(request['operationId']+'.json',os.O_RDONLY|os.O_NOFOLLOW|os.O_NONBLOCK|os.O_CLOEXEC,dir_fd=directory)
                except FileNotFoundError:return None
                try:
                    info=os.fstat(fd)
                    if not stat.S_ISREG(info.st_mode) or info.st_uid!=0 or info.st_nlink!=1 or stat.S_IMODE(info.st_mode)!=0o600 or not 0<info.st_size<=65536:self.reject('STATE_UNSAFE')
                    raw=os.read(fd,65537)
                    if len(raw)!=info.st_size:self.reject('STATE_UNSAFE')
                    value=json.loads(raw,object_pairs_hook=self.f.unique_object)
                finally:os.close(fd)
                if type(value) is not dict or set(value)!={'schemaVersion','source','restoreOperationId','restoreOwner','restoreFingerprint'} or type(value['schemaVersion']) is not int or value['schemaVersion']!=1:self.reject('STATE_UNSAFE')
                self.request(value['source'])
                if value['source']!=request or type(value['restoreOperationId']) is not str or not re.fullmatch('[a-f0-9]{32}',value['restoreOperationId']) or int(value['restoreOperationId'],16)==0 or value['restoreOperationId']==request['operationId']:self.reject('STATE_UNSAFE')
                if any(type(value[k]) is not str or not re.fullmatch('[A-F0-9]{64}',value[k]) for k in ('restoreOwner','restoreFingerprint')):self.reject('STATE_UNSAFE')
                return value
            finally:os.close(directory)

        def read_purge_fence(self, request):
            """Candidate source fence in the existing trusted source registry.

            Callers that mutate source/claims must hold this source's lock.
            A Released marker is a durable cancellation, not a missing record.
            """
            self.request(request)
            if not self.state_path.is_absolute() or self.state_path!=self.state_path.resolve(strict=True):self.reject('STATE_UNSAFE')
            directory=self.f.trusted_directory(self.state_path)
            try:
                if stat.S_IMODE(os.fstat(directory).st_mode)!=0o700:self.reject('STATE_UNSAFE')
                try:fd=os.open(request['operationId']+'.purge.json',os.O_RDONLY|os.O_NOFOLLOW|os.O_NONBLOCK|os.O_CLOEXEC,dir_fd=directory)
                except FileNotFoundError:return None
                try:
                    info=os.fstat(fd)
                    if not stat.S_ISREG(info.st_mode) or info.st_uid!=0 or info.st_gid!=0 or info.st_nlink!=1 or stat.S_IMODE(info.st_mode)!=0o600 or not 0<info.st_size<=65536:self.reject('STATE_UNSAFE')
                    raw=os.read(fd,65537)
                    if len(raw)!=info.st_size:self.reject('STATE_UNSAFE')
                    value=json.loads(raw,object_pairs_hook=self.f.unique_object)
                finally:os.close(fd)
                if type(value) is not dict or set(value)!={'schemaVersion','source','operationId','owner','fingerprint','state'} or type(value['schemaVersion']) is not int or value['schemaVersion']!=1:self.reject('STATE_UNSAFE')
                self.request(value['source']);self.f.identity(value['operationId'])
                if encode(value['source'])!=encode(request) or value['operationId']==request['operationId'] or value['state'] not in ('Reserved','Released','Unknown','Deleting','Purged'):self.reject('STATE_UNSAFE')
                if any(type(value[k]) is not str or not re.fullmatch('[A-F0-9]{64}',value[k]) for k in ('owner','fingerprint')):self.reject('STATE_UNSAFE')
                return value
            finally:os.close(directory)

        def check_purge_fence(self, request, purge_id=None):
            fence=self.read_purge_fence(request)
            if fence is not None and fence['state']!='Released' and not (fence['state']=='Reserved' and purge_id is not None and fence['operationId']==purge_id):self.reject('PURGE_CLAIMED')

        def check_restore_claim(self, request, restore_id):
            self.check_purge_fence(request)
            claim=self.read_restore_claim(request)
            if claim is not None and claim['restoreOperationId']!=restore_id:self.reject('RESTORE_CLAIMED')
            if restore_id is not None and claim is None:self.reject('RESTORE_CLAIMED')

        def restore(self, request, session):
            with self.locked(request) as (state,record):
                if record is None:self.reject('NOT_FOUND')
                self.recover(state,record,session)
                return self.restore_locked(request,state,record,session)

        def restore_locked(self, request, state, record, session, restore_id=None):
            if record['state']=='Restored':return self.public(record,session,True)
            if record['state']!='Trashed':self.reject('RESTORE_UNPROVEN')
            self.check_restore_claim(request,restore_id)
            if session is None:self.reject('SESSION_REQUIRED')
            plan=record['plan'];c=plan['command']
            with self.parent(plan,session) as parent:
                if self.present(parent,c['path'].rsplit('/',1)[-1]):self.reject('TARGET_EXISTS')
                private=self.private(parent,record)
                try:
                    actual=self.probe_file(private,record['operationId']+'.trash',plan,session)
                    if actual!=record['actualIdentity']:self.reject('SOURCE_CHANGED')
                    self.history.verify(request,record['historyDigest']);self.guard(c,session,plan['targetProof'])
                    with self.parent(plan,session):pass
                    record.update(state='Restoring',actualIdentity=None);self.save(state,record);self.checkpoint('Restoring')
                    with self.parent(plan,session):pass
                    verified_private=self.private(parent,record)
                    try:
                        if self.probe_file(verified_private,record['operationId']+'.trash',plan,session)!=actual:self.reject('SOURCE_CHANGED')
                    finally:os.close(verified_private)
                    self.check_purge_fence(request)
                    self.f.exchange(private,record['operationId']+'.trash',parent,c['path'].rsplit('/',1)[-1],1);os.fsync(private);os.fsync(parent);self.checkpoint('RestoredNamespace')
                finally:os.close(private)
            self.recover(state,record,session);return self.public(record,session,False)
        def inspect(self, request, session=None):
            with self.locked(request) as (state,record):
                if record is None:
                    if not self.history.has_artifacts(request):self.reject('NOT_FOUND')
                    record=self.new_record(request,'Unknown');record['historyMayExist']=True;self.save(state,record)
                self.recover(state,record,session);return self.public(record,session,True)

        def discard(self, request, session=None):
            with self.locked(request) as (state,record):
                if record is None:
                    orphan=self.history.has_artifacts(request)
                    record=self.new_record(request,'Unknown' if orphan else 'Aborted');record['historyMayExist']=orphan;self.save(state,record)
                else:self.recover(state,record,session)
                return self.public(record,session,True)

        def public(self, record, session, replayed):
            binding=False;available=False;request={k:record[k] for k in self.REQUEST}
            if record['historyVerified']:
                try:self.history.verify(request,record['historyDigest']);available=True
                except (OSError,self.r.Rejected,self.f.Rejected):pass
            if available and session is not None and record['state'] in ('Trashed','Restored'):
                try:
                    with self.parent(record['plan'],session) as parent:
                        c=record['plan']['command'];current=self.probe_file(parent,c['path'].rsplit('/',1)[-1],record['plan'],session)
                        private=self.private(parent,record)
                        try:retained=self.probe_file(private,record['operationId']+'.trash',record['plan'],session)
                        finally:os.close(private)
                        binding=(current is None and retained==record['actualIdentity']) if record['state']=='Trashed' else (retained is None and current==record['actualIdentity'])
                except (OSError,self.r.Rejected,self.f.Rejected):pass
            return dict(operationId=record['operationId'],containerId=record['plan']['command']['containerId'],instanceId=record['plan']['command']['instanceId'],
                fingerprint=record['plan']['fingerprint'],targetProof=record['plan']['targetProof'],state=record['state'],bindingCurrent=binding,replayed=replayed,
                historyMayExist=record['historyMayExist'],historyVerified=record['historyVerified'],historyDigest=record['historyDigest'],historyAvailable=available,
                sourceRetained=(record['state']=='Trashed') if record['state']!='Unknown' else None,
                sourceRestored=(record['state']=='Restored') if record['state']!='Unknown' else None)
    return ContainerRecycling
