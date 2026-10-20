"""Test the observer's exact scope and no-delete-on-mismatch gates, never RPC."""
import os
from pathlib import Path
import stat
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch
import validate_tree_overwrite_restoring_agent as observer


class OverwriteProbeScopeTests(unittest.TestCase):
    def test_fixture_name_parent_owner_and_mode_are_strict(self):
        root = observer.DIRECTORY
        good = root / ('wdv-agent-overwrite-' + 'a' * 16)
        with patch.object(Path, 'resolve', lambda value: value), patch.object(Path, 'is_dir', return_value=True), patch.object(Path, 'lstat', return_value=SimpleNamespace(st_uid=0, st_mode=stat.S_IFDIR | 0o700)):
            observer.fixture_scope(good)
            for invalid in (root, root / 'wdv-agent-overwrite-abc', root / ('wdv-agent-overwrite-' + 'a' * 16 + '\n'), root.parent / good.name):
                with self.subTest(path=str(invalid)), self.assertRaises(AssertionError): observer.fixture_scope(invalid)
            with patch.object(Path, 'lstat', return_value=SimpleNamespace(st_uid=1000, st_mode=stat.S_IFDIR | 0o700)), self.assertRaises(AssertionError): observer.fixture_scope(good)
            with patch.object(Path, 'lstat', return_value=SimpleNamespace(st_uid=0, st_mode=stat.S_IFDIR | 0o750)), self.assertRaises(AssertionError): observer.fixture_scope(good)

    def test_program_has_separate_mode_phase_and_bounded_public_output(self):
        source = Path(__file__).parent / 'TerminalProbe'
        program = (source / 'Program.cs').read_text(encoding='utf-8-sig')
        probe = (source / 'FileTreeOverwriteRestoringProbe.cs').read_text(encoding='utf-8-sig')
        self.assertIn('args[0] == "tree-overwrite-restore"', program)
        self.assertIn('args is ["tree-overwrite-restore"] ? FileTreeOverwriteRestoringProbe.Phase', program)
        self.assertIn('CryptographicOperations.ZeroMemory(key)', probe)
        self.assertIn('independentDiskObserver = "required"', probe)
        self.assertNotIn('File.Delete', probe)
        self.assertNotIn('Directory.Delete', probe)
        self.assertNotIn('docker.sock', probe)

    @unittest.skipUnless(hasattr(os, 'geteuid') and os.geteuid() == 0, 'Linux root filesystem metadata required')
    def test_exact_owned_cleanup_and_mismatches(self):
        for mismatch in ('none', 'identity', 'children', 'private-change', 'symlink', 'mount'):
            with self.subTest(mismatch=mismatch), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                fixture = root / ('wdv-agent-overwrite-' + 'a' * 16)
                fixture.mkdir(mode=0o700)
                payload = fixture / 'payload'
                payload.write_bytes(b'owned-fixture')
                private = root / 'owned.json'
                private.write_bytes(b'owned-receipt')
                identity = observer.proof(fixture)[:2]
                private_proofs = {private: observer.proof(private)}
                if mismatch == 'identity': identity = (identity[0], identity[1] + 1)
                if mismatch == 'children': (fixture / 'unexpected').write_bytes(b'preserve')
                if mismatch == 'private-change': private.write_bytes(b'changed-receipt')
                if mismatch == 'symlink': payload.unlink(); payload.symlink_to(private)
                with patch.object(observer, 'DIRECTORY', root):
                    if mismatch == 'none':
                        observer.exact_cleanup(fixture, identity, {'payload'}, private_proofs)
                        self.assertFalse(fixture.exists()); self.assertFalse(private.exists())
                    elif mismatch == 'mount':
                        with patch.object(Path, 'read_text', return_value=f'1 2 3 4 {fixture} x\n'), self.assertRaises(AssertionError): observer.exact_cleanup(fixture, identity, {'payload'}, private_proofs)
                        self.assertTrue(payload.exists()); self.assertTrue(private.exists())
                    else:
                        with self.assertRaises(AssertionError): observer.exact_cleanup(fixture, identity, {'payload'}, private_proofs)
                        self.assertTrue(fixture.exists()); self.assertTrue(private.exists())


if __name__ == '__main__': unittest.main()
