"""Negative signal targeting and real private-object ownership checks."""
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import bound_container_directory_cut as cut


class DirectoryCutOwnershipTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(prefix='wd-directory-cut-')
        self.addCleanup(temporary.cleanup); self.root = Path(temporary.name)
        self.record = {'operationId': 'a' * 32, 'state': 'Preparing', 'privateIdentity': None, 'pendingIdentity': None,
            'plan': {'command': {'path': 'work/目标'}}}

    def private(self):
        path = self.root / '.whaledeck-container-directories-v1'; path.mkdir(mode=0o700)
        self.record['privateIdentity'] = list(cut.identity(path)); return path

    @unittest.skipUnless(os.name == 'posix' and os.geteuid() == 0, 'requires Linux root private-owner fixture')
    def test_only_recorded_empty_private_pending_inode_is_disposable(self):
        private = self.private(); pending = private / ('a' * 32 + '.directory'); pending.mkdir()
        self.record['pendingIdentity'] = list(cut.identity(pending))
        self.assertTrue(cut.disposable_private(self.record, self.root))
        (pending / 'foreign').write_bytes(b'preserve')
        self.assertFalse(cut.disposable_private(self.record, self.root))
        self.assertEqual(b'preserve', (pending / 'foreign').read_bytes())

    @unittest.skipUnless(os.name == 'posix' and os.geteuid() == 0, 'requires Linux root private-owner fixture')
    def test_replaced_private_or_pending_inodes_are_rejected_and_retained(self):
        private = self.private(); pending = private / ('a' * 32 + '.directory'); pending.mkdir()
        self.record['pendingIdentity'] = list(cut.identity(pending))
        pending.rename(private / 'saved'); pending.mkdir()
        self.assertFalse(cut.disposable_private(self.record, self.root)); self.assertTrue((private / 'saved').exists())
        private.rename(self.root / 'saved-private'); private.mkdir(mode=0o700)
        self.assertFalse(cut.disposable_private(self.record, self.root))

    def test_existing_target_including_dangling_link_cannot_be_called_prepublication(self):
        target = self.root / '目标'; target.symlink_to(self.root / 'missing')
        self.assertFalse(cut.disposable_private(self.record, self.root)); self.assertTrue(target.is_symlink())

    def test_unrecorded_private_object_is_not_disposable(self):
        private = self.root / '.whaledeck-container-directories-v1'; private.mkdir(mode=0o700)
        self.assertFalse(cut.disposable_private(self.record, self.root)); self.assertTrue(private.exists())

    def test_published_and_terminal_records_are_never_cut(self):
        for state in ('Completed', 'Aborted', 'Unknown'):
            self.record['state'] = state; self.assertFalse(cut.disposable_private(self.record, self.root))

    @unittest.skipUnless(os.name == 'posix' and Path('/proc/self').exists(), 'requires procfs')
    def test_unrelated_process_is_rejected_even_if_it_holds_both_inodes(self):
        lock = self.root / 'lock'; lock.write_bytes(b'')
        root_fd = os.open(self.root, os.O_RDONLY | os.O_DIRECTORY); lock_fd = os.open(lock, os.O_RDONLY)
        try:
            self.assertFalse(cut.bound_fds(os.getpid(), cut.identity(self.root), cut.identity(lock)))
        finally: os.close(root_fd); os.close(lock_fd)

    @unittest.skipUnless(os.name == 'posix' and os.geteuid() == 0, 'requires Linux root validation entry')
    def test_non_fixture_source_is_rejected_before_pidfd_or_signal(self):
        row = {'container_id': 'a' * 64}
        with patch.object(cut.os, 'pidfd_open', side_effect=AssertionError('must not obtain pidfd')) as opened, \
             patch.object(cut.signal, 'pidfd_send_signal', side_effect=AssertionError('must not signal')) as signaling:
            with self.assertRaises(AssertionError): cut.freeze_prepublication(row, '/etc', 1)
            opened.assert_not_called(); signaling.assert_not_called()


if __name__ == '__main__': unittest.main()
