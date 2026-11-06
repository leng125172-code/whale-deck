"""Synthetic three-stage grammar only; never signed RPC or physical proof."""
import copy,json,unittest
import container_recycle_archive_workflow as workflow
import test_container_recycle_archive_rpc_proof as fixture
import test_container_recycle_purge_workflow as old


def transcript():
    frames,header=old.transcript();frames.pop();frames[0]['protocolMinor']=59;p=fixture.plan();cancel='e'*32;normal='f'*32
    frames.append(dict(kind='archive-plan',plan=json.dumps(fixture.wire_plan(p)),boundaryRefusals=22));frames.append(dict(kind='archive-rejected',operationId=cancel,refusals=31))
    def steps(op,profile):
        q=fixture.request(p,op)
        for action,state in profile:frames.append(dict(kind='archive-receipt',action=action,operationId=op,jobId=op,result=json.dumps(fixture.receipt(q,state,True if state=='Completed' else None))))
    steps(cancel,workflow.CANCEL);frames.append(dict(kind='archive-plan',plan=json.dumps(fixture.wire_plan(p)),boundaryRefusals=0));steps(normal,workflow.NORMAL);frames.append(copy.deepcopy(frames[18]));q=fixture.request(p,normal);frames.append(dict(kind='archive-receipt',action='archive-inspect',operationId=normal,jobId=normal,result=json.dumps(fixture.receipt(q,'Completed',False))));frames.append(dict(kind='done'));return frames,header


class ArchiveWorkflowTests(unittest.TestCase):
    def test_all_three_stages_and_five_ids_are_bound_without_physical_claim(self):
        f,h=transcript();s=workflow.transcript_proof(f,h);self.assertEqual(s['archiveReceiptFrames'],12);self.assertEqual(s['archivePlanRefusals'],22);self.assertFalse(s['physicalNamespaceChecked'])

    def test_missing_duplicated_reordered_wrong_id_or_count_never_signs(self):
        for kind in ('missing','duplicate','order','job','count','old-id','source-end'):
            f,h=transcript()
            if kind=='missing':f.pop(29)
            elif kind=='duplicate':f.insert(29,copy.deepcopy(f[29]))
            elif kind=='order':f[28],f[29]=f[29],f[28]
            elif kind=='job':f[29]['jobId']='a'*32
            elif kind=='count':f[20]['refusals']=30
            elif kind=='old-id':f[27]['operationId']='d'*32
            else:f[33]['action']='source-apply'
            with self.subTest(kind=kind),self.assertRaises(AssertionError):workflow.transcript_proof(f,h)

    def test_unknown_or_missing_current_binding_cannot_replace_live_completed(self):
        for kind in ('unknown','binding'):
            f,h=transcript();q=fixture.request();f[29]['result']=json.dumps(fixture.receipt(q,'Unknown' if kind=='unknown' else 'Completed',None))
            with self.subTest(kind=kind),self.assertRaises(AssertionError):workflow.transcript_proof(f,h)

    def test_live_prefix_is_not_a_finished_probe_and_cleanup_has_separate_binding_observation(self):
        f,h=transcript();prefix=workflow.prefix_proof(f[:34],h);self.assertFalse(prefix['closed']);self.assertFalse(prefix['afterCleanupBindingObserved'])
        q=fixture.request()
        result=workflow.transcript_proof(f,h);self.assertTrue(result['closed']);self.assertTrue(result['afterCleanupBindingObserved']);self.assertEqual(result['archiveReceiptFrames'],12)
        f[34]['result']=json.dumps(fixture.receipt(q,'Completed',True))
        with self.assertRaises(AssertionError):workflow.transcript_proof(f,h)
        f,h=transcript();f.pop(34)
        with self.assertRaises(AssertionError):workflow.transcript_proof(f,h)


if __name__=='__main__':unittest.main()
