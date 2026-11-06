"""Constructed SQL/UI logout evidence, never actual unlink/logout acceptance."""
import copy,unittest
from pathlib import Path
import test_container_recycle_purge_unlinked as prior
from recycle_purge_unlinked_logout_proof import row_proof,require_ack
from validate_container_recycle_purge_unlinked_logout import browser_proof


def data(ack=False):
    v,f=prior.data(ack);v['row']['cancel_requested_at_utc']=None;v['executionJob']['cancel_requested_at_utc']=None;v['pending']=0 if ack else 1
    return v,f


class UnlinkedLogoutTests(unittest.TestCase):
    def test_unknown_and_ack_keep_nullable_outcome_without_cancellation(self):
        for ack in (False,True):v,f=data(ack);row_proof(v,f,ack)

    def test_cancel_timestamp_on_either_row_or_extra_delivery_is_rejected(self):
        for target in ('row','executionJob'):
            v,f=data();v[target]['cancel_requested_at_utc']='cancel'
            with self.subTest(target=target),self.assertRaises(AssertionError):row_proof(v,f,False)
        for count in (0,2,True):
            v,f=data();v['pending']=count
            with self.subTest(count=count),self.assertRaises(AssertionError):row_proof(v,f,False)

    def test_unknown_never_defaults_to_false_true_or_a_canceled_result(self):
        for key,bad in [('purge_applied',False),('purge_applied',True),('state','Canceled'),('source_fenced',False),('publish_barrier',False),('root_state','Aborted')]:
            v,f=data();v['row'][key]=bad
            with self.subTest(key=key),self.assertRaises(AssertionError):row_proof(v,f,False)

    def test_ack_does_not_change_original_receipt_or_archive_and_requires_other_actor(self):
        old,_=data();new,_=data(True);before=dict(sql=old,receipt={'state':'Unknown'},records={'bytes':'same'},physical={'namesAbsent':True},hdd={'sha':'same'});after={**before,'sql':new};require_ack(before,after,'other')
        with self.assertRaises(AssertionError):require_ack(before,after,'subject')
        for key in ('receipt','records','physical','hdd'):
            with self.subTest(key=key),self.assertRaises(AssertionError):require_ack(before,{**after,key:{'changed':True}},'other')

    def test_real_bff_provider_logout_receipts_and_zero_cancel_are_required(self):
        r=dict(normalUiLogout=True,postStatus=303,providerNavigationObserved=True,bffStatus=401,identityStatus=403,finalLoginPage=True);f=dict(subject='s',recoverySubject='other')
        b=dict(actualUi=True,subject='s',recoverySubject='other',registrations=1,confirmations=1,cancellations=0,acknowledgements=1,managementWrites=1,originalSessionReadStatus=401,differentAdminOldIdStatus=404,hintsCleared=True,cancelledDecisionPreserved=True,logoutReceipts=[r,copy.deepcopy(r)],jobProofs=[{},{}]);browser_proof(b,f)
        for key,bad in [('cancellations',1),('managementWrites',2),('recoverySubject','s'),('hintsCleared',False)]:
            with self.subTest(key=key),self.assertRaises(AssertionError):browser_proof({**b,key:bad},f)
        wrong=copy.deepcopy(b);wrong['logoutReceipts'][0]['providerNavigationObserved']=False
        with self.assertRaises(AssertionError):browser_proof(wrong,f)

    def test_gate_binds_actual_navigation_and_original_pidfd_while_verifier_is_readonly(self):
        root=Path(__file__).parent;s=(root/'validate_container_recycle_purge_unlinked_logout.mjs').read_text()
        self.assertIn('request.isNavigationRequest()',s);self.assertIn("request.method()==='POST'",s);self.assertIn("u.pathname==='/api/v1/auth/logout'",s)
        self.assertNotIn("name:'请求取消留存清理'",s)
        for bad in ('page.route(','addCookies(','storageState:'):self.assertNotIn(bad,s)
        parent=(root/'validate_container_recycle_purge_unlinked_logout.py').read_text();self.assertIn('pool.submit(observe_logout_exit,held,began,e,20)',parent)
        verifier=(root/'verify_container_recycle_purge_unlinked_logout.py').read_text();self.assertIn('actualBffLogoutObservedWhileOriginalAlive',verifier)
        for bad in ('.unlink(','.rmdir(','.rmtree(','.Popen(','.kill('):self.assertNotIn(bad,verifier)


if __name__=='__main__':unittest.main()
