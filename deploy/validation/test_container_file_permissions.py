"""Actual Linux inode/mode semantics with explicit synthetic HDD attestation.

No Docker, installed helper, RPC, business/API/UI or physical disk proof here.
"""
import hashlib
import json
import os
from pathlib import Path
import select
import signal
import stat
import struct
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch
import uuid

import test_container_file_staging as staging
import test_file_backups as backups

candidate = staging.load('whaledeck-container-permissions.py', 'container_permissions_candidate')
directories = staging.load('whaledeck-container-directories.py', 'container_permissions_storage')
publisher = staging.load('whaledeck-container-publish.py', 'container_permissions_writability')


@unittest.skipUnless(os.name == 'posix' and os.geteuid() == 0, 'isolated Linux root required')
class LinuxContainerPermissionTests(unittest.TestCase):
    def setUp(self):
        self.disk = backups.FileBackupTests(); self.disk.setUp(); self.addCleanup(self.disk.tearDown)
        self.f, self.r, self.root = self.disk.engine.f, staging.read_engine, self.disk.root
        self.state = self.disk.disk / 'permission-state'; self.state.mkdir(mode=0o700)
        self.folder = os.open(self.root, os.O_RDONLY | os.O_DIRECTORY); self.addCleanup(os.close, self.folder)
        self.session = SimpleNamespace(root=self.folder, request={'containerId': staging.ID, 'instanceId': str(os.getpid()) + ':2026-10-30T01:02:03.123456789Z'},
            mounts=b'M'*32, identity=('12345', os.getpid(), 888), current=Mock(), revalidate=Mock(),
            container={'HostConfig': {'ReadonlyRootfs': False}, 'Mounts': []})
        self.uploads = staging.uploads.ContainerUploads(self.r, self.f, self.state)
        self.publisher = publisher.ContainerPublisher(self.uploads, SimpleNamespace(u=self.uploads))
        self.history = candidate.ContainerPermissionHistory(self.disk.engine)
        self.engine = candidate.container_permissions_type(directories.ContainerDirectories)(self.uploads, self.publisher, self.state, self.history)
        self.content = hashlib.shake_256(b'permission-content-preserved').digest(2 * 1024**2 + 7)
        self.leaf = self.root / '中文权限.bin'; self.leaf.write_bytes(self.content); os.chown(self.leaf, 2100, 3100); self.leaf.chmod(0o640)
        os.utime(self.leaf, ns=(1700000000123456789, 1700000000123456789))
        self.backup = self.disk.target / self.history.NAMESPACE

    def command(self, path=None, mode='0600'):
        path = self.leaf.name if path is None else path
        return {**self.session.request, 'path': path, 'mode': mode, 'expectedVersion': self.r.version(self.session.request, path, (self.root / path).lstat())}

    def request(self, command=None):
        return {'operationId': uuid.uuid4().hex, 'owner': staging.OWNER,
            'plan': self.engine.plan(self.command() if command is None else command, self.session)}

    def record(self, request): return json.loads((self.state / (request['operationId'] + '.json')).read_bytes())

    def history_file(self, request): return self.backup / (request['operationId'] + '.json')

    def test_plan_is_read_only_and_binds_full_inode_parent_instance_mode_and_attributes(self):
        before = self.r.fingerprint(self.leaf.stat()); parent = self.r.fingerprint(self.root.stat())
        request = self.request(); plan = request['plan']
        self.assertEqual(plan['sourceIdentity'], list(before)); self.assertEqual(plan['kind'], 'File')
        self.assertEqual(plan['command']['mode'], '0600'); self.assertFalse(plan['noChange'])
        self.assertEqual(before, self.r.fingerprint(self.leaf.stat())); self.assertEqual(parent, self.r.fingerprint(self.root.stat()))
        self.assertFalse(self.backup.exists()); self.assertEqual([], list(self.state.iterdir()))
        self.assertEqual(self.engine.digest(plan), plan['fingerprint'])

    def test_complete_keeps_all_content_original_inode_owner_mtime_and_private_hdd_metadata(self):
        request = self.request(); before = self.leaf.stat(); result = self.engine.apply(request, self.session); after = self.leaf.stat()
        self.assertEqual((result['state'], result['modeConfirmed'], result['bindingCurrent']), ('Completed', True, True))
        self.assertEqual((after.st_dev, after.st_ino, after.st_uid, after.st_gid, after.st_mtime_ns), (before.st_dev, before.st_ino, 2100, 3100, before.st_mtime_ns))
        self.assertEqual(stat.S_IMODE(after.st_mode), 0o600); self.assertEqual(self.leaf.read_bytes(), self.content)
        saved = json.loads(self.history_file(request).read_bytes())
        self.assertEqual(saved['resourceType'], 'ContainerPermissionMetadata'); self.assertEqual(saved['plan'], request['plan'])
        self.assertEqual(saved['plan']['sourceIdentity'][2] & 0o777, 0o640)
        self.assertTrue(result['historyVerified']); self.assertEqual(saved['historyDigest'], result['historyDigest'])
        self.assertEqual(stat.S_IMODE(self.history_file(request).stat().st_mode), 0o600)
        self.assertEqual(stat.S_IMODE(self.backup.stat().st_mode), 0o700)
        self.assertEqual({p.suffix for p in self.backup.iterdir()}, {'.json', '.lock'})
        with patch.object(os, 'fchmod', side_effect=AssertionError('must not repeat chmod')):
            replay = self.engine.apply(request, self.session)
        self.assertTrue(replay['replayed']); self.assertEqual(replay['version'], result['version'])

    def test_nonrecursive_directory_change_leaves_every_child_identity_unchanged(self):
        folder = self.root / '目录'; folder.mkdir(mode=0o750); folder.chmod(0o750); os.chown(folder, 2200, 3200)
        (folder / 'child').write_bytes(self.content); (folder / 'empty').mkdir()
        children = {name: self.r.fingerprint((folder / name).stat()) for name in ('child', 'empty')}
        info = folder.stat(); request = self.request(self.command(folder.name, '0700'))
        result = self.engine.apply(request, self.session)
        self.assertEqual((result['state'], result['kind']), ('Completed', 'Directory'))
        self.assertEqual((folder.stat().st_ino, folder.stat().st_uid, folder.stat().st_gid), (info.st_ino, 2200, 3200))
        self.assertEqual(stat.S_IMODE(folder.stat().st_mode), 0o700)
        self.assertEqual(children, {name: self.r.fingerprint((folder / name).stat()) for name in children})
        self.assertEqual((folder / 'child').read_bytes(), self.content)

    def test_sparse_ten_gib_file_is_a_metadata_operation_without_upload_size_limit(self):
        large = self.root / 'large'; large.touch(); os.truncate(large, 10 * 1024**3 + 7); large.chmod(0o640)
        before = large.stat(); result = self.engine.apply(self.request(self.command(large.name)), self.session)
        self.assertEqual(result['state'], 'Completed'); self.assertEqual(large.stat().st_size, before.st_size)
        self.assertEqual(large.stat().st_ino, before.st_ino); self.assertEqual(large.stat().st_blocks, before.st_blocks)
        self.assertEqual(stat.S_IMODE(large.stat().st_mode), 0o600)

    def test_no_change_records_observed_mode_without_mutating_ctime(self):
        request = self.request(self.command(mode='0640')); before = self.r.fingerprint(self.leaf.stat())
        with patch.object(os, 'fchmod', side_effect=AssertionError('no-op must not chmod')):
            result = self.engine.apply(request, self.session)
        self.assertEqual(result['state'], 'Completed'); self.assertTrue(result['noChange'])
        self.assertEqual(before, self.r.fingerprint(self.leaf.stat())); self.assertEqual(result['version'], request['plan']['command']['expectedVersion'])

    def test_source_change_after_plan_is_rejected_before_history_or_mode_change(self):
        request = self.request(); self.leaf.write_bytes(b'external'); info = self.r.fingerprint(self.leaf.stat())
        with self.assertRaisesRegex(self.r.Rejected, 'VERSION_CONFLICT'): self.engine.apply(request, self.session)
        self.assertEqual(self.r.fingerprint(self.leaf.stat()), info); self.assertFalse(self.backup.exists())
        self.assertEqual({path.name for path in self.state.iterdir()}, {request['operationId'] + '.lock'})
        self.assertEqual([], list(self.state.glob('*.json')))

    def test_directory_parent_replacement_cannot_redirect_chmod(self):
        parent = self.root / 'parent'; parent.mkdir(); path = parent / 'file'; path.write_bytes(b'keep'); path.chmod(0o640)
        request = self.request(self.command('parent/file')); parent.rename(self.root / 'old-parent')
        parent.mkdir(); path.write_bytes(b'foreign'); path.chmod(0o644); before = self.r.fingerprint(path.stat())
        with self.assertRaises((self.r.Rejected, self.f.Rejected)): self.engine.apply(request, self.session)
        self.assertEqual(self.r.fingerprint(path.stat()), before); self.assertEqual(stat.S_IMODE((self.root / 'old-parent/file').stat().st_mode), 0o640)

    def test_unsupported_commands_special_modes_links_and_special_objects_are_refused(self):
        for extra in ({'mode': '4755'}, {'mode': '07777'}, {'mode': '644'}, {'uid': 0}, {'recursive': True}, {'expectedVersion': 'ABSENT'}, {'path': '../escape'}):
            with self.subTest(extra=extra), self.assertRaises((self.r.Rejected, self.f.Rejected)):
                self.engine.plan({**self.command(), **extra}, self.session)
        self.leaf.chmod(0o4640)
        with self.assertRaisesRegex(self.r.Rejected, 'SOURCE_UNSUPPORTED'): self.request()
        self.leaf.chmod(0o640); os.link(self.leaf, self.root / 'hardlink')
        with self.assertRaisesRegex(self.r.Rejected, 'SOURCE_UNSUPPORTED'): self.request()
        (self.root / 'hardlink').unlink(); (self.root / 'link').symlink_to(self.leaf)
        with self.assertRaises((OSError, self.r.Rejected)): self.request(self.command('link'))
        os.mkfifo(self.root / 'fifo')
        with self.assertRaisesRegex(self.r.Rejected, 'SOURCE_UNSUPPORTED'): self.request(self.command('fifo'))

    def test_user_xattrs_are_preserved_and_only_their_digest_is_recorded(self):
        value = b'ordinary private attribute value'; os.setxattr(self.leaf, 'user.whaledeck-test', value)
        request = self.request(); result = self.engine.apply(request, self.session)
        self.assertEqual(result['state'], 'Completed'); self.assertEqual(os.getxattr(self.leaf, 'user.whaledeck-test'), value)
        self.assertNotIn(value.decode(), self.history_file(request).read_text()); self.assertNotIn(value.decode(), json.dumps(request))

    def test_existing_real_acl_is_not_silently_masked_by_ordinary_chmod(self):
        entries = [(1, 6, 0xffffffff), (2, 4, 2300), (4, 4, 0xffffffff), (16, 4, 0xffffffff), (32, 0, 0xffffffff)]
        acl = struct.pack('<I', 2) + b''.join(struct.pack('<HHI', *entry) for entry in entries)
        os.setxattr(self.leaf, 'system.posix_acl_access', acl); before = self.r.fingerprint(self.leaf.stat())
        with self.assertRaisesRegex(self.r.Rejected, 'EXTENDED_METADATA_REQUIRED'): self.request()
        self.assertEqual(os.getxattr(self.leaf, 'system.posix_acl_access'), acl); self.assertEqual(self.r.fingerprint(self.leaf.stat()), before)
        self.assertFalse(self.backup.exists())

    def test_security_attributes_are_refused_before_read_or_write(self):
        with patch.object(os, 'listxattr', return_value=['security.capability']), patch.object(os, 'getxattr', side_effect=AssertionError('must not read security values')):
            with self.assertRaisesRegex(self.r.Rejected, 'EXTENDED_METADATA_REQUIRED'): self.request()

    def test_backup_failure_preserves_source_and_replay_never_attempts_chmod(self):
        request = self.request(); before = self.r.fingerprint(self.leaf.stat())
        with patch.object(self.history, 'capture', side_effect=self.f.Rejected('FILES_BACKUP_DEVICE_MISMATCH')):
            with self.assertRaisesRegex(self.f.Rejected, 'DEVICE_MISMATCH'): self.engine.apply(request, self.session)
        result = self.engine.inspect(request, self.session); self.assertEqual(result['state'], 'Aborted')
        self.assertFalse(result['modeConfirmed']); self.assertFalse(result['historyVerified']); self.assertTrue(result['historyMayExist'])
        self.assertEqual(before, self.r.fingerprint(self.leaf.stat()))
        with patch.object(os, 'fchmod', side_effect=AssertionError('must not retry mode')), patch.object(self.history, 'capture', side_effect=AssertionError('must not recapture')):
            self.assertEqual(self.engine.apply(request, self.session)['state'], 'Aborted')

    def test_history_tampering_blocks_mutation_without_repair_or_adoption(self):
        request = self.request(); before = self.r.fingerprint(self.leaf.stat())
        def checkpoint(phase):
            if phase == 'HistoryVerified':
                path = self.history_file(request); saved = json.loads(path.read_bytes()); saved['owner'] = 'C'*64; path.write_text(json.dumps(saved))
        self.engine.checkpoint = checkpoint
        with patch.object(os, 'fchmod', side_effect=AssertionError('bad history must prevent chmod')):
            with self.assertRaisesRegex(self.f.Rejected, 'HISTORY_CHANGED'): self.engine.apply(request, self.session)
        self.assertEqual(self.engine.inspect(request, self.session)['state'], 'Aborted')
        self.assertEqual(before, self.r.fingerprint(self.leaf.stat())); self.assertEqual(json.loads(self.history_file(request).read_bytes())['owner'], 'C'*64)

    def test_real_process_kill_at_all_five_fences_only_observes_the_original_operation(self):
        for phase in ('Preparing', 'HistoryVerified', 'Prepared', 'Applying', 'ModeChanged'):
            with self.subTest(phase=phase):
                self.leaf.chmod(0o640); request = self.request(); original = self.leaf.stat()
                child = os.fork()
                if child == 0:
                    self.engine.checkpoint = lambda point: os.kill(os.getpid(), signal.SIGKILL) if point == phase else None
                    try: self.engine.apply(request, self.session)
                    except BaseException: os._exit(2)
                    os._exit(3)
                descriptor = os.pidfd_open(child)
                try:
                    ready = select.select([descriptor], [], [], 10)[0]
                    if not ready: signal.pidfd_send_signal(descriptor, signal.SIGKILL)
                    _, status = os.waitpid(child, 0)
                    self.assertTrue(ready); self.assertTrue(os.WIFSIGNALED(status)); self.assertEqual(os.WTERMSIG(status), signal.SIGKILL)
                finally: os.close(descriptor)
                with patch.object(os, 'fchmod', side_effect=AssertionError('observers must never chmod')):
                    result = self.engine.inspect(request, self.session); replay = self.engine.apply(request, self.session)
                expected = 'Completed' if phase == 'ModeChanged' else 'Aborted'
                self.assertEqual((result['state'], replay['state']), (expected, expected))
                self.assertEqual(stat.S_IMODE(self.leaf.stat().st_mode), 0o600 if expected == 'Completed' else 0o640)
                self.assertEqual((self.leaf.stat().st_ino, self.leaf.stat().st_uid, self.leaf.stat().st_gid), (original.st_ino, original.st_uid, original.st_gid))
                self.assertEqual(self.leaf.read_bytes(), self.content)

    def test_external_change_after_prepared_is_unknown_not_adopted_or_overwritten(self):
        request = self.request()
        self.engine.checkpoint = lambda phase: self.leaf.chmod(0o644) if phase == 'Prepared' else None
        with self.assertRaisesRegex(self.r.Rejected, 'VERSION_CONFLICT'): self.engine.apply(request, self.session)
        result = self.engine.inspect(request, self.session)
        self.assertEqual(result['state'], 'Unknown'); self.assertIsNone(result['modeConfirmed']); self.assertTrue(result['historyVerified'])
        with patch.object(os, 'fchmod', side_effect=AssertionError('unknown must not reapply')):
            self.assertEqual(self.engine.apply(request, self.session)['state'], 'Unknown')
        self.assertEqual(stat.S_IMODE(self.leaf.stat().st_mode), 0o644)

    def test_replaced_source_after_mode_change_is_unknown_and_foreign_file_untouched(self):
        request = self.request(); before = self.leaf.stat()
        def replace(phase):
            if phase == 'ModeChanged':
                self.leaf.rename(self.root / 'old-inode'); self.leaf.write_bytes(b'foreign'); self.leaf.chmod(0o600)
        self.engine.checkpoint = replace; result = self.engine.apply(request, self.session)
        self.assertEqual(result['state'], 'Unknown'); self.assertIsNone(result['modeConfirmed'])
        self.assertEqual(self.leaf.read_bytes(), b'foreign'); self.assertEqual((self.root / 'old-inode').stat().st_ino, before.st_ino)
        self.assertEqual((self.root / 'old-inode').read_bytes(), self.content)

    def test_history_missing_after_mode_change_cannot_be_recreated_or_claimed_complete(self):
        request = self.request()
        self.engine.checkpoint = lambda phase: self.history_file(request).unlink() if phase == 'ModeChanged' else None
        result = self.engine.apply(request, self.session)
        self.assertEqual(result['state'], 'Unknown'); self.assertTrue(result['historyVerified']); self.assertIsNone(result['modeConfirmed'])
        self.assertFalse(self.history_file(request).exists()); self.assertEqual(stat.S_IMODE(self.leaf.stat().st_mode), 0o600)
        self.assertEqual(self.engine.discard(request, self.session)['state'], 'Unknown'); self.assertFalse(self.history_file(request).exists())

    def test_discard_before_start_is_a_permanent_fence_even_when_container_unavailable(self):
        request = self.request(); before = self.r.fingerprint(self.leaf.stat())
        result = self.engine.discard(request, None)
        self.assertEqual(result['state'], 'Aborted'); self.assertFalse(result['historyMayExist'])
        with patch.object(os, 'fchmod', side_effect=AssertionError('late apply forbidden')):
            self.assertEqual(self.engine.apply(request, self.session)['state'], 'Aborted')
        self.assertEqual(self.r.fingerprint(self.leaf.stat()), before); self.assertFalse(self.backup.exists())

    def test_completed_receipt_is_immutable_but_reports_external_change_and_missing_history(self):
        request = self.request(); self.engine.apply(request, self.session); raw = (self.state / (request['operationId'] + '.json')).read_bytes()
        self.leaf.chmod(0o644); result = self.engine.inspect(request, self.session)
        self.assertEqual(result['state'], 'Completed'); self.assertFalse(result['bindingCurrent'])
        self.history_file(request).unlink(); self.assertFalse(self.engine.inspect(request, self.session)['bindingCurrent'])
        self.assertEqual((self.state / (request['operationId'] + '.json')).read_bytes(), raw)

    def test_history_owner_domain_and_plan_cannot_be_adopted(self):
        request = self.request(); digest = self.history.capture(request); raw = self.history_file(request).read_bytes()
        for changed in ({**request, 'owner': 'C'*64}, {**request, 'plan': {**request['plan'], 'command': {**request['plan']['command'], 'mode': '0777'}}}):
            with self.assertRaisesRegex(self.f.Rejected, 'HISTORY_CHANGED'): self.history.capture(changed)
        self.history.verify(request, digest); self.assertEqual(self.history_file(request).read_bytes(), raw)
        with self.assertRaisesRegex(ValueError, 'HISTORY_DOMAIN'): candidate.ContainerPermissionHistory(backups.backups.FileBackups(self.f, namespace='container-files-v1'))

    def test_history_unsafe_namespace_or_full_capacity_refuses_without_chmod(self):
        self.backup.mkdir(mode=0o700); self.backup.chmod(0o777); request = self.request(); before = self.r.fingerprint(self.leaf.stat())
        with self.assertRaisesRegex(self.f.Rejected, 'HISTORY_UNSAFE'): self.engine.apply(request, self.session)
        self.assertEqual(before, self.r.fingerprint(self.leaf.stat()))
        self.backup.chmod(0o700); request = self.request(); original = os.fstatvfs; inode = self.backup.stat().st_ino
        def no_space(fd):
            if os.fstat(fd).st_ino == inode: return SimpleNamespace(f_bavail=0, f_frsize=4096, f_blocks=1000, f_flag=0)
            return original(fd)
        with patch.object(os, 'fstatvfs', side_effect=no_space):
            with self.assertRaisesRegex(self.f.Rejected, 'HISTORY_CAPACITY'): self.engine.apply(request, self.session)
        self.assertEqual(before, self.r.fingerprint(self.leaf.stat())); self.assertFalse(self.history_file(request).exists())

    def test_read_only_mount_root_and_changed_target_proof_refuse_mode_write(self):
        before = self.r.fingerprint(self.leaf.stat())
        self.session.container['HostConfig']['ReadonlyRootfs'] = True
        with self.assertRaisesRegex(self.r.Rejected, 'READ_ONLY'): self.request()
        self.session.container['HostConfig']['ReadonlyRootfs'] = False
        self.session.container['Mounts'] = [{'Destination': '/' + self.leaf.name, 'RW': True}]
        with self.assertRaisesRegex(self.r.Rejected, 'MOUNT_ROOT'): self.request()
        self.session.container['Mounts'] = []; request = self.request(); self.session.mounts = b'N'*32
        with self.assertRaisesRegex(self.r.Rejected, 'VERSION_CONFLICT'): self.engine.apply(request, self.session)
        self.assertEqual(self.r.fingerprint(self.leaf.stat()), before); self.assertFalse(self.backup.exists())

    def test_lost_container_after_mode_change_stays_unknown_after_it_returns(self):
        request = self.request()
        def lose(phase):
            if phase == 'ModeChanged': self.session.current.side_effect = self.r.Rejected('CONTAINER_NOT_RUNNING')
        self.engine.checkpoint = lose
        result = self.engine.apply(request, self.session)
        self.assertEqual(result['state'], 'Unknown'); self.assertIsNone(result['modeConfirmed'])
        self.session.current.side_effect = None
        with patch.object(os, 'fchmod', side_effect=AssertionError('a returning container must not restart operation')):
            self.assertEqual(self.engine.apply(request, self.session)['state'], 'Unknown')
        self.assertEqual(stat.S_IMODE(self.leaf.stat().st_mode), 0o600)

    def test_corrupt_types_and_completed_proof_are_not_adopted(self):
        request = self.request()
        for field, value in (('noChange', 0), ('kind', 'Symlink'), ('sourceIdentity', [True]*9)):
            changed = json.loads(json.dumps(request)); changed['plan'][field] = value
            changed['plan']['fingerprint'] = self.engine.digest(changed['plan'])
            with self.assertRaises((self.r.Rejected, self.f.Rejected)): self.engine.apply(changed, self.session)
        self.engine.apply(request, self.session); path = self.state / (request['operationId'] + '.json')
        value = json.loads(path.read_bytes()); value['modeConfirmed'] = False; path.write_text(json.dumps(value))
        with self.assertRaisesRegex(self.r.Rejected, 'RECEIPT_INVALID'): self.engine.inspect(request, self.session)

    def test_full_history_keeps_original_readable_without_allocating_per_request_locks(self):
        self.disk.policy['maxVersions'] = 1; self.disk.write_policy()
        original = self.request(); digest = self.history.capture(original)
        names = {path.name for path in self.backup.iterdir()}
        self.assertEqual(names, {'history.lock', original['operationId'] + '.json'})
        for _ in range(4):
            another = self.request()
            with self.assertRaisesRegex(self.f.Rejected, 'HISTORY_LIMIT'): self.history.capture(another)
        self.history.verify(original, digest); self.assertEqual(self.history.capture(original), digest)
        self.assertEqual({path.name for path in self.backup.iterdir()}, names)

    def test_wrong_receipt_owner_or_changed_plan_cannot_replay_original_operation(self):
        request = self.request(); self.engine.discard(request, None)
        with self.assertRaisesRegex(self.r.Rejected, 'NOT_FOUND'): self.engine.inspect({**request, 'owner': 'C'*64}, self.session)
        changed = json.loads(json.dumps(request)); changed['plan']['command']['mode'] = '0777'; changed['plan']['noChange'] = False
        changed['plan']['fingerprint'] = self.engine.digest(changed['plan'])
        with self.assertRaisesRegex(self.r.Rejected, 'IDEMPOTENCY_CONFLICT'): self.engine.apply(changed, self.session)


if __name__ == '__main__': unittest.main()
