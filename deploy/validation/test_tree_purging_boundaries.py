"""Additional real temporary Linux I/O/crash boundaries, not deployed acceptance."""
import hashlib
import json
import os
from pathlib import Path
import tarfile
import unittest
from unittest.mock import patch
import uuid
import test_tree_purging as existing

driver = existing.driver


@unittest.skipUnless(os.name == 'posix' and os.geteuid() == 0, 'requires isolated Linux root I/O')
class TreePurgeBoundaryTests(unittest.TestCase):
    captured = existing.TreePurgingTests.captured
    request = staticmethod(existing.TreePurgingTests.request)
    lookup = staticmethod(existing.TreePurgingTests.lookup)
    proof = staticmethod(existing.TreePurgingTests.proof)
    tree = existing.TreePurgingTests.tree

    def test_separate_components_can_be_explicitly_purged_in_either_order(self):
        for kind in ('Trash', 'Overwrite'):
            for order in (('Archive', 'Originals'), ('Originals', 'Archive')):
                with self.subTest(kind=kind, order=order), self.captured(kind) as fixture:
                    first = self.request(fixture, order[0]); second_stale = self.request(fixture, order[1])
                    self.assertEqual(fixture.engine.purge(first)['state'], 'Completed')
                    with self.assertRaises(driver.Rejected): fixture.engine.purge(second_stale)
                    second = self.request(fixture, order[1]); self.assertEqual(fixture.engine.purge(second)['state'], 'Completed')
                    self.assertFalse(fixture.retained.exists()); self.assertFalse(fixture.archive.exists()); self.assertTrue(fixture.origin.exists())
                    for scope in ('Archive', 'Originals', 'All'):
                        with self.assertRaisesRegex(driver.Rejected, '^FILES_TREE_PURGE_ALREADY_REMOVED$'): self.request(fixture, scope)

    def test_additive_purge_grant_accepts_real_old_capture_without_rewriting_origin(self):
        for kind in ('Trash', 'Overwrite'):
            with self.subTest(kind=kind), self.captured(kind, grant_before_capture=False) as fixture:
                before = fixture.origin.read_bytes()
                self.assertEqual(fixture.engine.purge(self.request(fixture))['state'], 'Completed')
                self.assertEqual(before, fixture.origin.read_bytes())

    def test_archive_only_works_after_historical_parent_and_originals_are_gone(self):
        def nested(case):
            relative = case.file.relative_to(case.source); parent = case.root / '历史父目录'; parent.mkdir()
            target = parent / case.source.name; case.source.rename(target); case.source = target; case.file = target / relative
        with self.captured(prepare=nested) as fixture:
            # Deliberate test setup, not production cleanup: explicitly unlink
            # these three known payload objects and their known empty dirs.
            (fixture.retained / fixture.member).unlink()
            (fixture.retained / '代码').rmdir(); (fixture.retained / '空目录').rmdir(); fixture.retained.rmdir()
            staging = fixture.retained.parent; staging.rmdir(); staging.parent.rmdir()
            request = self.request(fixture, 'Archive'); self.assertIsNone(request['plan']['sourceParent'])
            self.assertEqual(fixture.engine.purge(request)['state'], 'Completed'); self.assertFalse(fixture.archive.exists())

    def test_permission_revoked_at_last_unlink_boundary_retains_bytes_and_never_resumes(self):
        for scope, phase in (('Originals', 'BeforeOriginalUnlink'), ('Archive', 'BeforeArchiveUnlink')):
            with self.subTest(scope=scope), self.captured() as fixture:
                request = self.request(fixture, scope); before = self.proof(fixture.retained / fixture.member)
                def revoke(value):
                    if value == phase:
                        policy = json.loads(fixture.registry.read_bytes()); policy[0]['allowedActions'].remove('files-tree-purge')
                        fixture.registry.write_text(json.dumps(policy))
                fixture.engine.checkpoint = revoke
                with self.assertRaisesRegex(driver.Rejected, '^FILES_ROOT_FORBIDDEN$'): fixture.engine.purge(request)
                self.assertEqual(self.lookup(fixture, request)['state'], 'Unknown')
                root = fixture.retained.parent / (request['operationId'] + fixture.engine.SUFFIX) if scope == 'Originals' else fixture.retained
                self.assertEqual(before, self.proof(root / fixture.member))
                self.assertEqual(fixture.engine.purge(request)['state'], 'Unknown')

    def test_swapped_private_namespace_is_not_deleted_through_a_detached_fd(self):
        with self.captured() as fixture:
            request = self.request(fixture, 'Originals'); staging = fixture.retained.parent; displaced = staging.with_name('displaced-private')
            old = self.proof(fixture.retained / fixture.member); foreign = staging / (request['operationId'] + fixture.engine.SUFFIX)
            def replace(phase):
                if phase == 'BeforeOriginalUnlink':
                    staging.rename(displaced); staging.mkdir(mode=0o700); foreign.mkdir(mode=0o700); (foreign / 'foreign').write_bytes(b'do not delete')
            fixture.engine.checkpoint = replace
            with self.assertRaisesRegex(driver.Rejected, '^FILES_STAGING_UNSAFE$'): fixture.engine.purge(request)
            self.assertEqual((foreign / 'foreign').read_bytes(), b'do not delete')
            self.assertEqual(old, self.proof(displaced / foreign.name / fixture.member)); self.assertTrue(fixture.archive.exists())
            self.assertEqual(self.lookup(fixture, request)['state'], 'Unknown')

    def test_relaxed_private_directory_owner_or_mode_at_unlink_boundary_stops_deletion(self):
        for change in ('mode', 'owner'):
            with self.subTest(change=change), self.captured() as fixture:
                request = self.request(fixture, 'Originals'); before = self.proof(fixture.retained / fixture.member)
                staging = fixture.retained.parent
                def loosen(phase):
                    if phase == 'BeforeOriginalUnlink':
                        if change == 'mode': staging.chmod(0o777)
                        else: os.chown(staging, 1001, 1001)
                fixture.engine.checkpoint = loosen
                with self.assertRaisesRegex(driver.Rejected, '^FILES_STAGING_UNSAFE$'): fixture.engine.purge(request)
                retained = staging / (request['operationId'] + fixture.engine.SUFFIX)
                self.assertEqual(before, self.proof(retained / fixture.member)); self.assertTrue(fixture.archive.exists())
                self.assertEqual(self.lookup(fixture, request)['state'], 'Unknown')

    def test_swapped_hdd_store_is_not_deleted_through_a_detached_fd(self):
        with self.captured() as fixture:
            request = self.request(fixture, 'Archive'); displaced = fixture.store.with_name('displaced-store')
            archive = self.proof(fixture.archive); name = request['operationId'] + '.purging.tar.gz'
            def replace(phase):
                if phase == 'BeforeArchiveUnlink':
                    fixture.store.rename(displaced); fixture.store.mkdir(mode=0o700)
                    (fixture.store / name).write_bytes(b'foreign archive')
            fixture.engine.checkpoint = replace
            with self.assertRaisesRegex(driver.Rejected, '^FILES_TREE_BACKUP_STORE_UNSAFE$'): fixture.engine.purge(request)
            self.assertEqual((fixture.store / name).read_bytes(), b'foreign archive')
            self.assertEqual(archive, self.proof(displaced / name)); self.assertTrue(fixture.retained.exists())
            self.assertEqual(self.lookup(fixture, request)['state'], 'Unknown')

    def test_explicit_archive_purge_releases_live_version_slot_without_recreating_old_payload(self):
        with self.captured() as fixture:
            request = self.request(fixture, 'Archive'); self.assertEqual(fixture.engine.purge(request)['state'], 'Completed')
            configuration = fixture.engine.captures.b.configuration(); configuration['maxVersions'] = 1
            fixture.case.make_source('新目录'); source = fixture.case.request()['plan']['source']
            manifest = fixture.engine.manifest_type(driver).scan(source)
            capture = {'operationId': uuid.uuid4().hex, 'owner': 'D' * 64, 'source': source,
                'manifestSha256': manifest['manifestSha256'], 'captureKind': 'Trash'}
            with patch.object(fixture.engine.captures.b, 'configuration', return_value=configuration):
                self.assertEqual(fixture.engine.captures.ensure(capture)['state'], 'Verified')
            with self.assertRaisesRegex(driver.Rejected, '^FILES_TREE_BACKUP_INCOMPLETE$'):
                fixture.engine.captures.ensure(fixture.engine.capture_request(fixture.command))
            self.assertFalse(fixture.archive.exists()); self.assertTrue(fixture.origin.exists())

    def test_originals_only_rechecks_a_still_verified_hdd_copy_before_destroying_originals(self):
        with self.captured() as fixture:
            request = self.request(fixture, 'Originals'); before = self.tree(fixture.retained)
            with fixture.archive.open('r+b') as stream: stream.seek(-8, 2); stream.write(b'corrupt!')
            with self.assertRaises(driver.Rejected): fixture.engine.purge(request)
            self.assertEqual(before, self.tree(fixture.retained)); self.assertTrue(fixture.archive.exists())

    def test_malformed_or_contradictory_purge_tombstone_does_not_release_capacity(self):
        with self.captured() as fixture:
            self.assertEqual(fixture.engine.purge(self.request(fixture, 'Archive'))['state'], 'Completed')
            path = fixture.archive.with_suffix('').with_suffix('.json'); original = path.read_bytes(); value = json.loads(original)
            descriptor = os.open(fixture.store, os.O_RDONLY | os.O_DIRECTORY)
            try:
                self.assertTrue(fixture.engine.captures.purged_payload(descriptor, fixture.command['captureId']))
                for key, changed in (('schemaVersion', 2), ('kind', 'File'), ('captureKind', 'unknown'), ('owner', 'invalid'), ('storageHash', 'invalid'), ('archivePurgedBy', '0' * 32)):
                    with self.subTest(field=key):
                        path.write_bytes(fixture.engine.encode({**value, key: changed}))
                        self.assertFalse(fixture.engine.captures.purged_payload(descriptor, fixture.command['captureId']))
                        path.write_bytes(original)
                fixture.archive.write_bytes(b'new payload, not absent')
                self.assertFalse(fixture.engine.captures.purged_payload(descriptor, fixture.command['captureId']))
            finally: os.close(descriptor)

    def test_recreated_selected_path_before_final_receipt_is_unknown_and_not_deleted_again(self):
        for scope in ('Archive', 'Originals'):
            with self.subTest(scope=scope), self.captured() as fixture:
                request = self.request(fixture, scope); foreign = fixture.archive if scope == 'Archive' else fixture.retained
                def recreate(phase):
                    if phase == 'Completing':
                        if scope == 'Archive': foreign.write_bytes(b'new unrelated payload')
                        else: foreign.mkdir(mode=0o700); (foreign / 'foreign').write_bytes(b'new unrelated payload')
                fixture.engine.checkpoint = recreate
                with self.assertRaisesRegex(driver.Rejected, '^FILES_TREE_CHANGED$'): fixture.engine.purge(request)
                self.assertEqual(self.lookup(fixture, request)['state'], 'Unknown')
                target = foreign if scope == 'Archive' else foreign / 'foreign'
                self.assertEqual(target.read_bytes(), b'new unrelated payload')
                self.assertEqual(fixture.engine.purge(request)['state'], 'Unknown'); self.assertTrue(target.exists())

    def test_one_gib_plus_seven_complete_archive_proof_then_exact_all_payload_purge(self):
        size = 1024**3 + 7; expected = hashlib.sha256(); block = bytes(range(256)) * 4096
        def large(case):
            with case.file.open('wb') as output:
                for _ in range(1024): output.write(block); expected.update(block)
                tail = b'\x00tail\xff\n'; output.write(tail); expected.update(tail); output.flush(); os.fsync(output.fileno())
            os.utime(case.file, ns=(1712345678123456789, 1712345678123456789))
        with self.captured(prepare=large) as fixture:
            original = self.proof(fixture.retained / fixture.member)
            self.assertEqual(original[6], size); self.assertEqual(original[-1], expected.hexdigest().upper())
            with tarfile.open(fixture.archive, 'r:gz') as archive:
                with archive.extractfile(str(fixture.member)) as stream:
                    self.assertEqual(hashlib.file_digest(stream, 'sha256').hexdigest(), expected.hexdigest())
            request = self.request(fixture); self.assertEqual(request['plan']['totalBytes'], size)
            self.assertEqual(fixture.engine.purge(request)['state'], 'Completed')
            self.assertFalse(fixture.retained.exists()); self.assertFalse(fixture.archive.exists())
            self.assertTrue(fixture.origin.exists()); self.assertTrue(fixture.archive.with_suffix('').with_suffix('.json').exists())


if __name__ == '__main__': unittest.main()
