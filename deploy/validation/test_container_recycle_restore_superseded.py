"""Superseded preserves completed ownership; pure projections are not live proof."""
import copy,json,unittest,uuid
from pathlib import Path
from unittest.mock import patch
import restore_superseded_proof as proof
from test_container_recycle_restore_rpc_proof import plan,rehash,receipt
from test_recycle_restore_browser_proof import managed
from recycle_restore_browser_proof import execution


def case():
    identifier='10000000-0000-0000-0000-000000000001';source_id='20000000-0000-0000-0000-000000000001'
    p=plan();p['command']['source']['operationId']=uuid.UUID(execution(source_id,source=True)).hex;rehash(p)
    q=dict(operationId=uuid.UUID(execution(identifier)).hex,owner='C'*64,plan=p);r=receipt(q);r['bindingCurrent']=False
    dto={k:v for k,v in r.items() if k!='operationId'};dto['id']=str(uuid.UUID(q['operationId']));dto['sourceOperationId']=execution(source_id,source=True)
    command=p['command']['source']['plan']['command'];fixture=dict(subject='actor',containerId=command['containerId'],instanceId=command['instanceId'])
    row=dict(id=identifier,actor_subject='actor',container_id=fixture['containerId'],instance_id=fixture['instanceId'],relative_path='work/recycle-rpc.bin',state='Superseded',result_code='CONTAINER_RECYCLE_RESTORE_TARGET_CHANGED',completed_at_utc='2026-11-06T01:00:00Z',root_started_at_utc='2026-11-06T00:59:00Z',cancel_requested_at_utc=None,lease_token=None,lease_expires_at_utc=None,publish_barrier=False,root_state='Completed',source_state='Restored',binding_current=False,source_retained=False,history_verified=True,history_available=True,source_restored=True,restored_by_this_operation=True,acknowledged_by=None,acknowledgement_json=None,execution_job_id=execution(identifier),source_intent_id=source_id,plan_json=managed(p),owner_hash=q['owner'],history_digest=p['command']['historyDigest'],receipt_json={k[0].upper()+k[1:]:v for k,v in dto.items()})
    safe=dict(schemaVersion=1,intentId=identifier,jobId=execution(identifier),sourceIntentId=source_id,sourceOperationId=execution(source_id,source=True),stage='execution',state='Superseded',code=row['result_code'],receiptState='Completed',sourceState='Restored',bindingCurrent=False,sourceRetained=False,sourceRestored=True,restoredByThisOperation=True,historyVerified=True,historyAvailable=True,publishBarrier=False)
    ready={**safe,'jobId':identifier,'stage':'planning','state':'Ready','code':'CONTAINER_RECYCLE_RESTORE_READY','receiptState':None,'sourceState':None,'bindingCurrent':None,'sourceRetained':None,'sourceRestored':None,'restoredByThisOperation':None,'historyVerified':False,'historyAvailable':None}
    value=dict(id=identifier,executionJobId=execution(identifier),sourceIntentId=source_id,sourceOperationId=execution(source_id,source=True),sourceHistoryDigest=p['command']['historyDigest'],state='Superseded',confirmation=None,receipt=dto)
    source=dict(sql=dict(row=dict(id=source_id,execution_job_id=execution(source_id,source=True))),request=p['command']['source'])
    data=dict(row=row,pending=0,accepted=1,confirmed=1,canceled=0,planJob=dict(id=identifier,state='Succeeded',result_json=ready),executionJob=dict(id=execution(identifier),state='Failed',phase='Superseded',error_code=row['result_code'],completed_at_utc=row['completed_at_utc'],cancel_requested_at_utc=None,result_json=safe))
    return value,data,fixture,source


