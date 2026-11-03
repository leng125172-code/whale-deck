#!/usr/bin/env python3
"""Fixed private-stdin recursive ACL transport. Root paths, modules and actions are fixed.

Only signed trusted Agent calls reach this driver. Inspect/Discard can observe
the durable old intent after its container disappears; this is never evidence
that the operation did not happen. No historical operation is physically rerun.
"""
import contextlib
import errno
import importlib.util
import json
import os
from pathlib import Path
import re
import signal
import stat
import sys

STATE = Path('/var/lib/whaledeck-container-recursive-acl')
MODULES = frozenset(('whaledeck-files.py', 'whaledeck-container-files.py', 'whaledeck-container-terminal.py',
    'whaledeck-container-upload.py', 'whaledeck-container-publish.py', 'whaledeck-file-backups.py',
    'whaledeck-container-backups.py', 'whaledeck-container-directories.py', 'whaledeck-container-permissions.py', 'whaledeck-container-acl.py', 'whaledeck-container-acl-transport.py', 'whaledeck-container-recursive-permissions.py', 'whaledeck-container-recursive-acl.py'))


class Rejected(Exception): pass


def load(filename):
    if filename not in MODULES: raise Rejected('CONTAINER_RECURSIVE_ACL_MODULE_UNSAFE')
    path = Path(__file__).with_name(filename); info = path.lstat()
    if not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_nlink != 1 or info.st_mode & 0o022 or info.st_size > 131072:
        raise Rejected('CONTAINER_RECURSIVE_ACL_MODULE_UNSAFE')
    name = 'recursive_acl_transport_' + filename.replace('-', '_').replace('.', '_')
    spec = importlib.util.spec_from_file_location(name, path); module = importlib.util.module_from_spec(spec); sys.modules[name] = module
    try: spec.loader.exec_module(module)
    except BaseException: sys.modules.pop(name, None); raise
    return module


def scoped_recursive_acl_type(base,acl_driver):
    class ScopedRecursiveAcl(base):
        @contextlib.contextmanager
        def root(self,scope,session):
            if session is None:self.reject('SESSION_REQUIRED')
            try:acl_driver.namespace_current(self.r,session)
            except acl_driver.Rejected as error:raise self.r.Rejected(str(error)) from None
            with super().root(scope,session) as descriptor:
                try:acl_driver.require_unmapped(descriptor)
                except acl_driver.Rejected as error:raise self.r.Rejected(str(error)) from None
                yield descriptor
        def child(self,scope,state_path,history):
            # Wrap the final confined child, so its parent override cannot bypass
            # namespace/FD checks (including the root directory's last ACL write).
            child=super().child(scope,state_path,history)
            return acl_driver.scoped_acl_type(type(child))(self.u,self.p,state_path,history)
    return ScopedRecursiveAcl


def execute(action, request, reader, files, docker, engine):
    if action == 'plan':
        engine.command(request)
        with reader.ContainerRoot(request, files, docker) as session: return engine.plan(request, session)
    if action not in ('apply', 'inspect', 'discard'): raise Rejected('CONTAINER_RECURSIVE_ACL_ACTION_INVALID')
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


def bind_cancellation(engine):
    # systemd stop targets only the invocation authenticated by the driver.
    # Cooperate at inode boundaries; the unit's hard deadline remains a fence
    # if a syscall/reconciliation does not finish during TimeoutStopSec.
    stopping=[False]
    def stop(_number,_frame):stopping[0]=True
    signal.signal(signal.SIGTERM,stop)
    engine.cancelled=lambda:stopping[0]


def main():
    if os.geteuid() != 0 or len(sys.argv) != 2 or sys.argv[1] not in ('plan', 'apply', 'inspect', 'discard'):
        raise Rejected('CONTAINER_RECURSIVE_ACL_ACTION_INVALID')
    invocation = os.environ.get('INVOCATION_ID', '')
    if not re.fullmatch('[a-f0-9]{32}', invocation): raise Rejected('CONTAINER_RECURSIVE_ACL_INVOCATION_INVALID')
    files, reader = load('whaledeck-files.py'), load('whaledeck-container-files.py')
    docker = load('whaledeck-container-terminal.py')
    upload = load('whaledeck-container-upload.py').ContainerUploads(reader, files, Path('/var/lib/whaledeck-container-uploads'))
    storage = load('whaledeck-file-backups.py').FileBackups(files, namespace='container-files-v1')
    captures = load('whaledeck-container-backups.py').ContainerBackups(upload, storage)
    publisher = load('whaledeck-container-publish.py').ContainerPublisher(upload, captures)
    acl = load('whaledeck-container-acl.py')
    permission = load('whaledeck-container-permissions.py')
    acl_driver = load('whaledeck-container-acl-transport.py')
    recursive = load('whaledeck-container-recursive-permissions.py')
    profile = load('whaledeck-container-recursive-acl.py')
    metadata = load('whaledeck-file-backups.py').FileBackups(files, namespace=None)
    directories = load('whaledeck-container-directories.py').ContainerDirectories
    engine_type = scoped_recursive_acl_type(profile.recursive_acl_type(acl, permission, directories, recursive), acl_driver)
    engine = engine_type(upload, publisher, STATE, profile.recursive_acl_history_type(recursive)(metadata))
    bind_cancellation(engine)
    files.emit({'type': 'driver', 'invocationId': invocation})
    def expired(_number, _frame): raise Rejected('CONTAINER_RECURSIVE_ACL_TIMEOUT')
    signal.signal(signal.SIGALRM, expired); signal.alarm(10)
    source = files.InputLines(sys.stdin.fileno()); request = files.line(source); source.finish()
    signal.alarm(310)
    try: files.emit(execute(sys.argv[1], request, reader, files, docker, engine))
    finally: signal.alarm(0)


