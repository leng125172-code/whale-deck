#!/usr/bin/env python3
"""Recover only the fixed maintenance controllers after interruption.

Never runs Git, Docker, an installer or application rollback. Checkpoint schema 1
is a persistent compatibility contract; rollback installers preserve this runner.
"""
import datetime as dt
import fcntl
import grp
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import sys
import uuid

STATE = Path('/var/lib/whaledeck-agent/platform-maintenance')
LIBEXEC = Path('/usr/local/libexec')
LOCK = Path('/run/lock/whaledeck-platform-maintenance.lock')
STATUS = Path('/run/whaledeck/maintenance/status.json')
SYSTEMD = Path('/etc/systemd/system')
TMPFILES = Path('/etc/tmpfiles.d')
ACTIVE = 'control-recovery.json'
CONTROLS = ('whaledeck-platform-maintenance', 'whaledeck-platform-revisions.py', 'whaledeck-privileged')
REVISION = re.compile('[a-f0-9]{40}')
TOKEN = re.compile('[a-f0-9]{32}')
SHA = re.compile('[a-f0-9]{64}')
LIMIT = 256 * 1024


class Rejected(Exception):
    pass


def require(value):
    if not value:
        raise Rejected()


def unique(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result)
        result[key] = value
    return result


def encoded(value):
    return (json.dumps(value, sort_keys=True, separators=(',', ':'), ensure_ascii=False) + '\n').encode()


def operation(value):
    require(type(value) is dict and set(value) == {'action', 'revision', 'token'})
    require(value['action'] in ('update', 'rollback'))
    require(type(value['revision']) is str and REVISION.fullmatch(value['revision']))
    require(type(value['token']) is str and TOKEN.fullmatch(value['token']))


def manifest(value):
    require(type(value) is dict and set(value) == {'schemaVersion', 'capture', 'operation', 'phase', 'controls'})
    require(type(value['schemaVersion']) is int and value['schemaVersion'] == 1)
    require(type(value['capture']) is str and TOKEN.fullmatch(value['capture']))
    operation(value['operation'])
    require(value['phase'] in ('prepared', 'committing'))
    require(type(value['controls']) is dict and set(value['controls']) == set(CONTROLS))
    for item in value['controls'].values():
        require(type(item) is dict and set(item) == {'sha256', 'sizeBytes'})
        require(type(item['sha256']) is str and SHA.fullmatch(item['sha256']))
        require(type(item['sizeBytes']) is int and 0 < item['sizeBytes'] <= LIMIT)


class Directory:
    def __init__(self, root, *, owner=0, group=0, private=True):
        self.root, self.owner, self.group, self.private = Path(root), owner, group, private
        self.fd = os.open(root, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW | os.O_CLOEXEC)
        try:
            self.check()
        except BaseException:
            os.close(self.fd)
            raise

    def __enter__(self):
        return self

    def __exit__(self, *_):
        os.close(self.fd)

    def check(self):
        a, b = os.fstat(self.fd), self.root.lstat()
        require(stat.S_ISDIR(b.st_mode) and a.st_uid == b.st_uid == self.owner)
        require((a.st_dev, a.st_ino) == (b.st_dev, b.st_ino))
        require(stat.S_IMODE(a.st_mode) == 0o700 if self.private else not a.st_mode & 0o022)

    def read(self, name, mode=0o600, limit=8192):
        require('/' not in name and name not in ('', '.', '..'))
        self.check()
        try:
            fd = os.open(name, os.O_RDONLY | os.O_NOFOLLOW | os.O_CLOEXEC | os.O_NONBLOCK, dir_fd=self.fd)
        except FileNotFoundError:
            return None
        try:
            a = os.fstat(fd)
            require(stat.S_ISREG(a.st_mode) and a.st_uid == self.owner and a.st_nlink == 1 and stat.S_IMODE(a.st_mode) == mode and a.st_size <= limit)
            raw = os.read(fd, limit + 1)
            b = os.fstat(fd)
            frame = lambda s: (s.st_dev, s.st_ino, s.st_mode, s.st_uid, s.st_gid, s.st_nlink, s.st_size, s.st_mtime_ns, s.st_ctime_ns)
            require(len(raw) <= limit and frame(a) == frame(b))
            return raw
        finally:
            os.close(fd)

    def json(self, name):
        raw = self.read(name)
        return None if raw is None else json.loads(raw, object_pairs_hook=unique)

    def write(self, name, raw, mode=0o600):
        require('/' not in name and name not in ('', '.', '..') and len(raw) <= LIMIT)
        self.check()
        temporary = '.control-stage-' + uuid.uuid4().hex
        fd = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW | os.O_CLOEXEC, 0o600, dir_fd=self.fd)
        try:
            os.fchown(fd, self.owner, self.group)
            os.fchmod(fd, mode)
            with os.fdopen(fd, 'wb', closefd=False) as stream:
                stream.write(raw)
                stream.flush()
                os.fsync(fd)
            self.check()
            os.replace(temporary, name, src_dir_fd=self.fd, dst_dir_fd=self.fd)
            os.fsync(self.fd)
        finally:
            os.close(fd)
            try:
                os.unlink(temporary, dir_fd=self.fd)
            except FileNotFoundError:
                pass

    def remove(self, name):
        self.check()
        os.unlink(name, dir_fd=self.fd)
        os.fsync(self.fd)


