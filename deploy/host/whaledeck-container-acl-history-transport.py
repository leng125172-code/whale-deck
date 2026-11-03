#!/usr/bin/env python3
"""Fixed read-only ACL history helper. No old-operation replay or restore action."""
import errno
import importlib.util
import json
import os
from pathlib import Path
import re
import signal
import stat
import sys

MODULES = frozenset(('whaledeck-container-acl-transport.py', 'whaledeck-container-acl-history.py'))

class Rejected(Exception): pass

def load(filename):
    if filename not in MODULES: raise Rejected('CONTAINER_ACL_HISTORY_MODULE_UNSAFE')
    path = Path(__file__).with_name(filename); info = path.lstat()
    if not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_nlink != 1 or info.st_mode & 0o022 or info.st_size > 131072:
        raise Rejected('CONTAINER_ACL_HISTORY_MODULE_UNSAFE')
    name = 'acl_history_transport_' + filename.replace('-', '_').replace('.', '_')
    spec = importlib.util.spec_from_file_location(name, path); module = importlib.util.module_from_spec(spec); sys.modules[name] = module
    try: spec.loader.exec_module(module)
    except BaseException: sys.modules.pop(name, None); raise
    return module

def execute(action, request, reader, files, docker, engine, bridge):
    if action not in ('list', 'read') or type(request) is not dict: raise Rejected('CONTAINER_ACL_HISTORY_REQUEST_INVALID')
    expected = {'containerId', 'instanceId', 'path'} | ({'after','take','expectedVersion'} if action == 'list' else {'captureId','expectedHistoryDigest'})
    if set(request) != expected: raise Rejected('CONTAINER_ACL_HISTORY_REQUEST_INVALID')
    reader.target_binding(request['containerId'], request['instanceId']); reader.canonical_path(request['path'], empty=False)
    if reader.protected(request['path'], files): raise Rejected('CONTAINER_ACL_HISTORY_PROTECTED')
    with reader.ContainerRoot(request, files, docker) as session:
        bridge.namespace_current(reader, session)
        def target():
            try: fd = files.open_at(session.root, request['path'], os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW, cross_mount=True)
            except FileNotFoundError: raise Rejected('CONTAINER_ACL_HISTORY_TARGET_MISSING') from None
            try:
                info = os.fstat(fd)
                if not (stat.S_ISREG(info.st_mode) or stat.S_ISDIR(info.st_mode)): raise Rejected('CONTAINER_ACL_HISTORY_PROTECTED')
                bridge.require_unmapped(fd)
                return (info.st_dev, info.st_ino)
            finally: os.close(fd)
        original = target()
        result = engine.listing(request, session) if action == 'list' else engine.read(request, session)
        bridge.namespace_current(reader, session)
        if target() != original: raise Rejected('CONTAINER_ACL_HISTORY_VERSION_CONFLICT')
        return result

def main():
    if os.geteuid() != 0 or len(sys.argv) != 2 or sys.argv[1] not in ('list','read'): raise Rejected('CONTAINER_ACL_HISTORY_REQUEST_INVALID')
    invocation = os.environ.get('INVOCATION_ID', '')
    if not re.fullmatch('[a-f0-9]{32}', invocation): raise Rejected('CONTAINER_ACL_HISTORY_INVOCATION_INVALID')
    bridge = load('whaledeck-container-acl-transport.py')
    files = bridge.load('whaledeck-files.py'); reader = bridge.load('whaledeck-container-files.py'); docker = bridge.load('whaledeck-container-terminal.py')
    engine = load('whaledeck-container-acl-history.py').ContainerAclHistoryReader(bridge.create_engine(reader, files))
    with reader.ReadLease():
        files.emit({'type':'driver','invocationId':invocation})
        def expired(_signal,_frame): raise Rejected('CONTAINER_ACL_HISTORY_TIMEOUT')
        signal.signal(signal.SIGALRM, expired); signal.alarm(10)
        source = files.InputLines(sys.stdin.fileno()); request = files.line(source); source.finish()
        signal.alarm(20)
        try: files.emit(execute(sys.argv[1], request, reader, files, docker, engine, bridge))
        finally: signal.alarm(0)

def error_code(error):
    value = str(error)
    if value in ('CONTAINER_ACL_PLAN_INVALID', 'CONTAINER_ACL_ENTRIES_INVALID'): return 'CONTAINER_ACL_HISTORY_INVALID'
    if value == 'CONTAINER_ACL_MAPPING_UNPROVEN': return 'CONTAINER_ACL_HISTORY_MAPPING_UNPROVEN'
    if value in {'CONTAINER_ACL_HISTORY_' + suffix for suffix in ('REQUEST_INVALID','SESSION_REQUIRED','PROTECTED','NOT_FOUND','TARGET_MISSING','VERSION_CONFLICT','INVALID','UNSAFE','TIMEOUT','LIMIT','RESPONSE_LIMIT','MODULE_UNSAFE','INVOCATION_INVALID','ENGINE_MISMATCH','MAPPING_UNPROVEN')}:
        return value
    allowed = {'CONTAINER_PROTECTED','CONTAINER_NOT_RUNNING','CONTAINER_UNSAFE_ISOLATION','CONTAINER_UNSAFE_MOUNT',
        'CONTAINER_FILES_TARGET_MISSING','CONTAINER_FILES_INSTANCE_CHANGED','CONTAINER_FILES_TARGET_CHANGED','CONTAINER_FILES_ROOT_CHANGED','CONTAINER_FILES_SHARED_CORE_MOUNT','CONTAINER_FILES_PATH_INVALID','CONTAINER_FILES_BUSY','CONTAINER_FILES_STATE_UNSAFE','CONTAINER_FILES_CLIENT_DISCONNECTED',
        'FILES_BACKUP_UNAVAILABLE','FILES_BACKUP_METADATA_UNSAFE','FILES_BACKUP_CONFIGURATION_INVALID','FILES_BACKUP_DIRECTORY_UNSAFE','FILES_BACKUP_DEVICE_MISMATCH',
        'FILES_BACKUP_UUID_MISMATCH','FILES_BACKUP_MOUNT_REQUIRED','FILES_BACKUP_HDD_REQUIRED','FILES_BACKUP_READ_ONLY','CONTAINER_ACL_PLAN_INVALID','CONTAINER_ACL_ENTRIES_INVALID'}
    if value in allowed: return value
    if isinstance(error, OSError) and error.errno in (errno.ELOOP,errno.EXDEV,errno.ENOTDIR,errno.EACCES,errno.EPERM): return 'CONTAINER_ACL_HISTORY_PROTECTED'
    # ENOENT here may mean lost HDD/base/configuration. Only explicit missing
    # capture or current target handling above can claim NotFound.
    return 'CONTAINER_ACL_HISTORY_UNAVAILABLE'

if __name__ == '__main__':
    try: main()
    except BaseException as error:
        try: print(json.dumps({'type':'error','code':error_code(error)}), flush=True)
        except (OSError,ValueError): pass
        raise SystemExit(1) from None
