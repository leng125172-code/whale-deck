"""QA-only exact post-unlink cut: original four FDs plus the unlinked inode FD.

No CLI, product hook, rename or unlink. Only an owned apply invocation can be
paused. A failed held proof terminates that bound process, never resumes Apply.
"""
import base64,hashlib,os,re,select,signal,stat,time,uuid
from pathlib import Path
from bound_container_copy_cut import HeldCopy
from bound_container_ownership_cut import identity,operation_lock
from bound_container_recycle_purge_cut import current_archive,unit_identity,bound_process,record_for,private_record,settle_unheld,CGROUP,PROC,GROUP,STATE,SOURCE_STATE
from recycle_purge_browser_proof import execution,managed_plan,stored_object
from container_recycle_rpc_proof import ROOT as SOURCE_RECORD
from container_recycle_purge_rpc_proof import fields
from acl_history_browser_proof import typed_equal
import container_recycle_agent_io as source_io
import container_recycle_purge_agent_io as purge_io


def unlinked_frame(frame,expected):
    assert type(frame) is list and len(frame)==9 and all(type(n) is int for n in frame)
    assert stat.S_ISREG(frame[2]) and frame[:5]==expected[:5] and frame[5]==0 and frame[6:8]==expected[6:8] and frame[8]>=expected[8]


def unlinked_fd(pid,expected):
    try:
        for path in (PROC/str(pid)/'fd').iterdir():
            try:
                value=source_io.frame(path.stat())
                if value[:2]!=expected[:2]:continue
                unlinked_frame(value,expected)
                if path.name.isdecimal():return int(path.name)
            except (FileNotFoundError,PermissionError,AssertionError):pass
    except (FileNotFoundError,ProcessLookupError,PermissionError):pass
    return None


def snapshot_unlinked(pid,number,expected):
    assert type(pid) is int and pid>1 and type(number) is int and number>=0
    # Following this kernel proc magic link is deliberate: the exact paused
    # helper and inode are bound first; O_NOFOLLOW would reject the proc FD.
    path=PROC/str(pid)/'fd'/str(number);fd=os.open(path,os.O_RDONLY|os.O_NONBLOCK|os.O_CLOEXEC)
    try:
        before=source_io.frame(os.fstat(fd));unlinked_frame(before,expected);attrs=[]
        for name in sorted(os.listxattr(fd)):
            assert name.startswith('user.');attrs.append([name,base64.b64encode(os.getxattr(fd,name)).decode()])
        h=hashlib.sha256();size=0
        while part:=os.read(fd,65536):size+=len(part);assert size<=expected[6];h.update(part)
        assert size==expected[6] and before==source_io.frame(os.fstat(fd))==source_io.frame(path.stat())
        return dict(identity=before,attributes=attrs,sha256=h.hexdigest().upper(),sizeBytes=size)
    finally:os.close(fd)


def after_unlink_proof(record,folder,snapshot):
    if record['state']!='Removing':return None
    p=record['plan'];ref=p['command']['source'];op=ref['operationId'];pending=record['pendingIdentity']
    assert pending is not None and pending[:8]==p['retainedIdentity'][:8] and pending[8]>=p['retainedIdentity'][8] and record['unlinkConfirmed'] is False
    unlinked_frame(snapshot['identity'],pending)
    assert snapshot['sha256']==ref['plan']['sha256'] and snapshot['sizeBytes']==p['archiveBytes'];typed_equal(snapshot['attributes'],ref['plan']['attributes'])
    original=private_record(SOURCE_STATE/(op+'.json'));assert original is not None;fields(original,SOURCE_RECORD)
    for k in ('operationId','owner','plan'):typed_equal(original[k],ref[k])
    assert type(original['schemaVersion']) is int and original['schemaVersion']==1
    assert original['state']=='Purging' and original['actualIdentity'] is None and original['historyVerified'] is original['historyMayExist'] is True
    assert original['historyDigest']==p['command']['historyDigest'];typed_equal(original['privateIdentity'],p['privateIdentity'])
    marker=private_record(SOURCE_STATE/(op+'.purge.json'));assert marker is not None
    fields(marker,('schemaVersion','source','operationId','owner','fingerprint','state'));assert type(marker['schemaVersion']) is int and marker['schemaVersion']==1;typed_equal(marker['source'],ref)
    assert (marker['operationId'],marker['owner'],marker['fingerprint'],marker['state'])==(record['operationId'],record['owner'],p['fingerprint'],'Deleting')
    physical=purge_io.physical(Path(folder),{k:record[k] for k in ('operationId','owner','plan')})
    assert physical['source'] is physical['retained'] is physical['pending'] is None
    return dict(rootPhase='Removing',sourcePhase='Purging',sourceMarkerPhase='Deleting',namespaceUnlinked=True,durableUnlinkConfirmation=False,
        purgeOperationId=record['operationId'],sourceOperationId=op,physical=physical,unlinkedOriginal=snapshot)


