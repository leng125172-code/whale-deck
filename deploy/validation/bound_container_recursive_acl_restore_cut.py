"""QA-only first-entry historical ACL restore cut: exact owned bind, original plan and two FDs.

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
import select
import stat
import time

from bound_container_copy_cut import HeldCopy, receipt_json
from bound_container_acl_cut import identity, operation_lock
from recursive_acl_restore_agent_io import RESTORE_STATE as STATE
from recursive_acl_restore_business_proof import managed_plan
from recursive_acl_restore_rpc_proof import child_id
import subprocess
from verify_container_recursive_permissions import digest, unsigned

CGROUP = Path('/sys/fs/cgroup/system.slice')
TRANSPORT = b'/usr/local/libexec/whaledeck-container-recursive-acl-restore-transport.py'
OBSERVER_WAIT_SECONDS = 120


def record_for(row):
    import uuid
    operation = uuid.UUID(row['execution_job_id']).hex
    value = receipt_json(STATE / (operation + '.json'))
    if value is None: raise FileNotFoundError(operation)
    plan = managed_plan(json.loads(row['plan_json']))
    assert value['operationId'] == operation and value['owner'] == row['owner_hash'] and value['plan'] == plan
    assert digest(b'whaledeck-container-recursive-acl-restore-plan-v1\n', unsigned(plan, 'fingerprint')) == plan['fingerprint']
    assert plan['command']['containerId'] == row['container_id'] and plan['command']['instanceId'] == row['instance_id']
    assert plan['command']['path'] == row['relative_path'] == 'work/recursive-tree'
    return value


def first_entry_boundary(record):
    p = record['plan']
    assert all(type(p[k]) is int for k in ('entryCount','changeCount','fileCount','directoryCount'))
    assert p['restoreSource']['changedAclType'] == 'access' and p['entryCount'] == p['changeCount'] == 26
    assert p['fileCount'] == 25 and p['directoryCount'] == 1
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


def process_start(pid):
    return int(Path(f'/proc/{pid}/stat').read_text().rsplit(')',1)[1].split()[19])


def pidfd_same(descriptor,pid,started):
    try:
        if select.select([descriptor],[],[],0)[0]:return False
        fields={line.split(':',1)[0]:line.split(':',1)[1].strip() for line in Path(f'/proc/self/fdinfo/{descriptor}').read_text().splitlines() if ':' in line}
        return fields.get('Pid')==str(pid) and process_start(pid)==started
    except (FileNotFoundError,ProcessLookupError,PermissionError):return False


def invocation_proof(pid,unit):
    assert type(pid) is int and pid>1 and re.fullmatch(r'whaledeck-container-recursive-acl-restore-[a-f0-9]{32}\.service',unit)
    cgroups=Path(f'/proc/{pid}/cgroup').read_text().splitlines()
    assert any(line.split(':',2)[-1]=='/system.slice/'+unit for line in cgroups)
    # Read only the single systemd identity; never export the process environment.
    values=[item.split(b'=',1)[1].decode('ascii') for item in Path(f'/proc/{pid}/environ').read_bytes().split(b'\0') if item.startswith(b'INVOCATION_ID=')]
    assert len(values)==1 and re.fullmatch('[a-f0-9]{32}',values[0])
    fields={}
    for line in subprocess.check_output(['systemctl','show',unit,'-p','InvocationID','-p','MainPID','-p','ControlGroup'],text=True,timeout=5).splitlines():
        key,value=line.split('=',1);fields[key]=value
    assert fields['InvocationID']==values[0] and fields['MainPID']==str(pid) and fields['ControlGroup']=='/system.slice/'+unit
    return dict(invocationId=values[0],mainPid=pid,exactCgroupAndInvocation=True)


def freeze_before_first_restore(row, source, container_pid, original, inventory,observe=lambda value:None,armed=None):
    return _freeze_restore(row,source,container_pid,original,inventory,observe,armed)

def freeze_after_first_restore(row,source,container_pid,original,inventory,source_capture,source_frame,attributes_digest,archive,observe=lambda value:None,armed=None):
    assert os.geteuid()==0
    from validate_container_upload_recovery_agent import trusted
    archive=Path(archive);trusted(archive.parent,True)
    assert not archive.exists(),'FIRST_COMPLETED_ARCHIVE_ALREADY_EXISTS'
    return _freeze_restore(row,source,container_pid,original,inventory,observe,armed,(source_capture,source_frame,attributes_digest,archive))

def completed_entry_boundary(record):
    # Keep the same owned 26-object access scope as the prewrite gate.
    p=record['plan'];assert all(type(p[k]) is int for k in ('entryCount','changeCount','fileCount','directoryCount'))
    assert p['restoreSource']['changedAclType']=='access' and p['entryCount']==p['changeCount']==26 and p['fileCount']==25 and p['directoryCount']==1
    assert type(record['startedThrough']) is type(record['finishedThrough']) is int
    return (record['state']=='Applying' and record['historyVerified'] is record['historyMayExist'] is True
        and re.fullmatch('[A-F0-9]{64}',record['historyDigest']) is not None and record['startedThrough']==record['finishedThrough']==0)

def _freeze_restore(row, source, container_pid, original, inventory,observe,armed,completed=None):
    assert os.geteuid() == 0 and re.fullmatch('[a-f0-9]{64}', row['container_id'])
    source = Path(source)
    assert source.parent == Path('/data/WhaleDeck/apps') and re.fullmatch('wdv-container-publish-[a-f0-9]{32}', source.name)
    assert source.resolve(strict=True) == source and not source.is_symlink()
    assert type(container_pid) is int and container_pid > 1 and row['instance_id'].startswith(str(container_pid) + ':')
    assert list(identity(source)) == managed_plan(json.loads(row['plan_json']))['parentIdentity']
    root_identity = identity(Path(f'/proc/{container_pid}/root'))
    operation = row['execution_job_id'].replace('-', '')
    lock = STATE / (operation + '.lock'); descriptor = None; pid = group = None; paused=False; started=None
    began = time.monotonic();deadline = began + OBSERVER_WAIT_SECONDS;record=None;observed_state=None
    observe(dict(stage='observer-armed',waitSeconds=OBSERVER_WAIT_SECONDS))
    if armed is not None:armed.set()
    try:
        while time.monotonic() < deadline:
            if descriptor is None and lock.exists():
                lock_identity = operation_lock(lock)
                for directory in CGROUP.glob('whaledeck-container-recursive-acl-restore-*.service'):
                    if not re.fullmatch(r'whaledeck-container-recursive-acl-restore-[a-f0-9]{32}\.service', directory.name): continue
                    try: pids = (directory / 'cgroup.procs').read_text().split()
                    except FileNotFoundError: continue
                    for candidate in pids:
                        if candidate.isdecimal() and bound_fds(int(candidate), root_identity, lock_identity):
                            pid = int(candidate)
                            try:
                                started=process_start(pid);descriptor=os.pidfd_open(pid)
                            except (ProcessLookupError,FileNotFoundError): continue
                            group = directory.name;observe(dict(stage='pidfd-bound',pid=pid,unit=group,started=started));break
                    if descriptor is not None: break
            try: record = record_for(row)
            except FileNotFoundError: record = None
            if record and record['state']!=observed_state:
                observed_state=record['state'];observe(dict(stage='state-observed',state=observed_state,elapsedMs=round((time.monotonic()-began)*1000)))
            if record and record['state'] in ('Completed', 'Partial', 'Unknown', 'Aborted'):
                observe(dict(stage='already-terminal',state=record['state'],startedThrough=record['startedThrough'],finishedThrough=record['finishedThrough']));return None
            boundary=first_entry_boundary if completed is None else completed_entry_boundary
            if descriptor is not None and record and boundary(record):
                if not pidfd_same(descriptor,pid,started) or not bound_fds(pid, root_identity, operation_lock(lock)): return None
                try: signal.pidfd_send_signal(descriptor, signal.SIGSTOP)
                except ProcessLookupError: return None
                paused=True;observe(dict(stage='sigstop-sent',pid=pid))
                until = time.monotonic() + 2
                while True:
                    state = Path(f'/proc/{pid}/stat').read_text().rsplit(')', 1)[1].split()[0]
                    if state in ('T', 't'): break
                    if state == 'Z': return None
                    assert time.monotonic() < until, 'RECURSIVE_ACL_STOP_NOT_OBSERVED'
                    time.sleep(.0005)
                assert pidfd_same(descriptor,pid,started) and bound_fds(pid, root_identity, operation_lock(lock)), 'STOPPED_RESTORE_FD_BINDING_CHANGED'
                service=invocation_proof(pid,group)
                frozen = record_for(row);observe(dict(stage='stopped-record',state=frozen['state'],startedThrough=frozen['startedThrough'],finishedThrough=frozen['finishedThrough']))
                if completed is not None and boundary(frozen):
                    from recursive_acl_restore_agent_io import installed
                    from recursive_acl_restore_partial_record_proof import read_applying_first,completed_first_facts
                    capture,frame,attrs,archive=completed
                    request=dict(operationId=operation,owner=row['owner_hash'],plan=frozen['plan'])
                    bundle=read_applying_first(installed(),request,archive,create=True)
                    assert bundle['root']==frozen,'FIRST_COMPLETED_ROOT_CHANGED_WHILE_STOPPED'
                    facts=completed_first_facts(request,bundle,capture,frame,original,inventory(),attrs)
                    proof=dict(**service,**facts,pidfdStartTimePinned=True,processStartTicks=started,twoKernelFdProofs=True,rootPhase='Applying',firstRestoreCompleted=True,startedThrough=0,finishedThrough=0)
                    held=HeldCopy(pid,descriptor,group,frozen,proof);descriptor=None;return held
                if completed is None and boundary(frozen) and inventory() == original:
                    child = receipt_json(STATE / (operation + '.entries') / (child_id(operation, 0) + '.json'))
                    if child is not None:
                        assert child['state'] in ('Preparing', 'Prepared', 'Applying') and child['aclConfirmed'] is child['actualIdentity'] is None and child['version']==''
                        assert child['owner'] == row['owner_hash'] and child['operationId'] == child_id(operation, 0)
                    proof = dict(**service,pidfdStartTimePinned=True,processStartTicks=started,twoKernelFdProofs=True, originalFullTreeUnchanged=True, rootPhase='Applying',
                        historyVerified=True, startedThrough=frozen['startedThrough'], finishedThrough=-1,
                        childPhase=None if child is None else child['state'])
                    held = HeldCopy(pid, descriptor, group, frozen, proof); descriptor = None; return held
                observe(dict(stage='missed-stopped-boundary'));return None
            time.sleep(.0005)
        observe(dict(stage='wait-expired',elapsedMs=round((time.monotonic()-began)*1000),state=record['state'] if record else None))
        return None
    finally:
        if descriptor is not None:
            try:
                if paused:signal.pidfd_send_signal(descriptor, signal.SIGCONT)
            except ProcessLookupError: pass
            finally: os.close(descriptor)
