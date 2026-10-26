"""Container-bound real filesystem copies with synthetic HDD attestation.

The mount/udev/sysfs policy fixture is explicit: these tests alone do not prove
physical HDD storage, installed helper authority, RPC or product UI behavior.
"""
import base64
import hashlib
import json
import os
import signal
import time
from types import SimpleNamespace
import unittest
import uuid
from unittest.mock import Mock, patch

import test_container_file_staging as staging
import test_file_backups as host_backups

candidate = staging.load('whaledeck-container-backups.py', 'container_backup_candidate')


@unittest.skipUnless(os.name == 'posix' and hasattr(os, 'geteuid') and os.geteuid() == 0,
                     'requires isolated Linux root')
class LinuxContainerBackupTests(unittest.TestCase):
    def setUp(self):
        self.disk = host_backups.FileBackupTests()
        self.disk.setUp()
        self.addCleanup(self.disk.tearDown)
        self.f, self.r = self.disk.engine.f, staging.read_engine
        self.root = self.disk.root
        self.state = self.disk.disk / 'container-state'
        self.state.mkdir(mode=0o700)
        self.folder = os.open(self.root, os.O_RDONLY | os.O_DIRECTORY)
        self.addCleanup(os.close, self.folder)
        self.session = SimpleNamespace(root=self.folder, request={'containerId': staging.ID, 'instanceId': staging.INSTANCE},
            mounts=b'M' * 32, identity=('12345', 777, 888), current=Mock(), revalidate=Mock())
        self.upload = staging.uploads.ContainerUploads(self.r, self.f, self.state)
        self.storage = host_backups.backups.FileBackups(self.f, namespace='container-files-v1')
        self.engine = candidate.ContainerBackups(self.upload, self.storage)
        self.target = self.disk.target / 'container-files-v1'
        self.original = hashlib.shake_256(b'container-before-publication-full-binary').digest(2 * 1024**2 + 7)
        self.replacement = b'new content is private and must not replace source yet\n'
        self.leaf = self.root / '原始.bin'
        self.leaf.write_bytes(self.original)
        self.leaf.chmod(0o640)

    def stage(self, absent=False, **extra):
        path = 'absent.bin' if absent else self.leaf.name
        request = {'containerId': staging.ID, 'instanceId': staging.INSTANCE, 'path': path,
            'expectedVersion': 'ABSENT' if absent else self.r.version(self.session.request, path, self.leaf.stat()),
            'expectedParentVersion': self.r.version(self.session.request, '', self.root.stat()),
            'operationId': uuid.uuid4().hex, 'owner': staging.OWNER, 'sizeBytes': len(self.replacement),
            'sha256': hashlib.sha256(self.replacement).hexdigest().upper(), **extra}
        packets = [{'type': 'chunk', 'offset': 0, 'data': base64.b64encode(self.replacement).decode()}, {'type': 'commit'}]
        import tempfile
        with tempfile.TemporaryFile() as stream:
            for row in packets: stream.write((json.dumps(row) + '\n').encode())
            stream.seek(0)
            self.assertEqual(self.upload.stage(request, self.session, self.f.InputLines(stream.fileno()))['state'], 'Staged')
        return request, staging.LinuxContainerStagingTests.reference(request)

    def saved(self, operation):
        return json.loads((self.target / (operation + '.json')).read_bytes())

    def test_full_old_content_copy_is_scoped_private_durable_and_does_not_publish(self):
        request, scope = self.stage()
        before, parent_before = self.r.fingerprint(self.leaf.stat()), self.r.fingerprint(self.root.stat())
        result = self.engine.capture(scope, self.session)
        self.assertEqual(result['sizeBytes'], len(self.original))
        self.assertEqual(result['sha256'], hashlib.sha256(self.original).hexdigest().upper())
        data = self.target / (scope['operationId'] + '.data')
        self.assertEqual(data.read_bytes(), self.original)
        saved = self.saved(scope['operationId'])
        self.assertEqual(saved['schemaVersion'], 2)
        self.assertEqual(saved['resourceType'], 'ContainerFile')
        self.assertEqual(saved['containerId'], staging.ID)
        self.assertEqual(saved['instanceId'], staging.INSTANCE)
        self.assertEqual(saved['sourceVersion'], request['expectedVersion'])
        self.assertEqual(saved['sourceParentVersion'], request['expectedParentVersion'])
        self.assertNotIn('rootId', saved)
        self.assertEqual((data.stat().st_uid, data.stat().st_mode & 0o777, data.stat().st_nlink), (0, 0o600, 1))
        self.assertEqual(self.leaf.read_bytes(), self.original)
        self.assertEqual((self.state / (scope['operationId'] + '.data')).read_bytes(), self.replacement)
        self.assertEqual((self.r.fingerprint(self.leaf.stat()), self.r.fingerprint(self.root.stat())), (before, parent_before))
        self.assertFalse(result['differentSourceDevice'])
        self.assertEqual(self.upload.inspect(scope, self.session)['state'], 'Staged')

    def test_replay_reads_and_proves_existing_full_capture_without_copy_or_rename(self):
        _, scope = self.stage()
        result = self.engine.capture(scope, self.session)
        before = (self.target / (scope['operationId'] + '.data')).stat()
        with patch.object(self.f, 'exchange', side_effect=AssertionError('must not recopy')):
            self.assertEqual(self.engine.capture(scope, self.session), result)
        self.assertEqual((self.target / (scope['operationId'] + '.data')).stat().st_ino, before.st_ino)

    def test_absent_target_has_no_original_to_capture_and_no_hdd_objects(self):
        _, scope = self.stage(absent=True)
        with self.assertRaisesRegex(self.r.Rejected, 'BACKUP_SOURCE_REQUIRED'): self.engine.capture(scope, self.session)
        self.assertEqual(list(self.disk.target.iterdir()), [])
        self.assertFalse((self.root / 'absent.bin').exists())

    def test_wrong_owner_container_or_start_is_not_a_capture_or_inspection_authority(self):
        _, scope = self.stage()
        for key, value in (('owner', 'C' * 64), ('containerId', 'c' * 64), ('instanceId', '778:2026-10-21T01:02:03.123456789Z')):
            with self.subTest(key=key), self.assertRaises(self.r.Rejected):
                self.engine.capture({**scope, key: value}, self.session)
        self.assertEqual(list(self.disk.target.iterdir()), [])

    def test_same_instance_with_changed_mount_or_namespace_is_refused_everywhere(self):
        _, scope = self.stage()
        for key, value in (('mounts', b'N' * 32), ('identity', ('12345', 777, 999))):
            previous = getattr(self.session, key)
            setattr(self.session, key, value)
            try:
                for action in (self.engine.capture, self.upload.inspect, self.upload.discard):
                    with self.subTest(key=key, action=action.__name__), self.assertRaisesRegex(self.r.Rejected, 'TARGET_CHANGED'):
                        action(scope, self.session)
            finally: setattr(self.session, key, previous)
        self.assertEqual(list(self.disk.target.iterdir()), [])

    def test_same_id_and_start_with_replaced_root_is_refused(self):
        _, scope = self.stage()
        other = self.disk.disk / 'different-root'; other.mkdir()
        descriptor = os.open(other, os.O_RDONLY | os.O_DIRECTORY)
        try:
            self.session.root = descriptor
            with self.assertRaisesRegex(self.r.Rejected, 'TARGET_CHANGED'): self.engine.capture(scope, self.session)
        finally: self.session.root = self.folder; os.close(descriptor)

    def test_changed_leaf_or_parent_version_refuses_before_hdd_write(self):
        _, scope = self.stage()
        self.leaf.write_bytes(self.original + b'changed')
        with self.assertRaisesRegex(self.r.Rejected, 'VERSION_CONFLICT'): self.engine.capture(scope, self.session)
        self.assertEqual(list(self.disk.target.iterdir()), [])
        _, scope = self.stage()
        (self.root / 'new-neighbor').touch()
        with self.assertRaisesRegex(self.r.Rejected, 'PARENT_VERSION_CONFLICT'): self.engine.capture(scope, self.session)
        self.assertEqual(list(self.disk.target.iterdir()), [])

    def test_missing_hdd_or_ssd_is_never_a_copy_to_fallback_storage(self):
        _, scope = self.stage()
        for path, value in ((self.disk.mountinfo, b''), (self.disk.rotational, b'0\n')):
            before = path.read_bytes()
            try:
                path.write_bytes(value)
                with self.assertRaises(self.f.Rejected): self.engine.capture(scope, self.session)
                self.assertEqual(list(self.disk.target.iterdir()), [])
            finally: path.write_bytes(before)
        self.assertEqual(self.leaf.read_bytes(), self.original)

    def test_host_history_cannot_be_adopted_as_container_capture(self):
        host_record = self.disk.source(self.original, self.leaf.name)
        _, scope = self.stage()
        host_record.update(operationId=scope['operationId'], expectedVersion=self.f.version(self.leaf.stat()),
                           originalIdentity=self.f.publication_identity(self.leaf.stat()))
        self.disk.ensure(host_record)
        self.target.mkdir(mode=0o700)
        # Even copying a host record into the private container namespace
        # cannot turn its root binding into container authority.
        for suffix in ('.json', '.data'):
            (self.target / (scope['operationId'] + suffix)).write_bytes((self.disk.target / (scope['operationId'] + suffix)).read_bytes())
            (self.target / (scope['operationId'] + suffix)).chmod(0o600)
        with self.assertRaisesRegex(self.f.Rejected, 'IDEMPOTENCY_CONFLICT'): self.engine.capture(scope, self.session)
        self.assertEqual(self.saved(scope['operationId'])['schemaVersion'], 1)

    def test_container_capture_cannot_be_adopted_by_host_history(self):
        _, scope = self.stage()
        self.engine.capture(scope, self.session)
        host_record = {'operationId': scope['operationId'], 'owner': staging.OWNER, 'rootId': 'test.shared',
            'path': self.leaf.name, 'expectedVersion': self.f.version(self.leaf.stat()),
            'originalIdentity': self.f.publication_identity(self.leaf.stat()), 'originalSha256': hashlib.sha256(self.original).hexdigest().upper()}
        for suffix in ('.json', '.data'):
            (self.disk.target / (scope['operationId'] + suffix)).write_bytes((self.target / (scope['operationId'] + suffix)).read_bytes())
            (self.disk.target / (scope['operationId'] + suffix)).chmod(0o600)
        with self.assertRaisesRegex(self.f.Rejected, 'IDEMPOTENCY_CONFLICT'): self.disk.ensure(host_record)
        self.assertEqual(self.saved(scope['operationId'])['schemaVersion'], 2)

    def test_corrupt_existing_copy_is_preserved_and_never_replaced_on_retry(self):
        _, scope = self.stage(); self.engine.capture(scope, self.session)
        data = self.target / (scope['operationId'] + '.data')
        data.write_bytes(b'x' * len(self.original))
        with self.assertRaisesRegex(self.f.Rejected, 'CONTENT_INVALID'): self.engine.capture(scope, self.session)
        self.assertEqual(data.read_bytes(), b'x' * len(self.original))
        self.assertEqual(self.leaf.read_bytes(), self.original)

    def test_discarded_or_incomplete_staging_does_not_capture(self):
        _, scope = self.stage(); self.upload.discard(scope, self.session)
        with self.assertRaisesRegex(self.r.Rejected, 'STAGED_REQUIRED'): self.engine.capture(scope, self.session)
        self.assertEqual(list(self.disk.target.iterdir()), [])

    def test_container_capture_requires_its_own_fixed_storage_namespace(self):
        with self.assertRaisesRegex(self.r.Rejected, 'BACKUP_ENGINE_MISMATCH'):
            candidate.ContainerBackups(self.upload, self.disk.engine)
        with self.assertRaisesRegex(self.f.Rejected, 'METADATA_UNSAFE'):
            host_backups.backups.FileBackups(self.f, namespace='../host-history')
        with self.assertRaisesRegex(self.f.Rejected, 'METADATA_UNSAFE'):
            self.storage.ensure({}, self.folder, self.leaf.name)
        self.assertEqual(list(self.disk.target.iterdir()), [])

    def test_symlink_or_untrusted_namespace_is_not_adopted_or_chmodded(self):
        _, scope = self.stage()
        outside = self.disk.disk / 'outside'; outside.mkdir(mode=0o700)
        self.target.symlink_to(outside, target_is_directory=True)
        with self.assertRaises((OSError, self.f.Rejected)): self.engine.capture(scope, self.session)
        self.assertEqual(list(outside.iterdir()), [])
        self.target.unlink(); self.target.mkdir(mode=0o755)
        with self.assertRaisesRegex(self.f.Rejected, 'DIRECTORY_UNSAFE'): self.engine.capture(scope, self.session)
        self.assertEqual(self.target.stat().st_mode & 0o777, 0o755)
        self.assertEqual(list(self.target.iterdir()), [])

    def test_changed_complete_private_upload_prevents_any_source_capture(self):
        _, scope = self.stage()
        data = self.state / (scope['operationId'] + '.data')
        data.write_bytes(b'x' * len(self.replacement))
        with self.assertRaisesRegex(self.r.Rejected, 'PAYLOAD_CHANGED'): self.engine.capture(scope, self.session)
        self.assertEqual(list(self.disk.target.iterdir()), [])

    def test_stop_during_data_copy_never_returns_verified_or_changes_source(self):
        _, scope = self.stage()
        original_write = os.write
        def stop_after_first_hdd_chunk(descriptor, value):
            result = original_write(descriptor, value)
            if (self.target / (scope['operationId'] + '.partial')).exists():
                self.session.current.side_effect = self.r.Rejected('CONTAINER_FILES_INSTANCE_CHANGED')
            return result
        with patch.object(host_backups.backups.os, 'write', side_effect=stop_after_first_hdd_chunk), \
                self.assertRaisesRegex(self.r.Rejected, 'INSTANCE_CHANGED'):
            self.engine.capture(scope, self.session)
        saved = self.saved(scope['operationId'])
        self.assertEqual(saved['state'], 'Preparing')
        partial = self.target / (scope['operationId'] + '.partial')
        self.assertGreater(partial.stat().st_size, 0)
        self.assertLess(partial.stat().st_size, len(self.original))
        self.assertEqual(self.leaf.read_bytes(), self.original)
        self.session.current.side_effect = None
        with self.assertRaisesRegex(self.f.Rejected, 'INCOMPLETE'): self.engine.capture(scope, self.session)

    def test_global_capture_gate_and_operation_lock_are_nonblocking(self):
        _, scope = self.stage()
        with self.upload.locked(scope['operationId']):
            with self.assertRaisesRegex(self.r.Rejected, 'BUSY'): self.engine.capture(scope, self.session)
        _, other = self.stage()
        with self.upload.locked(scope['operationId']) as state, self.engine.gate(state):
            with self.assertRaisesRegex(self.r.Rejected, 'BUSY'): self.engine.capture(other, self.session)
        self.assertEqual(list(self.disk.target.iterdir()), [])

    def test_instance_loss_mid_copy_keeps_only_explicit_incomplete_capture(self):
        _, scope = self.stage()
        exchange = self.f.exchange
        def stopped(*args):
            self.session.current.side_effect = self.r.Rejected('CONTAINER_FILES_INSTANCE_CHANGED')
            return exchange(*args)
        with patch.object(self.f, 'exchange', side_effect=stopped), self.assertRaisesRegex(self.r.Rejected, 'INSTANCE_CHANGED'):
            self.engine.capture(scope, self.session)
        self.session.current.side_effect = None
        self.assertEqual(self.saved(scope['operationId'])['state'], 'Preparing')
        self.assertEqual(self.leaf.read_bytes(), self.original)
        # Complete .data may be reconciled by fresh authority, never recopied.
        with patch.object(self.f, 'exchange', side_effect=AssertionError('must not recopy')):
            self.assertEqual(self.engine.capture(scope, self.session)['sizeBytes'], len(self.original))

    def test_actual_sigkill_at_three_durable_boundaries_has_no_source_write(self):
        for point in ('before-data-rename', 'before-hdd-verified', 'before-upload-proof'):
            with self.subTest(point=point):
                _, scope = self.stage()
                child = os.fork()
                if child == 0:
                    try:
                        exchange, save = self.f.exchange, self.f.save_receipt
                        def rename(*args):
                            if point == 'before-data-rename': os.kill(os.getpid(), signal.SIGKILL)
                            return exchange(*args)
                        def persist(*args):
                            value = args[2]
                            if point == 'before-hdd-verified' and value.get('state') == 'Verified' and value.get('resourceType') == 'ContainerFile' or \
                                    point == 'before-upload-proof' and value.get('backupCapture') is not None:
                                os.kill(os.getpid(), signal.SIGKILL)
                            return save(*args)
                        self.f.exchange, self.f.save_receipt = rename, persist
                        self.engine.capture(scope, self.session)
                    except BaseException: os._exit(2)
                    os._exit(3)
                deadline, status = time.monotonic() + 10, None
                while time.monotonic() < deadline:
                    done, code = os.waitpid(child, os.WNOHANG)
                    if done: status = code; break
                    time.sleep(0.01)
                if status is None:
                    os.kill(child, signal.SIGKILL); os.waitpid(child, 0); self.fail('capture child exceeded deadline')
                self.assertTrue(os.WIFSIGNALED(status)); self.assertEqual(os.WTERMSIG(status), signal.SIGKILL)
                self.assertEqual(self.leaf.read_bytes(), self.original)
                if point == 'before-data-rename':
                    with self.assertRaisesRegex(self.f.Rejected, 'INCOMPLETE'): self.engine.capture(scope, self.session)
                else:
                    with patch.object(self.f, 'exchange', side_effect=AssertionError('recovery must not copy')):
                        self.assertEqual(self.engine.capture(scope, self.session)['sha256'], hashlib.sha256(self.original).hexdigest().upper())


if __name__ == '__main__': unittest.main()
