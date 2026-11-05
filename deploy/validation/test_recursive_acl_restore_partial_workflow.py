"""Protocol/scope negatives with explicit doubles; never installed acceptance."""
import copy,json,unittest
from pathlib import Path
from tempfile import TemporaryDirectory
from unittest.mock import Mock,patch
from recursive_acl_restore_partial_workflow import wait_packet,normal_logout,authority_proof,exercise
from verify_recursive_acl_restore_partial_cancel import verify
from validate_recursive_acl_restore_paging import main

def packets():
    logout=dict(normalUiLogout=True,providerNavigationObserved=True,finalLoginPage=True,postStatus=302,bffStatus=401,identityStatus=403)
    partial=dict(id='original',subject='owner',registrations=1,confirmations=1,cancellations=1,uploads=0,anonymous=[401]*9,logout=copy.deepcopy(logout))
    recovery=dict(id='original',subject='resolver',acknowledgements=1,anonymous=[401]*9,logout=copy.deepcopy(logout))
    ordinary=dict(subject='ordinary',denied=[403]*9,posts=[403]*3,anonymous=[401]*9,uiRequests=0,logout=copy.deepcopy(logout))
    dismissed=dict(id='original',subject='resolver',acknowledgements=0,ownedLookup=404)
    return partial,recovery,ordinary,dismissed

class PartialWorkflowTests(unittest.TestCase):
    def test_three_distinct_normal_sessions_and_one_explicit_cancel_ack(self):authority_proof(*packets())
    def test_same_actor_replay_boolean_count_or_wrong_resource_is_not_accepted(self):
        for index,key,bad in ((1,'subject','owner'),(1,'id','other'),(0,'cancellations',2),(0,'confirmations',True),(1,'acknowledgements',True),(3,'acknowledgements',1),(3,'ownedLookup',200),(2,'uiRequests',False)):
            p=list(packets());p[index][key]=bad
            with self.subTest(index=index,key=key),self.assertRaises(AssertionError):authority_proof(*p)
    def test_every_private_route_and_write_must_be_denied(self):
        for index,key,bad in ((0,'anonymous',[200]*9),(1,'anonymous',[403]*9),(2,'denied',[200]*9),(2,'posts',[403]*2),(2,'anonymous',[401]*8)):
            p=list(packets());p[index][key]=bad
            with self.subTest(index=index,key=key),self.assertRaises(AssertionError):authority_proof(*p)
    def test_logout_must_be_normal_bff_and_provider_exit(self):
        for key,bad in (('normalUiLogout',False),('providerNavigationObserved',False),('finalLoginPage',False),('postStatus',200),('bffStatus',200),('identityStatus',200)):
            value=packets()[0]['logout'];value[key]=bad
            with self.subTest(key=key),self.assertRaises(AssertionError):normal_logout(value)
    def test_unknown_packet_or_wrong_evidence_mode_fails_before_io(self):
        for name in ('../escape','action','ready','recovery-result'):
            with self.subTest(name=name),self.assertRaises(AssertionError):wait_packet(Mock(),Path('/unused'),name)
        for name in ('../escape','recursive-acl-restore-prewrite-cancel-'+'a'*32,'recursive-acl-restore-partial-cancel-'+'A'*32):
            with self.subTest(name=name),self.assertRaises(AssertionError):verify(name)
        for kwargs in (dict(partial_cancel=True),dict(partial_cancel=True,job_results=True,inflight_logout=True),dict(partial_cancel='yes',job_results=True)):
            with self.subTest(kwargs=kwargs),self.assertRaises(AssertionError):main(**kwargs)
    def test_failure_before_ready_keeps_fixture_and_reaps_only_owned_browser(self):
        fixture={};process=Mock()
        with TemporaryDirectory() as directory,patch('recursive_acl_restore_partial_workflow.wait_signal',side_effect=RuntimeError('explicit fixture')),patch('recursive_acl_restore_partial_workflow.stop_browser') as stop,patch('recursive_acl_restore_partial_workflow.Worker') as worker:
            with self.assertRaisesRegex(RuntimeError,'explicit fixture'):exercise(process,Path(directory),{}, {},{},None,None,None,None,None,fixture)
            self.assertIs(fixture['retain_unsettled'],True);stop.assert_called_once_with(process);worker.assert_not_called()
            value=json.loads((Path(directory)/'partial-failure-context.json').read_text());self.assertEqual(value['type'],'RuntimeError')
    def test_ui_source_uses_visible_actions_and_no_auth_or_response_injection(self):
        source=(Path(__file__).parent/'installed_ui_restore_partial_resolution.mjs').read_text()
        for marker in ('.route(','.addCookies(','sessionStorage.setItem','localStorage.setItem','request.put('):self.assertNotIn(marker,source)
        for marker in ('暂不处置','确认处置并释放保护','logoutFromUserMenu','credentials.resolver','boundFirstCompleted','partialPhysicalVerified','ackAndPhysicalPreserved'):self.assertIn(marker,source)

if __name__=='__main__':unittest.main()
