"""Transcript grammar and exact namespace allowances, with explicit synthetic frames."""
import copy
import json
import unittest
import container_recycle_restore_workflow as workflow
import test_container_recycle_restore_rpc_proof as fixture
from validate_container_recycle_restore_agent import preserved


def transcript():
    p = fixture.plan(); old = p['command']['source']; cancel = 'c'*32; normal = 'd'*32
    header = dict(revision='a'*40, probeRevision='b'*40, containerId=old['plan']['command']['containerId'], instanceId=old['plan']['command']['instanceId'], coreId='b'*64)
    frames = [dict(kind='ready', revision=header['revision'], probeRevision=header['probeRevision'], protocolMinor=57), dict(kind='source-plan', plan=json.dumps(fixture.old_wire.wire_plan(old['plan'])))]
    steps = []
    def source(action, state):
        value = dict(operationId=old['operationId'], containerId=header['containerId'], instanceId=header['instanceId'], fingerprint=old['plan']['fingerprint'], targetProof=old['plan']['targetProof'], state=state,
            bindingCurrent=True, replayed=action != 'source-apply', historyMayExist=True, historyVerified=True, historyDigest=p['command']['historyDigest'], historyAvailable=True, sourceRetained=state == 'Trashed', sourceRestored=state == 'Restored')
        frame = dict(kind='source-receipt', action=action, operationId=old['operationId'], jobId=old['operationId'], result=json.dumps(value)); frames.append(frame)
        steps.append(dict(phase='source', action=action, request=old, frame=frame, receipt=workflow.source_receipt(frame, old), records=dict(state=state), physical=dict(state=state)))
    source('source-apply', 'Trashed'); source('source-inspect', 'Trashed')
    frames.extend([dict(kind='plan', plan=json.dumps(fixture.wire_plan(p)), boundaryRefusals=16), dict(kind='rejected', operationId=cancel, refusals=16)])
    for key, action, state in workflow.RESTORE_ACTIONS:
        op = cancel if key == 'cancel' else normal; request = dict(operationId=op, owner='C'*64, plan=p)
        value = fixture.receipt(request, state); value['replayed'] = action != 'apply' or key == 'cancel'
        if key == 'cancel':
            value.update(bindingCurrent=False, historyVerified=False, historyAvailable=False, historyDigest='')
            for k in ('sourceState', 'sourceRetained', 'sourceRestored'): del value[k]
        # The second normal Apply is replay; initial Apply is not.
        if key == 'normal': value['replayed'] = any(s['phase'] == 'restore' and s['request']['operationId'] == op for s in steps)
        frame = dict(kind='receipt', action=action, operationId=op, jobId=op, result=json.dumps(value)); frames.append(frame)
        steps.append(dict(phase='restore', action=action, request=request, frame=frame, receipt=workflow.restore.receipt_frame(frame, request), records=dict(state=state), physical=dict(state=state)))
    source('source-inspect', 'Restored'); frames.append(dict(kind='done'))
    return frames, steps, header


class RestoreWorkflowTests(unittest.TestCase):
    def test_original_and_two_distinct_new_ids_are_bound_through_all_19_frames(self):
        frames, steps, header = transcript(); result = workflow.transcript_proof(frames, steps, header)
        self.assertEqual((result['newReceiptFrames'], result['sourceReceiptFrames']), (11, 3)); self.assertEqual(len({result[k] for k in ('sourceOperationId', 'cancelOperationId', 'restoreOperationId')}), 3)

    def test_reordered_missing_or_extra_receipts_do_not_pass_the_full_gate(self):
        for kind in ('reorder', 'missing', 'extra', 'no-done', 'missing-refusal'):
            frames, steps, header = transcript()
            if kind == 'reorder': frames[6], frames[7] = frames[7], frames[6]
            elif kind == 'missing': frames.pop(9)
            elif kind == 'extra': frames.insert(9, copy.deepcopy(frames[8]))
            elif kind == 'no-done': frames[-1] = dict(kind='ready')
            else: frames[5]['refusals'] = 15
            with self.subTest(kind=kind), self.assertRaises(AssertionError): workflow.transcript_proof(frames, steps, header)

    def test_source_id_plan_history_and_new_jobs_cannot_be_relabelled(self):
        for kind in ('source-owner', 'source-plan', 'job', 'same-id', 'restore-plan', 'header'):
            frames, steps, header = copy.deepcopy(transcript())
            if kind == 'source-owner': steps[0]['request']['owner'] = 'F'*64
            elif kind == 'source-plan': steps[0]['request']['plan']['targetProof'] = 'F'*64
            elif kind == 'job': frames[6]['jobId'] = steps[0]['request']['operationId']
            elif kind == 'same-id': steps[6]['request']['operationId'] = steps[2]['request']['operationId']
            elif kind == 'restore-plan': steps[6]['request']['plan']['fingerprint'] = 'F'*64
            else: header['instanceId'] = '888:2026-11-05T01:02:03Z'
            with self.subTest(kind=kind), self.assertRaises(AssertionError): workflow.transcript_proof(frames, steps, header)

    def test_replay_cannot_change_persisted_originals_or_physical_namespace(self):
        for field in ('records', 'physical', 'receipt'):
            frames, steps, header = copy.deepcopy(transcript())
            if field == 'receipt':
                raw = json.loads(frames[7]['result']); raw['replayed'] = False; frames[7]['result'] = json.dumps(raw); steps[3]['receipt']['replayed'] = False
            else: steps[3][field]['state'] = 'different'
            with self.subTest(field=field), self.assertRaises(AssertionError): workflow.transcript_proof(frames, steps, header)


class RestorePreservationTests(unittest.TestCase):
    def setUp(self):
        self.source = 'a'*32; self.cancel = 'c'*32; self.normal = 'd'*32
        self.before = dict(core=['stable'], recycleRoot={'old': {'sha': 'stable'}}, recycleHdd={'old': {'sha': 'stable'}}, recycleRestoreRoot={'old': {'sha': 'stable'}})
        self.after = copy.deepcopy(self.before)
        self.after['recycleRoot'][self.source+'.json'] = {}; self.after['recycleHdd'][self.source+'.data'] = {}
        for op in (self.cancel, self.normal): self.after['recycleRestoreRoot'][op+'.json'] = {}; self.after['recycleRestoreRoot'][op+'.lock'] = {}
        self.after['recycleRestoreRoot']['claims/'+self.source+'.json'] = {}

    def check(self, value): preserved(self.before, value, self.source, (self.cancel, self.normal))

    def test_only_owned_source_two_restore_ids_and_the_original_source_claim_are_allowed(self): self.check(self.after)

    def test_new_restore_id_is_not_a_source_history_or_another_source_claim(self):
        for key, name in (('recycleRoot', self.normal+'.json'), ('recycleHdd', self.normal+'.data'), ('recycleRestoreRoot', 'claims/'+self.normal+'.json'), ('recycleRestoreRoot', 'e'*32+'.json'), ('recycleHdd', self.source+'.partial')):
            value = copy.deepcopy(self.after); value[key][name] = {}
            with self.subTest(key=key, name=name), self.assertRaises(AssertionError): self.check(value)

    def test_old_roots_claims_hdd_and_core_cannot_change_or_disappear(self):
        for key in ('core', 'recycleRoot', 'recycleHdd', 'recycleRestoreRoot'):
            value = copy.deepcopy(self.after)
            if key == 'core': value[key] = ['changed']
            else: del value[key]['old']
            with self.subTest(key=key), self.assertRaises(AssertionError): self.check(value)
