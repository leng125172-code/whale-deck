#!/usr/bin/env python3
"""Root-only partial-copy observer; never a product RPC or arbitrary PID tool.

Bind pidfd to the exact installed apply transport, operation lock FD and owned
container root FD. Only stop while an inode-recorded private file has some but
not all bytes, with no published target. The product must subsequently kill it.
"""
import json
import os
from pathlib import Path
import re
import select
import signal
import stat
import time

ROOT = Path('/var/lib/whaledeck-container-copies')
CGROUP = Path('/sys/fs/cgroup/system.slice')
TRANSPORT = b'/usr/local/libexec/whaledeck-container-copy-transport.py'


def identity(path):
    value = path.stat(); return value.st_dev, value.st_ino


def receipt_json(path):
    """Read one immutable receipt inode while the producer atomically replaces it."""
    try: descriptor = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK | os.O_CLOEXEC)
    except FileNotFoundError: return None
    try:
        info = os.fstat(descriptor)
        # Atomic replacement can detach the old inode after open. That sample
        # cannot authorize a signal; try a later linked receipt instead.
        if info.st_nlink == 0: return None
        assert stat.S_ISREG(info.st_mode) and info.st_uid == 0 and stat.S_IMODE(info.st_mode) == 0o600 and info.st_nlink == 1 and 0 < info.st_size <= 32768, 'COPY_OBSERVER_UNSAFE_RECEIPT'
        raw = os.read(descriptor, 32769)
        assert len(raw) == info.st_size, 'COPY_OBSERVER_RECEIPT_LENGTH_CHANGED'
        try: current = path.lstat()
        except FileNotFoundError: return None
        if (current.st_dev, current.st_ino) != (info.st_dev, info.st_ino): return None
        return json.loads(raw)
    finally: os.close(descriptor)


def record_for(row):
    operation = row['execution_job_id'].replace('-', ''); assert re.fullmatch('[a-f0-9]{32}', operation)
    path = ROOT / (operation + '.json'); value = receipt_json(path)
    if value is None: raise FileNotFoundError(path)
    plan = json.loads(row['plan_json']); command = json.loads(row['command_json'])
    assert value['operationId'] == operation and value['owner'] == row['owner_hash'] and value['plan']['kind'] == plan['Kind'] == 'File'
    assert value['plan']['fingerprint'] == plan['Fingerprint']
    assert value['plan']['command'] == {'containerId': row['container_id'], 'instanceId': row['instance_id'],
        'sourcePath': row['source_path'], 'targetPath': row['target_path'], 'expectedVersion': command['ExpectedVersion']}
    return value


def bound_fds(pid, root_identity, lock_identity):
    try:
        args = Path(f'/proc/{pid}/cmdline').read_bytes().rstrip(b'\0').split(b'\0')
        if args != [b'/usr/bin/python3', b'-I', TRANSPORT, b'apply'] or Path(f'/proc/{pid}').stat().st_uid != 0: return False
        found = set()
        for descriptor in Path(f'/proc/{pid}/fd').iterdir():
            try: found.add(identity(descriptor))
            except (FileNotFoundError, PermissionError): pass
        return root_identity in found and lock_identity in found
    except (FileNotFoundError, ProcessLookupError, PermissionError): return False


def partial_proof(record, source):
    assert record['plan']['kind'] == 'File'
    if record['state'] != 'Running': return None
    command = record['plan']['command']; leaf = command['targetPath'].removeprefix('work/')
    if not leaf or '/' in leaf or command['targetPath'] != 'work/' + leaf or os.path.lexists(source / leaf): return None
    assert list(identity(source)) == record['plan']['targetParent']
    operation = record['operationId']; path = ROOT / 'engines/operations' / (operation + '.json')
    inner = receipt_json(path)
    if inner is None: return None
    assert inner['operationId'] == operation and inner['owner'] == record['owner'] and inner['plan'] == record['plan']['innerPlan']
    if inner['state'] != 'Preparing' or 'newIdentity' not in inner: return None
    private = source / '.whaledeck-container-copies-v1'
    info = private.lstat(); assert stat.S_ISDIR(info.st_mode) and info.st_uid == 0 and stat.S_IMODE(info.st_mode) == 0o700
    pending = private / (operation + '.mutation')
    if {p.name for p in private.iterdir()} != {pending.name}: return None
    info = pending.lstat()
    assert stat.S_ISREG(info.st_mode) and info.st_nlink == 1 and info.st_uid == 0 and stat.S_IMODE(info.st_mode) == 0o600
    assert [info.st_dev, info.st_ino] == inner['newIdentity']
    if not 0 < info.st_size < inner['plan']['sizeBytes']: return None
    return {'inner': inner, 'partialBytes': info.st_size, 'totalBytes': inner['plan']['sizeBytes'],
            'privateIdentity': list(identity(private)), 'pendingIdentity': [info.st_dev, info.st_ino]}


