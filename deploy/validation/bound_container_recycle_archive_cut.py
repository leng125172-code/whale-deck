"""QA-only Root Reserved cut in the exact installed apply helper. No CLI/hook.

Only an owned fixture, original invocation and five current kernel FD identities
can authorize SIGSTOP. Applying/Copying is already uncertain, not cancellation.
A stopped matched helper is terminated if proof fails; no Apply is replayed.
"""
import contextlib,json,os,re,signal,stat,subprocess,time,uuid,select
from pathlib import Path
from bound_container_copy_cut import HeldCopy
from bound_container_ownership_cut import identity,operation_lock
from bound_container_recycle_restore_cut import private_record
from container_recycle_archive_rpc_proof import fields,RECORD,digest
from container_recycle_purge_rpc_proof import RECORD as PURGE_RECORD
from container_recycle_rpc_proof import ROOT as SOURCE_RECORD
from recycle_archive_browser_proof import execution,managed_plan,stored_object,guid
from acl_history_browser_proof import typed_equal
import container_recycle_agent_io as source_io
from container_recycle_archive_agent_io import STATE,CLAIMS,physical
from container_recycle_purge_agent_io import STATE as PURGE_STATE
from bound_container_recycle_purge_cut import current_archive as purge_current_archive
from recursive_acl_restore_agent_io import read_at

SOURCE_STATE=source_io.STATE
CGROUP=Path('/sys/fs/cgroup/system.slice')
PROC=Path('/proc')
TRANSPORT=b'/usr/local/libexec/whaledeck-container-recycle-archive-transport.py'
GROUP=re.compile(r'whaledeck-container-recycle-archives-[a-f0-9]{32}\.service')


class HeldArchive(HeldCopy):
    def release(self):
        # Cleanup may run after a failed UI action. Never resume that Apply.
        if self.descriptor is not None:
            descriptor=self.descriptor;self.descriptor=None
            settle_unheld(descriptor,True)


def record_for(row):
    operation=guid(row['execution_job_id']);assert str(operation)==execution(row['id'])
    value=private_record(STATE/(operation.hex+'.json'))
    if value is None:raise FileNotFoundError(operation.hex)
    fields(value,RECORD);assert type(value['schemaVersion']) is int and value['schemaVersion']==1
    p=managed_plan(stored_object(row['plan_json']));typed_equal(value['plan'],p)
    assert value['operationId']==operation.hex and value['owner']==row['owner_hash']
    deleted=p['command']['purge'];assert deleted['operationId']==uuid.UUID(execution(row['source_intent_id'],'purge')).hex
    ref=deleted['plan']['command']['source']
    c=ref['plan']['command'];assert (c['containerId'],c['instanceId'],c['path'])==(row['container_id'],row['instance_id'],row['relative_path'])
    assert row['relative_path']=='work/recycle-rpc.bin'
    return value


def before_copy_proof(record,folder):
    # Applying and every later phase are already uncertain, including before mkdir.
    if record['state']!='Reserved':return None
    assert all(record[k] is None for k in ('privateIdentity','pendingIdentity','preparedIdentity','publishedIdentity')) and record['parentAfterStage']==''
    p=record['plan'];purge=p['command']['purge'];ref=purge['plan']['command']['source'];source_op=ref['operationId']
    original=private_record(SOURCE_STATE/(source_op+'.json'));deleted=private_record(PURGE_STATE/(purge['operationId']+'.json'));marker=private_record(SOURCE_STATE/(source_op+'.purge.json'))
    if original is None or deleted is None or marker is None:return None
    fields(original,SOURCE_RECORD);fields(deleted,PURGE_RECORD);fields(marker,('schemaVersion','source','operationId','owner','fingerprint','state'))
    assert all(type(v['schemaVersion']) is int and v['schemaVersion']==1 for v in (original,deleted,marker))
    assert original['state']=='Purged' and original['actualIdentity'] is None and original['historyVerified'] is original['historyMayExist'] is True
    assert original['historyDigest']==p['command']['historyDigest']
    assert deleted['state']=='Completed' and deleted['unlinkConfirmed'] is True and marker['state']=='Purged'
    for key in ('operationId','owner','plan'):typed_equal(original[key],ref[key]);typed_equal(deleted[key],purge[key])
    typed_equal(marker['source'],ref);assert (marker['operationId'],marker['owner'],marker['fingerprint'])==(purge['operationId'],purge['owner'],purge['plan']['fingerprint'])
    assert digest(b'archive-source-v1\n',original)==p['sourceRecordDigest'] and digest(b'archive-purge-v1\n',deleted)==p['purgeRecordDigest'] and digest(b'archive-marker-v1\n',marker)==p['purgeMarkerDigest']
    claim=private_record(CLAIMS/(source_op+'.json'));typed_equal(claim,dict(schemaVersion=1,source=ref,restoreOperationId=record['operationId'],restoreOwner=record['owner'],restoreFingerprint=p['fingerprint']))
    assert list(identity(folder))==p['parentIdentity']
    actual=physical(folder,{k:record[k] for k in ('operationId','owner','plan')});typed_equal(actual['oldPrivateIdentity'],p['privateIdentity'])
    assert all(actual[k] is None for k in ('source','retained','pending','stageIdentity','staged'))
    return dict(rootPhase='Reserved',sourcePhase='Purged',purgePhase='Completed',sourceMarkerPhase='Purged',archiveOperationId=record['operationId'],purgeOperationId=purge['operationId'],sourceOperationId=source_op,
        sourceClaimMatches=True,targetAbsent=True,stageAbsent=True,copyingNotEntered=True,hddDataReread=False,privateIdentity=p['privateIdentity'])


