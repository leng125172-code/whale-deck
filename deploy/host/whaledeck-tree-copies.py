#!/usr/bin/env python3
"""Durable registered-directory copy, invoked only by the fixed file driver.

No directory source removal, overwrite, arbitrary paths, automatic retry of
publication or recursive cleanup. Partial private trees remain explicit evidence
for a later controlled cleanup workflow. Source and target hierarchy locks are
also required in the future BFF/Worker before exposing this engine.
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


class FileTreeCopies:
    DOMAIN = b'whaledeck-tree-copy-v1\n'
    STATE_DIRECTORY = 'tree-copies'
    SOURCE_ACTIONS = frozenset(('files-list', 'files-download', 'files-tree-copy'))
    TARGET_ACTIONS = frozenset(('files-mutate', 'files-upload', 'files-tree-copy'))
    TERMINAL = ('Completed', 'Aborted', 'Unknown')
    COMMAND_FIELDS = {'source', 'target'}
    PLAN_FIELDS = {'command', 'manifestSha256', 'entryCount', 'totalBytes', 'targetParent', 'sourcePolicyHash', 'targetPolicyHash', 'fingerprint'}

    def __init__(self, files, manifest_type, *, cancelled=None):
        self.f, self.manifest_type, self.cancelled = files, manifest_type, cancelled
        if cancelled is not None and not callable(cancelled): self.reject('FILES_TREE_COMMAND_INVALID')

    def reject(self, code): raise self.f.Rejected(code)

    @staticmethod
    def encode(value): return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(',', ':'), allow_nan=False).encode('utf-8')

    def digest(self, value): return hashlib.sha256(self.DOMAIN + self.encode(value)).hexdigest().upper()

    @staticmethod
    def parent_identity(descriptor):
        value = os.fstat(descriptor); return [value.st_dev, value.st_ino]

    def validate(self, command):
        if not isinstance(command, dict) or set(command) != self.COMMAND_FIELDS:
            self.reject('FILES_TREE_COMMAND_INVALID')
        for key, fields in (('source', {'rootId', 'path', 'expectedVersion'}), ('target', {'rootId', 'path'})):
            value = command[key]
            if not isinstance(value, dict) or set(value) != fields or any(type(item) is not str for item in value.values()):
                self.reject('FILES_TREE_COMMAND_INVALID')
            self.f.relative(value['path'])
            if self.f.protected(value['path']): self.reject('FILES_PROTECTED')
        if not re.fullmatch('[A-F0-9]{64}', command['source']['expectedVersion']): self.reject('FILES_VERSION_CONFLICT')

    def target(self, command): return self.f.directory(command['target'], 'mutate')

    def validate_plan(self, plan):
        if not isinstance(plan, dict) or set(plan) != self.PLAN_FIELDS: self.reject('FILES_PLAN_INVALID')
        self.validate(plan['command'])
        if any(type(plan[key]) is not str or not re.fullmatch('[A-F0-9]{64}', plan[key]) for key in ('manifestSha256', 'sourcePolicyHash', 'targetPolicyHash', 'fingerprint')) or \
                type(plan['entryCount']) is not int or not 1 <= plan['entryCount'] <= 10000 or \
                type(plan['totalBytes']) is not int or not 0 <= plan['totalBytes'] <= 16 * 1024**3 or \
                not isinstance(plan['targetParent'], list) or len(plan['targetParent']) != 2 or \
                any(type(value) is not int or value < 0 for value in plan['targetParent']) or plan['targetParent'][1] == 0:
            self.reject('FILES_PLAN_INVALID')
        if not hmac.compare_digest(plan['fingerprint'], self.digest({key: value for key, value in plan.items() if key != 'fingerprint'})):
            self.reject('FILES_PLAN_INVALID')

    def policies(self, command):
        roots = self.f.roots(); source = roots.get(command['source']['rootId']); target = roots.get(command['target']['rootId'])
        if source is None or target is None or not self.SOURCE_ACTIONS <= set(source['allowedActions']) or not self.TARGET_ACTIONS <= set(target['allowedActions']):
            self.reject('FILES_ROOT_FORBIDDEN')
        # Registered roots can overlap. Check their absolute normalized relation,
        # not just root IDs, and still use openat2 for every actual filesystem I/O.
        source_path = source['path'].rstrip('/') + '/' + command['source']['path']
        target_path = target['path'].rstrip('/') + '/' + command['target']['path']
        if source_path == target_path or source_path.startswith(target_path + '/') or target_path.startswith(source_path + '/'):
            self.reject('FILES_TREE_OVERLAP')
        return source, target

    def plan(self, command):
        self.validate(command); source, target = self.policies(command)
        inventory = self.manifest_type(self.f, cancelled=self.cancelled).scan(command['source'])
        with self.target(command) as (_, parent, leaf):
            try: os.stat(leaf, dir_fd=parent, follow_symlinks=False)
            except FileNotFoundError: pass
            else: self.reject('FILES_VERSION_CONFLICT')
            parent_identity = self.parent_identity(parent)
            if any(row['kind'] == 'Directory' and row['sourceIdentity'][:2] == parent_identity for row in inventory['entries']):
                self.reject('FILES_TREE_OVERLAP')
        plan = {'command': command, 'manifestSha256': inventory['manifestSha256'], 'entryCount': inventory['entryCount'],
                'totalBytes': inventory['totalBytes'], 'targetParent': parent_identity,
                'sourcePolicyHash': self.digest(source), 'targetPolicyHash': self.digest(target)}
        return {**plan, 'fingerprint': self.digest(plan)}

    @contextlib.contextmanager
    def state(self, operation, owner, writing=False):
        self.f.identity(operation)
        if type(owner) is not str or not re.fullmatch('[A-F0-9]{64}', owner): self.reject('FILES_OPERATION_INVALID')
        base = self.f.trusted_directory(self.f.STATE); state = lock = None
        try:
            try: os.mkdir(self.STATE_DIRECTORY, 0o700, dir_fd=base); os.fsync(base)
            except FileExistsError: pass
            state = self.f.open_at(base, self.STATE_DIRECTORY, os.O_RDONLY | os.O_DIRECTORY)
            info = os.fstat(state)
            if info.st_uid != 0 or stat.S_IMODE(info.st_mode) != 0o700: self.reject('FILES_RECEIPT_UNSAFE')
            record = self.read(state, operation)
            if record is not None and record.get('owner') != owner: self.reject('FILES_OPERATION_NOT_FOUND')
            if record is None and not writing: self.reject('FILES_OPERATION_NOT_FOUND')
            if record is None:
                with os.scandir(state) as entries:
                    for count, _ in enumerate(entries, 1):
                        if count >= 20000: self.reject('FILES_RECEIPT_LIMIT')
            lock = os.open(operation + '.lock', os.O_WRONLY | os.O_CREAT | os.O_NOFOLLOW, 0o600, dir_fd=state)
            info = os.fstat(lock)
            if info.st_uid != 0 or not stat.S_ISREG(info.st_mode) or info.st_nlink != 1 or stat.S_IMODE(info.st_mode) != 0o600:
                self.reject('FILES_RECEIPT_UNSAFE')
            try: fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
            except BlockingIOError: self.reject('FILES_OPERATION_BUSY')
            with self.f.namespace_lock():
                record = self.read(state, operation)
                if record is not None and record.get('owner') != owner: self.reject('FILES_OPERATION_NOT_FOUND')
                yield state, record
        finally:
            if lock is not None: os.close(lock)
            if state is not None: os.close(state)
            os.close(base)

    def read(self, state, operation):
        try: descriptor = self.f.open_at(state, operation + '.json', os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW)
        except FileNotFoundError: return None
        try:
            info = os.fstat(descriptor)
            if info.st_uid != 0 or stat.S_IMODE(info.st_mode) != 0o600 or not stat.S_ISREG(info.st_mode) or info.st_nlink != 1 or info.st_size > 32768:
                self.reject('FILES_RECEIPT_UNSAFE')
            value = json.loads(os.read(descriptor, 32769), object_pairs_hook=self.f.unique_object)
            if not isinstance(value, dict) or value.get('operationId') != operation or value.get('state') not in self.TERMINAL + ('Preparing', 'Staging', 'Ready', 'Publishing'):
                self.reject('FILES_RECEIPT_UNSAFE')
            try:
                self.validate_plan(value.get('plan'))
                if type(value.get('owner')) is not str or not re.fullmatch('[A-F0-9]{64}', value['owner']): self.reject('FILES_RECEIPT_UNSAFE')
                if value['state'] == 'Completed' and any(type(value.get(key)) is not str or not re.fullmatch('[A-F0-9]{64}', value[key]) for key in ('version', 'treeSha256')):
                    self.reject('FILES_RECEIPT_UNSAFE')
                if 'newIdentity' in value and (not isinstance(value['newIdentity'], list) or len(value['newIdentity']) != 2 or
                        any(type(item) is not int or item < 0 for item in value['newIdentity']) or value['newIdentity'][1] == 0):
                    self.reject('FILES_RECEIPT_UNSAFE')
                if value['state'] in ('Ready', 'Publishing', 'Completed') and ('newIdentity' not in value or
                        type(value.get('treeSha256')) is not str or not re.fullmatch('[A-F0-9]{64}', value['treeSha256'])):
                    self.reject('FILES_RECEIPT_UNSAFE')
                if 'mayHaveStaged' in value and type(value['mayHaveStaged']) is not bool: self.reject('FILES_RECEIPT_UNSAFE')
            except self.f.Rejected: self.reject('FILES_RECEIPT_UNSAFE')
            return value
        finally: os.close(descriptor)

    def save(self, state, record):
        if len(self.encode(record)) > 32768: self.reject('FILES_RECEIPT_LIMIT')
        self.f.save_receipt(state, record['operationId'], record)

    def destination_owner(self, row, policy):
        # Copies belong to the registered destination identity. Move's trusted
        # subclass overrides this with the already verified source metadata.
        return policy.get('ownerUid', 0), policy.get('ownerGid', 0)

    def snapshot(self, folder, command, policy, check, expected=None):
        inventory = self.manifest_type(self.f, cancelled=check)._scan_descriptor(
            {**command['target'], 'expectedVersion': self.f.version(os.fstat(folder))}, folder, policy)
        if expected is not None:
            if inventory['entryCount'] != expected['entryCount'] or inventory['totalBytes'] != expected['totalBytes']:
                self.reject('FILES_TREE_CHANGED')
            for actual, original in zip(inventory['entries'], expected['entries']):
                metadata = actual['sourceIdentity']; before = original['sourceIdentity']
                if (actual['path'], actual['kind'], actual['sizeBytes'], actual['sha256']) != (original['path'], original['kind'], original['sizeBytes'], original['sha256']) or \
                        metadata[2] & 0o7777 != before[2] & 0o777 or metadata[3:5] != list(self.destination_owner(original, policy)) or metadata[7] != before[7]:
                    self.reject('FILES_TREE_CHANGED')
        # rename changes ctime only. Everything else, including inode, mode,
        # owner, mtime, full byte hashes and complete membership remains bound.
        return self.digest([{'path': row['path'], 'kind': row['kind'], 'identity': row['sourceIdentity'],
                             'sizeBytes': row['sizeBytes'], 'sha256': row['sha256']} for row in inventory['entries']])

    def checkpoint(self, phase):
        """Trusted test instrumentation can kill the process at durable fences."""

    def copy(self, request):
        if not isinstance(request, dict) or set(request) != {'operationId', 'owner', 'plan'} or not isinstance(request['plan'], dict):
            self.reject('FILES_TREE_COMMAND_INVALID')
        plan = request['plan']; self.validate_plan(plan)
        with self.state(request['operationId'], request['owner'], True) as (state, record):
            if record is not None:
                if record['plan'] != plan: self.reject('FILES_IDEMPOTENCY_CONFLICT')
                self.recover(state, record); return self.public(record)
            if self.plan(plan['command']) != plan: self.reject('FILES_VERSION_CONFLICT')
            record = {'operationId': request['operationId'], 'owner': request['owner'], 'plan': plan, 'state': 'Preparing'}
            self.save(state, record)
            try: self.publish(state, record)
            except BaseException:
                with contextlib.suppress(OSError, self.f.Rejected): self.recover(state, record)
                raise
            return self.public(record)

    def discard(self, request):
        if not isinstance(request, dict) or set(request) != {'operationId', 'owner', 'plan'}: self.reject('FILES_TREE_COMMAND_INVALID')
        self.validate_plan(request['plan'])
        with self.state(request['operationId'], request['owner'], True) as (state, record):
            if record is None:
                # Original sealed plan only; no re-plan, so cancellation remains
                # possible after source change or root revocation.
                record = {**request, 'state': 'Aborted'}; self.save(state, record)
            else:
                if record['plan'] != request['plan']: self.reject('FILES_IDEMPOTENCY_CONFLICT')
                self.recover(state, record)
            return self.public(record)

    def publish(self, state, record):
        plan = record['plan']; command = plan['command']; operation = record['operationId']
        deadline = time.monotonic() + 600
        def check():
            if self.cancelled is not None and self.cancelled(): self.reject('FILES_TREE_CANCELED')
            if time.monotonic() >= deadline: self.reject('FILES_TREE_DEADLINE')
            return False
        manifest = self.manifest_type(self.f, cancelled=check).verify(command['source'], plan['manifestSha256'])
        _, policy = self.policies(command)
        with self.target(command) as (_, parent, leaf):
            if self.parent_identity(parent) != plan['targetParent']: self.reject('FILES_PARENT_CHANGED')
            capacity = os.fstatvfs(parent)
            reserve = max(512 * 1024**2, capacity.f_blocks * capacity.f_frsize // 20) + manifest['entryCount'] * 16384
            if capacity.f_bavail * capacity.f_frsize < plan['totalBytes'] + reserve: self.reject('FILES_CAPACITY_REQUIRED')
            private = self.f.staging(parent); folder = None; temporary = operation + '.tree-copy'
            try:
                # Save intent BEFORE creation. If killed between mkdir and the
                # identity receipt, recovery preserves the orphan as Unknown.
                record.update(state='Staging', mayHaveStaged=True); self.save(state, record)
                os.mkdir(temporary, 0o700, dir_fd=private); os.fsync(private)
                folder = self.f.open_at(private, temporary, os.O_RDONLY | os.O_DIRECTORY)
                record['newIdentity'] = self.parent_identity(folder); self.save(state, record)
                self.checkpoint('Staging')
                with self.f.directory(command['source'], 'download') as (_, source_parent, source_leaf):
                    source = self.f.open_at(source_parent, source_leaf, os.O_RDONLY | os.O_DIRECTORY)
                    try: self.populate(source, folder, manifest, policy, check)
                    finally: os.close(source)
                # Re-read the complete source, not only the top directory mtime.
                self.manifest_type(self.f, cancelled=check).verify(command['source'], plan['manifestSha256'])
                record['treeSha256'] = self.snapshot(folder, command, policy, check, manifest)
                record['state'] = 'Ready'; self.save(state, record); self.checkpoint('Ready'); check()
                source_policy, target_policy = self.policies(command)
                if self.digest(source_policy) != plan['sourcePolicyHash'] or self.digest(target_policy) != plan['targetPolicyHash']:
                    self.reject('FILES_ROOT_CHANGED')
                with self.target(command) as (_, bound_parent, _):
                    if self.parent_identity(bound_parent) != plan['targetParent']: self.reject('FILES_PARENT_CHANGED')
                record['state'] = 'Publishing'; self.save(state, record); self.checkpoint('Publishing'); check()
                if self.snapshot(folder, command, policy, check, manifest) != record['treeSha256']:
                    self.reject('FILES_TREE_CHANGED')
                self.f.exchange(private, temporary, parent, leaf, 1)
                os.fsync(parent); os.fsync(private); self.checkpoint('Published')
                self.recover(state, record)
            finally:
                if folder is not None: os.close(folder)
                os.close(private)

    def populate(self, source, target, manifest, policy, check):
        for row in manifest['entries']:
            check(); path = row['path']
            if not path: continue
            parent_path, _, leaf = path.rpartition('/')
            parent = self.f.open_at(target, parent_path or '.', os.O_RDONLY | os.O_DIRECTORY)
            try:
                if row['kind'] == 'Directory': os.mkdir(leaf, 0o700, dir_fd=parent)
                else:
                    original = self.f.open_at(source, path, os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW)
                    destination = None
                    try:
                        before = os.fstat(original)
                        if not stat.S_ISREG(before.st_mode) or before.st_nlink != 1 or self.f.version(before) != row['sourceVersion']:
                            self.reject('FILES_TREE_CHANGED')
                        destination = os.open(leaf, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600, dir_fd=parent)
                        digest = hashlib.sha256(); size = 0
                        while size < row['sizeBytes']:
                            check(); data = os.read(original, min(self.f.CHUNK, row['sizeBytes'] - size))
                            if not data: self.reject('FILES_READ_CHANGED')
                            digest.update(data); size += len(data); remaining = memoryview(data)
                            while remaining:
                                written = os.write(destination, remaining)
                                if written <= 0: self.reject('FILES_WRITE_FAILED')
                                remaining = remaining[written:]
                        if os.read(original, 1) or self.f.version(os.fstat(original)) != row['sourceVersion'] or digest.hexdigest().upper() != row['sha256']:
                            self.reject('FILES_READ_CHANGED')
                        os.fchown(destination, *self.destination_owner(row, policy)); os.fchmod(destination, row['sourceIdentity'][2] & 0o777)
                        os.utime(destination, ns=(before.st_atime_ns, before.st_mtime_ns)); os.fsync(destination)
                    finally:
                        if destination is not None: os.close(destination)
                        os.close(original)
                os.fsync(parent)
            finally: os.close(parent)
        # Finish directories bottom-up, after entries no longer change mtime.
        for row in reversed(manifest['entries']):
            check()
            if row['kind'] != 'Directory': continue
            directory = self.f.open_at(target, row['path'] or '.', os.O_RDONLY | os.O_DIRECTORY)
            try:
                os.fchown(directory, *self.destination_owner(row, policy)); os.fchmod(directory, row['sourceIdentity'][2] & 0o777)
                os.utime(directory, ns=(row['sourceIdentity'][7], row['sourceIdentity'][7])); os.fsync(directory)
            finally: os.close(directory)

    def recover(self, state, record):
        if record['state'] in self.TERMINAL: return
        command = record['plan']['command']; previous = record['state']; record['state'] = 'Unknown'
        try:
            _, policy = self.policies(command)
            with self.target(command) as (_, parent, leaf):
                if self.parent_identity(parent) != record['plan']['targetParent']: self.reject('FILES_PARENT_CHANGED')
                try: descriptor = self.f.open_at(parent, leaf, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
                except FileNotFoundError: descriptor = None
                if descriptor is not None:
                    try:
                        if self.parent_identity(descriptor) == record.get('newIdentity') and record.get('treeSha256') == self.snapshot(descriptor, command, policy, lambda: False):
                            record.update(state='Completed', version=self.f.version(os.fstat(descriptor)))
                    finally: os.close(descriptor)
                else:
                    # Absence of the target alone is not proof of non-publication.
                    # Only a receipt-owned still-private tree proves Aborted.
                    private = self.f.staging(parent)
                    try:
                        try: pending = self.f.open_at(private, record['operationId'] + '.tree-copy', os.O_RDONLY | os.O_DIRECTORY)
                        except FileNotFoundError: pending = None
                        if pending is not None:
                            try:
                                if self.parent_identity(pending) == record.get('newIdentity'): record['state'] = 'Aborted'
                            finally: os.close(pending)
                        elif previous == 'Preparing' and not record.get('mayHaveStaged') and not record.get('newIdentity'):
                            record['state'] = 'Aborted'
                    finally: os.close(private)
        except (OSError, self.f.Rejected): pass
        self.save(state, record)

    def lookup(self, request):
        if not isinstance(request, dict) or set(request) != {'operationId', 'owner'}: self.reject('FILES_STATUS_INVALID')
        with self.state(request['operationId'], request['owner']) as (state, record):
            self.recover(state, record); return self.public(record)

    def public(self, record):
        return {'operationId': record['operationId'], 'state': record['state'], 'fingerprint': record['plan']['fingerprint'],
                'entryCount': record['plan']['entryCount'], 'totalBytes': record['plan']['totalBytes'],
                'version': record.get('version', '') if record['state'] == 'Completed' else '',
                'partialRetained': record['state'] in ('Aborted', 'Unknown') and bool(record.get('mayHaveStaged'))}
