#!/usr/bin/env python3
"""Root-owned last-successful deployment ledger; checkout HEAD is never a baseline.

The maintenance shell holds its global deployment lock across prepare/publish/finish.
No automatic rollback, credentials, configuration dump or database downgrade.
"""
import copy
import datetime as dt
import grp
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import sys
import uuid

SOURCE = Path('/data/GitRepos/whale-deck')
STATE = Path('/var/lib/whaledeck-agent/platform-maintenance')
STATUS = Path('/run/whaledeck/maintenance/status.json')
REVISION = re.compile(r'[a-f0-9]{40}')
TOKEN = re.compile(r'[a-f0-9]{32}')
LEDGER = 'deployment-revisions.json'
PREVIOUS = 'previous-revision'


class Rejected(Exception):
    pass


def require(value):
    if not value:
        raise Rejected()


def revision(value):
    require(type(value) is str and REVISION.fullmatch(value) is not None)
    return value


def operation(value):
    require(type(value) is dict and set(value) == {'action', 'revision', 'token'})
    require(value['action'] in ('update', 'rollback'))
    revision(value['revision'])
    require(type(value['token']) is str and TOKEN.fullmatch(value['token']) is not None)


def validate(value):
    require(type(value) is dict and set(value) == {'schemaVersion', 'currentRevision', 'previousRevision', 'pending', 'lastCompleted'})
    require(type(value['schemaVersion']) is int and value['schemaVersion'] == 1)
    revision(value['currentRevision'])
    if value['previousRevision'] is not None:
        revision(value['previousRevision'])
        require(value['previousRevision'] != value['currentRevision'])
    if value['pending'] is not None:
        operation(value['pending'])
    if value['lastCompleted'] is not None:
        operation(value['lastCompleted'])
        require(value['lastCompleted']['revision'] == value['currentRevision'])


def unique(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result)
        result[key] = value
    return result


def write_status(path, state, group, *, owner=0):
    messages = {'updating': '正在部署当前 Whale Deck 开发版本。', 'rollingBack': '正在回退 Whale Deck；数据库迁移不会降级。',
        'healthy': 'Whale Deck 维护完成。', 'failed': 'Whale Deck 发布失败；请修复问题后重新发布。'}
    require(state in messages)
    raw = (json.dumps(dict(state=state, message=messages[state], updatedAtUtc=dt.datetime.now(dt.timezone.utc).isoformat()), ensure_ascii=False) + '\n').encode()
    parent = os.open(path.parent, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW | os.O_CLOEXEC)
    name = '.status-' + uuid.uuid4().hex
    fd = None
    try:
        fd = os.open(name, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW | os.O_CLOEXEC, 0o600, dir_fd=parent)
        os.fchown(fd, owner, group)
        os.fchmod(fd, 0o640)
        with os.fdopen(fd, 'wb', closefd=False) as stream:
            stream.write(raw)
            stream.flush()
            os.fsync(fd)
        a, b = os.fstat(fd), os.stat(name, dir_fd=parent, follow_symlinks=False)
        require((a.st_dev, a.st_ino) == (b.st_dev, b.st_ino))
        os.replace(name, path.name, src_dir_fd=parent, dst_dir_fd=parent)
        os.fsync(parent)
    finally:
        if fd is not None:
            os.close(fd)
        try:
            os.unlink(name, dir_fd=parent)
        except FileNotFoundError:
            pass
        os.close(parent)


