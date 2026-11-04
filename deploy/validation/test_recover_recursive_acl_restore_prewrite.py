"""One failed-run recovery scope; no installed actions."""
from pathlib import Path
import unittest
from recover_recursive_acl_restore_prewrite import target,NAME
class RecoveryScopeTests(unittest.TestCase):
    def test_only_the_named_original_failure_is_selected(self):
        self.assertEqual(NAME,target(NAME).name)
        for name in ('../escape',NAME.upper(),'recursive-acl-restore-prewrite-cancel-'+'a'*32):
            with self.subTest(name=name),self.assertRaises(AssertionError):target(name)
    def test_recovery_uses_normal_ui_ack_not_original_confirm_or_apply(self):
        root=Path(__file__).parent;s=(root/'recover_recursive_acl_restore_prewrite.mjs').read_text()
        for marker in ('loginVisible','logoutFromUserMenu','暂不处置','确认处置并释放保护','acknowledgements,0','acknowledgements,1'):self.assertIn(marker,s)
        for marker in ('.addCookies(','.route(',"/confirm'", "name:'确认递归ACL恢复'"):self.assertNotIn(marker,s)
        s=(root/'recover_recursive_acl_restore_prewrite.py').read_text();self.assertIn('originalGatePassed=False',s);self.assertIn('originalRestorationNotReplayed=True',s)
if __name__=='__main__':unittest.main()
