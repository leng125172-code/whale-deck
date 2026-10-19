"""Real filesystem checks of the QA observer, NOT Agent/BFF/UI acceptance.

Root-only isolated temporary roots; receipts are explicit test doubles. Verify
the observer's own success and refusal-before-delete behavior before live QA.
"""
import contextlib
import importlib.util
import io
import json
import os
from pathlib import Path
import stat
import tarfile
import tempfile
import unittest
from unittest.mock import patch
import uuid


@unittest.skipUnless(os.name == 'posix' and os.geteuid() == 0, 'isolated observer ownership tests require Linux root')
class UnpackingObserverTests(unittest.TestCase):
    def setUp(self):
        spec = importlib.util.spec_from_file_location('unpacking_browser_observer_test_target', Path(__file__).with_name('unpacking_browser_fixture.py'))
        self.module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(self.module)
        self.scratch = tempfile.TemporaryDirectory(prefix='wd-unpack-observer-')
        self.directory = Path(self.scratch.name).resolve()
        self.module.BASE = self.directory / 'files'
        self.module.BASE.mkdir()
        self.receipts = self.directory / 'receipts'
        self.receipts.mkdir()
        self.registry = self.directory / 'registry.json'
        self.registry.write_text(json.dumps([{'id': self.module.ROOT, 'path': str(self.module.BASE), 'ownerUid': 0, 'ownerGid': 0}]))
        self.registry.chmod(0o600)
        def routed(value):
            if str(value) == '/etc/whaledeck/file-roots.json':
                return self.registry
            if str(value) == '/var/lib/whaledeck-files/tree-unpacking':
                return self.receipts
            return Path(value)
        self.router = patch.object(self.module, 'Path', routed)
        self.router.start()
        self.raw = self.call({'action': 'create'})
        self.fixture = json.loads(self.raw)
        self.folder = self.module.BASE / self.fixture['marker']
        (self.folder / '.whaledeck-files').mkdir(mode=0o700)
        self.operations = []
        for theme, archive in self.fixture['archives'].items():
            target = self.folder / ('unpacked-' + theme)
            target.mkdir()
            directories = []
            # Explicit synthetic materialization; never counted as a product
            # extraction or installed Agent proof and no extractall is used.
            with tarfile.open(self.folder / archive['name']) as reader:
                for member in reader.getmembers():
                    relative = '' if member.name == '.' else member.name
                    path = target / relative
                    if member.isdir():
                        path.mkdir(exist_ok=True)
                        directories.append((path, member))
                    else:
                        path.write_bytes(self.module.payload(self.fixture['marker']))
                        self.metadata(path, member)
                for path, member in reversed(directories):
                    self.metadata(path, member)
            identifier = uuid.uuid4()
            record = {'operationId': identifier.hex, 'state': 'Completed', 'version': 'F' * 64,
                'newIdentity': [target.stat().st_dev, target.stat().st_ino],
                'plan': {'command': {'source': {'rootId': self.module.ROOT, 'path': self.fixture['marker'] + '/' + archive['name'], 'expectedVersion': 'B' * 64},
                    'target': {'rootId': self.module.ROOT, 'path': self.fixture['marker'] + '/unpacked-' + theme}, 'format': archive['format']},
                    'archiveBytes': archive['identity'][5], 'archiveSha256': archive['identity'][-1], 'entryCount': 4, 'totalBytes': self.module.SIZE}}
            receipt = self.receipts / (identifier.hex + '.json')
            receipt.write_text(json.dumps(record))
            receipt.chmod(0o600)
            self.operations.append({'theme': theme, 'operationId': str(identifier), 'format': archive['format'],
                'archiveBytes': archive['identity'][5], 'archiveSha256': archive['identity'][-1], 'publishedVersion': 'F' * 64})

    def metadata(self, path, member):
        nanos = int(self.module.Decimal(member.pax_headers.get('mtime', str(member.mtime))) * 1000000000)
        path.chmod(member.mode & 0o777)
        os.utime(path, ns=(nanos, nanos))

    def call(self, value):
        output = io.StringIO()
        incoming = io.TextIOWrapper(io.BytesIO(json.dumps(value).encode()), encoding='utf-8')
        with patch('sys.stdin', incoming), contextlib.redirect_stdout(output):
            self.module.main()
        return output.getvalue().strip()

    def finish(self):
        return self.call({'action': 'finish', 'fixture': self.raw, 'unpackings': self.operations})

    def assert_preserved(self):
        self.assertTrue(self.folder.is_dir())
        for archive in self.fixture['archives'].values():
            self.assertTrue((self.folder / archive['name']).is_file())
        for theme in ('light', 'dark'):
            self.assertTrue((self.folder / ('unpacked-' + theme) / self.module.MEMBER).is_file())
        self.assertEqual(len(list(self.receipts.iterdir())), 2)

    def tearDown(self):
        self.router.stop()
        # Framework-owned, resolved exact temporary tree only; never user data.
        self.assertEqual(self.directory.parent, Path(tempfile.gettempdir()).resolve())
        self.assertTrue(self.directory.name.startswith('wd-unpack-observer-'))
        self.scratch.cleanup()

    def test_complete_proof_removes_only_exact_fixture_but_keeps_receipts(self):
        result = json.loads(self.finish())
        self.assertEqual(result['realBrowserUnpackingIndependentProof'], 'passed')
        self.assertFalse(self.folder.exists())
        self.assertEqual(len(list(self.receipts.iterdir())), 2)

    def test_changed_payload_refuses_before_any_deletion(self):
        (self.folder / 'unpacked-light' / self.module.MEMBER).write_bytes(b'changed')
        with self.assertRaises(AssertionError):
            self.finish()
        self.assert_preserved()

    def test_unknown_member_refuses_before_any_deletion(self):
        (self.folder / 'unexpected').write_bytes(b'unknown')
        with self.assertRaises(AssertionError):
            self.finish()
        self.assert_preserved()

    def test_private_partial_object_is_never_implicitly_deleted(self):
        (self.folder / '.whaledeck-files/retained').write_bytes(b'partial')
        with self.assertRaises(AssertionError):
            self.finish()
        self.assert_preserved()

    def test_rounded_original_nanoseconds_cannot_authorize_cleanup(self):
        changed = json.loads(self.raw)
        changed['archives']['light']['identity'][6] += 1
        self.raw = json.dumps(changed)
        with self.assertRaises(AssertionError):
            self.finish()
        self.assert_preserved()

    def test_multiline_transport_is_rejected_before_any_deletion(self):
        incoming = io.TextIOWrapper(io.BytesIO(json.dumps({'action': 'finish', 'fixture': self.raw, 'unpackings': self.operations}, indent=2).encode()), encoding='utf-8')
        with patch('sys.stdin', incoming), contextlib.redirect_stdout(io.StringIO()), self.assertRaises(json.JSONDecodeError):
            self.module.main()
        self.assert_preserved()


if __name__ == '__main__':
    unittest.main()
