"""Real root chown/chmod/utime/rename semantics; synthetic namespace/HDD policy.

These tests do not attest installed RPC, user authority, SQL or browser support.
"""
import copy
import hashlib
import json
import os
import signal
import stat
import tempfile
import time
import unittest
import uuid
from unittest.mock import patch

import test_container_file_publishing as publishing
import test_container_file_staging as staging
import test_container_file_upload_recovery as recovery

restoring = staging.load('whaledeck-container-restore.py', 'container_restoration_candidate')
history = staging.load('whaledeck-container-history.py', 'container_restoration_history')


@unittest.skipUnless(os.name == 'posix' and hasattr(os, 'geteuid') and os.geteuid() == 0, 'requires isolated Linux root')
class LinuxContainerRestorationTests(unittest.TestCase):
    def setUp(self):
        self.fixture = publishing.LinuxContainerPublicationTests(); self.fixture.setUp()
        self.addCleanup(self.fixture.doCleanups)
        self.u, self.f, self.r = self.fixture.u, self.fixture.f, self.fixture.r
        self.session, self.leaf, self.state = self.fixture.session, self.fixture.leaf, self.fixture.state
        self.h = history.ContainerHistory(self.u, self.fixture.fixture.storage)
        self.engine = restoring.ContainerRestorations(self.u, self.h); self.u.restorations = self.engine
        self.attributes = {'uid': 1001, 'gid': 2002, 'mode': 0o640, 'modifiedNs': 1700000000123456789}
        os.chown(self.leaf, self.attributes['uid'], self.attributes['gid']); self.leaf.chmod(self.attributes['mode'])
        os.utime(self.leaf, ns=(self.attributes['modifiedNs'], self.attributes['modifiedNs']))
        _, self.captured = self.fixture.stage()
        self.fixture.b.capture(self.captured, self.session)
        self.capture = self.captured['operationId']
        self.metadata = json.loads((self.fixture.fixture.target / (self.capture + '.json')).read_bytes())
        self.old = self.leaf.read_bytes()
        self.leaf.write_bytes(b'current target must be retained before restoration')
        os.chown(self.leaf, 3003, 4004); self.leaf.chmod(0o600)
        self.current = self.leaf.read_bytes(); self.current_identity = self.f.publication_identity(self.leaf.stat())

    def request(self, path=None, **changes):
        path = self.leaf.name if path is None else path
        target = self.fixture.root / path
        value = {**self.session.request, 'path': path, 'operationId': uuid.uuid4().hex, 'owner': staging.OWNER,
            'expectedVersion': self.r.version(self.session.request, path, target.stat()) if target.exists() else 'ABSENT',
            'expectedParentVersion': self.r.version(self.session.request, '', self.fixture.root.stat()),
            'sizeBytes': len(self.old), 'sha256': hashlib.sha256(self.old).hexdigest().upper(),
            'restoreSource': {'path': self.leaf.name, 'captureId': self.capture, 'captureHash': self.h.digest('capture', self.metadata)}}
        return {**value, **changes}

    def stage(self, request=None):
        request = self.request() if request is None else request
        with tempfile.TemporaryFile() as stream:
            for row in staging.LinuxContainerStagingTests.packets(self.old): stream.write((json.dumps(row) + '\n').encode())
            stream.seek(0)
            result = self.u.stage(request, self.session, self.f.InputLines(stream.fileno()))
        self.assertEqual(result['state'], 'Staged')
        return staging.LinuxContainerStagingTests.reference(request)

    def record(self, scope): return self.fixture.record(scope)

    def assert_original_target(self):
        self.assertEqual(self.leaf.read_bytes(), self.current)
        self.assertEqual(self.f.publication_identity(self.leaf.stat()), self.current_identity)

    def test_overwrite_restores_verified_owner_mode_and_nanoseconds_before_exchange(self):
        scope = self.stage(); record = self.record(scope)
        self.assertEqual(record['schemaVersion'], 4); self.assertEqual(record['restorationAttributes'], self.attributes)
        self.assertEqual(self.fixture.engine.publish(scope, self.session)['state'], 'Published')
        self.assertEqual(self.leaf.read_bytes(), self.old); self.assertTrue(self.engine.matches(self.leaf.stat(), self.attributes))
        retained = self.fixture.pending(scope)
        self.assertEqual(retained.read_bytes(), self.current)
        self.assertEqual(self.f.publication_identity(retained.stat()), self.current_identity)
        self.assertEqual((self.fixture.fixture.target / (self.capture + '.data')).read_bytes(), self.old)

    def test_other_path_new_file_gets_historical_attributes_without_modifying_source(self):
        request = self.request('another.bin'); scope = self.stage(request)
        result = self.fixture.engine.publish(scope, self.session)
        self.assertFalse(result['previousRetained']); self.assertEqual(result['state'], 'Published')
        target = self.fixture.root / 'another.bin'
        self.assertTrue(self.engine.matches(target.stat(), self.attributes)); self.assertEqual(target.read_bytes(), self.old)
        self.assert_original_target()

    def test_stage_replay_observes_frozen_attributes_without_reopening_hdd_source(self):
        request = self.request(); scope = self.stage(request); before = self.record(scope)
        with patch.object(self.engine, 'capture', side_effect=AssertionError('must not re-read source')):
            self.assertEqual(self.stage(request), scope)
        self.assertEqual(before, self.record(scope))

    def test_missing_engine_cannot_create_partial_or_journal(self):
        request = self.request(); self.u.restorations = None
        with self.assertRaisesRegex(self.r.Rejected, 'RESTORE_ENGINE_REQUIRED'): self.stage(request)
        self.assertFalse((self.state / (request['operationId'] + '.json')).exists()); self.assert_original_target()

    def test_engine_lost_after_stage_refuses_publication_before_backup_or_target_write(self):
        scope = self.stage(); self.u.restorations = None
        with self.assertRaisesRegex(self.r.Rejected, 'RESTORE_ENGINE_REQUIRED'):
            self.fixture.engine.publish(scope, self.session)
        self.assert_original_target()
        self.assertNotIn('originalCapture', self.record(scope))
        self.assertFalse((self.fixture.fixture.target / (scope['operationId'] + '.json')).exists())

    def test_bad_source_hash_scope_or_content_never_creates_staging(self):
        for change in ({'captureHash': 'F' * 64}, {'path': 'another.bin'}, {'captureId': uuid.uuid4().hex}):
            request = self.request(); request['restoreSource'].update(change)
            with self.subTest(change=change), self.assertRaises((self.r.Rejected, self.f.Rejected, FileNotFoundError)):
                self.stage(request)
            self.assertFalse((self.state / (request['operationId'] + '.partial')).exists())
        request = self.request(sha256='F' * 64)
        with self.assertRaisesRegex(self.r.Rejected, 'RESTORE_CONTENT_MISMATCH'): self.stage(request)
        self.assert_original_target()

    def test_unmapped_owner_is_rejected_before_staging(self):
        with patch.object(self.engine, 'mapped', return_value=False), self.assertRaisesRegex(self.r.Rejected, 'RESTORE_OWNER_UNMAPPED'):
            self.stage()
        self.assert_original_target()

    def test_mapping_parser_rejects_overlaps_bounds_and_boolean_substitution(self):
        self.assertTrue(self.engine.mapped(b'0 100000 65536\n', 100001))
        self.assertFalse(self.engine.mapped(b'0 100000 65536\n', 1001))
        for value in (b'', b'0 0 0\n', b'0 0 4294967297\n', b'-1 0 1\n', b'0 0 2\n1 3 1\n', b'0 0 2\n3 1 1\n'):
            with self.subTest(value=value), self.assertRaisesRegex(self.r.Rejected, 'RESTORE_OWNER_UNMAPPED'):
                self.engine.mapped(value, 0)
        with self.assertRaisesRegex(self.r.Rejected, 'RESTORE_ATTRIBUTES_INVALID'):
            self.u.restoration_attributes({**self.attributes, 'uid': True})

    def test_special_mode_is_not_silently_downgraded(self):
        for mode in (0o4640, 0o2640, 0o1640):
            metadata = copy.deepcopy(self.metadata); metadata['sourceIdentity'][2] = stat.S_IFREG | mode
            path = self.fixture.fixture.target / (self.capture + '.json'); path.write_text(json.dumps(metadata)); path.chmod(0o600)
            request = self.request(); request['restoreSource']['captureHash'] = self.h.digest('capture', metadata)
            with self.subTest(mode=mode), self.assertRaisesRegex(self.r.Rejected, 'RESTORE_MODE_UNSUPPORTED'):
                self.stage(request)
        self.assert_original_target()

    def test_schema_and_fingerprint_prevent_restore_downgrade_or_source_substitution(self):
        scope = self.stage(); record = self.record(scope)
        with self.u.locked(scope['operationId']) as state:
            self.assertEqual(self.u.receipt(state, scope['operationId']), record)
            for changed in (dict(record, schemaVersion=1), dict(record, restorationAttributes=None)):
                with self.assertRaises(self.r.Rejected): self.u.validate_receipt(changed, scope['operationId'])
            changed = copy.deepcopy(record); changed['request']['restoreSource']['captureId'] = uuid.uuid4().hex
            with self.assertRaises(self.r.Rejected): self.u.validate_receipt(changed, scope['operationId'])
            old = recovery.LegacyUploads(self.r, self.f, self.state)
            with self.assertRaisesRegex(self.r.Rejected, 'RECEIPT_INVALID'): old.receipt(state, scope['operationId'])
        self.assert_original_target()

    def test_raw_request_cannot_supply_attributes_or_arbitrary_source_fields(self):
        for key in ('uid', 'mode', 'restorationAttributes'):
            request = self.request(**{key: 0})
            with self.subTest(key=key), self.assertRaisesRegex(self.r.Rejected, 'REQUEST_INVALID'): self.u.upload_request(request)
        request = self.request(); request['restoreSource']['uid'] = 0
        with self.assertRaisesRegex(self.r.Rejected, 'REQUEST_INVALID'): self.u.upload_request(request)

    def test_attributes_failure_leaves_target_unchanged_and_recovery_does_not_reapply(self):
        scope = self.stage()
        with patch.object(os, 'utime', side_effect=OSError('owned simulated utime failure')):
            with self.assertRaises(OSError): self.fixture.engine.publish(scope, self.session)
        self.assert_original_target()
        with patch.object(self.engine, 'apply', side_effect=AssertionError('observe must not reapply')):
            self.assertEqual(self.fixture.engine.inspect(scope, self.session)['state'], 'PublishInterrupted')
            self.assertEqual(self.fixture.engine.discard(scope, self.session)['state'], 'PublishDiscarded')
        self.assert_original_target()

    def test_kernel_precision_loss_is_detected_before_publication(self):
        scope = self.stage(); real_utime = os.utime
        def truncated(descriptor, *, ns): real_utime(descriptor, ns=(ns[0], ns[1] // 1000000000 * 1000000000))
        with patch.object(os, 'utime', side_effect=truncated), self.assertRaisesRegex(self.r.Rejected, 'RESTORE_ATTRIBUTES_CHANGED'):
            self.fixture.engine.publish(scope, self.session)
        self.assert_original_target()

    def test_partial_attribute_recovery_refuses_changed_bytes_or_missing_content_proof(self):
        scope = self.stage()
        with patch.object(os, 'utime', side_effect=OSError('owned utime failure')):
            with self.assertRaises(OSError): self.fixture.engine.publish(scope, self.session)
        pending = self.fixture.pending(scope)
        pending.write_bytes(b'changed private pending content')
        with self.assertRaisesRegex(self.r.Rejected, 'PUBLISH_OBJECT_CHANGED'):
            self.fixture.engine.discard(scope, self.session)
        self.assertTrue(pending.exists()); self.assert_original_target()

    def test_real_sigkill_after_each_attribute_syscall_retains_discardable_full_content_proof(self):
        for name in ('fchown', 'fchmod', 'utime'):
            scope = self.stage(); operation = getattr(os, name)
            def action():
                def killed(descriptor, *arguments, **keywords):
                    result = operation(descriptor, *arguments, **keywords)
                    path = self.fixture.pending(scope)
                    if isinstance(descriptor, int) and path.exists() and \
                            os.fstat(descriptor).st_ino == path.stat().st_ino:
                        os.kill(os.getpid(), signal.SIGKILL)
                    return result
                with patch.object(os, name, side_effect=killed): self.fixture.engine.publish(scope, self.session)
            self.fixture.kill(action)
            self.assertEqual(self.fixture.engine.inspect(scope, self.session)['state'], 'PublishInterrupted')
            self.assert_original_target()
            record = self.record(scope); self.assertIn('attributePreparationProof', record['publication'])
            self.assertNotIn('pendingProof', record['publication'])
            self.assertEqual(self.fixture.engine.discard(scope, self.session)['state'], 'PublishDiscarded')
            self.assertFalse(self.fixture.pending(scope).exists()); self.assert_original_target()

    def test_original_upload_schema_and_attributes_policy_remain_unchanged(self):
        _, scope = self.fixture.stage(); result = self.fixture.engine.publish(scope, self.session)
        self.assertEqual(result['state'], 'Published'); self.assertEqual(self.record(scope)['schemaVersion'], 1)
        self.assertNotIn('restorationAttributes', self.record(scope))
        self.assertEqual((self.leaf.stat().st_uid, self.leaf.stat().st_gid, stat.S_IMODE(self.leaf.stat().st_mode)), (3003, 4004, 0o600))

    def test_current_administrator_seal_retains_full_restore_journal_without_reentering_source(self):
        scope = self.stage(); request = {**scope, 'expectedTargetProof': self.u.target_proof(self.session)}
        before = self.record(scope); engine = self.u.recovery_engine(); observation = engine.observe(request)
        with patch.object(self.engine, 'capture', side_effect=AssertionError('recovery must not read HDD')):
            result = engine.seal(request, uuid.uuid4().hex, 'current-admin', 'owned-restore-trace', observation['observationHash'])
        self.assertTrue(result['sealed'])
        self.assertEqual(self.record(scope)['originalRecord'], before); self.assert_original_target()

    def test_real_sigkill_on_both_sides_of_exchange_is_observed_without_another_write(self):
        for after in (False, True):
            scope = self.stage(); original_exchange = self.f.exchange
            child = os.fork()
            if child == 0:
                def killed(*arguments):
                    target = arguments[1] == scope['operationId'] + '.upload'
                    if target and not after: os.kill(os.getpid(), signal.SIGKILL)
                    result = original_exchange(*arguments)
                    if target and after: os.kill(os.getpid(), signal.SIGKILL)
                    return result
                self.f.exchange = killed
                try: self.fixture.engine.publish(scope, self.session)
                except BaseException: os._exit(71)
                os._exit(72)
            deadline = time.monotonic() + 20
            while True:
                waited, status = os.waitpid(child, os.WNOHANG)
                if waited: break
                if time.monotonic() > deadline:
                    os.kill(child, signal.SIGKILL); os.waitpid(child, 0); self.fail('restore child timed out')
                time.sleep(0.01)
            self.assertTrue(os.WIFSIGNALED(status)); self.assertEqual(os.WTERMSIG(status), signal.SIGKILL)
            with patch.object(self.f, 'exchange', side_effect=AssertionError('observation must not write')):
                result = self.fixture.engine.inspect(scope, self.session)
            self.assertEqual(result['state'], 'Published' if after else 'PublishInterrupted')
            if after:
                self.assertEqual(self.leaf.read_bytes(), self.old); self.assertTrue(self.engine.matches(self.leaf.stat(), self.attributes))
            else:
                self.assert_original_target(); self.fixture.engine.discard(scope, self.session)
