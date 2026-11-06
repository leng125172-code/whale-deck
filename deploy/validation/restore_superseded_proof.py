"""QA-only returned-file displacement and strict Superseded historical facts.

No CLI or arbitrary PID/path endpoint. Actual Root/HDD and UI acquisition stay
in the owning gate; pure projection checks cannot establish a namespace.
"""
import json,os,re,select,stat,uuid
from pathlib import Path
from acl_history_browser_proof import typed_equal
from container_recycle_unknown_proof import rename_noreplace,displaced_proof
from container_recycle_agent_io import file_snapshot,physical
from validate_container_file_publishing import common,verify_container
from validate_container_recycle_restore_postreturn_cancel import cut_binding
from recycle_restore_browser_proof import execution,managed_plan,managed_receipt,job_summary,stored_object
import bound_container_recycle_restore_cut as cut

DISPLACED='restore-displaced-returned.bin'


def terminal(value,data,fixture,source):
    r=data['row'];assert r['id']==value['id'] and r['actor_subject']==fixture['subject']
    assert (r['container_id'],r['instance_id'],r['relative_path'])==(fixture['containerId'],fixture['instanceId'],'work/recycle-rpc.bin')
    assert r['state']==value['state']=='Superseded' and r['result_code']=='CONTAINER_RECYCLE_RESTORE_TARGET_CHANGED'
    assert r['completed_at_utc'] and r['root_started_at_utc'] and r['cancel_requested_at_utc'] is None and value['confirmation'] is None
    assert r['lease_token'] is r['lease_expires_at_utc'] is None and r['publish_barrier'] is False
    assert r['root_state']=='Completed' and r['source_state']=='Restored' and r['binding_current'] is r['source_retained'] is False
    assert r['history_verified'] is r['history_available'] is r['source_restored'] is r['restored_by_this_operation'] is True
    assert r['acknowledged_by'] is r['acknowledgement_json'] is None
    assert all(type(data[k]) is int for k in ('pending','accepted','confirmed','canceled'))
    assert data['pending']==0 and data['accepted']==data['confirmed']==1 and data['canceled']==0
    assert data['planJob']['id']==r['id'] and data['planJob']['state']=='Succeeded'
    op=execution(r['id']);source_op=execution(r['source_intent_id'],source=True)
    assert r['execution_job_id']==value['executionJobId']==data['executionJob']['id']==op
    assert r['source_intent_id']==value['sourceIntentId']==source['sql']['row']['id']
    assert source_op==value['sourceOperationId']==source['sql']['row']['execution_job_id']
    job=data['executionJob'];assert job['state']=='Failed' and job['phase']=='Superseded' and job['error_code']==r['result_code'] and job['completed_at_utc'] and job['cancel_requested_at_utc'] is None
    plan=managed_plan(stored_object(r['plan_json']));typed_equal(plan['command']['source'],source['request'])
    command=plan['command']['source']['plan']['command'];assert (command['containerId'],command['instanceId'],command['path'])==(r['container_id'],r['instance_id'],r['relative_path'])
    q=dict(operationId=uuid.UUID(op).hex,owner=r['owner_hash'],plan=plan);receipt=managed_receipt(value['receipt'],q)
    assert receipt['state']=='Completed' and receipt['bindingCurrent'] is False and receipt['sourceState']=='Restored'
    assert r['history_digest']==receipt['historyDigest']==plan['command']['historyDigest']==value['sourceHistoryDigest']
    stored=stored_object(r['receipt_json']);assert set(stored)=={k[0].upper()+k[1:] for k in value['receipt']}
    typed_equal({k[0].lower()+k[1:]:v for k,v in stored.items()},value['receipt'])
    job_summary(stored_object(data['planJob']['result_json']),r,execution_stage=False)
    job_summary(stored_object(job['result_json']),r,execution_stage=True)
    return q,receipt


def namespace(folder,original,records,receipt,expected_displaced):
    assert receipt['state']=='Completed' and receipt['bindingCurrent'] is False and receipt['restoredByThisOperation'] is True
    value=physical(folder,records['sourceRoot']['operationId']);assert value['source'] is value['retained'] is None
    typed_equal(value['privateIdentity'],records['sourceRoot']['privateIdentity'])
    moved=file_snapshot(folder/DISPLACED);assert moved is not None;typed_equal(moved,expected_displaced);displaced_proof(original,moved)
    typed_equal(moved['identity'][:8],records['sourceRoot']['actualIdentity'][:8])
    assert moved['identity'][8]>=records['sourceRoot']['actualIdentity'][8]
    return value,moved


