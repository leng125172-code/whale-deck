"""QA-only Prepared postwrite/prepublish observer. No product hook or CLI.

Eight distinct kernel FD identities and the original invocation authorize STOP.
Any stopped proof failure kills only that pidfd; it never resumes Apply. Passing
this candidate does not prove an installed fault or an Unknown disposition.
"""
import hashlib,json,os,re,select,signal,subprocess,time,uuid
from pathlib import Path
import bound_container_recycle_archive_cut as before
from container_recycle_archive_rpc_proof import fields,RECORD,request_proof,digest
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


def source_proof(record):
    p=record['plan'];purge=p['command']['purge'];ref=purge['plan']['command']['source'];op=ref['operationId']
    original=private_record(SOURCE_STATE/(op+'.json'));deleted=private_record(PURGE_STATE/(purge['operationId']+'.json'));marker=private_record(SOURCE_STATE/(op+'.purge.json'))
    assert original is not None and deleted is not None and marker is not None
    fields(original,before.SOURCE_RECORD);fields(deleted,before.PURGE_RECORD);fields(marker,('schemaVersion','source','operationId','owner','fingerprint','state'))
    assert all(type(v['schemaVersion']) is int and v['schemaVersion']==1 for v in (original,deleted,marker))
    assert original['state']=='Purged' and original['actualIdentity'] is None and original['historyVerified'] is original['historyMayExist'] is True and original['historyDigest']==p['command']['historyDigest']
    assert deleted['state']=='Completed' and deleted['unlinkConfirmed'] is True and marker['state']=='Purged'
    for key in ('operationId','owner','plan'):typed_equal(original[key],ref[key]);typed_equal(deleted[key],purge[key])
    typed_equal(marker['source'],ref);assert (marker['operationId'],marker['owner'],marker['fingerprint'])==(purge['operationId'],purge['owner'],purge['plan']['fingerprint'])
    assert digest(b'archive-source-v1\n',original)==p['sourceRecordDigest'] and digest(b'archive-purge-v1\n',deleted)==p['purgeRecordDigest'] and digest(b'archive-marker-v1\n',marker)==p['purgeMarkerDigest']
    claim=private_record(CLAIMS/(op+'.json'));typed_equal(claim,dict(schemaVersion=1,source=ref,restoreOperationId=record['operationId'],restoreOwner=record['owner'],restoreFingerprint=p['fingerprint']))


def parent_version(record,folder):
    c=record['plan']['command']['purge']['plan']['command']['source']['plan']['command']
    values=[c['containerId'],c['instanceId'],c['path'].rpartition('/')[0],*source_io.frame(folder.stat())]
    return hashlib.sha256(b'whaledeck-container-file-v1\n'+json.dumps(values,ensure_ascii=False,separators=(',',':'),allow_nan=False).encode()).hexdigest().upper()


def prepared_proof(record,folder):
    if record['state']!='Prepared':return None
    fields(record,RECORD);assert type(record['schemaVersion']) is int and record['schemaVersion']==1
    q={k:record[k] for k in ('operationId','owner','plan')};request_proof(q);p=q['plan'];ref=p['command']['purge']['plan']['command']['source']
    assert record['privateIdentity'] is not None and record['pendingIdentity'] is not None and record['preparedIdentity'] is not None and record['publishedIdentity'] is None
    source_proof(record);typed_equal(list(identity(folder)),p['parentIdentity']);assert parent_version(record,folder)==record['parentAfterStage']
    value=physical(folder,q);assert value['source'] is value['retained'] is value['pending'] is None and value['staged'] is not None
    typed_equal(value['oldPrivateIdentity'],p['privateIdentity']);typed_equal(value['stageIdentity'],record['privateIdentity'])
    staged=value['staged'];typed_equal(staged['identity'],record['preparedIdentity']);typed_equal(staged['identity'][:2],record['pendingIdentity'])
    assert staged['identity'][5]==1 and staged['identity'][:2]!=ref['plan']['sourceIdentity'][:2] and staged['identity'][:2]!=p['archiveIdentity'][:2]
    typed_equal(staged['identity'][2:8],ref['plan']['sourceIdentity'][2:8]);typed_equal(staged['attributes'],ref['plan']['attributes'])
    assert staged['sha256']==ref['plan']['sha256']==p['archiveSha256'] and staged['sizeBytes']==p['archiveBytes']
    assert set(os.listdir(folder))<={'original.bin',source_io.PRIVATE,STAGE_PREFIX+q['operationId']}
    assert parent_version(record,folder)==record['parentAfterStage']
    return dict(rootPhase='Prepared',sourcePhase='Purged',purgePhase='Completed',sourceClaimMatches=True,stagedFullCopyVerified=True,targetAbsent=True,publicationNotEntered=True,terminalCopyJournalSaved=False,archiveOperationId=q['operationId'],purgeOperationId=p['command']['purge']['operationId'],sourceOperationId=ref['operationId'],physical=value,hddDataReread=False)


