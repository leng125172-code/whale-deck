"""Negative boundaries for real ACL Unknown/ack and natural defer evidence."""
import copy
import datetime as dt
import unittest
import json
import uuid
from unittest.mock import patch
from verify_container_acl_unknown import require_acknowledgement, require_deferrals, IMMUTABLE
from validate_container_acl_unknown import require_blocked, WAITING_PATH, require_cut, expected_physical, verify_unknown_jobs, expected_job_summary
from verify_container_acl_unknown import require_source_version


class PostWriteEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.original={'acl-file.bin':{'identity':[1,2,0o100640,2100,3100,1,4,10,20],'access':[{'old':True}],'default':[],'attrs':['attribute']},'other':{'identity':[1,3,0,0,0,1,4,10,20]}}
        self.physical=copy.deepcopy(self.original);self.physical['acl-file.bin']['identity'][8]=21;self.physical['acl-file.bin']['access']=[{'new':True}]
        self.plan={'sourceIdentity':self.original['acl-file.bin']['identity'],'accessAcl':self.original['acl-file.bin']['access'],'defaultAcl':[],'command':{'entries':[{'new':True}]},'targetMode':0o640,'attributesDigest':'digest'}
        self.record={'state':'Applying','aclConfirmed':None,'historyVerified':True,'actualIdentity':None,'version':'','plan':self.plan}
        self.proof={'originalIdentity':self.plan['sourceIdentity'],'changedIdentity':self.physical['acl-file.bin']['identity'],'accessAcl':[{'new':True}],'defaultAcl':[],'attributesDigest':'digest'}
        self.base={'postAcl':True,'original':self.original,'cutPhysical':self.physical}
    def check(self):
        with patch('validate_container_acl_unknown.unchanged_except_target') as unchanged:
            require_cut(self.base,self.record,self.proof,self.physical);unchanged.assert_called_once_with(self.original,self.physical,'acl-file.bin',0o640)
    def test_write_after_cut_keeps_two_separate_inventories(self):
        self.check();self.assertEqual(expected_physical(self.base),self.physical);self.assertNotEqual(self.original,self.physical)
        self.assertEqual(expected_physical({'original':self.original}),self.original)
    def test_no_change_missing_ctime_or_false_completed_receipt_is_not_post_cut(self):
        self.check()
        for field,value in (('state','Prepared'),('aclConfirmed',True),('actualIdentity',self.proof['changedIdentity']),('version','invented')):
            with self.subTest(field=field),patch('validate_container_acl_unknown.unchanged_except_target'),self.assertRaises(AssertionError):require_cut(self.base,{**self.record,field:value},self.proof,self.physical)
        old=copy.deepcopy(self.physical);old['acl-file.bin']['identity'][8]=20
        with patch('validate_container_acl_unknown.unchanged_except_target'),self.assertRaises(AssertionError):require_cut(self.base,self.record,self.proof,old)
    def test_new_post_cut_cannot_be_claimed_by_old_proof_version(self):
        require_source_version({'proofVersion':2},{})
        require_source_version({'proofVersion':3},{'postAcl':True})
        for version,base in ((2,{'postAcl':True}),(3,{}),(True,{}),(4,{'postAcl':True})):
            with self.subTest(version=version),self.assertRaises(AssertionError):require_source_version({'proofVersion':version},base)


class UnknownJobEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.identifier=str(uuid.uuid4());self.execution=str(uuid.uuid4());self.plan={'command':{'aclType':'access'},'kind':'File','targetMode':416,'noChange':False}
    def fixture(self,stage):
        row={'id':self.identifier,'execution_job_id':self.execution,'plan_json':'{}','state':'Unknown' if stage=='unknown' else 'Acknowledged','result_code':'CONTAINER_ACL_RESULT_UNKNOWN' if stage=='unknown' else 'CONTAINER_ACL_UNKNOWN_ACKNOWLEDGED','history_digest':'H'*64,'actor_subject':'original-actor','relative_path':'work/acl-file.bin'}
        data={'row':row};proof={'id':self.identifier,'stage':stage,'readOnly':True,'taskCenter':{'id':self.execution,'nullConfirmationVisible':True,'noGenericWriteControls':True}}
        for execution in (False,True):
            value=expected_job_summary(row,self.plan,execution);state='Succeeded';identifier=self.execution if execution else self.identifier
            if execution:value.update(state=row['state'],code=row['result_code'],aclConfirmed=None,receiptState='Unknown',bindingCurrent=False,hasPublishedVersion=False);state='Running' if stage=='unknown' else 'Failed'
            job={'id':identifier,'state':state,'jobType':'containers.files.acl' if execution else 'containers.files.acl-plan','resultJson':json.dumps(value)}
            data['executionJob' if execution else 'planJob']=job;proof['execution' if execution else 'plan']=copy.deepcopy(job)
        return data,proof
    def check(self,data,proof,stage):
        with patch('validate_container_acl_unknown.managed_plan',return_value=self.plan):verify_unknown_jobs(data,proof,stage)
    def test_unknown_and_acknowledged_business_changes_preserve_null_historical_receipt(self):
        for stage in ('unknown','acknowledged'):
            data,proof=self.fixture(stage);self.check(data,proof,stage)
            value=json.loads(proof['execution']['resultJson']);self.assertIsNone(value['aclConfirmed']);self.assertEqual(value['receiptState'],'Unknown');self.assertFalse(value['hasPublishedVersion'])
    def test_false_confirmation_wrong_job_or_missing_summary_cannot_pass(self):
        for mode in ('false-confirmation','wrong-id','missing'):
            data,proof=self.fixture('unknown')
            if mode=='wrong-id':proof['execution']['id']=str(uuid.uuid4())
            elif mode=='missing':proof['execution']['resultJson']=None
            else:value=json.loads(proof['execution']['resultJson']);value['aclConfirmed']=False;proof['execution']['resultJson']=json.dumps(value)
            with self.subTest(mode=mode),self.assertRaises(AssertionError):self.check(data,proof,'unknown')
    def test_write_controls_or_missing_null_ui_are_not_safe_disposition(self):
        for field in ('nullConfirmationVisible','noGenericWriteControls'):
            data,proof=self.fixture('acknowledged');proof['taskCenter'][field]=False
            with self.subTest(field=field),self.assertRaises(AssertionError):self.check(data,proof,'acknowledged')


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
