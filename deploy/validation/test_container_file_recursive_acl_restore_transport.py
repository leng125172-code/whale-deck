"""Real temporary inode ACLs; explicit container/HDD fixtures, not installed RPC acceptance."""
import contextlib
import errno
import json
import os
from pathlib import Path
import signal
import unittest
from unittest.mock import patch

import test_container_file_recursive_acl_restore as kernel
from test_container_file_staging import load

transport = load('whaledeck-container-recursive-acl-restore-transport.py', 'restore_transport_candidate')


class RestoreTransportScopeTests(unittest.TestCase):
    def test_errors_are_finite_and_source_loss_never_means_private_receipt_absence(self):
        cases = {
            'CONTAINER_RECURSIVE_ACL_RESTORE_NOT_FOUND': 'NOT_FOUND',
            'CONTAINER_RECURSIVE_ACL_HISTORY_NOT_FOUND': 'SOURCE_NOT_FOUND',
            'CONTAINER_RECURSIVE_ACL_HISTORY_VERSION_CONFLICT': 'SOURCE_CHANGED',
            'CONTAINER_RECURSIVE_ACL_HISTORY_INVALID': 'SOURCE_INVALID',
            'CONTAINER_RECURSIVE_ACL_HISTORY_UNSAFE': 'SOURCE_INVALID',
            'CONTAINER_RECURSIVE_ACL_HISTORY_TIMEOUT': 'TIMEOUT',
            'CONTAINER_RECURSIVE_ACL_HISTORY_LIMIT': 'HISTORY_LIMIT',
            'CONTAINER_ACL_MAPPING_UNPROVEN': 'MAPPING_UNPROVEN',
        }
        for code, suffix in cases.items():
            with self.subTest(code=code):
                self.assertEqual('CONTAINER_RECURSIVE_ACL_RESTORE_' + suffix, transport.error_code(ValueError(code)))
        for error in (FileNotFoundError(errno.ENOENT, 'private HDD'), OSError(errno.EIO, 'private'), ValueError('/private input'), ValueError('CONTAINER_RECURSIVE_ACL_RESTORE_NOT_FOUND private')):
            self.assertEqual('CONTAINER_RECURSIVE_ACL_RESTORE_UNAVAILABLE', transport.error_code(error))

    def test_fixed_module_scope_and_minimal_helper_never_grant_source_state_writes(self):
        with self.assertRaisesRegex(transport.Rejected, 'MODULE_UNSAFE'):
            transport.load('../../private.py')
        host = Path(__file__).resolve().parents[1] / 'host'
        text = (host / 'whaledeck-privileged').read_text()
        block = text.split('  container-file-recursive-acl-restore)', 1)[1].split('  container-file-recursive-acl-restore-stop)', 1)[0]
        self.assertIn('ReadWritePaths=/var/lib/whaledeck-container-recursive-acl-restore /data/WhaleDeck/apps /data/DockerData/whaledeck/file-backups', block)
        self.assertIn('CapabilityBoundingSet=CAP_DAC_OVERRIDE CAP_DAC_READ_SEARCH CAP_FOWNER CAP_SYS_PTRACE', block)
        for value in ('CAP_CHOWN', 'CAP_SYS_ADMIN', 'CAP_NET_ADMIN', 'ReadWritePaths=/var/lib/whaledeck-container-recursive-acl ', 'RestrictAddressFamilies=AF_INET'):
            self.assertNotIn(value, block)
        self.assertIn('RestrictAddressFamilies=AF_UNIX', block)
        self.assertIn('RuntimeMaxSec=340', block)
        stop = text.split('  container-file-recursive-acl-restore-stop)', 1)[1].split('    ;;', 1)[0]
        self.assertIn('InvocationID', stop)
        self.assertIn('== "$3"', stop)
        for name in ('whaledeck-container-recursive-acl-restore.py', 'whaledeck-container-recursive-acl-restore-transport.py'):
            for script in ('install.sh', 'validate.sh'):
                self.assertIn(name, (host / script).read_text())


