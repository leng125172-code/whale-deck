#!/usr/bin/env python3
"""Bounded recursive ordinary-mode engine, not a public helper/RPC grant.

A sealed optimistic inventory and attested HDD manifest precede every write.
Each inode uses the existing single-permission journal, under a private batch
namespace. Recovery observes/fences original entry IDs; it never resumes chmod.
"""
import contextlib
import fcntl
import hashlib
import json
import math
import os
from pathlib import Path
import re
import stat
import time

MAX_ENTRIES = 10000
MAX_DEPTH = 32
MAX_METADATA = 16 * 1024**2
NAMESPACE = 'container-recursive-permissions-v1'


def encode(value):
    return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(',', ':'), allow_nan=False).encode()


def digest(domain, value): return hashlib.sha256(domain + encode(value)).hexdigest().upper()


def private_dir(files, parent, name, create):
    if create:
        try: os.mkdir(name, 0o700, dir_fd=parent); os.fsync(parent)
        except FileExistsError: pass
    fd = files.open_at(parent, name, os.O_RDONLY | os.O_DIRECTORY)
    try:
        info = os.fstat(fd)
        if (info.st_uid, info.st_gid, stat.S_IMODE(info.st_mode)) != (0, 0, 0o700): raise files.Rejected('CONTAINER_RECURSIVE_PERMISSION_PRIVATE_UNSAFE')
        return fd
    except BaseException: os.close(fd); raise


