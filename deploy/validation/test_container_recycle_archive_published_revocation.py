"""Constructed revocation/Unknown/ACK contracts; no identity or service writes."""
import ast,copy,unittest
from pathlib import Path
from datetime import datetime,timedelta
from unittest.mock import patch
import archive_published_revocation_proof as proof
import validate_container_recycle_archive_published_revocation as gate
from test_container_recycle_archive_revocation import sample as old_sample
from test_container_recycle_archive_prepared_restart import sample as row_sample
import test_container_recycle_revocation as native


def browser(kind):
    _,f,_,b=old_sample(kind);b.update(managementWrites=1,acknowledgements=1,jobProofs=[{},{}],cancelledDecisionPreserved=True,final=dict(state='Acknowledged',archiveCopied=None))
    b['archives'][1].update(state='Unknown',archiveCopied=None);return f,b


class PublishedRevocationTests(unittest.TestCase):
    def authority(self,kind):
        a=native.RecycleRevocationTests().authority(kind);confirmed=datetime.fromisoformat(a['confirmedAtUtc']);a['leaseExpiresAtCutUtc']=(confirmed+timedelta(seconds=60)).isoformat()
        f=dict(kind=kind,subject=native.SUBJECT);row=dict(state='Applying',publish_barrier=True,cancel_requested_at_utc=None,execution_expires_at_utc=a['executionExpiresAtUtc'],lease_expires_at_utc=a['leaseExpiresAtCutUtc'])
        x=dict(elapsedSeconds=2,observedWithOriginalPidfd=True,measurementBeginsBeforeAuthorityMutation=True,originalAliveBeforeAndAfterAuthorityConfirmation=True,beganMonotonicNs=100,confirmedMonotonicNs=200,exitObservedMonotonicNs=300,exitObservedAtUtc=(confirmed+timedelta(seconds=2)).isoformat());return a,f,row,x

    def test_both_current_identity_pages_and_unknown_ack_are_required(self):
        for kind in proof.KINDS:
            f,b=browser(kind);proof.browser_proof(b,f)
            a,f,row,x=self.authority(kind);proof.authority_exit_proof(a,f,row,x)

    def test_unknown_facts_are_not_reserved_aborted_or_successful_copy(self):
        for ack in (False,True):
            v,f,p=row_sample(ack);proof.row_proof(v,f,p,ack)
        for key,bad in (('root_state','Aborted'),('root_state','Completed'),('archive_copied',False),('archive_copied',True),('owned_fence',False),('cancel_requested_at_utc','explicit')):
            v,f,p=row_sample(False);v['row'][key]=bad
            with self.subTest(key=key,bad=bad),self.assertRaises(AssertionError):proof.row_proof(v,f,p,False)

    def test_authority_timing_cannot_be_expiry_or_unconfirmed_exit(self):
        a,f,row,x=self.authority('group-removal');confirmed=datetime.fromisoformat(a['confirmedAtUtc'])
        for key,bad in (('elapsedSeconds',20),('elapsedSeconds',True),('originalAliveBeforeAndAfterAuthorityConfirmation',False),('confirmedMonotonicNs',400),('exitObservedAtUtc',a['leaseExpiresAtCutUtc']),('exitObservedAtUtc',(confirmed-timedelta(seconds=1)).isoformat())):
            with self.subTest(key=key),self.assertRaises(AssertionError):proof.authority_exit_proof(a,f,row,{**x,key:bad})
        short=(confirmed+timedelta(seconds=25)).isoformat()
        with self.assertRaises(AssertionError):proof.authority_exit_proof({**a,'leaseExpiresAtCutUtc':short},f,{**row,'lease_expires_at_utc':short},x)

    def test_actual_unknown_audit_must_follow_confirmation_before_browser_release(self):
        a,_,_,_=self.authority('group-removal');at=datetime.fromisoformat(a['confirmedAtUtc']);row=dict(id='intent',execution_job_id='job',actor_subject='owner');settled=dict(id='intent',executionJobId='job',state='Unknown',observedAtUtc=(at+timedelta(seconds=4)).isoformat());audit=dict(count=1,actors=['owner'],atUtc=(at+timedelta(seconds=3)).isoformat());proof.result_order(a,row,settled,audit)
        for bad in ({**audit,'count':0},{**audit,'actors':['other']},{**audit,'atUtc':(at-timedelta(seconds=1)).isoformat()},{**audit,'atUtc':(at+timedelta(seconds=5)).isoformat()}):
            with self.subTest(audit=bad),self.assertRaises(AssertionError):proof.result_order(a,row,settled,bad)
        with self.assertRaises(AssertionError):proof.result_order(a,row,{**settled,'executionJobId':'foreign'},audit)

    def test_only_generated_owned_identity_and_original_sql_deadline_join(self):
        for kind in proof.KINDS:
            a,f,row,x=self.authority(kind)
            for key,bad in (('execution_expires_at_utc','other'),('lease_expires_at_utc','other'),('publish_barrier',False),('state','Unknown'),('cancel_requested_at_utc','explicit')):
                with self.subTest(kind=kind,key=key),self.assertRaises(AssertionError):proof.authority_exit_proof(a,f,{**row,key:bad},x)
            a['after']['username']='other-admin'
            with self.assertRaises(AssertionError):proof.authority_exit_proof(a,f,row,x)

    def test_no_cancel_logout_hidden_panel_or_old_reference_inheritance(self):
        for kind in proof.KINDS:
            for key,bad in (('ownerLogoutPosts',1),('privatePanelsAbsent',False),('hintsCleared',False),('oldReferenceStatuses',[404]*8+[200]),('recoverySubject','subject'),('renderedRevocationPage','containers'),('currentIdentityStatus',403 if kind=='group-removal' else 200)):
                f,b=browser(kind);b[key]=bad
                with self.subTest(kind=kind,key=key),self.assertRaises(AssertionError):proof.browser_proof(b,f)
            f,b=browser(kind);b['mutations'].append('/files/recycle-archives/'+b['archives'][1]['id']+'/cancel')
            with self.assertRaises(AssertionError):proof.browser_proof(b,f)

    def test_decline_zero_writes_and_one_explicit_ack_are_not_optional(self):
        for key,bad in (('managementWrites',0),('managementWrites',2),('acknowledgements',0),('acknowledgements',2),('cancelledDecisionPreserved',False),('jobProofs',[{}])):
            f,b=browser('group-removal');b[key]=bad
            with self.subTest(key=key,bad=bad),self.assertRaises(AssertionError):proof.browser_proof(b,f)
        f,b=browser('account-disable');b['final']['archiveCopied']=True
        with self.assertRaises(AssertionError):proof.browser_proof(b,f)

    def test_browser_recovery_is_new_context_with_private_credentials_and_no_owner_logout(self):
        root=Path(__file__).parent;s=(root/'validate_container_recycle_archive_published_revocation.mjs').read_text();segment=s.split("phase='wait-published-authority-change-and-original-result'",1)[1].split('  async function showJob(',1)[0]
        for bad in ('observedLogout(',"name:'请求取消归档恢复'",'process.env.VALIDATION_PASSWORD=','page.route(','storageState(','tracing.start(','addCookies('):self.assertNotIn(bad,segment if bad.startswith(('observed','name:')) else s)
        self.assertIn('credentials.recovery',s);self.assertIn("coordinate('decision'",s);self.assertIn("coordinate('ack'",s)
        self.assertLess(s.index("coordinate('decision'"),s.index("name:'确认处置并释放保护'"))

    def test_controller_keeps_namespace_cut_and_only_owned_identity_change(self):
        root=Path(__file__).parent;s=(root/'validate_container_recycle_archive_published_revocation.py').read_text()
        for required in ('freeze_published_namespace(',"held.record['state']=='Publishing'",'identity.change(kind)',"with OwnedAuthentikRevocation(username,fixture['subject'])",'originalAliveBeforeAndAfterAuthorityConfirmation=True','timedelta(seconds=45)'):self.assertIn(required,s)
        for bad in ('worker.start()',"'systemctl'","['docker','kill'",'restart(kind,'):self.assertNotIn(bad,s)
        self.assertLess(s.index('independent(repo,e,True)'),s.index('cleanup_owned_target(f['))

    def test_independent_verifier_cannot_mutate_or_omit_authority_and_published_target(self):
        root=Path(__file__).parent;s=(root/'verify_container_recycle_archive_published_revocation.py').read_text();calls={ast.unparse(n.func) for n in ast.walk(ast.parse(s)) if isinstance(n,ast.Call)}
        for required in ('authority_exit_proof','published_proof','require_ack','io.read_operation','hdd_observation','source_closure'):self.assertIn(required,calls)
        for bad in ('cleanup_owned_target','os.unlink','os.rmdir','subprocess.Popen'):self.assertNotIn(bad,calls)

    def test_unsupported_identity_mutation_is_rejected_before_any_host_read(self):
        for kind in ('logout','worker-crash','agent-restart','reboot'):
            with self.subTest(kind=kind),patch.object(gate.os,'geteuid') as read,self.assertRaises(AssertionError):gate.main('a'*40,kind)
            read.assert_not_called()


if __name__=='__main__':unittest.main()
