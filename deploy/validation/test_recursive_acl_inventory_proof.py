"""Offline adversarial inventory/chain proofs, not actual UI or HDD acceptance."""
import ast,copy
from pathlib import Path
import tempfile,unittest
from unittest.mock import patch
from recursive_acl_inventory_proof import verify_chain,summary,expected_snapshot,prove_signal
from recursive_acl_history_browser_proof import location,endpoint
from validate_recursive_acl_history_inventory import SOURCES,content,wait_case
from verify_recursive_acl_history_inventory import verify
from recursive_acl_history_paging_proof import verify_capture

ROOT=Path(__file__).resolve().parent

def fixture(label='last'):
    b=dict(containerId='a'*64,instanceId='123:2026-11-04T00:00:00Z',original={'round':0});rows=[];metadata={};access=[dict(tag='user_obj',id=None,permissions=6),dict(tag='group_obj',id=None,permissions=4),dict(tag='other',id=None,permissions=0)]
    for index in range(26):
        capture=f'{index+1:032x}';p=dict(command=dict(instanceId=b['instanceId']),manifestDigest='B'*64,entryCount=4,fileCount=2,directoryCount=2,changeCount=4 if index==0 else 2)
        rows.append(dict(index=index,profile='inventory-mask' if index%2 else 'inventory-named',before={'round':index},after={'round':index+1},request=dict(operationId=capture,plan=p)))
        metadata[capture]=dict(historyDigest=f'{index+1:064X}',createdAtUnixMs=1700000000000+index,entries=[dict(relativePath=relative,plan=dict(kind='File' if relative.endswith('.bin') else 'Directory',sourceIdentity=[1,2,0o100640 if relative.endswith('.bin') else 0o40750],accessAcl=copy.deepcopy(access),defaultAcl=[])) for relative in ('sub/中文.bin','one.bin','sub','')])
    expected={r['request']['operationId']:summary(r,metadata[r['request']['operationId']]) for r in rows};ids=sorted(expected);query=dict(instanceId=b['instanceId'],path='work/recursive-tree')
    def page(values,after,version,complete):
        return dict(request=dict(method='GET',pathname=endpoint(b),query=dict(**query,after=after,take='25',expectedVersion=version),status=200,noStore=True),page=dict(location=location(b),version='C'*64,entries=[copy.deepcopy(expected[c]) for c in values],nextAfter='' if complete else ids[24],scannedCount=len(values),invalidCount=0,complete=complete))
    pages=[page(ids[:25],'','',False)]
    if label!='first':pages.append(page(ids[25:],ids[24],'C'*64,True))
    signal=dict(label=label,subject='actual-subject',pages=pages,visibleCaptures=ids[:25] if label in ('first','back') else ids[25:],screenshot='inventory-'+label+'.png')
    if label=='selected':signal.update(snapshot=expected_snapshot(b,rows[-1],metadata[ids[-1]]),request=dict(method='GET',pathname=endpoint(b)+'/'+ids[-1],query=dict(**query,offset='0',take='25',expectedHistoryDigest=metadata[ids[-1]]['historyDigest']),status=200,noStore=True))
    return b,rows,metadata,copy.deepcopy(signal)

