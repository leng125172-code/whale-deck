"""Image provenance policy tests; real loop/quota acceptance is separate."""
import os
from pathlib import Path
import unittest
from unittest.mock import patch

import test_development_storage as fixture

storage = fixture.storage


@unittest.skipUnless(os.geteuid() == 0, 'Needs an isolated root user namespace')
class DevelopmentDiskImageTests(unittest.TestCase):
    def setUp(self):
        self.base = fixture.DevelopmentStorageTests('test_publish_and_replay_match_exact_receipt_and_inode')
        self.addCleanup(self.base.doCleanups)
        self.base.setUp()
        self.engine = self.base.engine
        self.images = self.base.mount / 'images'
        self.images.mkdir(mode=0o700)
        patch.object(storage, 'IMAGE_ROOT', self.images).start()
        self.image = self.images / 'qa.ext4'
        fd = os.open(self.image, os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o600)
        try:
            os.posix_fallocate(fd, 0, 1024**3)
        finally:
            os.close(fd)
        s = self.image.stat()
        self.pin = dict(path=str(self.image), device=self.base.dev, inode=s.st_ino,
                        sizeBytes=s.st_size, filesystemUuid=self.base.uuid)
        self.device = self.base.sys_devices / 'virtual/block/loop12345'
        (self.device / 'loop').mkdir(parents=True)
        self.block_class = self.base.mount / 'class-block'
        self.block_class.mkdir()
        (self.device / 'subsystem').symlink_to(self.block_class)
        patch.object(storage, 'BLOCK_CLASS', self.block_class).start()
        self.dev = '7:12345'
        (self.base.sys_dev / self.dev).symlink_to(self.device)
        values = {'loop/backing_file': str(self.image), 'loop/offset': '0', 'loop/sizelimit': '0',
                  'loop/partscan': '0', 'ro': '0', 'size': str(s.st_size // 512), 'dev': self.dev}
        for name, value in values.items():
            (self.device / name).write_text(value + '\n')

    def check(self):
        self.engine.require_nvme_image(self.dev, self.pin)

    def test_explicit_image_mode_preserves_direct_nvme_attestation(self):
        self.check()
        self.engine.require_nvme(self.base.dev)
        with self.assertRaisesRegex(fixture.files.Rejected, 'NVME_REQUIRED'):
            self.engine.require_nvme(self.dev)
        self.base.enable_quota()
        self.base.root_config['diskImage'] = self.pin
        self.base.write_config(quota=True)
        self.assertEqual(self.pin, self.base.engine.configuration(self.base.root_id)['diskImage'])

    def test_changed_loop_mapping_is_never_accepted(self):
        for name, wrong in [('loop/backing_file', str(self.images / 'other.ext4')), ('loop/offset', '512'),
                            ('loop/sizelimit', '512'), ('loop/partscan', '1'), ('ro', '1'),
                            ('size', str(1024**3 // 512 + 1)), ('dev', '7:12346')]:
            with self.subTest(name=name):
                p = self.device / name
                original = p.read_bytes()
                p.write_text(wrong + '\n')
                with self.assertRaisesRegex(fixture.files.Rejected, 'IMAGE_MISMATCH'):
                    self.check()
                p.write_bytes(original)

    def test_copied_inode_sparse_file_or_shared_link_is_rejected(self):
        for key, wrong in [('inode', self.pin['inode'] + 1), ('device', '8:1'),
                           ('sizeBytes', self.pin['sizeBytes'] + 512),
                           ('filesystemUuid', '00000000-0000-0000-0000-000000000000')]:
            with self.subTest(key=key), self.assertRaisesRegex(fixture.files.Rejected, 'IMAGE_MISMATCH'):
                self.engine.require_nvme_image(self.dev, self.pin | {key: wrong})
        self.image.chmod(0o640)
        with self.assertRaisesRegex(fixture.files.Rejected, 'IMAGE_MISMATCH'):
            self.check()
        self.image.chmod(0o600)
        alias = self.images / 'alias.ext4'
        os.link(self.image, alias)
        with self.assertRaisesRegex(fixture.files.Rejected, 'IMAGE_MISMATCH'):
            self.check()
        alias.unlink()
        with self.image.open('wb') as stream:
            stream.truncate(self.pin['sizeBytes'])
        with self.assertRaisesRegex(fixture.files.Rejected, 'IMAGE_MISMATCH'):
            self.check()

    def test_physical_rotating_device_and_late_backing_replacement_are_rejected(self):
        rotational = self.base.namespace / 'queue/rotational'
        rotational.write_text('1\n')
        with self.assertRaisesRegex(fixture.files.Rejected, 'NVME_REQUIRED'):
            self.check()
        rotational.write_text('0\n')
        def replace(_):
            self.image.rename(self.images / 'old.ext4')
            self.image.touch(mode=0o600)
        with patch.object(self.engine, 'require_nvme', side_effect=replace):
            with self.assertRaisesRegex(fixture.files.Rejected, 'IMAGE_MISMATCH'):
                self.check()

    def test_image_config_is_bounded_private_and_explicit(self):
        for value in [self.pin | {'path': '/tmp/qa.ext4'}, self.pin | {'path': str(self.images) + '/./qa.ext4'},
                      self.pin | {'sizeBytes': True}, self.pin | {'sizeBytes': 1024**4 + 512},
                      self.pin | {'inode': False}, self.pin | {'extra': 1}]:
            with self.subTest(value=value), self.assertRaisesRegex(fixture.files.Rejected, 'CONFIGURATION_INVALID'):
                self.engine.validate_image(value)
        self.images.chmod(0o755)
        with self.assertRaisesRegex(fixture.files.Rejected, 'DIRECTORY_UNSAFE'):
            self.check()


if __name__ == '__main__':
    unittest.main()
