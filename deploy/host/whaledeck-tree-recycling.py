#!/usr/bin/env python3
"""Internal HDD-backed directory recycle, no public CLI/RPC or recursive delete.

Verify a complete private HDD capture before touching the live tree. Retain
only individually verified original files, then remove verified empty dirs.
Recovery observes; it never captures again, resumes retention or deletes a
partial. A future BFF must add current authorization, sealed confirmation,
permanent jobs, hierarchy locks and audit before exposing this implementation.
"""
import contextlib
import copy
import fcntl
import hashlib
import hmac
import json
import os
import re
import stat
import time


class FileTreeRecycling:
    DOMAIN = b'whaledeck-tree-recycling-v1\n'
    FOLDER = 'tree-recycling'
    SUFFIX = '.tree-trash'
    MAX_MANIFEST = 16 * 1024**2
    TERMINAL = ('Completed', 'Aborted', 'Unknown')
    PHASES = ('Preparing', 'Capturing', 'CaptureVerified', 'SourcePreparing', 'SourceRetaining', 'SourceRemoving')
    ACTIONS = frozenset(('files-list', 'files-download', 'files-mutate', 'files-history', 'files-archive', 'files-tree-trash'))
    PLAN_FIELDS = {'source', 'sourceParent', 'sourcePolicyHash', 'manifestSha256', 'entryCount', 'totalBytes', 'fingerprint'}

    def __init__(self, files, captures, manifest_type, codec_type, io_type, *, cancelled=None):
        self.f, self.captures, self.manifest_type, self.codec_type, self.io = files, captures, manifest_type, codec_type, io_type
        if cancelled is not None and not callable(cancelled): self.reject('FILES_TREE_RECYCLE_INVALID')
        self.cancelled = cancelled

    def reject(self, code): raise self.f.Rejected(code)

    @staticmethod
    def encode(value): return json.dumps(value, sort_keys=True, ensure_ascii=False, separators=(',', ':'), allow_nan=False).encode()

    def digest(self, value): return hashlib.sha256(self.DOMAIN + self.encode(value)).hexdigest().upper()

    @staticmethod
    def identity(descriptor):
        info = os.fstat(descriptor)
        return [info.st_dev, info.st_ino]

    def source(self, value):
        if not isinstance(value, dict) or set(value) != {'rootId', 'path', 'expectedVersion'} or any(type(item) is not str for item in value.values()) or not re.fullmatch('[a-z0-9.-]{1,64}', value['rootId']) or not re.fullmatch('[A-F0-9]{64}', value['expectedVersion']):
            self.reject('FILES_TREE_RECYCLE_INVALID')
        self.f.relative(value['path'])
        if self.f.protected(value['path']): self.reject('FILES_PROTECTED')

    def policy(self, source):
        value = self.f.roots().get(source['rootId'])
        if value is None or not self.ACTIONS <= set(value['allowedActions']): self.reject('FILES_ROOT_FORBIDDEN')
        return value

    def directory(self, source):
        return self.f.directory(source, ('list', 'download', 'mutate', 'history', 'archive', 'tree-trash'))

    def bound(self, plan):
        policy = self.policy(plan['source'])
        if self.digest(policy) != plan['sourcePolicyHash']: self.reject('FILES_ROOT_CHANGED')
        return policy

    def guard(self, plan, cancellation=True):
        deadline = time.monotonic() + 600; checked = -float('inf')
        def check():
            nonlocal checked
            now = time.monotonic()
            if cancellation and self.cancelled is not None and self.cancelled(): self.reject('FILES_TREE_CANCELED')
            if now >= deadline: self.reject('FILES_TREE_DEADLINE')
            if now - checked >= 1: self.bound(plan); checked = now
            return False
        check(); return check

    def plan(self, source):
        self.source(source); policy = self.policy(source)
        manifest = self.manifest_type(self.f, cancelled=self.cancelled).scan(source)
        with self.directory(source) as (_, parent, leaf):
            if self.f.version(os.stat(leaf, dir_fd=parent, follow_symlinks=False)) != source['expectedVersion']: self.reject('FILES_TREE_CHANGED')
            parent_identity = self.identity(parent)
        value = {'source': source, 'sourceParent': parent_identity, 'sourcePolicyHash': self.digest(policy),
            'manifestSha256': manifest['manifestSha256'], 'entryCount': manifest['entryCount'], 'totalBytes': manifest['totalBytes']}
        self.bound(value)
        return {**value, 'fingerprint': self.digest(value)}

    def validate(self, plan):
        if not isinstance(plan, dict) or set(plan) != self.PLAN_FIELDS: self.reject('FILES_PLAN_INVALID')
        self.source(plan['source'])
        for key in ('sourcePolicyHash', 'manifestSha256', 'fingerprint'):
            if type(plan[key]) is not str or not re.fullmatch('[A-F0-9]{64}', plan[key]): self.reject('FILES_PLAN_INVALID')
        parent = plan['sourceParent']
        if not isinstance(parent, list) or len(parent) != 2 or any(type(item) is not int or not 0 <= item < 2**64 for item in parent) or not parent[1]: self.reject('FILES_PLAN_INVALID')
        if type(plan['entryCount']) is not int or not 1 <= plan['entryCount'] <= 10000 or type(plan['totalBytes']) is not int or not 0 <= plan['totalBytes'] <= 16 * 1024**3: self.reject('FILES_PLAN_INVALID')
        if not hmac.compare_digest(plan['fingerprint'], self.digest({key: item for key, item in plan.items() if key != 'fingerprint'})): self.reject('FILES_PLAN_INVALID')

    def read_json(self, folder, name, limit): return self.io.read_json(self, folder, name, limit)
    def write_json(self, folder, name, value, limit): return self.io.write_json(self, folder, name, value, limit)
    def save(self, folder, record): self.write_json(folder, record['operationId'] + '.json', record, 32768)

    def read(self, folder, operation):
        try: value = self.read_json(folder, operation + '.json', 32768)
        except FileNotFoundError: return None
        fields = {'operationId', 'owner', 'plan', 'state', 'captureStarted', 'captureVerified', 'sourceMutationStarted', 'sourceRemoved', 'sourceRetained',
            'archiveBytes', 'archiveSha256', 'retainedIdentity', 'manifestDigest', 'currentPath'}
        try:
            if not isinstance(value, dict) or set(value) - fields or value['operationId'] != operation or value['state'] not in self.TERMINAL + self.PHASES or not re.fullmatch('[A-F0-9]{64}', value['owner']): self.reject('FILES_RECEIPT_UNSAFE')
            self.validate(value['plan'])
            for field in ('captureStarted', 'captureVerified', 'sourceMutationStarted', 'sourceRemoved', 'sourceRetained'):
                if field in value and type(value[field]) is not bool: self.reject('FILES_RECEIPT_UNSAFE')
            for field in ('archiveSha256', 'manifestDigest'):
                if field in value and (type(value[field]) is not str or not re.fullmatch('[A-F0-9]{64}', value[field])): self.reject('FILES_RECEIPT_UNSAFE')
            if 'archiveBytes' in value and (type(value['archiveBytes']) is not int or not 1 <= value['archiveBytes'] <= 17 * 1024**3): self.reject('FILES_RECEIPT_UNSAFE')
            if value.get('captureVerified') and not all(key in value for key in ('archiveBytes', 'archiveSha256')): self.reject('FILES_RECEIPT_UNSAFE')
            if 'retainedIdentity' in value:
                identity = value['retainedIdentity']
                if not isinstance(identity, list) or len(identity) != 2 or any(type(item) is not int or not 0 <= item < 2**64 for item in identity) or not identity[1]: self.reject('FILES_RECEIPT_UNSAFE')
            if 'currentPath' in value: self.f.relative(value['currentPath'], empty=True)
            if value['state'] == 'Completed' and not all(value.get(field) for field in ('captureVerified', 'sourceRemoved', 'sourceRetained', 'retainedIdentity', 'manifestDigest')): self.reject('FILES_RECEIPT_UNSAFE')
            if value.get('sourceMutationStarted') and not value.get('captureVerified'): self.reject('FILES_RECEIPT_UNSAFE')
            if value.get('captureVerified') and not value.get('captureStarted'): self.reject('FILES_RECEIPT_UNSAFE')
            if value['state'] in ('SourcePreparing', 'SourceRetaining', 'SourceRemoving', 'Completed', 'Unknown') and not value.get('sourceMutationStarted'): self.reject('FILES_RECEIPT_UNSAFE')
            if value['state'] == 'Aborted' and value.get('sourceMutationStarted'): self.reject('FILES_RECEIPT_UNSAFE')
            if 'retainedIdentity' in value and (not value.get('sourceMutationStarted') or not value.get('manifestDigest')): self.reject('FILES_RECEIPT_UNSAFE')
        except (ValueError, KeyError, TypeError): self.reject('FILES_RECEIPT_UNSAFE')
        return value

    @contextlib.contextmanager
    def state(self, operation, owner, create=False):
        self.f.identity(operation)
        if type(owner) is not str or not re.fullmatch('[A-F0-9]{64}', owner): self.reject('FILES_OPERATION_INVALID')
        base = self.f.trusted_directory(self.f.STATE); folder = lock = None
        try:
            try: os.mkdir(self.FOLDER, 0o700, dir_fd=base); os.fsync(base)
            except FileExistsError: pass
            folder = self.f.open_at(base, self.FOLDER, os.O_RDONLY | os.O_DIRECTORY)
            info = os.fstat(folder)
            if info.st_uid != 0 or stat.S_IMODE(info.st_mode) != 0o700: self.reject('FILES_RECEIPT_UNSAFE')
            saved = self.read(folder, operation)
            if saved is not None and saved['owner'] != owner or saved is None and not create: self.reject('FILES_OPERATION_NOT_FOUND')
            if saved is None:
                with os.scandir(folder) as rows:
                    for count, _ in enumerate(rows, 1):
                        if count >= 19999: self.reject('FILES_RECEIPT_LIMIT')
            lock = os.open(operation + '.lock', os.O_WRONLY | os.O_CREAT | os.O_NOFOLLOW, 0o600, dir_fd=folder)
            info = os.fstat(lock)
            if not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or stat.S_IMODE(info.st_mode) != 0o600 or info.st_nlink != 1: self.reject('FILES_RECEIPT_UNSAFE')
            try: fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
            except BlockingIOError: self.reject('FILES_OPERATION_BUSY')
            with self.f.namespace_lock():
                saved = self.read(folder, operation)
                if saved is not None and saved['owner'] != owner: self.reject('FILES_OPERATION_NOT_FOUND')
                yield folder, saved
        finally:
            if lock is not None: os.close(lock)
            if folder is not None: os.close(folder)
            os.close(base)

    def request(self, request):
        if not isinstance(request, dict) or set(request) != {'operationId', 'owner', 'plan'}: self.reject('FILES_TREE_RECYCLE_INVALID')
        self.validate(request['plan'])

    @staticmethod
    def capture_request(record):
        return {'operationId': record['operationId'], 'owner': record['owner'], 'source': record['plan']['source'],
            'manifestSha256': record['plan']['manifestSha256'], 'captureKind': 'Trash'}

    @contextlib.contextmanager
    def verified_capture(self, record, check):
        self.bound(record['plan']); request = self.capture_request(record)
        with self.captures.storage(request, existing=True) as (configuration, folder):
            saved = self.captures.read(folder, request['operationId'], self.captures.seed(request, self.captures.request(request), configuration, folder))
            if saved is None or saved['state'] != 'Verified' or any(saved.get(key) != record.get(key) for key in ('archiveBytes', 'archiveSha256')): self.reject('FILES_TREE_BACKUP_CONTENT_INVALID')
            codec = self.codec_type(self.f, self.manifest_type, cancelled=check)
            self.captures.verify(folder, saved, codec); self.captures.b.attest(configuration, folder); check()
            manifest = self.captures.manifest(folder, saved)
            if manifest['manifestSha256'] != record['plan']['manifestSha256'] or (manifest['entryCount'], manifest['totalBytes']) != (record['plan']['entryCount'], record['plan']['totalBytes']): self.reject('FILES_TREE_BACKUP_CONTENT_INVALID')
            yield manifest
            self.captures.verify(folder, saved, codec); self.captures.b.attest(configuration, folder); self.bound(record['plan']); check()

    def checkpoint(self, phase):
        """Trusted isolated-test crash/race instrumentation; not request data."""

    def recycle(self, request):
        self.request(request)
        with self.state(request['operationId'], request['owner'], True) as (folder, record):
            if record is not None:
                if record['plan'] != request['plan']: self.reject('FILES_IDEMPOTENCY_CONFLICT')
                self.recover(folder, record); return self.public(record)
            if self.plan(request['plan']['source']) != request['plan']: self.reject('FILES_TREE_CHANGED')
            check = self.guard(request['plan']); record = {**request, 'state': 'Preparing'}
            self.save(folder, record); self.checkpoint('Preparing')
            try:
                record.update(state='Capturing', captureStarted=True); self.save(folder, record); self.checkpoint('Capturing')
                writer = copy.copy(self.captures); writer.cancelled = check
                captured = writer.ensure(self.capture_request(record))
                if captured['state'] != 'Verified': self.reject('FILES_TREE_BACKUP_INCOMPLETE')
                record.update(state='CaptureVerified', captureVerified=True, archiveBytes=captured['archiveBytes'], archiveSha256=captured['archiveSha256'])
                self.save(folder, record); self.checkpoint('CaptureVerified'); check()
                with self.verified_capture(record, check) as manifest:
                    self.manifest_type(self.f, cancelled=check).verify(record['plan']['source'], record['plan']['manifestSha256'])
                    self.write_json(folder, record['operationId'] + '.manifest.json', manifest, self.MAX_MANIFEST)
                    record['manifestDigest'] = self.digest(manifest); self.save(folder, record)
                    self.retain(folder, record, manifest, check)
                record.update(state='Completed', sourceRemoved=True, sourceRetained=True); record.pop('currentPath', None)
                self.save(folder, record); self.checkpoint('Completed')
            except BaseException:
                with contextlib.suppress(OSError, self.f.Rejected): self.recover(folder, record)
                raise
            return self.public(record)

    def check_parent(self, plan):
        with self.directory(plan['source']) as (_, parent, _):
            if self.identity(parent) != plan['sourceParent']: self.reject('FILES_PARENT_CHANGED')

    def check_source(self, record, parent, root):
        plan = record['plan']; self.check_parent(plan)
        leaf = plan['source']['path'].rsplit('/', 1)[-1]
        info = os.stat(leaf, dir_fd=parent, follow_symlinks=False)
        if self.identity(parent) != plan['sourceParent'] or [info.st_dev, info.st_ino] != self.identity(root) or not stat.S_ISDIR(info.st_mode): self.reject('FILES_TREE_CHANGED')

    def retain(self, folder, record, manifest, check):
        plan = record['plan']
        with self.directory(plan['source']) as (_, parent, leaf):
            root = self.f.open_at(parent, leaf, os.O_RDONLY | os.O_DIRECTORY); private = retained = None
            try:
                self.check_source(record, parent, root); self.checkpoint('SourceValidated'); check()
                self.check_source(record, parent, root)
                private = self.f.staging(parent); capacity = os.fstatvfs(private)
                if capacity.f_bavail * capacity.f_frsize < max(64 * 1024**2, manifest['entryCount'] * 16384): self.reject('FILES_CAPACITY_REQUIRED')
                record.update(state='SourcePreparing', sourceMutationStarted=True); self.save(folder, record)
                os.mkdir(record['operationId'] + self.SUFFIX, 0o700, dir_fd=private); os.fsync(private)
                retained = self.f.open_at(private, record['operationId'] + self.SUFFIX, os.O_RDONLY | os.O_DIRECTORY)
                record['retainedIdentity'] = self.identity(retained); self.save(folder, record); self.checkpoint('SourcePrepared')
                self.io.check_retention_path(self, parent, private, retained, record, self.SUFFIX)
                directories = {row['path']: row for row in manifest['entries'] if row['kind'] == 'Directory'}
                for row in manifest['entries']:
                    if row['kind'] != 'Directory' or not row['path']: continue
                    check(); path, _, name = row['path'].rpartition('/')
                    target = self.f.open_at(retained, path or '.', os.O_RDONLY | os.O_DIRECTORY)
                    try: os.mkdir(name, 0o700, dir_fd=target); os.fsync(target)
                    finally: os.close(target)
                for row in manifest['entries']:
                    if row['kind'] != 'File': continue
                    check(); self.check_source(record, parent, root); path, _, name = row['path'].rpartition('/')
                    origin = self.f.open_at(root, path or '.', os.O_RDONLY | os.O_DIRECTORY); target = None
                    try:
                        target = self.f.open_at(retained, path or '.', os.O_RDONLY | os.O_DIRECTORY)
                        if self.f.publication_identity(os.fstat(origin))[:5] != directories[path]['sourceIdentity'][:5] or self.f.version(os.stat(name, dir_fd=origin, follow_symlinks=False)) != row['sourceVersion'] or self.io.file_hash(self, origin, name, row['sourceIdentity'], check) != row['sha256']: self.reject('FILES_TREE_CHANGED')
                        record.update(state='SourceRetaining', currentPath=row['path']); self.save(folder, record); self.checkpoint('SourceRetaining')
                        self.check_source(record, parent, root); self.io.check_ancestors(self, root, path, directories)
                        self.io.check_retention_path(self, parent, private, retained, record, self.SUFFIX)
                        self.f.exchange(origin, name, target, name, 1); os.fsync(origin); os.fsync(target)
                        if self.io.file_hash(self, target, name, row['sourceIdentity'], check) != row['sha256']: self.reject('FILES_TREE_CHANGED')
                        self.check_source(record, parent, root); self.io.check_ancestors(self, root, path, directories)
                        self.io.check_retention_path(self, parent, private, retained, record, self.SUFFIX); self.checkpoint('SourceRetained')
                    finally:
                        os.close(origin)
                        if target is not None: os.close(target)
                for row in reversed(manifest['entries']):
                    if row['kind'] != 'Directory': continue
                    check(); self.check_source(record, parent, root); path, _, name = row['path'].rpartition('/')
                    self.io.check_ancestors(self, root, path, directories)
                    origin = self.f.open_at(root, path or '.', os.O_RDONLY | os.O_DIRECTORY) if row['path'] else os.dup(parent)
                    try:
                        if not row['path']: name = leaf
                        info = os.stat(name, dir_fd=origin, follow_symlinks=False)
                        if not stat.S_ISDIR(info.st_mode) or self.f.publication_identity(info)[:5] != row['sourceIdentity'][:5]: self.reject('FILES_TREE_CHANGED')
                        record.update(state='SourceRemoving', currentPath=row['path']); self.save(folder, record); self.checkpoint('SourceRemoving')
                        self.check_source(record, parent, root)
                        self.io.check_retention_path(self, parent, private, retained, record, self.SUFFIX)
                        if self.f.publication_identity(os.stat(name, dir_fd=origin, follow_symlinks=False))[:5] != row['sourceIdentity'][:5]: self.reject('FILES_TREE_CHANGED')
                        os.rmdir(name, dir_fd=origin); os.fsync(origin); self.checkpoint('SourceRemoved')
                    finally: os.close(origin)
                self.verify_retained(record, manifest, retained, check); self.check_parent(plan)
                self.io.check_retention_path(self, parent, private, retained, record, self.SUFFIX)
                try: os.stat(leaf, dir_fd=parent, follow_symlinks=False)
                except FileNotFoundError: pass
                else: self.reject('FILES_TREE_CHANGED')
                self.checkpoint('SourceAbsent')
            finally:
                if retained is not None: os.close(retained)
                if private is not None: os.close(private)
                os.close(root)

    def verify_retained(self, record, manifest, retained, check):
        if self.identity(retained) != record.get('retainedIdentity'): self.reject('FILES_TREE_CHANGED')
        source = record['plan']['source']; policy = self.bound(record['plan'])
        actual = self.manifest_type(self.f, cancelled=check)._scan_descriptor({**source, 'expectedVersion': self.f.version(os.fstat(retained))}, retained, policy)
        if (actual['entryCount'], actual['totalBytes']) != (manifest['entryCount'], manifest['totalBytes']): self.reject('FILES_TREE_CHANGED')
        for current, original in zip(actual['entries'], manifest['entries']):
            if (current['path'], current['kind']) != (original['path'], original['kind']): self.reject('FILES_TREE_CHANGED')
            if current['kind'] == 'File':
                if current['sourceIdentity'] != original['sourceIdentity'] or current['sha256'] != original['sha256']: self.reject('FILES_TREE_CHANGED')
            elif current['sourceIdentity'][3:5] != [0, 0] or current['sourceIdentity'][2] & 0o7777 != 0o700: self.reject('FILES_RECEIPT_UNSAFE')

    def recover(self, folder, record):
        if record['state'] in self.TERMINAL: return
        record['state'] = 'Unknown'
        if not record.get('sourceMutationStarted'): record['state'] = 'Aborted'
        else:
            try:
                check = self.guard(record['plan'], cancellation=False)
                manifest = self.read_json(folder, record['operationId'] + '.manifest.json', self.MAX_MANIFEST)
                if self.digest(manifest) != record.get('manifestDigest'): self.reject('FILES_RECEIPT_UNSAFE')
                with self.verified_capture(record, check) as captured:
                    if self.digest(captured) != record['manifestDigest']: self.reject('FILES_RECEIPT_UNSAFE')
                    with self.directory(record['plan']['source']) as (_, parent, leaf):
                        if self.identity(parent) != record['plan']['sourceParent']: self.reject('FILES_PARENT_CHANGED')
                        try: os.stat(leaf, dir_fd=parent, follow_symlinks=False)
                        except FileNotFoundError:
                            private = self.f.open_at(parent, self.f.STAGING, os.O_RDONLY | os.O_DIRECTORY)
                            try:
                                info = os.fstat(private)
                                if info.st_uid != 0 or stat.S_IMODE(info.st_mode) != 0o700: self.reject('FILES_RECEIPT_UNSAFE')
                                retained = self.f.open_at(private, record['operationId'] + self.SUFFIX, os.O_RDONLY | os.O_DIRECTORY)
                                try: self.verify_retained(record, manifest, retained, check)
                                finally: os.close(retained)
                            finally: os.close(private)
                            record.update(state='Completed', sourceRemoved=True, sourceRetained=True); record.pop('currentPath', None)
            except self.f.Rejected as error:
                if str(error) == 'FILES_OPERATION_BUSY': raise
                record['state'] = 'Unknown'
            except (OSError, KeyError, ValueError, TypeError): record['state'] = 'Unknown'
        self.save(folder, record)

    def lookup(self, request):
        if not isinstance(request, dict) or set(request) != {'operationId', 'owner'}: self.reject('FILES_STATUS_INVALID')
        with self.state(request['operationId'], request['owner']) as (folder, record):
            self.recover(folder, record); return self.public(record)

    def discard(self, request):
        self.request(request)
        with self.state(request['operationId'], request['owner'], True) as (folder, record):
            if record is None: record = {**request, 'state': 'Aborted'}; self.save(folder, record)
            else:
                if record['plan'] != request['plan']: self.reject('FILES_IDEMPOTENCY_CONFLICT')
                self.recover(folder, record)
            return self.public(record)

    @staticmethod
    def public(record):
        plan = record['plan']
        return {'operationId': record['operationId'], 'state': record['state'], 'fingerprint': plan['fingerprint'],
            'captureId': record['operationId'], 'manifestSha256': plan['manifestSha256'], 'entryCount': plan['entryCount'], 'totalBytes': plan['totalBytes'],
            'captureVerified': bool(record.get('captureVerified')), 'captureMayBeRetained': bool(record.get('captureStarted')),
            'archiveBytes': record.get('archiveBytes', 0), 'archiveSha256': record.get('archiveSha256', ''),
            'sourceMayHaveChanged': bool(record.get('sourceMutationStarted')), 'sourceRemoved': bool(record.get('sourceRemoved')),
            'originalsRetained': bool(record.get('sourceRetained'))}
