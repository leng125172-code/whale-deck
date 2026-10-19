"""Actual disposable Linux unpack/publication tests, not RPC/BFF/UI evidence."""
import gzip
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import signal
import shutil
import stat
import sys
import tarfile
import unittest
from unittest.mock import patch
import uuid
import test_file_transport as common
from test_file_tree_manifest import tree_module
from test_tree_archive_codec import codec

spec = importlib.util.spec_from_file_location('tree_unpacking', Path(__file__).resolve().parents[1] / 'host/whaledeck-tree-unpacking.py')
unpacking = importlib.util.module_from_spec(spec); spec.loader.exec_module(unpacking)
driver = common.driver


@unittest.skipUnless(os.name == 'posix' and os.geteuid() == 0, 'requires disposable real Linux root I/O')
class TreeUnpackingTests(unittest.TestCase):
    def setUp(self):
        self.fixture = common.FileTransportTests(); self.fixture.setUp()
        self.root, self.registry, self.state = self.fixture.root, self.fixture.registry, self.fixture.state
        policy = json.loads(self.registry.read_bytes())
        policy[0]['allowedActions'] += ['files-mutate', 'files-archive', 'files-tree-unpack']
        self.registry.write_text(json.dumps(policy))
        self.engine = unpacking.FileTreeUnpacking(driver, tree_module.FileTreeManifest, codec.TreeArchive)
        self.data = bytes(range(256)) * 8192 + b'\x00tail\xff\n'
        self.input = self.root / 'source.archive'
        self.make_archive()

    def tearDown(self): self.fixture.tearDown()

    def make_archive(self, archive_format='tar', members=None, standard=tarfile.PAX_FORMAT):
        if members is None:
            members = []
            for name, mode in (('.', 0o750), ('空目录', 0o710), ('代码', 0o750)):
                info = tarfile.TarInfo(name); info.type = tarfile.DIRTYPE; info.mode = mode
                members.append((info, None))
            info = tarfile.TarInfo('代码/中文.bin'); info.size = len(self.data); info.mode = 0o6751
            members.append((info, self.data))
        output = io.BytesIO()
        with tarfile.open(fileobj=output, mode='w', format=standard) as writer:
            for info, content in members:
                info.uid, info.gid = 12345, 23456
                info.pax_headers['mtime'] = '1712345678.123456789'
                writer.addfile(info, io.BytesIO(content) if content is not None else None)
        data = output.getvalue()
        if archive_format == 'tar.gz': data = gzip.compress(data, mtime=0)
        self.input.write_bytes(data); self.input.chmod(0o640)

    def command(self, archive_format='tar', target='target'):
        return {'source': {'rootId': 'test.shared', 'path': self.input.name, 'expectedVersion': driver.version(self.input.stat())},
            'target': {'rootId': 'test.shared', 'path': target}, 'format': archive_format}

    def request(self, archive_format='tar', target='target'):
        return {'operationId': uuid.uuid4().hex, 'owner': 'A' * 64, 'plan': self.engine.plan(self.command(archive_format, target))}

    def lookup(self, request): return self.engine.lookup({key: request[key] for key in ('operationId', 'owner')})
    def pending(self, request): return self.root / driver.STAGING / (request['operationId'] + '.tree-unpack')
    def receipt(self, request): return self.state / 'tree-unpacking' / (request['operationId'] + '.json')
    def source_identity(self): return driver.publication_identity(self.input.stat()) + [self.input.stat().st_ctime_ns]

    def test_both_formats_publish_complete_bytes_empty_directories_modes_and_ns_without_mutating_archive(self):
        for archive_format in ('tar', 'tar.gz'):
            self.make_archive(archive_format)
            before, digest = self.source_identity(), hashlib.sha256(self.input.read_bytes()).hexdigest()
            request = self.request(archive_format, 'result-' + archive_format)
            self.assertEqual(request['plan']['totalBytes'], len(self.data)); self.assertEqual(request['plan']['entryCount'], 4)
            self.assertNotIn('entries', request['plan'])
            result = self.engine.unpack(request); target = self.root / ('result-' + archive_format)
            self.assertEqual(result['state'], 'Completed'); self.assertFalse(result['partialRetained'])
            self.assertEqual((target / '代码/中文.bin').read_bytes(), self.data)
            self.assertTrue((target / '空目录').is_dir()); self.assertFalse(list((target / '空目录').iterdir()))
            value = (target / '代码/中文.bin').stat()
            self.assertEqual((stat.S_IMODE(value.st_mode), value.st_uid, value.st_gid, value.st_mtime_ns), (0o751, 0, 0, 1712345678123456789))
            self.assertEqual(stat.S_IMODE(target.stat().st_mode), 0o750)
            self.assertEqual(self.source_identity(), before); self.assertEqual(hashlib.sha256(self.input.read_bytes()).hexdigest(), digest)
            self.assertFalse(self.pending(request).exists())
            with patch.object(codec.TreeArchive, 'extract', side_effect=AssertionError('must not re-extract')):
                self.assertEqual(self.lookup(request), result); self.assertEqual(self.engine.unpack(request), result)

    def test_target_ownership_is_only_from_registry_never_archive_uid_gid(self):
        policy = json.loads(self.registry.read_bytes()); policy[0].update(ownerUid=1000, ownerGid=1001); self.registry.write_text(json.dumps(policy))
        request = self.request(); self.engine.unpack(request)
        for target in (self.root / 'target', self.root / 'target/空目录', self.root / 'target/代码/中文.bin'):
            self.assertEqual((target.stat().st_uid, target.stat().st_gid), (1000, 1001))

    def test_fixed_loader_rejects_untrusted_owner_mode_links_and_symlinks(self):
        modules = self.fixture.base / 'installed-unpacking'; modules.mkdir(mode=0o700)
        names = ('whaledeck-file-tree.py', 'whaledeck-tree-archive.py', 'whaledeck-tree-unpacking.py')
        for name in names:
            target = modules / name; shutil.copyfile(Path(driver.__file__).with_name(name), target); target.chmod(0o640)
        with patch.dict(sys.modules, {driver.__name__: driver}), patch.object(driver, '__file__', str(modules / 'whaledeck-files.py')):
            loaded = driver.tree_unpacking_engine(); request = self.request()
            self.assertEqual(loaded.plan(request['plan']['command']), request['plan'])
            self.assertEqual(loaded.unpack(request)['state'], 'Completed')
            target = modules / names[-1]; target.chmod(0o660)
            with self.assertRaisesRegex(driver.Rejected, 'FILES_MODULE_UNSAFE'): driver.tree_unpacking_engine()
            target.chmod(0o640); linked = modules / 'linked'; os.link(target, linked)
            with self.assertRaisesRegex(driver.Rejected, 'FILES_MODULE_UNSAFE'): driver.tree_unpacking_engine()
            linked.unlink(); os.chown(target, 12345, 0)
            with self.assertRaisesRegex(driver.Rejected, 'FILES_MODULE_UNSAFE'): driver.tree_unpacking_engine()
            os.chown(target, 0, 0); original = modules / 'original.py'; target.rename(original); target.symlink_to(original)
            with self.assertRaisesRegex(driver.Rejected, 'FILES_MODULE_UNSAFE'): driver.tree_unpacking_engine()

    def test_external_gnu_long_utf8_names_publish_complete_bytes_in_both_formats(self):
        name = '代码/' + '中文' * 35 + '.bin'
        for archive_format in ('tar', 'tar.gz'):
            info = tarfile.TarInfo(name); info.size = len(self.data); info.mode = 0o640
            self.make_archive(archive_format, [(info, self.data)], tarfile.GNU_FORMAT)
            request = self.request(archive_format, 'gnu-' + archive_format)
            self.assertEqual(self.engine.unpack(request)['state'], 'Completed')
            self.assertEqual((self.root / ('gnu-' + archive_format) / name).read_bytes(), self.data)

    def test_archive_root_may_be_implicit_and_noncanonical_source_suffix_is_not_a_format_grant(self):
        info = tarfile.TarInfo('folder/file'); info.size = 3; info.mode = 0o644
        self.make_archive(members=[(info, b'abc')]); request = self.request(); self.engine.unpack(request)
        self.assertEqual((self.root / 'target/folder/file').read_bytes(), b'abc')
        self.assertEqual(stat.S_IMODE((self.root / 'target').stat().st_mode), 0o700)
        with self.assertRaises(driver.Rejected): self.engine.plan(self.command('tar.gz', 'wrong-format'))

    def test_independent_source_and_target_grants_are_required(self):
        original = self.registry.read_bytes()
        for action in ('files-list', 'files-download', 'files-upload', 'files-mutate', 'files-archive', 'files-tree-unpack'):
            policy = json.loads(original); policy[0]['allowedActions'].remove(action)
            policy[0]['allowedActions'] += ['files-tree-copy', 'files-tree-pack']
            self.registry.write_text(json.dumps(policy))
            with self.subTest(action=action), self.assertRaises(driver.Rejected): self.request()
            self.assertFalse((self.root / 'target').exists())
        self.registry.write_bytes(original)

    def test_existing_file_directory_symlink_and_late_target_never_overwritten(self):
        target = self.root / 'target'
        for kind in ('file', 'directory', 'symlink'):
            if kind == 'file': target.write_bytes(b'foreign')
            elif kind == 'directory': target.mkdir()
            else: target.symlink_to(self.fixture.base / 'outside')
            with self.subTest(kind=kind), self.assertRaises(driver.Rejected): self.request()
            if kind == 'directory': target.rmdir()
            else: target.unlink()
        request = self.request()
        self.engine.checkpoint = lambda phase: target.write_bytes(b'foreign') if phase == 'Publishing' else None
        with self.assertRaises(OSError): self.engine.unpack(request)
        self.assertEqual(target.read_bytes(), b'foreign'); self.assertEqual(self.lookup(request)['state'], 'Unknown')
        self.assertTrue(self.pending(request).exists())

    def test_protected_traversal_special_links_duplicates_and_conflicting_members_never_stage(self):
        cases = []
        for name in ('.env', 'nested/.env', '../outside', '/absolute', 'a/../../outside'):
            info = tarfile.TarInfo(name); info.size = 1; cases.append([(info, b'x')])
        for kind in (tarfile.SYMTYPE, tarfile.LNKTYPE, tarfile.FIFOTYPE, tarfile.CHRTYPE):
            info = tarfile.TarInfo('special'); info.type = kind; info.linkname = 'outside'; cases.append([(info, None)])
        first = tarfile.TarInfo('same'); first.size = 1
        second = tarfile.TarInfo('same'); second.size = 1
        cases.append([(first, b'x'), (second, b'y')])
        first = tarfile.TarInfo('a'); first.size = 1
        second = tarfile.TarInfo('a/b'); second.size = 1
        cases.append([(first, b'x'), (second, b'y')])
        for members in cases:
            self.make_archive(members=members)
            with self.subTest(names=[info.name for info, _ in members]), self.assertRaises(driver.Rejected): self.request()
            self.assertFalse((self.root / driver.STAGING).exists()); self.assertFalse((self.root / 'target').exists())
        self.assertFalse((self.fixture.base / 'outside').exists())

    def test_wrong_source_version_hardlink_symlink_directory_and_changed_plan_are_rejected(self):
        command = self.command(); command['source']['expectedVersion'] = 'F' * 64
        with self.assertRaises(driver.Rejected): self.engine.plan(command)
        linked = self.root / 'other'; os.link(self.input, linked)
        with self.assertRaises(driver.Rejected): self.request()
        linked.unlink(); self.input.rename(self.root / 'saved'); self.input.symlink_to(self.root / 'saved')
        with self.assertRaises((driver.Rejected, OSError)): self.request()
        self.input.unlink(); self.input.mkdir()
        with self.assertRaises(driver.Rejected): self.request()

    def test_wrong_owner_plan_namespace_and_corrupt_receipts_are_not_adopted(self):
        request = self.request(); result = self.engine.unpack(request)
        with self.assertRaises(driver.Rejected): self.engine.lookup({'operationId': request['operationId'], 'owner': 'B' * 64})
        other = self.request(target='other'); other['operationId'] = request['operationId']
        with self.assertRaisesRegex(driver.Rejected, 'FILES_IDEMPOTENCY_CONFLICT'): self.engine.unpack(other)
        path = self.receipt(request); receipt = json.loads(path.read_bytes()); receipt['plan']['fingerprint'] = 'F' * 64
        path.write_text(json.dumps(receipt))
        with self.assertRaises(driver.Rejected): self.lookup(request)
        self.assertEqual(result['state'], 'Completed')

    def test_archive_change_before_execution_has_no_receipt_or_output(self):
        request = self.request(); self.input.write_bytes(b'changed')
        with self.assertRaises(driver.Rejected): self.engine.unpack(request)
        self.assertFalse(self.receipt(request).exists()); self.assertFalse(self.pending(request).exists())

    def test_late_archive_change_policy_revocation_and_parent_replacement_never_publish(self):
        for kind in ('source', 'policy', 'parent'):
            with self.subTest(kind=kind):
                original_policy = self.registry.read_bytes()
                self.make_archive(); parent = self.root / ('parent-' + kind); parent.mkdir()
                request = self.request(target=parent.name + '/target')
                def checkpoint(phase):
                    if phase != 'Publishing': return
                    if kind == 'source': self.input.write_bytes(b'late edit')
                    elif kind == 'policy':
                        policy = json.loads(self.registry.read_bytes()); policy[0]['ownerUid'] = 1001; self.registry.write_text(json.dumps(policy))
                    else: parent.rename(self.root / 'old-parent'); parent.mkdir()
                self.engine.checkpoint = checkpoint
                with self.assertRaises(driver.Rejected): self.engine.unpack(request)
                self.assertFalse((parent / 'target').exists()); self.assertTrue(self.receipt(request).exists())
                self.registry.write_bytes(original_policy); self.engine.checkpoint = lambda phase: None

    def test_cancel_before_execution_and_after_ready_never_publishes_or_restarts(self):
        request = self.request(); self.engine.cancelled = lambda: True
        with self.assertRaises(driver.Rejected): self.engine.unpack(request)
        self.assertFalse(self.pending(request).exists())
        self.engine.cancelled = None; request = self.request()
        self.engine.checkpoint = lambda phase: setattr(self.engine, 'cancelled', lambda: True) if phase == 'Ready' else None
        with self.assertRaises(driver.Rejected): self.engine.unpack(request)
        self.assertEqual(self.lookup(request)['state'], 'Aborted'); self.assertTrue(self.pending(request).exists())
        self.assertFalse((self.root / 'target').exists())
        self.engine.cancelled = None
        with patch.object(codec.TreeArchive, 'extract', side_effect=AssertionError('no retry')):
            self.assertEqual(self.engine.unpack(request)['state'], 'Aborted')

    def test_discard_before_start_is_a_durable_fence_even_after_revocation(self):
        request = self.request(); self.registry.write_text('[]')
        result = self.engine.discard(request); self.assertEqual(result['state'], 'Aborted')
        with patch.object(codec.TreeArchive, 'extract', side_effect=AssertionError('no retry')):
            self.assertEqual(self.engine.unpack(request), result)
        self.assertFalse(self.pending(request).exists())

    def test_foreign_private_object_or_replaced_staging_name_remains_unknown(self):
        request = self.request(); parent = os.open(self.root, os.O_RDONLY | os.O_DIRECTORY)
        try: private = driver.staging(parent)
        finally: os.close(parent)
        os.close(private); self.pending(request).mkdir()
        (self.pending(request) / 'foreign').write_bytes(b'keep')
        with self.assertRaises(FileExistsError): self.engine.unpack(request)
        self.assertEqual(self.lookup(request)['state'], 'Unknown')
        self.assertEqual((self.pending(request) / 'foreign').read_bytes(), b'keep')

    def test_mutated_complete_staged_tree_is_detected_before_publication(self):
        request = self.request()
        def checkpoint(phase):
            if phase == 'Ready': (self.pending(request) / '代码/中文.bin').write_bytes(b'tampered')
        self.engine.checkpoint = checkpoint
        with self.assertRaises(driver.Rejected): self.engine.unpack(request)
        self.assertFalse((self.root / 'target').exists()); self.assertEqual(self.lookup(request)['state'], 'Aborted')

    def test_partial_write_failure_is_retained_and_never_extracted_again(self):
        request = self.request(); before = self.source_identity()
        def broken(_self, _source, target, *_args, **_kwargs):
            output = os.open('partial', os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600, dir_fd=target)
            try: os.write(output, b'partial'); os.fsync(output)
            finally: os.close(output)
            raise OSError('test-only partial failure')
        with patch.object(codec.TreeArchive, 'extract', broken), self.assertRaises(OSError): self.engine.unpack(request)
        self.assertEqual(self.lookup(request)['state'], 'Aborted'); self.assertEqual(self.source_identity(), before)
        with patch.object(codec.TreeArchive, 'extract', side_effect=AssertionError('no retry')):
            self.assertEqual(self.engine.unpack(request)['state'], 'Aborted')
        self.assertEqual((self.pending(request) / 'partial').read_bytes(), b'partial')

    def test_real_sigkill_at_all_seven_fences_only_observes_never_promotes_or_reextracts(self):
        before = self.source_identity()
        for phase, expected in (('Preparing', 'Aborted'), ('StagingIntent', 'Unknown'), ('Created', 'Unknown'), ('Staging', 'Aborted'), ('Ready', 'Aborted'), ('Publishing', 'Aborted'), ('Published', 'Completed')):
            with self.subTest(phase=phase):
                request = self.request(target=phase); child = os.fork()
                if child == 0:
                    self.engine.checkpoint = lambda actual: os.kill(os.getpid(), signal.SIGKILL) if actual == phase else None
                    try: self.engine.unpack(request)
                    except BaseException: os._exit(97)
                    os._exit(98)
                _, status = os.waitpid(child, 0)
                self.assertTrue(os.WIFSIGNALED(status)); self.assertEqual(os.WTERMSIG(status), signal.SIGKILL)
                with patch.object(codec.TreeArchive, 'extract', side_effect=AssertionError('never re-extract')):
                    result = self.lookup(request); self.assertEqual(result['state'], expected); self.assertEqual(self.engine.unpack(request), result)
                self.assertEqual((self.root / phase).exists(), expected == 'Completed')
                self.assertEqual(self.source_identity(), before)

    def test_published_recovery_can_verify_output_without_reopening_deleted_input(self):
        request = self.request(); child = os.fork()
        if child == 0:
            self.engine.checkpoint = lambda phase: os.kill(os.getpid(), signal.SIGKILL) if phase == 'Published' else None
            try: self.engine.unpack(request)
            except BaseException: os._exit(97)
            os._exit(98)
        _, status = os.waitpid(child, 0); self.assertTrue(os.WIFSIGNALED(status))
        self.input.unlink()
        with patch.object(codec.TreeArchive, 'extract', side_effect=AssertionError('no replay')):
            self.assertEqual(self.lookup(request)['state'], 'Completed')
        self.assertEqual((self.root / 'target/代码/中文.bin').read_bytes(), self.data)

    def test_capacity_inode_reserve_and_whole_operation_deadline_prevent_publication(self):
        for capacity in (os.statvfs_result((4096,4096,1000000,0,0,1000,1000,1000,0,255)),
                         os.statvfs_result((4096,4096,1000000,900000,900000,1000,1,1,0,255))):
            request = self.request()
            with patch.object(os, 'fstatvfs', return_value=capacity), self.assertRaisesRegex(driver.Rejected, 'FILES_CAPACITY_REQUIRED'):
                self.engine.unpack(request)
            self.assertEqual(self.lookup(request)['state'], 'Aborted'); self.assertFalse(self.pending(request).exists())
        request = self.request()
        with patch.object(unpacking.time, 'monotonic', side_effect=[0, 601]), self.assertRaisesRegex(driver.Rejected, 'FILES_TREE_DEADLINE'):
            self.engine.unpack(request)
        self.assertFalse((self.root / 'target').exists())

    def test_completed_history_remains_readable_when_new_receipt_quota_is_full(self):
        request = self.request(); result = self.engine.unpack(request)
        with patch.object(os, 'scandir', side_effect=AssertionError('no quota scan for old operation')):
            self.assertEqual(self.lookup(request), result)

    def test_corrupt_or_wrong_format_source_has_no_private_effect(self):
        for data, archive_format in ((b'bad', 'tar'), (b'bad', 'tar.gz'), (gzip.compress(b'not tar'), 'tar.gz')):
            self.input.write_bytes(data)
            with self.subTest(archive_format=archive_format), self.assertRaises(driver.Rejected): self.request(archive_format)
            self.assertFalse((self.root / driver.STAGING).exists())

    def test_complete_one_gib_plus_tail_raw_tar_is_materialized_and_fully_rehashed(self):
        self.assertGreaterEqual(shutil.disk_usage(self.root).free, 4 * 1024**3, 'test requires private disk headroom')
        original = self.fixture.base / 'large.bin'; size = 1024**3 + 7
        with original.open('wb') as stream:
            stream.truncate(size); stream.seek(size - 7); stream.write(b'TAIL\x00\xff!')
        with tarfile.open(self.input, 'w', format=tarfile.PAX_FORMAT) as writer:
            info = tarfile.TarInfo('large.bin'); info.size = size; info.mode = 0o640
            with original.open('rb') as stream: writer.addfile(info, stream)
        before = self.source_identity(); request = self.request(); result = self.engine.unpack(request)
        self.assertGreater(request['plan']['archiveBytes'], 1024**3)
        self.assertEqual(result['state'], 'Completed'); self.assertEqual(result['totalBytes'], size)
        with original.open('rb') as stream: expected = hashlib.file_digest(stream, 'sha256').digest()
        target = self.root / 'target/large.bin'
        with target.open('rb') as stream: actual = hashlib.file_digest(stream, 'sha256').digest()
        self.assertEqual(actual, expected); self.assertEqual(target.stat().st_size, size)
        self.assertEqual(self.source_identity(), before)
