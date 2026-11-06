"""Strict Unknown/null attribution and explicit different-admin acknowledgement.

Projection helpers are not installed acceptance. Current Root/HDD and physical
originals must be acquired independently by the owning workflow.
"""
import json,uuid
from acl_history_browser_proof import typed_equal
from recycle_restore_browser_proof import execution,managed_plan,managed_receipt,job_summary,stored_object


def facts(value,data,fixture,source):
    r=data['row'];ack=value['state']=='Acknowledged';assert r['id']==value['id'] and r['state']==value['state'] in ('Unknown','Acknowledged')
    assert r['actor_subject']==fixture['subject'] and (r['container_id'],r['instance_id'],r['relative_path'])==(fixture['containerId'],fixture['instanceId'],'work/recycle-rpc.bin')
    assert r['root_started_at_utc'] and r['cancel_requested_at_utc'] is None and r['lease_token'] is r['lease_expires_at_utc'] is None
    assert r['publish_barrier'] is (not ack) and bool(r['completed_at_utc']) is ack and bool(r['acknowledged_by']) is ack and bool(r['acknowledgement_json']) is ack
    assert r['root_state']=='Unknown' and r['binding_current'] is False and r['source_state'] is r['source_retained'] is r['source_restored'] is r['restored_by_this_operation'] is None
    assert r['history_verified'] is r['history_available'] is True and value['confirmation'] is None
    assert all(type(data[k]) is int for k in ('pending','accepted','confirmed','canceled'))
    assert data['pending']==(0 if ack else 1) and data['accepted']==data['confirmed']==1 and data['canceled']==0
    assert data['planJob']['id']==r['id'] and data['planJob']['state']=='Succeeded'
    op=execution(r['id']);src=execution(r['source_intent_id'],source=True)
    assert r['execution_job_id']==value['executionJobId']==data['executionJob']['id']==op
    assert r['source_intent_id']==value['sourceIntentId']==source['sql']['row']['id'] and src==value['sourceOperationId']==source['sql']['row']['execution_job_id']
    job=data['executionJob'];assert job['state']==('Failed' if ack else 'Running') and job['phase']==('Acknowledged' if ack else 'AwaitingContainerRecycleRestoreReceipt') and bool(job['completed_at_utc']) is ack and job['cancel_requested_at_utc'] is None
    code='CONTAINER_RECYCLE_RESTORE_UNKNOWN_ACKNOWLEDGED' if ack else 'CONTAINER_RECYCLE_RESTORE_RESULT_UNKNOWN';assert r['result_code']==job['error_code']==code
    if ack:assert r['acknowledged_by']!=r['actor_subject']
    plan=managed_plan(stored_object(r['plan_json']));typed_equal(plan['command']['source'],source['request'])
    command=plan['command']['source']['plan']['command'];assert (command['containerId'],command['instanceId'],command['path'])==(r['container_id'],r['instance_id'],r['relative_path'])
    q=dict(operationId=uuid.UUID(op).hex,owner=r['owner_hash'],plan=plan);receipt=managed_receipt(value['receipt'],q)
    assert receipt['state']=='Unknown' and receipt['sourceState'] is receipt['sourceRetained'] is receipt['sourceRestored'] is receipt['restoredByThisOperation'] is None
    assert r['history_digest']==receipt['historyDigest']==plan['command']['historyDigest']==value['sourceHistoryDigest']
    stored=stored_object(r['receipt_json']);assert set(stored)=={k[0].upper()+k[1:] for k in value['receipt']};stored={k[0].lower()+k[1:]:v for k,v in stored.items()}
    # A natural read-only retry can change replay/version, never the facts.
    typed_equal({k:v for k,v in stored.items() if k!='replayed'},{k:v for k,v in value['receipt'].items() if k!='replayed'})
    job_summary(stored_object(data['planJob']['result_json']),r,execution_stage=False);job_summary(stored_object(job['result_json']),r,execution_stage=True)
    return q,receipt


def namespace(fixture,records):
    from pathlib import Path
    from container_recycle_agent_io import physical
    assert records['root']['state']=='Unknown' and records['root']['sourceOutcome'] is None and records['sourceRoot']['state']=='Restored'
    value=physical(Path(fixture['folder']),records['sourceRoot']['operationId']);assert value['retained'] is None
    typed_equal(value['source'],fixture['returned']);typed_equal(value['source']['identity'],records['sourceRoot']['actualIdentity']);typed_equal(value['privateIdentity'],records['sourceRoot']['privateIdentity'])
    return value
