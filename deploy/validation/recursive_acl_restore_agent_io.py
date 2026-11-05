"""Read installed private records through attested HDD FDs; archive original bytes.

No mutation engine, namespace creation, cleanup or process control. Fixture state
overrides are Python-only test seams; the installed gate has fixed state paths.
"""
from contextlib import contextmanager
import hashlib
import json
import os
from pathlib import Path
import stat
from types import SimpleNamespace

from validate_container_file_staging import load
from recursive_acl_restore_rpc_proof import child_id
from validate_container_upload_recovery_agent import trusted

SOURCE_STATE = Path('/var/lib/whaledeck-container-recursive-acl')
RESTORE_STATE = Path('/var/lib/whaledeck-container-recursive-acl-restore')


def installed():
    reader = load('/usr/local/libexec/whaledeck-container-files.py', 'restore_gate_reader')
    files = reader.trusted_module('whaledeck-files.py', 'restore_gate_files')
    return SimpleNamespace(reader=reader, files=files,
        docker=load('/usr/local/libexec/whaledeck-container-terminal.py', 'restore_gate_docker'),
        acl=load('/usr/local/libexec/whaledeck-container-acl.py', 'restore_gate_acl'),
        storage=load('/usr/local/libexec/whaledeck-file-backups.py', 'restore_gate_storage').FileBackups(files),
        sourceState=SOURCE_STATE, restoreState=RESTORE_STATE)


def unique(pairs):
    result = {}
    for key, value in pairs:
        assert key not in result, 'DUPLICATE_PRIVATE_JSON_FIELD'; result[key] = value
    return result


def frame(info):
    return [info.st_dev, info.st_ino, info.st_mode, info.st_uid, info.st_gid, info.st_nlink, info.st_size, info.st_mtime_ns, info.st_ctime_ns]


def read_at(parent, name):
    fd = os.open(name, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK | os.O_CLOEXEC, dir_fd=parent)
    try:
        info = os.fstat(fd); original = frame(info)
        assert stat.S_ISREG(info.st_mode) and info.st_uid == info.st_gid == 0 and info.st_nlink == 1 and stat.S_IMODE(info.st_mode) == 0o600
        assert 0 < info.st_size <= 16 * 1024**2
        data = bytearray()
        while len(data) <= info.st_size:
            part = os.read(fd, min(65536, info.st_size + 1 - len(data)))
            if not part: break
            data.extend(part)
        assert len(data) == info.st_size and frame(os.fstat(fd)) == original == frame(os.stat(name, dir_fd=parent, follow_symlinks=False))
        return bytes(data), json.loads(data, object_pairs_hook=unique), original
    finally: os.close(fd)


@contextmanager
def namespace(io, restore):
    name = 'container-recursive-acl-restore-v1' if restore else 'container-recursive-acl-v1'
    with io.storage.target(create_namespace=False, read_only=True) as (config, base):
        try: fd = io.files.open_at(base, name, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
        except FileNotFoundError:
            io.storage.attest(config, base, read_only=True); yield None; return
        try:
            trusted(Path(os.readlink('/proc/self/fd/' + str(fd))), True)
            io.storage.attest(config, fd, read_only=True); yield fd; io.storage.attest(config, fd, read_only=True)
        finally: os.close(fd)


def snapshot(root):
    trusted(root, True); result = {}
    for path in root.rglob('*'):
        if path.is_symlink(): raise AssertionError('PRIVATE_SNAPSHOT_SYMLINK')
        if path.is_dir(): trusted(path, True); continue
        info = path.stat(); assert info.st_size <= 16 * 1024**2
        if info.st_size:
            raw, _, identity = read_at(None, str(path))
        else:
            trusted(path); assert stat.S_IMODE(info.st_mode) == 0o600 and info.st_nlink == 1
            raw = path.read_bytes(); identity = frame(info); assert not raw and identity == frame(path.stat())
        result[str(path.relative_to(root))] = dict(identity=identity, sha256=hashlib.sha256(raw).hexdigest())
    return result


def hdd_snapshot(io, restore):
    with namespace(io, restore) as fd:
        return {} if fd is None else snapshot(Path(os.readlink('/proc/self/fd/' + str(fd))))


def absent(fd, name):
    if fd is None: return
    try: os.stat(name, dir_fd=fd, follow_symlinks=False)
    except FileNotFoundError: return
    raise AssertionError('UNEXPECTED_PRIVATE_ARTIFACT')


def archive_raw(folder, name, raw, create):
    path = folder / name
    if path.exists():
        trusted(path); assert stat.S_IMODE(path.stat().st_mode)==0o600, 'PRIVATE_ARCHIVE_MODE'
        assert path.read_bytes() == raw, 'PRIVATE_ARCHIVE_CHANGED'
    else:
        assert create, 'PRIVATE_ARCHIVE_MISSING'
        descriptor=os.open(path,os.O_WRONLY|os.O_CREAT|os.O_EXCL|os.O_NOFOLLOW|os.O_CLOEXEC,0o600)
        with os.fdopen(descriptor,'wb') as stream: stream.write(raw)


def read_bundle(io, request, restore, folder, *, create=False):
    operation = request['operationId']; child_id(operation, 0)  # Validate before joining any path.
    state = io.restoreState if restore else io.sourceState; trusted(state, True)
    if create: folder.mkdir(mode=0o700, parents=True, exist_ok=True)
    trusted(folder, True)
    raw, root, _ = read_at(None, str(state / (operation + '.json'))); archive_raw(folder, 'root.json', raw, create)
    children = []; metadata = None; source_frame = None
    with namespace(io, restore) as fd:
        if not root['historyVerified']:
            absent(fd, operation + '.json'); absent(fd, operation)
            assert not os.path.lexists(state / (operation + '.entries'))
        else:
            assert fd is not None
            raw, metadata, source_frame = read_at(fd, operation + '.json'); archive_raw(folder, 'hdd.json', raw, create)
            trusted(state / (operation + '.entries'), True)
            batch = io.files.open_at(fd, operation + '/entries', os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
            try:
                trusted(Path(os.readlink('/proc/self/fd/' + str(batch))), True)
                # namespace() keeps the attestation open across every child read.
                for index in range(request['plan']['entryCount']):
                    sub = child_id(operation, index) if restore else hashlib.sha256(b'whaledeck-recursive-acl-entry-id-v1:' + operation.encode() + b':' + str(index).encode()).hexdigest()[:32]
                    raw, cr, _ = read_at(None, str(state / (operation + '.entries') / (sub + '.json')))
                    archive_raw(folder, sub + '.root.json', raw, create)
                    raw, hdd, _ = read_at(batch, sub + '.json'); archive_raw(folder, sub + '.hdd.json', raw, create)
                    children.append(dict(root=cr, hdd=hdd))
            finally: os.close(batch)
    return dict(root=root, metadata=metadata, children=children), source_frame
