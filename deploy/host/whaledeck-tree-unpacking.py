#!/usr/bin/env python3
"""Internal registered archive -> verified new directory, no public entry point.

Independent grants, plan domain and durable receipts. Never overwrites a target,
removes an input, promotes a partial tree on recovery, or retries extraction.
The BFF/Worker must add original-login confirmation, hierarchy locks and audit
before a fixed Agent method can expose this engine. No arbitrary path/command.
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


class FileTreeUnpacking:
    DOMAIN = b'whaledeck-tree-unpacking-v1\n'
    FOLDER = 'tree-unpacking'
    TEMPORARY_SUFFIX = '.tree-unpack'
    TERMINAL = ('Completed', 'Aborted', 'Unknown')
    TRANSIENT = ('Preparing', 'Staging', 'Ready', 'Publishing')
    PLAN_FIELDS = {'command', 'archiveSha256', 'archiveBytes', 'contentSha256',
        'entryCount', 'totalBytes', 'targetParent', 'sourcePolicyHash', 'targetPolicyHash', 'fingerprint'}
    RECORD_FIELDS = {'operationId', 'owner', 'plan', 'state', 'mayHaveStaged', 'newIdentity', 'treeSha256', 'version'}
    SOURCE_ACTIONS = frozenset(('files-list', 'files-download', 'files-archive', 'files-tree-unpack'))
    TARGET_ACTIONS = frozenset(('files-mutate', 'files-upload', 'files-archive', 'files-tree-unpack'))

    def __init__(self, files, manifest_type, codec_type, *, cancelled=None):
        self.f, self.manifest_type, self.codec_type, self.cancelled = files, manifest_type, codec_type, cancelled
        if cancelled is not None and not callable(cancelled): self.reject('FILES_UNPACK_COMMAND_INVALID')

    def reject(self, code): raise self.f.Rejected(code)

    @staticmethod
    def encode(value): return json.dumps(value, sort_keys=True, ensure_ascii=False, separators=(',', ':'), allow_nan=False).encode()

    def digest(self, value): return hashlib.sha256(self.DOMAIN + self.encode(value)).hexdigest().upper()

    @staticmethod
    def identity(descriptor):
        info = os.fstat(descriptor)
        return [info.st_dev, info.st_ino]

    def guard(self, cancellation=True):
        deadline = time.monotonic() + 600
        def check():
            if cancellation and self.cancelled is not None and self.cancelled(): self.reject('FILES_TREE_CANCELED')
            if time.monotonic() >= deadline: self.reject('FILES_TREE_DEADLINE')
            return False
        check()
        return check

    def validate(self, command):
        if not isinstance(command, dict) or set(command) != {'source', 'target', 'format'} or command['format'] not in ('tar', 'tar.gz'):
            self.reject('FILES_UNPACK_COMMAND_INVALID')
        for key, fields in (('source', {'rootId', 'path', 'expectedVersion'}), ('target', {'rootId', 'path'})):
            value = command[key]
            if not isinstance(value, dict) or set(value) != fields or any(type(item) is not str for item in value.values()) or not re.fullmatch('[a-z0-9.-]{1,64}', value['rootId']):
                self.reject('FILES_UNPACK_COMMAND_INVALID')
            self.f.relative(value['path'])
            if self.f.protected(value['path']): self.reject('FILES_PROTECTED')
        if not re.fullmatch('[A-F0-9]{64}', command['source']['expectedVersion']): self.reject('FILES_VERSION_CONFLICT')

    def policies(self, command):
        roots = self.f.roots()
        source, target = roots.get(command['source']['rootId']), roots.get(command['target']['rootId'])
        if source is None or target is None or not self.SOURCE_ACTIONS <= set(source['allowedActions']) or not self.TARGET_ACTIONS <= set(target['allowedActions']):
            self.reject('FILES_ROOT_FORBIDDEN')
        origin = source['path'].rstrip('/') + '/' + command['source']['path']
        destination = target['path'].rstrip('/') + '/' + command['target']['path']
        if origin == destination or origin.startswith(destination + '/') or destination.startswith(origin + '/'):
            self.reject('FILES_TREE_OVERLAP')
        return source, target

    def target(self, command): return self.f.directory(command['target'], ('mutate', 'upload', 'archive', 'tree-unpack'))

    def bound(self, plan):
        source, target = self.policies(plan['command'])
        if self.digest(source) != plan['sourcePolicyHash'] or self.digest(target) != plan['targetPolicyHash']:
            self.reject('FILES_ROOT_CHANGED')
        return target

    @contextlib.contextmanager
    def archive(self, command, codec):
        with self.f.directory(command['source'], ('list', 'download', 'archive', 'tree-unpack')) as (_, parent, leaf):
            descriptor = self.f.open_at(parent, leaf, os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW)
            try:
                info = codec.regular(descriptor)
                if self.f.version(info) != command['source']['expectedVersion']: self.reject('FILES_VERSION_CONFLICT')
                yield descriptor
                if self.f.version(os.fstat(descriptor)) != command['source']['expectedVersion'] or self.f.version(os.stat(leaf, dir_fd=parent, follow_symlinks=False)) != command['source']['expectedVersion']:
                    self.reject('FILES_ARCHIVE_CHANGED')
            finally: os.close(descriptor)

    def inspect_source(self, plan, codec, check):
        self.bound(plan)
        with self.archive(plan['command'], codec) as descriptor:
            result = codec.inspect(descriptor, plan['command']['format'], check=check)
        if any(result[key] != plan[key] for key in ('archiveBytes', 'archiveSha256', 'contentSha256', 'entryCount', 'totalBytes')):
            self.reject('FILES_ARCHIVE_CHANGED')
        return result

    def plan(self, command, *, check=None):
        check = check or self.guard()
        self.validate(command)
        source, target = self.policies(command)
        codec = self.codec_type(self.f, self.manifest_type, cancelled=check)
        with self.archive(command, codec) as descriptor:
            inventory = codec.inspect(descriptor, command['format'], check=check)
        with self.target(command) as (_, parent, leaf):
            target_proof = self.target_proof(command, parent, leaf, check)
            parent_identity = self.identity(parent)
        # Full entries stay in the trusted process, not a private JSON RPC frame.
        value = {key: inventory[key] for key in ('archiveSha256', 'archiveBytes', 'contentSha256', 'entryCount', 'totalBytes')}
        value.update(command=command, targetParent=parent_identity, sourcePolicyHash=self.digest(source), targetPolicyHash=self.digest(target))
        value.update(target_proof)
        check()
        return {**value, 'fingerprint': self.digest(value)}

    def target_proof(self, command, parent, leaf, check):
        try: os.stat(leaf, dir_fd=parent, follow_symlinks=False)
        except FileNotFoundError: return {}
        self.reject('FILES_VERSION_CONFLICT')

    def validate_plan(self, plan):
        if not isinstance(plan, dict) or set(plan) != self.PLAN_FIELDS: self.reject('FILES_PLAN_INVALID')
        self.validate(plan['command'])
        for key in ('archiveSha256', 'contentSha256', 'sourcePolicyHash', 'targetPolicyHash', 'fingerprint'):
            if type(plan[key]) is not str or not re.fullmatch('[A-F0-9]{64}', plan[key]): self.reject('FILES_PLAN_INVALID')
        if type(plan['archiveBytes']) is not int or not 1 <= plan['archiveBytes'] <= 17 * 1024**3 or type(plan['totalBytes']) is not int or not 0 <= plan['totalBytes'] <= 16 * 1024**3 or type(plan['entryCount']) is not int or not 1 <= plan['entryCount'] <= 10000:
            self.reject('FILES_PLAN_INVALID')
        parent = plan['targetParent']
        if not isinstance(parent, list) or len(parent) != 2 or any(type(item) is not int or not 0 <= item < 2**64 for item in parent) or not parent[1]:
            self.reject('FILES_PLAN_INVALID')
        if not hmac.compare_digest(plan['fingerprint'], self.digest({key: item for key, item in plan.items() if key != 'fingerprint'})):
            self.reject('FILES_PLAN_INVALID')

    def read(self, folder, operation):
        try: descriptor = self.f.open_at(folder, operation + '.json', os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW)
        except FileNotFoundError: return None
        try:
            info = os.fstat(descriptor)
            if not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or stat.S_IMODE(info.st_mode) != 0o600 or info.st_nlink != 1 or info.st_size > 32768:
                self.reject('FILES_RECEIPT_UNSAFE')
            value = json.loads(os.read(descriptor, 32769), object_pairs_hook=self.f.unique_object)
            if not isinstance(value, dict) or set(value) - self.RECORD_FIELDS or value.get('operationId') != operation or value.get('state') not in self.TERMINAL + self.TRANSIENT:
                self.reject('FILES_RECEIPT_UNSAFE')
            self.validate_plan(value.get('plan'))
            if type(value.get('owner')) is not str or not re.fullmatch('[A-F0-9]{64}', value['owner']) or type(value.get('mayHaveStaged', False)) is not bool:
                self.reject('FILES_RECEIPT_UNSAFE')
            if 'newIdentity' in value and (not isinstance(value['newIdentity'], list) or len(value['newIdentity']) != 2 or any(type(item) is not int or not 0 <= item < 2**64 for item in value['newIdentity']) or not value['newIdentity'][1]):
                self.reject('FILES_RECEIPT_UNSAFE')
            if value['state'] in ('Ready', 'Publishing', 'Completed') and ('newIdentity' not in value or not value.get('mayHaveStaged')):
                self.reject('FILES_RECEIPT_UNSAFE')
            for key in ('treeSha256', 'version'):
                if key in value and (type(value[key]) is not str or not re.fullmatch('[A-F0-9]{64}', value[key])): self.reject('FILES_RECEIPT_UNSAFE')
            if value['state'] in ('Ready', 'Publishing', 'Completed') and 'treeSha256' not in value or value['state'] == 'Completed' and 'version' not in value:
                self.reject('FILES_RECEIPT_UNSAFE')
            return value
        except (ValueError, TypeError, KeyError, UnicodeError): self.reject('FILES_RECEIPT_UNSAFE')
        finally: os.close(descriptor)

    @contextlib.contextmanager
    def state(self, operation, owner, create=False):
        self.f.identity(operation)
        if type(owner) is not str or not re.fullmatch('[A-F0-9]{64}', owner): self.reject('FILES_OPERATION_INVALID')
        base = self.f.trusted_directory(self.f.STATE)
        folder = lock = None
        try:
            try: os.mkdir(self.FOLDER, 0o700, dir_fd=base); os.fsync(base)
            except FileExistsError: pass
            folder = self.f.open_at(base, self.FOLDER, os.O_RDONLY | os.O_DIRECTORY)
            info = os.fstat(folder)
            if info.st_uid != 0 or stat.S_IMODE(info.st_mode) != 0o700: self.reject('FILES_RECEIPT_UNSAFE')
            existing = self.read(folder, operation)
            if existing is not None and existing['owner'] != owner or existing is None and not create: self.reject('FILES_OPERATION_NOT_FOUND')
            if existing is None:
                with os.scandir(folder) as rows:
                    for count, _ in enumerate(rows, 1):
                        if count >= 19999: self.reject('FILES_RECEIPT_LIMIT')
            lock = os.open(operation + '.lock', os.O_WRONLY | os.O_CREAT | os.O_NOFOLLOW, 0o600, dir_fd=folder)
            info = os.fstat(lock)
            if not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_nlink != 1 or stat.S_IMODE(info.st_mode) != 0o600: self.reject('FILES_RECEIPT_UNSAFE')
            try: fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
            except BlockingIOError: self.reject('FILES_OPERATION_BUSY')
            with self.f.namespace_lock():
                existing = self.read(folder, operation)
                if existing is not None and existing['owner'] != owner: self.reject('FILES_OPERATION_NOT_FOUND')
                yield folder, existing
        finally:
            if lock is not None: os.close(lock)
            if folder is not None: os.close(folder)
            os.close(base)

    def save(self, folder, record):
        if len(self.encode(record)) > 32768: self.reject('FILES_RECEIPT_LIMIT')
        self.f.save_receipt(folder, record['operationId'], record)

    def request(self, request):
        if not isinstance(request, dict) or set(request) != {'operationId', 'owner', 'plan'}: self.reject('FILES_UNPACK_COMMAND_INVALID')
        self.validate_plan(request['plan'])

    def unpack(self, request):
        self.request(request)
        check = self.guard()
        with self.state(request['operationId'], request['owner'], True) as (folder, record):
            if record is not None:
                if record['plan'] != request['plan']: self.reject('FILES_IDEMPOTENCY_CONFLICT')
                self.recover(folder, record)
                return self.public(record)
            if self.plan(request['plan']['command'], check=check) != request['plan']: self.reject('FILES_VERSION_CONFLICT')
            record = {**request, 'state': 'Preparing'}
            self.save(folder, record); self.checkpoint('Preparing')
            try: self.publish(folder, record, check)
            except BaseException:
                with contextlib.suppress(OSError, self.f.Rejected): self.recover(folder, record)
                raise
            return self.public(record)

    def discard(self, request):
        self.request(request)
        with self.state(request['operationId'], request['owner'], True) as (folder, record):
            if record is None:
                record = {**request, 'state': 'Aborted'}
                self.save(folder, record)
            else:
                if record['plan'] != request['plan']: self.reject('FILES_IDEMPOTENCY_CONFLICT')
                self.recover(folder, record)
            return self.public(record)

    def checkpoint(self, phase):
        """Trusted Linux crash instrumentation; never controlled by request data."""

    def snapshot(self, descriptor, plan, policy, check, expected=None):
        command = {**plan['command']['target'], 'expectedVersion': self.f.version(os.fstat(descriptor))}
        inventory = self.manifest_type(self.f, timeout_seconds=600, cancelled=check)._scan_descriptor(command, descriptor, policy)
        if inventory['entryCount'] != plan['entryCount'] or inventory['totalBytes'] != plan['totalBytes']: self.reject('FILES_ARCHIVE_CONTENT_INVALID')
        if expected is not None:
            wanted = {row['path']: row for row in expected['entries']}
            if len(wanted) != len(inventory['entries']): self.reject('FILES_ARCHIVE_CONTENT_INVALID')
            for actual in inventory['entries']:
                original = wanted.get(actual['path'])
                metadata = actual['sourceIdentity']
                if original is None or any(actual[key] != original[key] for key in ('kind', 'sizeBytes', 'sha256')) or metadata[2] & 0o7777 != original['mode'] or metadata[3:5] != [policy.get('ownerUid', 0), policy.get('ownerGid', 0)] or metadata[7] != original['mtimeNs']:
                    self.reject('FILES_ARCHIVE_CONTENT_INVALID')
        # Directory rename changes ctime, which is deliberately not part of this
        # identity digest; file bytes/modes/owner/inodes/mtime/membership all are.
        return self.digest([{'path': row['path'], 'kind': row['kind'], 'identity': row['sourceIdentity'], 'sizeBytes': row['sizeBytes'], 'sha256': row['sha256']} for row in inventory['entries']])

    def publish(self, folder, record, check):
        plan = record['plan']; command = plan['command']
        codec = self.codec_type(self.f, self.manifest_type, cancelled=check)
        inventory = self.inspect_source(plan, codec, check)
        policy = self.bound(plan)
        with self.target(command) as (_, parent, leaf):
            if self.identity(parent) != plan['targetParent']: self.reject('FILES_PARENT_CHANGED')
            capacity = os.fstatvfs(parent)
            reserve = max(512 * 1024**2, capacity.f_blocks * capacity.f_frsize // 20) + plan['entryCount'] * 16384
            if capacity.f_bavail * capacity.f_frsize < plan['totalBytes'] + reserve or capacity.f_files > 0 and capacity.f_favail < plan['entryCount'] + 4:
                self.reject('FILES_CAPACITY_REQUIRED')
            private = self.f.staging(parent)
            target = None; temporary = record['operationId'] + self.TEMPORARY_SUFFIX
            try:
                record.update(state='Staging', mayHaveStaged=True)
                self.save(folder, record); self.checkpoint('StagingIntent'); check()
                os.mkdir(temporary, 0o700, dir_fd=private); os.fsync(private); self.checkpoint('Created')
                target = self.f.open_at(private, temporary, os.O_RDONLY | os.O_DIRECTORY)
                record['newIdentity'] = self.identity(target)
                self.save(folder, record); self.checkpoint('Staging')
                with self.archive(command, codec) as source:
                    codec.extract(source, target, inventory, command['format'], owner_uid=policy.get('ownerUid', 0), owner_gid=policy.get('ownerGid', 0))
                root = inventory['entries'][0]
                if root['path'] != '' or root['kind'] != 'Directory': self.reject('FILES_ARCHIVE_CONTENT_INVALID')
                os.fchown(target, policy.get('ownerUid', 0), policy.get('ownerGid', 0)); os.fchmod(target, root['mode'])
                os.utime(target, ns=(root['mtimeNs'], root['mtimeNs'])); os.fsync(target)
                record['treeSha256'] = self.snapshot(target, plan, policy, check, inventory)
                record['state'] = 'Ready'
                self.save(folder, record); self.checkpoint('Ready'); check()
                self.inspect_source(plan, codec, check); self.bound(plan)
                record['state'] = 'Publishing'
                self.save(folder, record); self.checkpoint('Publishing'); check()
                self.inspect_source(plan, codec, check)
                policy = self.bound(plan)
                if self.snapshot(target, plan, policy, check, inventory) != record['treeSha256']: self.reject('FILES_ARCHIVE_CONTENT_INVALID')
                with self.target(command) as (_, current, _):
                    if self.identity(current) != plan['targetParent']: self.reject('FILES_PARENT_CHANGED')
                staged_info = os.stat(temporary, dir_fd=private, follow_symlinks=False)
                if not stat.S_ISDIR(staged_info.st_mode) or self.identity(target) != [staged_info.st_dev, staged_info.st_ino]:
                    self.reject('FILES_ARCHIVE_CONTENT_INVALID')
                self.publish_target(folder, record, private, temporary, parent, leaf, check)
                os.fsync(parent); os.fsync(private); self.checkpoint('Published')
                self.recover(folder, record)
            finally:
                if target is not None: os.close(target)
                os.close(private)

    def publish_target(self, folder, record, private, temporary, parent, leaf, check):
        check(); self.bound(record['plan'])
        self.f.exchange(private, temporary, parent, leaf, 1)

    def recover(self, folder, record):
        if record['state'] in self.TERMINAL: return
        previous = record['state']; record['state'] = 'Unknown'
        # Reconcile an already-issued effect even after its write was canceled;
        # cancellation is not proof it never published. Still bound work/time.
        plan = record['plan']; check = self.guard(cancellation=False)
        try:
            policy = self.bound(plan)
            with self.target(plan['command']) as (_, parent, leaf):
                if self.identity(parent) != plan['targetParent']: self.reject('FILES_PARENT_CHANGED')
                try: target = self.f.open_at(parent, leaf, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
                except FileNotFoundError: target = None
                if target is not None:
                    try:
                        if previous in ('Ready', 'Publishing') and self.identity(target) == record.get('newIdentity') and self.snapshot(target, plan, policy, check) == record.get('treeSha256'):
                            self.bound(plan)
                            record.update(state='Completed', version=self.f.version(os.fstat(target)))
                    finally: os.close(target)
                else:
                    private = self.f.staging(parent)
                    try:
                        try: staged = self.f.open_at(private, record['operationId'] + self.TEMPORARY_SUFFIX, os.O_RDONLY | os.O_DIRECTORY)
                        except FileNotFoundError: staged = None
                        if staged is not None:
                            try:
                                if self.identity(staged) == record.get('newIdentity'): record['state'] = 'Aborted'
                            finally: os.close(staged)
                        elif previous == 'Preparing' and not record.get('mayHaveStaged') and not record.get('newIdentity'):
                            record['state'] = 'Aborted'
                    finally: os.close(private)
        except (OSError, self.f.Rejected): pass
        self.save(folder, record)

    def lookup(self, request):
        if not isinstance(request, dict) or set(request) != {'operationId', 'owner'}: self.reject('FILES_STATUS_INVALID')
        with self.state(request['operationId'], request['owner']) as (folder, record):
            self.recover(folder, record)
            return self.public(record)

    def public(self, record):
        plan = record['plan']; completed = record['state'] == 'Completed'
        return {'operationId': record['operationId'], 'state': record['state'], 'fingerprint': plan['fingerprint'],
            'format': plan['command']['format'], 'entryCount': plan['entryCount'], 'totalBytes': plan['totalBytes'],
            'archiveBytes': plan['archiveBytes'], 'archiveSha256': plan['archiveSha256'],
            'version': record.get('version', '') if completed else '',
            'partialRetained': not completed and bool(record.get('mayHaveStaged'))}
