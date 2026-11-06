"""Synthetic identity/UI/SQL evidence; actual revocation is verified separately."""
import copy,unittest
from pathlib import Path
import test_container_recycle_revocation as prior
from test_container_recycle_purge_unlinked_logout import data
from validate_container_recycle_purge_unlinked_revocation import browser_proof,authority_proof,SOURCES
from recycle_purge_unlinked_logout_proof import row_proof,require_ack


def browser(kind):
    value=prior.RecycleRevocationTests().browser(kind)
    value.pop('managementWrites');value.pop('recoveryLogout')
    r=dict(normalUiLogout=True,postStatus=303,providerNavigationObserved=True,bffStatus=401,identityStatus=403,finalLoginPage=True)
    value.update(managementWrites=1,acknowledgements=1,differentAdminOldIdStatus=404,cancelledDecisionPreserved=True,jobProofs=[{},{}],recoveryLogout=r)
    return value,dict(kind=kind,subject=prior.SUBJECT,recoverySubject='different')


class UnlinkedRevocationTests(unittest.TestCase):
    def test_both_authority_losses_have_distinct_visible_results_and_single_ack(self):
        for kind in ('group-removal','account-disable'):
            b,f=browser(kind);browser_proof(b,f)
            authority_proof(prior.RecycleRevocationTests().authority(kind),f)

    def test_voluntary_logout_cancel_same_actor_or_unconfirmed_decision_cannot_pass(self):
        for kind in ('group-removal','account-disable'):
            for key,bad in [('ownerLogoutPosts',1),('cancellations',1),('acknowledgements',2),('managementWrites',2),('recoverySubject',prior.SUBJECT),('hintsCleared',False),('cancelledDecisionPreserved',False),('currentIdentityStatus',403 if kind=='group-removal' else 200)]:
                b,f=browser(kind);b[key]=bad
                with self.subTest(kind=kind,key=key),self.assertRaises(AssertionError):browser_proof(b,f)

    def test_expired_authority_or_mutation_of_another_identity_is_rejected(self):
        for kind in ('group-removal','account-disable'):
            a=prior.RecycleRevocationTests().authority(kind);_,f=browser(kind)
            for key,bad in [('pk',42),('uuid','aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee'),('username','admin')]:
                changed=copy.deepcopy(a);changed['after'][key]=bad
                with self.subTest(kind=kind,key=key),self.assertRaises(AssertionError):authority_proof(changed,f)
            changed=copy.deepcopy(a);changed['executionExpiresAtUtc']=changed['confirmedAtUtc']
            with self.assertRaises(AssertionError):authority_proof(changed,f)

    def test_zero_cancel_unknown_requires_one_original_delivery_and_nullable_fact(self):
        v,f=data();row_proof(v,f,False)
        for key,bad in [('cancel_requested_at_utc','cancel'),('purge_applied',False),('purge_applied',True),('state','Canceled')]:
            changed=copy.deepcopy(v);changed['row'][key]=bad
            with self.subTest(key=key),self.assertRaises(AssertionError):row_proof(changed,f,False)
        v['pending']=2
        with self.assertRaises(AssertionError):row_proof(v,f,False)

    def test_ack_keeps_receipt_namespace_and_archive_facts(self):
        old,_=data();new,_=data(True);a=dict(sql=old,receipt={'state':'Unknown'},records={'bytes':'original'},physical={'absent':True},hdd={'sha':'same'});b={**a,'sql':new};require_ack(a,b,'other')
        for key in ('receipt','records','physical','hdd'):
            with self.subTest(key=key),self.assertRaises(AssertionError):require_ack(a,{**b,key:{'changed':True}},'other')

    def test_gate_uses_exact_owned_authority_change_before_settlement_and_no_owner_logout(self):
        root=Path(__file__).parent
        for name in SOURCES:self.assertTrue((root/name).is_file(),name)
        code=(root/'validate_container_recycle_purge_unlinked_revocation.py').read_text();self.assertIn("OwnedAuthentikRevocation(username,fixture['subject'])",code);self.assertIn('authority=identity.change(kind)',code);self.assertIn('originalAliveBeforeAndAfterAuthorityConfirmation=True',code)
        script=(root/'validate_container_recycle_purge_unlinked_revocation.mjs').read_text();self.assertNotIn('await logoutFromUserMenu',script[:script.index("phase='visible-recovery-login'")]);self.assertNotIn("name:'请求取消留存清理'",script)
        for bad in ('page.route(','addCookies(','storageState:'):self.assertNotIn(bad,script)
        verifier=(root/'verify_container_recycle_purge_unlinked_revocation.py').read_text();self.assertIn('authority_proof(authority,f)',verifier)
        for bad in ('.unlink(','.rmdir(','.rmtree(','.Popen(','.kill('):self.assertNotIn(bad,verifier)


if __name__=='__main__':unittest.main()
