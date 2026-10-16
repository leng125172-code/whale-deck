"""Actual Linux descriptor/rename/fsync tests in one owned temporary directory.

Run as root only so registry/state permissions and retained-file ownership are
not mocked. No production directory, Docker object or network is touched.
"""
import base64
import contextlib
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import select
import signal
import stat
import subprocess
import tempfile
import unittest
import uuid
from unittest.mock import patch
from types import SimpleNamespace

spec = importlib.util.spec_from_file_location('file_transport', Path(__file__).resolve().parents[1] / 'host/whaledeck-files.py')
driver = importlib.util.module_from_spec(spec)
spec.loader.exec_module(driver)


@unittest.skipUnless(os.geteuid() == 0, 'Requires root in a disposable Linux test environment, never a mocked permission pass')
class FileTransportTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix='wd-files-test-')
        self.base = Path(self.temporary.name)
        self.root = self.base / 'shared'; self.root.mkdir()
        self.state = self.base / 'state'; self.state.mkdir(mode=0o700)
        self.registry = self.base / 'resources.json'
        self.registry.write_text(json.dumps([{'id': 'test.shared', 'type': 'FileRoot', 'externalId': '测试文件',
            'protectionLevel': 'Managed', 'path': str(self.root), 'ownerUid': 0, 'ownerGid': 0,
            'allowedActions': ['files-list', 'files-stat', 'files-download', 'files-upload']}]))
        self.patches = [patch.object(driver, 'BASE', str(self.base)), patch.object(driver, 'REGISTRY', str(self.registry)), patch.object(driver, 'STATE', str(self.state))]
        for item in self.patches: item.start()

    def tearDown(self):
        for item in reversed(self.patches): item.stop()
        self.temporary.cleanup()

    def command(self, content=b'binary\x00\xff', name='测试.bin', expected='ABSENT', operation=None):
        return {'rootId': 'test.shared', 'path': name, 'operationId': operation or uuid.uuid4().hex, 'owner': 'A' * 64,
                'sizeBytes': len(content), 'sha256': hashlib.sha256(content).hexdigest().upper(), 'expectedVersion': expected}

    def body(self, content, trailing=b''):
        return io.BytesIO((json.dumps({'type': 'chunk', 'offset': 0, 'data': base64.b64encode(content).decode()}) + '\n' if content else '').encode() + b'{"type":"commit"}\n' + trailing)

    def revision(self, name):
        return driver.inspect({'rootId': 'test.shared', 'path': name})['version']

    def test_root_listing_and_unicode_stable_pagination(self):
        for name in ('a', 'b', '中文', '.env'): (self.root / name).write_bytes(b'x')
        first = driver.list_directory({'rootId': 'test.shared', 'path': '', 'after': '', 'take': 2})
        second = driver.list_directory({'rootId': 'test.shared', 'path': '', 'after': first['nextCursor'], 'take': 2})
        names = [row['name'] for row in first['entries'] + second['entries']]
        self.assertEqual(names, sorted(['a', 'b', '中文', '.env']))
        self.assertTrue(next(row for row in first['entries'] if row['name'] == '.env')['isProtected'])

    def test_paths_cannot_traverse_be_absolute_or_ambiguous(self):
        for name in ('../outside', '/etc/passwd', 'a//b', 'a/../b', 'a/./b', 'a\\b', 'a\x00b', ''):
            with self.subTest(name=name), self.assertRaises(driver.Rejected): driver.relative(name)

    def test_registry_permissions_and_duplicate_fields_are_not_adopted(self):
        os.chmod(self.registry, 0o666)
        with self.assertRaises(driver.Rejected): driver.roots()
        os.chmod(self.registry, 0o600)
        self.registry.write_text('[{"type":"FileRoot","type":"Container"}]')
        with self.assertRaises(driver.Rejected): driver.roots()

    def test_registry_cannot_register_outside_managed_base_or_core_configuration(self):
        value = json.loads(self.registry.read_text())
        for path in ('/etc', str(self.base / '../escape'), str(self.base / 'secrets'), str(self.base / '.env')):
            value[0]['path'] = path; self.registry.write_text(json.dumps(value))
            with self.subTest(path=path), self.assertRaises(driver.Rejected): driver.roots()

    def test_protected_names_denied_before_reading(self):
        for name in ('.env', '.env.production', 'x/compose.yml', 'secrets/key', 'app/manifest.json', '.git/config', 'private.pem'):
            with self.subTest(name=name), self.assertRaisesRegex(driver.Rejected, 'FILES_PROTECTED'):
                driver.inspect({'rootId': 'test.shared', 'path': name})

    def test_intermediate_and_leaf_symlinks_and_hardlinks_are_denied(self):
        outside = self.base / 'outside'; outside.mkdir(); (outside / 'secret').write_bytes(b'outside-only')
        (self.root / 'link').symlink_to(outside, target_is_directory=True)
        (self.root / 'leaf').symlink_to(outside / 'secret')
        os.link(outside / 'secret', self.root / 'hard')
        for name in ('link/secret', 'leaf', 'hard'):
            with self.subTest(name=name), self.assertRaises((OSError, driver.Rejected)):
                driver.inspect({'rootId': 'test.shared', 'path': name})
        self.assertEqual((outside / 'secret').read_bytes(), b'outside-only')

    def test_fifo_is_rejected_without_open_blocking(self):
        os.mkfifo(self.root / 'fifo')
        with self.assertRaisesRegex(driver.Rejected, 'FILES_REGULAR_SINGLE_LINK_REQUIRED'):
            driver.inspect({'rootId': 'test.shared', 'path': 'fifo'})

    def test_bind_mount_cannot_cross_registered_root_even_on_same_device(self):
        outside = self.base / 'outside'; outside.mkdir(); (outside / 'secret').write_bytes(b'outside')
        target = self.root / 'bind'; target.mkdir()
        self.assertTrue(target.resolve().is_relative_to(self.base.resolve()))
        self.assertTrue(outside.resolve().is_relative_to(self.base.resolve()))
        subprocess.run(['mount', '--bind', str(outside), str(target)], check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        try:
            with self.assertRaises(OSError): driver.inspect({'rootId': 'test.shared', 'path': 'bind/secret'})
        finally: subprocess.run(['umount', str(target)], check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)

    def test_binary_download_verifies_size_digest_and_version(self):
        content = os.urandom(100000); (self.root / 'binary').write_bytes(content)
        output = io.BytesIO()
        driver.download({'rootId': 'test.shared', 'path': 'binary', 'expectedVersion': self.revision('binary')}, output)
        rows = [json.loads(line) for line in output.getvalue().splitlines()]
        self.assertEqual(rows[0]['type'], 'metadata'); self.assertEqual(rows[-1]['type'], 'end')
        self.assertEqual(b''.join(base64.b64decode(row['data']) for row in rows[1:-1]), content)
        self.assertEqual(rows[-1]['sha256'], hashlib.sha256(content).hexdigest().upper())

    def test_changed_download_never_emits_successful_end(self):
        (self.root / 'file').write_bytes(b'a' * 100000); output = io.BytesIO(); emit = driver.emit
        def mutate(value, sink=None):
            emit(value, sink)
            if value.get('type') == 'chunk': (self.root / 'file').write_bytes(b'changed')
        with patch.object(driver, 'emit', side_effect=mutate), self.assertRaises(driver.Rejected):
            driver.download({'rootId': 'test.shared', 'path': 'file', 'expectedVersion': self.revision('file')}, output)
        self.assertFalse(any(json.loads(line)['type'] == 'end' for line in output.getvalue().splitlines()))

    def test_create_replay_and_changed_binding_never_overwrite_again(self):
        content = b'\x00\xffreal-bytes'; command = self.command(content)
        result = driver.upload(command, self.body(content))
        self.assertEqual(result['state'], 'Completed'); self.assertEqual((self.root / command['path']).read_bytes(), content)
        self.assertTrue(driver.upload(command, self.body(content))['replayed'])
        (self.root / command['path']).write_bytes(b'external-change')
        self.assertFalse(driver.upload(command, self.body(content))['bindingCurrent'])
        self.assertEqual((self.root / command['path']).read_bytes(), b'external-change')
        with self.assertRaisesRegex(driver.Rejected, 'FILES_IDEMPOTENCY_CONFLICT'):
            driver.upload({**command, 'path': 'other'}, self.body(content))

    def test_empty_upload_and_download_are_not_truncated(self):
        command = self.command(b'', 'empty'); result = driver.upload(command, self.body(b'')); output = io.BytesIO()
        driver.download({'rootId': 'test.shared', 'path': 'empty', 'expectedVersion': result['version']}, output)
        self.assertEqual([json.loads(row)['type'] for row in output.getvalue().splitlines()], ['metadata', 'end'])

    def test_overwrite_is_atomic_preserves_previous_bytes_and_permissions(self):
        target = self.root / 'file'; target.write_bytes(b'previous'); os.chmod(target, 0o640)
        command = self.command(b'new', 'file', self.revision('file'))
        result = driver.upload(command, self.body(b'new'))
        self.assertTrue(result['previousRetained']); self.assertEqual(target.read_bytes(), b'new')
        self.assertEqual(stat.S_IMODE(target.stat().st_mode), 0o640)
        self.assertEqual((self.root / driver.STAGING / (command['operationId'] + '.upload')).read_bytes(), b'previous')
        self.assertNotIn(driver.STAGING, [item['name'] for item in driver.list_directory({'rootId': 'test.shared', 'path': '', 'after': '', 'take': 100})['entries']])

    def test_wrong_version_hash_offset_eof_and_trailing_data_never_publish(self):
        for mode in ('version', 'hash', 'offset', 'eof', 'trailing'):
            target = self.root / ('file-' + mode); target.write_bytes(b'original')
            command = self.command(b'new', target.name, self.revision(target.name)); source = self.body(b'new')
            if mode == 'version': command['expectedVersion'] = 'A' * 64
            if mode == 'hash': command['sha256'] = 'B' * 64
            if mode == 'offset': source = io.BytesIO(b'{"type":"chunk","offset":1,"data":"bmV3"}\n')
            if mode == 'eof': source = io.BytesIO(b'')
            if mode == 'trailing': source = self.body(b'new', b'{"type":"commit"}\n')
            with self.subTest(mode=mode), self.assertRaises(driver.Rejected): driver.upload(command, source)
            self.assertEqual(target.read_bytes(), b'original')

    def test_capacity_and_four_slot_limits_are_real_and_release(self):
        with patch.object(driver.os, 'fstatvfs', return_value=SimpleNamespace(f_blocks=100, f_frsize=1, f_bavail=0)), self.assertRaisesRegex(driver.Rejected, 'FILES_CAPACITY_REQUIRED'):
            driver.upload(self.command(b'data'), self.body(b'data'))
        with contextlib.ExitStack() as stack:
            for _ in range(4): stack.enter_context(driver.slot())
            with self.assertRaisesRegex(driver.Rejected, 'FILES_TRANSFER_LIMIT'):
                with driver.slot(): pass
        with driver.slot(): pass

    def fork(self, action):
        child = os.fork()
        if child == 0:
            try: action(); os._exit(0)
            except BaseException: os._exit(99)
        return child

    def wait(self, child, expected):
        import time
        deadline = time.monotonic() + 10
        while time.monotonic() < deadline:
            result, code = os.waitpid(child, os.WNOHANG)
            if result:
                self.assertTrue(os.WIFEXITED(code)); self.assertEqual(os.WEXITSTATUS(code), expected); return
            time.sleep(.02)
        os.kill(child, signal.SIGKILL); os.waitpid(child, 0); self.fail('Owned child did not finish')

    def test_actual_process_death_mid_receive_recovers_aborted_without_publish(self):
        command = self.command(b'body')
        class KilledInput(io.BytesIO):
            count = 0
            def readline(self, *args):
                self.count += 1
                if self.count == 2: os._exit(77)
                return super().readline(*args)
        self.wait(self.fork(lambda: driver.upload(command, KilledInput(self.body(b'body').getvalue()))), 77)
        self.assertFalse((self.root / command['path']).exists())
        self.assertEqual(driver.status({'operationId': command['operationId'], 'owner': command['owner']})['state'], 'Aborted')
        self.assertFalse((self.root / driver.STAGING / (command['operationId'] + '.upload')).exists())

    def test_actual_process_death_after_rename_recovers_completed_from_inode_and_digest(self):
        target = self.root / 'file'; target.write_bytes(b'previous'); command = self.command(b'new', 'file', self.revision('file'))
        original_save = driver.save_receipt
        def crash(state, operation, record):
            if record['state'] == 'Completed': os._exit(78)
            original_save(state, operation, record)
        def action():
            with patch.object(driver, 'save_receipt', side_effect=crash): driver.upload(command, self.body(b'new'))
        self.wait(self.fork(action), 78)
        result = driver.status({'operationId': command['operationId'], 'owner': command['owner']})
        self.assertEqual(result['state'], 'Completed'); self.assertTrue(result['previousRetained'])
        self.assertEqual(target.read_bytes(), b'new')

    def test_actual_process_death_before_rename_recovers_aborted_without_changing_target(self):
        target = self.root / 'file'; target.write_bytes(b'previous'); command = self.command(b'new', 'file', self.revision('file'))
        def action():
            with patch.object(driver, 'exchange', side_effect=lambda *_: os._exit(79)): driver.upload(command, self.body(b'new'))
        self.wait(self.fork(action), 79)
        self.assertEqual(driver.status({'operationId': command['operationId'], 'owner': command['owner']})['state'], 'Aborted')
        self.assertEqual(target.read_bytes(), b'previous')

    def test_missing_root_during_recovery_returns_unknown_not_a_false_not_found(self):
        command = self.command(b'new')
        def action():
            with patch.object(driver, 'exchange', side_effect=lambda *_: os._exit(79)): driver.upload(command, self.body(b'new'))
        self.wait(self.fork(action), 79)
        self.root.rename(self.base / 'moved-root')
        result = driver.status({'operationId': command['operationId'], 'owner': command['owner']})
        self.assertEqual(result['state'], 'Unknown'); self.assertFalse(result['bindingCurrent'])

    def test_status_during_live_apply_does_not_recover_or_mutate_receipt(self):
        command = self.command(b'new'); read, write = os.pipe(); release, signal_release = os.pipe(); save = driver.save_receipt
        def paused(state, operation, record):
            save(state, operation, record)
            if record['state'] == 'Applying': os.write(write, b'r'); os.read(release, 1)
        def action():
            with patch.object(driver, 'save_receipt', side_effect=paused): driver.upload(command, self.body(b'new'))
        child = self.fork(action)
        try:
            self.assertTrue(select.select([read], [], [], 5)[0]); os.read(read, 1)
            result = driver.status({'operationId': command['operationId'], 'owner': command['owner']})
            self.assertTrue(result['isInProgress']); self.assertEqual(result['state'], 'Applying')
        finally:
            os.write(signal_release, b'x')
            self.wait(child, 0)
            for descriptor in (read, write, release, signal_release): os.close(descriptor)

    def test_external_race_preserves_displaced_bytes_and_marks_unknown_without_reapply(self):
        target = self.root / 'file'; target.write_bytes(b'previous'); command = self.command(b'new', 'file', self.revision('file')); exchange = driver.exchange
        def race(*args):
            target.write_bytes(b'external-newer')
            exchange(*args)
        with patch.object(driver, 'exchange', side_effect=race), self.assertRaisesRegex(driver.Rejected, 'FILES_CONCURRENT_PUBLICATION_UNKNOWN'):
            driver.upload(command, self.body(b'new'))
        self.assertEqual((self.root / driver.STAGING / (command['operationId'] + '.upload')).read_bytes(), b'external-newer')
        self.assertEqual(driver.status({'operationId': command['operationId'], 'owner': command['owner']})['state'], 'Unknown')
        with self.assertRaisesRegex(driver.Rejected, 'FILES_PREVIOUS_OPERATION_INCOMPLETE'): driver.upload(command, self.body(b'new'))


if __name__ == '__main__': unittest.main()
