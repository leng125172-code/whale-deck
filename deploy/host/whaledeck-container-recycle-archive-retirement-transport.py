#!/usr/bin/env python3
"""Source-only fixed private-stdin HDD retirement transport; not installed.

No caller paths/modules/clocks, scheduler, cleanup or retries. Ready binds only
this invocation, not a version grant. The trusted Agent must separately verify
capability and actor/resource authorization. Missing-target replay can observe a
historical outcome; the native executor still requires a live pinned session
and fresh locked expiry proof before any new payload unlink.
"""
import errno
import importlib.util
import json
import math
import os
from pathlib import Path
import re
import signal
import stat
import sys

SOURCE_STATE = Path('/var/lib/whaledeck-container-recycling')
RESTORE_STATE = Path('/var/lib/whaledeck-container-recycle-restores')
PURGE_STATE = Path('/var/lib/whaledeck-container-recycle-purges')
ARCHIVE_STATE = Path('/var/lib/whaledeck-container-recycle-archives')
STATE = Path('/var/lib/whaledeck-container-recycle-archive-retirements')
ACTIONS = ('plan', 'reserve', 'apply', 'inspect', 'discard')
MAX_BODY = 65536
MODULES = frozenset(('whaledeck-files.py', 'whaledeck-container-files.py', 'whaledeck-container-terminal.py',
    'whaledeck-container-upload.py', 'whaledeck-container-publish.py', 'whaledeck-file-backups.py',
    'whaledeck-container-directories.py', 'whaledeck-container-permissions.py', 'whaledeck-container-recycling.py', 'whaledeck-container-recycle-restores.py', 'whaledeck-container-recycle-purge-plan.py', 'whaledeck-container-recycle-purge-fences.py', 'whaledeck-container-recycle-purges.py', 'whaledeck-container-recycle-archive-plan.py', 'whaledeck-container-recycle-archive-fences.py', 'whaledeck-container-recycle-archive-copies.py', 'whaledeck-container-recycle-archive-retention.py', 'whaledeck-container-recycle-archive-retirement-fences.py', 'whaledeck-container-recycle-archive-retirements.py'))


class Rejected(Exception): pass


def read_request(files, source):
    # Agent's cap is UTF-8 body bytes excluding the single commit line feed.
    try: raw = source.readline(MAX_BODY + 2)
    except files.Rejected as error:
        code = 'TIMEOUT' if str(error) == 'FILES_INPUT_TIMEOUT' else 'REQUEST_INVALID'
        raise Rejected('CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_' + code) from None
    if not isinstance(raw, bytes) or not raw or len(raw) > MAX_BODY + 1 or not raw.endswith(b'\n') or b'\r' in raw or b'\n' in raw[:-1]:
        raise Rejected('CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_REQUEST_INVALID')
    def nonfinite(_value): raise ValueError('nonfinite JSON')
    def finite_float(value):
        result = float(value)
        if not math.isfinite(result): raise ValueError('nonfinite JSON')
        return result
    try:
        value = json.loads(raw, object_pairs_hook=files.unique_object, parse_constant=nonfinite, parse_float=finite_float)
    except (ValueError, UnicodeDecodeError, RecursionError, files.Rejected):
        raise Rejected('CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_REQUEST_INVALID') from None
    if type(value) is not dict: raise Rejected('CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_REQUEST_INVALID')
    pending = [(value, 1)]
    while pending:
        item, depth = pending.pop()
        if isinstance(item, (dict, list)):
            if depth > 20: raise Rejected('CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_REQUEST_INVALID')
            pending.extend((child, depth + 1) for child in (item.values() if isinstance(item, dict) else item))
    return value


def load(filename):
    if filename not in MODULES: raise Rejected('CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_MODULE_UNSAFE')
    path = Path(__file__).with_name(filename); info = path.lstat()
    if not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_nlink != 1 or info.st_mode & 0o022 or info.st_size > 131072:
        raise Rejected('CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_MODULE_UNSAFE')
    name = 'recycle_archive_retirement_transport_' + filename.replace('-', '_').replace('.', '_')
    spec = importlib.util.spec_from_file_location(name, path); module = importlib.util.module_from_spec(spec); sys.modules[name] = module
    try: spec.loader.exec_module(module)
    except BaseException: sys.modules.pop(name, None); raise
    return module


