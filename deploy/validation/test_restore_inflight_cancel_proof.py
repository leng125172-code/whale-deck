"""Wrong domain, source, FD aliases and terminal cuts cannot certify UI cancellation."""
import copy,json,unittest,uuid
from pathlib import Path
from validate_container_recycle_restore_inflight_cancel import cut_binding,preserved
from test_container_recycle_restore_rpc_proof import plan,rehash
from test_recycle_restore_browser_proof import managed
from recycle_restore_browser_proof import execution


class RestoreInflightCutBindingTests(unittest.TestCase):
    def setUp(self):
        identifier=str(uuid.uuid4());p=plan();self.op=uuid.UUID(execution(identifier)).hex
        self.row=dict(id=identifier,execution_job_id=execution(identifier),plan_json=json.dumps(managed(p)),owner_hash='A'*64)
        self.record=dict(operationId=self.op,owner='A'*64,plan=p,state='Applying',claimed=True,historyVerified=True)
        self.fixture=dict(rootIdentity=[1,2],original=dict(sha256=p['command']['source']['plan']['sha256']))
        self.proof=dict(restoreOperationId=self.op,sourceOperationId=p['command']['source']['operationId'],rootPhase='Applying',sourcePhase='Trashed',sourceNotReturned=True,sourceClaimMatches=True,threeKernelFdProofs=True,historyDigest=p['command']['historyDigest'],retainedIdentity=p['retainedIdentity'],retainedSha256=self.fixture['original']['sha256'],containerRootIdentity=[1,2],restoreLockIdentity=[1,3],sourceLockIdentity=[1,4],pid=333,invocationId='a'*32,cgroup='whaledeck-container-recycle-restores-'+('b'*32)+'.service')

    def test_original_cut_is_bound_to_its_new_execution_and_source(self):
        cut_binding(self.row,self.record,self.proof,self.fixture)
        for key,value in [('restoreOperationId','d'*32),('sourceOperationId','d'*32),('sourceNotReturned',False),('sourceClaimMatches',False),('rootPhase','Completed'),('sourcePhase','Restored'),('historyDigest','F'*64),('containerRootIdentity',[2,3]),('sourceLockIdentity',[1,3]),('pid',True),('cgroup','whaledeck-container-recycling-'+('b'*32)+'.service')]:
            with self.subTest(key=key),self.assertRaises(AssertionError):cut_binding(self.row,self.record,{**self.proof,key:value},self.fixture)

    def test_rehashed_foreign_plan_or_owner_is_not_the_paused_original(self):
        for kind in ('owner','plan','state'):
            record=copy.deepcopy(self.record)
            if kind=='owner':record['owner']='B'*64
            elif kind=='state':record['state']='Aborted'
            else:record['plan']['privateIdentity'][1]+=1;rehash(record['plan'])
            with self.subTest(kind=kind),self.assertRaises(AssertionError):cut_binding(self.row,record,self.proof,self.fixture)

    def test_new_restore_root_can_only_add_this_execution_pair(self):
        before=dict(recycleRestoreRoot={'old.json':{'sha256':'unchanged'}});after=copy.deepcopy(before);after['recycleRestoreRoot'].update({self.op+'.json':{},self.op+'.lock':{}})
        # The source/core preservation call has its own existing root tests; do not fabricate it here.
        from unittest.mock import patch
        with patch('validate_container_recycle_restore_inflight_cancel.preserve_source'):
            preserved(before,after,'a'*32,self.op)
            for extra in ('claims/'+('a'*32)+'.json','foreign.json'):
                bad=copy.deepcopy(after);bad['recycleRestoreRoot'][extra]={}
                with self.subTest(extra=extra),self.assertRaises(AssertionError):preserved(before,bad,'a'*32,self.op)

    def test_cleanup_follows_independent_live_verifier_and_keeps_unknown_resources(self):
        root=Path(__file__).parent;s=(root/'validate_container_recycle_restore_inflight_cancel.py').read_text();browser=(root/'validate_container_recycle_restore_inflight_cancel.mjs').read_text()
        self.assertLess(s.index('independent(repo,e,True)'),s.index('retained.unlink()'));self.assertIn("f['retain_unsettled']=True",s)
        self.assertLess(s.index('exit_future=pool.submit'),s.index("save('cut-held.json'"));self.assertIn('EXACT_RESTORE_CUT_MISSED_NO_REPLAY',s)
        for forbidden in ('page.route(', 'addCookies(', 'storageState:', 'docker exec'):self.assertNotIn(forbidden,browser)
        self.assertIn('isJobPageResponse(r, base)',browser)


if __name__=='__main__':unittest.main()
