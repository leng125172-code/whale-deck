"""Actual isolated returned-inode I/O, with explicit namespace/session doubles.

Completed historical ownership is distinct from current binding. These tests
are not installed RPC, SQL/UI Superseded acceptance, or physical HDD evidence.
"""
import os
import stat
import unittest
from unittest.mock import patch
import test_restore_returned_cut_scope as scope
import bound_container_recycle_restore_returned_cut as returned
import container_recycle_agent_io as source_io


@unittest.skipUnless(os.name == 'posix' and os.geteuid() == 0, 'isolated Linux root required')
class ReturnedOutcomeTests(unittest.TestCase):
    def setUp(self):
        self.cut = scope.ReturnedCutTests()
        self.cut.setUp()
        self.addCleanup(self.cut.doCleanups)
        self.f = self.cut.f
        self.assertTrue(returned.after_return_proof(self.cut.record, self.cut.folder)['sourceReturned'])
        self.source_before = self.cut.source_path.read_bytes()
        self.claim = self.f.state / 'claims' / (self.f.ref['operationId'] + '.json')
        self.claim_before = self.claim.read_bytes()

    def observe_completed_without_rename(self, session):
        before = {str(p.relative_to(self.cut.folder)): source_io.file_snapshot(p)
                  for p in self.cut.folder.rglob('*') if p.is_file()}
        with patch.object(self.f.base.f, 'exchange', side_effect=AssertionError('must not repeat restore')):
            result = self.f.engine.inspect(self.f.q, session)
            replay = self.f.engine.discard(self.f.q, session)
        for receipt in (result, replay):
            self.assertEqual(receipt['state'], 'Completed')
            self.assertFalse(receipt['bindingCurrent'])
            self.assertEqual(receipt['sourceState'], 'Restored')
            self.assertIs(receipt['sourceRetained'], False)
            self.assertIs(receipt['sourceRestored'], True)
            self.assertIs(receipt['restoredByThisOperation'], True)
            self.assertTrue(receipt['historyVerified'])
            self.assertTrue(receipt['historyAvailable'])
            self.assertEqual(receipt['historyDigest'], self.f.q['plan']['command']['historyDigest'])
        self.assertEqual(self.cut.source_path.read_bytes(), self.source_before)
        self.assertEqual(self.claim.read_bytes(), self.claim_before)
        after = {str(p.relative_to(self.cut.folder)): source_io.file_snapshot(p)
                 for p in self.cut.folder.rglob('*') if p.is_file()}
        self.assertEqual(before, after)
        return result

    def test_missing_returned_target_keeps_completed_ownership_not_unknown(self):
        displaced = self.cut.folder / 'displaced-returned-original'
        self.cut.leaf.rename(displaced)
        self.observe_completed_without_rename(self.f.base.session)
        self.assertFalse(self.cut.leaf.exists())
        self.assertEqual(displaced.read_bytes(), self.f.base.content)
        self.assertEqual(displaced.stat().st_ino, self.cut.original_before[1])

    def test_same_bytes_replacement_is_not_current_binding_or_a_second_restore(self):
        displaced = self.cut.folder / 'displaced-returned-original'
        self.cut.leaf.rename(displaced)
        self.cut.leaf.write_bytes(self.f.base.content)
        original = displaced.stat()
        self.cut.leaf.chmod(stat.S_IMODE(original.st_mode))
        os.chown(self.cut.leaf, original.st_uid, original.st_gid)
        for name in os.listxattr(displaced):
            os.setxattr(self.cut.leaf, name, os.getxattr(displaced, name))
        self.assertNotEqual(self.cut.leaf.stat().st_ino, displaced.stat().st_ino)
        self.observe_completed_without_rename(self.f.base.session)

    def test_unavailable_session_keeps_proven_history_without_claiming_current_namespace(self):
        original = source_io.file_snapshot(self.cut.leaf)
        self.observe_completed_without_rename(None)
        self.assertEqual(source_io.file_snapshot(self.cut.leaf), original)


if __name__ == '__main__':
    unittest.main()
