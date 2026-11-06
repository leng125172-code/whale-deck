"""Read-only Unknown/ACK evidence for the owned post-unlink logout purge execution."""
import json,uuid
from pathlib import Path
from validate_container_recycle_purge_browser import sql,hdd_observation
from validate_metric_continuity import query
from recycle_purge_browser_proof import execution,managed_plan,managed_receipt,job_summary,stored_object
from container_recycle_purge_restart_proof import same_operation
from acl_history_browser_proof import typed_equal
import container_recycle_agent_io as source_io
import container_recycle_purge_agent_io as purge_io


def row_proof(data,fixture,acknowledged):
    r=data['row'];assert type(acknowledged) is bool
    assert r['actor_subject']==fixture['subject'] and (r['container_id'],r['instance_id'],r['relative_path'])==(fixture['containerId'],fixture['instanceId'],'work/recycle-rpc.bin')
    assert r['state']==('Acknowledged' if acknowledged else 'Unknown') and r['result_code']==('CONTAINER_RECYCLE_PURGE_UNKNOWN_ACKNOWLEDGED' if acknowledged else 'CONTAINER_RECYCLE_PURGE_RESULT_UNKNOWN')
    assert r['execution_job_id']==execution(r['id']) and r['root_started_at_utc'] and r['reservation_json'] and r['apply_started_at_utc'] and r['cancel_requested_at_utc'] is None
    assert r['root_state']=='Unknown' and r['purge_applied'] is None and r['source_fenced'] is r['owned_fence'] is True
    assert r['publish_barrier'] is (not acknowledged) and bool(r['completed_at_utc']) is acknowledged
    assert r['lease_token'] is r['lease_expires_at_utc'] is None and bool(r['acknowledged_by']) is bool(r['acknowledgement_json']) is acknowledged
    assert type(data['pending']) is int and data['pending']==(0 if acknowledged else 1) and data['accepted']==data['confirmed']==1 and data['canceled']==0
    job=data['executionJob'];assert job['state']==('Failed' if acknowledged else 'Running') and job['phase']==('Acknowledged' if acknowledged else 'AwaitingContainerRecyclePurgeReceipt')
    assert bool(job['completed_at_utc']) is acknowledged and job['cancel_requested_at_utc'] is None and data['planJob']['state']=='Succeeded'
    job_summary(stored_object(job['result_json']),r,execution_stage=True);job_summary(stored_object(data['planJob']['result_json']),r,execution_stage=False)


def audits(row):
    return query("SELECT json_build_object('cancel',(SELECT count(*) FROM audit_events WHERE job_id IN (:'intent'::uuid,:'job'::uuid) AND action='containers.files.recycle-purge.cancel-requested'),'ack',(SELECT count(*) FROM audit_events WHERE job_id=:'job'::uuid AND action='containers.files.recycle-purge.unknown-acknowledged'),'ackActors',(SELECT coalesce(json_agg(actor_subject),'[]'::json) FROM audit_events WHERE job_id=:'job'::uuid AND action='containers.files.recycle-purge.unknown-acknowledged'))",intent=row['id'],job=row['execution_job_id'])


def prove(dto,fixture,source,e,*,create=False,acknowledged=False,live=True):
    data=sql(dto['id']);row_proof(data,fixture,acknowledged);r=data['row'];assert dto['state']==r['state'] and dto['executionJobId']==r['execution_job_id'] and dto['purgeApplied'] is None
    p=managed_plan(stored_object(r['plan_json']));typed_equal(p['command']['source'],source['request']);q=dict(operationId=uuid.UUID(r['execution_job_id']).hex,owner=r['owner_hash'],plan=p)
    receipt=managed_receipt(dto['receipt'],q);assert receipt['state']=='Unknown' and receipt['purgeApplied'] is None
    records=purge_io.read_operation(q,receipt,source['records']['root'],source['records']['history'],e/'purge-originals',create=create)
    assert records['root']['state']=='Unknown' and records['root']['pendingIdentity'] is not None and records['root']['unlinkConfirmed'] is False
    assert records['marker']['state']=='Unknown' and records['sourceRoot']['state']=='Purging'
    typed_equal(records['history'],source['records']['history']);typed_equal(records['data'],source['records']['data'])
    physical=purge_io.physical(Path(fixture['folder']),q) if live else None
    if live:assert physical['source'] is physical['retained'] is physical['pending'] is None
    from validate_container_recycle_browser import sql as source_sql
    typed_equal(source_sql(r['source_intent_id']),source['sql']);hdd=hdd_observation(source['request']['operationId']);typed_equal(hdd,source['hdd'])
    audit=audits(r);assert audit['cancel']==0 and audit['ack']==int(acknowledged)
    assert audit['ackActors']==([r['acknowledged_by']] if acknowledged else [])
    if acknowledged:assert r['acknowledged_by']!=r['actor_subject'] and stored_object(r['acknowledgement_json'])==stored_object(r['receipt_json'])
    return dict(sql=data,request=q,receipt=receipt,records=records,physical=physical,hdd=hdd,audits=audit)


def require_ack(before,after,actor):
    old,new=before['sql']['row'],after['sql']['row'];same_operation(old,new)
    assert old['state']=='Unknown' and new['state']=='Acknowledged' and actor!=old['actor_subject'] and new['acknowledged_by']==actor and new['version']>old['version']
    for key in ('root_state','purge_applied','source_fenced','owned_fence','cancel_requested_at_utc','reservation_json','apply_started_at_utc'):typed_equal(old[key],new[key])
    for key in ('receipt','records','physical','hdd'):typed_equal(before[key],after[key])


def preserved(before,after,source_op,purge_op):
    allowed=dict(recycleRoot={source_op+x for x in ('.json','.lock','.purge.json')},recycleHdd={source_op+x for x in ('.json','.data')},purgeRoot={purge_op+x for x in ('.json','.lock')})
    for key in before:
        if key not in allowed:typed_equal(before[key],after[key])
        else:
            assert all(after[key].get(n)==v for n,v in before[key].items());assert set(after[key])-set(before[key])==allowed[key]
