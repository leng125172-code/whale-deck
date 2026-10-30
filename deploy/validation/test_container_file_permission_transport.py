"""Private permission transport seams with real Linux metadata, synthetic disk attestation.

This is not installed signed-RPC or human business/UI acceptance.
"""
import contextlib
import errno
import json
import os
import stat
import unittest
from unittest.mock import patch
import test_container_file_permissions as fixtures
import test_container_file_staging as common
transport = common.load('whaledeck-container-permission-transport.py', 'permission_transport_test')

class PermissionErrorTests(unittest.TestCase):
    def test_only_exact_private_receipt_absence_means_not_found(self):
        for code in ('CONTAINER_PERMISSION_NOT_FOUND', 'CONTAINER_FILES_TARGET_MISSING', 'CONTAINER_PERMISSION_HISTORY_CHANGED'):
            self.assertEqual(code, transport.error_code(RuntimeError(code)))
        for error in (FileNotFoundError(errno.ENOENT, 'HDD/socket missing'), RuntimeError('/private password'), RuntimeError('CONTAINER_PERMISSION_NOT_FOUND private')):
            self.assertEqual('CONTAINER_PERMISSION_UNAVAILABLE', transport.error_code(error))
    def test_readonly_source_and_hdd_failure_are_not_aliased_to_success(self):
        self.assertEqual('CONTAINER_PERMISSION_PROTECTED', transport.error_code(PermissionError(errno.EACCES, 'private')))
        self.assertEqual('CONTAINER_PERMISSION_CAPACITY_LOW', transport.error_code(OSError(errno.ENOSPC, 'private')))

@unittest.skipUnless(os.name == 'posix' and os.geteuid() == 0, 'isolated Linux root required')
class PermissionTransportTests(unittest.TestCase):
    def setUp(self):
        self.fixture = fixtures.LinuxContainerPermissionTests(); self.fixture.setUp(); self.addCleanup(self.fixture.doCleanups)
        self.engine, self.r, self.f, self.session = self.fixture.engine, self.fixture.r, self.fixture.f, self.fixture.session
        changed = patch.object(self.r, 'ContainerRoot', side_effect=lambda *_: contextlib.nullcontext(self.session))
        changed.start(); self.addCleanup(changed.stop)
    def execute(self, action, request):
        return transport.execute(action, request, self.r, self.f, None, self.engine)
    def test_full_plan_apply_inspect_discard_keep_original_metadata_and_bytes(self):
        request = self.fixture.request(); plan = self.execute('plan', request['plan']['command']); self.assertEqual(request['plan'], plan)
        result = self.execute('apply', request)
        self.assertEqual(('Completed', True, True), (result['state'], result['modeConfirmed'], result['historyVerified']))
        record = self.fixture.record(request)
        self.assertEqual(self.fixture.content, self.fixture.leaf.read_bytes())
        for action in ('inspect', 'discard', 'apply'):
            replay = self.execute(action, request); self.assertTrue(replay['replayed']); self.assertEqual(result['version'], replay['version'])
            self.assertEqual(record, self.fixture.record(request))
    def test_missing_or_restarted_target_apply_only_observes_original_receipt(self):
        request = self.fixture.request(); result = self.execute('apply', request); saved = self.fixture.record(request)
        for code in ('CONTAINER_FILES_TARGET_MISSING', 'CONTAINER_FILES_INSTANCE_CHANGED', 'CONTAINER_FILES_TARGET_CHANGED', 'CONTAINER_FILES_ROOT_CHANGED'):
            with patch.object(self.r, 'ContainerRoot', side_effect=self.r.Rejected(code)):
                for action in ('apply', 'inspect', 'discard'):
                    replay = self.execute(action, request)
                    self.assertEqual(('Completed', False, result['version']), (replay['state'], replay['bindingCurrent'], replay['version']))
                    self.assertEqual(saved, self.fixture.record(request))
    def test_target_loss_discard_fence_prevents_delayed_chmod_and_hdd_write(self):
        request = self.fixture.request(); before = self.r.fingerprint(self.fixture.leaf.stat())
        with patch.object(self.r, 'ContainerRoot', side_effect=self.r.Rejected('CONTAINER_FILES_TARGET_MISSING')):
            self.assertEqual('Aborted', self.execute('discard', request)['state'])
        self.assertEqual('Aborted', self.execute('apply', request)['state'])
        self.assertEqual(before, self.r.fingerprint(self.fixture.leaf.stat())); self.assertFalse(self.fixture.backup.exists())
    def test_missing_target_new_apply_does_not_create_a_receipt_or_touch_mode(self):
        request = self.fixture.request(); before = self.r.fingerprint(self.fixture.leaf.stat())
        with patch.object(self.r, 'ContainerRoot', side_effect=self.r.Rejected('CONTAINER_FILES_TARGET_MISSING')):
            with self.assertRaisesRegex(self.r.Rejected, 'SESSION_REQUIRED'): self.execute('apply', request)
        self.assertEqual(before, self.r.fingerprint(self.fixture.leaf.stat()))
        self.assertEqual([], list(self.fixture.state.glob('*.json'))); self.assertFalse(self.fixture.backup.exists())
    def test_generic_docker_storage_and_registry_failures_cannot_be_tombstoned(self):
        request = self.fixture.request()
        for error in (ConnectionError('docker unavailable'), FileNotFoundError('missing HDD/socket'), PermissionError('REGISTRY_UNSAFE')):
            with patch.object(self.r, 'ContainerRoot', side_effect=error):
                for action in ('apply', 'inspect', 'discard'):
                    with self.assertRaises(type(error)): self.execute(action, request)
        self.assertEqual([], list(self.fixture.state.glob('*.json')))
    def test_missing_original_receipt_is_not_target_absence(self):
        request = self.fixture.request()
        with patch.object(self.r, 'ContainerRoot', side_effect=self.r.Rejected('CONTAINER_FILES_TARGET_MISSING')):
            with self.assertRaisesRegex(self.r.Rejected, 'CONTAINER_PERMISSION_NOT_FOUND'): self.execute('inspect', request)
    def test_unknown_action_extra_fields_and_unsafe_modules_refused(self):
        with self.assertRaisesRegex(transport.Rejected, 'ACTION_INVALID'): self.execute('shell', {})
        with self.assertRaises(self.r.Rejected): self.execute('apply', {**self.fixture.request(), 'hostPath': '/etc'})
        with self.assertRaisesRegex(transport.Rejected, 'MODULE_UNSAFE'): transport.load('not-an-approved-module.py')

if __name__ == '__main__': unittest.main()
