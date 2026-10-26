"""Native transport validation with isolated filesystem and explicit fake Docker scope.

Does not install a helper or prove a systemd/UDS/product authorization boundary.
"""
from contextlib import contextmanager
import errno
import hashlib
import os
from pathlib import Path
import signal
import stat
import unittest
import uuid
from unittest.mock import Mock, patch

import test_container_file_backups as fixtures
import test_container_file_staging as staging

driver = staging.load('whaledeck-container-file-write.py', 'container_write_transport_tests')
publishing = staging.load('whaledeck-container-publish.py', 'container_write_transport_publisher')


class ContainerWriteErrorTests(unittest.TestCase):
    def test_missing_storage_is_not_evidence_that_the_operation_never_existed(self):
        self.assertEqual(driver.error_code(FileNotFoundError(errno.ENOENT, 'private source or HDD path')), 'CONTAINER_UPLOAD_UNAVAILABLE')
        self.assertEqual(driver.error_code(driver.Rejected('CONTAINER_UPLOAD_NOT_FOUND')), 'CONTAINER_UPLOAD_NOT_FOUND')

    def test_unknown_exceptions_and_crafted_code_suffixes_do_not_echo_private_data(self):
        for message in ('password /private/path', 'CONTAINER_UPLOAD_PRIVATE_PASSWORD', 'FILES_PRIVATE_TOKEN'):
            self.assertEqual(driver.error_code(RuntimeError(message)), 'CONTAINER_UPLOAD_UNAVAILABLE')


