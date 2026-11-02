#!/usr/bin/env python3
"""Fixed private-stdin POSIX ACL transport. Root paths, modules and actions are fixed.

Only signed trusted Agent calls reach this driver. Inspect/Discard can observe
the durable old intent after its container disappears; this is never evidence
that the operation did not happen. No historical operation is physically rerun.
"""
import ctypes
import contextlib
import struct
import errno
import importlib.util
import json
import os
from pathlib import Path
import re
import signal
import stat
import sys

STATE = Path('/var/lib/whaledeck-container-acl')
MODULES = frozenset(('whaledeck-files.py', 'whaledeck-container-files.py', 'whaledeck-container-terminal.py',
    'whaledeck-container-upload.py', 'whaledeck-container-publish.py', 'whaledeck-file-backups.py',
    'whaledeck-container-backups.py', 'whaledeck-container-directories.py', 'whaledeck-container-permissions.py', 'whaledeck-container-acl.py'))


class Rejected(Exception): pass


def load(filename):
    if filename not in MODULES: raise Rejected('CONTAINER_ACL_MODULE_UNSAFE')
    path = Path(__file__).with_name(filename); info = path.lstat()
    if not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_nlink != 1 or info.st_mode & 0o022 or info.st_size > 131072:
        raise Rejected('CONTAINER_ACL_MODULE_UNSAFE')
    name = 'acl_transport_' + filename.replace('-', '_').replace('.', '_')
    spec = importlib.util.spec_from_file_location(name, path); module = importlib.util.module_from_spec(spec); sys.modules[name] = module
    try: spec.loader.exec_module(module)
    except BaseException: sys.modules.pop(name, None); raise
    return module


def mount_attributes(descriptor):
    # Linux UAPI statmount(2), mnt_id_req VER1, BY_FD. Never guess from path,
    # Docker usernames or filesystem type. Missing syscall/result fails closed.
    if os.uname().machine not in ('x86_64', 'aarch64'): raise Rejected('CONTAINER_ACL_MAPPING_UNPROVEN')
    request = ctypes.create_string_buffer(struct.pack('<IIQQQ', 32, descriptor, 0, 2, 0))
    output = ctypes.create_string_buffer(512)
    libc = ctypes.CDLL(None, use_errno=True); libc.syscall.restype = ctypes.c_long
    result = libc.syscall(ctypes.c_long(457), ctypes.byref(request), ctypes.byref(output), ctypes.c_size_t(512), ctypes.c_uint(1))
    if result != 0: raise Rejected('CONTAINER_ACL_MAPPING_UNPROVEN')
    size, mask, attributes = struct.unpack_from('<I', output.raw)[0], struct.unpack_from('<Q', output.raw, 8)[0], struct.unpack_from('<Q', output.raw, 64)[0]
    if not 80 <= size <= 512 or mask & 2 != 2: raise Rejected('CONTAINER_ACL_MAPPING_UNPROVEN')
    return attributes


def require_unmapped(descriptor):
    if mount_attributes(descriptor) & 0x00100000: raise Rejected('CONTAINER_ACL_MAPPING_UNPROVEN')


def namespace_current(reader, session):
    session.current()
    pid = reader.target_binding(session.request['containerId'], session.request['instanceId'])
    try:
        namespaces = [os.stat(path).st_ino for path in ('/proc/1/ns/user', '/proc/self/ns/user', '/proc/'+str(pid)+'/ns/user')]
        if len(set(namespaces)) != 1: raise Rejected('CONTAINER_ACL_MAPPING_UNPROVEN')
    except OSError: raise Rejected('CONTAINER_ACL_MAPPING_UNPROVEN') from None
    session.current()


def scoped_acl_type(base):
    class ScopedAcl(base):
        @contextlib.contextmanager
        def parent(self, plan, session):
            if session is None: self.reject('SESSION_REQUIRED')
            try: namespace_current(self.r, session)
            except Rejected as error: raise self.r.Rejected(str(error)) from None
            with super().parent(plan, session) as descriptor:
                try: require_unmapped(descriptor)
                except Rejected as error: raise self.r.Rejected(str(error)) from None
                yield descriptor

        def snapshot(self, descriptor):
            try: require_unmapped(descriptor)
            except Rejected as error: raise self.r.Rejected(str(error)) from None
            return super().snapshot(descriptor)
    return ScopedAcl


def execute(action, request, reader, files, docker, engine):
    if action == 'plan':
        engine.command(request)
        with reader.ContainerRoot(request, files, docker) as session: return engine.plan(request, session)
    if action not in ('apply', 'inspect', 'discard'): raise Rejected('CONTAINER_ACL_ACTION_INVALID')
    engine.request(request)
    command = request['plan']['command']
    # Target disappearance/restart must not turn an old operation into a
    # NOT_FOUND reply. A generic Docker/HDD/socket failure still fails closed.
    try:
        manager = reader.ContainerRoot(command, files, docker)
        session = manager.__enter__()
    except reader.Rejected as error:
        if str(error) not in ('CONTAINER_FILES_TARGET_MISSING', 'CONTAINER_FILES_INSTANCE_CHANGED', 'CONTAINER_FILES_TARGET_CHANGED', 'CONTAINER_FILES_ROOT_CHANGED'):
            raise
        return getattr(engine, action)(request, None)
    except (PermissionError, ValueError) as error:
        if str(error) not in ('CONTAINER_PROTECTED', 'CONTAINER_NOT_RUNNING', 'CONTAINER_UNSAFE_ISOLATION', 'CONTAINER_UNSAFE_MOUNT'): raise
        return getattr(engine, action)(request, None)
    try: return getattr(engine, action)(request, session)
    finally: manager.__exit__(None, None, None)


