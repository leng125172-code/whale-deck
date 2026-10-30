"""QA-only exact pidfd/Root/lock binding for an installed ordinary chmod.

No installed hooks, arbitrary PID/command, Root edits or host path input. A cut
counts only while the named synthetic inode has its full original fingerprint.
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

ROOT = Path('/var/lib/whaledeck-container-permissions')
CGROUP = Path('/sys/fs/cgroup/system.slice')
TRANSPORT = b'/usr/local/libexec/whaledeck-container-permission-transport.py'


def identity(path):
    info = path.stat()
    return info.st_dev, info.st_ino


def fingerprint(info):
    return [info.st_dev, info.st_ino, info.st_mode, info.st_uid, info.st_gid,
        info.st_nlink, info.st_size, info.st_mtime_ns, info.st_ctime_ns]


def operation_lock(path):
    info = path.lstat()
    assert stat.S_ISREG(info.st_mode) and info.st_uid == 0 and info.st_nlink == 1 and stat.S_IMODE(info.st_mode) == 0o600 and info.st_size == 0
    return info.st_dev, info.st_ino


def record_for(row):
    operation = row['execution_job_id'].replace('-', '')
    assert re.fullmatch('[a-f0-9]{32}', operation)
    value = receipt_json(ROOT / (operation + '.json'))
    if value is None: raise FileNotFoundError(operation)
    managed = json.loads(row['plan_json']); plan = value['plan']; command = managed['Command']
    assert value['operationId'] == operation and value['owner'] == row['owner_hash']
    assert plan['command'] == {'containerId': row['container_id'], 'instanceId': row['instance_id'],
        'path': row['relative_path'], 'mode': command['Mode'], 'expectedVersion': command['ExpectedVersion']}
    assert plan['fingerprint'] == managed['Fingerprint'] == hashlib.sha256(b'whaledeck-container-permission-plan-v1\n' +
        json.dumps({k: v for k, v in plan.items() if k != 'fingerprint'}, sort_keys=True, ensure_ascii=False, separators=(',', ':'), allow_nan=False).encode()).hexdigest().upper()
    source = managed['SourceIdentity']
    assert plan['sourceIdentity'] == [source[k] for k in ('Device', 'Inode', 'Mode', 'OwnerUid', 'OwnerGid', 'LinkCount', 'SizeBytes', 'ModifiedAtUnixNs')] + [managed['SourceChangedAtUnixNs']]
    assert plan['kind'] == managed['Kind'] and plan['targetProof'] == managed['TargetProof']
    assert plan['attributesDigest'] == managed['AttributesDigest'] and plan['noChange'] == managed['NoChange']
    return value


def original_proof(record, source):
    plan = record['plan']
    if record['state'] not in ('Preparing', 'Prepared'): return None
    path = plan['command']['path']; leaf = path.removeprefix('work/')
    assert leaf and '/' not in leaf and path == 'work/' + leaf
    assert list(identity(source)) == plan['parentIdentity']
    target = source / leaf
    info = target.lstat()
    assert not target.is_symlink() and stat.S_ISREG(info.st_mode) and info.st_nlink == 1
    assert fingerprint(info) == plan['sourceIdentity'], 'PERMISSION_OBSERVER_SOURCE_CHANGED'
    assert record['modeConfirmed'] is None
    return {'originalIdentity': fingerprint(info), 'rootPhase': record['state'],
        'historyMayExist': record['historyMayExist'], 'historyVerified': record['historyVerified']}


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


class HeldPermission(HeldCopy):
    """Resume/close only; product cancellation is responsible for helper exit."""


def freeze_before_mode(row, source, container_pid, phase='Any'):
    assert os.geteuid() == 0 and re.fullmatch('[a-f0-9]{64}', row['container_id'])
    assert phase in ('Any', 'HistoryVerified')
    source = Path(source)
    assert source.parent == Path('/data/WhaleDeck/apps') and re.fullmatch('wdv-container-publish-[a-f0-9]{32}', source.name)
    assert source.resolve(strict=True) == source and not source.is_symlink()
    assert container_pid > 1 and row['instance_id'].startswith(str(container_pid) + ':')
    root_identity = identity(Path(f'/proc/{container_pid}/root'))
    operation = row['execution_job_id'].replace('-', '')
    assert re.fullmatch('[a-f0-9]{32}', operation)
    lock = ROOT / (operation + '.lock'); descriptor = pid = group = None
    deadline = time.monotonic() + 25
    try:
        while time.monotonic() < deadline:
            if descriptor is None and lock.exists():
                lock_identity = operation_lock(lock)
                for directory in CGROUP.glob('whaledeck-container-permissions-*.service'):
                    if not re.fullmatch(r'whaledeck-container-permissions-[a-f0-9]{32}\.service', directory.name): continue
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
            if descriptor is not None and record is not None and (phase == 'Any' or record['historyVerified']) and original_proof(record, source) is not None:
                if not bound_fds(pid, root_identity, operation_lock(lock)): return None
                try: signal.pidfd_send_signal(descriptor, signal.SIGSTOP)
                except ProcessLookupError: return None
                until = time.monotonic() + 2
                while True:
                    state = Path(f'/proc/{pid}/stat').read_text().rsplit(')', 1)[1].split()[0]
                    if state in ('T', 't'): break
                    if state == 'Z': return None
                    assert time.monotonic() < until, 'PERMISSION_STOP_NOT_OBSERVED'
                    time.sleep(0.0005)
                frozen = record_for(row); proof = original_proof(frozen, source)
                if proof is not None and (phase == 'Any' or frozen['historyVerified']):
                    held = HeldPermission(pid, descriptor, group, frozen, proof); descriptor = None; return held
                signal.pidfd_send_signal(descriptor, signal.SIGCONT); return None
            time.sleep(0.0005)
        return None
    finally:
        if descriptor is not None:
            try: signal.pidfd_send_signal(descriptor, signal.SIGCONT)
            except ProcessLookupError: pass
            finally: os.close(descriptor)
