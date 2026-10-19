"""Actual isolated Linux restore I/O; HDD identity is an explicit fixture.

No production registration, RPC, BFF, jobs or browser acceptance is claimed.
"""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import signal
import stat
import unittest
from unittest.mock import patch
import uuid
import test_file_transport as common
from test_file_tree_manifest import tree_module
from test_tree_archive_codec import codec
from test_tree_backups import module as backups
from test_tree_unpacking import unpacking

spec = importlib.util.spec_from_file_location('tree_restoring', Path(__file__).resolve().parents[1] / 'host/whaledeck-tree-restoring.py')
restoring = importlib.util.module_from_spec(spec); spec.loader.exec_module(restoring)
driver = common.driver


@unittest.skipUnless(os.name == 'posix' and os.geteuid() == 0, 'requires isolated real Linux root I/O')
class TreeRestoringTests(unittest.TestCase):
    def setUp(self):
        self.fixture = common.FileTransportTests(); self.fixture.setUp()
        self.addCleanup(self.fixture.tearDown)
        self.root, self.state, self.registry = self.fixture.root, self.fixture.state, self.fixture.registry
        policies = json.loads(self.registry.read_bytes())
        policies[0]['allowedActions'] += ['files-mutate', 'files-archive', 'files-history', 'files-tree-restore']
        self.registry.write_text(json.dumps(policies))
        self.source = self.root / '原目录'; self.source.mkdir(mode=0o750)
        (self.source / '空目录').mkdir(mode=0o710)
        self.file = self.source / ('中文' * 30 + '.bin')
        self.data = bytes(range(256)) * 8192 + b'\x00tail\xff\n'
        self.file.write_bytes(self.data); self.file.chmod(0o6751)
        os.utime(self.file, ns=(1712345678123456789, 1712345678123456789))
        self.captures = backups.TreeBackups(driver, self.fixture.backups.engine, tree_module.FileTreeManifest, codec.TreeArchive)
        source = {'rootId': 'test.shared', 'path': self.source.name, 'expectedVersion': driver.version(self.source.stat())}
        manifest = tree_module.FileTreeManifest(driver).scan(source)
        self.capture = {'operationId': uuid.uuid4().hex, 'owner': 'A' * 64, 'source': source,
            'manifestSha256': manifest['manifestSha256'], 'captureKind': 'Trash'}
        self.captures.ensure(self.capture)
        self.store = self.fixture.backups.target / self.captures.FOLDER
        self.engine = restoring.restoration_type(unpacking.FileTreeUnpacking)(driver, self.captures, tree_module.FileTreeManifest, codec.TreeArchive)

    def command(self, target='恢复目录'):
        return {'source': self.capture['source'], 'target': {'rootId': 'test.shared', 'path': target},
            'format': 'tar.gz', 'captureId': self.capture['operationId'], 'captureOwner': self.capture['owner'],
            'captureKind': self.capture['captureKind'], 'manifestSha256': self.capture['manifestSha256']}

    def request(self, target='恢复目录'):
        return {'operationId': uuid.uuid4().hex, 'owner': 'B' * 64, 'plan': self.engine.plan(self.command(target))}

    def lookup(self, request): return self.engine.lookup({key: request[key] for key in ('operationId', 'owner')})

    def capture_proof(self):
        return {path.name: (path.stat().st_ino, path.stat().st_mode, path.stat().st_mtime_ns, path.read_bytes())
            for path in self.store.iterdir()}

    def remove_source(self):
        self.file.unlink(); (self.source / '空目录').rmdir(); self.source.rmdir()

    def assert_tree(self, target):
        self.assertEqual((target / self.file.name).read_bytes(), self.data)
        self.assertTrue((target / '空目录').is_dir())
        metadata = (target / self.file.name).stat()
        self.assertEqual((stat.S_IMODE(metadata.st_mode), metadata.st_uid, metadata.st_gid, metadata.st_mtime_ns),
            (0o751, 0, 0, 1712345678123456789))
        self.assertEqual(stat.S_IMODE(target.stat().st_mode), 0o750)

    def test_original_absent_path_restores_full_bytes_ns_and_metadata_and_keeps_capture_immutable(self):
        before = self.capture_proof(); self.remove_source()
        request = self.request(self.source.name); result = self.engine.restore(request)
        self.assertEqual(result['state'], 'Completed'); self.assertEqual(result['kind'], 'DirectoryRestore')
        self.assertEqual(result['captureId'], self.capture['operationId']); self.assertFalse(result['partialRetained'])
        self.assertEqual(result['totalBytes'], len(self.data)); self.assertEqual(result['entryCount'], 3)
        self.assert_tree(self.source); self.assertEqual(self.capture_proof(), before)
        with patch.object(codec.TreeArchive, 'extract', side_effect=AssertionError('must not re-extract')):
            self.assertEqual(self.engine.restore(request), result); self.assertEqual(self.lookup(request), result)

    def test_other_destination_does_not_mutate_the_still_existing_original(self):
        before = (driver.version(self.source.stat()), driver.version(self.file.stat()), self.file.read_bytes(), self.capture_proof())
        request = self.request(); self.engine.restore(request); self.assert_tree(self.root / '恢复目录')
        self.assertEqual((driver.version(self.source.stat()), driver.version(self.file.stat()), self.file.read_bytes(), self.capture_proof()), before)

    def test_trusted_target_owner_not_capture_identity_controls_restored_tree(self):
        target_root = self.root / 'target-root'; target_root.mkdir(mode=0o750)
        policies = json.loads(self.registry.read_bytes())
        policies.append({**policies[0], 'id': 'test.restore-target', 'path': str(target_root), 'ownerUid': 1000, 'ownerGid': 1001})
        self.registry.write_text(json.dumps(policies))
        command = {**self.command(), 'target': {'rootId': 'test.restore-target', 'path': 'restored'}}
        request = {'operationId': uuid.uuid4().hex, 'owner': 'B' * 64, 'plan': self.engine.plan(command)}
        self.engine.restore(request)
        for path in (target_root / 'restored', target_root / 'restored/空目录', target_root / 'restored' / self.file.name):
            self.assertEqual((path.stat().st_uid, path.stat().st_gid), (1000, 1001))
        self.assertEqual((self.file.stat().st_uid, self.file.stat().st_gid), (0, 0))

    def test_cancel_before_write_and_after_ready_never_deletes_capture_or_promotes_partial(self):
        before = self.capture_proof(); first = self.request('cancel-before')
        self.engine.cancelled = lambda: True
        with self.assertRaisesRegex(driver.Rejected, '^FILES_TREE_CANCELED$'): self.engine.restore(first)
        self.assertFalse((self.state / self.engine.FOLDER / (first['operationId'] + '.json')).exists())
        self.engine.cancelled = None; second = self.request('cancel-ready')
        def cancel(phase):
            if phase == 'Ready': self.engine.cancelled = lambda: True
        self.engine.checkpoint = cancel
        with self.assertRaisesRegex(driver.Rejected, '^FILES_TREE_CANCELED$'): self.engine.restore(second)
        self.assertFalse((self.root / 'cancel-ready').exists())
        staged = self.root / driver.STAGING / (second['operationId'] + '.tree-restore')
        self.assertTrue(staged.is_dir()); self.assertEqual((staged / self.file.name).read_bytes(), self.data)
        self.engine.cancelled = None
        with patch.object(codec.TreeArchive, 'extract', side_effect=AssertionError('no replay')):
            self.assertEqual(self.lookup(second)['state'], 'Aborted')
        self.assertEqual(self.capture_proof(), before)

    def test_overwrite_history_capture_can_restore_to_another_absent_target_without_overwriting(self):
        capture = {**self.capture, 'operationId': uuid.uuid4().hex, 'captureKind': 'Overwrite'}
        self.captures.ensure(capture); self.capture = capture
        request = self.request(); self.engine.restore(request); self.assert_tree(self.root / '恢复目录')
        self.assertEqual(self.file.read_bytes(), self.data)

    def test_older_capture_accepts_only_exact_addition_of_restore_grant_without_rewriting_history(self):
        current = self.registry.read_bytes(); policies = json.loads(current)
        policies[0]['allowedActions'].remove('files-tree-restore'); self.registry.write_text(json.dumps(policies))
        manifest = tree_module.FileTreeManifest(driver).scan(self.capture['source'])
        self.capture = {**self.capture, 'operationId': uuid.uuid4().hex, 'manifestSha256': manifest['manifestSha256']}
        self.captures.ensure(self.capture); before = self.capture_proof()
        self.registry.write_bytes(current)
        request = self.request(); self.engine.restore(request); self.assert_tree(self.root / '恢复目录')
        self.assertEqual(self.capture_proof(), before)
        for changed in ({'ownerUid': 1000}, {'externalId': 'renamed-resource'}):
            with self.subTest(field=tuple(changed)):
                policy = json.loads(current); policy[0].update(changed); self.registry.write_text(json.dumps(policy))
                with self.assertRaises(driver.Rejected): self.engine.plan(self.command('changed-policy'))
                self.assertEqual(self.capture_proof(), before)
        self.registry.write_bytes(current)

    def test_older_capture_accepts_only_exact_new_recycle_or_both_grants_and_preserves_all_metadata(self):
        current = json.loads(self.registry.read_bytes()); current[0]['allowedActions'].append('files-tree-trash')
        for index, added in enumerate((('files-tree-trash',), ('files-tree-trash', 'files-tree-restore'))):
            with self.subTest(added=added):
                previous = json.loads(json.dumps(current))
                previous[0]['allowedActions'] = [action for action in previous[0]['allowedActions'] if action not in added]
                self.registry.write_text(json.dumps(previous))
                manifest = tree_module.FileTreeManifest(driver).scan(self.capture['source'])
                self.capture = {**self.capture, 'operationId': uuid.uuid4().hex, 'manifestSha256': manifest['manifestSha256']}
                self.captures.ensure(self.capture); before = self.capture_proof()
                self.registry.write_text(json.dumps(current))
                request = self.request('legacy-new-grants-' + str(index)); self.engine.restore(request)
                self.assert_tree(self.root / ('legacy-new-grants-' + str(index)))
                self.assertEqual(self.capture_proof(), before)

    def test_nonexistent_capture_does_not_allocate_hdd_entries_or_restore_anything(self):
        before = self.capture_proof(); command = {**self.command(), 'captureId': uuid.uuid4().hex}
        with self.assertRaisesRegex(driver.Rejected, '^FILES_TREE_BACKUP_NOT_FOUND$'): self.engine.plan(command)
        self.assertEqual(self.capture_proof(), before); self.assertFalse((self.root / '恢复目录').exists())

    def test_more_than_one_gib_is_fully_restored_and_independently_rehashed_not_truncated_by_file_limit(self):
        block = bytes(range(256)) * 256; size = 1024**3 + 7; remaining = size; expected = hashlib.sha256()
        with self.file.open('wb') as output:
            while remaining:
                piece = block[:min(remaining, len(block))]
                output.write(piece); expected.update(piece); remaining -= len(piece)
        source = {**self.capture['source'], 'expectedVersion': driver.version(self.source.stat())}
        manifest = tree_module.FileTreeManifest(driver).scan(source)
        self.capture = {**self.capture, 'source': source, 'operationId': uuid.uuid4().hex, 'manifestSha256': manifest['manifestSha256']}
        self.captures.ensure(self.capture); before = self.capture_proof()
        source_identity = driver.publication_identity(self.file.stat())
        request = self.request('large-restored')
        result = self.engine.restore(request); self.assertEqual(result['totalBytes'], size)
        target = self.root / 'large-restored' / self.file.name
        self.assertEqual(target.stat().st_size, size)
        for path in (self.file, target):
            actual = hashlib.sha256(); count = 0
            with path.open('rb') as stream:
                while piece := stream.read(1024 * 1024): actual.update(piece); count += len(piece)
            self.assertEqual(count, size); self.assertEqual(actual.digest(), expected.digest())
        self.assertEqual(driver.publication_identity(self.file.stat()), source_identity)
        self.assertEqual(self.capture_proof(), before)

    def test_capture_owner_source_kind_and_manifest_are_immutable_not_current_login_impersonation(self):
        before = self.capture_proof()
        for changed in ({'captureOwner': 'C' * 64}, {'captureKind': 'Overwrite'}, {'manifestSha256': 'D' * 64},
                {'source': {**self.capture['source'], 'path': 'other'}}):
            with self.subTest(changed=tuple(changed)):
                with self.assertRaises(driver.Rejected): self.engine.plan({**self.command(), **changed})
                self.assertEqual(self.capture_proof(), before)
        request = self.request(); self.assertNotEqual(request['owner'], self.capture['owner'])
        self.assertEqual(self.engine.restore(request)['state'], 'Completed')

    def test_ready_but_unverified_capture_is_not_promoted_or_modified(self):
        path = self.store / (self.capture['operationId'] + '.json')
        saved = json.loads(path.read_bytes()); saved['state'] = 'Ready'; path.write_text(json.dumps(saved))
        before = self.capture_proof()
        with self.assertRaisesRegex(driver.Rejected, '^FILES_TREE_BACKUP_INCOMPLETE$'): self.engine.plan(self.command())
        self.assertEqual(self.capture_proof(), before)

    def test_corrupted_hdd_archive_or_manifest_cannot_publish_or_repair_itself(self):
        for suffix in ('.tar.gz', '.manifest.json'):
            with self.subTest(suffix=suffix):
                path = self.store / (self.capture['operationId'] + suffix); original = path.read_bytes()
                path.write_bytes(original[:-1] + b'!'); before = self.capture_proof()
                with self.assertRaises(driver.Rejected): self.engine.plan(self.command())
                self.assertEqual(self.capture_proof(), before); self.assertFalse((self.root / '恢复目录').exists())
                path.write_bytes(original)

    def test_existing_target_is_not_overwritten_and_changed_capture_blocks_execution_before_publication(self):
        target = self.root / 'existing'; target.mkdir(); (target / 'keep').write_bytes(b'original')
        with self.assertRaisesRegex(driver.Rejected, '^FILES_VERSION_CONFLICT$'): self.engine.plan(self.command('existing'))
        request = self.request(); archive = self.store / (self.capture['operationId'] + '.tar.gz')
        archive.write_bytes(b'corrupt')
        with self.assertRaises(driver.Rejected): self.engine.restore(request)
        self.assertEqual((target / 'keep').read_bytes(), b'original'); self.assertFalse((self.root / '恢复目录').exists())

    def test_root_revocation_and_hdd_attestation_failure_refuse_without_touching_original_or_capture(self):
        before = self.capture_proof(); policies = self.registry.read_bytes()
        values = json.loads(policies); values[0]['allowedActions'].remove('files-tree-restore'); self.registry.write_text(json.dumps(values))
        with self.assertRaisesRegex(driver.Rejected, '^FILES_ROOT_FORBIDDEN$'): self.engine.plan(self.command())
        self.registry.write_bytes(policies)
        with patch.object(self.captures.b, 'attest', side_effect=driver.Rejected('FILES_BACKUP_IDENTITY_INVALID')):
            with self.assertRaises(driver.Rejected): self.engine.plan(self.command())
        self.assertEqual(self.file.read_bytes(), self.data); self.assertEqual(self.capture_proof(), before)

    def test_restore_and_unpack_domains_and_staging_never_adopt_each_other(self):
        request = self.request(); private = self.root / driver.STAGING; private.mkdir(mode=0o700)
        old = private / (request['operationId'] + '.tree-unpack'); old.mkdir(mode=0o700)
        (old / 'keep').write_bytes(b'not-restore')
        self.engine.restore(request)
        self.assertEqual((old / 'keep').read_bytes(), b'not-restore')
        self.assertTrue((self.state / 'tree-restoring' / (request['operationId'] + '.json')).is_file())
        ordinary = unpacking.FileTreeUnpacking(driver, tree_module.FileTreeManifest, codec.TreeArchive)
        with self.assertRaises(driver.Rejected): ordinary.validate_plan(request['plan'])

    def test_real_sigkill_all_seven_fences_only_observes_never_reextracts_or_promotes_partial(self):
        for phase in ('Preparing', 'StagingIntent', 'Created', 'Staging', 'Ready', 'Publishing', 'Published'):
            with self.subTest(phase=phase):
                target = 'kill-' + phase; request = self.request(target); before = self.capture_proof()
                pid = os.fork()
                if pid == 0:
                    self.engine.checkpoint = lambda value: os.kill(os.getpid(), signal.SIGKILL) if value == phase else None
                    try: self.engine.restore(request)
                    finally: os._exit(90)
                _, status = os.waitpid(pid, 0); self.assertTrue(os.WIFSIGNALED(status)); self.assertEqual(os.WTERMSIG(status), signal.SIGKILL)
                with patch.object(codec.TreeArchive, 'extract', side_effect=AssertionError('no replay')):
                    result = self.lookup(request); self.assertEqual(self.engine.restore(request), result)
                if phase == 'Published':
                    self.assertEqual(result['state'], 'Completed'); self.assert_tree(self.root / target)
                else:
                    self.assertIn(result['state'], ('Aborted', 'Unknown')); self.assertFalse((self.root / target).exists())
                self.assertEqual(self.capture_proof(), before)


if __name__ == '__main__': unittest.main()