class Ledger:
    def __init__(self, root, exists, inspect, *, owner=0):
        self.root, self.exists, self.inspect, self.owner = Path(root), exists, inspect, owner
        self.fd = os.open(root, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW | os.O_CLOEXEC)
        try:
            self.directory()
        except BaseException:
            os.close(self.fd)
            raise

    def __enter__(self):
        return self

    def __exit__(self, *_):
        os.close(self.fd)

    def directory(self):
        a, b = os.fstat(self.fd), self.root.lstat()
        require(stat.S_ISDIR(b.st_mode) and a.st_uid == b.st_uid == self.owner and stat.S_IMODE(a.st_mode) == stat.S_IMODE(b.st_mode) == 0o700)
        require((a.st_dev, a.st_ino) == (b.st_dev, b.st_ino))

    def read(self, name):
        self.directory()
        try:
            fd = os.open(name, os.O_RDONLY | os.O_NOFOLLOW | os.O_CLOEXEC | os.O_NONBLOCK, dir_fd=self.fd)
        except FileNotFoundError:
            return None
        try:
            info = os.fstat(fd)
            require(stat.S_ISREG(info.st_mode) and info.st_nlink == 1 and info.st_uid == self.owner and stat.S_IMODE(info.st_mode) == 0o600 and info.st_size <= 4096)
            raw = os.read(fd, 4097)
            after = os.fstat(fd)
            fingerprint = lambda s: (s.st_dev, s.st_ino, s.st_mode, s.st_uid, s.st_gid, s.st_nlink, s.st_size, s.st_mtime_ns, s.st_ctime_ns)
            require(len(raw) <= 4096 and fingerprint(info) == fingerprint(after))
            return raw
        finally:
            os.close(fd)

    def write(self, name, raw):
        require(len(raw) <= 4096)
        self.read(name)  # Refuse unsafe existing entries rather than replacing them.
        self.directory()
        temporary = '.revision-' + uuid.uuid4().hex
        fd = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW | os.O_CLOEXEC, 0o600, dir_fd=self.fd)
        try:
            os.fchmod(fd, 0o600)
            with os.fdopen(fd, 'wb', closefd=False) as stream:
                stream.write(raw)
                stream.flush()
                os.fsync(fd)
            self.directory()
            os.replace(temporary, name, src_dir_fd=self.fd, dst_dir_fd=self.fd)
            os.fsync(self.fd)
        finally:
            os.close(fd)
            try:
                os.unlink(temporary, dir_fd=self.fd)
            except FileNotFoundError:
                pass

    def load(self):
        raw = self.read(LEDGER)
        if raw is None:
            return None
        value = json.loads(raw, object_pairs_hook=unique)
        validate(value)
        return value

    def save(self, value):
        validate(value)
        self.write(LEDGER, (json.dumps(value, sort_keys=True, separators=(',', ':')) + '\n').encode())

    def project_previous(self, value):
        # This file is only a compatibility projection. Rollback reads the ledger.
        old = self.read(PREVIOUS)
        if value['previousRevision'] is None:
            if old is not None:
                os.unlink(PREVIOUS, dir_fd=self.fd)
                os.fsync(self.fd)
        else:
            self.write(PREVIOUS, (value['previousRevision'] + '\n').encode())

    def prepare(self, action, target=None):
        require(action in ('update', 'rollback') and (action == 'update' or target is None))
        if action == 'update':
            revision(target)
            require(self.exists(target))
        value = self.load()
        if value is None:
            # A legacy checkout/previous file cannot establish rollback history.
            require(action == 'update')
            current = revision(self.inspect())
            require(self.exists(current))
            old = self.read(PREVIOUS)
            if old is not None:
                revision(old.decode('ascii').strip())
                archive = self.read('previous-revision.legacy')
                require(archive is None or archive == old)
                if archive is None:
                    self.write('previous-revision.legacy', old)
            value = dict(schemaVersion=1, currentRevision=current, previousRevision=None, pending=None, lastCompleted=None)
        if action == 'rollback':
            pending = value['pending']
            target = (pending['revision'] if pending['action'] == 'rollback' else value['currentRevision']) if pending else value['previousRevision']
        if action == 'rollback':
            revision(target)
            require(self.exists(target))
        pending = value['pending']
        if pending is None or pending['action'] != action or pending['revision'] != target:
            pending = dict(action=action, revision=target, token=uuid.uuid4().hex)
            value = {**value, 'pending': pending}
            self.save(value)
        self.project_previous(value)
        return copy.deepcopy(pending)

    def finish(self, expected):
        operation(expected)
        value = self.load()
        require(value is not None)
        repeated = value['pending'] is None and value['lastCompleted'] == expected
        require(repeated or value['pending'] == expected)
        require(self.inspect() == expected['revision'])
        if not repeated:
            old = value['currentRevision']
            value = {**value, 'currentRevision': expected['revision'],
                'previousRevision': old if old != expected['revision'] else value['previousRevision'],
                'pending': None, 'lastCompleted': copy.deepcopy(expected)}
            self.save(value)
        self.project_previous(value)
        return copy.deepcopy(value)


def run(arguments):
    result = subprocess.run(arguments, capture_output=True, timeout=40, check=False)
    require(result.returncode == 0 and len(result.stdout) <= 16384)
    return result.stdout.decode('utf-8').strip()


def commit_exists(value):
    revision(value)
    return run(['/usr/bin/git', '-c', 'safe.directory=' + str(SOURCE), '-C', str(SOURCE), 'cat-file', '-t', value]) == 'commit'


