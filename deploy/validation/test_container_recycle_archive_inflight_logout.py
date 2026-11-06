"""Constructed evidence/negative contracts only; installed logout is separate."""
import ast,copy,unittest
from pathlib import Path
from archive_inflight_logout_proof import terminal_proof,browser_proof,exit_proof,explicit_cancel_absent
from test_container_recycle_archive_inflight_cancel import sample as canceled_sample


def sample():
    v,f,p,b=canceled_sample();v['row']['cancel_requested_at_utc']=None;v['executionJob']['cancel_requested_at_utc']=None
    receipt=dict(normalUiLogout=True,postStatus=303,providerNavigationObserved=True,bffStatus=401,identityStatus=403,finalLoginPage=True)
    identifier=v['row']['id'];b.update(subject=f['subject'],normalDualLogouts=2,originalSessionReadStatus=401,newSessionSubject=f['subject'],oldReferenceStatuses=[404]*9,logoutReceipts=[receipt,copy.deepcopy(receipt)],mutations=['/api/v1/containers/fixture/files/recycle-archives/'+identifier+'/confirm'])
    return v,f,p,b


class ArchiveLogoutProofTests(unittest.TestCase):
    def test_normal_logout_stops_without_explicit_cancel(self):
        v,f,p,b=sample();proof=terminal_proof(v,f,p,b);self.assertTrue(proof['originalExecutionStoppedByLogout'] and proof['explicitCancelAbsent']);self.assertFalse(proof['archiveCopied'])

    def test_either_job_or_intent_cancel_timestamp_is_refused(self):
        for target in ('row','executionJob'):
            v,f,p,b=sample();v[target]['cancel_requested_at_utc']='explicit'
            with self.subTest(target=target),self.assertRaises(AssertionError):terminal_proof(v,f,p,b)

    def test_any_explicit_cancel_audit_is_refused(self):
        v,_,_,_=sample();explicit_cancel_absent(v['row'],0)
        for count in (1,-1,True,None,'0'):
            with self.subTest(count=count),self.assertRaises(AssertionError):explicit_cancel_absent(v['row'],count)

    def test_both_actual_bff_and_provider_logout_receipts_required(self):
        for key,bad in (('normalUiLogout',False),('postStatus',200),('providerNavigationObserved',False),('bffStatus',200),('identityStatus',200),('finalLoginPage',False)):
            for index in (0,1):
                v,f,p,b=sample();b['logoutReceipts'][index][key]=bad
                with self.subTest(key=key,index=index),self.assertRaises(AssertionError):terminal_proof(v,f,p,b)

    def test_relogin_same_subject_and_nine_old_reference_refusals_required(self):
        for key,bad in (('newSessionSubject','other'),('originalSessionReadStatus',200),('oldReferenceStatuses',[404]*8),('oldReferenceStatuses',[404]*8+[200]),('normalDualLogouts',1)):
            _,_,_,b=sample();b[key]=bad
            with self.subTest(key=key),self.assertRaises(AssertionError):browser_proof(b)
        _,_,_,b=sample();b['mutations'].append(b['mutations'][0].removesuffix('/confirm')+'/cancel')
        with self.assertRaises(AssertionError):browser_proof(b)
        _,_,_,b=sample();b['mutations']=[]
        with self.assertRaises(AssertionError):browser_proof(b)

    def test_uncertain_ready_only_or_copied_result_is_not_safe_logout(self):
        for key,bad in (('archive_copied',True),('archive_copied',None),('root_state','Unknown'),('reservation_json',None),('apply_started_at_utc',None),('publish_barrier',True),('source_fenced',True)):
            v,f,p,b=sample();v['row'][key]=bad
            with self.subTest(key=key),self.assertRaises(AssertionError):terminal_proof(v,f,p,b)

    def test_planning_history_and_actual_nineteen_field_jobs_must_agree(self):
        for which,key,bad in (('planJob','state','Canceled'),('executionJob','archiveCopied',True),('executionJob','sourceIntentId','other'),('executionJob','extra','value')):
            v,f,p,b=sample();v[which]['result_json'][key]=bad
            with self.subTest(which=which,key=key),self.assertRaises(AssertionError):terminal_proof(v,f,p,b)

    def test_original_alive_at_navigation_post_and_bounded_exit_order_required(self):
        v,_,_,_=sample();r=v['row'];a=dict(id=r['id'],executionJobId=r['execution_job_id'],actualNavigationPost=True,path='/api/v1/auth/logout',monotonicNs='200')
        e=dict(elapsedSeconds=1,observedWithOriginalPidfd=True,measurementBeginsBeforeAllowingUiAction=True,actualBffLogoutObservedWhileOriginalAlive=True,beganMonotonicNs=100,actionMonotonicNs=200,exitObservedMonotonicNs=300)
        exit_proof(e,a,r['id'])
        for key,bad in (('elapsedSeconds',20),('actualBffLogoutObservedWhileOriginalAlive',False),('observedWithOriginalPidfd',False),('beganMonotonicNs',250),('exitObservedMonotonicNs',150)):
            with self.subTest(key=key),self.assertRaises(AssertionError):exit_proof({**e,key:bad},a,r['id'])
        with self.assertRaises(AssertionError):exit_proof(e,{**a,'actualNavigationPost':False},r['id'])

    def test_browser_action_is_navigation_logout_and_never_target_cancel(self):
        root=Path(__file__).parent;s=(root/'validate_container_recycle_archive_inflight_logout.mjs').read_text();action=s.split("phase='normal-logout-bound-archive'",1)[1]
        self.assertIn('request.isNavigationRequest()',action);self.assertIn("request.method()==='POST'",action);self.assertIn("url.pathname==='/api/v1/auth/logout'",action);self.assertIn('process.hrtime.bigint()',action)
        self.assertNotIn("name:'请求取消归档恢复'",action);self.assertIn("'/api/v1/files/container-recycle-archives/recovery/'",action)
        for forbidden in ('page.route(','storageState(','tracing.start(','addCookies('):self.assertNotIn(forbidden,s)
        parent=(root/'validate_container_recycle_archive_inflight_logout.py').read_text();self.assertIn("job_id IN (:'intent'::uuid,:'job'::uuid)",parent);self.assertIn('pool.submit(observe_archive_logout_exit,held,began,began_ns,e,20)',parent);self.assertIn("'credentials':dict(username=username,password=password)",parent);self.assertNotIn("'VALIDATION_PASSWORD':password",parent)

    def test_independent_current_reads_precede_exact_cleanup_and_hdd_reopen(self):
        root=Path(__file__).parent;s=(root/'validate_container_recycle_archive_inflight_logout.py').read_text();self.assertLess(s.index('independent(repo,e,True)'),s.index('private.rmdir()'));self.assertLess(s.index('private.rmdir()'),s.index('after_hdd=hdd_observation'));self.assertIn('late=future.result()',s)
        verifier=(root/'verify_container_recycle_archive_inflight_logout.py').read_text();calls=[ast.unparse(n.func) for n in ast.walk(ast.parse(verifier)) if isinstance(n,ast.Call)]
        for forbidden in ('cleanup_new_copy','requests.post','os.unlink','os.rmdir','subprocess.Popen'):self.assertNotIn(forbidden,calls)
        for expected in ('reservation_proof','cut_proof','exit_proof','record_proof','archive_io.read_operation','archive_io.physical','hdd_observation','source_closure'):self.assertIn(expected,calls)


if __name__=='__main__':unittest.main()
