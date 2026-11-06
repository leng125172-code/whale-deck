"""Actual temporary index loss never promotes a still-Applying return to success.

Namespace/HDD attestation are explicit fixture doubles, not installed acceptance.
"""
import json,os,unittest
from unittest.mock import patch
import test_restore_returned_cut_scope as scope
import restore_claim_loss_proof as proof
from recursive_acl_restore_agent_io import read_at


@unittest.skipUnless(os.name=='posix' and os.geteuid()==0,'isolated Linux root required')
class ClaimLossTests(unittest.TestCase):
    def setUp(self):
        self.cut=scope.ReturnedCutTests();self.cut.setUp();self.addCleanup(self.cut.doCleanups);self.f=self.cut.f
        self.claim=self.f.state/'claims'/(self.f.ref['operationId']+'.json');self.original=self.claim.read_bytes();self.returned=self.cut.leaf.read_bytes();self.source=self.cut.source_path.read_bytes()
        self.patch=patch.object(proof.restore_io,'STATE',self.f.state);self.patch.start();self.addCleanup(self.patch.stop)

    def loss(self):
        fd=proof.restore_io.private_directory(self.f.state/'claims')
        try:return proof._isolate(fd,self.f.q,json.loads(self.original))
        finally:os.close(fd)

    def test_missing_claim_after_return_is_unknown_and_product_rebuilds_the_known_fence(self):
        loss=self.loss();self.assertFalse(self.claim.exists())
        with patch.object(self.f.base.f,'exchange',side_effect=AssertionError('must not reapply restore')):
            result=self.f.engine.inspect(self.f.q,self.f.base.session)
        self.assertEqual(result['state'],'Unknown');self.assertIsNone(result['sourceState']);self.assertIsNone(result['sourceRestored']);self.assertIsNone(result['restoredByThisOperation']);self.assertTrue(result['historyAvailable'])
        rebuilt=proof.rebuilt(self.f.q,loss);self.assertTrue(rebuilt['quarantinedOriginalUnchanged']);self.assertEqual(self.cut.leaf.read_bytes(),self.returned);self.assertEqual(self.cut.source_path.read_bytes(),self.source)
        self.assertEqual(self.f.engine.discard(self.f.q,self.f.base.session)['state'],'Unknown')
        proof.remove_quarantine(self.f.q,loss,rebuilt);self.assertTrue(self.claim.exists());self.assertEqual(json.loads(self.claim.read_bytes()),json.loads(self.original))
        with patch.object(self.f.base.f,'exchange',side_effect=AssertionError('legacy result must not repeat rename')):
            legacy=self.f.source.restore(self.f.ref,self.f.base.session)
        self.assertEqual(legacy['state'],'Restored');self.assertTrue(legacy['replayed']);self.assertTrue(self.claim.exists());self.assertEqual(self.cut.leaf.read_bytes(),self.returned)

    def test_existing_quarantine_refuses_before_moving_claim(self):
        _,target=proof.names(self.f.q);path=self.f.state/'claims'/target;path.write_bytes(self.original);path.chmod(0o600)
        with self.assertRaises(AssertionError):self.loss()
        self.assertEqual(self.claim.read_bytes(),self.original)

    def test_foreign_claim_cannot_authorize_quarantine(self):
        value=json.loads(self.original);value['restoreOwner']='D'*64;self.claim.write_text(json.dumps(value));self.claim.chmod(0o600)
        with self.assertRaises(AssertionError):self.loss()
        self.assertTrue(self.claim.exists())

    def test_completed_record_is_not_rewritten_when_a_terminal_claim_is_missing(self):
        self.assertEqual(self.f.engine.inspect(self.f.q,self.f.base.session)['state'],'Completed');loss=self.loss()
        result=self.f.engine.inspect(self.f.q,self.f.base.session);self.assertEqual(result['state'],'Completed');self.assertTrue(result['restoredByThisOperation'])
        self.assertEqual(self.cut.leaf.read_bytes(),self.returned);self.assertTrue(proof.rebuilt(self.f.q,loss)['quarantinedOriginalUnchanged'])


if __name__=='__main__':unittest.main()
