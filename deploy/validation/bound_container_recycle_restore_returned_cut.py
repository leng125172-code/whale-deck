"""QA-only post-return / pre-final-journal cut. No product hook or CLI.

The old source must already be durably Restored while this restore is still
Applying. Restoring/ambiguous namespaces and already terminal executions do
not count. Current HDD bytes and a returned RPC receipt need separate proof.
"""
import os,re,signal,time,uuid,subprocess
from pathlib import Path
import bound_container_recycle_restore_cut as before
from container_recycle_restore_rpc_proof import fields,claim_proof
from container_recycle_rpc_proof import ROOT as SOURCE_RECORD
from container_recycle_agent_io import physical
from recycle_restore_browser_proof import execution,managed_plan,stored_object
from acl_history_browser_proof import typed_equal


def after_return_proof(record,folder):
    if record['state']!='Applying':return None
    assert record['claimed'] is record['historyVerified'] is True and record['sourceOutcome'] is None
    plan=record['plan'];source=plan['command']['source'];assert record['historyDigest']==plan['command']['historyDigest']
    request={k:record[k] for k in ('operationId','owner','plan')}
    claim=before.private_record(before.STATE/'claims'/(source['operationId']+'.json'))
    assert claim_proof(claim,request),'RETURNED_CUT_SOURCE_CLAIM_MISMATCH'
    original=before.private_record(before.SOURCE_STATE/(source['operationId']+'.json'))
    assert original is not None;fields(original,SOURCE_RECORD)
    assert type(original['schemaVersion']) is int and original['schemaVersion']==1
    for key in ('operationId','owner','plan'):typed_equal(original[key],source[key])
    if original['state']!='Restored':return None
    assert original['historyMayExist'] is original['historyVerified'] is True and original['historyDigest']==record['historyDigest']
    typed_equal(original['privateIdentity'],plan['privateIdentity'])
    actual=original['actualIdentity'];assert type(actual) is list and len(actual)==9 and all(type(v) is int for v in actual)
    typed_equal(actual[:8],plan['retainedIdentity'][:8]);assert actual[8]>=plan['retainedIdentity'][8]
    assert list(before.identity(folder))==plan['parentIdentity']
    value=physical(folder,source['operationId'])
    if value['source'] is None or value['retained'] is not None:return None
    restored=value['source'];typed_equal(restored['identity'],actual)
    typed_equal(value['privateIdentity'],plan['privateIdentity'])
    for key in ('sha256','attributes'):typed_equal(restored[key],source['plan'][key])
    assert restored['sizeBytes']==source['plan']['sourceIdentity'][6]
    return dict(rootPhase='Applying',sourcePhase='Restored',sourceClaimMatches=True,sourceReturned=True,
        terminalRestoreJournalSaved=False,sourceOperationId=source['operationId'],restoreOperationId=record['operationId'],
        historyDigest=record['historyDigest'],restoredIdentity=restored['identity'],restoredSha256=restored['sha256'],
        privateIdentity=value['privateIdentity'],hddDataReread=False,rpcReceiptObserved=False)


def freeze_after_return(row,folder,container_pid):
    assert os.geteuid()==0 and re.fullmatch('[a-f0-9]{64}',row['container_id']) and type(container_pid) is int and container_pid>1 and row['instance_id'].startswith(str(container_pid)+':')
    folder=Path(folder);assert folder.parent==Path('/data/WhaleDeck/apps') and re.fullmatch('wdv-container-publish-[a-f0-9]{32}',folder.name) and folder.resolve(strict=True)==folder and not folder.is_symlink()
    assert row['execution_job_id']==execution(row['id'])
    plan=managed_plan(stored_object(row['plan_json']));assert list(before.identity(folder))==plan['parentIdentity']
    root_identity=before.identity(before.PROC/str(container_pid)/'root');operation=uuid.UUID(row['execution_job_id']).hex
    source_op=plan['command']['source']['operationId'];lock=before.STATE/(operation+'.lock');source_lock=before.SOURCE_STATE/(source_op+'.lock')
    fd=None;stopped=False;pid=group=invocation=None;deadline=time.monotonic()+105
    try:
        while time.monotonic()<deadline:
            if fd is None and lock.exists() and source_lock.exists():
                restore_id=before.operation_lock(lock);source_id=before.operation_lock(source_lock)
                for candidate in before.CGROUP.glob('whaledeck-container-recycle-restores-*.service'):
                    if not before.GROUP.fullmatch(candidate.name):continue
                    try:current,nonce=before.unit_identity(candidate.name)
                    except (OSError,subprocess.SubprocessError,AssertionError):continue
                    if before.bound_process(current,candidate.name,nonce,root_identity,restore_id,source_id):
                        try:fd=os.pidfd_open(current)
                        except ProcessLookupError:continue
                        pid,group,invocation=current,candidate.name,nonce;break
            try:record=before.record_for(row)
            except FileNotFoundError:record=None
            if record is not None and record['state'] in ('Completed','Aborted','Unknown'):return None
            source=before.private_record(before.SOURCE_STATE/(source_op+'.json'))
            if fd is not None and record is not None and record['state']=='Applying' and source is not None and source['state']=='Restored':
                locks=(before.operation_lock(lock),before.operation_lock(source_lock))
                if before.unit_identity(group)!=(pid,invocation) or not before.bound_process(pid,group,invocation,root_identity,*locks):return None
                try:signal.pidfd_send_signal(fd,signal.SIGSTOP)
                except ProcessLookupError:return None
                stopped=True;until=time.monotonic()+2
                while True:
                    state=(before.PROC/str(pid)/'stat').read_text().rsplit(')',1)[1].split()[0]
                    if state in ('T','t'):break
                    if state=='Z':return None
                    assert time.monotonic()<until,'RETURNED_STOP_NOT_OBSERVED';time.sleep(.0005)
                frozen=before.record_for(row);proof=after_return_proof(frozen,folder)
                if proof is None:return None
                assert before.unit_identity(group)==(pid,invocation) and before.bound_process(pid,group,invocation,root_identity,before.operation_lock(lock),before.operation_lock(source_lock))
                proof.update(threeKernelFdProofs=True,pid=pid,containerRootIdentity=list(root_identity),restoreLockIdentity=list(before.operation_lock(lock)),sourceLockIdentity=list(before.operation_lock(source_lock)),invocationId=invocation,cgroup=group)
                held=before.HeldCopy(pid,fd,group,frozen,proof);fd=None;return held
            time.sleep(.0005)
        return None
    finally:
        if fd is not None:before.release_unheld(fd,stopped)
