"""Real Linux file history/trash/restore/purge I/O and SIGKILL recovery.

The backup fixture explicitly synthesizes device identity; this is NOT physical
HDD, installed Agent, BFF/SQL/Worker or UI evidence. All files are disposable.
"""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import signal
import stat
import time
import unittest
import uuid
from unittest.mock import patch
import test_file_transport as common

spec = importlib.util.spec_from_file_location('file_archives', Path(__file__).resolve().parents[1] / 'host/whaledeck-file-archives.py')
archives = importlib.util.module_from_spec(spec); spec.loader.exec_module(archives)
driver = common.driver


@unittest.skipUnless(os.geteuid() == 0, 'requires real Linux root file semantics')
class FileArchiveTests(unittest.TestCase):
    tearDown = common.FileTransportTests.tearDown
    revision = common.FileTransportTests.revision

    def setUp(self):
        common.FileTransportTests.setUp(self)
        values = json.loads(self.registry.read_bytes()); values[0]['allowedActions'] += ['files-mutate', 'files-history', 'files-archive']
        self.registry.write_text(json.dumps(values)); self.engine = archives.FileArchives(driver, self.backups.engine)

    def capture(self, content=b'old\x00\xff\xe4\xb8\xad', name='source', operation=None):
        source = self.root / name; source.write_bytes(content); info = source.stat(); identifier = operation or uuid.uuid4().hex
        record = dict(operationId=identifier, owner='A' * 64, rootId='test.shared', path=name, expectedVersion=driver.version(info),
            originalIdentity=driver.publication_identity(info), originalSha256=hashlib.sha256(content).hexdigest().upper())
        parent = os.open(source.parent, os.O_RDONLY | os.O_DIRECTORY)
        try: self.backups.engine.ensure(record, parent, source.name)
        finally: os.close(parent)
        return identifier

    def command(self, kind='trash', path='source', version='', expected=None):
        if expected is None: expected = 'ABSENT' if kind == 'purge' or not (self.root / path).exists() else self.revision(path)
        return dict(kind=kind, rootId='test.shared', path=path, versionId=version, expectedVersion=expected)

    def request(self, command): return dict(operationId=uuid.uuid4().hex, owner='A' * 64, plan=self.engine.plan(command))
    def lookup(self, request): return self.engine.lookup({key: request[key] for key in ('operationId', 'owner')})
    def listing(self, after='', take=50, version='', path=''):
        return self.engine.list(dict(rootId='test.shared', path=path, after=after, take=take, expectedVersion=version))
    def metadata(self, identifier): return json.loads((self.backups.target / (identifier + '.json')).read_bytes())

    def test_legacy_overwrite_copies_are_listed_without_owner_or_absolute_path_leak(self):
        identifier = self.capture(); filename = self.backups.target / (identifier + '.json')
        value = self.metadata(identifier); value.pop('captureKind'); value.pop('captureState'); filename.write_text(json.dumps(value))
        result = self.listing(); self.assertTrue(result['complete']); self.assertEqual(len(result['entries']), 1)
        row = result['entries'][0]; self.assertEqual(row['versionId'], identifier); self.assertEqual(row['captureKind'], 'Overwrite')
        self.assertEqual(row['captureState'], 'Available'); self.assertEqual(row['availability'], 'Present')
        self.assertNotIn('owner', row); self.assertNotIn(str(self.backups.target), json.dumps(result))

    def test_trash_keeps_exact_source_and_hdd_bytes_and_never_repeats_the_removal(self):
        content = os.urandom(65543); (self.root / 'source').write_bytes(content); request = self.request(self.command())
        result = self.engine.apply(request); self.assertEqual(result['state'], 'Completed'); self.assertTrue(result['sourceRetained']); self.assertTrue(result['bindingCurrent'])
        self.assertFalse((self.root / 'source').exists())
        retained = self.root / '.whaledeck-files' / (request['operationId'] + '.archive')
        self.assertEqual(retained.read_bytes(), content); self.assertEqual((self.backups.target / (request['operationId'] + '.data')).read_bytes(), content)
        self.assertEqual(self.metadata(request['operationId'])['captureState'], 'Trashed')
        (self.root / 'source').write_bytes(b'external-new-file')
        replay = self.engine.apply(request); self.assertEqual(replay['state'], 'Completed'); self.assertFalse(replay['bindingCurrent'])
        self.assertEqual((self.root / 'source').read_bytes(), b'external-new-file')

    def test_trash_restore_absent_preserves_binary_mode_owner_and_mtime_but_never_special_bits(self):
        content = b'\xef\xbb\xbftext\r\n\x00\xff'; source = self.root / 'source'; source.write_bytes(content)
        os.chown(source, 1000, 1000); source.chmod(0o4750); before = source.stat()
        trash = self.request(self.command()); self.engine.apply(trash)
        restore = self.request(self.command('restore', version=trash['operationId']))
        result = self.engine.apply(restore); self.assertEqual(result['state'], 'Completed'); self.assertTrue(result['bindingCurrent'])
        info = source.stat(); self.assertEqual(source.read_bytes(), content); self.assertEqual(stat.S_IMODE(info.st_mode), 0o750)
        self.assertEqual((info.st_uid, info.st_gid, info.st_mtime_ns), (before.st_uid, before.st_gid, before.st_mtime_ns))
        self.assertEqual(self.metadata(trash['operationId'])['captureState'], 'Restored')
        inode = source.stat().st_ino; self.assertEqual(self.engine.apply(restore)['state'], 'Completed'); self.assertEqual(source.stat().st_ino, inode)

    def test_overwrite_restore_first_captures_current_full_bytes_in_hdd_and_local_private_storage(self):
        identifier = self.capture(b'old'); (self.root / 'source').write_bytes(b'current')
        request = self.request(self.command('restore', version=identifier)); result = self.engine.apply(request)
        self.assertEqual(result['state'], 'Completed'); self.assertTrue(result['sourceRetained']); self.assertEqual((self.root / 'source').read_bytes(), b'old')
        self.assertEqual((self.backups.target / (request['operationId'] + '.data')).read_bytes(), b'current')
        self.assertEqual(result['hddBackup']['sha256'], hashlib.sha256(b'current').hexdigest().upper())
        self.assertEqual((self.root / '.whaledeck-files' / (request['operationId'] + '.archive')).read_bytes(), b'current')

    def test_confirmed_purge_removes_only_the_exact_version_and_replays_without_any_second_delete(self):
        one = self.capture(b'one'); two = self.capture(b'two'); request = self.request(self.command('purge', version=one))
        self.assertEqual(self.engine.apply(request)['state'], 'Completed')
        self.assertFalse((self.backups.target / (one + '.data')).exists()); self.assertFalse((self.backups.target / (one + '.json')).exists())
        self.assertEqual((self.backups.target / (two + '.data')).read_bytes(), b'two'); self.assertEqual((self.root / 'source').read_bytes(), b'two')
        with patch.object(archives.os, 'unlink', side_effect=AssertionError('terminal purge must not unlink again')):
            self.assertEqual(self.engine.apply(request)['state'], 'Completed')

    def test_explicit_history_and_archive_grants_are_required_without_expanding_existing_mutation_rights(self):
        self.capture(); values = json.loads(self.registry.read_bytes())
        values[0]['allowedActions'].remove('files-archive'); self.registry.write_text(json.dumps(values))
        with self.assertRaisesRegex(driver.Rejected, 'FILES_ROOT_FORBIDDEN'): self.engine.plan(self.command())
        self.assertEqual(len(self.listing()['entries']), 1)
        values[0]['allowedActions'].remove('files-history'); self.registry.write_text(json.dumps(values))
        with self.assertRaisesRegex(driver.Rejected, 'FILES_ROOT_FORBIDDEN'): self.listing()

    def test_stale_version_conflict_never_overwrites_a_new_target(self):
        version = self.capture(b'old'); request = self.request(self.command('restore', version=version)); (self.root / 'source').write_bytes(b'external')
        with self.assertRaisesRegex(driver.Rejected, 'FILES_VERSION_CONFLICT'): self.engine.apply(request)
        self.assertEqual((self.root / 'source').read_bytes(), b'external')
        (self.root / 'source').unlink(); request = self.request(self.command('restore', version=version)); (self.root / 'source').write_bytes(b'new')
        with self.assertRaisesRegex(driver.Rejected, 'FILES_VERSION_CONFLICT'): self.engine.apply(request)
        self.assertEqual((self.root / 'source').read_bytes(), b'new')

    def test_wrong_location_and_corrupt_hardlinked_or_missing_backup_cannot_be_restored_or_purged(self):
        version = self.capture(); data = self.backups.target / (version + '.data'); original = data.read_bytes()
        with self.assertRaisesRegex(driver.Rejected, 'FILES_ARCHIVE_NOT_FOUND'): self.engine.plan(self.command('restore', path='other', version=version, expected='ABSENT'))
        data.write_bytes(b'x' * len(original))
        for kind in ('restore', 'purge'):
            with self.assertRaisesRegex(driver.Rejected, 'FILES_BACKUP_CONTENT_INVALID'): self.engine.plan(self.command(kind, version=version))
        data.write_bytes(original); link = self.base / 'link'; os.link(data, link)
        with self.assertRaises(driver.Rejected): self.engine.plan(self.command('restore', version=version))
        link.unlink(); data.unlink()
        self.assertEqual(self.listing()['entries'][0]['availability'], 'Missing')
        with self.assertRaises(FileNotFoundError): self.engine.plan(self.command('restore', version=version))

    def test_symlink_hardlink_fifo_directory_and_protected_targets_remain_denied(self):
        (self.root / 'source').write_bytes(b'keep'); (self.root / 'link').symlink_to(self.root / 'source')
        os.link(self.root / 'source', self.root / 'hard'); os.mkfifo(self.root / 'fifo'); (self.root / 'directory').mkdir()
        for name in ('source', 'link', 'hard', 'fifo', 'directory', '.env', '../escape'):
            with self.subTest(name=name), self.assertRaises((driver.Rejected, OSError)):
                self.engine.plan(self.command(path=name, expected='A' * 64))
        self.assertEqual((self.root / 'source').read_bytes(), b'keep')

    def test_cancel_before_apply_writes_a_permanent_fence_even_after_root_grants_are_revoked(self):
        (self.root / 'source').write_bytes(b'keep'); request = self.request(self.command())
        values = json.loads(self.registry.read_bytes()); original = self.registry.read_bytes(); values[0]['allowedActions'].remove('files-archive'); self.registry.write_text(json.dumps(values))
        self.assertEqual(self.engine.discard(request)['state'], 'Aborted')
        self.registry.write_bytes(original); self.assertEqual(self.engine.apply(request)['state'], 'Aborted')
        self.assertEqual((self.root / 'source').read_bytes(), b'keep')

    def test_owner_plan_and_shared_namespace_locks_are_enforced(self):
        (self.root / 'source').write_bytes(b'keep'); request = self.request(self.command())
        self.engine.discard(request)
        with self.assertRaisesRegex(driver.Rejected, 'FILES_OPERATION_NOT_FOUND'): self.engine.apply({**request, 'owner': 'B' * 64})
        with driver.namespace_lock(), self.assertRaisesRegex(driver.Rejected, 'FILES_OPERATION_BUSY'): self.lookup(request)
        altered = {**request, 'plan': {**request['plan'], 'sha256': 'F' * 64}}
        with self.assertRaisesRegex(driver.Rejected, 'FILES_PLAN_INVALID'): self.engine.discard(altered)

    def test_cancel_fence_survives_a_later_protected_name_policy(self):
        (self.root / 'source').write_bytes(b'keep'); request = self.request(self.command())
        with patch.object(driver, 'protected', return_value=True):
            self.assertEqual(self.engine.discard(request)['state'], 'Aborted')
            with self.assertRaisesRegex(driver.Rejected, 'FILES_PROTECTED'): self.engine.apply(request)
        self.assertEqual(self.engine.apply(request)['state'], 'Aborted')
        self.assertEqual((self.root / 'source').read_bytes(), b'keep')

    def test_metadata_capture_kind_and_state_cannot_contradict_each_other(self):
        version = self.capture(); filename = self.backups.target / (version + '.json'); original = self.metadata(version)
        for kind, state in (('Overwrite', 'Trashed'), ('Overwrite', 'Pending'), ('Trash', 'Available')):
            with self.subTest(kind=kind, state=state):
                filename.write_text(json.dumps({**original, 'captureKind': kind, 'captureState': state}))
                result = self.listing(); self.assertEqual(result['invalidRecords'], 1); self.assertEqual(result['entries'], [])
                with self.assertRaisesRegex(driver.Rejected, 'FILES_ARCHIVE_METADATA_INVALID'): self.engine.plan(self.command('purge', version=version))

    def test_hdd_unavailability_stops_preflight_and_a_late_backup_failure_keeps_the_original(self):
        (self.root / 'source').write_bytes(b'keep'); request = self.request(self.command())
        with patch.object(self.backups.engine, 'target', side_effect=driver.Rejected('FILES_BACKUP_MOUNT_MISMATCH')):
            with self.assertRaisesRegex(driver.Rejected, 'FILES_BACKUP_MOUNT_MISMATCH'): self.engine.plan(self.command())
        original = self.backups.engine.ensure
        def failed(*args, **kwargs): raise driver.Rejected('FILES_BACKUP_CAPACITY_REQUIRED')
        with patch.object(self.backups.engine, 'ensure', side_effect=failed), self.assertRaisesRegex(driver.Rejected, 'FILES_BACKUP_CAPACITY_REQUIRED'):
            self.engine.apply(request)
        self.assertEqual((self.root / 'source').read_bytes(), b'keep'); self.assertEqual(self.lookup(request)['state'], 'Aborted')
        self.assertEqual(self.backups.engine.ensure, original)

    def test_external_source_change_after_hdd_copy_never_removes_the_new_content(self):
        (self.root / 'source').write_bytes(b'old'); request = self.request(self.command()); ensure = self.backups.engine.ensure
        def replaced(*args, **kwargs):
            proof = ensure(*args, **kwargs); (self.root / 'source').write_bytes(b'external-new'); return proof
        with patch.object(self.backups.engine, 'ensure', side_effect=replaced), self.assertRaisesRegex(driver.Rejected, 'FILES_VERSION_CONFLICT'):
            self.engine.apply(request)
        self.assertEqual((self.root / 'source').read_bytes(), b'external-new')
        self.assertEqual((self.backups.target / (request['operationId'] + '.data')).read_bytes(), b'old')
        self.assertEqual(self.lookup(request)['state'], 'Unknown')

    def test_external_parent_rename_after_hdd_copy_cannot_redirect_trash_through_the_pinned_fd(self):
        parent = self.root / 'parent'; parent.mkdir(); (parent / 'source').write_bytes(b'old')
        request = self.request(self.command(path='parent/source')); ensure = self.backups.engine.ensure
        def replaced(*args, **kwargs):
            proof = ensure(*args, **kwargs); parent.rename(self.root / 'moved-parent'); parent.mkdir(); (parent / 'source').write_bytes(b'new'); return proof
        with patch.object(self.backups.engine, 'ensure', side_effect=replaced), self.assertRaisesRegex(driver.Rejected, 'FILES_PARENT_CHANGED'):
            self.engine.apply(request)
        self.assertEqual((parent / 'source').read_bytes(), b'new')
        self.assertEqual((self.root / 'moved-parent/source').read_bytes(), b'old')
        self.assertEqual(self.lookup(request)['state'], 'Unknown')

    def test_history_filters_protection_and_resumes_encoded_pages_without_skipping_long_names(self):
        identifiers = []
        path = '/'.join(['"' * 250] * 14) + '/sample.txt'
        for _ in range(20):
            version = self.capture(); metadata = self.metadata(version); metadata['path'] = path
            (self.backups.target / (version + '.json')).write_text(json.dumps(metadata)); identifiers.append(version)
        protected = self.capture(); metadata = self.metadata(protected); metadata['path'] = '.env'; (self.backups.target / (protected + '.json')).write_text(json.dumps(metadata))
        found = []; after = ''; revision = ''
        for _ in range(30):
            page = self.listing(after=after, take=50, version=revision); revision = page['version']; found.extend(row['versionId'] for row in page['entries'])
            self.assertLess(len(json.dumps(page, ensure_ascii=False).encode()), 65536)
            if page['complete']: break
            self.assertTrue(page['nextCursor']); after = page['nextCursor']
        else: self.fail('bounded history did not finish')
        self.assertEqual(found, sorted(identifiers)); self.assertEqual(len(found), len(set(found)))

    def test_history_directory_changes_reject_old_pagination_and_malformed_metadata_does_not_expose_it(self):
        self.capture(); revision = self.listing()['version']; second = self.capture()
        with self.assertRaisesRegex(driver.Rejected, 'FILES_VERSION_CONFLICT'): self.listing(version=revision)
        (self.backups.target / (second + '.json')).write_text('{"state":"Verified","state":"Verified"}')
        result = self.listing(); self.assertEqual(result['invalidRecords'], 1); self.assertEqual(len(result['entries']), 1)

    def crash(self, request, hook):
        child = os.fork()
        if child == 0:
            try: hook(); self.engine.apply(request)
            except BaseException: os._exit(2)
            os._exit(3)
        deadline = time.monotonic() + 10
        while time.monotonic() < deadline:
            done, status = os.waitpid(child, os.WNOHANG)
            if done:
                self.assertTrue(os.WIFSIGNALED(status)); self.assertEqual(os.WTERMSIG(status), signal.SIGKILL); return
            time.sleep(0.01)
        os.kill(child, signal.SIGKILL); os.waitpid(child, 0); self.fail('owned crash child exceeded deadline')

    def test_actual_sigkill_after_trash_rename_recovers_the_observed_result_without_repeating_the_move(self):
        (self.root / 'source').write_bytes(b'keep'); request = self.request(self.command())
        def hook():
            original = driver.exchange
            def killed(*args):
                original(*args)
                if args[3] == request['operationId'] + '.archive': os.kill(os.getpid(), signal.SIGKILL)
            driver.exchange = killed
        self.crash(request, hook)
        with patch.object(driver, 'exchange', side_effect=AssertionError('recover never repeats rename')):
            self.assertEqual(self.lookup(request)['state'], 'Completed')
        self.assertFalse((self.root / 'source').exists()); self.assertEqual(self.metadata(request['operationId'])['captureState'], 'Trashed')

    def test_actual_sigkill_after_hdd_fsync_before_root_proof_recovers_canceled_not_pending(self):
        content = b'untouched-source'; (self.root / 'source').write_bytes(content); request = self.request(self.command())
        def hook():
            original = self.backups.engine.ensure
            def killed(*args, **kwargs):
                original(*args, **kwargs); os.kill(os.getpid(), signal.SIGKILL)
            self.backups.engine.ensure = killed
        self.crash(request, hook)
        self.assertEqual(self.metadata(request['operationId'])['captureState'], 'Pending')
        with patch.object(driver, 'exchange', side_effect=AssertionError('recovery must not publish')):
            result = self.lookup(request)
        self.assertEqual(result['state'], 'Aborted'); self.assertIsNotNone(result['hddBackup'])
        self.assertEqual(self.metadata(request['operationId'])['captureState'], 'Canceled')
        self.assertEqual((self.root / 'source').read_bytes(), content)
        self.assertEqual((self.backups.target / (request['operationId'] + '.data')).read_bytes(), content)

    def test_actual_sigkill_before_and_after_restore_publication_distinguishes_aborted_and_completed(self):
        version = self.capture(b'old')
        for after in (False, True):
            with self.subTest(after=after):
                (self.root / 'source').write_bytes(b'current'); request = self.request(self.command('restore', version=version))
                def hook():
                    original = driver.exchange
                    def killed(*args):
                        publication = args[1] == request['operationId'] + '.archive' and args[3] == 'source'
                        if publication and not after: os.kill(os.getpid(), signal.SIGKILL)
                        original(*args)
                        if publication and after: os.kill(os.getpid(), signal.SIGKILL)
                    driver.exchange = killed
                self.crash(request, hook)
                with patch.object(driver, 'exchange', side_effect=AssertionError('recover never repeats publication')):
                    self.assertEqual(self.lookup(request)['state'], 'Completed' if after else 'Aborted')
                self.assertEqual((self.root / 'source').read_bytes(), b'old' if after else b'current')

    def test_actual_sigkill_during_purge_preserves_uncertainty_and_never_finishes_a_missing_unlink(self):
        version = self.capture(b'old'); request = self.request(self.command('purge', version=version))
        def hook():
            original = driver.exchange
            def killed(*args):
                original(*args)
                if args[3] == request['operationId'] + '.purging': os.kill(os.getpid(), signal.SIGKILL)
            driver.exchange = killed
        self.crash(request, hook)
        unlink = archives.os.unlink
        def forbid_payload_delete(path, *args, **kwargs):
            if path in (version + '.data', version + '.json', request['operationId'] + '.purging'):
                raise AssertionError('recovery must never continue a destructive payload step')
            return unlink(path, *args, **kwargs)
        with patch.object(archives.os, 'unlink', side_effect=forbid_payload_delete):
            self.assertEqual(self.lookup(request)['state'], 'Unknown')
        self.assertEqual((self.backups.target / (request['operationId'] + '.purging')).read_bytes(), b'old')
        self.assertTrue((self.backups.target / (version + '.json')).exists())

    def test_actual_sigkill_after_purge_unlinks_is_unknown_until_last_metadata_unlink_is_observed(self):
        for after_metadata in (False, True):
            with self.subTest(after_metadata=after_metadata):
                version = self.capture(b'old'); request = self.request(self.command('purge', version=version))
                def hook():
                    original = archives.os.unlink
                    def killed(path, *args, **kwargs):
                        original(path, *args, **kwargs)
                        if path == (version + '.json' if after_metadata else request['operationId'] + '.purging'):
                            os.kill(os.getpid(), signal.SIGKILL)
                    archives.os.unlink = killed
                self.crash(request, hook)
                result = self.lookup(request)
                self.assertEqual(result['state'], 'Completed' if after_metadata else 'Unknown')
                self.assertFalse((self.backups.target / (version + '.data')).exists())
                self.assertEqual((self.backups.target / (version + '.json')).exists(), not after_metadata)
                self.assertEqual((self.root / 'source').read_bytes(), b'old')


if __name__ == '__main__': unittest.main()
