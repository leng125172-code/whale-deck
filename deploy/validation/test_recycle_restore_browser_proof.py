"""Foreign IDs, lossy identities and fabricated restore/job facts fail closed.

Pure fixture tests do not establish installed identity, RPC or HDD acceptance.
"""
import copy,unittest,uuid
from pathlib import Path
from recycle_restore_browser_proof import managed_plan,managed_receipt,job_summary,execution
from test_container_recycle_restore_rpc_proof import plan,rehash,receipt
from test_container_recycle_browser_scope import managed as managed_source
from container_recycle_restore_rpc_proof import native_plan_proof


def managed(p):
    return dict(SchemaVersion=p['schemaVersion'],Command=dict(Source=dict(OperationId=str(uuid.UUID(p['command']['source']['operationId'])),Owner=p['command']['source']['owner'],Plan=managed_source(p['command']['source']['plan'])),HistoryDigest=p['command']['historyDigest']),TargetProof=p['targetProof'],
        ParentIdentity=dict(zip(('Device','Inode'),p['parentIdentity'],strict=True)),ParentVersion=p['parentVersion'],OwnerUid=p['uid'],OwnerGid=p['gid'],
        PrivateIdentity=dict(zip(('Device','Inode'),p['privateIdentity'],strict=True)),RetainedIdentity=dict(zip(('Device','Inode','Mode','OwnerUid','OwnerGid','LinkCount','SizeBytes','ModifiedAtUnixNs'),p['retainedIdentity'][:8],strict=True)),RetainedChangedAtUnixNs=p['retainedIdentity'][8],Fingerprint=p['fingerprint'])


class RestoreBrowserProofTests(unittest.TestCase):
    def test_managed_plan_preserves_uint64_nanoseconds_and_source_domain(self):
        p=plan();p['privateIdentity']=[0,2**64-1];p['retainedIdentity'][8]=2**63-1;rehash(p)
        value=managed_plan(managed(p));self.assertEqual(p,value);native_plan_proof(value)

    def test_float_and_boolean_cannot_masquerade_as_retained_identity(self):
        for key,value in [('RetainedChangedAtUnixNs',float(2**63-1)),('OwnerUid',True),('RetainedChangedAtUnixNs',True)]:
            m=managed(plan());m[key]=value
            with self.subTest(key=key,value=value),self.assertRaises(AssertionError):managed_plan(m)

    def test_unknown_nested_plan_fields_cannot_be_dropped_by_projection(self):
        for path in ((),('Command',),('Command','Source'),('Command','Source','Plan'),('Command','Source','Plan','Command'),('RetainedIdentity',),('PrivateIdentity',)):
            m=managed(plan());node=m
            for key in path:node=node[key]
            node['HostPath']='/etc'
            with self.subTest(path=path),self.assertRaises(AssertionError):managed_plan(m)

    def test_rehashed_plan_cannot_borrow_another_source_inode_or_owner(self):
        for kind in ('inode','owner','source-fingerprint'):
            p=plan()
            if kind=='inode':p['retainedIdentity'][1]+=1
            elif kind=='owner':p['uid']+=1
            else:p['command']['source']['plan']['fingerprint']='F'*64
            rehash(p)
            with self.subTest(kind=kind),self.assertRaises(AssertionError):managed_plan(managed(p))

    def test_managed_receipt_requires_separate_restore_id_and_exact_source(self):
        q=dict(operationId='c'*32,owner='C'*64,plan=plan());r=receipt(q)
        dto={k:v for k,v in r.items() if k!='operationId'};dto['id']=str(uuid.UUID(q['operationId']));dto['sourceOperationId']=str(uuid.UUID(dto['sourceOperationId']))
        self.assertEqual(managed_receipt(dto,q),r)
        for changes in (dict(id=dto['sourceOperationId']),dict(sourceOperationId=dto['id']),dict(restoredByThisOperation=False),dict(historyAvailable=1),dict(hostPath='/etc')):
            with self.subTest(changes=changes),self.assertRaises(AssertionError):managed_receipt({**dto,**changes},q)

    def test_null_unknown_facts_remain_null_and_cannot_become_success(self):
        q=dict(operationId='c'*32,owner='C'*64,plan=plan());r=receipt(q,'Unknown')
        dto={k:v for k,v in r.items() if k!='operationId'};dto.update(id=str(uuid.UUID(q['operationId'])),sourceOperationId=str(uuid.UUID(r['sourceOperationId'])),sourceState=None,sourceRetained=None,sourceRestored=None,restoredByThisOperation=None)
        self.assertIsNone(managed_receipt(dto,q)['restoredByThisOperation'])
        for key in ('restoredByThisOperation','sourceRestored','sourceRetained'):
            with self.subTest(key=key),self.assertRaises(AssertionError):managed_receipt({**dto,key:False},q)

    def test_source_and_restore_execution_domains_and_guid_endianness_differ(self):
        value='00000000-0000-0000-0000-000000000001'
        self.assertNotEqual(execution(value),execution(value,source=True))
        for bad in (value.upper().replace('1','A'),uuid.UUID(value).hex,'00000000-0000-0000-0000-000000000000',True):
            with self.subTest(bad=bad),self.assertRaises((AssertionError,ValueError)):execution(bad)

    def test_planning_summary_does_not_claim_canceled_ready_intent_was_executed(self):
        row=dict(id='10000000-0000-0000-0000-000000000001',source_intent_id='20000000-0000-0000-0000-000000000001',state='Canceled')
        value=dict(schemaVersion=1,intentId=row['id'],jobId=row['id'],sourceIntentId=row['source_intent_id'],sourceOperationId=execution(row['source_intent_id'],source=True),stage='planning',state='Ready',code='CONTAINER_RECYCLE_RESTORE_READY',receiptState=None,sourceState=None,bindingCurrent=None,sourceRetained=None,sourceRestored=None,restoredByThisOperation=None,historyVerified=False,historyAvailable=None,publishBarrier=False)
        self.assertEqual(job_summary(value,row,execution_stage=False),value)
        for change in (dict(state='Succeeded'),dict(restoredByThisOperation=False),dict(jobId=execution(row['id'])),dict(sourceOperationId=execution(row['source_intent_id']))):
            with self.subTest(change=change),self.assertRaises(AssertionError):job_summary({**value,**change},row,execution_stage=False)

    def test_gate_cleanup_is_after_separate_live_verification_and_keeps_failed_fixture(self):
        root=Path(__file__).parent;parent=(root/'validate_container_recycle_restore_browser.py').read_text();browser=(root/'validate_container_recycle_restore_browser.mjs').read_text()
        self.assertLess(parent.index('independent(repo,e,True)'),parent.index('leaf.unlink()'))
        self.assertIn("f['retain_unsettled']=True",parent);self.assertIn("f['retain_unsettled']=False",parent)
        for forbidden in ('page.route(', 'addCookies(', 'storageState:', 'docker exec', 'restore_io.apply('):self.assertNotIn(forbidden,browser+parent)
        self.assertIn("coordinate('source'",browser);self.assertIn('coordinate(item.name',browser)
        self.assertIn('isJobPageResponse(r, base)',browser)


if __name__=='__main__':unittest.main()
