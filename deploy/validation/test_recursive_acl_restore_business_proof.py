"""Synthetic SQL/C# projections + real temporary ACL records, never installed UI/SQL."""
import copy
import json
import os
import re
import unittest
import uuid
import test_recursive_acl_restore_record_probe_scope as native
from test_recursive_acl_restore_agent_probe_scope import synthetic, wire
from recursive_acl_restore_business_proof import managed_plan,managed_receipt,private_json,database_proof,metadata,records_bound_to_business

NUMBERS={'device','inode','linkCount','sizeBytes','modifiedAtUnixNs','changedAtUnixNs','rootChangedAtUnixNs','createdAtUnixMs'}
def managed(value,key=''):
    if type(value) is dict:return {k[0].upper()+k[1:]:managed(v,k) for k,v in value.items()}
    if type(value) is list:return [managed(v) for v in value]
    return int(value) if key in NUMBERS and type(value) is str and re.fullmatch('-?[0-9]+',value) else value

def api_receipt(value):
    item=copy.deepcopy(value);item['id']=str(uuid.UUID(item.pop('operationId')));return item

class BusinessJsonTests(unittest.TestCase):
    def test_full_csharp_frame_retains_uint64_and_signed_nanoseconds(self):
        plan=synthetic();plan['rootIdentity'][0]=18446744073709551615;plan['restoreSourceFrame'][7]=-9223372036854775808
        self.assertEqual(plan,managed_plan(json.dumps(managed(wire(plan)))))
    def test_managed_numbers_cannot_be_coerced_from_proto_strings_or_booleans(self):
        for path in [('RootIdentity','Inode'),('RestoreSourceFrame','ModifiedAtUnixNs'),('RestoreSource','CreatedAtUnixMs'),('EntryCount',)]:
            for bad in (True,'0',0.0):
                value=managed(wire(synthetic()));target=value
                for key in path[:-1]:target=target[key]
                target[path[-1]]=bad
                with self.subTest(path=path,bad=bad),self.assertRaises(AssertionError):managed_plan(value)
    def test_duplicate_alias_missing_and_extra_fields_cannot_hide_in_projection(self):
        with self.assertRaisesRegex(AssertionError,'DUPLICATE'):private_json('{"State":1,"State":2}')
        with self.assertRaisesRegex(AssertionError,'NONFINITE'):private_json('{"State":NaN}')
        with self.assertRaisesRegex(AssertionError,'LIMIT'):private_json(' '*65537)
        original=managed(wire(synthetic()))
        for name in original:
            value=copy.deepcopy(original);del value[name]
            with self.subTest(name=name),self.assertRaises(AssertionError):managed_plan(value)
        value=copy.deepcopy(original);value['RootIdentity']['ownerUid']=0
        with self.assertRaisesRegex(AssertionError,'AMBIGUOUS'):managed_plan(value)
        value=copy.deepcopy(original);value['Command']['Entries']=[]
        with self.assertRaises(AssertionError):managed_plan(value)

