#!/usr/bin/env python3
"""Private container directory-creation engine, not a helper or RPC entry point.

The future authenticated workflow must freeze this plan in a permanent intent
and acquire the shared publication barrier before Apply. This engine accepts
only an already pinned ContainerRoot, never a host path, command or chosen UID.
No overwrite, recursive creation, shell, Docker Exec or automatic write replay.
"""
import contextlib
import fcntl
import hashlib
import json
import os
from pathlib import Path
import re
import stat


class ContainerDirectories:
    COMMAND = {'containerId', 'instanceId', 'path', 'mode'}
    PLAN = {'schemaVersion', 'command', 'targetProof', 'parentIdentity', 'parentVersion', 'uid', 'gid', 'fingerprint'}
    REQUEST = {'operationId', 'owner', 'plan'}
    TERMINAL = ('Completed', 'Aborted', 'Unknown')
    PRIVATE = '.whaledeck-container-directories-v1'
    LIMIT = 16384

    def __init__(self, uploads, publisher, state_path):
        if publisher.u is not uploads:
            raise ValueError('CONTAINER_DIRECTORY_ENGINE_MISMATCH')
        self.u, self.p, self.r, self.f = uploads, publisher, uploads.r, uploads.f
        self.state_path = Path(state_path)

    def reject(self, suffix):
        raise self.r.Rejected('CONTAINER_DIRECTORY_' + suffix)

    @staticmethod
    def digest(plan):
        unsigned = {key: value for key, value in plan.items() if key != 'fingerprint'}
        return hashlib.sha256(b'whaledeck-container-directory-plan-v1\n' + json.dumps(unsigned,
            sort_keys=True, separators=(',', ':'), ensure_ascii=False).encode()).hexdigest().upper()

    def command(self, value):
        if type(value) is not dict or set(value) != self.COMMAND:
            self.reject('REQUEST_INVALID')
        self.r.target_binding(value['containerId'], value['instanceId'])
        self.r.canonical_path(value['path'], empty=False)
        if self.r.protected(value['path'], self.f): self.reject('PROTECTED')
        if type(value['mode']) is not str or not re.fullmatch('0[0-7]{3}', value['mode']):
            self.reject('MODE_INVALID')

    def validate_plan(self, plan):
        if type(plan) is not dict or set(plan) != self.PLAN or type(plan['schemaVersion']) is not int or plan['schemaVersion'] != 1:
            self.reject('PLAN_INVALID')
        self.command(plan['command'])
        if any(type(plan[key]) is not str or not re.fullmatch('[A-F0-9]{64}', plan[key])
               for key in ('targetProof', 'parentVersion', 'fingerprint')):
            self.reject('PLAN_INVALID')
        parent = plan['parentIdentity']
        if type(parent) is not list or len(parent) != 2 or any(type(n) is not int or n < 0 or n > 18446744073709551615 for n in parent) or parent[1] == 0:
            self.reject('PLAN_INVALID')
        if any(type(plan[key]) is not int or not 0 <= plan[key] < 4294967295 for key in ('uid', 'gid')):
            self.reject('PLAN_INVALID')
        if plan['fingerprint'] != self.digest(plan): self.reject('PLAN_INVALID')

    def request(self, value):
        if type(value) is not dict or set(value) != self.REQUEST: self.reject('REQUEST_INVALID')
        self.f.identity(value['operationId'])
        if type(value['owner']) is not str or not re.fullmatch('[A-F0-9]{64}', value['owner']): self.reject('REQUEST_INVALID')
        self.validate_plan(value['plan'])

    def guard(self, command, session, expected_proof=None):
        self.u.session_scope(command, session)
        session.revalidate()
        proof = self.u.target_proof(session)
        if expected_proof is not None and proof != expected_proof: self.reject('TARGET_CHANGED')
        return proof

    @contextlib.contextmanager
    def parent(self, plan, session):
        command = plan['command']
        self.guard(command, session, plan.get('targetProof'))
        path = command['path'].rpartition('/')[0]
        descriptor = self.f.open_at(session.root, path or '.', os.O_RDONLY | os.O_DIRECTORY, cross_mount=True)
        try:
            info = os.fstat(descriptor)
            if 'parentIdentity' in plan and [info.st_dev, info.st_ino] != plan['parentIdentity']:
                self.reject('PARENT_CHANGED')
            self.p.writable({'request': command}, session, descriptor)
            yield descriptor
        finally: os.close(descriptor)

    def plan(self, command, session):
        self.command(command)
        proof = self.guard(command, session)
        with self.parent({'command': command, 'targetProof': proof}, session) as parent:
            leaf = command['path'].rsplit('/', 1)[-1]
            if self.present(parent, leaf): self.reject('TARGET_EXISTS')
            uid, gid = self.p.owner(session)
            info = os.fstat(parent)
            result = {'schemaVersion': 1, 'command': dict(command), 'targetProof': proof,
                'parentIdentity': [info.st_dev, info.st_ino],
                'parentVersion': self.r.version(command, command['path'].rpartition('/')[0], info), 'uid': uid, 'gid': gid}
            self.guard(command, session, proof)
            if (uid, gid) != tuple(self.p.owner(session)): self.reject('OWNER_CHANGED')
            result['fingerprint'] = self.digest(result)
            return result

    @staticmethod
    def present(parent, name):
        try: os.stat(name, dir_fd=parent, follow_symlinks=False); return True
        except FileNotFoundError: return False

    @contextlib.contextmanager
    def locked(self, request):
        self.request(request)
        if not self.state_path.is_absolute() or self.state_path != self.state_path.resolve(strict=True): self.reject('STATE_UNSAFE')
        state = self.f.trusted_directory(self.state_path); lock = None
        try:
            if stat.S_IMODE(os.fstat(state).st_mode) != 0o700: self.reject('STATE_UNSAFE')
            operation = request['operationId']
            if len(os.listdir(state)) >= 20000 and not self.present(state, operation + '.json'): self.reject('RECEIPT_LIMIT')
            lock = os.open(operation + '.lock', os.O_RDWR | os.O_CREAT | os.O_NOFOLLOW | os.O_NONBLOCK | os.O_CLOEXEC, 0o600, dir_fd=state)
            value = os.fstat(lock)
            if not stat.S_ISREG(value.st_mode) or value.st_uid != 0 or value.st_nlink != 1 or stat.S_IMODE(value.st_mode) != 0o600 or value.st_size != 0:
                self.reject('STATE_UNSAFE')
            try: fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
            except BlockingIOError: self.reject('BUSY')
            record = self.read(state, operation)
            if record is not None:
                if record['owner'] != request['owner']: self.reject('NOT_FOUND')
                if record['plan'] != request['plan']: self.reject('IDEMPOTENCY_CONFLICT')
            yield state, record
        finally:
            if lock is not None: os.close(lock)
            os.close(state)

    def read(self, state, operation):
        try: descriptor = os.open(operation + '.json', os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK | os.O_CLOEXEC, dir_fd=state)
        except FileNotFoundError: return None
        try:
            value = os.fstat(descriptor)
            if not stat.S_ISREG(value.st_mode) or value.st_uid != 0 or value.st_nlink != 1 or stat.S_IMODE(value.st_mode) != 0o600 or not 0 < value.st_size <= self.LIMIT:
                self.reject('RECEIPT_INVALID')
            raw = os.read(descriptor, self.LIMIT + 1)
            if len(raw) != value.st_size: self.reject('RECEIPT_INVALID')
            record = json.loads(raw, object_pairs_hook=self.f.unique_object)
            self.validate_record(record, operation)
            return record
        finally: os.close(descriptor)

    def validate_record(self, record, operation):
        fields = {'schemaVersion', 'operationId', 'owner', 'plan', 'state', 'version', 'privateIdentity', 'pendingIdentity', 'pendingMetadata'}
        if type(record) is not dict or set(record) != fields or type(record['schemaVersion']) is not int or record['schemaVersion'] != 1:
            self.reject('RECEIPT_INVALID')
        self.request({key: record[key] for key in self.REQUEST})
        if record['operationId'] != operation or record['state'] not in (*self.TERMINAL, 'Preparing', 'Prepared', 'Applying'):
            self.reject('RECEIPT_INVALID')
        for key in ('privateIdentity', 'pendingIdentity'):
            value = record[key]
            if value is not None and (type(value) is not list or len(value) != 2 or any(type(n) is not int or not 0 <= n <= 18446744073709551615 for n in value) or value[1] == 0):
                self.reject('RECEIPT_INVALID')
        metadata = record['pendingMetadata']
        if metadata is not None and (type(metadata) is not list or len(metadata) != 8 or any(type(n) is not int for n in metadata) or
                metadata[:2] != record['pendingIdentity'] or not stat.S_ISDIR(metadata[2]) or metadata[3:5] != [record['plan']['uid'], record['plan']['gid']] or
                stat.S_IMODE(metadata[2]) != int(record['plan']['command']['mode'], 8)):
            self.reject('RECEIPT_INVALID')
        if record['state'] in ('Prepared', 'Applying', 'Completed') and (metadata is None or record['privateIdentity'] is None): self.reject('RECEIPT_INVALID')
        if record['state'] == 'Completed':
            if type(record['version']) is not str or not re.fullmatch('[A-F0-9]{64}', record['version']): self.reject('RECEIPT_INVALID')
        elif record['version'] != '': self.reject('RECEIPT_INVALID')

    def save(self, state, record):
        self.validate_record(record, record['operationId'])
        if len(json.dumps(record).encode()) > self.LIMIT: self.reject('RECEIPT_LIMIT')
        self.f.save_receipt(state, record['operationId'], record)

    def new_record(self, request, phase):
        return {'schemaVersion': 1, **request, 'state': phase, 'version': '',
            'privateIdentity': None, 'pendingIdentity': None, 'pendingMetadata': None}

    def private(self, parent, record, create=False):
        if create:
            try: os.mkdir(self.PRIVATE, 0o700, dir_fd=parent); os.fsync(parent)
            except FileExistsError: pass
        descriptor = self.f.open_at(parent, self.PRIVATE, os.O_RDONLY | os.O_DIRECTORY)
        try:
            info = os.fstat(descriptor)
            if info.st_uid != 0 or stat.S_IMODE(info.st_mode) != 0o700: self.reject('PRIVATE_UNSAFE')
            identity = [info.st_dev, info.st_ino]
            if record['privateIdentity'] is not None and record['privateIdentity'] != identity: self.reject('PRIVATE_CHANGED')
            return descriptor
        except BaseException: os.close(descriptor); raise

    def apply(self, request, session):
        with self.locked(request) as (state, record):
            if record is not None:
                self.recover(state, record, session)
                return self.public(record, session, True)
            plan = request['plan']; command = plan['command']
            if self.plan(command, session) != plan: self.reject('VERSION_CONFLICT')
            record = self.new_record(request, 'Preparing'); self.save(state, record)
            try:
                with self.parent(plan, session) as parent:
                    private = self.private(parent, record, create=True)
                    try:
                        value = os.fstat(private); record['privateIdentity'] = [value.st_dev, value.st_ino]; self.save(state, record)
                        temporary = request['operationId'] + '.directory'
                        os.mkdir(temporary, 0o700, dir_fd=private)
                        descriptor = self.f.open_at(private, temporary, os.O_RDONLY | os.O_DIRECTORY)
                        try:
                            value = os.fstat(descriptor); record['pendingIdentity'] = [value.st_dev, value.st_ino]; self.save(state, record)
                            os.fchown(descriptor, plan['uid'], plan['gid']); os.fchmod(descriptor, int(command['mode'], 8))
                            os.fsync(descriptor); os.fsync(private)
                            record['pendingMetadata'] = self.f.publication_identity(os.fstat(descriptor))
                        finally: os.close(descriptor)
                        record['state'] = 'Prepared'; self.save(state, record)
                        # Fresh path resolution proves that a renamed/replaced
                        # parent is not the directory selected by the user.
                        with self.parent(plan, session): pass
                        if tuple(self.p.owner(session)) != (plan['uid'], plan['gid']): self.reject('OWNER_CHANGED')
                        self.guard(command, session, plan['targetProof'])
                        verified_private = self.private(parent, record)
                        try:
                            pending = self.probe(verified_private, temporary)
                            if pending is None or not pending[1] or self.f.publication_identity(pending[0]) != record['pendingMetadata']:
                                self.reject('PRIVATE_CHANGED')
                        finally: os.close(verified_private)
                        record['state'] = 'Applying'; self.save(state, record)
                        self.f.exchange(private, temporary, parent, command['path'].rsplit('/', 1)[-1], 1)
                        os.fsync(parent); os.fsync(private)
                    finally: os.close(private)
                self.recover(state, record, session)
            except BaseException:
                # Observation/owned empty-private cleanup only. Never repeat
                # rename or remove a published/foreign/nonempty directory.
                with contextlib.suppress(OSError, self.r.Rejected, self.f.Rejected): self.recover(state, record, session)
                raise
            return self.public(record, session, False)

    def probe(self, parent, name):
        try: descriptor = self.f.open_at(parent, name, os.O_RDONLY | os.O_DIRECTORY | os.O_NONBLOCK | os.O_NOFOLLOW)
        except FileNotFoundError: return None
        try: return os.fstat(descriptor), not os.listdir(descriptor)
        finally: os.close(descriptor)

    def recover(self, state, record, session):
        if record['state'] in self.TERMINAL: return
        plan = record['plan']; command = plan['command']; phase = record['state']
        try:
            with self.parent(plan, session) as parent:
                temporary = record['operationId'] + '.directory'
                try: private = self.private(parent, record)
                except FileNotFoundError:
                    if phase == 'Preparing' and record['privateIdentity'] is None and record['pendingIdentity'] is None:
                        record['state'] = 'Aborted'
                    else: record['state'] = 'Unknown'
                else:
                    try:
                        pending = self.probe(private, temporary)
                        actual = self.probe(parent, command['path'].rsplit('/', 1)[-1])
                        if phase == 'Applying' and pending is None and actual is not None and self.f.publication_identity(actual[0]) == record['pendingMetadata']:
                            record.update(state='Completed', version=self.r.version(command, command['path'], actual[0]))
                        elif pending is not None and [pending[0].st_dev, pending[0].st_ino] == record['pendingIdentity'] and pending[1] and \
                                (record['pendingMetadata'] is None or self.f.publication_identity(pending[0]) == record['pendingMetadata']):
                            # Only the private inode we positively recorded and
                            # proved empty is disposable. Never recurse/unlink.
                            os.rmdir(temporary, dir_fd=private); os.fsync(private); record['state'] = 'Aborted'
                        elif phase == 'Preparing' and record['pendingIdentity'] is None and pending is None:
                            record['state'] = 'Aborted'
                        else: record['state'] = 'Unknown'
                    finally: os.close(private)
        except (OSError, self.r.Rejected, self.f.Rejected): record['state'] = 'Unknown'
        self.save(state, record)

    def inspect(self, request, session):
        with self.locked(request) as (state, record):
            if record is None: self.reject('NOT_FOUND')
            self.recover(state, record, session)
            return self.public(record, session, True)

    def discard(self, request, session=None):
        with self.locked(request) as (state, record):
            if record is None:
                record = self.new_record(request, 'Aborted'); self.save(state, record)
            elif record['state'] not in self.TERMINAL:
                if session is None:
                    record['state'] = 'Unknown'; self.save(state, record)
                else: self.recover(state, record, session)
            return self.public(record, session, True)

    def public(self, record, session, replayed):
        binding = False; plan = record['plan']; command = plan['command']
        if record['state'] == 'Completed' and session is not None:
            try:
                with self.parent(plan, session) as parent:
                    actual = self.probe(parent, command['path'].rsplit('/', 1)[-1])
                    binding = actual is not None and self.r.version(command, command['path'], actual[0]) == record['version']
            except (OSError, self.r.Rejected, self.f.Rejected): pass
        return {'operationId': record['operationId'], 'containerId': command['containerId'], 'instanceId': command['instanceId'],
            'fingerprint': plan['fingerprint'], 'targetProof': plan['targetProof'], 'state': record['state'],
            'version': record['version'], 'bindingCurrent': binding, 'replayed': replayed}
