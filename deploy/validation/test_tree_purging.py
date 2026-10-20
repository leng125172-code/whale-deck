"""Actual isolated Linux deletion tests; no production purge authorization claim.

Only TemporaryDirectory-owned fixtures are destroyed. Backup mount identity is
an explicit fixture, not physical HDD/Agent/RPC/BFF or browser acceptance.
"""
import contextlib
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import signal
import stat
from types import SimpleNamespace
import unittest
from unittest.mock import patch
import uuid
import test_tree_recycling as recycling_tests
import test_tree_overwrite_restoring as overwrite_tests

driver = recycling_tests.driver
spec = importlib.util.spec_from_file_location('tree_purging_tests', Path(__file__).resolve().parents[1] / 'host/whaledeck-tree-purging.py')
purging = importlib.util.module_from_spec(spec); spec.loader.exec_module(purging)


@unittest.skipUnless(os.name == 'posix' and os.geteuid() == 0, 'requires real isolated Linux root I/O')
class TreePurgingTests(unittest.TestCase):
    @staticmethod
    def proof(path):
        info = path.lstat(); digest = None
        if stat.S_ISREG(info.st_mode):
            with path.open('rb') as stream: digest = hashlib.file_digest(stream, 'sha256').hexdigest().upper()
        return (*driver.publication_identity(info), digest)

    @classmethod
    def tree(cls, path):
        if not os.path.lexists(path): return None
        return {'': cls.proof(path), **{str(item.relative_to(path)): cls.proof(item) for item in path.rglob('*')}}

    @contextlib.contextmanager
    def captured(self, kind='Trash', *, grant_before_capture=True, prepare=None):
        # Grant the new action before ANY real capture is created, including
        # Overwrite's historical input capture. This is not a policy bypass.
        original_setup = recycling_tests.common.FileTransportTests.setUp
        def initialize(fixture):
            original_setup(fixture); policies = json.loads(fixture.registry.read_bytes())
            if grant_before_capture: policies[0]['allowedActions'].append('files-tree-purge')
            fixture.registry.write_text(json.dumps(policies))
        case = recycling_tests.TreeRecyclingTests() if kind == 'Trash' else overwrite_tests.TreeOverwriteRestoringTests()
        self.addCleanup(case.doCleanups)
        try:
            with patch.object(recycling_tests.common.FileTransportTests, 'setUp', initialize): case.setUp()
            if prepare is not None: prepare(case)
            original = case.request()
            if kind == 'Trash':
                self.assertEqual(case.engine.recycle(original)['state'], 'Completed')
                captures, manifest_type, codec_type, io_type = case.captures, case.engine.manifest_type, case.engine.codec_type, case.engine.io
                store, retained = case.archive(original).parent, case.private(original)
                source, manifest = original['plan']['source'], original['plan']['manifestSha256']
                registry = case.registry; member = case.file.relative_to(case.source); visible = case.source
            else:
                self.assertEqual(case.engine.restore(original)['state'], 'Completed')
                captures, manifest_type, codec_type, io_type = case.engine.captures, case.engine.manifest_type, case.engine.codec_type, recycling_tests.moves.FileTreeMoves
                store, retained = case.fixture.store, case.retained(original)
                source, manifest = original['plan']['command']['target'], original['plan']['targetManifestSha256']
                registry = case.fixture.registry; member = Path(case.old_file.name); visible = case.target
            if not grant_before_capture:
                policies = json.loads(registry.read_bytes()); policies[0]['allowedActions'].append('files-tree-purge'); registry.write_text(json.dumps(policies))
            engine = purging.purging_type(recycling_tests.recycling.FileTreeRecycling)(driver, captures, manifest_type, codec_type, io_type,
                origins={'Trash': case.engine, 'Overwrite': case.engine})
            command = {'source': source, 'captureId': original['operationId'], 'captureOwner': original['owner'],
                'captureKind': kind, 'manifestSha256': manifest, 'scope': 'All'}
            yield SimpleNamespace(case=case, engine=engine, command=command, root=case.root, state=case.state, store=store, registry=registry,
                retained=retained, member=member, visible=visible, archive=store / (original['operationId'] + '.tar.gz'),
                origin=case.state / case.engine.FOLDER / (original['operationId'] + '.json'))
        finally: case.doCleanups()

    @staticmethod
    def request(fixture, scope='All'):
        return {'operationId': uuid.uuid4().hex, 'owner': 'E' * 64, 'plan': fixture.engine.plan({**fixture.command, 'scope': scope})}

    @staticmethod
    def lookup(fixture, request):
        return fixture.engine.lookup({key: request[key] for key in ('operationId', 'owner')})

    def test_plans_are_read_only_for_all_three_scopes_and_both_real_capture_kinds(self):
        for kind in ('Trash', 'Overwrite'):
            with self.subTest(kind=kind), self.captured(kind) as fixture:
                before = (self.tree(fixture.retained), self.tree(fixture.store), self.tree(fixture.visible), fixture.origin.read_bytes())
                for scope in ('Archive', 'Originals', 'All'):
                    plan = self.request(fixture, scope)['plan']
                    self.assertEqual(plan['command']['scope'], scope)
                    self.assertGreater(plan['totalBytes'], 2 * 1024**2)
                    if scope == 'Archive': self.assertIsNone(plan['retainedIdentity'])
                    else: self.assertEqual(tuple(plan['retainedIdentity']), self.proof(fixture.retained)[:2])
                    self.assertEqual(before, (self.tree(fixture.retained), self.tree(fixture.store), self.tree(fixture.visible), fixture.origin.read_bytes()))
                self.assertFalse((fixture.state / fixture.engine.FOLDER).exists())

    def test_scoped_purge_only_removes_requested_payload_and_retains_manifest_origin_and_metadata(self):
        for kind in ('Trash', 'Overwrite'):
            for scope in ('Archive', 'Originals', 'All'):
                with self.subTest(kind=kind, scope=scope), self.captured(kind) as fixture:
                    request = self.request(fixture, scope); retained, archive = self.tree(fixture.retained), self.proof(fixture.archive)
                    visible, origin = self.tree(fixture.visible), fixture.origin.read_bytes()
                    metadata = fixture.store / (fixture.command['captureId'] + '.json')
                    before = json.loads(metadata.read_bytes()); manifest = fixture.store / (fixture.command['captureId'] + '.manifest.json')
                    manifest_proof = self.proof(manifest)
                    result = fixture.engine.purge(request)
                    self.assertEqual((result['state'], result['kind'], result['scope']), ('Completed', 'DirectoryPurge', scope))
                    self.assertEqual(result['captureId'], fixture.command['captureId']); self.assertNotEqual(result['operationId'], result['captureId'])
                    self.assertTrue(result['deletionStarted']); self.assertEqual(result['archiveRemoved'], scope in ('Archive', 'All'))
                    self.assertEqual(result['originalsRemoved'], scope in ('Originals', 'All'))
                    if scope in ('Archive', 'All'): self.assertFalse(os.path.lexists(fixture.archive))
                    else: self.assertEqual(archive, self.proof(fixture.archive))
                    if scope in ('Originals', 'All'): self.assertFalse(os.path.lexists(fixture.retained))
                    else: self.assertEqual(retained, self.tree(fixture.retained))
                    saved = json.loads(metadata.read_bytes())
                    self.assertEqual(saved['state'], 'Purged' if scope in ('Archive', 'All') else 'Verified')
                    if scope in ('Originals', 'All'):
                        self.assertTrue(saved['originalsPurged']); self.assertEqual(saved['originalsPurgedBy'], request['operationId'])
                    if scope in ('Archive', 'All'): self.assertEqual(saved['archivePurgedBy'], request['operationId'])
                    for key in ('operationId', 'owner', 'source', 'manifestSha256', 'manifestFileSha256', 'archiveIdentity', 'archiveSha256', 'archiveBytes', 'contentSha256'):
                        self.assertEqual(saved[key], before[key])
                    self.assertEqual(manifest_proof, self.proof(manifest)); self.assertEqual(origin, fixture.origin.read_bytes())
                    self.assertEqual(visible, self.tree(fixture.visible))
                    with patch.object(fixture.engine, 'remove_originals', side_effect=AssertionError('no repeated original deletion')), patch.object(fixture.engine, 'remove_archive', side_effect=AssertionError('no repeated archive deletion')):
                        self.assertEqual(fixture.engine.purge(request), result)
                        self.assertEqual(self.lookup(fixture, request), result); self.assertEqual(fixture.engine.discard(request), result)

    def test_all_keeps_full_hdd_archive_through_each_original_file_unlink(self):
        with self.captured() as fixture:
            request = self.request(fixture); archive = self.proof(fixture.archive); seen = []
            def checkpoint(phase):
                if phase in ('BeforeOriginalUnlink', 'OriginalUnlinked'):
                    self.assertEqual(self.proof(fixture.archive), archive); seen.append(phase)
            fixture.engine.checkpoint = checkpoint
            self.assertEqual(fixture.engine.purge(request)['state'], 'Completed'); self.assertEqual(seen, ['BeforeOriginalUnlink', 'OriginalUnlinked'])

    def test_discard_before_start_permanently_fences_apply_without_deleting_any_payload(self):
        with self.captured() as fixture:
            request = self.request(fixture); before = (self.tree(fixture.retained), self.tree(fixture.store))
            result = fixture.engine.discard(request); self.assertEqual(result['state'], 'Aborted'); self.assertFalse(result['deletionStarted'])
            with patch.object(fixture.engine, 'remove_originals', side_effect=AssertionError('no apply after discard')), patch.object(fixture.engine, 'remove_archive', side_effect=AssertionError('no apply after discard')):
                self.assertEqual(fixture.engine.purge(request), result); self.assertEqual(self.lookup(fixture, request), result)
            self.assertEqual(before, (self.tree(fixture.retained), self.tree(fixture.store)))

    def test_cross_owner_and_changed_plan_are_rejected_without_effects(self):
        with self.captured() as fixture:
            request = self.request(fixture); before = (self.tree(fixture.retained), self.tree(fixture.store))
            fixture.engine.discard(request)
            with self.assertRaisesRegex(driver.Rejected, '^FILES_OPERATION_NOT_FOUND$'):
                fixture.engine.lookup({'operationId': request['operationId'], 'owner': 'F' * 64})
            with self.assertRaisesRegex(driver.Rejected, '^FILES_OPERATION_NOT_FOUND$'):
                fixture.engine.purge({**request, 'owner': 'F' * 64})
            with self.assertRaisesRegex(driver.Rejected, '^FILES_PLAN_INVALID$'):
                fixture.engine.purge({**request, 'plan': {**request['plan'], 'totalBytes': 1}})
            with self.assertRaisesRegex(driver.Rejected, '^FILES_OPERATION_NOT_FOUND$'):
                fixture.engine.plan({**fixture.command, 'captureOwner': 'F' * 64})
            self.assertEqual(before, (self.tree(fixture.retained), self.tree(fixture.store)))

    def test_no_grant_protected_path_and_caller_selected_private_path_never_authorize_purge(self):
        with self.captured() as fixture:
            before = (self.tree(fixture.retained), self.tree(fixture.store)); raw = fixture.registry.read_bytes(); values = json.loads(raw)
            values[0]['allowedActions'].remove('files-tree-purge'); fixture.registry.write_text(json.dumps(values))
            with self.assertRaisesRegex(driver.Rejected, '^FILES_ROOT_FORBIDDEN$'): self.request(fixture)
            fixture.registry.write_bytes(raw)
            for command in ({**fixture.command, 'source': {**fixture.command['source'], 'path': '.env'}},
                    {**fixture.command, 'privatePath': str(fixture.retained)}, {**fixture.command, 'scope': 'Everything'}):
                with self.subTest(command=tuple(command)), self.assertRaises(driver.Rejected): fixture.engine.plan(command)
            self.assertEqual(before, (self.tree(fixture.retained), self.tree(fixture.store)))

    def test_revoked_permission_after_plan_preserves_both_components(self):
        with self.captured() as fixture:
            request = self.request(fixture); before = (self.tree(fixture.retained), self.tree(fixture.store))
            values = json.loads(fixture.registry.read_bytes()); values[0]['allowedActions'].remove('files-tree-purge'); fixture.registry.write_text(json.dumps(values))
            with self.assertRaisesRegex(driver.Rejected, '^FILES_ROOT_FORBIDDEN$'): fixture.engine.purge(request)
            self.assertEqual(before, (self.tree(fixture.retained), self.tree(fixture.store)))

    def test_unknown_symlink_hardlink_and_protected_retained_members_are_never_deleted(self):
        for kind in ('Trash', 'Overwrite'):
            for change in ('unknown', 'symlink', 'hardlink', 'protected'):
                with self.subTest(kind=kind, change=change), self.captured(kind) as fixture:
                    request = self.request(fixture); archive = self.proof(fixture.archive)
                    outsider = fixture.root / 'outside'; outsider.write_bytes(b'do not delete')
                    name = '.env' if change == 'protected' else 'foreign'
                    path = fixture.retained / name
                    if change == 'symlink': path.symlink_to(outsider)
                    elif change == 'hardlink': os.link(outsider, path)
                    else: path.write_bytes(b'unknown file')
                    with self.assertRaises((driver.Rejected, OSError)): fixture.engine.purge(request)
                    self.assertTrue(os.path.lexists(path)); self.assertEqual(outsider.read_bytes(), b'do not delete')
                    self.assertEqual(archive, self.proof(fixture.archive)); self.assertTrue((fixture.retained / fixture.member).is_file())

    def test_unsafe_archive_symlink_or_hardlink_never_deletes_originals(self):
        for change in ('symlink', 'hardlink'):
            with self.subTest(change=change), self.captured() as fixture:
                request = self.request(fixture); retained = self.tree(fixture.retained); other = fixture.store / 'foreign-archive'
                if change == 'symlink': fixture.archive.rename(other); fixture.archive.symlink_to(other)
                else: os.link(fixture.archive, other)
                with self.assertRaises((driver.Rejected, OSError)): fixture.engine.purge(request)
                self.assertTrue(os.path.lexists(fixture.archive)); self.assertTrue(other.is_file()); self.assertEqual(retained, self.tree(fixture.retained))

    def test_cancellation_before_first_quarantine_is_aborted_and_preserves_complete_bytes(self):
        with self.captured() as fixture:
            request = self.request(fixture); before = (self.tree(fixture.retained), self.tree(fixture.store))
            fixture.engine.checkpoint = lambda phase: setattr(fixture.engine, 'cancelled', lambda: True) if phase == 'Preparing' else None
            with self.assertRaisesRegex(driver.Rejected, '^FILES_TREE_CANCELED$'): fixture.engine.purge(request)
            fixture.engine.cancelled = None
            self.assertEqual(self.lookup(fixture, request)['state'], 'Aborted'); self.assertEqual(before, (self.tree(fixture.retained), self.tree(fixture.store)))

    def test_cancellation_after_quarantine_is_unknown_and_never_resumes_deleting(self):
        for kind in ('Trash', 'Overwrite'):
            for scope, phase in (('Originals', 'OriginalsQuarantined'), ('Archive', 'ArchiveQuarantined')):
                with self.subTest(kind=kind, scope=scope), self.captured(kind) as fixture:
                    request = self.request(fixture, scope)
                    fixture.engine.checkpoint = lambda current: setattr(fixture.engine, 'cancelled', lambda: True) if current == phase else None
                    with self.assertRaisesRegex(driver.Rejected, '^FILES_TREE_CANCELED$'): fixture.engine.purge(request)
                    fixture.engine.cancelled = None; before = (self.tree(fixture.root), self.tree(fixture.store))
                    with patch.object(fixture.engine, 'remove_originals', side_effect=AssertionError('unknown must not resume')), patch.object(fixture.engine, 'remove_archive', side_effect=AssertionError('unknown must not resume')):
                        result = self.lookup(fixture, request); self.assertEqual(result['state'], 'Unknown')
                        self.assertTrue(result['deletionStarted']); self.assertEqual(fixture.engine.purge(request), result)
                        self.assertEqual(fixture.engine.discard(request), result)
                    self.assertEqual(before, (self.tree(fixture.root), self.tree(fixture.store)))

    def test_actual_sigkill_at_deletion_fences_only_observes_and_never_finishes_by_inference(self):
        for phase in ('Preparing', 'OriginalsQuarantined', 'OriginalUnlinked', 'ArchiveQuarantined', 'ArchiveUnlinked', 'Completing'):
            with self.subTest(phase=phase), self.captured() as fixture:
                request = self.request(fixture); child = os.fork()
                if child == 0:
                    fixture.engine.checkpoint = lambda current: os.kill(os.getpid(), signal.SIGKILL) if current == phase else None
                    try: fixture.engine.purge(request)
                    except BaseException: os._exit(2)
                    os._exit(3)
                _, status = os.waitpid(child, 0)
                self.assertTrue(os.WIFSIGNALED(status)); self.assertEqual(os.WTERMSIG(status), signal.SIGKILL)
                before = (self.tree(fixture.root), self.tree(fixture.store))
                with patch.object(fixture.engine, 'remove_originals', side_effect=AssertionError('must not resume after death')), patch.object(fixture.engine, 'remove_archive', side_effect=AssertionError('must not resume after death')):
                    result = self.lookup(fixture, request)
                    self.assertEqual(result['state'], 'Aborted' if phase == 'Preparing' else 'Unknown')
                    self.assertEqual(fixture.engine.purge(request), result); self.assertEqual(fixture.engine.discard(request), result)
                self.assertEqual(before, (self.tree(fixture.root), self.tree(fixture.store)))


if __name__ == '__main__': unittest.main()
