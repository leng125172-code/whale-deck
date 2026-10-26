"""Actual Linux rename/fsync/UID semantics with explicit synthetic read/HDD scopes.

These tests never use production data or a Docker/helper/API surface. Liveness
and Docker policy are supplied by the independently tested ContainerRoot seam.
"""
import hashlib
import json
import os
import signal
import tempfile
import time
import unittest
import uuid
from unittest.mock import patch

import test_container_file_backups as backup_tests
import test_container_file_staging as staging

publisher = staging.load('whaledeck-container-publish.py', 'container_publication_candidate')


@unittest.skipUnless(os.name == 'posix' and hasattr(os, 'geteuid') and os.geteuid() == 0, 'requires isolated Linux root')
class LinuxContainerPublicationTests(unittest.TestCase):
    def setUp(self):
        instance = str(os.getpid()) + ':2026-10-21T01:02:03.123456789Z'
        self.instance_patch = patch.object(staging, 'INSTANCE', instance)
        self.instance_patch.start(); self.addCleanup(self.instance_patch.stop)
        self.fixture = backup_tests.LinuxContainerBackupTests(); self.fixture.setUp()
        self.addCleanup(self.fixture.doCleanups)
        self.f, self.r, self.u, self.b, self.session = self.fixture.f, self.fixture.r, self.fixture.upload, self.fixture.engine, self.fixture.session
        self.session.container = {'HostConfig': {'ReadonlyRootfs': False}, 'Mounts': []}
        self.engine = publisher.ContainerPublisher(self.u, self.b)
        self.root, self.state, self.leaf = self.fixture.root, self.fixture.state, self.fixture.leaf
        self.content = b'new publication\x00\xff\xe4\xb8\xad'

    def stage(self, absent=False, content=None):
        content = self.content if content is None else content
        path = 'new.bin' if absent else self.leaf.name
        location = dict(self.session.request)
        request = {**location, 'path': path, 'operationId': uuid.uuid4().hex, 'owner': staging.OWNER,
            'expectedVersion': 'ABSENT' if absent else self.r.version(location, path, self.leaf.stat()),
            'expectedParentVersion': self.r.version(location, '', self.root.stat()),
            'sizeBytes': len(content), 'sha256': hashlib.sha256(content).hexdigest().upper()}
        with tempfile.TemporaryFile() as stream:
            for row in staging.LinuxContainerStagingTests.packets(content): stream.write((json.dumps(row) + '\n').encode())
            stream.seek(0)
            self.u.stage(request, self.session, self.f.InputLines(stream.fileno()))
        return request, staging.LinuxContainerStagingTests.reference(request)

    def record(self, scope):
        return json.loads((self.state / (scope['operationId'] + '.json')).read_bytes())

    def pending(self, scope): return self.root / publisher.PRIVATE / (scope['operationId'] + '.upload')

    def test_complete_binary_overwrite_retains_old_inode_and_proven_hdd_bytes(self):
        content = hashlib.shake_256(b'container-full-publication').digest(2 * 1024**2 + 7)
        os.chown(self.leaf, 1000, 1001); self.leaf.chmod(0o6755)
        old_info = self.leaf.stat(); request, scope = self.stage(content=content)
        result = self.engine.publish(scope, self.session)
        self.assertEqual(result['state'], 'Published'); self.assertTrue(result['bindingCurrent']); self.assertTrue(result['previousRetained'])
        self.assertEqual(self.leaf.read_bytes(), content)
        self.assertEqual((self.leaf.stat().st_uid, self.leaf.stat().st_gid, self.leaf.stat().st_mode & 0o7777), (1000, 1001, 0o755))
        self.assertEqual(self.pending(scope).read_bytes(), self.fixture.original)
        self.assertEqual(self.pending(scope).stat().st_ino, old_info.st_ino)
        self.assertEqual((self.fixture.target / (scope['operationId'] + '.data')).read_bytes(), self.fixture.original)
        self.assertEqual((self.state / (scope['operationId'] + '.data')).read_bytes(), content)
        self.assertEqual(self.r.version(request, request['path'], self.leaf.stat()), result['version'])
        self.assertNotEqual(self.leaf.stat().st_ino, old_info.st_ino)

    def test_absent_create_uses_kernel_filesystem_owner_and_private_mode_without_fake_backup(self):
        _, scope = self.stage(absent=True)
        result = self.engine.publish(scope, self.session)
        created = self.root / 'new.bin'
        self.assertEqual(created.read_bytes(), self.content)
        self.assertEqual((created.stat().st_uid, created.stat().st_gid, created.stat().st_mode & 0o777), (os.geteuid(), os.getegid(), 0o600))
        self.assertEqual(result['state'], 'Published'); self.assertFalse(result['previousRetained']); self.assertIsNone(result['backupCapture'])
        self.assertFalse(self.pending(scope).exists()); self.assertFalse(self.fixture.target.exists())

    def test_empty_file_publishes_and_replay_never_copies_or_renames_again(self):
        _, scope = self.stage(absent=True, content=b'')
        first = self.engine.publish(scope, self.session)
        with patch.object(self.f, 'exchange', side_effect=AssertionError('repeat rename')), patch.object(self.engine, 'copy', side_effect=AssertionError('repeat copy')):
            replay = self.engine.publish(scope, self.session)
        self.assertEqual(replay['version'], first['version']); self.assertTrue(replay['replayed']); self.assertTrue(replay['bindingCurrent'])
        self.assertEqual((self.root / 'new.bin').read_bytes(), b'')

    def test_published_outcome_is_monotonic_when_target_later_changes(self):
        _, scope = self.stage(); self.engine.publish(scope, self.session)
        self.leaf.write_bytes(b'another writer')
        result = self.engine.inspect(scope, self.session)
        self.assertEqual(result['state'], 'Published'); self.assertFalse(result['bindingCurrent'])
        with self.assertRaisesRegex(self.r.Rejected, 'ALREADY_PUBLISHED'): self.engine.discard(scope, self.session)
        self.assertEqual(self.leaf.read_bytes(), b'another writer')

    def test_read_only_root_and_bind_or_mount_root_are_refused_before_copy(self):
        for host, mounts in (({'ReadonlyRootfs': True}, []), ({'ReadonlyRootfs': False}, [{'Destination': '/', 'RW': False}]),
                             ({'ReadonlyRootfs': False}, [{'Destination': '/' + self.leaf.name, 'RW': True}])):
            _, scope = self.stage()
            self.session.container = {'HostConfig': host, 'Mounts': mounts}
            with self.assertRaisesRegex(self.r.Rejected, 'PUBLISH_(READ_ONLY|MOUNT_ROOT)'): self.engine.publish(scope, self.session)
            self.assertFalse((self.root / publisher.PRIVATE).exists())
        self.assertEqual(self.leaf.read_bytes(), self.fixture.original)

    def test_writable_longest_bind_allows_file_under_readonly_root(self):
        self.session.container = {'HostConfig': {'ReadonlyRootfs': True}, 'Mounts': [
            {'Destination': '/work', 'RW': False}, {'Destination': '/work/nested', 'RW': True}]}
        record = {'request': {'path': 'work/nested/file.bin'}}
        self.engine.writable(record, self.session, self.fixture.folder)
        self.session.container['Mounts'][1]['RW'] = False
        with self.assertRaisesRegex(self.r.Rejected, 'PUBLISH_READ_ONLY'):
            self.engine.writable(record, self.session, self.fixture.folder)

    def test_source_parent_and_leaf_conflicts_preserve_both_payloads(self):
        _, scope = self.stage(); self.leaf.write_bytes(b'outside edit')
        with self.assertRaisesRegex(self.r.Rejected, 'VERSION_CONFLICT'): self.engine.publish(scope, self.session)
        self.assertEqual(self.leaf.read_bytes(), b'outside edit'); self.assertTrue((self.state / (scope['operationId'] + '.data')).exists())
        _, scope = self.stage(); (self.root / 'neighbor').touch()
        with self.assertRaisesRegex(self.r.Rejected, 'PARENT_VERSION_CONFLICT'): self.engine.publish(scope, self.session)
        self.assertFalse((self.root / publisher.PRIVATE).exists())

    def test_foreign_scope_and_reconfigured_session_cannot_publish(self):
        _, scope = self.stage()
        with self.assertRaises(self.r.Rejected): self.engine.publish({**scope, 'owner': 'C' * 64}, self.session)
        self.session.mounts = b'N' * 32
        with self.assertRaisesRegex(self.r.Rejected, 'TARGET_CHANGED'): self.engine.publish(scope, self.session)
        self.assertEqual(self.leaf.read_bytes(), self.fixture.original)

    def test_missing_hdd_or_corrupt_original_capture_never_replaces_source(self):
        _, scope = self.stage()
        self.fixture.disk.mountinfo.write_bytes(b'')
        with self.assertRaises(self.f.Rejected): self.engine.publish(scope, self.session)
        self.assertFalse((self.root / publisher.PRIVATE).exists())
        self.assertEqual(self.leaf.read_bytes(), self.fixture.original)

    def test_private_directory_symlink_or_unsafe_permissions_are_not_adopted(self):
        outside = self.fixture.disk.disk / 'outside'; outside.mkdir()
        private = self.root / publisher.PRIVATE; private.symlink_to(outside, target_is_directory=True)
        _, scope = self.stage()
        with self.assertRaises((OSError, self.f.Rejected)): self.engine.publish(scope, self.session)
        self.assertEqual(list(outside.iterdir()), [])
        private.unlink(); private.mkdir(mode=0o755)
        _, scope = self.stage()
        with self.assertRaisesRegex(self.r.Rejected, 'PRIVATE_UNSAFE'): self.engine.publish(scope, self.session)
        self.assertEqual(private.stat().st_mode & 0o777, 0o755)

    def test_conflicting_exchange_becomes_unknown_and_preserves_every_object(self):
        _, scope = self.stage()
        exchange = self.f.exchange
        def concurrent(*args):
            if args[-1] == 2 and args[1].endswith('.upload'):
                self.leaf.write_bytes(b'external last moment edit')
            return exchange(*args)
        with patch.object(self.f, 'exchange', side_effect=concurrent): result = self.engine.publish(scope, self.session)
        self.assertEqual(result['state'], 'Unknown')
        self.assertEqual(self.pending(scope).read_bytes(), b'external last moment edit')
        self.assertEqual(self.leaf.read_bytes(), self.content)
        with self.assertRaisesRegex(self.r.Rejected, 'PREVIOUS_OPERATION_INCOMPLETE'): self.engine.discard(scope, self.session)
        self.assertEqual(self.pending(scope).read_bytes(), b'external last moment edit')

    def test_target_appearing_before_noreplace_is_preserved_and_not_overwritten(self):
        _, scope = self.stage(absent=True)
        exchange = self.f.exchange
        def concurrent(*args):
            if args[-1] == 1 and args[1].endswith('.upload'): (self.root / 'new.bin').write_bytes(b'concurrent')
            return exchange(*args)
        with patch.object(self.f, 'exchange', side_effect=concurrent), self.assertRaises(FileExistsError): self.engine.publish(scope, self.session)
        self.assertEqual((self.root / 'new.bin').read_bytes(), b'concurrent')
        self.assertEqual(self.engine.inspect(scope, self.session)['state'], 'Unknown')
        self.assertEqual(self.pending(scope).read_bytes(), self.content)

    def test_original_staging_api_cannot_discard_or_hide_publication(self):
        _, scope = self.stage(); self.engine.publish(scope, self.session)
        for action in (self.u.inspect, self.u.discard):
            with self.assertRaisesRegex(self.r.Rejected, 'PUBLISHER_REQUIRED'): action(scope, self.session)
        self.assertEqual(self.record(scope)['state'], 'Published')

    def test_prepared_output_is_read_back_before_any_target_replacement(self):
        _, scope = self.stage()
        copy = self.engine.copy
        def corrupt(*args):
            copy(*args)
            pending = self.pending(scope); before = pending.stat()
            pending.write_bytes(b'x' * len(self.content))
            os.utime(pending, ns=(before.st_atime_ns, before.st_mtime_ns))
        with patch.object(self.engine, 'copy', side_effect=corrupt), self.assertRaisesRegex(self.r.Rejected, 'OBJECT_CHANGED'):
            self.engine.publish(scope, self.session)
        self.assertEqual(self.leaf.read_bytes(), self.fixture.original)
        self.assertEqual(self.pending(scope).read_bytes(), b'x' * len(self.content))
        with self.assertRaisesRegex(self.r.Rejected, 'OBJECT_CHANGED'): self.engine.discard(scope, self.session)
        self.assertEqual(self.pending(scope).read_bytes(), b'x' * len(self.content))
        self.assertTrue((self.state / (scope['operationId'] + '.data')).exists())

    def test_hdd_copy_is_reverified_after_preparation_and_before_exchange(self):
        _, scope = self.stage()
        copy = self.engine.copy
        def corrupt(*args):
            copy(*args)
            (self.fixture.target / (scope['operationId'] + '.data')).write_bytes(b'corrupt')
        with patch.object(self.engine, 'copy', side_effect=corrupt), self.assertRaisesRegex(self.f.Rejected, 'CONTENT_INVALID'):
            self.engine.publish(scope, self.session)
        self.assertEqual(self.leaf.read_bytes(), self.fixture.original)
        self.assertEqual(self.pending(scope).read_bytes(), self.content)

    def test_same_operation_and_global_publication_gates_refuse_concurrent_commit(self):
        _, scope = self.stage()
        with self.u.locked(scope['operationId']):
            with self.assertRaisesRegex(self.r.Rejected, 'BUSY'): self.engine.publish(scope, self.session)
        with self.u.locked(scope['operationId']) as state, self.b.gate(state):
            _, other = self.stage()
            with self.assertRaisesRegex(self.r.Rejected, 'BUSY'): self.engine.publish(other, self.session)
        self.assertEqual(self.leaf.read_bytes(), self.fixture.original)
        self.assertFalse((self.root / publisher.PRIVATE).exists())

    def test_new_file_owner_change_after_copy_does_not_publish(self):
        _, scope = self.stage(absent=True)
        with patch.object(self.engine, 'owner', side_effect=([0, 0], [1000, 1000])), \
                self.assertRaisesRegex(self.r.Rejected, 'PUBLISH_OWNER_CHANGED'):
            self.engine.publish(scope, self.session)
        self.assertFalse((self.root / 'new.bin').exists())
        self.assertEqual(self.engine.discard(scope, self.session)['state'], 'PublishDiscarded')

    def test_hardlinked_prepared_inode_is_never_exchanged(self):
        _, scope = self.stage(); copy = self.engine.copy
        def link(*args):
            copy(*args)
            os.link(self.pending(scope), self.root / 'outside-link')
        with patch.object(self.engine, 'copy', side_effect=link), self.assertRaisesRegex(self.f.Rejected, 'SINGLE_LINK_REQUIRED'):
            self.engine.publish(scope, self.session)
        self.assertEqual(self.leaf.read_bytes(), self.fixture.original)
        self.assertEqual((self.root / 'outside-link').read_bytes(), self.content)

    def test_foreign_hdd_metadata_after_preparation_is_not_accepted(self):
        _, scope = self.stage(); copy = self.engine.copy
        def change(*args):
            copy(*args)
            metadata = self.fixture.target / (scope['operationId'] + '.json')
            saved = json.loads(metadata.read_bytes()); saved['containerId'] = 'd' * 64
            metadata.write_text(json.dumps(saved))
        with patch.object(self.engine, 'copy', side_effect=change), self.assertRaisesRegex(self.r.Rejected, 'BACKUP_BINDING_CHANGED'):
            self.engine.publish(scope, self.session)
        self.assertEqual(self.leaf.read_bytes(), self.fixture.original)

    def test_observation_does_not_recreate_a_missing_hdd_history_namespace(self):
        _, scope = self.stage(); exchange = self.f.exchange
        def fail(*args):
            if args[1].endswith('.upload'): raise OSError('fixture rename refusal')
            return exchange(*args)
        with patch.object(self.f, 'exchange', side_effect=fail), self.assertRaises(OSError): self.engine.publish(scope, self.session)
        moved = self.fixture.disk.disk / 'temporarily-unavailable-history'
        self.fixture.target.rename(moved)
        try:
            with self.assertRaises(FileNotFoundError): self.engine.inspect(scope, self.session)
            self.assertFalse(self.fixture.target.exists())
            self.assertEqual(self.record(scope)['state'], 'PublishApplying')
            self.assertEqual(self.leaf.read_bytes(), self.fixture.original)
        finally: moved.rename(self.fixture.target)
        self.assertEqual(self.engine.inspect(scope, self.session)['state'], 'PublishInterrupted')
        self.assertEqual(self.engine.discard(scope, self.session)['state'], 'PublishDiscarded')

    def test_changed_parent_path_after_copy_cannot_publish_into_detached_directory(self):
        folder = self.root / 'folder'; folder.mkdir()
        self.leaf.rename(folder / self.leaf.name)
        # Stage a nested path with its own parent version.
        self.leaf = folder / self.leaf.name
        path = 'folder/' + self.leaf.name; location = dict(self.session.request)
        request = {**location, 'path': path, 'operationId': uuid.uuid4().hex, 'owner': staging.OWNER,
            'expectedVersion': self.r.version(location, path, self.leaf.stat()),
            'expectedParentVersion': self.r.version(location, 'folder', folder.stat()),
            'sizeBytes': len(self.content), 'sha256': hashlib.sha256(self.content).hexdigest().upper()}
        with tempfile.TemporaryFile() as stream:
            for row in staging.LinuxContainerStagingTests.packets(self.content): stream.write((json.dumps(row) + '\n').encode())
            stream.seek(0); self.u.stage(request, self.session, self.f.InputLines(stream.fileno()))
        scope = staging.LinuxContainerStagingTests.reference(request); copy = self.engine.copy
        def replace(*args):
            copy(*args)
            folder.rename(self.root / 'detached'); folder.mkdir()
        with patch.object(self.engine, 'copy', side_effect=replace), self.assertRaisesRegex(self.r.Rejected, 'PUBLISH_PARENT_CHANGED'):
            self.engine.publish(scope, self.session)
        self.assertFalse((folder / self.leaf.name).exists())
        self.assertEqual((self.root / 'detached' / self.leaf.name).read_bytes(), self.fixture.original)

    def kill(self, action):
        child = os.fork()
        if child == 0:
            try: action()
            except BaseException: os._exit(2)
            os._exit(3)
        deadline, status = time.monotonic() + 15, None
        while time.monotonic() < deadline:
            done, code = os.waitpid(child, os.WNOHANG)
            if done: status = code; break
            time.sleep(0.01)
        if status is None:
            os.kill(child, signal.SIGKILL); os.waitpid(child, 0); self.fail('publication child exceeded deadline')
        self.assertTrue(os.WIFSIGNALED(status)); self.assertEqual(os.WTERMSIG(status), signal.SIGKILL)

    def test_actual_sigkill_at_seven_publication_boundaries_never_repeats_rename(self):
        points = ('before-private', 'before-pending-journal', 'during-copy', 'before-applying-journal',
                  'before-rename', 'after-rename', 'before-published-journal')
        for point in points:
            with self.subTest(point=point):
                # Each case has a newly versioned path; earlier successful
                # publications remain legitimate current originals.
                _, scope = self.stage()
                before = self.leaf.read_bytes()
                def action():
                    save, exchange, private, write = self.f.save_receipt, self.f.exchange, self.engine.private, os.write
                    def persist(*args):
                        record = args[2]
                        if point == 'before-pending-journal' and record.get('publication', {}).get('pendingIdentity') is not None or \
                                point == 'before-applying-journal' and record.get('state') == 'PublishApplying' or \
                                point == 'before-published-journal' and record.get('state') == 'Published':
                            os.kill(os.getpid(), signal.SIGKILL)
                        return save(*args)
                    def directory(*args, **kwargs):
                        if point == 'before-private': os.kill(os.getpid(), signal.SIGKILL)
                        return private(*args, **kwargs)
                    def rename(*args):
                        if args[1].endswith('.upload'):
                            if point == 'before-rename': os.kill(os.getpid(), signal.SIGKILL)
                            result = exchange(*args)
                            if point == 'after-rename': os.kill(os.getpid(), signal.SIGKILL)
                            return result
                        return exchange(*args)
                    def output(*args):
                        result = write(*args)
                        if point == 'during-copy' and self.pending(scope).exists(): os.kill(os.getpid(), signal.SIGKILL)
                        return result
                    self.f.save_receipt, self.f.exchange, self.engine.private, os.write = persist, rename, directory, output
                    self.engine.publish(scope, self.session)
                self.kill(action)
                with patch.object(self.f, 'exchange', side_effect=AssertionError('recovery cannot rename')):
                    result = self.engine.inspect(scope, self.session)
                    replay = self.engine.publish(scope, self.session)
                self.assertEqual(result['state'], replay['state'])
                expected = 'Published' if point in ('after-rename', 'before-published-journal') else 'Unknown' if point == 'before-pending-journal' else 'PublishInterrupted'
                self.assertEqual(result['state'], expected)
                self.assertEqual(self.leaf.read_bytes(), self.content if expected == 'Published' else before)
                if expected == 'PublishInterrupted':
                    self.assertEqual(self.engine.discard(scope, self.session)['state'], 'PublishDiscarded')
                    self.assertFalse(self.pending(scope).exists())
                    self.assertFalse((self.state / (scope['operationId'] + '.data')).exists())

    def test_actual_sigkill_during_explicit_discard_resumes_only_owned_cleanup(self):
        for point in ('before-pending-unlink', 'before-host-unlink', 'before-discarded-journal'):
            with self.subTest(point=point):
                _, scope = self.stage()
                # A definite rename failure leaves full prepared data, never
                # permission for the status observer to retry the overwrite.
                exchange = self.f.exchange
                def fail(*args):
                    if args[1].endswith('.upload'): raise OSError('fixture rename refusal')
                    return exchange(*args)
                with patch.object(self.f, 'exchange', side_effect=fail), self.assertRaises(OSError): self.engine.publish(scope, self.session)
                self.assertEqual(self.engine.inspect(scope, self.session)['state'], 'PublishInterrupted')
                before = self.leaf.read_bytes()
                def action():
                    unlink, save = os.unlink, self.f.save_receipt
                    def remove(name, *args, **kwargs):
                        if point == 'before-pending-unlink' and name == scope['operationId'] + '.upload' or \
                                point == 'before-host-unlink' and name == scope['operationId'] + '.data':
                            os.kill(os.getpid(), signal.SIGKILL)
                        return unlink(name, *args, **kwargs)
                    def persist(*args):
                        if point == 'before-discarded-journal' and args[2].get('state') == 'PublishDiscarded': os.kill(os.getpid(), signal.SIGKILL)
                        return save(*args)
                    os.unlink, self.f.save_receipt = remove, persist
                    self.engine.discard(scope, self.session)
                self.kill(action)
                result = self.engine.inspect(scope, self.session)
                self.assertIn(result['state'], ('PublishDiscarding', 'PublishDiscarded'))
                self.assertEqual(self.engine.discard(scope, self.session)['state'], 'PublishDiscarded')
                self.assertEqual(self.leaf.read_bytes(), before)
                self.assertFalse(self.pending(scope).exists())
                self.assertFalse((self.state / (scope['operationId'] + '.data')).exists())
                self.assertTrue((self.fixture.target / (scope['operationId'] + '.data')).exists())


if __name__ == '__main__': unittest.main()
