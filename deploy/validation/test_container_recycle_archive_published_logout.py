"""Constructed post-publication logout witnesses; never real UI acceptance."""
import ast,copy,unittest
from pathlib import Path
from datetime import datetime,timedelta,timezone
from unittest.mock import patch
import archive_published_logout_proof as proof
import validate_container_recycle_archive_published_logout as gate
from test_container_recycle_archive_prepared_restart import sample as row_sample


def sample():
    v,f,p=row_sample();identifier=v['row']['id'];f['recoverySubject']='different-admin'
    receipt=dict(normalUiLogout=True,postStatus=302,providerNavigationObserved=True,rpCallbackObserved=True,bffStatus=401,identityStatus=403,finalLoginPage=True)
    b=dict(actualUi=True,subject=f['subject'],recoverySubject=f['recoverySubject'],ownerLogoutPosts=1,originalSessionReadStatus=401,hintsCleared=True,cancelledDecisionPreserved=True,oldReferenceStatuses=[404]*9,managementWrites=1,acknowledgements=1,jobProofs=[{},{}],logoutReceipts=[receipt,copy.deepcopy(receipt)],archives=[{},dict(id=identifier,state='Unknown',archiveCopied=None)],mutations=['/api/v1/containers/fixture/files/recycle-archives/'+identifier+'/confirm'],final=dict(state='Acknowledged',archiveCopied=None));return v,f,p,b


