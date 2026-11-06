"""Synthetic complete transcripts test grammar only, not RPC or HDD acceptance."""
import copy
import json
import unittest
import container_recycle_purge_workflow as workflow
import test_container_recycle_purge_rpc_proof as fixture


def transcript():
    p=fixture.plan();ref=p['command']['source'];header=dict(revision='a'*40,probeRevision='b'*40,containerId=ref['plan']['command']['containerId'],instanceId=ref['plan']['command']['instanceId'],coreId='b'*64)
    frames=[dict(kind='ready',revision=header['revision'],probeRevision=header['probeRevision'],protocolMinor=58),dict(kind='source-plan',plan=json.dumps(fixture.old.wire_plan(ref['plan'])))]
    def src(action,state):
        value=dict(operationId=ref['operationId'],containerId=header['containerId'],instanceId=header['instanceId'],fingerprint=ref['plan']['fingerprint'],targetProof=ref['plan']['targetProof'],state=state,bindingCurrent=state=='Trashed',replayed=action=='source-inspect',historyMayExist=True,historyVerified=True,historyAvailable=True,historyDigest=p['command']['historyDigest'],sourceRetained=state=='Trashed',sourceRestored=False)
        frames.append(dict(kind='source-receipt',action=action,operationId=ref['operationId'],jobId=ref['operationId'],result=json.dumps(value)))
    src('source-apply','Trashed');src('source-inspect','Trashed');frames.extend([dict(kind='plan',plan=json.dumps(fixture.wire_plan(p)),boundaryRefusals=18),dict(kind='rejected',operationId='c'*32,refusals=26)])
    def steps(operation,profile):
        q=dict(operationId=operation,owner='C'*64,plan=p)
        for action,state in profile:frames.append(dict(kind='receipt',action=action,operationId=operation,jobId=operation,result=json.dumps(fixture.receipt(q,state))))
    steps('c'*32,workflow.CANCEL);frames.append(dict(kind='plan',plan=json.dumps(fixture.wire_plan(p)),boundaryRefusals=0));steps('d'*32,workflow.NORMAL);src('source-inspect','Purged');frames.append(dict(kind='done'));return frames,header


class PurgeWorkflowTests(unittest.TestCase):
    def test_full_profile_has_three_distinct_ids_and_all_twenty_frames(self):
        f,h=transcript();v=workflow.transcript_proof(f,h);self.assertEqual((v['receiptFrames'],v['sourceReceiptFrames']),(11,3));self.assertEqual(len({v[k] for k in ('sourceOperationId','cancelOperationId','purgeOperationId')}),3);self.assertFalse(v['physicalNamespaceChecked'])

    def test_missing_duplicate_reordered_and_wrong_job_frames_are_rejected(self):
        for kind in ('missing','duplicate','order','job','refusal','done'):
            f,h=transcript()
            if kind=='missing':f.pop(14)
            elif kind=='duplicate':f.insert(14,copy.deepcopy(f[14]))
            elif kind=='order':f[13],f[14]=f[14],f[13]
            elif kind=='job':f[14]['jobId']='e'*32
            elif kind=='refusal':f[5]['refusals']=25
            else:f[-1]=dict(kind='ready')
            with self.subTest(kind=kind),self.assertRaises(AssertionError):workflow.transcript_proof(f,h)

    def test_unknown_or_forged_success_cannot_replace_any_required_state(self):
        for at in (6,8,12,14):
            f,h=transcript();frame=f[at];q=dict(operationId=frame['operationId'],owner='C'*64,plan=fixture.plan());frame['result']=json.dumps(fixture.receipt(q,'Unknown'))
            with self.subTest(at=at),self.assertRaises(AssertionError):workflow.transcript_proof(f,h)


if __name__=='__main__':unittest.main()
