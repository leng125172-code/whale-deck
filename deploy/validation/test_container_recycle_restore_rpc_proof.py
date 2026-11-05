"""Independent proof rejects self-consistent forgeries; real temp I/O is not installed RPC/HDD acceptance."""
import copy
import hashlib
import json
import os
import unittest
import uuid
from unittest.mock import patch
import container_recycle_restore_rpc_proof as proof
import test_container_recycle_rpc_proof as old_wire
import test_container_file_recycle_restores as native


def rehash(plan):
    plan['fingerprint'] = proof.digest(b'whaledeck-container-recycle-restore-plan-v1\n', {k: v for k, v in plan.items() if k != 'fingerprint'})
    return plan


def plan():
    original = old_wire.plan()
    return rehash(dict(schemaVersion=1, command=dict(source=dict(operationId='a'*32, owner='B'*64, plan=original), historyDigest='D'*64), targetProof=original['targetProof'],
        parentIdentity=original['parentIdentity'][:], parentVersion='E'*64, uid=1000, gid=1000, privateIdentity=[2049, 67], retainedIdentity=original['sourceIdentity'][:]))


def wire_plan(p):
    p = copy.deepcopy(p); p['command']['source']['plan'] = old_wire.wire_plan(p['command']['source']['plan'])
    for key in ('parentIdentity', 'privateIdentity'): p[key] = dict(zip(('device', 'inode'), map(str, p[key]), strict=True))
    p['ownerUid'] = p.pop('uid'); p['ownerGid'] = p.pop('gid'); i = p['retainedIdentity']
    p['retainedIdentity'] = {k: v if k in ('mode', 'ownerUid', 'ownerGid') else str(v) for k, v in zip(proof.FRAME[:8], i[:8], strict=True)}
    p['retainedChangedAtUnixNs'] = str(i[8]); return p


def request(p=None): return dict(operationId='c'*32, owner='C'*64, plan=p or plan())


def receipt(q, state='Completed'):
    value = dict(operationId=q['operationId'], sourceOperationId=q['plan']['command']['source']['operationId'], fingerprint=q['plan']['fingerprint'], targetProof=q['plan']['targetProof'], state=state,
        bindingCurrent=state == 'Completed', replayed=False, historyVerified=True, historyDigest=q['plan']['command']['historyDigest'], historyAvailable=True)
    if state == 'Completed': value.update(sourceState='Restored', sourceRetained=False, sourceRestored=True, restoredByThisOperation=True)
    if state == 'Aborted': value.update(sourceState='Trashed', sourceRetained=True, sourceRestored=False, restoredByThisOperation=False)
    return value