class RecursivePermissionHistory:
    DOMAIN = b'whaledeck-container-recursive-permission-history-v1\n'
    def __init__(self, storage):
        if storage.namespace is not None: raise ValueError('CONTAINER_RECURSIVE_PERMISSION_HISTORY_DOMAIN')
        self.storage, self.f = storage, storage.f
    @contextlib.contextmanager
    def target(self, create):
        with self.storage.target(create_namespace=False, read_only=not create) as (configuration, parent):
            fd = private_dir(self.f, parent, NAMESPACE, create)
            try:
                self.storage.attest(configuration, fd, read_only=not create)
                yield configuration, fd
            finally: os.close(fd)
    def has_artifacts(self, request):
        # Only absence under an available, attested HDD is proof of absence.
        # A lost aggregate may still leave immutable per-entry HDD history.
        with self.storage.target(create_namespace=False, read_only=True) as (_, base):
            try:fd=private_dir(self.f,base,NAMESPACE,False)
            except FileNotFoundError:return False
            try:
                for name in (request['operationId']+'.json',request['operationId']):
                    try:os.stat(name,dir_fd=fd,follow_symlinks=False)
                    except FileNotFoundError:continue
                    return True
                return False
            finally:os.close(fd)

    def load(self, request):
        with self.target(False) as (_, fd): value = self.storage.read_json(fd, request['operationId']+'.json', MAX_METADATA)
        required = {'schemaVersion','resourceType','storageId','operationId','owner','plan','entries','createdAtUnixMs','historyDigest'}
        if set(value) != required or value['schemaVersion'] != 1 or value['resourceType'] != 'ContainerRecursivePermissionMetadata' or value['storageId'] != 'database-platform.backup.hdd' or \
                any(value[k] != request[k] for k in ('operationId','owner','plan')) or type(value['createdAtUnixMs']) is not int or value['createdAtUnixMs'] < 0 or \
                digest(self.DOMAIN,{k:v for k,v in value.items() if k!='historyDigest'}) != value['historyDigest']:
            raise self.f.Rejected('CONTAINER_RECURSIVE_PERMISSION_HISTORY_CHANGED')
        return value
    def capture(self, request, entries):
        with self.target(True) as (configuration, fd):
            lock = os.open('history.lock',os.O_RDWR|os.O_CREAT|os.O_NOFOLLOW|os.O_CLOEXEC,0o600,dir_fd=fd)
            try:
                info=os.fstat(lock)
                if not stat.S_ISREG(info.st_mode) or info.st_uid!=0 or info.st_nlink!=1 or stat.S_IMODE(info.st_mode)!=0o600 or info.st_size:
                    raise self.f.Rejected('CONTAINER_RECURSIVE_PERMISSION_HISTORY_UNSAFE')
                try:fcntl.flock(lock,fcntl.LOCK_EX|fcntl.LOCK_NB)
                except BlockingIOError:raise self.f.Rejected('CONTAINER_RECURSIVE_PERMISSION_BUSY') from None
                try:
                    existing=self.load(request)
                    if existing['entries']!=entries:raise self.f.Rejected('CONTAINER_RECURSIVE_PERMISSION_HISTORY_CHANGED')
                    return existing
                except FileNotFoundError:pass
                if len(entries)>configuration['maxVersions'] or sum(n.endswith('.json') for n in os.listdir(fd))>=configuration['maxVersions']:
                    raise self.f.Rejected('CONTAINER_RECURSIVE_PERMISSION_HISTORY_LIMIT')
                value={'schemaVersion':1,'resourceType':'ContainerRecursivePermissionMetadata','storageId':'database-platform.backup.hdd',
                    **request,'entries':entries,'createdAtUnixMs':time.time_ns()//1000000}
                value['historyDigest']=digest(self.DOMAIN,value)
                size=len(json.dumps(value,separators=(',',':')).encode())
                if size>MAX_METADATA:raise self.f.Rejected('CONTAINER_RECURSIVE_PERMISSION_MANIFEST_LIMIT')
                capacity=os.fstatvfs(fd)
                reserve=max(configuration['reserveBytes'],capacity.f_blocks*capacity.f_frsize*configuration['reservePercent']//100)
                if capacity.f_bavail*capacity.f_frsize < reserve+size+len(entries)*16384+65536:
                    raise self.f.Rejected('CONTAINER_RECURSIVE_PERMISSION_HISTORY_CAPACITY')
                self.f.save_receipt(fd,request['operationId'],value)
                return self.load(request)
            finally:os.close(lock)


def recursive_permissions_type(permission_type, directory_type, permission_history_type):
    class EntryHistory(permission_history_type):
        DOMAIN=b'whaledeck-container-recursive-permission-entry-history-v1\n'
        def __init__(self, outer, request):
            super().__init__(outer.history.storage);self.outer,self.batch=outer,request
        def expected(self,request):
            return {**super().expected(request),'resourceType':'ContainerRecursivePermissionEntryMetadata','parentOperationId':self.batch['operationId'],
                'manifestDigest':self.batch['plan']['manifestDigest']}
        def artifact_directories(self):return (NAMESPACE,self.batch['operationId'],'entries')
        @contextlib.contextmanager
        def target(self,create):
            with self.outer.history.target(create) as (configuration,base):
                batch=private_dir(self.f,base,self.batch['operationId'],create)
                try:fd=private_dir(self.f,batch,'entries',create)
                finally:os.close(batch)
                try:
                    self.storage.attest(configuration,fd,read_only=not create);yield configuration,fd
                finally:os.close(fd)

    class RecursivePermissions(directory_type):
        COMMAND={'containerId','instanceId','path','expectedVersion','fileMode','directoryMode'}
        PLAN=directory_type.PLAN|{'rootIdentity','manifestDigest','entryCount','fileCount','directoryCount','changeCount'}
        RECORD={'schemaVersion','operationId','owner','plan','state','startedThrough','finishedThrough','historyMayExist','historyVerified','historyDigest',
            'completedCount','changedCount','abortedCount','notStartedCount','unknownCount','outcomeDigest'}
        TERMINAL=('Completed','Aborted','Partial','Unknown')
        def __init__(self,uploads,publisher,state_path,history,*,max_entries=MAX_ENTRIES,max_depth=MAX_DEPTH,timeout_seconds=300,cancelled=None):
            super().__init__(uploads,publisher,state_path);self.history=history
            if history.f is not self.f or type(max_entries) is not int or not 1<=max_entries<=MAX_ENTRIES or type(max_depth) is not int or not 0<=max_depth<=MAX_DEPTH or \
                    type(timeout_seconds) not in (int,float) or not math.isfinite(timeout_seconds) or not .001<=timeout_seconds<=300 or cancelled is not None and not callable(cancelled):
                self.reject('LIMITS_INVALID')
            self.max_entries,self.max_depth,self.timeout,self.cancelled=max_entries,max_depth,timeout_seconds,cancelled
            self.deadline=0;self.observing=False
        def reject(self,suffix):raise self.r.Rejected('CONTAINER_RECURSIVE_PERMISSION_'+suffix)
        @staticmethod
        def digest(plan):return digest(b'whaledeck-container-recursive-permission-plan-v1\n',{k:v for k,v in plan.items() if k!='fingerprint'})
        @staticmethod
        def manifest_digest(entries):return digest(b'whaledeck-container-recursive-permission-manifest-v1\n',entries)
        def begin(self,observing=False):self.deadline=time.monotonic()+self.timeout;self.observing=observing
        def tick(self):
            if time.monotonic()>self.deadline:self.reject('TIMEOUT')
            if not self.observing and self.cancelled is not None and self.cancelled():self.reject('CANCELED')
        def command(self,value):
            if type(value) is not dict or set(value)!=self.COMMAND:self.reject('REQUEST_INVALID')
            self.r.target_binding(value['containerId'],value['instanceId']);self.r.canonical_path(value['path'],empty=False)
            if self.r.protected(value['path'],self.f):self.reject('PROTECTED')
            for key in ('fileMode','directoryMode'):
                if type(value[key]) is not str or not re.fullmatch('0[0-7]{3}',value[key]):self.reject('MODE_INVALID')
            if type(value['expectedVersion']) is not str or not re.fullmatch('[A-F0-9]{64}',value['expectedVersion']):self.reject('VERSION_INVALID')
        def validate_plan(self,plan):
            directory_type.validate_plan(self,plan);permission_type.validate_identity(self,plan['rootIdentity'],'Directory')
            if any(type(plan[k]) is not int for k in ('entryCount','fileCount','directoryCount','changeCount')) or not 1<=plan['entryCount']<=MAX_ENTRIES or \
                    not 0<=plan['fileCount']<plan['entryCount'] or plan['directoryCount']!=plan['entryCount']-plan['fileCount'] or not 0<=plan['changeCount']<=plan['entryCount'] or \
                    type(plan['manifestDigest']) is not str or not re.fullmatch('[A-F0-9]{64}',plan['manifestDigest']):self.reject('PLAN_INVALID')
            version=hashlib.sha256(b'whaledeck-container-file-v1\n'+json.dumps([plan['command']['containerId'],plan['command']['instanceId'],plan['command']['path'],*plan['rootIdentity']],ensure_ascii=False,separators=(',',':')).encode()).hexdigest().upper()
            if version!=plan['command']['expectedVersion']:self.reject('PLAN_INVALID')
        @contextlib.contextmanager
        def root(self,scope,session):
            self.tick()
            with directory_type.parent(self,scope,session) as parent:
                fd=self.f.open_at(parent,scope['command']['path'].rsplit('/',1)[-1],os.O_RDONLY|os.O_DIRECTORY,cross_mount=True)
                try:
                    info=os.fstat(fd)
                    if [info.st_dev,info.st_ino]!=scope['rootIdentity'][:2]:self.reject('ROOT_CHANGED')
                    if 'uid' in scope and tuple(self.p.owner(session))!=(scope['uid'],scope['gid']):self.reject('OWNER_CHANGED')
                    yield fd
                finally:os.close(fd)
        def child(self,scope,state_path,history):
            outer=self
            class ConfinedPermission(permission_type):
                @contextlib.contextmanager
                def parent(child_self,plan,session):
                    path=plan['command']['path'];base=scope['command']['path']
                    if path==base:
                        with directory_type.parent(child_self,plan,session) as fd:yield fd
                        return
                    if not path.startswith(base+'/'):outer.reject('ENTRY_SCOPE_INVALID')
                    relative=path[len(base)+1:].rpartition('/')[0]
                    with outer.root(scope,session) as root:
                        fd=outer.f.open_at(root,relative or '.',os.O_RDONLY|os.O_DIRECTORY)
                        try:
                            child_self.guard(plan['command'],session,plan.get('targetProof'))
                            info=os.fstat(fd)
                            if 'parentIdentity' in plan and [info.st_dev,info.st_ino]!=plan['parentIdentity']:outer.reject('PARENT_CHANGED')
                            child_self.p.writable({'request':plan['command']},session,fd)
                            if 'uid' in plan and tuple(outer.p.owner(session))!=(plan['uid'],plan['gid']):outer.reject('OWNER_CHANGED')
                            yield fd
                        finally:os.close(fd)
                def open_source(child_self,parent,plan):
                    return outer.f.open_at(parent,plan['command']['path'].rsplit('/',1)[-1],os.O_RDONLY|os.O_NONBLOCK|os.O_NOFOLLOW,
                        cross_mount=plan['command']['path']==scope['command']['path'])
            return ConfinedPermission(self.u,self.p,state_path,history)
        def walk(self,scope,session):
            rows=[];seen=set();inspector=self.child(scope,self.state_path,self.history)
            with self.root(scope,session) as root:
                device=os.fstat(root).st_dev
                def visit(fd,relative,depth):
                    self.tick()
                    if len(rows)>=self.max_entries or depth>self.max_depth:self.reject('TREE_LIMIT')
                    full=scope['command']['path']+('/'+relative if relative else '')
                    self.r.canonical_path(full,empty=False)
                    if self.r.protected(full,self.f):self.reject('PROTECTED')
                    kind,identity,attrs=inspector.snapshot(fd)
                    if identity[0]!=device or tuple(identity[:2]) in seen:self.reject('TREE_ALIAS_OR_MOUNT')
                    seen.add(tuple(identity[:2]));rows.append({'relativePath':relative,'kind':kind,'identity':identity,'attributesDigest':attrs})
                    if kind=='Directory':
                        names=[]
                        with os.scandir(fd) as entries:
                            for item in entries:
                                if len(rows)+len(names)>=self.max_entries:self.reject('TREE_LIMIT')
                                names.append(item.name)
                        for name in sorted(names,key=lambda n:n.encode('utf-8')):
                            self.tick();path=relative+'/'+name if relative else name
                            full_child=scope['command']['path']+'/'+path;self.r.canonical_path(full_child,empty=False)
                            if self.r.protected(full_child,self.f):self.reject('PROTECTED')
                            child=self.f.open_at(fd,name,os.O_RDONLY|os.O_NONBLOCK|os.O_NOFOLLOW)
                            try:
                                visit(child,path,depth+1)
                                named=self.f.open_at(fd,name,os.O_RDONLY|os.O_NONBLOCK|os.O_NOFOLLOW)
                                try:
                                    if inspector.metadata(os.fstat(named))!=inspector.metadata(os.fstat(child)):self.reject('TREE_CHANGED')
                                finally:os.close(named)
                            finally:os.close(child)
                    if inspector.metadata(os.fstat(fd))!=identity:self.reject('TREE_CHANGED')
                visit(root,'',0)
            return rows
        def inventory(self,command,session):
            self.command(command);proof=self.guard(command,session)
            with directory_type.parent(self,{'command':command,'targetProof':proof},session) as parent:
                fd=self.f.open_at(parent,command['path'].rsplit('/',1)[-1],os.O_RDONLY|os.O_DIRECTORY,cross_mount=True)
                try:root_identity=list(self.r.fingerprint(os.fstat(fd)))
                finally:os.close(fd)
                uid,gid=self.p.owner(session);parent_info=os.fstat(parent)
                scope={'schemaVersion':1,'command':dict(command),'targetProof':proof,'parentIdentity':[parent_info.st_dev,parent_info.st_ino],
                    'parentVersion':self.r.version(command,command['path'].rpartition('/')[0],parent_info),'uid':uid,'gid':gid,'rootIdentity':root_identity}
            rows=self.walk(scope,session);builder=self.child(scope,self.state_path,self.history);entries=[];size=0
            for row in sorted(rows,key=lambda row:(-(row['relativePath'].count('/')+1 if row['relativePath'] else 0),row['relativePath'].encode())):
                self.tick();path=command['path']+('/'+row['relativePath'] if row['relativePath'] else '')
                version=hashlib.sha256(b'whaledeck-container-file-v1\n'+json.dumps([command['containerId'],command['instanceId'],path,*row['identity']],ensure_ascii=False,separators=(',',':')).encode()).hexdigest().upper()
                child_command={'containerId':command['containerId'],'instanceId':command['instanceId'],'path':path,'mode':command['directoryMode'] if row['kind']=='Directory' else command['fileMode'],'expectedVersion':version}
                plan=builder.plan(child_command,session)
                if plan['sourceIdentity']!=row['identity'] or plan['attributesDigest']!=row['attributesDigest']:self.reject('TREE_CHANGED')
                entry={'relativePath':row['relativePath'],'plan':plan};size+=len(json.dumps(entry,separators=(',',':')).encode())+1
                if size>MAX_METADATA-65536:self.reject('MANIFEST_LIMIT')
                entries.append(entry)
            if self.walk(scope,session)!=rows:self.reject('TREE_CHANGED')
            scope.update(manifestDigest=self.manifest_digest(entries),entryCount=len(entries),fileCount=sum(e['plan']['kind']=='File' for e in entries),
                directoryCount=sum(e['plan']['kind']=='Directory' for e in entries),changeCount=sum(not e['plan']['noChange'] for e in entries))
            scope['fingerprint']=self.digest(scope);self.validate_plan(scope);return scope,entries
        def plan(self,command,session):self.begin();return self.inventory(command,session)[0]
        def validate_record(self,record,operation):
            if type(record) is not dict or set(record)!=self.RECORD or record['schemaVersion']!=1:self.reject('RECEIPT_INVALID')
            self.request({k:record[k] for k in self.REQUEST})
            n=record['plan']['entryCount']
            if record['operationId']!=operation or record['state'] not in (*self.TERMINAL,'Preparing','Applying') or \
                    any(type(record[k]) is not int or not -1<=record[k]<n for k in ('startedThrough','finishedThrough')) or record['finishedThrough']>record['startedThrough'] or \
                    any(type(record[k]) is not bool for k in ('historyMayExist','historyVerified')):self.reject('RECEIPT_INVALID')
            keys=('completedCount','abortedCount','notStartedCount','unknownCount')
            if any(type(record[k]) is not int or not 0<=record[k]<=n for k in (*keys,'changedCount')) or sum(record[k] for k in keys)!=n or record['changedCount']>record['completedCount']:self.reject('RECEIPT_INVALID')
            if record['historyVerified']:
                if not record['historyMayExist'] or not re.fullmatch('[A-F0-9]{64}',record['historyDigest']):self.reject('RECEIPT_INVALID')
            elif record['historyDigest']!='':self.reject('RECEIPT_INVALID')
            if record['changedCount']>record['plan']['changeCount']:self.reject('RECEIPT_INVALID')
            if record['state']=='Completed' and (record['completedCount']!=n or record['changedCount']!=record['plan']['changeCount'] or not record['historyVerified'] or record['finishedThrough']!=n-1):self.reject('RECEIPT_INVALID')
            if record['state']=='Partial' and (not 0<record['changedCount']<=n or record['completedCount']==n or record['unknownCount'] or not record['historyVerified']):self.reject('RECEIPT_INVALID')
            if record['state']=='Unknown' and not record['unknownCount']:self.reject('RECEIPT_INVALID')
            if record['state']=='Aborted' and (record['changedCount'] or record['unknownCount']):self.reject('RECEIPT_INVALID')
            if record['state'] in ('Completed','Partial') and not re.fullmatch('[A-F0-9]{64}',record['outcomeDigest']):self.reject('RECEIPT_INVALID')
        def new_record(self,request):
            return {'schemaVersion':1,**json.loads(encode(request)),'state':'Preparing','startedThrough':-1,'finishedThrough':-1,
                'historyMayExist':False,'historyVerified':False,'historyDigest':'','completedCount':0,'changedCount':0,'abortedCount':0,
                'notStartedCount':request['plan']['entryCount'],'unknownCount':0,'outcomeDigest':''}
        def validate_manifest(self,request,saved):
            entries=saved['entries'];plan=request['plan']
            if type(entries) is not list or len(entries)!=plan['entryCount'] or self.manifest_digest(entries)!=plan['manifestDigest']:self.reject('HISTORY_CHANGED')
            seen=set();inodes=set();last=None;files=changes=0;root=None
            inspector=self.child(plan,self.state_path,self.history)
            for entry in entries:
                if type(entry) is not dict or set(entry)!={'relativePath','plan'}:self.reject('HISTORY_CHANGED')
                rel=entry['relativePath']
                if type(rel) is not str or rel in seen:self.reject('HISTORY_CHANGED')
                if rel:self.r.canonical_path(rel,empty=False)
                child=entry['plan'];permission_type.validate_plan(inspector,child)
                path=plan['command']['path']+('/'+rel if rel else '')
                if child['command']['path']!=path or any(child['command'][key]!=plan['command'][key] for key in ('containerId','instanceId')) or \
                        child['command']['mode']!=plan['command']['directoryMode' if child['kind']=='Directory' else 'fileMode'] or \
                        any(child[key]!=plan[key] for key in ('targetProof','uid','gid')):self.reject('HISTORY_CHANGED')
                inode=tuple(child['sourceIdentity'][:2])
                if inode in inodes or inode[0]!=plan['rootIdentity'][0]:self.reject('HISTORY_CHANGED')
                inodes.add(inode);files+=child['kind']=='File';changes+=not child['noChange']
                if not rel:root=child
                key=(-(rel.count('/')+1 if rel else 0),rel.encode())
                if last is not None and key<=last:self.reject('HISTORY_CHANGED')
                last=key;seen.add(rel)
            if root is None or root['kind']!='Directory' or root['sourceIdentity']!=plan['rootIdentity'] or \
                    any(root[key]!=plan[key] for key in ('parentIdentity','parentVersion')) or files!=plan['fileCount'] or changes!=plan['changeCount']:
                self.reject('HISTORY_CHANGED')
            return entries
        def child_request(self,request,index,entry):
            operation=hashlib.sha256(b'whaledeck-recursive-permission-entry-id-v1:'+request['operationId'].encode()+b':'+str(index).encode()).hexdigest()[:32]
            return {'operationId':operation,'owner':request['owner'],'plan':entry['plan']}
        @contextlib.contextmanager
        def child_engine(self,state,request,create):
            name=request['operationId']+'.entries';fd=private_dir(self.f,state,name,create)
            try:yield self.child(request['plan'],self.state_path/name,EntryHistory(self,request)),fd
            finally:os.close(fd)
        def checkpoint(self,phase,index):
            """Trusted disposable test boundary; not request data or a public control."""
        def apply(self,request,session):
            self.begin()
            with self.locked(request) as (state,record):
                if record is not None:
                    if record['state'] not in self.TERMINAL:self.recover(state,record,session)
                    return self.public(record,session,True)
                record=self.new_record(request)
                if self.history.has_artifacts(request) or self.present(state,request['operationId']+'.entries'):
                    record.update(state='Unknown',unknownCount=request['plan']['entryCount'],notStartedCount=0,historyMayExist=True)
                    self.save(state,record);return self.public(record,session,False)
                if session is None:self.reject('SESSION_REQUIRED')
                self.save(state,record)
                try:
                    plan,entries=self.inventory(request['plan']['command'],session)
                    if plan!=request['plan']:self.reject('TREE_CHANGED')
                    record['historyMayExist']=True;self.save(state,record)
                    saved=self.history.capture(request,entries);self.validate_manifest(request,saved)
                    record.update(state='Applying',historyVerified=True,historyDigest=saved['historyDigest']);self.save(state,record)
                    with self.child_engine(state,request,True) as (child,_):
                        for index,entry in enumerate(entries):
                            self.tick();record['startedThrough']=index;self.save(state,record);self.checkpoint('BeforeEntry',index);self.tick()
                            result=child.apply(self.child_request(request,index,entry),session)
                            if result['state']!='Completed' or not result['bindingCurrent']:self.reject('ENTRY_RESULT_UNKNOWN')
                            record['finishedThrough']=index;self.save(state,record);self.checkpoint('AfterEntry',index)
                    self.recover(state,record,session)
                except BaseException:
                    try:self.recover(state,record,session)
                    except BaseException:
                        record.update(state='Unknown',completedCount=0,changedCount=0,abortedCount=0,notStartedCount=0,unknownCount=request['plan']['entryCount'])
                        self.save(state,record)
                    raise
                return self.public(record,session,False)
        def observe(self,state,record,session,entries):
            self.begin(True);outcomes=[]
            current=self.walk(record['plan'],session)
            original={e['relativePath']:(e['plan']['kind'],e['plan']['sourceIdentity'][:2]) for e in entries}
            if {e['relativePath']:(e['kind'],e['identity'][:2]) for e in current}!=original:self.reject('TREE_CHANGED')
            with self.child_engine(state,record,False) as (child,fd):
                for index,entry in enumerate(entries):
                    self.tick();sub=self.child_request(record,index,entry);phase='NotStarted';version='';changed=False
                    if index<=record['startedThrough']:
                        try:result=child.inspect(sub,session)
                        except self.r.Rejected as error:
                            if str(error)!='CONTAINER_PERMISSION_NOT_FOUND':raise
                            result=None
                        if result is None and index>record['finishedThrough']:
                            if child.plan(sub['plan']['command'],session)==sub['plan']:result=child.discard(sub,session)
                        if result is None:phase='Unknown'
                        elif result['state']=='Completed' and result['bindingCurrent']:phase='Completed';version=result['version'];changed=not entry['plan']['noChange']
                        elif result['state']=='Aborted' and child.plan(sub['plan']['command'],session)==sub['plan']:phase='Aborted'
                        else:phase='Unknown'
                    elif child.present(fd,sub['operationId']+'.json') or child.plan(sub['plan']['command'],session)!=sub['plan']:phase='Unknown'
                    outcomes.append({'id':sub['operationId'],'state':phase,'version':version,'changed':changed})
            return outcomes
        def recover(self,state,record,session):
            if record['state'] in self.TERMINAL:return
            try:
                saved=self.history.load(record);entries=self.validate_manifest(record,saved)
                record.update(historyMayExist=True,historyVerified=True,historyDigest=saved['historyDigest'])
                if record['startedThrough']<0:
                    record['state']='Aborted';self.save(state,record);return
                outcomes=self.observe(state,record,session,entries)
                for phase,key in [('Completed','completedCount'),('Aborted','abortedCount'),('NotStarted','notStartedCount'),('Unknown','unknownCount')]:record[key]=sum(r['state']==phase for r in outcomes)
                record['changedCount']=sum(r['changed'] for r in outcomes);record['outcomeDigest']=digest(b'whaledeck-recursive-permission-outcomes-v1\n',outcomes)
                for index,outcome in enumerate(outcomes):
                    if outcome['state']!='Completed':break
                    record['finishedThrough']=max(record['finishedThrough'],index)
                record['state']='Unknown' if record['unknownCount'] else 'Completed' if record['completedCount']==len(entries) else 'Partial' if record['changedCount'] else 'Aborted'
            except FileNotFoundError:
                record['state']='Aborted' if record['startedThrough']<0 else 'Unknown'
                if record['state']=='Unknown':record.update(completedCount=0,changedCount=0,abortedCount=0,notStartedCount=0,unknownCount=record['plan']['entryCount'])
            except (OSError,self.r.Rejected,self.f.Rejected,ValueError,TypeError,KeyError):
                record.update(state='Unknown',completedCount=0,changedCount=0,abortedCount=0,notStartedCount=0,unknownCount=record['plan']['entryCount'])
            self.save(state,record)
        def inspect(self,request,session):
            self.begin(True)
            with self.locked(request) as (state,record):
                if record is None:self.reject('NOT_FOUND')
                self.recover(state,record,session);return self.public(record,session,True)
        def discard(self,request,session):
            self.begin(True)
            with self.locked(request) as (state,record):
                if record is None:
                    record=self.new_record(request)
                    if self.history.has_artifacts(request) or self.present(state,request['operationId']+'.entries'):
                        record.update(state='Unknown',unknownCount=request['plan']['entryCount'],notStartedCount=0,historyMayExist=True)
                    else:record['state']='Aborted'
                    self.save(state,record)
                else:self.recover(state,record,session)
                return self.public(record,session,False)
        def public(self,record,session,replayed):
            binding=False;available=False;entries=None
            if record['historyVerified']:
                try:
                    saved=self.history.load(record);entries=self.validate_manifest(record,saved);available=True
                except (OSError,self.r.Rejected,self.f.Rejected,ValueError,TypeError,KeyError):pass
            if record['state']=='Completed' and session is not None and available:
                try:
                    fd=self.f.trusted_directory(self.state_path)
                    try:
                        outcomes=self.observe(fd,record,session,entries)
                        binding=all(r['state']=='Completed' for r in outcomes) and digest(b'whaledeck-recursive-permission-outcomes-v1\n',outcomes)==record['outcomeDigest']
                    finally:os.close(fd)
                except (OSError,self.r.Rejected,self.f.Rejected,ValueError,TypeError,KeyError):pass
            keys=('operationId','state','completedCount','changedCount','abortedCount','notStartedCount','unknownCount','historyMayExist','historyVerified','historyDigest','outcomeDigest')
            return {**{k:record[k] for k in keys},'containerId':record['plan']['command']['containerId'],'instanceId':record['plan']['command']['instanceId'],
                'targetProof':record['plan']['targetProof'],'fingerprint':record['plan']['fingerprint'],'bindingCurrent':binding,'historyAvailable':available,'replayed':replayed}
    return RecursivePermissions
