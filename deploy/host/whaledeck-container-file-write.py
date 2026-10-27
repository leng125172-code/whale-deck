#!/usr/bin/env python3
"""Fixed, private-stdin container write transport. No paths/commands in argv.

Only the trusted Agent may call the root helper. Every target is re-resolved by
the protected ContainerRoot; public requests require the planned target proof.
"""
import contextlib
import errno
import fcntl
import importlib.util
import json
import os
from pathlib import Path
import re
import signal
import stat
import sys

STATE = Path('/var/lib/whaledeck-container-uploads')
MODULES = frozenset(('whaledeck-files.py', 'whaledeck-container-terminal.py', 'whaledeck-container-files.py',
    'whaledeck-container-upload.py', 'whaledeck-file-backups.py', 'whaledeck-container-backups.py', 'whaledeck-container-publish.py',
    'whaledeck-container-history.py', 'whaledeck-container-restore.py'))


class Rejected(Exception): pass


def load(filename):
    if filename not in MODULES: raise Rejected('CONTAINER_UPLOAD_MODULE_UNSAFE')
    path = Path(__file__).with_name(filename); info = path.lstat()
    if not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_nlink != 1 or info.st_mode & 0o022 or info.st_size > 131072:
        raise Rejected('CONTAINER_UPLOAD_MODULE_UNSAFE')
    name = 'container_write_' + filename.replace('-', '_').replace('.', '_')
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec); sys.modules[name] = module
    try: spec.loader.exec_module(module)
    except BaseException: sys.modules.pop(name, None); raise
    return module


@contextlib.contextmanager
def lease(files):
    state = files.trusted_directory(STATE); descriptor = None
    try:
        if stat.S_IMODE(os.fstat(state).st_mode) != 0o700: raise Rejected('CONTAINER_UPLOAD_STATE_UNSAFE')
        for index in range(2):
            candidate = os.open('transport-slot-' + str(index) + '.lock', os.O_RDWR | os.O_CREAT | os.O_NOFOLLOW | os.O_CLOEXEC,
                0o600, dir_fd=state)
            try:
                info = os.fstat(candidate)
                if not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_nlink != 1 or stat.S_IMODE(info.st_mode) != 0o600 or info.st_size != 0:
                    raise Rejected('CONTAINER_UPLOAD_STATE_UNSAFE')
                try: fcntl.flock(candidate, fcntl.LOCK_EX | fcntl.LOCK_NB)
                except BlockingIOError: continue
                descriptor, candidate = candidate, None; break
            finally:
                if candidate is not None: os.close(candidate)
        if descriptor is None: raise Rejected('CONTAINER_UPLOAD_BUSY')
        yield
    finally:
        if descriptor is not None: os.close(descriptor)
        os.close(state)


def snapshot(request, session, uploads, publisher):
    reader, files = uploads.r, uploads.f
    reader.request_fields(request, 'stat')
    if not request['path'] or reader.protected(request['path'], files): raise Rejected('CONTAINER_UPLOAD_PROTECTED')
    parent_path, _, leaf = request['path'].rpartition('/')
    parent = files.open_at(session.root, parent_path or '.', os.O_RDONLY | os.O_DIRECTORY, cross_mount=True)
    try:
        session.revalidate(); publisher.writable({'request': request}, session, parent)
        # NO_XDEV also refuses implicit Docker file mounts (e.g. hosts), not
        # just the explicit configured directory mounts checked by the reader.
        current = files.current(parent, leaf)
        proof = uploads.target_proof(session)
        result = {'containerId': request['containerId'], 'instanceId': request['instanceId'], 'path': request['path'],
            'targetProof': proof, 'expectedParentVersion': reader.version(request, parent_path, os.fstat(parent)),
            'expectedVersion': 'ABSENT' if current is None else reader.version(request, request['path'], current),
            'currentSizeBytes': 0 if current is None else current.st_size}
        session.revalidate()
        if uploads.target_proof(session) != proof: raise Rejected('CONTAINER_UPLOAD_TARGET_CHANGED')
        return result
    finally: os.close(parent)


