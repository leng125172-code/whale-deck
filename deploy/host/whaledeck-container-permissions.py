#!/usr/bin/env python3
"""Internal non-recursive ordinary-mode engine, not a CLI/helper/RPC grant.

Require a pinned ContainerRoot, a sealed public version, an attested HDD
metadata receipt, and the future shared business publication fence. No shell,
host path, selected UID/GID, recursive chmod, ACL writes or automatic replay.
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


def encode(value):
    return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(',', ':'), allow_nan=False).encode()


class ContainerPermissionHistory:
    NAMESPACE = 'container-permissions-v1'
    DOMAIN = b'whaledeck-container-permission-history-v1\n'

    def __init__(self, storage):
        if storage.namespace is not None: raise ValueError('CONTAINER_PERMISSION_HISTORY_DOMAIN')
        self.storage, self.f = storage, storage.f

    def reject(self, suffix): raise self.f.Rejected('CONTAINER_PERMISSION_' + suffix)

    @contextlib.contextmanager
    def target(self, create):
        with self.storage.target(create_namespace=False, read_only=not create) as (configuration, base):
            if create:
                try: os.mkdir(self.NAMESPACE, 0o700, dir_fd=base); os.fsync(base)
                except FileExistsError: pass
            descriptor = self.f.open_at(base, self.NAMESPACE, os.O_RDONLY | os.O_DIRECTORY)
            try:
                info = os.fstat(descriptor)
                if info.st_uid != 0 or info.st_gid != 0 or stat.S_IMODE(info.st_mode) != 0o700:
                    self.reject('HISTORY_UNSAFE')
                self.storage.attest(configuration, descriptor, read_only=not create)
                yield configuration, descriptor
            finally: os.close(descriptor)

    @staticmethod
    def expected(request):
        return {'schemaVersion': 1, 'resourceType': 'ContainerPermissionMetadata', 'storageId': 'database-platform.backup.hdd',
            'operationId': request['operationId'], 'owner': request['owner'], 'plan': request['plan']}

    def read(self, directory, request):
        saved = self.storage.read_json(directory, request['operationId'] + '.json')
        expected = self.expected(request)
        if set(saved) != set(expected) | {'createdAtUnixMs', 'historyDigest'} or any(saved[key] != value for key, value in expected.items()) or \
                type(saved['createdAtUnixMs']) is not int or saved['createdAtUnixMs'] < 0 or \
                type(saved['historyDigest']) is not str or not re.fullmatch('[A-F0-9]{64}', saved['historyDigest']):
            self.reject('HISTORY_CHANGED')
        digest = hashlib.sha256(self.DOMAIN + encode({key: value for key, value in saved.items() if key != 'historyDigest'})).hexdigest().upper()
        if digest != saved['historyDigest']: self.reject('HISTORY_CHANGED')
        return digest

    def capture(self, request):
        self.f.identity(request['operationId'])
        with self.target(True) as (configuration, directory):
            lock = os.open('history.lock', os.O_RDWR | os.O_CREAT | os.O_NOFOLLOW | os.O_CLOEXEC, 0o600, dir_fd=directory)
            try:
                info = os.fstat(lock)
                if not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_nlink != 1 or stat.S_IMODE(info.st_mode) != 0o600 or info.st_size:
                    self.reject('HISTORY_UNSAFE')
                try: fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
                except BlockingIOError: self.reject('BUSY')
                try: return self.read(directory, request)
                except FileNotFoundError: pass
                if sum(name.endswith('.json') for name in os.listdir(directory)) >= configuration['maxVersions']:
                    self.reject('HISTORY_LIMIT')
                capacity = os.fstatvfs(directory)
                if capacity.f_bavail * capacity.f_frsize < max(configuration['reserveBytes'], capacity.f_blocks * capacity.f_frsize * configuration['reservePercent'] // 100) + 65536:
                    self.reject('HISTORY_CAPACITY')
                saved = {**self.expected(request), 'createdAtUnixMs': time.time_ns() // 1_000_000}
                saved['historyDigest'] = hashlib.sha256(self.DOMAIN + encode(saved)).hexdigest().upper()
                self.f.save_receipt(directory, request['operationId'], saved)
                return self.read(directory, request)
            finally: os.close(lock)

    def verify(self, request, digest):
        with self.target(False) as (_, directory):
            if self.read(directory, request) != digest: self.reject('HISTORY_CHANGED')


def container_permissions_type(directory_type):
    class ContainerPermissions(directory_type):
        COMMAND = {'containerId', 'instanceId', 'path', 'mode', 'expectedVersion'}
        PLAN = directory_type.PLAN | {'kind', 'sourceIdentity', 'attributesDigest', 'noChange'}
        RECORD = {'schemaVersion', 'operationId', 'owner', 'plan', 'state', 'version', 'actualIdentity',
            'modeConfirmed', 'historyMayExist', 'historyVerified', 'historyDigest'}

        def __init__(self, uploads, publisher, state_path, history):
            super().__init__(uploads, publisher, state_path)
            if history.f is not self.f: raise ValueError('CONTAINER_PERMISSION_HISTORY_ENGINE_MISMATCH')
            self.history = history

        def reject(self, suffix): raise self.r.Rejected('CONTAINER_PERMISSION_' + suffix)

        @staticmethod
        def digest(plan):
            return hashlib.sha256(b'whaledeck-container-permission-plan-v1\n' + encode({key: value for key, value in plan.items() if key != 'fingerprint'})).hexdigest().upper()

        def command(self, value):
            if type(value) is not dict or set(value) != self.COMMAND: self.reject('REQUEST_INVALID')
            self.r.target_binding(value['containerId'], value['instanceId']); self.r.canonical_path(value['path'], empty=False)
            if self.r.protected(value['path'], self.f): self.reject('PROTECTED')
            if type(value['mode']) is not str or not re.fullmatch('0[0-7]{3}', value['mode']): self.reject('MODE_INVALID')
            if type(value['expectedVersion']) is not str or not re.fullmatch('[A-F0-9]{64}', value['expectedVersion']): self.reject('VERSION_INVALID')

        @staticmethod
        def metadata(info):
            return [info.st_dev, info.st_ino, info.st_mode, info.st_uid, info.st_gid, info.st_nlink, info.st_size, info.st_mtime_ns, info.st_ctime_ns]

        def validate_identity(self, identity, kind):
            if type(identity) is not list or len(identity) != 9 or any(type(value) is not int for value in identity) or \
                    not 0 <= identity[0] <= 18446744073709551615 or not 0 < identity[1] <= 18446744073709551615 or \
                    not 0 <= identity[2] <= 65535 or identity[2] & 0o7000 or any(not 0 <= identity[key] < 4294967295 for key in (3, 4)) or \
                    not 1 <= identity[5] <= 18446744073709551615 or kind == 'File' and identity[5] != 1 or not 0 <= identity[6] <= 9223372036854775807 or \
                    any(not -9223372036854775808 <= identity[key] <= 9223372036854775807 for key in (7, 8)) or \
                    kind not in ('File', 'Directory') or stat.S_IFMT(identity[2]) != (stat.S_IFREG if kind == 'File' else stat.S_IFDIR):
                self.reject('SOURCE_UNSUPPORTED')

        def snapshot(self, descriptor):
            before = os.fstat(descriptor); kind = 'Directory' if stat.S_ISDIR(before.st_mode) else 'File'
            identity = self.metadata(before); self.validate_identity(identity, kind)
            names = os.listxattr(descriptor)
            if len(names) > 128: self.reject('ATTRIBUTES_LIMIT')
            values = []; size = 0
            for name in sorted(names):
                if name.startswith(('system.posix_acl_', 'security.')): self.reject('EXTENDED_METADATA_REQUIRED')
                value = os.getxattr(descriptor, name); size += len(name.encode('utf-8')) + len(value)
                if size > 65536: self.reject('ATTRIBUTES_LIMIT')
                values.append([name, base64.b64encode(value).decode('ascii')])
            if self.metadata(os.fstat(descriptor)) != identity: self.reject('VERSION_CONFLICT')
            return kind, identity, hashlib.sha256(b'whaledeck-container-permission-attributes-v1\n' + encode(values)).hexdigest().upper()

        def validate_plan(self, plan):
            super().validate_plan(plan)
            self.validate_identity(plan['sourceIdentity'], plan['kind'])
            if type(plan['attributesDigest']) is not str or not re.fullmatch('[A-F0-9]{64}', plan['attributesDigest']) or \
                    type(plan['noChange']) is not bool or plan['noChange'] != (plan['sourceIdentity'][2] & 0o777 == int(plan['command']['mode'], 8)):
                self.reject('PLAN_INVALID')
            source = plan['sourceIdentity']; command = plan['command']
            expected = hashlib.sha256(b'whaledeck-container-file-v1\n' + json.dumps([command['containerId'], command['instanceId'], command['path'], *source],
                ensure_ascii=False, separators=(',', ':'), allow_nan=False).encode()).hexdigest().upper()
            if expected != command['expectedVersion']: self.reject('PLAN_INVALID')

        @contextlib.contextmanager
        def parent(self, plan, session):
            with super().parent(plan, session) as parent:
                if 'uid' in plan and tuple(self.p.owner(session)) != (plan['uid'], plan['gid']): self.reject('OWNER_CHANGED')
                yield parent

        def open_source(self, parent, plan):
            return self.f.open_at(parent, plan['command']['path'].rsplit('/', 1)[-1], os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW, cross_mount=True)

        def plan(self, command, session):
            self.command(command); proof = self.guard(command, session)
            with self.parent({'command': command, 'targetProof': proof}, session) as parent:
                descriptor = self.open_source(parent, {'command': command})
                try:
                    kind, identity, attrs = self.snapshot(descriptor)
                    if self.r.version(command, command['path'], os.fstat(descriptor)) != command['expectedVersion']: self.reject('VERSION_CONFLICT')
                    named = os.stat(command['path'].rsplit('/', 1)[-1], dir_fd=parent, follow_symlinks=False)
                    if self.metadata(named) != identity: self.reject('VERSION_CONFLICT')
                finally: os.close(descriptor)
                info = os.fstat(parent); uid, gid = self.p.owner(session)
                plan = {'schemaVersion': 1, 'command': dict(command), 'targetProof': proof, 'parentIdentity': [info.st_dev, info.st_ino],
                    'parentVersion': self.r.version(command, command['path'].rpartition('/')[0], info), 'uid': uid, 'gid': gid,
                    'kind': kind, 'sourceIdentity': identity, 'attributesDigest': attrs, 'noChange': identity[2] & 0o777 == int(command['mode'], 8)}
            plan['fingerprint'] = self.digest(plan); self.validate_plan(plan); return plan

        def new_record(self, request, phase):
            return {'schemaVersion': 1, **json.loads(encode(request)), 'state': phase, 'version': '', 'actualIdentity': None,
                'modeConfirmed': False if phase == 'Aborted' else None, 'historyMayExist': False, 'historyVerified': False, 'historyDigest': ''}

        def validate_record(self, record, operation):
            if type(record) is not dict or set(record) != self.RECORD or type(record['schemaVersion']) is not int or record['schemaVersion'] != 1: self.reject('RECEIPT_INVALID')
            self.request({key: record[key] for key in self.REQUEST})
            if record['operationId'] != operation or record['state'] not in (*self.TERMINAL, 'Preparing', 'Prepared', 'Applying') or \
                    any(type(record[key]) is not bool for key in ('historyMayExist', 'historyVerified')) or \
                    record['modeConfirmed'] is not None and type(record['modeConfirmed']) is not bool or type(record['historyDigest']) is not str:
                self.reject('RECEIPT_INVALID')
            if record['historyVerified']:
                if not record['historyMayExist'] or not re.fullmatch('[A-F0-9]{64}', record['historyDigest']): self.reject('RECEIPT_INVALID')
            elif record['historyDigest'] != '': self.reject('RECEIPT_INVALID')
            if record['state'] in ('Prepared', 'Applying', 'Completed') and not record['historyVerified']: self.reject('RECEIPT_INVALID')
            if record['state'] == 'Completed':
                self.validate_identity(record['actualIdentity'], record['plan']['kind'])
                desired = list(record['plan']['sourceIdentity']); desired[2] = desired[2] & ~0o777 | int(record['plan']['command']['mode'], 8)
                if record['actualIdentity'][:8] != desired[:8] or record['modeConfirmed'] is not True or \
                        type(record['version']) is not str or not re.fullmatch('[A-F0-9]{64}', record['version']): self.reject('RECEIPT_INVALID')
                command = record['plan']['command']
                expected = hashlib.sha256(b'whaledeck-container-file-v1\n' + json.dumps([command['containerId'], command['instanceId'], command['path'], *record['actualIdentity']],
                    ensure_ascii=False, separators=(',', ':'), allow_nan=False).encode()).hexdigest().upper()
                if expected != record['version']: self.reject('RECEIPT_INVALID')
            elif record['actualIdentity'] is not None or record['version'] != '' or record['modeConfirmed'] != (False if record['state'] == 'Aborted' else None):
                self.reject('RECEIPT_INVALID')

        def checkpoint(self, phase):
            """Trusted disposable-test checkpoint only, never request data."""

        def unchanged(self, parent, descriptor, plan):
            command = plan['command']; kind, identity, attrs = self.snapshot(descriptor)
            if kind != plan['kind'] or identity != plan['sourceIdentity'] or attrs != plan['attributesDigest'] or \
                    self.metadata(os.stat(command['path'].rsplit('/', 1)[-1], dir_fd=parent, follow_symlinks=False)) != identity or \
                    self.r.version(command, command['path'].rpartition('/')[0], os.fstat(parent)) != plan['parentVersion']:
                self.reject('VERSION_CONFLICT')

        def apply(self, request, session):
            with self.locked(request) as (state, record):
                if record is not None:
                    self.recover(state, record, session); return self.public(record, session, True)
                if self.plan(request['plan']['command'], session) != request['plan']: self.reject('VERSION_CONFLICT')
                record = self.new_record(request, 'Preparing'); self.save(state, record); self.checkpoint('Preparing')
                try:
                    record['historyMayExist'] = True; self.save(state, record)
                    record['historyDigest'] = self.history.capture(request); record['historyVerified'] = True; self.save(state, record); self.checkpoint('HistoryVerified')
                    plan = record['plan']
                    with self.parent(plan, session) as parent:
                        descriptor = self.open_source(parent, plan)
                        try:
                            self.unchanged(parent, descriptor, plan); self.history.verify(request, record['historyDigest'])
                            record['state'] = 'Prepared'; self.save(state, record); self.checkpoint('Prepared')
                            with self.parent(plan, session): pass
                            self.unchanged(parent, descriptor, plan)
                            record['state'] = 'Applying'; self.save(state, record); self.checkpoint('Applying')
                            with self.parent(plan, session): pass
                            self.unchanged(parent, descriptor, plan)
                            if not plan['noChange']: os.fchmod(descriptor, int(plan['command']['mode'], 8)); os.fsync(descriptor)
                            self.checkpoint('ModeChanged')
                        finally: os.close(descriptor)
                    self.recover(state, record, session)
                except BaseException:
                    with contextlib.suppress(OSError, self.r.Rejected, self.f.Rejected): self.recover(state, record, session)
                    raise
                return self.public(record, session, False)

        def recover(self, state, record, session):
            if record['state'] in self.TERMINAL: return
            phase = record['state']; plan = record['plan']; record['state'] = 'Unknown'; record['modeConfirmed'] = None
            try:
                with self.parent(plan, session) as parent:
                    descriptor = self.open_source(parent, plan)
                    try:
                        kind, identity, attrs = self.snapshot(descriptor)
                        named = os.stat(plan['command']['path'].rsplit('/', 1)[-1], dir_fd=parent, follow_symlinks=False)
                        if kind != plan['kind'] or identity != self.metadata(named) or attrs != plan['attributesDigest']: self.reject('SOURCE_CHANGED')
                        desired = list(plan['sourceIdentity']); desired[2] = desired[2] & ~0o777 | int(plan['command']['mode'], 8)
                        if phase == 'Applying' and identity[:8] == desired[:8] and (not plan['noChange'] or identity == plan['sourceIdentity']):
                            self.history.verify({key: record[key] for key in self.REQUEST}, record['historyDigest'])
                            record.update(state='Completed', actualIdentity=identity, modeConfirmed=True,
                                version=self.r.version(plan['command'], plan['command']['path'], named))
                        elif identity == plan['sourceIdentity']: record.update(state='Aborted', modeConfirmed=False)
                    finally: os.close(descriptor)
            except (OSError, self.r.Rejected, self.f.Rejected): pass
            self.save(state, record)

        def public(self, record, session, replayed):
            binding = False; plan = record['plan']; command = plan['command']
            if record['state'] == 'Completed' and session is not None:
                try:
                    with self.parent(plan, session) as parent:
                        descriptor = self.open_source(parent, plan)
                        try:
                            _, identity, attrs = self.snapshot(descriptor)
                            named = os.stat(command['path'].rsplit('/', 1)[-1], dir_fd=parent, follow_symlinks=False)
                            self.history.verify({key: record[key] for key in self.REQUEST}, record['historyDigest'])
                            binding = identity == record['actualIdentity'] == self.metadata(named) and attrs == plan['attributesDigest']
                        finally: os.close(descriptor)
                except (OSError, self.r.Rejected, self.f.Rejected): pass
            return {'operationId': record['operationId'], 'containerId': command['containerId'], 'instanceId': command['instanceId'],
                'fingerprint': plan['fingerprint'], 'targetProof': plan['targetProof'], 'kind': plan['kind'], 'state': record['state'], 'version': record['version'],
                'modeConfirmed': record['modeConfirmed'], 'noChange': plan['noChange'], 'bindingCurrent': binding,
                'historyMayExist': record['historyMayExist'], 'historyVerified': record['historyVerified'], 'historyDigest': record['historyDigest'], 'replayed': replayed}

    return ContainerPermissions
