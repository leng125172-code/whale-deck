"""Stop and reclaim an attested Runner invocation through its cgroup v2 identity."""

import contextlib
import errno
import os
from pathlib import Path
import re
import stat
import time

CGROUP_ROOT = Path('/sys/fs/cgroup')
BOOT_ID = Path('/proc/sys/kernel/random/boot_id')
UNIT_PATH = re.compile(r'/system\.slice/whaledeck-development-run-start-[a-f0-9]{32}\.service\Z')
UUID = re.compile(r'[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}\Z')
EXECUTION = 'execution.json'
CANCEL = 'cancel'
DIR = os.O_RDONLY | os.O_DIRECTORY
STOP_SECONDS = 12


def boot_identity():
    value = BOOT_ID.read_text(encoding='ascii').strip()
    if not UUID.fullmatch(value):
        raise PermissionError('Invalid boot identifier')
    return value


@contextlib.contextmanager
def open_scope(driver, path):
    if not isinstance(path, str) or not UNIT_PATH.fullmatch(path):
        raise PermissionError('Invalid Runner cgroup path')
    with driver.absolute(CGROUP_ROOT) as root:
        with driver.opened(root, path[1:], DIR) as group:
            yield group


def record(driver, rootfs, directory, path):
    with open_scope(driver, path) as group:
        info = os.fstat(group)
    rootfs.write_metadata(directory, EXECUTION,
                          {'bootId': boot_identity(), 'path': path, 'device': info.st_dev, 'inode': info.st_ino})
    fd = os.open(CANCEL, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW | os.O_CLOEXEC, 0o600, dir_fd=directory)
    with os.fdopen(fd, 'wb') as stream:
        stream.write(b'0')
        stream.flush()
        os.fsync(stream.fileno())
    os.fsync(directory)


def populate_state(driver, group):
    with driver.opened(group, 'cgroup.events', os.O_RDONLY) as fd:
        data = os.read(fd, 513)
    if not data:
        raise FileNotFoundError(errno.ENODEV, 'Runner cgroup events no longer available')
    values = [line.split() for line in data.splitlines() if line.startswith(b'populated ')]
    if len(data) > 512 or values not in ([[b'populated', b'0']], [[b'populated', b'1']]):
        raise PermissionError('Unknown Runner cgroup population')
    return values[0][1] == b'1'


def cancel_state(directory, *, mark=False):
    flags = os.O_RDWR if mark else os.O_RDONLY
    fd = os.open(CANCEL, flags | os.O_NOFOLLOW | os.O_NONBLOCK | os.O_CLOEXEC, dir_fd=directory)
    try:
        info = os.fstat(fd)
        if (not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_nlink != 1
                or stat.S_IMODE(info.st_mode) != 0o600 or info.st_size != 1):
            raise PermissionError('Invalid Runner cancellation record')
        state = os.pread(fd, 2, 0)
        if state not in (b'0', b'1'):
            raise PermissionError('Unknown Runner cancellation state')
        if mark:
            if state == b'0' and os.pwrite(fd, b'1', 0) != 1:
                raise OSError('Incomplete Runner cancellation record')
            os.fsync(fd)
        return mark or state == b'1'
    finally:
        os.close(fd)


def scope_gone(driver, root, relative, identity, refuse):
    try:
        with driver.opened(root, relative, DIR) as current:
            if not os.path.samestat(identity, os.fstat(current)):
                refuse('EXECUTION_PROOF_CHANGED')
    except FileNotFoundError:
        return True
    return False


def scope_empty(driver, root, group, relative, identity, refuse):
    try:
        return not populate_state(driver, group)
    except OSError as error:
        # kernfs can remove cgroup.events before the pinned directory FD
        # reports removal. Reopen only the original path under the same root.
        if error.errno not in (errno.ENOENT, errno.ENODEV):
            refuse('CLEANUP_UNCONFIRMED')
        return scope_gone(driver, root, relative, identity, refuse)


def settle(driver, directory, *, stop, refuse):
    try:
        record = driver.read_json(directory, EXECUTION, 512)
    except FileNotFoundError:
        refuse('EXECUTION_PROOF_MISSING')
    if (set(record) != {'bootId', 'path', 'device', 'inode'}
            or not isinstance(record['bootId'], str) or not UUID.fullmatch(record['bootId'])
            or not isinstance(record['path'], str) or not UNIT_PATH.fullmatch(record['path'])
            or any(type(record[key]) is not int or record[key] <= 0 for key in ('device', 'inode'))):
        refuse('EXECUTION_PROOF_INVALID')
    cancelled = cancel_state(directory)
    if record['bootId'] != boot_identity():
        return 'Cancelled' if cancelled else 'Interrupted'

    with contextlib.ExitStack() as stack:
        root = stack.enter_context(driver.absolute(CGROUP_ROOT))
        if os.fstat(root).st_dev != record['device']:
            refuse('EXECUTION_PROOF_CHANGED')
        relative = record['path'][1:]
        try:
            group = stack.enter_context(driver.opened(root, relative, DIR))
        except FileNotFoundError:
            return 'Cancelled' if cancelled else 'Interrupted'
        identity = os.fstat(group)
        if (identity.st_dev, identity.st_ino) != (record['device'], record['inode']):
            refuse('EXECUTION_PROOF_CHANGED')
        if scope_empty(driver, root, group, relative, identity, refuse):
            return 'Cancelled' if cancelled else 'Interrupted'
        if not stop:
            refuse('BUSY')
        cancel_state(directory, mark=True)
        try:
            fd = os.open('cgroup.kill', os.O_WRONLY | os.O_NOFOLLOW | os.O_CLOEXEC, dir_fd=group)
            try:
                if os.write(fd, b'1') != 1:
                    refuse('CLEANUP_UNCONFIRMED')
            finally:
                os.close(fd)
        except OSError as error:
            if error.errno not in (errno.ENOENT, errno.ENODEV) or not scope_empty(
                    driver, root, group, relative, identity, refuse):
                refuse('CLEANUP_UNCONFIRMED')
        deadline = time.monotonic() + STOP_SECONDS
        while not scope_empty(driver, root, group, relative, identity, refuse):
            if time.monotonic() >= deadline:
                refuse('CLEANUP_UNCONFIRMED')
            time.sleep(0.05)
        return 'Cancelled'
