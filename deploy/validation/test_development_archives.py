"""Real FD/codec/crash tests; quota and HDD attestation are explicit doubles.

The opt-in native proof separately exercises actual ext4 and physical HDD/NVMe.
No installed source, private configuration or production workspace is changed.
"""
import contextlib
import fcntl
import hashlib
import json
import os
from pathlib import Path
import signal
import stat
import struct
import tarfile
import tempfile
import unittest
import uuid
from unittest.mock import patch

import test_development_storage as fixture

archives = fixture.load('workspace_archive_test', 'whaledeck-development-archives.py')
lifecycle = fixture.load('workspace_archive_lifecycle_test', 'whaledeck-development-lifecycle.py')
tree = fixture.load('workspace_archive_tree_test', 'whaledeck-file-tree.py')
codec = fixture.load('workspace_archive_codec_test', 'whaledeck-tree-archive.py')


class TargetDouble:
    def __init__(self, directory):
        self.directory = directory
        self.full = False
        info = directory.stat()
        self.proof = dict(schemaVersion=1, rootInode=info.st_ino, device=str(info.st_dev),
            filesystemUuid='e9e5dd67-5a82-43f6-a051-f48badc7c441', mountId='unit-first-mount', declared='unit-test-double')

    @contextlib.contextmanager
    def locked(self, *, exclusive):
        with fixture.storage.WorkspaceStorage(fixture.files).absolute(self.directory) as fd:
            yield fd, dict(self.proof)

    def recheck(self, _, proof):
        assert proof == self.proof

    def capacity(self, _, expected):
        assert 0 < expected <= archives.MAX_ARCHIVE_BYTES
        if self.full:
            raise fixture.files.Rejected('WORKSPACE_STORAGE_ARCHIVE_CAPACITY_REQUIRED')


@unittest.skipUnless(os.geteuid() == 0, 'Requires root-owned private layout fixture')
class ArchiveDeclarationTests(unittest.TestCase):
    def setUp(self):
        directory = tempfile.TemporaryDirectory(); self.addCleanup(directory.cleanup)
        self.directory = Path(directory.name); self.layout = self.directory / 'layout.json'
        self.driver = fixture.storage.WorkspaceStorage(fixture.files)
        self.store = archives.HddArchiveStorage(self.driver)
        token = 'cbead2f5-0b02-448a-96bf-81a2649412e2'
        self.record = dict(schemaVersion=1, phase='ready', plan=dict(runtime={}, archive=dict(
            target='/data/DevSpace', sizeBytes=300 * 10**9, image='/data/.whaledeck-development-disks/archive.ext4',
            filesystemUuid=token, original=dict(device='8:1', inode=1, uid=0, gid=0, mode=0o700, mountId='1'))),
            images=dict(runtime={}, archive=dict(inode=1, uuid=token, phase='mounted')))
        patcher = patch.object(archives, 'LAYOUT', self.layout); patcher.start(); self.addCleanup(patcher.stop)

    def write(self, value=None):
        value = self.record if value is None else value
        value['planSha256'] = archives.digest(self.driver, value['plan'])
        self.layout.write_bytes(self.driver.canonical(value)); self.layout.chmod(0o600)

    def test_exact_bounded_ready_image_declaration_is_required(self):
        self.write(); self.assertEqual(300 * 10**9, self.store.declaration()['diskImage']['sizeBytes'])
        for key, value in [('sizeBytes', 300 * 10**9 + 512), ('sizeBytes', True), ('target', '/data/../DevSpace'),
                           ('target', '/data/.hidden/mount'), ('image', '/data/unrelated.ext4'), ('filesystemUuid', 'invalid')]:
            record = json.loads(json.dumps(self.record)); record['plan']['archive'][key] = value; self.write(record)
            with self.subTest(key=key, value=value), self.assertRaisesRegex(fixture.files.Rejected, 'ARCHIVE_LAYOUT_INVALID'):
                self.store.declaration()
        self.write(); record = json.loads(self.layout.read_bytes()); record['planSha256'] = 'f' * 64
        self.layout.write_bytes(self.driver.canonical(record))
        with self.assertRaisesRegex(fixture.files.Rejected, 'ARCHIVE_LAYOUT_INVALID'): self.store.declaration()

    def test_unpublished_layout_and_image_phase_are_refused(self):
        for path in ('layout', 'image'):
            record = json.loads(json.dumps(self.record))
            if path == 'layout': record['phase'] = 'planned'
            else: record['images']['archive']['phase'] = 'formatted'
            self.write(record)
            with self.subTest(path=path), self.assertRaisesRegex(fixture.files.Rejected, 'ARCHIVE_LAYOUT_INVALID'): self.store.declaration()

    def test_layout_metadata_symlinks_hardlinks_and_nonprivate_files_are_refused(self):
        self.write(); retained = self.directory / 'retained'; self.layout.rename(retained)
        self.layout.symlink_to(retained)
        with self.assertRaises((fixture.files.Rejected, OSError)): self.store.declaration()
        self.layout.unlink(); os.link(retained, self.layout)
        with self.assertRaises(fixture.files.Rejected): self.store.declaration()
        self.layout.unlink(); retained.rename(self.layout); self.layout.chmod(0o644)
        with self.assertRaises(fixture.files.Rejected): self.store.declaration()