@unittest.skipUnless(os.geteuid() == 0, 'isolated Linux root required')
class RestoreTransportTests(unittest.TestCase):
    def setUp(self):
        self.k = kernel.RecursiveAclRestoreTests()
        self.k.setUp()
        self.addCleanup(self.k.doCleanups)
        self.session = self.k.b.base.session
        self.reader, self.files = self.k.b.base.r, self.k.b.base.f
        bridge = load('whaledeck-container-acl-transport.py', 'restore_transport_bridge')
        self.bridge = bridge
        engine_type = transport.scoped_recursive_acl_type(self.k.type, bridge)
        self.k.engine = engine_type(self.k.b.base.uploads, self.k.b.base.publisher, self.k.state, self.k.history)
        for method in ('namespace_current', 'require_unmapped'):
            guard = patch.object(bridge, method)
            guard.start()
            self.addCleanup(guard.stop)
        root = patch.object(self.reader, 'ContainerRoot', side_effect=lambda *_: contextlib.nullcontext(self.session))
        root.start()
        self.addCleanup(root.stop)

    def execute(self, action, request):
        return transport.execute(action, request, self.reader, self.files, None, self.k.engine)

    def request(self, acl_type='access'):
        source, result, old = self.k.seed(acl_type)
        request = self.k.request(source, result)
        return source, request, old

    def test_small_plan_apply_inspect_discard_restore_physical_acl_without_content_change(self):
        for kind in ('access', 'default'):
            source, request, old = self.request(kind)
            before = self.k.b.snapshot()
            self.assertEqual(request['plan'], self.execute('plan', request['plan']['command']))
            result = self.execute('apply', request)
            self.assertEqual(('Completed', True, True), (result['state'], result['bindingCurrent'], result['historyVerified']))
            after = self.k.b.snapshot()
            self.k.b.preserved(before, after, kind)
            for relative in before:
                self.assertEqual(old[relative][kind], after[relative][kind])
            saved = self.k.record(request)
            with self.k.no_write():
                for action in ('inspect', 'apply', 'discard'):
                    replay = self.execute(action, request)
                    self.assertEqual(result['outcomeDigest'], replay['outcomeDigest'])
                    self.assertEqual(saved, self.k.record(request))

    def test_source_loss_after_completion_never_causes_physical_reexecution(self):
        source, request, _ = self.request()
        result = self.execute('apply', request)
        self.k.source_file(source).unlink()
        with self.k.no_write():
            for action in ('inspect', 'apply', 'discard'):
                self.assertEqual(result['outcomeDigest'], self.execute(action, request)['outcomeDigest'])

    def test_missing_target_keeps_original_result_and_only_exact_receipt_absence_is_not_found(self):
        _, request, _ = self.request()
        with patch.object(self.reader, 'ContainerRoot', side_effect=self.reader.Rejected('CONTAINER_FILES_TARGET_MISSING')):
            with self.assertRaisesRegex(self.reader.Rejected, 'CONTAINER_RECURSIVE_ACL_RESTORE_NOT_FOUND'):
                self.execute('inspect', request)
        result = self.execute('apply', request)
        with patch.object(self.reader, 'ContainerRoot', side_effect=self.reader.Rejected('CONTAINER_FILES_INSTANCE_CHANGED')), self.k.no_write():
            for action in ('inspect', 'apply', 'discard'):
                replay = self.execute(action, request)
                self.assertEqual('Completed', replay['state'])
                self.assertFalse(replay['bindingCurrent'])
                self.assertEqual(result['outcomeDigest'], replay['outcomeDigest'])

    def test_missing_target_discard_fences_delayed_apply_without_restore_history(self):
        _, request, _ = self.request()
        before = self.k.b.snapshot()
        with patch.object(self.reader, 'ContainerRoot', side_effect=self.reader.Rejected('CONTAINER_FILES_TARGET_MISSING')):
            self.assertEqual('Aborted', self.execute('discard', request)['state'])
        with self.k.no_write():
            self.assertEqual('Aborted', self.execute('apply', request)['state'])
        self.assertEqual(before, self.k.b.snapshot())

    def test_generic_storage_and_docker_errors_never_become_absence_or_fences(self):
        _, request, _ = self.request()
        for error in (FileNotFoundError('private HDD'), ConnectionError('private docker'), PermissionError('REGISTRY_UNSAFE')):
            with patch.object(self.reader, 'ContainerRoot', side_effect=error):
                for action in ('apply', 'inspect', 'discard'):
                    with self.assertRaises(type(error)):
                        self.execute(action, request)
        self.assertFalse(list(self.k.state.iterdir()))

    def test_namespace_and_fd_guards_fence_planning_and_final_root_child(self):
        source, result, _ = self.k.seed()
        before = self.k.b.snapshot()
        for method in ('namespace_current', 'require_unmapped'):
            with patch.object(self.bridge, method, side_effect=self.bridge.Rejected('CONTAINER_ACL_MAPPING_UNPROVEN')):
                with self.assertRaisesRegex(self.reader.Rejected, 'MAPPING_UNPROVEN'):
                    self.k.request(source, result)
        self.assertEqual(before, self.k.b.snapshot())
        request = self.k.request(source, result)
        checked = set()
        def mapping(fd):
            info = os.fstat(fd)
            checked.add((info.st_dev, info.st_ino))
        with patch.object(self.bridge, 'require_unmapped', side_effect=mapping):
            self.execute('apply', request)
        for relative in self.k.b.paths:
            info = (self.k.b.tree / relative).stat()
            self.assertIn((info.st_dev, info.st_ino), checked)

    def test_real_sigterm_preserves_partial_and_never_continues(self):
        _, request, _ = self.request()
        pid = os.fork()
        if pid == 0:
            transport.bind_cancellation(self.k.engine)
            self.k.engine.checkpoint = lambda phase, index: os.kill(os.getpid(), signal.SIGTERM) if phase == 'AfterEntry' and index == 0 else None
            try:
                self.execute('apply', request)
            except self.reader.Rejected as error:
                os._exit(0 if str(error) == 'CONTAINER_RECURSIVE_ACL_RESTORE_CANCELED' else 2)
            except BaseException:
                os._exit(3)
            os._exit(4)
        _, status = os.waitpid(pid, 0)
        self.assertTrue(os.WIFEXITED(status))
        self.assertEqual(0, os.WEXITSTATUS(status))
        before = self.k.b.snapshot()
        with self.k.no_write():
            result = self.execute('inspect', request)
            replay = self.execute('apply', request)
        self.assertEqual(('Partial', 1, 3), (result['state'], result['changedCount'], result['notStartedCount']))
        self.assertEqual(result['outcomeDigest'], replay['outcomeDigest'])
        self.assertEqual(before, self.k.b.snapshot())

    def test_unknown_actions_and_per_inode_payloads_are_refused_before_write(self):
        _, request, _ = self.request()
        with self.assertRaisesRegex(transport.Rejected, 'ACTION_INVALID'):
            self.execute('restore;sh', request)
        with self.assertRaises(self.reader.Rejected):
            self.execute('apply', {**request, 'hostPath': '/etc'})
        with self.assertRaises(self.reader.Rejected):
            self.execute('plan', {**request['plan']['command'], 'entries': []})
        self.assertFalse(list(self.k.state.iterdir()))


if __name__ == '__main__':
    unittest.main()