def displace_returned(f,fixture,held,row,pending):
    assert os.geteuid()==0 and fixture['containerId']==f['identifier'] and re.fullmatch('[a-f0-9]{64}',f['identifier'])
    folder=Path(fixture['folder']);assert folder==f['folder'] and folder.parent==common.APPS and folder.resolve(strict=True)==folder and not folder.is_symlink()
    assert re.fullmatch('wdv-container-publish-[a-f0-9]{32}',folder.name) and fixture['containerName']==folder.name
    current=json.loads(common.command(['docker','inspect',fixture['containerId']]))[0];verify_container(current,fixture['containerId'],fixture['containerName'],folder)
    assert current['State']['Pid']==fixture['containerPid'] and str(current['State']['Pid'])+':'+current['State']['StartedAt']==fixture['instanceId']
    cut_binding(row,held.record,held.proof,fixture);proof=held.proof
    assert held.pid==proof['pid'] and held.cgroup==proof['cgroup'] and held.descriptor is not None and not select.select([held.descriptor],[],[],0)[0]
    command=held.record['plan']['command']['source']['plan']['command'];assert (command['containerId'],command['instanceId'],command['path'])==(fixture['containerId'],fixture['instanceId'],'work/recycle-rpc.bin')
    assert row['state']=='Applying' and row['publish_barrier'] and row['root_started_at_utc']
    typed_equal(pending['root'],held.record);assert pending['verification']['hddDataReread'] is True and pending['sourceRoot']['state']=='Restored'
    op=held.record['operationId'];source_op=held.record['plan']['command']['source']['operationId']
    restore_lock=cut.operation_lock(cut.STATE/(op+'.lock'));source_lock=cut.operation_lock(cut.SOURCE_STATE/(source_op+'.lock'))
    root=tuple(proof['containerRootIdentity']);assert cut.unit_identity(proof['cgroup'])==(held.pid,proof['invocationId'])
    assert cut.bound_process(held.pid,held.cgroup,proof['invocationId'],root,restore_lock,source_lock)
    assert (cut.PROC/str(held.pid)/'stat').read_text().rsplit(')',1)[1].split()[0] in ('T','t')
    typed_equal(cut.record_for(row),held.record);typed_equal(cut.private_record(cut.SOURCE_STATE/(source_op+'.json')),pending['sourceRoot'])
    fd=os.open(folder,os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW|os.O_CLOEXEC)
    try:
        i=os.fstat(fd);assert [i.st_dev,i.st_ino]==held.record['plan']['parentIdentity'] and i.st_uid==0 and stat.S_IMODE(i.st_mode)==0o755
        before=file_snapshot(folder/'recycle-rpc.bin');typed_equal(before['identity'][:8],fixture['original']['identity'][:8]);displaced_proof(fixture['original'],before)
        typed_equal(before['identity'],pending['sourceRoot']['actualIdentity']);assert not os.path.lexists(folder/DISPLACED)
        assert not select.select([held.descriptor],[],[],0)[0]
        rename_noreplace(fd,'recycle-rpc.bin',DISPLACED)
    finally:os.close(fd)
    moved=file_snapshot(folder/DISPLACED);displaced_proof(before,moved);assert file_snapshot(folder/'recycle-rpc.bin') is None
    assert not select.select([held.descriptor],[],[],0)[0]
    return dict(sourceAbsent=True,displaced=moved,onlyExactReturnedLeafMoved=True,originalHelperStillAlive=True,threeKernelFdProofsRechecked=True)


def cleanup_displaced_proof(actual,original,verified):
    assert verified['physical']['source'] is verified['physical']['retained'] is None
    typed_equal(actual,verified['displaced']);assert actual is not None
    typed_equal(actual['identity'][:8],original['identity'][:8])
    assert type(actual['identity'][8]) is int and actual['identity'][8]>=original['identity'][8]
    for key in ('sha256','attributes','sizeBytes'):typed_equal(actual[key],original[key])
