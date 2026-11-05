"""QA-only pre-removal pidfd cut; never a product endpoint or arbitrary PID tool.

Only an exact installed apply helper in its original systemd Invocation, holding
both the owned container root FD and original operation lock FD, may be paused.
An unmatched or missed boundary is failure; it never authorizes another Apply.
"""
import json,os,re,signal,stat,subprocess,time,uuid
from pathlib import Path
from bound_container_copy_cut import HeldCopy,receipt_json
from bound_container_ownership_cut import identity,operation_lock
from container_recycle_agent_io import STATE,PRIVATE,file_snapshot
from validate_container_recycle_browser import native_plan

CGROUP=Path('/sys/fs/cgroup/system.slice')
PROC=Path('/proc')
TRANSPORT=b'/usr/local/libexec/whaledeck-container-recycle-transport.py'
GROUP=re.compile(r'whaledeck-container-recycling-[a-f0-9]{32}\.service')


def record_for(row):
    assert str(uuid.UUID(row['execution_job_id']))==row['execution_job_id']
    op=uuid.UUID(row['execution_job_id']).hex;record=receipt_json(STATE/(op+'.json'))
    if record is None:raise FileNotFoundError(op)
    plan=native_plan(json.loads(row['plan_json']))
    assert record['operationId']==op and record['owner']==row['owner_hash'] and record['plan']==plan
    assert plan['command']['containerId']==row['container_id'] and plan['command']['instanceId']==row['instance_id'] and plan['command']['path']==row['relative_path']=='work/recycle-rpc.bin'
    return record


def pre_removal_proof(record,source):
    if record['state'] not in ('Preparing','Retaining') or record['historyVerified'] is not True:return None
    p=record['plan'];assert record['historyMayExist'] is True and re.fullmatch('[A-F0-9]{64}',record['historyDigest']) and record['actualIdentity'] is None
    assert list(identity(source))==p['parentIdentity'] and p['command']['path']=='work/recycle-rpc.bin'
    original=file_snapshot(source/'recycle-rpc.bin')
    if original is None:return None
    assert original['identity']==p['sourceIdentity'] and original['sha256']==p['sha256'] and original['attributes']==p['attributes'],'RECYCLE_CUT_SOURCE_CHANGED'
    private=source/PRIVATE
    if record['privateIdentity'] is None:
        # A mkdir not yet durably bound is not a countable cut.
        if os.path.lexists(private):return None
    else:
        i=private.lstat();assert stat.S_ISDIR(i.st_mode) and (i.st_uid,i.st_gid,stat.S_IMODE(i.st_mode))==(0,0,0o700) and [i.st_dev,i.st_ino]==record['privateIdentity'] and not list(private.iterdir())
    return dict(rootPhase=record['state'],historyVerified=True,historyDigest=record['historyDigest'],sourceIdentity=original['identity'],sourceSha256=original['sha256'],privateIdentity=record['privateIdentity'],sourceNotRemoved=True)


def unit_identity(group):
    assert GROUP.fullmatch(group)
    values=dict(line.split('=',1) for line in subprocess.check_output(['systemctl','show',group,'-p','MainPID,InvocationID,ActiveState,SubState'],text=True,timeout=3).splitlines())
    assert values['MainPID'].isdecimal() and int(values['MainPID'])>1 and re.fullmatch('[a-f0-9]{32}',values['InvocationID']) and values['ActiveState']=='active' and values['SubState']=='running'
    return int(values['MainPID']),values['InvocationID']


def bound_process(pid,group,invocation,root_identity,lock_identity):
    if type(pid) is not int or pid<=1 or not GROUP.fullmatch(group) or not re.fullmatch('[a-f0-9]{32}',invocation):return False
    try:
        proc=PROC/str(pid)
        if proc.stat().st_uid!=0 or (proc/'cmdline').read_bytes().rstrip(b'\0').split(b'\0')!=[b'/usr/bin/python3',b'-I',TRANSPORT,b'apply']:return False
        if b'0::/system.slice/'+group.encode() not in (proc/'cgroup').read_bytes().splitlines():return False
        if b'INVOCATION_ID='+invocation.encode() not in (proc/'environ').read_bytes().split(b'\0'):return False
        found=set()
        for fd in (proc/'fd').iterdir():
            try:found.add(identity(fd))
            except (FileNotFoundError,PermissionError):pass
        return root_identity in found and lock_identity in found
    except (FileNotFoundError,ProcessLookupError,PermissionError):return False


def release_unheld(descriptor,stopped):
    try:
        if stopped:signal.pidfd_send_signal(descriptor,signal.SIGCONT)
    except ProcessLookupError:pass
    finally:os.close(descriptor)


def freeze_before_removal(row,source,container_pid):
    assert os.geteuid()==0 and re.fullmatch('[a-f0-9]{64}',row['container_id']) and type(container_pid) is int and container_pid>1 and row['instance_id'].startswith(str(container_pid)+':')
    source=Path(source);assert source.parent==Path('/data/WhaleDeck/apps') and re.fullmatch('wdv-container-publish-[a-f0-9]{32}',source.name) and source.resolve(strict=True)==source and not source.is_symlink()
    plan=native_plan(json.loads(row['plan_json']));assert list(identity(source))==plan['parentIdentity'];root_identity=identity(Path('/proc')/str(container_pid)/'root');lock=STATE/(uuid.UUID(row['execution_job_id']).hex+'.lock');fd=None;pid=group=invocation=None
    deadline=time.monotonic()+25;stopped=False
    try:
        while time.monotonic()<deadline:
            if fd is None and lock.exists():
                lock_identity=operation_lock(lock)
                for candidate in CGROUP.glob('whaledeck-container-recycling-*.service'):
                    if not GROUP.fullmatch(candidate.name):continue
                    try:current,nonce=unit_identity(candidate.name)
                    except (OSError,subprocess.SubprocessError,AssertionError):continue
                    if bound_process(current,candidate.name,nonce,root_identity,lock_identity):
                        try:fd=os.pidfd_open(current)
                        except ProcessLookupError:continue
                        pid,group,invocation=current,candidate.name,nonce;break
            try:record=record_for(row)
            except FileNotFoundError:record=None
            if record is not None and record['state'] in ('Trashed','Restored','Aborted','Unknown'):return None
            if fd is not None and record is not None and record['historyVerified'] is True:
                if unit_identity(group)!=(pid,invocation) or not bound_process(pid,group,invocation,root_identity,operation_lock(lock)):return None
                signal.pidfd_send_signal(fd,signal.SIGSTOP);stopped=True;until=time.monotonic()+2
                while True:
                    state=Path(f'/proc/{pid}/stat').read_text().rsplit(')',1)[1].split()[0]
                    if state in ('T','t'):break
                    if state=='Z':return None
                    assert time.monotonic()<until,'RECYCLE_STOP_NOT_OBSERVED';time.sleep(.0005)
                frozen=record_for(row);proof=pre_removal_proof(frozen,source)
                if proof is None:return None
                assert unit_identity(group)==(pid,invocation) and bound_process(pid,group,invocation,root_identity,operation_lock(lock))
                proof.update(twoKernelFdProofs=True,invocationId=invocation,cgroup=group)
                held=HeldCopy(pid,fd,group,frozen,proof);fd=None;return held
            time.sleep(.0005)
        return None
    finally:
        if fd is not None:
            release_unheld(fd,stopped)
