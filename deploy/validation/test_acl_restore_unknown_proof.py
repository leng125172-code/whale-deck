"""Restore Unknown provenance/ack rejection; synthetic rows, no real services."""
import copy
import unittest
import test_acl_restore_fault_proof as restoration_fixtures
import test_acl_unknown_proof as unknown_fixtures
from acl_restore_unknown_proof import require_restore_unknown,verify_restore_unknown_audits,restore_unknown_sources
from acl_restore_fault_proof import prove_binding,verify_restore_audits
from verify_container_acl_unknown import require_source_version,require_acknowledgement

class RestoreUnknownProofTests(unittest.TestCase):
    def setUp(self):
        self.fixture=restoration_fixtures.RestoreFaultProvenanceTests();self.fixture.setUp()
        self.row,self.audits,_=self.fixture.audit_fixture()
        self.row.update(state='Unknown',acknowledged_by=None)
        self.audits[-1]['result']='Unknown'
        self.baseline={**self.fixture.baseline,'aclRestoreFaultVersion':7,'kind':'post-acl-target-removed','postAcl':True,'instanceChanged':False}

    def test_restore_unknown_has_distinct_provenance_and_selected_source(self):
        require_restore_unknown(self.baseline)
        source=dict(proofVersion=5,aclRestoreFaultVersion=7)
        require_source_version(source,self.baseline)
        self.assertEqual(prove_binding(self.fixture.row,self.baseline,self.fixture.source,self.fixture.proof),self.fixture.source['accessAcl'])
        for change in ({'proofVersion':4},{'proofVersion':True},{'aclRestoreFaultVersion':6},{'aclRestoreFaultVersion':True}):
            with self.subTest(change=change),self.assertRaises(AssertionError):require_source_version({**source,**change},self.baseline)
        for change in ({'postAcl':False},{'instanceChanged':True},{'kind':'post-acl-agent-restart'},{'aclRestoreFaultVersion':True}):
            with self.subTest(change=change),self.assertRaises(AssertionError):require_restore_unknown({**self.baseline,**change})
        with self.assertRaises(AssertionError):require_source_version(source,dict(postAcl=True,instanceChanged=False))

    def test_unknown_result_is_not_success_failure_or_cancellation(self):
        verify_restore_unknown_audits(self.audits,self.row,self.baseline)
        verify_restore_audits(self.audits,self.row,self.baseline)
        for wrong in ('Succeeded','Failed','Canceled'):
            changed=copy.deepcopy(self.audits);changed[-1]['result']=wrong
            with self.subTest(wrong=wrong),self.assertRaises(AssertionError):verify_restore_unknown_audits(changed,self.row,self.baseline)

    def test_restore_audit_requires_original_source_hash_and_dedicated_prefix(self):
        for key,value in [('action','containers.files.acl.result'),('actor_subject','another-admin'),('job_id',self.row['id'])]:
            changed=copy.deepcopy(self.audits);changed[-1][key]=value
            with self.subTest(key=key),self.assertRaises(AssertionError):verify_restore_unknown_audits(changed,self.row,self.baseline)
        for key,value in [('historyRestore',False),('restoreSourceHash','foreign-source')]:
            changed=copy.deepcopy(self.audits);changed[-1]['detail_json'][key]=value
            with self.subTest(key=key),self.assertRaises(AssertionError):verify_restore_unknown_audits(changed,self.row,self.baseline)

    def test_ack_is_separate_current_admin_event_and_retains_unknown_result(self):
        row={**self.row,'state':'Acknowledged','acknowledged_by':'new-admin'}
        ack={**copy.deepcopy(self.audits[-1]),'action':'containers.files.acl.restore.unknown-acknowledged','actor_subject':'new-admin'}
        verify_restore_unknown_audits(self.audits+[ack],row,self.baseline)
        for key,value in [('actor_subject',row['actor_subject']),('result','Succeeded'),('job_id',row['id']),('action','containers.files.acl.unknown-acknowledged')]:
            changed={**ack,key:value}
            with self.subTest(key=key),self.assertRaises(AssertionError):verify_restore_unknown_audits(self.audits+[changed],row,self.baseline)
        with self.assertRaises(AssertionError):verify_restore_unknown_audits(self.audits,row,self.baseline)
        with self.assertRaises(AssertionError):verify_restore_unknown_audits(self.audits+[ack],self.row,self.baseline)

    def test_ack_cannot_rewrite_restore_source_or_any_authority_hash(self):
        fixture=unknown_fixtures.UnknownProofTests();fixture.setUp()
        keys=('restore_source_json','request_hash','plan_hash','execution_hash','owner_hash')
        for key in keys:
            fixture.original['row'][key]=fixture.final['row'][key]='original-'+key
        require_acknowledgement(fixture.original,fixture.final,'old','new')
        for key in keys:
            changed=copy.deepcopy(fixture.final);changed['row'][key]='rewritten'
            with self.subTest(key=key),self.assertRaises(AssertionError):require_acknowledgement(fixture.original,changed,'old','new')

    def test_restore_dependencies_include_real_capture_ui_and_shared_barrier_sources(self):
        sources=restore_unknown_sources()
        self.assertEqual(len(sources),len(set(sources)))
        for name in ('acl_restore_fault_ui.mjs','acl_restore_fault_proof.py','validate_container_acl_restore_unknown.py','verify_container_acl_restore_unknown.py','validate_container_directory_unknown.py','bound_container_directory_cut.py','test_acl_restore_unknown_proof.py'):
            self.assertIn(name,sources)

if __name__=='__main__':unittest.main()
