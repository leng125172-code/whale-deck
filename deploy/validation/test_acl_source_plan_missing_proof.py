"""Isolated proof inputs only; no product calls, identity, disk or clock changes."""
import copy
import json
import unittest
import uuid
from acl_source_plan_missing_proof import FROZEN,EMPTY,CODE,require_queued,require_failed,verify_audits,summary
from acl_restore_browser_proof import source_json,expected_metadata


class SourcePlanMissingProofTests(unittest.TestCase):
    def setUp(self):
        source=dict(location=dict(containerId='a'*64,instanceId='123:owned',path='work/acl-file.bin'),entry=dict(captureId=uuid.uuid4().hex,
            historyDigest='D'*64,createdAtUnixMs=1,sourceInstanceId='123:owned',changedAclType='access',kind='File',originalMode=416,
            accessEntryCount=3,defaultEntryCount=0),accessAcl=[dict(tag=t,id=None,permissions=p) for t,p in [('user_obj',6),('group_obj',4),('other',0)]],defaultAcl=[])
        self.baseline=dict(captureIntentId=str(uuid.uuid4()),subject='own-subject',containerId='a'*64,instanceId='123:owned',restoreSource=source)
        row={key:'original-'+key for key in FROZEN};row.update({key:None for key in EMPTY})
        row.update(id=str(uuid.uuid4()),state='Queued',version=0,actor_subject='own-subject',container_id='a'*64,instance_id='123:owned',
            relative_path='work/acl-file.bin',restore_source_json=json.dumps(source_json(source)),command_json=json.dumps(dict(Location=source_json(source)['Location'],
            AclType='access',Entries=source_json(source)['AccessAcl'],ExpectedVersion='original-version')),completed_at_utc=None,result_code=None,
            lease_token=None,lease_expires_at_utc=None,publish_barrier=False,history_may_exist=False,history_verified=False)
        self.queued=dict(row=row,executionJob=None,pending=1,accepted=1,confirmed=0,planJob=dict(id=row['id'],job_type='containers.files.acl-plan',state='Queued',result_json=None))
        self.final=dict(row={**row,'state':'Failed','version':3,'completed_at_utc':'completed','result_code':CODE},executionJob=None,pending=0,accepted=1,confirmed=0,
            planJob=dict(id=row['id'],job_type='containers.files.acl-plan',state='Failed',result_json=json.dumps(summary(row['id']))))

    def test_exact_new_queued_and_failed_plan_without_execution(self):require_queued(self.queued,self.baseline);require_failed(self.final,self.queued)

    def test_selected_source_wrong_container_actor_instance_or_command_refused(self):
        for key,value in [('actor_subject','other'),('container_id','b'*64),('instance_id','other'),('relative_path','work/other'),('restore_source_json','{}'),('command_json','{}')]:
            changed=copy.deepcopy(self.queued);changed['row'][key]=value
            with self.subTest(key=key),self.assertRaises((AssertionError,KeyError)):require_queued(changed,self.baseline)

    def test_ready_pending_confirmed_or_started_cannot_be_reported_as_no_plan(self):
        for key in EMPTY:
            changed=copy.deepcopy(self.final);changed['row'][key]='present'
            with self.subTest(key=key),self.assertRaises(AssertionError):require_failed(changed,self.queued)
        for key,value in [('executionJob',{'state':'Failed'}),('confirmed',1),('pending',1)]:
            with self.subTest(key=key),self.assertRaises(AssertionError):require_failed({**self.final,key:value},self.queued)

    def test_frozen_identity_source_command_trace_and_expiry_cannot_change(self):
        for key in FROZEN:
            changed=copy.deepcopy(self.final);changed['row'][key]='modified'
            with self.subTest(key=key),self.assertRaises(AssertionError):require_failed(changed,self.queued)

    def test_cancel_expired_wrong_failure_or_live_lease_refused(self):
        for key,value in [('state','Canceled'),('state','Expired'),('result_code','CONTAINER_ACL_VERSION_CONFLICT'),('lease_token','live'),
                          ('lease_expires_at_utc','expiry'),('publish_barrier',True),('history_may_exist',True),('history_verified',True)]:
            changed=copy.deepcopy(self.final);changed['row'][key]=value
            with self.subTest(key=key,value=value),self.assertRaises(AssertionError):require_failed(changed,self.queued)

    def test_summary_must_keep_unproved_not_false_or_aborted(self):
        for key,value in [('aclConfirmed',False),('bindingCurrent',False),('receiptState','Aborted'),('kind','File'),('targetMode',416),('noChange',False),('stage','execution')]:
            changed=copy.deepcopy(self.final);s=json.loads(changed['planJob']['result_json']);s[key]=value;changed['planJob']['result_json']=json.dumps(s)
            with self.subTest(key=key),self.assertRaises(AssertionError):require_failed(changed,self.queued)

    def test_audits_exactly_original_accepted_and_failed_plan_with_source_hash(self):
        row=self.final['row'];rows=[dict(action='containers.files.acl.restore.'+action,result=result,job_id=row['id'],actor_subject=row['actor_subject'],
            target_id=row['container_id'],target_type='container',trace_id='trace',detail_json=expected_metadata(row)) for action,result in [('plan-accepted','Accepted'),('plan-result','Failed')]]
        verify_audits(rows,row)
        for key,value in [('actor_subject','other'),('result','Ready'),('job_id','execution'),('action','containers.files.acl.restore.confirmed')]:
            changed=copy.deepcopy(rows);changed[-1][key]=value
            with self.subTest(key=key),self.assertRaises(AssertionError):verify_audits(changed,row)
        changed=copy.deepcopy(rows);changed[-1]['detail_json']['restoreSourceHash']='wrong'
        with self.assertRaises(AssertionError):verify_audits(changed,row)
        with self.assertRaises(AssertionError):verify_audits(rows+[rows[-1]],row)

    def test_only_unclaimed_single_queued_registration_may_enter_fault_cut(self):
        for key,value in [('pending',0),('pending',2),('accepted',0),('confirmed',1)]:
            with self.subTest(key=key,value=value),self.assertRaises(AssertionError):require_queued({**self.queued,key:value},self.baseline)
        for key,value in [('state','Planning'),('version',1),('lease_token','live')]:
            changed=copy.deepcopy(self.queued);changed['row'][key]=value
            with self.subTest(key=key),self.assertRaises(AssertionError):require_queued(changed,self.baseline)


if __name__=='__main__':unittest.main()
