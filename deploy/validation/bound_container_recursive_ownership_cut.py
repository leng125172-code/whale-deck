"""QA-only installed recursive chown cut, bound to two kernel FDs and pidfd.

Only an exactly owned fixture and its original SQL plan authorize a signal.
No product checkpoint, receipt mutation, arbitrary PID or host path input.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import signal
import stat
import time

from bound_container_copy_cut import HeldCopy, receipt_json
from bound_container_permission_cut import identity, fingerprint, operation_lock
from validate_container_recursive_ownership_browser import managed_plan

ROOT = Path('/var/lib/whaledeck-container-recursive-ownership')
CGROUP = Path('/sys/fs/cgroup/system.slice')
TRANSPORT = b'/usr/local/libexec/whaledeck-container-recursive-ownership-transport.py'


def child_id(operation, index):
    assert re.fullmatch('[a-f0-9]{32}', operation) and type(index) is int and 0 <= index < 10000
    return hashlib.sha256(b'whaledeck-recursive-ownership-entry-id-v1:' + operation.encode() + b':' + str(index).encode()).hexdigest()[:32]


def record_for(row):
    operation = row['execution_job_id'].replace('-', '')
    assert re.fullmatch('[a-f0-9]{32}', operation)
    value = receipt_json(ROOT / (operation + '.json'))
    if value is None: raise FileNotFoundError(operation)
    plan = managed_plan(json.loads(row['plan_json']))
    assert value['operationId'] == operation and value['owner'] == row['owner_hash'] and value['plan'] == plan
    command = plan['command']
    assert command['containerId'] == row['container_id'] and command['instanceId'] == row['instance_id']
    assert command['path'] == row['relative_path'] == 'work/recursive-tree'
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
    plan = record['plan']; count = plan['entryCount']
    if record['state'] != 'Applying' or not record['historyVerified'] or not 0 <= record['finishedThrough'] <= record['startedThrough'] < count - 1: return None
    assert record['historyMayExist'] and re.fullmatch('[A-F0-9]{64}', record['historyDigest'])
    assert list(identity(source)) == plan['parentIdentity']
    root = source / 'recursive-tree'
    assert not root.is_symlink() and stat.S_ISDIR(root.lstat().st_mode)
    if fingerprint(root.lstat()) != plan['rootIdentity']: return None
    first_id = child_id(record['operationId'], 0)
    first = receipt_json(ROOT / (record['operationId'] + '.entries') / (first_id + '.json'))
    if first is None or first['state'] != 'Completed': return None
    assert first['operationId'] == first_id and first['owner'] == record['owner']
    assert first['ownerConfirmed'] and first['historyVerified'] and not first['plan']['noChange']
    relative = first['plan']['command']['path'].removeprefix('work/recursive-tree/')
    assert re.fullmatch(r'sub/entry-[0-9]{2}\.bin', relative)
    target = root / relative; info = target.lstat()
    assert stat.S_ISREG(info.st_mode) and not target.is_symlink() and info.st_nlink == 1
    assert fingerprint(info) == first['actualIdentity']
    assert [info.st_uid,info.st_gid] == [plan['command']['uid'],plan['command']['gid']]
    assert first['plan']['sourceIdentity'][3:5] != [info.st_uid,info.st_gid]
    assert first['plan']['sourceIdentity'][2] == info.st_mode
    assert [first['plan']['command']['uid'],first['plan']['command']['gid']] == [plan['command']['uid'],plan['command']['gid']]
    return {'firstCompletedId': first_id, 'firstChangedIdentity': fingerprint(info),
        'rootStillOriginal': fingerprint(root.lstat()), 'startedThrough': record['startedThrough'],
        'finishedThrough': record['finishedThrough'], 'entryCount': count, 'rootPhase': record['state']}


def freeze_partial(row, source, container_pid):
    assert os.geteuid() == 0 and re.fullmatch('[a-f0-9]{64}', row['container_id'])
    source = Path(source)
    assert source.parent == Path('/data/WhaleDeck/apps') and re.fullmatch('wdv-container-publish-[a-f0-9]{32}', source.name)
    assert source.resolve(strict=True) == source and not source.is_symlink()
    assert container_pid > 1 and row['instance_id'].startswith(str(container_pid) + ':')
    root_identity = identity(Path(f'/proc/{container_pid}/root'))
    operation = row['execution_job_id'].replace('-', ''); assert re.fullmatch('[a-f0-9]{32}', operation)
    lock = ROOT / (operation + '.lock'); descriptor = pid = group = None
    deadline = time.monotonic() + 25
    try:
        while time.monotonic() < deadline:
            if descriptor is None and lock.exists():
                lock_identity = operation_lock(lock)
                for directory in CGROUP.glob('whaledeck-container-recursive-ownership-*.service'):
                    if not re.fullmatch(r'whaledeck-container-recursive-ownership-[a-f0-9]{32}\.service', directory.name): continue
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
            if record is not None and record['state'] in ('Completed', 'Aborted', 'Partial', 'Unknown'): return None
            if descriptor is not None and record is not None and partial_proof(record, source) is not None:
                if not bound_fds(pid, root_identity, operation_lock(lock)): return None
                try: signal.pidfd_send_signal(descriptor, signal.SIGSTOP)
                except ProcessLookupError: return None
                until = time.monotonic() + 2
                while True:
                    state = Path(f'/proc/{pid}/stat').read_text().rsplit(')', 1)[1].split()[0]
                    if state in ('T', 't'): break
                    if state == 'Z': return None
                    assert time.monotonic() < until, 'RECURSIVE_STOP_NOT_OBSERVED'
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
