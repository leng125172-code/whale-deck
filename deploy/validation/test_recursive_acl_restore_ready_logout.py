"""Synthetic expiry/authority projections and negative tests; never real SQL/UI."""
import ast
import copy
import json
from pathlib import Path
import unittest
from unittest.mock import Mock
from test_recursive_acl_restore_agent_probe_scope import synthetic,wire
from test_recursive_acl_restore_business_proof import managed
from recursive_acl_restore_business_proof import metadata
from recursive_acl_restore_ready_logout import observation,expired_proof,samples_proof,wait_signal,timestamp,SOURCES
from verify_recursive_acl_restore_ready_logout import verify

ROOT=Path(__file__).resolve().parent
NULLS=('root_started_at_utc','cancel_requested_at_utc','confirmed_at_utc','execution_expires_at_utc','execution_hash','root_state','receipt_json','receipt_hash','history_digest','completed_count','changed_count','aborted_count','not_started_count','unknown_count','outcome_digest','binding_current','history_available')

def fixture():
    plan=synthetic();counts=dict(entryCount=26,fileCount=25,directoryCount=1,changeCount=26);plan.update(counts);plan['restoreSource'].update(counts)
    row=dict(id='11111111-1111-4111-8111-111111111111',idempotency_key='22222222-2222-4222-8222-222222222222',actor_subject='synthetic-admin',container_id=plan['command']['containerId'],instance_id=plan['command']['instanceId'],relative_path='work/recursive-tree',state='Ready',version=2,ready_expires_at_utc='2026-11-05T00:05:00Z',completed_at_utc=None,lease_token=None,lease_expires_at_utc=None,next_attempt_at_utc='2026-11-05T00:05:00Z',result_code='CONTAINER_RECURSIVE_ACL_RESTORE_READY',execution_job_id=None,history_may_exist=False,history_verified=False,publish_barrier=False,plan_json=json.dumps(managed(wire(plan))))
    row.update(dict.fromkeys(NULLS))
    initial=dict(row=row,confirmed=0,executionJob=None,pending=0,accepted=1,events=2,planJob=dict(state='Succeeded',job_type='containers.files.recursive-acl-restore-plan',request_json=metadata(row)),audits=[])
    final=copy.deepcopy(initial);final['row'].update(state='Expired',version=4,completed_at_utc='2026-11-05T00:05:01Z',result_code='CONTAINER_RECURSIVE_ACL_RESTORE_PLAN_EXPIRED')
    final['audits']=[dict(id=str(n),actor_subject=row['actor_subject'],target_type='container',target_id=row['container_id'],job_id=row['id'],trace_id='synthetic-trace',action='containers.files.recursive-acl-restore.'+action,result=state,detail_json=metadata(row)) for n,(action,state) in enumerate((('plan-accepted','Accepted'),('plan-result','Ready'),('plan-result','Expired')))]
    src=plan['restoreSource'];source=dict(request=dict(operationId=src['captureId'],plan=dict(command=dict(instanceId=src['sourceInstanceId']),manifestDigest=src['manifestDigest'])))
    meta=dict(historyDigest=src['historyDigest'],createdAtUnixMs=src['createdAtUnixMs'])
    return initial,final,dict(containerId=row['container_id'],instanceId=row['instance_id']),source,meta