@unittest.skipUnless(os.name == 'posix' and hasattr(os, 'geteuid') and os.geteuid() == 0, 'requires isolated Linux root')
class LinuxContainerWriteTransportTests(unittest.TestCase):
    def setUp(self):
        self.fixture = fixtures.LinuxContainerBackupTests(); self.fixture.setUp()
        self.addCleanup(self.fixture.doCleanups)
        self.f, self.r, self.u, self.session = self.fixture.f, self.fixture.r, self.fixture.upload, self.fixture.session
        self.session.container = {'HostConfig': {'ReadonlyRootfs': False}, 'Mounts': []}
        self.publisher = publishing.ContainerPublisher(self.u, self.fixture.engine)
        self.location = {**self.session.request, 'path': self.fixture.leaf.name}
        state_patch = patch.object(driver, 'STATE', self.fixture.state)
        state_patch.start(); self.addCleanup(state_patch.stop)
        @contextmanager
        def root(*_args): yield self.session
        root_patch = patch.object(self.r, 'ContainerRoot', side_effect=root)
        root_patch.start(); self.addCleanup(root_patch.stop)

    def execute(self, action, request, source=None):
        source = Mock() if source is None else source
        return driver.execute(action, request, source, self.r, self.f, None, self.u, self.publisher)

    def test_snapshot_freezes_source_parent_and_live_target_without_changing_either(self):
        before = self.r.fingerprint(self.fixture.leaf.stat()), self.r.fingerprint(self.fixture.root.stat())
        source = Mock()
        target = self.execute('target', self.location, source)
        source.finish.assert_called_once()
        self.assertEqual(target['targetProof'], self.u.target_proof(self.session))
        self.assertEqual(target['expectedVersion'], self.r.version(self.location, self.location['path'], self.fixture.leaf.stat()))
        self.assertEqual(target['expectedParentVersion'], self.r.version(self.location, '', self.fixture.root.stat()))
        self.assertEqual(target['currentSizeBytes'], len(self.fixture.original))
        self.assertEqual(before, (self.r.fingerprint(self.fixture.leaf.stat()), self.r.fingerprint(self.fixture.root.stat())))
        self.assertEqual(list(self.fixture.state.iterdir()), [])

    def test_snapshot_distinguishes_absent_leaf_from_missing_parent(self):
        result = self.execute('target', {**self.location, 'path': 'new.bin'})
        self.assertEqual(result['expectedVersion'], 'ABSENT'); self.assertEqual(result['currentSizeBytes'], 0)
        with self.assertRaisesRegex(driver.Rejected, 'PATH_NOT_FOUND'):
            self.execute('target', {**self.location, 'path': 'missing/new.bin'})

    def test_snapshot_refuses_protected_paths_and_readonly_target(self):
        for path in ('.env', '.whaledeck-container-files-v1/old', 'proc/1/root/private'):
            with self.assertRaisesRegex(driver.Rejected, 'PROTECTED'): self.execute('target', {**self.location, 'path': path})
        self.session.container['HostConfig']['ReadonlyRootfs'] = True
        with self.assertRaisesRegex(self.r.Rejected, 'PUBLISH_READ_ONLY'): self.execute('target', self.location)

    def test_public_stage_requires_the_planned_proof_before_resolving_target(self):
        request, _ = self.fixture.stage()
        with self.assertRaisesRegex(driver.Rejected, 'REQUEST_INVALID'): self.execute('stage', request)
        request = {**request, 'operationId': uuid.uuid4().hex, 'expectedTargetProof': 'F' * 64}
        with self.assertRaisesRegex(self.r.Rejected, 'TARGET_CHANGED'): self.execute('stage', request)
        self.assertFalse((self.fixture.state / (request['operationId'] + '.json')).exists())

    def test_public_lookup_requires_exact_planned_proof_and_preserves_original_scope(self):
        _, scope = self.fixture.stage()
        reference = {**scope, 'expectedTargetProof': self.u.target_proof(self.session)}
        self.assertEqual(self.execute('inspect', reference)['state'], 'Staged')
        with self.assertRaisesRegex(driver.Rejected, 'TARGET_CHANGED'):
            self.execute('inspect', {**reference, 'expectedTargetProof': 'F' * 64})
        with self.assertRaisesRegex(self.r.Rejected, 'SCOPE_MISMATCH'):
            self.execute('inspect', {**reference, 'owner': 'C' * 64})
        with self.assertRaisesRegex(driver.Rejected, 'REQUEST_INVALID'):
            self.execute('inspect', {**reference, 'path': '/host/private'})

    def test_receiving_receipt_from_a_dead_owner_is_observed_as_interrupted(self):
        _, scope = self.fixture.stage()
        with self.u.locked(scope['operationId']) as state:
            record = self.u.scoped(state, scope); record['state'] = 'Receiving'
            self.f.save_receipt(state, scope['operationId'], record)
        reference = {**scope, 'expectedTargetProof': self.u.target_proof(self.session)}
        self.assertEqual(self.execute('inspect', reference)['state'], 'Interrupted')
        self.assertEqual(self.fixture.leaf.read_bytes(), self.fixture.original)

    def test_snapshot_proof_can_be_used_by_the_stage_engine_without_normalization(self):
        target = self.execute('target', self.location)
        _, scope = self.fixture.stage(expectedTargetProof=target['targetProof'])
        reference = {**scope, 'expectedTargetProof': target['targetProof']}
        self.assertEqual(self.execute('discard', reference)['state'], 'Discarded')
        self.assertEqual(self.fixture.leaf.read_bytes(), self.fixture.original)

    def test_two_kernel_transport_slots_and_actual_sigkill_release(self):
        ready, writer = os.pipe()
        child = os.fork()
        if child == 0:
            os.close(ready)
            try:
                with driver.lease(self.f):
                    os.write(writer, b'1'); signal.pause()
            except BaseException: os._exit(2)
            os._exit(3)
        os.close(writer)
        try:
            import select
            self.assertTrue(select.select([ready], [], [], 5)[0]); self.assertEqual(os.read(ready, 1), b'1')
            with driver.lease(self.f):
                with self.assertRaisesRegex(driver.Rejected, 'BUSY'):
                    with driver.lease(self.f): pass
            os.kill(child, signal.SIGKILL); _, status = os.waitpid(child, 0); child = None
            self.assertTrue(os.WIFSIGNALED(status)); self.assertEqual(os.WTERMSIG(status), signal.SIGKILL)
            with driver.lease(self.f), driver.lease(self.f): pass
        finally:
            os.close(ready)
            if child is not None: os.kill(child, signal.SIGKILL); os.waitpid(child, 0)

    def test_module_loader_rejects_links_permissions_owner_and_unapproved_paths(self):
        directory = self.fixture.disk.disk / 'modules'; directory.mkdir(mode=0o700)
        path = directory / 'whaledeck-container-upload.py'; path.write_text('VALUE = 7\n'); path.chmod(0o750)
        with patch.object(driver, '__file__', str(directory / 'whaledeck-container-file-write.py')):
            self.assertEqual(driver.load(path.name).VALUE, 7)
            with self.assertRaisesRegex(driver.Rejected, 'MODULE_UNSAFE'): driver.load('/etc/passwd')
            path.chmod(0o777)
            with self.assertRaisesRegex(driver.Rejected, 'MODULE_UNSAFE'): driver.load(path.name)
            path.chmod(0o750); os.chown(path, 1000, 1000)
            with self.assertRaisesRegex(driver.Rejected, 'MODULE_UNSAFE'): driver.load(path.name)
            os.chown(path, 0, 0); link = directory / 'hardlink'; os.link(path, link)
            with self.assertRaisesRegex(driver.Rejected, 'MODULE_UNSAFE'): driver.load(path.name)
            link.unlink(); path.unlink(); path.symlink_to(directory / 'other')
            with self.assertRaisesRegex(driver.Rejected, 'MODULE_UNSAFE'): driver.load(path.name)


if __name__ == '__main__': unittest.main()
