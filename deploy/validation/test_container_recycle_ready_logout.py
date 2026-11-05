import copy
import unittest
from pathlib import Path
from validate_container_recycle_ready_logout import terminal_proof


def sample():
    row=dict(id='id',actor_subject='subject',container_id='cid',instance_id='instance',relative_path='work/recycle-rpc.bin',state='Expired',result_code='CONTAINER_RECYCLE_PLAN_EXPIRED',completed_at_utc='2026-11-05T10:05:01+00:00',cancel_requested_at_utc=None,confirmed_at_utc=None,execution_job_id=None,execution_hash=None,root_started_at_utc=None,receipt_json=None,receipt_hash=None,publish_barrier=False,lease_token=None,lease_expires_at_utc=None,history_may_exist=False,history_verified=False,history_digest=None,source_retained=None,source_restored=None,binding_current=None,history_available=None,root_state=None,ready_expires_at_utc='2026-11-05T10:05:00+00:00')
    facts=dict(stage='planning',state='Ready',receiptState=None,historyMayExist=False,historyVerified=False)
    for i in range(9):facts['extra'+str(i)]=None
    value=dict(row=row,executionJob=None,pending=0,accepted=1,confirmed=0,planJob=dict(state='Succeeded',result_json=facts))
    browser=dict(original=dict(id='id',readyExpiresAtUtc='2026-11-05T10:05:00.000000+00:00'),final=dict(id='id'),subject='subject',actualUi=True,normalDualLogouts=2,registrations=1,confirmations=0,cancellations=0,clockChanged=False,anonymous=[401]*4,oldConfirmStatus=401,newLoginReads=[404]*2,newConfirmStatus=404,elapsedMs=300000)
    fixture=dict(containerId='cid',instanceId='instance');return value,browser,fixture

class ReadyLogoutProofTests(unittest.TestCase):
    def test_original_plan_job_and_equivalent_timestamp_formats_are_retained(self):
        v,b,f=sample();self.assertEqual(terminal_proof(v,b,f)['sqlExpiryLatenessSeconds'],1)
    def test_cancel_execution_or_source_claim_cannot_masquerade_as_natural_expiry(self):
        for key,value in [('execution_job_id','new'),('cancel_requested_at_utc','now'),('root_started_at_utc','now'),('source_retained',False),('publish_barrier',True),('history_may_exist',True),('state','Canceled')]:
            v,b,f=sample();v['row'][key]=value
            with self.subTest(key=key),self.assertRaises(AssertionError):terminal_proof(v,b,f)
    def test_foreign_login_authority_and_fake_clock_are_not_accepted(self):
        for key,value in [('clockChanged',True),('newConfirmStatus',202),('newLoginReads',[200,200]),('oldConfirmStatus',400),('normalDualLogouts',1),('elapsedMs',1000)]:
            v,b,f=sample();b[key]=value
            with self.subTest(key=key),self.assertRaises(AssertionError):terminal_proof(v,b,f)
    def test_early_and_long_delayed_expiry_do_not_pass(self):
        for timestamp in ('2026-11-05T10:04:59+00:00','2026-11-05T10:06:00+00:00'):
            v,b,f=sample();v['row']['completed_at_utc']=timestamp
            with self.assertRaises(AssertionError):terminal_proof(v,b,f)
    def test_original_facts_are_not_relabelled_as_failed_plan(self):
        v,b,f=sample();v['planJob']['state']='Failed'
        with self.assertRaises(AssertionError):terminal_proof(v,b,f)
    def test_no_clock_session_injection_or_task_mutation_shortcut(self):
        code=Path(__file__).with_name('validate_container_recycle_ready_logout.mjs').read_text()
        for forbidden in ('page.route(', 'addCookies(', 'storageState:', 'setSystemTime', 'clock.install('):
            self.assertNotIn(forbidden,code)
        self.assertNotRegex(code,r'request\.post\([^\n]*?/cancel');self.assertIn('logoutFromUserMenu(page,context,base)',code);self.assertIn('natural-expiry-observation',code)

if __name__=='__main__':unittest.main()