class RecursiveAclInventoryProofTests(unittest.TestCase):
    def test_partial_full_cached_and_selected_proofs_bind_twenty_six_captures(self):
        for label in ('first','last','back','next','selected'):
            b,r,m,s=fixture(label);self.assertEqual(26,len(prove_signal(s,b,r,m,label)))

    def test_chain_binds_each_original_to_previous_completion_and_fixed_profile(self):
        b,rows,metadata,_=fixture()
        def read(_evidence,baseline,row,_storage,profile):
            self.assertEqual(row['before'],baseline['original']);self.assertEqual(row['profile'],profile);return metadata[row['request']['operationId']]
        with patch('recursive_acl_inventory_proof.verify_capture',side_effect=read) as call:
            self.assertEqual(metadata,verify_chain(Path('/private-evidence'),b,rows,None));self.assertEqual(26,call.call_count)

    def test_chain_rejects_missing_duplicate_wrong_initial_profile_or_change_count(self):
        for change in (lambda x:x.pop(),lambda x:x[2]['request'].update(operationId=x[0]['request']['operationId']),lambda x:x[0].update(before={'round':99}),lambda x:x[1].update(profile='inventory-named'),lambda x:x[3]['request']['plan'].update(changeCount=4)):
            b,r,m,_=fixture();change(r)
            with patch('recursive_acl_inventory_proof.verify_capture',side_effect=lambda e,b,row,s,**kw:m[row['request']['operationId']]),self.assertRaises(AssertionError):verify_chain(Path('/private-evidence'),b,r,None)

    def test_first_page_cannot_claim_completion_truncate_extend_or_skip_captures(self):
        changes=(lambda s:s['pages'][0]['page'].update(complete=True,nextAfter=''),lambda s:s['pages'][0]['page']['entries'].pop(),lambda s:s['pages'][0]['page']['entries'].reverse(),lambda s:s['pages'][0]['page'].update(scannedCount=True),lambda s:s['pages'][0]['page'].update(nextAfter='0'*31+'1'),lambda s:s['pages'][0]['request']['query'].update(take='1'))
        for change in changes:
            b,r,m,s=fixture('first');change(s)
            with self.assertRaises(AssertionError):prove_signal(s,b,r,m,'first')

    def test_continuation_binds_same_version_cursor_location_and_exact_final_capture(self):
        for change in (lambda s:s['pages'][1]['request']['query'].update(expectedVersion='D'*64),lambda s:s['pages'][1]['page'].update(version='D'*64),lambda s:s['pages'][1]['request']['query'].update(after=''),lambda s:s['pages'][1]['page']['location'].update(instanceId='other'),lambda s:s['pages'][1]['page']['entries'].clear()):
            b,r,m,s=fixture();change(s)
            with self.assertRaises(AssertionError):prove_signal(s,b,r,m,'last')

    def test_actual_visible_ids_and_selected_original_cannot_be_substituted(self):
        for change in (lambda s:s['visibleCaptures'].clear(),lambda s:s['snapshot']['summary'].update(changeCount=False),lambda s:s['snapshot']['entries'][0]['accessAcl'][0].update(permissions=0),lambda s:s['request']['query'].update(offset='1'),lambda s:s['request'].update(noStore=False),lambda s:s.update(screenshot='../escape.png')):
            b,r,m,s=fixture('selected');change(s)
            with self.assertRaises(AssertionError):prove_signal(s,b,r,m,'selected')

    def test_unknown_capture_profile_cannot_access_filesystem(self):
        for profile in ('dynamic','inventory-root','../escape',False):
            with self.assertRaises(AssertionError):verify_capture(None,None,None,None,profile=profile)

    def test_invalid_artifact_and_wait_selection_fail_before_dependency_access(self):
        for name in ('../escape','recursive-acl-history-inventory-'+'A'*32,'recursive-acl-history-paging-'+'a'*32):
            with self.assertRaises(AssertionError):verify(name)
        with tempfile.TemporaryDirectory() as folder:
            for name in ('inventory-0','../escape','inventory-first/result'):
                with self.assertRaises(AssertionError):wait_case(None,Path(folder),name)
        self.assertEqual(65543,len(content('a'*64)))
        with self.assertRaises(AssertionError):content('A'*64)

    def test_source_uses_real_controls_fixed_budget_no_injection_and_select_queries(self):
        source=(ROOT/'validate_recursive_acl_history_inventory.mjs').read_text()
        for forbidden in ('.route(','.addCookies(','storageState:','localStorage.setItem','sessionStorage.setItem','request.post(','request.put('):self.assertNotIn(forbidden,source)
        for expected in ('继续扫描','上一页历史','下一页已读历史','requests.length,2','requests.length,3','inventoryMultipleCaptures:true','writeSignal','waitSignal'):self.assertIn(expected,source)
        self.assertTrue(all((ROOT/name).is_file() for name in SOURCES))
        main=(ROOT/'validate_recursive_acl_history_inventory.py').read_text();self.assertIn('for index in range(26)',main);self.assertIn("profile='mask' if index%2 else 'named'",main)
        tree=ast.parse(main)
        for node in ast.walk(tree):
            if isinstance(node,ast.Call) and isinstance(node.func,ast.Name) and node.func.id=='query':self.assertTrue(node.args[0].value.startswith('SELECT '))

if __name__=='__main__':unittest.main()
