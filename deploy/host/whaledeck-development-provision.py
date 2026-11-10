#!/usr/bin/env python3
"""Root-only provisioning of new, fully reserved development filesystem images.

Formats only a new image or this transaction's unpublished, inode-bound image.
Existing partitions, filesystems and unrelated images are never formatted.
"""
import argparse
import fcntl
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import sys
import uuid

IMAGE_ROOT = Path('/var/lib/whaledeck-development-disks')
BASE = Path('/var/lib/whaledeck-development')
CONFIG = Path('/etc/whaledeck/development-storage.json')
LAYOUT = IMAGE_ROOT / 'layout.json'
UNIT_ROOT = Path('/etc/systemd/system')


def require(value, code):
    if not value:
        raise RuntimeError(code)


def command(*args, optional=False):
    value = subprocess.run(args, capture_output=True, text=True, timeout=120)
    require(optional or value.returncode == 0, 'STORAGE_COMMAND_FAILED:' + args[0])
    return value.stdout.strip() if value.returncode == 0 else None


def load(name):
    path = Path(__file__).with_name('whaledeck-' + name + '.py')
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def canonical(value):
    return json.dumps(value, sort_keys=True, separators=(',', ':')).encode()


def digest(value):
    return hashlib.sha256(canonical(value)).hexdigest()


def write_all(fd, raw):
    remaining = memoryview(raw)
    while remaining:
        written = os.write(fd, remaining)
        require(written > 0, 'STATE_WRITE_FAILED')
        remaining = remaining[written:]


def write_new(path, raw, mode):
    # A cut during creation leaves a private temporary file, never an empty
    # authoritative configuration or half-written systemd mount unit.
    with driver.absolute(path.parent) as parent:
        driver.directory(parent)
        name = '.whaledeck-new-' + uuid.uuid4().hex
        fd = os.open(name, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600, dir_fd=parent)
        try:
            write_all(fd, raw)
            os.fchmod(fd, mode)
            os.fsync(fd)
        finally:
            os.close(fd)
        try:
            files.exchange(parent, name, parent, path.name, 1)  # RENAME_NOREPLACE
            os.fsync(parent)
        except BaseException:
            os.unlink(name, dir_fd=parent)
            raise


def private_json(path):
    info = path.lstat()
    require(stat.S_ISREG(info.st_mode) and info.st_uid == info.st_gid == 0 and info.st_nlink == 1
            and stat.S_IMODE(info.st_mode) == 0o600 and info.st_size <= 65536, 'PRIVATE_STATE_UNSAFE')
    return json.loads(path.read_text(), object_pairs_hook=files.unique_object)


def save(value):
    raw = canonical(value) + b'\n'
    temporary = IMAGE_ROOT / ('.layout-' + uuid.uuid4().hex)
    fd = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
    try:
        write_all(fd, raw)
        os.fsync(fd)
    finally:
        os.close(fd)
    os.replace(temporary, LAYOUT)
    fd = os.open(IMAGE_ROOT, os.O_RDONLY | os.O_DIRECTORY)
    try:
        os.fsync(fd)
    finally:
        os.close(fd)


def device_uuid(device):
    values = command('udevadm', 'info', '--query=property', '--path=/sys/dev/block/' + device).splitlines()
    result = [v.removeprefix('ID_FS_UUID=') for v in values if v.startswith('ID_FS_UUID=')]
    require(len(result) == 1 and re.fullmatch(storage.UUID, result[0]), 'DEVICE_UUID_UNAVAILABLE')
    return result[0]


def identity(fd):
    value = os.fstat(fd)
    return dict(device=f'{os.major(value.st_dev)}:{os.minor(value.st_dev)}', inode=value.st_ino,
                uid=value.st_uid, gid=value.st_gid, mode=stat.S_IMODE(value.st_mode), mountId=driver.mount_id(fd))


