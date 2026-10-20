"""Root-only isolated filesystem tests of the QA observer, not product evidence."""
import contextlib
import importlib.util
import io
import json
import os
from pathlib import Path
import shutil
import tarfile
import tempfile
import unittest
from unittest.mock import patch
import uuid


@unittest.skipUnless(os.name == 'posix' and os.geteuid() == 0, 'requires isolated Linux root')
class RestoringObserverTests(unittest.TestCase):
    def setUp(self):
        spec = importlib.util.spec_from_file_location('restoring_browser_observer_tests', Path(__file__).with_name('restoring_browser_fixture.py'))
        self.module = importlib.util.module_from_spec(spec); spec.loader.exec_module(self.module)
        self.scratch = tempfile.TemporaryDirectory(prefix='wd-restore-observer-')
        self.directory = Path(self.scratch.name).resolve()
        self.module.BASE = self.directory / 'files'; self.module.BASE.mkdir()
        self.module.STATE = self.directory / 'receipts'
        for kind in ('tree-restoring', 'tree-recycling'): (self.module.STATE / kind).mkdir(parents=True)
        self.module.POLICY = self.directory / 'roots.json'
        self.module.POLICY.write_text(json.dumps([{'id': self.module.ROOT, 'path': str(self.module.BASE), 'ownerUid': 0, 'ownerGid': 0, 'allowedActions': ['files-tree-trash', 'files-tree-restore']}]))
        self.module.BACKUPS = self.directory / 'backups.json'
        self.module.BACKUPS.write_text(json.dumps({'mountPoint': str(self.directory), 'targetRelative': 'store'}))
        self.store = self.directory / 'store/directory-archives'; self.store.mkdir(parents=True)
        self.raw = self.call({'action': 'create'}); self.fixture = json.loads(self.raw)
        self.folder = self.module.BASE / self.fixture['marker']; namespace = self.folder / '.whaledeck-files'; namespace.mkdir(mode=0o700)
        self.operations = []
        for theme, source in self.fixture['sources'].items():
            capture, operation = uuid.uuid4(), uuid.uuid4()
            original = self.folder / source['name']; private = namespace / (capture.hex + '.tree-trash')
            original.rename(private); private.chmod(0o700)
            archive = self.store / (capture.hex + '.tar.gz')
            with tarfile.open(archive, 'w:gz', format=tarfile.PAX_FORMAT) as writer:
                writer.add(private, arcname='.', recursive=False)
                for relative in ('空目录', '代码', self.module.MEMBER):
                    writer.add(private / relative, arcname=relative, recursive=False)
            archive.chmod(0o600); archive_proof = self.module.proof(archive)
            location = {'rootId': self.module.ROOT, 'path': self.fixture['marker'] + '/' + source['name']}
            owner, manifest = 'C' * 64, 'D' * 64
            saved = {'state': 'Verified', 'source': location, 'owner': owner, 'manifestSha256': manifest, 'differentSourceDevice': False,
                'archiveIdentity': archive_proof[:2], 'archiveBytes': archive_proof[6], 'archiveSha256': archive_proof[-1]}
            self.write(self.store / (capture.hex + '.json'), saved)
            self.write(self.store / (capture.hex + '.manifest.json'), {'fixture': True})
            self.write(self.store / (capture.hex + '.lock'), {})
            self.write(self.module.STATE / 'tree-recycling' / (capture.hex + '.json'), {'state': 'Completed', 'operationId': capture.hex,
                'sourceRemoved': True, 'sourceRetained': True, 'retainedIdentity': self.module.proof(private)[:2], 'owner': owner,
                'plan': {'source': location, 'entryCount': 4, 'totalBytes': self.module.SIZE, 'manifestSha256': manifest},
                'archiveBytes': archive_proof[6], 'archiveSha256': archive_proof[-1]})
            target = source['name'] if theme == 'light' else 'restored-dark'
            output = self.folder / target
            # Explicit test double for output, never counted as product restoration.
            shutil.copytree(private, output); output.chmod(0o750)
            self.write(self.module.STATE / 'tree-restoring' / (operation.hex + '.json'), {'operationId': operation.hex, 'state': 'Completed', 'version': 'F' * 64,
                'owner': owner if theme == 'light' else 'E' * 64,
                'newIdentity': self.module.proof(output)[:2], 'plan': {'entryCount': 4, 'totalBytes': self.module.SIZE,
                    'archiveBytes': archive_proof[6], 'archiveSha256': archive_proof[-1], 'command': {'source': location,
                        'target': {'rootId': self.module.ROOT, 'path': self.fixture['marker'] + '/' + target}, 'captureId': capture.hex,
                        'captureOwner': owner, 'captureKind': 'Trash', 'manifestSha256': manifest, 'format': 'tar.gz'}}})
            self.operations.append({'theme': theme, 'captureId': str(capture), 'restoringOperationId': str(operation),
                'archiveBytes': archive_proof[6], 'archiveSha256': archive_proof[-1], 'publishedVersion': 'F' * 64})

    def write(self, path, value):
        path.write_text(json.dumps(value)); path.chmod(0o600)

    def call(self, value):
        output = io.StringIO(); incoming = io.TextIOWrapper(io.BytesIO(json.dumps(value).encode()), encoding='utf-8')
        with patch('sys.stdin', incoming), contextlib.redirect_stdout(output): self.module.main()
        incoming.close()
        return output.getvalue().strip()

    def finish(self, action='finish'):
        return self.call({'action': action, 'fixture': self.raw, 'operations': self.operations})

    def assert_preserved(self):
        self.assertTrue(self.folder.is_dir())
        self.assertTrue((self.folder / self.fixture['sources']['light']['name'] / self.module.MEMBER).is_file())
        self.assertEqual(len(list(self.store.iterdir())), 8)
        self.assertEqual(len(list((self.module.STATE / 'tree-restoring').iterdir())), 2)

    def tearDown(self):
        self.assertEqual(self.directory.parent, Path(tempfile.gettempdir()).resolve())
        self.assertTrue(self.directory.name.startswith('wd-restore-observer-'))
        self.scratch.cleanup()

    def test_inspect_proves_everything_but_deletes_nothing(self):
        result = json.loads(self.finish('inspect'))
        self.assertEqual(result['realBrowserDirectoryRestoringDiskObserver'], 'passed')
        self.assertFalse(result['exactOwnedPayloadsRemoved']); self.assert_preserved()

    def test_complete_proof_removes_only_owned_payloads_and_retains_receipts(self):
        self.assertTrue(json.loads(self.finish())['exactOwnedPayloadsRemoved'])
        self.assertFalse(self.folder.exists()); self.assertEqual(list(self.store.iterdir()), [])
        self.assertEqual(len(list((self.module.STATE / 'tree-restoring').iterdir())), 2)
        self.assertEqual(len(list((self.module.STATE / 'tree-recycling').iterdir())), 2)

    def test_changed_output_refuses_before_any_deletion(self):
        (self.folder / 'restored-dark' / self.module.MEMBER).write_bytes(b'changed')
        with self.assertRaises(AssertionError): self.finish()
        self.assert_preserved()

    def test_foreign_object_refuses_before_any_deletion(self):
        (self.folder / 'foreign').write_bytes(b'unknown')
        with self.assertRaises(AssertionError): self.finish()
        self.assert_preserved()

    def test_original_inode_metadata_must_be_exact_not_rounded(self):
        changed = json.loads(self.raw); changed['sources']['light']['identity'][7] += 1; self.raw = json.dumps(changed)
        with self.assertRaises(AssertionError): self.finish()
        self.assert_preserved()

    def test_changed_archive_refuses_before_any_deletion(self):
        (self.store / (uuid.UUID(self.operations[1]['captureId']).hex + '.tar.gz')).write_bytes(b'changed')
        with self.assertRaises(AssertionError): self.finish()
        self.assert_preserved()

    def test_repeated_operation_cannot_substitute_the_capture(self):
        self.operations[1]['restoringOperationId'] = self.operations[0]['restoringOperationId']
        with self.assertRaises(AssertionError): self.finish()
        self.assert_preserved()

    def test_dark_restore_must_not_impersonate_the_original_capture_owner(self):
        path = self.module.STATE / 'tree-restoring' / (uuid.UUID(self.operations[1]['restoringOperationId']).hex + '.json')
        value = json.loads(path.read_bytes()); value['owner'] = 'C' * 64; self.write(path, value)
        with self.assertRaises(AssertionError): self.finish()
        self.assert_preserved()


if __name__ == '__main__': unittest.main()
