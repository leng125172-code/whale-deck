#!/usr/bin/env python3
"""Internal FD-bound Linux POSIX ACL journal; no helper/RPC or browser grant.

One operation replaces one access/default ACL. Explicit masks and numeric IDs,
independent metadata history, and observe-only recovery; no shell or recursion.
Linux UAPI posix_acl_xattr.h (version 2, little-endian) defines the wire format.
"""
import base64
import contextlib
import errno
import hashlib
import json
import os
import re
import stat
import struct

XATTRS = {'access': 'system.posix_acl_access', 'default': 'system.posix_acl_default'}
TAGS = {'user_obj': 1, 'user': 2, 'group_obj': 4, 'group': 8, 'mask': 16, 'other': 32}
MAX_ENTRIES = 64
UNDEFINED = 0xffffffff


def encode(value):
    return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(',', ':'), allow_nan=False).encode()


def validate_entries(entries, *, empty=False):
    if type(entries) is not list or len(entries) > MAX_ENTRIES: raise ValueError('ACL_ENTRIES')
    if not entries:
        if empty: return
        raise ValueError('ACL_ENTRIES')
    seen = set(); order = []; tags = []
    for entry in entries:
        if type(entry) is not dict or set(entry) != {'tag', 'id', 'permissions'}: raise ValueError('ACL_ENTRY')
        tag, identifier, permissions = entry['tag'], entry['id'], entry['permissions']
        if type(tag) is not str or tag not in TAGS or type(permissions) is not int or not 0 <= permissions <= 7: raise ValueError('ACL_ENTRY')
        if tag in ('user', 'group'):
            if type(identifier) is not int or not 0 <= identifier < UNDEFINED: raise ValueError('ACL_ID')
        elif identifier is not None: raise ValueError('ACL_ID')
        key = (TAGS[tag], -1 if identifier is None else identifier)
        if key in seen: raise ValueError('ACL_DUPLICATE')
        seen.add(key); order.append(key); tags.append(tag)
    if order != sorted(order) or any(tags.count(t) != 1 for t in ('user_obj', 'group_obj', 'other')) or \
            any(t in tags for t in ('user', 'group')) and 'mask' not in tags: raise ValueError('ACL_STRUCTURE')


def acl_mode(entries):
    validate_entries(entries)
    values = {e['tag']: e['permissions'] for e in entries if e['id'] is None}
    return values['user_obj'] << 6 | values.get('mask', values['group_obj']) << 3 | values['other']


def encode_acl(entries):
    validate_entries(entries)
    return struct.pack('<I', 2) + b''.join(struct.pack('<HHI', TAGS[e['tag']], e['permissions'], UNDEFINED if e['id'] is None else e['id']) for e in entries)


def decode_acl(raw):
    if type(raw) is not bytes or not 28 <= len(raw) <= 4 + MAX_ENTRIES * 8 or (len(raw)-4) % 8 or struct.unpack_from('<I', raw)[0] != 2: raise ValueError('ACL_BINARY')
    reverse = {v: k for k, v in TAGS.items()}; entries = []
    for offset in range(4, len(raw), 8):
        tag, permissions, identifier = struct.unpack_from('<HHI', raw, offset)
        if tag not in reverse: raise ValueError('ACL_TAG')
        entries.append({'tag': reverse[tag], 'id': None if identifier == UNDEFINED else identifier, 'permissions': permissions})
    validate_entries(entries); return entries


def base_acl(mode):
    return [{'tag': t, 'id': None, 'permissions': mode >> shift & 7} for t, shift in (('user_obj', 6), ('group_obj', 3), ('other', 0))]


def read_acl(descriptor, kind, mode):
    try: raw = os.getxattr(descriptor, XATTRS[kind])
    except OSError as error:
        if error.errno != errno.ENODATA: raise
        return base_acl(mode) if kind == 'access' else []
    return decode_acl(raw)


def write_acl(descriptor, kind, entries):
    # FD operations only. Empty default ACL removes inheritance; access ACL is
    # reduced to explicit base entries instead of conflating absent and denied.
    validate_entries(entries, empty=kind == 'default')
    if kind == 'default' and not entries:
        try: os.removexattr(descriptor, XATTRS[kind])
        except OSError as error:
            if error.errno != errno.ENODATA: raise
    else: os.setxattr(descriptor, XATTRS[kind], encode_acl(entries))


