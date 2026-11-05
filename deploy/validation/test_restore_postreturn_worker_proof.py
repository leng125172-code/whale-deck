"""Post-return worker fault cannot borrow cancel, pre-return failure or another claim."""
import copy,unittest
from pathlib import Path
from unittest.mock import patch
import restore_postreturn_worker_proof as proof
import validate_container_recycle_restore_postreturn_worker as gate
import test_restore_restart_proof as prior


class PostreturnWorkerProofTests(unittest.TestCase):
    def test_only_worker_crash_is_exposed_before_runtime_access(self):
        for kind in ('agent-restart','cancel','reboot'):
            with self.subTest(kind=kind),patch.object(gate.os,'geteuid') as uid,self.assertRaises(AssertionError):gate.main('a'*40,kind)
            uid.assert_not_called()

    def test_fixed_worker_restart_preserves_the_success_claim_and_rejects_foreign_changes(self):
        before,after,service=prior.RestoreRestartTests().baseline('worker-crash')
        after['recycleRestoreRoot']['claims/'+('a'*32)+'.json']={'this':'claim'}
        proof.preserved_restart(before,after,'a'*32,'b'*32,service,'worker-crash')
        bad=copy.deepcopy(after);bad['recycleRestoreRoot'].pop('claims/'+('a'*32)+'.json')
        with self.assertRaises(AssertionError):proof.preserved_restart(before,bad,'a'*32,'b'*32,service,'worker-crash')
        bad=copy.deepcopy(after);bad['core']['/unrelated'][0]='recreated'
        with self.assertRaises(AssertionError):proof.preserved_restart(before,bad,'a'*32,'b'*32,service,'worker-crash')

    def test_late_cancel_cannot_masquerade_as_worker_only_success(self):
        with patch.object(gate,'prove_result',return_value=dict(sql=dict(row=dict(state='Succeeded',cancel_requested_at_utc='explicit',result_code='CONTAINER_RECYCLE_RESTORE_COMPLETED')))),self.assertRaises(AssertionError):gate.prove({}, {}, {}, Path('/unused'))

    def test_actual_boundary_and_cleanup_remain_bound_to_original_returned_leaf(self):
        root=Path(__file__).parent;s=Path(gate.__file__).read_text();browser=(root/'validate_container_recycle_restore_postreturn_worker.mjs').read_text()
        self.assertLess(s.index("assert held is not None,'EXACT_RESTORE_CUT_MISSED_NO_REPLAY'"),s.index('restart(kind,held,worker,revision,e)'))
        self.assertLess(s.index('independent(repo,e,True)'),s.index('leaf.unlink()'));self.assertNotIn('retained.unlink()',s)
        self.assertIn("settled.state, 'Succeeded'",browser);self.assertIn("result.receipt.state, 'Completed'",browser)
        self.assertIn("assert.equal(cancellations, 0)",browser);self.assertIn('worker.start()',s)


if __name__=='__main__':unittest.main()