@unittest.skipUnless(os.geteuid() == 0, 'Requires disposable private root fixtures')
class ArchiveTests(unittest.TestCase):
    write_config = fixture.DevelopmentStorageTests.write_config
    write_mountinfo = fixture.DevelopmentStorageTests.write_mountinfo
    enable_quota = fixture.DevelopmentStorageTests.enable_quota

    def setUp(self):
        fixture.DevelopmentStorageTests.setUp(self)
        self.enable_quota()
        self.engine.allocate(self.request)
        self.payload = self.target / 'data'
        (self.payload / '代码').mkdir(mode=0o750)
        self.file = self.payload / '代码/二进制.bin'
        self.file.write_bytes(bytes(range(256)) * 257)
        self.file.chmod(0o751)
        os.chown(self.file, 65534, 65534)
        os.utime(self.file, ns=(1712345678123456789, 1712345678123456789))
        (self.payload / '.env').write_bytes(b'private-fixture-value')
        (self.payload / 'empty').mkdir(mode=0o710)
        self.control = lifecycle.Lifecycle(self.engine)
        self.stop()
        target = tempfile.TemporaryDirectory(dir='/dev/shm')
        self.addCleanup(target.cleanup)
        self.hdd = Path(target.name)
        self.store = TargetDouble(self.hdd)
        self.capture = archives.DevelopmentArchives(self.engine, tree.FileTreeManifest, codec.TreeArchive, self.store)

    def stop(self):
        command = dict(storage=self.request, action='stop', expectedEpoch=1, operationId='', planSha256='', storageProofSha256='')
        with patch.object(lifecycle, 'GIT_CGROUP', self.mount / 'no-git-cgroup'):
            planned = self.control.run(command)
            self.control.run(dict(command, operationId=str(uuid.uuid4()),
                planSha256=planned['planSha256'], storageProofSha256=planned['storageProofSha256']), apply=True)

    def command(self):
        plan = self.capture.plan(self.request, 2)
        return dict(storage=self.request, expectedEpoch=2, operationId=str(uuid.uuid4()), planSha256=plan['planSha256'])

    def entry(self, command):
        return self.hdd / archives.NAMESPACE / command['operationId']

    def snapshot(self):
        return {str(p.relative_to(self.payload)): (fixture.files.version(p.stat()),
            hashlib.sha256(p.read_bytes()).hexdigest() if p.is_file() else None)
            for p in [self.payload, *self.payload.rglob('*')]}

    def test_real_codec_captures_private_unicode_binary_and_metadata_without_source_changes(self):
        command = self.command(); before = self.snapshot(); quotas = dict(self.quota.projects)
        result = self.capture.capture(command)
        self.assertEqual(result, self.capture.verify(command, result['receiptSha256']))
        self.assertEqual(result, self.capture.capture(command))
        self.assertEqual(before, self.snapshot()); self.assertEqual(quotas, self.quota.projects)
        self.assertEqual(5, result['entryCount'])
        archive = self.entry(command) / 'payload.tar.gz'
        self.assertEqual((0, 0o600, 1), (archive.stat().st_uid, stat.S_IMODE(archive.stat().st_mode), archive.stat().st_nlink))
        with tarfile.open(archive) as stream:
            self.assertEqual(self.file.read_bytes(), stream.extractfile('代码/二进制.bin').read())
            self.assertEqual(b'private-fixture-value', stream.extractfile('.env').read())
            self.assertEqual((65534, 65534, 0o751), (stream.getmember('代码/二进制.bin').uid, stream.getmember('代码/二进制.bin').gid, stream.getmember('代码/二进制.bin').mode))
        self.assertNotIn('private-fixture-value', json.dumps(result))
        self.assertNotIn('二进制', json.dumps(result, ensure_ascii=False))

    def test_late_content_change_invalidates_plan_without_hdd_bytes(self):
        command = self.command(); self.file.write_bytes(b'late edit')
        with self.assertRaisesRegex(fixture.files.Rejected, 'ARCHIVE_PLAN_CHANGED'):
            self.capture.capture(command)
        self.assertEqual([], list(self.hdd.iterdir()))
        self.assertEqual(b'late edit', self.file.read_bytes())

    def test_live_attested_reader_blocks_capture_before_hdd_mutation(self):
        command = self.command()
        with self.engine.attested_payload(self.request):
            with self.assertRaisesRegex(fixture.files.Rejected, 'STORAGE_BUSY'):
                self.capture.capture(command)
        self.assertEqual([], list(self.hdd.iterdir()))

    def test_active_epoch_and_invalid_commands_are_refused_without_publication(self):
        command = self.command()
        for change in ({'expectedEpoch': True}, {'expectedEpoch': 1}, {'expectedEpoch': 4},
                       {'operationId': '../other'}, {'operationId': archives.ZERO},
                       {'planSha256': 'x' * 64}, {'unknown': True}):
            with self.subTest(change=change), self.assertRaises(fixture.files.Rejected):
                self.capture.capture(dict(command, **change))
        marker = self.root / (fixture.storage.LIFECYCLE + self.request['workspaceId'] + '.json')
        marker.unlink()
        with self.assertRaisesRegex(fixture.files.Rejected, 'ARCHIVE_NOT_STOPPED'):
            self.capture.capture(command)
        self.assertEqual([], list(self.hdd.iterdir()))

    def test_partial_archive_is_retained_without_rewrite_or_source_deletion(self):
        command = self.command(); entry = self.entry(command)
        entry.mkdir(mode=0o700, parents=True); entry.parent.chmod(0o700)
        partial = entry / 'archive.partial'; partial.write_bytes(b'incomplete retained stream'); partial.chmod(0o600)
        inode = partial.stat().st_ino; before = self.snapshot()
        with self.assertRaisesRegex(fixture.files.Rejected, 'ARCHIVE_INCOMPLETE'):
            self.capture.capture(command)
        self.assertEqual((inode, b'incomplete retained stream'), (partial.stat().st_ino, partial.read_bytes()))
        self.assertFalse((entry / 'receipt.json').exists()); self.assertEqual(before, self.snapshot())

    def test_real_process_death_on_both_sides_of_archive_rename_recovers_original_bytes(self):
        original = fixture.files.exchange
        for after in (False, True):
            command = self.command(); before = self.snapshot()
            def interrupted(source, name, target, leaf, flags):
                selected = name == 'archive.partial' and leaf == 'payload.tar.gz'
                if selected and not after: os.kill(os.getpid(), signal.SIGSTOP)
                result = original(source, name, target, leaf, flags)
                if selected and after: os.kill(os.getpid(), signal.SIGSTOP)
                return result
            child = os.fork()
            if child == 0:
                try:
                    with patch.object(fixture.files, 'exchange', interrupted): self.capture.capture(command)
                finally: os._exit(3)
            _, status = os.waitpid(child, os.WUNTRACED)
            self.assertTrue(os.WIFSTOPPED(status))
            os.kill(child, signal.SIGKILL); os.waitpid(child, 0)
            old = self.entry(command) / ('payload.tar.gz' if after else 'archive.partial')
            identity, content = old.stat().st_ino, hashlib.sha256(old.read_bytes()).hexdigest()
            result = self.capture.capture(command); archive = self.entry(command) / 'payload.tar.gz'
            self.assertEqual((identity, content), (archive.stat().st_ino, hashlib.sha256(archive.read_bytes()).hexdigest()))
            self.assertEqual(result, self.capture.verify(command, result['receiptSha256']))
            self.assertEqual(before, self.snapshot())

    def test_metadata_prefix_recovers_and_conflicting_staging_is_preserved(self):
        command = self.command(); result = self.capture.capture(command)
        receipt = self.entry(command) / 'receipt.json'; raw = receipt.read_bytes(); receipt.unlink()
        pending = self.entry(command) / '.receipt.json.pending'
        pending.write_bytes(raw[:11]); pending.chmod(0o600)
        self.assertEqual(result, self.capture.capture(command)); self.assertFalse(pending.exists())
        receipt.unlink(); pending.write_bytes(b'foreign prefix'); pending.chmod(0o600)
        with self.assertRaisesRegex(fixture.files.Rejected, 'ARCHIVE_METADATA_CONFLICT'):
            self.capture.capture(command)
        self.assertEqual(b'foreign prefix', pending.read_bytes()); self.assertTrue(self.file.exists())

    def test_independent_hdd_verification_does_not_need_the_source_or_free_copy_capacity(self):
        command = self.command(); result = self.capture.capture(command)
        self.store.full = True
        self.assertEqual(result, self.capture.capture(command))
        self.target.rename(self.root / 'retained-in-unit-fixture')
        with patch.object(self.engine, 'locked_root', side_effect=AssertionError('must not open source')):
            self.assertEqual(result, self.capture.verify(command, result['receiptSha256']))

    def test_corrupt_archive_or_wrong_receipt_digest_is_rejected(self):
        command = self.command(); result = self.capture.capture(command)
        with self.assertRaisesRegex(fixture.files.Rejected, 'ARCHIVE_RECEIPT_CHANGED'):
            self.capture.verify(command, 'f' * 64)
        archive = self.entry(command) / 'payload.tar.gz'
        body = archive.read_bytes(); archive.write_bytes(body[:-10])
        with self.assertRaises(fixture.files.Rejected): self.capture.verify(command, result['receiptSha256'])
        self.assertTrue(self.file.exists())

    def test_durable_binding_does_not_pin_transient_mount_id_or_loop_number(self):
        command = self.command(); result = self.capture.capture(command)
        self.store.proof.update(mountId='unit-remounted', device='unit-new-loop')
        self.assertEqual(result, self.capture.verify(command, result['receiptSha256']))
        self.assertEqual(result, self.capture.capture(command))

    def test_symlink_hardlink_and_nonprivate_archive_are_refused_without_external_changes(self):
        command = self.command(); result = self.capture.capture(command)
        archive = self.entry(command) / 'payload.tar.gz'; retained = archive.with_name('retained')
        archive.rename(retained); external = self.mount / 'external'; external.write_bytes(b'outside')
        archive.symlink_to(external)
        with self.assertRaises((fixture.files.Rejected, OSError)): self.capture.verify(command, result['receiptSha256'])
        self.assertEqual(b'outside', external.read_bytes()); archive.unlink(); os.link(retained, archive)
        with self.assertRaises(fixture.files.Rejected): self.capture.verify(command, result['receiptSha256'])
        archive.unlink(); retained.rename(archive); archive.chmod(0o644)
        with self.assertRaises(fixture.files.Rejected): self.capture.verify(command, result['receiptSha256'])

    def test_extended_metadata_and_special_objects_are_not_silently_lost(self):
        for special in ('xattr', 'setgid', 'symlink', 'inode-flag'):
            with self.subTest(special=special):
                if special == 'xattr': os.setxattr(self.file, 'user.qa-fixture', b'value')
                elif special == 'setgid': self.file.chmod(0o2751)
                elif special == 'symlink': (self.payload / 'link').symlink_to(self.file)
                else:
                    with self.file.open('rb') as stream:
                        original = fcntl.ioctl(stream.fileno(), archives.GET_FLAGS, bytes(8))
                        fcntl.ioctl(stream.fileno(), 0x40086602, struct.pack('=Q', struct.unpack('=I', original[:4])[0] | 0x40))
                with self.assertRaises(fixture.files.Rejected): self.capture.plan(self.request, 2)
                if special == 'xattr': os.removexattr(self.file, 'user.qa-fixture')
                elif special == 'setgid': self.file.chmod(0o751)
                elif special == 'symlink': (self.payload / 'link').unlink()
                else:
                    with self.file.open('rb') as stream: fcntl.ioctl(stream.fileno(), 0x40086602, original)
        self.assertEqual([], list(self.hdd.iterdir()))

    def test_full_target_fails_before_archive_creation_and_allows_same_operation_retry(self):
        command = self.command(); self.store.full = True
        with self.assertRaisesRegex(fixture.files.Rejected, 'ARCHIVE_CAPACITY_REQUIRED'):
            self.capture.capture(command)
        self.assertFalse((self.entry(command) / 'archive.partial').exists()); self.assertTrue(self.file.exists())
        self.store.full = False
        result = self.capture.capture(command)
        self.assertEqual(result, self.capture.verify(command, result['receiptSha256']))

    def test_history_bound_allows_original_replay_but_not_a_new_capture(self):
        command = self.command(); result = self.capture.capture(command)
        with patch.object(archives, 'MAX_CAPTURES', 1):
            self.assertEqual(result, self.capture.capture(command))
            with self.assertRaisesRegex(fixture.files.Rejected, 'ARCHIVE_HISTORY_LIMIT'):
                self.capture.capture(dict(command, operationId=str(uuid.uuid4())))

    def test_changed_hdd_proof_or_quota_claim_blocks_capture(self):
        command = self.command(); self.store.proof['declared'] = 'changed'
        with self.assertRaisesRegex(fixture.files.Rejected, 'ARCHIVE_PLAN_CHANGED'): self.capture.capture(command)
        self.store.proof['declared'] = 'unit-test-double'
        project = next(iter(self.quota.projects)); self.quota.projects[project] = (1, 1)
        with self.assertRaises(fixture.quota_module.QuotaError): self.capture.capture(command)
        self.assertEqual([], list(self.hdd.iterdir()))


if __name__ == '__main__':
    unittest.main()