def acl_history_type(permission_module):
    class ContainerAclHistory(permission_module.ContainerPermissionHistory):
        NAMESPACE = 'container-acl-v1'
        DOMAIN = b'whaledeck-container-acl-history-v1\n'

        def reject(self, suffix): raise self.f.Rejected('CONTAINER_ACL_' + suffix)

        @staticmethod
        def expected(request):
            return {'schemaVersion': 1, 'resourceType': 'ContainerAclMetadata', 'storageId': 'database-platform.backup.hdd',
                'operationId': request['operationId'], 'owner': request['owner'], 'plan': request['plan']}
    return ContainerAclHistory


def container_acl_type(directory_type):
    class ContainerAcl(directory_type):
        COMMAND = {'containerId', 'instanceId', 'path', 'aclType', 'entries', 'expectedVersion'}
        PLAN = directory_type.PLAN | {'kind', 'sourceIdentity', 'attributesDigest', 'accessAcl', 'defaultAcl', 'targetMode', 'noChange'}
        RECORD = {'schemaVersion', 'operationId', 'owner', 'plan', 'state', 'version', 'actualIdentity',
            'aclConfirmed', 'historyMayExist', 'historyVerified', 'historyDigest'}

        def __init__(self, uploads, publisher, state_path, history):
            super().__init__(uploads, publisher, state_path)
            if history.f is not self.f: raise ValueError('CONTAINER_ACL_HISTORY_ENGINE_MISMATCH')
            self.history = history

        def reject(self, suffix): raise self.r.Rejected('CONTAINER_ACL_' + suffix)

        @staticmethod
        def digest(plan):
            return hashlib.sha256(b'whaledeck-container-acl-plan-v1\n' + encode({key: value for key, value in plan.items() if key != 'fingerprint'})).hexdigest().upper()

        def command(self, value):
            if type(value) is not dict or set(value) != self.COMMAND: self.reject('REQUEST_INVALID')
            self.r.target_binding(value['containerId'], value['instanceId']); self.r.canonical_path(value['path'], empty=False)
            if self.r.protected(value['path'], self.f): self.reject('PROTECTED')
            if value['aclType'] not in ('access', 'default'): self.reject('TYPE_INVALID')
            try: validate_entries(value['entries'], empty=value['aclType'] == 'default')
            except ValueError: self.reject('ENTRIES_INVALID')
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
            try:
                access = read_acl(descriptor, 'access', before.st_mode)
                default = read_acl(descriptor, 'default', before.st_mode)
            except ValueError: self.reject('ACL_INVALID')
            if kind == 'File' and default: self.reject('SOURCE_UNSUPPORTED')
            if acl_mode(access) != before.st_mode & 0o777: self.reject('VERSION_CONFLICT')
            names = os.listxattr(descriptor)
            if len(names) > 128: self.reject('ATTRIBUTES_LIMIT')
            values = []; size = 0
            for name in sorted(names):
                if name in XATTRS.values(): continue
                if name.startswith(('system.posix_acl_', 'security.')): self.reject('EXTENDED_METADATA_REQUIRED')
                value = os.getxattr(descriptor, name); size += len(name.encode('utf-8')) + len(value)
                if size > 65536: self.reject('ATTRIBUTES_LIMIT')
                values.append([name, base64.b64encode(value).decode('ascii')])
            if self.metadata(os.fstat(descriptor)) != identity: self.reject('VERSION_CONFLICT')
            return kind, identity, hashlib.sha256(b'whaledeck-container-acl-attributes-v1\n' + encode(values)).hexdigest().upper(), access, default

        @staticmethod
        def desired(plan):
            command = plan['command']; access = command['entries'] if command['aclType'] == 'access' else plan['accessAcl']
            default = command['entries'] if command['aclType'] == 'default' else plan['defaultAcl']
            identity = list(plan['sourceIdentity']); identity[2] = identity[2] & ~0o777 | plan['targetMode']
            return identity, access, default

        def validate_plan(self, plan):
            super().validate_plan(plan)
            self.validate_identity(plan['sourceIdentity'], plan['kind'])
            try:
                validate_entries(plan['accessAcl']); validate_entries(plan['defaultAcl'], empty=True)
            except ValueError: self.reject('PLAN_INVALID')
            source = plan['sourceIdentity']; command = plan['command']
            if plan['kind'] == 'File' and (plan['defaultAcl'] or command['aclType'] == 'default'): self.reject('DEFAULT_REQUIRES_DIRECTORY')
            if acl_mode(plan['accessAcl']) != source[2] & 0o777: self.reject('PLAN_INVALID')
            desired_mode = acl_mode(command['entries']) if command['aclType'] == 'access' else source[2] & 0o777
            old_acl = plan['accessAcl'] if command['aclType'] == 'access' else plan['defaultAcl']
            if type(plan['targetMode']) is not int or plan['targetMode'] != desired_mode or \
                    type(plan['attributesDigest']) is not str or not re.fullmatch('[A-F0-9]{64}', plan['attributesDigest']) or \
                    type(plan['noChange']) is not bool or plan['noChange'] != (old_acl == command['entries']): self.reject('PLAN_INVALID')
            expected = hashlib.sha256(b'whaledeck-container-file-v1\n' + json.dumps([command['containerId'], command['instanceId'], command['path'], *source],
                ensure_ascii=False, separators=(',', ':'), allow_nan=False).encode()).hexdigest().upper()
            if expected != command['expectedVersion'] or len(encode(plan)) > 14000: self.reject('PLAN_INVALID')

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
                    kind, identity, attrs, access, default = self.snapshot(descriptor)
                    if kind == 'File' and command['aclType'] == 'default': self.reject('DEFAULT_REQUIRES_DIRECTORY')
                    if self.r.version(command, command['path'], os.fstat(descriptor)) != command['expectedVersion']: self.reject('VERSION_CONFLICT')
                    named = os.stat(command['path'].rsplit('/', 1)[-1], dir_fd=parent, follow_symlinks=False)
                    if self.metadata(named) != identity: self.reject('VERSION_CONFLICT')
                finally: os.close(descriptor)
                info = os.fstat(parent); uid, gid = self.p.owner(session)
                plan = {'schemaVersion': 1, 'command': dict(command), 'targetProof': proof, 'parentIdentity': [info.st_dev, info.st_ino],
                    'parentVersion': self.r.version(command, command['path'].rpartition('/')[0], info), 'uid': uid, 'gid': gid,
                    'kind': kind, 'sourceIdentity': identity, 'attributesDigest': attrs, 'accessAcl': access, 'defaultAcl': default,
                    'targetMode': acl_mode(command['entries']) if command['aclType'] == 'access' else identity[2] & 0o777,
                    'noChange': command['entries'] == (access if command['aclType'] == 'access' else default)}
            plan['fingerprint'] = self.digest(plan); self.validate_plan(plan); return plan

        def new_record(self, request, phase):
            return {'schemaVersion': 1, **json.loads(encode(request)), 'state': phase, 'version': '', 'actualIdentity': None,
                'aclConfirmed': False if phase == 'Aborted' else None, 'historyMayExist': False, 'historyVerified': False, 'historyDigest': ''}

        def validate_record(self, record, operation):
            if type(record) is not dict or set(record) != self.RECORD or type(record['schemaVersion']) is not int or record['schemaVersion'] != 1: self.reject('RECEIPT_INVALID')
            self.request({key: record[key] for key in self.REQUEST})
            if record['operationId'] != operation or record['state'] not in (*self.TERMINAL, 'Preparing', 'Prepared', 'Applying') or \
                    any(type(record[key]) is not bool for key in ('historyMayExist', 'historyVerified')) or \
                    record['aclConfirmed'] is not None and type(record['aclConfirmed']) is not bool or type(record['historyDigest']) is not str:
                self.reject('RECEIPT_INVALID')
            if record['historyVerified']:
                if not record['historyMayExist'] or not re.fullmatch('[A-F0-9]{64}', record['historyDigest']): self.reject('RECEIPT_INVALID')
            elif record['historyDigest'] != '': self.reject('RECEIPT_INVALID')
            if record['state'] in ('Prepared', 'Applying', 'Completed') and not record['historyVerified']: self.reject('RECEIPT_INVALID')
            if record['state'] == 'Completed':
                self.validate_identity(record['actualIdentity'], record['plan']['kind'])
                desired, _, _ = self.desired(record['plan'])
                if record['actualIdentity'][:8] != desired[:8] or record['aclConfirmed'] is not True or \
                        type(record['version']) is not str or not re.fullmatch('[A-F0-9]{64}', record['version']): self.reject('RECEIPT_INVALID')
                command = record['plan']['command']
                expected = hashlib.sha256(b'whaledeck-container-file-v1\n' + json.dumps([command['containerId'], command['instanceId'], command['path'], *record['actualIdentity']],
                    ensure_ascii=False, separators=(',', ':'), allow_nan=False).encode()).hexdigest().upper()
                if expected != record['version']: self.reject('RECEIPT_INVALID')
            elif record['actualIdentity'] is not None or record['version'] != '' or record['aclConfirmed'] != (False if record['state'] == 'Aborted' else None):
                self.reject('RECEIPT_INVALID')

        def checkpoint(self, phase):
            """Trusted disposable-test checkpoint only, never request data."""

        def unchanged(self, parent, descriptor, plan):
            command = plan['command']; kind, identity, attrs, access, default = self.snapshot(descriptor)
            if kind != plan['kind'] or identity != plan['sourceIdentity'] or attrs != plan['attributesDigest'] or access != plan['accessAcl'] or default != plan['defaultAcl'] or \
                    self.metadata(os.stat(command['path'].rsplit('/', 1)[-1], dir_fd=parent, follow_symlinks=False)) != identity or \
                    self.r.version(command, command['path'].rpartition('/')[0], os.fstat(parent)) != plan['parentVersion']:
                self.reject('VERSION_CONFLICT')

        def apply(self, request, session):
            with self.locked(request) as (state, record):
                if record is not None:
                    self.recover(state, record, session); return self.public(record, session, True)
                if self.history.has_artifacts(request):
                    record=self.new_record(request,'Unknown');record['historyMayExist']=True;self.save(state,record)
                    return self.public(record,session,False)
                if session is None: self.reject('SESSION_REQUIRED')
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
                            if not plan['noChange']:
                                write_acl(descriptor, plan['command']['aclType'], plan['command']['entries'])
                                self.checkpoint('AclWritten'); os.fsync(descriptor)
                            self.checkpoint('AclChanged')
                        finally: os.close(descriptor)
                    self.recover(state, record, session)
                except BaseException:
                    with contextlib.suppress(OSError, self.r.Rejected, self.f.Rejected): self.recover(state, record, session)
                    raise
                return self.public(record, session, False)

        def discard(self, request, session=None):
            with self.locked(request) as (state,record):
                replayed=True
                if record is None:
                    orphan=self.history.has_artifacts(request)
                    record=self.new_record(request,'Unknown' if orphan else 'Aborted');record['historyMayExist']=orphan;self.save(state,record)
                else:self.recover(state,record,session)
                return self.public(record,session,replayed)

        def recover(self, state, record, session):
            if record['state'] in self.TERMINAL: return
            phase = record['state']; plan = record['plan']; record['state'] = 'Unknown'; record['aclConfirmed'] = None
            try:
                with self.parent(plan, session) as parent:
                    descriptor = self.open_source(parent, plan)
                    try:
                        kind, identity, attrs, access, default = self.snapshot(descriptor)
                        named = os.stat(plan['command']['path'].rsplit('/', 1)[-1], dir_fd=parent, follow_symlinks=False)
                        if kind != plan['kind'] or identity != self.metadata(named) or attrs != plan['attributesDigest']: self.reject('SOURCE_CHANGED')
                        desired, target_access, target_default = self.desired(plan)
                        if phase == 'Applying' and identity[:8] == desired[:8] and access == target_access and default == target_default and (not plan['noChange'] or identity == plan['sourceIdentity']):
                            self.history.verify({key: record[key] for key in self.REQUEST}, record['historyDigest'])
                            # A visible ACL after an interrupted syscall is not durable
                            # proof. Sync this exact still-bound inode before completion.
                            os.fsync(descriptor)
                            record.update(state='Completed', actualIdentity=identity, aclConfirmed=True,
                                version=self.r.version(plan['command'], plan['command']['path'], named))
                        elif identity == plan['sourceIdentity'] and access == plan['accessAcl'] and default == plan['defaultAcl']: record.update(state='Aborted', aclConfirmed=False)
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
                            _, identity, attrs, access, default = self.snapshot(descriptor)
                            named = os.stat(command['path'].rsplit('/', 1)[-1], dir_fd=parent, follow_symlinks=False)
                            self.history.verify({key: record[key] for key in self.REQUEST}, record['historyDigest'])
                            _, target_access, target_default = self.desired(plan)
                            binding = identity == record['actualIdentity'] == self.metadata(named) and attrs == plan['attributesDigest'] and access == target_access and default == target_default
                        finally: os.close(descriptor)
                except (OSError, self.r.Rejected, self.f.Rejected): pass
            return {'operationId': record['operationId'], 'containerId': command['containerId'], 'instanceId': command['instanceId'],
                'fingerprint': plan['fingerprint'], 'targetProof': plan['targetProof'], 'kind': plan['kind'], 'state': record['state'], 'version': record['version'],
                'aclConfirmed': record['aclConfirmed'], 'aclType': command['aclType'], 'targetMode': plan['targetMode'], 'noChange': plan['noChange'], 'bindingCurrent': binding,
                'historyMayExist': record['historyMayExist'], 'historyVerified': record['historyVerified'], 'historyDigest': record['historyDigest'], 'replayed': replayed}

    return ContainerAcl
