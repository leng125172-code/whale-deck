"""Post-return logout must retain completion and prove real session isolation."""
import copy,unittest
from pathlib import Path
from unittest.mock import patch
import validate_container_recycle_restore_postreturn_logout as gate
import validate_container_recycle_restore_postreturn_worker as completed


class PostreturnLogoutProofTests(unittest.TestCase):
    def test_both_real_logout_receipts_and_three_old_reference_denials_are_required(self):
        receipt=dict(normalUiLogout=True,postStatus=302,providerNavigationObserved=True,bffStatus=401,identityStatus=403,finalLoginPage=True)
        b=dict(actualUi=True,normalDualLogout=True,registrations=1,confirmations=1,cancellations=0,normalDualLogouts=2,newSessionIdStatus=404,newSessionKeyStatus=404,newSourceIdStatus=404,originalSessionReadStatus=401,newSessionSubject='s',subject='s',logoutReceipts=[receipt.copy(),receipt.copy()])
        gate.browser_proof(b)
        for key,value in [('cancellations',1),('newSessionKeyStatus',200),('newSourceIdStatus',200),('newSessionSubject','another'),('logoutReceipts',[receipt])]:
            with self.subTest(key=key),self.assertRaises(AssertionError):gate.browser_proof({**b,key:value})
        bad=copy.deepcopy(b);bad['logoutReceipts'][0]['providerNavigationObserved']=False
        with self.assertRaises(AssertionError):gate.browser_proof(bad)

    def test_explicit_cancel_cannot_substitute_for_logout(self):
        with patch.object(completed,'prove_result',return_value=dict(sql=dict(row=dict(state='Succeeded',cancel_requested_at_utc='explicit',result_code='CONTAINER_RECYCLE_RESTORE_COMPLETED')))),self.assertRaises(AssertionError):gate.prove({}, {}, {}, Path('/unused'))

    def test_pre_return_failure_cannot_be_a_successful_returned_original(self):
        with patch.object(completed,'prove_result',return_value=dict(sql=dict(row=dict(state='Failed',cancel_requested_at_utc=None,result_code='CONTAINER_RECYCLE_RESTORE_INTERRUPTED')))),self.assertRaises(AssertionError):gate.prove({}, {}, {}, Path('/unused'))

    def test_real_logout_observer_precedes_ui_release_and_cleanup_follows_independent_proof(self):
        code=Path(gate.__file__).read_text();browser=Path(gate.__file__).with_suffix('.mjs').read_text()
        self.assertLess(code.index('pool.submit(observe_logout_exit,held,began,e,20)'),code.index("save('cut-held.json'"))
        self.assertLess(code.index('independent(repo,e,True)'),code.index('leaf.unlink()'));self.assertNotIn('retained.unlink()',code)
        self.assertIn("request.isNavigationRequest() && request.method() === 'POST'",browser)
        for forbidden in ('page.route(', 'addCookies(', 'storageState:', "name: '确认请求取消'"):self.assertNotIn(forbidden,browser)


if __name__=='__main__':unittest.main()
