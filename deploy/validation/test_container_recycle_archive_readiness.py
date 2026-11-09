"""Readiness diagnostic over native tempfile I/O, not actual expiry/retirement."""
import ast
import copy
import os
from pathlib import Path
import unittest
from unittest.mock import patch
import test_container_recycle_archive_retirements as native
from test_container_recycle_purge_plan import snapshot
from validate_container_recycle_archive_readiness import inspect_readiness,readiness_proof


@unittest.skipUnless(os.name=='posix' and os.geteuid()==0,'isolated Linux root required')
class ReadinessTests(unittest.TestCase):
    def setUp(self):
        self.f=native.ArchiveRetirementTests();self.f.setUp();self.addCleanup(self.f.doCleanups);self.f.reserve()

    def observe(self):return inspect_readiness(self.f.engine,self.f.q,self.f.session)
    def protected(self):return dict(original=self.f.f.protected(),roots=snapshot(self.f.engine.state_path))

    def test_before_due_reads_fresh_plan_without_mutation_or_apply(self):
        before=self.protected()
        with patch.object(self.f.engine,'apply',side_effect=AssertionError('no execution')),patch.object(self.f.engine,'reconcile',side_effect=AssertionError('no repair')):
            value=self.observe()
        self.assertEqual(value['status'],'NotDue');self.assertFalse(value['retirementExecuted']);self.assertEqual(before,self.protected())

    def test_due_clock_double_reports_readiness_without_unlink_or_certificate(self):
        before=self.protected()
        with self.f.clock(),self.f.no_data_unlink():value=self.observe()
        self.assertEqual(value['status'],'ReadyForExplicitAcceptance');self.assertTrue(self.f.data.exists());self.assertEqual(before,self.protected())

    def test_missing_operation_lock_cannot_be_recreated(self):
        path=self.f.engine.state_path/(self.f.q['operationId']+'.lock');path.unlink();before=self.protected()
        with self.assertRaises(FileNotFoundError):self.observe()
        self.assertFalse(path.exists());self.assertEqual(before,self.protected())

    def test_released_marker_and_lost_payload_reject_without_repair(self):
        self.f.engine.write_marker(self.f.q,'Released');before=self.protected()
        with self.assertRaises(AssertionError):self.observe()
        self.assertEqual(before,self.protected());self.f.engine.write_marker(self.f.q,'Reserved');self.f.data.unlink();before=self.protected()
        with self.assertRaises(Exception):self.observe()
        self.assertEqual(before,self.protected())

    def test_boolean_clock_changed_facts_and_forged_status_cannot_pass(self):
        value=self.observe()
        for changes in (dict(clockBeforeUnixMs=True),dict(status='ReadyForExplicitAcceptance'),dict(retirementExecuted=True)):
            with self.subTest(changes=changes),self.assertRaises(AssertionError):readiness_proof({**value,**changes})
        bad=copy.deepcopy(value);bad['currentPlan']['archiveSha256']='F'*64
        with self.assertRaises(AssertionError):readiness_proof(bad)

    def test_due_boundary_crossing_is_refused_for_a_fresh_check(self):
        value=self.observe();value['clockAfterUnixMs']=value['currentPlan']['notBeforeUnixMs']
        with self.assertRaises(AssertionError):readiness_proof(value)

    def test_gate_contains_no_native_execution_cleanup_or_recursive_directory_creation(self):
        path=Path(__file__).with_name('validate_container_recycle_archive_readiness.py');tree=ast.parse(path.read_bytes())
        forbidden={'apply','reserve','discard','inspect','reconcile','recover','unlink','rmdir','remove','rmtree','makedirs'}
        names={n.func.attr for n in ast.walk(tree) if isinstance(n,ast.Call) and isinstance(n.func,ast.Attribute)}
        self.assertFalse(names&forbidden)
