"""Negative provenance checks; no service, credentials or real restoration."""
import copy
import json
from pathlib import Path
import unittest
import uuid
from unittest.mock import patch

from acl_restore_fault_proof import prove_binding, RESTORE_SOURCES, restore_fault_version, verify_restore_audits
from acl_restore_browser_proof import source_json, expected_metadata
from verify_container_acl_faults import validate_source_profile, SOURCES, verify_current_admin, verify_restart
from validate_container_acl_faults import RESTART_KEYS


class RestoreFaultProvenanceTests(unittest.TestCase):
    def setUp(self):
        basic=[dict(tag='user_obj',id=None,permissions=6),dict(tag='group_obj',id=None,permissions=4),dict(tag='other',id=None,permissions=0)]
        current=[*basic,dict(tag='user',id=2201,permissions=6)]
        old=str(uuid.uuid4());self.proof=dict(result=dict(id=str(uuid.uuid4()),executionJobId=old),
            before={'acl-file.bin':{'access':basic}},after={'acl-file.bin':{'access':current}})
        self.source=dict(location=dict(containerId='a'*64,instanceId='100:owned',path='work/acl-file.bin'),
            entry=dict(captureId=uuid.UUID(old).hex,historyDigest='D'*64,createdAtUnixMs=1700000000000,
                sourceInstanceId='100:owned',changedAclType='access',kind='File',originalMode=416,accessEntryCount=3,defaultEntryCount=0),accessAcl=basic,defaultAcl=[])
        self.baseline=dict(aclRestoreFaultVersion=1,kind='cancel',original=self.proof['after'])
        self.row=dict(id=str(uuid.uuid4()),execution_job_id=str(uuid.uuid4()),container_id='a'*64,
            instance_id='100:owned',relative_path='work/acl-file.bin',restore_source_json=json.dumps(source_json(self.source)),history_digest='E'*64,
            plan_json=json.dumps(dict(Command=dict(AclType='access',Entries=source_json(self.source)['AccessAcl']),NoChange=False)))

    def check(self):return prove_binding(self.row,self.baseline,self.source,self.proof)

    def test_new_operation_and_distinct_selected_and_current_preimage(self):
        self.assertEqual(self.check(),self.source['accessAcl'])
        self.baseline['kind']='logout';self.check()

    def test_original_capture_or_execution_cannot_be_reused(self):
        for key,value in [('id',self.proof['result']['id']),('execution_job_id',self.proof['result']['executionJobId'])]:
            with self.subTest(key=key),self.assertRaises(AssertionError):prove_binding({**self.row,key:value},self.baseline,self.source,self.proof)

    def test_foreign_container_instance_or_path_refused(self):
        for key,value in [('container_id','b'*64),('instance_id','101:other'),('relative_path','work/other')]:
            with self.subTest(key=key),self.assertRaises(AssertionError):prove_binding({**self.row,key:value},self.baseline,self.source,self.proof)

    def test_wrong_capture_or_type_not_a_restore_source(self):
        for key,value in [('captureId',uuid.uuid4().hex),('changedAclType','default')]:
            source=copy.deepcopy(self.source);source['entry'][key]=value
            with self.subTest(key=key),self.assertRaises(AssertionError):prove_binding(self.row,self.baseline,source,self.proof)

    def test_sql_source_tamper_even_with_same_target_refused(self):
        for key,value in [('historyDigest','F'*64),('createdAtUnixMs',1),('originalMode',0)]:
            source=copy.deepcopy(self.source);source['entry'][key]=value
            with self.subTest(key=key),self.assertRaises(AssertionError):prove_binding(self.row,self.baseline,source,self.proof)

    def test_target_cannot_be_current_or_noop(self):
        for key,value in [('NoChange',True),('Command',dict(AclType='access',Entries=source_json({**self.source,'accessAcl':[]})['AccessAcl']))]:
            plan=json.loads(self.row['plan_json']);plan[key]=value
            with self.subTest(key=key),self.assertRaises(AssertionError):prove_binding({**self.row,'plan_json':json.dumps(plan)},self.baseline,self.source,self.proof)

    def test_wrong_current_inventory_or_reused_hdd_digest_refused(self):
        with self.assertRaises(AssertionError):prove_binding(self.row,{**self.baseline,'original':self.proof['before']},self.source,self.proof)
        with self.assertRaises(AssertionError):prove_binding({**self.row,'history_digest':self.source['entry']['historyDigest']},self.baseline,self.source,self.proof)

    def test_unknown_mode_or_kind_does_not_enter_controlled_cut(self):
        for delta in ({'aclRestoreFaultVersion':2},{'aclRestoreFaultVersion':True},{'kind':'agent-restart'}):
            with self.subTest(delta=delta),self.assertRaises(AssertionError):prove_binding(self.row,{**self.baseline,**delta},self.source,self.proof)

    def test_source_profile_requires_explicit_mode_and_all_sources(self):
        p=dict(schemaVersion=7,aclRestoreFaultVersion=1,sha256={n:'digest' for n in set(SOURCES)|set(RESTORE_SOURCES)})
        validate_source_profile(p,['cancel','logout'])
        for delta in ({'aclRestoreFaultVersion':0},{'aclRestoreFaultVersion':True},{'sha256':{n:'digest' for n in SOURCES}}):
            with self.subTest(delta=list(delta)),self.assertRaises(AssertionError):validate_source_profile({**p,**delta},['cancel'])
        with self.assertRaises(AssertionError):validate_source_profile(p,['worker-crash'])

    def test_both_restore_profiles_reach_source_verification_not_ordinary_acl(self):
        from validate_container_acl_faults import expected_fault_acl
        for version,kind in ((1,'cancel'),(2,'group-removal'),(2,'account-disable'),(3,'worker-crash'),(3,'agent-restart'),(4,'post-acl-cancel'),(4,'post-acl-worker-crash'),(5,'post-acl-group-removal'),(5,'post-acl-account-disable'),(6,'post-acl-agent-restart')):
            baseline={**self.baseline,'aclRestoreFaultVersion':version,'kind':kind}
            with self.subTest(version=version,kind=kind),patch('acl_restore_fault_proof.verify_restore_binding',return_value=['selected-source']) as check:
                self.assertEqual(expected_fault_acl(self.row,baseline,None,None),['selected-source'])
                check.assert_called_once_with(self.row,baseline,None,None)
        with patch('acl_restore_fault_proof.verify_restore_binding') as check:
            with self.assertRaises(AssertionError):expected_fault_acl(self.row,{**self.baseline,'aclRestoreFaultVersion':2},None,None)
            check.assert_not_called()

    def test_revocation_binding_requires_matching_explicit_profile(self):
        for kind in ('group-removal','account-disable'):
            with self.subTest(kind=kind):
                self.assertEqual(prove_binding(self.row,{**self.baseline,'aclRestoreFaultVersion':2,'kind':kind},self.source,self.proof),self.source['accessAcl'])
                with self.assertRaises(AssertionError):prove_binding(self.row,{**self.baseline,'kind':kind},self.source,self.proof)

    def test_profile_selection_rejects_mixed_unknown_empty_or_duplicate_cases(self):
        self.assertEqual(restore_fault_version(('cancel','logout')),1)
        self.assertEqual(restore_fault_version(('group-removal','account-disable')),2)
        self.assertEqual(restore_fault_version(('worker-crash','agent-restart')),3)
        self.assertEqual(restore_fault_version(('post-acl-cancel','post-acl-worker-crash')),4)
        self.assertEqual(restore_fault_version(('post-acl-group-removal','post-acl-account-disable')),5)
        self.assertEqual(restore_fault_version(('post-acl-agent-restart',)),6)
        for kinds in ((),('cancel','group-removal'),('post-acl-unknown',),('cancel','cancel'),('agent-restart','post-acl-agent-restart')):
            with self.subTest(kinds=kinds),self.assertRaises(AssertionError):restore_fault_version(kinds)

    def test_revocation_schema_cannot_be_mislabeled_as_cancel_or_ordinary_fault(self):
        p=dict(schemaVersion=8,aclRestoreFaultVersion=2,sha256={n:'digest' for n in set(SOURCES)|set(RESTORE_SOURCES)})
        validate_source_profile(p,['group-removal','account-disable'])
        for delta,kinds in [({'schemaVersion':7},['group-removal']),({'aclRestoreFaultVersion':1},['group-removal']),({},['cancel']),({'aclRestoreFaultVersion':True},['account-disable'])]:
            with self.subTest(delta=delta,kinds=kinds),self.assertRaises(AssertionError):validate_source_profile({**p,**delta},kinds)
        with self.assertRaises(AssertionError):validate_source_profile(dict(schemaVersion=6,aclRestoreFaultVersion=2,sha256={n:'digest' for n in SOURCES}),['group-removal'])

    def test_restart_profiles_require_new_schema_and_full_source_proof(self):
        p=dict(schemaVersion=9,aclRestoreFaultVersion=3,sha256={n:'digest' for n in set(SOURCES)|set(RESTORE_SOURCES)})
        validate_source_profile(p,['worker-crash','agent-restart'])
        for kind in ('worker-crash','agent-restart'):
            self.assertEqual(prove_binding(self.row,{**self.baseline,'aclRestoreFaultVersion':3,'kind':kind},self.source,self.proof),self.source['accessAcl'])
        for delta,kinds in [({'schemaVersion':8},['worker-crash']),({'aclRestoreFaultVersion':2},['agent-restart']),({},['cancel']),({},['group-removal'])]:
            with self.subTest(delta=delta,kinds=kinds),self.assertRaises(AssertionError):validate_source_profile({**p,**delta},kinds)

    def audit_fixture(self):
        row={**self.row,'actor_subject':'original-subject'}
        events=[('plan-accepted','Accepted',row['id']),('plan-result','Ready',row['id']),('confirmed','Accepted',row['execution_job_id']),('result','Failed',row['execution_job_id'])]
        rows=[dict(actor_subject=row['actor_subject'],target_type='container',target_id=row['container_id'],action='containers.files.acl.restore.'+action,result=result,job_id=job,trace_id='fixture-trace',detail_json=expected_metadata(row)) for action,result,job in events]
        return row,rows,{**self.baseline,'aclRestoreFaultVersion':3,'kind':'worker-crash'}

    def test_restart_audit_must_report_failed_not_canceled_or_success(self):
        row,rows,baseline=self.audit_fixture();verify_restore_audits(rows,row,baseline)
        for state in ('Canceled','Succeeded','Unknown'):
            changed=copy.deepcopy(rows);changed[-1]['result']=state
            with self.subTest(state=state),self.assertRaises(AssertionError):verify_restore_audits(changed,row,baseline)

    def test_wrong_actor_operation_source_hash_or_audit_prefix_cannot_pass(self):
        row,rows,baseline=self.audit_fixture()
        for key,value in [('actor_subject','other'),('job_id',row['id']),('action','result'),('trace_id','')]:
            changed=copy.deepcopy(rows);changed[-1][key]=value
            with self.subTest(key=key),self.assertRaises(AssertionError):verify_restore_audits(changed,row,baseline)
        changed=copy.deepcopy(rows);changed[-1]['detail_json']['restoreSourceHash']='wrong-source'
        with self.assertRaises(AssertionError):verify_restore_audits(changed,row,baseline)
        with self.assertRaises(AssertionError):verify_restore_audits(rows+[rows[-1]],row,baseline)

    def test_post_write_schema_is_distinct_and_still_binds_original_capture(self):
        p=dict(schemaVersion=10,aclRestoreFaultVersion=4,sha256={n:'digest' for n in set(SOURCES)|set(RESTORE_SOURCES)})
        validate_source_profile(p,['post-acl-cancel','post-acl-worker-crash'])
        for kind in ('post-acl-cancel','post-acl-worker-crash'):
            self.assertEqual(prove_binding(self.row,{**self.baseline,'aclRestoreFaultVersion':4,'kind':kind},self.source,self.proof),self.source['accessAcl'])
        for delta,kinds in [({'schemaVersion':9},['post-acl-cancel']),({'aclRestoreFaultVersion':3},['post-acl-worker-crash']),({},['cancel']),({},['worker-crash'])]:
            with self.subTest(delta=delta,kinds=kinds),self.assertRaises(AssertionError):validate_source_profile({**p,**delta},kinds)

    def test_post_write_audit_truth_is_success_even_if_user_requested_cancel(self):
        row,rows,baseline=self.audit_fixture();baseline.update(aclRestoreFaultVersion=4,kind='post-acl-worker-crash');rows[-1]['result']='Succeeded'
        verify_restore_audits(rows,row,baseline)
        for wrong in ('Failed','Canceled','Unknown'):
            changed=copy.deepcopy(rows);changed[-1]['result']=wrong
            with self.subTest(wrong=wrong),self.assertRaises(AssertionError):verify_restore_audits(changed,row,baseline)
        baseline['kind']='post-acl-cancel'
        with self.assertRaises(AssertionError):verify_restore_audits(rows,row,baseline)
        cancel={**copy.deepcopy(rows[-1]),'action':'containers.files.acl.restore.cancel-requested','result':'Accepted'}
        verify_restore_audits(rows+[cancel],row,baseline)
        cancel['job_id']=row['id']
        with self.assertRaises(AssertionError):verify_restore_audits(rows+[cancel],row,baseline)

    def test_post_revocation_requires_its_own_schema_and_source_binding(self):
        p=dict(schemaVersion=11,aclRestoreFaultVersion=5,sha256={n:'digest' for n in set(SOURCES)|set(RESTORE_SOURCES)})
        validate_source_profile(p,['post-acl-group-removal','post-acl-account-disable'])
        for kind in ('post-acl-group-removal','post-acl-account-disable'):
            self.assertEqual(prove_binding(self.row,{**self.baseline,'aclRestoreFaultVersion':5,'kind':kind},self.source,self.proof),self.source['accessAcl'])
        for delta,kinds in [({'schemaVersion':10},['post-acl-group-removal']),({'aclRestoreFaultVersion':4},['post-acl-account-disable']),({},['group-removal']),({},['post-acl-cancel'])]:
            with self.subTest(delta=delta,kinds=kinds),self.assertRaises(AssertionError):validate_source_profile({**p,**delta},kinds)

    def test_post_revocation_audit_retains_completed_fact_without_cancel_request(self):
        row,rows,baseline=self.audit_fixture();baseline.update(aclRestoreFaultVersion=5,kind='post-acl-group-removal');rows[-1]['result']='Succeeded'
        verify_restore_audits(rows,row,baseline)
        for state in ('Failed','Canceled','Unknown'):
            changed=copy.deepcopy(rows);changed[-1]['result']=state
            with self.subTest(state=state),self.assertRaises(AssertionError):verify_restore_audits(changed,row,baseline)
        with self.assertRaises(AssertionError):verify_restore_audits(rows+[rows[-1]],row,baseline)

    def test_current_admin_read_does_not_inherit_original_owner_restore_access(self):
        row={**self.row,'actor_subject':'original'};record=dict(version='new-version',historyDigest='new-history')
        intent=dict(id=row['id'],executionJobId=row['execution_job_id'],state='Succeeded',publishedVersion=record['version'],aclConfirmed=True,confirmation=None,historyVerified=True,historyDigest=record['historyDigest'],receipt=dict(id=row['execution_job_id'],bindingCurrent=True,state='Completed'))
        path='/api/v1/containers/'+row['container_id']+'/files/acls/restores/'+row['id']
        proof=dict(subject='different-admin',currentlyAdministrator=True,readStatus=200,readOnlyOperation=True,normalHttpDualLogout=True,intent=intent,ownerRestoreStatus=404,ownerRestorePath=path,recoveryNoStore=True)
        verify_current_admin(proof,row,record,'original')
        for key,value in [('ownerRestoreStatus',200),('ownerRestorePath',path+'-other'),('recoveryNoStore',False),('subject','original')]:
            with self.subTest(key=key),self.assertRaises(AssertionError):verify_current_admin({**proof,key:value},row,record,'original')

    def test_existing_source_profile_cannot_claim_new_restore_sources(self):
        validate_source_profile(dict(schemaVersion=6,sha256={n:'digest' for n in SOURCES}),['cancel','logout'])
        with self.assertRaises(AssertionError):validate_source_profile(dict(schemaVersion=6,sha256={n:'digest' for n in set(SOURCES)|set(RESTORE_SOURCES)}),['cancel'])

    def test_post_agent_restart_requires_schema_twelve_and_original_capture(self):
        p=dict(schemaVersion=12,aclRestoreFaultVersion=6,sha256={n:'digest' for n in set(SOURCES)|set(RESTORE_SOURCES)})
        validate_source_profile(p,['post-acl-agent-restart'])
        baseline={**self.baseline,'aclRestoreFaultVersion':6,'kind':'post-acl-agent-restart'}
        self.assertEqual(prove_binding(self.row,baseline,self.source,self.proof),self.source['accessAcl'])
        for delta,kinds in [({'schemaVersion':11},['post-acl-agent-restart']),({'schemaVersion':6},['post-acl-agent-restart']),({'aclRestoreFaultVersion':5},['post-acl-agent-restart']),({'aclRestoreFaultVersion':True},['post-acl-agent-restart']),({},['agent-restart']),({},['post-acl-worker-crash']),({},['post-acl-account-disable'])]:
            with self.subTest(delta=delta,kinds=kinds),self.assertRaises(AssertionError):validate_source_profile({**p,**delta},kinds)

    def test_post_agent_audit_retains_completed_restore_without_cancel(self):
        row,rows,baseline=self.audit_fixture();baseline.update(aclRestoreFaultVersion=6,kind='post-acl-agent-restart');rows[-1]['result']='Succeeded'
        verify_restore_audits(rows,row,baseline)
        for state in ('Failed','Canceled','Unknown'):
            changed=copy.deepcopy(rows);changed[-1]['result']=state
            with self.subTest(state=state),self.assertRaises(AssertionError):verify_restore_audits(changed,row,baseline)
        cancel={**copy.deepcopy(rows[-1]),'action':'containers.files.acl.restore.cancel-requested','result':'Accepted'}
        with self.assertRaises(AssertionError):verify_restore_audits(rows+[cancel],row,baseline)

    def test_post_agent_restart_requires_restore_wrapper_binding_and_maintenance_continuity(self):
        source=copy.deepcopy(self.source);revision='a'*40
        row={key:'original-'+key for key in ('id','execution_job_id','container_id','instance_id','actor_subject','owner_hash','plan_json','command_json','root_started_at_utc','restore_source_json','request_hash','plan_hash','execution_hash','login_binding')}
        row.update(cancel_requested_at_utc=None,state='Applying',publish_barrier=True,lease_token='original-lease',lease_expires_at_utc='2026-11-03T00:01:00Z')
        final={**row,'state':'Succeeded','publish_barrier':False,'lease_token':None,'lease_expires_at_utc':None}
        baseline=dict(aclRestoreFaultVersion=6,kind='post-acl-agent-restart',restoreSource=source,revision=revision)
        hosts={'whaledeck-agent.service':dict(pid=100,sourceRevision=revision),'whaledeck-maintenance.service':dict(pid=200,sourceRevision=revision)}
        service=dict(hostsBefore=hosts,hostsAfter={**hosts,'whaledeck-agent.service':dict(pid=101,sourceRevision=revision)},maintenancePidUnchanged=True,
            otherActiveWork={key:0 for key in RESTART_KEYS},restartBeganAtUtc='2026-11-03T00:00:00Z',restartEndedAtUtc='2026-11-03T00:00:02Z',totalRestartSeconds=2,maintenanceSamples=4)
        ui=dict(normalDualLogout=True,cancellations=0,intentReadPath='/api/v1/containers/'+row['container_id']+'/files/acls/restores/'+row['id'],restoreTerminalSource=source,
            terminalIntent=dict(id=row['id'],executionJobId=row['execution_job_id'],state='Succeeded',resultCode='CONTAINER_ACL_COMPLETED',aclConfirmed=True,confirmation=None,historyVerified=True,receipt=dict(state='Completed')))
        samples=[dict(atUtc=at,httpStatus=200,state='healthy',phase='Ready',noStore=True,bytes=100,responseSeconds=.05) for at in ('2026-11-02T23:59:59Z','2026-11-03T00:00:00.5Z','2026-11-03T00:00:01Z','2026-11-03T00:00:03Z')]
        def check():verify_restart('agent-restart',dict(serviceRestart=service),baseline,ui,final,dict(row=row,executionJob=dict(state='Running')),lambda path: samples,Path('/isolated-fixture'),post=True)
        check()
        good_ui=copy.deepcopy(ui)
        for key,value in [('intentReadPath',ui['intentReadPath'].replace('/restores/','/')),('restoreTerminalSource',{**source,'defaultAcl':[dict(tag='other',id=None,permissions=7)]}),('terminalIntent',{**ui['terminalIntent'],'state':'Failed'}),('normalDualLogout',False)]:
            ui.update({key:value})
            with self.subTest(key=key),self.assertRaises(AssertionError):check()
            ui.clear();ui.update(copy.deepcopy(good_ui))
        for key in ('restore_source_json','request_hash','plan_hash','execution_hash','login_binding'):
            final[key]='changed'
            with self.subTest(key=key),self.assertRaises(AssertionError):check()
            final[key]=row[key]
        samples[2]['httpStatus']=503
        with self.assertRaises(AssertionError):check()

if __name__=='__main__':unittest.main()
