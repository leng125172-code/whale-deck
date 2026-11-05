"""Owned temporary files and explicit protocol/ACL doubles; not installed acceptance."""
import copy
import json
import os
from pathlib import Path
from tempfile import TemporaryDirectory
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

from recursive_acl_restore_unknown_workflow import authority_proof, exercise, wait_packet
from recursive_acl_restore_unknown_physical import OLD, NEW, FILES, owned_root, rename_owned, inventory, remove_proved
from recursive_acl_restore_unknown_physical import original_inventory, frame
from test_recursive_acl_restore_partial_workflow import packets as partial_packets
from validate_recursive_acl_restore_paging import main
from verify_recursive_acl_restore_unknown_rename import verify


def packets():
    values = list(partial_packets())
    values[0]['cancellations'] = 0
    return values


class UnknownWorkflowTests(unittest.TestCase):
    def test_three_actual_packet_subjects_zero_cancel_and_one_ack(self):
        values = packets()
        before = copy.deepcopy(values)
        authority_proof(*values)
        self.assertEqual(values, before)

    def test_old_partial_cancellation_cannot_be_relabelled_unknown(self):
        with self.assertRaises(AssertionError):
            authority_proof(*partial_packets())
        for index, key, bad in ((0, 'cancellations', False), (1, 'subject', 'owner'),
                (1, 'acknowledgements', True), (3, 'ownedLookup', 200), (3, 'acknowledgements', 1),
                (2, 'uiRequests', False), (2, 'posts', [403, 403])):
            values = packets()
            values[index][key] = bad
            with self.subTest(key=key), self.assertRaises(AssertionError):
                authority_proof(*values)

    def test_unknown_mode_and_artifacts_cannot_use_old_gate_names(self):
        for kwargs in (dict(unknown_rename=True), dict(unknown_rename='yes', job_results=True),
                dict(unknown_rename=True, partial_cancel=True, job_results=True),
                dict(unknown_rename=True, inflight_logout=True, job_results=True),
                dict(unknown_rename=True, partial_logout=True, job_results=True)):
            with self.subTest(kwargs=kwargs), self.assertRaises(AssertionError):
                main(**kwargs)
        for name in ('../escape', 'recursive-acl-restore-partial-cancel-' + 'a' * 32,
                'recursive-acl-restore-unknown-rename-' + 'A' * 32):
            with self.subTest(name=name), self.assertRaises(AssertionError):
                verify(name)
        for name in ('../escape', 'partial', 'action'):
            with self.subTest(packet=name), self.assertRaises(AssertionError):
                wait_packet(Mock(), Path('/unused'), name)

    def test_failure_before_ready_retains_fixture_without_worker_or_rename(self):
        fixture = {}
        process = Mock()
        with TemporaryDirectory() as folder, patch('recursive_acl_restore_unknown_workflow.wait_signal', side_effect=RuntimeError('explicit fixture')), patch('recursive_acl_restore_unknown_workflow.stop_browser') as stop, patch('recursive_acl_restore_unknown_workflow.Worker') as worker, patch('recursive_acl_restore_unknown_workflow.rename_owned') as rename:
            with self.assertRaisesRegex(RuntimeError, 'explicit fixture'):
                exercise(process, Path(folder), dict(restoreUnknownRename=True, restorePartialWorkflow=False),
                    {}, {}, None, None, None, None, None, fixture)
            self.assertIs(fixture['retain_unsettled'], True)
            stop.assert_called_once_with(process)
            worker.assert_not_called()
            rename.assert_not_called()
            self.assertEqual(json.loads((Path(folder) / 'unknown-failure-context.json').read_text())['type'], 'RuntimeError')

    def test_browser_uses_visible_unknown_and_ack_without_response_or_auth_injection(self):
        source = (Path(__file__).parent / 'installed_ui_restore_unknown_resolution.mjs').read_text()
        for forbidden in ('.route(', '.addCookies(', 'sessionStorage.setItem', 'localStorage.setItem',
                'request.put(', '确认请求取消', 'logoutCase', 'partial-result.json'):
            self.assertNotIn(forbidden, source)
        for required in ('结果未知，保护已保留', '暂不处置', '确认处置并释放保护',
                'unknownPhysicalVerified', 'ackAndPhysicalPreserved', 'unknownAndAckVerified',
                'credentials.resolver', '[0,0,0,0,26]', 'logoutFromUserMenu'):
            self.assertIn(required, source)

    def test_verifier_rechecks_renamed_tree_and_originals_before_and_after_cleanup(self):
        source = (Path(__file__).parent / 'verify_recursive_acl_restore_unknown_rename.py').read_text()
        for required in ('unknown_renamed_first_proof', 'prefix_archives_unchanged', 'renamed_inventory',
                "saved('rename-proof.json')", "saved('live-independent.json')", 'source_closure'):
            # Source closure acquisition is deliberately shared read-only code.
            if required == 'source_closure':
                self.assertIn('acquisition(name,seeds)', source)
            else:
                self.assertIn(required, source)
        self.assertNotIn('legacy_report', source)
        self.assertNotIn('require_partial(', source)

    def test_retention_is_cleared_only_by_owner_after_independent_live_and_exact_cleanup(self):
        folder = Path(__file__).parent
        workflow = (folder / 'recursive_acl_restore_unknown_workflow.py').read_text()
        owner = (folder / 'validate_recursive_acl_restore_paging.py').read_text()
        self.assertNotIn("fixture['retain_unsettled']=False", workflow)
        self.assertLess(owner.index("PHASE='independent-live-before-cleanup'"), owner.index('remove_renamed(root,reader,data,acl,expected)'))
        self.assertLess(owner.index('remove_renamed(root,reader,data,acl,expected)'), owner.index("fixture['retain_unsettled']=False"))
        self.assertIn("actualRecursiveRestorePaging='passed',**outcome,restoreUnknownRename=unknown_rename", owner)


