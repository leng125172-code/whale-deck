"""QA-only exact pidfd/Root/lock binding for an installed single-object ACL change.

No installed hooks, arbitrary PID/command, Root edits or host path input. A cut
counts only with the exact original inode and independently observed ACL state.
"""
from functools import lru_cache
import base64
import hashlib
import json
import os
from pathlib import Path
import re
import signal
import stat
import time

from bound_container_copy_cut import HeldCopy, receipt_json

ROOT = Path('/var/lib/whaledeck-container-acl')
CGROUP = Path('/sys/fs/cgroup/system.slice')
TRANSPORT = b'/usr/local/libexec/whaledeck-container-acl-transport.py'


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
    from validate_container_acl_browser import managed_plan
    operation = row['execution_job_id'].replace('-', '')
    assert re.fullmatch('[a-f0-9]{32}', operation)
    value = receipt_json(ROOT / (operation + '.json'))
    if value is None: raise FileNotFoundError(operation)
    plan = managed_plan(json.loads(row['plan_json']))
    assert value['operationId'] == operation and value['owner'] == row['owner_hash']
    assert value['plan'] == plan
    command = plan['command']
    assert command['containerId'] == row['container_id'] and command['instanceId'] == row['instance_id'] and command['path'] == row['relative_path']
    assert plan['fingerprint'] == hashlib.sha256(b'whaledeck-container-acl-plan-v1\n' +
        json.dumps({k: v for k, v in plan.items() if k != 'fingerprint'}, sort_keys=True, ensure_ascii=False, separators=(',', ':'), allow_nan=False).encode()).hexdigest().upper()
    return value


@lru_cache(maxsize=1)
def acl_module():
    from validate_container_file_staging import load
    return load('/usr/local/libexec/whaledeck-container-acl.py', 'acl_fault_cut_metadata')


def original_proof(record, source):
    plan = record['plan']
    if record['state'] not in ('Preparing', 'Prepared'): return None
    path = plan['command']['path']; leaf = path.removeprefix('work/')
    assert leaf == 'acl-file.bin' and path == 'work/' + leaf
    assert list(identity(source)) == plan['parentIdentity']
    target = source / leaf; info = target.lstat()
    assert not target.is_symlink() and stat.S_ISREG(info.st_mode) and info.st_nlink == 1
    assert fingerprint(info) == plan['sourceIdentity'], 'ACL_OBSERVER_SOURCE_CHANGED'
    assert record['aclConfirmed'] is None and record['actualIdentity'] is None and record['version'] == ''
    assert plan['kind'] == 'File' and plan['command']['aclType'] == 'access' and plan['noChange'] is False
    module = acl_module()
    descriptor = os.open(target, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK | os.O_CLOEXEC)
    try:
        assert fingerprint(os.fstat(descriptor)) == plan['sourceIdentity']
        access = module.read_acl(descriptor, 'access', info.st_mode)
        default = module.read_acl(descriptor, 'default', info.st_mode)
        assert access == plan['accessAcl'] and default == plan['defaultAcl']
        names = os.listxattr(descriptor); assert len(names) <= 128
        values = []; size = 0
        for name in sorted(names):
            if name in module.XATTRS.values(): continue
            assert not name.startswith(('system.posix_acl_', 'security.'))
            value = os.getxattr(descriptor, name); size += len(name.encode()) + len(value); assert size <= 65536
            values.append([name, base64.b64encode(value).decode('ascii')])
        digest = hashlib.sha256(b'whaledeck-container-acl-attributes-v1\n' + module.encode(values)).hexdigest().upper()
        assert digest == plan['attributesDigest']
        assert fingerprint(os.fstat(descriptor)) == fingerprint(target.lstat()) == plan['sourceIdentity']
    finally: os.close(descriptor)
    return {'originalIdentity': fingerprint(info), 'accessAcl': access, 'defaultAcl': default, 'attributesDigest': digest,
        'rootPhase': record['state'], 'historyMayExist': record['historyMayExist'], 'historyVerified': record['historyVerified']}


