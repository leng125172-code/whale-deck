#!/usr/bin/env python3
"""Root-owned, fixed-user PTY driver. Input/output never enter journald or files.

Runs only inside the fixed transient systemd unit. Its complete process cgroup
is killed on driver exit, timeout, helper failure, or Agent shutdown.
"""
import base64
import binascii
import errno
import fcntl
import grp
import json
import os
from pathlib import Path
import pwd
import re
import selectors
import signal
import stat
import struct
import sys
import termios
import tempfile
import time

STATE = Path('/var/lib/whaledeck-terminal')
HOME = '/var/lib/whaledeck-shell'
MAX_LINE = 8192
MAX_INPUT = 1024 * 1024
MAX_OUTPUT = 16 * 1024 * 1024
LIFETIME = 600
IDLE = 120


def frame(raw):
    if len(raw) > MAX_LINE:
        raise ValueError('FRAME_INVALID')
    def unique(pairs):
        result = {}
        for key, value in pairs:
            if key in result:
                raise ValueError('FRAME_INVALID')
            result[key] = value
        return result
    value = json.loads(raw, object_pairs_hook=unique)
    if not isinstance(value, dict):
        raise ValueError('FRAME_INVALID')
    kind = value.get('type')
    if kind in ('open', 'resize'):
        if set(value) != {'type', 'columns', 'rows'} or any(type(value[key]) is not int or not 2 <= value[key] <= 400 for key in ('columns', 'rows')):
            raise ValueError('FRAME_INVALID')
    elif kind == 'input':
        if set(value) != {'type', 'data'} or not isinstance(value['data'], str):
            raise ValueError('FRAME_INVALID')
        value['data'] = base64.b64decode(value['data'], validate=True)
        if not 1 <= len(value['data']) <= 4096:
            raise ValueError('FRAME_INVALID')
    elif kind != 'close' or set(value) != {'type'}:
        raise ValueError('FRAME_INVALID')
    return value


def emit(value):
    packet = json.dumps(value, separators=(',', ':')).encode() + b'\n'
    deadline = time.monotonic() + 5
    while packet:
        try:
            count = os.write(1, packet)
            if count == 0:
                raise BrokenPipeError()
            packet = packet[count:]
        except BlockingIOError:
            if time.monotonic() > deadline:
                raise TimeoutError('SLOW_CONSUMER')
            with selectors.DefaultSelector() as writable:
                writable.register(1, selectors.EVENT_WRITE)
                writable.select(.1)


def process_start(pid):
    try:
        text = Path(f'/proc/{pid}/stat').read_text()
        return text[text.rfind(')') + 2:].split()[19]
    except (OSError, IndexError):
        return None


def reserve(session):
    info = STATE.lstat()
    if not stat.S_ISDIR(info.st_mode) or info.st_uid != 0 or stat.S_IMODE(info.st_mode) != 0o700:
        raise PermissionError('STATE_UNSAFE')
    lock = os.open(STATE / 'lock', os.O_CREAT | os.O_RDWR | os.O_NOFOLLOW, 0o600)
    try:
        fcntl.flock(lock, fcntl.LOCK_EX)
        count = active = 0
        boot_id = Path('/proc/sys/kernel/random/boot_id').read_text().strip()
        for path in STATE.iterdir():
            if not re.fullmatch(r'[a-f0-9]{32}\.json', path.name):
                continue
            metadata = path.lstat()
            if not stat.S_ISREG(metadata.st_mode) or metadata.st_uid != 0 or metadata.st_size > 4096:
                raise PermissionError('STATE_UNSAFE')
            row = json.loads(path.read_bytes())
            if row.get('version') == 2:
                alive = row.get('bootId') == boot_id and row.get('active') is True and 0 <= time.monotonic() - row['monotonic'] < 670
            else:
                alive = row.get('bootId') == boot_id and row.get('start') is not None and process_start(row.get('pid', 0)) == row['start']
            if not alive and time.time() - metadata.st_mtime > 86400:
                path.unlink()
                continue
            count += 1
            active += int(alive)
            if count >= 4096 or active >= 4:
                raise ValueError('SESSION_BUDGET')
        fd = os.open(STATE / (session + '.json'), os.O_CREAT | os.O_EXCL | os.O_WRONLY | os.O_NOFOLLOW, 0o600)
        with os.fdopen(fd, 'w') as record:
            json.dump({'version': 2, 'sessionId': session, 'invocationId': invocation(), 'active': True, 'monotonic': time.monotonic(), 'bootId': boot_id, 'createdAt': int(time.time())}, record)
            record.flush()
            os.fsync(record.fileno())
        sync_directory()
    finally:
        os.close(lock)


