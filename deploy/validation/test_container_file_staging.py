"""Isolated Linux-root tests for private container staging, never publication.

No Docker, live resource registry, privileged helper, API or production state is
used. Target-liveness callbacks stand in for the already tested read session.
"""
import base64
from contextlib import contextmanager
import hashlib
import importlib.util
import json
import multiprocessing
import os
from pathlib import Path
import stat
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import Mock
import uuid

HOST = Path(__file__).resolve().parents[1] / 'host'


def load(filename, name):
    specification = importlib.util.spec_from_file_location(name, HOST / filename)
    module = importlib.util.module_from_spec(specification)
    specification.loader.exec_module(module)
    return module


read_engine = load('whaledeck-container-files.py', 'container_staging_read_engine')
uploads = load('whaledeck-container-upload.py', 'container_staging_upload_engine')
ID = 'a' * 64
INSTANCE = '777:2026-10-21T01:02:03.123456789Z'
OWNER = 'B' * 64


@unittest.skipUnless(os.name == 'posix' and hasattr(os, 'geteuid') and os.geteuid() == 0,
                     'requires isolated Linux root')
class LinuxContainerStagingTests(unittest.TestCase):
    def setUp(self):
        self.scratch = tempfile.TemporaryDirectory(prefix='wd-container-stage-')
        self.addCleanup(self.scratch.cleanup)
        self.base = Path(self.scratch.name).resolve()
        self.root = self.base / 'root'
        self.root.mkdir()
        self.state = self.base / 'state'
        self.state.mkdir(mode=0o700)
        self.files = load('whaledeck-files.py', 'container_staging_primitives')
        self.folder = os.open(self.root, os.O_RDONLY | os.O_DIRECTORY)
        self.addCleanup(os.close, self.folder)
        self.session = SimpleNamespace(root=self.folder, request={'containerId': ID, 'instanceId': INSTANCE},
            mounts=b'M' * 32, identity=('12345', 777, 888), current=Mock(), revalidate=Mock())
        self.engine = uploads.ContainerUploads(read_engine, self.files, self.state)
        self.content = hashlib.shake_256(b'container-private-stage-full-content-v1').digest(2 * 1024**2 + 7)
        self.original = b'original source must remain byte-for-byte intact\n'
        self.leaf = self.root / '中文.bin'
        self.leaf.write_bytes(self.original)
        self.leaf.chmod(0o640)

    def request(self, path=None, content=None, **extra):
        path = self.leaf.name if path is None else path
        content = self.content if content is None else content
        target = self.root / path
        parent_path = path.rpartition('/')[0]
        location = {'containerId': ID, 'instanceId': INSTANCE, 'path': path}
        value = {**location,
                 'expectedVersion': read_engine.version(location, path, target.lstat()) if target.exists() else 'ABSENT',
                 'expectedParentVersion': read_engine.version(location, parent_path, (self.root / parent_path).stat()),
                 'operationId': uuid.uuid4().hex, 'owner': OWNER,
                 'sizeBytes': len(content), 'sha256': hashlib.sha256(content).hexdigest().upper()}
        return {**value, **extra}

    @staticmethod
    def reference(request):
        return {key: request[key] for key in ('containerId', 'instanceId', 'operationId', 'owner')}

    @staticmethod
    def packets(content):
        return [{'type': 'chunk', 'offset': offset,
                 'data': base64.b64encode(content[offset:offset + 32768]).decode('ascii')}
                for offset in range(0, len(content), 32768)] + [{'type': 'commit'}]

    @contextmanager
    def source(self, content=None, packets=None, trailing=b''):
        # InputLines uses a real descriptor and checks exact EOF after commit.
        # A temporary regular stream avoids pipe-size/blocking assumptions.
        with tempfile.TemporaryFile() as stream:
            for row in self.packets(self.content if content is None else content) if packets is None else packets:
                stream.write(json.dumps(row, separators=(',', ':')).encode() + b'\n')
            stream.write(trailing)
            stream.seek(0)
            yield self.files.InputLines(stream.fileno())

    def stage(self, request=None, content=None, **source_options):
        request = self.request(content=content) if request is None else request
        with self.source(content=content, **source_options) as source:
            return self.engine.stage(request, self.session, source)

    def records(self):
        return list(self.state.rglob('*.json'))

    def payloads(self):
        return list(self.state.rglob('*.data'))

    def assert_original(self, fingerprint=None):
        self.assertEqual(self.original, self.leaf.read_bytes())
        if fingerprint is not None:
            self.assertEqual(fingerprint, read_engine.fingerprint(self.leaf.stat()))

    def test_real_two_mib_plus_seven_stage_has_full_sha_without_target_mutation(self):
        request = self.request()
        original = read_engine.fingerprint(self.leaf.stat())
        parent = read_engine.fingerprint(self.root.stat())
        receipt = self.stage(request)
        self.assertEqual('Staged', receipt['state'])
        self.assertEqual(len(self.content), receipt['sizeBytes'])
        self.assertEqual(hashlib.sha256(self.content).hexdigest().upper(), receipt['sha256'])
        self.assertEqual(1, len(self.payloads()))
        payload = self.payloads()[0]
        self.assertEqual(self.content, payload.read_bytes())
        self.assertEqual(0o600, stat.S_IMODE(payload.stat().st_mode))
        self.assert_original(original)
        self.assertEqual(parent, read_engine.fingerprint(self.root.stat()))
        self.assertEqual('Staged', self.engine.inspect(self.reference(request), self.session)['state'])

    def test_absent_create_is_private_and_does_not_create_target_or_change_parent(self):
        request = self.request('new-file.bin')
        parent = read_engine.fingerprint(self.root.stat())
        self.assertEqual('Staged', self.stage(request)['state'])
        self.assertFalse((self.root / request['path']).exists())
        self.assertEqual(parent, read_engine.fingerprint(self.root.stat()))
        self.assert_original()

    def test_empty_file_can_stage_without_target_publication(self):
        request = self.request('empty.bin', content=b'')
        self.assertEqual('Staged', self.stage(request, content=b'')['state'])
        self.assertEqual(b'', self.payloads()[0].read_bytes())
        self.assertFalse((self.root / 'empty.bin').exists())

    def test_mismatched_session_is_refused_before_creating_state(self):
        request = self.request()
        self.session.request = {'containerId': 'c' * 64, 'instanceId': INSTANCE}
        with self.assertRaisesRegex(read_engine.Rejected, 'SESSION_SCOPE_MISMATCH'):
            self.stage(request)
        self.assertEqual([], list(self.state.iterdir()))
        self.assert_original()

    def test_full_receipt_budget_keeps_existing_inspect_replay_and_discard_available(self):
        request = self.request()
        self.engine.MAX_RECEIPTS = 1
        self.stage(request)
        before = {path.name for path in self.state.iterdir()}
        with self.assertRaisesRegex(read_engine.Rejected, 'RECEIPT_LIMIT'):
            self.stage(self.request())
        self.assertEqual(before, {path.name for path in self.state.iterdir()})
        self.assertEqual('Staged', self.engine.inspect(self.reference(request), self.session)['state'])
        self.assertEqual('Staged', self.stage(request)['state'])
        self.assertEqual('Discarded', self.engine.discard(self.reference(request), self.session)['state'])

    def test_unknown_status_or_discard_does_not_allocate_lock_files(self):
        reference = self.reference(self.request())
        for action in (self.engine.inspect, self.engine.discard):
            with self.assertRaisesRegex(read_engine.Rejected, 'NOT_FOUND'):
                action(reference, self.session)
        self.assertEqual([], list(self.state.iterdir()))

    def test_global_stream_slots_are_bounded_and_release_on_exception(self):
        with self.engine.locked(self.request()['operationId'], allow_new=True) as state:
            with self.engine.stream_slot(state), self.engine.stream_slot(state):
                with self.assertRaisesRegex(read_engine.Rejected, 'BUSY'):
                    with self.engine.stream_slot(state):
                        self.fail('third stream must not start')
            with self.engine.stream_slot(state):
                pass

    def test_real_sigkill_releases_operation_and_stream_locks_without_publishing(self):
        context = multiprocessing.get_context('fork')
        received = context.Event()
        read_fd, write_fd = os.pipe()
        request = self.request()

        def receive():
            os.close(write_fd)
            calls = 0

            def current():
                nonlocal calls
                calls += 1
                if calls == 2:
                    received.set()

            self.session.current.side_effect = current
            try:
                self.engine.stage(request, self.session, self.files.InputLines(read_fd))
            finally:
                os.close(read_fd)

        process = context.Process(target=receive)
        process.start()
        os.close(read_fd)
        try:
            first = (json.dumps(self.packets(self.content)[0]) + '\n').encode()
            remaining = memoryview(first)
            while remaining:
                remaining = remaining[os.write(write_fd, remaining):]
            self.assertTrue(received.wait(10), 'receiver must actually write a chunk before SIGKILL')
            process.kill()
            process.join(10)
            self.assertEqual(-9, process.exitcode)
        finally:
            os.close(write_fd)
            if process.is_alive():
                process.kill()
                process.join(10)
            process.close()
        self.assert_original()
        self.assertEqual('Interrupted', self.engine.inspect(self.reference(request), self.session)['state'])
        with self.assertRaisesRegex(read_engine.Rejected, 'PREVIOUS_OPERATION_INCOMPLETE'):
            self.stage(request)
        self.assertEqual('Discarded', self.engine.discard(self.reference(request), self.session)['state'])
        self.assertEqual([], list(self.state.glob('*.partial')))
        self.assertEqual('Staged', self.stage(self.request())['state'])
        self.assert_original()

    def test_replay_requires_full_validated_stream_and_leaves_private_payload_unchanged(self):
        request = self.request()
        self.stage(request)
        payload = self.payloads()[0]
        before = read_engine.fingerprint(payload.stat())
        self.assertEqual('Staged', self.stage(request)['state'])
        self.assertEqual(before, read_engine.fingerprint(payload.stat()))
        for content, options in ((b'wrong', {}), (self.content, {'trailing': b'not EOF'})):
            with self.subTest(options=options, size=len(content)), self.assertRaises(Exception):
                self.stage(request, content=content, **options)
            self.assertEqual(before, read_engine.fingerprint(payload.stat()))
            self.assertEqual(self.content, payload.read_bytes())
        self.assert_original()

    def test_bad_digest_offsets_encoding_and_trailing_data_never_mutate_source(self):
        rows = self.packets(self.content)
        cases = [({'sha256': 'F' * 64}, {}),
                 ({}, {'trailing': b'{}\n'}),
                 ({}, {'packets': [{**rows[0], 'offset': 1}, *rows[1:]]}),
                 ({}, {'packets': [{**rows[0], 'data': 'not base64'}, *rows[1:]]}),
                 ({}, {'packets': rows[:-1]}),
                 ({}, {'packets': [{'type': 'commit'}]})]
        before = read_engine.fingerprint(self.leaf.stat())
        for changes, options in cases:
            with self.subTest(changes=changes, options=list(options)), self.assertRaises(Exception):
                self.stage(self.request(**changes), **options)
            self.assert_original(before)

    def test_symlink_and_hardlink_sources_are_rejected(self):
        link = self.root / 'linked.bin'
        link.symlink_to(self.leaf)
        hard = self.root / 'hard.bin'
        os.link(self.leaf, hard)
        for path in (link.name, hard.name):
            with self.subTest(path=path), self.assertRaises(Exception):
                self.stage(self.request(path))
        self.assert_original()
        self.assertEqual(self.original, hard.read_bytes())

    def test_symlink_parent_cannot_stage_into_outside_directory(self):
        outside = self.base / 'outside'
        outside.mkdir()
        (self.root / 'linked').symlink_to(outside, target_is_directory=True)
        with self.assertRaises(Exception):
            self.stage(self.request('linked/escape.bin'))
        self.assertEqual([], list(outside.iterdir()))
        self.assert_original()

    def test_stale_leaf_and_parent_versions_are_rejected(self):
        leaf_request = self.request()
        self.leaf.write_bytes(b'changed by container')
        with self.assertRaises(Exception):
            self.stage(leaf_request)
        self.assertEqual(b'changed by container', self.leaf.read_bytes())
        parent_request = self.request('absent.bin')
        (self.root / 'other').write_bytes(b'parent changed')
        with self.assertRaises(Exception):
            self.stage(parent_request)
        self.assertFalse((self.root / 'absent.bin').exists())

    def test_target_change_during_receive_fails_without_publishing(self):
        request = self.request()
        changed = b'container replaced its own file'
        self.session.current.side_effect = lambda: self.leaf.write_bytes(changed)
        with self.assertRaises(Exception):
            self.stage(request)
        self.assertEqual(changed, self.leaf.read_bytes())
        self.assertNotEqual(self.content, self.leaf.read_bytes())

    def test_target_liveness_and_permission_loss_preserve_original(self):
        before = read_engine.fingerprint(self.leaf.stat())
        for callback, failure in (('current', read_engine.Rejected('CONTAINER_FILES_INSTANCE_CHANGED')),
                                  ('revalidate', PermissionError('CONTAINER_PROTECTED'))):
            with self.subTest(callback=callback):
                getattr(self.session, callback).side_effect = failure
                with self.assertRaises(Exception):
                    self.stage(self.request())
                getattr(self.session, callback).side_effect = None
                self.assert_original(before)

    def test_protected_paths_are_rejected_without_creating_targets(self):
        for path in ('.env', 'key.pem', 'compose.yml', 'proc/status', 'run/secret', '.ssh/key'):
            request = self.request(path='safe.bin')
            request['path'] = path
            with self.subTest(path=path), self.assertRaises(Exception):
                self.stage(request)
            self.assertFalse((self.root / path).exists())
        self.assert_original()

    def test_replay_fingerprint_cannot_change_target_owner_or_payload(self):
        request = self.request()
        self.stage(request)
        changes = ({'containerId': 'c' * 64}, {'instanceId': INSTANCE.replace('777:', '778:')},
                   {'owner': 'C' * 64}, {'path': 'other.bin'}, {'sizeBytes': 1}, {'sha256': 'D' * 64},
                   {'expectedParentVersion': 'E' * 64}, {'expectedVersion': 'ABSENT'})
        for change in changes:
            with self.subTest(change=change), self.assertRaises(Exception):
                self.stage({**request, **change})
        self.assertEqual(self.content, self.payloads()[0].read_bytes())
        self.assert_original()

    def test_inspect_and_discard_require_exact_owned_target_reference(self):
        request = self.request()
        self.stage(request)
        reference = self.reference(request)
        for change in ({'owner': 'C' * 64}, {'containerId': 'c' * 64},
                       {'instanceId': INSTANCE.replace('777:', '778:')}, {'operationId': '0' * 32}):
            for action in (self.engine.inspect, self.engine.discard):
                with self.subTest(change=change, action=action.__name__), self.assertRaises(Exception):
                    action({**reference, **change}, self.session)
        self.assertEqual(self.content, self.payloads()[0].read_bytes())
        self.assert_original()

    def test_interrupted_receive_is_observed_and_never_automatically_restaged(self):
        request = self.request()
        with self.assertRaises(Exception):
            self.stage(request, packets=self.packets(self.content)[:1])
        self.assertEqual('Aborted', self.engine.inspect(self.reference(request), self.session)['state'])
        # Reconstruct the persisted snapshot a killed receiver leaves before
        # its exception handler can make the terminal Aborted transition.
        record_path = self.records()[0]
        record = json.loads(record_path.read_text())
        record['state'] = 'Receiving'
        record_path.write_text(json.dumps(record))
        self.assertEqual('Interrupted', self.engine.inspect(self.reference(request), self.session)['state'])
        with self.assertRaises(Exception):
            self.stage(request)
        self.assert_original()

    def test_verified_complete_payload_recovers_only_to_staged(self):
        request = self.request()
        self.stage(request)
        record_path = self.records()[0]
        record = json.loads(record_path.read_text())
        record['state'] = 'Verified'
        record_path.write_text(json.dumps(record))
        self.assertEqual('Staged', self.engine.inspect(self.reference(request), self.session)['state'])
        self.assert_original()
        self.assertEqual(self.content, self.payloads()[0].read_bytes())

    def test_discard_removes_owned_payload_but_preserves_source(self):
        request = self.request()
        self.stage(request)
        self.assertEqual('Discarded', self.engine.discard(self.reference(request), self.session)['state'])
        self.assertEqual([], self.payloads())
        self.assert_original()

    def test_discard_preserves_unexpected_private_inode_even_with_same_bytes(self):
        request = self.request()
        self.stage(request)
        payload = self.payloads()[0]
        retained = payload.with_suffix('.held')
        payload.rename(retained)
        payload.write_bytes(self.content)
        payload.chmod(0o600)
        self.assertNotEqual(payload.stat().st_ino, retained.stat().st_ino)
        try:
            self.engine.discard(self.reference(request), self.session)
        except Exception:
            pass  # Rejection or explicit uncertain receipt both preserve evidence.
        self.assertTrue(payload.exists())
        self.assertEqual(self.content, payload.read_bytes())
        self.assertEqual(self.content, retained.read_bytes())
        self.assert_original()

    def test_discard_preserves_corrupted_staged_payload_as_evidence(self):
        request = self.request()
        self.stage(request)
        payload = self.payloads()[0]
        payload.write_bytes(b'changed private payload')
        try:
            self.engine.discard(self.reference(request), self.session)
        except Exception:
            pass
        self.assertTrue(payload.exists())
        self.assertEqual(b'changed private payload', payload.read_bytes())
        self.assert_original()

    def test_invalid_request_shapes_and_limits_never_mutate_source(self):
        for change in ({'operationId': '0' * 32}, {'operationId': 'A' * 32}, {'owner': 'b' * 64},
                       {'sizeBytes': True}, {'sizeBytes': -1}, {'sizeBytes': 1024**3 + 1},
                       {'path': '../escape'}, {'path': '/absolute'}, {'path': ''},
                       {'expectedParentVersion': 'bad'}, {'unexpected': 'field'}):
            with self.subTest(change=change), self.assertRaises(Exception):
                self.stage(self.request(**change))
        self.assert_original()

    def test_unsafe_private_state_permissions_fail_before_target_mutation(self):
        request = self.request()
        self.state.chmod(0o755)
        with self.assertRaises(Exception):
            self.stage(request)
        self.assert_original()


if __name__ == '__main__':
    unittest.main()
