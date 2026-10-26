#!/usr/bin/env python3
"""Container-scoped HDD capture before publication; no CLI or write endpoint.

The caller supplies trusted upload/storage engines and a live ContainerRoot.
Only existing staged overwrite operations can capture their current source.
No container path, host root alias or Docker archive PUT is accepted separately.
"""
import contextlib
import fcntl
import hashlib
import os
import stat


class ContainerBackups:
    def __init__(self, uploads, storage):
        self.u, self.storage = uploads, storage
        self.f, self.r = uploads.f, uploads.r
        if storage.f is not self.f or storage.namespace != 'container-files-v1':
            self.u.reject('BACKUP_ENGINE_MISMATCH')

    @contextlib.contextmanager
    def gate(self, state):
        descriptor = os.open('container-capture.lock', os.O_RDWR | os.O_CREAT | os.O_NOFOLLOW | os.O_CLOEXEC,
                             0o600, dir_fd=state)
        try:
            info = os.fstat(descriptor)
            if not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_nlink != 1 or info.st_size != 0 or stat.S_IMODE(info.st_mode) != 0o600:
                self.u.reject('STATE_UNSAFE')
            try: fcntl.flock(descriptor, fcntl.LOCK_EX | fcntl.LOCK_NB)
            except BlockingIOError: self.u.reject('BUSY')
            yield
        finally: os.close(descriptor)

    def capture(self, request, session):
        self.u.scope_request(request)
        self.u.session_scope(request, session)
        session.revalidate()
        with self.u.locked(request['operationId']) as state, self.gate(state):
            staged = self.u.scoped(state, request)
            self.u.validate_target_proof(staged, session)
            if staged['state'] != 'Staged': self.u.reject('STAGED_REQUIRED')
            self.u.payload(state, staged, complete=True)
            target = staged['request']
            if target['expectedVersion'] == 'ABSENT': self.u.reject('BACKUP_SOURCE_REQUIRED')
            self.u.target(target, session)
            parent_path, _, leaf = target['path'].rpartition('/')
            parent = self.f.open_at(session.root, parent_path or '.', os.O_RDONLY | os.O_DIRECTORY, cross_mount=True)
            try:
                binding = _ContainerSource(self, session, staged, parent, parent_path)
                binding.validate()
                descriptor = self.f.open_at(parent, leaf, os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW, cross_mount=True)
                try:
                    original = self.f.regular(descriptor)
                    if original.st_size > self.r.MAX_BYTES: self.u.reject('SIZE_LIMIT')
                    if binding.version(original) != target['expectedVersion']: self.u.reject('VERSION_CONFLICT')
                    digest, count = hashlib.sha256(), 0
                    while chunk := os.read(descriptor, self.r.CHUNK):
                        binding.validate()
                        count += len(chunk)
                        if count > original.st_size: self.u.reject('VERSION_CONFLICT')
                        digest.update(chunk)
                    if count != original.st_size or binding.version(self.f.regular(descriptor)) != target['expectedVersion']:
                        self.u.reject('VERSION_CONFLICT')
                finally: os.close(descriptor)
                record = {**target, 'targetProof': staged['targetProof'], 'uploadFingerprint': staged['fingerprint'],
                          'originalIdentity': self.f.publication_identity(original), 'originalSha256': digest.hexdigest().upper()}
                # A captured intent is immutable even if an attacker can make a
                # later source object appear at the same visible relative path.
                previous = staged.get('originalCapture')
                intent = {key: record[key] for key in ('originalIdentity', 'originalSha256')}
                if previous is not None and previous != intent: self.u.reject('BACKUP_BINDING_CHANGED')
                if previous is None:
                    staged['originalCapture'] = intent
                    self.f.save_receipt(state, request['operationId'], staged)
                session.revalidate()
                self.u.target(target, session)
                result = self.storage.ensure(record, parent, leaf, source_binding=binding)
                session.revalidate()
                self.u.validate_target_proof(staged, session)
                self.u.target(target, session)
                proof = {**result, 'resourceType': 'ContainerFile', 'containerId': request['containerId'],
                         'instanceId': request['instanceId'], 'targetProof': staged['targetProof']}
                if staged.get('backupCapture') is not None and staged['backupCapture'] != proof:
                    self.u.reject('BACKUP_BINDING_CHANGED')
                staged['backupCapture'] = proof
                self.f.save_receipt(state, request['operationId'], staged)
                return proof
            finally: os.close(parent)

    def verify_capture(self, staged, session):
        """Verify the durable original without reopening a now-published path.

        Called under the upload/namespace locks. This is not another capture,
        nor permission to adopt a host history or another container's original.
        """
        self.u.validate_target_proof(staged, session)
        session.current()
        request = staged['request']; original = staged.get('originalCapture')
        if type(original) is not dict or set(original) != {'originalIdentity', 'originalSha256'}:
            self.u.reject('BACKUP_BINDING_CHANGED')
        identity = original['originalIdentity']
        if type(identity) is not list or len(identity) != 8 or any(type(value) is not int for value in identity) or \
                not stat.S_ISREG(identity[2]) or identity[5] != 1 or not 0 <= identity[6] <= self.r.MAX_BYTES:
            self.u.reject('BACKUP_BINDING_CHANGED')
        expected = {'schemaVersion': 2, 'resourceType': 'ContainerFile', 'storageId': 'database-platform.backup.hdd',
            'operationId': staged['operationId'], 'owner': staged['owner'], 'containerId': staged['containerId'],
            'instanceId': staged['instanceId'], 'path': request['path'], 'sourceVersion': request['expectedVersion'],
            'sourceParentVersion': request['expectedParentVersion'], 'targetProof': staged['targetProof'],
            'uploadFingerprint': staged['fingerprint'], 'sourceIdentity': identity, 'sizeBytes': identity[6],
            'sha256': original['originalSha256']}
        with self.storage.target(create_namespace=False) as (_, target):
            saved = self.storage.read_json(target, staged['operationId'] + '.json')
            if any(saved.get(key) != value for key, value in expected.items()) or saved.get('state') != 'Verified' or \
                    saved.get('captureKind') != 'Overwrite' or saved.get('captureState') != 'Available':
                self.u.reject('BACKUP_BINDING_CHANGED')
            self.storage.verify_data(target, staged['operationId'], expected)
            proof = {**self.storage.public(saved), 'resourceType': 'ContainerFile', 'containerId': staged['containerId'],
                'instanceId': staged['instanceId'], 'targetProof': staged['targetProof']}
            if proof != staged.get('backupCapture'): self.u.reject('BACKUP_BINDING_CHANGED')
        session.current()
        return proof


