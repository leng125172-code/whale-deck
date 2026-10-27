"""Private transport seam tests. Docker liveness is explicit, never an installed RPC claim."""
import contextlib
import errno
import json
import os
from pathlib import Path
import unittest
from unittest.mock import patch

import test_container_file_directories as fixtures
import test_container_file_staging as common

transport = common.load('whaledeck-container-directory-transport.py', 'directory_transport_test')


class DirectoryErrorTests(unittest.TestCase):
    def test_only_exact_missing_receipt_means_not_found(self):
        self.assertEqual('CONTAINER_DIRECTORY_NOT_FOUND', transport.error_code(RuntimeError('CONTAINER_DIRECTORY_NOT_FOUND')))
        self.assertEqual('CONTAINER_FILES_TARGET_MISSING', transport.error_code(RuntimeError('CONTAINER_FILES_TARGET_MISSING')))
        self.assertEqual('CONTAINER_DIRECTORY_UNAVAILABLE', transport.error_code(FileNotFoundError(errno.ENOENT, 'storage missing')))
    def test_unknown_error_text_is_never_returned(self):
        for value in ('/private/path password', 'CONTAINER_DIRECTORY_TOKEN_SECRET', 'CONTAINER_DIRECTORY_NOT_FOUND /private'):
            self.assertEqual('CONTAINER_DIRECTORY_UNAVAILABLE', transport.error_code(RuntimeError(value)))


@unittest.skipUnless(os.name == 'posix' and hasattr(os, 'geteuid') and os.geteuid() == 0, 'requires isolated Linux root')
class DirectoryTransportTests(unittest.TestCase):
    def setUp(self):
        self.fixture = fixtures.LinuxContainerDirectoryTests(); self.fixture.setUp()
        self.addCleanup(self.fixture.doCleanups)
        self.engine, self.r, self.f, self.session = self.fixture.engine, self.fixture.r, self.fixture.f, self.fixture.session
        p = patch.object(self.r, 'ContainerRoot', side_effect=lambda *_: contextlib.nullcontext(self.session))
        p.start(); self.addCleanup(p.stop)
    def execute(self, action, value):
        return transport.execute(action, value, self.r, self.f, None, self.engine)
    def test_plan_apply_inspect_discard_preserve_bound_terminal_result(self):
        command = self.fixture.command(); plan = self.execute('plan', command)
        request = {**self.fixture.request(), 'plan': plan}
        self.assertEqual('Completed', self.execute('apply', request)['state'])
        self.assertEqual('Completed', self.execute('inspect', request)['state'])
        self.assertEqual('Completed', self.execute('discard', request)['state'])
        self.assertTrue((self.fixture.root / command['path']).is_dir())
    def test_missing_or_restarted_target_still_observes_prior_receipt_without_new_write(self):
        request = self.fixture.request(); self.engine.apply(request, self.session)
        for code in ('CONTAINER_FILES_TARGET_MISSING', 'CONTAINER_FILES_INSTANCE_CHANGED'):
            with patch.object(self.r, 'ContainerRoot', side_effect=self.r.Rejected(code)):
                result = self.execute('inspect', request)
                self.assertEqual('Completed', result['state']); self.assertFalse(result['bindingCurrent'])
                self.assertEqual('Completed', self.execute('discard', request)['state'])
    def test_missing_target_cancellation_writes_tombstone_and_fences_delayed_apply(self):
        request = self.fixture.request()
        with patch.object(self.r, 'ContainerRoot', side_effect=self.r.Rejected('CONTAINER_FILES_TARGET_MISSING')):
            self.assertEqual('Aborted', self.execute('discard', request)['state'])
        self.assertEqual('Aborted', self.execute('apply', request)['state'])
        self.assertFalse((self.fixture.root / '新建目录').exists())
    def test_generic_docker_or_storage_failure_cannot_create_success_or_tombstone(self):
        request = self.fixture.request()
        for error in (ConnectionError('docker unavailable'), FileNotFoundError('missing HDD/socket'), PermissionError('REGISTRY_UNSAFE')):
            with patch.object(self.r, 'ContainerRoot', side_effect=error):
                with self.assertRaises(type(error)): self.execute('discard', request)
        self.assertEqual([], list(self.fixture.state.glob('*.json')))
    def test_missing_receipt_is_looked_up_in_private_state_even_after_container_disappears(self):
        request = self.fixture.request()
        with patch.object(self.r, 'ContainerRoot', side_effect=self.r.Rejected('CONTAINER_FILES_TARGET_MISSING')):
            with self.assertRaisesRegex(self.r.Rejected, 'CONTAINER_DIRECTORY_NOT_FOUND'): self.execute('inspect', request)
        self.assertFalse((self.fixture.root / '新建目录').exists())
    def test_unknown_action_or_extra_request_fields_fail_before_target_resolution(self):
        with self.assertRaisesRegex(transport.Rejected, 'ACTION_INVALID'): self.execute('shell', {})
        request = self.fixture.request()
        with self.assertRaises(self.r.Rejected): self.execute('apply', {**request, 'hostPath': '/etc'})


if __name__ == '__main__': unittest.main()
