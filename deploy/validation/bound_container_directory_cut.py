"""Pause only an installed mkdir Apply with both exact Root and lock FD proof.

Root-only validation utility, never a product endpoint. It accepts no PID or
command. A pidfd is obtained only for the fixed Python transport in its exact
systemd cgroup, holding the owned container root and this operation's lock.
The actual permanent Root record must match the accepted business intent.
Missed cuts are reported as missed, never as interrupted publications.
"""
import json
import os
from pathlib import Path
import re
import signal
import stat
import time

ROOT = Path('/var/lib/whaledeck-container-directories')
CGROUP = Path('/sys/fs/cgroup/system.slice')
TRANSPORT = b'/usr/local/libexec/whaledeck-container-directory-transport.py'


def identity(path):
    value = path.stat(); return value.st_dev, value.st_ino


class HeldDirectory:
    def __init__(self, pid, descriptor, cgroup, record):
        self.pid, self.descriptor, self.cgroup, self.record = pid, descriptor, cgroup, record

    def kill(self):
        assert self.descriptor is not None
        signal.pidfd_send_signal(self.descriptor, signal.SIGKILL)
        deadline = time.monotonic() + 8
        while Path('/proc/' + str(self.pid)).exists():
            state = Path('/proc/' + str(self.pid) + '/stat')
            try:
                if state.read_text().rsplit(')', 1)[1].split()[0] == 'Z': break
            except FileNotFoundError: break
            assert time.monotonic() < deadline, 'BOUND_DIRECTORY_DRIVER_DID_NOT_EXIT'
            time.sleep(0.01)
        os.close(self.descriptor); self.descriptor = None

    def release(self):
        if self.descriptor is not None:
            try: signal.pidfd_send_signal(self.descriptor, signal.SIGCONT)
            except ProcessLookupError: pass
            finally: os.close(self.descriptor); self.descriptor = None


def record_for(row):
    operation = row['id'].replace('-', ''); assert re.fullmatch('[a-f0-9]{32}', operation)
    path = ROOT / (operation + '.json'); info = path.lstat()
    assert stat.S_ISREG(info.st_mode) and info.st_uid == 0 and info.st_nlink == 1
    assert stat.S_IMODE(info.st_mode) == 0o600 and 0 < info.st_size <= 16384
    value = json.loads(path.read_bytes()); plan = json.loads(row['plan_json'])
    assert value['operationId'] == operation and value['owner'] == row['owner_hash']
    assert value['plan']['fingerprint'] == plan['Fingerprint']
    assert value['plan']['command'] == {'containerId': row['container_id'], 'instanceId': row['instance_id'],
        'path': row['relative_path'], 'mode': plan['Command']['Mode']}
    return value


def bound_fds(pid, root_identity, lock_identity):
    try:
        args = Path(f'/proc/{pid}/cmdline').read_bytes().rstrip(b'\0').split(b'\0')
        if args != [b'/usr/bin/python3', b'-I', TRANSPORT, b'apply']: return False
        if Path(f'/proc/{pid}').stat().st_uid != 0: return False
        found = set()
        for descriptor in Path(f'/proc/{pid}/fd').iterdir():
            try: found.add(identity(descriptor))
            except (FileNotFoundError, PermissionError): pass
        return root_identity in found and lock_identity in found
    except (FileNotFoundError, ProcessLookupError, PermissionError): return False


def disposable_private(record, source):
    # Prove that a frozen pre-publication cut will not strand an unrecorded
    # private object. Foreign/nonempty/unbound objects are never cleaned.
    if record['state'] not in ('Preparing', 'Prepared', 'Applying'): return False
    leaf = record['plan']['command']['path'].removeprefix('work/')
    if not leaf or '/' in leaf or os.path.lexists(source / leaf): return False
    private = source / '.whaledeck-container-directories-v1'
    if not private.exists(): return record['privateIdentity'] is None and record['pendingIdentity'] is None
    info = private.lstat()
    if not stat.S_ISDIR(info.st_mode) or stat.S_IMODE(info.st_mode) != 0o700 or info.st_uid != 0: return False
    if list(identity(private)) != record['privateIdentity']: return False
    pending = private / (record['operationId'] + '.directory')
    names = {entry.name for entry in private.iterdir()}
    if not names: return record['pendingIdentity'] is None
    if names != {pending.name} or not pending.is_dir() or pending.is_symlink(): return False
    return list(identity(pending)) == record['pendingIdentity'] and not list(pending.iterdir())


def freeze_prepublication(row, source, container_pid):
    assert os.geteuid() == 0 and re.fullmatch('[a-f0-9]{64}', row['container_id'])
    source = Path(source)
    assert source.parent == Path('/data/WhaleDeck/apps') and re.fullmatch('wdv-container-publish-[a-f0-9]{32}', source.name)
    assert not source.is_symlink()
    plan = json.loads(row['plan_json']); assert list(identity(source)) == [plan['ParentIdentity']['Device'], plan['ParentIdentity']['Inode']]
    assert container_pid > 0
    root_identity = identity(Path(f'/proc/{container_pid}/root'))
    operation = row['id'].replace('-', ''); lock = ROOT / (operation + '.lock')
    deadline = time.monotonic() + 20
    descriptor = None; pid = None; group = None
    try:
        while time.monotonic() < deadline:
            if descriptor is None and lock.exists():
                lock_identity = identity(lock)
                for directory in CGROUP.glob('whaledeck-container-directories-*.service'):
                    if not re.fullmatch(r'whaledeck-container-directories-[a-f0-9]{32}\.service', directory.name): continue
                    try: pids = (directory / 'cgroup.procs').read_text().split()
                    except FileNotFoundError: continue
                    for candidate in pids:
                        if candidate.isdecimal() and bound_fds(int(candidate), root_identity, lock_identity):
                            pid = int(candidate)
                            try: descriptor = os.pidfd_open(pid)
                            except ProcessLookupError: continue
                            group = directory.name
                            break
                    if descriptor is not None: break
            try: value = record_for(row)
            except FileNotFoundError: value = None
            if value is not None and value['state'] in ('Completed', 'Aborted', 'Unknown'): return None
            if descriptor is not None and value is not None:
                if not bound_fds(pid, root_identity, identity(lock)): return None
                try: signal.pidfd_send_signal(descriptor, signal.SIGSTOP)
                except ProcessLookupError: return None
                stopped_deadline = time.monotonic() + 2
                while True:
                    state = Path(f'/proc/{pid}/stat').read_text().rsplit(')', 1)[1].split()[0]
                    if state in ('T', 't'): break
                    if state == 'Z': return None
                    assert time.monotonic() < stopped_deadline, 'DIRECTORY_STOP_NOT_OBSERVED'
                    time.sleep(0.0005)
                frozen = record_for(row)
                if disposable_private(frozen, source):
                    held = HeldDirectory(pid, descriptor, group, frozen); descriptor = None; return held
                signal.pidfd_send_signal(descriptor, signal.SIGCONT); return None
            time.sleep(0.0005)
        return None
    finally:
        if descriptor is not None:
            try: signal.pidfd_send_signal(descriptor, signal.SIGCONT)
            except ProcessLookupError: pass
            finally: os.close(descriptor)
