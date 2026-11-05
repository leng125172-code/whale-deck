import copy
import unittest
from pathlib import Path
from unittest.mock import patch
import validate_container_recycle_inflight_cancel as gate

class InflightCancelGateTests(unittest.TestCase):
    def test_unconfirmed_or_uncertain_task_is_not_a_cancellation_proof(self):
        fixture=dict(subject='s',containerId='a'*64,instanceId='i')
        row=dict(actor_subject='s',container_id='a'*64,instance_id='i',relative_path='work/recycle-rpc.bin',state='Unknown')
        value=dict(id='id',state='Canceled')
        with patch.object(gate,'sql',return_value=dict(row=row,executionJob=dict(state='Running'))),self.assertRaises(AssertionError):gate.prove(value,fixture,Path('/unused'))
    def test_no_root_mutation_or_clock_shortcut_in_browser(self):
        code=Path(__file__).with_name('validate_container_recycle_inflight_cancel.mjs').read_text()
        for forbidden in ('page.route(', 'addCookies(', 'storageState:', 'setSystemTime', 'clock.install(', 'docker exec'):
            self.assertNotIn(forbidden,code)
        self.assertLess(code.index("await awaitMarker('cut-held.json',"),code.index("'cancel-request-began.json'"));self.assertIn("'cancel-request-began.json'",code);self.assertIn('logoutFromUserMenu(page,context,base)',code)
    def test_gate_never_reapplies_a_missed_boundary_and_retains_unsettled_fixture(self):
        code=Path(gate.__file__).read_text();self.assertIn("assert held is not None,'EXACT_CUT_MISSED_NO_REPLAY'",code);self.assertIn("f['retain_unsettled']=True",code);self.assertLess(code.index("e/'independent-live.log'"),code.index('leaf.unlink()'));self.assertIn('pool.submit(held.wait_exit_seconds,began,20)',code);self.assertIn('held.release()',code)

if __name__=='__main__':unittest.main()
