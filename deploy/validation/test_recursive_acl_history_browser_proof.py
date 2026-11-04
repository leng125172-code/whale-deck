"""Offline adversarial evidence proofs; never a substitute for the installed UI."""
import ast
import copy
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import Mock

from recursive_acl_history_browser_proof import expected_summary,prove_snapshot,prove_pages,prove_audits,digest,endpoint,location
from validate_container_recursive_acl_history_browser import wait_case,SOURCES

ROOT=Path(__file__).resolve().parent

def fixture():
    baseline=dict(containerId='a'*64,instanceId='123:2026-11-04T01:02:03Z')
    result=dict(name='access',executionJobId='11111111-1111-4111-8111-111111111111',historyDigest='A'*64)
    access=[dict(tag='user_obj',id=None,permissions=6),dict(tag='user',id=0,permissions=0),dict(tag='group_obj',id=None,permissions=0),dict(tag='mask',id=None,permissions=4),dict(tag='other',id=None,permissions=0)]
    entries=[dict(relativePath=p,plan=dict(kind=k,sourceIdentity=[1,2,0o100640 if k=='File' else 0o40640],accessAcl=copy.deepcopy(access),defaultAcl=[])) for p,k in [('sub/中文.bin','File'),('one.bin','File'),('sub','Directory'),('','Directory')]]
    metadata=dict(operationId=result['executionJobId'].replace('-',''),historyDigest='A'*64,createdAtUnixMs=1700000000000,entries=entries,
                  plan=dict(command=dict(instanceId=baseline['instanceId'],aclType='access'),manifestDigest='B'*64,entryCount=4,fileCount=2,directoryCount=2,changeCount=0))
    summary=expected_summary(result,metadata)
    query=dict(instanceId=baseline['instanceId'],path='work/recursive-tree')
    request=dict(method='GET',pathname=endpoint(baseline)+'/'+summary['captureId'],query=dict(**query,expectedHistoryDigest='A'*64,offset='0',take='25'),status=200,noStore=True)
    snapshot=dict(location=location(baseline),summary=summary,offset=0,entries=[dict(relativePath=e['relativePath'],kind=e['plan']['kind'],originalMode=e['plan']['sourceIdentity'][2]&0o777,accessAcl=copy.deepcopy(e['plan']['accessAcl']),defaultAcl=[]) for e in entries],complete=True,nextOffset=None)
    page=dict(location=location(baseline),version='C'*64,entries=[copy.deepcopy(summary)],nextAfter='',scannedCount=1,invalidCount=0,complete=True)
    pages=[dict(request=dict(method='GET',pathname=endpoint(baseline),query=dict(**query,after='',take='25',expectedVersion=''),status=200,noStore=True),page=page)]
    signal=dict(name='access',captureId=summary['captureId'],subject='subject-a',snapshot=snapshot,request=request,pages=pages,screenshot='history-access.png')
    common=dict(pathHash=digest('work/recursive-tree'),instanceHash=digest(baseline['instanceId']))
    details=[('list',dict(**common,afterHash=None,take=25,inventoryVersion='C'*64,returnedCount=1,scannedCount=1,invalidCount=0,complete=True)),
             ('read',dict(**common,captureHash=digest(summary['captureId']),historyDigest='A'*64,offset=0,take=25,returnedCount=4,complete=True))]
    audits=[dict(actor_subject='subject-a',target_type='container',target_id=baseline['containerId'],result='Validated',job_id=None,id=str(n),trace_id='trace-'+str(n),action='containers.files.recursive-acl-history-'+action,detail_json=detail) for n,(action,detail) in enumerate(details)]
    return baseline,result,metadata,signal,audits

