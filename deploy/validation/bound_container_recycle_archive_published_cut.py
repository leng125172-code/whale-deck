"""QA-only post-NOREPLACE / Publishing observer. No product hook or CLI.

Eight distinct kernel FD identities and the original invocation authorize STOP.
Any stopped proof failure kills only that pidfd; it never resumes Apply.
Namespace publication does not prove directory fsync or a terminal copy receipt.
"""
import hashlib,json,os,re,select,signal,subprocess,time,uuid
from pathlib import Path
import bound_container_recycle_archive_cut as before
from bound_container_recycle_archive_prepared_cut import source_proof,parent_version
from container_recycle_archive_rpc_proof import fields,RECORD,request_proof
from container_recycle_purge_rpc_proof import directory
from acl_history_browser_proof import typed_equal
from container_recycle_archive_agent_io import STATE,CLAIMS,STAGE_PREFIX,physical
from container_recycle_purge_agent_io import STATE as PURGE_STATE
from recycle_archive_browser_proof import execution,managed_plan,stored_object
from bound_container_ownership_cut import identity,operation_lock
from bound_container_recycle_restore_cut import private_record
import container_recycle_agent_io as source_io

SOURCE_STATE=source_io.STATE
PROC=Path('/proc')
CGROUP=before.CGROUP


def namespace_visible(record,folder):
    # Before STOP only a cheap, still-linked pending inode check is used. Full
    # hashes/attributes, directory and stage evidence remain mandatory stopped.
    try:
        i=(folder/'recycle-rpc.bin').lstat()
        return [i.st_dev,i.st_ino]==record['pendingIdentity'] and i.st_nlink==1
    except FileNotFoundError:return False


def published_namespace_proof(record,folder):
    # Publishing alone is before-or-after NOREPLACE. Only the physical inode
    # join below establishes namespace publication; no directory-fsync claim.
    if record['state']!='Publishing':return None
    fields(record,RECORD);assert type(record['schemaVersion']) is int and record['schemaVersion']==1
    q={k:record[k] for k in ('operationId','owner','plan')};request_proof(q);p=q['plan'];ref=p['command']['purge']['plan']['command']['source']
    assert record['privateIdentity'] is not None and record['pendingIdentity'] is not None and record['preparedIdentity'] is not None and record['publishedIdentity'] is None
    directory(record['privateIdentity']);directory(record['pendingIdentity']);prepared=record['preparedIdentity']
    assert type(prepared) is list and len(prepared)==9 and all(type(v) is int for v in prepared) and 0<=prepared[8]<2**63
    assert type(record['parentAfterStage']) is str and re.fullmatch('[A-F0-9]{64}',record['parentAfterStage'])
    source_proof(record);typed_equal(list(identity(folder)),p['parentIdentity']);parent_before=parent_version(record,folder)
    value=physical(folder,q)
    if value['source'] is None or value['staged'] is not None:return None
    assert value['retained'] is value['pending'] is None
    typed_equal(value['oldPrivateIdentity'],p['privateIdentity']);typed_equal(value['stageIdentity'],record['privateIdentity'])
    target=value['source'];prepared=record['preparedIdentity'];typed_equal(target['identity'][:8],prepared[:8]);assert target['identity'][8]>=prepared[8]
    typed_equal(target['identity'][:2],record['pendingIdentity']);assert target['identity'][5]==1 and target['identity'][:2]!=ref['plan']['sourceIdentity'][:2] and target['identity'][:2]!=p['archiveIdentity'][:2]
    typed_equal(target['identity'][2:8],ref['plan']['sourceIdentity'][2:8]);typed_equal(target['attributes'],ref['plan']['attributes'])
    assert target['sha256']==ref['plan']['sha256']==p['archiveSha256'] and target['sizeBytes']==p['archiveBytes']
    assert set(os.listdir(folder))<={'original.bin',source_io.PRIVATE,STAGE_PREFIX+q['operationId'],'recycle-rpc.bin'}
    # A plain pre-exchange Publishing journal must not be mistaken for this cut.
    assert parent_before!=record['parentAfterStage'] and parent_version(record,folder)==parent_before
    return dict(rootPhase='Publishing',sourcePhase='Purged',purgePhase='Completed',sourceClaimMatches=True,publishedNamespaceVerified=True,targetFullCopyVerified=True,stageEmpty=True,durablePublicationJournalSaved=False,terminalCopyJournalSaved=False,directoryFsyncProven=False,archiveOperationId=q['operationId'],purgeOperationId=p['command']['purge']['operationId'],sourceOperationId=ref['operationId'],physical=value,parentVersionAfterPublication=parent_before,hddDataReread=False)


def bound_published_process(pid,group,invocation,root,own,purge,source,archive,parent,stage,output):
    ids=(root,own,purge,source,archive,parent,stage,output)
    if len(set(ids))!=8 or not before.bound_process(pid,group,invocation,*ids[:5]):return False
    try:
        found=set()
        for fd in (PROC/str(pid)/'fd').iterdir():
            try:found.add(identity(fd))
            except (FileNotFoundError,PermissionError):pass
        return set(ids)<=found
    except (FileNotFoundError,ProcessLookupError,PermissionError):return False