@contextlib.contextmanager
def current_archive(plan):
    # Both observers are read-only. Verify metadata on this attested namespace
    # before stopping and again before handing a stopped helper to the UI gate.
    purge=plan['command']['purge']['plan'];op=purge['command']['source']['operationId']
    with purge_current_archive(purge) as (archive,proof):
        io=source_io.installed()
        with io.storage.target(create_namespace=False,read_only=True) as (config,base):
            namespace=io.files.open_at(base,source_io.NAMESPACE,os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW)
            try:
                info=os.fstat(namespace);assert (info.st_uid,info.st_gid,stat.S_IMODE(info.st_mode))==(0,0,0o700)
                typed_equal([info.st_dev,info.st_ino],plan['storeIdentity']);assert digest(b'',config)==plan['storageHash']
                io.storage.attest(config,namespace,read_only=True)
                _,history,_=read_at(namespace,op+'.json')
                assert history['captureState']=='Purged' and history['historyDigest']==plan['command']['historyDigest']
                assert digest(b'archive-history-v1\n',history)==plan['historyMetadataDigest']
                yield archive,{**proof,'hddDataReread':True,'hddMetadataDigest':plan['historyMetadataDigest'],'hddMetadataJoined':True}
                _,after,_=read_at(namespace,op+'.json');typed_equal(after,history)
                io.storage.attest(config,namespace,read_only=True);assert os.path.samestat(info,os.fstat(namespace))
            finally:os.close(namespace)



def unit_identity(group):
    assert GROUP.fullmatch(group)
    v=dict(line.split('=',1) for line in subprocess.check_output(['systemctl','show',group,'-p','MainPID,InvocationID,ActiveState,SubState'],text=True,timeout=3).splitlines())
    assert v['MainPID'].isdecimal() and int(v['MainPID'])>1 and re.fullmatch('[a-f0-9]{32}',v['InvocationID']) and v['ActiveState']=='active' and v['SubState']=='running'
    return int(v['MainPID']),v['InvocationID']


def bound_process(pid,group,invocation,root,own_lock,purge_lock,source_lock,archive):
    if type(pid) is not int or pid<=1 or not GROUP.fullmatch(group) or not re.fullmatch('[a-f0-9]{32}',invocation) or len({root,own_lock,purge_lock,source_lock,archive})!=5:return False
    try:
        p=PROC/str(pid)
        if p.stat().st_uid!=0 or (p/'cmdline').read_bytes().rstrip(b'\0').split(b'\0')!=[b'/usr/bin/python3',b'-I',TRANSPORT,b'apply']:return False
        if b'0::/system.slice/'+group.encode() not in (p/'cgroup').read_bytes().splitlines() or b'INVOCATION_ID='+invocation.encode() not in (p/'environ').read_bytes().split(b'\0'):return False
        found=set()
        for fd in (p/'fd').iterdir():
            try:found.add(identity(fd))
            except (FileNotFoundError,PermissionError):pass
        return {root,own_lock,purge_lock,source_lock,archive}<=found
    except (FileNotFoundError,ProcessLookupError,PermissionError):return False


def settle_unheld(descriptor,stopped):
    # SIGSTOP was possible only after exact five-FD/argv/unit binding. Do not
    # resume a destructive Apply when its final proof/attestation fails.
    try:
        if stopped:
            try:signal.pidfd_send_signal(descriptor,signal.SIGKILL)
            except ProcessLookupError:pass
            assert select.select([descriptor],[],[],5)[0],'BOUND_UNHELD_ARCHIVE_DID_NOT_EXIT'
    finally:os.close(descriptor)