class ReadyLogoutProofTests(unittest.TestCase):
    def test_ready_then_natural_expiry_preserves_planning_success_and_no_execution(self):
        a,z,b,r,m=fixture();expired_proof(z,a,b,r,m);observation(a,0)
        self.assertEqual(z['planJob'],a['planJob'])
    def test_early_expiry_or_cancel_does_not_count_as_natural_deadline(self):
        for change in (lambda z:z['row'].update(completed_at_utc='2026-11-05T00:04:59Z'),lambda z:z['row'].update(state='Canceled'),lambda z:z['row'].update(cancel_requested_at_utc='2026-11-05T00:04:00Z')):
            a,z,b,r,m=fixture();change(z)
            with self.subTest(change=change),self.assertRaises(AssertionError):expired_proof(z,a,b,r,m)
    def test_any_execution_receipt_history_or_barrier_cannot_hide_as_zero_writes(self):
        for key in NULLS:
            a,z,b,r,m=fixture();z['row'][key]='unexpected'
            with self.subTest(key=key),self.assertRaises(AssertionError):observation(z,0)
        for key in ('history_may_exist','history_verified','publish_barrier'):
            a,z,b,r,m=fixture();z['row'][key]=True
            with self.subTest(key=key),self.assertRaises(AssertionError):observation(z,0)
    def test_same_actor_different_plan_or_source_is_rejected(self):
        for key in ('actor_subject','container_id','instance_id','relative_path','plan_json'):
            a,z,b,r,m=fixture();z['row'][key]='changed'
            with self.subTest(key=key),self.assertRaises(AssertionError):expired_proof(z,a,b,r,m)
    def test_duplicate_missing_or_confirmed_audit_is_rejected(self):
        for change in (lambda z:z['audits'].pop(),lambda z:z['audits'].append(copy.deepcopy(z['audits'][0])),lambda z:z['audits'][-1].update(action='containers.files.recursive-acl-restore.confirmed'),lambda z:z['audits'][-1].update(detail_json={'private':'path'})):
            a,z,b,r,m=fixture();change(z)
            with self.subTest(change=change),self.assertRaises(AssertionError):expired_proof(z,a,b,r,m)
    def test_job_success_must_not_be_rewritten_to_fake_a_cancel(self):
        a,z,b,r,m=fixture();z['planJob']['state']='Canceled'
        with self.assertRaises(AssertionError):expired_proof(z,a,b,r,m)
    def test_natural_samples_require_ready_before_deadline_and_expired_at_end(self):
        a,z,b,r,m=fixture();first=observation(a,0);last=observation(z,60)
        first['observedUnixMs']=int(timestamp(a['row']['ready_expires_at_utc'])*1000)-60000;last['observedUnixMs']=first['observedUnixMs']+60000;samples_proof([first,last],a,z)
        for change in (lambda s:s[0].update(state='Expired'),lambda s:s[1].update(observedUnixMs=s[0]['observedUnixMs']),lambda s:s[1].update(elapsedMs=0),lambda s:s[1].update(confirmed=False)):
            values=copy.deepcopy([first,last]);change(values)
            with self.subTest(change=change),self.assertRaises(AssertionError):samples_proof(values,a,z)
    def test_synthetic_boolean_counts_and_naive_clock_are_rejected(self):
        a,_,_,_,_=fixture();a['confirmed']=False
        with self.assertRaises(AssertionError):observation(a,0)
        with self.assertRaises(AssertionError):timestamp('2026-11-05T00:05:00')
    def test_artifact_paths_and_signal_labels_are_bounded_before_io(self):
        for name in ('../escape','recursive-acl-restore-paging-'+'a'*32,'recursive-acl-restore-ready-logout-'+'A'*32):
            with self.subTest(name=name),self.assertRaises(AssertionError):verify(name)
        with self.assertRaises(AssertionError):wait_signal(Mock(),Path('/none'),'arbitrary-file')
    def test_real_ui_logout_relogin_without_cancel_or_confirm_clicks_and_no_secret_export(self):
        s=(ROOT/'validate_recursive_acl_restore_ready_logout.mjs').read_text()
        for marker in ('logoutFromUserMenu','same-subject-normal-relogin','hintsAbsentAfterRelogin','refusedConfirmation(context,404)','natural-ready-expiry','confirmation:null'):
            self.assertIn(marker,s)
        for marker in ('.addCookies(','.route(','sessionStorage.setItem',"name:'确认递归ACL恢复',exact:true}).click()","name:'确认请求取消',exact:true}).click()"):
            self.assertNotIn(marker,s)
        self.assertTrue(all((ROOT/n).is_file() for n in SOURCES))
    def test_sql_observation_is_select_only_and_physical_claims_are_distinct(self):
        for filename in ('recursive_acl_restore_ready_logout.py','verify_recursive_acl_restore_ready_logout.py'):
            s=(ROOT/filename).read_text()
            for node in ast.walk(ast.parse(s)):
                if isinstance(node,ast.Call) and isinstance(node.func,ast.Name) and node.func.id=='query':self.assertTrue(node.args[0].value.lstrip().upper().startswith('SELECT '))
            self.assertNotIn('agent-capability.key',s)
        s=(ROOT/'verify_recursive_acl_restore_ready_logout.py').read_text();self.assertIn('physicalTreeReread=live',s);self.assertIn('sourceBytesRereadAfterCleanup=False',s)

if __name__=='__main__':unittest.main()