class UnknownPhysicalTests(unittest.TestCase):
    def setUp(self):
        self.temp = TemporaryDirectory(prefix='wd-unknown-owned-')
        self.addCleanup(self.temp.cleanup)
        self.apps = Path(self.temp.name)
        self.source = self.apps / ('wdv-container-publish-' + 'a' * 32)
        self.source.mkdir()
        self.tree = self.source / 'recursive-tree'
        self.tree.mkdir()
        self.reader = SimpleNamespace(fingerprint=frame)
        self.acl = SimpleNamespace(XATTRS={}, read_acl=lambda *args: [])
        self.data = b'actual temporary file bytes'
        for name in FILES:
            path = self.tree / name
            path.write_bytes(self.data)
            os.setxattr(path, 'user.whaledeck-recursive', b'preserve ordinary attribute')
        os.setxattr(self.tree, 'user.whaledeck-recursive', b'preserve ordinary attribute')
        self.before = original_inventory(self.tree, self.reader, self.data, self.acl)

    def rename(self):
        # Only the scope root is an explicit double, never the real apps root.
        with patch('recursive_acl_restore_unknown_physical.APPS', self.apps):
            return rename_owned(self.tree, self.reader, self.data, self.acl, self.before)

    def test_fixed_atomic_rename_and_exact_cleanup_keep_inode_and_bytes(self):
        after, proof = self.rename()
        self.assertFalse((self.tree / OLD).exists())
        self.assertEqual(after[NEW]['identity'][:8], self.before[OLD]['identity'][:8])
        self.assertTrue(proof['originalInodeRetained'])
        self.assertEqual(inventory(self.tree, self.reader, self.data, self.acl), after)
        with patch('recursive_acl_restore_unknown_physical.APPS', self.apps):
            remove_proved(self.tree, self.reader, self.data, self.acl, after)
        self.assertFalse(self.tree.exists())

    def test_unowned_or_symlinked_root_is_refused_before_rename(self):
        with self.assertRaises(AssertionError):
            owned_root(self.tree)
        saved = self.source / 'saved-tree'
        self.tree.rename(saved)
        self.tree.symlink_to(saved, target_is_directory=True)
        with patch('recursive_acl_restore_unknown_physical.APPS', self.apps), self.assertRaises(AssertionError):
            owned_root(self.tree)
        self.assertTrue((self.tree / OLD).is_file())

    def test_replacement_extra_file_or_existing_destination_is_not_overwritten(self):
        (self.tree / NEW).write_bytes(b'not ours')
        with self.assertRaises(AssertionError):
            self.rename()
        self.assertEqual((self.tree / NEW).read_bytes(), b'not ours')
        (self.tree / NEW).unlink()
        (self.tree / OLD).write_bytes(b'changed')
        with self.assertRaises(AssertionError):
            self.rename()
        self.assertFalse((self.tree / NEW).exists())

    def test_cleanup_refuses_changed_bytes_and_retains_every_other_file(self):
        after, _ = self.rename()
        (self.tree / NEW).write_bytes(b'changed after proof')
        with patch('recursive_acl_restore_unknown_physical.APPS', self.apps), self.assertRaises(AssertionError):
            remove_proved(self.tree, self.reader, self.data, self.acl, after)
        self.assertEqual(len(list(self.tree.iterdir())), 25)

    def test_stale_inode_cannot_be_renamed(self):
        value = copy.deepcopy(self.before)
        value[OLD]['identity'][1] += 1
        self.before = value
        with self.assertRaises(AssertionError):
            self.rename()
        self.assertTrue((self.tree / OLD).exists())

    def test_real_hardlink_or_symlink_leaf_is_refused_before_mutation(self):
        other = self.source / 'other-link'
        os.link(self.tree / OLD, other)
        with self.assertRaises(AssertionError):
            self.rename()
        other.unlink()
        (self.tree / OLD).unlink()
        (self.tree / OLD).symlink_to(self.tree / FILES[0])
        with self.assertRaises(AssertionError):
            self.rename()
        self.assertFalse((self.tree / NEW).exists())

    def test_destination_created_after_precheck_is_never_overwritten(self):
        from recursive_acl_restore_unknown_physical import _rename_no_replace
        def late_destination(fd):
            (self.tree / NEW).write_bytes(b'late destination')
            _rename_no_replace(fd)
        with patch('recursive_acl_restore_unknown_physical._rename_no_replace', side_effect=late_destination), self.assertRaises(FileExistsError):
            self.rename()
        self.assertEqual((self.tree / NEW).read_bytes(), b'late destination')
        self.assertEqual((self.tree / OLD).read_bytes(), self.data)


if __name__ == '__main__':
    unittest.main()
