"""QA-only restore-before-return pidfd cut, with a fixed installed scope.

No CLI, product hook, configurable transport or arbitrary PID signal endpoint.
A missed cut never authorizes replay. Current HDD bytes require a separate
parent proof; this observer proves the claimed inode has not been returned.
"""
import os,re,signal,stat,subprocess,time,uuid
from pathlib import Path
from bound_container_copy_cut import HeldCopy
from bound_container_ownership_cut import identity,operation_lock
from bound_container_recycle_cut import release_unheld
from container_recycle_restore_rpc_proof import unique_json,fields,claim_proof,RECORD
from container_recycle_rpc_proof import ROOT as SOURCE_RECORD
from container_recycle_agent_io import STATE as SOURCE_STATE,physical
from container_recycle_restore_agent_io import STATE
from recycle_restore_browser_proof import execution,managed_plan,stored_object,guid
from acl_history_browser_proof import typed_equal

CGROUP=Path('/sys/fs/cgroup/system.slice')
PROC=Path('/proc')
TRANSPORT=b'/usr/local/libexec/whaledeck-container-recycle-restore-transport.py'
GROUP=re.compile(r'whaledeck-container-recycle-restores-[a-f0-9]{32}\.service')


def private_record(path):
    """Read one still-linked immutable inode, accepting the full 64KiB format."""
    try:fd=os.open(path,os.O_RDONLY|os.O_NOFOLLOW|os.O_NONBLOCK|os.O_CLOEXEC)
    except FileNotFoundError:return None
    try:
        i=os.fstat(fd)
        if i.st_nlink==0:return None
        assert stat.S_ISREG(i.st_mode) and (i.st_uid,i.st_gid,stat.S_IMODE(i.st_mode),i.st_nlink)==(0,0,0o600,1) and 0<i.st_size<=65536
        raw=os.read(fd,65537);assert len(raw)==i.st_size
        try:now=path.lstat()
        except FileNotFoundError:return None
        if not os.path.samestat(i,now):return None
        return unique_json(raw.decode('utf-8'))
    finally:os.close(fd)


def record_for(row):
    operation=guid(row['execution_job_id']);assert str(operation)==execution(row['id'])
    record=private_record(STATE/(operation.hex+'.json'))
    if record is None:raise FileNotFoundError(operation.hex)
    fields(record,RECORD);assert type(record['schemaVersion']) is int and record['schemaVersion']==1
    assert record['state'] in ('Preparing','Applying','Completed','Aborted','Unknown')
    assert type(record['claimed']) is type(record['historyVerified']) is bool
    plan=managed_plan(stored_object(row['plan_json']))
    typed_equal(record['plan'],plan);assert record['operationId']==operation.hex and record['owner']==row['owner_hash']
    source=plan['command']['source'];assert source['operationId']==uuid.UUID(execution(row['source_intent_id'],source=True)).hex
    command=source['plan']['command']
    assert (command['containerId'],command['instanceId'],command['path'])==(row['container_id'],row['instance_id'],row['relative_path'])
    assert row['relative_path']=='work/recycle-rpc.bin'
    return record


def before_return_proof(record,folder):
    if record['state']!='Applying':return None
    assert record['claimed'] is record['historyVerified'] is True and record['sourceOutcome'] is None
    plan=record['plan'];source=plan['command']['source'];assert record['historyDigest']==plan['command']['historyDigest']
    request={k:record[k] for k in ('operationId','owner','plan')}
    claim=private_record(STATE/'claims'/(source['operationId']+'.json'))
    assert claim_proof(claim,request),'RESTORE_CUT_SOURCE_CLAIM_MISMATCH'
    original=private_record(SOURCE_STATE/(source['operationId']+'.json'))
    assert original is not None;fields(original,SOURCE_RECORD)
    for key in ('operationId','owner','plan'):typed_equal(original[key],source[key])
    if original['state']!='Trashed':return None
    assert original['historyMayExist'] is original['historyVerified'] is True and original['historyDigest']==record['historyDigest']
    typed_equal(original['privateIdentity'],plan['privateIdentity']);typed_equal(original['actualIdentity'],plan['retainedIdentity'])
    assert list(identity(folder))==plan['parentIdentity']
    value=physical(folder,source['operationId'])
    if value['source'] is not None or value['retained'] is None:return None
    retained=value['retained'];typed_equal(retained['identity'],plan['retainedIdentity'])
    typed_equal(value['privateIdentity'],plan['privateIdentity'])
    for key in ('sha256','attributes'):typed_equal(retained[key],source['plan'][key])
    assert retained['sizeBytes']==source['plan']['sourceIdentity'][6]
    return dict(rootPhase='Applying',sourcePhase='Trashed',sourceClaimMatches=True,sourceNotReturned=True,
        sourceOperationId=source['operationId'],restoreOperationId=record['operationId'],historyDigest=record['historyDigest'],
        retainedIdentity=retained['identity'],retainedSha256=retained['sha256'],privateIdentity=value['privateIdentity'],hddDataReread=False)