def freeze_published_namespace(row,folder,container_pid,diagnostics=None):
    if diagnostics is None:diagnostics={}
    diagnostics.update(boundOriginal=False,stopSent=False,cutAccepted=False)
    assert os.geteuid()==0 and re.fullmatch('[a-f0-9]{64}',row['container_id']) and type(container_pid) is int and container_pid>1 and row['instance_id'].startswith(str(container_pid)+':')
    folder=Path(folder);assert folder.parent==Path('/data/WhaleDeck/apps') and re.fullmatch('wdv-container-publish-[a-f0-9]{32}',folder.name) and folder.resolve(strict=True)==folder and not folder.is_symlink()
    assert row['execution_job_id']==execution(row['id']);p=managed_plan(stored_object(row['plan_json']));typed_equal(list(identity(folder)),p['parentIdentity'])
    root=identity(PROC/str(container_pid)/'root');op=uuid.UUID(row['execution_job_id']).hex;purge=p['command']['purge'];source=purge['plan']['command']['source']['operationId']
    paths=(STATE/(op+'.lock'),PURGE_STATE/(purge['operationId']+'.lock'),SOURCE_STATE/(source+'.lock'))
    fd=None;stopped=False;held=None;pid=group=invocation=None;deadline=time.monotonic()+105
    try:
        with before.current_archive(p) as (archive,hdd):
            while time.monotonic()<deadline:
                if fd is None and all(path.exists() for path in paths):
                    locks=tuple(operation_lock(path) for path in paths)
                    for candidate in CGROUP.glob('whaledeck-container-recycle-archives-*.service'):
                        if not before.GROUP.fullmatch(candidate.name):continue
                        try:current,nonce=before.unit_identity(candidate.name)
                        except (OSError,subprocess.SubprocessError,AssertionError):continue
                        if before.bound_process(current,candidate.name,nonce,root,*locks,archive):
                            try:fd=os.pidfd_open(current)
                            except ProcessLookupError:continue
                            pid,group,invocation=current,candidate.name,nonce;diagnostics.update(boundOriginal=True,pid=pid,invocationId=nonce,cgroup=group);break
                try:record=before.record_for(row)
                except FileNotFoundError:record=None
                if record is not None:
                    diagnostics['lastRootState']=record['state']
                    if record['state'] in ('Published','Completed','Unknown','Aborted'):
                        diagnostics['missReason']='PAST_PUBLISHED_NAMESPACE_WINDOW';return None
                if fd is not None:
                    if select.select([fd],[],[],0)[0]:diagnostics['missReason']='ORIGINAL_EXITED';return None
                    if record is not None and record['state']=='Publishing' and namespace_visible(record,folder):
                        locks=tuple(operation_lock(path) for path in paths);extra=(tuple(p['parentIdentity']),tuple(record['privateIdentity']),tuple(record['pendingIdentity']))
                        if before.unit_identity(group)!=(pid,invocation) or not bound_published_process(pid,group,invocation,root,*locks,archive,*extra):return None
                        try:signal.pidfd_send_signal(fd,signal.SIGSTOP)
                        except ProcessLookupError:return None
                        stopped=True;diagnostics['stopSent']=True;until=time.monotonic()+2
                        while (PROC/str(pid)/'stat').read_text().rsplit(')',1)[1].split()[0] not in ('T','t'):
                            assert time.monotonic()<until,'PUBLISHED_NAMESPACE_STOP_NOT_OBSERVED';time.sleep(.0005)
                        frozen=before.record_for(row);diagnostics['stoppedRootState']=frozen['state'];proof=published_namespace_proof(frozen,folder)
                        if proof is None:diagnostics['missReason']='STOPPED_PROOF_NOT_PUBLISHED_NAMESPACE';return None
                        locks=tuple(operation_lock(path) for path in paths);extra=(tuple(p['parentIdentity']),tuple(frozen['privateIdentity']),tuple(frozen['pendingIdentity']))
                        assert before.unit_identity(group)==(pid,invocation) and bound_published_process(pid,group,invocation,root,*locks,archive,*extra)
                        proof.update(hdd,eightKernelFdProofs=True,pid=pid,containerRootIdentity=list(root),archiveLockIdentity=list(locks[0]),purgeLockIdentity=list(locks[1]),sourceLockIdentity=list(locks[2]),archiveFdIdentity=list(archive),parentFdIdentity=list(extra[0]),stageFdIdentity=list(extra[1]),outputFdIdentity=list(extra[2]),invocationId=invocation,cgroup=group)
                        held=before.HeldArchive(pid,fd,group,frozen,proof);break
                time.sleep(.0005)
        if held is not None:fd=None;diagnostics['cutAccepted']=True;return held
        return None
    finally:
        if fd is not None:before.settle_unheld(fd,stopped)
