"""Source-missing proof rejects fabricated stages; no product or disk faults."""
import copy
import json
import unittest
from unittest.mock import patch
from validate_container_acl_source_missing import require_canceled, verify_audits, require_ready
from acl_restore_browser_proof import expected_metadata,source_json


class SourceMissingProofTests(unittest.TestCase):
    def setUp(self):
        self.row={k:'original-'+k for k in ('id','actor_subject','login_binding','container_id','instance_id','relative_path','owner_hash',
            'request_hash','command_json','plan_json','plan_hash','restore_source_json','execution_hash','planning_expires_at_utc','ready_expires_at_utc')}
        self.row.update(state='Ready',version=4)
        self.ready=dict(row=self.row,planJob={'state':'Succeeded'},events=2)
        self.final=dict(row={**self.row,'state':'Canceled','result_code':'CONTAINER_ACL_CANCELED','cancel_requested_at_utc':'cancel','completed_at_utc':'completed',
            'execution_job_id':None,'root_started_at_utc':None,'receipt_json':None,'receipt_hash':None,'acl_confirmed':None,'lease_token':None,
            'lease_expires_at_utc':None,'publish_barrier':False,'history_may_exist':False,'history_verified':False,'history_digest':None,'published_version':None,'version':5},
            planJob=self.ready['planJob'],events=2,executionJob=None,pending=0,confirmed=0,accepted=1)

    def test_explicit_cancel_without_execution_preserves_original_bindings(self):require_canceled(self.final,self.ready)

    def test_changed_source_identity_command_plan_or_deadline_refused(self):
        for key in self.row.keys()-{'state','version'}:
            changed=copy.deepcopy(self.final);changed['row'][key]='tamper'
            with self.subTest(key=key),self.assertRaises(AssertionError):require_canceled(changed,self.ready)

    def test_execution_root_receipt_or_wrong_job_state_not_a_confirm_rejection(self):
        for key in ('execution_job_id','root_started_at_utc','receipt_json','receipt_hash','acl_confirmed','lease_token','lease_expires_at_utc'):
            changed=copy.deepcopy(self.final);changed['row'][key]='present'
            with self.subTest(key=key),self.assertRaises(AssertionError):require_canceled(changed,self.ready)
        for key,value in [('confirmed',1),('pending',1),('executionJob',{'state':'Succeeded'}),('planJob',{'state':'Failed'}),('events',3)]:
            with self.subTest(key=key),self.assertRaises(AssertionError):require_canceled({**self.final,key:value},self.ready)

    def audits(self):
        row={**self.row,'restore_source_json':'{"immutable":"source"}'}
        rows=[dict(action='containers.files.acl.restore.'+name,actor_subject=row['actor_subject'],target_id=row['container_id'],target_type='container',job_id=row['id'],
            result=result,trace_id='actual-scope',detail_json=expected_metadata(row)) for name,result in [('plan-accepted','Accepted'),('plan-result','Ready'),('cancel-requested','Accepted')]]
        return row,rows

    def test_audits_require_exact_original_source_hash_and_no_confirmed_execution(self):
        row,rows=self.audits();verify_audits(rows,row)
        changed=copy.deepcopy(rows);changed[-1]['detail_json']['restoreSourceHash']='wrong'
        with self.assertRaises(AssertionError):verify_audits(changed,row)
        for key,value in [('action','containers.files.acl.restore.confirmed'),('actor_subject','other'),('job_id','execution'),('result','Succeeded'),('trace_id','')]:
            changed=copy.deepcopy(rows);changed[-1][key]=value
            with self.subTest(key=key),self.assertRaises(AssertionError):verify_audits(changed,row)
        with self.assertRaises(AssertionError):verify_audits(rows+[rows[-1]],row)

    def test_ready_requires_selected_source_and_zero_execution_facts(self):
        source=dict(location={'containerId':'cid','instanceId':'instance','path':'work/acl-file.bin'},entry=dict(captureId='capture',historyDigest='digest',createdAtUnixMs=1,
            sourceInstanceId='instance',changedAclType='access',kind='File',originalMode=416,accessEntryCount=0,defaultEntryCount=0),accessAcl=[],defaultAcl=[])
        baseline=dict(captureIntentId='capture-intent',subject='subject',containerId='cid',instanceId='instance',restoreSource=source,original={'acl-file.bin':{'identity':[1]*9}})
        row={**self.row,'actor_subject':'subject','container_id':'cid','instance_id':'instance','relative_path':'work/acl-file.bin',
            'execution_job_id':None,'root_started_at_utc':None,'completed_at_utc':None,'lease_token':None,'lease_expires_at_utc':None,'publish_barrier':False,
            'acl_confirmed':None,'history_digest':None,'receipt_json':None,'receipt_hash':None,'published_version':None,'history_may_exist':False,'history_verified':False,
            'restore_source_json':json.dumps(source_json(source)),'plan_json':'{}'}
        data=dict(row=row,planJob={'state':'Succeeded'},executionJob=None,pending=0,confirmed=0,accepted=1)
        plan=dict(command=dict(entries=[],aclType='access'),noChange=False,sourceIdentity=[1]*9)
        with patch('validate_container_acl_source_missing.acl.managed_plan',return_value=plan):
            require_ready(data,baseline)
            for key,value in [('execution_job_id','execution'),('state','Pending'),('restore_source_json','{}'),('root_started_at_utc','started')]:
                changed=copy.deepcopy(data);changed['row'][key]=value
                with self.subTest(key=key),self.assertRaises(AssertionError):require_ready(changed,baseline)


if __name__=='__main__':unittest.main()
