"""Actual Linux inode/ownership semantics with explicit synthetic HDD attestation.

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

candidate = staging.load('whaledeck-container-ownership.py', 'container_ownership_candidate')
directories = staging.load('whaledeck-container-directories.py', 'container_ownership_storage')
publisher = staging.load('whaledeck-container-publish.py', 'container_ownership_writability')


@unittest.skipUnless(os.name == 'posix' and os.geteuid() == 0, 'isolated Linux root required')
class LinuxContainerOwnershipTests(unittest.TestCase):
    def setUp(self):
        self.disk = backups.FileBackupTests(); self.disk.setUp(); self.addCleanup(self.disk.tearDown)
        self.f, self.r, self.root = self.disk.engine.f, staging.read_engine, self.disk.root
        self.state = self.disk.disk / 'ownership-state'; self.state.mkdir(mode=0o700)
        self.folder = os.open(self.root, os.O_RDONLY | os.O_DIRECTORY); self.addCleanup(os.close, self.folder)
        self.session = SimpleNamespace(root=self.folder, request={'containerId': staging.ID, 'instanceId': str(os.getpid()) + ':2026-10-30T01:02:03.123456789Z'},
            mounts=b'M'*32, identity=('12345', os.getpid(), 888), current=Mock(), revalidate=Mock(),
            container={'Config': {'User': '0:0'}, 'HostConfig': {'ReadonlyRootfs': False}, 'Mounts': []})
        self.uploads = staging.uploads.ContainerUploads(self.r, self.f, self.state)
        self.publisher = publisher.ContainerPublisher(self.uploads, SimpleNamespace(u=self.uploads))
        self.history = candidate.ContainerOwnershipHistory(self.disk.engine)
        self.engine = candidate.container_ownership_type(directories.ContainerDirectories)(self.uploads, self.publisher, self.state, self.history)
        self.content = hashlib.shake_256(b'ownership-content-preserved').digest(2 * 1024**2 + 7)
        self.leaf = self.root / '中文属主.bin'; self.leaf.write_bytes(self.content); os.chown(self.leaf, 2100, 3100); self.leaf.chmod(0o640)
        os.utime(self.leaf, ns=(1700000000123456789, 1700000000123456789))
        self.backup = self.disk.target / self.history.NAMESPACE

    def command(self, path=None, uid=2200, gid=3200):
        path = self.leaf.name if path is None else path
        return {**self.session.request, 'path': path, 'uid': uid, 'gid': gid, 'expectedVersion': self.r.version(self.session.request, path, (self.root / path).lstat())}

    def request(self, command=None):
        return {'operationId': uuid.uuid4().hex, 'owner': staging.OWNER,
            'plan': self.engine.plan(self.command() if command is None else command, self.session)}

    def record(self, request): return json.loads((self.state / (request['operationId'] + '.json')).read_bytes())

    def history_file(self, request): return self.backup / (request['operationId'] + '.json')

    def test_plan_distinguishes_execution_identity_source_owner_and_requested_owner_without_writes(self):
        before = self.r.fingerprint(self.leaf.stat()); parent = self.r.fingerprint(self.root.stat())
        plan = self.request()['plan']
        # This fixture's pinned process is this Linux-root test process; the
        # native Docker gate separately proves a non-root container process.
        self.assertEqual([plan['uid'], plan['gid']], [os.geteuid(), os.getegid()])
        self.assertEqual(plan['sourceIdentity'][3:5], [2100, 3100])
        self.assertEqual([plan['command']['uid'], plan['command']['gid']], [2200, 3200])
        self.assertEqual(plan['sourceIdentity'], list(before)); self.assertFalse(plan['noChange'])
        self.assertEqual(before, self.r.fingerprint(self.leaf.stat())); self.assertEqual(parent, self.r.fingerprint(self.root.stat()))
        self.assertFalse(self.backup.exists()); self.assertEqual([], list(self.state.iterdir()))

    def test_actual_fchown_preserves_inode_content_mode_mtime_and_hdd_original_owner(self):
        request = self.request(); before = self.leaf.stat()
        result = self.engine.apply(request, self.session); after = self.leaf.stat()
        self.assertEqual((result['state'], result['ownerConfirmed'], result['bindingCurrent']), ('Completed', True, True))
        self.assertEqual((after.st_uid, after.st_gid), (2200, 3200))
        self.assertEqual((after.st_dev, after.st_ino, after.st_mode, after.st_mtime_ns, after.st_size),
            (before.st_dev, before.st_ino, before.st_mode, before.st_mtime_ns, before.st_size))
        self.assertEqual(self.leaf.read_bytes(), self.content)
        saved = json.loads(self.history_file(request).read_bytes())
        self.assertEqual(saved['resourceType'], 'ContainerOwnershipMetadata'); self.assertEqual(saved['plan']['sourceIdentity'][3:5], [2100, 3100])
        self.assertEqual(saved['historyDigest'], result['historyDigest']); self.assertTrue(result['historyVerified'])
        self.assertEqual(stat.S_IMODE(self.history_file(request).stat().st_mode), 0o600)
        self.assertEqual(stat.S_IMODE(self.backup.stat().st_mode), 0o700)
        self.assertEqual(self.engine.digest(request['plan']), request['plan']['fingerprint'])

    def test_owner_and_group_can_each_change_independently_and_root_is_explicit_not_a_sentinel(self):
        for uid, gid in ((2100, 3200), (2200, 3100), (0, 0), (2100, 3100)):
            with self.subTest(uid=uid, gid=gid):
                before = self.leaf.stat(); result = self.engine.apply(self.request(self.command(uid=uid, gid=gid)), self.session)
                self.assertEqual((self.leaf.stat().st_uid, self.leaf.stat().st_gid), (uid, gid))
                self.assertEqual(self.leaf.stat().st_mode, before.st_mode); self.assertEqual(self.leaf.stat().st_ino, before.st_ino)
                self.assertEqual(self.leaf.stat().st_mtime_ns, before.st_mtime_ns); self.assertEqual(result['state'], 'Completed')
                self.assertEqual(self.leaf.read_bytes(), self.content)

    def test_no_change_never_calls_fchown_or_changes_ctime(self):
        request = self.request(self.command(uid=2100, gid=3100)); before = self.r.fingerprint(self.leaf.stat())
        self.assertTrue(request['plan']['noChange'])
        with patch.object(os, 'fchown', side_effect=AssertionError('no-change must not call fchown')):
            result = self.engine.apply(request, self.session)
        self.assertEqual(before, self.r.fingerprint(self.leaf.stat())); self.assertEqual(result['state'], 'Completed')

    def test_complete_and_discard_replays_never_repeat_fchown_or_rewrite_permanent_history(self):
        request = self.request(); self.engine.apply(request, self.session)
        record = (self.state / (request['operationId'] + '.json')).read_bytes(); history = self.history_file(request).read_bytes()
        current = self.r.fingerprint(self.leaf.stat())
        with patch.object(os, 'fchown', side_effect=AssertionError('no repeated mutation')):
            self.assertTrue(self.engine.apply(request, self.session)['replayed'])
            self.assertEqual(self.engine.discard(request, self.session)['state'], 'Completed')
        self.assertEqual(record, (self.state / (request['operationId'] + '.json')).read_bytes())
        self.assertEqual(history, self.history_file(request).read_bytes()); self.assertEqual(current, self.r.fingerprint(self.leaf.stat()))

    def test_directory_changes_only_its_own_inode_and_preserves_mixed_children(self):
        folder = self.root / '目录'; folder.mkdir(mode=0o750); os.chown(folder, 2300, 3300)
        child = folder / 'child'; child.write_bytes(self.content); os.chown(child, 2400, 3400); child.chmod(0o640)
        empty = folder / 'empty'; empty.mkdir(mode=0o700); os.chown(empty, 2500, 3500)
        old = self.r.fingerprint(folder.stat()); children = {p.name: self.r.fingerprint(p.stat()) for p in folder.iterdir()}
        result = self.engine.apply(self.request(self.command(folder.name)), self.session)
        self.assertEqual((result['state'], result['kind']), ('Completed', 'Directory'))
        self.assertEqual((folder.stat().st_uid, folder.stat().st_gid), (2200, 3200))
        self.assertEqual((folder.stat().st_ino, folder.stat().st_mode, folder.stat().st_mtime_ns), (old[1], old[2], old[7]))
        self.assertEqual(children, {p.name: self.r.fingerprint(p.stat()) for p in folder.iterdir()}); self.assertEqual(child.read_bytes(), self.content)

    def test_numeric_ids_reject_boolean_float_string_negative_and_no_change_sentinel(self):
        for key in ('uid', 'gid'):
            for value in (True, False, 1.0, '1000', -1, 4294967295, 4294967296, None, [], {}):
                with self.subTest(key=key, value=value), self.assertRaisesRegex(self.r.Rejected, 'UID_GID_INVALID'):
                    self.engine.plan({**self.command(), key: value}, self.session)
        for value in (0, 4294967294):
            plan = self.engine.plan(self.command(uid=value, gid=value), self.session)
            self.assertEqual(plan['command']['uid'], value)

    def test_mode_recursive_acl_and_host_paths_are_not_ownership_authority(self):
        for extra in ({'mode': '0777'}, {'recursive': True}, {'acl': []}, {'path': '../escape'}, {'path': '/etc/shadow'}):
            with self.subTest(extra=extra), self.assertRaises((self.r.Rejected, self.f.Rejected)):
                self.engine.plan({**self.command(), **extra}, self.session)
        self.assertFalse(self.backup.exists())

    def test_special_modes_links_and_non_regular_objects_are_refused_without_chown(self):
        with patch.object(os, 'fchown', side_effect=AssertionError('must not mutate')):
            self.leaf.chmod(0o4640)
            with self.assertRaisesRegex(self.r.Rejected, 'SOURCE_UNSUPPORTED'): self.request()
            self.leaf.chmod(0o640); os.link(self.leaf, self.root / 'hardlink')
            with self.assertRaisesRegex(self.r.Rejected, 'SOURCE_UNSUPPORTED'): self.request()
            (self.root / 'hardlink').unlink(); (self.root / 'link').symlink_to(self.leaf)
            with self.assertRaises((OSError, self.r.Rejected)): self.request(self.command('link'))
            os.mkfifo(self.root / 'fifo')
            with self.assertRaisesRegex(self.r.Rejected, 'SOURCE_UNSUPPORTED'): self.request(self.command('fifo'))

    def test_existing_acl_is_preserved_and_not_silently_reinterpreted(self):
        acl = struct.pack('<I', 2) + b''.join(struct.pack('<HHI', *entry) for entry in
            [(1, 6, 0xffffffff), (2, 4, 2300), (4, 4, 0xffffffff), (16, 4, 0xffffffff), (32, 0, 0xffffffff)])
        os.setxattr(self.leaf, 'system.posix_acl_access', acl); before = self.r.fingerprint(self.leaf.stat())
        with self.assertRaisesRegex(self.r.Rejected, 'EXTENDED_METADATA_REQUIRED'): self.request()
        self.assertEqual(os.getxattr(self.leaf, 'system.posix_acl_access'), acl); self.assertEqual(before, self.r.fingerprint(self.leaf.stat()))

    def test_security_xattrs_refused_without_reading_values(self):
        with patch.object(os, 'listxattr', return_value=['security.capability']), patch.object(os, 'getxattr', side_effect=AssertionError('no secret/security reads')):
            with self.assertRaisesRegex(self.r.Rejected, 'EXTENDED_METADATA_REQUIRED'): self.request()

    def test_user_attributes_are_unchanged_and_only_a_digest_is_saved(self):
        value = b'private-user-attribute'; os.setxattr(self.leaf, 'user.ownership-test', value)
        request = self.request(); self.engine.apply(request, self.session)
        self.assertEqual(os.getxattr(self.leaf, 'user.ownership-test'), value)
        self.assertNotIn(value.decode(), self.history_file(request).read_text())

    def test_source_or_parent_replacement_after_plan_is_refused_before_history(self):
        request = self.request(); self.leaf.rename(self.root / 'old'); self.leaf.write_bytes(b'foreign'); self.leaf.chmod(0o640)
        before = self.r.fingerprint(self.leaf.stat())
        with self.assertRaisesRegex(self.r.Rejected, 'VERSION_CONFLICT'): self.engine.apply(request, self.session)
        self.assertEqual(self.leaf.read_bytes(), b'foreign'); self.assertEqual(before, self.r.fingerprint(self.leaf.stat())); self.assertFalse(self.backup.exists())

    def test_hdd_failure_records_aborted_then_never_retries_backup_or_owner_change(self):
        request = self.request(); before = self.r.fingerprint(self.leaf.stat())
        with patch.object(self.history, 'capture', side_effect=self.f.Rejected('FILES_BACKUP_DEVICE_MISMATCH')):
            with self.assertRaisesRegex(self.f.Rejected, 'DEVICE_MISMATCH'): self.engine.apply(request, self.session)
        result = self.engine.inspect(request, self.session); self.assertEqual(result['state'], 'Aborted'); self.assertFalse(result['ownerConfirmed'])
        with patch.object(os, 'fchown', side_effect=AssertionError('must not retry')), patch.object(self.history, 'capture', side_effect=AssertionError('must not recapture')):
            self.assertEqual(self.engine.apply(request, self.session)['state'], 'Aborted')
        self.assertEqual(before, self.r.fingerprint(self.leaf.stat()))

    def test_missing_chown_privilege_keeps_original_and_verified_hdd_without_retry(self):
        request = self.request(); before = self.r.fingerprint(self.leaf.stat())
        with patch.object(os, 'fchown', side_effect=PermissionError('explicit kernel denial')):
            with self.assertRaises(PermissionError): self.engine.apply(request, self.session)
        result = self.engine.inspect(request, self.session)
        self.assertEqual((result['state'], result['ownerConfirmed'], result['historyVerified']), ('Aborted', False, True))
        self.assertEqual(before, self.r.fingerprint(self.leaf.stat()))

    def test_fsync_failure_after_actual_chown_observes_completed_not_false_aborted(self):
        request = self.request(); real_fsync = os.fsync; inode = self.leaf.stat().st_ino
        def fail_source(fd):
            if os.fstat(fd).st_ino == inode: raise OSError('synthetic fsync error after real fchown')
            return real_fsync(fd)
        with patch.object(os, 'fsync', side_effect=fail_source):
            with self.assertRaises(OSError): self.engine.apply(request, self.session)
        with patch.object(os, 'fchown', side_effect=AssertionError('must not repeat')):
            result = self.engine.inspect(request, self.session)
        self.assertEqual((result['state'], result['ownerConfirmed']), ('Completed', True)); self.assertEqual((self.leaf.stat().st_uid, self.leaf.stat().st_gid), (2200, 3200))

    def test_real_process_sigkill_at_each_fence_recovers_only_original_operation(self):
        for phase in ('Preparing', 'HistoryVerified', 'Prepared', 'Applying', 'OwnerChanged'):
            with self.subTest(phase=phase):
                os.chown(self.leaf, 2100, 3100); request = self.request(); original = self.leaf.stat()
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
                with patch.object(os, 'fchown', side_effect=AssertionError('recovery cannot redo')):
                    result = self.engine.inspect(request, self.session); replay = self.engine.apply(request, self.session)
                expected = 'Completed' if phase == 'OwnerChanged' else 'Aborted'
                self.assertEqual((result['state'], replay['state']), (expected, expected))
                self.assertEqual((self.leaf.stat().st_uid, self.leaf.stat().st_gid), (2200, 3200) if expected == 'Completed' else (2100, 3100))
                self.assertEqual((self.leaf.stat().st_ino, self.leaf.stat().st_mode, self.leaf.stat().st_mtime_ns), (original.st_ino, original.st_mode, original.st_mtime_ns))
                self.assertEqual(self.leaf.read_bytes(), self.content)

    def test_external_change_after_prepared_is_unknown_and_not_adopted(self):
        request = self.request(); captured = {}
        def replace(phase):
            if phase == 'Prepared':
                self.leaf.rename(self.root / 'original-kept'); self.leaf.write_bytes(b'foreign'); self.leaf.chmod(0o600)
                captured['identity'] = self.r.fingerprint(self.leaf.stat())
        self.engine.checkpoint = replace
        with self.assertRaises((self.r.Rejected, self.f.Rejected)): self.engine.apply(request, self.session)
        self.assertEqual(self.engine.inspect(request, self.session)['state'], 'Unknown')
        with patch.object(os, 'fchown', side_effect=AssertionError('Unknown must not redo')):
            self.assertEqual(self.engine.apply(request, self.session)['state'], 'Unknown')
        self.assertEqual(captured['identity'], self.r.fingerprint(self.leaf.stat())); self.assertEqual(self.leaf.read_bytes(), b'foreign')
        self.assertEqual((self.root / 'original-kept').read_bytes(), self.content)

    def test_lost_container_after_owner_change_remains_unknown_when_returning(self):
        request = self.request()
        def lose(phase):
            if phase == 'OwnerChanged': self.session.current.side_effect = self.r.Rejected('CONTAINER_NOT_RUNNING')
        self.engine.checkpoint = lose
        result = self.engine.apply(request, self.session); self.assertEqual(result['state'], 'Unknown'); self.assertIsNone(result['ownerConfirmed'])
        self.session.current.side_effect = None
        with patch.object(os, 'fchown', side_effect=AssertionError('no delayed write')):
            self.assertEqual(self.engine.apply(request, self.session)['state'], 'Unknown')

    def test_discard_before_start_is_permanent_even_without_container(self):
        request = self.request(); before = self.r.fingerprint(self.leaf.stat())
        self.assertEqual(self.engine.discard(request, None)['state'], 'Aborted')
        with patch.object(os, 'fchown', side_effect=AssertionError('no late write')):
            self.assertEqual(self.engine.apply(request, self.session)['state'], 'Aborted')
        self.assertEqual(before, self.r.fingerprint(self.leaf.stat())); self.assertFalse(self.backup.exists())

    def test_history_tamper_or_missing_after_change_never_recreated_or_claimed_current(self):
        request = self.request(); self.engine.apply(request, self.session); root = (self.state / (request['operationId'] + '.json')).read_bytes()
        self.history_file(request).unlink()
        with patch.object(os, 'fchown', side_effect=AssertionError('no owner redo')):
            result = self.engine.apply(request, self.session)
        self.assertEqual(result['state'], 'Completed'); self.assertFalse(result['bindingCurrent']); self.assertFalse(self.history_file(request).exists())
        self.assertEqual(root, (self.state / (request['operationId'] + '.json')).read_bytes())

    def test_completed_proof_cannot_adopt_changed_uid_or_mode(self):
        request = self.request(); self.engine.apply(request, self.session); path = self.state / (request['operationId'] + '.json')
        original = json.loads(path.read_bytes())
        for index in (2, 3, 4, 7):
            value = json.loads(json.dumps(original)); value['actualIdentity'][index] += 1; path.write_text(json.dumps(value))
            with self.subTest(index=index), self.assertRaisesRegex(self.r.Rejected, 'RECEIPT_INVALID'): self.engine.inspect(request, self.session)
        path.write_text(json.dumps(original))

    def test_history_capacity_namespace_and_max_versions_fail_closed_with_original_preserved(self):
        request = self.request(); before = self.r.fingerprint(self.leaf.stat()); self.backup.mkdir(mode=0o777); self.backup.chmod(0o777)
        with self.assertRaisesRegex(self.f.Rejected, 'HISTORY_UNSAFE'): self.engine.apply(request, self.session)
        self.assertEqual(before, self.r.fingerprint(self.leaf.stat()))
        self.backup.chmod(0o700); self.disk.policy['maxVersions'] = 1; self.disk.write_policy()
        original = self.request(); digest = self.history.capture(original)
        for _ in range(2):
            with self.assertRaisesRegex(self.f.Rejected, 'HISTORY_LIMIT'): self.history.capture(self.request())
        self.history.verify(original, digest); self.assertEqual(before, self.r.fingerprint(self.leaf.stat()))

    def test_readonly_mount_root_and_changed_container_binding_are_not_bypassed(self):
        before = self.r.fingerprint(self.leaf.stat()); self.session.container['HostConfig']['ReadonlyRootfs'] = True
        with self.assertRaisesRegex(self.r.Rejected, 'READ_ONLY'): self.request()
        self.session.container['HostConfig']['ReadonlyRootfs'] = False; self.session.container['Mounts'] = [{'Destination': '/' + self.leaf.name, 'RW': True}]
        with self.assertRaisesRegex(self.r.Rejected, 'MOUNT_ROOT'): self.request()
        self.session.container['Mounts'] = []; request = self.request(); self.session.mounts = b'N' * 32
        with self.assertRaisesRegex(self.r.Rejected, 'VERSION_CONFLICT'): self.engine.apply(request, self.session)
        self.assertEqual(before, self.r.fingerprint(self.leaf.stat())); self.assertFalse(self.backup.exists())


if __name__ == '__main__': unittest.main()
