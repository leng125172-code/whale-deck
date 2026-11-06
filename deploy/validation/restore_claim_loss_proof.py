"""QA-only loss of one exact owned claim index; original bytes stay quarantined.

Only the still-Applying returned cut may enter this fault. No SQL/journal edits,
no deletion of a completed record, no arbitrary path or signal endpoint.
"""
import hashlib,json,os,re,select,signal,stat,uuid
from pathlib import Path
from acl_history_browser_proof import typed_equal
from container_recycle_unknown_proof import rename_noreplace
from container_recycle_restore_rpc_proof import request_proof,claim_proof
from recursive_acl_restore_agent_io import read_at
import container_recycle_restore_agent_io as restore_io
import container_recycle_agent_io as source_io
from restore_instance_proof import bound_returned,instance_frame,require_frame,as_baseline
from validate_container_file_publishing import common
from container_recycle_restore_restart_proof import same_operation
from validate_metric_continuity import query


def names(q):
    request_proof(q);source=q['plan']['command']['source']['operationId']
    return source+'.json',source+'.'+q['operationId']+'.qa-claim-hold'


def _isolate(fd,q,expected):
    source,target=names(q);raw,value,before=read_at(fd,source);assert len(raw)<=65536 and claim_proof(value,q);typed_equal(value,expected)
    try:os.stat(target,dir_fd=fd,follow_symlinks=False);raise AssertionError('OWNED_CLAIM_QUARANTINE_EXISTS')
    except FileNotFoundError:pass
    rename_noreplace(fd,source,target)
    moved,claim,after=read_at(fd,target);assert moved==raw;typed_equal(claim,value);typed_equal(after[:8],before[:8]);assert after[8]>=before[8]
    try:os.stat(source,dir_fd=fd,follow_symlinks=False);raise AssertionError('OWNED_CLAIM_NOT_ABSENT')
    except FileNotFoundError:pass
    return dict(sourceName=source,quarantineName=target,rawSha256=hashlib.sha256(raw).hexdigest(),originalIdentity=before,quarantineIdentity=after,claim=value)


def isolate(f,fixture,held,row,pending,e):
    assert os.geteuid()==0 and f['identifier']==fixture['containerId'] and re.fullmatch('[a-f0-9]{64}',f['identifier'])
    folder=Path(fixture['folder']);assert folder==f['folder'] and folder.parent==common.APPS and folder.resolve(strict=True)==folder and not folder.is_symlink()
    assert re.fullmatch('wdv-container-publish-[a-f0-9]{32}',folder.name) and fixture['containerName']==folder.name
    frame=instance_frame(json.loads(common.command(['docker','inspect',fixture['containerId']]))[0]);require_frame(as_baseline(fixture),frame)
    assert frame['State']['Pid']==fixture['containerPid'] and str(frame['State']['Pid'])+':'+frame['State']['StartedAt']==fixture['instanceId']
    fixture['returned']=bound_returned(fixture,held,row,pending)
    q={k:held.record[k] for k in ('operationId','owner','plan')};fd=restore_io.private_directory(restore_io.STATE/'claims')
    try:
        assert not select.select([held.descriptor],[],[],0)[0]
        result=_isolate(fd,q,pending['claim']);assert not select.select([held.descriptor],[],[],0)[0]
    finally:os.close(fd)
    result.update(onlyExactOwnedClaimMoved=True,originalHelperStillAlive=True,sourceAndHddNotModified=True)
    with (e/'claim-loss.json').open('x') as out:json.dump(result,out)
    return result


def resume(fixture,held,row,pending,loss,e):
    # Still stopped, holding both operation locks: never open a window for a legacy writer.
    bound_returned(fixture,held,row,pending);q={k:held.record[k] for k in ('operationId','owner','plan')}
    fd=restore_io.private_directory(restore_io.STATE/'claims')
    try:
        raw,value,identity=read_at(fd,loss['quarantineName']);assert hashlib.sha256(raw).hexdigest()==loss['rawSha256'];typed_equal(value,loss['claim']);typed_equal(identity,loss['quarantineIdentity'])
        try:os.stat(names(q)[0],dir_fd=fd,follow_symlinks=False);raise AssertionError('CLAIM_REAPPEARED_BEFORE_CONTROLLED_RESUME')
        except FileNotFoundError:pass
    finally:os.close(fd)
    request=dict(pid=held.pid,cgroup=held.cgroup,invocationId=held.proof['invocationId'],sameOriginalPidfd=True,claimAbsent=True)
    with (e/'claim-loss-resume.json').open('x') as out:json.dump(request,out)
    signal.pidfd_send_signal(held.descriptor,signal.SIGCONT)