class PublishedLogoutTests(unittest.TestCase):
    def timing(self):
        v,_,_,_=sample();r=v['row'];start=datetime(2026,11,7,10,tzinfo=timezone.utc)
        row=dict(id=r['id'],execution_job_id=r['execution_job_id'],state='Applying',publish_barrier=True,cancel_requested_at_utc=None,lease_expires_at_utc=(start+timedelta(seconds=60)).isoformat(),execution_expires_at_utc=(start+timedelta(minutes=15)).isoformat(),actor_subject=r['actor_subject'])
        action=dict(id=row['id'],executionJobId=row['execution_job_id'],actualNavigationPost=True,path='/api/v1/auth/logout',monotonicNs='200',observedAtUtc=(start+timedelta(seconds=2)).isoformat())
        obs=dict(elapsedSeconds=5,observedWithOriginalPidfd=True,measurementBeginsBeforeAllowingUiAction=True,actualBffLogoutObservedWhileOriginalAlive=True,beganMonotonicNs=100,actionMonotonicNs=200,exitObservedMonotonicNs=500,beganAtUtc=start.isoformat(),exitObservedAtUtc=(start+timedelta(seconds=5)).isoformat());return row,action,obs

    def test_actual_normal_logout_and_unknown_are_both_required(self):
        v,f,p,b=sample();proof.browser_proof(b,f);proof.row_proof(v,f,p,False)
        row,action,obs=self.timing();proof.logout_exit_proof(obs,action,row)

    def test_no_cancel_aborted_or_successful_copy_can_masquerade_as_logout(self):
        for key,bad in (('cancel_requested_at_utc','explicit'),('root_state','Aborted'),('root_state','Completed'),('archive_copied',False),('archive_copied',True),('owned_fence',False)):
            v,f,p,_=sample();v['row'][key]=bad
            with self.subTest(key=key,bad=bad),self.assertRaises(AssertionError):proof.row_proof(v,f,p,False)

    def test_original_pidfd_alive_at_real_bff_navigation_post_is_mandatory(self):
        row,action,obs=self.timing()
        for key,bad in (('elapsedSeconds',20),('elapsedSeconds',True),('actualBffLogoutObservedWhileOriginalAlive',False),('observedWithOriginalPidfd',False),('beganMonotonicNs',250),('exitObservedMonotonicNs',150)):
            with self.subTest(key=key),self.assertRaises(AssertionError):proof.logout_exit_proof({**obs,key:bad},action,row)
        for key,bad in (('actualNavigationPost',False),('path','/other'),('executionJobId',row['id']),('monotonicNs','600')):
            with self.subTest(key=key),self.assertRaises(AssertionError):proof.logout_exit_proof(obs,{**action,key:bad},row)

    def test_lease_or_execution_expiry_is_not_a_logout_stop(self):
        row,action,obs=self.timing();start=datetime.fromisoformat(obs['beganAtUtc'])
        for key,bad in (('lease_expires_at_utc',(start+timedelta(seconds=40)).isoformat()),('execution_expires_at_utc',(start+timedelta(minutes=10)).isoformat()),('cancel_requested_at_utc','explicit'),('state','Unknown'),('publish_barrier',False)):
            with self.subTest(key=key),self.assertRaises(AssertionError):proof.logout_exit_proof(obs,action,{**row,key:bad})
        with self.assertRaises(AssertionError):proof.logout_exit_proof({**obs,'exitObservedAtUtc':row['lease_expires_at_utc']},action,row)

    def test_unknown_audit_follows_actual_navigation_before_browser_release(self):
        row,action,obs=self.timing();at=datetime.fromisoformat(action['observedAtUtc']);settled=dict(id=row['id'],executionJobId=row['execution_job_id'],state='Unknown',observedAtUtc=(at+timedelta(seconds=5)).isoformat());audit=dict(count=1,actors=[row['actor_subject']],atUtc=(at+timedelta(seconds=3)).isoformat());proof.result_order(row,action,settled,audit)
        for bad in ({**audit,'count':0},{**audit,'actors':['other']},{**audit,'atUtc':(at-timedelta(seconds=1)).isoformat()},{**audit,'atUtc':(at+timedelta(seconds=6)).isoformat()}):
            with self.subTest(audit=bad),self.assertRaises(AssertionError):proof.result_order(row,action,settled,bad)

    def test_both_bff_and_provider_logout_receipts_are_exact(self):
        for index in (0,1):
            for key,bad in (('normalUiLogout',False),('postStatus',200),('providerNavigationObserved',False),('rpCallbackObserved',False),('bffStatus',200),('identityStatus',200),('finalLoginPage',False)):
                _,f,_,b=sample();b['logoutReceipts'][index][key]=bad
                with self.subTest(index=index,key=key),self.assertRaises(AssertionError):proof.browser_proof(b,f)

    def test_old_session_and_old_references_do_not_survive_logout(self):
        for key,bad in (('ownerLogoutPosts',0),('ownerLogoutPosts',True),('ownerLogoutPosts',2),('originalSessionReadStatus',200),('oldReferenceStatuses',[404]*8+[200]),('recoverySubject','subject'),('hintsCleared',False)):
            _,f,_,b=sample();b[key]=bad
            with self.subTest(key=key),self.assertRaises(AssertionError):proof.browser_proof(b,f)

    def test_decline_and_unique_distinct_admin_ack_cannot_be_omitted(self):
        for key,bad in (('cancelledDecisionPreserved',False),('managementWrites',2),('acknowledgements',0),('logoutReceipts',[])):
            _,f,_,b=sample();b[key]=bad
            with self.subTest(key=key),self.assertRaises(AssertionError):proof.browser_proof(b,f)
        _,f,_,b=sample();b['final']['archiveCopied']=True
        with self.assertRaises(AssertionError):proof.browser_proof(b,f)

    def test_browser_observes_navigation_and_closes_dialog_drawer_before_owner_logout(self):
        s=(Path(__file__).parent/'validate_container_recycle_archive_published_logout.mjs').read_text();action=s.split("phase='normal-logout-bound-published-archive'",1)[1]
        for required in ('request.isNavigationRequest()',"request.method()==='POST'","url.pathname==='/api/v1/auth/logout'",'process.hrtime.bigint()',"observedLogout('owner-logout'",'credentials.recovery'):self.assertIn(required,action)
        self.assertLess(action.index('closePermissionDetails(page,dialog)'),action.index("observedLogout('owner-logout'"))
        for bad in ("name:'请求取消归档恢复'",'page.route(','storageState(','tracing.start(','addCookies(','process.env.VALIDATION_PASSWORD='):self.assertNotIn(bad,action if bad.startswith('name:') else s)

    def test_parent_observer_starts_before_allowing_ui_and_cleanup_after_live_proof(self):
        s=(Path(__file__).parent/'validate_container_recycle_archive_published_logout.py').read_text();self.assertIn("held.record['state']=='Publishing'",s);self.assertIn('freeze_published_namespace(',s)
        self.assertLess(s.index('pool.submit(observe_archive_logout_exit'),s.index("save('cut-held.json',dict(id=r['id']"));self.assertLess(s.index('independent(repo,e,True)'),s.index('cleanup_owned_target(f['))
        for bad in ('OwnedAuthentikRevocation(',"'systemctl'",'worker.start()'):self.assertNotIn(bad,s)

    def test_independent_verifier_is_readonly_and_closes_logout_and_published_joins(self):
        s=(Path(__file__).parent/'verify_container_recycle_archive_published_logout.py').read_text();calls={ast.unparse(n.func) for n in ast.walk(ast.parse(s)) if isinstance(n,ast.Call)}
        for expected in ('logout_exit_proof','published_proof','require_ack','result_order','result_audit','io.read_operation','hdd_observation'):self.assertIn(expected,calls)
        for bad in ('cleanup_owned_target','os.unlink','os.rmdir','subprocess.Popen'):self.assertNotIn(bad,calls)


if __name__=='__main__':unittest.main()
