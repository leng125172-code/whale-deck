"""Synthetic SQL/UI/identity proofs only, never actual revocation acceptance."""
import copy,unittest
from pathlib import Path
from test_container_recycle_purge_inflight_logout import sample as logout_sample
from validate_container_recycle_purge_revocation import terminal_proof,browser_proof,restore_fixture,authority_proof,explicit_cancel_absent
import test_container_recycle_revocation as prior_revocation
SUBJECT=prior_revocation.SUBJECT


def sample(kind):
    v,b,f=logout_sample();f.update(kind=kind,recoverySubject='recovery')
    restored='11111111-2222-3333-4444-555555555555';base='/api/v1/containers/'+f['containerId']+'/files/recycle-restores'
    b.update(kind=kind,recoverySubject='recovery',ownerLogoutPosts=0,privatePanelsAbsent=True,hintsCleared=True,oldOwnerReferencesDenied=3,
        originalOperationRecoveryWrites=[],recoveryWrites=[base,base+'/'+restored+'/confirm'],restored=dict(id=restored),recoveryLogout=b['logoutReceipts'][0],
        originalIntentStatus=403,currentIdentityStatus=200 if kind=='group-removal' else 403,currentSubject=f['subject'] if kind=='group-removal' else None,
        currentlyAdministrator=False if kind=='group-removal' else None,renderedRevocationPage='forbidden' if kind=='group-removal' else 'login')
    return v,b,f


class PurgeRevocationProofTests(unittest.TestCase):
    def test_both_selected_revocation_outcomes_can_prove_no_delete(self):
        for kind in ('group-removal','account-disable'):
            v,b,f=sample(kind)
            with self.subTest(kind=kind):self.assertTrue(terminal_proof(v,b,f)['originalExecutionStoppedByRevocation'])

    def test_cancel_requests_or_audits_cannot_be_counted_as_revocation(self):
        for key in ('row','executionJob'):
            v,b,f=sample('group-removal');v[key]['cancel_requested_at_utc']='requested'
            with self.subTest(key=key),self.assertRaises(AssertionError):terminal_proof(v,b,f)
        v,_,_=sample('group-removal')
        with self.assertRaises(AssertionError):explicit_cancel_absent(v['row'],1)

    def test_current_owner_identity_and_visible_revocation_page_must_agree(self):
        for kind in ('group-removal','account-disable'):
            for key,bad in [('ownerLogoutPosts',1),('cancellations',1),('hintsCleared',False),('privatePanelsAbsent',False),('recoverySubject','subject'),('oldOwnerReferencesDenied',0),('renderedRevocationPage','containers'),('currentIdentityStatus',403 if kind=='group-removal' else 200)]:
                _,b,f=sample(kind);b[key]=bad
                with self.subTest(kind=kind,key=key),self.assertRaises(AssertionError):browser_proof(b,f)

    def test_only_new_restore_registration_and_confirmation_are_recovery_writes(self):
        for extra in ('/api/v1/files/container-recycle-purges/recovery/old/acknowledge','/api/v1/containers/cid/files/recycle-purges/old/cancel','/api/v1/containers/cid/files/recycle-restores'):
            _,b,f=sample('account-disable');b['recoveryWrites'].append(extra)
            with self.subTest(extra=extra),self.assertRaises(AssertionError):browser_proof(b,f)
        _,b,f=sample('group-removal');b['originalOperationRecoveryWrites']=['old-confirm']
        with self.assertRaises(AssertionError):browser_proof(b,f)

    def test_exact_authority_delta_and_far_future_execution_expiry_required(self):
        prior=prior_revocation.RecycleRevocationTests()
        for kind in ('group-removal','account-disable'):
            value=prior.authority(kind);f=dict(kind=kind,subject=SUBJECT);authority_proof(value,f)
            for field,bad in [('uuid','aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee'),('pk',42),('username','admin')]:
                wrong=copy.deepcopy(value);wrong['after'][field]=bad
                with self.subTest(kind=kind,field=field),self.assertRaises(AssertionError):authority_proof(wrong,f)
            wrong=copy.deepcopy(value);wrong['executionExpiresAtUtc']='2026-11-05T10:09:00+00:00'
            with self.assertRaises(AssertionError):authority_proof(wrong,f)

    def test_restore_actor_adaptation_keeps_original_source_owner_unchanged(self):
        _,_,f=sample('group-removal');before=copy.deepcopy(f);restore=restore_fixture(f)
        self.assertEqual(f,before);self.assertEqual(restore['subject'],f['recoverySubject']);self.assertNotEqual(restore['subject'],f['subject'])
        with self.assertRaises(AssertionError):restore_fixture({**f,'recoverySubject':f['subject']})

    def test_unknown_or_deleted_outcome_never_passes_reserved_revocation(self):
        for key,bad in [('purge_applied',True),('purge_applied',None),('root_state','Unknown'),('reservation_json',None),('apply_started_at_utc',None),('publish_barrier',True)]:
            v,b,f=sample('group-removal');v['row'][key]=bad
            with self.subTest(key=key),self.assertRaises(AssertionError):terminal_proof(v,b,f)

    def test_source_keeps_owner_logged_in_and_verifier_is_read_only(self):
        root=Path(__file__).parent;s=(root/'validate_container_recycle_purge_revocation.mjs').read_text()
        self.assertNotIn('await logoutFromUserMenu',s[:s.index("phase='visible-recovery-login'")])
        for bad in ('page.route(','addCookies(','storageState:'):self.assertNotIn(bad,s)
        self.assertIn("'/api/v1/files/container-recycle-purges/recovery/'",s)
        parent=(root/'validate_container_recycle_purge_revocation.py').read_text()
        self.assertIn('with OwnedAuthentikRevocation(username,fixture',parent)
        self.assertIn('authority=identity.change(kind)',parent)
        self.assertIn('originalAliveBeforeAndAfterAuthorityConfirmation=True',parent)
        verifier=(root/'verify_container_recycle_purge_revocation.py').read_text()
        self.assertIn("rr['actor_subject']!=f['subject']",verifier)
        for bad in ('.unlink(','.rmdir(','.rmtree(','.Popen(','.kill('):self.assertNotIn(bad,verifier)


if __name__=='__main__':unittest.main()