class _ContainerSource:
    def __init__(self, engine, session, staged, parent, parent_path):
        self.engine, self.session, self.staged = engine, session, staged
        self.parent, self.parent_path = parent, parent_path

    def version(self, info):
        request = self.staged['request']
        return self.engine.r.version(request, request['path'], info)

    def validate(self):
        self.session.current()
        self.engine.u.validate_target_proof(self.staged, self.session)
        request = self.staged['request']
        if self.engine.r.version(request, self.parent_path, os.fstat(self.parent)) != request['expectedParentVersion']:
            self.engine.u.reject('PARENT_VERSION_CONFLICT')

    def metadata(self, record, original):
        # Distinct schema/resource domain prevents host history and a container
        # capture with a coincident operation ID from adopting each other.
        return {'schemaVersion': 2, 'resourceType': 'ContainerFile', 'storageId': 'database-platform.backup.hdd',
                'operationId': self.engine.f.identity(record['operationId']), 'owner': record['owner'],
                'containerId': record['containerId'], 'instanceId': record['instanceId'], 'path': record['path'],
                'sourceVersion': record['expectedVersion'], 'sourceParentVersion': record['expectedParentVersion'],
                'targetProof': record['targetProof'], 'uploadFingerprint': record['uploadFingerprint'],
                'sourceIdentity': self.engine.f.publication_identity(original), 'sizeBytes': original.st_size,
                'sha256': record['originalSha256']}