def lock_file(path=LOCK, *, owner=0):
    parent = os.open(path.parent, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW | os.O_CLOEXEC)
    try:
        p = os.fstat(parent)
        require(p.st_uid == owner and (not p.st_mode & 0o022 or p.st_mode & stat.S_ISVTX))
        fd = os.open(path.name, os.O_RDWR | os.O_CREAT | os.O_NOFOLLOW | os.O_CLOEXEC | os.O_NONBLOCK, 0o600, dir_fd=parent)
        try:
            info = os.fstat(fd)
            require(stat.S_ISREG(info.st_mode) and info.st_uid == owner and info.st_nlink == 1 and stat.S_IMODE(info.st_mode) == 0o600)
            require(info.st_ino == os.stat(path.name, dir_fd=parent, follow_symlinks=False).st_ino)
            return fd
        except BaseException:
            os.close(fd)
            raise
    finally:
        os.close(parent)


def require_held(path=LOCK, *, owner=0):
    other = lock_file(path, owner=owner)
    try:
        a, b = os.fstat(9), os.fstat(other)
        require((a.st_dev, a.st_ino) == (b.st_dev, b.st_ino))
        fcntl.flock(9, fcntl.LOCK_EX | fcntl.LOCK_NB)
        try:
            fcntl.flock(other, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            return
        raise Rejected()
    finally:
        os.close(other)


class Recovery:
    def __init__(self, state=STATE, libexec=LIBEXEC, *, owner=0, group=0, status=None):
        self.state, self.libexec, self.owner, self.group = Path(state), Path(libexec), owner, group
        self.status = status if status is not None else public_status

    def ledger(self, directory, op):
        value = directory.json('deployment-revisions.json')
        require(type(value) is dict and set(value) == {'schemaVersion', 'currentRevision', 'previousRevision', 'pending', 'lastCompleted'})
        require(type(value['schemaVersion']) is int and value['schemaVersion'] == 1)
        require(type(value['currentRevision']) is str and REVISION.fullmatch(value['currentRevision']))
        previous = value['previousRevision']
        require(previous is None or type(previous) is str and REVISION.fullmatch(previous) and previous != value['currentRevision'])
        for name in ('pending', 'lastCompleted'):
            if value[name] is not None:
                operation(value[name])
        completed = value['pending'] is None and value['lastCompleted'] == op and value['currentRevision'] == op['revision']
        require(value['pending'] == op or completed)
        return completed

    def backup(self, value):
        return Directory(self.state / ('control-' + value['capture']), owner=self.owner, group=self.group)

    def verified(self, directory, value):
        saved = directory.json('checkpoint.json')
        require(saved is not None)
        manifest(saved)
        require(saved['phase'] == 'prepared')
        require({**saved, 'phase': value['phase']} == value)
        require(set(os.listdir(directory.fd)) <= {*CONTROLS, 'checkpoint.json', 'receipt.json'})
        result = {}
        for name in CONTROLS:
            raw = directory.read(name, limit=LIMIT)
            item = value['controls'][name]
            require(raw is not None and len(raw) == item['sizeBytes'] and hashlib.sha256(raw).hexdigest() == item['sha256'])
            result[name] = raw
        return result

    def checkpoint(self, op):
        operation(op)
        with Directory(self.state, owner=self.owner, group=self.group) as state:
            require(not self.ledger(state, op))
            existing = state.json(ACTIVE)
            if existing is not None:
                manifest(existing)
                require(existing['operation'] == op and existing['phase'] == 'prepared')
                with self.backup(existing) as backup:
                    require(backup.json('receipt.json') is None)
                    self.verified(backup, existing)
                return existing['capture']
            capture = uuid.uuid4().hex
            temporary, final = 'control-preparing-' + capture, 'control-' + capture
            os.mkdir(temporary, 0o700, dir_fd=state.fd)
            path = self.state / temporary
            with Directory(path, owner=self.owner, group=self.group) as backup, Directory(self.libexec, owner=self.owner, group=self.group, private=False) as controls:
                items = {}
                for name in CONTROLS:
                    raw = controls.read(name, mode=0o750, limit=LIMIT)
                    require(raw is not None and raw)
                    backup.write(name, raw)
                    items[name] = dict(sha256=hashlib.sha256(raw).hexdigest(), sizeBytes=len(raw))
                value = dict(schemaVersion=1, capture=capture, operation=op, phase='prepared', controls=items)
                manifest(value)
                backup.write('checkpoint.json', encoded(value))
                os.fsync(backup.fd)
            state.check()
            os.rename(temporary, final, src_dir_fd=state.fd, dst_dir_fd=state.fd)
            os.fsync(state.fd)
            state.write(ACTIVE, encoded(value))
            return capture

    def installation_mode(self):
        if not os.path.lexists(self.state):
            return 'replace'
        with Directory(self.state, owner=self.owner, group=self.group) as state:
            value = state.json(ACTIVE)
            if value is None:
                return 'replace'
            manifest(value)
            return 'preserve' if value['operation']['action'] == 'rollback' else 'replace'

    def complete(self, capture, op):
        operation(op)
        with Directory(self.state, owner=self.owner, group=self.group) as state:
            value = state.json(ACTIVE)
            require(value is not None)
            manifest(value)
            require(value['capture'] == capture and value['operation'] == op and self.ledger(state, op))
            with self.backup(value) as backup:
                self.verified(backup, value)
            value = {**value, 'phase': 'committing'}
            state.write(ACTIVE, encoded(value))
        return self.recover()

    def recover(self):
        if not os.path.lexists(self.state):
            return None
        with Directory(self.state, owner=self.owner, group=self.group) as state:
            value = state.json(ACTIVE)
            if value is None:
                return None
            manifest(value)
            completed = self.ledger(state, value['operation'])
            require(value['phase'] != 'committing' or completed)
            with self.backup(value) as backup:
                originals = self.verified(backup, value)
                restored = value['operation']['action'] == 'rollback' or not completed
                receipt = backup.json('receipt.json')
                expected = dict(schemaVersion=1, capture=value['capture'], operation=value['operation'],
                    outcome='completed' if value['phase'] == 'committing' else 'interrupted', controlsRestored=restored,
                    applicationReplayed=False, automaticRollback=False)
                if receipt is not None:
                    require(receipt == expected)
                if restored:
                    with Directory(self.libexec, owner=self.owner, group=self.group, private=False) as controls:
                        # Validate every source before the first replacement. Never
                        # follow destination links or execute captured source data.
                        for name, raw in originals.items():
                            try:
                                current = controls.read(name, mode=0o750, limit=LIMIT)
                            except (OSError, Rejected):
                                current = None
                            if current != raw:
                                controls.write(name, raw, mode=0o750)
                        require(all(controls.read(name, mode=0o750, limit=LIMIT) == raw for name, raw in originals.items()))
                if receipt is None:
                    backup.write('receipt.json', encoded(expected))
                self.status('healthy' if value['phase'] == 'committing' else 'failed')
                state.remove(ACTIVE)
                return expected


def install_sources(source, *, state=STATE, libexec=LIBEXEC, systemd=SYSTEMD, tmpfiles=TMPFILES, owner=0, group=0):
    # This is an installer-only entry point. Recovery itself never invokes it.
    mode = Recovery(state, libexec, owner=owner, group=group).installation_mode()
    if mode == 'preserve':
        with Directory(libexec, owner=owner, group=group, private=False) as target:
            require(target.read('whaledeck-platform-control-recovery.py', mode=0o750, limit=LIMIT))
        return dict(installationMode='preserve', filesReplaced=0)
    source = Path(source)
    files = [('whaledeck-platform-control-recovery.py', libexec, 0o750),
        ('whaledeck-platform-control-recovery.service', systemd, 0o644),
        ('whaledeck-platform-control-recovery.timer', systemd, 0o644),
        ('whaledeck-platform-control.conf', tmpfiles, 0o644)]
    contents = {}
    for name, _, _ in files:
        path = source / name
        fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_CLOEXEC | os.O_NONBLOCK)
        try:
            info = os.fstat(fd)
            require(stat.S_ISREG(info.st_mode) and info.st_nlink == 1 and 0 < info.st_size <= LIMIT)
            raw = os.read(fd, LIMIT + 1)
            require(len(raw) == info.st_size and len(raw) <= LIMIT)
            contents[name] = raw
        finally:
            os.close(fd)
    compile(contents[files[0][0]], files[0][0], 'exec')
    for name, destination, permissions in files:
        with Directory(destination, owner=owner, group=group, private=False) as target:
            target.write(name, contents[name], permissions)
    return dict(installationMode='replace', filesReplaced=4)