def unit_identity(group):
    assert GROUP.fullmatch(group)
    values=dict(line.split('=',1) for line in subprocess.check_output(['systemctl','show',group,'-p','MainPID,InvocationID,ActiveState,SubState'],text=True,timeout=3).splitlines())
    assert values['MainPID'].isdecimal() and int(values['MainPID'])>1 and re.fullmatch('[a-f0-9]{32}',values['InvocationID']) and values['ActiveState']=='active' and values['SubState']=='running'
    return int(values['MainPID']),values['InvocationID']


def bound_process(pid,group,invocation,root_identity,restore_lock_identity,source_lock_identity):
    if type(pid) is not int or pid<=1 or not GROUP.fullmatch(group) or not re.fullmatch('[a-f0-9]{32}',invocation):return False
    if len({root_identity,restore_lock_identity,source_lock_identity})!=3:return False
    try:
        proc=PROC/str(pid)
        if proc.stat().st_uid!=0 or (proc/'cmdline').read_bytes().rstrip(b'\0').split(b'\0')!=[b'/usr/bin/python3',b'-I',TRANSPORT,b'apply']:return False
        if b'0::/system.slice/'+group.encode() not in (proc/'cgroup').read_bytes().splitlines():return False
        if b'INVOCATION_ID='+invocation.encode() not in (proc/'environ').read_bytes().split(b'\0'):return False
        found=set()
        for fd in (proc/'fd').iterdir():
            try:found.add(identity(fd))
            except (FileNotFoundError,PermissionError):pass
        return {root_identity,restore_lock_identity,source_lock_identity}<=found
    except (FileNotFoundError,ProcessLookupError,PermissionError):return False


def freeze_before_return(row,folder,container_pid):
    assert os.geteuid()==0 and re.fullmatch('[a-f0-9]{64}',row['container_id']) and type(container_pid) is int and container_pid>1 and row['instance_id'].startswith(str(container_pid)+':')
    folder=Path(folder);assert folder.parent==Path('/data/WhaleDeck/apps') and re.fullmatch('wdv-container-publish-[a-f0-9]{32}',folder.name) and folder.resolve(strict=True)==folder and not folder.is_symlink()
    assert row['execution_job_id']==execution(row['id'])
    plan=managed_plan(stored_object(row['plan_json']));assert list(identity(folder))==plan['parentIdentity']
    root_identity=identity(PROC/str(container_pid)/'root');operation=uuid.UUID(row['execution_job_id']).hex
    source_op=plan['command']['source']['operationId'];lock=STATE/(operation+'.lock');source_lock=SOURCE_STATE/(source_op+'.lock')
    fd=None;stopped=False;pid=group=invocation=None;deadline=time.monotonic()+105
    try:
        while time.monotonic()<deadline:
            if fd is None and lock.exists() and source_lock.exists():
                restore_id=operation_lock(lock);source_id=operation_lock(source_lock)
                for candidate in CGROUP.glob('whaledeck-container-recycle-restores-*.service'):
                    if not GROUP.fullmatch(candidate.name):continue
                    try:current,nonce=unit_identity(candidate.name)
                    except (OSError,subprocess.SubprocessError,AssertionError):continue
                    if bound_process(current,candidate.name,nonce,root_identity,restore_id,source_id):
                        try:fd=os.pidfd_open(current)
                        except ProcessLookupError:continue
                        pid,group,invocation=current,candidate.name,nonce;break
            try:record=record_for(row)
            except FileNotFoundError:record=None
            if record is not None and record['state'] in ('Completed','Aborted','Unknown'):return None
            if fd is not None and record is not None and record['state']=='Applying':
                locks=(operation_lock(lock),operation_lock(source_lock))
                if unit_identity(group)!=(pid,invocation) or not bound_process(pid,group,invocation,root_identity,*locks):return None
                try:signal.pidfd_send_signal(fd,signal.SIGSTOP)
                except ProcessLookupError:return None
                stopped=True;until=time.monotonic()+2
                while True:
                    state=(PROC/str(pid)/'stat').read_text().rsplit(')',1)[1].split()[0]
                    if state in ('T','t'):break
                    if state=='Z':return None
                    assert time.monotonic()<until,'RESTORE_STOP_NOT_OBSERVED';time.sleep(.0005)
                frozen=record_for(row);proof=before_return_proof(frozen,folder)
                if proof is None:return None
                assert unit_identity(group)==(pid,invocation) and bound_process(pid,group,invocation,root_identity,operation_lock(lock),operation_lock(source_lock))
                proof.update(threeKernelFdProofs=True,pid=pid,containerRootIdentity=list(root_identity),restoreLockIdentity=list(operation_lock(lock)),sourceLockIdentity=list(operation_lock(source_lock)),invocationId=invocation,cgroup=group)
                held=HeldCopy(pid,fd,group,frozen,proof);fd=None;return held
            time.sleep(.0005)
        return None
    finally:
        if fd is not None:release_unheld(fd,stopped)
