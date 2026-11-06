"""Constructed SQL/UI evidence only; real logout acceptance is a separate gate."""
import copy,unittest
from pathlib import Path
from test_container_recycle_purge_inflight_cancel import sample as canceled_sample
from validate_container_recycle_purge_inflight_logout import terminal_proof,browser_proof,explicit_cancel_absent


def sample():
    v,b,f=canceled_sample();v['row']['cancel_requested_at_utc']=None
    v['executionJob']['cancel_requested_at_utc']=None
    receipt=dict(normalUiLogout=True,postStatus=303,providerNavigationObserved=True,bffStatus=401,identityStatus=403,finalLoginPage=True)
    b.update(cancellations=0,normalDualLogout=True,reloginOriginalReferencesDenied=3,newSessionIdStatus=404,newSessionKeyStatus=404,newSourceIdStatus=404,
        originalSessionReadStatus=401,newSessionSubject=b['subject'],logoutReceipts=[receipt,copy.deepcopy(receipt)])
    return v,b,f


class InflightLogoutProofTests(unittest.TestCase):
    def test_canceled_outcome_can_be_due_to_logout_with_no_cancel_request(self):
        v,b,f=sample();p=terminal_proof(v,b,f)
        self.assertTrue(p['originalExecutionStoppedByLogout'] and p['explicitCancelAbsent'])
        self.assertTrue(p['durableApplyMarkerIsNotDeletion'])

    def test_either_intent_or_job_cancel_timestamp_is_rejected(self):
        for target in ('row','executionJob'):
            v,b,f=sample();v[target]['cancel_requested_at_utc']='explicit cancel'
            with self.subTest(target=target),self.assertRaises(AssertionError):terminal_proof(v,b,f)

    def test_any_explicit_cancel_audit_or_non_integer_count_is_rejected(self):
        v,_,_=sample();explicit_cancel_absent(v['row'],0)
        for count in (1,-1,True,None,'0'):
            with self.subTest(count=count),self.assertRaises(AssertionError):explicit_cancel_absent(v['row'],count)

    def test_both_bff_and_provider_sessions_must_be_cleared(self):
        for field,bad in [('normalUiLogout',False),('providerNavigationObserved',False),('bffStatus',200),('identityStatus',200),('postStatus',200),('finalLoginPage',False)]:
            for index in (0,1):
                v,b,f=sample();b['logoutReceipts'][index][field]=bad
                with self.subTest(field=field,index=index),self.assertRaises(AssertionError):terminal_proof(v,b,f)

    def test_new_same_subject_login_cannot_inherit_old_references(self):
        for key,bad in [('newSessionIdStatus',200),('newSessionKeyStatus',200),('newSourceIdStatus',200),('newSessionSubject','different'),('originalSessionReadStatus',200),('cancellations',1),('reloginOriginalReferencesDenied',0)]:
            _,b,_=sample();b[key]=bad
            with self.subTest(key=key),self.assertRaises(AssertionError):browser_proof(b)

    def test_unknown_or_deleted_result_does_not_pass_predelete_logout(self):
        for key,bad in [('purge_applied',True),('purge_applied',None),('root_state','Unknown'),('reservation_json',None),('apply_started_at_utc',None),('publish_barrier',True)]:
            v,b,f=sample();v['row'][key]=bad
            with self.subTest(key=key),self.assertRaises(AssertionError):terminal_proof(v,b,f)

    def test_source_binds_logout_navigation_and_queries_current_admin_metadata(self):
        root=Path(__file__).parent;s=(root/'validate_container_recycle_purge_inflight_logout.mjs').read_text()
        self.assertIn('request.isNavigationRequest()',s)
        self.assertIn("request.method()==='POST'",s)
        self.assertIn("url.pathname==='/api/v1/auth/logout'",s)
        self.assertIn("'/api/v1/files/container-recycle-purges/recovery/'",s)
        self.assertNotIn("name:'请求取消留存清理'",s)
        for bad in ('page.route(','addCookies(','storageState:'):self.assertNotIn(bad,s)
        parent=(root/'validate_container_recycle_purge_inflight_logout.py').read_text()
        self.assertIn("job_id IN (:'intent'::uuid,:'job'::uuid)",parent)
        self.assertIn('pool.submit(observe_logout_exit,held,began,e,20)',parent)
        verifier=(root/'verify_container_recycle_purge_inflight_logout.py').read_text()
        self.assertIn('actualBffLogoutObservedWhileOriginalAlive',verifier)
        for bad in ('.unlink(','.rmdir(','.rmtree(','.Popen(','.kill('):self.assertNotIn(bad,verifier)


if __name__=='__main__':unittest.main()
