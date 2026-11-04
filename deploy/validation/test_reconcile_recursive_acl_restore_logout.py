import unittest
from reconcile_recursive_acl_restore_logout import NAME,target

class LogoutRecoveryScopeTests(unittest.TestCase):
    def test_cleanup_cannot_target_any_other_failed_run_or_arbitrary_path(self):
        self.assertEqual(NAME,target(NAME).name)
        for name in ('../escape',NAME+'/child','recursive-acl-restore-inflight-logout-'+'a'*32):
            with self.subTest(name=name),self.assertRaises(AssertionError):target(name)

if __name__=='__main__':unittest.main()
