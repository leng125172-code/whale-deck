"""Adversarial proofs use synthetic metadata/SQL only; never a live acceptance."""
import copy
import json
import unittest
import uuid
from acl_history_browser_proof import digest, expected_entry, prove_snapshot, prove_audits, prove_pages

class HistoryBrowserProofTests(unittest.TestCase):
    def setUp(self):
        self.baseline=dict(containerId='a'*64,instanceId='777:2026-11-03T01:02:03Z')
        self.path='work/acl-file.bin';self.capture='a'*32;self.subject='synthetic-subject'
        self.result=dict(executionJobId=str(uuid.UUID(self.capture)),historyDigest='B'*64,command=dict(location=dict(**self.baseline,path=self.path)))
        acl=[dict(tag='user_obj',id=None,permissions=0),dict(tag='user',id=0,permissions=0),dict(tag='group_obj',id=None,permissions=0),dict(tag='mask',id=None,permissions=0),dict(tag='other',id=None,permissions=0)]
        self.metadata=dict(operationId=self.capture,historyDigest='B'*64,createdAtUnixMs=0,plan=dict(command=dict(instanceId=self.baseline['instanceId'],aclType='access'),kind='File',sourceIdentity=[1,2,0o100000],accessAcl=acl,defaultAcl=[]))
        self.entry=expected_entry(self.result,self.metadata)
        self.location=dict(**self.baseline,path=self.path)
        self.snapshot=dict(location=self.location,entry=self.entry,accessAcl=acl,defaultAcl=[])
        endpoint='/api/v1/containers/'+self.baseline['containerId']+'/files/acls/history'
        self.request=dict(method='GET',pathname=endpoint+'/'+self.capture,query=dict(instanceId=self.baseline['instanceId'],path=self.path,expectedHistoryDigest='B'*64),status=200,noStore=True)
        self.signal=dict(subject=self.subject,captureId=self.capture,snapshot=self.snapshot,request=self.request)
        self.page=dict(location=self.location,version='C'*64,entries=[self.entry],nextAfter='',scannedCount=1,invalidCount=0,complete=True)
        self.page_request=dict(method='GET',pathname=endpoint,query=dict(instanceId=self.baseline['instanceId'],path=self.path,after='',take='25',expectedVersion=''),status=200,noStore=True,page=self.page)
        self.expected={self.capture:(self.path,self.entry)}
        self.rows=[]
        for index,action in enumerate(('list','read')):
            self.rows.append(dict(actor_subject=self.subject,target_type='container',target_id=self.baseline['containerId'],result='Validated',job_id=None,
                id=str(uuid.uuid4()),trace_id='synthetic-'+str(index),action='containers.files.acl-history-'+action,
                detail_json=json.dumps(dict(pathHash=digest(self.path),instanceHash=digest(self.baseline['instanceId']),version=('C' if action=='list' else 'B')*64,sizeBytes=None,sha256=None))))
    def test_exact_zero_snapshot_and_real_shape_of_private_audit_are_accepted(self):
        prove_snapshot(self.signal,self.baseline,self.result,self.metadata)
        prove_pages([self.page_request],self.baseline,self.expected)
        prove_audits(self.rows,self.subject,self.baseline,[self.page],[self.snapshot])
    def test_actual_jsonb_objects_and_text_have_identical_strict_audit_semantics(self):
        rows=copy.deepcopy(self.rows)
        for row in rows:row['detail_json']=json.loads(row['detail_json'])
        prove_audits(rows,self.subject,self.baseline,[self.page],[self.snapshot])
        for value in ([],None,False):
            bad=copy.deepcopy(rows);bad[0]['detail_json']=value
            with self.assertRaises(AssertionError):prove_audits(bad,self.subject,self.baseline,[self.page],[self.snapshot])
        bad=copy.deepcopy(self.rows);bad[0]['detail_json']=bad[0]['detail_json'][:-1]+',"version":"C"}'
        with self.assertRaises(AssertionError):prove_audits(bad,self.subject,self.baseline,[self.page],[self.snapshot])
    def test_altered_capture_digest_scope_and_boolean_zero_cannot_be_accepted(self):
        for field,value in (('captureId','b'*32),('historyDigest','D'*64),('originalMode',False),('createdAtUnixMs',False)):
            signal=copy.deepcopy(self.signal);signal['snapshot']['entry'][field]=value
            with self.subTest(field=field),self.assertRaises(AssertionError):prove_snapshot(signal,self.baseline,self.result,self.metadata)
        signal=copy.deepcopy(self.signal);signal['snapshot']['location']['path']='other'
        with self.assertRaises(AssertionError):prove_snapshot(signal,self.baseline,self.result,self.metadata)
    def test_bool_cannot_replace_acl_zero_permissions(self):
        signal=copy.deepcopy(self.signal);signal['snapshot']['accessAcl'][1]['permissions']=False
        with self.assertRaises(AssertionError):prove_snapshot(signal,self.baseline,self.result,self.metadata)
    def test_post_missing_no_store_and_request_body_substitutes_cannot_be_proven(self):
        for change in ({'method':'POST'},{'noStore':False},{'status':404},{'pathname':'/other'},{'query':{'path':'other'}}):
            signal=copy.deepcopy(self.signal);signal['request'].update(change)
            with self.subTest(change=change),self.assertRaises(AssertionError):prove_snapshot(signal,self.baseline,self.result,self.metadata)
    def test_missing_duplicate_foreign_or_private_audit_is_rejected(self):
        variations=[self.rows[:1],[self.rows[0],self.rows[0]]]
        for key,value in (('actor_subject','other'),('target_id','b'*64),('result','Started'),('job_id',str(uuid.uuid4())),('trace_id','')):
            rows=copy.deepcopy(self.rows);rows[0][key]=value;variations.append(rows)
        rows=copy.deepcopy(self.rows);detail=json.loads(rows[1]['detail_json']);detail['accessAcl']=[];rows[1]['detail_json']=json.dumps(detail);variations.append(rows)
        for rows in variations:
            with self.assertRaises(AssertionError):prove_audits(rows,self.subject,self.baseline,[self.page],[self.snapshot])
    def test_audit_path_instance_and_metadata_version_are_independently_bound(self):
        for key in ('pathHash','instanceHash','version'):
            rows=copy.deepcopy(self.rows);detail=json.loads(rows[1]['detail_json']);detail[key]='F'*64;rows[1]['detail_json']=json.dumps(detail)
            with self.subTest(key=key),self.assertRaises(AssertionError):prove_audits(rows,self.subject,self.baseline,[self.page],[self.snapshot])
    def test_incomplete_duplicate_or_wrong_version_pages_are_not_complete_history(self):
        for change in ({'complete':False,'nextAfter':'b'*32},{'entries':[self.entry,self.entry],'scannedCount':2},{'entries':[]},{'scannedCount':201},{'scannedCount':True}):
            request=copy.deepcopy(self.page_request);request['page'].update(change)
            with self.subTest(change=change),self.assertRaises(AssertionError):prove_pages([request],self.baseline,self.expected)

if __name__=='__main__':unittest.main()
