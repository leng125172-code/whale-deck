#!/usr/bin/env python3
"""Initial pin of the existing HDD; never silently rebind an existing policy."""
import contextlib
import importlib.util
import json
import os
from pathlib import Path
import re
import stat
import sys
import uuid

MOUNT_POINT = '/data'
TARGET_RELATIVE = 'DockerData/whaledeck/file-backups'


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec); sys.modules[name] = module; spec.loader.exec_module(module)
    return module


def configure(check=False):
    if os.geteuid() != 0: raise RuntimeError('FILES_BACKUP_ROOT_REQUIRED')
    source = Path(__file__).resolve().parent
    files = load('file_backup_install_driver', source / 'whaledeck-files.py')
    backups = load('file_backup_install_policy', source / 'whaledeck-file-backups.py')
    engine = backups.FileBackups(files)
    policy = Path(backups.CONFIGURATION)
    if check:
        with engine.target(): pass
        print('Verified pinned HDD file backup policy.')
        return
    if policy.exists() or policy.is_symlink():
        configuration = engine.configuration()
        if configuration['mountPoint'] != MOUNT_POINT or configuration['targetRelative'] != TARGET_RELATIVE:
            raise RuntimeError('FILES_BACKUP_APPROVED_TARGET_REQUIRED')
    else:
        data = os.open(MOUNT_POINT, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW | os.O_CLOEXEC)
        try:
            info = os.fstat(data); device = f'{os.major(info.st_dev)}:{os.minor(info.st_dev)}'
            udev = os.open(backups.UDEV, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW | os.O_CLOEXEC)
            try:
                fd = files.open_at(udev, 'b' + device, os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW)
                try:
                    record = os.fstat(fd)
                    if not stat.S_ISREG(record.st_mode) or record.st_uid != 0 or record.st_nlink != 1 or record.st_mode & 0o022 or record.st_size > 65536:
                        raise RuntimeError('FILES_BACKUP_DEVICE_METADATA_UNSAFE')
                    values = os.read(fd, 65537).decode('utf-8').splitlines()
                finally: os.close(fd)
            finally: os.close(udev)
            identifiers = [value.removeprefix('E:ID_FS_UUID=') for value in values if value.startswith('E:ID_FS_UUID=')]
            if len(identifiers) != 1 or not re.fullmatch(r'[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}', identifiers[0]):
                raise RuntimeError('FILES_BACKUP_DEVICE_MISMATCH')
            configuration = {'schemaVersion': 1, 'storageId': backups.STORAGE, 'mountPoint': MOUNT_POINT, 'filesystemUuid': identifiers[0],
                'targetRelative': TARGET_RELATIVE, 'reserveBytes': 512 * 1024**2, 'reservePercent': 5, 'maxVersions': 10000}
            # Check physical device/mount before creating even an empty backup
            # directory. Missing HDD must not write its OS-disk fallback path.
            engine.attest(configuration, data)
        finally: os.close(data)
        parent = os.open(policy.parent, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW | os.O_CLOEXEC)
        temporary = '.file-backups-' + uuid.uuid4().hex
        try:
            info = os.fstat(parent)
            if info.st_uid != 0 or info.st_mode & 0o022: raise RuntimeError('FILES_BACKUP_METADATA_UNSAFE')
            fd = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600, dir_fd=parent)
            with os.fdopen(fd, 'wb') as stream:
                stream.write(json.dumps(configuration, separators=(',', ':')).encode()); stream.flush(); os.fsync(stream.fileno())
            try: files.exchange(parent, temporary, parent, policy.name, 1)
            except FileExistsError:
                if engine.configuration() != configuration: raise RuntimeError('FILES_BACKUP_CONFIGURATION_CHANGED')
            os.fsync(parent)
        finally:
            with contextlib.suppress(FileNotFoundError): os.unlink(temporary, dir_fd=parent)
            os.close(parent)
    # Existing configuration gets the same attestation before any mkdir too.
    mounted = os.open(MOUNT_POINT, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW | os.O_CLOEXEC)
    try:
        engine.attest(configuration, mounted)
        files.relative(TARGET_RELATIVE)
        parts = TARGET_RELATIVE.split('/')
        for index, part in enumerate(parts):
            # Pin each directory FD below the verified mount; never traverse a
            # symlink/cross-mount ancestor or chmod/adopt an unsafe old target.
            info = os.fstat(mounted)
            # Data mount/container-storage ancestors may legitimately belong
            # to the workstation user. Never chown those shared directories.
            # Walk them via no-symlink/no-cross-mount FDs, reject world-write,
            # and require the immediate backup namespace to be root-controlled.
            if info.st_mode & 0o002 or index == len(parts) - 1 and (info.st_uid != 0 or info.st_mode & 0o022):
                raise RuntimeError('FILES_BACKUP_DIRECTORY_UNSAFE')
            try: os.mkdir(part, 0o700 if index == len(parts) - 1 else 0o755, dir_fd=mounted); os.fsync(mounted)
            except FileExistsError: pass
            child = files.open_at(mounted, part, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
            os.close(mounted); mounted = child
    finally: os.close(mounted)
    with engine.target(): pass
    # Provision the fixed container history domain during installation, never
    # during an authenticated read or when HDD attestation has failed.
    with backups.FileBackups(files, namespace='container-files-v1').target(): pass
    print('Verified pinned HDD file backup policy; existing configuration preserved.')


if __name__ == '__main__':
    try:
        if sys.argv[1:] not in ([], ['--check']): raise RuntimeError('invalid-arguments')
        configure(check=sys.argv[1:] == ['--check'])
    except BaseException:
        # Do not print filesystem paths, device metadata or arbitrary exceptions.
        print('FILES_BACKUP_CONFIGURATION_FAILED', file=sys.stderr); raise SystemExit(2) from None