def sync_directory():
    directory = os.open(STATE, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
    try: os.fsync(directory)
    finally: os.close(directory)


def invocation():
    value = os.environ.get('INVOCATION_ID', '')
    if not re.fullmatch(r'[a-f0-9]{32}', value): raise PermissionError('UNIT_REQUIRED')
    return value


def finish(session):
    lock = os.open(STATE / 'lock', os.O_CREAT | os.O_RDWR | os.O_NOFOLLOW, 0o600)
    temporary = None
    try:
        fcntl.flock(lock, fcntl.LOCK_EX)
        path = STATE / (session + '.json')
        if not path.exists(): return
        info = path.lstat()
        if not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_size > 4096: raise PermissionError('STATE_UNSAFE')
        row = json.loads(path.read_bytes())
        # A rejected duplicate's stop hook cannot close the original record.
        if row.get('invocationId') != invocation(): return
        row['active'] = False
        row['closedAt'] = int(time.time())
        fd, temporary = tempfile.mkstemp(prefix=session + '.', suffix='.tmp', dir=STATE)
        with os.fdopen(fd, 'w') as record:
            json.dump(row, record); record.flush(); os.fsync(record.fileno())
        os.replace(temporary, path); temporary = None
        sync_directory()
    finally:
        if temporary is not None: os.unlink(temporary)
        os.close(lock)


def identity():
    user = pwd.getpwnam('whaledeck-shell')
    memberships = [item.gr_name for item in grp.getgrall() if 'whaledeck-shell' in item.gr_mem]
    if user.pw_uid == 0 or user.pw_dir != HOME or grp.getgrgid(user.pw_gid).gr_name != 'whaledeck-shell' or any(name != 'whaledeck-shell' for name in memberships):
        raise PermissionError('IDENTITY_UNSAFE')
    directory = Path(HOME).lstat()
    if not stat.S_ISDIR(directory.st_mode) or directory.st_uid != user.pw_uid or stat.S_IMODE(directory.st_mode) != 0o700:
        raise PermissionError('IDENTITY_UNSAFE')
    return user


def approved_host():
    path = Path('/etc/whaledeck/resources.json')
    info = path.lstat()
    if not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_mode & 0o022 or info.st_size > 65536:
        raise PermissionError('REGISTRY_UNSAFE')
    resources = json.loads(path.read_bytes())
    if not isinstance(resources, list): raise ValueError('REGISTRY_UNSAFE')
    matches = [row for row in resources if isinstance(row, dict) and row.get('id') == 'whaledeck.host']
    if len(matches) != 1 or matches[0].get('type') != 'Host' or matches[0].get('externalId') != 'local' or 'terminal-open' not in matches[0].get('allowedActions', []):
        raise PermissionError('HOST_NOT_APPROVED')


def run(session):
    # Require systemd ownership; running the raw driver outside the unit would
    # lose the cgroup cleanup guarantee for children that create a new session.
    invocation()
    user = identity()
    if os.geteuid() != user.pw_uid or os.getuid() != user.pw_uid or os.getgid() != user.pw_gid or not set(os.getgroups()) <= {user.pw_gid}:
        raise PermissionError('IDENTITY_UNSAFE')
    os.set_blocking(0, False)
    os.set_blocking(1, False)
    started = last_input = time.monotonic()
    heartbeat = started
    buffer = bytearray()
    child = master = None
    incoming = outgoing = 0
    tokens = 65536.0
    token_time = started
    reason = 'TERMINAL_CLOSED'
    status = None
    with selectors.DefaultSelector() as selector:
        selector.register(0, selectors.EVENT_READ)
        try:
            while True:
                now = time.monotonic()
                if now - started > LIFETIME:
                    reason = 'TERMINAL_EXPIRED'; break
                if now - last_input > (IDLE if child else 10):
                    reason = 'TERMINAL_IDLE'; break
                if child and now - heartbeat >= 5:
                    emit({'type': 'heartbeat'}); heartbeat = now
                if child:
                    exited, result = os.waitpid(child, os.WNOHANG)
                    if exited:
                        status = result
                        # Drain the remaining bounded PTY tail, then close.
                        try:
                            while True:
                                data = os.read(master, 4096)
                                if not data: break
                                outgoing += len(data)
                                if outgoing > MAX_OUTPUT: reason = 'TERMINAL_OUTPUT_LIMIT'; break
                                emit({'type': 'output', 'data': base64.b64encode(data).decode()})
                        except OSError as error:
                            if error.errno not in (errno.EAGAIN, errno.EIO): raise
                        break
                for event, _ in selector.select(.25):
                    if event.fd == master:
                        try: data = os.read(master, 4096)
                        except OSError as error:
                            if error.errno == errno.EIO: continue
                            raise
                        if data:
                            outgoing += len(data)
                            if outgoing > MAX_OUTPUT: reason = 'TERMINAL_OUTPUT_LIMIT'; return
                            emit({'type': 'output', 'data': base64.b64encode(data).decode()})
                        continue
                    data = os.read(0, MAX_LINE + 1)
                    if not data: return
                    buffer.extend(data)
                    while b'\n' in buffer:
                        line, _, remainder = buffer.partition(b'\n')
                        buffer = bytearray(remainder)
                        command = frame(line)
                        kind = command['type']
                        last_input = time.monotonic()
                        if kind == 'close': return
                        if kind == 'open':
                            if child: raise ValueError('FRAME_INVALID')
                            child, master = os.forkpty()
                            if child == 0:
                                try:
                                    # PID 1 already created this whole process
                                    # tree as the fixed unprivileged identity.
                                    os.umask(0o077); os.chdir(HOME)
                                    os.closerange(3, 1048576)
                                    os.execve('/bin/bash', ['bash', '--noprofile', '--norc', '-i'], {
                                        'HOME': HOME, 'USER': user.pw_name, 'LOGNAME': user.pw_name,
                                        'TERM': 'xterm-256color', 'LANG': 'C.UTF-8', 'PATH': '/usr/local/bin:/usr/bin:/bin', 'SHELL': '/bin/bash', 'HISTFILE': '/dev/null'})
                                except BaseException:
                                    os._exit(126)
                            os.set_blocking(master, False)
                            selector.register(master, selectors.EVENT_READ)
                            emit({'type': 'ready', 'invocationId': os.environ['INVOCATION_ID']})
                        if kind in ('open', 'resize'):
                            if not child: raise ValueError('FRAME_INVALID')
                            fcntl.ioctl(master, termios.TIOCSWINSZ, struct.pack('HHHH', command['rows'], command['columns'], 0, 0))
                        elif kind == 'input':
                            if not child: raise ValueError('FRAME_INVALID')
                            now = time.monotonic()
                            tokens = min(65536, tokens + (now - token_time) * 32768); token_time = now
                            incoming += len(command['data'])
                            if incoming > MAX_INPUT or len(command['data']) > tokens:
                                reason = 'TERMINAL_INPUT_LIMIT'; return
                            tokens -= len(command['data'])
                            # Nonblocking PTY writes may be partial; never drop
                            # bytes or block indefinitely behind an unread child.
                            pending = command['data']; deadline = now + 2
                            while pending:
                                try: count = os.write(master, pending); pending = pending[count:]
                                except BlockingIOError:
                                    if time.monotonic() > deadline: raise TimeoutError('INPUT_BLOCKED')
                                    time.sleep(.01)
                    if len(buffer) > MAX_LINE: raise ValueError('FRAME_INVALID')
        except (OSError, ValueError, KeyError, TimeoutError):
            reason = 'TERMINAL_REJECTED'
            raise
        finally:
            if child and status is None:
                try: os.killpg(child, signal.SIGKILL)
                except ProcessLookupError: pass
                try: _, status = os.waitpid(child, 0)
                except ChildProcessError: pass
            if master is not None: os.close(master)
            try: emit({'type': 'exit', 'code': reason, 'exitCode': os.waitstatus_to_exitcode(status) if status is not None else -1})
            except (OSError, TimeoutError): pass


if __name__ == '__main__':
    try:
        if len(sys.argv) != 3 or not re.fullmatch(r'[a-f0-9]{32}', sys.argv[2]): raise ValueError('TERMINAL_INVALID')
        action, session = sys.argv[1:]
        if action == 'run': run(session)
        elif action in ('reserve', 'finish') and os.geteuid() == 0:
            invocation()
            if action == 'reserve': approved_host(); identity(); reserve(session)
            else: finish(session)
        else: raise PermissionError('TERMINAL_DENIED')
    except (OSError, ValueError, KeyError, TypeError, RecursionError, TimeoutError, binascii.Error):
        try: emit({'type': 'error', 'code': 'TERMINAL_REJECTED'})
        except (OSError, TimeoutError): pass
        raise SystemExit(2)
