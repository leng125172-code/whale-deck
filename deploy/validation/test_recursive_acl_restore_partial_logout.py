"""Constructed protocol/SQL facts only; not real logout or installed acceptance."""
import copy,unittest
from test_recursive_acl_restore_partial_business_proof import fixture
from test_recursive_acl_restore_partial_workflow import packets
from recursive_acl_restore_partial_business_proof import require_partial,require_acknowledged
from recursive_acl_restore_partial_workflow import authority_proof
from verify_recursive_acl_restore_partial_cancel import verify
from validate_recursive_acl_restore_paging import main

def logout_fixture():
    f=fixture()
    for data in (f['data'],f['after']):
        data['row']['cancel_requested_at_utc']=None;data['executionJob']['cancel_requested_at_utc']=None
        data['audits']=[a for a in data['audits'] if not a['action'].endswith('.cancel-requested')]
    f['initial']['row']['cancel_requested_at_utc']=None
    return f

def partial(f):return require_partial(f['data'],f['ready'],f['initial'],f['baseline'],f['source'],f['api'],f['job'],logout=True)
def ack(f):return require_acknowledged(f['data'],f['after'],'resolver',f['ack_api'],f['ack_job'],logout=True)

def logout_packets():
    p=list(packets());p[0].update(cancellations=0,executionLogout=copy.deepcopy(p[0]['logout']),executionAnonymous=[401]*9,reloginSubject=p[0]['subject'],oldLookups=[404,404],hintsCleared=True,originalAcknowledgements=0);return p

class PartialLogoutTests(unittest.TestCase):
    def test_logout_without_cancel_keeps_partial_and_distinct_admin_ack(self):
        f=logout_fixture();self.assertEqual(partial(f)[1]['changedCount'],1);self.assertEqual(ack(f)[1]['changedCount'],1);authority_proof(*logout_packets(),logout=True)
    def test_explicit_cancel_is_never_accepted_as_logout(self):
        f=fixture()
        with self.assertRaises(AssertionError):partial(f)
        with self.assertRaises(AssertionError):ack(f)
        with self.assertRaises(AssertionError):authority_proof(*packets(),logout=True)
    def test_a_cancel_audit_cannot_hide_behind_null_cancel_time(self):
        f=logout_fixture();f['data']['audits'].append(fixture()['data']['audits'][3])
        with self.assertRaises(AssertionError):partial(f)
    def test_original_session_exit_is_required_separately_from_later_exit(self):
        p=logout_packets();p[0]['executionLogout']['providerNavigationObserved']=False
        with self.assertRaises(AssertionError):authority_proof(*p,logout=True)
        p=logout_packets();p[0]['executionAnonymous']=[200]*9
        with self.assertRaises(AssertionError):authority_proof(*p,logout=True)
    def test_same_subject_new_login_cannot_reacquire_or_ack_old_owner_reference(self):
        for key,bad in (('reloginSubject','other'),('oldLookups',[200,404]),('hintsCleared',False),('originalAcknowledgements',1)):
            p=logout_packets();p[0][key]=bad
            with self.subTest(key=key),self.assertRaises(AssertionError):authority_proof(*p,logout=True)
    def test_evidence_and_entrypoint_modes_cannot_be_relabelled(self):
        for name,mode in (('recursive-acl-restore-partial-cancel-'+'a'*32,True),('recursive-acl-restore-partial-logout-'+'a'*32,False)):
            with self.subTest(name=name),self.assertRaises(AssertionError):verify(name,logout=mode)
        with self.assertRaises(AssertionError):main(partial_logout=True)
        with self.assertRaises(AssertionError):main(partial_cancel=True,job_results=True,partial_logout='yes')

if __name__=='__main__':unittest.main()