def public_status(state):
    require(state in ('updating', 'rollingBack', 'healthy', 'failed'))
    messages = {'updating': '正在部署当前 Whale Deck 开发版本。', 'rollingBack': '正在回退 Whale Deck；数据库迁移不会降级。',
        'healthy': 'Whale Deck 维护完成。', 'failed': 'Whale Deck 维护失败或中断；应用不会自动继续或回退，请检查维护记录。'}
    parent = os.open(STATUS.parent, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW | os.O_CLOEXEC)
    temporary, fd = '.control-status-' + uuid.uuid4().hex, None
    try:
        fd = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW | os.O_CLOEXEC, 0o600, dir_fd=parent)
        os.fchown(fd, 0, grp.getgrnam('whaledeck').gr_gid)
        os.fchmod(fd, 0o640)
        raw = encoded(dict(state=state, message=messages[state], updatedAtUtc=dt.datetime.now(dt.timezone.utc).isoformat()))
        with os.fdopen(fd, 'wb', closefd=False) as stream:
            stream.write(raw)
            stream.flush()
            os.fsync(fd)
        a, b = os.fstat(fd), os.stat(temporary, dir_fd=parent, follow_symlinks=False)
        require((a.st_dev, a.st_ino) == (b.st_dev, b.st_ino))
        os.replace(temporary, STATUS.name, src_dir_fd=parent, dst_dir_fd=parent)
        os.fsync(parent)
    finally:
        if fd is not None:
            os.close(fd)
        try:
            os.unlink(temporary, dir_fd=parent)
        except FileNotFoundError:
            pass
        os.close(parent)


