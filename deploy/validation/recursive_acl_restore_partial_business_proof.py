"""Read-only Partial/ACK SQL, original API receipt and Jobs binding.

MAC columns are format-checked only, never verified with secret keys here.
Native source/Root/HDD/physical and real identity proofs remain mandatory.
"""
from collections import Counter
import re,uuid
from acl_history_browser_proof import typed_equal
from recursive_acl_restore_business_proof import managed_plan,managed_receipt,json_projection,metadata
from recursive_acl_restore_job_results_proof import projection,job_proof,ready_job_proof,COUNTS
from recursive_acl_restore_ack_job_proof import acknowledgement_facts
from validate_container_recursive_acl_restore_browser import expected_execution

PREFIX='CONTAINER_RECURSIVE_ACL_RESTORE_'
STABLE=('id','idempotency_key','ready_expires_at_utc','actor_subject','login_binding','container_id','instance_id','relative_path','owner_hash','command_json','request_hash','plan_json','plan_hash','execution_hash','execution_job_id','confirmed_at_utc','execution_expires_at_utc','root_started_at_utc','root_state','completed_count','changed_count','aborted_count','not_started_count','unknown_count','outcome_digest','history_digest','history_verified','history_may_exist','cancel_requested_at_utc')

def audits(data,*,resolver=None):
    row=data['row'];pid=row['id'];eid=row['execution_job_id'];actor=row['actor_subject']
    wanted=Counter({('plan-accepted','Accepted',pid,actor):1,('plan-result','Ready',pid,actor):1,('confirmed','Accepted',eid,actor):1,('cancel-requested','Accepted',eid,actor):1,('result','Partial',eid,actor):1})
    if resolver is not None:wanted[('partial-acknowledged','Partial',eid,resolver)]+=1
    seen=Counter();ids=set()
    for a in data['audits']:
        assert str(uuid.UUID(a['id']))==a['id'] and a['id'] not in ids;ids.add(a['id'])
        assert a['target_type']=='container' and a['target_id']==row['container_id'] and type(a['trace_id']) is str and 0<len(a['trace_id'])<=128
        prefix='containers.files.recursive-acl-restore.';assert a['action'].startswith(prefix)
        seen[(a['action'].removeprefix(prefix),a['result'],a['job_id'],a['actor_subject'])]+=1;typed_equal(json_projection(a['detail_json']),metadata(row))
    assert seen==wanted

def phase(data,*,ack=False):
    row=data['row'];job=data['executionJob'];assert type(ack) is bool
    assert row['state']==('Acknowledged' if ack else 'Partial') and row['root_state']=='Partial'
    assert row['result_code']==PREFIX+('PARTIAL_ACKNOWLEDGED' if ack else 'PARTIAL_REQUIRES_ACKNOWLEDGEMENT')
    assert row['publish_barrier'] is (not ack) and bool(row['completed_at_utc']) is ack and row['lease_token'] is row['lease_expires_at_utc'] is None
    assert row['confirmed_at_utc'] and row['root_started_at_utc'] and row['cancel_requested_at_utc']
    assert row['history_may_exist'] is row['history_verified'] is True
    assert job['state']==('Failed' if ack else 'Running') and job['phase']==('Acknowledged' if ack else 'AwaitingContainerRecursiveAclRestoreResolution')
    assert bool(job['completed_at_utc']) is ack and job['cancel_requested_at_utc']==row['cancel_requested_at_utc']
    if ack:
        assert type(row['completed_at_utc']) is type(job['completed_at_utc']) is str
    else:assert row['completed_at_utc'] is job['completed_at_utc'] is None
    assert type(row['version']) is int and row['version']>0
    for key in ('pending','accepted','confirmed','events'):assert type(data[key]) is int and data[key]>=0
    assert data['accepted']==data['confirmed']==1 and data['events']>=4
    assert data['pending']==0 if ack else data['pending']>0
    for key in ('login_binding','owner_hash','request_hash','plan_hash','execution_hash','receipt_hash'):assert type(row[key]) is str and re.fullmatch('[A-F0-9]{64}',row[key])
    plan=managed_plan(row['plan_json']);assert [plan[k] for k in ('entryCount','fileCount','directoryCount','changeCount')]==[26,25,1,26]
    assert plan['restoreSource']['changedAclType']=='access'
    receipt=managed_receipt(row['receipt_json'])
    assert receipt['state']=='Partial' and receipt['operationId']==uuid.UUID(row['execution_job_id']).hex and row['execution_job_id']==expected_execution(row['id'])
    for key in (*COUNTS,'outcomeDigest','historyMayExist','historyVerified','historyDigest','historyAvailable','bindingCurrent'):
        column=re.sub(r'(?<!^)(?=[A-Z])','_',key).lower();typed_equal(receipt[key],row[column])
    assert [receipt[k] for k in COUNTS]==[1,1,0,25,0]
    assert receipt['bindingCurrent'] is False
    assert receipt['containerId']==row['container_id'] and receipt['instanceId']==row['instance_id'] and receipt['fingerprint']==plan['fingerprint'] and receipt['targetProof']==plan['targetProof'] and receipt['aclType']=='access'
    for label,jid,kind in (('planJob',row['id'],'containers.files.recursive-acl-restore-plan'),('executionJob',row['execution_job_id'],'containers.files.recursive-acl-restore')):
        j=data[label];assert j['id']==jid and j['job_type']==kind and j['actor_subject']==row['actor_subject'];typed_equal(json_projection(j['request_json']),metadata(row))
    assert data['planJob']['state']=='Succeeded' and data['planJob']['idempotency_key']==row['idempotency_key'] and job['idempotency_key']==row['execution_job_id']
    return dict(operationId=receipt['operationId'],owner=row['owner_hash'],plan=plan),receipt