class RunningRevision:
    def __init__(self, command=run):
        self.run = command

    def containers(self):
        result = {}
        for service in ('api', 'worker', 'gateway'):
            value = self.run(['docker', 'inspect', '--format', '{{.Id}}|{{.Image}}|{{.State.Running}}', 'whaledeck-' + service]).split('|')
            require(len(value) == 3 and re.fullmatch('[a-f0-9]{64}', value[0]) and re.fullmatch('sha256:[a-f0-9]{64}', value[1]) and value[2] == 'true')
            result[service] = value
        return result

    def hosts(self):
        result = {}
        for name, app in (('agent', 'WhaleDeck.Agent'), ('maintenance', 'WhaleDeck.MaintenanceHost')):
            unit = 'whaledeck-' + name + '.service'
            require(self.run(['systemctl', 'show', unit, '-p', 'ActiveState', '--value']) == 'active')
            pid = self.run(['systemctl', 'show', unit, '-p', 'MainPID', '--value'])
            require(pid.isdecimal() and int(pid) > 1)
            exe = Path('/proc') / pid / 'exe'
            installed = Path('/opt/whaledeck-' + name) / app
            a, b = exe.stat(), installed.stat()
            require(exe.readlink().name == app and (a.st_dev, a.st_ino) == (b.st_dev, b.st_ino))
            dll = Path(str(installed) + '.dll')
            info = dll.stat()
            matched = False
            for line in (Path('/proc') / pid / 'maps').read_text().splitlines():
                fields = line.split(maxsplit=5)
                if len(fields) == 6 and fields[5] == str(dll):
                    device = tuple(int(n, 16) for n in fields[3].split(':'))
                    require(device == (os.major(info.st_dev), os.minor(info.st_dev)) and int(fields[4]) == info.st_ino)
                    matched = True
            require(matched)
            result[name] = (pid, a.st_dev, a.st_ino, info.st_dev, info.st_ino)
        return result

    def __call__(self):
        containers, hosts = self.containers(), self.hosts()
        migrator = self.run(['docker', 'image', 'inspect', '--format', '{{.Id}}', 'whaledeck-migrator'])
        require(re.fullmatch('sha256:[a-f0-9]{64}', migrator) is not None)
        values = []
        def accept(raw, component):
            data = json.loads(raw, object_pairs_hook=unique)
            require(type(data) is dict and set(data) == {'component', 'version', 'sourceRevision'} and data['component'] == component)
            require(type(data['version']) is str and re.fullmatch(r'[0-9]{1,5}\.[0-9]{1,5}\.[0-9]{1,5}(?:[-+][A-Za-z0-9.-]{1,40})?', data['version']) is not None)
            values.append(revision(data['sourceRevision']))
        for service, frame in containers.items():
            label = self.run(['docker', 'image', 'inspect', '--format', '{{index .Config.Labels "org.opencontainers.image.revision"}}', frame[1]])
            revision(label)
            command = ['cat', '/usr/share/nginx/build-info.json'] if service == 'gateway' else ['dotnet', 'WhaleDeck.' + service.title() + '.dll', '--build-info']
            accept(self.run(['docker', 'exec', 'whaledeck-' + service, *command]), 'WhaleDeck.' + service.title())
            require(values[-1] == label)
        for service, frame in hosts.items():
            accept(self.run(['/proc/' + frame[0] + '/exe', '--build-info']), 'WhaleDeck.' + ('Agent' if service == 'agent' else 'MaintenanceHost'))
        accept(self.run(['docker', 'run', '--rm', '--network', 'none', '--read-only', '--cap-drop', 'ALL', '--security-opt', 'no-new-privileges:true', migrator, '--build-info']), 'WhaleDeck.Migrator')
        require(len(set(values)) == 1 and self.containers() == containers and self.hosts() == hosts)
        require(self.run(['docker', 'image', 'inspect', '--format', '{{.Id}}', 'whaledeck-migrator']) == migrator)
        return values[0]


def main(args):
    require(os.geteuid() == 0)
    os.umask(0o077)
    os.environ['GIT_OPTIONAL_LOCKS'] = '0'
    if args == ['probe']:
        print(RunningRevision()())
        return
    if len(args) == 2 and args[0] == 'status':
        write_status(STATUS, args[1], grp.getgrnam('whaledeck').gr_gid)
        return
    with Ledger(STATE, commit_exists, RunningRevision()) as ledger:
        if len(args) in (2, 3) and args[0] == 'prepare':
            require((args[1] == 'update' and len(args) == 3) or (args[1] == 'rollback' and len(args) == 2))
            op = ledger.prepare(args[1], args[2] if len(args) == 3 else None)
            print(op['revision'] + ' ' + op['token'])
        elif len(args) == 4 and args[0] == 'finish':
            ledger.finish(dict(action=args[1], revision=args[2], token=args[3]))
        else:
            raise Rejected()


if __name__ == '__main__':
    try:
        main(sys.argv[1:])
    except Exception:
        print(json.dumps({'code': 'PLATFORM_REVISION_UNPROVEN', 'message': '无法证明可信运行版本或发布记录，已拒绝；未自动回退。'}, ensure_ascii=False), file=sys.stderr)
        raise SystemExit(1) from None
