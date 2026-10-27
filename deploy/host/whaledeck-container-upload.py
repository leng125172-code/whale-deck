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

    def recovery_engine(self): return ContainerUploadRecovery(self)

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
        if type(request) is not dict or set(request) not in (required, required | {'expectedTargetProof'}):
            self.reject('REQUEST_INVALID')
        if 'expectedTargetProof' in request and (type(request['expectedTargetProof']) is not str or not re.fullmatch('[A-F0-9]{64}', request['expectedTargetProof'])):
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
        try: info = os.stat(operation + '.json', dir_fd=state, follow_symlinks=False)
        except FileNotFoundError: return None
        if not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_nlink != 1 or stat.S_IMODE(info.st_mode) != 0o600:
            self.reject('RECEIPT_INVALID')
        value = self.f.read_receipt(state, operation)
        return self.validate_receipt(value, operation)

    def validate_receipt(self, value, operation):
        if value is None: return None
        if type(value) is dict and value.get('schemaVersion') == 2:
            expected = {'schemaVersion', 'resourceType', 'state', 'operationId', 'owner', 'containerId', 'instanceId', 'targetProof', 'createdAtUnixMs'}
            if set(value) != expected or type(value['schemaVersion']) is not int or value['resourceType'] != 'ContainerUploadCancellation' or value['state'] != 'Prevented':
                self.reject('RECEIPT_INVALID')
            self.scope_request({key: value[key] for key in ('operationId', 'owner', 'containerId', 'instanceId')})
            if value['operationId'] != operation or type(value['targetProof']) is not str or not re.fullmatch('[A-F0-9]{64}', value['targetProof']) or \
                    type(value['createdAtUnixMs']) is not int or not 0 <= value['createdAtUnixMs'] <= 253402300799999:
                self.reject('RECEIPT_INVALID')
            return value
        if type(value) is not dict or type(value.get('schemaVersion')) is not int or value['schemaVersion'] != 1:
            self.reject('RECEIPT_INVALID')
        request = value.get('request')
        if self.upload_request(request) != value.get('fingerprint') or any(value.get(key) != request[key]
                for key in ('operationId', 'owner', 'containerId', 'instanceId')) or value['operationId'] != operation:
            self.reject('RECEIPT_INVALID')
        if value.get('state') not in ('Receiving', 'Verified', 'Staged', 'Interrupted', 'Aborted', 'Discarded', 'Unknown',
                'PublishPreparing', 'PublishApplying', 'PublishInterrupted', 'Published', 'PublishDiscarding', 'PublishDiscarded'):
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
        if record['state'] == 'Prevented':
            return {'operationId': record['operationId'], 'containerId': record['containerId'], 'instanceId': record['instanceId'],
                'state': 'Prevented', 'targetProof': record['targetProof'], 'sizeBytes': 0,
                'sha256': hashlib.sha256(b'').hexdigest().upper(), 'replayed': replayed, 'backupCapture': None}
        return {'operationId': record['operationId'], 'containerId': record['containerId'],
                'instanceId': record['instanceId'], 'state': record['state'],
                'targetProof': record['targetProof'],
                'sizeBytes': record['request']['sizeBytes'], 'sha256': record['request']['sha256'], 'replayed': replayed,
                'backupCapture': record.get('backupCapture')}

    def prevent_missing(self, request, session, expected_proof):
        """Fence a delayed first sender; never fabricate a staged byte receipt."""
        self.scope_request(request); self.session_scope(request, session); session.revalidate()
        if self.target_proof(session) != expected_proof: self.reject('TARGET_CHANGED')
        with self.locked(request['operationId'], allow_new=True) as state:
            record = self.receipt(state, request['operationId'])
            if record is not None:
                record = self.scoped(state, request); self.validate_target_proof(record, session)
                return self.public(record, True) if record['state'] == 'Prevented' else None
            # This shares the exact operation lock with stage, including after
            # a process crash. If stage won the lock, normal discard must prove
            # and remove that payload; missing data is never called discarded.
            record = {'schemaVersion': 2, 'resourceType': 'ContainerUploadCancellation', 'state': 'Prevented',
                **request, 'targetProof': expected_proof, 'createdAtUnixMs': time.time_ns() // 1000000}
            session.revalidate(); self.validate_target_proof(record, session)
            self.f.save_receipt(state, request['operationId'], record)
            return self.public(record)

    def stage(self, request, session, source):
        fingerprint = self.upload_request(request)
        self.session_scope(request, session)
        session.revalidate()
        proof = self.target_proof(session)
        if request.get('expectedTargetProof', proof) != proof: self.reject('TARGET_CHANGED')
        self.target(request, session)
        operation = request['operationId']
        with self.locked(operation, allow_new=True) as state, self.stream_slot(state):
            record = self.receipt(state, operation)
            descriptor = None
            created = False
            try:
                if record is not None:
                    if record['state'] == 'Prevented':
                        self.scoped(state, {key: request[key] for key in ('operationId', 'owner', 'containerId', 'instanceId')})
                        self.validate_target_proof(record, session)
                        self.reject('CANCELED')
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
            if record['state'] == 'Prevented': return self.public(record, True)
            if 'publication' in record: self.reject('PUBLISHER_REQUIRED')
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
            if record['state'] == 'Prevented': return self.public(record, True)
            if 'publication' in record: self.reject('PUBLISHER_REQUIRED')
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


class ContainerUploadRecovery:
    """Internal receipt-only observation and irreversible late-writer sealing.

    No Docker/process/target path access and no CLI surface. The business layer
    must separately validate current administrator authority, a permanent plan
    and the original immutable intent. A seal never proves publication success
    or deletes any payload, HDD capture or displaced object.
    """
    DOMAIN = b'whaledeck-container-upload-recovery-v1\n'
    SCOPE = frozenset(('containerId', 'instanceId', 'operationId', 'owner', 'expectedTargetProof'))
    SEAL = frozenset(('schemaVersion', 'resourceType', 'state', 'containerId', 'instanceId', 'operationId',
        'owner', 'targetProof', 'originalRecord', 'originalRecordHash', 'recoveryId', 'actorHash', 'traceHash',
        'requestHash', 'expectedObservationHash', 'sealedAtUnixMs'))

    def __init__(self, uploads): self.u, self.f = uploads, uploads.f

    @classmethod
    def digest(cls, name, value):
        return hashlib.sha256(cls.DOMAIN + name.encode('ascii') + b'\n' +
            json.dumps(value, sort_keys=True, separators=(',', ':')).encode()).hexdigest().upper()

    def scope(self, request):
        if type(request) is not dict or set(request) != self.SCOPE: self.u.reject('RECOVERY_REQUEST_INVALID')
        scope = {key: request[key] for key in ('containerId', 'instanceId', 'operationId', 'owner')}
        self.u.scope_request(scope)
        if type(request['expectedTargetProof']) is not str or not re.fullmatch('[A-F0-9]{64}', request['expectedTargetProof']):
            self.u.reject('RECOVERY_REQUEST_INVALID')
        return scope

    def read(self, state, scope, expected_proof):
        operation = scope['operationId']
        # Preserve the same strict owner/mode/link checks as the normal reader.
        try: info = os.stat(operation + '.json', dir_fd=state, follow_symlinks=False)
        except FileNotFoundError: return None, None
        if not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_nlink != 1 or stat.S_IMODE(info.st_mode) != 0o600:
            self.u.reject('RECEIPT_INVALID')
        value = self.f.read_receipt(state, operation)
        if value is None: self.u.reject('RECEIPT_INVALID')
        seal = value if type(value) is dict and value.get('schemaVersion') == 3 else None
        if seal is not None:
            if set(seal) != self.SEAL or type(seal['schemaVersion']) is not int or seal['resourceType'] != 'ContainerUploadRecoverySeal' or seal['state'] != 'Sealed':
                self.u.reject('RECOVERY_SEAL_INVALID')
            self.f.identity(seal['recoveryId'])
            if seal['recoveryId'] == operation: self.u.reject('RECOVERY_SEAL_INVALID')
            for key in ('originalRecordHash', 'actorHash', 'traceHash', 'requestHash', 'expectedObservationHash'):
                if type(seal[key]) is not str or not re.fullmatch('[A-F0-9]{64}', seal[key]): self.u.reject('RECOVERY_SEAL_INVALID')
            if type(seal['sealedAtUnixMs']) is not int or not 0 <= seal['sealedAtUnixMs'] <= 253402300799999:
                self.u.reject('RECOVERY_SEAL_INVALID')
            if self.digest('original', seal['originalRecord']) != seal['originalRecordHash']:
                self.u.reject('RECOVERY_SEAL_INVALID')
            value = seal['originalRecord']
        record = self.u.validate_receipt(value, operation)
        for bound in (record, seal):
            if bound is not None and (any(bound.get(key) != scope[key] for key in self.SCOPE - {'expectedTargetProof'}) or bound.get('targetProof') != expected_proof):
                self.u.reject('RECOVERY_SCOPE_CHANGED')
        if seal is not None:
            before = self.public(scope, expected_proof, record, None)
            if before['observationHash'] != seal['expectedObservationHash']:
                self.u.reject('RECOVERY_SEAL_INVALID')
            material = {key: seal[key] for key in ('recoveryId', 'actorHash', 'traceHash', 'expectedObservationHash')}
            if self.digest('seal-request', {**scope, 'expectedTargetProof': expected_proof, **material}) != seal['requestHash']:
                self.u.reject('RECOVERY_SEAL_INVALID')
        return record, seal

    def public(self, scope, expected_proof, record, seal):
        # These are retained journal facts, never a current target/version proof.
        facts = {'containerId': scope['containerId'], 'instanceId': scope['instanceId'], 'operationId': scope['operationId'],
            'targetProof': expected_proof, 'rootState': 'Missing' if record is None else record['state'],
            'originalRecordHash': self.digest('original', record), 'sealed': seal is not None,
            'sealId': None if seal is None else seal['recoveryId'], 'bindingCurrent': False,
            'mayHavePublished': record is not None and ('publication' in record or record['state'] == 'Unknown'),
            'retainedObjects': record is not None and record['state'] != 'Prevented'}
        # The opaque owner stays inside the digest; it never enters public output.
        return {**facts, 'observationHash': self.digest('observation', {**facts, 'owner': scope['owner']})}

    def observe(self, request):
        scope = self.scope(request)
        with self.u.locked(scope['operationId'], allow_new=True) as state:
            record, seal = self.read(state, scope, request['expectedTargetProof'])
            return self.public(scope, request['expectedTargetProof'], record, seal)

    def seal(self, request, recovery_id, actor, trace, expected_observation):
        scope = self.scope(request); self.f.identity(recovery_id)
        if recovery_id == scope['operationId']: self.u.reject('RECOVERY_REQUEST_INVALID')
        for value, maximum in ((actor, 256), (trace, 128)):
            if type(value) is not str or not value.strip() or len(value) > maximum or any(ord(c) < 32 or 127 <= ord(c) < 160 for c in value):
                self.u.reject('RECOVERY_ACTOR_INVALID')
            try: value.encode('utf-8', 'strict')
            except UnicodeError: self.u.reject('RECOVERY_ACTOR_INVALID')
        if type(expected_observation) is not str or not re.fullmatch('[A-F0-9]{64}', expected_observation):
            self.u.reject('RECOVERY_REQUEST_INVALID')
        material = {'recoveryId': recovery_id, 'actorHash': self.digest('actor', actor), 'traceHash': self.digest('trace', trace),
            'expectedObservationHash': expected_observation}
        request_hash = self.digest('seal-request', {**request, **material})
        with self.u.locked(scope['operationId'], allow_new=True) as state:
            record, previous = self.read(state, scope, request['expectedTargetProof'])
            if previous is not None:
                if previous['requestHash'] != request_hash: self.u.reject('RECOVERY_IDEMPOTENCY_CONFLICT')
                return self.public(scope, request['expectedTargetProof'], record, previous)
            observation = self.public(scope, request['expectedTargetProof'], record, None)
            if observation['observationHash'] != expected_observation: self.u.reject('RECOVERY_OBSERVATION_CHANGED')
            value = {'schemaVersion': 3, 'resourceType': 'ContainerUploadRecoverySeal', 'state': 'Sealed', **scope,
                'targetProof': request['expectedTargetProof'], 'originalRecord': record,
                'originalRecordHash': observation['originalRecordHash'], **material, 'requestHash': request_hash,
                'sealedAtUnixMs': time.time_ns() // 1000000}
            # Match read_receipt's bound exactly, including escaped Unicode.
            if len(json.dumps(value, separators=(',', ':')).encode()) > 16384: self.u.reject('RECOVERY_RECORD_LIMIT')
            # Existing minor39/40 readers only admit schemas1/2. Replacing the
            # original journal under its lock therefore fences their delayed
            # stage/publish/capture calls too, without relying on a new sidecar
            # that an old in-memory writer would not know to check.
            self.f.save_receipt(state, scope['operationId'], value)
            return self.public(scope, request['expectedTargetProof'], record, value)
