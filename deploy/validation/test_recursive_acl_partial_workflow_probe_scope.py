"""Partial protection and truthful explicit acknowledgement boundaries only."""
import ast,copy,json
from pathlib import Path
import unittest
from recursive_acl_partial_proof import require_phase,require_ack
from verify_container_recursive_acl_partial import target

ROOT=Path(__file__).resolve().parent


def phase():
    stable=('id','actor_subject','login_binding','container_id','instance_id','relative_path','owner_hash','command_json','request_hash','plan_json','plan_hash','execution_hash','execution_job_id','confirmed_at_utc','execution_expires_at_utc','root_started_at_utc','outcome_digest','history_digest','cancel_requested_at_utc')
    row={k:k for k in stable};receipt=dict(Id='original-execution',State='Partial',ChangedCount=1,Replayed=True,BindingCurrent=False,HistoryAvailable=True)
    row.update(state='Partial',root_state='Partial',publish_barrier=True,completed_at_utc=None,lease_token=None,lease_expires_at_utc=None,history_verified=True,history_may_exist=True,
        result_code='CONTAINER_RECURSIVE_ACL_PARTIAL_REQUIRES_ACKNOWLEDGEMENT',completed_count=1,changed_count=1,aborted_count=0,not_started_count=3,unknown_count=0,
        acknowledged_by=None,acknowledgement_json=None,receipt_json=json.dumps(receipt),receipt_hash='A'*64,version=5)
    return dict(row=row,planJob=dict(state='Succeeded'),executionJob=dict(state='Running',completed_at_utc=None),pending=1,accepted=1,confirmed=1)


def acknowledgement(before):
    after=copy.deepcopy(before);r=after['row'];receipt=json.loads(r['receipt_json']);receipt['Replayed']=False
    r.update(state='Acknowledged',publish_barrier=False,completed_at_utc='now',result_code='CONTAINER_RECURSIVE_ACL_PARTIAL_ACKNOWLEDGED',acknowledged_by='different-admin',acknowledgement_json=json.dumps(receipt),receipt_json=json.dumps(receipt),receipt_hash='B'*64,version=6)
    after['executionJob']=dict(state='Failed',completed_at_utc='now',phase='Acknowledged');after['pending']=0;return after


class RecursiveAclPartialWorkflowScope(unittest.TestCase):
    def test_partial_cannot_finish_job_release_barrier_or_assert_zero_changes(self):
        original=phase();require_phase(original)
        for key,value in [('publish_barrier',False),('completed_at_utc','early'),('root_state','Aborted'),('changed_count',0),('changed_count',True),('unknown_count',1),('history_verified',False),('lease_token','live'),('acknowledged_by','somebody')]:
            bad=copy.deepcopy(original);bad['row'][key]=value
            with self.subTest(key=key),self.assertRaises(AssertionError):require_phase(bad)
        bad=copy.deepcopy(original);bad['executionJob']['state']='Canceled'
        with self.assertRaises(AssertionError):require_phase(bad)

    def test_ack_releases_only_after_different_admin_preserves_real_counts(self):
        before=phase();after=acknowledgement(before);require_ack(before,after,'different-admin')
        for key,value in [('actor_subject','rewritten'),('execution_job_id','new-operation'),('root_started_at_utc','new-start'),('plan_json','new-plan'),('changed_count',0),('publish_barrier',True),('acknowledged_by','original-owner')]:
            bad=copy.deepcopy(after);bad['row'][key]=value
            with self.subTest(key=key),self.assertRaises(AssertionError):require_ack(before,bad,'different-admin')
        bad=copy.deepcopy(after);receipt=json.loads(bad['row']['receipt_json']);receipt['ChangedCount']=0;bad['row']['receipt_json']=json.dumps(receipt);bad['row']['acknowledgement_json']=json.dumps(receipt)
        with self.assertRaises(AssertionError):require_ack(before,bad,'different-admin')
        with self.assertRaises(AssertionError):require_ack(before,after,before['row']['actor_subject'])

    def test_artefacts_and_queries_cannot_select_foreign_paths_or_mutate_sql(self):
        for name in ('../escape','container-recursive-acl-faults-'+'a'*32,'container-recursive-acl-partial-'+'A'*32):
            with self.subTest(name=name),self.assertRaises(AssertionError):target(name)
        for name in ('validate_container_recursive_acl_partial.py','verify_container_recursive_acl_partial.py','recursive_acl_partial_proof.py'):
            text=(ROOT/name).read_text();self.assertNotIn('rmtree(',text);self.assertNotIn('os.kill(',text)
            for node in ast.walk(ast.parse(text)):
                if isinstance(node,ast.Call) and isinstance(node.func,ast.Name) and node.func.id=='query':
                    self.assertIsInstance(node.args[0],ast.Constant);self.assertTrue(node.args[0].value.startswith('SELECT '))

    def test_browser_only_real_controls_with_stdin_and_no_implicit_ack_on_failure(self):
        text=(ROOT/'validate_container_recursive_acl_partial.mjs').read_text()
        for forbidden in ('page.route(', 'context.route(', 'addCookies(', 'storageState:', 'sessionStorage.setItem','localStorage.setItem'):
            self.assertNotIn(forbidden,text)
        for expected in ('loginVisible','logoutFromUserMenu',"'/dev/stdin'","'暂不处置'","'确认处置并释放保护'",'assert.notEqual(profile.subject,f.originalSubject)'):
            self.assertIn(expected,text)
        catch=text[text.index('}catch(error)'):];self.assertNotIn('context.request.post',catch)


if __name__=='__main__':unittest.main()