def execute(action, request, reader, files, docker, engine):
    if action not in ACTIONS: raise Rejected('CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_ACTION_INVALID')
    if action == 'plan':
        engine.preflight.command(request)
        command = request['archive']['plan']['command']['purge']['plan']['command']['source']['plan']['command']
        with reader.ContainerRoot(command, files, docker) as session: return engine.plan(request, session)
    engine.request(request)
    command = request['plan']['command']['archive']['plan']['command']['purge']['plan']['command']['source']['plan']['command']
    try:
        manager = reader.ContainerRoot(command, files, docker)
        session = manager.__enter__()
    except reader.Rejected as error:
        if str(error) not in ('CONTAINER_FILES_TARGET_MISSING', 'CONTAINER_FILES_INSTANCE_CHANGED', 'CONTAINER_FILES_TARGET_CHANGED', 'CONTAINER_FILES_ROOT_CHANGED'): raise
        return getattr(engine, action)(request, None)
    except (PermissionError, ValueError) as error:
        if str(error) not in ('CONTAINER_PROTECTED', 'CONTAINER_NOT_RUNNING', 'CONTAINER_UNSAFE_ISOLATION', 'CONTAINER_UNSAFE_MOUNT'): raise
        return getattr(engine, action)(request, None)
    try: return getattr(engine, action)(request, session)
    finally: manager.__exit__(None, None, None)


def bootstrap():
    files, reader = load('whaledeck-files.py'), load('whaledeck-container-files.py')
    docker = load('whaledeck-container-terminal.py')
    upload = load('whaledeck-container-upload.py').ContainerUploads(reader, files, Path('/var/lib/whaledeck-container-uploads'))
    publisher = load('whaledeck-container-publish.py').ContainerPublisher(upload, type('UnusedCapture', (), {'u': upload})())
    base = load('whaledeck-container-permissions.py').ContainerPermissionHistory
    metadata = load('whaledeck-file-backups.py').FileBackups(files, namespace=None)
    recycle = load('whaledeck-container-recycling.py')
    history = recycle.history_type(base)(metadata)
    engine_type = recycle.recycling_type(load('whaledeck-container-directories.py').ContainerDirectories)
    source_engine = engine_type(upload, publisher, SOURCE_STATE, history, restore_claims_path=RESTORE_STATE/'claims')
    directory = load('whaledeck-container-directories.py').ContainerDirectories
    restore = load('whaledeck-container-recycle-restores.py').restores_type(directory)(source_engine, RESTORE_STATE)
    preflight = load('whaledeck-container-recycle-purge-plan.py').ContainerRecyclePurgePreflight(restore)
    fence = load('whaledeck-container-recycle-purge-fences.py').purge_fences_type(directory)
    purges = load('whaledeck-container-recycle-purges.py').purges_type(fence)(preflight, PURGE_STATE)
    archive = load('whaledeck-container-recycle-archive-plan.py').ContainerRecycleArchivePreflight(purges)
    archive_fence = load('whaledeck-container-recycle-archive-fences.py').archive_fences_type(directory)
    engine = load('whaledeck-container-recycle-archive-copies.py').archive_copies_type(archive_fence)(archive, ARCHIVE_STATE)
    retention = load('whaledeck-container-recycle-archive-retention.py').ContainerRecycleArchiveRetentionPreflight(engine)
    fences = load('whaledeck-container-recycle-archive-retirement-fences.py').retirement_fences_type(directory)
    executor = load('whaledeck-container-recycle-archive-retirements.py').archive_retirements_type(fences)(retention, STATE)
    return files, reader, docker, executor


