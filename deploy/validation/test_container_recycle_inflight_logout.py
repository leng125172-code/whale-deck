import copy,json,os,subprocess,sys,tempfile,time
from threading import Thread
from types import SimpleNamespace
import unittest
from pathlib import Path
from unittest.mock import patch
import validate_container_recycle_inflight_logout as gate

class InflightLogoutGateTests(unittest.TestCase):
    def test_unconfirmed_or_uncertain_task_is_not_a_logout_proof(self):
        fixture=dict(subject='s',containerId='a'*64,instanceId='i')
        row=dict(actor_subject='s',container_id='a'*64,instance_id='i',relative_path='work/recycle-rpc.bin',state='Unknown')
        value=dict(id='id',state='Canceled')
        with patch.object(gate,'sql',return_value=dict(row=row,executionJob=dict(state='Running'))),self.assertRaises(AssertionError):gate.prove(value,fixture,Path('/unused'))
    def test_normal_logout_proof_rejects_cancel_or_incomplete_session_clearance(self):
        receipt=dict(normalUiLogout=True,postStatus=302,providerNavigationObserved=True,bffStatus=401,identityStatus=403,finalLoginPage=True)
        b=dict(actualUi=True,normalDualLogout=True,registrations=1,confirmations=1,cancellations=0,normalDualLogouts=2,newSessionIdStatus=404,originalSessionReadStatus=401,newSessionSubject='s',subject='s',logoutReceipts=[receipt.copy(),receipt.copy()])
        gate.browser_proof(b)
        for field,bad in [('cancellations',1),('newSessionIdStatus',200),('originalSessionReadStatus',200),('newSessionSubject','other'),('logoutReceipts',[receipt])]:
            with self.subTest(field=field),self.assertRaises(AssertionError):gate.browser_proof({**b,field:bad})
        for field,bad in [('providerNavigationObserved',False),('bffStatus',200),('identityStatus',200),('finalLoginPage',False)]:
            altered=copy.deepcopy(b);altered['logoutReceipts'][0][field]=bad
            with self.subTest(field=field),self.assertRaises(AssertionError):gate.browser_proof(altered)
    def test_sql_explicit_cancel_is_not_counted_as_logout(self):
        fixture=dict(subject='s',containerId='a'*64,instanceId='i')
        row=dict(actor_subject='s',container_id='a'*64,instance_id='i',relative_path='work/recycle-rpc.bin',state='Canceled',cancel_requested_at_utc='explicit-cancel')
        with patch.object(gate,'sql',return_value=dict(row=row,executionJob=dict(state='Canceled'))),self.assertRaises(AssertionError):gate.prove(dict(id='id',state='Canceled'),fixture,Path('/unused'))
    def test_exit_observer_requires_original_alive_after_actual_logout_post(self):
        with tempfile.TemporaryDirectory() as folder,subprocess.Popen([sys.executable,'-c','import time;time.sleep(60)']) as child:
            fd=os.pidfd_open(child.pid);e=Path(folder)
            def trigger():
                time.sleep(.03);(e/'logout-request-began.json').write_text(json.dumps(dict(actualNavigationPost=True,path='/api/v1/auth/logout')));time.sleep(.05);child.terminate()
            thread=Thread(target=trigger);thread.start()
            try:
                proof=gate.observe_logout_exit(SimpleNamespace(descriptor=fd),time.monotonic(),e,2)
                self.assertTrue(proof['actualBffLogoutObservedWhileOriginalAlive'])
                thread.join();child.wait(timeout=2)
                with self.assertRaisesRegex(AssertionError,'BEFORE_LOGOUT'):gate.observe_logout_exit(SimpleNamespace(descriptor=fd),time.monotonic(),e,2)
            finally:
                thread.join();os.close(fd)
                if child.poll() is None:child.terminate();child.wait(timeout=2)
    def test_no_root_mutation_or_clock_shortcut_in_browser(self):
        code=Path(__file__).with_name('validate_container_recycle_inflight_logout.mjs').read_text()
        for forbidden in ('page.route(', 'addCookies(', 'storageState:', 'setSystemTime', 'clock.install(', 'docker exec'):
            self.assertNotIn(forbidden,code)
        self.assertIn("r.isNavigationRequest()&&r.method()==='POST'",code);self.assertIn("'logout-request-began.json'",code);self.assertIn('logoutFromUserMenu(page,context,base)',code)
    def test_gate_never_reapplies_a_missed_boundary_and_retains_unsettled_fixture(self):
        code=Path(gate.__file__).read_text();self.assertIn("assert held is not None,'EXACT_CUT_MISSED_NO_REPLAY'",code);self.assertIn("f['retain_unsettled']=True",code);self.assertLess(code.index("e/'independent-live.log'"),code.index('leaf.unlink()'));self.assertIn('pool.submit(observe_logout_exit,held,began,e,20)',code);self.assertIn('held.release()',code)

if __name__=='__main__':unittest.main()