def freeze_after_unlink(row,folder,container_pid,diagnostics=None):
    if diagnostics is None:diagnostics={}
    diagnostics.update(boundOriginal=False,stopSent=False,cutAccepted=False)
    assert os.geteuid()==0 and re.fullmatch('[a-f0-9]{64}',row['container_id']) and type(container_pid) is int and container_pid>1 and row['instance_id'].startswith(str(container_pid)+':')
    folder=Path(folder);assert folder.parent==Path('/data/WhaleDeck/apps') and re.fullmatch('wdv-container-publish-[a-f0-9]{32}',folder.name) and folder.resolve(strict=True)==folder and not folder.is_symlink()
    assert row['execution_job_id']==execution(row['id']);p=managed_plan(stored_object(row['plan_json']));assert list(identity(folder))==p['parentIdentity']
    root=identity(PROC/str(container_pid)/'root');operation=uuid.UUID(row['execution_job_id']).hex;source=p['command']['source']['operationId'];own=STATE/(operation+'.lock');old=SOURCE_STATE/(source+'.lock')
    fd=None;stopped=False;held=None;pid=group=invocation=None;deadline=time.monotonic()+105
    try:
        with current_archive(p) as (archive,hdd):
            while time.monotonic()<deadline:
                if fd is None and own.exists() and old.exists():
                    locks=(operation_lock(own),operation_lock(old))
                    for candidate in CGROUP.glob('whaledeck-container-recycle-purges-*.service'):
                        if not GROUP.fullmatch(candidate.name):continue
                        try:current,nonce=unit_identity(candidate.name)
                        except (OSError,AssertionError):continue
                        if bound_process(current,candidate.name,nonce,root,*locks,archive):
                            try:fd=os.pidfd_open(current)
                            except ProcessLookupError:continue
                            pid,group,invocation=current,candidate.name,nonce;diagnostics['boundOriginal']=True;break
                if fd is not None:
                    if select.select([fd],[],[],0)[0]:diagnostics['missReason']='ORIGINAL_EXITED';return None
                    number=unlinked_fd(pid,p['retainedIdentity'])
                    if number is not None and bound_process(pid,group,invocation,root,operation_lock(own),operation_lock(old),archive):
                        signal.pidfd_send_signal(fd,signal.SIGSTOP);stopped=True;diagnostics['stopSent']=True;until=time.monotonic()+2
                        while (PROC/str(pid)/'stat').read_text().rsplit(')',1)[1].split()[0] not in ('T','t'):
                            assert time.monotonic()<until;time.sleep(.0005)
                        r=record_for(row);diagnostics['stoppedRootState']=r['state'];snapshot=snapshot_unlinked(pid,number,p['retainedIdentity']);proof=after_unlink_proof(r,folder,snapshot)
                        if proof is None:diagnostics['missReason']='NOT_REMOVING';return None
                        assert unit_identity(group)==(pid,invocation) and bound_process(pid,group,invocation,root,operation_lock(own),operation_lock(old),archive)
                        original=tuple(snapshot['identity'][:2]);assert len({root,operation_lock(own),operation_lock(old),archive,original})==5
                        proof.update(hdd,fiveKernelFdProofs=True,pid=pid,unlinkedFdNumber=number,containerRootIdentity=list(root),purgeLockIdentity=list(operation_lock(own)),sourceLockIdentity=list(operation_lock(old)),archiveFdIdentity=list(archive),invocationId=invocation,cgroup=group)
                        held=HeldCopy(pid,fd,group,r,proof);break
                try:r=record_for(row)
                except FileNotFoundError:r=None
                if r is not None:
                    diagnostics['lastRootState']=r['state']
                    if r['state'] in ('Removed','Completed','Unknown','Aborted'):diagnostics['missReason']='PAST_UNLINK_WINDOW';return None
                time.sleep(.0005)
        if held is not None:fd=None;diagnostics['cutAccepted']=True;return held
        return None
    finally:
        if fd is not None:settle_unheld(fd,stopped)
