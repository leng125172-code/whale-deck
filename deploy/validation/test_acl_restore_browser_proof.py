"""Negative source/new-backup/audit proof boundaries; synthetic inputs, no live claims."""
import copy
import json
import unittest
import uuid
from acl_restore_browser_proof import prove_source,prove_audits,source_json,expected_metadata,restore_spec

class RestoreBrowserProofTests(unittest.TestCase):
    def setUp(self):
        self.location=dict(containerId='a'*64,instanceId='777:2026-11-03T01:02:03Z',path='work/acl-file.bin')
        acl=[dict(tag='user_obj',id=None,permissions=0),dict(tag='user',id=0,permissions=0),dict(tag='group_obj',id=None,permissions=0),dict(tag='mask',id=None,permissions=0),dict(tag='other',id=None,permissions=0)]
        self.source=dict(location=self.location,entry=dict(captureId='b'*32,historyDigest='C'*64,createdAtUnixMs=0,sourceInstanceId=self.location['instanceId'],changedAclType='access',kind='File',originalMode=0,accessEntryCount=5,defaultEntryCount=0),accessAcl=acl,defaultAcl=[])
        intent=str(uuid.uuid4());execution=str(uuid.uuid4())
        self.row=dict(id=intent,execution_job_id=execution,container_id=self.location['containerId'],instance_id=self.location['instanceId'],relative_path=self.location['path'],actor_subject='synthetic-admin',restore_source_json=json.dumps(source_json(self.source)),history_digest='D'*64)
        self.result=dict(name='restore-root',id=intent,executionJobId=execution,command=dict(aclType='access',entries=copy.deepcopy(acl)),noChange=False)
    def audits(self,cancel=False):
        row=self.row;out=[]
        for action,status,job in [('plan-accepted','Accepted',row['id']),('plan-result','Ready',row['id']),*([('cancel-requested','Accepted',row['id'])] if cancel else [('confirmed','Accepted',row['execution_job_id']),('result','Succeeded',row['execution_job_id'])])]:
            out.append(dict(action='containers.files.acl.restore.'+action,actor_subject=row['actor_subject'],target_type='container',target_id=row['container_id'],job_id=job,result=status,trace_id='synthetic-trace',detail_json=expected_metadata(row)))
        return out
    def test_explicit_root_zero_epoch_zero_and_empty_default_are_preserved(self):
        prove_source(self.row,self.source,self.result);prove_audits(self.audits(),self.row)
    def test_incorrect_scope_capture_acl_metadata_or_private_fields_are_rejected(self):
        for section,key,value in [('Location','ContainerId','f'*64),('Location','InstanceId','other'),('Location','Path','work/other'),('Entry','CaptureId','d'*32),('Entry','HistoryDigest','D'*64),('Entry','OriginalMode',False),('Entry','CreatedAtUnixMs',False),('Entry','AccessEntryCount',True),('Entry','SourceInstanceId','other'),('Entry','ChangedAclType','default'),('Entry','Owner','private')]:
            row=copy.deepcopy(self.row);body=json.loads(row['restore_source_json']);body[section][key]=value;row['restore_source_json']=json.dumps(body)
            with self.subTest(section=section,key=key),self.assertRaises(AssertionError):prove_source(row,self.source,self.result)
    def test_missing_fields_or_duplicate_source_json_do_not_count_as_canonical(self):
        for field in ('Location','Entry','AccessAcl','DefaultAcl'):
            row=copy.deepcopy(self.row);body=json.loads(row['restore_source_json']);del body[field];row['restore_source_json']=json.dumps(body)
            with self.subTest(field=field),self.assertRaises(AssertionError):prove_source(row,self.source,self.result)
        row=copy.deepcopy(self.row);row['restore_source_json']=row['restore_source_json'][:-1]+',"Entry":{}}'
        with self.assertRaises(AssertionError):prove_source(row,self.source,self.result)
    def test_old_capture_is_never_a_new_intent_or_execution(self):
        for field in ('id','execution_job_id'):
            row=copy.deepcopy(self.row);result=copy.deepcopy(self.result);row[field]=str(uuid.UUID(self.source['entry']['captureId']));result['id' if field=='id' else 'executionJobId']=row[field]
            with self.subTest(field=field),self.assertRaises(AssertionError):prove_source(row,self.source,result)
    def test_changed_target_entries_type_and_boolean_noop_cannot_be_accepted(self):
        for field in ('entries','aclType','noChange'):
            result=copy.deepcopy(self.result)
            if field=='entries':result['command']['entries'][0]['permissions']=7
            elif field=='aclType':result['command']['aclType']='default'
            else:result['noChange']=0
            with self.subTest(field=field),self.assertRaises(AssertionError):prove_source(self.row,self.source,result)
    def test_current_acl_backup_cannot_be_claimed_using_selected_old_digest(self):
        row=copy.deepcopy(self.row);row['history_digest']=self.source['entry']['historyDigest']
        with self.assertRaises(AssertionError):prove_source(row,self.source,self.result)
    def test_audits_are_complete_unique_authorized_and_bound_to_new_jobs(self):
        rows=self.audits();variations=[rows[:-1],rows+[rows[0]],rows[1:]+[rows[1]]]
        for field,value in [('actor_subject','other'),('job_id',str(uuid.uuid4())),('target_id','b'*64),('target_type','host'),('result','Ready'),('trace_id','')]:
            changed=copy.deepcopy(rows);changed[-1][field]=value;variations.append(changed)
        for changed in variations:
            with self.assertRaises(AssertionError):prove_audits(changed,self.row)
    def test_audit_metadata_rejects_raw_acl_source_digests_or_untyped_restore_flags(self):
        for field,value in [('historyRestore',1),('restoreSourceHash',self.source['entry']['historyDigest']),('pathHash','0'*64),('instanceHash','0'*64),('captureId',self.source['entry']['captureId']),('accessAcl',[])]:
            changed=self.audits();changed[-1]['detail_json'][field]=value
            with self.subTest(field=field),self.assertRaises(AssertionError):prove_audits(changed,self.row)
    def test_canceled_preflight_requires_three_audits_and_no_execution_confirmation(self):
        self.row['execution_job_id']=None;prove_audits(self.audits(cancel=True),self.row)
        with self.assertRaises(AssertionError):prove_audits(self.audits(cancel=True)+[self.audits()[0]],self.row)
    def test_unknown_scenario_cannot_skip_required_cases(self):
        for name in ('access','restore','restore-root-extra','restore-root\n'):
            with self.subTest(name=name),self.assertRaises(AssertionError):restore_spec(name)

if __name__=='__main__':unittest.main()
