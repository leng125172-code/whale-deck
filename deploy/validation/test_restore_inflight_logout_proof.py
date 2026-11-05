"""Logout must not be certified by an explicit cancel or a different session."""
import copy,unittest,uuid
from pathlib import Path
from unittest.mock import patch
import validate_container_recycle_restore_inflight_logout as gate
from recycle_restore_browser_proof import execution


class RestoreLogoutProofTests(unittest.TestCase):
    def test_logout_requires_both_session_clearance_receipts_and_new_binding_denials(self):
        receipt=dict(normalUiLogout=True,postStatus=302,providerNavigationObserved=True,bffStatus=401,identityStatus=403,finalLoginPage=True)
        value=dict(actualUi=True,normalDualLogout=True,registrations=1,confirmations=1,cancellations=0,normalDualLogouts=2,newSessionIdStatus=404,newSessionKeyStatus=404,newSourceIdStatus=404,originalSessionReadStatus=401,newSessionSubject='s',subject='s',logoutReceipts=[receipt.copy(),receipt.copy()])
        gate.browser_proof(value)
        for key,bad in [('cancellations',1),('newSessionKeyStatus',200),('newSourceIdStatus',200),('newSessionSubject','other'),('logoutReceipts',[receipt])]:
            with self.subTest(key=key),self.assertRaises(AssertionError):gate.browser_proof({**value,key:bad})
        for key in ('providerNavigationObserved','finalLoginPage'):
            bad=copy.deepcopy(value);bad['logoutReceipts'][0][key]=False
            with self.subTest(key=key),self.assertRaises(AssertionError):gate.browser_proof(bad)

    def test_explicit_cancellation_stamp_cannot_pass_as_a_normal_logout(self):
        identifier=str(uuid.uuid4());source=str(uuid.uuid4());op=execution(identifier);srcop=execution(source,source=True)
        fixture=dict(subject='s',containerId='a'*64,instanceId='original')
        row=dict(id=identifier,source_intent_id=source,actor_subject='s',container_id='a'*64,instance_id='original',relative_path='work/recycle-rpc.bin',execution_job_id=op,state='Canceled',cancel_requested_at_utc='explicit')
        value=dict(id=identifier,sourceIntentId=source,sourceOperationId=srcop,executionJobId=op,state='Canceled')
        original=dict(sql=dict(row=dict(id=source,execution_job_id=srcop)))
        with patch.object(gate,'sql',return_value=dict(row=row,executionJob=dict(state='Canceled'))),self.assertRaises(AssertionError):gate.prove(value,fixture,original,Path('/unused'))

    def test_unknown_or_running_is_not_a_finished_logout_fence(self):
        identifier=str(uuid.uuid4());source=str(uuid.uuid4());op=execution(identifier);srcop=execution(source,source=True)
        fixture=dict(subject='s',containerId='a'*64,instanceId='original')
        row=dict(id=identifier,source_intent_id=source,actor_subject='s',container_id='a'*64,instance_id='original',relative_path='work/recycle-rpc.bin',execution_job_id=op,state='Unknown')
        value=dict(id=identifier,sourceIntentId=source,sourceOperationId=srcop,executionJobId=op,state='Canceled')
        original=dict(sql=dict(row=dict(id=source,execution_job_id=srcop)))
        with patch.object(gate,'sql',return_value=dict(row=row,executionJob=dict(state='Running'))),self.assertRaises(AssertionError):gate.prove(value,fixture,original,Path('/unused'))

    def test_browser_observes_the_navigation_post_and_never_calls_cancel(self):
        code=Path(gate.__file__).with_suffix('.mjs').read_text()
        self.assertIn("request.isNavigationRequest() && request.method() === 'POST'",code)
        self.assertIn("url.pathname === '/api/v1/auth/logout'",code)
        for forbidden in ('page.route(', 'addCookies(', 'storageState:', "name: '确认请求取消'",'clock.install('):self.assertNotIn(forbidden,code)
        parent=Path(gate.__file__).read_text();self.assertIn('pool.submit(observe_logout_exit,held,began,e,20)',parent)
        self.assertLess(parent.index('independent(repo,e,True)'),parent.index('retained.unlink()'))


if __name__=='__main__':unittest.main()
