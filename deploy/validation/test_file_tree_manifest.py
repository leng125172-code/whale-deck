"""Real Linux tree inventory checks; no installed RPC/API/directory write claim."""
import errno
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import stat
import subprocess
import unittest
import test_file_transport as common

spec = importlib.util.spec_from_file_location('file_tree_manifest', Path(__file__).resolve().parents[1] / 'host/whaledeck-file-tree.py')
tree_module = importlib.util.module_from_spec(spec); spec.loader.exec_module(tree_module)
driver = common.driver


@unittest.skipUnless(os.name == 'posix' and os.geteuid() == 0, 'requires real Linux root descriptor validation')
class FileTreeManifestTests(unittest.TestCase):
    def setUp(self):
        self.fixture = common.FileTransportTests(); self.fixture.setUp()
        self.root, self.base, self.registry = self.fixture.root, self.fixture.base, self.fixture.registry
        self.source = self.root / 'source'; self.source.mkdir(); self.engine = tree_module.FileTreeManifest(driver)

    def tearDown(self): self.fixture.tearDown()

    def command(self, **values):
        return {'rootId': 'test.shared', 'path': 'source', 'expectedVersion': driver.version(self.source.stat()), **values}

    def test_binary_unicode_empty_directories_and_metadata_are_inventory_not_payload(self):
        (self.source / '空目录').mkdir(); folder = self.source / '代码'; folder.mkdir()
        data = bytes(range(256)) * 257 + b'\x00\xff'; (folder / 'a.txt').write_bytes(data); (self.source / 'empty').touch()
        os.chmod(folder / 'a.txt', 0o640)
        result = self.engine.scan(self.command())
        self.assertEqual((result['entryCount'], result['directoryCount'], result['fileCount'], result['totalBytes']), (5, 3, 2, len(data)))
        row = next(item for item in result['entries'] if item['path'] == '代码/a.txt')
        self.assertEqual(row['sha256'], hashlib.sha256(data).hexdigest().upper())
        self.assertEqual(row['sourceIdentity'], driver.publication_identity((folder / 'a.txt').stat()))
        self.assertEqual(row['sourceVersion'], driver.version((folder / 'a.txt').stat()))
        self.assertEqual(set(row), {'path', 'kind', 'sourceIdentity', 'sourceVersion', 'sizeBytes', 'sha256'})
        encoded = self.engine.encode(result)
        self.assertNotIn(str(self.base).encode(), encoded); self.assertNotIn(b'"data"', encoded)
        self.assertEqual([item['path'] for item in result['entries']], ['', 'empty', '代码', '代码/a.txt', '空目录'])
        self.assertFalse((self.source / '.whaledeck-files').exists())

    def test_repeated_reads_and_full_digest_revalidation_are_stable(self):
        (self.source / 'one').write_bytes(b'one\x00'); command = self.command()
        first = self.engine.scan(command); second = self.engine.verify(command, first['manifestSha256'])
        self.assertEqual(first, second)
        self.assertEqual(self.engine.scan(command)['manifestSha256'], first['manifestSha256'])

    def test_file_content_change_without_parent_entry_change_is_detected_by_digest(self):
        target = self.source / 'one'; target.write_bytes(b'old'); command = self.command(); before = self.engine.scan(command)
        root_version = driver.version(self.source.stat()); target.write_bytes(b'new')
        self.assertEqual(driver.version(self.source.stat()), root_version)
        with self.assertRaisesRegex(driver.Rejected, '^FILES_TREE_CHANGED$'):
            self.engine.verify(command, before['manifestSha256'])
        self.assertEqual(target.read_bytes(), b'new')

    def test_file_permission_change_is_not_mistaken_for_the_same_inventory(self):
        target = self.source / 'one'; target.write_bytes(b'old'); command = self.command(); before = self.engine.scan(command)
        target.chmod(0o640)
        with self.assertRaisesRegex(driver.Rejected, '^FILES_TREE_CHANGED$'):
            self.engine.verify(command, before['manifestSha256'])
        self.assertEqual(stat.S_IMODE(target.stat().st_mode), 0o640)

    def test_root_replacement_and_new_children_invalidate_the_expected_root_version(self):
        command = self.command(); (self.source / 'new').touch()
        with self.assertRaisesRegex(driver.Rejected, '^FILES_VERSION_CONFLICT$'): self.engine.scan(command)
        command = self.command(); self.source.rename(self.root / 'retained'); self.source.mkdir()
        with self.assertRaisesRegex(driver.Rejected, '^FILES_VERSION_CONFLICT$'): self.engine.scan(command)
        self.assertTrue((self.root / 'retained' / 'new').exists())

    def test_nested_protected_objects_are_never_silently_omitted(self):
        nested = self.source / 'nested'; nested.mkdir()
        for name in ('.env', '.ENV.production', 'compose.yml', 'resources.json', 'server.key', '.whaledeck-files', 'secrets'):
            with self.subTest(name=name):
                target = nested / name; target.write_bytes(b'private-no-body-output')
                with self.assertRaisesRegex(driver.Rejected, '^FILES_TREE_PROTECTED$'): self.engine.scan(self.command())
                self.assertEqual(target.read_bytes(), b'private-no-body-output'); target.unlink()

    def test_symlink_to_file_or_directory_and_fifo_are_rejected_without_following(self):
        outside = self.base / 'outside'; outside.mkdir(); (outside / 'private').write_bytes(b'private')
        for name, action in (('link-file', lambda path: path.symlink_to(outside / 'private')),
                ('link-dir', lambda path: path.symlink_to(outside, target_is_directory=True)),
                ('pipe', lambda path: os.mkfifo(path))):
            with self.subTest(name=name):
                target = self.source / name; action(target)
                with self.assertRaisesRegex(driver.Rejected, '^FILES_TREE_SPECIAL_OBJECT$'): self.engine.scan(self.command())
                target.unlink()
        self.assertEqual((outside / 'private').read_bytes(), b'private')

    def test_hardlinked_files_are_rejected_and_neither_link_is_changed(self):
        outside = self.base / 'private'; outside.write_bytes(b'private')
        os.link(outside, self.source / 'linked')
        with self.assertRaisesRegex(driver.Rejected, '^FILES_REGULAR_SINGLE_LINK_REQUIRED$'): self.engine.scan(self.command())
        self.assertEqual(outside.stat().st_nlink, 2); self.assertEqual((self.source / 'linked').read_bytes(), b'private')

    def test_requested_file_symlink_protected_root_and_missing_action_fail_closed(self):
        (self.root / 'plain').write_bytes(b'plain'); (self.root / 'link').symlink_to(self.source, target_is_directory=True)
        for path in ('plain', 'link'):
            with self.subTest(path=path), self.assertRaises(OSError): self.engine.scan(self.command(path=path))
        for path in ('../outside', '/etc', 'source/../source', '.env'):
            with self.subTest(path=path), self.assertRaises(driver.Rejected): self.engine.scan(self.command(path=path))
        registry = json.loads(self.registry.read_bytes()); registry[0]['allowedActions'].remove('files-list'); self.registry.write_text(json.dumps(registry))
        with self.assertRaisesRegex(driver.Rejected, '^FILES_ROOT_FORBIDDEN$'): self.engine.scan(self.command())

    def test_entry_budget_includes_directories_and_root_before_sorting(self):
        for name in ('a', 'b', 'c'): (self.source / name).mkdir()
        engine = tree_module.FileTreeManifest(driver, max_entries=3)
        with self.assertRaisesRegex(driver.Rejected, '^FILES_TREE_ENTRY_LIMIT$'): engine.scan(self.command())

    def test_depth_and_aggregate_size_limits_reject_instead_of_truncating(self):
        (self.source / 'child').mkdir(); (self.source / 'child' / 'one').write_bytes(b'123')
        with self.assertRaisesRegex(driver.Rejected, '^FILES_TREE_DEPTH_LIMIT$'):
            tree_module.FileTreeManifest(driver, max_depth=1).scan(self.command())
        (self.source / 'two').write_bytes(b'123')
        with self.assertRaisesRegex(driver.Rejected, '^FILES_TREE_SIZE_LIMIT$'):
            tree_module.FileTreeManifest(driver, max_bytes=5).scan(self.command())
        self.assertEqual((self.source / 'two').read_bytes(), b'123')

    def test_manifest_byte_budget_handles_escaped_names_without_large_output(self):
        for index in range(15): (self.source / ('"' * 120 + str(index))).touch()
        with self.assertRaisesRegex(driver.Rejected, '^FILES_TREE_MANIFEST_LIMIT$'):
            tree_module.FileTreeManifest(driver, max_manifest_bytes=2048).scan(self.command())

    def test_zero_data_budget_and_zero_depth_allow_an_empty_directory(self):
        result = tree_module.FileTreeManifest(driver, max_entries=1, max_depth=0, max_bytes=0).scan(self.command())
        self.assertEqual((result['entryCount'], result['totalBytes']), (1, 0))

    def test_cancel_and_deadline_never_create_staging_or_receipts(self):
        with self.assertRaisesRegex(driver.Rejected, '^FILES_TREE_CANCELED$'):
            tree_module.FileTreeManifest(driver, cancelled=lambda: True).scan(self.command())
        ticks = iter((0.0, 2.0))
        with self.assertRaisesRegex(driver.Rejected, '^FILES_TREE_DEADLINE$'):
            tree_module.FileTreeManifest(driver, timeout_seconds=1, clock=lambda: next(ticks)).scan(self.command())
        self.assertEqual(list(self.source.iterdir()), []); self.assertEqual(list(self.fixture.state.iterdir()), [])

    def test_content_change_in_a_stream_is_rejected_with_no_partial_manifest(self):
        target = self.source / 'large'; target.write_bytes(b'A' * (2 * driver.CHUNK + 7)); calls = 0
        def during_read():
            nonlocal calls
            calls += 1
            if calls == 7: target.write_bytes(b'B' * (2 * driver.CHUNK + 7))
            return False
        with self.assertRaisesRegex(driver.Rejected, '^FILES_READ_CHANGED$'):
            tree_module.FileTreeManifest(driver, cancelled=during_read).scan(self.command())
        self.assertEqual(target.read_bytes(), b'B' * (2 * driver.CHUNK + 7))

    def test_source_name_replacement_in_a_stream_preserves_both_external_objects(self):
        target = self.source / 'large'; original = b'A' * (2 * driver.CHUNK + 7); target.write_bytes(original); calls = 0
        retained = self.base / 'external-retained'
        def replace():
            nonlocal calls
            calls += 1
            if calls == 6: target.rename(retained); target.write_bytes(b'external-replacement')
            return False
        with self.assertRaises(driver.Rejected): tree_module.FileTreeManifest(driver, cancelled=replace).scan(self.command())
        self.assertEqual(retained.read_bytes(), original); self.assertEqual(target.read_bytes(), b'external-replacement')

    def test_policy_change_after_opening_is_rejected_without_adopting_it(self):
        (self.source / 'one').write_bytes(b'one'); calls = 0
        def tighten():
            nonlocal calls
            calls += 1
            if calls == 6:
                value = json.loads(self.registry.read_bytes()); value[0]['allowedActions'].remove('files-download'); self.registry.write_text(json.dumps(value))
            return False
        with self.assertRaisesRegex(driver.Rejected, '^FILES_ROOT_CHANGED$'):
            tree_module.FileTreeManifest(driver, cancelled=tighten).scan(self.command())
        self.assertNotIn('files-download', json.loads(self.registry.read_bytes())[0]['allowedActions'])

    def test_invalid_utf8_and_control_names_fail_instead_of_producing_unsafe_json(self):
        for name in ('bad\x1fname', 'bad\x85name'):
            target = self.source / name; target.touch()
            with self.assertRaisesRegex(driver.Rejected, '^FILES_PATH_INVALID$'): self.engine.scan(self.command())
            target.unlink()
        raw = os.fsencode(self.source) + b'/bad-\xff'; descriptor = os.open(raw, os.O_WRONLY | os.O_CREAT, 0o600); os.close(descriptor)
        try:
            with self.assertRaisesRegex(driver.Rejected, '^FILES_PATH_INVALID$'): self.engine.scan(self.command())
        finally: os.unlink(raw)

    def test_real_same_device_bind_mount_cannot_be_inventoried(self):
        outside = self.base / 'outside'; outside.mkdir(); (outside / 'private').write_bytes(b'private')
        mounted = self.source / 'mounted'; mounted.mkdir()
        result = subprocess.run(['mount', '--bind', str(outside), str(mounted)], stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        if result.returncode: self.skipTest('isolated Linux runner lacks bind-mount capability')
        try:
            self.assertEqual(mounted.stat().st_dev, self.source.stat().st_dev)
            with self.assertRaises(OSError) as error: self.engine.scan(self.command())
            self.assertEqual(error.exception.errno, errno.EXDEV)
        finally: subprocess.run(['umount', str(mounted)], check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)

    def test_invalid_limits_commands_and_fingerprint_do_not_start_a_scan(self):
        for values in ({'max_entries': True}, {'max_entries': 10001}, {'max_depth': 33}, {'max_bytes': -1},
                {'max_manifest_bytes': 127}, {'timeout_seconds': float('nan')}, {'timeout_seconds': 601}):
            with self.subTest(values=values), self.assertRaisesRegex(driver.Rejected, '^FILES_TREE_LIMITS_INVALID$'):
                tree_module.FileTreeManifest(driver, **values)
        for command in ({**self.command(), 'extra': True}, self.command(expectedVersion='bad'), self.command(path=''), self.command(rootId=False)):
            with self.assertRaises(driver.Rejected): self.engine.scan(command)
        for fingerprint in ('bad', False, 'a' * 64):
            with self.assertRaisesRegex(driver.Rejected, '^FILES_TREE_MANIFEST_INVALID$'): self.engine.verify(self.command(), fingerprint)

    def test_descriptors_are_closed_after_repeated_failures(self):
        (self.source / 'safe').write_bytes(b'safe'); (self.source / 'z-env.key').write_bytes(b'private')
        descriptors = len(os.listdir('/proc/self/fd'))
        for _ in range(50):
            with self.assertRaises(driver.Rejected): self.engine.scan(self.command())
        self.assertEqual(len(os.listdir('/proc/self/fd')), descriptors)


if __name__ == '__main__': unittest.main()