def main(args):
    require(os.geteuid() == 0)
    os.umask(0o077)
    if args == ['init-lock']:
        os.close(lock_file())
        return
    if args == ['installation-mode']:
        print(Recovery().installation_mode())
        return
    if len(args) == 2 and args[0] == 'install':
        require_held()
        print(json.dumps(install_sources(args[1])))
        return
    if args == ['recover']:
        fd = lock_file()
        try:
            try:
                fcntl.flock(fd, fcntl.LOCK_EX | fcntl.LOCK_NB)
            except BlockingIOError:
                return
            result = Recovery().recover()
            if result is not None:
                print(json.dumps(result))
        finally:
            os.close(fd)
        return
    require_held()
    if args == ['verify-lock']:
        return
    if args == ['recover-held']:
        Recovery().recover()
    elif len(args) == 2 and args[0] == 'status-held':
        public_status(args[1])
    elif len(args) == 4 and args[0] == 'checkpoint-held':
        print(Recovery().checkpoint(dict(action=args[1], revision=args[2], token=args[3])))
    elif len(args) == 5 and args[0] == 'complete-held':
        Recovery().complete(args[1], dict(action=args[2], revision=args[3], token=args[4]))
    else:
        raise Rejected()


if __name__ == '__main__':
    try:
        main(sys.argv[1:])
    except Exception:
        print(json.dumps({'code': 'PLATFORM_CONTROL_RECOVERY_UNPROVEN', 'message': '无法安全核验或恢复维护工具；原件保留，应用未自动执行或回退。'}, ensure_ascii=False), file=sys.stderr)
        raise SystemExit(1) from None
