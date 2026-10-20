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
    def test_retained_skeleton_and_displaced_root_have_distinct_identity_rules(self):
        retained = Path('/owned/retained')
        original_tree = (1, 10, stat.S_IFDIR | 0o750, 0, 0, 3, 4096, 100, 101, None)
        skeleton = (1, 20, stat.S_IFDIR | 0o700, 0, 0, 3, 4096, 102, 103, None)
        original_data = (1, 30, stat.S_IFREG | 0o640, 0, 0, 1, 7, 104, 105, 'A' * 64)
        leaves = [retained / '空目录', retained / '中文.bin']
        def check(kind, root, data=original_data, identity=None):
            with patch.object(Path, 'resolve', lambda value: value), patch.object(Path, 'rglob', return_value=iter(leaves)), patch.object(observer, 'proof', side_effect=lambda path: root if path == retained else data):
                observer.attest_retained(retained, kind, original_tree, original_data, identity or root[:2])
        check('source', skeleton)
        check('target', original_tree)
        for kind, root, data, identity in (
            ('target', skeleton, original_data, None),
            ('source', skeleton, original_data, original_tree[:2]),
            ('source', (*skeleton[:3], 1000, *skeleton[4:]), original_data, None),
            ('source', original_tree, original_data, None),
            ('source', skeleton, (1, 31, *original_data[2:]), None),
            ('source', skeleton, (*original_data[:7], 999, *original_data[8:]), None),
            ('target', original_tree, (*original_data[:-1], 'B' * 64), None),
            ('unknown', skeleton, original_data, None)):
            with self.subTest(kind=kind, root=root, data=data), self.assertRaises(AssertionError): check(kind, root, data, identity)

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
    def test_evidence_is_private_exclusive_and_never_follows_an_existing_link(self):
        with tempfile.TemporaryDirectory() as temporary:
            repository = Path(temporary)
            folder = repository / 'deploy/artifacts' / ('overwrite-evidence-' + 'a' * 16)
            folder.mkdir(mode=0o700, parents=True)
            with patch.object(observer, 'REPOSITORY', repository), patch('builtins.print'):
                observer.preserve_evidence(folder, 'baseline.json', {'owned': True})
                self.assertEqual(stat.S_IMODE((folder / 'baseline.json').stat().st_mode), 0o600)
                with self.assertRaises(FileExistsError): observer.preserve_evidence(folder, 'baseline.json', {'changed': True})
                external = repository / 'preserve.txt'; external.write_bytes(b'untouched')
                (folder / 'rpc-result.json').symlink_to(external)
                with self.assertRaises(FileExistsError): observer.preserve_evidence(folder, 'rpc-result.json', {'changed': True})
                self.assertEqual(external.read_bytes(), b'untouched')
                with self.assertRaises(AssertionError): observer.preserve_evidence(folder, '../escape.json', {})
                folder.chmod(0o750)
                with self.assertRaises(AssertionError): observer.preserve_evidence(folder, 'rpc-result.json', {})

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
