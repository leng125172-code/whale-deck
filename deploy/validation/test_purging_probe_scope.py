"""Independent observer scope gates; temporary Linux I/O, never installed RPC.

Mountinfo is a named test double: no real mount or host cleanup is performed.
Only explicit TemporaryDirectory-owned fixtures are passed to cleanup.
"""
import contextlib
import json
import os
from pathlib import Path
import stat
import tempfile
import unittest
from unittest.mock import patch
import validate_tree_purging_agent as observer


class PurgingProbeSourceTests(unittest.TestCase):
    def test_probe_is_a_separate_installed_rpc_mode_with_private_key_zeroing(self):
        directory = Path(__file__).parent / 'TerminalProbe'
        program = (directory / 'Program.cs').read_text(encoding='utf-8-sig')
        source = (directory / 'FileTreePurgingProbe.cs').read_text(encoding='utf-8-sig')
        self.assertIn('args[0] == "tree-purge"', program)
        self.assertIn('args is ["tree-purge"] ? FileTreePurgingProbe.Phase', program)
        self.assertIn('FileTreePurgingService.FileTreePurgingServiceClient(channel)', source)
        self.assertIn('UnixDomainSocketEndPoint("/run/whaledeck/agent.sock")', source)
        self.assertIn('finally { CryptographicOperations.ZeroMemory(key); }', source)
        self.assertIn('independentDiskObserver = "required"', source)
        self.assertIn('bffWorkerHumanAuditAndUi = "not-covered"', source)
        self.assertIn('actualInterruption = "not-covered"', source)
        for forbidden in ('File.Delete', 'Directory.Delete', 'docker.sock', 'Process.Start', 'Console.WriteLine(key'):
            self.assertNotIn(forbidden, source)

    def test_both_observer_acknowledgements_precede_their_irreversible_rpc_phases(self):
        directory = Path(__file__).parent
        probe = (directory / 'TerminalProbe/FileTreePurgingProbe.cs').read_text(encoding='utf-8-sig')
        first = probe.index('await Checkpoint("attested-captures")')
        archive = probe.index('await Purge(capture, FileTreePurgeScope.Archive)')
        second = probe.index('await Checkpoint("attested-archives")')
        originals = probe.index('await Purge(capture, capture.Name.StartsWith')
        self.assertLess(first, archive); self.assertLess(archive, second); self.assertLess(second, originals)
        self.assertIn('Console.In.ReadLineAsync().WaitAsync(TimeSpan.FromMinutes(10)) == expected', probe)
        source = (directory / 'validate_tree_purging_agent.py').read_text(encoding='utf-8-sig')
        self.assertLess(source.index("preserve_evidence(evidence, 'captures.json', first)"), source.index("process.stdin.write('attested-captures\\n')"))
        self.assertLess(source.index("preserve_evidence(evidence, 'archives-purged.json', second)"), source.index("process.stdin.write('attested-archives\\n')"))
        self.assertIn('exact_cleanup(fixture, identity, private_proofs, allowed_private)', source)
        self.assertNotIn('shutil.rmtree', source)


@unittest.skipUnless(os.name == 'posix' and hasattr(os, 'geteuid') and os.geteuid() == 0,
                     'requires real isolated Linux root metadata and I/O')