def bound_prepared_process(pid,group,invocation,root,own,purge,source,archive,parent,stage,output):
    ids=(root,own,purge,source,archive,parent,stage,output)
    if len(set(ids))!=8 or not before.bound_process(pid,group,invocation,*ids[:5]):return False
    try:
        found=set()
        for fd in (PROC/str(pid)/'fd').iterdir():
            try:found.add(identity(fd))
            except (FileNotFoundError,PermissionError):pass
        return set(ids)<=found
    except (FileNotFoundError,ProcessLookupError,PermissionError):return False


def freeze_prepared(row,folder,container_pid,diagnostics=None):
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
                    if record['state'] in ('Publishing','Published','Completed','Unknown','Aborted'):
                        diagnostics['missReason']='PAST_PREPARED_WINDOW';return None
                if fd is not None:
                    if select.select([fd],[],[],0)[0]:diagnostics['missReason']='ORIGINAL_EXITED';return None
                    if record is not None and record['state']=='Prepared':
                        locks=tuple(operation_lock(path) for path in paths);extra=(tuple(p['parentIdentity']),tuple(record['privateIdentity']),tuple(record['pendingIdentity']))
                        if before.unit_identity(group)!=(pid,invocation) or not bound_prepared_process(pid,group,invocation,root,*locks,archive,*extra):return None
                        try:signal.pidfd_send_signal(fd,signal.SIGSTOP)
                        except ProcessLookupError:return None
                        stopped=True;diagnostics['stopSent']=True;until=time.monotonic()+2
                        while (PROC/str(pid)/'stat').read_text().rsplit(')',1)[1].split()[0] not in ('T','t'):
                            assert time.monotonic()<until,'PREPARED_STOP_NOT_OBSERVED';time.sleep(.0005)
                        frozen=before.record_for(row);diagnostics['stoppedRootState']=frozen['state'];proof=prepared_proof(frozen,folder)
                        if proof is None:diagnostics['missReason']='STOPPED_PROOF_NOT_PREPARED';return None
                        locks=tuple(operation_lock(path) for path in paths);extra=(tuple(p['parentIdentity']),tuple(frozen['privateIdentity']),tuple(frozen['pendingIdentity']))
                        assert before.unit_identity(group)==(pid,invocation) and bound_prepared_process(pid,group,invocation,root,*locks,archive,*extra)
                        proof.update(hdd,eightKernelFdProofs=True,pid=pid,containerRootIdentity=list(root),archiveLockIdentity=list(locks[0]),purgeLockIdentity=list(locks[1]),sourceLockIdentity=list(locks[2]),archiveFdIdentity=list(archive),parentFdIdentity=list(extra[0]),stageFdIdentity=list(extra[1]),outputFdIdentity=list(extra[2]),invocationId=invocation,cgroup=group)
                        held=before.HeldArchive(pid,fd,group,frozen,proof);break
                time.sleep(.0005)
        if held is not None:fd=None;diagnostics['cutAccepted']=True;return held
        return None
    finally:
        if fd is not None:before.settle_unheld(fd,stopped)
