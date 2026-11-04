"""QA-only first-entry ACL cut: exact owned bind, original plan and two FDs.

The installed aggregate must already attest its HDD manifest. Only SIGSTOP and
SIGCONT are sent; product cancellation/logout must terminate the bound helper.
A missed or changed boundary is failure, never authorization to replay a write.
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
from bound_container_acl_cut import identity, operation_lock
from validate_container_recursive_acl_agent import STATE
from validate_container_recursive_acl_browser import managed_plan
from verify_container_recursive_permissions import digest, unsigned

CGROUP = Path('/sys/fs/cgroup/system.slice')
TRANSPORT = b'/usr/local/libexec/whaledeck-container-recursive-acl-transport.py'


def child_id(operation, index):
    assert re.fullmatch('[a-f0-9]{32}', operation) and type(index) is int and 0 <= index < 4
    return hashlib.sha256(b'whaledeck-recursive-acl-entry-id-v1:' + operation.encode() + b':' + str(index).encode()).hexdigest()[:32]


def record_for(row):
    import uuid
    operation = uuid.UUID(row['execution_job_id']).hex
    value = receipt_json(STATE / (operation + '.json'))
    if value is None: raise FileNotFoundError(operation)
    plan = managed_plan(json.loads(row['plan_json']))
    assert value['operationId'] == operation and value['owner'] == row['owner_hash'] and value['plan'] == plan
    assert digest(b'whaledeck-container-recursive-acl-plan-v1\n', unsigned(plan, 'fingerprint')) == plan['fingerprint']
    assert plan['command']['containerId'] == row['container_id'] and plan['command']['instanceId'] == row['instance_id']
    assert plan['command']['path'] == row['relative_path'] == 'work/recursive-tree'
    return value


def first_entry_boundary(record):
    p = record['plan']
    assert p['command']['aclType'] == 'access' and p['entryCount'] == p['changeCount'] == 4
    assert p['fileCount'] == p['directoryCount'] == 2
    for key in ('startedThrough', 'finishedThrough'):
        assert type(record[key]) is int
    return (record['state'] == 'Applying' and record['historyVerified'] is True and record['historyMayExist'] is True
        and re.fullmatch('[A-F0-9]{64}', record['historyDigest']) is not None
        and record['finishedThrough'] == -1 and record['startedThrough'] in (-1, 0))


def bound_fds(pid, root_identity, lock_identity):
    try:
        args = Path(f'/proc/{pid}/cmdline').read_bytes().rstrip(b'\0').split(b'\0')
        if args != [b'/usr/bin/python3', b'-I', TRANSPORT, b'apply'] or Path(f'/proc/{pid}').stat().st_uid != 0: return False
        found = set()
        for fd in Path(f'/proc/{pid}/fd').iterdir():
            try: found.add(identity(fd))
            except (FileNotFoundError, PermissionError): pass
        return root_identity in found and lock_identity in found
    except (FileNotFoundError, ProcessLookupError, PermissionError): return False


def freeze_before_first_acl(row, source, container_pid, original, inventory):
    assert os.geteuid() == 0 and re.fullmatch('[a-f0-9]{64}', row['container_id'])
    source = Path(source)
    assert source.parent == Path('/data/WhaleDeck/apps') and re.fullmatch('wdv-container-publish-[a-f0-9]{32}', source.name)
    assert source.resolve(strict=True) == source and not source.is_symlink()
    assert type(container_pid) is int and container_pid > 1 and row['instance_id'].startswith(str(container_pid) + ':')
    assert list(identity(source)) == managed_plan(json.loads(row['plan_json']))['parentIdentity']
    root_identity = identity(Path(f'/proc/{container_pid}/root'))
    operation = row['execution_job_id'].replace('-', '')
    lock = STATE / (operation + '.lock'); descriptor = None; pid = group = None
    deadline = time.monotonic() + 25
    try:
        while time.monotonic() < deadline:
            if descriptor is None and lock.exists():
                lock_identity = operation_lock(lock)
                for directory in CGROUP.glob('whaledeck-container-recursive-acl-*.service'):
                    if not re.fullmatch(r'whaledeck-container-recursive-acl-[a-f0-9]{32}\.service', directory.name): continue
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
            if record and record['state'] in ('Completed', 'Partial', 'Unknown', 'Aborted'): return None
            if descriptor is not None and record and first_entry_boundary(record):
                if not bound_fds(pid, root_identity, operation_lock(lock)): return None
                try: signal.pidfd_send_signal(descriptor, signal.SIGSTOP)
                except ProcessLookupError: return None
                until = time.monotonic() + 2
                while True:
                    state = Path(f'/proc/{pid}/stat').read_text().rsplit(')', 1)[1].split()[0]
                    if state in ('T', 't'): break
                    if state == 'Z': return None
                    assert time.monotonic() < until, 'RECURSIVE_ACL_STOP_NOT_OBSERVED'
                    time.sleep(.0005)
                frozen = record_for(row)
                if first_entry_boundary(frozen) and inventory() == original:
                    child = receipt_json(STATE / (operation + '.entries') / (child_id(operation, 0) + '.json'))
                    if child is not None:
                        assert child['state'] in ('Preparing', 'Prepared') and child['aclConfirmed'] is child['actualIdentity'] is None
                        assert child['owner'] == row['owner_hash'] and child['operationId'] == child_id(operation, 0)
                    proof = dict(twoKernelFdProofs=True, originalFullTreeUnchanged=True, rootPhase='Applying',
                        historyVerified=True, startedThrough=frozen['startedThrough'], finishedThrough=-1,
                        childPhase=None if child is None else child['state'])
                    held = HeldCopy(pid, descriptor, group, frozen, proof); descriptor = None; return held
                return None
            time.sleep(.0005)
        return None
    finally:
        if descriptor is not None:
            try: signal.pidfd_send_signal(descriptor, signal.SIGCONT)
            except ProcessLookupError: pass
            finally: os.close(descriptor)