class PurgingProbeScopeTests(unittest.TestCase):
    @contextlib.contextmanager
    def fixture(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            fixture = root / ('wdv-agent-purge-' + 'a' * 16)
            fixture.mkdir(mode=0o700)
            for name in ('small-target', 'large-target'):
                folder = fixture / name; folder.mkdir(mode=0o750)
                (folder / '空目录').mkdir(mode=0o710)
                data = folder / '中文.bin'; data.write_bytes(b'owned-output\0\xff'); data.chmod(0o640)
            (fixture / '.whaledeck-files').mkdir(mode=0o700)
            private = root / 'owned-receipt.json'; private.write_bytes(b'{"owned":true}'); private.chmod(0o600)
            with patch.object(observer, 'DIRECTORY', root):
                yield root, fixture, private

    def test_fixture_scope_requires_exact_parent_name_owner_mode_and_real_directory(self):
        with self.fixture() as (root, fixture, _):
            observer.fixture_scope(fixture)
            for bad in (root, root / 'wdv-agent-purge-abc', root / ('wdv-agent-purge-' + 'A' * 16),
                        root / (fixture.name + '\n'), root.parent / fixture.name):
                with self.subTest(path=str(bad)), self.assertRaises(AssertionError): observer.fixture_scope(bad)
            fixture.chmod(0o750)
            with self.assertRaises(AssertionError): observer.fixture_scope(fixture)
            fixture.chmod(0o700)
            try:
                os.chown(fixture, 12345, 0)
                with self.assertRaises(AssertionError): observer.fixture_scope(fixture)
            finally:
                os.chown(fixture, 0, 0)
            linked = root / ('wdv-agent-purge-' + 'b' * 16); linked.symlink_to(fixture, target_is_directory=True)
            with self.assertRaises(AssertionError): observer.fixture_scope(linked)

    def test_evidence_is_private_exclusive_bounded_and_rejects_existing_symlinks(self):
        with tempfile.TemporaryDirectory() as temporary:
            repository = Path(temporary)
            folder = repository / 'deploy/artifacts' / ('purge-evidence-' + 'a' * 16)
            folder.mkdir(mode=0o700, parents=True)
            outsider = repository / 'outside'; outsider.write_bytes(b'preserve')
            with patch.object(observer, 'REPOSITORY', repository), patch('builtins.print'):
                observer.preserve_evidence(folder, 'baseline.json', {'owned': True, '中文': '证据'})
                saved = folder / 'baseline.json'
                self.assertEqual(stat.S_IMODE(saved.stat().st_mode), 0o600)
                self.assertEqual(json.loads(saved.read_bytes()), {'owned': True, '中文': '证据'})
                before = saved.read_bytes()
                with self.assertRaises(FileExistsError): observer.preserve_evidence(folder, 'baseline.json', {})
                self.assertEqual(saved.read_bytes(), before)
                (folder / 'rpc-result.json').symlink_to(outsider)
                with self.assertRaises((FileExistsError, AssertionError)): observer.preserve_evidence(folder, 'rpc-result.json', {})
                self.assertEqual(outsider.read_bytes(), b'preserve')
                for name in ('../escape.json', 'unknown.json', '/tmp/escape.json'):
                    with self.subTest(name=name), self.assertRaises(AssertionError): observer.preserve_evidence(folder, name, {})
                with self.assertRaises(AssertionError): observer.preserve_evidence(folder, 'verified.json', {'tooLarge': 'a' * 65536})
                with self.assertRaises(ValueError): observer.preserve_evidence(folder, 'verified.json', {'invalid': float('nan')})
                self.assertFalse((folder / 'verified.json').exists())

    def test_evidence_folder_must_be_root_private_exact_and_not_a_symlink(self):
        with tempfile.TemporaryDirectory() as temporary:
            repository = Path(temporary)
            artifacts = repository / 'deploy/artifacts'; artifacts.mkdir(parents=True)
            folder = artifacts / ('purge-evidence-' + 'a' * 16); folder.mkdir(mode=0o700)
            with patch.object(observer, 'REPOSITORY', repository), patch('builtins.print'):
                for mode in (0o750, 0o777):
                    folder.chmod(mode)
                    with self.assertRaises(AssertionError): observer.preserve_evidence(folder, 'baseline.json', {})
                folder.chmod(0o700)
                try:
                    os.chown(folder, 12345, 0)
                    with self.assertRaises(AssertionError): observer.preserve_evidence(folder, 'baseline.json', {})
                finally: os.chown(folder, 0, 0)
                linked = artifacts / ('purge-evidence-' + 'b' * 16); linked.symlink_to(folder, target_is_directory=True)
                with self.assertRaises(AssertionError): observer.preserve_evidence(linked, 'baseline.json', {})
                wrong = artifacts / 'wrong-name'; wrong.mkdir(mode=0o700)
                with self.assertRaises(AssertionError): observer.preserve_evidence(wrong, 'baseline.json', {})
                self.assertFalse((folder / 'baseline.json').exists())

    def test_exact_cleanup_removes_only_complete_fixture_and_explicit_private_set(self):
        with self.fixture() as (root, fixture, private):
            untouched = root / 'not-allowed.json'; untouched.write_bytes(b'never remove')
            identity = observer.proof(fixture)[:2]
            private_proofs = {private: observer.proof(private)}
            observer.exact_cleanup(fixture, identity, private_proofs, {private})
            self.assertFalse(fixture.exists()); self.assertFalse(private.exists())
            self.assertEqual(untouched.read_bytes(), b'never remove')

    def test_cleanup_refuses_identity_foreign_objects_nonempty_private_and_private_changes_before_deleting(self):
        for mismatch in ('identity', 'foreign-top', 'foreign-nested', 'missing-target', 'private-not-empty', 'private-change', 'private-owner',
                         'private-not-allowed', 'allowed-not-proven'):
            with self.subTest(mismatch=mismatch), self.fixture() as (root, fixture, private):
                identity = observer.proof(fixture)[:2]
                private_proofs = {private: observer.proof(private)}; allowed = {private}
                payload = fixture / 'small-target/中文.bin'
                if mismatch == 'identity': identity = (identity[0], identity[1] + 1)
                elif mismatch == 'foreign-top': (fixture / 'foreign').write_bytes(b'preserve foreign')
                elif mismatch == 'foreign-nested': (fixture / 'small-target/foreign').write_bytes(b'preserve nested foreign')
                elif mismatch == 'missing-target': (fixture / 'large-target').rename(fixture / 'other-target')
                elif mismatch == 'private-not-empty': (fixture / '.whaledeck-files/unknown').write_bytes(b'preserve')
                elif mismatch == 'private-change': private.write_bytes(b'changed receipt')
                elif mismatch == 'private-owner': os.chown(private, 12345, 0)
                elif mismatch == 'private-not-allowed': allowed = set()
                else: allowed.add(root / 'unproven.json')
                try:
                    with self.assertRaises(AssertionError): observer.exact_cleanup(fixture, identity, private_proofs, allowed)
                    self.assertEqual(payload.read_bytes(), b'owned-output\0\xff')
                    self.assertTrue(private.exists()); self.assertTrue(fixture.exists())
                finally:
                    if mismatch == 'private-owner': os.chown(private, 0, 0)

    def test_cleanup_rejects_symlink_payload_or_replaced_private_file_without_following(self):
        for target in ('payload', 'private'):
            with self.subTest(target=target), self.fixture() as (root, fixture, private):
                identity = observer.proof(fixture)[:2]; private_proofs = {private: observer.proof(private)}
                outsider = root / 'outsider'; outsider.write_bytes(b'preserve outsider')
                selected = fixture / 'small-target/中文.bin' if target == 'payload' else private
                selected.unlink(); selected.symlink_to(outsider)
                with self.assertRaises(AssertionError): observer.exact_cleanup(fixture, identity, private_proofs, {private})
                self.assertTrue(selected.is_symlink()); self.assertEqual(outsider.read_bytes(), b'preserve outsider')
                self.assertTrue((fixture / 'large-target/中文.bin').exists())

    def test_cleanup_rejects_actual_hardlinked_payload_and_private_file(self):
        # No fake stat: nlink=2 is real, even when both aliases are test-owned.
        for target in ('payload', 'private'):
            with self.subTest(target=target), self.fixture() as (root, fixture, private):
                selected = fixture / 'small-target/中文.bin' if target == 'payload' else private
                alias = root / 'outside-alias'; os.link(selected, alias)
                identity = observer.proof(fixture)[:2]; private_proofs = {private: observer.proof(private)}
                with self.assertRaises(AssertionError): observer.exact_cleanup(fixture, identity, private_proofs, {private})
                self.assertTrue(selected.exists()); self.assertTrue(alias.exists()); self.assertTrue(private.exists())
                self.assertTrue((fixture / 'large-target/中文.bin').exists())

    def test_cleanup_rejects_fixture_and_nested_mountpoints_before_deleting(self):
        original_read = Path.read_text
        for nested in ('', '/small-target'):
            with self.subTest(nested=nested), self.fixture() as (_, fixture, private):
                identity = observer.proof(fixture)[:2]; private_proofs = {private: observer.proof(private)}
                def mountinfo(path, *args, **kwargs):
                    if path == Path('/proc/self/mountinfo'):
                        return f'1 2 0:3 / {fixture}{nested} rw - tmpfs tmpfs rw\n'
                    return original_read(path, *args, **kwargs)
                with patch.object(Path, 'read_text', mountinfo), self.assertRaises(AssertionError):
                    observer.exact_cleanup(fixture, identity, private_proofs, {private})
                self.assertTrue(private.exists()); self.assertTrue((fixture / 'small-target/中文.bin').exists())


if __name__ == '__main__': unittest.main()
