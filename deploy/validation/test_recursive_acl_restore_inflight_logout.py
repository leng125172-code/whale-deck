"""Negative evidence checks; synthetic identity projections are not real login."""
import copy
from pathlib import Path
import unittest
from recursive_acl_restore_logout_proof import authority_proof
from recursive_acl_restore_prewrite_proof import cancellation_cause
from verify_recursive_acl_restore_prewrite import verify

def evidence():
    logout=dict(normalUiLogout=True,providerNavigationObserved=True,finalLoginPage=True,postStatus=302,bffStatus=401,identityStatus=403)
    return dict(logoutDuringExecution=True,prewriteCancellation=False,registrations=1,confirmations=1,cancellations=0,uploads=0,hintsCleared=True,normalDualLogout=True,anonymous=[401]*6,
        subject='original',pending=dict(executionJobId='same-original'),terminal=dict(executionJobId='same-original'),logout=logout,adminReloginLogout=dict(logout),
        logoutIsolation=dict(reloginSubject='original',newLoginLookups=[404,404],hintsAbsentAfterRelogin=True,oldTaskButtons=0,jobId='same-original',jobState='Canceled',adminAnonymous=[401]*6))

class RestoreInflightLogoutTests(unittest.TestCase):
    def test_explicit_cancel_cannot_be_counted_as_logout(self):
        self.assertEqual({},dict(cancellation_cause(dict(cancel_requested_at_utc=None),logout=True)))
        row=dict(cancel_requested_at_utc='actual',execution_job_id='original')
        self.assertEqual({('cancel-requested','Accepted','original'):1},dict(cancellation_cause(row)))
        with self.assertRaises(AssertionError):cancellation_cause(row,logout=True)
        with self.assertRaises(AssertionError):cancellation_cause(dict(cancel_requested_at_utc=None))
        with self.assertRaises(AssertionError):cancellation_cause(row,logout='false')

    def test_original_and_new_login_are_proved_separately(self):authority_proof(evidence())

    def test_cookie_clear_or_a_direct_logout_request_is_not_normal_ui_logout(self):
        for key,bad in (('normalUiLogout',False),('providerNavigationObserved',False),('bffStatus',200),('identityStatus',200),('finalLoginPage',False),('postStatus',200)):
            ui=evidence();ui['logout'][key]=bad
            with self.subTest(key=key),self.assertRaises(AssertionError):authority_proof(ui)

    def test_same_subject_new_binding_must_not_reacquire_old_task(self):
        for key,bad in (('reloginSubject','other'),('newLoginLookups',[200,404]),('hintsAbsentAfterRelogin',False),('oldTaskButtons',1),('jobId','replayed'),('jobState','Succeeded'),('adminAnonymous',[200]*6)):
            ui=evidence();ui['logoutIsolation'][key]=bad
            with self.subTest(key=key),self.assertRaises(AssertionError):authority_proof(ui)

    def test_cancel_upload_or_unconfirmed_execution_is_rejected(self):
        for key,bad in (('logoutDuringExecution',False),('prewriteCancellation',True),('confirmations',0),('cancellations',1),('uploads',1),('anonymous',[403]*6),('hintsCleared',False)):
            ui=evidence();ui[key]=bad
            with self.subTest(key=key),self.assertRaises(AssertionError):authority_proof(ui)

    def test_new_login_must_normally_logout_again(self):
        ui=evidence();ui['adminReloginLogout']['providerNavigationObserved']=False
        with self.assertRaises(AssertionError):authority_proof(ui)

    def test_evidence_names_cannot_cross_old_cancel_and_new_logout_modes(self):
        for name,mode in (('../escape',True),('recursive-acl-restore-prewrite-cancel-'+'a'*32,True),('recursive-acl-restore-inflight-logout-'+'a'*32,False)):
            with self.subTest(name=name),self.assertRaises(AssertionError):verify(name,logout=mode)

    def test_visible_flow_contains_no_auth_injection_or_automatic_replay(self):
        text=(Path(__file__).parent/'validate_recursive_acl_restore_prewrite.mjs').read_text()
        for marker in ('inflight-normal-dual-logout','same-subject-new-login','original-job-terminal-ui','newLoginLookups,[404,404]','logoutFromUserMenu'):
            self.assertIn(marker,text)
        for marker in ('.route(','.addCookies(','sessionStorage.setItem'):
            self.assertNotIn(marker,text)

if __name__=='__main__':unittest.main()
