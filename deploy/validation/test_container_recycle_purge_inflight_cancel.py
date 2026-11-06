"""Explicit SQL/browser fixtures, not actual cancellation or helper exit proof."""
import copy,unittest,os,subprocess,signal,time,select
from pathlib import Path
from validate_container_recycle_purge_inflight_cancel import terminal_proof,preserved,settle_held
from bound_container_copy_cut import HeldCopy
from test_container_recycle_purge_browser_proof import row,summary


def sample():
    r=row('Canceled');r.update(actor_subject='subject',container_id='cid',instance_id='instance',relative_path='work/recycle-rpc.bin',cancel_requested_at_utc='now',confirmed_at_utc='now',completed_at_utc='now',reservation_json={'fixture':'Reserved'},lease_token=None,lease_expires_at_utc=None)
    v=dict(row=r,executionJob=dict(state='Canceled',result_json=summary(r)),pending=0,accepted=1,confirmed=1,canceled=0,planJob=dict(state='Succeeded',result_json=summary(r,False)))
    b=dict(final=dict(id=r['id']),subject='subject',actualUi=True,registrations=1,confirmations=1,cancellations=1,normalDualLogouts=2,reloginOriginalReferencesDenied=4)
    return v,b,dict(subject='subject',containerId='cid',instanceId='instance')


class InflightCancelProofTests(unittest.TestCase):
    def test_durable_apply_marker_is_distinct_from_root_aborted_outcome(self):
        v,b,f=sample();self.assertTrue(terminal_proof(v,b,f)['durableApplyMarkerIsNotDeletion'])

    def test_ready_only_cancel_or_unproved_outcome_cannot_pass(self):
        for key,bad in [('execution_job_id',None),('root_started_at_utc',None),('reservation_json',None),('apply_started_at_utc',None),('purge_applied',None),('purge_applied',True),('root_state','Unknown'),('publish_barrier',True),('source_fenced',True)]:
            v,b,f=sample();v['row'][key]=bad
            with self.subTest(key=key,bad=bad),self.assertRaises(AssertionError):terminal_proof(v,b,f)

    def test_one_ui_registration_confirmation_cancellation_and_normal_logout_required(self):
        for key,bad in [('registrations',2),('confirmations',0),('cancellations',0),('normalDualLogouts',1),('reloginOriginalReferencesDenied',0),('actualUi',False)]:
            v,b,f=sample();b[key]=bad
            with self.subTest(key=key),self.assertRaises(AssertionError):terminal_proof(v,b,f)

    def test_other_actor_target_or_pending_message_fails(self):
        for key,bad in [('actor_subject','foreign'),('relative_path','other'),('container_id','other'),('instance_id','other')]:
            v,b,f=sample();v['row'][key]=bad
            with self.subTest(key=key),self.assertRaises(AssertionError):terminal_proof(v,b,f)
        v,b,f=sample();v['pending']=1
        with self.assertRaises(AssertionError):terminal_proof(v,b,f)

    def test_precise_source_restore_and_single_purge_additions_only(self):
        source,restore,purge='a'*32,'b'*32,'c'*32
        before={k:{} for k in ('recycleRoot','recycleHdd','recycleRestoreRoot','purgeRoot','core')};after=copy.deepcopy(before)
        after['recycleRoot']={source+'.json':1,source+'.purge.json':2};after['purgeRoot']={purge+'.json':3}
        preserved(before,after,source,restore,purge)
        bad=copy.deepcopy(after);bad['purgeRoot']['d'*32+'.json']=1
        with self.assertRaises(AssertionError):preserved(before,bad,source,restore,purge)

    def test_gate_waits_exact_cut_before_cancel_and_verifier_never_mutates(self):
        root=Path(__file__).parent;s=(root/'validate_container_recycle_purge_inflight_cancel.mjs').read_text()
        self.assertLess(s.index("'cut-held.json'"),s.index("name:'请求取消留存清理'"))
        self.assertLess(s.index("'cancel-request-began.json'"),s.index("name:'确认请求取消'",s.index("phase='visible-cancel-original-execution'")))
        for bad in ('page.route(','addCookies(','storageState:'):self.assertNotIn(bad,s)
        v=(root/'verify_container_recycle_purge_inflight_cancel.py').read_text()
        for bad in ('.unlink(','.rmdir(','.rmtree(','.Popen(','.kill('):self.assertNotIn(bad,v)

    @unittest.skipUnless(hasattr(os,'pidfd_open'),'Linux pidfd required')
    def test_failed_owned_held_flow_cannot_resume_into_mutation(self):
        child=subprocess.Popen(['/bin/sleep','20']);fd=os.pidfd_open(child.pid);held=HeldCopy(child.pid,fd,'owned-test',{}, {})
        try:
            signal.pidfd_send_signal(fd,signal.SIGSTOP);until=time.monotonic()+2
            while Path(f'/proc/{child.pid}/stat').read_text().rsplit(')',1)[1].split()[0] not in ('T','t'):
                self.assertLess(time.monotonic(),until);time.sleep(.001)
            settle_held(held);child.wait(timeout=2);self.assertEqual(child.returncode,-signal.SIGKILL);self.assertIsNone(held.descriptor)
        finally:
            if child.poll() is None:child.kill();child.wait(timeout=2)
            if held.descriptor is not None:held.release()


if __name__=='__main__':unittest.main()
