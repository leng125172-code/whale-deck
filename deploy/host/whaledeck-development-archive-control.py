"""Exact cgroup identity for one private archive task; no storage-root locks.

The root-owned journal pins boot + cgroup device/inode. A unit name alone is
never authority to stop a process. Nothing here removes archives or payloads.
"""
import contextlib
import errno
import os
from pathlib import Path
import re
import time

CGROUP = Path('/sys/fs/cgroup')
BOOT = Path('/proc/sys/kernel/random/boot_id')
SELF = Path('/proc/self/cgroup')
UNIT = re.compile(r'/system\.slice/whaledeck-development-archive-run-[a-f0-9]{32}\.service\Z')
UUID = re.compile(r'[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}\Z')
DIR = os.O_RDONLY | os.O_DIRECTORY


class Execution:
    def __init__(self, driver):
        self.d = driver

    def refuse(self, code):
        self.d.reject('ARCHIVE_TASK_' + code)

    def boot(self):
        value = BOOT.read_text(encoding='ascii').strip()
        if not UUID.fullmatch(value):
            self.refuse('EXECUTION_INVALID')
        return value

    def identity(self):
        value = SELF.read_text(encoding='ascii')
        if not value.startswith('0::') or not UNIT.fullmatch(value[3:].rstrip('\n')):
            self.refuse('EXECUTION_INVALID')
        path = value[3:].rstrip('\n')
        with self.d.absolute(CGROUP) as root:
            with self.d.opened(root, path[1:], DIR) as group:
                info = os.fstat(group)
                return dict(bootId=self.boot(), path=path, device=info.st_dev, inode=info.st_ino)

    def validate(self, proof):
        if (not isinstance(proof, dict) or set(proof) != {'bootId', 'path', 'device', 'inode'}
                or not isinstance(proof['bootId'], str) or not UUID.fullmatch(proof['bootId'])
                or not isinstance(proof['path'], str) or not UNIT.fullmatch(proof['path'])
                or any(type(proof[k]) is not int or proof[k] <= 0 for k in ('device', 'inode'))):
            self.refuse('EXECUTION_INVALID')

    def populated(self, group):
        with self.d.opened(group, 'cgroup.events', os.O_RDONLY) as fd:
            raw = os.read(fd, 513)
        if not raw:
            raise FileNotFoundError(errno.ENODEV, 'cgroup is gone')
        rows = [line.split() for line in raw.splitlines() if line.startswith(b'populated ')]
        if len(raw) > 512 or rows not in ([[b'populated', b'0']], [[b'populated', b'1']]):
            self.refuse('EXECUTION_INVALID')
        return rows == [[b'populated', b'1']]

    def empty(self, root, group, proof):
        try:
            return not self.populated(group)
        except OSError as error:
            if error.errno not in (errno.ENOENT, errno.ENODEV):
                raise
            try:
                with self.d.opened(root, proof['path'][1:], DIR) as current:
                    if (os.fstat(current).st_dev, os.fstat(current).st_ino) != (proof['device'], proof['inode']):
                        self.refuse('EXECUTION_CHANGED')
            except FileNotFoundError:
                return True
            self.refuse('STOP_UNCONFIRMED')

    def settled(self, proof, *, stop=False):
        self.validate(proof)
        # A different boot proves that this precise old execution is gone; it
        # does not prove that its archive is complete or permit a new capture.
        if proof['bootId'] != self.boot():
            return True
        with contextlib.ExitStack() as stack:
            root = stack.enter_context(self.d.absolute(CGROUP))
            if os.fstat(root).st_dev != proof['device']:
                self.refuse('EXECUTION_CHANGED')
            try:
                group = stack.enter_context(self.d.opened(root, proof['path'][1:], DIR))
            except FileNotFoundError:
                return True
            if (os.fstat(group).st_dev, os.fstat(group).st_ino) != (proof['device'], proof['inode']):
                self.refuse('EXECUTION_CHANGED')
            if self.empty(root, group, proof):
                return True
            if not stop:
                return False
            try:
                fd = os.open('cgroup.kill', os.O_WRONLY | os.O_NOFOLLOW | os.O_CLOEXEC, dir_fd=group)
                try:
                    if os.write(fd, b'1') != 1:
                        self.refuse('STOP_UNCONFIRMED')
                finally:
                    os.close(fd)
            except OSError as error:
                if error.errno not in (errno.ENOENT, errno.ENODEV) or not self.empty(root, group, proof):
                    self.refuse('STOP_UNCONFIRMED')
            deadline = time.monotonic() + 12
            while not self.empty(root, group, proof):
                if time.monotonic() >= deadline:
                    self.refuse('STOP_UNCONFIRMED')
                time.sleep(0.05)
            return True