@unittest.skipUnless(os.geteuid()==0,'real temporary root-private ACL fixtures required')
class BusinessTemporaryRecordsTests(unittest.TestCase):
    def setUp(self):
        self.fixture=native.RestoreRecordProofTests();self.fixture.setUp();self.addCleanup(self.fixture.doCleanups)
        # Use the real gate's fixed scope without relaxing the proof's path fence.
        k=self.fixture.k;work=k.b.base.root/'work';work.mkdir();target=work/'recursive-tree';k.b.tree.rename(target);k.b.tree=target
        old_source=k.b.command;old_restore=k.command
        def relocate(value):
            return {**value,'path':'work/recursive-tree','expectedVersion':k.b.base.r.version(k.b.base.session.request,'work/recursive-tree',target.stat())}
        k.b.command=lambda kind='access',directory=None:relocate(old_source(kind,directory))
        k.command=lambda source,result:relocate(old_restore(source,result))
    def projection(self,mode='access',native_case=None):
        source,frame,item,bundle=self.fixture.case(mode) if native_case is None else native_case;request=item['request'];plan=request['plan'];receipt=item['result'];operation=str(uuid.UUID(request['operationId']));intent=str(uuid.uuid4());key=str(uuid.uuid4())
        command=plan['command'];command=dict(location={k:command[k] for k in ('containerId','instanceId','path')},**{k:command[k] for k in ('captureId','expectedHistoryDigest','expectedVersion')})
        row=dict(id=intent,idempotency_key=key,actor_subject='proof-admin',container_id=command['location']['containerId'],instance_id=command['location']['instanceId'],relative_path=command['location']['path'],state='Succeeded',completed_at_utc='2026-11-05T00:00:00Z',publish_barrier=False,lease_token=None,lease_expires_at_utc=None,version=5,login_binding='A'*64,owner_hash=request['owner'],request_hash='B'*64,plan_hash='C'*64,plan_json=json.dumps(managed(wire(plan))),command_json=json.dumps(managed(command)),execution_job_id=operation,execution_hash='D'*64,root_started_at_utc='2026-11-05T00:00:00Z',receipt_hash='E'*64,receipt_json=json.dumps(managed(api_receipt(receipt))),root_state='Completed')
        for field in ('completedCount','changedCount','abortedCount','notStartedCount','unknownCount','outcomeDigest','historyMayExist','historyVerified','historyDigest','historyAvailable','bindingCurrent'):row[re.sub(r'(?<!^)(?=[A-Z])','_',field).lower()]=receipt[field]
        result=dict(id=intent,planningJobId=intent,executionJobId=operation,command=command,state='Succeeded',confirmation=None,version=5,restoreSource=plan['restoreSource'],manifestDigest=plan['manifestDigest'],receipt=api_receipt(receipt),rootUid=plan['rootIdentity'][3],rootGid=plan['rootIdentity'][4],rootMode=plan['rootIdentity'][2],executorUid=plan['uid'],executorGid=plan['gid'])
        for field in ('entryCount','fileCount','directoryCount','changeCount'):result[field]=plan[field]
        for field in ('historyMayExist','historyVerified','historyDigest'):result[field]=receipt[field]
        details=json.dumps(metadata(row));p=dict(id=intent,actor_subject='proof-admin',state='Succeeded',job_type='containers.files.recursive-acl-restore-plan',idempotency_key=key,request_json=details);e=dict(id=operation,actor_subject='proof-admin',state='Succeeded',job_type='containers.files.recursive-acl-restore',idempotency_key=operation,request_json=details)
        actions=[('plan-accepted','Accepted',intent),('plan-result','Ready',intent),('confirmed','Accepted',operation),('result','Succeeded',operation)]
        audits=[dict(id=str(uuid.uuid4()),actor_subject='proof-admin',target_type='container',target_id=row['container_id'],action='containers.files.recursive-acl-restore.'+action,result=status,job_id=job,trace_id='test-trace',detail_json=details) for action,status,job in actions]
        data=dict(row=row,planJob=p,executionJob=e,pending=0,accepted=1,confirmed=1,events=4,audits=audits);baseline=dict(containerId=row['container_id'],instanceId=row['instance_id'])
        return source,frame,item,bundle,data,result,baseline
    def prove(self,data,result,baseline):return database_proof(data,result,baseline,'proof-admin')
    def test_access_and_default_bind_csharp_sql_to_actual_children_and_hdd(self):
        for mode in ['access','default']:
            self.fixture.archive=self.fixture.k.state.parent/('business-'+mode);self.fixture.archive.mkdir(mode=0o700)
            source,frame,item,bundle,data,result,baseline=self.projection(mode)
            with self.subTest(mode=mode):self.assertEqual(4,records_bound_to_business(self.prove(data,result,baseline),bundle,source,frame,item['before'],item['after'],self.fixture.attrs))
    def test_actual_noop_keeps_new_job_domain_all_children_and_source_original_count(self):
        source,frame,_,_=self.fixture.case();k=self.fixture.k;before=k.b.snapshot();request=k.request(source['request'],source['result']);reply=k.engine.apply(request,k.b.base.session)
        item=dict(request=request,result=reply,before=before,after=k.b.snapshot())
        bundle,_=native.read_bundle(self.fixture.io,request,True,self.fixture.archive/'business-noop',create=True)
        _,_,_,_,data,result,baseline=self.projection(native_case=(source,frame,item,bundle));self.assertEqual(0,result['changeCount']);self.assertGreater(result['restoreSource']['changeCount'],0);self.assertEqual(before,item['after'])
        self.assertEqual(4,records_bound_to_business(self.prove(data,result,baseline),bundle,source,frame,before,item['after'],self.fixture.attrs))
    def test_cancel_projection_is_not_a_proof_that_root_files_are_absent(self):
        source,frame,item,bundle,data,result,baseline=self.projection();row=data['row'];row.update(state='Canceled',execution_job_id=None,execution_hash=None,root_started_at_utc=None,cancel_requested_at_utc='2026-11-05T00:00:00Z',receipt_json=None,receipt_hash=None,root_state=None,history_digest=None,history_verified=False,history_may_exist=False)
        data.update(executionJob=None,confirmed=0);result.update(state='Canceled',executionJobId=None,receipt=None,historyVerified=False,historyMayExist=False,historyDigest=None)
        data['audits']=[a for a in data['audits'] if a['action'].endswith(('plan-accepted','plan-result'))]
        data['audits'].append(dict(id=str(uuid.uuid4()),actor_subject='proof-admin',target_type='container',target_id=row['container_id'],action='containers.files.recursive-acl-restore.cancel-requested',result='Accepted',job_id=row['id'],trace_id='test-trace',detail_json=json.dumps(metadata(row))))
        proof=self.prove(data,result,baseline);self.assertTrue(proof['canceled']);self.assertIsNone(proof['request'])
        with self.assertRaises(AssertionError):records_bound_to_business(proof,bundle,source,frame,item['before'],item['after'],self.fixture.attrs)
    def test_real_jsonb_projection_objects_and_text_metadata_bind_identically(self):
        _,_,_,_,data,result,baseline=self.projection();expected=self.prove(data,result,baseline)
        for job in (data['planJob'],data['executionJob']):job['request_json']=json.loads(job['request_json'])
        for audit in data['audits']:audit['detail_json']=json.loads(audit['detail_json'])
        self.assertEqual(expected,self.prove(data,result,baseline))
        data['audits'][0]['detail_json']['extra']='private'
        with self.assertRaises(AssertionError):self.prove(data,result,baseline)
    def test_job_receipt_scope_count_and_metadata_tampering_fail_closed(self):
        _,_,_,_,data,result,baseline=self.projection()
        for name in ['actor','container','job','pending','receipt-id','count','source','audit-extra','metadata-extra','receipt-MAC-missing']:
            changed=copy.deepcopy(data);response=copy.deepcopy(result)
            if name=='actor':changed['row']['actor_subject']='another'
            elif name=='container':changed['row']['container_id']='b'*64
            elif name=='job':changed['executionJob']['id']=str(uuid.uuid4())
            elif name=='pending':changed['pending']=1
            elif name=='receipt-id':response['receipt']['id']=str(uuid.uuid4())
            elif name=='count':response['receipt']['changedCount']=0
            elif name=='source':response['restoreSource']['changeCount']+=1
            elif name=='audit-extra':changed['audits'].append(copy.deepcopy(changed['audits'][0]))
            elif name=='metadata-extra':changed['planJob']['request_json']=json.dumps({**metadata(changed['row']),'path':'private'})
            else:changed['row']['receipt_hash']=None
            with self.subTest(name=name),self.assertRaises(AssertionError):self.prove(changed,response,baseline)
    def test_self_consistent_changed_source_cannot_override_original_record_proof(self):
        source,frame,item,bundle,data,result,baseline=self.projection();proof=self.prove(data,result,baseline)
        proof['plan']['restoreSource']['changeCount']+=1
        with self.assertRaises(AssertionError):records_bound_to_business(proof,bundle,source,frame,item['before'],item['after'],self.fixture.attrs)
    def test_receipt_uses_distinct_guid_domain_and_no_scalar_coercion(self):
        _,_,_,_,data,result,_=self.projection();value=managed_receipt(data['row']['receipt_json']);self.assertEqual(value,managed_receipt(result['receipt'],api=True))
        for field,bad in [('id','0'*32),('id',str(uuid.uuid4()).upper()),('changedCount',True),('completedCount','4'),('bindingCurrent',1)]:
            changed=copy.deepcopy(result['receipt']);changed[field]=bad
            with self.subTest(field=field),self.assertRaises(AssertionError):managed_receipt(changed,api=True)

if __name__=='__main__':unittest.main()
