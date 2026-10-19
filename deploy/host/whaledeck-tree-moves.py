#!/usr/bin/env python3
"""Directory move engine behind a distinct fixed helper and signed UDS RPC.

Publish a fully verified target, then retain individually verified original
files in an operation-owned private tree. Never rename a source ancestor or
recursively delete it. Recovery never resumes copy/rename/rmdir. Retained
originals are not an HDD backup. BFF hierarchy locks are still required.
"""
import contextlib
import fcntl
import hashlib
import hmac
import json
import os
import re
import stat
import time
import uuid


class FileTreeMoves:
    TERMINAL = ('Completed', 'Aborted', 'Unknown')
    PHASES = ('Preparing', 'DestinationCopying', 'DestinationCompleted', 'SourcePreparing', 'SourceRetaining', 'SourceRemoving')
    MAX_MANIFEST = 16 * 1024**2

    def __init__(self, files, manifest_type, copy_type, *, cancelled=None):
        self.f, self.manifest_type, self.cancelled = files, manifest_type, cancelled
        engine = self
        class MoveCopy(copy_type):
            DOMAIN = b'whaledeck-tree-move-destination-v1\n'
            STATE_DIRECTORY = 'tree-move-copies'
            SOURCE_ACTIONS = frozenset(('files-list', 'files-download', 'files-mutate', 'files-tree-move'))
            TARGET_ACTIONS = frozenset(('files-mutate', 'files-upload', 'files-tree-move'))
            def save(self, folder, record):
                engine.write_json(folder, record['operationId'] + '.json', record, 32768)
        self.copy = MoveCopy(files, manifest_type, cancelled=cancelled)

    def reject(self, code): raise self.f.Rejected(code)

    @staticmethod
    def encode(value): return json.dumps(value, sort_keys=True, ensure_ascii=False, separators=(',', ':'), allow_nan=False).encode()

    @classmethod
    def digest(cls, value): return hashlib.sha256(b'whaledeck-tree-move-v1\n' + cls.encode(value)).hexdigest().upper()

    def plan(self, command):
        copied = self.copy.plan(command)
        with self.f.directory(command['source'], 'mutate') as (_, parent, leaf):
            if self.f.version(os.stat(leaf, dir_fd=parent, follow_symlinks=False)) != command['source']['expectedVersion']: self.reject('FILES_TREE_CHANGED')
            value = {'copyPlan': copied, 'sourceParent': self.copy.parent_identity(parent)}
        return {**value, 'fingerprint': self.digest(value)}

    def validate(self, plan):
        if not isinstance(plan, dict) or set(plan) != {'copyPlan', 'sourceParent', 'fingerprint'}: self.reject('FILES_PLAN_INVALID')
        self.copy.validate_plan(plan['copyPlan']); identity = plan['sourceParent']
        if not isinstance(identity, list) or len(identity) != 2 or any(type(x) is not int or not 0 <= x <= 18446744073709551615 for x in identity) or identity[1] == 0: self.reject('FILES_PLAN_INVALID')
        if type(plan['fingerprint']) is not str or not hmac.compare_digest(plan['fingerprint'], self.digest({key: value for key, value in plan.items() if key != 'fingerprint'})):
            self.reject('FILES_PLAN_INVALID')

    @contextlib.contextmanager
    def state(self, operation, owner, writing=False):
        self.f.identity(operation)
        if type(owner) is not str or not re.fullmatch('[A-F0-9]{64}', owner): self.reject('FILES_OPERATION_INVALID')
        base = self.f.trusted_directory(self.f.STATE); folder = lock = None
        try:
            try: os.mkdir('tree-moves', 0o700, dir_fd=base); os.fsync(base)
            except FileExistsError: pass
            folder = self.f.open_at(base, 'tree-moves', os.O_RDONLY | os.O_DIRECTORY)
            info = os.fstat(folder)
            if info.st_uid != 0 or stat.S_IMODE(info.st_mode) != 0o700: self.reject('FILES_RECEIPT_UNSAFE')
            record = self.read(folder, operation)
            if record is not None and record['owner'] != owner: self.reject('FILES_OPERATION_NOT_FOUND')
            if record is None and not writing: self.reject('FILES_OPERATION_NOT_FOUND')
            if record is None:
                with os.scandir(folder) as entries:
                    for count, _ in enumerate(entries, 1):
                        if count >= 30000: self.reject('FILES_RECEIPT_LIMIT')
            lock = os.open(operation + '.lock', os.O_WRONLY | os.O_CREAT | os.O_NOFOLLOW, 0o600, dir_fd=folder)
            info = os.fstat(lock)
            if not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_nlink != 1 or stat.S_IMODE(info.st_mode) != 0o600: self.reject('FILES_RECEIPT_UNSAFE')
            try: fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
            except BlockingIOError: self.reject('FILES_OPERATION_BUSY')
            record = self.read(folder, operation)
            if record is not None and record['owner'] != owner: self.reject('FILES_OPERATION_NOT_FOUND')
            yield folder, record
        finally:
            if lock is not None: os.close(lock)
            if folder is not None: os.close(folder)
            os.close(base)

    def read_json(self, folder, name, limit):
        descriptor = self.f.open_at(folder, name, os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW)
        try:
            info = os.fstat(descriptor)
            if info.st_uid != 0 or stat.S_IMODE(info.st_mode) != 0o600 or not stat.S_ISREG(info.st_mode) or info.st_nlink != 1 or not 0 < info.st_size <= limit: self.reject('FILES_RECEIPT_UNSAFE')
            chunks = []; total = 0
            while True:
                block = os.read(descriptor, min(65536, limit + 1 - total))
                if not block: break
                chunks.append(block); total += len(block)
                if total > limit: self.reject('FILES_RECEIPT_UNSAFE')
            if total != info.st_size or self.f.version(os.fstat(descriptor)) != self.f.version(info): self.reject('FILES_RECEIPT_UNSAFE')
            try: return json.loads(b''.join(chunks), object_pairs_hook=self.f.unique_object)
            except (ValueError, UnicodeError): self.reject('FILES_RECEIPT_UNSAFE')
        finally: os.close(descriptor)

    def read(self, folder, operation):
        try: value = self.read_json(folder, operation + '.json', 32768)
        except FileNotFoundError: return None
        try:
            if not isinstance(value, dict) or value['operationId'] != operation or value['state'] not in self.TERMINAL + self.PHASES or not re.fullmatch('[A-F0-9]{64}', value['owner']): self.reject('FILES_RECEIPT_UNSAFE')
            self.validate(value['plan'])
            for field in ('destinationPublished', 'destinationStagingRetained', 'sourceMutationStarted', 'sourceRemoved', 'sourceRetained'):
                if field in value and type(value[field]) is not bool: self.reject('FILES_RECEIPT_UNSAFE')
            for field in ('version', 'manifestDigest'):
                if field in value and (type(value[field]) is not str or not re.fullmatch('[A-F0-9]{64}', value[field])): self.reject('FILES_RECEIPT_UNSAFE')
            if value['state'] == 'Completed' and not all(value.get(field) for field in ('destinationPublished', 'sourceRemoved', 'sourceRetained', 'version', 'manifestDigest', 'retainedIdentity')): self.reject('FILES_RECEIPT_UNSAFE')
            if 'retainedIdentity' in value:
                identity = value['retainedIdentity']
                if not isinstance(identity, list) or len(identity) != 2 or any(type(x) is not int or not 0 <= x <= 18446744073709551615 for x in identity) or identity[1] == 0: self.reject('FILES_RECEIPT_UNSAFE')
        except (KeyError, TypeError, ValueError): self.reject('FILES_RECEIPT_UNSAFE')
        return value

    def save(self, folder, record):
        self.write_json(folder, record['operationId'] + '.json', record, 32768)

    def write_json(self, folder, name, value, limit):
        if not re.fullmatch(r'[a-f0-9]{32}(\.manifest)?\.json', name): self.reject('FILES_RECEIPT_UNSAFE')
        payload = self.encode(value)
        if len(payload) > limit: self.reject('FILES_RECEIPT_LIMIT')
        temporary = '.' + uuid.uuid4().hex
        descriptor = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600, dir_fd=folder)
        try:
            with os.fdopen(descriptor, 'wb') as stream: stream.write(payload); stream.flush(); os.fsync(stream.fileno())
            if name.endswith('.manifest.json'): self.f.exchange(folder, temporary, folder, name, 1)
            else: os.replace(temporary, name, src_dir_fd=folder, dst_dir_fd=folder)
            os.fsync(folder)
        finally:
            try: os.unlink(temporary, dir_fd=folder)
            except FileNotFoundError: pass

    def checkpoint(self, phase):
        """Trusted disposable-test crash/race instrumentation, never request data."""

    def move(self, request):
        if not isinstance(request, dict) or set(request) != {'operationId', 'owner', 'plan'}: self.reject('FILES_TREE_COMMAND_INVALID')
        plan = request['plan']; self.validate(plan)
        with self.state(request['operationId'], request['owner'], True) as (folder, record):
            if record is not None:
                if record['plan'] != plan: self.reject('FILES_IDEMPOTENCY_CONFLICT')
                self.recover(folder, record); return self.public(record)
            record = {**request, 'state': 'Preparing'}; self.save(folder, record); self.checkpoint('Preparing')
            try:
                record['state'] = 'DestinationCopying'; self.save(folder, record)
                self.copy.checkpoint = lambda phase: self.checkpoint('Copy' + phase)
                result = self.copy.copy({**request, 'plan': plan['copyPlan']})
                if result['state'] != 'Completed': self.reject('FILES_TREE_DESTINATION_UNCONFIRMED')
                record.update(state='DestinationCompleted', destinationPublished=True, version=result['version'])
                self.save(folder, record); self.checkpoint('DestinationCompleted')
                with self.copy.state(request['operationId'], request['owner']) as (_, copied):
                    self.retain_source(folder, record, copied)
            except BaseException:
                with contextlib.suppress(OSError, self.f.Rejected): self.recover(folder, record)
                raise
            return self.public(record)

    def check_policies(self, record):
        plan = record['plan']['copyPlan']; source, target = self.copy.policies(plan['command'])
        if self.copy.digest(source) != plan['sourcePolicyHash'] or self.copy.digest(target) != plan['targetPolicyHash']: self.reject('FILES_ROOT_CHANGED')
        return source, target

    def check_source(self, record, parent, root):
        command = record['plan']['copyPlan']['command']
        with self.f.directory(command['source'], 'mutate') as (_, resolved, _):
            if self.copy.parent_identity(resolved) != record['plan']['sourceParent']: self.reject('FILES_PARENT_CHANGED')
        current = os.stat(command['source']['path'].rsplit('/', 1)[-1], dir_fd=parent, follow_symlinks=False)
        if self.copy.parent_identity(parent) != record['plan']['sourceParent'] or [current.st_dev, current.st_ino] != self.copy.parent_identity(root) or not stat.S_ISDIR(current.st_mode): self.reject('FILES_TREE_CHANGED')

    def check_ancestors(self, root, path, directories):
        relative = ''
        for component in [''] + (path.split('/') if path else []):
            if component: relative = component if not relative else relative + '/' + component
            descriptor = self.f.open_at(root, relative or '.', os.O_RDONLY | os.O_DIRECTORY)
            try:
                if self.f.publication_identity(os.fstat(descriptor))[:5] != directories[relative]['sourceIdentity'][:5]: self.reject('FILES_TREE_CHANGED')
            finally: os.close(descriptor)

    def check_retention_path(self, parent, private, retained, record, suffix='.tree-move'):
        """A pinned FD alone does not prove its private name still resolves."""
        current = self.f.open_at(parent, self.f.STAGING, os.O_RDONLY | os.O_DIRECTORY)
        try:
            info, original, saved = os.fstat(current), os.fstat(private), os.fstat(retained)
            child = os.stat(record['operationId'] + suffix, dir_fd=current, follow_symlinks=False)
            if info.st_uid != 0 or stat.S_IMODE(info.st_mode) != 0o700 or (info.st_dev, info.st_ino) != (original.st_dev, original.st_ino) or not stat.S_ISDIR(child.st_mode) or [child.st_dev, child.st_ino] != record.get('retainedIdentity') or (child.st_dev, child.st_ino) != (saved.st_dev, saved.st_ino):
                self.reject('FILES_RECEIPT_UNSAFE')
        finally: os.close(current)

    def destination(self, record, copied, check):
        plan = record['plan']['copyPlan']; command = plan['command']; _, target = self.check_policies(record)
        if copied is None or copied['state'] != 'Completed' or copied['plan'] != plan: self.reject('FILES_TREE_DESTINATION_UNCONFIRMED')
        with self.copy.target(command) as (_, parent, leaf):
            if self.copy.parent_identity(parent) != plan['targetParent']: self.reject('FILES_PARENT_CHANGED')
            descriptor = self.f.open_at(parent, leaf, os.O_RDONLY | os.O_DIRECTORY)
            try:
                if self.copy.parent_identity(descriptor) != copied['newIdentity'] or self.copy.snapshot(descriptor, command, target, check) != copied['treeSha256']: self.reject('FILES_TREE_CHANGED')
                return self.f.version(os.fstat(descriptor))
            finally: os.close(descriptor)

    def file_hash(self, parent, leaf, expected, check):
        descriptor = self.f.open_at(parent, leaf, os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW)
        try:
            before = os.fstat(descriptor)
            if not stat.S_ISREG(before.st_mode) or before.st_nlink != 1 or self.f.publication_identity(before) != expected: self.reject('FILES_TREE_CHANGED')
            digest = hashlib.sha256(); count = 0
            while count < before.st_size:
                check(); block = os.read(descriptor, min(self.f.CHUNK, before.st_size - count))
                if not block: self.reject('FILES_READ_CHANGED')
                digest.update(block); count += len(block)
            if os.read(descriptor, 1) or self.f.version(os.fstat(descriptor)) != self.f.version(before) or self.f.publication_identity(os.stat(leaf, dir_fd=parent, follow_symlinks=False)) != expected:
                self.reject('FILES_TREE_CHANGED')
            return digest.hexdigest().upper()
        finally: os.close(descriptor)

    def retain_source(self, folder, record, copied):
        deadline = time.monotonic() + 600; policy_checked = 0
        def check():
            nonlocal policy_checked
            now = time.monotonic()
            if self.cancelled is not None and self.cancelled(): self.reject('FILES_TREE_CANCELED')
            if now >= deadline: self.reject('FILES_TREE_DEADLINE')
            if now - policy_checked >= 1: self.check_policies(record); policy_checked = now
            return False
        check(); self.destination(record, copied, check)
        command = record['plan']['copyPlan']['command']
        manifest = self.manifest_type(self.f, cancelled=self.cancelled).verify(command['source'], record['plan']['copyPlan']['manifestSha256'])
        if len(self.encode(manifest)) > self.MAX_MANIFEST: self.reject('FILES_TREE_MANIFEST_LIMIT')
        self.write_json(folder, record['operationId'] + '.manifest.json', manifest, self.MAX_MANIFEST)
        record['manifestDigest'] = self.digest(manifest); self.save(folder, record)
        with self.f.directory(command['source'], 'mutate') as (_, parent, leaf):
            root = self.f.open_at(parent, leaf, os.O_RDONLY | os.O_DIRECTORY); private = retained = None
            try:
                self.check_source(record, parent, root); self.checkpoint('SourceValidated'); check()
                self.check_source(record, parent, root)
                private = self.f.staging(parent); capacity = os.fstatvfs(private)
                if capacity.f_bavail * capacity.f_frsize < max(64 * 1024**2, manifest['entryCount'] * 16384): self.reject('FILES_CAPACITY_REQUIRED')
                record.update(state='SourcePreparing', sourceMutationStarted=True); self.save(folder, record)
                name = record['operationId'] + '.tree-move'; os.mkdir(name, 0o700, dir_fd=private); os.fsync(private)
                retained = self.f.open_at(private, name, os.O_RDONLY | os.O_DIRECTORY)
                record['retainedIdentity'] = self.copy.parent_identity(retained); self.save(folder, record); self.checkpoint('SourcePrepared')
                self.check_retention_path(parent, private, retained, record)
                directories = {entry['path']: entry for entry in manifest['entries'] if entry['kind'] == 'Directory'}
                for entry in manifest['entries']:
                    check()
                    if not entry['path'] or entry['kind'] != 'Directory': continue
                    path, _, name = entry['path'].rpartition('/')
                    target = self.f.open_at(retained, path or '.', os.O_RDONLY | os.O_DIRECTORY)
                    try: os.mkdir(name, 0o700, dir_fd=target); os.fsync(target)
                    finally: os.close(target)
                for entry in manifest['entries']:
                    if entry['kind'] != 'File': continue
                    check(); self.check_source(record, parent, root)
                    path, _, name = entry['path'].rpartition('/')
                    origin = self.f.open_at(root, path or '.', os.O_RDONLY | os.O_DIRECTORY); target = None
                    try:
                        target = self.f.open_at(retained, path or '.', os.O_RDONLY | os.O_DIRECTORY)
                        if self.f.publication_identity(os.fstat(origin))[:5] != directories[path]['sourceIdentity'][:5]: self.reject('FILES_TREE_CHANGED')
                        if self.f.version(os.stat(name, dir_fd=origin, follow_symlinks=False)) != entry['sourceVersion'] or self.file_hash(origin, name, entry['sourceIdentity'], check) != entry['sha256']: self.reject('FILES_TREE_CHANGED')
                        record.update(state='SourceRetaining', currentPath=entry['path']); self.save(folder, record); self.checkpoint('SourceRetaining')
                        self.check_source(record, parent, root); self.check_ancestors(root, path, directories)
                        self.check_retention_path(parent, private, retained, record)
                        self.f.exchange(origin, name, target, name, 1); os.fsync(origin); os.fsync(target)
                        if self.file_hash(target, name, entry['sourceIdentity'], check) != entry['sha256']: self.reject('FILES_TREE_CHANGED')
                        self.check_source(record, parent, root); self.check_ancestors(root, path, directories)
                        self.check_retention_path(parent, private, retained, record)
                        self.checkpoint('SourceRetained')
                    finally:
                        os.close(origin)
                        if target is not None: os.close(target)
                for entry in reversed(manifest['entries']):
                    if entry['kind'] != 'Directory': continue
                    check(); self.check_source(record, parent, root)
                    path, _, name = entry['path'].rpartition('/')
                    self.check_ancestors(root, path, directories)
                    origin = self.f.open_at(root, path or '.', os.O_RDONLY | os.O_DIRECTORY) if entry['path'] else os.dup(parent)
                    try:
                        if not entry['path']: name = leaf
                        current = os.stat(name, dir_fd=origin, follow_symlinks=False)
                        if self.f.publication_identity(current)[:5] != entry['sourceIdentity'][:5] or not stat.S_ISDIR(current.st_mode): self.reject('FILES_TREE_CHANGED')
                        record.update(state='SourceRemoving', currentPath=entry['path']); self.save(folder, record); self.checkpoint('SourceRemoving')
                        self.check_source(record, parent, root)
                        self.check_retention_path(parent, private, retained, record)
                        if self.f.publication_identity(os.stat(name, dir_fd=origin, follow_symlinks=False))[:5] != entry['sourceIdentity'][:5]: self.reject('FILES_TREE_CHANGED')
                        os.rmdir(name, dir_fd=origin); os.fsync(origin); self.checkpoint('SourceRemoved')
                    finally: os.close(origin)
                self.verify_retained(record, manifest, retained, check)
                record['version'] = self.destination(record, copied, check)
                self.check_retention_path(parent, private, retained, record)
                try: os.stat(leaf, dir_fd=parent, follow_symlinks=False)
                except FileNotFoundError: pass
                else: self.reject('FILES_TREE_CHANGED')
                record.update(state='Completed', sourceRemoved=True, sourceRetained=True); record.pop('currentPath', None)
                self.save(folder, record); self.checkpoint('Completed')
            finally:
                if retained is not None: os.close(retained)
                if private is not None: os.close(private)
                os.close(root)

    def verify_retained(self, record, manifest, retained, check):
        if self.copy.parent_identity(retained) != record.get('retainedIdentity'): self.reject('FILES_TREE_CHANGED')
        command = record['plan']['copyPlan']['command']['source']
        inventory = self.manifest_type(self.f, cancelled=check)._scan_descriptor({**command, 'expectedVersion': self.f.version(os.fstat(retained))}, retained, self.f.roots()[command['rootId']])
        if inventory['entryCount'] != manifest['entryCount'] or inventory['totalBytes'] != manifest['totalBytes']: self.reject('FILES_TREE_CHANGED')
        for actual, original in zip(inventory['entries'], manifest['entries']):
            if (actual['path'], actual['kind']) != (original['path'], original['kind']): self.reject('FILES_TREE_CHANGED')
            if actual['kind'] == 'File':
                if actual['sourceIdentity'] != original['sourceIdentity'] or actual['sha256'] != original['sha256']: self.reject('FILES_TREE_CHANGED')
            elif actual['sourceIdentity'][3:5] != [0, 0] or actual['sourceIdentity'][2] & 0o7777 != 0o700: self.reject('FILES_RECEIPT_UNSAFE')

    def recover(self, folder, record):
        if record['state'] in self.TERMINAL: return
        try:
            try:
                with self.copy.state(record['operationId'], record['owner']) as (copy_folder, copied):
                    self.copy.recover(copy_folder, copied)
                    record['destinationPublished'] = copied['state'] == 'Completed'
                    record['destinationStagingRetained'] = bool(copied.get('mayHaveStaged')) and copied['state'] != 'Completed'
                    if copied['state'] == 'Aborted' and not record.get('sourceMutationStarted'): record['state'] = 'Aborted'
                    else:
                        record['state'] = 'Unknown'
                        if record.get('retainedIdentity') and record.get('manifestDigest'):
                            manifest = self.read_json(folder, record['operationId'] + '.manifest.json', self.MAX_MANIFEST)
                            if self.digest(manifest) != record['manifestDigest']: self.reject('FILES_RECEIPT_UNSAFE')
                            command = record['plan']['copyPlan']['command']
                            with self.f.directory(command['source'], 'mutate') as (_, parent, leaf):
                                if self.copy.parent_identity(parent) != record['plan']['sourceParent']: self.reject('FILES_PARENT_CHANGED')
                                try: os.stat(leaf, dir_fd=parent, follow_symlinks=False)
                                except FileNotFoundError:
                                    private = self.f.open_at(parent, self.f.STAGING, os.O_RDONLY | os.O_DIRECTORY)
                                    try:
                                        info = os.fstat(private)
                                        if info.st_uid != 0 or stat.S_IMODE(info.st_mode) != 0o700: self.reject('FILES_RECEIPT_UNSAFE')
                                        retained = self.f.open_at(private, record['operationId'] + '.tree-move', os.O_RDONLY | os.O_DIRECTORY)
                                        try: self.verify_retained(record, manifest, retained, lambda: False)
                                        finally: os.close(retained)
                                    finally: os.close(private)
                                    record['version'] = self.destination(record, copied, lambda: False)
                                    record.update(state='Completed', sourceRemoved=True, sourceRetained=True)
            except self.f.Rejected as error:
                if str(error) == 'FILES_OPERATION_BUSY': raise
                if str(error) == 'FILES_OPERATION_NOT_FOUND' and record['state'] == 'Preparing': record['state'] = 'Aborted'
                else: raise
        except self.f.Rejected as error:
            if str(error) == 'FILES_OPERATION_BUSY': raise
            record['state'] = 'Unknown'
        except (OSError, KeyError, TypeError, ValueError): record['state'] = 'Unknown'
        self.save(folder, record)

    def lookup(self, request):
        if not isinstance(request, dict) or set(request) != {'operationId', 'owner'}: self.reject('FILES_STATUS_INVALID')
        with self.state(request['operationId'], request['owner']) as (folder, record):
            self.recover(folder, record); return self.public(record)

    def discard(self, request):
        if not isinstance(request, dict) or set(request) != {'operationId', 'owner', 'plan'}: self.reject('FILES_TREE_COMMAND_INVALID')
        self.validate(request['plan'])
        with self.state(request['operationId'], request['owner'], True) as (folder, record):
            if record is None: record = {**request, 'state': 'Aborted'}; self.save(folder, record)
            elif record['plan'] != request['plan']: self.reject('FILES_IDEMPOTENCY_CONFLICT')
            else: self.recover(folder, record)
            return self.public(record)

    def public(self, record):
        copied = record['plan']['copyPlan']
        return {'operationId': record['operationId'], 'state': record['state'], 'fingerprint': record['plan']['fingerprint'],
            'entryCount': copied['entryCount'], 'totalBytes': copied['totalBytes'], 'version': record.get('version', '') if record['state'] == 'Completed' else '',
            'destinationPublished': bool(record.get('destinationPublished')), 'sourceRemoved': bool(record.get('sourceRemoved')),
            'sourceRetained': bool(record.get('sourceRetained')), 'partialRetained': record['state'] != 'Completed' and bool(record.get('sourceMutationStarted') or record.get('destinationPublished') or record.get('destinationStagingRetained'))}
