"""Disposable real Linux recycle I/O; HDD identity is explicitly simulated.

No installed helper/Agent, actual HDD identity, BFF/jobs or browser claim.
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
from test_tree_moves import moves
from test_tree_restoring import restoring, unpacking

spec = importlib.util.spec_from_file_location('tree_recycling', Path(__file__).resolve().parents[1] / 'host/whaledeck-tree-recycling.py')
recycling = importlib.util.module_from_spec(spec); spec.loader.exec_module(recycling)
driver = common.driver


@unittest.skipUnless(os.name == 'posix' and os.geteuid() == 0, 'requires actual disposable Linux root I/O')
class TreeRecyclingTests(unittest.TestCase):
    def setUp(self):
        self.fixture = common.FileTransportTests(); self.fixture.setUp(); self.addCleanup(self.fixture.tearDown)
        self.root, self.state, self.registry = self.fixture.root, self.fixture.state, self.fixture.registry
        values = json.loads(self.registry.read_bytes())
        values[0]['allowedActions'] += ['files-mutate', 'files-history', 'files-archive', 'files-tree-trash', 'files-tree-restore']
        self.registry.write_text(json.dumps(values))
        self.data = bytes(range(256)) * 8192 + b'\x00tail\xff\n'
        self.make_source('原目录')
        self.captures = backups.TreeBackups(driver, self.fixture.backups.engine, tree_module.FileTreeManifest, codec.TreeArchive)
        self.engine = recycling.FileTreeRecycling(driver, self.captures, tree_module.FileTreeManifest, codec.TreeArchive, moves.FileTreeMoves)

    def make_source(self, name):
        self.source = self.root / name; self.source.mkdir(mode=0o750)
        (self.source / '空目录').mkdir(mode=0o710); (self.source / '代码').mkdir(mode=0o750)
        self.file = self.source / '代码' / ('中文' * 30 + '.bin')
        self.file.write_bytes(self.data); self.file.chmod(0o751); os.utime(self.file, ns=(1712345678123456789, 1712345678123456789))

    def request(self):
        source = {'rootId': 'test.shared', 'path': str(self.source.relative_to(self.root)), 'expectedVersion': driver.version(self.source.stat())}
        return {'operationId': uuid.uuid4().hex, 'owner': 'A' * 64, 'plan': self.engine.plan(source)}

    def lookup(self, request): return self.engine.lookup({key: request[key] for key in ('operationId', 'owner')})
    def private(self, request): return self.source.parent / driver.STAGING / (request['operationId'] + self.engine.SUFFIX)
    def receipt(self, request): return self.state / self.engine.FOLDER / (request['operationId'] + '.json')
    def archive(self, request): return self.fixture.backups.target / self.captures.FOLDER / (request['operationId'] + '.tar.gz')
    def capture_request(self, request): return self.engine.capture_request(request)

    def source_proof(self):
        return (driver.publication_identity(self.source.stat()), driver.publication_identity(self.file.stat()), self.file.read_bytes())

    def test_hdd_is_verified_before_original_inodes_are_retained_and_source_path_removed(self):
        request = self.request(); original = driver.publication_identity(self.file.stat()); phases = []
        def checkpoint(phase):
            phases.append(phase)
            if phase in ('CaptureVerified', 'SourceValidated', 'SourcePrepared', 'SourceRetaining'):
                saved = json.loads((self.archive(request).parent / (request['operationId'] + '.json')).read_bytes())
                self.assertEqual(saved['state'], 'Verified'); self.assertTrue(self.source.exists())
        self.engine.checkpoint = checkpoint
        result = self.engine.recycle(request)
        self.assertEqual(result['state'], 'Completed'); self.assertTrue(result['sourceRemoved']); self.assertTrue(result['originalsRetained'])
        self.assertTrue(result['captureVerified']); self.assertFalse(self.source.exists())
        retained = self.private(request) / self.file.relative_to(self.source)
        self.assertEqual(driver.publication_identity(retained.stat()), original); self.assertEqual(retained.read_bytes(), self.data)
        self.assertEqual(stat.S_IMODE(self.private(request).stat().st_mode), 0o700)
        self.assertEqual(result['archiveSha256'], hashlib.sha256(self.archive(request).read_bytes()).hexdigest().upper())
        self.assertLess(phases.index('CaptureVerified'), phases.index('SourceRetaining'))
        with patch.object(codec.TreeArchive, 'create', side_effect=AssertionError('no recapture')):
            self.assertEqual(self.lookup(request), result); self.assertEqual(self.engine.recycle(request), result)

    def test_hdd_capture_restores_complete_tree_to_original_absent_path_under_another_operation_owner(self):
        request = self.request(); self.engine.recycle(request); before = self.archive(request).read_bytes()
        restore = restoring.restoration_type(unpacking.FileTreeUnpacking)(driver, self.captures, tree_module.FileTreeManifest, codec.TreeArchive)
        command = {'source': request['plan']['source'], 'target': {'rootId': 'test.shared', 'path': self.source.name}, 'format': 'tar.gz',
            'captureId': request['operationId'], 'captureOwner': request['owner'], 'captureKind': 'Trash', 'manifestSha256': request['plan']['manifestSha256']}
        intent = {'operationId': uuid.uuid4().hex, 'owner': 'B' * 64, 'plan': restore.plan(command)}
        self.assertEqual(restore.restore(intent)['state'], 'Completed'); self.assertEqual(self.file.read_bytes(), self.data)
        self.assertEqual(self.file.stat().st_mtime_ns, 1712345678123456789)
        self.assertTrue((self.source / '空目录').is_dir()); self.assertNotEqual(self.file.stat().st_ino, (self.private(request) / self.file.relative_to(self.source)).stat().st_ino)
        self.assertEqual(self.archive(request).read_bytes(), before)

    def test_empty_directory_has_real_hdd_capture_and_can_be_restored(self):
        self.file.unlink(); (self.source / '代码').rmdir(); (self.source / '空目录').rmdir()
        request = self.request(); result = self.engine.recycle(request)
        self.assertEqual((result['state'], result['entryCount'], result['totalBytes']), ('Completed', 1, 0))
        self.assertTrue(result['captureVerified']); self.assertFalse(self.source.exists()); self.assertEqual(list(self.private(request).iterdir()), [])
        restore = restoring.restoration_type(unpacking.FileTreeUnpacking)(driver, self.captures, tree_module.FileTreeManifest, codec.TreeArchive)
        command = {'source': request['plan']['source'], 'target': {'rootId': 'test.shared', 'path': self.source.name}, 'format': 'tar.gz',
            'captureId': request['operationId'], 'captureOwner': request['owner'], 'captureKind': 'Trash', 'manifestSha256': request['plan']['manifestSha256']}
        intent = {'operationId': uuid.uuid4().hex, 'owner': 'B' * 64, 'plan': restore.plan(command)}
        self.assertEqual(restore.restore(intent)['state'], 'Completed'); self.assertEqual(list(self.source.iterdir()), [])

    def test_revoked_grant_during_retention_is_durable_unknown_without_more_source_changes(self):
        request = self.request()
        def revoke(phase):
            if phase == 'SourceRetained':
                values = json.loads(self.registry.read_bytes()); values[0]['allowedActions'].remove('files-tree-trash'); self.registry.write_text(json.dumps(values))
        self.engine.checkpoint = revoke
        with self.assertRaisesRegex(driver.Rejected, '^FILES_ROOT_FORBIDDEN$'): self.engine.recycle(request)
        self.assertEqual(self.lookup(request)['state'], 'Unknown'); self.assertTrue(self.source.exists())
        self.assertEqual((self.private(request) / self.file.relative_to(self.source)).read_bytes(), self.data)
        saved = json.loads(self.receipt(request).read_bytes()); self.assertEqual(saved['state'], 'Unknown')

    def test_no_dedicated_grant_or_invalid_source_never_starts_capture(self):
        before = self.source_proof(); values = json.loads(self.registry.read_bytes()); values[0]['allowedActions'].remove('files-tree-trash'); self.registry.write_text(json.dumps(values))
        with self.assertRaisesRegex(driver.Rejected, '^FILES_ROOT_FORBIDDEN$'): self.request()
        self.assertEqual(self.source_proof(), before)
        for path in ('', '../other', '/absolute', '.env'):
            with self.subTest(path=path), self.assertRaises(driver.Rejected):
                self.engine.plan({'rootId': 'test.shared', 'path': path, 'expectedVersion': 'A' * 64})
        self.assertFalse((self.fixture.backups.target / self.captures.FOLDER).exists())

    def test_changed_descendant_before_execution_never_captures_or_removes_source(self):
        request = self.request(); self.file.write_bytes(b'changed')
        with self.assertRaises(driver.Rejected): self.engine.recycle(request)
        self.assertTrue(self.source.exists()); self.assertEqual(self.file.read_bytes(), b'changed'); self.assertFalse(self.archive(request).exists())

    def test_hdd_failure_and_cancel_during_capture_leave_original_tree_unchanged(self):
        request = self.request(); before = self.source_proof()
        with patch.object(codec.TreeArchive, 'create', side_effect=driver.Rejected('FILES_BACKUP_UNAVAILABLE')):
            with self.assertRaises(driver.Rejected): self.engine.recycle(request)
        result = self.lookup(request); self.assertEqual(result['state'], 'Aborted'); self.assertFalse(result['sourceMayHaveChanged'])
        self.assertEqual(self.source_proof(), before)
        second = self.request()
        self.engine.checkpoint = lambda phase: setattr(self.engine, 'cancelled', lambda: True) if phase == 'Capturing' else None
        with self.assertRaisesRegex(driver.Rejected, '^FILES_TREE_CANCELED$'): self.engine.recycle(second)
        self.assertEqual(self.source_proof(), before)

    def test_late_protected_and_ordinary_members_are_never_carried_by_an_ancestor(self):
        for index, name in enumerate(('.env', 'late.txt', '代码/.env')):
            with self.subTest(name=name):
                if index: self.make_source('late-' + str(index))
                request = self.request()
                self.engine.checkpoint = lambda phase: (self.source / name).write_bytes(b'late-must-stay') if phase == 'SourceValidated' else None
                with self.assertRaises(OSError): self.engine.recycle(request)
                self.assertEqual((self.source / name).read_bytes(), b'late-must-stay')
                self.assertEqual(self.lookup(request)['state'], 'Unknown')
                self.assertEqual((self.private(request) / self.file.relative_to(self.source)).read_bytes(), self.data)

    def test_parent_path_replacement_before_retention_does_not_mutate_detached_tree(self):
        parent = self.root / 'parent'; parent.mkdir(); self.source.rename(parent / self.source.name)
        self.source = parent / self.source.name; self.file = self.source / '代码' / self.file.name
        request = self.request(); before = self.source_proof()
        def replace(phase):
            if phase == 'SourceValidated': parent.rename(self.root / 'detached'); parent.mkdir()
        self.engine.checkpoint = replace
        with self.assertRaises(driver.Rejected): self.engine.recycle(request)
        detached = self.root / 'detached' / self.source.name
        self.assertEqual((driver.publication_identity(detached.stat()), driver.publication_identity((detached / '代码' / self.file.name).stat()), (detached / '代码' / self.file.name).read_bytes()), before)
        self.assertEqual(self.lookup(request)['state'], 'Aborted')

    def test_wrong_owner_rebound_plan_and_explicit_discard_cannot_reuse_operation(self):
        request = self.request(); before = self.source_proof(); result = self.engine.discard(request)
        self.assertEqual(result['state'], 'Aborted'); self.assertEqual(self.engine.recycle(request), result)
        with self.assertRaisesRegex(driver.Rejected, '^FILES_OPERATION_NOT_FOUND$'): self.engine.recycle({**request, 'owner': 'B' * 64})
        changed = {**request['plan'], 'totalBytes': 0}; changed['fingerprint'] = self.engine.digest({key: value for key, value in changed.items() if key != 'fingerprint'})
        with self.assertRaisesRegex(driver.Rejected, '^FILES_IDEMPOTENCY_CONFLICT$'): self.engine.recycle({**request, 'plan': changed})
        self.assertEqual(self.source_proof(), before); self.assertFalse(self.archive(request).exists())

    def test_capture_corruption_before_source_mutation_aborts_without_touching_original(self):
        request = self.request(); before = self.source_proof()
        self.engine.checkpoint = lambda phase: self.archive(request).write_bytes(b'broken-archive') if phase == 'CaptureVerified' else None
        with self.assertRaises(driver.Rejected): self.engine.recycle(request)
        self.assertEqual(self.lookup(request)['state'], 'Aborted'); self.assertEqual(self.source_proof(), before)
        self.assertEqual(self.archive(request).read_bytes(), b'broken-archive'); self.assertFalse(self.private(request).exists())

    def test_capture_corruption_after_source_absent_preserves_full_original_inodes_and_unknown(self):
        request = self.request(); identity = driver.publication_identity(self.file.stat())
        self.engine.checkpoint = lambda phase: self.archive(request).write_bytes(b'broken-archive') if phase == 'SourceAbsent' else None
        with self.assertRaises(driver.Rejected): self.engine.recycle(request)
        self.assertEqual(self.lookup(request)['state'], 'Unknown'); self.assertFalse(self.source.exists())
        retained = self.private(request) / self.file.relative_to(self.source)
        self.assertEqual(retained.read_bytes(), self.data); self.assertEqual(driver.publication_identity(retained.stat()), identity)
        self.assertEqual(self.archive(request).read_bytes(), b'broken-archive')

    def test_late_leaf_replacement_is_retained_as_unknown_never_unlinked_or_adopted(self):
        request = self.request(); renamed = self.source / 'original-preserved'; fired = False
        def replace(phase):
            nonlocal fired
            if phase == 'SourceRetaining' and not fired:
                fired = True; self.file.rename(renamed); self.file.write_bytes(b'foreign-replacement')
        self.engine.checkpoint = replace
        with self.assertRaises(driver.Rejected): self.engine.recycle(request)
        self.assertEqual(self.lookup(request)['state'], 'Unknown'); self.assertEqual(renamed.read_bytes(), self.data)
        self.assertEqual((self.private(request) / self.file.relative_to(self.source)).read_bytes(), b'foreign-replacement')

    def test_existing_private_object_is_never_overwritten_adopted_or_removed(self):
        request = self.request(); before = self.source_proof()
        def collide(phase):
            if phase == 'SourceValidated':
                descriptor = os.open(self.root, os.O_RDONLY | os.O_DIRECTORY)
                try:
                    private = driver.staging(descriptor); os.close(private)
                finally: os.close(descriptor)
                self.private(request).mkdir(mode=0o700); (self.private(request) / 'keep').write_bytes(b'foreign')
        self.engine.checkpoint = collide
        with self.assertRaises(FileExistsError): self.engine.recycle(request)
        self.assertEqual(self.lookup(request)['state'], 'Unknown'); self.assertEqual(self.source_proof(), before)
        self.assertEqual((self.private(request) / 'keep').read_bytes(), b'foreign')

    def test_renamed_private_base_is_unknown_and_never_claims_originals_are_at_the_old_path(self):
        request = self.request(); moved = self.root / 'private-relocated'; fired = False
        def rename(phase):
            nonlocal fired
            if phase == 'SourceRetained' and not fired:
                fired = True; self.private(request).parent.rename(moved)
        self.engine.checkpoint = rename
        with self.assertRaises(OSError): self.engine.recycle(request)
        self.assertEqual(self.lookup(request)['state'], 'Unknown'); self.assertTrue(self.source.exists())
        self.assertFalse(self.private(request).exists())
        self.assertEqual((moved / (request['operationId'] + self.engine.SUFFIX) / self.file.relative_to(self.source)).read_bytes(), self.data)

    def test_namespace_and_operation_lock_contention_do_not_start_or_falsify_receipt(self):
        request = self.request(); before = self.source_proof()
        with self.engine.state(request['operationId'], request['owner'], True):
            with self.assertRaisesRegex(driver.Rejected, '^FILES_OPERATION_BUSY$'): self.engine.recycle(request)
        self.assertFalse(self.receipt(request).exists()); self.assertFalse(self.archive(request).exists())
        with driver.namespace_lock():
            with self.assertRaisesRegex(driver.Rejected, '^FILES_OPERATION_BUSY$'): self.engine.recycle(request)
        self.assertFalse(self.receipt(request).exists()); self.assertEqual(self.source_proof(), before)

    def test_malformed_or_inconsistent_receipt_is_preserved_and_does_not_capture(self):
        request = self.request(); self.engine.discard(request); original = self.receipt(request).read_bytes(); before = self.source_proof()
        for fields in ({'state': 'Completed'}, {'captureVerified': True}, {'sourceMutationStarted': True}, {'state': 'Unknown'}, {'retainedIdentity': [1, 1]}):
            with self.subTest(fields=fields):
                saved = json.loads(original); saved.update(fields); bad = json.dumps(saved).encode(); self.receipt(request).write_bytes(bad)
                with self.assertRaisesRegex(driver.Rejected, '^FILES_RECEIPT_UNSAFE$'): self.lookup(request)
                self.assertEqual(self.receipt(request).read_bytes(), bad); self.assertEqual(self.source_proof(), before)
                self.assertFalse(self.archive(request).exists())
        self.receipt(request).write_bytes(original)

    def test_corrupt_manifest_after_actual_kill_is_unknown_without_retention_replay(self):
        request = self.request(); pid = os.fork()
        if pid == 0:
            self.engine.checkpoint = lambda phase: os.kill(os.getpid(), signal.SIGKILL) if phase == 'SourceAbsent' else None
            try: self.engine.recycle(request)
            finally: os._exit(90)
        _, status = os.waitpid(pid, 0); self.assertTrue(os.WIFSIGNALED(status))
        manifest = self.receipt(request).with_name(request['operationId'] + '.manifest.json')
        saved = json.loads(manifest.read_bytes()); saved['totalBytes'] += 1; bad = json.dumps(saved).encode(); manifest.write_bytes(bad)
        with patch.object(self.engine, 'retain', side_effect=AssertionError('no retention replay')), patch.object(codec.TreeArchive, 'create', side_effect=AssertionError('no recapture')):
            self.assertEqual(self.lookup(request)['state'], 'Unknown')
        self.assertEqual(manifest.read_bytes(), bad); self.assertEqual((self.private(request) / self.file.relative_to(self.source)).read_bytes(), self.data)

    def test_more_than_one_gib_recycles_and_restores_all_bytes_and_retains_original_inode(self):
        block = bytes(range(256)) * 256; size = 1024**3 + 7; remaining = size; expected = hashlib.sha256()
        with self.file.open('wb') as output:
            while remaining:
                piece = block[:min(remaining, len(block))]; output.write(piece); expected.update(piece); remaining -= len(piece)
        inode = self.file.stat().st_ino; request = self.request(); result = self.engine.recycle(request)
        self.assertEqual(result['state'], 'Completed'); self.assertEqual(result['totalBytes'], size); self.assertFalse(self.source.exists())
        retained = self.private(request) / self.file.relative_to(self.source); self.assertEqual(retained.stat().st_ino, inode)
        restore = restoring.restoration_type(unpacking.FileTreeUnpacking)(driver, self.captures, tree_module.FileTreeManifest, codec.TreeArchive)
        command = {'source': request['plan']['source'], 'target': {'rootId': 'test.shared', 'path': self.source.name}, 'format': 'tar.gz',
            'captureId': request['operationId'], 'captureOwner': request['owner'], 'captureKind': 'Trash', 'manifestSha256': request['plan']['manifestSha256']}
        intent = {'operationId': uuid.uuid4().hex, 'owner': 'B' * 64, 'plan': restore.plan(command)}
        self.assertEqual(restore.restore(intent)['state'], 'Completed'); self.assertNotEqual(self.file.stat().st_ino, inode)
        for path in (retained, self.file):
            actual = hashlib.sha256(); count = 0
            with path.open('rb') as stream:
                while piece := stream.read(1024 * 1024): actual.update(piece); count += len(piece)
            self.assertEqual((count, actual.digest()), (size, expected.digest()))

    def test_real_sigkill_seven_fences_never_recaptures_or_resumes_retention(self):
        for index, phase in enumerate(('Preparing', 'CaptureVerified', 'SourcePrepared', 'SourceRetaining', 'SourceRetained', 'SourceRemoving', 'SourceAbsent')):
            with self.subTest(phase=phase):
                if index: self.make_source('kill-' + phase)
                request = self.request(); pid = os.fork()
                if pid == 0:
                    self.engine.checkpoint = lambda value: os.kill(os.getpid(), signal.SIGKILL) if value == phase else None
                    try: self.engine.recycle(request)
                    finally: os._exit(90)
                _, status = os.waitpid(pid, 0); self.assertTrue(os.WIFSIGNALED(status)); self.assertEqual(os.WTERMSIG(status), signal.SIGKILL)
                with patch.object(codec.TreeArchive, 'create', side_effect=AssertionError('no recapture')), patch.object(self.engine, 'retain', side_effect=AssertionError('no retention replay')):
                    result = self.lookup(request); self.assertEqual(self.engine.recycle(request), result)
                if phase == 'SourceAbsent': self.assertEqual(result['state'], 'Completed'); self.assertFalse(self.source.exists())
                elif phase in ('Preparing', 'CaptureVerified'): self.assertEqual(result['state'], 'Aborted'); self.assertEqual(self.file.read_bytes(), self.data)
                else: self.assertEqual(result['state'], 'Unknown')


if __name__ == '__main__': unittest.main()
