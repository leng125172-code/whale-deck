"""Negative constructed revocation evidence; real identity mutations are separate."""
import ast,copy,unittest
from pathlib import Path
from test_container_recycle_archive_inflight_logout import sample as logout_sample
from archive_revocation_proof import terminal_proof,browser_proof,exit_proof,explicit_cancel_absent
from validate_container_recycle_revocation import authority_proof
import test_container_recycle_revocation as prior


def sample(kind):
    v,f,p,b=logout_sample();f.update(kind=kind,recoverySubject='recovery')
    b.update(kind=kind,recoverySubject='recovery',ownerLogoutPosts=0,privatePanelsAbsent=True,hintsCleared=True,recoveryWrites=[],originalOperationRecoveryWrites=[],recoveryLogout=b['logoutReceipts'][0],originalIntentStatus=403,currentIdentityStatus=200 if kind=='group-removal' else 403,currentSubject=f['subject'] if kind=='group-removal' else None,currentlyAdministrator=False if kind=='group-removal' else None,renderedRevocationPage='forbidden' if kind=='group-removal' else 'login')
    return v,f,p,b


class ArchiveRevocationTests(unittest.TestCase):
    def test_two_owned_revocation_cases_stop_without_copy(self):
        for kind in ('group-removal','account-disable'):
            v,f,p,b=sample(kind)
            with self.subTest(kind=kind):self.assertTrue(terminal_proof(v,f,p,b)['originalExecutionStoppedByRevocation'])

    def test_cancellation_or_owner_logout_cannot_be_counted_as_revocation(self):
        for target in ('row','executionJob'):
            v,f,p,b=sample('group-removal');v[target]['cancel_requested_at_utc']='explicit'
            with self.subTest(target=target),self.assertRaises(AssertionError):terminal_proof(v,f,p,b)
        v,f,p,b=sample('group-removal');b['ownerLogoutPosts']=1
        with self.assertRaises(AssertionError):terminal_proof(v,f,p,b)
        for n in (True,1,None,'0'):
            with self.subTest(n=n),self.assertRaises(AssertionError):explicit_cancel_absent(v['row'],n)

    def test_current_identity_page_and_sensitive_hint_removal_must_agree(self):
        for kind in ('group-removal','account-disable'):
            for key,bad in (('hintsCleared',False),('privatePanelsAbsent',False),('renderedRevocationPage','containers'),('currentIdentityStatus',403 if kind=='group-removal' else 200),('oldReferenceStatuses',[404]*8+[200])):
                _,f,_,b=sample(kind);b[key]=bad
                with self.subTest(kind=kind,key=key),self.assertRaises(AssertionError):browser_proof(b,f)

    def test_distinct_admin_cannot_write_or_inherit_original_references(self):
        for key,bad in (('recoverySubject','subject'),('recoveryWrites',['/any/write']),('originalOperationRecoveryWrites',['/old/confirm'])):
            _,f,_,b=sample('account-disable');b[key]=bad
            with self.subTest(key=key),self.assertRaises(AssertionError):browser_proof(b,f)
        _,f,_,b=sample('group-removal');b['mutations'].append(b['mutations'][0].removesuffix('/confirm')+'/cancel')
        with self.assertRaises(AssertionError):browser_proof(b,f)

    def test_exact_generated_identity_delta_and_far_future_expiry_required(self):
        fixture=prior.RecycleRevocationTests()
        for kind in ('group-removal','account-disable'):
            a=fixture.authority(kind);f=dict(kind=kind,subject=prior.SUBJECT);authority_proof(a,f)
            for field,bad in (('uuid','aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee'),('pk',42),('username','admin')):
                changed=copy.deepcopy(a);changed['after'][field]=bad
                with self.subTest(kind=kind,field=field),self.assertRaises(AssertionError):authority_proof(changed,f)
            changed=copy.deepcopy(a);changed['executionExpiresAtUtc']='2026-11-05T10:09:00+00:00'
            with self.assertRaises(AssertionError):authority_proof(changed,f)

    def test_original_pidfd_must_remain_alive_after_authority_confirmation(self):
        v=dict(elapsedSeconds=2,observedWithOriginalPidfd=True,measurementBeginsBeforeAuthorityMutation=True,originalAliveBeforeAndAfterAuthorityConfirmation=True,beganMonotonicNs=100,confirmedMonotonicNs=200,exitObservedMonotonicNs=300);exit_proof(v)
        for key,bad in (('elapsedSeconds',20),('elapsedSeconds',True),('originalAliveBeforeAndAfterAuthorityConfirmation',False),('confirmedMonotonicNs',400),('confirmedMonotonicNs',50)):
            with self.subTest(key=key),self.assertRaises(AssertionError):exit_proof({**v,key:bad})

    def test_uncertain_or_copied_outcome_is_not_safe_revocation(self):
        for key,bad in (('archive_copied',True),('archive_copied',None),('root_state','Unknown'),('reservation_json',None),('apply_started_at_utc',None),('publish_barrier',True)):
            v,f,p,b=sample('group-removal');v['row'][key]=bad
            with self.subTest(key=key),self.assertRaises(AssertionError):terminal_proof(v,f,p,b)

    def test_nineteen_field_jobs_and_planning_snapshot_remain_exact(self):
        for target,key,bad in (('planJob','state','Canceled'),('executionJob','archiveCopied',True),('executionJob','sourceIntentId','other'),('executionJob','extra',True)):
            v,f,p,b=sample('account-disable');v[target]['result_json'][key]=bad
            with self.subTest(target=target,key=key),self.assertRaises(AssertionError):terminal_proof(v,f,p,b)

    def test_browser_keeps_owner_logged_in_and_uses_distinct_private_recovery_credentials(self):
        root=Path(__file__).parent;s=(root/'validate_container_recycle_archive_revocation.mjs').read_text();owner=s.split("phase='wait-authority-change-and-original-exit'",1)[1].split("phase='visible-recovery-login'",1)[0]
        self.assertNotIn('logoutFromUserMenu(',owner);self.assertNotIn('diagnosedLogout(',owner);self.assertNotIn("name:'请求取消归档恢复'",owner)
        for expected in ('credentials.recovery',"'/api/v1/files/container-recycle-archives/recovery/'",'recoveryWrites',"name:'独立归档恢复'"):
            self.assertIn(expected,s)
        for bad in ('page.route(','storageState(','tracing.start(','addCookies('):self.assertNotIn(bad,s)
        p=(root/'validate_container_recycle_archive_revocation.py').read_text();self.assertIn('with OwnedAuthentikRevocation(username,fixture',p);self.assertIn('authority=identity.change(kind)',p);self.assertIn('originalAliveBeforeAndAfterAuthorityConfirmation=True',p);self.assertIn('credentials\':dict(owner=',p);self.assertNotIn("'VALIDATION_PASSWORD':password",p)

    def test_independent_verifier_is_readonly_and_cleanup_waits_for_live_proof(self):
        root=Path(__file__).parent;s=(root/'validate_container_recycle_archive_revocation.py').read_text();self.assertLess(s.index('independent(repo,e,True)'),s.index('private.rmdir()'));self.assertLess(s.index('private.rmdir()'),s.index('after_hdd=hdd_observation'));self.assertIn('late=future.result()',s)
        v=(root/'verify_container_recycle_archive_revocation.py').read_text();calls=[ast.unparse(n.func) for n in ast.walk(ast.parse(v)) if isinstance(n,ast.Call)]
        for bad in ('cleanup_new_copy','requests.post','os.unlink','os.rmdir','subprocess.Popen'):self.assertNotIn(bad,calls)
        for required in ('authority_proof','reservation_proof','cut_proof','exit_proof','record_proof','archive_io.read_operation','archive_io.physical','hdd_observation','source_closure'):self.assertIn(required,calls)


if __name__=='__main__':unittest.main()
