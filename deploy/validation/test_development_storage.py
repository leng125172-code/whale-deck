import errno
import fcntl
import importlib.util
import io
import json
import os
from pathlib import Path
import select
import signal
import subprocess
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch


def load(name, filename):
    path = Path(__file__).resolve().parents[1] / 'host' / filename
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


files = load('workspace_test_files', 'whaledeck-files.py')
storage = load('workspace_test_storage', 'whaledeck-development-storage.py')
transport = load('workspace_test_transport', 'whaledeck-development-storage-transport.py')
real_fstatvfs = os.fstatvfs


@unittest.skipUnless(os.geteuid() == 0, 'Run as root in an isolated user namespace or the hosted test VM')
class DevelopmentStorageTests(unittest.TestCase):
    def setUp(self):
        self.addCleanup(patch.stopall)
        mount_test = self._testMethodName == 'test_sandbox_accepts_only_exact_real_bind_on_approved_filesystem'
        directory = tempfile.TemporaryDirectory(dir='/var/tmp' if mount_test else None)
        self.addCleanup(directory.cleanup)
        self.mount = Path(directory.name)
        self.root_id = 'workspace-nvme'
        self.base = self.mount / 'registered'
        self.root = self.base / self.root_id
        self.root.mkdir(mode=0o700, parents=True)
        self.base.chmod(0o755)
        self.config = self.mount / 'storage.json'
        self.info = self.mount / 'mountinfo'
        self.udev = self.mount / 'udev'
        self.udev.mkdir(mode=0o755)
        self.sys_devices = self.mount / 'devices'
        self.sys_dev = self.mount / 'dev-block'
        self.sys_dev.mkdir()
        self.nvme_class = self.mount / 'class-nvme'
        self.nvme_class.mkdir()
        self.controller = self.sys_devices / 'pci' / 'nvme' / 'nvme0'
        self.namespace = self.controller / 'nvme0n1'
        self.partition = self.namespace / 'nvme0n1p1'
        self.partition.mkdir(parents=True)
        (self.partition / 'partition').write_text('1\n')
        (self.controller / 'subsystem').symlink_to(self.nvme_class)
        (self.controller / 'transport').write_text('pcie\n')
        (self.namespace / 'queue').mkdir()
        (self.namespace / 'queue' / 'rotational').write_text('0\n')
        actual = self.mount.stat().st_dev
        self.dev = f'{os.major(actual)}:{os.minor(actual)}'
        (self.sys_dev / self.dev).symlink_to(self.partition)
        self.uuid = 'baa2470b-8d6a-4db5-abaa-7eff66a408ef'
        self.root_config = {'mountPoint': str(self.mount), 'filesystemUuid': self.uuid, 'reserveMiB': 512}
        self.write_config()
        self.record = self.udev / ('b' + self.dev)
        self.record.write_text(f'E:ID_FS_UUID={self.uuid}\nE:ID_FS_USAGE=filesystem\n')
        self.record.chmod(0o644)
        self.engine = storage.WorkspaceStorage(files)
        with self.engine.absolute(self.mount) as fd:
            self.mount_id = self.engine.mount_id(fd)
        self.write_mountinfo()
        for attr, value in {'CONFIG': self.config, 'BASE': self.base, 'MOUNTINFO': self.info, 'UDEV': self.udev,
                            'SYS_DEV': self.sys_dev, 'SYS_DEVICES': self.sys_devices, 'NVME_CLASS': self.nvme_class}.items():
            patch.object(storage, attr, value).start()
        self.request = {'schemaVersion': 1, 'rootId': self.root_id,
                        'workspaceId': 'ee96d069-0494-462b-9c99-250dfe97d55a',
                        'environmentSha256': 'a' * 64, 'diskMiB': 256}
        self.target = self.root / self.request['workspaceId']
        self.pending = self.root / ('.pending-' + self.request['workspaceId'])
        actual_space = os.statvfs(self.mount)
        patch.object(storage.os, 'fstatvfs', return_value=SimpleNamespace(
            f_flag=actual_space.f_flag, f_bavail=1024**3, f_frsize=4096)).start()

    def write_config(self):
        self.config.write_text(json.dumps({'schemaVersion': 1, 'roots': {self.root_id: self.root_config}}))
        self.config.chmod(0o600)

    def write_mountinfo(self, mount_id=None, root='/', filesystem='ext4'):
        self.info.write_text(f'{mount_id or self.mount_id} 0 {self.dev} {root} {self.mount} rw - {filesystem} /dev/qa rw\n')

    def test_publish_and_replay_match_exact_receipt_and_inode(self):
        proof = self.engine.allocate(self.request)
        self.assertEqual(proof, self.engine.allocate(self.request))
        self.assertEqual(proof, json.loads((self.target / storage.MARKER).read_bytes()))
        self.assertEqual(self.target.stat().st_ino, proof['directoryInode'])
        self.assertEqual(self.root.stat().st_ino, proof['rootInode'])
        self.assertEqual(0o700, self.target.stat().st_mode & 0o777)
        self.assertEqual(0o600, (self.target / storage.MARKER).stat().st_mode & 0o777)
        self.assertFalse(self.pending.exists())
        with self.assertRaisesRegex(files.Rejected, 'DIRECTORY_CONFLICT'):
            self.engine.allocate(dict(self.request, environmentSha256='b' * 64))
        self.assertEqual(proof, json.loads((self.target / storage.MARKER).read_bytes()))

    def test_bad_requests_never_create_a_child(self):
        for values in ({'workspaceId': '../production'}, {'rootId': 'not-approved'}, {'diskMiB': True},
                       {'schemaVersion': True}, {'environmentSha256': 'x' * 64}, {'path': '/data'}):
            with self.subTest(values=values), self.assertRaises(files.Rejected):
                self.engine.allocate(dict(self.request, **values))
        self.assertEqual([], list(self.root.iterdir()))

    def test_transport_rejects_duplicate_trailing_and_oversized_requests(self):
        valid = json.dumps(self.request).encode('ascii')
        for raw in (b'', valid + valid, b'{"rootId":"a","rootId":"b"}', b' ' * 1025, b'\xff'):
            with self.subTest(raw=raw[:30]), self.assertRaisesRegex(files.Rejected, 'REQUEST_INVALID'):
                transport.allocate(io.BytesIO(raw), files, storage)
        self.assertEqual([], list(self.root.iterdir()))

    @unittest.skipUnless(os.environ.get('WHALEDECK_TEST_PARENT_MNT'), 'Needs isolated mount namespace')
    def test_sandbox_accepts_only_exact_real_bind_on_approved_filesystem(self):
        self.assertNotEqual(os.readlink('/proc/self/ns/mnt'), os.environ['WHALEDECK_TEST_PARENT_MNT'])
        self.root_config['mountPoint'] = '/'
        self.write_config()
        self.base.chmod(0o700)
        other = self.mount / 'other'
        other.mkdir(mode=0o700)
        (other / self.root_id).mkdir(mode=0o700)
        request = json.dumps(self.request).encode('ascii')

        def bind(source, target):
            subprocess.run(['mount', '--bind', source, target], check=True)

        def unmount(target):
            subprocess.run(['umount', target], check=True)

        directory = storage.WorkspaceStorage.directory
        host_root = os.stat('/')

        def check_directory(engine, fd, *, private=False):
            # A one-UID namespace maps the external host's UID 0 to overflow.
            # All fixture directories still pass the real ownership checks.
            info = os.fstat(fd)
            if not private and host_root.st_uid == 65534 and os.path.samestat(info, host_root):
                return info
            return directory(engine, fd, private=private)

        with (patch.object(storage, 'MOUNTINFO', Path('/proc/self/mountinfo')),
              patch.object(storage.WorkspaceStorage, 'directory', check_directory),
              patch.object(storage.os, 'fstatvfs', real_fstatvfs)):
            # A different same-device subtree at the exact target must fail.
            bind(other, self.base)
            try:
                with self.assertRaisesRegex(files.Rejected, 'MOUNT_MISMATCH'):
                    transport.allocate(io.BytesIO(request), files, storage)
            finally:
                unmount(self.base)
            self.assertEqual([], list((other / self.root_id).iterdir()))
            bind(self.base, self.base)
            try:
                subprocess.run(['mount', '-o', 'remount,bind,ro', self.base], check=True)
                with self.assertRaisesRegex(files.Rejected, 'READ_ONLY'):
                    transport.allocate(io.BytesIO(request), files, storage)
                subprocess.run(['mount', '-o', 'remount,bind,rw', self.base], check=True)
                bind(self.root, self.root)
                try:
                    with self.assertRaises(OSError) as blocked:
                        transport.allocate(io.BytesIO(request), files, storage)
                    self.assertEqual(errno.EXDEV, blocked.exception.errno)
                finally:
                    unmount(self.root)
                result = transport.allocate(io.BytesIO(request), files, storage)
                self.assertEqual(result, transport.allocate(io.BytesIO(request), files, storage))
                self.assertEqual(result['directoryInode'], self.target.stat().st_ino)
            finally:
                unmount(self.base)

    def test_attestation_rejects_wrong_mount_root_filesystem_and_uuid(self):
        for mount_id, root, filesystem in (('0', '/', 'ext4'), (None, '/bound', 'ext4'), (None, '/', 'tmpfs')):
            with self.subTest(root=root, filesystem=filesystem):
                self.write_mountinfo(mount_id, root, filesystem)
                with self.assertRaises(files.Rejected):
                    self.engine.allocate(self.request)
        self.write_mountinfo()
        self.record.write_text(f'E:ID_FS_UUID={self.uuid}\nE:ID_FS_UUID=other\nE:ID_FS_USAGE=filesystem\n')
        with self.assertRaisesRegex(files.Rejected, 'DEVICE_MISMATCH'):
            self.engine.allocate(self.request)
        self.assertEqual([], list(self.root.iterdir()))

    def test_rejects_rotational_and_non_pcie_devices(self):
        (self.namespace / 'queue' / 'rotational').write_text('1\n')
        with self.assertRaisesRegex(files.Rejected, 'NVME_REQUIRED'):
            self.engine.allocate(self.request)
        (self.namespace / 'queue' / 'rotational').write_text('0\n')
        (self.controller / 'transport').write_text('tcp\n')
        with self.assertRaisesRegex(files.Rejected, 'NVME_REQUIRED'):
            self.engine.allocate(self.request)
        self.assertEqual([], list(self.root.iterdir()))

    def test_does_not_follow_configuration_root_or_workspace_links(self):
        copy = self.mount / 'config-copy'
        self.config.rename(copy)
        self.config.symlink_to(copy)
        with self.assertRaises(OSError):
            self.engine.allocate(self.request)
        self.config.unlink()
        copy.rename(self.config)
        parked = self.base / 'parked'
        self.root.rename(parked)
        self.root.symlink_to(parked, target_is_directory=True)
        with self.assertRaises(OSError):
            self.engine.allocate(self.request)
        self.root.unlink()
        parked.rename(self.root)
        self.target.symlink_to(self.mount, target_is_directory=True)
        with self.assertRaises(OSError):
            self.engine.allocate(self.request)
        self.assertTrue(self.target.is_symlink())
        self.assertFalse(self.pending.exists())

    def test_rejects_insufficient_capacity_and_concurrent_allocator(self):
        with patch.object(storage.os, 'fstatvfs', return_value=SimpleNamespace(f_flag=0, f_bavail=1, f_frsize=4096)):
            with self.assertRaisesRegex(files.Rejected, 'CAPACITY_REQUIRED'):
                self.engine.allocate(self.request)
        with self.engine.absolute(self.root) as blocker:
            fcntl.flock(blocker, fcntl.LOCK_EX)
            with self.assertRaisesRegex(files.Rejected, 'BUSY'):
                self.engine.allocate(self.request)
        self.assertEqual([], list(self.root.iterdir()))

    def test_rejects_unsafe_config_permissions_and_root_mode(self):
        self.config.chmod(0o644)
        with self.assertRaisesRegex(files.Rejected, 'METADATA_UNSAFE'):
            self.engine.allocate(self.request)
        self.config.chmod(0o600)
        self.root.chmod(0o770)
        with self.assertRaisesRegex(files.Rejected, 'DIRECTORY_UNSAFE'):
            self.engine.allocate(self.request)
        self.assertEqual([], list(self.root.iterdir()))

    def test_incomplete_pending_is_preserved_and_never_adopted(self):
        self.pending.mkdir(mode=0o700)
        with self.assertRaisesRegex(files.Rejected, 'STAGING_UNKNOWN'):
            self.engine.allocate(self.request)
        self.assertTrue(self.pending.is_dir())
        self.assertFalse(self.target.exists())

    def test_replays_sealed_pending_after_interruption_before_publish(self):
        with patch.object(files, 'exchange', side_effect=OSError(errno.EIO, 'injected')):
            with self.assertRaises(OSError):
                self.engine.allocate(self.request)
        self.assertFalse(self.target.exists())
        sealed = (self.pending / storage.MARKER).read_bytes()
        self.assertEqual(json.loads(sealed), self.engine.allocate(self.request))
        self.assertEqual(sealed, (self.target / storage.MARKER).read_bytes())
        self.assertFalse(self.pending.exists())

    def test_kernel_releases_allocator_lock_on_sigkill_after_staging(self):
        ready, written = os.pipe()
        stage = self.engine.stage
        pid = os.fork()
        if pid == 0:
            try:
                os.close(ready)

                def paused(*args):
                    stage(*args)
                    os.write(written, b'R')
                    signal.pause()
                    raise AssertionError('unexpected resume')

                with patch.object(self.engine, 'stage', side_effect=paused):
                    self.engine.allocate(self.request)
            finally:
                os._exit(2)

        os.close(written)
        try:
            self.assertTrue(select.select([ready], [], [], 5)[0])
            self.assertEqual(b'R', os.read(ready, 1))
            with self.assertRaisesRegex(files.Rejected, 'BUSY'):
                self.engine.allocate(self.request)
        finally:
            os.close(ready)
            os.kill(pid, signal.SIGKILL)
            _, status = os.waitpid(pid, 0)
        self.assertEqual(-signal.SIGKILL, os.waitstatus_to_exitcode(status))
        self.assertEqual(json.loads((self.pending / storage.MARKER).read_bytes()), self.engine.allocate(self.request))

    def test_replays_published_directory_after_interruption_at_rename(self):
        exchange = files.exchange

        def lost_reply(*args):
            exchange(*args)
            raise OSError(errno.EIO, 'injected after rename')

        with patch.object(files, 'exchange', side_effect=lost_reply):
            with self.assertRaises(OSError):
                self.engine.allocate(self.request)
        self.assertFalse(self.pending.exists())
        self.assertEqual(json.loads((self.target / storage.MARKER).read_bytes()), self.engine.allocate(self.request))

    def test_atomic_publish_never_replaces_a_late_conflicting_target(self):
        exchange = files.exchange

        def intruder(*args):
            self.target.mkdir(mode=0o700)
            (self.target / 'sentinel').write_text('foreign directory')
            exchange(*args)

        with patch.object(files, 'exchange', side_effect=intruder):
            with self.assertRaises(FileExistsError):
                self.engine.allocate(self.request)
        self.assertEqual('foreign directory', (self.target / 'sentinel').read_text())
        self.assertTrue((self.pending / storage.MARKER).is_file())

    def test_replaced_storage_root_cannot_receive_the_staged_publication(self):
        stage = self.engine.stage
        parked = self.base / 'parked'

        def replace(*args):
            proof = stage(*args)
            self.root.rename(parked)
            self.root.mkdir(mode=0o700)
            return proof

        with patch.object(self.engine, 'stage', side_effect=replace):
            with self.assertRaisesRegex(files.Rejected, 'ROOT_CHANGED'):
                self.engine.allocate(self.request)
        self.assertEqual([], list(self.root.iterdir()))
        self.assertTrue((parked / self.pending.name / storage.MARKER).is_file())

    def test_copied_marker_does_not_attest_a_replacement_inode(self):
        self.engine.allocate(self.request)
        parked = self.root / 'parked'
        self.target.rename(parked)
        self.target.mkdir(mode=0o700)
        marker = self.target / storage.MARKER
        marker.write_bytes((parked / storage.MARKER).read_bytes())
        marker.chmod(0o600)
        with self.assertRaisesRegex(files.Rejected, 'DIRECTORY_CONFLICT'):
            self.engine.allocate(self.request)
