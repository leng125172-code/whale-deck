"""Applying/restored originals and HDD must not masquerade as a terminal RPC."""
import copy,os,unittest
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch
import test_restore_returned_cut_scope as returned
import restore_returned_cut_io as io


@unittest.skipUnless(os.name=='posix' and os.geteuid()==0,'isolated Linux root required')
class PendingReturnedIoTests(unittest.TestCase):
    def setUp(self):
        self.t=returned.ReturnedCutTests();self.t.setUp();self.addCleanup(self.t.doCleanups);f=self.t.f
        for p in (patch.object(io.restore_io,'STATE',f.state),patch.object(io.source_io,'STATE',f.base.state),patch.object(io.restore_io,'installed',return_value=SimpleNamespace(storage=f.base.disk.engine))):
            p.start();self.addCleanup(p.stop)
        self.folder=f.base.disk.disk/'pending-evidence'

    def test_actual_pending_return_and_hdd_are_archived_without_terminal_claim(self):
        q=self.t.f.q;value=io.read_pending(q,self.folder,True)
        self.assertEqual(value,io.read_pending(q,self.folder));self.assertEqual(value['root']['state'],'Applying');self.assertEqual(value['sourceRoot']['state'],'Restored')
        self.assertTrue(value['verification']['hddDataReread']);self.assertFalse(value['verification']['rpcReceiptObserved']);self.assertFalse(value['verification']['restoreTerminalJournalSaved'])

    def test_foreign_claim_or_hdd_data_cannot_be_accepted(self):
        f=self.t.f;v=io.read_pending(f.q,self.folder,True)
        for key in ('claim','history','data','sourceRoot'):
            bad=copy.deepcopy(v)
            if key=='claim':bad[key]['restoreOperationId']='d'*32
            elif key=='history':bad[key]['owner']='D'*64
            elif key=='data':bad[key]['sha256']='D'*64
            else:bad[key]['state']='Trashed'
            with self.subTest(key=key),self.assertRaises(AssertionError):io.record_projection(f.q,bad['root'],bad['claim'],bad['sourceRoot'],bad['history'],bad['data'])

    def test_terminal_own_record_does_not_count_as_pending_cut(self):
        f=self.t.f;v=io.read_pending(f.q,self.folder,True)
        with self.assertRaises(AssertionError):io.record_projection(f.q,{**v['root'],'state':'Completed'},v['claim'],v['sourceRoot'],v['history'],v['data'])

    def test_late_cancel_cleanup_waits_for_independent_proof_of_returned_leaf(self):
        root=Path(__file__).parent;s=(root/'validate_container_recycle_restore_postreturn_cancel.py').read_text()
        self.assertLess(s.index('independent(repo,e,True)'),s.index('leaf.unlink()'));self.assertNotIn('retained.unlink()',s)
        browser=(root/'validate_container_recycle_restore_postreturn_cancel.mjs').read_text()
        self.assertIn("assert.equal(result.state, 'Succeeded')",browser);self.assertIn("assert.equal(result.receipt.state, 'Completed')",browser)


if __name__=='__main__':unittest.main()
