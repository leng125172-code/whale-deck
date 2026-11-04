"""QA candidate: first ACL child Completed, three other entries still original.

No CLI, installed hook or arbitrary signal target. This observer alone is not a
Partial/recovery acceptance gate; a future parent must prove SQL/Root/HDD/UI.
"""
import base64
import json
import os
from pathlib import Path
import re
import signal
import time

from bound_container_copy_cut import HeldCopy, receipt_json
from bound_container_recursive_acl_cut import STATE, CGROUP, identity, operation_lock, child_id, record_for, bound_fds, managed_plan
from validate_container_recursive_acl import PATHS
from verify_container_recursive_acl import acl_mode, file_version
from verify_container_recursive_permissions import digest, unsigned


def partial_boundary(record):
    plan=record['plan'];assert plan['command']['aclType']=='access' and plan['entryCount']==plan['changeCount']==4 and plan['fileCount']==plan['directoryCount']==2
    assert all(type(plan[k]) is int for k in ('entryCount','changeCount','fileCount','directoryCount'))
    assert type(record['startedThrough']) is type(record['finishedThrough']) is int
    return (record['state']=='Applying' and record['historyVerified'] is True and record['historyMayExist'] is True
        and re.fullmatch('[A-F0-9]{64}',record['historyDigest']) is not None
        and record['finishedThrough']==0 and record['startedThrough'] in (0,1))


def partial_proof(record,original,current):
    if not partial_boundary(record):return None
    assert original.keys()==current.keys()==PATHS.keys()
    plan=record['plan'];command=plan['command'];operation=record['operationId'];first_id=child_id(operation,0)
    first=receipt_json(STATE/(operation+'.entries')/(first_id+'.json'))
    if first is None or first['state']!='Completed':return None
    assert first['schemaVersion']==1 and first['operationId']==first_id and first['owner']==record['owner']
    assert first['aclConfirmed'] is first['historyVerified'] is first['historyMayExist'] is True
    assert re.fullmatch('[A-F0-9]{64}',first['historyDigest'])
    cp=first['plan'];old=original['sub/中文.bin'];actual=current['sub/中文.bin'];path='work/recursive-tree/sub/中文.bin'
    assert cp['kind']=='File' and cp['noChange'] is False and cp['sourceIdentity']==old['identity'] and cp['accessAcl']==old['access'] and cp['defaultAcl']==old['default']
    assert cp['command']==dict(containerId=command['containerId'],instanceId=command['instanceId'],path=path,
        expectedVersion=file_version(command,path,old['identity']),aclType='access',entries=command['fileEntries'])
    assert cp['targetMode']==acl_mode(command['fileEntries']) and cp['attributesDigest']==digest(b'whaledeck-container-acl-attributes-v1\n',
        [['user.whaledeck-recursive',base64.b64encode(b'preserve ordinary attribute').decode('ascii')]])
    assert cp['fingerprint']==digest(b'whaledeck-container-acl-plan-v1\n',unsigned(cp,'fingerprint'))
    wanted=list(old['identity']);wanted[2]=wanted[2]&~0o777|cp['targetMode']
    assert actual['identity'][:8]==wanted[:8] and actual['identity'][8]>old['identity'][8]
    assert first['actualIdentity']==actual['identity'] and actual['access']==command['fileEntries'] and actual['access']!=old['access'] and actual['default']==old['default']
    assert first['version']==file_version(command,path,actual['identity'])
    for relative in PATHS:
        if relative!='sub/中文.bin':assert current[relative]==original[relative],'RECURSIVE_ACL_OTHER_ENTRY_ALREADY_CHANGED'
    assert current['']['identity']==plan['rootIdentity']
    return dict(firstCompletedId=first_id,firstRelativePath='sub/中文.bin',firstChangedIdentity=actual['identity'],rootStillOriginal=original['']['identity'],
        rootPhase='Applying',startedThrough=record['startedThrough'],finishedThrough=0,completedChildHistoryDigest=first['historyDigest'],otherThreeEntriesOriginal=True)


def freeze_partial_acl(row,source,container_pid,original,inventory):
    assert os.geteuid()==0 and re.fullmatch('[a-f0-9]{64}',row['container_id'])
    source=Path(source)
    assert source.parent==Path('/data/WhaleDeck/apps') and re.fullmatch('wdv-container-publish-[a-f0-9]{32}',source.name)
    assert source.resolve(strict=True)==source and not source.is_symlink()
    assert type(container_pid) is int and container_pid>1 and row['instance_id'].startswith(str(container_pid)+':')
    assert list(identity(source))==managed_plan(json.loads(row['plan_json']))['parentIdentity']
    root_identity=identity(Path(f'/proc/{container_pid}/root'));operation=row['execution_job_id'].replace('-','')
    lock=STATE/(operation+'.lock');descriptor=None;pid=group=None;deadline=time.monotonic()+25
    try:
        while time.monotonic()<deadline:
            if descriptor is None and lock.exists():
                lock_identity=operation_lock(lock)
                for directory in CGROUP.glob('whaledeck-container-recursive-acl-*.service'):
                    if not re.fullmatch(r'whaledeck-container-recursive-acl-[a-f0-9]{32}\.service',directory.name):continue
                    try:pids=(directory/'cgroup.procs').read_text().split()
                    except FileNotFoundError:continue
                    for candidate in pids:
                        if candidate.isdecimal() and bound_fds(int(candidate),root_identity,lock_identity):
                            pid=int(candidate)
                            try:descriptor=os.pidfd_open(pid)
                            except ProcessLookupError:continue
                            group=directory.name;break
                    if descriptor is not None:break
            try:record=record_for(row)
            except FileNotFoundError:record=None
            if record and record['state'] in ('Completed','Partial','Unknown','Aborted'):return None
            if descriptor is not None and record and partial_boundary(record):
                if not bound_fds(pid,root_identity,operation_lock(lock)):return None
                try:signal.pidfd_send_signal(descriptor,signal.SIGSTOP)
                except ProcessLookupError:return None
                until=time.monotonic()+2
                while True:
                    state=Path(f'/proc/{pid}/stat').read_text().rsplit(')',1)[1].split()[0]
                    if state in ('T','t'):break
                    if state=='Z':return None
                    assert time.monotonic()<until,'RECURSIVE_ACL_PARTIAL_STOP_NOT_OBSERVED'
                    time.sleep(.0005)
                frozen=record_for(row);proof=partial_proof(frozen,original,inventory())
                if proof is not None:
                    held=HeldCopy(pid,descriptor,group,frozen,{**proof,'twoKernelFdProofs':True});descriptor=None;return held
                return None
            time.sleep(.0005)
        return None
    finally:
        if descriptor is not None:
            try:signal.pidfd_send_signal(descriptor,signal.SIGCONT)
            except ProcessLookupError:pass
            finally:os.close(descriptor)
