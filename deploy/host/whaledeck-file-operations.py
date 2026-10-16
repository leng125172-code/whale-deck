#!/usr/bin/env python3
"""Registered file mutation engine; called only by the fixed file driver.

No shell, user-selected UID, arbitrary root or automatic retry of side effects.
The transport module supplies its existing openat2/protection/receipt policy.
"""
import contextlib
import fcntl
import hashlib
import json
import os
import re
import stat


class FileOperations:
    FIELDS = {'kind', 'sourceRootId', 'sourcePath', 'targetRootId', 'targetPath', 'expectedVersion', 'mode'}
    PLAN_FIELDS = {'command', 'sourceIdentity', 'sourceParent', 'targetParent', 'sizeBytes', 'sha256', 'fingerprint'}

    def __init__(self, files):
        self.f = files

    def reject(self, code):
        raise self.f.Rejected(code)

    @staticmethod
    def parent_identity(parent):
        value = os.fstat(parent)
        return [value.st_dev, value.st_ino]

    def location(self, command, target=False):
        prefix = 'target' if target else 'source'
        return {'rootId': command[prefix + 'RootId'], 'path': command[prefix + 'Path']}

    def validate(self, command):
        if not isinstance(command, dict) or set(command) != self.FIELDS or any(not isinstance(value, str) for value in command.values()):
            self.reject('FILES_OPERATION_INVALID')
        kind = command['kind']
        if kind not in ('mkdir', 'copy', 'move', 'chmod'):
            self.reject('FILES_OPERATION_INVALID')
        if kind in ('mkdir', 'chmod'):
            if not re.fullmatch(r'0[0-7]{3}', command['mode']): self.reject('FILES_MODE_INVALID')
        elif command['mode'] != '': self.reject('FILES_MODE_INVALID')
        if kind == 'mkdir':
            if any(command[key] != '' for key in ('sourceRootId', 'sourcePath')) or command['expectedVersion'] != 'ABSENT':
                self.reject('FILES_OPERATION_INVALID')
        elif not re.fullmatch(r'[A-F0-9]{64}', command['expectedVersion']): self.reject('FILES_VERSION_CONFLICT')
        if kind == 'chmod' and any(command[key] != '' for key in ('targetRootId', 'targetPath')):
            self.reject('FILES_OPERATION_INVALID')

    def plan(self, command):
        self.validate(command)
        kind = command['kind']
        source_identity = source_parent = target_parent = None
        size = 0; digest = ''
        if kind != 'mkdir':
            with self.f.directory(self.location(command), 'mutate') as (_, parent, leaf):
                descriptor = self.f.open_at(parent, leaf, os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW)
                try:
                    info = self.f.regular(descriptor)
                    if self.f.version(info) != command['expectedVersion']: self.reject('FILES_VERSION_CONFLICT')
                    source_identity = self.f.publication_identity(info)
                    source_parent = self.parent_identity(parent)
                    size = info.st_size; digest = self.hash_descriptor(descriptor, info)
                finally: os.close(descriptor)
                actions = self.f.roots()[command['sourceRootId']]['allowedActions']
                if kind in ('copy', 'move') and 'files-download' not in actions: self.reject('FILES_ROOT_FORBIDDEN')
        if kind != 'chmod':
            with self.f.directory(self.location(command, True), 'mutate') as (_, parent, leaf):
                try: os.stat(leaf, dir_fd=parent, follow_symlinks=False)
                except FileNotFoundError: pass
                else: self.reject('FILES_VERSION_CONFLICT')
                target_parent = self.parent_identity(parent)
                actions = self.f.roots()[command['targetRootId']]['allowedActions']
                if kind != 'mkdir' and 'files-upload' not in actions: self.reject('FILES_ROOT_FORBIDDEN')
        result = {'command': command, 'sourceIdentity': source_identity, 'sourceParent': source_parent,
                  'targetParent': target_parent, 'sizeBytes': size, 'sha256': digest}
        result['fingerprint'] = hashlib.sha256(b'whaledeck-file-operation-v1:' + json.dumps(result, sort_keys=True, separators=(',', ':')).encode()).hexdigest().upper()
        return result

    def hash_descriptor(self, descriptor, expected, output=None):
        digest = hashlib.sha256(); offset = 0
        while offset < expected.st_size:
            data = os.read(descriptor, min(self.f.CHUNK, expected.st_size - offset))
            if not data: self.reject('FILES_READ_CHANGED')
            digest.update(data); offset += len(data)
            if output is not None:
                pending = memoryview(data)
                while pending:
                    count = os.write(output, pending)
                    if count <= 0: self.reject('FILES_WRITE_FAILED')
                    pending = pending[count:]
        if self.f.version(self.f.regular(descriptor)) != self.f.version(expected): self.reject('FILES_READ_CHANGED')
        return digest.hexdigest().upper()

    @contextlib.contextmanager
    def state(self, operation, owner, writing=False):
        self.f.identity(operation)
        if not isinstance(owner, str) or not re.fullmatch(r'[A-F0-9]{64}', owner): self.reject('FILES_OPERATION_INVALID')
        base = self.f.trusted_directory(self.f.STATE); state = lock = exclusive = None
        try:
            try: os.mkdir('operations', 0o700, dir_fd=base); os.fsync(base)
            except FileExistsError: pass
            state = self.f.open_at(base, 'operations', os.O_RDONLY | os.O_DIRECTORY)
            value = os.fstat(state)
            if value.st_uid != 0 or stat.S_IMODE(value.st_mode) != 0o700: self.reject('FILES_RECEIPT_UNSAFE')
            record = self.read(state, operation)
            if record is not None and record['owner'] != owner: self.reject('FILES_OPERATION_NOT_FOUND')
            if record is None and not writing: self.reject('FILES_OPERATION_NOT_FOUND')
            if record is None and (len(os.listdir(state)) >= 20000): self.reject('FILES_RECEIPT_LIMIT')
            lock = os.open(operation + '.lock', os.O_WRONLY | os.O_CREAT | os.O_NOFOLLOW, 0o600, dir_fd=state)
            try: fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
            except BlockingIOError: self.reject('FILES_OPERATION_BUSY')
            if writing:
                exclusive = self.f.namespace_lock(); exclusive.__enter__()
            record = self.read(state, operation)
            if record is not None and record['owner'] != owner: self.reject('FILES_OPERATION_NOT_FOUND')
            yield state, record
        finally:
            if exclusive is not None: exclusive.__exit__(None, None, None)
            if lock is not None: os.close(lock)
            if state is not None: os.close(state)
            os.close(base)

    def read(self, state, operation):
        try: descriptor = os.open(operation + '.json', os.O_RDONLY | os.O_NOFOLLOW, dir_fd=state)
        except FileNotFoundError: return None
        try:
            value = os.fstat(descriptor)
            if value.st_uid != 0 or value.st_mode & 0o077 or not stat.S_ISREG(value.st_mode) or value.st_size > 32768:
                self.reject('FILES_RECEIPT_UNSAFE')
            return json.loads(os.read(descriptor, 32769), object_pairs_hook=self.f.unique_object)
        finally: os.close(descriptor)

    def save(self, state, record):
        if len(json.dumps(record).encode()) > 32768: self.reject('FILES_RECEIPT_LIMIT')
        self.f.save_receipt(state, record['operationId'], record)

    def check_parent(self, command, expected, target=False):
        with self.f.directory(self.location(command, target), 'mutate') as (_, parent, _):
            if self.parent_identity(parent) != expected: self.reject('FILES_PARENT_CHANGED')

    def matches(self, parent, leaf, record, directory=False):
        try: descriptor = self.f.open_at(parent, leaf, os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW | (os.O_DIRECTORY if directory else 0))
        except FileNotFoundError: return None
        try:
            info = os.fstat(descriptor) if directory else self.f.regular(descriptor)
            if [info.st_dev, info.st_ino] != record.get('newIdentity'): return None
            if self.f.publication_identity(info) != record.get('newMetadata'): return None
            if not directory and self.hash_descriptor(descriptor, info) != record['plan']['sha256']: return None
            return info
        finally: os.close(descriptor)

    def apply(self, request):
        if not isinstance(request, dict) or set(request) != {'operationId', 'owner', 'plan'} or not isinstance(request['plan'], dict) or set(request['plan']) != self.PLAN_FIELDS:
            self.reject('FILES_OPERATION_INVALID')
        self.validate(request['plan']['command'])
        with self.state(request['operationId'], request['owner'], writing=True) as (state, record):
            if record is not None:
                if record['plan'] != request['plan']: self.reject('FILES_IDEMPOTENCY_CONFLICT')
                self.recover(state, record)
                return self.public(record)
            plan = self.plan(request['plan']['command'])
            if plan != request['plan']: self.reject('FILES_VERSION_CONFLICT')
            record = {'operationId': request['operationId'], 'owner': request['owner'], 'plan': plan, 'state': 'Preparing'}
            self.save(state, record)
            try:
                if plan['command']['kind'] == 'chmod': self.permissions(state, record)
                else: self.publish(state, record)
            except BaseException:
                # Recovery proves only the observed outcome. Never run a missing
                # publication, chmod or source removal a second time here.
                with contextlib.suppress(OSError, self.f.Rejected): self.recover(state, record)
                raise
            return self.public(record)

    def permissions(self, state, record):
        plan = record['plan']; command = plan['command']
        with self.f.directory(self.location(command), 'mutate') as (_, parent, leaf):
            descriptor = self.f.open_at(parent, leaf, os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW)
            try:
                before = self.f.regular(descriptor)
                if self.f.version(before) != command['expectedVersion'] or self.hash_descriptor(descriptor, before) != plan['sha256']:
                    self.reject('FILES_VERSION_CONFLICT')
                self.check_parent(command, plan['sourceParent'])
                record['state'] = 'PermissionApplying'; self.save(state, record)
                os.fchmod(descriptor, int(command['mode'], 8)); os.fsync(descriptor); os.fsync(parent)
            finally: os.close(descriptor)
        self.recover(state, record)

    def publish(self, state, record):
        plan = record['plan']; command = plan['command']; kind = command['kind']; operation = record['operationId']
        with self.f.directory(self.location(command, True), 'mutate') as (_, parent, leaf):
            if self.parent_identity(parent) != plan['targetParent']: self.reject('FILES_PARENT_CHANGED')
            space = os.fstatvfs(parent)
            if space.f_bavail * space.f_frsize < plan['sizeBytes'] + max(512 * 1024 * 1024, space.f_blocks * space.f_frsize // 20) + 4 * self.f.MAX_BYTES:
                self.reject('FILES_CAPACITY_REQUIRED')
            private = self.f.staging(parent); temporary = operation + '.mutation'
            try:
                if kind == 'mkdir':
                    os.mkdir(temporary, 0o700, dir_fd=private)
                    descriptor = self.f.open_at(private, temporary, os.O_RDONLY | os.O_DIRECTORY)
                else:
                    descriptor = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600, dir_fd=private)
                try:
                    info = os.fstat(descriptor); record['newIdentity'] = [info.st_dev, info.st_ino]; self.save(state, record)
                    registered = self.f.roots()[command['targetRootId']]
                    uid, gid = registered.get('ownerUid', 0), registered.get('ownerGid', 0)
                    mode = int(command['mode'], 8) if kind == 'mkdir' else plan['sourceIdentity'][2] & 0o777
                    if kind != 'mkdir':
                        with self.f.directory(self.location(command), 'mutate') as (_, source_parent, source_leaf):
                            source = self.f.open_at(source_parent, source_leaf, os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW)
                            try:
                                before = self.f.regular(source)
                                if self.f.version(before) != command['expectedVersion']: self.reject('FILES_VERSION_CONFLICT')
                                if self.hash_descriptor(source, before, descriptor) != plan['sha256']: self.reject('FILES_READ_CHANGED')
                                os.utime(descriptor, ns=(before.st_atime_ns, before.st_mtime_ns))
                                if kind == 'move': uid, gid = before.st_uid, before.st_gid
                            finally: os.close(source)
                            self.check_parent(command, plan['sourceParent'])
                    os.fchown(descriptor, uid, gid); os.fchmod(descriptor, mode); os.fsync(descriptor); os.fsync(private)
                    record['newMetadata'] = self.f.publication_identity(os.fstat(descriptor))
                finally: os.close(descriptor)
                self.check_parent(command, plan['targetParent'], True)
                record['state'] = 'DestinationApplying'; self.save(state, record)
                self.f.exchange(private, temporary, parent, leaf, 1); os.fsync(parent); os.fsync(private)
                if kind == 'move':
                    record['state'] = 'SourceApplying'; self.save(state, record)
                    with self.f.directory(self.location(command), 'mutate') as (_, source_parent, source_leaf):
                        self.check_parent(command, plan['sourceParent'])
                        source_private = self.f.staging(source_parent)
                        try:
                            # Quarantine, then verify. Never unlink a possibly
                            # externally replaced source pathname or its bytes.
                            self.f.exchange(source_parent, source_leaf, source_private, temporary, 1)
                            os.fsync(source_parent); os.fsync(source_private)
                        finally: os.close(source_private)
                self.recover(state, record)
            finally: os.close(private)

    def recover(self, state, record):
        if record['state'] in ('Completed', 'Aborted', 'Unknown'): return
        plan = record['plan']; command = plan['command']; kind = command['kind']
        try:
            if kind == 'chmod':
                if record['state'] == 'Preparing':
                    record['state'] = 'Aborted'; self.save(state, record); return
                with self.f.directory(self.location(command), 'mutate') as (_, parent, leaf):
                    source = self.f.open_at(parent, leaf, os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW)
                    try:
                        current = self.f.regular(source)
                        desired = list(plan['sourceIdentity']); desired[2] = (desired[2] & ~0o7777) | int(command['mode'], 8)
                        if self.f.publication_identity(current) == desired and self.hash_descriptor(source, current) == plan['sha256']:
                            record.update(state='Completed', version=self.f.version(current))
                        elif self.f.version(current) == command['expectedVersion']: record['state'] = 'Aborted'
                        else: record['state'] = 'Unknown'
                    finally: os.close(source)
            else:
                with self.f.directory(self.location(command, True), 'mutate') as (_, parent, leaf):
                    private = self.f.staging(parent); temporary = record['operationId'] + '.mutation'
                    try:
                        actual = self.matches(parent, leaf, record, kind == 'mkdir')
                        pending = None
                        try:
                            candidate = os.stat(temporary, dir_fd=private, follow_symlinks=False)
                            if [candidate.st_dev, candidate.st_ino] == record.get('newIdentity') and ((kind == 'mkdir' and stat.S_ISDIR(candidate.st_mode)) or (kind != 'mkdir' and stat.S_ISREG(candidate.st_mode) and candidate.st_nlink == 1)):
                                pending = candidate
                        except FileNotFoundError: candidate = None
                        if actual is not None and kind != 'move': record.update(state='Completed', version=self.f.version(actual))
                        elif actual is not None and kind == 'move':
                            with self.f.directory(self.location(command), 'mutate') as (_, source_parent, source_leaf):
                                source_private = self.f.staging(source_parent)
                                try:
                                    retained = self.f.current(source_private, temporary)
                                    if retained is not None and self.f.publication_identity(retained) == plan['sourceIdentity'] and self.f.checksum(source_private, temporary) == plan['sha256']:
                                        record.update(state='Completed', sourceRetained=True, version=self.f.version(actual))
                                    else: record['state'] = 'Unknown'
                                finally: os.close(source_private)
                        elif pending is not None:
                            if kind == 'mkdir': os.rmdir(temporary, dir_fd=private)
                            else: os.unlink(temporary, dir_fd=private)
                            os.fsync(private); record['state'] = 'Aborted'
                        elif record['state'] == 'Preparing' and not record.get('newIdentity') and candidate is None:
                            record['state'] = 'Aborted'
                        else:
                            # No receipt-owned pending object is not proof that
                            # publication never happened (including inode loss).
                            record['state'] = 'Unknown'
                    finally: os.close(private)
            if record['state'] == 'Completed':
                self.check_parent(command, plan['sourceParent'] if kind == 'chmod' else plan['targetParent'], kind != 'chmod')
        except (OSError, self.f.Rejected): record['state'] = 'Unknown'
        self.save(state, record)

    def lookup(self, request):
        if not isinstance(request, dict) or set(request) != {'operationId', 'owner'}: self.reject('FILES_STATUS_INVALID')
        with self.state(request['operationId'], request['owner']) as (state, record):
            with self.f.namespace_lock() if record['state'] not in ('Completed', 'Aborted', 'Unknown') else contextlib.nullcontext():
                self.recover(state, record)
            return self.public(record)

    def public(self, record):
        plan = record['plan']; command = plan['command']; kind = command['kind']; binding = False
        if record['state'] == 'Completed':
            try:
                location = self.location(command, kind != 'chmod')
                with self.f.directory(location, 'stat') as (_, parent, leaf):
                    descriptor = self.f.open_at(parent, leaf, os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW | (os.O_DIRECTORY if kind == 'mkdir' else 0))
                    try: binding = self.f.version(os.fstat(descriptor)) == record['version']
                    finally: os.close(descriptor)
            except (OSError, self.f.Rejected): pass
        return {'operationId': record['operationId'], 'kind': kind, 'state': record['state'], 'fingerprint': plan['fingerprint'],
                'version': record.get('version', '') if record['state'] == 'Completed' else '', 'sizeBytes': plan['sizeBytes'], 'sha256': plan['sha256'],
                'sourceRetained': record['state'] == 'Completed' and bool(record.get('sourceRetained')), 'bindingCurrent': binding}