class RestoreWireProofTests(unittest.TestCase):
    def test_complete_wire_native_roundtrip_has_a_separate_domain_and_exact_numbers(self):
        p = plan(); self.assertEqual(proof.proto_plan(wire_plan(p)), p)
        p['privateIdentity'] = [0, 2**64-1]; p['retainedIdentity'][8] = 2**63-1; rehash(p)
        self.assertEqual(proof.proto_plan(wire_plan(p)), p)
        self.assertNotEqual(p['fingerprint'], proof.digest(b'whaledeck-container-recycle-plan-v1\n', {k: v for k, v in p.items() if k != 'fingerprint'}))

    def test_self_consistent_rehash_cannot_change_source_parent_owner_or_retained_inode(self):
        for field in ('uid', 'gid', 'parent', 'target', 'inode', 'ctime', 'bool-owner', 'bool-private', 'bool-retained', 'extra'):
            p = plan()
            if field in ('uid', 'gid'): p[field] += 1
            elif field == 'parent': p['parentIdentity'][1] += 1
            elif field == 'target': p['targetProof'] = 'F'*64
            elif field == 'inode': p['retainedIdentity'][1] += 1
            elif field == 'ctime': p['retainedIdentity'][8] -= 1
            elif field == 'bool-owner': p['uid'] = True
            elif field == 'bool-private': p['privateIdentity'][0] = True
            elif field == 'bool-retained': p['retainedIdentity'][0] = True
            else: p['hostPath'] = '/etc'
            rehash(p)
            with self.subTest(field=field), self.assertRaises(AssertionError): proof.native_plan_proof(p)

    def test_nested_extra_missing_and_duplicate_fields_are_not_normalized_away(self):
        for path in ((), ('command',), ('command', 'source'), ('command', 'source', 'plan'), ('retainedIdentity',), ('privateIdentity',)):
            p = wire_plan(plan()); node = p
            for k in path: node = node[k]
            node['hostPath'] = '/etc'
            with self.subTest(path=path), self.assertRaises(AssertionError): proof.proto_plan(p)
        for key in proof.PLAN:
            p = wire_plan(plan()); del p[key]
            with self.subTest(key=key), self.assertRaises(AssertionError): proof.proto_plan(p)
        for value in ('{"a":1,"a":2}', '{"source":{"a":1,"a":2}}', '{"a":NaN}'):
            with self.subTest(value=value), self.assertRaises(AssertionError): proof.unique_json(value)

    def test_unknown_is_absent_fact_not_zero_false_null_or_success(self):
        q = request(); value = receipt(q, 'Unknown'); result = proof.proto_receipt(value, q)
        self.assertIsNone(result['restoredByThisOperation'])
        for key in ('sourceRetained', 'sourceRestored', 'restoredByThisOperation'):
            for bad in (False, True, None, 0):
                with self.subTest(key=key, bad=bad), self.assertRaises(AssertionError): proof.proto_receipt({**value, key: bad}, q)
        for state in ('Completed', 'Aborted'):
            value = receipt(q, state); del value['sourceRetained']
            with self.subTest(state=state), self.assertRaises(AssertionError): proof.proto_receipt(value, q)

    def test_full_request_receipt_and_new_source_identity_cannot_be_substituted(self):
        q = request(); good = receipt(q)
        for key, bad in (('operationId', 'd'*32), ('sourceOperationId', q['operationId']), ('fingerprint', 'F'*64), ('targetProof', 'F'*64), ('historyDigest', 'F'*64), ('historyVerified', 1), ('sourceRestored', False)):
            with self.subTest(key=key), self.assertRaises(AssertionError): proof.proto_receipt({**good, key: bad}, q)
        q['operationId'] = q['plan']['command']['source']['operationId']
        with self.assertRaises(AssertionError): proof.request_proof(q)

    def test_unclaimed_abort_never_borrows_current_source_history_or_restoration(self):
        q = request(); value = receipt(q, 'Aborted'); value.update(historyVerified=False, historyAvailable=False, historyDigest='')
        for k in ('sourceState', 'sourceRetained', 'sourceRestored'): del value[k]
        self.assertFalse(proof.proto_receipt(value, q)['restoredByThisOperation'])
        for changes in (dict(historyAvailable=True), dict(historyDigest=q['plan']['command']['historyDigest']), dict(sourceState='Restored'), dict(restoredByThisOperation=True)):
            with self.subTest(changes=changes), self.assertRaises(AssertionError): proof.proto_receipt({**value, **changes}, q)

    def test_capability_and_framing_need_the_correct_phase_protocol_and_job(self):
        error = dict(terminalProbe='failed', phase='capabilities', code='CONTAINER_RECYCLE_RESTORE_CAPABILITY_UNAVAILABLE', failureType='InvalidOperationException', rpcStatus=None, fileCode=None)
        self.assertEqual(proof.capability_refusal(1, '', error)['operationFramesSent'], 0)
        for changes in (dict(code='CONTAINER_RECYCLE_CAPABILITY_UNAVAILABLE'), dict(phase='apply'), dict(rpcStatus='Unavailable')):
            with self.subTest(changes=changes), self.assertRaises(AssertionError): proof.capability_refusal(1, '', {**error, **changes})
        q = request(); header = dict(revision='a'*40, probeRevision='b'*40, containerId='a'*64, coreId='b'*64, instanceId=q['plan']['command']['source']['plan']['command']['instanceId'])
        frame = dict(kind='ready', revision=header['revision'], probeRevision=header['probeRevision'], protocolMinor=56)
        with self.assertRaises(AssertionError): proof.ready_proof(frame, header)
        frame['protocolMinor'] = 57; proof.ready_proof(frame, header)
        frame = dict(kind='plan', plan=json.dumps(wire_plan(q['plan'])), boundaryRefusals=16)
        self.assertEqual(proof.plan_frame(frame, header, boundaries=True), q['plan'])
        frame = dict(kind='receipt', action='apply', operationId=q['operationId'], jobId=q['operationId'], result=json.dumps(receipt(q)))
        proof.receipt_frame(frame, q); frame['jobId'] = q['plan']['command']['source']['operationId']
        with self.assertRaises(AssertionError): proof.receipt_frame(frame, q)


