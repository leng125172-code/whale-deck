"""Explicit-execution gate tests; actual tempfile kernel, synthetic age only."""
import ast,copy
from pathlib import Path
import unittest
from validate_container_recycle_archive_expired import may_execute,completion_bundle
import test_container_recycle_archive_completion_acceptance as native_acceptance
import test_container_recycle_archive_retirements as native
from validate_container_recycle_archive_readiness import inspect_readiness
from collect_container_recycle_archive_completion import parent_identity_proof


class ExplicitGateTests(unittest.TestCase):
    def setUp(self):
        self.f=native_acceptance.CompletionAcceptanceTests();self.f.setUp();self.addCleanup(self.f.doCleanups);self.value=self.f.value
        v=self.value
        self.before=dict(request=v['request'],journal=v['journalBefore'],marker=v['markerBefore'],roots=v['rootsBefore'],hdd=v['hddBefore'],protected=v['protectedBefore'],namespace=v['namespaceBefore'],baseline=dict(recycleHdd=v['hddBefore'],core={'unchanged':True}),business={'unchanged':True})
        self.after=dict(request=v['request'],journal=v['journalAfter'],marker=v['markerAfter'],roots=v['rootsAfter'],hdd=v['hddAfter'],protected=v['protectedAfter'],namespace=v['namespaceAfter'],observation=v['observation'],baseline=dict(recycleHdd=v['hddAfter'],core={'unchanged':True}),business={'unchanged':True})

    def bundle(self,after=None):
        v=self.value
        return completion_bundle(self.before,self.after if after is None else after,v['readiness'],v['receipt'],v['clockBeforeUnixMs'],v['clockAfterUnixMs'])

    def test_due_does_not_implicitly_execute_without_explicit_boolean(self):
        self.assertFalse(may_execute(self.value['readiness'],False));self.assertTrue(may_execute(self.value['readiness'],True))
        for bad in (1,'true',None):
            with self.assertRaises(AssertionError):may_execute(self.value['readiness'],bad)

    def test_fake_notdue_or_changed_eligibility_cannot_bypass_fresh_proof(self):
        r=copy.deepcopy(self.value['readiness']);r['status']='NotDue'
        with self.assertRaises(AssertionError):may_execute(r,True)
        r=copy.deepcopy(self.value['readiness']);r['currentPlan']['retirementEligible']=False
        with self.assertRaises(AssertionError):may_execute(r,True)

    def test_real_notdue_plan_refuses_even_explicit_execution(self):
        f=native.ArchiveRetirementTests();f.setUp();self.addCleanup(f.doCleanups);f.reserve()
        before=f.f.protected();ready=inspect_readiness(f.engine,f.q,f.session)
        self.assertEqual(ready['status'],'NotDue');self.assertFalse(may_execute(ready,True));self.assertFalse(may_execute(ready,False));self.assertTrue(f.data.exists());self.assertEqual(before,f.f.protected())

    def test_native_temp_completion_joins_independent_physical_before_after(self):
        self.assertEqual(self.bundle()['observation']['disposition'],'Retired')

    def test_business_or_production_core_changes_reject_completion(self):
        for key in ('business','baseline'):
            after=copy.deepcopy(self.after);after[key]={}
            with self.assertRaises((AssertionError,KeyError)):self.bundle(after)

    def test_extra_global_hdd_loss_rejects_even_if_local_report_unchanged(self):
        after=copy.deepcopy(self.after);after['baseline']['recycleHdd']={}
        with self.assertRaises(AssertionError):self.bundle(after)

    def test_no_automatic_cleanup_or_retry_in_runner(self):
        path=Path(__file__).with_name('validate_container_recycle_archive_expired.py');tree=ast.parse(path.read_bytes())
        calls=[n.func.attr for n in ast.walk(tree) if isinstance(n,ast.Call) and isinstance(n.func,ast.Attribute)]
        self.assertEqual(calls.count('apply'),1);self.assertFalse(set(calls)&{'unlink','rmdir','rmtree','recover','discard','reserve'})

    def test_collector_has_no_native_mutation_entrypoint(self):
        path=Path(__file__).with_name('collect_container_recycle_archive_completion.py');tree=ast.parse(path.read_bytes())
        calls={n.func.attr for n in ast.walk(tree) if isinstance(n,ast.Call) and isinstance(n.func,ast.Attribute)}
        self.assertFalse(calls&{'apply','unlink','rmdir','rmtree','recover','discard','reserve','inspect','reconcile','mkdir'})

    def test_collector_parent_identity_is_bound_to_both_frozen_references(self):
        folder=self.f.f.native.base.root;info=folder.stat();fixture=dict(parentIdentity=[info.st_dev,info.st_ino]);q=self.value['request']
        # The native temp parent has the same Root-owned namespace contract.
        parent_identity_proof(folder,fixture,q)
        for which in ('fixture','plan'):
            f=copy.deepcopy(fixture);r=copy.deepcopy(q)
            if which=='fixture':f['parentIdentity'][1]+=1
            else:r['plan']['command']['archive']['plan']['parentIdentity'][1]+=1
            with self.assertRaises(AssertionError):parent_identity_proof(folder,f,r)
