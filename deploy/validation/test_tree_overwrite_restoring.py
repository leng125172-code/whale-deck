"""Isolated Linux whole-byte overwrite/swap tests; no installed RPC/BFF/UI claim.

HDD mount identity comes from the explicit disposable backup fixture, not a
physical second disk. Every tested object is below a TemporaryDirectory.
"""
import hashlib
import importlib.util
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
import test_tree_restoring as existing

restoring, unpacking, driver = existing.restoring, existing.unpacking, existing.driver

spec = importlib.util.spec_from_file_location('tree_overwrite_restoring', Path(__file__).resolve().parents[1] / 'host/whaledeck-tree-overwrite-restoring.py')
overwriting = importlib.util.module_from_spec(spec); spec.loader.exec_module(overwriting)


@unittest.skipUnless(os.name == 'posix' and os.geteuid() == 0, 'requires isolated real Linux root I/O')
class TreeOverwriteRestoringTests(unittest.TestCase):
    def setUp(self):
        self.fixture = existing.TreeRestoringTests(); self.fixture.setUp()
        self.addCleanup(self.fixture.doCleanups)
        policies = json.loads(self.fixture.registry.read_bytes())
        policies[0]['allowedActions'].append('files-tree-overwrite-restore')
        self.fixture.registry.write_text(json.dumps(policies))
        self.root, self.state = self.fixture.root, self.fixture.state
        self.target = self.root / '当前目录'; self.target.mkdir(mode=0o750)
        (self.target / '保留空目录').mkdir(mode=0o710)
        self.old_file = self.target / '旧内容.bin'
        self.old_data = bytes(reversed(range(256))) * 8192 + b'old\x00\xff!\n'
        self.old_file.write_bytes(self.old_data); self.old_file.chmod(0o640)
        os.utime(self.old_file, ns=(1712345678123456789, 1712345678123456789))
        self.old_inode = self.old_file.stat().st_ino
        engine_type = overwriting.overwrite_restoration_type(restoring.restoration_type(unpacking.FileTreeUnpacking))
        self.engine = engine_type(driver, self.fixture.captures, self.fixture.engine.manifest_type, self.fixture.engine.codec_type)

    def command(self):
        return {**self.fixture.command(self.target.name), 'target': {'rootId': 'test.shared', 'path': self.target.name,
            'expectedVersion': driver.version(self.target.stat())}}

    def request(self):
        return {'operationId': uuid.uuid4().hex, 'owner': 'B' * 64, 'plan': self.engine.plan(self.command())}

    def lookup(self, request):
        return self.engine.lookup({key: request[key] for key in ('operationId', 'owner')})

    def retained(self, request):
        return self.root / driver.STAGING / (request['operationId'] + self.engine.TEMPORARY_SUFFIX)

    def assert_original(self, path):
        self.assertEqual((path / self.old_file.name).read_bytes(), self.old_data)
        info = (path / self.old_file.name).stat()
        self.assertEqual((info.st_ino, stat.S_IMODE(info.st_mode), info.st_uid, info.st_gid, info.st_mtime_ns),
            (self.old_inode, 0o640, 0, 0, 1712345678123456789))
        self.assertTrue((path / '保留空目录').is_dir())

    def assert_backup(self, request):
        capture = self.fixture.store / (request['operationId'] + '.tar.gz')
        receipt = json.loads(capture.with_suffix('').with_suffix('.json').read_bytes())
        self.assertEqual(receipt['captureKind'], 'Overwrite')
        self.assertEqual(receipt['source'], request['plan']['command']['target'])
        self.assertEqual(receipt['archiveSha256'], hashlib.sha256(capture.read_bytes()).hexdigest().upper())
        with tarfile.open(capture, 'r:gz') as archive:
            self.assertEqual({item.name.rstrip('/') for item in archive.getmembers()}, {'.', '保留空目录', self.old_file.name})
            with archive.extractfile(self.old_file.name) as stream: self.assertEqual(stream.read(), self.old_data)

    def test_plan_is_read_only_and_binds_both_complete_trees(self):
        before = self.fixture.capture_proof(); request = self.request()
        self.assert_original(self.target)
        self.assertEqual(self.fixture.capture_proof(), before)
        self.assertFalse((self.root / driver.STAGING).exists())
        self.assertEqual(request['plan']['targetTotalBytes'], len(self.old_data))
        self.assertEqual(request['plan']['totalBytes'], len(self.fixture.data))

    def test_swap_preserves_entire_old_inode_tree_and_verified_hdd_capture(self):
        original_capture = self.fixture.capture_proof(); request = self.request()
        result = self.engine.restore(request)
        self.assertEqual(result['state'], 'Completed')
        self.assertEqual(result['kind'], 'DirectoryOverwriteRestore')
        self.assertEqual(result['overwrittenCaptureId'], request['operationId'])
        self.assertTrue(result['backupVerified'] and result['originalsRetained'])
        self.fixture.assert_tree(self.target); self.assert_original(self.retained(request)); self.assert_backup(request)
        for name, proof in original_capture.items(): self.assertEqual(self.fixture.capture_proof()[name], proof)
        with patch.object(self.engine.captures, 'ensure', side_effect=AssertionError('no recapture')), patch.object(driver, 'exchange', side_effect=AssertionError('no repeat swap')):
            self.assertEqual(self.engine.restore(request), result); self.assertEqual(self.lookup(request), result)

    def test_absent_and_non_directory_targets_are_rejected(self):
        command = self.command()
        (self.root / 'file-target').write_bytes(b'not a directory')
        for name in ('absent', 'file-target'):
            with self.subTest(name=name), self.assertRaises((OSError, driver.Rejected)):
                self.engine.plan({**command, 'target': {**command['target'], 'path': name}})
        self.assert_original(self.target)
        self.assertEqual((self.root / 'file-target').read_bytes(), b'not a directory')

    def test_absent_restore_plan_cannot_cross_adopt_overwrite_domain(self):
        ordinary = self.fixture.request()
        with self.assertRaises(driver.Rejected): self.engine.restore(ordinary)
        overwrite = self.request()
        with self.assertRaises(driver.Rejected): self.fixture.engine.restore(overwrite)
        self.assert_original(self.target)

    def test_changed_child_without_directory_version_change_rejects_before_backup(self):
        request = self.request(); version = driver.version(self.target.stat())
        self.old_file.write_bytes(b'external update')
        self.assertEqual(driver.version(self.target.stat()), version)
        with self.assertRaises(driver.Rejected): self.engine.restore(request)
        self.assertEqual(self.old_file.read_bytes(), b'external update')
        self.assertFalse((self.fixture.store / (request['operationId'] + '.json')).exists())

    def test_revoked_history_permission_rejects_before_any_swap(self):
        request = self.request(); values = json.loads(self.fixture.registry.read_bytes())
        values[0]['allowedActions'].remove('files-history'); self.fixture.registry.write_text(json.dumps(values))
        with self.assertRaisesRegex(driver.Rejected, 'FILES_ROOT_FORBIDDEN'): self.engine.restore(request)
        self.assert_original(self.target)

    def test_ordinary_restore_grant_does_not_authorize_overwrite(self):
        values = json.loads(self.fixture.registry.read_bytes())
        values[0]['allowedActions'].remove('files-tree-overwrite-restore')
        self.fixture.registry.write_text(json.dumps(values))
        with self.assertRaisesRegex(driver.Rejected, 'FILES_ROOT_FORBIDDEN'): self.request()
        self.assert_original(self.target)

    def test_staged_tree_changed_during_hdd_backup_is_not_published(self):
        request = self.request()
        def checkpoint(phase):
            if phase == 'BackupVerified': (self.retained(request) / self.fixture.file.name).write_bytes(b'bad stage')
        self.engine.checkpoint = checkpoint
        with self.assertRaises(driver.Rejected): self.engine.restore(request)
        self.assert_original(self.target); self.assert_backup(request)
        self.assertEqual(self.lookup(request)['state'], 'Aborted')

    def test_parent_path_replacement_during_backup_never_targets_new_parent(self):
        nested = self.root / 'nested'; nested.mkdir(); self.target.rename(nested / self.target.name)
        self.target = nested / self.target.name
        command = self.command(); command['target']['path'] = 'nested/' + self.target.name
        request = {'operationId': uuid.uuid4().hex, 'owner': 'B' * 64, 'plan': self.engine.plan(command)}
        def checkpoint(phase):
            if phase == 'BackupVerified':
                nested.rename(self.root / 'moved-parent'); nested.mkdir()
                (nested / self.target.name).mkdir(); (nested / self.target.name / 'foreign').write_bytes(b'leave alone')
        self.engine.checkpoint = checkpoint
        with self.assertRaises(driver.Rejected): self.engine.restore(request)
        self.assertEqual((nested / self.target.name / 'foreign').read_bytes(), b'leave alone')
        self.assert_original(self.root / 'moved-parent' / self.target.name)
        self.assertEqual(self.lookup(request)['state'], 'Unknown')

    def test_staging_path_replacement_never_loses_old_target(self):
        request = self.request()
        def checkpoint(phase):
            if phase == 'BackupVerified':
                (self.root / driver.STAGING).rename(self.root / 'moved-private')
                (self.root / driver.STAGING).mkdir(mode=0o700)
        self.engine.checkpoint = checkpoint
        with self.assertRaises(driver.Rejected): self.engine.restore(request)
        self.assert_original(self.target)
        self.assertTrue((self.root / 'moved-private' / (request['operationId'] + self.engine.TEMPORARY_SUFFIX)).is_dir())

    def test_staging_permissions_changed_during_backup_reject_before_swap(self):
        request = self.request()
        def checkpoint(phase):
            if phase == 'BackupVerified': (self.root / driver.STAGING).chmod(0o755)
        self.engine.checkpoint = checkpoint
        with self.assertRaisesRegex(driver.Rejected, '^FILES_STAGING_UNSAFE$'): self.engine.restore(request)
        self.assert_original(self.target)

    def test_retained_backup_corruption_after_swap_cannot_be_claimed_completed(self):
        request = self.request()
        def checkpoint(phase):
            if phase == 'Swapped':
                path = self.fixture.store / (request['operationId'] + '.tar.gz')
                with path.open('r+b') as stream: stream.seek(10); stream.write(b'bad')
        self.engine.checkpoint = checkpoint
        with self.assertRaises(driver.Rejected): self.engine.restore(request)
        self.fixture.assert_tree(self.target); self.assert_original(self.retained(request))
        self.assertEqual(self.lookup(request)['state'], 'Unknown')

    def test_more_than_one_gib_overwritten_payload_is_independently_rehashed_in_capture_and_original(self):
        size = 1024**3 + 7; block = bytes(range(256)) * 4096; remaining = size; expected = hashlib.sha256()
        with self.old_file.open('wb') as output:
            while remaining:
                value = block[:min(remaining, len(block))]; output.write(value); expected.update(value); remaining -= len(value)
        os.utime(self.old_file, ns=(1712345678123456789, 1712345678123456789))
        request = self.request(); result = self.engine.restore(request)
        self.assertEqual(result['state'], 'Completed'); self.fixture.assert_tree(self.target)
        old = self.retained(request) / self.old_file.name
        self.assertEqual(old.stat().st_size, size); self.assertEqual(old.stat().st_ino, self.old_inode)
        with old.open('rb') as stream: self.assertEqual(hashlib.file_digest(stream, 'sha256').digest(), expected.digest())
        path = self.fixture.store / (request['operationId'] + '.tar.gz')
        with tarfile.open(path, 'r:gz') as archive:
            member = archive.getmember(self.old_file.name); self.assertEqual(member.size, size)
            with archive.extractfile(member) as stream: self.assertEqual(hashlib.file_digest(stream, 'sha256').digest(), expected.digest())

    def test_protected_descendant_and_symlink_never_enter_overwrite(self):
        for name in ('.env', 'link'):
            with self.subTest(name=name):
                value = self.target / name
                if name == 'link': value.symlink_to(self.fixture.file)
                else: value.write_bytes(b'secret')
                try:
                    with self.assertRaises(driver.Rejected): self.request()
                finally: value.unlink()
        self.assert_original(self.target)

    def test_wrong_owner_and_changed_plan_cannot_read_or_replay_receipt(self):
        request = self.request(); self.engine.discard(request)
        with self.assertRaisesRegex(driver.Rejected, '^FILES_OPERATION_NOT_FOUND$'):
            self.engine.lookup({'operationId': request['operationId'], 'owner': 'C' * 64})
        changed = {**request['plan'], 'targetTotalBytes': 1}
        with self.assertRaisesRegex(driver.Rejected, '^FILES_PLAN_INVALID$'): self.engine.restore({**request, 'plan': changed})
        self.assert_original(self.target)

    def test_existing_capture_identity_cannot_be_reused_as_overwritten_capture(self):
        before = self.fixture.capture_proof()
        request = {**self.request(), 'operationId': self.fixture.capture['operationId']}
        with self.assertRaises(driver.Rejected): self.engine.restore(request)
        self.assert_original(self.target); self.assertEqual(self.fixture.capture_proof(), before)

    def test_completed_receipt_requires_backup_and_retained_original_evidence(self):
        request = self.request(); self.engine.restore(request)
        path = self.state / self.engine.FOLDER / (request['operationId'] + '.json')
        original = path.read_bytes(); record = json.loads(original)
        for change in ({'backupVerified': False}, {'originalsRetained': False}, {'swapStarted': False}, {'backupArchiveBytes': True}):
            with self.subTest(change=tuple(change)):
                try:
                    path.write_text(json.dumps({**record, **change}))
                    with self.assertRaisesRegex(driver.Rejected, '^FILES_RECEIPT_UNSAFE$'): self.lookup(request)
                finally: path.write_bytes(original)
        self.fixture.assert_tree(self.target); self.assert_original(self.retained(request))

    def test_corrupted_restore_capture_never_touches_target(self):
        request = self.request(); capture = self.fixture.store / (self.fixture.capture['operationId'] + '.tar.gz')
        with capture.open('r+b') as stream: stream.seek(10); stream.write(b'bad')
        with self.assertRaises(driver.Rejected): self.engine.restore(request)
        self.assert_original(self.target)

    def test_hdd_capacity_failure_preserves_old_target_and_no_swap(self):
        request = self.request()
        with patch.object(self.engine.captures, 'capacity', side_effect=driver.Rejected('FILES_BACKUP_CAPACITY_REQUIRED')):
            with self.assertRaisesRegex(driver.Rejected, 'FILES_BACKUP_CAPACITY_REQUIRED'): self.engine.restore(request)
        self.assert_original(self.target); self.assertEqual(self.lookup(request)['state'], 'Aborted')

    def test_cancel_after_full_backup_keeps_both_versions_but_never_swaps(self):
        request = self.request()
        def checkpoint(phase):
            if phase == 'BackupVerified': self.engine.cancelled = lambda: True
        self.engine.checkpoint = checkpoint
        with self.assertRaisesRegex(driver.Rejected, '^FILES_TREE_CANCELED$'): self.engine.restore(request)
        self.engine.cancelled = None
        self.assert_original(self.target); self.assert_backup(request)
        self.assertEqual(self.lookup(request)['state'], 'Aborted')

    def test_cancel_after_actual_swap_is_reconciled_as_completed_without_undo(self):
        request = self.request()
        def checkpoint(phase):
            if phase == 'Swapped': self.engine.cancelled = lambda: True
        self.engine.checkpoint = checkpoint
        with self.assertRaisesRegex(driver.Rejected, '^FILES_TREE_CANCELED$'): self.engine.restore(request)
        self.engine.cancelled = None
        self.assertEqual(self.lookup(request)['state'], 'Completed')
        self.fixture.assert_tree(self.target); self.assert_original(self.retained(request)); self.assert_backup(request)

    def test_external_update_during_backup_is_not_silently_overwritten(self):
        request = self.request()
        def checkpoint(phase):
            if phase == 'BackupVerified': self.old_file.write_bytes(b'concurrent writer')
        self.engine.checkpoint = checkpoint
        with self.assertRaises(driver.Rejected): self.engine.restore(request)
        self.assertEqual(self.old_file.read_bytes(), b'concurrent writer')
        self.assertEqual(self.lookup(request)['state'], 'Unknown')
        self.assert_backup(request)

    def test_swapped_original_mutation_is_unknown_not_completed_or_rolled_back(self):
        request = self.request()
        def checkpoint(phase):
            if phase == 'Swapped': (self.retained(request) / self.old_file.name).write_bytes(b'tampered retained')
        self.engine.checkpoint = checkpoint
        with self.assertRaises(driver.Rejected): self.engine.restore(request)
        self.fixture.assert_tree(self.target)
        self.assertEqual(self.lookup(request)['state'], 'Unknown'); self.assert_backup(request)

    def test_actual_sigkill_at_each_boundary_observes_without_repeating_effects(self):
        phases = ('Preparing', 'StagingIntent', 'Ready', 'BackupStarting', 'BackupVerified', 'SwapStarting', 'Swapped', 'Published')
        for phase in phases:
            with self.subTest(phase=phase):
                # Each case gets its own complete fixture and receipt namespace.
                case = TreeOverwriteRestoringTests(); case.setUp()
                try:
                    request = case.request()
                    child = os.fork()
                    if child == 0:
                        case.engine.checkpoint = lambda current: os.kill(os.getpid(), signal.SIGKILL) if current == phase else None
                        try: case.engine.restore(request)
                        except BaseException: os._exit(2)
                        os._exit(3)
                    _, status = os.waitpid(child, 0)
                    self.assertTrue(os.WIFSIGNALED(status)); self.assertEqual(os.WTERMSIG(status), signal.SIGKILL)
                    with patch.object(case.engine.captures, 'ensure', side_effect=AssertionError('no recapture')), patch.object(driver, 'exchange', side_effect=AssertionError('no retry')):
                        observed = case.lookup(request)
                        self.assertEqual(case.engine.restore(request), observed)
                    if phase in ('Swapped', 'Published'):
                        self.assertEqual(observed['state'], 'Completed')
                        case.fixture.assert_tree(case.target); case.assert_original(case.retained(request)); case.assert_backup(request)
                    else:
                        self.assertEqual(observed['state'], 'Unknown' if phase == 'SwapStarting' else 'Aborted')
                        case.assert_original(case.target)
                finally: case.doCleanups()

    def test_fixed_loader_only_loads_all_root_owned_single_link_non_writable_regular_modules(self):
        folder = self.fixture.fixture.base / 'installed-overwrite-restoring'; folder.mkdir(mode=0o700)
        names = ('whaledeck-file-tree.py', 'whaledeck-tree-archive.py', 'whaledeck-tree-backups.py',
            'whaledeck-tree-unpacking.py', 'whaledeck-tree-restoring.py', 'whaledeck-tree-overwrite-restoring.py')
        for name in names:
            target = folder / name; shutil.copyfile(Path(driver.__file__).with_name(name), target); target.chmod(0o640)
        before = self.fixture.capture_proof()
        with patch.dict(sys.modules, {driver.__name__: driver}), patch.object(driver, '__file__', str(folder / 'whaledeck-files.py')), patch.object(driver, 'backup_engine', return_value=self.fixture.fixture.backups.engine):
            loaded = driver.tree_overwrite_restoring_engine(); command = self.command()
            self.assertEqual(loaded.plan(command), self.engine.plan(command))
            self.assertEqual(driver.tree_restoring_engine(overwrite=True).DOMAIN, self.engine.DOMAIN)
            ordinary = driver.tree_restoring_engine()
            self.assertNotEqual(loaded.DOMAIN, ordinary.DOMAIN)
            with self.assertRaises(driver.Rejected): ordinary.plan(command)
            request = {'operationId': uuid.uuid4().hex, 'owner': 'B' * 64, 'plan': loaded.plan(command)}
            result = loaded.restore(request); self.assertEqual(result['state'], 'Completed')
            self.fixture.assert_tree(self.target); self.assert_original(self.retained(request)); self.assert_backup(request)
            for name in names:
                target = folder / name
                for mode in (0o660, 0o642):
                    with self.subTest(module=name, unsafe='writable', mode=oct(mode)):
                        try:
                            target.chmod(mode)
                            with self.assertRaisesRegex(driver.Rejected, '^FILES_MODULE_UNSAFE$'): driver.tree_overwrite_restoring_engine()
                        finally: target.chmod(0o640)
                with self.subTest(module=name, unsafe='hardlink'):
                    linked = folder / 'linked'; os.link(target, linked)
                    try:
                        with self.assertRaisesRegex(driver.Rejected, '^FILES_MODULE_UNSAFE$'): driver.tree_overwrite_restoring_engine()
                    finally: linked.unlink()
                with self.subTest(module=name, unsafe='owner'):
                    try:
                        os.chown(target, 12345, 0)
                        with self.assertRaisesRegex(driver.Rejected, '^FILES_MODULE_UNSAFE$'): driver.tree_overwrite_restoring_engine()
                    finally: os.chown(target, 0, 0)
                for kind in ('symlink', 'directory'):
                    with self.subTest(module=name, unsafe=kind):
                        original = folder / 'original.py'; target.rename(original)
                        try:
                            if kind == 'symlink': target.symlink_to(original)
                            else: target.mkdir(mode=0o700)
                            with self.assertRaisesRegex(driver.Rejected, '^FILES_MODULE_UNSAFE$'): driver.tree_overwrite_restoring_engine()
                        finally:
                            if kind == 'symlink': target.unlink()
                            else: target.rmdir()
                            original.rename(target)
                with self.subTest(module=name, unsafe='oversized'):
                    contents = target.read_bytes()
                    try:
                        with target.open('ab') as stream: stream.write(b' ' * 131073)
                        with self.assertRaisesRegex(driver.Rejected, '^FILES_MODULE_UNSAFE$'): driver.tree_overwrite_restoring_engine()
                    finally: target.write_bytes(contents)
            with patch.object(driver, 'exchange', side_effect=AssertionError('loader status must not repeat swap')):
                self.assertEqual(driver.tree_overwrite_restoring_engine().lookup({key: request[key] for key in ('operationId', 'owner')}), result)
        for name, proof in before.items(): self.assertEqual(self.fixture.capture_proof()[name], proof)

    def invoke_main(self, action, value, trailing=b''):
        reader, writer = os.pipe()
        try: os.write(writer, json.dumps(value).encode() + b'\n' + trailing)
        finally: os.close(writer)
        output = []
        with os.fdopen(reader, 'r') as stream, patch.object(sys, 'stdin', stream), patch.object(sys, 'argv', ['whaledeck-files.py', action]), patch.dict(os.environ, {'INVOCATION_ID': uuid.uuid4().hex}), patch.object(driver, 'tree_overwrite_restoring_engine', return_value=self.engine), patch.object(driver, 'tree_restoring_engine', side_effect=AssertionError('overwrite commands must not use ordinary restoration')), patch.object(driver, 'emit', side_effect=output.append):
            driver.main()
        self.assertEqual(output[0]['type'], 'driver'); self.assertEqual(len(output), 2)
        return output[1]

    def test_fixed_driver_plan_apply_status_and_discard_overwrite_without_repeating_swap(self):
        before = self.fixture.capture_proof(); plan = self.invoke_main('tree-overwrite-restore-plan', self.command())
        self.assert_original(self.target); self.assertEqual(self.fixture.capture_proof(), before)
        canceled = {'operationId': uuid.uuid4().hex, 'owner': 'B' * 64, 'plan': plan}
        self.assertEqual(self.invoke_main('tree-overwrite-restore-discard', canceled)['state'], 'Aborted')
        with patch.object(self.engine.captures, 'ensure', side_effect=AssertionError('discard must not capture')), patch.object(driver, 'exchange', side_effect=AssertionError('discard must not swap')):
            self.assertEqual(self.invoke_main('tree-overwrite-restore-apply', canceled)['state'], 'Aborted')
            self.assertEqual(self.invoke_main('tree-overwrite-restore-status', {key: canceled[key] for key in ('operationId', 'owner')})['state'], 'Aborted')
        self.assert_original(self.target); self.assertEqual(self.fixture.capture_proof(), before)
        request = {**canceled, 'operationId': uuid.uuid4().hex}
        result = self.invoke_main('tree-overwrite-restore-apply', request)
        self.assertEqual(result['state'], 'Completed'); self.assertEqual(result['kind'], 'DirectoryOverwriteRestore')
        self.assertEqual(result['backupArchiveBytes'], (self.fixture.store / (request['operationId'] + '.tar.gz')).stat().st_size)
        self.assertEqual(result['backupArchiveSha256'], hashlib.sha256((self.fixture.store / (request['operationId'] + '.tar.gz')).read_bytes()).hexdigest().upper())
        self.fixture.assert_tree(self.target); self.assert_original(self.retained(request)); self.assert_backup(request)
        with patch.object(self.engine.captures, 'ensure', side_effect=AssertionError('receipt must not recapture')), patch.object(driver, 'exchange', side_effect=AssertionError('receipt must not repeat swap')):
            self.assertEqual(self.invoke_main('tree-overwrite-restore-status', {key: request[key] for key in ('operationId', 'owner')}), result)
            self.assertEqual(self.invoke_main('tree-overwrite-restore-apply', request), result)
            self.assertEqual(self.invoke_main('tree-overwrite-restore-discard', request), result)
        for name, proof in before.items(): self.assertEqual(self.fixture.capture_proof()[name], proof)

    def test_fixed_driver_rejects_trailing_input_and_unapproved_commands_before_mutation(self):
        before = self.fixture.capture_proof(); command = self.command(); request = self.request()
        for action, value in (('tree-overwrite-restore-plan', command), ('tree-overwrite-restore-apply', request),
                ('tree-overwrite-restore-status', {key: request[key] for key in ('operationId', 'owner')}),
                ('tree-overwrite-restore-discard', request)):
            with self.subTest(action=action), patch.object(self.engine, 'plan', side_effect=AssertionError('no plan with trailing input')), patch.object(self.engine, 'restore', side_effect=AssertionError('no swap with trailing input')), patch.object(self.engine, 'lookup', side_effect=AssertionError('no lookup with trailing input')), patch.object(self.engine, 'discard', side_effect=AssertionError('no discard with trailing input')):
                with self.assertRaises(driver.Rejected): self.invoke_main(action, value, b'{}\n')
        for action in ('tree-overwrite-restore-delete', 'tree-overwrite-restore-shell', 'tree-overwrite-restore-apply;true',
                'tree-overwrite-restore', 'tree-overwrite-restore-apply --force'):
            with self.subTest(action=action), self.assertRaisesRegex(driver.Rejected, '^FILES_ACTION_INVALID$'):
                self.invoke_main(action, request)
        self.assert_original(self.target); self.assertEqual(self.fixture.capture_proof(), before)
        self.assertFalse((self.root / driver.STAGING).exists())
        self.assertFalse((self.state / self.engine.FOLDER / (request['operationId'] + '.json')).exists())


if __name__ == '__main__': unittest.main()
