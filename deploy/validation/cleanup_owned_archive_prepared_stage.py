"""QA-only exact staged inode cleanup AFTER verified Unknown ACK. No CLI/RPC.

Never a product recovery or general Unknown cleanup. Preserve Root/claim/HDD;
only a freshly reread, operation-bound private stage in an owned fixture may go.
"""
import os,re,stat,fcntl,contextlib
from pathlib import Path
from acl_history_browser_proof import typed_equal
from recycle_archive_browser_proof import managed_plan,stored_object,receipt_join
from bound_container_ownership_cut import operation_lock
from container_recycle_archive_rpc_proof import request_proof
from bound_container_recycle_archive_prepared_cut import source_proof,parent_version
from bound_container_recycle_archive_cut import current_archive,private_record
import container_recycle_archive_agent_io as io
import container_recycle_agent_io as source_io
import container_recycle_purge_agent_io as purge_io


def owned_fixture(folder):
    assert os.geteuid()==0 and folder.parent==Path('/data/WhaleDeck/apps') and re.fullmatch('wdv-container-publish-[a-f0-9]{32}',folder.name) and folder.resolve(strict=True)==folder and not folder.is_symlink()


@contextlib.contextmanager
def existing_locks(q):
    purge=q['plan']['command']['purge'];source=purge['plan']['command']['source'];fds=[]
    try:
        for path in (io.STATE/(q['operationId']+'.lock'),purge_io.STATE/(purge['operationId']+'.lock'),source_io.STATE/(source['operationId']+'.lock')):
            expected=operation_lock(path);fd=os.open(path,os.O_RDWR|os.O_NOFOLLOW|os.O_NONBLOCK|os.O_CLOEXEC);fds.append(fd);i=os.fstat(fd)
            assert (i.st_dev,i.st_ino)==expected and (i.st_uid,i.st_gid,stat.S_IMODE(i.st_mode),i.st_nlink,i.st_size)==(0,0,0o600,1,0)
            fcntl.flock(fd,fcntl.LOCK_EX|fcntl.LOCK_NB)
        yield
    finally:
        for fd in reversed(fds):os.close(fd)


def cleanup_owned_stage(folder,q,root,expected,row):
    owned_fixture(folder);request_proof(q)
    with existing_locks(q):return _cleanup_locked(folder,q,root,expected,row)


def _cleanup_locked(folder,q,root,expected,row):
    assert row['state']=='Acknowledged' and row['root_state']=='Unknown' and row['archive_copied'] is None and row['completed_at_utc'] and row['acknowledged_by']!=row['actor_subject'] and row['acknowledged_by']
    assert not row['publish_barrier'] and row['lease_token'] is row['lease_expires_at_utc'] is None and row['execution_job_id'].replace('-','')==q['operationId'] and row['owner_hash']==q['owner']
    typed_equal(managed_plan(stored_object(row['plan_json'])),q['plan']);assert root['state']=='Unknown' and root['publishedIdentity'] is None
    typed_equal(stored_object(row['acknowledgement_json']),stored_object(row['receipt_json']))
    dto={k[0].lower()+k[1:]:v for k,v in stored_object(row['receipt_json']).items()};receipt=receipt_join(row,dto,q)
    assert receipt['state']=='Unknown' and receipt['archiveCopied'] is None and receipt['sourceFenced'] is receipt['ownedFence'] is True
    for k in ('operationId','owner','plan'):typed_equal(root[k],q[k])
    path=io.STATE/(q['operationId']+'.json');saved=private_record(path);typed_equal(saved,root);source_proof(root)
    current=io.physical(folder,q);typed_equal(current,expected);assert current['source'] is None and current['staged'] is not None and parent_version(root,folder)==root['parentAfterStage']
    typed_equal(current['staged']['identity'],root['preparedIdentity']);typed_equal(current['staged']['identity'][:2],root['pendingIdentity']);typed_equal(current['stageIdentity'],root['privateIdentity'])
    original=q['plan']['command']['purge']['plan']['command']['source']['plan'];typed_equal(current['staged']['attributes'],original['attributes']);assert current['staged']['sha256']==original['sha256'] and current['staged']['sizeBytes']==q['plan']['archiveBytes']
    name=io.STAGE_PREFIX+q['operationId'];parent=os.open(folder,os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW|os.O_CLOEXEC)
    try:
        assert [os.fstat(parent).st_dev,os.fstat(parent).st_ino]==q['plan']['parentIdentity'] and os.path.samestat(os.fstat(parent),folder.lstat())
        with current_archive(q['plan']):
            stage=os.open(name,os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW|os.O_CLOEXEC,dir_fd=parent)
            try:
                st=os.fstat(stage);assert [st.st_dev,st.st_ino]==root['privateIdentity'] and (st.st_uid,st.st_gid,stat.S_IMODE(st.st_mode))==(0,0,0o700) and os.listdir(stage)==['copy.partial']
                output=os.open('copy.partial',os.O_RDONLY|os.O_NOFOLLOW|os.O_NONBLOCK|os.O_CLOEXEC,dir_fd=stage)
                try:
                    frame=source_io.frame(os.fstat(output));typed_equal(frame,root['preparedIdentity']);assert frame[5]==1
                    typed_equal(source_io.file_snapshot(Path('/proc/self/fd/'+str(stage))/'copy.partial'),current['staged'])
                    try:os.stat('recycle-rpc.bin',dir_fd=parent,follow_symlinks=False)
                    except FileNotFoundError:pass
                    else:raise AssertionError('FOREIGN_TARGET_PRESERVED')
                    typed_equal(private_record(path),saved);source_proof(root);assert parent_version(root,folder)==root['parentAfterStage'] and os.path.samestat(st,os.stat(name,dir_fd=parent,follow_symlinks=False))
                    typed_equal(source_io.frame(os.stat('copy.partial',dir_fd=stage,follow_symlinks=False)),frame)
                    os.unlink('copy.partial',dir_fd=stage);os.fsync(stage);assert not os.listdir(stage)
                finally:os.close(output)
                assert os.path.samestat(st,os.stat(name,dir_fd=parent,follow_symlinks=False));os.rmdir(name,dir_fd=parent);os.fsync(parent)
            finally:os.close(stage)
    finally:os.close(parent)
    typed_equal(private_record(path),saved);source_proof(root)
    return dict(onlyOwnedStageRemoved=True,rootClaimHddRetained=True,productRestorePerformed=False,threeOriginalOperationLocksHeld=True,parentVersionChangeExpected=True,rootParentVersionNotRewritten=True,atomicInodeConditionalUnlink=False)
