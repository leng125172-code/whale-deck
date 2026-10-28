"""Container-domain copy receipts on real isolated Linux filesystems.

Docker identity/liveness is synthetic here. Child SIGKILL stops only the test's
forked candidate engine; no installed Agent, product process or SQL is changed.
"""
import copy
import hashlib
import json
import os
import select
import signal
import stat
import unittest
from unittest.mock import patch
import uuid

import test_container_copy_scope as common

intents = common.common.common.load('whaledeck-container-copies.py', 'container_copy_intents')


@unittest.skipUnless(os.name == 'posix' and hasattr(os, 'geteuid') and os.geteuid() == 0, 'requires isolated Linux root')
class LinuxContainerCopyIntentsTests(unittest.TestCase):
    def setUp(self):
        self.fixture = common.LinuxContainerCopyScopeTests(); self.fixture.setUp()
        self.addCleanup(self.fixture.doCleanups)
        self.root, self.session, self.state = self.fixture.root, self.fixture.session, self.fixture.state
        parent = self.fixture.fixture
        self.f, self.r = parent.f, parent.r
        self.engine = intents.ContainerCopies(parent.u, parent.publisher, common.scope_module.ContainerCopyScope,
            common.operations.FileOperations, common.copies.FileTreeCopies, common.trees.FileTreeManifest, self.state)

    def request(self, command=None):
        return {'operationId': uuid.uuid4().hex, 'owner': 'B' * 64,
                'plan': self.engine.plan(self.fixture.command() if command is None else command, self.session)}

    def record_path(self, request): return self.state / (request['operationId'] + '.json')
    def record(self, request): return json.loads(self.record_path(request).read_text())

    def inner_path(self, request):
        return self.state / 'engines' / ('operations' if request['plan']['kind'] == 'File' else 'tree-copies') / (request['operationId'] + '.json')

    def test_plan_is_read_only_seals_container_and_inner_domains_parent_owner_and_kind(self):
        before = self.r.fingerprint(self.root.stat()); request = self.request(); plan = request['plan']
        self.assertEqual(before, self.r.fingerprint(self.root.stat())); self.assertEqual([], list(self.state.iterdir()))
        self.assertEqual('File', plan['kind']); self.assertEqual(list(self.r.fingerprint(self.fixture.source.stat())), plan['sourceIdentity'])
        self.assertNotEqual(plan['fingerprint'], plan['innerPlan']['fingerprint'])
        self.assertEqual(plan['fingerprint'], self.engine.digest(plan))
        self.assertEqual(self.fixture.command()['expectedVersion'], plan['command']['expectedVersion'])
        self.assertEqual(self.f.version(self.fixture.source.stat()), plan['innerPlan']['command']['expectedVersion'])
        self.assertEqual([os.getuid(), os.getgid()], [plan['uid'], plan['gid']])

    def test_file_completed_receipt_uses_container_etag_and_replay_never_calls_inner_apply(self):
        request = self.request(); result = self.engine.apply(request, self.session); target = self.root / '复制.bin'
        self.assertEqual('Completed', result['state']); self.assertTrue(result['bindingCurrent']); self.assertFalse(result['replayed'])
        self.assertEqual(self.fixture.content, target.read_bytes())
        self.assertEqual(self.r.version(request['plan']['command'], '复制.bin', target.stat()), result['version'])
        self.assertNotEqual(self.f.version(target.stat()), result['version']); inode = target.stat().st_ino
        raw = self.record_path(request).read_bytes()
        with patch.object(common.operations.FileOperations, 'apply', side_effect=AssertionError('no repeat')):
            again = self.engine.apply(request, self.session)
        self.assertEqual('Completed', again['state']); self.assertTrue(again['replayed']); self.assertTrue(again['bindingCurrent'])
        self.assertEqual(inode, target.stat().st_ino); self.assertEqual(raw, self.record_path(request).read_bytes())
        self.assertEqual(0o600, stat.S_IMODE(self.record_path(request).stat().st_mode))
        self.assertEqual('Completed', self.engine.inspect(request, None)['state'])
        self.assertFalse(self.engine.inspect(request, None)['bindingCurrent'])

    def test_directory_completed_receipt_detects_descendant_changes_without_top_metadata_change(self):
        self.fixture.tree(); request = self.request(self.fixture.command('项目', '项目副本'))
        self.assertEqual('Directory', request['plan']['kind'])
        result = self.engine.apply(request, self.session); self.assertEqual('Completed', result['state']); self.assertTrue(result['bindingCurrent'])
        target = self.root / '项目副本'; top = self.r.fingerprint(target.stat())
        (target / '模块' / '代码.txt').write_bytes(b'changed descendant')
        self.assertEqual(top, self.r.fingerprint(target.stat()))
        current = self.engine.inspect(request, self.session)
        self.assertEqual('Completed', current['state']); self.assertFalse(current['bindingCurrent'])
        self.assertEqual(result['version'], current['version'])

    def test_discard_before_apply_fences_late_requests_after_source_and_container_loss(self):
        request = self.request(); self.fixture.source.write_bytes(b'new source')
        self.assertEqual('Aborted', self.engine.discard(request, None)['state'])
        raw = self.record_path(request).read_bytes()
        with patch.object(self.engine, 'plan', side_effect=AssertionError('no replan')), \
                patch.object(common.operations.FileOperations, 'apply', side_effect=AssertionError('no delayed copy')):
            self.assertEqual('Aborted', self.engine.apply(request, None)['state'])
        self.assertEqual(raw, self.record_path(request).read_bytes()); self.assertFalse((self.root / '复制.bin').exists())

    def test_owner_and_changed_plan_cannot_alias_existing_operation(self):
        request = self.request(); self.engine.discard(request)
        with self.assertRaisesRegex(self.f.Rejected, 'NOT_FOUND'): self.engine.inspect({**request, 'owner': 'C' * 64})
        other = self.request(self.fixture.command(target='other'))
        with self.assertRaisesRegex(self.f.Rejected, 'IDEMPOTENCY_CONFLICT'):
            self.engine.apply({**other, 'operationId': request['operationId']}, self.session)

    def test_malformed_plan_types_and_inner_move_uid_or_version_are_rejected_before_state(self):
        request = self.request()
        for key, value in (('schemaVersion', True), ('uid', True), ('sourceParent', [0, True]),
                ('sourceIdentity', [0] * 9), ('targetProof', 'bad'), ('fingerprint', 'F' * 64), ('extra', 1)):
            plan = {**request['plan'], key: value}
            with self.subTest(key=key), self.assertRaises((self.f.Rejected, self.r.Rejected)):
                self.engine.discard({**request, 'plan': plan})
        for field, value in (('kind', 'move'), ('expectedVersion', 'A' * 64), ('mode', '0777')):
            plan = copy.deepcopy(request['plan']); plan['innerPlan']['command'][field] = value
            plan['fingerprint'] = self.engine.digest(plan)
            with self.subTest(field=field), self.assertRaises(self.f.Rejected): self.engine.validate_plan(plan)
        plan = copy.deepcopy(request['plan']); plan['innerPlan']['sourceIdentity'][5] = True
        inner = plan['innerPlan']; inner['fingerprint'] = hashlib.sha256(b'whaledeck-file-operation-v1:' + json.dumps(
            {k: v for k, v in inner.items() if k != 'fingerprint'}, sort_keys=True, separators=(',', ':')).encode()).hexdigest().upper()
        plan['fingerprint'] = self.engine.digest(plan)
        with self.assertRaises(self.f.Rejected): self.engine.validate_plan(plan)
        self.assertEqual([], list(self.state.iterdir()))

    def test_tree_forged_owner_policy_hash_is_rejected_even_with_recomputed_hashes(self):
        self.fixture.tree(); plan = self.request(self.fixture.command('项目', '项目副本'))['plan']
        plan['innerPlan']['targetPolicyHash'] = 'A' * 64
        plan['innerPlan']['fingerprint'] = common.copies.FileTreeCopies(self.f, common.trees.FileTreeManifest).digest(
            {k: v for k, v in plan['innerPlan'].items() if k != 'fingerprint'})
        plan['fingerprint'] = self.engine.digest(plan)
        with self.assertRaises(self.f.Rejected): self.engine.validate_plan(plan)

    def test_source_change_after_plan_is_rejected_without_any_root_receipt(self):
        request = self.request(); self.fixture.source.write_bytes(b'new source')
        with self.assertRaises(self.f.Rejected): self.engine.apply(request, self.session)
        self.assertFalse(self.record_path(request).exists()); self.assertFalse((self.root / '复制.bin').exists())

    def kill_at(self, request, point):
        ready_read, ready_write = os.pipe(); continue_read, continue_write = os.pipe(); pid = os.fork()
        if pid == 0:
            os.close(ready_read); os.close(continue_write)
            def pause(): os.write(ready_write, b'R'); os.read(continue_read, 1)
            def checkpoint(phase):
                if phase == point: pause()
            self.engine.checkpoint = checkpoint; factory = self.engine.engine
            def instrument(scope, kind):
                engine = factory(scope, kind)
                if point == 'FilePrepared':
                    save = engine.save
                    def saved(directory, record):
                        save(directory, record)
                        if record['state'] == 'DestinationApplying': pause()
                    engine.save = saved
                elif point.startswith('Tree'):
                    engine.checkpoint = lambda phase: pause() if point == 'Tree' + phase else None
                return engine
            self.engine.engine = instrument
            try: self.engine.apply(request, self.session)
            except BaseException: os._exit(2)
            os._exit(3)
        os.close(ready_write); os.close(continue_read)
        try:
            self.assertTrue(select.select([ready_read], [], [], 12)[0], 'child did not reach exact fence')
            self.assertEqual(b'R', os.read(ready_read, 1)); os.kill(pid, signal.SIGKILL)
            _, status = os.waitpid(pid, 0); pid = None; self.assertEqual(signal.SIGKILL, os.WTERMSIG(status))
        finally:
            os.close(ready_read); os.close(continue_write)
            if pid is not None:
                with __import__('contextlib').suppress(ProcessLookupError): os.kill(pid, signal.SIGKILL)
                os.waitpid(pid, 0)

    def test_real_process_kills_outer_fences_abort_or_unknown_without_repeat(self):
        for phase, expected in (('Preparing', 'Aborted'), ('Running', 'Unknown'), ('InnerFinished', 'Completed')):
            with self.subTest(phase=phase):
                request = self.request(self.fixture.command(target='copy-' + phase)); self.kill_at(request, phase)
                with patch.object(common.operations.FileOperations, 'apply', side_effect=AssertionError('no reapply')):
                    result = self.engine.inspect(request, self.session)
                    self.assertEqual(expected, result['state']); self.assertEqual(expected, self.engine.apply(request, self.session)['state'])
                self.assertEqual(expected == 'Completed', (self.root / ('copy-' + phase)).exists())

    def test_real_process_kill_file_prepublication_aborts_owned_private_without_publishing(self):
        request = self.request(); self.kill_at(request, 'FilePrepared')
        self.assertEqual('Aborted', self.engine.inspect(request, self.session)['state'])
        self.assertFalse((self.root / '复制.bin').exists())
        self.assertEqual(self.fixture.content, self.fixture.source.read_bytes())

    def test_real_process_kill_tree_staging_ready_and_published_never_repopulates(self):
        self.fixture.tree()
        for phase, expected in (('TreeStaging', 'Aborted'), ('TreeReady', 'Aborted'), ('TreePublished', 'Completed')):
            with self.subTest(phase=phase):
                request = self.request(self.fixture.command('项目', phase)); self.kill_at(request, phase)
                with patch.object(common.copies.FileTreeCopies, 'populate', side_effect=AssertionError('no recopy')):
                    result = self.engine.inspect(request, self.session)
                self.assertEqual(expected, result['state']); self.assertEqual(expected != 'Completed', result['privateMayExist'])

    def test_actual_inner_completed_but_target_unavailable_retains_unknown_and_original_inner_proof(self):
        request = self.request(); self.kill_at(request, 'InnerFinished'); raw = self.inner_path(request).read_bytes()
        result = self.engine.inspect(request, None)
        self.assertEqual('Unknown', result['state']); self.assertTrue(result['privateMayExist'])
        self.assertEqual('Completed', self.record(request)['innerState']); self.assertEqual(raw, self.inner_path(request).read_bytes())
        self.assertTrue((self.root / '复制.bin').exists())
        with patch.object(common.operations.FileOperations, 'apply', side_effect=AssertionError('no reapply')):
            self.assertEqual('Unknown', self.engine.apply(request, self.session)['state'])

    def test_changed_mount_proof_before_apply_and_new_session_recovery_cannot_rebind(self):
        request = self.request(); self.session.mounts = b'Z' * 32
        with self.assertRaises(self.f.Rejected): self.engine.apply(request, self.session)
        self.assertFalse(self.record_path(request).exists()); self.session.mounts = b'M' * 32
        self.kill_at(request, 'FilePrepared'); self.session.mounts = b'Z' * 32
        self.assertEqual('Unknown', self.engine.inspect(request, self.session)['state'])
        self.assertTrue((self.root / common.scope_module.ContainerCopyScope.PRIVATE / (request['operationId'] + '.mutation')).exists())

    def test_root_record_and_lock_symlink_or_corrupt_metadata_refused(self):
        request = self.request(); lock = self.state / (request['operationId'] + '.lock'); lock.symlink_to(self.fixture.source)
        with self.assertRaises(OSError): self.engine.discard(request)
        lock.unlink(); self.engine.discard(request)
        self.record_path(request).write_text('{}'); self.record_path(request).chmod(0o600)
        with self.assertRaisesRegex(self.f.Rejected, 'RECEIPT_INVALID'): self.engine.inspect(request)


if __name__ == '__main__': unittest.main()