def target_plan(path, size, kind):
    require(re.fullmatch(r'/[A-Za-z0-9/_-]+', str(path)) and str(path) == os.path.normpath(path)
            and path not in (Path('/'), BASE) and not path.is_mount(), 'TARGET_INVALID')
    with driver.absolute(path) as fd:
        require(not os.listdir(fd), 'TARGET_NOT_EMPTY')
        result = identity(fd)
    if kind == 'runtime':
        driver.require_nvme(result['device'])
        image = IMAGE_ROOT / 'nvme.ext4'
    else:
        device = (storage.SYS_DEV / result['device']).resolve(strict=True)
        disk = device.parent if (device / 'partition').is_file() else device
        require(device.is_relative_to(storage.SYS_DEVICES) and 'virtual' not in disk.parts
                and (disk / 'queue/rotational').read_bytes() == b'1\n', 'ARCHIVE_HDD_REQUIRED')
        image = path.parent / '.whaledeck-development-disks/archive.ext4'
    require(not image.exists() and not image.is_symlink(), 'IMAGE_ALREADY_EXISTS')
    existing = image.parent
    while not existing.exists():
        existing = existing.parent
    with driver.absolute(existing) as parent:
        # Fail during the read-only plan if the fixed image directory is not
        # on the target's physical filesystem. Never strand a wrong-device file.
        require(identity(parent)['device'] == result['device'], 'IMAGE_DEVICE_CHANGED')
        free = os.fstatvfs(parent)
        reserve = (64 if kind == 'runtime' else 8) * 1024**3
        require(free.f_bavail * free.f_frsize >= size + reserve, 'PHYSICAL_RESERVE_INSUFFICIENT')
    return dict(target=str(path), sizeBytes=size, original=result, image=str(image),
                filesystemUuid=device_uuid(result['device']))


def plan(arguments):
    if LAYOUT.exists() or LAYOUT.is_symlink():
        record = private_json(LAYOUT)
        require(record.get('schemaVersion') == 1 and digest(record['plan']) == record['planSha256'], 'LAYOUT_INVALID')
        expected = [('runtime', arguments.runtime, arguments.runtime_gb), ('archive', arguments.archive, arguments.archive_gb)]
        for key, target, size in expected:
            require(record['plan'][key]['target'] == str(target)
                    and record['plan'][key]['sizeBytes'] == size * 10**9, 'LAYOUT_CONFLICT')
        return record
    require(arguments.runtime != arguments.archive and not arguments.runtime.is_relative_to(arguments.archive)
            and not arguments.archive.is_relative_to(arguments.runtime), 'TARGETS_OVERLAP')
    for target in (arguments.runtime, arguments.archive):
        require(not target.is_relative_to(BASE) and not target.is_relative_to(IMAGE_ROOT), 'TARGET_INVALID')
    value = dict(runtime=target_plan(arguments.runtime, arguments.runtime_gb * 10**9, 'runtime'),
                 archive=target_plan(arguments.archive, arguments.archive_gb * 10**9, 'archive'))
    require(value['runtime']['original']['device'] != value['archive']['original']['device'], 'SEPARATE_DISKS_REQUIRED')
    require(not CONFIG.exists() and not CONFIG.is_symlink(), 'STORAGE_CONFIG_ALREADY_EXISTS')
    if BASE.exists():
        with driver.absolute(BASE) as fd:
            driver.directory(fd, private=True)
            require(not os.listdir(fd) and not BASE.is_mount(), 'BASE_NOT_EMPTY')
    return dict(schemaVersion=1, plan=value, planSha256=digest(value), phase='planned', images={})


def own_directory(path):
    path.mkdir(mode=0o700, exist_ok=True)
    with driver.absolute(path) as fd:
        driver.directory(fd, private=True)


