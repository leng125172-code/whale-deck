"""A distinct revoked owner, unchanged observer and no substitute cancel/logout."""
import unittest
from pathlib import Path
from unittest.mock import patch
import validate_container_recycle_restore_revocation as gate
import test_container_recycle_revocation as old


class RestoreRevocationProofTests(unittest.TestCase):
    def test_unknown_scenario_is_rejected_before_any_runtime_access(self):
        for kind in ('logout','worker-crash','delete-user'):
            with self.subTest(kind=kind),patch.object(gate.os,'geteuid') as uid,self.assertRaises(AssertionError):gate.main('a'*40,kind)
            uid.assert_not_called()

    def test_owner_and_current_observer_must_be_different_and_neither_can_cancel(self):
        fixtures=old.RecycleRevocationTests()
        for kind in ('group-removal','account-disable'):
            b=fixtures.browser(kind);f=dict(kind=kind,subject=old.SUBJECT);gate.browser_proof(b,f)
            for key,value in [('cancellations',1),('ownerLogoutPosts',1),('managementWrites',1),('recoverySubject',old.SUBJECT)]:
                with self.subTest(kind=kind,key=key),self.assertRaises(AssertionError):gate.browser_proof({**b,key:value},f)

    def test_early_expiry_or_foreign_profile_cannot_certify_live_revocation(self):
        fixtures=old.RecycleRevocationTests()
        for kind in ('group-removal','account-disable'):
            f=dict(kind=kind,subject=old.SUBJECT);a=fixtures.authority(kind);gate.authority_proof(a,f)
            a['executionExpiresAtUtc']=a['confirmedAtUtc']
            with self.subTest(kind=kind),self.assertRaises(AssertionError):gate.authority_proof(a,f)
            a=fixtures.authority(kind);a['after']['username']='administrator'
            with self.subTest(kind=kind),self.assertRaises(AssertionError):gate.authority_proof(a,f)

    def test_both_private_hint_namespaces_and_original_restore_job_are_checked(self):
        root=Path(gate.__file__).parent;browser=(root/'validate_container_recycle_restore_revocation.mjs').read_text();parent=Path(gate.__file__).read_text()
        for key in ('whaledeck-container-recycle-restore-hints-v1','whaledeck-container-recycle-hints-v1'):self.assertIn(key,browser)
        self.assertIn("name: '独立恢复安全结果'",browser);self.assertIn('RECOVERY_PASSWORD',browser)
        self.assertLess(parent.index("assert held is not None,'EXACT_RESTORE_CUT_MISSED_NO_REPLAY'"),parent.index('identity.change(kind)'))
        self.assertIn('HELPER_EXIT_BEFORE_AUTHORITY_CONFIRMED',parent);self.assertLess(parent.index('independent(repo,e,True)'),parent.index('retained.unlink()'))


if __name__=='__main__':unittest.main()
