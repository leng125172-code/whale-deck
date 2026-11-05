"""Real temporary retained-inode return before the separate restore journal.

Namespace/liveness/HDD attestation are fixture doubles. The native rename and
owned child SIGKILL are real; neither is installed RPC or human UI acceptance.
"""
import copy,json,os,select,signal,unittest
from pathlib import Path
from unittest.mock import patch
import bound_container_recycle_restore_returned_cut as returned
import test_container_recycle_restore_rpc_proof as native


@unittest.skipUnless(os.name=='posix' and os.geteuid()==0,'isolated Linux root required')
class ReturnedCutTests(unittest.TestCase):
    def setUp(self):
        self.f=native.RestoreOriginalProofTests();self.f.setUp();self.addCleanup(self.f.doCleanups)
        self.folder=self.f.base.root/'work';self.leaf=self.folder/'recycle-rpc.bin'
        self.original_before=self.f.source.metadata((self.folder/'.whaledeck-container-recycling-v1'/(self.f.ref['operationId']+'.trash')).stat())
        child=os.fork()
        if child==0:
            try:
                self.f.engine.checkpoint=lambda phase:os.kill(os.getpid(),signal.SIGKILL) if phase=='SourceReturned' else None
                self.f.engine.apply(self.f.q,self.f.base.session)
            except BaseException:os._exit(2)
            os._exit(3)
        fd=os.pidfd_open(child)
        try:
            ready=bool(select.select([fd],[],[],15)[0])
            if not ready:signal.pidfd_send_signal(fd,signal.SIGKILL)
            _,status=os.waitpid(child,0);self.assertTrue(ready);self.assertTrue(os.WIFSIGNALED(status));self.assertEqual(os.WTERMSIG(status),signal.SIGKILL)
        finally:os.close(fd)
        self.patch=patch.multiple(returned.before,STATE=self.f.state,SOURCE_STATE=self.f.base.state);self.patch.start();self.addCleanup(self.patch.stop)
        self.path=self.f.state/(self.f.q['operationId']+'.json');self.record=json.loads(self.path.read_text())
        self.source_path=self.f.base.state/(self.f.ref['operationId']+'.json');self.source=json.loads(self.source_path.read_text())

    def test_native_return_is_original_inode_and_not_yet_a_terminal_restore_receipt(self):
        self.assertEqual(self.record['state'],'Applying');self.assertIsNone(self.record['sourceOutcome']);self.assertEqual(self.source['state'],'Restored')
        raw=self.path.read_bytes();source_raw=self.source_path.read_bytes();proof=returned.after_return_proof(self.record,self.folder)
        self.assertTrue(proof['sourceReturned']);self.assertTrue(proof['sourceClaimMatches']);self.assertFalse(proof['terminalRestoreJournalSaved'])
        self.assertFalse(proof['hddDataReread']);self.assertFalse(proof['rpcReceiptObserved'])
        self.assertEqual(proof['restoredIdentity'][:8],self.original_before[:8]);self.assertGreaterEqual(proof['restoredIdentity'][8],self.original_before[8])
        self.assertEqual(raw,self.path.read_bytes());self.assertEqual(source_raw,self.source_path.read_bytes());self.assertEqual(self.leaf.read_bytes(),self.f.base.content)

    def test_written_namespace_is_not_accepted_by_the_old_pre_return_proof(self):
        self.assertIsNone(returned.before.before_return_proof(self.record,self.folder))
        for state in ('Preparing','Completed','Aborted','Unknown'):
            with self.subTest(state=state):self.assertIsNone(returned.after_return_proof({**self.record,'state':state},self.folder))

    def test_restoring_or_unknown_source_is_not_a_durable_source_return(self):
        for state in ('Trashed','Restoring','Unknown','Aborted'):
            self.source_path.write_text(json.dumps({**self.source,'state':state}));self.source_path.chmod(0o600)
            with self.subTest(state=state):self.assertIsNone(returned.after_return_proof(self.record,self.folder))

    def test_foreign_or_missing_claim_cannot_attribute_the_already_returned_inode(self):
        path=self.f.state/'claims'/(self.f.ref['operationId']+'.json');claim=json.loads(path.read_text())
        for change in (dict(restoreOperationId='d'*32),dict(restoreOwner='D'*64),dict(restoreFingerprint='D'*64)):
            path.write_text(json.dumps({**claim,**change}));path.chmod(0o600)
            with self.subTest(change=change),self.assertRaises(AssertionError):returned.after_return_proof(self.record,self.folder)
        path.unlink()
        with self.assertRaises(AssertionError):returned.after_return_proof(self.record,self.folder)

    def test_same_bytes_on_a_new_inode_cannot_certify_return(self):
        original=self.folder/'original-held';self.leaf.rename(original);self.leaf.write_bytes(self.f.base.content);self.leaf.chmod(0o640);os.chown(self.leaf,2100,3100);os.setxattr(self.leaf,'user.recycle',b'original metadata')
        self.assertNotEqual(self.leaf.stat().st_ino,original.stat().st_ino)
        with self.assertRaises(AssertionError):returned.after_return_proof(self.record,self.folder)

    def test_changed_attributes_link_count_or_parent_is_not_the_planned_original(self):
        os.setxattr(self.leaf,'user.changed',b'unexpected')
        with self.assertRaises(AssertionError):returned.after_return_proof(self.record,self.folder)
        with self.assertRaises(AssertionError):returned.after_return_proof(self.record,self.f.base.root)
        os.link(self.leaf,self.folder/'hardlink')
        with self.assertRaises(AssertionError):returned.after_return_proof(self.record,self.folder)

    def test_foreign_source_version_owner_identity_or_digest_is_rejected(self):
        for change in (dict(schemaVersion=2),dict(schemaVersion=True),dict(owner='D'*64),dict(historyDigest='D'*64),dict(privateIdentity=[0,1]),dict(actualIdentity=[0]*9)):
            self.source_path.write_text(json.dumps({**self.source,**change}));self.source_path.chmod(0o600)
            with self.subTest(change=change),self.assertRaises(AssertionError):returned.after_return_proof(self.record,self.folder)

    def test_missing_target_does_not_count(self):
        self.leaf.rename(self.folder/'missing-original');self.assertIsNone(returned.after_return_proof(self.record,self.folder))

    def test_reappearing_trash_alongside_the_original_target_does_not_count(self):
        retained=self.folder/'.whaledeck-container-recycling-v1'/(self.f.ref['operationId']+'.trash')
        retained.write_bytes(b'foreign leftover');retained.chmod(0o600)
        self.assertIsNone(returned.after_return_proof(self.record,self.folder))

    def test_discard_after_actual_return_records_completed_not_aborted_or_another_rename(self):
        before=self.f.source.metadata(self.leaf.stat())
        result=self.f.engine.discard(self.f.q,self.f.base.session)
        self.assertEqual(result['state'],'Completed');self.assertTrue(result['restoredByThisOperation']);self.assertEqual(result['sourceState'],'Restored')
        self.assertEqual(self.f.source.metadata(self.leaf.stat()),before)
        with patch.object(self.f.base.f,'exchange',side_effect=AssertionError('must not repeat native rename')):
            replay=self.f.engine.apply(self.f.q,self.f.base.session);self.assertEqual(replay['state'],'Completed')

    def test_foreign_host_scope_is_rejected_before_any_pidfd_or_signal(self):
        row=dict(container_id='a'*64,instance_id='777:original')
        with patch.object(returned.os,'pidfd_open') as opened,patch.object(returned.signal,'pidfd_send_signal') as send,self.assertRaises(AssertionError):returned.freeze_after_return(row,self.folder,777)
        opened.assert_not_called();send.assert_not_called()


if __name__=='__main__':unittest.main()
