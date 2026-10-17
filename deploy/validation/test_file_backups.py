"""Real Linux backup I/O with explicit synthetic mount/udev/sysfs fixtures.

These are NOT physical HDD evidence. A separately recorded workstation probe
must attest its actual mounted device before this policy can be deployed.
"""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import stat
import signal
import tempfile
import time
import unittest
import uuid
from unittest.mock import patch
import test_file_transport as common

spec = importlib.util.spec_from_file_location('file_backups', Path(__file__).resolve().parents[1] / 'host/whaledeck-file-backups.py')
backups = importlib.util.module_from_spec(spec); spec.loader.exec_module(backups)
install_spec = importlib.util.spec_from_file_location('configure_file_backups', Path(__file__).resolve().parents[1] / 'host/configure-file-backups.py')
installer = importlib.util.module_from_spec(install_spec); install_spec.loader.exec_module(installer)
driver = common.driver


@unittest.skipUnless(os.geteuid() == 0, 'requires actual Linux root filesystem semantics')
class FileBackupTests(unittest.TestCase):
    def setUp(self):
        self.fixture = common.FileTransportTests(); self.fixture.setUp()
        self.root = self.fixture.root
        self.temporary = tempfile.TemporaryDirectory(prefix='wd-backup-store-')
        self.disk = Path(self.temporary.name); self.target = self.disk / 'versions'; self.target.mkdir(mode=0o700)
        self.device = f'{os.major(self.target.stat().st_dev)}:{os.minor(self.target.stat().st_dev)}'
        self.udev = self.disk / 'udev'; self.udev.mkdir()
        self.uuid = '00000000-0000-0000-0000-000000000001'
        self.udev_record = self.udev / ('b' + self.device)
        self.udev_record.write_text('E:ID_FS_UUID=' + self.uuid + '\nE:ID_FS_USAGE=filesystem\n')
        self.sys = self.disk / 'sys'; self.rotational = self.sys / self.device / 'queue' / 'rotational'
        self.rotational.parent.mkdir(parents=True); self.rotational.write_bytes(b'1\n')
        self.mountinfo = self.disk / 'mountinfo'
        self.mountinfo.write_text(f'1 0 {self.device} / {self.disk} rw - ext4 /dev/fixture-only rw\n')
        self.configuration = self.disk / 'policy.json'
        self.policy = {'schemaVersion': 1, 'storageId': backups.STORAGE, 'mountPoint': str(self.disk), 'filesystemUuid': self.uuid,
            'targetRelative': 'versions', 'reserveBytes': 512 * 1024**2, 'reservePercent': 5, 'maxVersions': 10000}
        self.write_policy()
        self.patches = [patch.object(backups, 'CONFIGURATION', str(self.configuration)), patch.object(backups, 'UDEV', str(self.udev)),
            patch.object(backups, 'SYS_DEVICES', str(self.sys)), patch.object(backups, 'MOUNTINFO', str(self.mountinfo))]
        for item in self.patches: item.start()
        self.engine = backups.FileBackups(driver)

    def tearDown(self):
        for item in reversed(self.patches): item.stop()
        self.temporary.cleanup(); self.fixture.tearDown()

    def write_policy(self): self.configuration.write_text(json.dumps(self.policy))

    def source(self, content=b'old\x00\xff\xe4\xb8\xad', name='old.txt'):
        (self.root / name).write_bytes(content); info = (self.root / name).stat()
        return {'operationId': uuid.uuid4().hex, 'owner': 'A' * 64, 'rootId': 'test.shared', 'path': name,
            'expectedVersion': driver.version(info), 'originalIdentity': driver.publication_identity(info),
            'originalSha256': hashlib.sha256(content).hexdigest().upper()}

    def ensure(self, record):
        parent = os.open(self.root, os.O_RDONLY | os.O_DIRECTORY)
        try: return self.engine.ensure(record, parent, record['path'])
        finally: os.close(parent)

    def install(self, check=False, target='versions'):
        def modules(name, _): return driver if name.endswith('_driver') else backups
        with patch.object(installer, 'MOUNT_POINT', str(self.disk)), patch.object(installer, 'TARGET_RELATIVE', target), patch.object(installer, 'load', side_effect=modules):
            installer.configure(check=check)

    def test_fresh_install_creates_missing_ancestors_only_below_attested_mount(self):
        self.configuration.unlink()
        relative = 'missing/ancestors/versions'
        self.install(target=relative)
        target = self.disk / relative
        self.assertEqual(stat.S_IMODE(target.stat().st_mode), 0o700)
        self.assertEqual(stat.S_IMODE(target.parent.stat().st_mode), 0o755)
        self.assertEqual(json.loads(self.configuration.read_bytes())['targetRelative'], relative)

    def test_installer_refuses_symlink_or_writable_ancestor_without_writing_through_it(self):
        relative = 'ancestor/versions'
        self.policy['targetRelative'] = relative; self.write_policy()
        outside = self.disk / 'outside'; outside.mkdir()
        ancestor = self.disk / 'ancestor'; ancestor.symlink_to(outside, target_is_directory=True)
        with self.assertRaises((OSError, driver.Rejected)): self.install(target=relative)
        self.assertFalse((outside / 'versions').exists())
        ancestor.unlink(); ancestor.mkdir(mode=0o777); ancestor.chmod(0o777)
        with self.assertRaisesRegex(RuntimeError, 'FILES_BACKUP_DIRECTORY_UNSAFE'): self.install(target=relative)
        self.assertFalse((ancestor / 'versions').exists())

    def user_owned_mount(self, existing_namespace):
        self.configuration.unlink()
        private_configuration = self.disk / 'configuration'; private_configuration.mkdir(mode=0o700)
        self.configuration = private_configuration / 'policy.json'
        relative = 'DockerData/whaledeck/file-backups'
        data = self.disk / 'DockerData'; data.mkdir(mode=0o775); data.chmod(0o775); os.chown(data, 1000, 1000)
        if existing_namespace: (data / 'whaledeck').mkdir(mode=0o700)
        os.chown(self.disk, 1000, 1000); self.disk.chmod(0o755)
        before = self.disk.stat(), data.stat()
        with patch.object(backups, 'CONFIGURATION', str(self.configuration)): self.install(target=relative)
        self.assertEqual([(item.st_uid, item.st_gid, stat.S_IMODE(item.st_mode)) for item in before],
            [(item.stat().st_uid, item.stat().st_gid, stat.S_IMODE(item.stat().st_mode)) for item in (self.disk, data)])
        for path in (data / 'whaledeck', self.disk / relative):
            self.assertEqual(path.stat().st_uid, 0); self.assertFalse(path.stat().st_mode & 0o022)
        self.assertEqual(stat.S_IMODE((self.disk / relative).stat().st_mode), 0o700)

    def test_existing_root_private_namespace_below_user_owned_mount_and_storage_is_preserved(self):
        self.user_owned_mount(existing_namespace=True)

    def test_fresh_root_namespace_below_user_owned_mount_and_storage_is_created_without_chown(self):
        self.user_owned_mount(existing_namespace=False)

    def test_existing_non_root_backup_namespace_is_not_adopted_or_changed(self):
        relative = 'DockerData/whaledeck/file-backups'
        self.policy['targetRelative'] = relative; self.write_policy()
        namespace = self.disk / 'DockerData' / 'whaledeck'; namespace.mkdir(parents=True); os.chown(namespace, 1000, 1000)
        with self.assertRaisesRegex(RuntimeError, 'FILES_BACKUP_DIRECTORY_UNSAFE'): self.install(target=relative)
        self.assertEqual(namespace.stat().st_uid, 1000); self.assertFalse((namespace / 'file-backups').exists())

    def test_installer_creates_policy_atomically_then_preserves_user_configuration(self):
        self.configuration.unlink(); self.install()
        self.assertEqual(json.loads(self.configuration.read_bytes()), self.policy)
        self.assertEqual(stat.S_IMODE(self.configuration.stat().st_mode), 0o600)
        self.policy['reservePercent'] = 12; self.write_policy()
        before = self.configuration.read_bytes(); inode = self.configuration.stat().st_ino
        self.install(); self.assertEqual(self.configuration.read_bytes(), before); self.assertEqual(self.configuration.stat().st_ino, inode)

    def test_installer_missing_mount_does_not_create_policy_or_fallback_directory(self):
        self.configuration.unlink(); self.target.rmdir(); self.mountinfo.write_bytes(b'')
        with self.assertRaisesRegex(driver.Rejected, 'FILES_BACKUP_MOUNT_MISMATCH'): self.install()
        self.assertFalse(self.configuration.exists()); self.assertFalse(self.target.exists())

    def test_installer_check_is_read_only_and_never_recreates_missing_objects(self):
        before = self.configuration.stat(); self.install(check=True)
        self.assertEqual((self.configuration.stat().st_ino, self.configuration.stat().st_mtime_ns), (before.st_ino, before.st_mtime_ns))
        self.target.rmdir()
        with self.assertRaises(FileNotFoundError): self.install(check=True)
        self.assertFalse(self.target.exists())

    def test_installer_does_not_take_over_an_unsafe_existing_target(self):
        self.target.chmod(0o755)
        with self.assertRaisesRegex(driver.Rejected, 'FILES_BACKUP_DIRECTORY_UNSAFE'): self.install()
        self.assertEqual(stat.S_IMODE(self.target.stat().st_mode), 0o755)

    def test_installer_concurrent_policy_is_preserved_instead_of_overwritten(self):
        self.configuration.unlink(); concurrent = {**self.policy, 'reservePercent': 15}
        def appeared(*_):
            descriptor = os.open(self.configuration, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
            with os.fdopen(descriptor, 'w') as stream: json.dump(concurrent, stream)
            raise FileExistsError()
        with patch.object(driver, 'exchange', side_effect=appeared), self.assertRaisesRegex(RuntimeError, 'FILES_BACKUP_CONFIGURATION_CHANGED'):
            self.install()
        self.assertEqual(json.loads(self.configuration.read_bytes()), concurrent)
        self.assertEqual(list(self.disk.glob('.file-backups-*')), [])

    def test_full_binary_copy_is_durable_private_version_bound_and_replayed_without_write(self):
        content = bytes(range(256)) * 257; record = self.source(content, '旧文件.bin')
        result = self.ensure(record); data = self.target / (record['operationId'] + '.data')
        self.assertEqual(data.read_bytes(), content); self.assertEqual(result['sha256'], record['originalSha256'])
        self.assertEqual(result['sourceVersion'], record['expectedVersion']); self.assertFalse(result['differentSourceDevice'])
        self.assertEqual(stat.S_IMODE(data.stat().st_mode), 0o600); self.assertEqual(data.stat().st_uid, 0); self.assertEqual(data.stat().st_nlink, 1)
        original_inode = data.stat().st_ino
        with patch.object(driver, 'exchange', side_effect=AssertionError('replay must not copy or rename')):
            self.assertEqual(self.ensure(record), result)
        self.assertEqual(data.stat().st_ino, original_inode)
        self.assertEqual((self.root / record['path']).read_bytes(), content)

    def test_missing_mount_uuid_mismatch_ssd_and_read_only_device_are_rejected_without_data_write(self):
        record = self.source()
        for filename, invalid in ((self.mountinfo, b''), (self.udev_record, b'E:ID_FS_UUID=other\nE:ID_FS_USAGE=filesystem\n'), (self.rotational, b'0\n')):
            original = filename.read_bytes()
            with self.subTest(file=filename.name):
                filename.write_bytes(invalid)
                try:
                    with self.assertRaises(driver.Rejected): self.ensure(record)
                    self.assertEqual(list(self.target.iterdir()), [])
                finally: filename.write_bytes(original)
        original_stat = os.fstatvfs
        def readonly(fd):
            info = original_stat(fd)
            return type('ReadOnly', (), {'f_flag': info.f_flag | os.ST_RDONLY})()
        with patch.object(backups.os, 'fstatvfs', side_effect=readonly), self.assertRaisesRegex(driver.Rejected, 'FILES_BACKUP_READ_ONLY'):
            self.ensure(record)
        self.assertEqual(list(self.target.iterdir()), [])

    def test_policy_and_device_metadata_permissions_symlinks_and_duplicate_json_are_rejected(self):
        record = self.source()
        for file in (self.configuration, self.udev_record):
            file.chmod(0o666)
            with self.assertRaises(driver.Rejected): self.ensure(record)
            file.chmod(0o644)
        self.target.chmod(0o755)
        with self.assertRaisesRegex(driver.Rejected, 'FILES_BACKUP_DIRECTORY_UNSAFE'): self.ensure(record)
        self.target.chmod(0o700)
        original = self.configuration.read_bytes(); self.configuration.unlink(); self.configuration.symlink_to(self.disk / 'elsewhere')
        with self.assertRaises((OSError, driver.Rejected)): self.ensure(record)
        self.configuration.unlink(); self.configuration.write_bytes(original)
        self.configuration.write_text('{"schemaVersion":1,"schemaVersion":1}')
        with self.assertRaises(driver.Rejected): self.ensure(record)

    def test_capacity_and_history_limits_preserve_source_and_prior_versions(self):
        record = self.source(); original_stat = os.fstatvfs
        def full(fd):
            info = original_stat(fd)
            return type('Full', (), {'f_flag': info.f_flag, 'f_blocks': info.f_blocks, 'f_frsize': info.f_frsize, 'f_bavail': 0})()
        with patch.object(backups.os, 'fstatvfs', side_effect=full), self.assertRaisesRegex(driver.Rejected, 'FILES_BACKUP_CAPACITY_REQUIRED'):
            self.ensure(record)
        self.assertEqual(list(self.target.iterdir()), [])
        self.policy['maxVersions'] = 1; self.write_policy(); self.ensure(record)
        next_record = self.source()
        with self.assertRaisesRegex(driver.Rejected, 'FILES_BACKUP_HISTORY_LIMIT'): self.ensure(next_record)
        self.assertEqual(len(list(self.target.iterdir())), 2)

    def test_invalid_creation_time_is_rejected_before_creating_any_backup_object(self):
        record = self.source()
        with patch.object(backups.time, 'time_ns', return_value=-1), self.assertRaisesRegex(driver.Rejected, 'FILES_BACKUP_METADATA_UNSAFE'):
            self.ensure(record)
        self.assertEqual(list(self.target.iterdir()), [])

    def test_failed_copy_keeps_explicit_incomplete_receipt_and_never_overwrites_source_or_retries(self):
        record = self.source(); original = (self.root / record['path']).read_bytes()
        with patch.object(driver, 'exchange', side_effect=driver.Rejected('FILES_TEST_INTERRUPTION')), self.assertRaises(driver.Rejected): self.ensure(record)
        saved = json.loads((self.target / (record['operationId'] + '.json')).read_bytes())
        self.assertEqual(saved['state'], 'Preparing'); self.assertTrue((self.target / (record['operationId'] + '.partial')).is_file())
        with self.assertRaisesRegex(driver.Rejected, 'FILES_BACKUP_INCOMPLETE'): self.ensure(record)
        self.assertEqual((self.root / record['path']).read_bytes(), original)

    def test_complete_data_with_preparing_receipt_recovers_by_verification_not_copy(self):
        record = self.source(); result = self.ensure(record)
        metadata = self.target / (record['operationId'] + '.json'); saved = json.loads(metadata.read_bytes()); saved['state'] = 'Preparing'; metadata.write_text(json.dumps(saved))
        with patch.object(driver, 'exchange', side_effect=AssertionError('recovery must not copy or rename')):
            self.assertEqual(self.ensure(record), result)
        self.assertEqual(json.loads(metadata.read_bytes())['state'], 'Verified')

    def test_corrupt_prior_copy_and_wrong_owner_or_source_binding_are_not_adopted(self):
        record = self.source(); self.ensure(record)
        with self.assertRaisesRegex(driver.Rejected, 'FILES_BACKUP_IDEMPOTENCY_CONFLICT'): self.ensure({**record, 'owner': 'B' * 64})
        data = self.target / (record['operationId'] + '.data'); data.write_bytes(b'corrupt')
        with self.assertRaisesRegex(driver.Rejected, 'FILES_BACKUP_CONTENT_INVALID'): self.ensure(record)
        (self.root / record['path']).write_bytes(b'changed')
        with self.assertRaisesRegex(driver.Rejected, 'FILES_VERSION_CONFLICT'): self.ensure(record)

    def test_hardlinked_backup_data_and_stale_device_ownership_are_not_accepted(self):
        record = self.source(); self.ensure(record)
        data = self.target / (record['operationId'] + '.data'); os.link(data, self.disk / 'hardlink')
        with self.assertRaises(driver.Rejected): self.ensure(record)
        os.unlink(self.disk / 'hardlink'); os.chown(self.udev_record, 1000, 1000)
        with self.assertRaisesRegex(driver.Rejected, 'FILES_BACKUP_DEVICE_METADATA_UNSAFE'): self.ensure(record)

    def test_actual_sigkill_before_or_after_copy_commit_never_rewrites_source_on_recovery(self):
        for point in ('before-data-rename', 'before-verified-metadata'):
            with self.subTest(point=point):
                record = self.source(); old = (self.root / record['path']).read_bytes()
                child = os.fork()
                if child == 0:
                    try:
                        exchange = driver.exchange; save = driver.save_receipt
                        def kill_rename(*args):
                            if point == 'before-data-rename': os.kill(os.getpid(), signal.SIGKILL)
                            return exchange(*args)
                        def kill_metadata(*args):
                            if args[2].get('state') == 'Verified' and point == 'before-verified-metadata': os.kill(os.getpid(), signal.SIGKILL)
                            return save(*args)
                        driver.exchange = kill_rename; driver.save_receipt = kill_metadata
                        self.ensure(record)
                    except BaseException: os._exit(2)
                    os._exit(3)
                deadline = time.monotonic() + 10; status = None
                while time.monotonic() < deadline:
                    done, code = os.waitpid(child, os.WNOHANG)
                    if done: status = code; break
                    time.sleep(0.01)
                if status is None:
                    os.kill(child, signal.SIGKILL); os.waitpid(child, 0); self.fail('backup child exceeded test deadline')
                self.assertTrue(os.WIFSIGNALED(status)); self.assertEqual(os.WTERMSIG(status), signal.SIGKILL)
                if point == 'before-data-rename':
                    with self.assertRaisesRegex(driver.Rejected, 'FILES_BACKUP_INCOMPLETE'): self.ensure(record)
                else:
                    with patch.object(driver, 'exchange', side_effect=AssertionError('recovery cannot copy again')):
                        self.assertEqual(self.ensure(record)['sha256'], record['originalSha256'])
                self.assertEqual((self.root / record['path']).read_bytes(), old)


if __name__ == '__main__': unittest.main()
