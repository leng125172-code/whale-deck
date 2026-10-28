#!/usr/bin/env python3
"""Fixed private-stdin container copy transport, not an authorization grant.

The Agent's signed capability/helper integration must precede installation.
Only enumerated actions and trusted adjacent modules; no command/path/module
parameters. Historic receipts remain observable after a known target loss.
"""
import errno
import importlib.util
import json
import os
from pathlib import Path
import re
import signal
import stat
import sys

STATE = Path('/var/lib/whaledeck-container-copies')
MODULES = frozenset(('whaledeck-files.py', 'whaledeck-container-files.py', 'whaledeck-container-terminal.py',
    'whaledeck-container-upload.py', 'whaledeck-container-publish.py', 'whaledeck-file-backups.py',
    'whaledeck-container-backups.py', 'whaledeck-container-copy-scope.py', 'whaledeck-container-copies.py',
    'whaledeck-file-operations.py', 'whaledeck-tree-copies.py', 'whaledeck-file-tree.py'))
TARGET_LOSS = frozenset(('CONTAINER_FILES_TARGET_MISSING', 'CONTAINER_FILES_INSTANCE_CHANGED',
    'CONTAINER_FILES_TARGET_CHANGED', 'CONTAINER_FILES_ROOT_CHANGED', 'CONTAINER_PROTECTED', 'CONTAINER_NOT_RUNNING',
    'CONTAINER_UNSAFE_ISOLATION', 'CONTAINER_UNSAFE_MOUNT'))


class Rejected(Exception): pass


def load(filename):
    if filename not in MODULES: raise Rejected('CONTAINER_COPY_MODULE_UNSAFE')
    path = Path(__file__).with_name(filename); info = path.lstat()
    if not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_nlink != 1 or info.st_mode & 0o022 or info.st_size > 131072:
        raise Rejected('CONTAINER_COPY_MODULE_UNSAFE')
    name = 'copy_transport_' + filename.replace('-', '_').replace('.', '_')
    specification = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(specification); sys.modules[name] = module
    try: specification.loader.exec_module(module)
    except BaseException: sys.modules.pop(name, None); raise
    return module


def execute(action, request, reader, files, docker, engine):
    if action not in ('plan', 'apply', 'inspect', 'discard'): raise Rejected('CONTAINER_COPY_ACTION_INVALID')
    if action == 'plan': engine.command(request); command = request
    else: engine.request(request); command = request['plan']['command']
    try:
        manager = reader.ContainerRoot(command, files, docker); session = manager.__enter__()
    except (reader.Rejected, PermissionError, ValueError) as error:
        # No generic filesystem/socket exception is evidence of target loss.
        if action == 'plan' or str(error) not in TARGET_LOSS: raise
        return getattr(engine, action)(request, None)
    try: return getattr(engine, action)(request, session)
    finally: manager.__exit__(None, None, None)


def main():
    if os.geteuid() != 0 or len(sys.argv) != 2 or sys.argv[1] not in ('plan', 'apply', 'inspect', 'discard'):
        raise Rejected('CONTAINER_COPY_ACTION_INVALID')
    invocation = os.environ.get('INVOCATION_ID', '')
    if not re.fullmatch('[a-f0-9]{32}', invocation): raise Rejected('CONTAINER_COPY_INVOCATION_INVALID')
    files, reader = load('whaledeck-files.py'), load('whaledeck-container-files.py')
    docker = load('whaledeck-container-terminal.py')
    upload = load('whaledeck-container-upload.py').ContainerUploads(reader, files, Path('/var/lib/whaledeck-container-uploads'))
    storage = load('whaledeck-file-backups.py').FileBackups(files, namespace='container-files-v1')
    captures = load('whaledeck-container-backups.py').ContainerBackups(upload, storage)
    publisher = load('whaledeck-container-publish.py').ContainerPublisher(upload, captures)
    engine = load('whaledeck-container-copies.py').ContainerCopies(upload, publisher,
        load('whaledeck-container-copy-scope.py').ContainerCopyScope, load('whaledeck-file-operations.py').FileOperations,
        load('whaledeck-tree-copies.py').FileTreeCopies, load('whaledeck-file-tree.py').FileTreeManifest, STATE)
    files.emit({'type': 'driver', 'invocationId': invocation})
    def expired(_number, _frame): raise Rejected('CONTAINER_COPY_TIMEOUT')
    signal.signal(signal.SIGALRM, expired); signal.alarm(10)
    source = files.InputLines(sys.stdin.fileno()); request = files.line(source); source.finish()
    signal.alarm(600)
    try: files.emit(execute(sys.argv[1], request, reader, files, docker, engine))
    finally: signal.alarm(0)