def error_code(error):
    codes = {'CONTAINER_RECURSIVE_ACL_' + suffix for suffix in ('ACTION_INVALID', 'REQUEST_INVALID', 'INVOCATION_INVALID', 'TIMEOUT',
        'MODULE_UNSAFE', 'TYPE_INVALID', 'ENTRIES_INVALID', 'DEFAULT_FILE_ENTRIES_INVALID', 'MAPPING_UNPROVEN', 'UNSUPPORTED', 'ACL_INVALID', 'PLAN_INVALID', 'PROTECTED', 'TARGET_EXISTS', 'VERSION_CONFLICT', 'PARENT_CHANGED',
        'TARGET_CHANGED', 'OWNER_CHANGED', 'STATE_UNSAFE', 'BUSY', 'NOT_FOUND', 'IDEMPOTENCY_CONFLICT', 'RECEIPT_LIMIT',
        'RECEIPT_INVALID', 'PRIVATE_UNSAFE', 'PRIVATE_CHANGED', 'CAPACITY_LOW', 'VERSION_INVALID', 'SOURCE_UNSUPPORTED', 'ATTRIBUTES_LIMIT', 'EXTENDED_METADATA_REQUIRED',
        'HISTORY_UNSAFE', 'HISTORY_CHANGED', 'HISTORY_LIMIT', 'HISTORY_CAPACITY', 'SESSION_REQUIRED')}
    codes |= {'CONTAINER_PROTECTED', 'CONTAINER_NOT_RUNNING', 'CONTAINER_UNSAFE_ISOLATION', 'CONTAINER_UNSAFE_MOUNT',
        'CONTAINER_FILES_TARGET_MISSING', 'CONTAINER_FILES_INSTANCE_CHANGED', 'CONTAINER_FILES_TARGET_CHANGED', 'CONTAINER_FILES_ROOT_CHANGED',
        'CONTAINER_FILES_SHARED_CORE_MOUNT', 'CONTAINER_FILES_PATH_INVALID', 'CONTAINER_UPLOAD_PUBLISH_READ_ONLY',
        'CONTAINER_UPLOAD_PUBLISH_MOUNT_ROOT', 'FILES_INPUT_TIMEOUT', 'FILES_BACKUP_TARGET_CHANGED', 'FILES_BACKUP_NOT_HDD'}
    codes |= {'CONTAINER_RECURSIVE_ACL_' + suffix for suffix in ('LIMITS_INVALID', 'TREE_LIMIT', 'MANIFEST_LIMIT',
        'TREE_ALIAS_OR_MOUNT', 'TREE_CHANGED', 'ROOT_CHANGED', 'ENTRY_SCOPE_INVALID', 'ENTRY_RESULT_UNKNOWN', 'CANCELED', 'HISTORY_DOMAIN')}
    value = str(error)
    if value.startswith('CONTAINER_ACL_'): value = 'CONTAINER_RECURSIVE_ACL_' + value[len('CONTAINER_ACL_'):]
    if value in codes: return value
    if isinstance(error, OSError):
        if error.errno in (errno.ENOTSUP, errno.EOPNOTSUPP): return 'CONTAINER_RECURSIVE_ACL_UNSUPPORTED'
        if error.errno in (errno.ELOOP, errno.EXDEV, errno.ENOTDIR, errno.EACCES, errno.EPERM): return 'CONTAINER_RECURSIVE_ACL_PROTECTED'
        if error.errno == errno.EEXIST: return 'CONTAINER_RECURSIVE_ACL_TARGET_EXISTS'
        if error.errno == errno.ENOSPC: return 'CONTAINER_RECURSIVE_ACL_CAPACITY_LOW'
    return 'CONTAINER_RECURSIVE_ACL_UNAVAILABLE'


if __name__ == '__main__':
    try: main()
    except BaseException as error:
        try: print(json.dumps({'type': 'error', 'code': error_code(error)}), flush=True)
        except (OSError, ValueError): pass
        raise SystemExit(1) from None
