"""Real Linux filesystem/root mutation semantics, synthetic pinned Docker scope.

These are engine tests. They are not evidence of a deployed RPC, BFF or UI.
"""
import json
import os
from pathlib import Path
import select
import signal
import stat
import tempfile
import unittest
from unittest.mock import patch
import uuid

import test_container_file_staging as common

directories = common.load('whaledeck-container-directories.py', 'container_directory_candidate')
publish = common.load('whaledeck-container-publish.py', 'container_directory_publication_policy')


@unittest.skipUnless(os.name == 'posix' and hasattr(os, 'geteuid') and os.geteuid() == 0, 'requires isolated Linux root')
class LinuxContainerDirectoryTests(unittest.TestCase):
    def setUp(self):
        self.fixture = common.LinuxContainerStagingTests(); self.fixture.setUp()
        self.addCleanup(self.fixture.doCleanups)
        self.f, self.r, self.u = self.fixture.files, self.fixture.engine.r, self.fixture.engine
        self.root, self.session = self.fixture.root, self.fixture.session
        self.session.container = {'HostConfig': {'ReadonlyRootfs': False}, 'Mounts': []}
        self.session.request['instanceId'] = str(os.getpid()) + ':2026-10-27T01:02:03.123456789Z'
        # Directory creation needs only the publication/owner policy. Never
        # invent an HDD backup for a previously absent directory.
        self.publisher = publish.ContainerPublisher(self.u, type('Backups', (), {'u': self.u})())
        self.state = self.fixture.base / 'directories'; self.state.mkdir(mode=0o700)
        self.engine = directories.ContainerDirectories(self.u, self.publisher, self.state)

    def command(self, path='新建目录', mode='0750'):
        return {**self.session.request, 'path': path, 'mode': mode}

    def request(self, command=None):
        return {'operationId': uuid.uuid4().hex, 'owner': common.OWNER,
            'plan': self.engine.plan(self.command() if command is None else command, self.session)}

    def record(self, request):
        return json.loads((self.state / (request['operationId'] + '.json')).read_text())

    def pending(self, request):
        command = request['plan']['command']
        return self.root / command['path'].rpartition('/')[0] / self.engine.PRIVATE / (request['operationId'] + '.directory')

    def test_plan_is_read_only_binds_parent_instance_proof_owner_and_mode(self):
        before = self.r.fingerprint(self.root.stat()); contents = set(self.root.iterdir())
        command = self.command(); plan = self.engine.plan(command, self.session)
        self.assertEqual(before, self.r.fingerprint(self.root.stat())); self.assertEqual(contents, set(self.root.iterdir()))
        self.assertEqual([], list(self.state.iterdir())); self.assertEqual(command, plan['command'])
        self.assertEqual((os.geteuid(), os.getegid()), (plan['uid'], plan['gid']))
        self.assertEqual(self.u.target_proof(self.session), plan['targetProof'])
        self.assertEqual([self.root.stat().st_dev, self.root.stat().st_ino], plan['parentIdentity'])
        self.assertEqual(self.engine.digest(plan), plan['fingerprint'])

    def test_create_unicode_directory_exact_owner_and_mode_idempotent_replay(self):
        request = self.request(); result = self.engine.apply(request, self.session)
        folder = self.root / '新建目录'; info = folder.stat()
        self.assertTrue(stat.S_ISDIR(info.st_mode)); self.assertEqual(0o750, stat.S_IMODE(info.st_mode))
        self.assertEqual((os.geteuid(), os.getegid()), (info.st_uid, info.st_gid))
        self.assertEqual('Completed', result['state']); self.assertTrue(result['bindingCurrent']); self.assertFalse(result['replayed'])
        self.assertEqual(self.r.version(request['plan']['command'], '新建目录', info), result['version'])
        with patch.object(self.f, 'exchange', side_effect=AssertionError('must not rename again')):
            replay = self.engine.apply(request, self.session)
        self.assertTrue(replay['replayed']); self.assertEqual(result['version'], replay['version'])
        self.assertEqual(info.st_ino, folder.stat().st_ino); self.assertFalse(self.pending(request).exists())

    def test_terminal_result_stays_completed_but_reports_external_change(self):
        request = self.request(); self.engine.apply(request, self.session)
        child = self.root / '新建目录/external'; child.write_bytes(b'preserve external content')
        result = self.engine.inspect(request, self.session)
        self.assertEqual('Completed', result['state']); self.assertFalse(result['bindingCurrent'])
        self.assertEqual('Completed', self.engine.discard(request, self.session)['state'])
        self.assertEqual(b'preserve external content', child.read_bytes())

    def test_existing_file_directory_or_symlink_never_overwritten_or_followed(self):
        for kind in ('file', 'directory', 'symlink'):
            path = self.root / kind
            if kind == 'file': path.write_bytes(b'keep')
            elif kind == 'directory': path.mkdir()
            else: path.symlink_to(self.root / 'missing')
            before = self.r.fingerprint(path.lstat())
            with self.assertRaisesRegex(self.r.Rejected, 'TARGET_EXISTS'): self.engine.plan(self.command(kind), self.session)
            self.assertEqual(before, self.r.fingerprint(path.lstat()))
        self.assertEqual([], list(self.state.glob('*.json')))

    def test_stale_parent_or_target_rejects_before_permanent_root_intent(self):
        request = self.request(); (self.root / 'external').touch()
        with self.assertRaisesRegex(self.r.Rejected, 'VERSION_CONFLICT'): self.engine.apply(request, self.session)
        self.assertEqual([], list(self.state.glob('*.json')))
        request = self.request(); (self.root / '新建目录').write_bytes(b'foreign')
        with self.assertRaisesRegex(self.r.Rejected, 'TARGET_EXISTS'): self.engine.apply(request, self.session)
        self.assertEqual(b'foreign', (self.root / '新建目录').read_bytes())

    def test_special_modes_arbitrary_paths_virtual_secrets_and_extra_fields_rejected(self):
        for mode in ('1755', '2755', '4755', '755', '0999', '', None, True):
            with self.subTest(mode=mode), self.assertRaises(self.r.Rejected): self.engine.plan(self.command(mode=mode), self.session)
        for path in ('', '/absolute', '../outside', 'a/../b', 'a//b', 'proc/new', 'sys/new', 'dev/new', 'run/new',
                'var/run/new', '.env', '.whaledeck-internal/new', 'nested/shadow', 'a\x00b'):
            with self.subTest(path=path), self.assertRaises((self.r.Rejected, self.f.Rejected)):
                self.engine.plan(self.command(path), self.session)
        with self.assertRaises(self.r.Rejected): self.engine.plan({**self.command(), 'uid': 0}, self.session)

    def test_readonly_root_mount_root_and_readonly_bind_are_refused(self):
        for mounts, readonly in (([], True), ([{'Destination': '/新建目录', 'RW': True}], False),
                ([{'Destination': '/work', 'RW': False}], False)):
            self.session.container = {'HostConfig': {'ReadonlyRootfs': readonly}, 'Mounts': mounts}
            command = self.command('work/new' if mounts and mounts[0]['Destination'] == '/work' else '新建目录')
            if command['path'].startswith('work/'): (self.root / 'work').mkdir(exist_ok=True)
            with self.assertRaisesRegex(self.r.Rejected, 'PUBLISH_(READ_ONLY|MOUNT_ROOT)'): self.engine.plan(command, self.session)

    def test_writable_bind_under_readonly_root_and_symlink_parent_policy(self):
        (self.root / 'work').mkdir()
        self.session.container = {'HostConfig': {'ReadonlyRootfs': True}, 'Mounts': [{'Destination': '/work', 'RW': True}]}
        request = self.request(self.command('work/子目录')); self.assertEqual('Completed', self.engine.apply(request, self.session)['state'])
        (self.root / 'link').symlink_to(self.root / 'work', target_is_directory=True)
        with self.assertRaises(OSError): self.engine.plan(self.command('link/not-allowed'), self.session)

    def test_foreign_session_instance_reconfiguration_and_owner_changes_rejected(self):
        request = self.request()
        self.session.mounts = b'Z' * 32
        with self.assertRaisesRegex(self.r.Rejected, 'VERSION_CONFLICT'): self.engine.apply(request, self.session)
        self.session.mounts = b'M' * 32
        original = self.session.request['instanceId']; self.session.request['instanceId'] = '888:2026-10-27T01:02:03Z'
        with self.assertRaisesRegex(self.r.Rejected, 'SESSION_SCOPE_MISMATCH'): self.engine.apply(request, self.session)
        self.session.request['instanceId'] = original
        with patch.object(self.publisher, 'owner', return_value=[1000, 1001]):
            with self.assertRaisesRegex(self.r.Rejected, 'VERSION_CONFLICT'): self.engine.apply(request, self.session)
        self.assertFalse((self.root / '新建目录').exists())

    def test_cancel_before_apply_fences_delayed_requests_without_live_container(self):
        request = self.request(); self.assertEqual('Aborted', self.engine.discard(request)['state'])
        with patch.object(self.engine, 'plan', side_effect=AssertionError('late plan')), patch.object(self.f, 'exchange', side_effect=AssertionError('late rename')):
            self.assertEqual('Aborted', self.engine.apply(request, None)['state'])
        self.assertEqual('Aborted', self.engine.discard(request)['state']); self.assertFalse((self.root / '新建目录').exists())

    def test_foreign_owner_plan_change_and_corrupt_receipt_never_alias(self):
        request = self.request(); self.engine.discard(request)
        with self.assertRaisesRegex(self.r.Rejected, 'NOT_FOUND'): self.engine.inspect({**request, 'owner': 'C' * 64}, self.session)
        modified = {**request['plan'], 'uid': 123}; modified['fingerprint'] = self.engine.digest(modified)
        with self.assertRaisesRegex(self.r.Rejected, 'IDEMPOTENCY_CONFLICT'): self.engine.apply({**request, 'plan': modified}, self.session)
        path = self.state / (request['operationId'] + '.json'); path.write_text('{}'); path.chmod(0o600)
        with self.assertRaisesRegex(self.r.Rejected, 'RECEIPT_INVALID'): self.engine.inspect(request, self.session)

    def test_unsafe_or_symlink_state_lock_and_private_directory_refused(self):
        request = self.request()
        lock = self.state / (request['operationId'] + '.lock'); lock.symlink_to(self.fixture.leaf)
        with self.assertRaises(OSError): self.engine.apply(request, self.session)
        lock.unlink(); self.state.chmod(0o755)
        with self.assertRaisesRegex(self.r.Rejected, 'STATE_UNSAFE'): self.engine.apply(request, self.session)
        self.state.chmod(0o700)
        private = self.root / self.engine.PRIVATE; private.symlink_to(self.state, target_is_directory=True)
        # A private-directory error must leave an honest unknown receipt, not
        # assume no side effect or recursively clean the foreign directory.
        request = self.request()
        with self.assertRaises(OSError): self.engine.apply(request, self.session)
        self.assertEqual('Unknown', self.record(request)['state']); self.assertTrue(private.is_symlink())

    def test_nonempty_owned_pending_directory_is_preserved_as_unknown(self):
        request = self.request(); original = self.engine.save
        def interrupt(state, record):
            original(state, record)
            if record['state'] == 'Prepared':
                (self.pending(request) / 'foreign').write_bytes(b'keep')
                raise RuntimeError('lost response')
        with patch.object(self.engine, 'save', side_effect=interrupt):
            with self.assertRaises(RuntimeError): self.engine.apply(request, self.session)
        self.assertEqual('Unknown', self.record(request)['state'])
        self.assertEqual(b'keep', (self.pending(request) / 'foreign').read_bytes())
        self.assertFalse((self.root / '新建目录').exists())

    def test_replaced_private_inode_cannot_publish_and_foreign_empty_directory_is_preserved(self):
        request = self.request(); original = self.engine.save
        def replace(state, record):
            original(state, record)
            if record['state'] == 'Prepared':
                # Keep the old inode alive; do not rely on inode recycling.
                self.pending(request).rename(self.pending(request).with_suffix('.original'))
                self.pending(request).mkdir()
        with patch.object(self.engine, 'save', side_effect=replace):
            with self.assertRaisesRegex(self.r.Rejected, 'PRIVATE_CHANGED'): self.engine.apply(request, self.session)
        self.assertEqual('Unknown', self.record(request)['state']); self.assertTrue(self.pending(request).exists())
        self.assertFalse((self.root / '新建目录').exists())

    def test_external_permission_change_in_prepared_directory_is_not_cleaned_or_published(self):
        request = self.request(); original = self.engine.save
        def change(state, record):
            original(state, record)
            if record['state'] == 'Prepared': self.pending(request).chmod(0o777)
        with patch.object(self.engine, 'save', side_effect=change):
            with self.assertRaisesRegex(self.r.Rejected, 'PRIVATE_CHANGED'): self.engine.apply(request, self.session)
        self.assertEqual('Unknown', self.record(request)['state'])
        self.assertEqual(0o777, stat.S_IMODE(self.pending(request).stat().st_mode)); self.assertFalse((self.root / '新建目录').exists())

    def test_target_race_during_atomic_publish_preserves_external_file(self):
        request = self.request(); exchange = self.f.exchange
        def race(*args):
            (self.root / '新建目录').write_bytes(b'external content')
            return exchange(*args)
        with patch.object(self.f, 'exchange', side_effect=race):
            with self.assertRaises(FileExistsError): self.engine.apply(request, self.session)
        self.assertEqual(b'external content', (self.root / '新建目录').read_bytes())
        self.assertEqual('Unknown', self.record(request)['state'])

    def test_malformed_plan_types_fields_or_digest_are_rejected_before_locks(self):
        request = self.request()
        for field, value in (('schemaVersion', True), ('uid', True), ('gid', -1), ('parentIdentity', [0, True]),
                ('parentVersion', 'invalid'), ('fingerprint', 'F' * 64), ('unexpected', 1)):
            with self.subTest(field=field), self.assertRaises(self.r.Rejected):
                self.engine.discard({**request, 'plan': {**request['plan'], field: value}})
        self.assertEqual([], list(self.state.iterdir()))

    def kill_at(self, request, point):
        ready_read, ready_write = os.pipe(); continue_read, continue_write = os.pipe()
        pid = os.fork()
        if pid == 0:
            os.close(ready_read); os.close(continue_write)
            def pause():
                os.write(ready_write, b'R'); os.read(continue_read, 1)
            original_save, original_exchange = self.engine.save, self.f.exchange
            def save(state, record):
                original_save(state, record)
                if point == record['state']: pause()
            def exchange(*args):
                value = original_exchange(*args)
                if point == 'after-rename': pause()
                return value
            try:
                with patch.object(self.engine, 'save', side_effect=save), patch.object(self.f, 'exchange', side_effect=exchange):
                    self.engine.apply(request, self.session)
                os._exit(3)
            except BaseException: os._exit(4)
        os.close(ready_write); os.close(continue_read)
        try:
            self.assertTrue(select.select([ready_read], [], [], 10)[0], point)
            self.assertEqual(b'R', os.read(ready_read, 1))
            os.kill(pid, signal.SIGKILL)
            self.assertEqual((pid, signal.SIGKILL), os.waitpid(pid, 0))
        finally:
            os.close(ready_read); os.close(continue_write)
            try: os.kill(pid, signal.SIGKILL); os.waitpid(pid, 0)
            except ProcessLookupError: pass
            except ChildProcessError: pass

    def test_sigkill_after_preparing_leaves_durable_abort_without_directory(self):
        request = self.request(); self.kill_at(request, 'Preparing')
        self.assertEqual('Preparing', self.record(request)['state'])
        self.assertEqual('Aborted', self.engine.inspect(request, self.session)['state'])
        self.assertFalse((self.root / '新建目录').exists())
        self.assertEqual('Aborted', self.engine.apply(request, self.session)['state'])

    def test_sigkill_after_private_preparation_only_removes_exact_empty_private_inode(self):
        request = self.request(); self.kill_at(request, 'Prepared')
        self.assertTrue(self.pending(request).is_dir())
        self.assertEqual('Aborted', self.engine.inspect(request, self.session)['state'])
        self.assertFalse(self.pending(request).exists()); self.assertFalse((self.root / '新建目录').exists())

    def test_sigkill_before_rename_never_replays_publication(self):
        request = self.request(); self.kill_at(request, 'Applying')
        self.assertEqual('Applying', self.record(request)['state'])
        with patch.object(self.f, 'exchange', side_effect=AssertionError('no second write')):
            self.assertEqual('Aborted', self.engine.inspect(request, self.session)['state'])
            self.assertEqual('Aborted', self.engine.apply(request, self.session)['state'])
        self.assertFalse((self.root / '新建目录').exists())

    def test_sigkill_after_rename_proves_completed_inode_without_second_write(self):
        request = self.request(); self.kill_at(request, 'after-rename')
        inode = (self.root / '新建目录').stat().st_ino
        self.assertEqual('Applying', self.record(request)['state'])
        with patch.object(self.f, 'exchange', side_effect=AssertionError('no second write')):
            result = self.engine.inspect(request, self.session)
            self.assertEqual('Completed', result['state']); self.assertTrue(result['bindingCurrent'])
            self.assertEqual('Completed', self.engine.apply(request, self.session)['state'])
        self.assertEqual(inode, (self.root / '新建目录').stat().st_ino)

    def test_published_inode_removed_before_observation_is_unknown_not_abort_or_success(self):
        request = self.request(); self.kill_at(request, 'after-rename')
        (self.root / '新建目录').rmdir()
        self.assertEqual('Unknown', self.engine.inspect(request, self.session)['state'])
        self.assertEqual('Unknown', self.engine.apply(request, self.session)['state'])
        self.assertFalse((self.root / '新建目录').exists())

    def test_live_operation_lock_refuses_concurrent_apply_or_discard(self):
        request = self.request()
        with self.engine.locked(request):
            with self.assertRaisesRegex(self.r.Rejected, 'BUSY'): self.engine.apply(request, self.session)
            with self.assertRaisesRegex(self.r.Rejected, 'BUSY'): self.engine.discard(request)


if __name__ == '__main__': unittest.main()
