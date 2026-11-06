"""QA-only exact published target + empty stage removal AFTER independent live ACK.

Never a product Unknown cleanup. Preserve Root/claim/HDD. The caller supplies
the post-publication parent version and physical snapshot from the original cut.
"""
import os,stat,re
from pathlib import Path
from acl_history_browser_proof import typed_equal
from recycle_archive_browser_proof import managed_plan,stored_object,receipt_join
from container_recycle_archive_rpc_proof import request_proof
from bound_container_recycle_archive_prepared_cut import source_proof,parent_version
from bound_container_recycle_archive_cut import current_archive,private_record
from cleanup_owned_archive_prepared_stage import owned_fixture,existing_locks
from archive_published_unknown_proof import published_proof
import container_recycle_archive_agent_io as io
import container_recycle_agent_io as source_io


def cleanup_owned_target(folder,q,root,cut,expected,version,row):
    owned_fixture(folder);request_proof(q)
    assert type(version) is str and re.fullmatch('[A-F0-9]{64}',version)
    with existing_locks(q):return _cleanup_locked(folder,q,root,cut,expected,version,row)


def _cleanup_locked(folder,q,root,cut,expected,version,row):
    assert row['state']=='Acknowledged' and row['root_state']=='Unknown' and row['archive_copied'] is None and row['completed_at_utc'] and row['acknowledged_by']!=row['actor_subject'] and row['acknowledged_by']
    assert not row['publish_barrier'] and row['lease_token'] is row['lease_expires_at_utc'] is None and row['execution_job_id'].replace('-','')==q['operationId'] and row['owner_hash']==q['owner']
    typed_equal(managed_plan(stored_object(row['plan_json'])),q['plan']);typed_equal(stored_object(row['acknowledgement_json']),stored_object(row['receipt_json']))
    dto={k[0].lower()+k[1:]:v for k,v in stored_object(row['receipt_json']).items()};receipt=receipt_join(row,dto,q)
    assert receipt['state']=='Unknown' and receipt['archiveCopied'] is None and receipt['sourceFenced'] is receipt['ownedFence'] is True
    for k in ('operationId','owner','plan'):typed_equal(root[k],q[k])
    path=io.STATE/(q['operationId']+'.json');saved=private_record(path);typed_equal(saved,root);source_proof(root)
    current=io.physical(folder,q);typed_equal(current,expected);published_proof(current,q,root,cut)
    assert version!=root['parentAfterStage'] and parent_version(root,folder)==version
    name=io.STAGE_PREFIX+q['operationId'];parent=os.open(folder,os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW|os.O_CLOEXEC)
    try:
        assert [os.fstat(parent).st_dev,os.fstat(parent).st_ino]==q['plan']['parentIdentity'] and os.path.samestat(os.fstat(parent),folder.lstat())
        assert set(os.listdir(parent))<={'original.bin',source_io.PRIVATE,name,'recycle-rpc.bin'}
        with current_archive(q['plan']):
            stage=os.open(name,os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW|os.O_CLOEXEC,dir_fd=parent)
            try:
                st=os.fstat(stage);assert [st.st_dev,st.st_ino]==root['privateIdentity'] and (st.st_uid,st.st_gid,stat.S_IMODE(st.st_mode))==(0,0,0o700) and not os.listdir(stage)
                output=os.open('recycle-rpc.bin',os.O_RDONLY|os.O_NOFOLLOW|os.O_NONBLOCK|os.O_CLOEXEC,dir_fd=parent)
                try:
                    frame=source_io.frame(os.fstat(output));typed_equal(frame,current['source']['identity']);assert frame[5]==1
                    typed_equal(source_io.file_snapshot(Path('/proc/self/fd/'+str(parent))/'recycle-rpc.bin'),current['source'])
                    typed_equal(private_record(path),saved);source_proof(root)
                    assert parent_version(root,folder)==version and os.path.samestat(st,os.stat(name,dir_fd=parent,follow_symlinks=False)) and not os.listdir(stage)
                    typed_equal(source_io.frame(os.stat('recycle-rpc.bin',dir_fd=parent,follow_symlinks=False)),frame)
                    os.unlink('recycle-rpc.bin',dir_fd=parent);assert os.fstat(output).st_nlink==0;os.fsync(parent)
                finally:os.close(output)
                assert not os.listdir(stage) and os.path.samestat(st,os.stat(name,dir_fd=parent,follow_symlinks=False))
                os.fsync(stage);os.rmdir(name,dir_fd=parent);os.fsync(parent)
            finally:os.close(stage)
    finally:os.close(parent)
    typed_equal(private_record(path),saved);source_proof(root)
    return dict(onlyOwnedPublishedTargetAndEmptyStageRemoved=True,rootClaimHddRetained=True,productRestorePerformed=False,threeOriginalOperationLocksHeld=True,parentVersionChangeExpected=True,rootParentVersionNotRewritten=True,atomicInodeConditionalUnlink=False)
