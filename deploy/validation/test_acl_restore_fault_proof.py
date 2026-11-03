"""Negative provenance checks; no service, credentials or real restoration."""
import copy
import json
import unittest
import uuid

from acl_restore_fault_proof import prove_binding, RESTORE_SOURCES
from acl_restore_browser_proof import source_json
from verify_container_acl_faults import validate_source_profile, SOURCES


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

    def test_existing_source_profile_cannot_claim_new_restore_sources(self):
        validate_source_profile(dict(schemaVersion=6,sha256={n:'digest' for n in SOURCES}),['cancel','logout'])
        with self.assertRaises(AssertionError):validate_source_profile(dict(schemaVersion=6,sha256={n:'digest' for n in set(SOURCES)|set(RESTORE_SOURCES)}),['cancel'])

if __name__=='__main__':unittest.main()