class RecursiveAclHistoryBrowserProofTests(unittest.TestCase):
    def test_exact_http_projection_keeps_null_id_zero_permission_root_last_and_noop(self):
        b,r,m,s,a=fixture();summary=prove_snapshot(s,b,r,m)
        self.assertEqual(0,summary['changeCount']);self.assertEqual({'tag':'user','id':0,'permissions':0},s['snapshot']['entries'][0]['accessAcl'][1])
        prove_pages(s['pages'],b,{summary['captureId']:summary});prove_audits(a,'subject-a',b,s['pages'],[s])

    def test_snapshot_tampering_including_bool_as_number_and_omitted_null_is_rejected(self):
        changes=[lambda x:x['snapshot'].update(offset=False),lambda x:x['snapshot'].update(nextOffset=0),
                 lambda x:x['snapshot']['summary'].update(changeCount=True),lambda x:x['snapshot']['entries'].reverse(),
                 lambda x:x['snapshot']['entries'][0]['accessAcl'][0].pop('id'),lambda x:x['snapshot']['entries'][0]['accessAcl'][1].update(id=None),
                 lambda x:x['snapshot']['entries'][0]['accessAcl'][1].update(permissions=False),lambda x:x['snapshot']['entries'][0].update(originalMode=0o777),
                 lambda x:x['snapshot']['summary'].update(historyDigest='D'*64),lambda x:x['snapshot']['location'].update(instanceId='other'),
                 lambda x:x.update(screenshot='../history-access.png'),lambda x:x['snapshot'].update(extra='untrusted')]
        for change in changes:
            b,r,m,s,_=fixture();change(s)
            with self.subTest(change=changes.index(change)),self.assertRaises(AssertionError):prove_snapshot(s,b,r,m)

    def test_http_request_cannot_change_method_range_digest_or_cache_policy(self):
        changes=[lambda x:x.update(method='POST'),lambda x:x.update(status=True),lambda x:x.update(noStore=False),
                 lambda x:x['query'].update(take='50'),lambda x:x['query'].pop('offset'),lambda x:x['query'].update(offset='1'),
                 lambda x:x['query'].update(expectedHistoryDigest='D'*64),lambda x:x['query'].update(extra='x'),lambda x:x.update(pathname='/other')]
        for change in changes:
            b,r,m,s,_=fixture();change(s['request'])
            with self.subTest(change=changes.index(change)),self.assertRaises(AssertionError):prove_snapshot(s,b,r,m)

    def test_listing_requires_exact_coverage_version_cursor_types_and_query(self):
        changes=[lambda x:x[0]['page']['entries'].clear(),lambda x:x[0]['page']['entries'].append(copy.deepcopy(x[0]['page']['entries'][0])),
                 lambda x:x[0]['page'].update(scannedCount=True),lambda x:x[0]['page'].update(invalidCount=2),
                 lambda x:x[0]['page'].update(complete=False,nextAfter=''),lambda x:x[0]['page'].update(version='wrong'),
                 lambda x:x[0]['request']['query'].update(expectedVersion='wrong'),lambda x:x[0]['page']['entries'][0].update(entryCount=True)]
        for change in changes:
            b,r,m,s,_=fixture();expected={m['operationId']:expected_summary(r,m)};change(s['pages'])
            with self.subTest(change=changes.index(change)),self.assertRaises(AssertionError):prove_pages(s['pages'],b,expected)

    def test_cursor_sequence_rejects_stale_version_duplicate_or_truncated_final_page(self):
        b,r,m,s,_=fixture();first=s['pages'][0];last=copy.deepcopy(first)
        first['page'].update(complete=False,nextAfter=m['operationId'])
        last['request']['query'].update(after=m['operationId'],expectedVersion='C'*64)
        last['page'].update(entries=[],scannedCount=0)
        pages=[first,last];expected={m['operationId']:expected_summary(r,m)};prove_pages(pages,b,expected)
        for change in (lambda x:x[1]['page'].update(version='D'*64),lambda x:x[1]['request']['query'].update(after=''),lambda x:x.pop()):
            value=copy.deepcopy(pages);change(value)
            with self.assertRaises(AssertionError):prove_pages(value,b,expected)

    def test_durable_audits_accept_jsonb_or_json_and_ignore_order(self):
        b,_,_,s,a=fixture();a[0]['detail_json']=json.dumps(a[0]['detail_json']);prove_audits(list(reversed(a)),'subject-a',b,s['pages'],[s])

    def test_incomplete_page_cursor_cannot_precede_its_returned_capture(self):
        b,r,m,s,_=fixture();first=s['pages'][0];last=copy.deepcopy(first)
        cursor='0'*31+'1'
        first['page'].update(complete=False,nextAfter=cursor)
        last['request']['query'].update(after=cursor,expectedVersion='C'*64)
        last['page'].update(entries=[],scannedCount=0)
        with self.assertRaises(AssertionError):prove_pages([first,last],b,{m['operationId']:expected_summary(r,m)})

    def test_audit_identity_hash_capture_counts_types_duplicates_and_extra_plaintext_reject(self):
        changes=[lambda x:x[0].update(actor_subject='other'),lambda x:x[1].update(trace_id=x[0]['trace_id']),
                 lambda x:x[1].update(id=x[0]['id']),lambda x:x[1].update(job_id='not-null'),lambda x:x[0].update(result='Accepted'),
                 lambda x:x[0]['detail_json'].update(pathHash='D'*64),lambda x:x[1]['detail_json'].update(captureHash='D'*64),
                 lambda x:x[1]['detail_json'].update(offset=False),lambda x:x[0]['detail_json'].update(complete=1),
                 lambda x:x[1]['detail_json'].update(returnedCount=3),lambda x:x[0]['detail_json'].update(path='work/recursive-tree'),
                 lambda x:x[0].update(detail_json='{"pathHash":"x","pathHash":"y"}'),lambda x:x.pop()]
        for change in changes:
            b,_,_,s,a=fixture();change(a)
            with self.subTest(change=changes.index(change)),self.assertRaises(AssertionError):prove_audits(a,'subject-a',b,s['pages'],[s])

    def test_waiter_cannot_select_old_or_unbounded_evidence_paths(self):
        with tempfile.TemporaryDirectory() as folder:
            for name in ('../escape','history-cancel','access','history-root/escape'):
                with self.subTest(name=name),self.assertRaises(AssertionError):wait_case(Mock(),Path(folder),name)

    def test_gate_preserves_original_contexts_and_never_intercepts_or_injects(self):
        source=(ROOT/'validate_container_recursive_acl_history_browser.mjs').read_text()
        for prohibited in ('.route(','.addCookies(','storageState:','sessionStorage.setItem','localStorage.setItem','request.post(','request.put('):self.assertNotIn(prohibited,source)
        for expected in ('查看逐项前像','writeSignal','waitSignal','realUiSnapshotPagination:false','requests.length,pages.length+7'):self.assertIn(expected,source)
        caller=(ROOT/'validate_container_recursive_acl_browser.mjs').read_text()
        self.assertLess(caller.index('recursiveHistory=await runRecursiveHistoryBrowser'),caller.index("phase='visible-dual-logout'"))
        self.assertIn('normalDualLogout:ordinaryLogout',caller)
        self.assertTrue(all((ROOT/name).is_file() for name in SOURCES))

    def test_history_sql_is_select_only(self):
        tree=ast.parse((ROOT/'validate_container_recursive_acl_history_browser.py').read_text())
        for node in ast.walk(tree):
            if isinstance(node,ast.Call) and isinstance(node.func,ast.Name) and node.func.id=='query':
                self.assertIsInstance(node.args[0],ast.Constant)
                self.assertTrue(node.args[0].value.lstrip().startswith('SELECT '));self.assertNotRegex(node.args[0].value,r'(?i)\b(?:INSERT|UPDATE|DELETE|ALTER|TRUNCATE)\b')

if __name__=='__main__':unittest.main()
