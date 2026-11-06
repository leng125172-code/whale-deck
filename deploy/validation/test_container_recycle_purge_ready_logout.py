"""Expiry/cleanup negatives use explicit fixtures, not actual identity or disk proof."""
import copy
from pathlib import Path
import unittest
from validate_container_recycle_purge_ready_logout import terminal_proof,preserved,restored_hdd
from test_container_recycle_purge_browser_proof import row,summary,hdd


def sample():
    r=row();r.update(actor_subject='subject',container_id='cid',instance_id='instance',relative_path='work/recycle-rpc.bin',state='Expired',result_code='CONTAINER_RECYCLE_PURGE_PLAN_EXPIRED',completed_at_utc='2026-11-06T10:05:01+00:00',ready_expires_at_utc='2026-11-06T10:05:00+00:00')
    for k in ('cancel_requested_at_utc','confirmed_at_utc','execution_job_id','execution_hash','root_started_at_utc','reservation_json','reservation_hash','apply_started_at_utc','receipt_json','receipt_hash','root_state','purge_applied','source_fenced','owned_fence','lease_token','lease_expires_at_utc'):r[k]=None
    r['publish_barrier']=False
    v=dict(row=r,executionJob=None,pending=0,accepted=1,confirmed=0,canceled=0,planJob=dict(state='Succeeded',result_json=summary(r,False)))
    b=dict(original=dict(id=r['id'],readyExpiresAtUtc='2026-11-06T10:05:00.000000+00:00'),final=dict(id=r['id']),subject='subject',actualUi=True,clockChanged=False,registrations=1,confirmations=0,cancellations=0,anonymous=[401]*4,oldConfirmStatus=401,newLoginReads=[404]*2,newConfirmStatus=404,elapsedMs=300000,normalDualLogouts=2)
    return v,b,dict(containerId='cid',instanceId='instance')


class ReadyExpiryTests(unittest.TestCase):
    def test_equivalent_timestamp_formats_keep_historical_ready(self):
        v,b,f=sample();self.assertTrue(terminal_proof(v,b,f)['planningJobHistoricalReady'])

    def test_no_cancellation_execution_reservation_or_false_outcome_is_inferred(self):
        for k,bad in [('cancel_requested_at_utc','now'),('execution_job_id','id'),('reservation_json',{}),('apply_started_at_utc','now'),('purge_applied',False),('source_fenced',False),('publish_barrier',True),('state','Canceled')]:
            v,b,f=sample();v['row'][k]=bad
            with self.subTest(k=k),self.assertRaises(AssertionError):terminal_proof(v,b,f)

    def test_wrong_login_short_wait_or_clock_change_cannot_pass(self):
        for k,bad in [('newConfirmStatus',202),('oldConfirmStatus',400),('newLoginReads',[200,200]),('clockChanged',True),('elapsedMs',1000),('normalDualLogouts',1)]:
            v,b,f=sample();b[k]=bad
            with self.subTest(k=k),self.assertRaises(AssertionError):terminal_proof(v,b,f)

    def test_api_expired_projection_is_not_sql_terminal_proof(self):
        for k,bad in [('completed_at_utc',None),('state','Ready'),('completed_at_utc','2026-11-06T10:04:59+00:00'),('completed_at_utc','2026-11-06T10:06:00+00:00')]:
            v,b,f=sample();v['row'][k]=bad
            with self.subTest(k=k,bad=bad),self.assertRaises(AssertionError):terminal_proof(v,b,f)

    def test_original_job_and_exact_acceptance_audit_are_preserved(self):
        for change in ('job','confirmed','canceled','pending'):
            v,b,f=sample()
            if change=='job':v['planJob']['state']='Failed'
            else:v[change]=1
            with self.subTest(change=change),self.assertRaises(AssertionError):terminal_proof(v,b,f)

    def test_qa_restore_only_changes_hdd_capture_state(self):
        before=hdd();after=copy.deepcopy(before);after['history']['captureState']='Restored';restored_hdd(before,after)
        after['data']['identity'][1]+=1
        with self.assertRaises(AssertionError):restored_hdd(before,after)

    def test_only_exact_new_source_and_restore_records_are_allowed(self):
        before={k:{} for k in ('recycleRoot','recycleHdd','recycleRestoreRoot','purgeRoot','core')};after=copy.deepcopy(before)
        source,restore='a'*32,'b'*32
        after['recycleRoot']={source+'.json':1};after['recycleHdd']={source+'.data':2};after['recycleRestoreRoot']={'claims/'+source+'.json':3}
        preserved(before,after,source,restore)
        for k,name in [('purgeRoot','c'*32+'.json'),('recycleRoot','foreign.json'),('recycleHdd',restore+'.data')]:
            bad=copy.deepcopy(after);bad[k][name]=1
            with self.subTest(k=k),self.assertRaises(AssertionError):preserved(before,bad,source,restore)

    def test_browser_never_confirms_purge_or_edits_clock_session_or_records(self):
        s=Path(__file__).with_name('validate_container_recycle_purge_ready_logout.mjs').read_text()
        for bad in ('page.route(', 'addCookies(', 'storageState:', 'setSystemTime', 'clock.install(', "name:'确认删除留存原件'"):
            self.assertNotIn(bad,s)
        self.assertIn('natural-expiry-observation',s);self.assertIn('logoutFromUserMenu',s)
        self.assertIn("coordinate('expiry', final)",s);self.assertIn("coordinate('restore-ready'",s)
        v=Path(__file__).with_name('verify_container_recycle_purge_ready_logout.py').read_text()
        for bad in ('.unlink(','.rmdir(','.rmtree(','.Popen(','.kill('):self.assertNotIn(bad,v)


if __name__=='__main__':unittest.main()