class HeldCopy:
    def __init__(self, pid, descriptor, group, record, proof):
        self.pid, self.descriptor, self.cgroup, self.record, self.proof = pid, descriptor, group, record, proof

    def wait_exit_seconds(self, began, timeout=15):
        """Observe pidfd concurrently with the action, excluding service drain.

        A late waiter cannot prove an earlier exit. Keep the original absolute
        deadline even if its observer thread is scheduled late.
        """
        assert self.descriptor is not None and timeout > 0
        remaining = timeout - (time.monotonic() - began)
        if remaining <= 0: return None
        readable, _, _ = select.select([self.descriptor], [], [], remaining)
        elapsed = time.monotonic() - began
        return elapsed if readable and elapsed < timeout else None

    def release(self):
        if self.descriptor is not None:
            try: signal.pidfd_send_signal(self.descriptor, signal.SIGCONT)
            except ProcessLookupError: pass
            finally: os.close(self.descriptor); self.descriptor = None


def freeze_partial_copy(row, source, container_pid):
    assert os.geteuid() == 0 and re.fullmatch('[a-f0-9]{64}', row['container_id'])
    source = Path(source); assert source.parent == Path('/data/WhaleDeck/apps') and re.fullmatch('wdv-container-publish-[a-f0-9]{32}', source.name)
    assert source.resolve(strict=True) == source and not source.is_symlink()
    assert container_pid > 1 and row['instance_id'].startswith(str(container_pid) + ':')
    root_identity = identity(Path(f'/proc/{container_pid}/root'))
    operation = row['execution_job_id'].replace('-', ''); assert re.fullmatch('[a-f0-9]{32}', operation)
    lock = ROOT / (operation + '.lock'); descriptor = None; pid = None; group = None
    deadline = time.monotonic() + 25
    try:
        while time.monotonic() < deadline:
            if descriptor is None and lock.exists():
                lock_identity = identity(lock)
                for directory in CGROUP.glob('whaledeck-container-copies-*.service'):
                    if not re.fullmatch(r'whaledeck-container-copies-[a-f0-9]{32}\.service', directory.name): continue
                    try: pids = (directory / 'cgroup.procs').read_text().split()
                    except FileNotFoundError: continue
                    for candidate in pids:
                        if candidate.isdecimal() and bound_fds(int(candidate), root_identity, lock_identity):
                            pid = int(candidate)
                            try: descriptor = os.pidfd_open(pid)
                            except ProcessLookupError: continue
                            group = directory.name; break
                    if descriptor is not None: break
            try: record = record_for(row)
            except FileNotFoundError: record = None
            if record is not None and record['state'] in ('Completed', 'Aborted', 'Unknown'): return None
            if descriptor is not None and record is not None and partial_proof(record, source) is not None:
                if not bound_fds(pid, root_identity, identity(lock)): return None
                try: signal.pidfd_send_signal(descriptor, signal.SIGSTOP)
                except ProcessLookupError: return None
                until = time.monotonic() + 2
                while True:
                    state = Path(f'/proc/{pid}/stat').read_text().rsplit(')', 1)[1].split()[0]
                    if state in ('T', 't'): break
                    if state == 'Z': return None
                    assert time.monotonic() < until, 'COPY_STOP_NOT_OBSERVED'
                    time.sleep(0.0005)
                frozen = record_for(row); proof = partial_proof(frozen, source)
                if proof is not None:
                    held = HeldCopy(pid, descriptor, group, frozen, proof); descriptor = None; return held
                signal.pidfd_send_signal(descriptor, signal.SIGCONT); return None
            time.sleep(0.0005)
        return None
    finally:
        if descriptor is not None:
            try: signal.pidfd_send_signal(descriptor, signal.SIGCONT)
            except ProcessLookupError: pass
            finally: os.close(descriptor)