def error_code(error):
    codes = {'CONTAINER_COPY_' + suffix for suffix in ('ACTION_INVALID', 'INVOCATION_INVALID', 'TIMEOUT', 'MODULE_UNSAFE',
        'REQUEST_INVALID', 'PLAN_INVALID', 'BINDING_INVALID', 'VERSION_INVALID', 'VERSION_CONFLICT', 'PROTECTED', 'OVERLAP',
        'PARENT_INVALID', 'PARENT_CHANGED', 'TARGET_CHANGED', 'TARGET_UNAVAILABLE', 'OWNER_CHANGED', 'STATE_UNSAFE', 'BUSY',
        'NOT_FOUND', 'IDEMPOTENCY_CONFLICT', 'RECEIPT_INVALID', 'RECEIPT_LIMIT', 'SPECIAL_MODE_UNSUPPORTED',
        'LOCATION_INVALID', 'PRIVATE_UNSAFE', 'PUBLICATION_INVALID')}
    codes |= TARGET_LOSS | {'CONTAINER_FILES_PATH_INVALID', 'CONTAINER_FILES_ID_REQUIRED', 'CONTAINER_FILES_INSTANCE_INVALID',
        'CONTAINER_FILES_SHARED_CORE_MOUNT', 'CONTAINER_UPLOAD_PUBLISH_READ_ONLY', 'CONTAINER_UPLOAD_PUBLISH_MOUNT_ROOT',
        'FILES_VERSION_CONFLICT', 'FILES_PARENT_CHANGED', 'FILES_READ_CHANGED', 'FILES_TREE_CHANGED', 'FILES_TREE_OVERLAP',
        'FILES_TREE_CANCELED', 'FILES_TREE_DEADLINE', 'FILES_CAPACITY_REQUIRED', 'FILES_SIZE_LIMIT', 'FILES_TREE_SIZE_LIMIT',
        'FILES_TREE_ENTRY_LIMIT', 'FILES_TREE_DEPTH_LIMIT', 'FILES_TREE_MANIFEST_LIMIT', 'FILES_TREE_PROTECTED',
        'FILES_REGULAR_SINGLE_LINK_REQUIRED', 'FILES_OPERATION_BUSY', 'FILES_RECEIPT_UNSAFE', 'FILES_INPUT_TIMEOUT'}
    value = str(error)
    if value in codes: return value
    if isinstance(error, OSError):
        if error.errno in (errno.ELOOP, errno.EXDEV, errno.ENOTDIR, errno.EACCES, errno.EPERM): return 'CONTAINER_COPY_PROTECTED'
        if error.errno == errno.EEXIST: return 'CONTAINER_COPY_VERSION_CONFLICT'
        if error.errno == errno.ENOSPC: return 'FILES_CAPACITY_REQUIRED'
    return 'CONTAINER_COPY_UNAVAILABLE'


if __name__ == '__main__':
    try: main()
    except BaseException as error:
        try: print(json.dumps({'type': 'error', 'code': error_code(error)}), flush=True)
        except (OSError, ValueError): pass
        raise SystemExit(1) from None
