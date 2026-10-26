#!/usr/bin/env python3
"""Private container-bound upload staging, with no publication or CLI surface.

The trusted caller must supply ContainerRoot and the existing openat2 primitives.
This module stores only private host-side payloads. Publication must separately
require the current authority, a durable job, and a verified HDD capture.
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


class ContainerUploads:
    MAX_RECEIPTS = 10000
    def __init__(self, read_engine, files, state_path):
        self.r, self.f, self.state_path = read_engine, files, Path(state_path)

    def reject(self, suffix):
        raise self.r.Rejected('CONTAINER_UPLOAD_' + suffix)

    def session_scope(self, request, session):
        binding = getattr(session, 'request', None)
        if type(binding) is not dict or any(binding.get(key) != request[key] for key in ('containerId', 'instanceId')):
            self.reject('SESSION_SCOPE_MISMATCH')

    def target_proof(self, session):
        """Freeze the real root, namespaces and approved Docker configuration.

        This is independent of the file version and must survive a new session
        before capture/publication. An unchanged ID/start time alone is weaker.
        """
        mounts, identity = getattr(session, 'mounts', None), getattr(session, 'identity', None)
        if type(mounts) is not bytes or len(mounts) != 32 or not isinstance(identity, tuple) or len(identity) != 3 or \
                type(identity[0]) is not str or not identity[0].isdigit() or any(type(value) is not int or value <= 0 for value in identity[1:]):
            self.reject('SESSION_PROOF_INVALID')
        root = os.fstat(session.root)
        value = [root.st_dev, root.st_ino, *identity, mounts.hex()]
        return hashlib.sha256(b'whaledeck-container-upload-target-v1\n' + json.dumps(value,
            separators=(',', ':')).encode()).hexdigest().upper()

    def validate_target_proof(self, record, session):
        if record.get('targetProof') != self.target_proof(session):
            self.reject('TARGET_CHANGED')

    def upload_request(self, request):
        required = {'containerId', 'instanceId', 'path', 'expectedVersion', 'expectedParentVersion',
                    'operationId', 'owner', 'sizeBytes', 'sha256'}
        if type(request) is not dict or set(request) != required:
            self.reject('REQUEST_INVALID')
        self.r.target_binding(request['containerId'], request['instanceId'])
        self.r.canonical_path(request['path'], empty=False)
        self.f.identity(request['operationId'])
        if self.r.protected(request['path'], self.f): self.reject('PROTECTED')
        if type(request['sizeBytes']) is not int or not 0 <= request['sizeBytes'] <= self.r.MAX_BYTES:
            self.reject('SIZE_LIMIT')
        for key in ('owner', 'sha256', 'expectedParentVersion'):
            if type(request[key]) is not str or not re.fullmatch('[A-F0-9]{64}', request[key]):
                self.reject('REQUEST_INVALID')
        if request['expectedVersion'] != 'ABSENT' and (type(request['expectedVersion']) is not str or
                not re.fullmatch('[A-F0-9]{64}', request['expectedVersion'])):
            self.reject('VERSION_INVALID')
        return hashlib.sha256(b'whaledeck-container-upload-stage-v1\n' + json.dumps(request,
            sort_keys=True, separators=(',', ':'), ensure_ascii=False).encode()).hexdigest().upper()

    def scope_request(self, request):
        if type(request) is not dict or set(request) != {'containerId', 'instanceId', 'operationId', 'owner'}:
            self.reject('REQUEST_INVALID')
        self.r.target_binding(request['containerId'], request['instanceId'])
        self.f.identity(request['operationId'])
        if type(request['owner']) is not str or not re.fullmatch('[A-F0-9]{64}', request['owner']):
            self.reject('REQUEST_INVALID')

    @contextlib.contextmanager
    def locked(self, operation, allow_new=False):
        if not self.state_path.is_absolute() or self.state_path != self.state_path.resolve(strict=True):
            self.reject('STATE_UNSAFE')
        state = self.f.trusted_directory(self.state_path)
        lock = None
        try:
            if stat.S_IMODE(os.fstat(state).st_mode) != 0o700: self.reject('STATE_UNSAFE')
            try: os.stat(operation + '.json', dir_fd=state, follow_symlinks=False)
            except FileNotFoundError:
                if not allow_new: self.reject('NOT_FOUND')
                names = os.listdir(state)
                if len(names) >= 3 * self.MAX_RECEIPTS + 32 or sum(name.endswith('.json') for name in names) >= self.MAX_RECEIPTS:
                    self.reject('RECEIPT_LIMIT')
            lock = os.open(operation + '.lock', os.O_RDWR | os.O_CREAT | os.O_NOFOLLOW | os.O_CLOEXEC,
                           0o600, dir_fd=state)
            info = os.fstat(lock)
            if not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_nlink != 1 or stat.S_IMODE(info.st_mode) != 0o600:
                self.reject('STATE_UNSAFE')
            try: fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
            except BlockingIOError: self.reject('BUSY')
            yield state
        finally:
            if lock is not None: os.close(lock)
            os.close(state)

    @contextlib.contextmanager
    def stream_slot(self, state):
        descriptor = None
        try:
            for index in range(2):
                candidate = os.open('upload-slot-' + str(index) + '.lock', os.O_RDWR | os.O_CREAT | os.O_NOFOLLOW | os.O_CLOEXEC,
                                    0o600, dir_fd=state)
                try:
                    info = os.fstat(candidate)
                    if not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_nlink != 1 or info.st_size != 0 or stat.S_IMODE(info.st_mode) != 0o600:
                        self.reject('STATE_UNSAFE')
                    try: fcntl.flock(candidate, fcntl.LOCK_EX | fcntl.LOCK_NB)
                    except BlockingIOError: continue
                    descriptor, candidate = candidate, None
                    break
                finally:
                    if candidate is not None: os.close(candidate)
            if descriptor is None: self.reject('BUSY')
            yield
        finally:
            if descriptor is not None: os.close(descriptor)

    def receipt(self, state, operation):
        value = self.f.read_receipt(state, operation)
        if value is None: return None
        if type(value) is not dict or type(value.get('schemaVersion')) is not int or value['schemaVersion'] != 1:
            self.reject('RECEIPT_INVALID')
        request = value.get('request')
        if self.upload_request(request) != value.get('fingerprint') or any(value.get(key) != request[key]
                for key in ('operationId', 'owner', 'containerId', 'instanceId')) or value['operationId'] != operation:
            self.reject('RECEIPT_INVALID')
        if value.get('state') not in ('Receiving', 'Verified', 'Staged', 'Interrupted', 'Aborted', 'Discarded', 'Unknown'):
            self.reject('RECEIPT_INVALID')
        if type(value.get('targetProof')) is not str or not re.fullmatch('[A-F0-9]{64}', value['targetProof']):
            self.reject('RECEIPT_INVALID')
        return value

    def scoped(self, state, request):
        value = self.receipt(state, request['operationId'])
        if value is None: self.reject('NOT_FOUND')
        if any(value[key] != request[key] for key in ('owner', 'containerId', 'instanceId')):
            self.reject('SCOPE_MISMATCH')
        return value

    def target(self, request, session):
        parent_path, _, leaf = request['path'].rpartition('/')
        parent = self.f.open_at(session.root, parent_path or '.', os.O_RDONLY | os.O_DIRECTORY, cross_mount=True)
        try:
            info = os.fstat(parent)
            if self.r.version(request, parent_path, info) != request['expectedParentVersion']:
                self.reject('PARENT_VERSION_CONFLICT')
            try:
                descriptor = self.f.open_at(parent, leaf, os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW, cross_mount=True)
            except FileNotFoundError:
                if request['expectedVersion'] != 'ABSENT': self.reject('VERSION_CONFLICT')
                return
            try:
                current = self.f.regular(descriptor)
                if self.r.version(request, request['path'], current) != request['expectedVersion']:
                    self.reject('VERSION_CONFLICT')
            finally: os.close(descriptor)
        finally: os.close(parent)

    def payload(self, state, record, complete=False):
        operation = record['operationId']
        found = []
        for suffix in ('.partial', '.data'):
            try: info = os.stat(operation + suffix, dir_fd=state, follow_symlinks=False)
            except FileNotFoundError: continue
            if not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_nlink != 1 or stat.S_IMODE(info.st_mode) != 0o600 or \
                    [info.st_dev, info.st_ino] != record.get('payloadIdentity'):
                self.reject('PAYLOAD_CHANGED')
            found.append(operation + suffix)
        if len(found) != 1: self.reject('PAYLOAD_CHANGED')
        name = found[0]
        if complete:
            descriptor = self.f.open_at(state, name, os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW)
            try:
                before = self.f.regular(descriptor)
                if self.f.publication_identity(before) != record.get('payloadProof') or before.st_size != record['request']['sizeBytes']:
                    self.reject('PAYLOAD_CHANGED')
                digest = hashlib.sha256()
                while chunk := os.read(descriptor, self.r.CHUNK): digest.update(chunk)
                if self.r.fingerprint(before) != self.r.fingerprint(os.fstat(descriptor)) or digest.hexdigest().upper() != record['request']['sha256']:
                    self.reject('PAYLOAD_CHANGED')
            finally: os.close(descriptor)
        return name

    def public(self, record, replayed=False):
        return {'operationId': record['operationId'], 'containerId': record['containerId'],
                'instanceId': record['instanceId'], 'state': record['state'],
                'sizeBytes': record['request']['sizeBytes'], 'sha256': record['request']['sha256'], 'replayed': replayed}

    def stage(self, request, session, source):
        fingerprint = self.upload_request(request)
        self.session_scope(request, session)
        session.revalidate()
        proof = self.target_proof(session)
        self.target(request, session)
        operation = request['operationId']
        with self.locked(operation, allow_new=True) as state, self.stream_slot(state):
            record = self.receipt(state, operation)
            descriptor = None
            created = False
            try:
                if record is not None:
                    if record['fingerprint'] != fingerprint: self.reject('IDEMPOTENCY_CONFLICT')
                    self.validate_target_proof(record, session)
                    if record['state'] not in ('Verified', 'Staged'): self.reject('PREVIOUS_OPERATION_INCOMPLETE')
                    self.payload(state, record, complete=True)
                else:
                    capacity = os.fstatvfs(state)
                    reserve = max(128 * 1024**2, capacity.f_blocks * capacity.f_frsize // 20)
                    if capacity.f_bavail * capacity.f_frsize < request['sizeBytes'] + reserve:
                        self.reject('CAPACITY_LOW')
                    descriptor = os.open(operation + '.partial', os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW,
                                         0o600, dir_fd=state)
                    created = True
                    info = os.fstat(descriptor)
                    record = {'schemaVersion': 1, **{key: request[key] for key in ('operationId', 'owner', 'containerId', 'instanceId')},
                              'request': dict(request), 'fingerprint': fingerprint, 'state': 'Receiving',
                              'targetProof': proof,
                              'payloadIdentity': [info.st_dev, info.st_ino], 'createdAtUnixMs': time.time_ns() // 1000000}
                    self.f.save_receipt(state, operation, record)
                digest, offset, deadline = hashlib.sha256(), 0, time.monotonic() + 300
                while True:
                    session.current()
                    if time.monotonic() > deadline: self.reject('TIMEOUT')
                    row = self.f.line(source)
                    if row == {'type': 'commit'}:
                        if hasattr(source, 'finish'): source.finish()
                        elif source.read(1): self.reject('COMMIT_TRAILING_DATA')
                        break
                    if set(row) != {'type', 'offset', 'data'} or row['type'] != 'chunk' or type(row['offset']) is not int or \
                            row['offset'] != offset or type(row['data']) is not str or len(row['data']) > 43692:
                        self.reject('CHUNK_INVALID')
                    try: data = base64.b64decode(row['data'], validate=True)
                    except ValueError: self.reject('CHUNK_INVALID')
                    if not data or len(data) > self.r.CHUNK or offset + len(data) > request['sizeBytes']:
                        self.reject('SIZE_MISMATCH')
                    digest.update(data)
                    offset += len(data)
                    if created:
                        capacity = os.fstatvfs(state)
                        if capacity.f_bavail * capacity.f_frsize < len(data) + max(128 * 1024**2, capacity.f_blocks * capacity.f_frsize // 20):
                            self.reject('CAPACITY_LOW')
                        remaining = memoryview(data)
                        while remaining:
                            written = os.write(descriptor, remaining)
                            if written <= 0: self.reject('WRITE_FAILED')
                            remaining = remaining[written:]
                if offset != request['sizeBytes'] or digest.hexdigest().upper() != request['sha256']:
                    self.reject('CHECKSUM_MISMATCH')
                session.revalidate()
                self.validate_target_proof(record, session)
                self.target(request, session)
                if created:
                    os.fsync(descriptor)
                    record.update(state='Verified', receivedBytes=offset, receivedSha256=request['sha256'],
                                  payloadProof=self.f.publication_identity(os.fstat(descriptor)))
                    self.f.save_receipt(state, operation, record)
                name = self.payload(state, record, complete=True)
                if name.endswith('.partial'):
                    self.f.exchange(state, name, state, operation + '.data', 1)
                    os.fsync(state)
                replayed = not created
                record['state'] = 'Staged'
                self.f.save_receipt(state, operation, record)
                return self.public(record, replayed)
            finally:
                if descriptor is not None: os.close(descriptor)
                if created and record is not None and record['state'] == 'Receiving':
                    try:
                        name = self.payload(state, record)
                        os.unlink(name, dir_fd=state)
                        os.fsync(state)
                        record['state'] = 'Aborted'
                    except (OSError, self.r.Rejected): record['state'] = 'Unknown'
                    self.f.save_receipt(state, operation, record)

    def inspect(self, request, session):
        self.scope_request(request)
        self.session_scope(request, session)
        session.revalidate()
        with self.locked(request['operationId']) as state:
            record = self.scoped(state, request)
            self.validate_target_proof(record, session)
            if record['state'] == 'Receiving':
                record['state'] = 'Interrupted'
                self.f.save_receipt(state, record['operationId'], record)
            elif record['state'] in ('Verified', 'Staged'):
                name = self.payload(state, record, complete=True)
                if record['state'] == 'Verified' and name.endswith('.data'):
                    record['state'] = 'Staged'
                    self.f.save_receipt(state, record['operationId'], record)
            session.revalidate()
            return self.public(record)

    def discard(self, request, session):
        self.scope_request(request)
        self.session_scope(request, session)
        session.revalidate()
        with self.locked(request['operationId']) as state:
            record = self.scoped(state, request)
            self.validate_target_proof(record, session)
            if record['state'] == 'Discarded': return self.public(record, True)
            if record['state'] == 'Unknown': self.reject('PREVIOUS_OPERATION_INCOMPLETE')
            if record['state'] == 'Aborted':
                for suffix in ('.partial', '.data'):
                    try: os.stat(record['operationId'] + suffix, dir_fd=state, follow_symlinks=False)
                    except FileNotFoundError: continue
                    self.reject('PAYLOAD_CHANGED')
            else:
                name = self.payload(state, record, complete=record['state'] in ('Verified', 'Staged'))
                session.revalidate()
                os.unlink(name, dir_fd=state)
                os.fsync(state)
            record['state'] = 'Discarded'
            self.f.save_receipt(state, record['operationId'], record)
            return self.public(record)