def changed_proof(record, source):
    """A real access-ACL syscall occurred; its permanent receipt is still Applying."""
    if record['state']!='Applying':return None
    plan=record['plan'];old=plan['sourceIdentity'];command=plan['command']
    assert plan['kind']=='File' and command['aclType']=='access' and plan['noChange'] is False
    assert record['aclConfirmed'] is None and record['actualIdentity'] is None and record['version']==''
    assert record['historyMayExist'] and record['historyVerified'] and re.fullmatch('[A-F0-9]{64}',record['historyDigest'])
    assert command['path']=='work/acl-file.bin' and list(identity(source))==plan['parentIdentity']
    target=source/'acl-file.bin';named=target.lstat();assert stat.S_ISREG(named.st_mode) and not target.is_symlink() and named.st_nlink==1
    actual=fingerprint(named)
    if actual==old:return None
    desired=list(old);desired[2]=(old[2]&~0o777)|plan['targetMode']
    assert actual[:8]==desired[:8] and actual[8]>old[8],'ACL_OBSERVER_CHANGED_SOURCE_INVALID'
    module=acl_module();descriptor=os.open(target,os.O_RDONLY|os.O_NOFOLLOW|os.O_NONBLOCK|os.O_CLOEXEC)
    try:
        assert fingerprint(os.fstat(descriptor))==actual
        access=module.read_acl(descriptor,'access',named.st_mode);default=module.read_acl(descriptor,'default',named.st_mode)
        assert access==command['entries'] and access!=plan['accessAcl'] and default==plan['defaultAcl']
        names=os.listxattr(descriptor);assert len(names)<=128;values=[];size=0
        for name in sorted(names):
            if name in module.XATTRS.values():continue
            assert not name.startswith(('system.posix_acl_','security.'))
            value=os.getxattr(descriptor,name);size+=len(name.encode())+len(value);assert size<=65536;values.append([name,base64.b64encode(value).decode('ascii')])
        digest=hashlib.sha256(b'whaledeck-container-acl-attributes-v1\n'+module.encode(values)).hexdigest().upper();assert digest==plan['attributesDigest']
        assert fingerprint(os.fstat(descriptor))==fingerprint(target.lstat())==actual
    finally:os.close(descriptor)
    return {'originalIdentity':old,'changedIdentity':actual,'accessAcl':access,'defaultAcl':default,'attributesDigest':digest,
        'rootPhase':'Applying','historyMayExist':True,'historyVerified':True}


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


class HeldAcl(HeldCopy):
    """Resume/close only; product cancellation is responsible for helper exit."""


def freeze_before_acl(row, source, container_pid, phase='Any'):
    return _freeze_acl(row, source, container_pid, phase, original_proof)


def freeze_after_acl(row, source, container_pid, phase='HistoryVerified'):
    return _freeze_acl(row, source, container_pid, phase, changed_proof)


def _freeze_acl(row, source, container_pid, phase, prove):
    assert os.geteuid() == 0 and re.fullmatch('[a-f0-9]{64}', row['container_id'])
    assert phase in ('Any', 'HistoryVerified')
    source = Path(source)
    assert source.parent == Path('/data/WhaleDeck/apps') and re.fullmatch('wdv-container-publish-[a-f0-9]{32}', source.name)
    assert source.resolve(strict=True) == source and not source.is_symlink()
    assert container_pid > 1 and row['instance_id'].startswith(str(container_pid) + ':')
    acl_module()  # Load before observing the short syscall boundary.
    root_identity = identity(Path(f'/proc/{container_pid}/root'))
    operation = row['execution_job_id'].replace('-', '')
    assert re.fullmatch('[a-f0-9]{32}', operation)
    lock = ROOT / (operation + '.lock'); descriptor = pid = group = None
    deadline = time.monotonic() + 25
    try:
        while time.monotonic() < deadline:
            if descriptor is None and lock.exists():
                lock_identity = operation_lock(lock)
                for directory in CGROUP.glob('whaledeck-container-acl-*.service'):
                    if not re.fullmatch(r'whaledeck-container-acl-[a-f0-9]{32}\.service', directory.name): continue
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
            if descriptor is not None and record is not None and (phase == 'Any' or record['historyVerified']) and prove(record, source) is not None:
                if not bound_fds(pid, root_identity, operation_lock(lock)): return None
                try: signal.pidfd_send_signal(descriptor, signal.SIGSTOP)
                except ProcessLookupError: return None
                until = time.monotonic() + 2
                while True:
                    state = Path(f'/proc/{pid}/stat').read_text().rsplit(')', 1)[1].split()[0]
                    if state in ('T', 't'): break
                    if state == 'Z': return None
                    assert time.monotonic() < until, 'ACL_STOP_NOT_OBSERVED'
                    time.sleep(0.0005)
                frozen = record_for(row); proof = prove(frozen, source)
                if proof is not None and (phase == 'Any' or frozen['historyVerified']):
                    held = HeldAcl(pid, descriptor, group, frozen, proof); descriptor = None; return held
                signal.pidfd_send_signal(descriptor, signal.SIGCONT); return None
            time.sleep(0.0005)
        return None
    finally:
        if descriptor is not None:
            try: signal.pidfd_send_signal(descriptor, signal.SIGCONT)
            except ProcessLookupError: pass
            finally: os.close(descriptor)
