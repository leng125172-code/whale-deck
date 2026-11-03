"""Proof guards for source loss after registration and before asynchronous plan."""
from acl_source_apply_missing_proof import source_closure,SEEDS as PREVIOUS,worker_unchanged_except_start
from acl_restore_browser_proof import unique_json,source_json,expected_metadata
from acl_history_browser_proof import typed_equal
from validate_container_acl_browser import validate_job_summary

KIND='source-plan-missing'
CODE='CONTAINER_ACL_RESTORE_SOURCE_CHANGED'
SEEDS=(*PREVIOUS,'acl_source_plan_missing_proof.py','validate_container_acl_source_plan_missing.py',
       'validate_container_acl_source_plan_missing.mjs','verify_container_acl_source_plan_missing.py','test_acl_source_plan_missing_proof.py')
FROZEN=('id','idempotency_key','trace_id','actor_subject','login_binding','container_id','instance_id','relative_path','owner_hash','request_hash',
        'command_json','restore_source_json','created_at_utc','planning_expires_at_utc')
EMPTY=('plan_json','plan_hash','ready_expires_at_utc','execution_job_id','confirmed_at_utc','execution_expires_at_utc','execution_hash',
       'root_started_at_utc','receipt_json','receipt_hash','acl_confirmed','history_digest','published_version','cancel_requested_at_utc')


def manifest(repo,revision):return source_closure(repo,revision,seeds=SEEDS)


def no_execution_facts(data):
    row=data['row']
    for key in EMPTY:assert row[key] is None,key
    assert data['executionJob'] is None and data['confirmed']==0 and data['accepted']==1
    assert row['lease_token'] is row['lease_expires_at_utc'] is None and not row['publish_barrier']
    assert row['history_may_exist'] is row['history_verified'] is False


def require_queued(data,baseline):
    no_execution_facts(data);row=data['row']
    assert row['id']!=baseline['captureIntentId'] and row['state']==data['planJob']['state']=='Queued' and row['version']==0
    assert row['completed_at_utc'] is row['result_code'] is None
    assert data['pending']==1
    assert row['actor_subject']==baseline['subject'] and row['container_id']==baseline['containerId'] and row['instance_id']==baseline['instanceId']
    assert row['relative_path']=='work/acl-file.bin' and data['planJob']['id']==row['id']
    assert data['planJob']['job_type']=='containers.files.acl-plan' and data['planJob']['result_json'] is None
    typed_equal(unique_json(row['restore_source_json']),source_json(baseline['restoreSource']))
    command=unique_json(row['command_json'])
    source=source_json(baseline['restoreSource'])
    typed_equal(command['Location'],source['Location']);typed_equal(command['Entries'],source['AccessAcl'])
    assert command['AclType']=='access' and command['ExpectedVersion']


def summary(identifier):
    return dict(schemaVersion=1,intentId=identifier,stage='planning',state='Failed',code=CODE,aclType='access',
        kind=None,targetMode=None,noChange=None,aclConfirmed=None,receiptState=None,bindingCurrent=None,
        historyMayExist=False,historyVerified=False,hasPublishedVersion=False)


def require_failed(data,queued):
    no_execution_facts(data);row=data['row'];old=queued['row']
    assert row['state']==data['planJob']['state']=='Failed' and row['result_code']==CODE and row['completed_at_utc']
    assert data['pending']==0 and row['version']>old['version']
    for key in FROZEN:assert row[key]==old[key],key
    validate_job_summary(data['planJob'],summary(row['id']),row['id'],'Failed')


def verify_audits(rows,row):
    expected={'plan-accepted':'Accepted','plan-result':'Failed'}
    assert len(rows)==len(expected);seen=set()
    for value in rows:
        action=value['action'].removeprefix('containers.files.acl.restore.')
        assert action in expected and action not in seen;seen.add(action)
        assert value['job_id']==row['id'] and value['result']==expected[action]
        assert value['actor_subject']==row['actor_subject'] and value['target_id']==row['container_id'] and value['target_type']=='container' and value['trace_id']
        typed_equal(unique_json(value['detail_json']),expected_metadata(row))
