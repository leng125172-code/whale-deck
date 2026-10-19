#!/usr/bin/env python3
"""Internal durable directory capture on the existing attested HDD store.

No CLI/RPC or source mutation. Callers still need signed administrator intent,
resource locks and a separate recycle/restore/publication workflow. Interrupted
captures are never truncated, re-copied or automatically promoted. Recovery may
only recognize an already-published exact archive and finish its receipt.
"""
import contextlib
import fcntl
import hashlib
import json
import os
import re
import stat
import time


class TreeBackups:
    FOLDER = 'directory-archives'

    def __init__(self, files, backups, manifest_type, codec_type, *, cancelled=None):
        self.f, self.b, self.manifest_type, self.codec_type = files, backups, manifest_type, codec_type
        if cancelled is not None and not callable(cancelled): self.reject('FILES_TREE_BACKUP_INVALID')
        self.cancelled = cancelled

    def reject(self, code): raise self.f.Rejected(code)

    @staticmethod
    def encode(value): return json.dumps(value, sort_keys=True, ensure_ascii=False, separators=(',', ':'), allow_nan=False).encode()

    @classmethod
    def digest(cls, value): return hashlib.sha256(cls.encode(value)).hexdigest().upper()

    def request(self, request):
        if not isinstance(request, dict) or set(request) != {'operationId','owner','source','manifestSha256','captureKind'}: self.reject('FILES_TREE_BACKUP_INVALID')
        self.f.identity(request['operationId'])
        if any(type(request[key]) is not str or not re.fullmatch('[A-F0-9]{64}',request[key]) for key in ('owner','manifestSha256')) or request['captureKind'] not in ('Trash','Overwrite'):
            self.reject('FILES_TREE_BACKUP_INVALID')
        source = request['source']
        if not isinstance(source,dict) or set(source) != {'rootId','path','expectedVersion'} or any(type(item) is not str for item in source.values()) or not re.fullmatch('[a-z0-9.-]{1,64}',source['rootId']) or not re.fullmatch('[A-F0-9]{64}',source['expectedVersion']):
            self.reject('FILES_TREE_BACKUP_INVALID')
        self.f.relative(source['path'])
        if self.f.protected(source['path']): self.reject('FILES_TREE_PROTECTED')
        policy = self.f.roots().get(source['rootId'])
        if policy is None or not {'files-list','files-download'} <= set(policy['allowedActions']): self.reject('FILES_ROOT_FORBIDDEN')
        return policy

    @contextlib.contextmanager
    def storage(self, request):
        with self.b.target() as (configuration, base):
            try: os.mkdir(self.FOLDER,0o700,dir_fd=base); os.fsync(base)
            except FileExistsError: pass
            folder = self.f.open_at(base,self.FOLDER,os.O_RDONLY | os.O_DIRECTORY); lock = None
            try:
                info = os.fstat(folder)
                if info.st_uid != 0 or stat.S_IMODE(info.st_mode) != 0o700: self.reject('FILES_TREE_BACKUP_STORE_UNSAFE')
                # Reserve a bounded number of directory entries even when a
                # failed capture left only a lock or partial object.
                try: os.stat(request['operationId']+'.json',dir_fd=folder,follow_symlinks=False); existing = True
                except FileNotFoundError: existing = False
                if not existing:
                    count = 0
                    with os.scandir(folder) as children:
                        for _ in children:
                            count += 1
                            if count >= configuration['maxVersions'] * 5: self.reject('FILES_BACKUP_HISTORY_LIMIT')
                lock = os.open(request['operationId']+'.lock',os.O_WRONLY | os.O_CREAT | os.O_NOFOLLOW,0o600,dir_fd=folder)
                info = os.fstat(lock)
                if not stat.S_ISREG(info.st_mode) or info.st_nlink != 1 or info.st_uid != 0 or stat.S_IMODE(info.st_mode) != 0o600: self.reject('FILES_TREE_BACKUP_STORE_UNSAFE')
                try: fcntl.flock(lock,fcntl.LOCK_EX | fcntl.LOCK_NB)
                except BlockingIOError: self.reject('FILES_OPERATION_BUSY')
                yield configuration,folder
            finally:
                if lock is not None: os.close(lock)
                os.close(folder)

    def seed(self, request, policy, configuration, folder):
        binding = {key:configuration[key] for key in ('storageId','mountPoint','filesystemUuid','targetRelative')}
        info = os.fstat(folder)
        return {'schemaVersion':1,'kind':'Directory','storageId':configuration['storageId'],**request,
            'policyHash':self.digest(policy),'storageHash':self.digest(binding),'storeIdentity':[info.st_dev,info.st_ino]}

    def capacity(self, folder, configuration, estimate):
        count = 0
        with os.scandir(folder) as children:
            for item in children:
                if re.fullmatch(r'[a-f0-9]{32}\.json',item.name): count += 1
                if count >= configuration['maxVersions']: self.reject('FILES_BACKUP_HISTORY_LIMIT')
        usage = os.fstatvfs(folder)
        reserve = max(configuration['reserveBytes'],usage.f_blocks * usage.f_frsize * configuration['reservePercent']//100)
        if usage.f_bavail * usage.f_frsize < estimate + reserve: self.reject('FILES_BACKUP_CAPACITY_REQUIRED')

    def checkpoint(self, phase):
        """Trusted disposable-test crash instrumentation, never request data."""

    def read(self, folder, operation, seed):
        try: value = self.b.read_json(folder,operation+'.json')
        except FileNotFoundError: return None
        if value.get('owner') != seed['owner']: self.reject('FILES_OPERATION_NOT_FOUND')
        if any(value.get(key) != item for key,item in seed.items()): self.reject('FILES_TREE_BACKUP_IDEMPOTENCY_CONFLICT')
        if type(value.get('schemaVersion')) is not int or not isinstance(value.get('storeIdentity'),list) or len(value['storeIdentity']) != 2 or any(type(item) is not int for item in value['storeIdentity']):
            self.reject('FILES_TREE_BACKUP_METADATA_INVALID')
        if value.get('state') not in ('Preparing','Ready','Verified') or type(value.get('createdAtUnixMs')) is not int or not 0 <= value['createdAtUnixMs'] <= 253402300799999 or type(value.get('differentSourceDevice')) is not bool:
            self.reject('FILES_TREE_BACKUP_METADATA_INVALID')
        for key in ('manifestFileSha256','contentSha256'):
            if type(value.get(key)) is not str or not re.fullmatch('[A-F0-9]{64}',value[key]): self.reject('FILES_TREE_BACKUP_METADATA_INVALID')
        if type(value.get('entryCount')) is not int or not 1 <= value['entryCount'] <= 10000 or type(value.get('totalBytes')) is not int or not 0 <= value['totalBytes'] <= 16 * 1024**3:
            self.reject('FILES_TREE_BACKUP_METADATA_INVALID')
        if value['state'] in ('Ready','Verified'):
            if type(value.get('archiveSha256')) is not str or not re.fullmatch('[A-F0-9]{64}',value['archiveSha256']) or type(value.get('archiveBytes')) is not int or not 1 <= value['archiveBytes'] <= 17 * 1024**3:
                self.reject('FILES_TREE_BACKUP_METADATA_INVALID')
            identity = value.get('archiveIdentity')
            if not isinstance(identity,list) or len(identity) != 2 or any(type(item) is not int or not 0 <= item <= 18446744073709551615 for item in identity) or not identity[1]: self.reject('FILES_TREE_BACKUP_METADATA_INVALID')
        return value

    def manifest(self, folder, record):
        try:
            value = self.b.read_json(folder,record['operationId']+'.manifest.json',maximum=16*1024**2)
            if self.digest(value) != record['manifestFileSha256'] or value['manifestSha256'] != record['manifestSha256'] or value['rootId'] != record['source']['rootId'] or value['path'] != record['source']['path'] or value['rootVersion'] != record['source']['expectedVersion'] or value['entryCount'] != record['entryCount'] or value['totalBytes'] != record['totalBytes']:
                self.reject('FILES_TREE_BACKUP_METADATA_INVALID')
            rows = [self.codec_type.from_manifest(row) for row in value['entries']]
            if hashlib.sha256(b'whaledeck-tree-archive-content-v1\n'+self.encode(rows)).hexdigest().upper() != record['contentSha256']: self.reject('FILES_TREE_BACKUP_METADATA_INVALID')
            return value
        except (KeyError,TypeError,ValueError,UnicodeError): self.reject('FILES_TREE_BACKUP_METADATA_INVALID')

    def save_manifest(self, folder, operation, manifest, check):
        raw = self.encode(manifest)
        if len(raw) > 16 * 1024**2: self.reject('FILES_TREE_MANIFEST_LIMIT')
        pending = operation+'.manifest.partial'
        try: descriptor = os.open(pending,os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW,0o600,dir_fd=folder)
        except FileExistsError: self.reject('FILES_TREE_BACKUP_INCOMPLETE')
        try:
            view = memoryview(raw)
            while view:
                check(); count = os.write(descriptor,view[:65536])
                if count <= 0: self.reject('FILES_WRITE_FAILED')
                view = view[count:]
            os.fsync(descriptor)
            self.f.exchange(folder,pending,folder,operation+'.manifest.json',1); os.fsync(folder)
        finally: os.close(descriptor)

    def verify(self, folder, record, codec):
        if record['state'] not in ('Ready','Verified'): self.reject('FILES_TREE_BACKUP_INCOMPLETE')
        self.manifest(folder,record)
        try: descriptor = self.f.open_at(folder,record['operationId']+'.tar.gz',os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW)
        except FileNotFoundError: self.reject('FILES_TREE_BACKUP_INCOMPLETE')
        try:
            info = codec.regular(descriptor,private=True)
            if [info.st_dev,info.st_ino] != record['archiveIdentity']: self.reject('FILES_TREE_BACKUP_CONTENT_INVALID')
            result = codec.inspect(descriptor)
            if any(result[key] != record[key] for key in ('archiveBytes','archiveSha256','contentSha256','entryCount','totalBytes')): self.reject('FILES_TREE_BACKUP_CONTENT_INVALID')
        finally: os.close(descriptor)

    def ensure(self, request):
        policy = self.request(request); deadline = time.monotonic()+600
        def check():
            if self.cancelled is not None and self.cancelled(): self.reject('FILES_TREE_CANCELED')
            if time.monotonic() >= deadline: self.reject('FILES_TREE_DEADLINE')
        check(); codec = self.codec_type(self.f,self.manifest_type,cancelled=lambda:(check(),False)[1])
        with self.storage(request) as (configuration,folder):
            seed = self.seed(request,policy,configuration,folder); saved = self.read(folder,request['operationId'],seed)
            if saved is not None:
                self.verify(folder,saved,codec)
                self.b.attest(configuration,folder); check()
                if self.digest(self.request(request)) != seed['policyHash']: self.reject('FILES_ROOT_CHANGED')
                if saved['state'] != 'Verified':
                    saved['state']='Verified'; self.f.save_receipt(folder,request['operationId'],saved)
                return self.public(saved)
            manifest = self.manifest_type(self.f,cancelled=lambda:(check(),False)[1]).verify(request['source'],request['manifestSha256'])
            estimate = manifest['totalBytes'] + manifest['totalBytes']//100 + manifest['entryCount']*2048 + 16*1024**2 + 65536
            self.capacity(folder,configuration,estimate)
            rows = [self.codec_type.from_manifest(row) for row in manifest['entries']]
            saved = {**seed,'state':'Preparing','createdAtUnixMs':time.time_ns()//1000000,
                'differentSourceDevice':manifest['entries'][0]['sourceIdentity'][0] != os.fstat(folder).st_dev,
                'manifestFileSha256':self.digest(manifest),'contentSha256':hashlib.sha256(b'whaledeck-tree-archive-content-v1\n'+self.encode(rows)).hexdigest().upper(),
                'entryCount':manifest['entryCount'],'totalBytes':manifest['totalBytes']}
            self.f.save_receipt(folder,request['operationId'],saved); self.checkpoint('Preparing')
            self.save_manifest(folder,request['operationId'],manifest,check); self.checkpoint('ManifestStored')
            try: output = os.open(request['operationId']+'.partial',os.O_CREAT | os.O_EXCL | os.O_RDWR | os.O_NOFOLLOW,0o600,dir_fd=folder)
            except FileExistsError: self.reject('FILES_TREE_BACKUP_INCOMPLETE')
            try:
                self.checkpoint('ArchiveCreated')
                result = codec.create(request['source'],request['manifestSha256'],output)
                self.b.attest(configuration,folder); check()
                current = self.f.roots().get(request['source']['rootId'])
                if current is None or self.digest(current) != seed['policyHash']: self.reject('FILES_ROOT_CHANGED')
                info = os.fstat(output)
                saved.update(state='Ready',archiveIdentity=[info.st_dev,info.st_ino],archiveBytes=result['archiveBytes'],archiveSha256=result['archiveSha256'])
                self.f.save_receipt(folder,request['operationId'],saved); self.checkpoint('Ready'); check()
                self.f.exchange(folder,request['operationId']+'.partial',folder,request['operationId']+'.tar.gz',1); os.fsync(folder)
                self.checkpoint('Published'); self.verify(folder,saved,codec)
                self.b.attest(configuration,folder); check()
                if self.digest(self.request(request)) != seed['policyHash']: self.reject('FILES_ROOT_CHANGED')
                saved['state']='Verified'; self.f.save_receipt(folder,request['operationId'],saved)
                return self.public(saved)
            finally: os.close(output)

    @staticmethod
    def public(record):
        return {key:record[key] for key in ('operationId','storageId','captureKind','state','manifestSha256','entryCount','totalBytes',
            'archiveBytes','archiveSha256','contentSha256','createdAtUnixMs','differentSourceDevice')}
