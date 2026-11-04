"""Negative paging/identity/scope tests. Synthetic data is not live acceptance."""
import ast
import copy
from pathlib import Path
import unittest
from unittest.mock import Mock

from test_recursive_acl_history_paging_proof import fixture
from recursive_acl_restore_paging_proof import page_proof,OFFSETS
from validate_recursive_acl_restore_paging import wait_case,SOURCES
from verify_recursive_acl_restore_paging import verify

ROOT=Path(__file__).resolve().parent

class RestorePagingProofTests(unittest.TestCase):
    def test_default_take_25_traverses_twenty_five_files_and_last_root(self):
        self.assertEqual(OFFSETS,(0,25,0,25))
        for index in range(4):
            b,row,m,s=fixture(index);page_proof(s,b,row,m,index)
            self.assertEqual(len(s['snapshot']['entries']),1 if index%2 else 25)
            if index%2:self.assertEqual('',s['snapshot']['entries'][0]['relativePath'])
    def test_source_selection_has_its_own_evidence_name(self):
        b,r,m,s=fixture();s['screenshot']='selection.png';page_proof(s,b,r,m,0,selection=True)
        with self.assertRaises(AssertionError):page_proof(s,b,r,m,0)
    def test_shift_duplicate_order_or_cross_page_residue_rejected(self):
        changes=[lambda s:s['snapshot'].update(offset=False),lambda s:s['snapshot'].update(nextOffset=26),lambda s:s['snapshot'].update(complete=True),lambda s:s['snapshot']['entries'].reverse(),lambda s:s['snapshot']['entries'].append(copy.deepcopy(s['snapshot']['entries'][0]))]
        for change in changes:
            b,r,m,s=fixture();change(s)
            with self.subTest(change=change),self.assertRaises(AssertionError):page_proof(s,b,r,m,0)
    def test_changed_source_digest_or_capture_and_take_cannot_be_substituted(self):
        changes=[lambda s:s['snapshot']['summary'].update(historyDigest='F'*64),lambda s:s['snapshot']['summary'].update(captureId='f'*32),lambda s:s['request']['query'].update(take='1'),lambda s:s['request']['query'].update(offset='1'),lambda s:s['request']['query'].update(expectedHistoryDigest='F'*64),lambda s:s['request'].update(noStore=False),lambda s:s['request'].update(method='POST')]
        for change in changes:
            b,r,m,s=fixture();change(s)
            with self.subTest(change=change),self.assertRaises(AssertionError):page_proof(s,b,r,m,0)
    def test_named_zero_id_mask_zero_and_non_target_acl_are_exact(self):
        for change in (lambda s:s['snapshot']['entries'][0]['accessAcl'][1].update(id=None),lambda s:s['snapshot']['entries'][0]['accessAcl'][1].update(permissions=False),lambda s:s['snapshot']['entries'][0].update(defaultAcl=[dict(tag='other',id=None,permissions=7)])):
            b,r,m,s=fixture();change(s)
            with self.subTest(change=change),self.assertRaises(AssertionError):page_proof(s,b,r,m,0)
    def test_last_page_requires_original_root_identity_type_and_default_acl(self):
        for change in (lambda s:s['snapshot']['entries'][0].update(relativePath='fake'),lambda s:s['snapshot']['entries'][0].update(kind='File'),lambda s:s['snapshot']['entries'][0].update(defaultAcl=[]),lambda s:s['snapshot'].update(nextOffset=26)):
            b,r,m,s=fixture(1);change(s)
            with self.subTest(change=change),self.assertRaises(AssertionError):page_proof(s,b,r,m,1)
    def test_artifact_scope_rejects_paths_before_io(self):
        for name in ('../escape','recursive-acl-history-paging-'+'a'*32,'recursive-acl-restore-paging-'+'A'*32):
            with self.subTest(name=name),self.assertRaises(AssertionError):verify(name)
        with self.assertRaises(AssertionError):wait_case(Mock(),Path('/does-not-exist'),'../escape')
    def test_sql_collectors_only_select_and_no_recovery_or_confirmation_rpc(self):
        for filename in ('validate_recursive_acl_restore_paging.py','verify_recursive_acl_restore_paging.py'):
            source=(ROOT/filename).read_text()
            for node in ast.walk(ast.parse(source)):
                if isinstance(node,ast.Call) and isinstance(node.func,ast.Name) and node.func.id=='query':
                    self.assertIsInstance(node.args[0],ast.Constant);self.assertTrue(node.args[0].value.lstrip().upper().startswith('SELECT '))
            self.assertNotIn('agent-capability.key',source)
        self.assertTrue(all((ROOT/n).is_file() for n in SOURCES))
    def test_actual_ui_navigation_cancel_and_original_identity_context_are_required(self):
        source=(ROOT/'validate_recursive_acl_restore_paging.mjs').read_text()
        for name in ('下一页恢复前像','上一页恢复前像','查看逐项恢复目标ACL','dismissedWithoutWrite:true','checkReference','logoutFromUserMenu','confirmation:null'):
            self.assertIn(name,source)
        for marker in ('.route(','.addCookies(','sessionStorage.setItem','localStorage.setItem'):
            self.assertNotIn(marker,source)
        self.assertNotIn("name:'确认递归ACL恢复',exact:true}).click()",source)
    def test_live_and_retained_only_claims_are_separate(self):
        source=(ROOT/'verify_recursive_acl_restore_paging.py').read_text()
        self.assertIn('inventory(Path(b[\'root\']),reader,data,acl)==row[\'after\']',source)
        self.assertIn('sourceBytesRereadAfterCleanup=False',source)
        self.assertIn('physicalTreeReread=live',source)
        self.assertIn('restoreExecutions=0',source)

if __name__=='__main__':unittest.main()
