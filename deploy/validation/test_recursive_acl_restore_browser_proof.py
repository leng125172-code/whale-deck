"""Offline adversarial bindings; explicit temporary ACLs, no installed login or writes."""
import ast
import copy
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import Mock,patch

from test_recursive_acl_history_browser_proof import fixture
from validate_container_recursive_acl_restore_browser import selected_snapshot,wait_case,SPECS,SOURCES,expected_execution
from verify_container_recursive_acl_restore_browser import verify,live_verify

ROOT=Path(__file__).resolve().parent

def selection():
    b,r,m,s,_=fixture();signal=dict(source=copy.deepcopy(s['snapshot']),preview=copy.deepcopy(s['snapshot']),sourceRequest=copy.deepcopy(s['request']),previewRequest=copy.deepcopy(s['request']))
    return b,r,dict(metadata=m),signal

class RecursiveRestoreBrowserProofTests(unittest.TestCase):
    def test_source_and_target_preview_keep_exact_zero_mask_and_original_count(self):
        b,r,source,signal=selection();value=selected_snapshot(signal,b,r,source);self.assertEqual(value['changeCount'],0);self.assertEqual(signal['preview']['entries'][0]['accessAcl'][1],dict(tag='user',id=0,permissions=0))
    def test_preview_and_selection_cannot_drop_change_scope_or_coerce_source(self):
        changes=[lambda s:s['source']['summary'].update(changeCount=True),lambda s:s['source']['entries'][0]['accessAcl'][1].update(id=None),lambda s:s['source']['entries'].reverse(),lambda s:s['source'].update(complete=False,nextOffset=1),lambda s:s['preview']['summary'].update(historyDigest='F'*64),lambda s:s['preview']['location'].update(path='other'),lambda s:s['preview']['entries'][0]['accessAcl'][0].update(permissions=True),lambda s:s['preview']['entries'].pop(),lambda s:s['source']['entries'][0]['accessAcl'][1].pop('id')]
        for change in changes:
            b,r,source,s=selection();change(s)
            with self.subTest(index=changes.index(change)),self.assertRaises(AssertionError):selected_snapshot(s,b,r,source)
    def test_both_http_reads_must_be_exact_authorized_get_without_cache(self):
        for field in ('sourceRequest','previewRequest'):
            for change in [lambda q:q.update(method='POST'),lambda q:q.update(status=True),lambda q:q.update(noStore=False),lambda q:q['query'].update(offset='1'),lambda q:q['query'].update(expectedHistoryDigest='F'*64),lambda q:q['query'].update(take='50'),lambda q:q['query'].update(extra='private')]:
                b,r,source,s=selection();change(s[field])
                with self.subTest(field=field),self.assertRaises(AssertionError):selected_snapshot(s,b,r,source)
    def test_waiter_and_verifier_never_select_an_old_or_arbitrary_artifact(self):
        with tempfile.TemporaryDirectory() as folder:
            for name in ('../escape','history-access','restore-access-result.json','restore-cancel/escape','access'):
                with self.subTest(name=name),self.assertRaises(AssertionError):wait_case(Mock(),Path(folder),name)
        for name in ('../escape','container-recursive-acl-browser-'+'A'*32,'container-acl-browser-'+'a'*32):
            with self.subTest(name=name),self.assertRaises(AssertionError):live_verify(name)
            with self.subTest(name=name),self.assertRaises(AssertionError):verify(name)
    def test_only_fixed_restores_with_one_noop_and_one_cancel_are_selected(self):
        self.assertEqual(tuple(v[0] for v in SPECS),('restore-access','restore-noop','restore-default','restore-clear','restore-cancel'))
        self.assertEqual(tuple(v[2] for v in SPECS),(4,0,2,2,4));self.assertTrue(all((ROOT/name).is_file() for name in SOURCES))
        import uuid,hashlib
        intent='ad12ad12-ad12-4d12-ad12-ad12ad12ad12';execution=expected_execution(intent)
        self.assertNotEqual(execution,intent);self.assertEqual(uuid.UUID(execution).bytes_le,hashlib.sha256(('whaledeck-container-recursive-acl-restore-execution-id-v1:'+uuid.UUID(intent).hex).encode()).digest()[:16])
    def test_sql_collectors_are_read_only_and_receipt_does_not_touch_secret_keys(self):
        for filename in ('validate_container_recursive_acl_restore_browser.py','verify_container_recursive_acl_restore_browser.py'):
            for node in ast.walk(ast.parse((ROOT/filename).read_text())):
                if isinstance(node,ast.Call) and isinstance(node.func,ast.Name) and node.func.id=='query':
                    self.assertIsInstance(node.args[0],ast.Constant);self.assertTrue(node.args[0].value.lstrip().upper().startswith('SELECT '));self.assertNotRegex(node.args[0].value,r'(?i)\b(?:UPDATE|DELETE|INSERT|ALTER|TRUNCATE)\b')
        source=(ROOT/'validate_container_recursive_acl_restore_browser.py').read_text();self.assertIn('expected_execution',source);self.assertIn('records_bound_to_business',source);self.assertIn('source-physical-final.json',source)
        for marker in ('agent-capability.key','request.post(','.apply(','.discard('):self.assertNotIn(marker,source)
    def test_browser_normal_controls_original_references_and_no_response_or_cookie_injection(self):
        source=(ROOT/'validate_container_recursive_acl_restore_browser.mjs').read_text()
        for marker in ('.route(','.addCookies(','storageState:','sessionStorage.setItem','localStorage.setItem','request.put('):self.assertNotIn(marker,source)
        for marker in ('预检恢复此递归ACL历史','查看逐项恢复目标ACL','originalReference','confirmationReplayId','physicalRootHddAndSqlVerified','cancellations,0'):self.assertIn(marker,source)
        caller=(ROOT/'validate_container_recursive_acl_browser.mjs').read_text();self.assertLess(caller.index('recursiveRestore=await runRecursiveRestoreBrowser'),caller.index("phase='visible-dual-logout'"));self.assertIn('normalDualLogout:ordinaryLogout',caller)
        parent=(ROOT/'validate_container_recursive_acl_browser.py').read_text();self.assertLess(parent.index('independent-live-restore-proof-before-cleanup'),parent.index("PHASE = 'proved-source-cleanup'"))
    def test_live_and_final_physical_claims_are_distinct(self):
        source=(ROOT/'verify_container_recursive_acl_restore_browser.py').read_text();self.assertIn('inventory(root,io.reader,content,module)==current',source);self.assertIn('sourceBytesRereadBeforeCleanup=True',source);self.assertIn('sourceBytesRereadAfterCleanup=False',source)