def execute(action, request, source, reader, files, docker, uploads, publisher):
    if action in ('recovery-observe', 'recovery-seal'):
        # The class comes from the same root-owned fixed upload module.
        engine = uploads.recovery_engine()
        if action == 'recovery-observe':
            source.finish(); return engine.observe(request)
        fields = {'containerId', 'instanceId', 'operationId', 'owner', 'expectedTargetProof',
            'recoveryId', 'actorSubject', 'traceId', 'expectedObservationHash'}
        if type(request) is not dict or set(request) != fields: raise Rejected('CONTAINER_UPLOAD_RECOVERY_REQUEST_INVALID')
        source.finish()
        scope = {key: request[key] for key in engine.SCOPE}
        return engine.seal(scope, request['recoveryId'], request['actorSubject'], request['traceId'], request['expectedObservationHash'])
    if action == 'target': reader.request_fields(request, 'stat')
    elif action == 'stage':
        if type(request) is not dict or 'expectedTargetProof' not in request: raise Rejected('CONTAINER_UPLOAD_REQUEST_INVALID')
        uploads.upload_request(request)
    else:
        if type(request) is not dict or set(request) != {'containerId', 'instanceId', 'operationId', 'owner', 'expectedTargetProof'}:
            raise Rejected('CONTAINER_UPLOAD_REQUEST_INVALID')
        if type(request['expectedTargetProof']) is not str or not re.fullmatch('[A-F0-9]{64}', request['expectedTargetProof']):
            raise Rejected('CONTAINER_UPLOAD_REQUEST_INVALID')
        scope = {key: request[key] for key in ('containerId', 'instanceId', 'operationId', 'owner')}
        uploads.scope_request(scope)
    if action != 'stage': source.finish()
    with reader.ContainerRoot(request, files, docker) as session:
        if action == 'target':
            try: return snapshot(request, session, uploads, publisher)
            except FileNotFoundError: raise Rejected('CONTAINER_UPLOAD_PATH_NOT_FOUND') from None
        if action == 'stage': return uploads.stage(request, session, source)
        if action == 'discard':
            prevented = uploads.prevent_missing(scope, session, request['expectedTargetProof'])
            if prevented is not None: return prevented
        with uploads.locked(scope['operationId']) as state:
            record = uploads.scoped(state, scope)
            if record['targetProof'] != request['expectedTargetProof']: raise Rejected('CONTAINER_UPLOAD_TARGET_CHANGED')
        if action == 'publish': return publisher.publish(scope, session)
        if action == 'inspect': return publisher.inspect(scope, session)
        if action == 'discard': return publisher.discard(scope, session)
        raise Rejected('CONTAINER_UPLOAD_ACTION_INVALID')


def main():
    if os.geteuid() != 0 or len(sys.argv) != 2 or sys.argv[1] not in ('target', 'stage', 'publish', 'inspect', 'discard', 'recovery-observe', 'recovery-seal'):
        raise Rejected('CONTAINER_UPLOAD_ACTION_INVALID')
    invocation = os.environ.get('INVOCATION_ID', '')
    if not re.fullmatch('[a-f0-9]{32}', invocation): raise Rejected('CONTAINER_UPLOAD_INVOCATION_INVALID')
    files, reader = load('whaledeck-files.py'), load('whaledeck-container-files.py')
    docker = load('whaledeck-container-terminal.py')
    uploads = load('whaledeck-container-upload.py').ContainerUploads(reader, files, STATE)
    storage = load('whaledeck-file-backups.py').FileBackups(files, namespace='container-files-v1')
    captures = load('whaledeck-container-backups.py').ContainerBackups(uploads, storage)
    history = load('whaledeck-container-history.py').ContainerHistory(uploads, storage)
    uploads.restorations = load('whaledeck-container-restore.py').ContainerRestorations(uploads, history)
    publisher = load('whaledeck-container-publish.py').ContainerPublisher(uploads, captures)
    with lease(files):
        files.emit({'type': 'driver', 'invocationId': invocation})
        def expired(_number, _frame): raise Rejected('CONTAINER_UPLOAD_TIMEOUT')
        signal.signal(signal.SIGALRM, expired); signal.alarm(10)
        source = files.InputLines(sys.stdin.fileno())
        request = files.line(source)
        signal.alarm(330 if sys.argv[1] == 'stage' else 640)
        try: result = execute(sys.argv[1], request, source, reader, files, docker, uploads, publisher)
        finally: signal.alarm(0)
        files.emit(result)