def create_engine(reader, files):
    upload = load('whaledeck-container-upload.py').ContainerUploads(reader, files, Path('/var/lib/whaledeck-container-uploads'))
    storage = load('whaledeck-file-backups.py').FileBackups(files, namespace='container-files-v1')
    captures = load('whaledeck-container-backups.py').ContainerBackups(upload, storage)
    publisher = load('whaledeck-container-publish.py').ContainerPublisher(upload, captures)
    acl = load('whaledeck-container-acl.py')
    permission = load('whaledeck-container-permissions.py')
    metadata = load('whaledeck-file-backups.py').FileBackups(files, namespace=None)
    engine_type = scoped_acl_type(acl.container_acl_type(load('whaledeck-container-directories.py').ContainerDirectories))
    engine = engine_type(upload, publisher, STATE, acl.acl_history_type(permission)(metadata))
    return engine


def main():
    if os.geteuid() != 0 or len(sys.argv) != 2 or sys.argv[1] not in ('plan', 'apply', 'inspect', 'discard'):
        raise Rejected('CONTAINER_ACL_ACTION_INVALID')
    invocation = os.environ.get('INVOCATION_ID', '')
    if not re.fullmatch('[a-f0-9]{32}', invocation): raise Rejected('CONTAINER_ACL_INVOCATION_INVALID')
    files, reader = load('whaledeck-files.py'), load('whaledeck-container-files.py')
    docker = load('whaledeck-container-terminal.py')
    engine = create_engine(reader, files)
    files.emit({'type': 'driver', 'invocationId': invocation})
    def expired(_number, _frame): raise Rejected('CONTAINER_ACL_TIMEOUT')
    signal.signal(signal.SIGALRM, expired); signal.alarm(10)
    source = files.InputLines(sys.stdin.fileno()); request = files.line(source); source.finish()
    signal.alarm(50)
    try: files.emit(execute(sys.argv[1], request, reader, files, docker, engine))
    finally: signal.alarm(0)


def error_code(error):
    codes = {'CONTAINER_ACL_' + suffix for suffix in ('ACTION_INVALID', 'REQUEST_INVALID', 'INVOCATION_INVALID', 'TIMEOUT',
        'UNSUPPORTED', 'MODULE_UNSAFE', 'TYPE_INVALID', 'ENTRIES_INVALID', 'DEFAULT_REQUIRES_DIRECTORY', 'ACL_INVALID', 'MAPPING_UNPROVEN', 'PLAN_INVALID', 'PROTECTED', 'TARGET_EXISTS', 'VERSION_CONFLICT', 'PARENT_CHANGED',
        'TARGET_CHANGED', 'OWNER_CHANGED', 'STATE_UNSAFE', 'BUSY', 'NOT_FOUND', 'IDEMPOTENCY_CONFLICT', 'RECEIPT_LIMIT',
        'RECEIPT_INVALID', 'PRIVATE_UNSAFE', 'PRIVATE_CHANGED', 'CAPACITY_LOW', 'VERSION_INVALID', 'SOURCE_UNSUPPORTED', 'ATTRIBUTES_LIMIT', 'EXTENDED_METADATA_REQUIRED',
        'HISTORY_UNSAFE', 'HISTORY_CHANGED', 'HISTORY_LIMIT', 'HISTORY_CAPACITY', 'SESSION_REQUIRED')}
    codes |= {'CONTAINER_PROTECTED', 'CONTAINER_NOT_RUNNING', 'CONTAINER_UNSAFE_ISOLATION', 'CONTAINER_UNSAFE_MOUNT',
        'CONTAINER_FILES_TARGET_MISSING', 'CONTAINER_FILES_INSTANCE_CHANGED', 'CONTAINER_FILES_TARGET_CHANGED', 'CONTAINER_FILES_ROOT_CHANGED',
        'CONTAINER_FILES_SHARED_CORE_MOUNT', 'CONTAINER_FILES_PATH_INVALID', 'CONTAINER_UPLOAD_PUBLISH_READ_ONLY',
        'CONTAINER_UPLOAD_PUBLISH_MOUNT_ROOT', 'FILES_INPUT_TIMEOUT', 'FILES_BACKUP_TARGET_CHANGED', 'FILES_BACKUP_NOT_HDD'}
    value = str(error)
    if value in codes: return value
    if isinstance(error, OSError):
        if error.errno in (errno.ENOTSUP, errno.EOPNOTSUPP): return 'CONTAINER_ACL_UNSUPPORTED'
        if error.errno in (errno.ELOOP, errno.EXDEV, errno.ENOTDIR, errno.EACCES, errno.EPERM): return 'CONTAINER_ACL_PROTECTED'
        if error.errno == errno.EEXIST: return 'CONTAINER_ACL_TARGET_EXISTS'
        if error.errno == errno.ENOSPC: return 'CONTAINER_ACL_CAPACITY_LOW'
    return 'CONTAINER_ACL_UNAVAILABLE'


if __name__ == '__main__':
    try: main()
    except BaseException as error:
        try: print(json.dumps({'type': 'error', 'code': error_code(error)}), flush=True)
        except (OSError, ValueError): pass
        raise SystemExit(1) from None