@unittest.skipUnless(os.geteuid()==0,'temporary root-private actual ACL records required')
class RecursiveRestoreBrowserBridgeTests(unittest.TestCase):
    def test_complete_http_sql_and_native_record_bridge_before_next_write(self):
        import uuid
        import test_recursive_acl_restore_business_proof as business
        import validate_container_recursive_acl_restore_browser as gate
        from recursive_acl_history_browser_proof import expected_summary,endpoint,location
        fixture=business.BusinessTemporaryRecordsTests();fixture.setUp();self.addCleanup(fixture.doCleanups)
        source,frame,item,bundle,data,result,baseline=fixture.projection()
        # The independent synthetic SQL layer uses a separate intent; match the
        # real workflow's deterministic execution identity for the bridge test.
        with patch.object(gate,'expected_execution',return_value=result['executionJobId']):
            source_item=dict(name='access',executionJobId=str(uuid.UUID(source['request']['operationId'])),historyDigest=source['result']['historyDigest'])
            summary=expected_summary(source_item,source['metadata'])
            entries=[dict(relativePath=e['relativePath'],kind=e['plan']['kind'],originalMode=e['plan']['sourceIdentity'][2]&0o777,accessAcl=e['plan']['accessAcl'],defaultAcl=e['plan']['defaultAcl']) for e in source['metadata']['entries']]
            selected=dict(location=location(baseline),summary=summary,offset=0,entries=entries,complete=True,nextOffset=None)
            request=dict(method='GET',pathname=endpoint(baseline)+'/'+summary['captureId'],query=dict(instanceId=baseline['instanceId'],path='work/recursive-tree',expectedHistoryDigest=summary['historyDigest'],offset='0',take='25'),status=200,noStore=True)
            result.update(name='restore-access',readyExpiresAtUtc='2026-11-05T00:05:00Z')
            ready=copy.deepcopy(result);ready['state']='Ready'
            command=result['command'];body=dict(instanceId=baseline['instanceId'],path='work/recursive-tree',captureId=summary['captureId'],expectedHistoryDigest=summary['historyDigest'],expectedVersion=command['expectedVersion'])
            signal=dict(name='restore-access',subject='proof-admin',result=result,source=selected,sourceRequest=request,preview=copy.deepcopy(selected),previewRequest=copy.deepcopy(request),registered=dict(id=result['id'],state='Queued',executionJobId=None),ready=ready,key=data['row']['idempotency_key'],body=body,originalReference=dict(subject='proof-admin',containerId=baseline['containerId'],key=data['row']['idempotency_key'],id=result['id']),duplicateRegistrationId=result['id'],confirmationReplayId=result['executionJobId'],cancelDismissedWithoutWrite=False)
            with tempfile.TemporaryDirectory() as folder,patch.object(gate,'sql_snapshot',return_value=data),patch.object(gate,'read_bundle',return_value=(bundle,None)),patch.object(gate,'attribute_digest',return_value=fixture.fixture.attrs):
                accepted=gate.verify_case(signal,baseline,source_item,source,frame,item['before'],item['after'],Path(folder),None)
                self.assertEqual(accepted['children'],4)
                changed=copy.deepcopy(signal);changed['body']['expectedVersion']='F'*64
                with self.assertRaises(AssertionError):gate.verify_case(changed,baseline,source_item,source,frame,item['before'],item['after'],Path(folder),None,read_only=True)


if __name__=='__main__':unittest.main()