def rebuilt(q,loss):
    source,target=names(q);assert (source,target)==(loss['sourceName'],loss['quarantineName'])
    fd=restore_io.private_directory(restore_io.STATE/'claims')
    try:
        old,claim,old_identity=read_at(fd,target);assert hashlib.sha256(old).hexdigest()==loss['rawSha256'];typed_equal(claim,loss['claim']);typed_equal(old_identity,loss['quarantineIdentity'])
        raw,new,identity=read_at(fd,source);assert claim_proof(new,q);typed_equal(new,claim);assert identity[:2]!=old_identity[:2]
        return dict(rebuiltClaim=new,rebuiltIdentity=identity,rebuiltRawSha256=hashlib.sha256(raw).hexdigest(),quarantinedOriginalUnchanged=True)
    finally:os.close(fd)


def remove_quarantine(q,loss,verified):
    current=rebuilt(q,loss);typed_equal(current,verified)
    fd=restore_io.private_directory(restore_io.STATE/'claims')
    try:
        raw,value,identity=read_at(fd,loss['quarantineName']);assert hashlib.sha256(raw).hexdigest()==loss['rawSha256'];typed_equal(identity,loss['quarantineIdentity']);typed_equal(value,loss['claim'])
        os.unlink(loss['quarantineName'],dir_fd=fd);os.fsync(fd)
    finally:os.close(fd)


def acknowledgement_audit(identifier):
    assert str(uuid.UUID(identifier))==identifier
    return query("SELECT json_build_object('count',count(*),'actors',coalesce(json_agg(actor_subject),'[]'::json),'entries',coalesce(json_agg(json_build_object('result',result,'targetType',target_type,'targetId',target_id,'tracePresent',length(trace_id)>0)),'[]'::json)) FROM audit_events WHERE job_id=:'id'::uuid AND action='containers.files.recycle-restore.unknown-acknowledged';",id=identifier)


def stable_unknown(before,after):
    a,z=before['sql']['row'],after['sql']['row'];same_operation(a,z);assert a['state']==z['state']=='Unknown' and a['publish_barrier'] is z['publish_barrier'] is True
    # Unknown is excluded from ClaimAsync; no decline operation may advance SQL.
    typed_equal(before['sql'],after['sql'])
    for key in ('root_state','source_state','source_retained','source_restored','restored_by_this_operation','binding_current','history_digest','history_verified','history_available','cancel_requested_at_utc','acknowledged_by','acknowledgement_json'):typed_equal(a[key],z[key])
    for key in ('records','physical','claimRebuild','acknowledgementAudit'):typed_equal(before[key],after[key])
    facts=lambda r:{k:v for k,v in r.items() if k!='replayed'};typed_equal(facts(before['receipt']),facts(after['receipt']))


def require_ack(before,after,actor):
    a,z=before['sql']['row'],after['sql']['row'];same_operation(a,z)
    assert a['state']=='Unknown' and a['publish_barrier'] and z['state']=='Acknowledged' and not z['publish_barrier'] and z['acknowledged_by']==actor!=a['actor_subject'] and z['version']>a['version']
    for key in ('root_state','source_state','source_retained','source_restored','restored_by_this_operation','binding_current','history_digest','history_verified','history_available','cancel_requested_at_utc'):typed_equal(a[key],z[key])
    facts=lambda r:{k:v for k,v in r.items() if k!='replayed'};typed_equal(facts(before['receipt']),facts(after['receipt']))
    for key in ('records','physical','claimRebuild'):typed_equal(before[key],after[key])
    typed_equal(z['acknowledgement_json'],z['receipt_json'])


def preserve(before,after,source_op,restore_op,loss=None):
    from validate_container_recycle_restore_postreturn_cancel import preserved
    if loss is not None:
        key='claims/'+loss['quarantineName'];current=after['recycleRestoreRoot'];assert key not in before['recycleRestoreRoot'] and key in current and current[key]['sha256']==loss['rawSha256']
        typed_equal(current[key]['identity'],loss['quarantineIdentity'])
        after={**after,'recycleRestoreRoot':{k:v for k,v in current.items() if k!=key}}
    preserved(before,after,source_op,restore_op)
