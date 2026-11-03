"""Read-only guards for missing selected source BEFORE first ACL Apply."""
import ast
import hashlib
import json
from pathlib import Path
import re
import subprocess
import uuid

from validate_container_acl_browser import managed_plan, validate_job_summary
from acl_restore_browser_proof import expected_metadata, unique_json
from acl_history_browser_proof import typed_equal
from validate_container_acl_source_missing import SOURCES as PREVIOUS

CODE='CONTAINER_ACL_RESTORE_SOURCE_CHANGED'
KIND='source-apply-missing'
SEEDS=tuple(dict.fromkeys((*PREVIOUS,'acl_source_apply_missing_proof.py','validate_container_acl_source_apply_missing.py',
    'validate_container_acl_source_apply_missing.mjs','verify_container_acl_source_apply_missing.py','test_acl_source_apply_missing_proof.py')))
FROZEN=('id','actor_subject','login_binding','container_id','instance_id','relative_path','owner_hash','request_hash',
        'command_json','plan_json','plan_hash','restore_source_json','planning_expires_at_utc','ready_expires_at_utc')


def source_closure(repo,revision,*,seeds=None):
    """Freeze the whole local Python/MJS static import closure BEFORE running."""
    assert re.fullmatch('[a-f0-9]{40}',revision)
    prefix=revision+':deploy/validation/'
    available=set(subprocess.check_output(['git','-C',str(repo),'ls-tree','--name-only',revision+':deploy/validation'],text=True).splitlines())
    seen=set(SEEDS if seeds is None else seeds);todo=list(seen)
    while todo:
        name=todo.pop();assert name in available and re.fullmatch(r'[A-Za-z0-9_.-]+\.(py|mjs)',name)
        raw=subprocess.check_output(['git','-C',str(repo),'show',prefix+name])
        children=[]
        if name.endswith('.py'):
            for node in ast.walk(ast.parse(raw)):
                modules=([node.module] if isinstance(node,ast.ImportFrom) and node.module else [a.name for a in node.names] if isinstance(node,ast.Import) else [])
                children.extend(m.split('.')[0]+'.py' for m in modules)
        else:
            children=re.findall(r'''(?:from\s*|import\(\s*)['"]\./([A-Za-z0-9_.-]+\.mjs)['"]''',raw.decode())
        for child in children:
            if child in available and child not in seen:seen.add(child);todo.append(child)
    return {name:hashlib.sha256(subprocess.check_output(['git','-C',str(repo),'show',prefix+name])).hexdigest() for name in sorted(seen)}


def require_pending(data,ready):
    row=data['row'];old=ready['row']
    assert row['state']=='Pending' and row['result_code']=='CONTAINER_ACL_ACCEPTED'
    assert row['execution_job_id'] and row['confirmed_at_utc'] and row['execution_expires_at_utc']
    assert row['root_started_at_utc'] is row['completed_at_utc'] is row['lease_token'] is row['lease_expires_at_utc'] is None
    assert not row['publish_barrier'] and row['acl_confirmed'] is None and row['receipt_json'] is None
    assert row['history_may_exist'] is row['history_verified'] is False
    assert row['history_digest'] is row['published_version'] is None and row['execution_hash']
    assert row['version']==old['version']+1 and data['accepted']==data['confirmed']==1
    assert data['planJob']==ready['planJob'] and data['executionJob']['id']==row['execution_job_id'] and data['executionJob']['state']=='Queued'
    for key in FROZEN:assert row[key]==old[key],key


def summary(row):
    plan=managed_plan(unique_json(row['plan_json']))
    return dict(schemaVersion=1,intentId=row['id'],stage='execution',state='Failed',code=CODE,aclType=plan['command']['aclType'],
        kind=plan['kind'],targetMode=plan['targetMode'],noChange=plan['noChange'],aclConfirmed=False,receiptState='Aborted',bindingCurrent=False,
        historyMayExist=False,historyVerified=False,hasPublishedVersion=False)


def require_failed(data,pending,record):
    row=data['row'];old=pending['row'];plan=managed_plan(unique_json(row['plan_json']))
    assert row['state']==data['executionJob']['state']=='Failed' and row['result_code']==CODE and row['completed_at_utc']
    assert row['root_started_at_utc'] is row['cancel_requested_at_utc'] is row['lease_token'] is row['lease_expires_at_utc'] is None
    assert not row['publish_barrier'] and row['acl_confirmed'] is False
    assert row['history_may_exist'] is row['history_verified'] is False and row['history_digest'] is row['published_version'] is None
    for key in (*FROZEN,'execution_job_id','execution_hash','confirmed_at_utc','execution_expires_at_utc'):assert row[key]==old[key],key
    assert data['planJob']==pending['planJob'] and data['pending']==0 and data['accepted']==data['confirmed']==1
    assert record['operationId']==uuid.UUID(row['execution_job_id']).hex and record['owner']==row['owner_hash'] and record['plan']==plan
    assert record['state']=='Aborted' and record['aclConfirmed'] is False and record['actualIdentity'] is None and record['version']==''
    assert record['historyMayExist'] is record['historyVerified'] is False and record['historyDigest']==''
    receipt=unique_json(row['receipt_json'])
    assert receipt['Id']==row['execution_job_id'] and receipt['State']=='Aborted' and receipt['AclConfirmed'] is False
    assert receipt['BindingCurrent'] is False and receipt['HistoryMayExist'] is receipt['HistoryVerified'] is False and receipt['HistoryDigest']==receipt['Version']==''
    assert receipt['ContainerId']==row['container_id'] and receipt['InstanceId']==row['instance_id']
    assert receipt['Fingerprint']==plan['fingerprint'] and receipt['TargetProof']==plan['targetProof'] and receipt['AclType']==plan['command']['aclType']
    assert receipt['Kind']==plan['kind'] and receipt['TargetMode']==plan['targetMode'] and receipt['NoChange'] is plan['noChange']
    assert re.fullmatch('[A-F0-9]{64}',row['receipt_hash'])
    validate_job_summary(data['executionJob'],summary(row),row['execution_job_id'],'Failed')


def verify_audits(rows,row):
    expected={'plan-accepted':('Accepted',row['id']),'plan-result':('Ready',row['id']),
              'confirmed':('Accepted',row['execution_job_id']),'result':('Failed',row['execution_job_id'])}
    assert len(rows)==len(expected);seen=set()
    for value in rows:
        action=value['action'].removeprefix('containers.files.acl.restore.')
        assert action in expected and action not in seen;seen.add(action)
        assert (value['result'],value['job_id'])==expected[action]
        assert value['actor_subject']==row['actor_subject'] and value['target_id']==row['container_id'] and value['target_type']=='container' and value['trace_id']
        typed_equal(unique_json(value['detail_json']),expected_metadata(row))


def worker_unchanged_except_start(before,after):
    assert before.keys()==after.keys()
    for name,value in before.items():
        actual=after[name]
        if name=='/whaledeck-worker':assert tuple(actual[:3])==tuple(value[:3]) and actual[3]!=value[3]
        else:assert tuple(actual)==tuple(value)
