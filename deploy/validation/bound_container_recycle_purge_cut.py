"""QA-only Root Reserved cut in the exact installed apply helper. No CLI/hook.

Only an owned fixture, original invocation and four current kernel FD identities
can authorize SIGSTOP. Applying/Deleting is already uncertain, not cancellation.
A stopped matched helper is terminated if proof fails; no Apply is replayed.
"""
import contextlib,json,os,re,signal,stat,subprocess,time,uuid,select
from pathlib import Path
from bound_container_copy_cut import HeldCopy
from bound_container_ownership_cut import identity,operation_lock
from bound_container_recycle_restore_cut import private_record
from container_recycle_purge_rpc_proof import fields,RECORD,digest
from container_recycle_rpc_proof import ROOT as SOURCE_RECORD
from recycle_purge_browser_proof import execution,managed_plan,stored_object,guid
from acl_history_browser_proof import typed_equal
import container_recycle_agent_io as source_io
from container_recycle_purge_agent_io import STATE

SOURCE_STATE=source_io.STATE
CGROUP=Path('/sys/fs/cgroup/system.slice')
PROC=Path('/proc')
TRANSPORT=b'/usr/local/libexec/whaledeck-container-recycle-purge-transport.py'
GROUP=re.compile(r'whaledeck-container-recycle-purges-[a-f0-9]{32}\.service')


def record_for(row):
    operation=guid(row['execution_job_id']);assert str(operation)==execution(row['id'])
    value=private_record(STATE/(operation.hex+'.json'))
    if value is None:raise FileNotFoundError(operation.hex)
    fields(value,RECORD);assert type(value['schemaVersion']) is int and value['schemaVersion']==1
    p=managed_plan(stored_object(row['plan_json']));typed_equal(value['plan'],p)
    assert value['operationId']==operation.hex and value['owner']==row['owner_hash']
    ref=p['command']['source'];assert ref['operationId']==uuid.UUID(execution(row['source_intent_id'],source=True)).hex
    c=ref['plan']['command'];assert (c['containerId'],c['instanceId'],c['path'])==(row['container_id'],row['instance_id'],row['relative_path'])
    assert row['relative_path']=='work/recycle-rpc.bin'
    return value


def before_delete_proof(record,folder):
    # Applying itself is a durable uncertain boundary, even before Deleting.
    if record['state']!='Reserved':return None
    assert record['pendingIdentity'] is None and record['unlinkConfirmed'] is False
    p=record['plan'];ref=p['command']['source'];op=ref['operationId']
    original=private_record(SOURCE_STATE/(op+'.json'));assert original is not None;fields(original,SOURCE_RECORD)
    for k in ('operationId','owner','plan'):typed_equal(original[k],ref[k])
    if original['state']!='Trashed':return None
    assert type(original['schemaVersion']) is int and original['schemaVersion']==1 and original['historyVerified'] is original['historyMayExist'] is True
    assert original['historyDigest']==p['command']['historyDigest'] and digest(b'whaledeck-container-recycle-purge-source-v1\n',original)==p['sourceRecordDigest']
    typed_equal(original['actualIdentity'],p['retainedIdentity']);typed_equal(original['privateIdentity'],p['privateIdentity'])
    marker=private_record(SOURCE_STATE/(op+'.purge.json'));assert marker is not None
    fields(marker,('schemaVersion','source','operationId','owner','fingerprint','state'))
    if marker['state']!='Reserved':return None
    assert type(marker['schemaVersion']) is int and marker['schemaVersion']==1
    typed_equal(marker['source'],ref);assert (marker['operationId'],marker['owner'],marker['fingerprint'])==(record['operationId'],record['owner'],p['fingerprint'])
    assert list(identity(folder))==p['parentIdentity']
    physical=source_io.physical(folder,op)
    if physical['source'] is not None or physical['retained'] is None:return None
    typed_equal(physical['retained']['identity'],p['retainedIdentity']);typed_equal(physical['privateIdentity'],p['privateIdentity'])
    for k in ('sha256','attributes'):typed_equal(physical['retained'][k],ref['plan'][k])
    assert physical['retained']['sizeBytes']==p['archiveBytes']
    return dict(rootPhase='Reserved',sourcePhase='Trashed',sourceMarkerPhase='Reserved',purgeOperationId=record['operationId'],sourceOperationId=op,
        originalNotQuarantined=True,unlinkConfirmed=False,retainedIdentity=p['retainedIdentity'],retainedSha256=physical['retained']['sha256'],privateIdentity=p['privateIdentity'])


@contextlib.contextmanager
def current_archive(p):
    io=source_io.installed();op=p['command']['source']['operationId']
    with io.storage.target(create_namespace=False,read_only=True) as (config,base):
        ns=io.files.open_at(base,source_io.NAMESPACE,os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW)
        try:
            i=os.fstat(ns);assert (i.st_uid,i.st_gid,stat.S_IMODE(i.st_mode))==(0,0,0o700)
            typed_equal([i.st_dev,i.st_ino],p['storeIdentity']);assert digest(b'',config)==p['storageHash'];io.storage.attest(config,ns,read_only=True)
            fd=io.files.open_at(ns,op+'.data',os.O_RDONLY|os.O_NOFOLLOW|os.O_NONBLOCK)
            try:
                snap=source_io.file_snapshot(Path('/proc/self/fd/'+str(ns))/(op+'.data'));assert snap is not None
                typed_equal(snap['identity'],p['archiveIdentity']);assert snap['sha256']==p['archiveSha256'] and snap['sizeBytes']==p['archiveBytes']
                assert identity(Path('/proc/self/fd/'+str(fd)))==tuple(p['archiveIdentity'][:2])
                yield tuple(p['archiveIdentity'][:2]),dict(currentHddFdAttested=True,hddIdentity=snap['identity'],hddSha256=snap['sha256'],storeIdentity=p['storeIdentity'],storageHash=p['storageHash'])
                io.storage.attest(config,ns,read_only=True)
                typed_equal(source_io.frame(os.fstat(fd)),p['archiveIdentity'])
            finally:os.close(fd)
        finally:os.close(ns)


