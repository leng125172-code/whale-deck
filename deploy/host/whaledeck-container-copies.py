#!/usr/bin/env python3
"""Container-domain durable copy plans/receipts, no transport entry point.

An authenticated Agent caller and the business publication barrier are required
before exposing this engine. Inner host-engine plans and versions never become
public container identities. Recovery only observes or fences a missing inner
operation; it never invokes copy/apply again or removes a source.
"""
import contextlib
import fcntl
import hashlib
import json
import os
from pathlib import Path
import re
import stat


class ContainerCopies:
    COMMAND = {'containerId', 'instanceId', 'sourcePath', 'targetPath', 'expectedVersion'}
    PLAN = {'schemaVersion', 'command', 'kind', 'targetProof', 'sourceIdentity', 'sourceParent',
            'targetParent', 'targetParentVersion', 'uid', 'gid', 'innerPlan', 'fingerprint'}
    REQUEST = {'operationId', 'owner', 'plan'}
    TERMINAL = ('Completed', 'Aborted', 'Unknown')
    RECORD = {'schemaVersion', 'operationId', 'owner', 'plan', 'state', 'version',
              'publicationIdentity', 'innerState', 'innerReceiptHash', 'privateMayExist'}
    LIMIT = 32768

    def __init__(self, uploads, publisher, scope_type, file_type, tree_type, manifest_type, state_path):
        if publisher.u is not uploads: raise ValueError('CONTAINER_COPY_ENGINE_MISMATCH')
        self.u, self.p, self.r, self.f = uploads, publisher, uploads.r, uploads.f
        self.scope_type, self.file_type, self.tree_type, self.manifest_type = scope_type, file_type, tree_type, manifest_type
        self.state_path = Path(state_path)

    def reject(self, suffix): raise self.f.Rejected('CONTAINER_COPY_' + suffix)

    @staticmethod
    def encode(value): return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(',', ':'), allow_nan=False).encode()

    @classmethod
    def digest(cls, value):
        return hashlib.sha256(b'whaledeck-container-copy-plan-v1\n' + cls.encode({k: v for k, v in value.items() if k != 'fingerprint'})).hexdigest().upper()

    @staticmethod
    def hexadecimal(value): return type(value) is str and re.fullmatch('[A-F0-9]{64}', value) is not None

    @staticmethod
    def inode_value(value):
        return type(value) is list and len(value) == 2 and all(type(n) is int and 0 <= n <= 18446744073709551615 for n in value) and value[1] > 0

    def metadata(self, value, kind, length=9):
        if type(value) is not list or len(value) != length or any(type(n) is not int for n in value): self.reject('PLAN_INVALID')
        if not self.inode_value(value[:2]) or not 0 <= value[2] <= 0o177777 or value[2] & 0o7000 or \
                any(not 0 <= n < 4294967295 for n in value[3:5]) or not 1 <= value[5] <= 18446744073709551615 or \
                not 0 <= value[6] <= 18446744073709551615 or any(not -9223372036854775808 <= n <= 9223372036854775807 for n in value[7:]):
            self.reject('PLAN_INVALID')
        if kind == 'File' and (not stat.S_ISREG(value[2]) or value[5] != 1 or value[6] > self.f.MAX_BYTES) or \
                kind == 'Directory' and not stat.S_ISDIR(value[2]): self.reject('PLAN_INVALID')

    def command(self, value):
        if type(value) is not dict or set(value) != self.COMMAND: self.reject('REQUEST_INVALID')
        self.r.target_binding(value['containerId'], value['instanceId'])
        for key in ('sourcePath', 'targetPath'):
            self.r.canonical_path(value[key], empty=False)
            if self.r.protected(value[key], self.f): self.reject('PROTECTED')
        source, target = value['sourcePath'], value['targetPath']
        if source == target or source.startswith(target + '/') or target.startswith(source + '/'): self.reject('OVERLAP')
        if not self.hexadecimal(value['expectedVersion']): self.reject('VERSION_INVALID')

    @staticmethod
    def host_version(identity):
        return hashlib.sha256(json.dumps(identity, separators=(',', ':')).encode()).hexdigest().upper()

    @staticmethod
    def container_version(command, path, identity):
        return hashlib.sha256(b'whaledeck-container-file-v1\n' + json.dumps(
            [command['containerId'], command['instanceId'], path, *identity], ensure_ascii=False,
            separators=(',', ':'), allow_nan=False).encode()).hexdigest().upper()

    def inner_command(self, plan):
        command = plan['command']; expected = self.host_version(plan['sourceIdentity'])
        if plan['kind'] == 'File':
            return {'kind': 'copy', 'sourceRootId': self.scope_type.SOURCE, 'sourcePath': command['sourcePath'],
                'targetRootId': self.scope_type.TARGET, 'targetPath': command['targetPath'], 'expectedVersion': expected, 'mode': ''}
        return {'source': {'rootId': self.scope_type.SOURCE, 'path': command['sourcePath'], 'expectedVersion': expected},
                'target': {'rootId': self.scope_type.TARGET, 'path': command['targetPath']}}

    def validate_plan(self, plan):
        if type(plan) is not dict or set(plan) != self.PLAN or type(plan['schemaVersion']) is not int or plan['schemaVersion'] != 1:
            self.reject('PLAN_INVALID')
        self.command(plan['command'])
        if type(plan['kind']) is not str or plan['kind'] not in ('File', 'Directory'): self.reject('PLAN_INVALID')
        self.metadata(plan['sourceIdentity'], plan['kind'])
        if any(not self.hexadecimal(plan[k]) for k in ('targetProof', 'targetParentVersion', 'fingerprint')) or \
                any(type(plan[k]) is not int or not 0 <= plan[k] < 4294967295 for k in ('uid', 'gid')) or \
                any(not self.inode_value(plan[k]) for k in ('sourceParent', 'targetParent')):
            self.reject('PLAN_INVALID')
        if self.container_version(plan['command'], plan['command']['sourcePath'], plan['sourceIdentity']) != plan['command']['expectedVersion']:
            self.reject('PLAN_INVALID')
        inner = plan['innerPlan']
        if type(inner) is not dict or inner.get('command') != self.inner_command(plan) or inner.get('targetParent') != plan['targetParent']:
            self.reject('PLAN_INVALID')
        if plan['kind'] == 'File':
            if set(inner) != self.file_type.PLAN_FIELDS or inner['sourceIdentity'] != plan['sourceIdentity'][:8] or \
                    inner['sourceParent'] != plan['sourceParent'] or type(inner['sizeBytes']) is not int or inner['sizeBytes'] != plan['sourceIdentity'][6] or \
                    not self.hexadecimal(inner['sha256']) or not self.hexadecimal(inner['fingerprint']): self.reject('PLAN_INVALID')
            self.metadata(inner['sourceIdentity'], 'File', 8)
            if not self.inode_value(inner['sourceParent']) or not self.inode_value(inner['targetParent']): self.reject('PLAN_INVALID')
            unsigned = {k: v for k, v in inner.items() if k != 'fingerprint'}
            digest = hashlib.sha256(b'whaledeck-file-operation-v1:' + json.dumps(unsigned, sort_keys=True, separators=(',', ':')).encode()).hexdigest().upper()
            if digest != inner['fingerprint']: self.reject('PLAN_INVALID')
        else:
            self.tree_type(self.f, self.manifest_type).validate_plan(inner)
            common = {'path': '/container', 'containerId': plan['command']['containerId'], 'instanceId': plan['command']['instanceId'],
                      'targetProof': plan['targetProof'], 'ownerUid': plan['uid'], 'ownerGid': plan['gid']}
            for field, actions in (('sourcePolicyHash', ['files-list', 'files-download', 'files-tree-copy']),
                    ('targetPolicyHash', ['files-mutate', 'files-upload', 'files-tree-copy'])):
                policy = {**common, 'allowedActions': actions}
                if inner[field] != self.tree_type(self.f, self.manifest_type).digest(policy): self.reject('PLAN_INVALID')
        if len(self.encode(plan)) > self.LIMIT or self.digest(plan) != plan['fingerprint']: self.reject('PLAN_INVALID')

    def request(self, value):
        if type(value) is not dict or set(value) != self.REQUEST: self.reject('REQUEST_INVALID')
        self.f.identity(value['operationId'])
        if not self.hexadecimal(value['owner']): self.reject('REQUEST_INVALID')
        self.validate_plan(value['plan'])

    def engine(self, scope, kind):
        return self.file_type(scope) if kind == 'File' else self.tree_type(scope, self.manifest_type)

    def plan(self, command, session):
        self.command(command)
        scope = self.scope_type(self.u, self.p, session, command, self.state_path)
        with scope.directory({'rootId': scope.SOURCE, 'path': command['sourcePath']}, 'stat') as (_, parent, leaf):
            source = self.f.open_at(parent, leaf, os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW)
            try:
                info = os.fstat(source); kind = 'Directory' if stat.S_ISDIR(info.st_mode) else 'File'
                identity = list(self.r.fingerprint(info)); self.metadata(identity, kind)
                source_parent = list(scope.inode(parent))
            finally: os.close(source)
        inner = self.engine(scope, kind).plan(scope.file_command() if kind == 'File' else scope.tree_command())
        with scope.directory({'rootId': scope.TARGET, 'path': command['targetPath']}, 'mutate') as (_, parent, _):
            parent_version = self.r.version(command, command['targetPath'].rpartition('/')[0], os.fstat(parent))
        result = {'schemaVersion': 1, 'command': dict(command), 'kind': kind, 'targetProof': scope.proof,
            'sourceIdentity': identity, 'sourceParent': source_parent, 'targetParent': list(scope.target_parent),
            'targetParentVersion': parent_version, 'uid': scope.owner[0], 'gid': scope.owner[1], 'innerPlan': inner}
        result['fingerprint'] = self.digest(result); self.validate_plan(result)
        return json.loads(self.encode(result))

    @contextlib.contextmanager
    def locked(self, request):
        self.request(request)
        if not self.state_path.is_absolute() or self.state_path != self.state_path.resolve(strict=True): self.reject('STATE_UNSAFE')
        directory = self.f.trusted_directory(self.state_path); lock = None
        try:
            if stat.S_IMODE(os.fstat(directory).st_mode) != 0o700: self.reject('STATE_UNSAFE')
            operation = request['operationId']
            if len(os.listdir(directory)) >= 20000 and not os.path.lexists(self.state_path / (operation + '.json')): self.reject('RECEIPT_LIMIT')
            lock = os.open(operation + '.lock', os.O_WRONLY | os.O_CREAT | os.O_NOFOLLOW | os.O_NONBLOCK | os.O_CLOEXEC, 0o600, dir_fd=directory)
            info = os.fstat(lock)
            if not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_nlink != 1 or stat.S_IMODE(info.st_mode) != 0o600 or info.st_size != 0:
                self.reject('STATE_UNSAFE')
            try: fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
            except BlockingIOError: self.reject('BUSY')
            record = self.read(directory, operation)
            if record is not None:
                if record['owner'] != request['owner']: self.reject('NOT_FOUND')
                if record['plan'] != request['plan']: self.reject('IDEMPOTENCY_CONFLICT')
            yield directory, record
        finally:
            if lock is not None: os.close(lock)
            os.close(directory)

    def read(self, directory, operation):
        try: descriptor = self.f.open_at(directory, operation + '.json', os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW)
        except FileNotFoundError: return None
        try:
            info = os.fstat(descriptor)
            if not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_nlink != 1 or stat.S_IMODE(info.st_mode) != 0o600 or not 0 < info.st_size <= self.LIMIT:
                self.reject('RECEIPT_INVALID')
            raw = os.read(descriptor, self.LIMIT + 1)
            if len(raw) != info.st_size: self.reject('RECEIPT_INVALID')
            record = json.loads(raw, object_pairs_hook=self.f.unique_object)
            self.validate_record(record, operation); return record
        finally: os.close(descriptor)

    def validate_record(self, record, operation):
        if type(record) is not dict or set(record) != self.RECORD or type(record['schemaVersion']) is not int or record['schemaVersion'] != 1:
            self.reject('RECEIPT_INVALID')
        self.request({k: record[k] for k in self.REQUEST})
        if record['operationId'] != operation or type(record['state']) is not str or record['state'] not in (*self.TERMINAL, 'Preparing', 'Running') or \
                type(record['innerState']) is not str or record['innerState'] not in ('', *self.TERMINAL) or type(record['privateMayExist']) is not bool or \
                record['innerReceiptHash'] != '' and not self.hexadecimal(record['innerReceiptHash']): self.reject('RECEIPT_INVALID')
        if record['state'] == 'Completed':
            self.metadata(record['publicationIdentity'], record['plan']['kind'])
            identity = record['publicationIdentity']; plan = record['plan']
            if identity[3:5] != [plan['uid'], plan['gid']] or identity[2] & 0o777 != plan['sourceIdentity'][2] & 0o777 or \
                    identity[7] != plan['sourceIdentity'][7] or plan['kind'] == 'File' and identity[6] != plan['sourceIdentity'][6]:
                self.reject('RECEIPT_INVALID')
            if not self.hexadecimal(record['version']) or record['version'] != self.container_version(record['plan']['command'],
                    record['plan']['command']['targetPath'], record['publicationIdentity']) or record['innerState'] != 'Completed' or \
                    not self.hexadecimal(record['innerReceiptHash']) or record['privateMayExist']: self.reject('RECEIPT_INVALID')
        elif record['publicationIdentity'] is not None or record['version'] != '': self.reject('RECEIPT_INVALID')

    def save(self, directory, record):
        self.validate_record(record, record['operationId'])
        if len(self.encode(record)) > self.LIMIT: self.reject('RECEIPT_LIMIT')
        self.f.save_receipt(directory, record['operationId'], record)

    def new_record(self, request, phase):
        return {'schemaVersion': 1, **json.loads(self.encode(request)), 'state': phase, 'version': '',
                'publicationIdentity': None, 'innerState': '', 'innerReceiptHash': '', 'privateMayExist': phase == 'Unknown'}

    def adapter(self, plan, session):
        path = self.state_path / 'engines'
        base = self.f.trusted_directory(self.state_path)
        try:
            try: os.mkdir('engines', 0o700, dir_fd=base); os.fsync(base)
            except FileExistsError: pass
        finally: os.close(base)
        binding = {key: plan[key] for key in ('targetProof', 'uid', 'gid')}
        return self.scope_type(self.u, self.p, session, plan['command'], path,
            target_parent=plan['targetParent'], expected_binding=binding)

    def checkpoint(self, phase):
        """Trusted validation instrumentation only, never an RPC parameter."""

    def apply(self, request, session):
        with self.locked(request) as (directory, record):
            if record is not None:
                self.recover(directory, record, session); return self.public(record, session, True)
            if session is None: self.reject('TARGET_UNAVAILABLE')
            if self.plan(request['plan']['command'], session) != request['plan']: self.reject('VERSION_CONFLICT')
            record = self.new_record(request, 'Preparing'); self.save(directory, record)
            try:
                self.checkpoint('Preparing')
                scope = self.adapter(record['plan'], session); engine = self.engine(scope, record['plan']['kind'])
                record['state'] = 'Running'; self.save(directory, record); self.checkpoint('Running')
                inner = {**request, 'plan': record['plan']['innerPlan']}
                if record['plan']['kind'] == 'File': engine.apply(inner)
                else: engine.copy(inner)
                self.checkpoint('InnerFinished'); self.recover(directory, record, session)
            except BaseException:
                with contextlib.suppress(OSError, self.r.Rejected, self.f.Rejected): self.recover(directory, record, session)
                raise
            return self.public(record, session, False)

    def validate_inner(self, record, plan, operation, owner):
        if type(record) is not dict or record.get('operationId') != operation or record.get('owner') != owner or record.get('plan') != plan['innerPlan']:
            self.reject('RECEIPT_INVALID')
        allowed = (*self.TERMINAL, 'Preparing', 'DestinationApplying') if plan['kind'] == 'File' else (*self.TERMINAL, 'Preparing', 'Staging', 'Ready', 'Publishing')
        if type(record.get('state')) is not str or record['state'] not in allowed: self.reject('RECEIPT_INVALID')
        if 'newIdentity' in record and not self.inode_value(record['newIdentity']): self.reject('RECEIPT_INVALID')
        if plan['kind'] == 'File' and 'newMetadata' in record:
            value = record['newMetadata']; self.metadata(value, 'File', 8)
            if value[:2] != record.get('newIdentity') or value[2] & 0o777 != plan['sourceIdentity'][2] & 0o777 or \
                    value[3:5] != [plan['uid'], plan['gid']] or value[6:8] != plan['sourceIdentity'][6:8]: self.reject('RECEIPT_INVALID')
        if record['state'] == 'Completed' and (not self.hexadecimal(record.get('version')) or 'newIdentity' not in record or
                plan['kind'] == 'File' and 'newMetadata' not in record): self.reject('RECEIPT_INVALID')

    def observe_inner(self, record, scope, unstarted=False):
        plan = record['plan']; engine = self.engine(scope, plan['kind'])
        with engine.state(record['operationId'], record['owner'], writing=True) as (directory, inner):
            if inner is None:
                # Preparing precedes the durable Running fence and cannot have
                # invoked the inner engine. After Running, a missing receipt
                # alone cannot prove non-publication (including lost evidence).
                # Permanently fence either case; never rerun a physical copy.
                inner = {'operationId': record['operationId'], 'owner': record['owner'], 'plan': plan['innerPlan'],
                         'state': 'Aborted' if unstarted else 'Unknown'}
                engine.save(directory, inner)
            else:
                self.validate_inner(inner, plan, record['operationId'], record['owner'])
                engine.recover(directory, inner)
            self.validate_inner(inner, plan, record['operationId'], record['owner'])
            return inner

    def published(self, plan, scope, inner):
        with scope.directory({'rootId': scope.TARGET, 'path': plan['command']['targetPath']}, 'stat') as (_, parent, leaf):
            descriptor = self.f.open_at(parent, leaf, os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW |
                (os.O_DIRECTORY if plan['kind'] == 'Directory' else 0))
            try:
                info = os.fstat(descriptor)
                if scope.version(info) != inner['version'] or list(scope.inode(descriptor)) != inner['newIdentity']:
                    self.reject('TARGET_CHANGED')
                engine = self.engine(scope, plan['kind'])
                if plan['kind'] == 'File':
                    if scope.publication_identity(info) != inner['newMetadata'] or engine.hash_descriptor(descriptor, scope.regular(descriptor)) != plan['innerPlan']['sha256']:
                        self.reject('TARGET_CHANGED')
                else:
                    policy = scope.roots()[scope.TARGET]
                    if engine.snapshot(descriptor, plan['innerPlan']['command'], policy, lambda: False) != inner['treeSha256']:
                        self.reject('TARGET_CHANGED')
                if scope.version(os.fstat(descriptor)) != inner['version'] or scope.version(os.stat(leaf, dir_fd=parent, follow_symlinks=False)) != inner['version']:
                    self.reject('TARGET_CHANGED')
                return list(self.r.fingerprint(info))
            finally: os.close(descriptor)

    def recover(self, directory, record, session):
        if record['state'] in self.TERMINAL: return
        unstarted = record['state'] == 'Preparing'
        record['state'] = 'Unknown'; record['privateMayExist'] = True
        try:
            # Detached scope permits only metadata. A changed or unavailable
            # live target cannot silently become an unbound new container.
            try: scope = self.adapter(record['plan'], session)
            except (OSError, self.r.Rejected, self.f.Rejected): scope = self.adapter(record['plan'], None)
            inner = self.observe_inner(record, scope, unstarted)
            record['innerState'] = inner['state']; record['innerReceiptHash'] = hashlib.sha256(self.encode(inner)).hexdigest().upper()
            if inner['state'] == 'Aborted':
                record.update(state='Aborted', privateMayExist=bool(inner.get('mayHaveStaged')))
            elif inner['state'] == 'Completed':
                identity = self.published(record['plan'], scope, inner)
                record.update(state='Completed', publicationIdentity=identity, privateMayExist=False,
                    version=self.container_version(record['plan']['command'], record['plan']['command']['targetPath'], identity))
        except (OSError, self.r.Rejected, self.f.Rejected): pass
        self.save(directory, record)

    def inspect(self, request, session=None):
        with self.locked(request) as (directory, record):
            if record is None: self.reject('NOT_FOUND')
            self.recover(directory, record, session); return self.public(record, session, True)

    def discard(self, request, session=None):
        with self.locked(request) as (directory, record):
            if record is None:
                record = self.new_record(request, 'Aborted'); self.save(directory, record)
            else: self.recover(directory, record, session)
            return self.public(record, session, True)

    def public(self, record, session, replayed):
        plan = record['plan']; command = plan['command']; current = False
        if record['state'] == 'Completed' and session is not None:
            try:
                scope = self.adapter(plan, session); inner = self.observe_inner(record, scope)
                current = inner['state'] == 'Completed' and self.published(plan, scope, inner) == record['publicationIdentity']
            except (OSError, self.r.Rejected, self.f.Rejected): pass
        return {'operationId': record['operationId'], 'containerId': command['containerId'], 'instanceId': command['instanceId'],
            'kind': plan['kind'], 'fingerprint': plan['fingerprint'], 'targetProof': plan['targetProof'], 'state': record['state'],
            'version': record['version'], 'bindingCurrent': current, 'privateMayExist': record['privateMayExist'], 'replayed': replayed}
