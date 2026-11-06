"""Independent Unknown/ACK and physical-stage joins; no mutation in this module."""
from pathlib import Path
from acl_history_browser_proof import typed_equal
from recycle_archive_browser_proof import execution,managed_plan,stored_object,receipt_join,job_summary
from container_recycle_archive_restart_proof import same_operation
from validate_container_recycle_archive_browser import archive_sql
from validate_container_recycle_purge_browser import hdd_observation
from validate_metric_continuity import query
import container_recycle_archive_agent_io as io


def row_proof(data,fixture,purge_row,acknowledged):
    assert type(acknowledged) is bool;r=data['row']
    assert r['actor_subject']==fixture['subject'] and (r['container_id'],r['instance_id'],r['relative_path'])==(fixture['containerId'],fixture['instanceId'],'work/recycle-rpc.bin')
    assert r['state']==('Acknowledged' if acknowledged else 'Unknown') and r['result_code']==('CONTAINER_RECYCLE_ARCHIVE_UNKNOWN_ACKNOWLEDGED' if acknowledged else 'CONTAINER_RECYCLE_ARCHIVE_RESULT_UNKNOWN')
    assert r['execution_job_id']==execution(r['id']) and all(r[k] for k in ('root_started_at_utc','reservation_json','apply_started_at_utc','cancel_requested_at_utc'))
    assert r['root_state']=='Unknown' and r['archive_copied'] is None and r['source_fenced'] is r['owned_fence'] is True
    assert r['binding_current'] is False or r['binding_current'] is None
    assert r['publish_barrier'] is (not acknowledged) and bool(r['completed_at_utc']) is acknowledged
    assert r['lease_token'] is r['lease_expires_at_utc'] is None and bool(r['acknowledged_by']) is bool(r['acknowledgement_json']) is acknowledged
    assert type(data['pending']) is int and data['pending']==(0 if acknowledged else 2) and data['accepted']==data['confirmed']==1 and data['canceled']==0
    job=data['executionJob'];assert job['state']==('Failed' if acknowledged else 'Running') and job['phase']==('Acknowledged' if acknowledged else 'AwaitingContainerRecycleArchiveReceipt') and bool(job['completed_at_utc']) is acknowledged and job['cancel_requested_at_utc']
    assert data['planJob']['state']=='Succeeded';job_summary(stored_object(job['result_json']),r,purge_row,execution_stage=True);job_summary(stored_object(data['planJob']['result_json']),r,purge_row,execution_stage=False)


def staged_proof(value,q,root,cut):
    assert root['state']=='Unknown' and cut['state']=='Prepared'
    for key in cut:
        if key!='state':typed_equal(root[key],cut[key])
    p=q['plan'];original=p['command']['purge']['plan']['command']['source']['plan']
    assert root['publishedIdentity'] is None and value['source'] is value['retained'] is value['pending'] is None
    typed_equal(value['parentIdentity'],p['parentIdentity']);typed_equal(value['oldPrivateIdentity'],p['privateIdentity']);typed_equal(value['stageIdentity'],root['privateIdentity'])
    file=value['staged'];assert file is not None;typed_equal(file['identity'],root['preparedIdentity']);typed_equal(file['identity'][:2],root['pendingIdentity']);typed_equal(file['identity'][2:8],original['sourceIdentity'][2:8]);typed_equal(file['attributes'],original['attributes'])
    assert file['identity'][5]==1 and file['identity'][:2]!=p['archiveIdentity'][:2] and file['identity'][:2]!=original['sourceIdentity'][:2] and file['sha256']==p['archiveSha256']==original['sha256'] and file['sizeBytes']==p['archiveBytes']
    return dict(physicalStagedFullCopy=True,targetAbsent=True,publishedCopyNotProven=True)


def audits(r):
    return query("SELECT json_build_object('cancel',(SELECT count(*) FROM audit_events WHERE job_id IN (:'intent'::uuid,:'job'::uuid) AND action='containers.files.recycle-archive.cancel-requested'),'ack',(SELECT count(*) FROM audit_events WHERE job_id=:'job'::uuid AND action='containers.files.recycle-archive.unknown-acknowledged'),'ackActors',(SELECT coalesce(json_agg(actor_subject),'[]'::json) FROM audit_events WHERE job_id=:'job'::uuid AND action='containers.files.recycle-archive.unknown-acknowledged'))",intent=r['id'],job=r['execution_job_id'])


def prove(dto,fixture,source,purge,cut,e,*,create=False,acknowledged=False):
    data=archive_sql(dto['id']);row_proof(data,fixture,purge['sql']['row'],acknowledged);r=data['row'];assert dto['state']==r['state'] and dto['executionJobId']==r['execution_job_id'] and dto['archiveCopied'] is None
    p=managed_plan(stored_object(r['plan_json']));typed_equal(p['command']['purge'],purge['request']);q=dict(operationId=r['execution_job_id'].replace('-',''),owner=r['owner_hash'],plan=p)
    receipt=receipt_join(r,dto['receipt'],q);assert receipt['state']=='Unknown' and receipt['archiveCopied'] is None and receipt['ownedFence'] is receipt['sourceFenced'] is True
    records=io.read_operation(q,receipt,purge['records'],e/'archive-originals',create=create);physical=io.physical(Path(fixture['folder']),q);staged_proof(physical,q,records['root'],cut)
    from validate_container_recycle_browser import sql as source_sql
    from validate_container_recycle_purge_browser import sql as purge_sql
    typed_equal(source_sql(source['sql']['row']['id']),source['sql']);typed_equal(purge_sql(purge['sql']['row']['id']),purge['sql']);hdd=hdd_observation(source['request']['operationId']);typed_equal(hdd,purge['hdd'])
    audit=audits(r);assert audit['cancel']==1 and audit['ack']==int(acknowledged) and audit['ackActors']==([r['acknowledged_by']] if acknowledged else [])
    if acknowledged:assert r['acknowledged_by']!=r['actor_subject'] and stored_object(r['acknowledgement_json'])==stored_object(r['receipt_json'])
    return dict(sql=data,request=q,receipt=receipt,records=records,physical=physical,hdd=hdd,audits=audit)


def require_ack(before,after,actor):
    a,b=before['sql']['row'],after['sql']['row'];same_operation(a,b)
    assert a['state']=='Unknown' and b['state']=='Acknowledged' and actor!=a['actor_subject'] and b['acknowledged_by']==actor and b['version']>a['version']
    for key in ('root_state','archive_copied','source_fenced','owned_fence','binding_current','cancel_requested_at_utc'):typed_equal(a[key],b[key])
    for key in ('receipt','records','physical','hdd'):typed_equal(before[key],after[key])