def main():
    if os.geteuid() != 0 or len(sys.argv) != 2 or sys.argv[1] not in ACTIONS:
        raise Rejected('CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_ACTION_INVALID')
    invocation = os.environ.get('INVOCATION_ID', '')
    if not re.fullmatch('[a-f0-9]{32}', invocation): raise Rejected('CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_INVOCATION_INVALID')
    files, reader, docker, engine = bootstrap()
    files.emit({'type': 'driver', 'invocationId': invocation})
    def expired(_number, _frame): raise Rejected('CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_TIMEOUT')
    signal.signal(signal.SIGALRM, expired); signal.alarm(10)
    try:
        source = files.InputLines(sys.stdin.fileno()); request = read_request(files, source); source.finish()
        signal.alarm(110)
        files.emit(execute(sys.argv[1], request, reader, files, docker, engine))
    finally: signal.alarm(0)


def error_code(error):
    codes = {'CONTAINER_RECYCLE_' + suffix for suffix in ('ACTION_INVALID', 'REQUEST_INVALID', 'INVOCATION_INVALID', 'TIMEOUT',
        'MODULE_UNSAFE', 'PLAN_INVALID', 'PROTECTED', 'TARGET_EXISTS', 'SOURCE_CHANGED', 'PARENT_CHANGED',
        'TARGET_CHANGED', 'OWNER_CHANGED', 'STATE_UNSAFE', 'BUSY', 'NOT_FOUND', 'IDEMPOTENCY_CONFLICT', 'RECEIPT_LIMIT',
        'RECEIPT_INVALID', 'PRIVATE_UNSAFE', 'PRIVATE_CHANGED', 'CAPACITY_LOW', 'VERSION_INVALID', 'SOURCE_UNSUPPORTED', 'ATTRIBUTES_LIMIT', 'EXTENDED_METADATA_REQUIRED',
        'RESTORE_UNPROVEN','RESTORE_CLAIMED','SOURCE_CAPACITY','HISTORY_WRITE_FAILED','HISTORY_INCOMPLETE','HISTORY_STATE_CONFLICT','HISTORY_UNSAFE', 'HISTORY_CHANGED', 'HISTORY_LIMIT', 'HISTORY_CAPACITY', 'SESSION_REQUIRED','PURGE_CLAIMED')}
    codes |= {'CONTAINER_RECYCLE_PURGE_'+suffix for suffix in ('ACTION_INVALID','MODULE_UNSAFE','INVOCATION_INVALID','TIMEOUT','REQUEST_INVALID','SOURCE_INVALID','PLAN_INVALID','NEW_OPERATION_REQUIRED','RECEIPT_INVALID','RECEIPT_LIMIT','STATE_UNSAFE','SOURCE_BUSY','SOURCE_CHANGED','TARGET_EXISTS','SESSION_REQUIRED','PLAN_CHANGED','NOT_FOUND','IDEMPOTENCY_CONFLICT','BUSY','NOT_RESERVED','FENCE_CHANGED','HISTORY_CHANGED','SOURCE_CHANGED','TARGET_CHANGED','OWNER_CHANGED','PARENT_CHANGED','PRIVATE_UNSAFE','PRIVATE_CHANGED','TARGET_EXISTS','PROTECTED','STATE_UNSAFE')}
    codes |= {'CONTAINER_PROTECTED', 'CONTAINER_NOT_RUNNING', 'CONTAINER_UNSAFE_ISOLATION', 'CONTAINER_UNSAFE_MOUNT',
        'CONTAINER_FILES_TARGET_MISSING', 'CONTAINER_FILES_INSTANCE_CHANGED', 'CONTAINER_FILES_TARGET_CHANGED', 'CONTAINER_FILES_ROOT_CHANGED',
        'CONTAINER_FILES_SHARED_CORE_MOUNT', 'CONTAINER_FILES_PATH_INVALID', 'CONTAINER_UPLOAD_PUBLISH_READ_ONLY',
        'CONTAINER_UPLOAD_PUBLISH_MOUNT_ROOT', 'FILES_INPUT_TIMEOUT', 'FILES_BACKUP_TARGET_CHANGED', 'FILES_BACKUP_NOT_HDD'}
    codes |= {'FILES_BACKUP_HISTORY_LIMIT', 'FILES_BACKUP_DEVICE_MISMATCH', 'FILES_BACKUP_CONTENT_INVALID', 'FILES_BACKUP_HDD_REQUIRED', 'FILES_BACKUP_METADATA_UNSAFE', 'FILES_BACKUP_DIRECTORY_UNSAFE', 'FILES_BACKUP_READ_ONLY', 'FILES_BACKUP_WRITE_FAILED', 'FILES_BACKUP_DEVICE_METADATA_UNSAFE', 'FILES_BACKUP_MOUNT_MISMATCH', 'FILES_BACKUP_IDEMPOTENCY_CONFLICT', 'FILES_BACKUP_UNAVAILABLE', 'FILES_BACKUP_FILESYSTEM_UNSUPPORTED', 'FILES_BACKUP_CONFIGURATION_INVALID', 'FILES_BACKUP_CAPACITY_REQUIRED', 'FILES_BACKUP_INCOMPLETE'}
    codes |= {x.replace('CONTAINER_RECYCLE_PURGE_', 'CONTAINER_RECYCLE_RESTORE_') for x in codes if x.startswith('CONTAINER_RECYCLE_PURGE_')}
    codes |= {'CONTAINER_RECYCLE_ARCHIVE_'+suffix for suffix in ('ACTION_INVALID','MODULE_UNSAFE','INVOCATION_INVALID','TIMEOUT','REQUEST_INVALID','PLAN_INVALID','NEW_OPERATION_REQUIRED','RECEIPT_INVALID','RECEIPT_LIMIT','STATE_UNSAFE','SOURCE_BUSY','SOURCE_CHANGED','SOURCE_NOT_FOUND','TARGET_EXISTS','SESSION_REQUIRED','PLAN_CHANGED','NOT_FOUND','IDEMPOTENCY_CONFLICT','BUSY','NOT_RESERVED','FENCE_CHANGED','HISTORY_CHANGED','TARGET_CHANGED','OWNER_CHANGED','PARENT_CHANGED','PRIVATE_UNSAFE','PRIVATE_CHANGED','PROTECTED','PURGE_UNCONFIRMED','COPY_CHANGED','COPY_CAPACITY','COPY_WRITE_FAILED','DIRECTORY_LIMIT','PRIVATE_EXISTS','RESERVATION_ONLY')}
    codes |= {'CONTAINER_RECYCLE_ARCHIVE_RETENTION_'+suffix for suffix in ('REQUEST_INVALID','SESSION_REQUIRED','COPY_UNCONFIRMED','PURGE_UNCONFIRMED','SOURCE_CHANGED','FENCE_CHANGED','HISTORY_CHANGED','TIME_RANGE','CLOCK_BEFORE_CAPTURE','COPY_CHANGED','PLAN_INVALID','PLAN_CHANGED')}
    codes |= {'CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_'+suffix for suffix in ('ACTION_INVALID','MODULE_UNSAFE','INVOCATION_INVALID','TIMEOUT','REQUEST_INVALID','PLAN_INVALID','NEW_OPERATION_REQUIRED','RECEIPT_INVALID','RECEIPT_LIMIT','STATE_UNSAFE','SOURCE_BUSY','MARKER_INVALID','NOT_FOUND','IDEMPOTENCY_CONFLICT','BUSY','FENCE_CHANGED','SESSION_REQUIRED','NOT_RESERVED','NOT_ELIGIBLE','NOT_DUE','HISTORY_CHANGED','COPY_CHANGED','COMPLETION_INVALID','COPY_UNAVAILABLE','PLAN_CHANGED','RESERVATION_ONLY')}
    value = str(error)
    if value in codes: return value
    if isinstance(error, OSError):
        if error.errno in (errno.ELOOP, errno.EXDEV, errno.ENOTDIR, errno.EACCES, errno.EPERM): return 'CONTAINER_RECYCLE_PROTECTED'
        if error.errno == errno.EEXIST: return 'CONTAINER_RECYCLE_TARGET_EXISTS'
        if error.errno == errno.ENOSPC: return 'CONTAINER_RECYCLE_SOURCE_CAPACITY'
    return 'CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_UNAVAILABLE'


if __name__ == '__main__':
    try: main()
    except BaseException as error:
        try: print(json.dumps({'type': 'error', 'code': error_code(error)}), flush=True)
        except (OSError, ValueError): pass
        raise SystemExit(1) from None
