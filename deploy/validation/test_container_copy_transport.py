"""Fixed transport dispatch against isolated real filesystem copy receipts."""
import contextlib
import errno
import os
import unittest
from unittest.mock import patch

import test_container_copy_intents as common

transport = common.common.common.common.load('whaledeck-container-copy-transport.py', 'container_copy_transport_tests')


class CopyTransportErrors(unittest.TestCase):
    def test_only_approved_codes_escape_and_paths_never_become_error_output(self):
        for code in ('CONTAINER_COPY_NOT_FOUND', 'CONTAINER_FILES_TARGET_MISSING', 'FILES_TREE_ENTRY_LIMIT'):
            self.assertEqual(code, transport.error_code(RuntimeError(code)))
        for error in (FileNotFoundError(errno.ENOENT, 'private storage'), RuntimeError('password=private'),
                      RuntimeError('CONTAINER_COPY_NOT_FOUND /private/path')):
            self.assertEqual('CONTAINER_COPY_UNAVAILABLE', transport.error_code(error))


@unittest.skipUnless(os.name == 'posix' and hasattr(os, 'geteuid') and os.geteuid() == 0, 'requires isolated Linux root')
class LinuxContainerCopyTransportTests(unittest.TestCase):
    def setUp(self):
        self.fixture = common.LinuxContainerCopyIntentsTests(); self.fixture.setUp()
        self.addCleanup(self.fixture.doCleanups)
        self.engine, self.r, self.f, self.session = self.fixture.engine, self.fixture.r, self.fixture.f, self.fixture.session
        replacement = patch.object(self.r, 'ContainerRoot', side_effect=lambda *_: contextlib.nullcontext(self.session))
        replacement.start(); self.addCleanup(replacement.stop)

    def execute(self, action, value): return transport.execute(action, value, self.r, self.f, None, self.engine)

    def test_plan_apply_inspect_discard_preserve_container_domain_version(self):
        command = self.fixture.fixture.command(); plan = self.execute('plan', command)
        request = {**self.fixture.request(), 'plan': plan}
        result = self.execute('apply', request)
        self.assertEqual('Completed', result['state']); self.assertTrue(result['bindingCurrent'])
        self.assertEqual(result['version'], self.execute('inspect', request)['version'])
        self.assertEqual('Completed', self.execute('discard', request)['state'])

    def test_known_loss_reads_terminal_receipt_and_discard_fences_delayed_apply_without_target(self):
        request = self.fixture.request()
        with patch.object(self.r, 'ContainerRoot', side_effect=self.r.Rejected('CONTAINER_FILES_TARGET_MISSING')):
            self.assertEqual('Aborted', self.execute('discard', request)['state'])
            self.assertEqual('Aborted', self.execute('apply', request)['state'])
            self.assertEqual('Aborted', self.execute('inspect', request)['state'])
        self.assertFalse((self.fixture.root / '复制.bin').exists())

    def test_known_loss_cannot_start_new_apply_or_invent_a_plan(self):
        request = self.fixture.request()
        with patch.object(self.r, 'ContainerRoot', side_effect=self.r.Rejected('CONTAINER_FILES_TARGET_MISSING')):
            with self.assertRaisesRegex(self.f.Rejected, 'TARGET_UNAVAILABLE'): self.execute('apply', request)
            with self.assertRaises(self.r.Rejected): self.execute('plan', request['plan']['command'])
            with self.assertRaisesRegex(self.f.Rejected, 'NOT_FOUND'): self.execute('inspect', request)
        self.assertFalse(self.fixture.record_path(request).exists())

    def test_completed_history_survives_known_loss_and_never_reports_current_binding(self):
        request = self.fixture.request(); result = self.execute('apply', request)
        for code in ('CONTAINER_FILES_TARGET_MISSING', 'CONTAINER_FILES_INSTANCE_CHANGED'):
            with patch.object(self.r, 'ContainerRoot', side_effect=self.r.Rejected(code)):
                current = self.execute('inspect', request)
                self.assertEqual('Completed', current['state']); self.assertFalse(current['bindingCurrent'])
                self.assertEqual(result['version'], current['version'])

    def test_generic_socket_storage_or_registry_error_never_becomes_tombstone(self):
        request = self.fixture.request()
        for error in (FileNotFoundError('storage unavailable'), ConnectionError('Docker unavailable'), PermissionError('REGISTRY_UNSAFE')):
            with self.subTest(error=type(error).__name__), patch.object(self.r, 'ContainerRoot', side_effect=error):
                with self.assertRaises(type(error)): self.execute('discard', request)
        self.assertFalse(self.fixture.record_path(request).exists())

    def test_unknown_action_and_extra_parameters_fail_before_target_lookup(self):
        with self.assertRaisesRegex(transport.Rejected, 'ACTION_INVALID'): self.execute('shell', {})
        with patch.object(self.r, 'ContainerRoot', side_effect=AssertionError('no target lookup')):
            with self.assertRaises(self.f.Rejected): self.execute('apply', {**self.fixture.request(), 'hostPath': '/etc'})


if __name__ == '__main__': unittest.main()
