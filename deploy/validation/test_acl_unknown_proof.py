"""Negative boundaries for real ACL Unknown/ack and natural defer evidence."""
import copy
import datetime as dt
import unittest
from verify_container_acl_unknown import require_acknowledgement, require_deferrals, IMMUTABLE
from validate_container_acl_unknown import require_blocked, WAITING_PATH


class UnknownProofTests(unittest.TestCase):
    def setUp(self):
        old={key:'original-'+key for key in IMMUTABLE};old.update(actor_subject='old',state='Unknown',publish_barrier=True,completed_at_utc=None,acl_confirmed=None,published_version=None,
            history_may_exist=True,history_verified=True,lease_token=None,lease_expires_at_utc=None)
        final={**old,'state':'Acknowledged','publish_barrier':False,'completed_at_utc':'2026-11-03T04:00:00Z','acknowledged_by':'new','result_code':'CONTAINER_ACL_UNKNOWN_ACKNOWLEDGED'}
        self.original={'row':old,'executionJob':{'state':'Running'}};self.final={'row':final,'executionJob':{'state':'Failed'},'planJob':{'state':'Succeeded'},'pending':0}

    def test_ack_retains_exact_first_historical_receipt_and_all_original_facts(self):
        require_acknowledgement(self.original,self.final,'old','new')
        for key in IMMUTABLE:
            final=copy.deepcopy(self.final);final['row'][key]='rewritten'
            with self.subTest(key=key),self.assertRaises(AssertionError):require_acknowledgement(self.original,final,'old','new')

    def test_original_identity_or_wrong_current_actor_is_not_different_admin_proof(self):
        with self.assertRaises(AssertionError):require_acknowledgement(self.original,self.final,'old','old')
        final=copy.deepcopy(self.final);final['row']['acknowledged_by']='foreign'
        with self.assertRaises(AssertionError):require_acknowledgement(self.original,final,'old','new')

    def test_ack_must_fail_original_execution_and_clear_both_lease_fields(self):
        for change in ({'publish_barrier':True},{'lease_token':'live'},{'lease_expires_at_utc':'future'},{'completed_at_utc':None}):
            final=copy.deepcopy(self.final);final['row'].update(change)
            with self.subTest(change=change),self.assertRaises(AssertionError):require_acknowledgement(self.original,final,'old','new')
        final=copy.deepcopy(self.final);final['executionJob']['state']='Succeeded'
        with self.assertRaises(AssertionError):require_acknowledgement(self.original,final,'old','new')

    def test_unknown_confirmation_remains_null_even_when_qa_observed_unchanged_file(self):
        for value in (False,True):
            old=copy.deepcopy(self.original);final=copy.deepcopy(self.final);old['row']['acl_confirmed']=final['row']['acl_confirmed']=value
            with self.subTest(value=value),self.assertRaises(AssertionError):require_acknowledgement(old,final,'old','new')


class DeferProofTests(unittest.TestCase):
    def setUp(self):
        self.start=dt.datetime(2026,11,3,tzinfo=dt.timezone.utc);self.waiting={'id':'waiting','containerId':'other-cid'}
        self.samples=[]
        for index in range(4):
            at=self.start+dt.timedelta(seconds=index*6);row={'id':'waiting','actor_subject':'new','container_id':'other-cid','relative_path':WAITING_PATH,'state':'Pending','root_started_at_utc':None,
                'publish_barrier':False,'completed_at_utc':None,'lease_token':None,'lease_expires_at_utc':None,'version':index*2+2,'next_attempt_at_utc':(at+dt.timedelta(seconds=6)).isoformat()}
            self.samples.append({'observedAtUtc':at.isoformat(),'sql':{'row':row},'rootAbsent':True,'targetAbsent':True,'aclUnknownProtected':True,'sourceUnchanged':True})
        self.release=self.start+dt.timedelta(seconds=30)

    def check(self,samples):require_deferrals(samples,self.waiting,'new',self.release)

    def test_four_distinct_natural_deferrals_are_required_not_repeated_polls(self):
        self.check(self.samples)
        for changed in (self.samples[:3],[self.samples[0]]*4):
            with self.subTest(count=len(changed)),self.assertRaises(AssertionError):self.check(changed)

    def test_initial_six_second_queue_delay_is_not_a_real_defer(self):
        changed=copy.deepcopy(self.samples);changed[0]['sql']['row']['version']=0
        with self.assertRaises(AssertionError):self.check(changed)
        self.assertFalse(require_blocked(changed[0]['sql'],self.waiting,'new',self.start))

    def test_rapid_observations_with_future_spaced_attempts_do_not_prove_elapsed_retries(self):
        changed=copy.deepcopy(self.samples)
        for index,sample in enumerate(changed):sample['observedAtUtc']=(self.start+dt.timedelta(milliseconds=index*500)).isoformat()
        with self.assertRaises(AssertionError):self.check(changed)

    def test_created_root_target_or_released_unknown_cannot_be_a_blocked_sample(self):
        for key in ('rootAbsent','targetAbsent','aclUnknownProtected','sourceUnchanged'):
            changed=copy.deepcopy(self.samples);changed[2][key]=False
            with self.subTest(key=key),self.assertRaises(AssertionError):self.check(changed)
        for change in ({'container_id':'original-cid'},{'root_started_at_utc':'now'},{'publish_barrier':True},{'relative_path':'work/foreign'}):
            changed=copy.deepcopy(self.samples);changed[1]['sql']['row'].update(change)
            with self.subTest(change=change),self.assertRaises(AssertionError):self.check(changed)

    def test_live_or_expired_attempt_not_counted_as_a_completed_defer(self):
        value=self.samples[0]['sql'];self.assertTrue(require_blocked(value,self.waiting,'new',self.start))
        for change in ({'lease_token':'claimed'},{'lease_expires_at_utc':'future'},{'next_attempt_at_utc':self.start.isoformat()}):
            changed=copy.deepcopy(value);changed['row'].update(change)
            with self.subTest(change=change):self.assertFalse(require_blocked(changed,self.waiting,'new',self.start))

    def test_fake_subsecond_attempts_and_post_release_samples_are_rejected(self):
        changed=copy.deepcopy(self.samples)
        for index,sample in enumerate(changed):sample['sql']['row']['next_attempt_at_utc']=(self.start+dt.timedelta(seconds=20+index)).isoformat()
        with self.assertRaises(AssertionError):self.check(changed)
        changed=copy.deepcopy(self.samples);changed[-1]['observedAtUtc']=self.release.isoformat()
        with self.assertRaises(AssertionError):self.check(changed)


if __name__=='__main__':unittest.main()
