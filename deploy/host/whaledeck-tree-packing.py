#!/usr/bin/env python3
"""Durable registered directory -> new tar/tar.gz, not a public entry point.

Source never removed; no existing target overwritten. Signed administrator
plans, shared hierarchy locks, RPC/Worker and audit must surround this engine
before exposure. A failed attempt is observed, never re-compressed/promoted.
"""
import contextlib
import base64
import fcntl
import hashlib
import hmac
import json
import os
import re
import stat
import time


class FileTreePacking:
    DOMAIN = b'whaledeck-tree-packing-v1\n'
    FOLDER = 'tree-packing'
    TERMINAL = ('Completed', 'Aborted', 'Unknown')
    TRANSIENT = ('Preparing', 'Staging', 'Ready', 'Publishing')
    PLAN_FIELDS = {'command','manifestSha256','entryCount','totalBytes','targetParent','sourcePolicyHash','targetPolicyHash','fingerprint'}

    def __init__(self, files, manifest_type, codec_type, *, cancelled=None):
        self.f, self.manifest_type, self.codec_type, self.cancelled = files, manifest_type, codec_type, cancelled
        if cancelled is not None and not callable(cancelled): self.reject('FILES_TREE_COMMAND_INVALID')

    def reject(self, code): raise self.f.Rejected(code)

    @staticmethod
    def encode(value): return json.dumps(value,sort_keys=True,ensure_ascii=False,separators=(',',':'),allow_nan=False).encode()

    def digest(self, value): return hashlib.sha256(self.DOMAIN+self.encode(value)).hexdigest().upper()

    @staticmethod
    def identity(descriptor):
        info = os.fstat(descriptor); return [info.st_dev,info.st_ino]

    def validate(self, command):
        if not isinstance(command,dict) or set(command) != {'source','target','format'} or command['format'] not in ('tar','tar.gz'):
            self.reject('FILES_PACK_COMMAND_INVALID')
        for key,fields in (('source',{'rootId','path','expectedVersion'}),('target',{'rootId','path'})):
            value = command[key]
            if not isinstance(value,dict) or set(value) != fields or any(type(item) is not str for item in value.values()) or not re.fullmatch('[a-z0-9.-]{1,64}',value['rootId']):
                self.reject('FILES_PACK_COMMAND_INVALID')
            self.f.relative(value['path'])
            if self.f.protected(value['path']): self.reject('FILES_PROTECTED')
        if not re.fullmatch('[A-F0-9]{64}',command['source']['expectedVersion']): self.reject('FILES_VERSION_CONFLICT')
        if not command['target']['path'].endswith('.'+command['format']): self.reject('FILES_ARCHIVE_FORMAT_INVALID')

    def policies(self, command):
        roots = self.f.roots(); source = roots.get(command['source']['rootId']); target = roots.get(command['target']['rootId'])
        if source is None or target is None or not {'files-list','files-download','files-archive','files-tree-pack'} <= set(source['allowedActions']) or not {'files-mutate','files-upload','files-archive','files-tree-pack'} <= set(target['allowedActions']):
            self.reject('FILES_ROOT_FORBIDDEN')
        origin = source['path'].rstrip('/')+'/'+command['source']['path']
        destination = target['path'].rstrip('/')+'/'+command['target']['path']
        if origin == destination or destination.startswith(origin+'/') or origin.startswith(destination+'/'):
            self.reject('FILES_TREE_OVERLAP')
        return source,target

    def target(self, command): return self.f.directory(command['target'],('mutate','upload','archive'))

    def bound(self, plan):
        source,target = self.policies(plan['command'])
        if self.digest(source) != plan['sourcePolicyHash'] or self.digest(target) != plan['targetPolicyHash']: self.reject('FILES_ROOT_CHANGED')
        return target

    def validate_plan(self, plan):
        if not isinstance(plan,dict) or set(plan) != self.PLAN_FIELDS: self.reject('FILES_PLAN_INVALID')
        self.validate(plan['command'])
        for key in ('manifestSha256','sourcePolicyHash','targetPolicyHash','fingerprint'):
            if type(plan[key]) is not str or not re.fullmatch('[A-F0-9]{64}',plan[key]): self.reject('FILES_PLAN_INVALID')
        parent = plan['targetParent']
        if type(plan['entryCount']) is not int or not 1 <= plan['entryCount'] <= 10000 or type(plan['totalBytes']) is not int or not 0 <= plan['totalBytes'] <= 16*1024**3 or not isinstance(parent,list) or len(parent) != 2 or any(type(item) is not int or not 0 <= item < 2**64 for item in parent) or not parent[1]:
            self.reject('FILES_PLAN_INVALID')
        if not hmac.compare_digest(plan['fingerprint'],self.digest({key:item for key,item in plan.items() if key != 'fingerprint'})): self.reject('FILES_PLAN_INVALID')

    def plan(self, command):
        self.validate(command); source,target = self.policies(command)
        manifest = self.manifest_type(self.f,cancelled=self.cancelled).scan(command['source'])
        with self.target(command) as (_,parent,leaf):
            try: os.stat(leaf,dir_fd=parent,follow_symlinks=False)
            except FileNotFoundError: pass
            else: self.reject('FILES_VERSION_CONFLICT')
            parent_identity = self.identity(parent)
            if any(row['kind'] == 'Directory' and row['sourceIdentity'][:2] == parent_identity for row in manifest['entries']): self.reject('FILES_TREE_OVERLAP')
        value = {'command':command,'manifestSha256':manifest['manifestSha256'],'entryCount':manifest['entryCount'],'totalBytes':manifest['totalBytes'],
            'targetParent':parent_identity,'sourcePolicyHash':self.digest(source),'targetPolicyHash':self.digest(target)}
        return {**value,'fingerprint':self.digest(value)}

    def read(self, folder, operation):
        try: descriptor = self.f.open_at(folder,operation+'.json',os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW)
        except FileNotFoundError: return None
        try:
            info = os.fstat(descriptor)
            if not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or stat.S_IMODE(info.st_mode) != 0o600 or info.st_nlink != 1 or info.st_size > 32768: self.reject('FILES_RECEIPT_UNSAFE')
            value = json.loads(os.read(descriptor,32769),object_pairs_hook=self.f.unique_object)
            if not isinstance(value,dict) or value.get('operationId') != operation or value.get('state') not in self.TERMINAL+self.TRANSIENT or type(value.get('owner')) is not str or not re.fullmatch('[A-F0-9]{64}',value['owner']): self.reject('FILES_RECEIPT_UNSAFE')
            self.validate_plan(value.get('plan'))
            if type(value.get('mayHaveStaged',False)) is not bool: self.reject('FILES_RECEIPT_UNSAFE')
            if 'newIdentity' in value:
                identity = value['newIdentity']
                if not isinstance(identity,list) or len(identity) != 8 or any(type(item) is not int for item in identity) or identity[1] <= 0: self.reject('FILES_RECEIPT_UNSAFE')
            if value['state'] in ('Ready','Publishing','Completed'):
                if 'newIdentity' not in value or type(value.get('archiveBytes')) is not int or not 1 <= value['archiveBytes'] <= 17*1024**3 or value['archiveBytes'] != value['newIdentity'][6]: self.reject('FILES_RECEIPT_UNSAFE')
                for key in ('archiveSha256','contentSha256'):
                    if type(value.get(key)) is not str or not re.fullmatch('[A-F0-9]{64}',value[key]): self.reject('FILES_RECEIPT_UNSAFE')
            if value['state'] == 'Completed' and (type(value.get('version')) is not str or not re.fullmatch('[A-F0-9]{64}',value['version'])): self.reject('FILES_RECEIPT_UNSAFE')
            return value
        except (ValueError,TypeError,KeyError,UnicodeError): self.reject('FILES_RECEIPT_UNSAFE')
        finally: os.close(descriptor)

    @contextlib.contextmanager
    def state(self, operation, owner, create=False, namespace=True):
        self.f.identity(operation)
        if type(owner) is not str or not re.fullmatch('[A-F0-9]{64}',owner): self.reject('FILES_OPERATION_INVALID')
        base = self.f.trusted_directory(self.f.STATE); folder = lock = None
        try:
            try: os.mkdir(self.FOLDER,0o700,dir_fd=base); os.fsync(base)
            except FileExistsError: pass
            folder = self.f.open_at(base,self.FOLDER,os.O_RDONLY | os.O_DIRECTORY); info = os.fstat(folder)
            if info.st_uid != 0 or stat.S_IMODE(info.st_mode) != 0o700: self.reject('FILES_RECEIPT_UNSAFE')
            existing = self.read(folder,operation)
            if existing is not None and existing['owner'] != owner or existing is None and not create: self.reject('FILES_OPERATION_NOT_FOUND')
            if existing is None:
                with os.scandir(folder) as rows:
                    for count,_ in enumerate(rows,1):
                        if count >= 20000: self.reject('FILES_RECEIPT_LIMIT')
            lock = os.open(operation+'.lock',os.O_WRONLY | os.O_CREAT | os.O_NOFOLLOW,0o600,dir_fd=folder); info = os.fstat(lock)
            if not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_nlink != 1 or stat.S_IMODE(info.st_mode) != 0o600: self.reject('FILES_RECEIPT_UNSAFE')
            try: fcntl.flock(lock,fcntl.LOCK_EX | fcntl.LOCK_NB)
            except BlockingIOError: self.reject('FILES_OPERATION_BUSY')
            with (self.f.namespace_lock() if namespace else contextlib.nullcontext()):
                existing = self.read(folder,operation)
                if existing is not None and existing['owner'] != owner: self.reject('FILES_OPERATION_NOT_FOUND')
                yield folder,existing
        finally:
            if lock is not None: os.close(lock)
            if folder is not None: os.close(folder)
            os.close(base)

    def save(self, folder, record):
        if len(self.encode(record)) > 32768: self.reject('FILES_RECEIPT_LIMIT')
        self.f.save_receipt(folder,record['operationId'],record)

    def checkpoint(self, phase):
        """Trusted disposable-test crash instrumentation, never request input."""

    def request(self, request):
        if not isinstance(request,dict) or set(request) != {'operationId','owner','plan'}: self.reject('FILES_PACK_COMMAND_INVALID')
        self.validate_plan(request['plan'])

    def pack(self, request):
        self.request(request); plan = request['plan']
        with self.state(request['operationId'],request['owner'],True) as (folder,record):
            if record is not None:
                if record['plan'] != plan: self.reject('FILES_IDEMPOTENCY_CONFLICT')
                self.recover(folder,record); return self.public(record)
            if self.plan(plan['command']) != plan: self.reject('FILES_VERSION_CONFLICT')
            record = {**request,'state':'Preparing'}; self.save(folder,record); self.checkpoint('Preparing')
            try: self.publish(folder,record)
            except BaseException:
                with contextlib.suppress(OSError,self.f.Rejected): self.recover(folder,record)
                raise
            return self.public(record)

    def discard(self, request):
        self.request(request)
        with self.state(request['operationId'],request['owner'],True) as (folder,record):
            if record is None: record = {**request,'state':'Aborted'}; self.save(folder,record)
            else:
                if record['plan'] != request['plan']: self.reject('FILES_IDEMPOTENCY_CONFLICT')
                self.recover(folder,record)
            return self.public(record)

    def publish(self, folder, record):
        plan = record['plan']; command = plan['command']; deadline = time.monotonic()+600
        def check():
            if self.cancelled is not None and self.cancelled(): self.reject('FILES_TREE_CANCELED')
            if time.monotonic() >= deadline: self.reject('FILES_TREE_DEADLINE')
            return False
        codec = self.codec_type(self.f,self.manifest_type,cancelled=check)
        policy = self.bound(plan); check()
        with self.target(command) as (_,parent,leaf):
            if self.identity(parent) != plan['targetParent']: self.reject('FILES_PARENT_CHANGED')
            capacity = os.fstatvfs(parent)
            estimate = plan['totalBytes']+plan['totalBytes']//100+plan['entryCount']*2048+16*1024**2+65536
            reserve = max(512*1024**2,capacity.f_blocks*capacity.f_frsize//20)
            if capacity.f_bavail*capacity.f_frsize < estimate+reserve: self.reject('FILES_CAPACITY_REQUIRED')
            private = self.f.staging(parent); output = None; name = record['operationId']+'.tree-pack'
            try:
                record.update(state='Staging',mayHaveStaged=True); self.save(folder,record)
                self.checkpoint('StagingIntent')
                output = os.open(name,os.O_CREAT | os.O_EXCL | os.O_RDWR | os.O_NOFOLLOW,0o600,dir_fd=private); os.fsync(private)
                self.checkpoint('Created')
                record['newIdentity'] = self.f.publication_identity(os.fstat(output)); self.save(folder,record); self.checkpoint('Staging')
                result = codec.create(command['source'],plan['manifestSha256'],output,command['format'])
                if (result['entryCount'],result['totalBytes']) != (plan['entryCount'],plan['totalBytes']): self.reject('FILES_TREE_CHANGED')
                os.fchown(output,policy.get('ownerUid',0),policy.get('ownerGid',0)); os.fchmod(output,0o640); os.fsync(output)
                record.update(state='Ready',newIdentity=self.f.publication_identity(os.fstat(output)),archiveBytes=result['archiveBytes'],archiveSha256=result['archiveSha256'],contentSha256=result['contentSha256'])
                self.save(folder,record); self.checkpoint('Ready'); check(); self.bound(plan)
                self.manifest_type(self.f,cancelled=check).verify(command['source'],plan['manifestSha256'])
                with self.target(command) as (_,current,_):
                    if self.identity(current) != plan['targetParent']: self.reject('FILES_PARENT_CHANGED')
                self.verify(output,record,codec)
                record['state']='Publishing'; self.save(folder,record); self.checkpoint('Publishing'); check(); self.bound(plan)
                self.manifest_type(self.f,cancelled=check).verify(command['source'],plan['manifestSha256'])
                with self.target(command) as (_,current,_):
                    if self.identity(current) != plan['targetParent']: self.reject('FILES_PARENT_CHANGED')
                if self.f.publication_identity(os.stat(name,dir_fd=private,follow_symlinks=False)) != record['newIdentity']: self.reject('FILES_ARCHIVE_CHANGED')
                self.f.exchange(private,name,parent,leaf,1); os.fsync(parent); os.fsync(private); self.checkpoint('Published')
                self.recover(folder,record)
            finally:
                if output is not None: os.close(output)
                os.close(private)

    def verify(self, descriptor, record, codec):
        if self.f.publication_identity(os.fstat(descriptor)) != record['newIdentity']: self.reject('FILES_ARCHIVE_CHANGED')
        result = codec.inspect(descriptor,record['plan']['command']['format'])
        if any(result[key] != record[key] for key in ('archiveBytes','archiveSha256','contentSha256')) or (result['entryCount'],result['totalBytes']) != (record['plan']['entryCount'],record['plan']['totalBytes']): self.reject('FILES_ARCHIVE_CHANGED')

    def recover(self, folder, record):
        if record['state'] in self.TERMINAL: return
        previous = record['state']; record['state']='Unknown'; plan = record['plan']; codec = self.codec_type(self.f,self.manifest_type)
        try:
            self.bound(plan)
            with self.target(plan['command']) as (_,parent,leaf):
                if self.identity(parent) != plan['targetParent']: self.reject('FILES_PARENT_CHANGED')
                try: target = self.f.open_at(parent,leaf,os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW)
                except FileNotFoundError: target = None
                if target is not None:
                    try:
                        if previous in ('Ready','Publishing'):
                            self.verify(target,record,codec); record.update(state='Completed',version=self.f.version(os.fstat(target)))
                    finally: os.close(target)
                else:
                    private = self.f.staging(parent)
                    try:
                        try: staged = self.f.open_at(private,record['operationId']+'.tree-pack',os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW)
                        except FileNotFoundError: staged = None
                        if staged is not None:
                            try:
                                info = os.fstat(staged)
                                if stat.S_ISREG(info.st_mode) and info.st_nlink == 1 and self.identity(staged) == record.get('newIdentity',[])[:2]: record['state']='Aborted'
                            finally: os.close(staged)
                        elif previous == 'Preparing' and not record.get('mayHaveStaged'): record['state']='Aborted'
                    finally: os.close(private)
        except (OSError,self.f.Rejected): pass
        self.save(folder,record)

    def lookup(self, request):
        if not isinstance(request,dict) or set(request) != {'operationId','owner'}: self.reject('FILES_STATUS_INVALID')
        with self.state(request['operationId'],request['owner']) as (folder,record):
            self.recover(folder,record); return self.public(record)

    def public(self, record):
        completed = record['state'] == 'Completed'
        return {'operationId':record['operationId'],'state':record['state'],'fingerprint':record['plan']['fingerprint'],'format':record['plan']['command']['format'],
            'entryCount':record['plan']['entryCount'],'totalBytes':record['plan']['totalBytes'],'archiveBytes':record.get('archiveBytes',0) if completed else 0,
            'archiveSha256':record.get('archiveSha256','') if completed else '', 'version':record.get('version','') if completed else '',
            'partialRetained':not completed and bool(record.get('mayHaveStaged'))}

    def download(self, request, output=None):
        if not isinstance(request,dict) or set(request) != {'operationId','owner'}: self.reject('FILES_STATUS_INVALID')
        codec = self.codec_type(self.f,self.manifest_type,cancelled=self.cancelled); guard=codec.guard(); policy_checked=0
        # Read-only completed downloads hold their own operation lock, not the
        # global write gate. Concurrent target edits fail version/digest checks;
        # unrelated file writes must not queue behind a large archive stream.
        with self.state(request['operationId'],request['owner'],namespace=False) as (_,record):
            if record['state'] != 'Completed': self.reject('FILES_PACK_NOT_COMPLETED')
            plan=record['plan']; target=plan['command']['target']
            def check_policy():
                policy=self.f.roots().get(target['rootId'])
                if policy is None or not {'files-download','files-archive','files-tree-pack'} <= set(policy['allowedActions']): self.reject('FILES_ROOT_FORBIDDEN')
                if self.digest(policy) != plan['targetPolicyHash']: self.reject('FILES_ROOT_CHANGED')
            def check():
                nonlocal policy_checked
                guard(); now=time.monotonic()
                if now-policy_checked>=1: check_policy(); policy_checked=now
            check_policy(); check()
            with self.f.directory(target,('download','archive','tree-pack')) as (_,parent,leaf):
                if self.identity(parent) != plan['targetParent']: self.reject('FILES_PARENT_CHANGED')
                descriptor=self.f.open_at(parent,leaf,os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW)
                try:
                    before=codec.regular(descriptor)
                    if self.f.version(before) != record['version']: self.reject('FILES_ARCHIVE_CHANGED')
                    self.verify(descriptor,record,codec); check_policy(); check()
                    self.f.emit({'type':'metadata',**self.f.metadata(leaf,before)},output)
                    position=0; checksum=hashlib.sha256()
                    while position<before.st_size:
                        check(); block=os.pread(descriptor,min(self.f.CHUNK,before.st_size-position),position)
                        if not block: self.reject('FILES_ARCHIVE_CHANGED')
                        checksum.update(block); self.f.emit({'type':'chunk','offset':position,'data':base64.b64encode(block).decode('ascii')},output); position+=len(block)
                    check_policy(); check()
                    if os.pread(descriptor,1,position) or self.f.version(os.fstat(descriptor)) != record['version'] or self.f.version(os.stat(leaf,dir_fd=parent,follow_symlinks=False)) != record['version'] or checksum.hexdigest().upper() != record['archiveSha256']:
                        self.reject('FILES_ARCHIVE_CHANGED')
                    with self.f.directory(target,('download','archive','tree-pack')) as (_,current,_):
                        if self.identity(current) != plan['targetParent']: self.reject('FILES_PARENT_CHANGED')
                    self.f.emit({'type':'end','sizeBytes':position,'sha256':record['archiveSha256'],'version':record['version']},output)
                finally: os.close(descriptor)
