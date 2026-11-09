"""Acceptance proof against actual tempfile unlink; age/hardware are doubles.

No configured HDD or retained aging fixture is touched. Not natural expiry.
"""
import copy,json,os,time,unittest
from recursive_acl_restore_agent_io import snapshot as root_snapshot,read_at
from validate_container_recycle_archive_plan import payload_snapshot
from validate_container_recycle_archive_copies import retention_namespace_snapshot
from validate_container_recycle_archive_readiness import inspect_readiness
from archive_retirement_completion_proof import completion_acceptance_proof
import test_container_recycle_archive_retirements as native


@unittest.skipUnless(os.name=='posix' and os.geteuid()==0,'isolated Linux root required')
class CompletionAcceptanceTests(unittest.TestCase):
    def setUp(self):
        self.f=native.ArchiveRetirementTests();self.f.setUp();self.addCleanup(self.f.doCleanups);self.f.reserve()
        e=self.f.engine;q=self.f.q;source=self.f.ref['operationId'];folder=self.f.native.base.root
        read=lambda path:read_at(None,str(path))[1]
        journal=e.state_path/(q['operationId']+'.json');marker=e.claims_path/(source+'.json')
        with self.f.clock():
            ready=inspect_readiness(e,q,self.f.session)
            value=dict(request=q,readiness=ready,journalBefore=read(journal),markerBefore=read(marker),rootsBefore=root_snapshot(e.state_path),hddBefore=payload_snapshot(self.f.native.base.target),protectedBefore=self.f.protected(),namespaceBefore=retention_namespace_snapshot(folder),clockBeforeUnixMs=time.time_ns()//1000000)
            receipt=self.f.apply();value.update(clockAfterUnixMs=time.time_ns()//1000000,journalAfter=read(journal),markerAfter=read(marker),rootsAfter=root_snapshot(e.state_path),hddAfter=payload_snapshot(self.f.native.base.target),protectedAfter=self.f.protected(),namespaceAfter=retention_namespace_snapshot(folder),receipt=receipt,observation=self.f.observe())
        self.value=value
        self.reference=copy.deepcopy(dict(request=value['request'],roots=value['rootsBefore'],hdd=value['hddBefore'],protected=value['protectedBefore'],namespace=value['namespaceBefore']))

    def reject(self,value):
        with self.assertRaises((AssertionError,KeyError,TypeError,ValueError)):completion_acceptance_proof(value,self.reference)

    def test_real_temp_unlink_and_certificate_join(self):
        self.assertFalse(self.f.data.exists());self.assertEqual(completion_acceptance_proof(self.value,self.reference)['postRetirementEvidence'],'passed')

    def test_not_due_and_boolean_or_reversed_clocks_reject(self):
        for field,bad in (('clockBeforeUnixMs',True),('clockAfterUnixMs',0)):
            v=copy.deepcopy(self.value);v[field]=bad;self.reject(v)
        v=copy.deepcopy(self.value);v['readiness']['status']='NotDue';self.reject(v)

    def test_wrong_unlinked_inode_or_unconfirmed_unlink_rejects(self):
        for key,bad in (('unlinkConfirmed',1),('unlinkConfirmed',False),('unlinkedIdentity',[1]*9)):
            v=copy.deepcopy(self.value);v['journalAfter']['completion'][key]=bad;self.reject(v)

    def test_original_hdd_metadata_or_sibling_removal_rejects(self):
        v=copy.deepcopy(self.value);meta=self.f.ref['operationId']+'.json';v['hddAfter'][meta]['sha256']='F'*64;self.reject(v)
        v=copy.deepcopy(self.value);v['hddAfter'].pop(meta);self.reject(v)

    def test_data_reappeared_or_foreign_new_entry_rejects(self):
        for name in (self.f.ref['operationId']+'.data','foreign.data'):
            v=copy.deepcopy(self.value);v['hddAfter'][name]=next(iter(v['hddBefore'].values()));self.reject(v)

    def test_changed_original_claim_or_copy_namespace_rejects(self):
        v=copy.deepcopy(self.value);v['protectedAfter']={};self.reject(v)
        v=copy.deepcopy(self.value);v['namespaceAfter']={};self.reject(v)

    def test_incomplete_marker_wrong_receipt_and_false_readback_reject(self):
        v=copy.deepcopy(self.value);v['markerAfter']['state']='Unknown';self.reject(v)
        v=copy.deepcopy(self.value);v['receipt']['retirementApplied']=False;self.reject(v)
        v=copy.deepcopy(self.value);v['observation']['disposition']='Available';self.reject(v)

    def test_coherent_truncated_trees_need_independent_before_observation(self):
        v=copy.deepcopy(self.value);v['protectedBefore']=v['protectedAfter']={};self.reject(v)
        v=copy.deepcopy(self.value);v['namespaceBefore']=v['namespaceAfter']={};self.reject(v)
        v=copy.deepcopy(self.value);meta=self.f.ref['operationId']+'.json'
        v['hddBefore']={k:x for k,x in v['hddBefore'].items() if k!=meta};v['hddAfter']={k:x for k,x in v['hddAfter'].items() if k!=meta};self.reject(v)