def freeze_before_copy(row,folder,container_pid,diagnostics=None):
    if diagnostics is None:diagnostics={}
    assert type(diagnostics) is dict
    diagnostics.update(boundOriginal=False,stopSent=False,cutAccepted=False)
    assert os.geteuid()==0 and re.fullmatch('[a-f0-9]{64}',row['container_id']) and type(container_pid) is int and container_pid>1 and row['instance_id'].startswith(str(container_pid)+':')
    folder=Path(folder);assert folder.parent==Path('/data/WhaleDeck/apps') and re.fullmatch('wdv-container-publish-[a-f0-9]{32}',folder.name) and folder.resolve(strict=True)==folder and not folder.is_symlink()
    assert row['execution_job_id']==execution(row['id']);p=managed_plan(stored_object(row['plan_json']));assert list(identity(folder))==p['parentIdentity']
    root=identity(PROC/str(container_pid)/'root');operation=uuid.UUID(row['execution_job_id']).hex;source=p['command']['purge']['plan']['command']['source']['operationId'];purge_op=p['command']['purge']['operationId']
    own=STATE/(operation+'.lock');old=SOURCE_STATE/(source+'.lock');purge_lock=PURGE_STATE/(purge_op+'.lock');fd=None;stopped=False;pid=group=invocation=None;held=None;deadline=time.monotonic()+105
    try:
        with current_archive(p) as (archive,hdd):
            while time.monotonic()<deadline:
                if fd is None and own.exists() and old.exists() and purge_lock.exists():
                    locks=(operation_lock(own),operation_lock(purge_lock),operation_lock(old))
                    # The Agent transport uses an independent random Driver.Id.
                    # The exact operation is bound by all three kernel lock FDs,
                    # not by equating the unit suffix with the durable job ID.
                    for candidate in CGROUP.glob('whaledeck-container-recycle-archives-*.service'):
                        if not candidate.exists() or not GROUP.fullmatch(candidate.name):continue
                        try:current,nonce=unit_identity(candidate.name)
                        except (OSError,subprocess.SubprocessError,AssertionError):continue
                        if bound_process(current,candidate.name,nonce,root,*locks,archive):
                            try:fd=os.pidfd_open(current)
                            except ProcessLookupError:continue
                            pid,group,invocation=current,candidate.name,nonce;diagnostics.update(boundOriginal=True,pid=pid,invocationId=invocation,cgroup=group);break
                if fd is not None:
                    # Bind again after pidfd_open, then stop that exact process
                    # immediately. A second systemctl round-trip consumes the
                    # real hashing window. All plan/state/storage proof remains
                    # mandatory while stopped, before any UI handoff.
                    locks=(operation_lock(own),operation_lock(purge_lock),operation_lock(old))
                    if select.select([fd],[],[],0)[0] or not bound_process(pid,group,invocation,root,*locks,archive):
                        diagnostics['missReason']='ORIGINAL_PROCESS_NO_LONGER_BOUND';return None
                    try:signal.pidfd_send_signal(fd,signal.SIGSTOP)
                    except ProcessLookupError:return None
                    stopped=True;diagnostics['stopSent']=True;until=time.monotonic()+2
                    while True:
                        state=(PROC/str(pid)/'stat').read_text().rsplit(')',1)[1].split()[0]
                        if state in ('T','t'):break
                        if state=='Z':return None
                        assert time.monotonic()<until,'ARCHIVE_STOP_NOT_OBSERVED';time.sleep(.0005)
                    frozen=record_for(row);diagnostics['lastRootState']=diagnostics['stoppedRootState']=frozen['state'];proof=before_copy_proof(frozen,folder)
                    if proof is None:
                        diagnostics['missReason']='STOPPED_PROOF_NOT_RESERVED';return None
                    assert unit_identity(group)==(pid,invocation) and bound_process(pid,group,invocation,root,operation_lock(own),operation_lock(purge_lock),operation_lock(old),archive)
                    proof.update(hdd,fiveKernelFdProofs=True,pid=pid,containerRootIdentity=list(root),archiveLockIdentity=list(operation_lock(own)),purgeLockIdentity=list(operation_lock(purge_lock)),sourceLockIdentity=list(operation_lock(old)),archiveFdIdentity=list(archive),invocationId=invocation,cgroup=group)
                    held=HeldArchive(pid,fd,group,frozen,proof);break
                try:r=record_for(row)
                except FileNotFoundError:r=None
                if r is not None:
                    diagnostics['lastRootState']=r['state']
                    if r['state'] not in ('Preparing','Reserved'):
                        diagnostics['missReason']='ROOT_ALREADY_PAST_RESERVED';return None
                time.sleep(.0005)
        if held is not None:
            fd=None;diagnostics['cutAccepted']=True;return held
        return None
    finally:
        if fd is not None:settle_unheld(fd,stopped)