def image_ready(record, key):
    spec = record['plan'][key]
    image = Path(spec['image'])
    own_directory(image.parent)
    with driver.absolute(image.parent) as parent:
        require(identity(parent)['device'] == spec['original']['device']
                and device_uuid(spec['original']['device']) == spec['filesystemUuid'], 'IMAGE_DEVICE_CHANGED')
    pending = record['images'].get(key)
    if pending is None:
        # Space is checked on the physical outer filesystem before reservation.
        free = os.statvfs(image.parent)
        reserve = (64 if key == 'runtime' else 8) * 1024**3
        require(free.f_bavail * free.f_frsize >= spec['sizeBytes'] + reserve, 'PHYSICAL_RESERVE_INSUFFICIENT')
        require(not image.exists() and not image.is_symlink(), 'IMAGE_ALREADY_EXISTS')
        pending = dict(inode=None, uuid=str(uuid.uuid4()), phase='creating')
        record['images'][key] = pending
        save(record)
    if pending['phase'] == 'creating':
        # The durable declaration precedes O_EXCL. If death occurred between
        # create and journaling the inode, only its still-empty private file
        # can be adopted. A nonempty/unmanaged image is never reformatted.
        if image.exists() or image.is_symlink():
            fd = os.open(image, os.O_RDWR | os.O_NOFOLLOW)
        else:
            fd = os.open(image, os.O_RDWR | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
        try:
            info = os.fstat(fd)
            require(stat.S_ISREG(info.st_mode) and info.st_size == 0 and info.st_uid == info.st_gid == 0
                    and stat.S_IMODE(info.st_mode) == 0o600 and info.st_nlink == 1
                    and f'{os.major(info.st_dev)}:{os.minor(info.st_dev)}' == spec['original']['device'], 'IMAGE_IDENTITY_CHANGED')
            os.fsync(fd)
            directory = os.open(image.parent, os.O_RDONLY | os.O_DIRECTORY)
            try:
                os.fsync(directory)
            finally:
                os.close(directory)
            pending.update(inode=info.st_ino, phase='created')
            save(record)
        finally:
            os.close(fd)
    info = image.lstat()
    require(stat.S_ISREG(info.st_mode) and info.st_uid == info.st_gid == 0 and info.st_nlink == 1
            and stat.S_IMODE(info.st_mode) == 0o600 and info.st_ino == pending['inode']
            and f'{os.major(info.st_dev)}:{os.minor(info.st_dev)}' == spec['original']['device'], 'IMAGE_IDENTITY_CHANGED')
    if pending['phase'] == 'created':
        require(not command('losetup', '--associated', str(image), '--noheadings', '--output', 'NAME'), 'IMAGE_ALREADY_ATTACHED')
        fd = os.open(image, os.O_RDWR | os.O_NOFOLLOW)
        try:
            require(os.fstat(fd).st_ino == pending['inode'] and os.fstat(fd).st_size in (0, spec['sizeBytes']), 'IMAGE_IDENTITY_CHANGED')
            os.posix_fallocate(fd, 0, spec['sizeBytes'])
            os.fsync(fd)
        finally:
            os.close(fd)
        existing_uuid = command('blkid', '-p', '-s', 'UUID', '-o', 'value', str(image), optional=True)
        require(existing_uuid in (None, pending['uuid']), 'UNEXPECTED_IMAGE_FILESYSTEM')
        if existing_uuid is None:
            command('mkfs.ext4', '-q', '-F', '-m', '0', '-U', pending['uuid'], '-O', 'project,quota', '-E',
                    'quotatype=prjquota,nodiscard,lazy_itable_init=0,lazy_journal_init=0', str(image))
        pending['phase'] = 'formatted'
        save(record)
    require(image.stat().st_size == spec['sizeBytes'] and image.stat().st_blocks * 512 >= spec['sizeBytes']
            and command('blkid', '-p', '-s', 'UUID', '-o', 'value', str(image)) == pending['uuid'], 'IMAGE_RESERVATION_CHANGED')


def unit_name(path):
    return command('systemd-escape', '--path', '--suffix=mount', str(path))


def install_mount(target, source, *, bind=False):
    name = unit_name(target)
    requirements = 'Requires=' + unit_name(source) + '\nAfter=' + unit_name(source) + '\n' if bind else ''
    text = ('[Unit]\nDescription=Whale Deck bounded development storage\n' + requirements +
            'RequiresMountsFor=' + str(source.parent) + '\nBefore=local-fs.target\n\n[Mount]\nWhat=' + str(source) +
            '\nWhere=' + str(target) + '\nOptions=' + ('bind,nosuid,nodev' if bind else 'loop,prjquota,nosuid,nodev') +
            ('\n' if bind else '\nType=ext4\n') + 'TimeoutSec=30\nDirectoryMode=0700\n\n[Install]\nWantedBy=local-fs.target\n')
    destination = UNIT_ROOT / name
    if destination.exists() or destination.is_symlink():
        require(destination.is_file() and not destination.is_symlink() and destination.read_text() == text
                and destination.stat().st_uid == destination.stat().st_gid == 0
                and stat.S_IMODE(destination.stat().st_mode) == 0o644
                and destination.stat().st_nlink == 1, 'MOUNT_UNIT_CONFLICT')
    else:
        write_new(destination, text.encode(), 0o644)
    command('systemctl', 'daemon-reload')
    command('systemctl', 'enable', name)
    command('systemctl', 'start', name)


def publish(record):
    for key in ('runtime', 'archive'):
        spec = record['plan'][key]
        target = Path(spec['target'])
        pending = record['images'][key]
        if not target.is_mount():
            with driver.absolute(target) as fd:
                current = identity(fd)
                expected = spec['original']
                require(current['device'] == expected['device'] and current['inode'] == expected['inode']
                        and current['mountId'] == expected['mountId'] and not os.listdir(fd), 'TARGET_CHANGED')
                require((current['uid'], current['gid'], current['mode']) in
                        [(expected['uid'], expected['gid'], expected['mode']), (0, 0, 0o700)], 'TARGET_CHANGED')
                os.fchown(fd, 0, 0)
                os.fchmod(fd, 0o700)
                require(not os.listdir(fd), 'TARGET_CHANGED')
                require(os.path.samestat(os.fstat(fd), target.stat(follow_symlinks=False)), 'TARGET_CHANGED')
        install_mount(target, Path(spec['image']))
        actual = command('findmnt', '--mountpoint', str(target), '--noheadings', '--output', 'UUID')
        require(actual == pending['uuid'], 'MOUNT_UUID_MISMATCH')
        with driver.absolute(target) as fd:
            os.fchmod(fd, 0o700)
            quota.require_enforcing(fd)
        pending['phase'] = 'mounted'
        save(record)
    own_directory(BASE)
    if not BASE.is_mount():
        require(not list(BASE.iterdir()), 'BASE_NOT_EMPTY')
    install_mount(BASE, Path(record['plan']['runtime']['target']), bind=True)
    root = BASE / 'nvme-main'
    own_directory(root)
    runtime = record['plan']['runtime']
    pin = dict(path=runtime['image'], device=runtime['original']['device'],
        inode=record['images']['runtime']['inode'], sizeBytes=runtime['sizeBytes'], filesystemUuid=runtime['filesystemUuid'])
    configuration = dict(schemaVersion=2, roots={'nvme-main': dict(mountPoint=str(BASE),
        filesystemUuid=record['images']['runtime']['uuid'], reserveMiB=1024,
        projectIdStart=1500000000, projectIdEnd=1500004095, diskImage=pin)})
    # Validate the actual mount and its physical image chain before making the
    # root available to the standard Agent. No test metadata is substituted.
    with driver.absolute(BASE) as fd:
        driver.attest_mount(fd, configuration['roots']['nvme-main'])
        quota.require_enforcing(fd)
    if CONFIG.exists() or CONFIG.is_symlink():
        require(private_json(CONFIG) == configuration, 'STORAGE_CONFIG_CONFLICT')
    else:
        write_new(CONFIG, canonical(configuration) + b'\n', 0o600)
    record['phase'] = 'ready'
    save(record)


def main():
    global files, storage, driver, quota
    require(os.geteuid() == 0 and __debug__, 'ROOT_REQUIRED')
    os.umask(0o077)
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=('plan', 'apply'))
    parser.add_argument('--runtime', type=Path, required=True)
    parser.add_argument('--archive', type=Path, required=True)
    parser.add_argument('--runtime-gb', type=int, required=True)
    parser.add_argument('--archive-gb', type=int, required=True)
    parser.add_argument('--plan-sha256')
    arguments = parser.parse_args()
    require(2 <= arguments.runtime_gb <= 1000 and 2 <= arguments.archive_gb <= 1000, 'SIZE_INVALID')
    files, storage, quota = load('files'), load('development-storage'), load('development-project-quota')
    driver = storage.WorkspaceStorage(files, quota=quota)
    record = plan(arguments)
    if arguments.action == 'plan':
        print(json.dumps(record, ensure_ascii=False))
        return
    require(arguments.plan_sha256 == record['planSha256'], 'PLAN_CHANGED')
    own_directory(IMAGE_ROOT)
    lock = os.open(IMAGE_ROOT / 'provision.lock', os.O_RDWR | os.O_CREAT | os.O_NOFOLLOW, 0o600)
    try:
        info = os.fstat(lock)
        require(stat.S_ISREG(info.st_mode) and info.st_uid == 0 and info.st_nlink == 1
                and stat.S_IMODE(info.st_mode) == 0o600, 'LOCK_UNSAFE')
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        # Re-read only under the lock; a second provisioner cannot replace the
        # original transaction with a newly generated set of inode credentials.
        if LAYOUT.exists():
            record = private_json(LAYOUT)
            require(record['planSha256'] == arguments.plan_sha256, 'LAYOUT_CONFLICT')
        else:
            require(digest(record['plan']) == arguments.plan_sha256, 'PLAN_CHANGED')
            save(record)
        for key in ('runtime', 'archive'):
            image_ready(record, key)
        publish(record)
        print(json.dumps(dict(phase=record['phase'], planSha256=record['planSha256'],
            runtimeBytes=record['plan']['runtime']['sizeBytes'], archiveBytes=record['plan']['archive']['sizeBytes'])))
    finally:
        os.close(lock)


if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        # No private state, environment or command output is printed.
        print(json.dumps(dict(provision='failed', error=str(error) if isinstance(error, RuntimeError) else type(error).__name__)), file=sys.stderr)
        raise SystemExit(1) from None
