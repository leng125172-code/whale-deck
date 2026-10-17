#!/usr/bin/env python3
"""Upgrade only unmodified v1 defaults; preserve custom root policy."""
import json
import os
from pathlib import Path
import stat
import uuid


def unique(pairs):
    result = {}
    for key, value in pairs:
        if key in result: raise ValueError('FILES_REGISTRY_INVALID')
        result[key] = value
    return result


def read(path):
    descriptor = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
    try:
        info = os.fstat(descriptor)
        if not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_nlink != 1 or stat.S_IMODE(info.st_mode) not in (0o600, 0o640, 0o644) or info.st_size > 65536:
            raise ValueError('FILES_REGISTRY_UNSAFE')
        payload = os.read(descriptor, 65537)
        return payload, json.loads(payload, object_pairs_hook=unique), info
    finally: os.close(descriptor)


def upgrade(path, baseline, desired, backup_suffix='.pre-mutations.json'):
    if backup_suffix not in ('.pre-mutations.json', '.pre-archives.json'): raise ValueError('FILES_REGISTRY_BACKUP_INVALID')
    payload, current, info = read(path)
    if current == desired: return 'unchanged'
    if current != baseline: return 'custom-preserved'
    parent = os.open(path.parent, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
    temporary = '.' + path.name + '-' + uuid.uuid4().hex
    backup = path.name + backup_suffix
    try:
        try:
            descriptor = os.open(backup, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600, dir_fd=parent)
            with os.fdopen(descriptor, 'wb') as stream:
                stream.write(payload); stream.flush(); os.fchown(stream.fileno(), 0, info.st_gid); os.fsync(stream.fileno())
            os.fsync(parent)
        except FileExistsError:
            if read(path.parent / backup)[1] != baseline: raise ValueError('FILES_REGISTRY_BACKUP_CONFLICT')
        descriptor = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600, dir_fd=parent)
        with os.fdopen(descriptor, 'wb') as stream:
            stream.write(json.dumps(desired, ensure_ascii=False, indent=2).encode() + b'\n'); stream.flush()
            os.fchown(stream.fileno(), 0, info.st_gid); os.fchmod(stream.fileno(), stat.S_IMODE(info.st_mode)); os.fsync(stream.fileno())
        latest = os.stat(path, follow_symlinks=False)
        if (latest.st_dev, latest.st_ino, latest.st_mtime_ns, latest.st_ctime_ns, latest.st_size) != (info.st_dev, info.st_ino, info.st_mtime_ns, info.st_ctime_ns, info.st_size):
            raise ValueError('FILES_REGISTRY_CHANGED')
        os.replace(temporary, path.name, src_dir_fd=parent, dst_dir_fd=parent); os.fsync(parent)
        return 'upgraded-defaults'
    finally:
        try: os.unlink(temporary, dir_fd=parent)
        except FileNotFoundError: pass
        os.close(parent)


if __name__ == '__main__':
    try:
        if os.geteuid() != 0: raise ValueError('FILES_ROOT_REQUIRED')
        folder = Path(__file__).resolve().parent
        baseline = json.loads((folder / 'file-roots.v1.json').read_text(), object_pairs_hook=unique)
        desired = json.loads((folder / 'file-roots.json').read_text(), object_pairs_hook=unique)
        path = Path('/etc/whaledeck/file-roots.json')
        previous = json.loads((folder / 'file-roots.v1.25.json').read_text(), object_pairs_hook=unique)
        print(json.dumps({'fileRootsUpgrade': upgrade(path, previous, desired, '.pre-archives.json') if read(path)[1] == previous else upgrade(path, baseline, desired)}))
    except Exception:
        print(json.dumps({'fileRootsUpgrade': 'rejected'}))
        raise SystemExit(1) from None