@unittest.skipUnless(os.name == 'posix' and os.geteuid() == 0, 'root temporary Linux metadata required')
class RestoreOriginalProofTests(unittest.TestCase):
    def setUp(self):
        self.base = native.recycling.LinuxContainerRecycleTests(); self.base.setUp(); self.addCleanup(self.base.doCleanups)
        (self.base.root/'work').mkdir(); leaf = self.base.root/proof.source_proof.PATH; self.base.leaf.rename(leaf); self.base.leaf = leaf
        self.source = self.base.engine
        command = {**self.base.session.request, 'path': proof.source_proof.PATH}
        command['expectedVersion'] = self.base.r.version(command, command['path'], leaf.stat())
        self.ref = dict(operationId=uuid.uuid4().hex, owner=native.staging.OWNER, plan=self.source.plan(command, self.base.session))
        self.source.apply(self.ref, self.base.session)
        self.state = self.base.disk.disk/'restore-state'; self.state.mkdir(mode=0o700); (self.state/'claims').mkdir(mode=0o700)
        self.engine = native.candidate.restores_type(native.recycling.directories.ContainerDirectories)(self.source, self.state)
        self.command = dict(source=copy.deepcopy(self.ref), historyDigest=self.base.record(self.ref)['historyDigest'])
        self.q = dict(operationId=uuid.uuid4().hex, owner='F'*64, plan=self.engine.plan(self.command, self.base.session))

    def originals(self):
        own = json.loads((self.state/(self.q['operationId']+'.json')).read_bytes()); claim = self.state/'claims'/(self.ref['operationId']+'.json')
        return [own, json.loads(claim.read_bytes()) if claim.exists() else None, self.base.record(self.ref), self.base.saved(self.ref)]

    def check(self, result, originals=None):
        return proof.record_proof(self.q, result, *(originals or self.originals()), data_sha256=hashlib.sha256(self.base.content).hexdigest().upper(), data_size=len(self.base.content))

    def test_completed_projects_new_journal_claim_old_journal_and_hdd_without_claiming_namespace(self):
        result = self.engine.apply(self.q, self.base.session); value = self.check(result)
        self.assertTrue(value['sourceClaimMatches']); self.assertFalse(value['physicalNamespaceChecked']); self.assertEqual(value['state'], 'Completed')
        self.assertEqual(self.base.leaf.read_bytes(), self.base.content)

    def test_cancel_and_late_apply_cannot_claim_another_jobs_later_restoration(self):
        result = self.engine.discard(self.q, self.base.session); self.check(result)
        other = dict(operationId=uuid.uuid4().hex, owner='E'*64, plan=self.q['plan']); self.engine.apply(other, self.base.session)
        with patch.object(self.base.f, 'exchange', side_effect=AssertionError('never resume')):
            result = self.engine.apply(self.q, self.base.session)
        value = self.check(result); self.assertFalse(value['sourceClaimMatches']); self.assertFalse(result['restoredByThisOperation'])

    def test_actual_namespace_conflict_yields_unknown_and_known_false_forgery_is_rejected(self):
        def conflict(phase):
            if phase == 'Restoring': self.base.leaf.write_bytes(b'late target')
        with patch.object(self.source, 'checkpoint', side_effect=conflict), self.assertRaises(OSError): self.engine.apply(self.q, self.base.session)
        result = self.engine.inspect(self.q, self.base.session); self.assertEqual(result['state'], 'Unknown'); self.check(result)
        bad = copy.deepcopy(result); bad['restoredByThisOperation'] = False
        with self.assertRaises(AssertionError): self.check(bad)
        self.assertEqual(self.base.leaf.read_bytes(), b'late target'); self.assertEqual((self.base.leaf.parent/self.source.PRIVATE/(self.ref['operationId']+'.trash')).read_bytes(), self.base.content)

    def test_completed_label_without_the_exact_durable_source_claim_is_not_proof(self):
        result = self.engine.apply(self.q, self.base.session)
        for field in ('missing', 'owner', 'fingerprint', 'source-plan', 'source-id', 'source-owner', 'claim-id'):
            originals = self.originals(); claim = originals[1]
            if field == 'missing': originals[1] = None
            elif field == 'owner': claim['restoreOwner'] = 'A'*64
            elif field == 'fingerprint': claim['restoreFingerprint'] = 'A'*64
            elif field == 'source-plan': claim['source']['plan']['sha256'] = 'A'*64
            elif field == 'source-id': claim['source']['operationId'] = 'e'*32
            elif field == 'source-owner': claim['source']['owner'] = 'A'*64
            else: claim['restoreOperationId'] = 'e'*32
            with self.subTest(field=field), self.assertRaises(AssertionError): self.check(result, originals)

    def test_self_consistent_hdd_rehash_wrong_source_or_old_identity_does_not_certify_completion(self):
        result = self.engine.apply(self.q, self.base.session)
        for field in ('new-root-id', 'claimed', 'outcome', 'source-state', 'private', 'source-inode', 'hdd-bytes', 'hdd-state', 'hdd-owner', 'digest'):
            originals = self.originals(); own, _, old, hdd = originals
            if field == 'new-root-id': own['operationId'] = self.ref['operationId']
            elif field == 'claimed': own['claimed'] = False
            elif field == 'outcome': own['sourceOutcome'] = 'Trashed'
            elif field == 'source-state': old['state'] = 'Trashed'
            elif field == 'private': old['privateIdentity'][1] += 1
            elif field == 'source-inode': old['actualIdentity'][1] += 1
            elif field == 'hdd-bytes': hdd['sha256'] = 'A'*64
            elif field == 'hdd-state': hdd['captureState'] = 'Trashed'
            elif field == 'hdd-owner': hdd['owner'] = 'A'*64
            else:
                hdd['createdAtUnixMs'] += 1; hdd['historyDigest'] = proof.digest(b'whaledeck-container-recycle-history-v1\n', {k: v for k, v in hdd.items() if k not in ('state', 'captureState', 'historyDigest')}); old['historyDigest'] = hdd['historyDigest']
            with self.subTest(field=field), self.assertRaises(AssertionError): self.check(result, originals)