def require_partial(data,ready_signal,initial,baseline,source_summary,api_result,job_observation):
    ready_job_proof(ready_signal,initial);row=data['row'];old=initial['row'];subject=ready_signal['subject']
    assert row['actor_subject']==old['actor_subject']==subject and row['container_id']==baseline['containerId'] and row['instance_id']==baseline['instanceId'] and row['relative_path']=='work/recursive-tree'
    for key in ('id','idempotency_key','login_binding','owner_hash','command_json','request_hash','plan_json','plan_hash','ready_expires_at_utc'):typed_equal(row[key],old[key])
    typed_equal(data['planJob'],initial['planJob']);request,receipt=phase(data);audits(data)
    assert row['acknowledged_by'] is row['acknowledgement_json'] is None
    typed_equal(request['plan']['restoreSource'],source_summary)
    assert api_result['id']==api_result['planningJobId']==row['id'] and api_result['executionJobId']==row['execution_job_id'] and api_result['state']=='Partial' and api_result['resultCode']==row['result_code']
    assert api_result['confirmation'] is None and type(api_result['version']) is int and api_result['version']==row['version']
    typed_equal(api_result['command'],ready_signal['ready']['command']);typed_equal(api_result['restoreSource'],source_summary)
    for key in ('entryCount','fileCount','directoryCount','changeCount','manifestDigest'):typed_equal(api_result[key],request['plan'][key])
    typed_equal([api_result[k] for k in ('rootUid','rootGid','rootMode','executorUid','executorGid')],[*request['plan']['rootIdentity'][3:5],request['plan']['rootIdentity'][2],request['plan']['uid'],request['plan']['gid']])
    typed_equal(managed_receipt(api_result['receipt'],api=True),receipt)
    job_proof(job_observation,data['executionJob'],projection(row,request['plan'],execution=True),prefix='job-fault-partial-execution')
    return request,receipt

def require_acknowledged(before,after,resolver,api_result,job_observation):
    phase(before);request,receipt=phase(after,ack=True);old=before['row'];row=after['row']
    assert type(resolver) is str and resolver and resolver!=old['actor_subject'] and row['acknowledged_by']==resolver and row['version']>old['version']
    for key in STABLE:typed_equal(row[key],old[key])
    typed_equal(after['planJob'],before['planJob'])
    prior=managed_receipt(old['receipt_json']);facts=lambda r:{k:v for k,v in r.items() if k not in ('replayed','bindingCurrent','historyAvailable')}
    typed_equal(facts(prior),facts(receipt));typed_equal(managed_receipt(row['acknowledgement_json']),receipt)
    audits(after,resolver=resolver)
    assert api_result['id']==row['id'] and api_result['state']=='Acknowledged' and api_result['executionJobId']==row['execution_job_id'] and api_result['resultCode']==row['result_code']
    assert api_result['planningJobId']==row['id'] and api_result['confirmation'] is None and type(api_result['version']) is int and api_result['version']==row['version']
    typed_equal(api_result['restoreSource'],request['plan']['restoreSource'])
    for key in ('entryCount','fileCount','directoryCount','changeCount','manifestDigest'):typed_equal(api_result[key],request['plan'][key])
    typed_equal([api_result[k] for k in ('rootUid','rootGid','rootMode','executorUid','executorGid')],[*request['plan']['rootIdentity'][3:5],request['plan']['rootIdentity'][2],request['plan']['uid'],request['plan']['gid']])
    typed_equal(api_result['command'],dict(location={k:request['plan']['command'][k] for k in ('containerId','instanceId','path')},**{k:request['plan']['command'][k] for k in ('captureId','expectedHistoryDigest','expectedVersion')}))
    typed_equal(managed_receipt(api_result['receipt'],api=True),receipt)
    summary=projection(row,request['plan'],execution=True)
    acknowledgement_facts(projection(old,request['plan'],execution=True),summary)
    job_proof(job_observation,after['executionJob'],summary,prefix='job-fault-acknowledged-execution')
    return request,receipt
