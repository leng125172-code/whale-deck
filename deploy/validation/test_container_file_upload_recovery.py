"""Real Linux root journal/lock/fsync tests with synthetic container sessions.

No installed helper/RPC, Docker, human authority or SQL recovery is asserted.
A retained journal is historical metadata, never a current target proof.
"""
import copy
import hashlib
import json
import multiprocessing
import os
from pathlib import Path
import signal
import unittest
import uuid
from unittest.mock import Mock, patch

import test_container_file_staging as staging
import test_container_file_publishing as publishing

legacy = staging.load(str(Path(__file__).with_name('fixtures')/'container_upload_v40_receipt.py'), 'frozen_v40_upload_reader')


class LegacyUploads(staging.uploads.ContainerUploads):
    # The stage/inspect/discard methods are unchanged. Only the exact frozen
    # pre-recovery receipt method replaces the current refactored reader.
    receipt = legacy.receipt


@unittest.skipUnless(os.name == 'posix' and hasattr(os, 'geteuid') and os.geteuid() == 0, 'requires isolated Linux root')
class LinuxContainerUploadRecoveryTests(unittest.TestCase):
    def setUp(self):
        self.fixture = staging.LinuxContainerStagingTests(); self.fixture.setUp()
        self.addCleanup(self.fixture.doCleanups)
        self.u, self.f = self.fixture.engine, self.fixture.files
        self.engine = staging.uploads.ContainerUploadRecovery(self.u)
        self.request = self.fixture.request(content=b'recovery-content\x00\xff')
        self.scope = {**self.fixture.reference(self.request), 'expectedTargetProof': self.u.target_proof(self.fixture.session)}
        self.recovery_id = uuid.uuid4().hex
        self.path = self.fixture.state / (self.request['operationId'] + '.json')

    def stage(self): return self.fixture.stage(self.request, content=b'recovery-content\x00\xff')

    def seal(self, observation=None, **changes):
        values = {'recovery_id': self.recovery_id, 'actor': 'current-admin', 'trace': 'current-recovery-trace',
            'expected_observation': (self.engine.observe(self.scope) if observation is None else observation)['observationHash']}
        values.update(changes)
        return self.engine.seal(self.scope, **values)

    def test_fixed_transport_recovery_never_enters_a_target_namespace(self):
        driver = staging.load('whaledeck-container-file-write.py', 'recovery_transport_fixed_driver')
        source = Mock(); publisher = Mock()
        with patch.object(staging.read_engine, 'ContainerRoot', side_effect=AssertionError('no Docker/namespace access')) as root:
            observed = driver.execute('recovery-observe', self.scope, source, staging.read_engine, self.f, None, self.u, publisher)
            request = {**self.scope, 'recoveryId': self.recovery_id, 'actorSubject': 'new-admin', 'traceId': 'new-trace',
                'expectedObservationHash': observed['observationHash']}
            sealed = driver.execute('recovery-seal', request, source, staging.read_engine, self.f, None, self.u, publisher)
            self.assertTrue(sealed['sealed']); root.assert_not_called()
            self.assertEqual(2, source.finish.call_count); self.assertFalse(publisher.mock_calls)
            for change in ({'extra':'field'}, {'recoveryId':self.request['operationId']}):
                with self.subTest(change=change), self.assertRaises((staging.read_engine.Rejected,driver.Rejected)):
                    driver.execute('recovery-seal',{**request,**change},source,staging.read_engine,self.f,None,self.u,publisher)

    def test_missing_observation_is_read_only_and_does_not_fabricate_upload_or_target_facts(self):
        self.fixture.session.revalidate.side_effect = AssertionError('must not enter old namespace')
        result = self.engine.observe(self.scope)
        self.assertEqual(result['rootState'], 'Missing'); self.assertFalse(result['sealed'])
        self.assertFalse(result['bindingCurrent']); self.assertFalse(result['mayHavePublished']); self.assertFalse(result['retainedObjects'])
        self.assertFalse(self.path.exists()); self.assertEqual(self.fixture.leaf.read_bytes(), self.fixture.original)
        self.assertFalse({'owner', 'path', 'version', 'sha256', 'actor', 'trace'} & result.keys())
        self.assertEqual(result, self.engine.observe(self.scope))

    def test_missing_seal_replays_exactly_and_original_reader_rejects_delayed_sender(self):
        observed = self.engine.observe(self.scope); sealed = self.seal(observed)
        self.assertTrue(sealed['sealed']); self.assertEqual(sealed['rootState'], 'Missing')
        self.assertEqual(sealed, self.seal(observed)); self.assertEqual(sealed, self.engine.observe(self.scope))
        before = self.path.read_bytes(); source = Mock()
        with self.assertRaisesRegex(staging.read_engine.Rejected, 'RECEIPT_INVALID'):
            self.u.stage(self.request, self.fixture.session, source)
        self.assertEqual([], source.mock_calls); self.assertEqual(before, self.path.read_bytes())
        self.fixture.assert_original(); self.assertEqual([], self.fixture.payloads())

    def test_frozen_minor40_reader_and_delayed_stage_refuse_new_seal_schema(self):
        self.seal()
        old = LegacyUploads(self.u.r,self.f,self.fixture.state)
        before = self.path.read_bytes(); source = Mock()
        with self.assertRaisesRegex(staging.read_engine.Rejected,'RECEIPT_INVALID'):
            old.stage(self.request,self.fixture.session,source)
        self.assertEqual([],source.mock_calls); self.assertEqual(before,self.path.read_bytes())
        self.assertEqual([],self.fixture.payloads()); self.fixture.assert_original()

    def test_staged_seal_retains_exact_original_journal_and_payload_without_current_instance(self):
        self.stage(); before = self.path.read_bytes(); payloads = {p.name: p.read_bytes() for p in self.fixture.payloads()}
        self.fixture.session.revalidate.side_effect = RuntimeError('old container stopped')
        observed = self.engine.observe(self.scope); result = self.seal(observed)
        self.assertEqual('Staged', result['rootState']); self.assertTrue(result['retainedObjects']); self.assertFalse(result['bindingCurrent'])
        retained = json.loads(self.path.read_bytes())
        self.assertEqual(json.loads(before), retained['originalRecord'])
        self.assertEqual(payloads, {p.name: p.read_bytes() for p in self.fixture.payloads()})
        self.fixture.assert_original()
        self.assertEqual(result['originalRecordHash'], self.engine.digest('original', json.loads(before)))
        self.assertNotIn('current-admin', self.path.read_text()); self.assertNotIn('current-recovery-trace', self.path.read_text())

    def test_changed_observation_and_other_admin_cannot_reuse_frozen_confirmation(self):
        observed = self.engine.observe(self.scope); self.stage(); before = self.path.read_bytes()
        with self.assertRaisesRegex(staging.read_engine.Rejected, 'RECOVERY_OBSERVATION_CHANGED'): self.seal(observed)
        self.assertEqual(before, self.path.read_bytes())
        observed = self.engine.observe(self.scope); self.seal(observed); sealed = self.path.read_bytes()
        for changes in ({'actor': 'another-admin'}, {'trace': 'another-trace'}, {'recovery_id': uuid.uuid4().hex}):
            with self.subTest(changes=changes), self.assertRaisesRegex(staging.read_engine.Rejected, 'RECOVERY_IDEMPOTENCY_CONFLICT'):
                self.seal(observed, **changes)
            self.assertEqual(sealed, self.path.read_bytes())

    def test_wrong_original_scope_refuses_before_and_after_seal(self):
        self.stage()
        for sealed in (False, True):
            if sealed: self.seal()
            before = self.path.read_bytes()
            for change in ({'owner': 'C'*64}, {'containerId': 'b'*64}, {'instanceId': staging.INSTANCE.replace('777:', '778:')}, {'expectedTargetProof': 'F'*64}):
                with self.subTest(sealed=sealed, change=change), self.assertRaisesRegex(staging.read_engine.Rejected, 'RECOVERY_SCOPE_CHANGED'):
                    self.engine.observe({**self.scope, **change})
                self.assertEqual(before, self.path.read_bytes())

    def test_corrupt_nested_record_or_seal_material_never_yields_an_observation(self):
        self.stage(); self.seal(); original = json.loads(self.path.read_bytes())
        changes = [('originalRecordHash', 'F'*64), ('expectedObservationHash', 'E'*64), ('requestHash', 'D'*64),
            ('recoveryId', self.request['operationId']), ('sealedAtUnixMs', True), ('unexpected', 'field')]
        for key, value in changes:
            record = copy.deepcopy(original); record[key] = value
            self.path.write_text(json.dumps(record)); self.path.chmod(0o600)
            with self.subTest(key=key), self.assertRaisesRegex((staging.read_engine.Rejected, self.f.Rejected), 'RECOVERY_SEAL_INVALID'):
                self.engine.observe(self.scope)
        record = copy.deepcopy(original); record['originalRecord']['request']['sha256'] = 'F'*64
        record['originalRecordHash'] = self.engine.digest('original', record['originalRecord'])
        self.path.write_text(json.dumps(record)); self.path.chmod(0o600)
        with self.assertRaisesRegex(staging.read_engine.Rejected, 'RECEIPT_INVALID'): self.engine.observe(self.scope)

    def test_unsafe_receipt_permissions_links_and_duplicates_are_refused(self):
        self.stage(); original = self.path.read_bytes()
        self.path.chmod(0o640)
        with self.assertRaisesRegex(staging.read_engine.Rejected, 'RECEIPT_INVALID'): self.engine.observe(self.scope)
        self.path.chmod(0o600); other = self.fixture.state / 'hardlink'; os.link(self.path, other)
        with self.assertRaisesRegex(staging.read_engine.Rejected, 'RECEIPT_INVALID'): self.engine.observe(self.scope)
        other.unlink(); self.path.unlink(); other.write_bytes(original); other.chmod(0o600); self.path.symlink_to(other)
        with self.assertRaisesRegex(staging.read_engine.Rejected, 'RECEIPT_INVALID'): self.engine.observe(self.scope)
        self.path.unlink(); self.path.write_bytes(b'{"schemaVersion":1,"schemaVersion":2}'); self.path.chmod(0o600)
        with self.assertRaises(self.f.Rejected): self.engine.observe(self.scope)

    def test_invalid_current_actor_or_confirmation_cannot_mutate_journal(self):
        self.stage(); before = self.path.read_bytes()
        for change in ({'actor':''}, {'actor':'x\nadmin'}, {'actor':'\ud800'}, {'trace':'x'*129}, {'trace':'x\x80'},
            {'recovery_id':self.request['operationId']}, {'expected_observation':'f'*64}):
            with self.subTest(change=change), self.assertRaises((staging.read_engine.Rejected, self.f.Rejected)):
                self.seal(**change)
            self.assertEqual(before, self.path.read_bytes())

    def test_envelope_size_limit_never_replaces_a_valid_near_limit_record(self):
        self.stage(); record = json.loads(self.path.read_bytes()); record['padding'] = ''
        record['padding'] = 'x'*(16000-len(json.dumps(record,separators=(',',':')).encode()))
        self.path.write_bytes(json.dumps(record,separators=(',',':')).encode()); self.path.chmod(0o600)
        self.assertEqual(16000, self.path.stat().st_size); before = self.path.read_bytes()
        with self.assertRaisesRegex(staging.read_engine.Rejected, 'RECOVERY_RECORD_LIMIT'): self.seal()
        self.assertEqual(before, self.path.read_bytes())

    def test_live_stage_holds_the_same_kernel_lock_and_cannot_be_sealed_mid_stream(self):
        context = multiprocessing.get_context('fork'); parent, child = context.Pipe()
        save = self.f.save_receipt
        def writer():
            parent.close()
            def paused(state, operation, record):
                save(state, operation, record)
                if record['state'] == 'Receiving': child.send('receiving'); child.recv()
            try:
                with patch.object(self.f, 'save_receipt', side_effect=paused): self.stage()
                child.send('finished')
            finally: child.close()
        process = context.Process(target=writer); process.start(); child.close()
        try:
            self.assertTrue(parent.poll(10)); self.assertEqual('receiving', parent.recv())
            with self.assertRaisesRegex(staging.read_engine.Rejected, 'BUSY'): self.engine.observe(self.scope)
            with self.assertRaisesRegex(staging.read_engine.Rejected, 'BUSY'):
                self.engine.seal(self.scope, self.recovery_id, 'current-admin', 'trace', 'A'*64)
            self.assertEqual('Receiving', json.loads(self.path.read_bytes())['state'])
            parent.send('continue'); self.assertTrue(parent.poll(10)); self.assertEqual('finished', parent.recv())
            process.join(10); self.assertEqual(0, process.exitcode)
            self.assertEqual('Staged', self.seal()['rootState']); self.fixture.assert_original()
        finally:
            if process.is_alive(): process.kill(); process.join(10)
            parent.close(); process.close()

    def test_actual_kill_before_or_after_atomic_save_leaves_old_or_permanent_seal(self):
        self.stage(); observed = self.engine.observe(self.scope); original = self.path.read_bytes()
        for after in (False, True):
            self.path.write_bytes(original); self.path.chmod(0o600)
            child = os.fork()
            if child == 0:
                save = self.f.save_receipt
                def killed(*args):
                    if after: save(*args)
                    os.kill(os.getpid(), signal.SIGKILL)
                self.f.save_receipt = killed
                try: self.seal(observed)
                except BaseException: os._exit(98)
                os._exit(99)
            _, status = os.waitpid(child, 0)
            self.assertTrue(os.WIFSIGNALED(status)); self.assertEqual(signal.SIGKILL, os.WTERMSIG(status))
            result = self.engine.observe(self.scope)
            self.assertEqual(after, result['sealed']); self.assertEqual('Staged', result['rootState'])
            self.assertEqual(observed['originalRecordHash'], result['originalRecordHash'])
            self.assertTrue(self.seal(observed)['sealed']); self.fixture.assert_original()

    def test_published_history_remains_unknown_currentness_and_all_original_writers_refuse(self):
        fixture = publishing.LinuxContainerPublicationTests(); fixture.setUp(); self.addCleanup(fixture.doCleanups)
        request, scope = fixture.stage(); result = fixture.engine.publish(scope, fixture.session)
        self.assertEqual('Published', result['state'])
        engine = staging.uploads.ContainerUploadRecovery(fixture.u)
        reference = {**scope, 'expectedTargetProof': result['targetProof']}
        files = [fixture.leaf, fixture.pending(scope), fixture.state/(scope['operationId']+'.data'), fixture.fixture.target/(scope['operationId']+'.data')]
        before = [(p.read_bytes(), p.stat().st_ino) for p in files]
        fixture.session.revalidate.side_effect = RuntimeError('original instance stopped/restarted')
        observed = engine.observe(reference); self.assertEqual('Published', observed['rootState'])
        self.assertFalse(observed['bindingCurrent']); self.assertTrue(observed['mayHavePublished'])
        sealed = engine.seal(reference, uuid.uuid4().hex, 'new-admin', 'new-trace', observed['observationHash'])
        self.assertTrue(sealed['sealed']); self.assertEqual('Published', sealed['rootState'])
        fixture.session.revalidate.side_effect = None
        for call in (lambda: fixture.u.inspect(scope, fixture.session), lambda: fixture.u.discard(scope, fixture.session),
            lambda: fixture.engine.publish(scope, fixture.session), lambda: fixture.b.capture(scope, fixture.session)):
            with self.assertRaisesRegex(staging.read_engine.Rejected, 'RECEIPT_INVALID'): call()
        old = LegacyUploads(fixture.u.r,fixture.u.f,fixture.state)
        old_backups = type(fixture.b)(old,fixture.b.storage)
        old_publisher = publishing.publisher.ContainerPublisher(old,old_backups)
        for call in (lambda: old.inspect(scope,fixture.session),lambda: old.discard(scope,fixture.session),
            lambda: old_publisher.publish(scope,fixture.session),lambda: old_backups.capture(scope,fixture.session)):
            with self.assertRaisesRegex(staging.read_engine.Rejected,'RECEIPT_INVALID'): call()
        self.assertEqual(before, [(p.read_bytes(), p.stat().st_ino) for p in files])


if __name__ == '__main__': unittest.main()