def unit_identity(group):
    assert GROUP.fullmatch(group)
    v=dict(line.split('=',1) for line in subprocess.check_output(['systemctl','show',group,'-p','MainPID,InvocationID,ActiveState,SubState'],text=True,timeout=3).splitlines())
    assert v['MainPID'].isdecimal() and int(v['MainPID'])>1 and re.fullmatch('[a-f0-9]{32}',v['InvocationID']) and v['ActiveState']=='active' and v['SubState']=='running'
    return int(v['MainPID']),v['InvocationID']


def bound_process(pid,group,invocation,root,own_lock,source_lock,archive):
    if type(pid) is not int or pid<=1 or not GROUP.fullmatch(group) or not re.fullmatch('[a-f0-9]{32}',invocation) or len({root,own_lock,source_lock,archive})!=4:return False
    try:
        p=PROC/str(pid)
        if p.stat().st_uid!=0 or (p/'cmdline').read_bytes().rstrip(b'\0').split(b'\0')!=[b'/usr/bin/python3',b'-I',TRANSPORT,b'apply']:return False
        if b'0::/system.slice/'+group.encode() not in (p/'cgroup').read_bytes().splitlines() or b'INVOCATION_ID='+invocation.encode() not in (p/'environ').read_bytes().split(b'\0'):return False
        found=set()
        for fd in (p/'fd').iterdir():
            try:found.add(identity(fd))
            except (FileNotFoundError,PermissionError):pass
        return {root,own_lock,source_lock,archive}<=found
    except (FileNotFoundError,ProcessLookupError,PermissionError):return False


def settle_unheld(descriptor,stopped):
    # SIGSTOP was possible only after exact four-FD/argv/unit binding. Do not
    # resume a destructive Apply when its final proof/attestation fails.
    try:
        if stopped:
            try:signal.pidfd_send_signal(descriptor,signal.SIGKILL)
            except ProcessLookupError:pass
            assert select.select([descriptor],[],[],5)[0],'BOUND_UNHELD_PURGE_DID_NOT_EXIT'
    finally:os.close(descriptor)


def freeze_before_delete(row,folder,container_pid):
    assert os.geteuid()==0 and re.fullmatch('[a-f0-9]{64}',row['container_id']) and type(container_pid) is int and container_pid>1 and row['instance_id'].startswith(str(container_pid)+':')
    folder=Path(folder);assert folder.parent==Path('/data/WhaleDeck/apps') and re.fullmatch('wdv-container-publish-[a-f0-9]{32}',folder.name) and folder.resolve(strict=True)==folder and not folder.is_symlink()
    assert row['execution_job_id']==execution(row['id']);p=managed_plan(stored_object(row['plan_json']));assert list(identity(folder))==p['parentIdentity']
    root=identity(PROC/str(container_pid)/'root');operation=uuid.UUID(row['execution_job_id']).hex;source=p['command']['source']['operationId']
    own=STATE/(operation+'.lock');old=SOURCE_STATE/(source+'.lock');fd=None;stopped=False;pid=group=invocation=None;held=None;deadline=time.monotonic()+105
    try:
        with current_archive(p) as (archive,hdd):
            while time.monotonic()<deadline:
                if fd is None and own.exists() and old.exists():
                    locks=(operation_lock(own),operation_lock(old))
                    for candidate in (CGROUP/('whaledeck-container-recycle-purges-'+operation+'.service'),):
                        if not candidate.exists() or not GROUP.fullmatch(candidate.name):continue
                        try:current,nonce=unit_identity(candidate.name)
                        except (OSError,subprocess.SubprocessError,AssertionError):continue
                        if bound_process(current,candidate.name,nonce,root,*locks,archive):
                            try:fd=os.pidfd_open(current)
                            except ProcessLookupError:continue
                            pid,group,invocation=current,candidate.name,nonce;break
                try:r=record_for(row)
                except FileNotFoundError:r=None
                if r is not None and r['state'] not in ('Preparing','Reserved'):return None
                if fd is not None and r is not None and r['state']=='Reserved':
                    locks=(operation_lock(own),operation_lock(old))
                    if unit_identity(group)!=(pid,invocation) or not bound_process(pid,group,invocation,root,*locks,archive):return None
                    try:signal.pidfd_send_signal(fd,signal.SIGSTOP)
                    except ProcessLookupError:return None
                    stopped=True;until=time.monotonic()+2
                    while True:
                        state=(PROC/str(pid)/'stat').read_text().rsplit(')',1)[1].split()[0]
                        if state in ('T','t'):break
                        if state=='Z':return None
                        assert time.monotonic()<until,'PURGE_STOP_NOT_OBSERVED';time.sleep(.0005)
                    frozen=record_for(row);proof=before_delete_proof(frozen,folder)
                    if proof is None:return None
                    assert unit_identity(group)==(pid,invocation) and bound_process(pid,group,invocation,root,operation_lock(own),operation_lock(old),archive)
                    proof.update(hdd,fourKernelFdProofs=True,pid=pid,containerRootIdentity=list(root),purgeLockIdentity=list(operation_lock(own)),sourceLockIdentity=list(operation_lock(old)),archiveFdIdentity=list(archive),invocationId=invocation,cgroup=group)
                    held=HeldCopy(pid,fd,group,frozen,proof);break
                time.sleep(.0005)
        if held is not None:
            fd=None;return held
        return None
    finally:
        if fd is not None:settle_unheld(fd,stopped)
