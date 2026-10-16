"""Actual Linux mutation/recovery tests in owned temporary roots only."""
import importlib.util
import contextlib
import json
import os
from pathlib import Path
import signal
import select
import sys
import stat
import subprocess
import time
import unittest
import uuid
from unittest.mock import patch
import test_file_transport as common

spec = importlib.util.spec_from_file_location('file_operations', Path(__file__).resolve().parents[1] / 'host/whaledeck-file-operations.py')
operations = importlib.util.module_from_spec(spec)
spec.loader.exec_module(operations)
driver = common.driver


@unittest.skipUnless(os.geteuid() == 0, 'Requires actual Linux root filesystem semantics')
class FileOperationTests(unittest.TestCase):
    tearDown = common.FileTransportTests.tearDown
    revision = common.FileTransportTests.revision

    def setUp(self):
        common.FileTransportTests.setUp(self)
        entries = json.loads(self.registry.read_text()); entries[0]['allowedActions'].append('files-mutate')
        self.registry.write_text(json.dumps(entries)); self.engine = operations.FileOperations(driver)

    def command(self, kind='copy', source='source', target='target', mode=''):
        return {'kind': kind, 'sourceRootId': '' if kind == 'mkdir' else 'test.shared', 'sourcePath': '' if kind == 'mkdir' else source,
                'targetRootId': '' if kind == 'chmod' else 'test.shared', 'targetPath': '' if kind == 'chmod' else target,
                'expectedVersion': 'ABSENT' if kind == 'mkdir' else self.revision(source), 'mode': mode}

    def request(self, command):
        return {'operationId': uuid.uuid4().hex, 'owner': 'A' * 64, 'plan': self.engine.plan(command)}

    def lookup(self, request):
        return self.engine.lookup({key: request[key] for key in ('operationId', 'owner')})

    def test_create_directory_permissions_and_replay_do_not_recreate(self):
        request = self.request(self.command('mkdir', target='中文目录', mode='0750'))
        result = self.engine.apply(request); self.assertEqual(result['state'], 'Completed')
        folder = self.root / '中文目录'; self.assertEqual(stat.S_IMODE(folder.stat().st_mode), 0o750)
        before = folder.stat().st_ino; (folder / 'external').write_bytes(b'keep')
        replay = self.engine.apply(request)
        self.assertEqual(replay['state'], 'Completed'); self.assertFalse(replay['bindingCurrent'])
        self.assertEqual(folder.stat().st_ino, before); self.assertEqual((folder / 'external').read_bytes(), b'keep')

    def test_copy_exact_unicode_bytes_strips_special_bits_and_preserves_source(self):
        content = os.urandom(100007); source = self.root / 'source'; source.write_bytes(content); source.chmod(0o4750)
        request = self.request(self.command(target='中文副本'))
        self.assertEqual(self.engine.apply(request)['state'], 'Completed')
        self.assertEqual((self.root / '中文副本').read_bytes(), content); self.assertEqual(source.read_bytes(), content)
        self.assertEqual(stat.S_IMODE((self.root / '中文副本').stat().st_mode), 0o750)
        self.assertEqual((self.root / '中文副本').stat().st_mtime_ns, source.stat().st_mtime_ns)
        (self.root / '中文副本').write_bytes(b'new-external')
        self.assertFalse(self.engine.apply(request)['bindingCurrent'])
        self.assertEqual((self.root / '中文副本').read_bytes(), b'new-external')

    def test_move_keeps_complete_original_private_and_does_not_delete_new_source(self):
        content = os.urandom(30007); (self.root / 'source').write_bytes(content); (self.root / 'folder').mkdir()
        request = self.request(self.command('move', target='folder/target'))
        result = self.engine.apply(request); self.assertEqual(result['state'], 'Completed'); self.assertTrue(result['sourceRetained'])
        self.assertFalse((self.root / 'source').exists()); self.assertEqual((self.root / 'folder/target').read_bytes(), content)
        self.assertEqual((self.root / driver.STAGING / (request['operationId'] + '.mutation')).read_bytes(), content)
        (self.root / 'source').write_bytes(b'new-source')
        self.assertEqual(self.engine.apply(request)['state'], 'Completed'); self.assertEqual((self.root / 'source').read_bytes(), b'new-source')

    def test_permissions_are_exact_ordinary_bits_and_stale_plan_is_rejected(self):
        source = self.root / 'source'; source.write_bytes(b'private'); source.chmod(0o644)
        request = self.request(self.command('chmod', mode='0600'))
        self.assertEqual(self.engine.apply(request)['state'], 'Completed'); self.assertEqual(stat.S_IMODE(source.stat().st_mode), 0o600)
        source.chmod(0o640); replay = self.engine.apply(request)
        self.assertFalse(replay['bindingCurrent']); self.assertEqual(stat.S_IMODE(source.stat().st_mode), 0o640)
        for mode in ('4755', '2755', '1777', '9999', '777', '-1'):
            with self.subTest(mode=mode), self.assertRaises(driver.Rejected): self.engine.plan(self.command('chmod', mode=mode))

    def test_owner_operation_and_fingerprint_are_bound_permanently(self):
        (self.root / 'source').write_bytes(b'body'); request = self.request(self.command()); self.engine.apply(request)
        other = {**request, 'owner': 'B' * 64}
        with self.assertRaisesRegex(driver.Rejected, 'FILES_OPERATION_NOT_FOUND'): self.lookup(other)
        changed = {**request, 'plan': {**request['plan'], 'sha256': 'F' * 64}}
        with self.assertRaisesRegex(driver.Rejected, 'FILES_IDEMPOTENCY_CONFLICT'): self.engine.apply(changed)
        changed['operationId'] = uuid.uuid4().hex
        with self.assertRaises(driver.Rejected): self.engine.apply(changed)

    def test_target_conflict_and_source_change_never_overwrite(self):
        (self.root / 'source').write_bytes(b'original'); request = self.request(self.command())
        (self.root / 'target').write_bytes(b'external')
        with self.assertRaisesRegex(driver.Rejected, 'FILES_VERSION_CONFLICT'): self.engine.apply(request)
        self.assertEqual((self.root / 'target').read_bytes(), b'external')
        (self.root / 'target').unlink(); request = self.request(self.command())
        (self.root / 'source').write_bytes(b'new-original')
        with self.assertRaisesRegex(driver.Rejected, 'FILES_VERSION_CONFLICT'): self.engine.apply(request)
        self.assertFalse((self.root / 'target').exists())

    def test_explicit_mutation_permission_and_protected_targets_required(self):
        (self.root / 'source').write_bytes(b'original')
        for name in ('.env', 'compose.yml', 'secrets/a', '../outside'):
            with self.subTest(name=name), self.assertRaises(driver.Rejected): self.engine.plan(self.command(target=name))
        entries = json.loads(self.registry.read_text()); entries[0]['allowedActions'].remove('files-mutate'); self.registry.write_text(json.dumps(entries))
        with self.assertRaisesRegex(driver.Rejected, 'FILES_ROOT_FORBIDDEN'): self.engine.plan(self.command())

    def test_symlink_hardlink_fifo_directory_sources_cannot_be_mutated(self):
        outside = self.base / 'outside'; outside.write_bytes(b'outside'); (self.root / 'link').symlink_to(outside)
        os.link(outside, self.root / 'hard'); os.mkfifo(self.root / 'fifo'); (self.root / 'directory').mkdir()
        for source in ('link', 'hard', 'fifo', 'directory'):
            for kind in ('copy', 'move', 'chmod'):
                with self.subTest(source=source, kind=kind), self.assertRaises((driver.Rejected, OSError)):
                    self.engine.plan({'kind': kind, 'sourceRootId': 'test.shared', 'sourcePath': source,
                        'targetRootId': '' if kind == 'chmod' else 'test.shared', 'targetPath': '' if kind == 'chmod' else 'target',
                        'expectedVersion': 'A' * 64, 'mode': '0600' if kind == 'chmod' else ''})
        self.assertEqual(outside.read_bytes(), b'outside')

    def crash_after_rename(self, request, ordinal):
        child = os.fork()
        if child == 0:
            calls = 0; original = driver.exchange
            def crash(*args):
                nonlocal calls
                original(*args); calls += 1
                if calls == ordinal: os.kill(os.getpid(), signal.SIGKILL)
            try:
                with patch.object(driver, 'exchange', side_effect=crash): self.engine.apply(request)
            finally: os._exit(89)
        self.assertEqual(os.waitstatus_to_exitcode(self.wait_child(child)), -signal.SIGKILL)

    @staticmethod
    def wait_child(child):
        deadline = time.monotonic() + 10
        while time.monotonic() < deadline:
            result, status = os.waitpid(child, os.WNOHANG)
            if result == child: return status
            time.sleep(0.02)
        os.kill(child, signal.SIGKILL)
        _, status = os.waitpid(child, 0)
        raise AssertionError('Owned test child exceeded bounded deadline')

    def test_real_process_crash_after_copy_publish_recovers_without_reapply(self):
        (self.root / 'source').write_bytes(b'copy-body'); request = self.request(self.command())
        self.crash_after_rename(request, 1)
        with patch.object(driver, 'exchange', side_effect=AssertionError('must-not-reapply')):
            self.assertEqual(self.lookup(request)['state'], 'Completed')
        self.assertEqual((self.root / 'target').read_bytes(), b'copy-body')

    def test_real_process_crash_after_directory_publish_recovers_without_recreate(self):
        request = self.request(self.command('mkdir', target='folder', mode='0755'))
        self.crash_after_rename(request, 1)
        with patch.object(driver, 'exchange', side_effect=AssertionError('must-not-reapply')):
            self.assertEqual(self.lookup(request)['state'], 'Completed')

    def test_mid_copy_failure_removes_only_owned_partial_and_records_aborted(self):
        (self.root / 'source').write_bytes(b'original'); request = self.request(self.command())
        original = self.engine.hash_descriptor
        def broken(descriptor, info, output=None):
            if output is not None: os.write(output, b'partial'); raise OSError('synthetic-write-failure')
            return original(descriptor, info, output)
        with patch.object(self.engine, 'hash_descriptor', side_effect=broken), self.assertRaises(OSError): self.engine.apply(request)
        self.assertEqual(self.lookup(request)['state'], 'Aborted'); self.assertFalse((self.root / 'target').exists())
        self.assertFalse((self.root / driver.STAGING / (request['operationId'] + '.mutation')).exists())
        self.assertEqual((self.root / 'source').read_bytes(), b'original')

    def test_missing_directory_parent_after_plan_does_not_adopt_a_replacement(self):
        (self.root / 'folder').mkdir(); request = self.request(self.command('mkdir', target='folder/child', mode='0755'))
        (self.root / 'folder').rename(self.root / 'original'); (self.root / 'folder').mkdir()
        with self.assertRaisesRegex(driver.Rejected, 'FILES_VERSION_CONFLICT'): self.engine.apply(request)
        self.assertFalse((self.root / 'folder/child').exists()); self.assertFalse((self.root / 'original/child').exists())

    def test_permission_recovery_rejects_external_ownership_change(self):
        (self.root / 'source').write_bytes(b'original'); request = self.request(self.command('chmod', mode='0600'))
        original = os.fchmod
        def change_owner(descriptor, mode):
            original(descriptor, mode); os.fchown(descriptor, 12345, 12345)
        with patch.object(os, 'fchmod', side_effect=change_owner):
            self.assertEqual(self.engine.apply(request)['state'], 'Unknown')
        self.assertEqual((self.root / 'source').stat().st_uid, 12345)

    def test_noop_permissions_and_bounded_receipt_permissions(self):
        source = self.root / 'source'; source.write_bytes(b'body'); source.chmod(0o600)
        request = self.request(self.command('chmod', mode='0600'))
        self.assertEqual(self.engine.apply(request)['state'], 'Completed')
        receipt = self.state / 'operations' / (request['operationId'] + '.json'); receipt.chmod(0o644)
        with self.assertRaisesRegex(driver.Rejected, 'FILES_RECEIPT_UNSAFE'): self.lookup(request)

    def test_actual_sigkill_before_and_after_chmod_does_not_reapply_permissions(self):
        source = self.root / 'source'; source.write_bytes(b'body')
        for applied in (False, True):
            source.chmod(0o644); request = self.request(self.command('chmod', mode='0600'))
            child = os.fork()
            if child == 0:
                original = os.fchmod
                def killed(descriptor, mode):
                    if applied: original(descriptor, mode)
                    os.kill(os.getpid(), signal.SIGKILL)
                try:
                    with patch.object(os, 'fchmod', side_effect=killed): self.engine.apply(request)
                finally: os._exit(89)
            self.assertEqual(os.waitstatus_to_exitcode(self.wait_child(child)), -signal.SIGKILL)
            with patch.object(os, 'fchmod', side_effect=AssertionError('must-not-reapply')):
                result = self.lookup(request)
            self.assertEqual(result['state'], 'Completed' if applied else 'Aborted')
            self.assertEqual(stat.S_IMODE(source.stat().st_mode), 0o600 if applied else 0o644)

    def test_live_mutation_receipt_cannot_be_recovered_by_another_process(self):
        (self.root / 'source').write_bytes(b'body'); request = self.request(self.command())
        ready_read, ready_write = os.pipe(); release_read, release_write = os.pipe()
        child = os.fork()
        if child == 0:
            os.close(ready_read); os.close(release_write); original = self.engine.hash_descriptor
            def held(descriptor, info, output=None):
                if output is not None: os.write(ready_write, b'R'); os.read(release_read, 1)
                return original(descriptor, info, output)
            try:
                with patch.object(self.engine, 'hash_descriptor', side_effect=held): self.engine.apply(request)
            except BaseException: os._exit(90)
            os._exit(0)
        os.close(ready_write); os.close(release_read)
        try:
            self.assertTrue(select.select([ready_read], [], [], 5)[0], 'Owned child did not reach ready phase')
            self.assertEqual(os.read(ready_read, 1), b'R')
            receipt = self.state / 'operations' / (request['operationId'] + '.json'); before = receipt.read_bytes()
            with self.assertRaisesRegex(driver.Rejected, 'FILES_OPERATION_BUSY'): self.lookup(request)
            self.assertEqual(receipt.read_bytes(), before)
        finally:
            with contextlib.suppress(BrokenPipeError): os.write(release_write, b'R')
            os.close(release_write); os.close(ready_read)
            self.assertEqual(os.waitstatus_to_exitcode(self.wait_child(child)), 0)
        self.assertEqual(self.lookup(request)['state'], 'Completed')

    def test_upload_commit_uses_the_same_namespace_gate_and_keeps_staged_bytes(self):
        (self.root / 'source').write_bytes(b'body'); request = self.request(self.command())
        content = b'upload-body'
        upload = common.FileTransportTests.command(self, content, name='uploaded')
        driver.upload(upload, common.FileTransportTests.body(self, content), staged_only=True)
        ready_read, ready_write = os.pipe(); release_read, release_write = os.pipe(); child = os.fork()
        if child == 0:
            os.close(ready_read); os.close(release_write); original = self.engine.hash_descriptor
            def held(descriptor, info, output=None):
                if output is not None: os.write(ready_write, b'R'); os.read(release_read, 1)
                return original(descriptor, info, output)
            try:
                with patch.object(self.engine, 'hash_descriptor', side_effect=held): self.engine.apply(request)
            except BaseException: os._exit(90)
            os._exit(0)
        os.close(ready_write); os.close(release_read)
        lookup = {key: upload[key] for key in ('operationId', 'owner')}
        try:
            self.assertTrue(select.select([ready_read], [], [], 5)[0]); self.assertEqual(os.read(ready_read, 1), b'R')
            with self.assertRaisesRegex(driver.Rejected, 'FILES_OPERATION_BUSY'): driver.staged_action(lookup, 'publish')
            self.assertEqual(driver.status(lookup)['state'], 'Staged'); self.assertFalse((self.root / 'uploaded').exists())
            inline = common.FileTransportTests.command(self, content, name='inline')
            with self.assertRaisesRegex(driver.Rejected, 'FILES_OPERATION_BUSY'): driver.upload(inline, common.FileTransportTests.body(self, content))
            self.assertEqual(driver.status({key: inline[key] for key in ('operationId', 'owner')})['state'], 'Aborted')
            self.assertFalse((self.root / 'inline').exists())
        finally:
            with contextlib.suppress(BrokenPipeError): os.write(release_write, b'R')
            os.close(release_write); os.close(ready_read); self.assertEqual(os.waitstatus_to_exitcode(self.wait_child(child)), 0)
        self.assertEqual(driver.staged_action(lookup, 'publish')['state'], 'Completed'); self.assertEqual((self.root / 'uploaded').read_bytes(), content)

    def test_fixed_module_loader_rejects_untrusted_mode_and_uses_only_its_adjacent_module(self):
        module = self.base / 'whaledeck-file-operations.py'
        module.write_bytes(Path(operations.__file__).read_bytes()); module.chmod(0o750)
        with patch.object(driver, '__file__', str(self.base / 'whaledeck-files.py')), patch.dict(sys.modules, {driver.__name__: driver}):
            loaded = driver.operation_engine()
            request = self.request(self.command('mkdir', target='loader-test', mode='0750'))
            self.assertEqual(loaded.apply(request)['state'], 'Completed')
            module.chmod(0o777)
            with self.assertRaisesRegex(driver.Rejected, 'FILES_MODULE_UNSAFE'): driver.operation_engine()

    def test_busy_namespace_does_not_register_a_false_unknown_mutation(self):
        (self.root / 'source').write_bytes(b'body'); request = self.request(self.command())
        with driver.namespace_lock():
            with self.assertRaisesRegex(driver.Rejected, 'FILES_OPERATION_BUSY'): self.engine.apply(request)
        with self.assertRaisesRegex(driver.Rejected, 'FILES_OPERATION_NOT_FOUND'): self.lookup(request)
        self.assertEqual(self.engine.apply(request)['state'], 'Completed')

    def test_unknown_receipt_never_exposes_an_unverified_completed_version(self):
        request = self.request(self.command('mkdir', target='folder', mode='0755')); original = self.engine.check_parent; calls = 0
        def changed(*args, **kwargs):
            nonlocal calls
            calls += 1
            if calls == 2: raise driver.Rejected('FILES_PARENT_CHANGED')
            return original(*args, **kwargs)
        with patch.object(self.engine, 'check_parent', side_effect=changed): result = self.engine.apply(request)
        self.assertEqual(result['state'], 'Unknown'); self.assertEqual(result['version'], ''); self.assertFalse(result['sourceRetained'])
        self.assertTrue((self.root / 'folder').is_dir()); self.assertEqual(self.lookup(request)['state'], 'Unknown')

    def test_real_process_crash_between_move_steps_is_unknown_and_keeps_both(self):
        (self.root / 'source').write_bytes(b'move-body'); request = self.request(self.command('move'))
        self.crash_after_rename(request, 1)
        with patch.object(driver, 'exchange', side_effect=AssertionError('must-not-reapply')):
            self.assertEqual(self.lookup(request)['state'], 'Unknown'); self.assertEqual(self.engine.apply(request)['state'], 'Unknown')
        self.assertEqual((self.root / 'source').read_bytes(), b'move-body'); self.assertEqual((self.root / 'target').read_bytes(), b'move-body')

    def test_real_process_crash_after_source_quarantine_recovers_completed(self):
        (self.root / 'source').write_bytes(b'move-body'); request = self.request(self.command('move'))
        self.crash_after_rename(request, 2)
        self.assertEqual(self.lookup(request)['state'], 'Completed'); self.assertFalse((self.root / 'source').exists())
        self.assertEqual((self.root / driver.STAGING / (request['operationId'] + '.mutation')).read_bytes(), b'move-body')

    def test_external_source_replacement_is_retained_not_destroyed(self):
        (self.root / 'source').write_bytes(b'original'); request = self.request(self.command('move'))
        original = driver.exchange; calls = 0
        def replace_source(*args):
            nonlocal calls
            original(*args); calls += 1
            if calls == 1: (self.root / 'source').write_bytes(b'external-data')
        with patch.object(driver, 'exchange', side_effect=replace_source):
            self.assertEqual(self.engine.apply(request)['state'], 'Unknown')
        self.assertEqual((self.root / 'target').read_bytes(), b'original')
        self.assertEqual((self.root / driver.STAGING / (request['operationId'] + '.mutation')).read_bytes(), b'external-data')

    def test_actual_cross_filesystem_move_is_copy_and_retained_source_not_rename(self):
        mounted = self.base / 'other-filesystem'; mounted.mkdir()
        self.assertTrue(mounted.resolve().is_relative_to(self.base.resolve()))
        subprocess.run(['mount', '-t', 'tmpfs', '-o', 'size=8G,mode=0700', 'tmpfs', str(mounted)], check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        try:
            self.assertNotEqual(mounted.stat().st_dev, self.root.stat().st_dev)
            entries = json.loads(self.registry.read_text()); entries.append({**entries[0], 'id': 'test.other', 'path': str(mounted)})
            self.registry.write_text(json.dumps(entries)); (self.root / 'source').write_bytes(b'cross-filesystem')
            command = self.command('move'); command['targetRootId'] = 'test.other'; request = self.request(command)
            self.assertEqual(self.engine.apply(request)['state'], 'Completed'); self.assertEqual((mounted / 'target').read_bytes(), b'cross-filesystem')
            self.assertFalse((self.root / 'source').exists())
        finally: subprocess.run(['umount', str(mounted)], check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


if __name__ == '__main__': unittest.main()