class SupersededProofTests(unittest.TestCase):
    def test_completed_history_with_lost_binding_is_superseded_not_unknown(self):
        v,d,f,s=case();q,r=proof.terminal(v,d,f,s);self.assertEqual(r['state'],'Completed');self.assertFalse(r['bindingCurrent']);self.assertEqual(uuid.UUID(v['executionJobId']).hex,q['operationId'])

    def test_unknown_canceled_or_current_binding_cannot_borrow_completed_ownership(self):
        for key,bad in [('state','Unknown'),('root_state','Unknown'),('source_state','Trashed'),('binding_current',True),('source_retained',True),('source_restored',None),('restored_by_this_operation',False),('publish_barrier',True),('history_verified',False),('cancel_requested_at_utc','cancel'),('acknowledged_by','other')]:
            v,d,f,s=case();d['row'][key]=bad
            with self.subTest(key=key),self.assertRaises(AssertionError):proof.terminal(v,d,f,s)

    def test_foreign_job_source_or_actor_is_rejected(self):
        for target,key,bad in [('row','execution_job_id','foreign'),('row','source_intent_id','foreign'),('row','actor_subject','foreign'),('executionJob','id','foreign'),('executionJob','state','Succeeded'),('executionJob','phase','Succeeded')]:
            v,d,f,s=case();d[target][key]=bad
            with self.subTest(target=target,key=key),self.assertRaises((AssertionError,ValueError)):proof.terminal(v,d,f,s)

    def test_receipt_summary_or_stored_dto_cannot_silently_drop_a_fact(self):
        for target in ('receipt','summary','stored'):
            v,d,f,s=case()
            if target=='receipt':v['receipt']['restoredByThisOperation']=None
            elif target=='summary':d['executionJob']['result_json']['restoredByThisOperation']=None
            else:d['row']['receipt_json']['RestoredByThisOperation']=False
            with self.subTest(target=target),self.assertRaises(AssertionError):proof.terminal(v,d,f,s)

    def test_pending_outbox_cancel_or_unconfirmed_execution_is_not_terminal(self):
        for key,bad in [('pending',1),('canceled',1),('confirmed',0)]:
            v,d,f,s=case();d[key]=bad
            with self.subTest(key=key),self.assertRaises(AssertionError):proof.terminal(v,d,f,s)

    def test_foreign_fixture_is_rejected_before_docker_read_or_rename(self):
        with patch.object(proof.os,'geteuid',return_value=0),patch.object(proof.common,'command') as command,patch.object(proof,'rename_noreplace') as rename,self.assertRaises(AssertionError):
            proof.displace_returned(dict(identifier='a'*64,folder=Path('/etc')),dict(containerId='b'*64,folder='/etc'),None,{}, {})
        command.assert_not_called();rename.assert_not_called()

    def test_boolean_counts_do_not_substitute_for_database_counts(self):
        v,d,f,s=case();d['pending']=False
        with self.assertRaises(AssertionError):proof.terminal(v,d,f,s)

    def test_displaced_cleanup_requires_independent_exact_inode_and_bytes(self):
        original=dict(identity=[2049,71,0o100640,1000,1000,1,7,10,11],sha256='A'*64,attributes=[],sizeBytes=7)
        moved=copy.deepcopy(original);moved['identity'][8]=22;verified=dict(physical=dict(source=None,retained=None),displaced=copy.deepcopy(moved))
        proof.cleanup_displaced_proof(moved,original,verified)
        for offset in range(8):
            bad=copy.deepcopy(moved);bad['identity'][offset]+=1
            with self.subTest(identity=offset),self.assertRaises(AssertionError):proof.cleanup_displaced_proof(bad,original,{**verified,'displaced':bad})
        with self.assertRaises(AssertionError):proof.cleanup_displaced_proof(moved,original,{**verified,'physical':dict(source=moved,retained=None)})
        stale=copy.deepcopy(moved);stale['identity'][8]+=1
        with self.assertRaises(AssertionError):proof.cleanup_displaced_proof(stale,original,verified)

    def test_fault_and_cleanup_follow_current_hdd_and_independent_proofs(self):
        root=Path(__file__).parent;s=(root/'validate_container_recycle_restore_superseded.py').read_text();browser=(root/'validate_container_recycle_restore_superseded.mjs').read_text()
        self.assertLess(s.index('pending=read_pending('),s.index('displacement=displace_returned('))
        self.assertLess(s.index('independent(repo,e,True)'),s.index('moved.unlink()'))
        for forbidden in ('page.route(', 'addCookies(', 'storageState:', "name: '确认请求取消'"):self.assertNotIn(forbidden,browser)
        self.assertIn("if (result.state === 'Superseded')",browser)
        self.assertIn('assert.deepEqual(retryRequests, [])',browser)


if __name__=='__main__':unittest.main()
