"""Independent restart Unknown/ACK and namespace-published target joins; no mutation in this module."""
from pathlib import Path
from acl_history_browser_proof import typed_equal
from recycle_archive_browser_proof import execution,managed_plan,stored_object,receipt_join,job_summary
from container_recycle_archive_restart_proof import same_operation
from validate_container_recycle_archive_browser import archive_sql
from validate_container_recycle_purge_browser import hdd_observation
from validate_metric_continuity import query
import container_recycle_archive_agent_io as io
from archive_published_unknown_proof import published_proof
from bound_container_recycle_archive_prepared_cut import parent_version


def row_proof(data,fixture,purge_row,acknowledged):
    assert type(acknowledged) is bool;r=data['row']
    assert r['actor_subject']==fixture['subject'] and (r['container_id'],r['instance_id'],r['relative_path'])==(fixture['containerId'],fixture['instanceId'],'work/recycle-rpc.bin')
    assert r['state']==('Acknowledged' if acknowledged else 'Unknown') and r['result_code']==('CONTAINER_RECYCLE_ARCHIVE_UNKNOWN_ACKNOWLEDGED' if acknowledged else 'CONTAINER_RECYCLE_ARCHIVE_RESULT_UNKNOWN')
    assert r['execution_job_id']==execution(r['id']) and all(r[k] for k in ('root_started_at_utc','reservation_json','apply_started_at_utc'))
    assert r['cancel_requested_at_utc'] is None
    assert r['root_state']=='Unknown' and r['archive_copied'] is None and r['source_fenced'] is r['owned_fence'] is True
    assert r['binding_current'] is False or r['binding_current'] is None
    assert r['publish_barrier'] is (not acknowledged) and bool(r['completed_at_utc']) is acknowledged
    assert r['lease_token'] is r['lease_expires_at_utc'] is None and bool(r['acknowledged_by']) is bool(r['acknowledgement_json']) is acknowledged
    assert type(data['pending']) is int and data['pending']==(0 if acknowledged else 1) and data['accepted']==data['confirmed']==1 and data['canceled']==0
    job=data['executionJob'];assert job['state']==('Failed' if acknowledged else 'Running') and job['phase']==('Acknowledged' if acknowledged else 'AwaitingContainerRecycleArchiveReceipt') and bool(job['completed_at_utc']) is acknowledged and job['cancel_requested_at_utc'] is None
    assert data['planJob']['state']=='Succeeded';job_summary(stored_object(job['result_json']),r,purge_row,execution_stage=True);job_summary(stored_object(data['planJob']['result_json']),r,purge_row,execution_stage=False)


def audits(r):
    return query("SELECT json_build_object('cancel',(SELECT count(*) FROM audit_events WHERE job_id IN (:'intent'::uuid,:'job'::uuid) AND action='containers.files.recycle-archive.cancel-requested'),'ack',(SELECT count(*) FROM audit_events WHERE job_id=:'job'::uuid AND action='containers.files.recycle-archive.unknown-acknowledged'),'ackActors',(SELECT coalesce(json_agg(actor_subject),'[]'::json) FROM audit_events WHERE job_id=:'job'::uuid AND action='containers.files.recycle-archive.unknown-acknowledged'))",intent=r['id'],job=r['execution_job_id'])


def prove(dto,fixture,source,purge,cut,e,*,create=False,acknowledged=False):
    data=archive_sql(dto['id']);row_proof(data,fixture,purge['sql']['row'],acknowledged);r=data['row'];assert dto['state']==r['state'] and dto['executionJobId']==r['execution_job_id'] and dto['archiveCopied'] is None
    p=managed_plan(stored_object(r['plan_json']));typed_equal(p['command']['purge'],purge['request']);q=dict(operationId=r['execution_job_id'].replace('-',''),owner=r['owner_hash'],plan=p)
    receipt=receipt_join(r,dto['receipt'],q);assert receipt['state']=='Unknown' and receipt['archiveCopied'] is None and receipt['ownedFence'] is receipt['sourceFenced'] is True
    records=io.read_operation(q,receipt,purge['records'],e/'archive-originals',create=create);folder=Path(fixture['folder']);physical=io.physical(folder,q);published_proof(physical,q,records['root'],cut)
    import json
    cut_proof=json.loads((e/'cut-proof.json').read_text());typed_equal(physical,cut_proof['physical']);assert parent_version(cut,folder)==cut_proof['parentVersionAfterPublication']
    from validate_container_recycle_browser import sql as source_sql
    from validate_container_recycle_purge_browser import sql as purge_sql
    typed_equal(source_sql(source['sql']['row']['id']),source['sql']);typed_equal(purge_sql(purge['sql']['row']['id']),purge['sql']);hdd=hdd_observation(source['request']['operationId']);typed_equal(hdd,purge['hdd'])
    audit=audits(r);assert type(audit['cancel']) is int and audit['cancel']==0 and audit['ack']==int(acknowledged) and audit['ackActors']==([r['acknowledged_by']] if acknowledged else [])
    if acknowledged:assert r['acknowledged_by']!=r['actor_subject'] and stored_object(r['acknowledgement_json'])==stored_object(r['receipt_json'])
    return dict(sql=data,request=q,receipt=receipt,records=records,physical=physical,hdd=hdd,audits=audit)


def require_ack(before,after,actor):
    a,b=before['sql']['row'],after['sql']['row'];same_operation(a,b)
    assert a['state']=='Unknown' and b['state']=='Acknowledged' and actor!=a['actor_subject'] and b['acknowledged_by']==actor and b['version']>a['version']
    for key in ('root_state','archive_copied','source_fenced','owned_fence','binding_current','cancel_requested_at_utc'):typed_equal(a[key],b[key])
    for key in ('receipt','records','physical','hdd'):typed_equal(before[key],after[key])