def error_code(error):
    # No exception message or arbitrary code suffix is ever emitted.
    codes = {'CONTAINER_UPLOAD_' + suffix for suffix in (
        'RESTORE_ENGINE_REQUIRED', 'RESTORE_ENGINE_MISMATCH', 'RESTORE_CONTENT_MISMATCH', 'RESTORE_MODE_UNSUPPORTED',
        'RESTORE_OWNER_UNMAPPED', 'RESTORE_ATTRIBUTES_INVALID', 'RESTORE_ATTRIBUTES_CHANGED',
        'RECOVERY_REQUEST_INVALID', 'RECOVERY_ACTOR_INVALID', 'RECOVERY_SCOPE_CHANGED', 'RECOVERY_SEAL_INVALID',
        'RECOVERY_IDEMPOTENCY_CONFLICT', 'RECOVERY_OBSERVATION_CHANGED', 'RECOVERY_RECORD_LIMIT',
        'MODULE_UNSAFE', 'STATE_UNSAFE', 'BUSY', 'REQUEST_INVALID', 'ACTION_INVALID', 'INVOCATION_INVALID', 'TIMEOUT', 'CANCELED',
        'SESSION_SCOPE_MISMATCH', 'SESSION_PROOF_INVALID', 'TARGET_CHANGED', 'PROTECTED', 'SIZE_LIMIT', 'VERSION_INVALID',
        'NOT_FOUND', 'PATH_NOT_FOUND', 'RECEIPT_LIMIT', 'RECEIPT_INVALID', 'SCOPE_MISMATCH', 'PARENT_VERSION_CONFLICT', 'VERSION_CONFLICT',
        'PAYLOAD_CHANGED', 'IDEMPOTENCY_CONFLICT', 'PREVIOUS_OPERATION_INCOMPLETE', 'CAPACITY_LOW', 'CHUNK_INVALID',
        'SIZE_MISMATCH', 'COMMIT_TRAILING_DATA', 'CHECKSUM_MISMATCH', 'WRITE_FAILED', 'BACKUP_ENGINE_MISMATCH',
        'STAGED_REQUIRED', 'BACKUP_SOURCE_REQUIRED', 'BACKUP_BINDING_CHANGED', 'PUBLISH_TIMEOUT', 'PUBLISH_PARENT_CHANGED',
        'PUBLISH_MOUNT_ROOT', 'PUBLISH_READ_ONLY', 'PUBLISH_OWNER_INVALID', 'PUBLISH_PRIVATE_UNSAFE',
        'PUBLISH_PRIVATE_CHANGED', 'PUBLISH_OBJECT_CHANGED', 'PUBLISH_OWNER_CHANGED', 'ALREADY_PUBLISHED', 'PUBLISHER_REQUIRED')}
    codes |= {'CONTAINER_PROTECTED', 'CONTAINER_UNSAFE_ISOLATION', 'CONTAINER_UNSAFE_MOUNT', 'CONTAINER_NOT_RUNNING'}
    codes |= {'CONTAINER_HISTORY_' + suffix for suffix in ('REQUEST_INVALID', 'PROTECTED', 'SCOPE_MISMATCH',
        'CAPTURE_CHANGED', 'CONTENT_CHANGED', 'CONTENT_INVALID', 'METADATA_INVALID', 'METADATA_CHANGED', 'INCOMPLETE')}
    codes |= {'CONTAINER_FILES_' + suffix for suffix in ('INSTANCE_CHANGED', 'ROOT_CHANGED', 'TARGET_CHANGED', 'PROTECTED',
        'SHARED_CORE_MOUNT', 'MOUNT_INVALID', 'MOUNT_IDENTITY_CHANGED', 'HOST_ROOT', 'PROCESS_TARGET_CHANGED', 'NAMESPACE_INVALID')}
    codes |= {'FILES_' + suffix for suffix in ('BACKUP_UNAVAILABLE', 'BACKUP_METADATA_UNSAFE', 'BACKUP_CONFIGURATION_INVALID',
        'BACKUP_MOUNT_MISMATCH', 'BACKUP_FILESYSTEM_UNSUPPORTED', 'BACKUP_DEVICE_METADATA_UNSAFE', 'BACKUP_DEVICE_MISMATCH',
        'BACKUP_HDD_REQUIRED', 'BACKUP_READ_ONLY', 'BACKUP_DIRECTORY_UNSAFE', 'BACKUP_CONTENT_INVALID', 'BACKUP_HISTORY_LIMIT',
        'BACKUP_CAPACITY_REQUIRED', 'BACKUP_IDEMPOTENCY_CONFLICT', 'BACKUP_INCOMPLETE', 'BACKUP_WRITE_FAILED', 'VERSION_CONFLICT',
        'INPUT_LIMIT', 'INPUT_TIMEOUT', 'COMMIT_TRAILING_DATA', 'REGULAR_SINGLE_LINK_REQUIRED', 'SIZE_LIMIT', 'KERNEL_UNSUPPORTED', 'READ_CHANGED')}
    value = str(error)
    if value in codes: return value
    if isinstance(error, OSError):
        if error.errno in (errno.ELOOP, errno.EXDEV, errno.ENOTDIR, errno.EACCES, errno.EPERM): return 'CONTAINER_UPLOAD_PROTECTED'
        # A missing HDD object/path is not evidence that the durable operation
        # never existed. Only the explicit receipt lookup emits NOT_FOUND.
        if error.errno == errno.ENOSPC: return 'CONTAINER_UPLOAD_CAPACITY_LOW'
    return 'CONTAINER_UPLOAD_UNAVAILABLE'


if __name__ == '__main__':
    try: main()
    except BaseException as error:
        try: print(json.dumps({'type': 'error', 'code': error_code(error)}), flush=True)
        except (OSError, ValueError): pass
        raise SystemExit(1) from None
