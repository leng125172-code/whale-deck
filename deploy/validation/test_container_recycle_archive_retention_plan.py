"""Real temporary HDD/journal reads, explicit namespace and trusted-clock doubles.

No deletion, installed RPC or production retention-policy acceptance.
"""
import ast
import copy
import fcntl
import json
import os
import unittest
import uuid
from pathlib import Path
from unittest.mock import patch
import test_container_file_staging as staging
import test_container_recycle_archive_copies as copies
from test_container_recycle_purge_plan import snapshot

candidate=staging.load('whaledeck-container-recycle-archive-retention.py','archive_retention_candidate')


@unittest.skipUnless(os.name=='posix' and os.geteuid()==0,'isolated Linux root required')
class ArchiveRetentionPlanTests(unittest.TestCase):
    def setUp(self):
        self.f=copies.ArchiveCopyTests();self.f.setUp();self.addCleanup(self.f.doCleanups)
        self.f.reserve();self.f.engine.apply(self.f.q,self.f.session)
        self.engine=candidate.ContainerRecycleArchiveRetentionPreflight(self.f.engine)
        self.command=dict(archive=self.f.q,retainDays=30,mode='ArchiveData');self.session=self.f.session
        self.history=self.f.base.target/(self.f.engine.ref(self.f.q)['operationId']+'.json')
        self.created=json.loads(self.history.read_bytes())['createdAtUnixMs'];self.due=self.created+30*candidate.ContainerRecycleArchiveRetentionPreflight.DAY_MS

    def state(self):
        e=self.f.engine
        return [snapshot(p) for p in (e.source.state_path,e.restore.state_path,e.preflight.purges.state_path,e.state_path,self.f.base.target,self.f.base.root)]

    def clock(self,ms):return patch.object(candidate.time,'time_ns',return_value=ms*1000000)

    def change(self,path,**values):
        v=json.loads(path.read_bytes());v.update(values);path.write_text(json.dumps(v))

    def test_due_plan_is_readonly_and_binds_all_histories_and_payload(self):
        before=self.state()
        with self.clock(self.due),patch.object(self.f.engine,'reconcile',side_effect=AssertionError('no reconciliation')),patch.object(self.f.engine,'clear_claim',side_effect=AssertionError('no release')),patch.object(os,'unlink',side_effect=AssertionError('no deletion')),patch.object(os,'rmdir',side_effect=AssertionError('no stage deletion')):
            p=self.engine.plan(self.command,self.session);self.assertEqual(self.engine.revalidate(p,self.session),p)
        self.assertTrue(p['retentionElapsed'] and p['bindingCurrent'] and p['retirementEligible']);self.assertEqual(p['notBeforeUnixMs'],self.due);self.assertEqual(p['historyCreatedAtUnixMs'],self.created);self.assertEqual(p['archiveIdentity'],self.f.q['plan']['archiveIdentity']);self.assertEqual(p['publishedIdentity'],self.f.record()['publishedIdentity']);self.assertEqual(before,self.state())
        for name in ('apply','delete','reserve','discard','release','cleanup'):self.assertFalse(hasattr(self.engine,name))

    def test_young_plan_is_diagnostic_and_boundary_invalidates_it(self):
        before=self.state()
        with self.clock(self.due-1):p=self.engine.plan(self.command,self.session);self.assertFalse(p['retentionElapsed']);self.assertFalse(p['retirementEligible']);self.assertTrue(p['bindingCurrent'])
        with self.clock(self.due),self.assertRaisesRegex(Exception,'PLAN_CHANGED'):self.engine.revalidate(p,self.session)
        self.assertEqual(before,self.state())

    def test_clock_before_capture_is_rejected_without_using_file_mtime(self):
        before=self.state()
        with self.clock(self.created-1),self.assertRaisesRegex(Exception,'CLOCK_BEFORE_CAPTURE'):self.engine.plan(self.command,self.session)
        self.assertEqual(before,self.state())

    def test_absent_or_changed_live_copy_retains_history_and_is_never_eligible(self):
        held=self.f.base.leaf.with_suffix('.held');self.f.base.leaf.rename(held)
        for foreign in (False,True):
            if foreign:self.f.base.leaf.write_bytes(b'foreign copy')
            before=self.state()
            with self.subTest(foreign=foreign),self.clock(self.due):
                p=self.engine.plan(self.command,self.session);self.assertTrue(p['retentionElapsed']);self.assertFalse(p['bindingCurrent']);self.assertFalse(p['retirementEligible'])
            self.assertEqual(before,self.state());self.assertEqual(self.f.record()['state'],'Completed')

    def test_current_binding_change_invalidates_existing_plan(self):
        with self.clock(self.due):p=self.engine.plan(self.command,self.session)
        self.f.base.leaf.rename(self.f.base.leaf.with_suffix('.held'));before=self.state()
        with self.clock(self.due),self.assertRaisesRegex(Exception,'PLAN_CHANGED'):self.engine.revalidate(p,self.session)
        self.assertEqual(before,self.state())

    def test_unconfirmed_publication_is_not_completion_authority(self):
        for state in ('Unknown','Published'):
            self.change(self.f.f.journal(),state=state);before=self.state()
            with self.subTest(state=state),self.assertRaisesRegex(Exception,'COPY_UNCONFIRMED'):self.engine.plan(self.command,self.session)
            self.assertEqual(before,self.state())

    def test_missing_or_foreign_claim_is_not_repaired_or_overwritten(self):
        claim=self.f.f.claim();raw=claim.read_bytes();claim.unlink();before=self.state()
        with self.assertRaisesRegex(Exception,'FENCE_CHANGED'):self.engine.plan(self.command,self.session)
        self.assertEqual(before,self.state());self.assertFalse(claim.exists());claim.write_bytes(raw);claim.chmod(0o600)
        self.change(claim,restoreOperationId=uuid.uuid4().hex);before=self.state()
        with self.assertRaisesRegex(Exception,'FENCE_CHANGED'):self.engine.plan(self.command,self.session)
        self.assertEqual(before,self.state())

    def test_changed_source_purge_or_marker_is_rejected_without_reconcile(self):
        e=self.f.engine;ref=e.ref(self.f.q);purge=self.f.q['plan']['command']['purge']
        for path,values in ((e.source.state_path/(ref['operationId']+'.json'),dict(state='Purging')),(e.preflight.purges.state_path/(purge['operationId']+'.json'),dict(state='Unknown')),(e.source.state_path/(ref['operationId']+'.purge.json'),dict(state='Unknown'))):
            raw=path.read_bytes();self.change(path,**values);before=self.state()
            with self.subTest(path=path.name),self.assertRaises(Exception):self.engine.plan(self.command,self.session)
            self.assertEqual(before,self.state());path.write_bytes(raw)

    def test_replaced_same_bytes_hdd_and_corruption_are_rejected(self):
        data=self.history.with_suffix('.data');held=data.with_suffix('.held');data.rename(held);data.write_bytes(held.read_bytes());data.chmod(0o600);before=self.state()
        with self.assertRaisesRegex(Exception,'HISTORY_CHANGED'):self.engine.plan(self.command,self.session)
        self.assertEqual(before,self.state());data.unlink();held.rename(data);data.write_bytes(b'corrupt archive');before=self.state()
        with self.assertRaises(Exception):self.engine.plan(self.command,self.session)
        self.assertEqual(before,self.state())

    def test_history_age_cannot_be_backdated_and_rehashed_as_new_authority(self):
        v=json.loads(self.history.read_bytes());v['createdAtUnixMs']-=86400000;v['historyDigest']=self.f.source.history.digest(v);self.history.write_text(json.dumps(v));before=self.state()
        with self.assertRaisesRegex(Exception,'HISTORY_CHANGED'):self.engine.plan(self.command,self.session)
        self.assertEqual(before,self.state())

    def test_missing_unsafe_and_busy_existing_locks_are_not_created_or_changed(self):
        e=self.f.engine;ref=e.ref(self.f.q);purge=self.f.q['plan']['command']['purge']
        paths=(e.state_path/(self.f.q['operationId']+'.lock'),e.preflight.purges.state_path/(purge['operationId']+'.lock'),e.source.state_path/(ref['operationId']+'.lock'),self.f.base.target/'history.lock')
        for path in paths:
            path.unlink();before=self.state()
            with self.subTest(path=path.name),self.assertRaises(Exception):self.engine.plan(self.command,self.session)
            self.assertFalse(path.exists());self.assertEqual(before,self.state());path.write_bytes(b'');path.chmod(0o600)
            fd=os.open(path,os.O_RDONLY)
            try:
                fcntl.flock(fd,fcntl.LOCK_EX|fcntl.LOCK_NB);before=self.state()
                with self.assertRaisesRegex(Exception,'BUSY'):self.engine.plan(self.command,self.session)
                self.assertEqual(before,self.state())
            finally:os.close(fd)
            path.chmod(0o644);before=self.state()
            with self.assertRaises(Exception):self.engine.plan(self.command,self.session)
            self.assertEqual(before,self.state());path.chmod(0o600)

    def test_exact_commands_no_caller_clock_and_explicit_day_bounds(self):
        before=self.state()
        for values in (dict(retainDays=True),dict(retainDays=1.0),dict(retainDays=0),dict(retainDays=3651),dict(mode='All'),dict(nowUnixMs=self.due),dict(releaseClaim=True)):
            with self.subTest(values=values),self.assertRaises(Exception):self.engine.plan({**self.command,**values},self.session)
        with self.assertRaisesRegex(Exception,'SESSION_REQUIRED'):self.engine.plan(self.command,None)
        self.assertEqual(before,self.state())

    def test_plan_rehash_does_not_hide_numeric_types_or_extra_fields(self):
        with self.clock(self.due):p=self.engine.plan(self.command,self.session)
        before=self.state()
        for key,value in (('retirementEligible',1),('notBeforeUnixMs',float(self.due)),('archiveBytes',float(p['archiveBytes'])),('extra',1)):
            bad=copy.deepcopy(p);bad[key]=value;bad['fingerprint']=self.engine.digest(bad)
            with self.subTest(key=key),self.clock(self.due),self.assertRaises(Exception):self.engine.revalidate(bad,self.session)
        self.assertEqual(before,self.state())

    def test_session_or_storage_attestation_failure_never_changes_anything(self):
        before=self.state()
        with patch.object(self.session,'revalidate',side_effect=RuntimeError('lost session')),self.assertRaisesRegex(RuntimeError,'lost session'):self.engine.plan(self.command,self.session)
        self.assertEqual(before,self.state())
        with patch.object(self.f.source.history.storage,'attest',side_effect=RuntimeError('lost storage')),self.assertRaisesRegex(RuntimeError,'lost storage'):self.engine.plan(self.command,self.session)
        self.assertEqual(before,self.state())

    def test_structural_validation_rejects_nonboolean_and_inconsistent_eligibility(self):
        with self.clock(self.due-1):p=self.engine.plan(self.command,self.session)
        for values in (dict(retentionElapsed=1),dict(retirementEligible=True),dict(notBeforeUnixMs=self.due-1),dict(storeIdentity=[float(n) for n in p['storeIdentity']])):
            bad={**copy.deepcopy(p),**values};bad['fingerprint']=self.engine.digest(bad)
            with self.subTest(values=values),self.assertRaisesRegex(Exception,'PLAN_INVALID'):self.engine.validate_plan(bad)
        # Even a structurally consistent rehashed assertion is not authority.
        bad={**p,'retentionElapsed':True,'retirementEligible':True};bad['fingerprint']=self.engine.digest(bad);self.engine.validate_plan(bad)
        with self.clock(self.due-1),self.assertRaisesRegex(Exception,'PLAN_CHANGED'):self.engine.revalidate(bad,self.session)

    def test_namespace_capture_reads_container_binary_and_empty_stage_without_json_rules(self):
        from container_recycle_agent_io import file_snapshot
        from recursive_acl_restore_agent_io import frame
        source=Path(__file__).with_name('validate_container_recycle_archive_copies.py');node=next(n for n in ast.parse(source.read_bytes()).body if isinstance(n,ast.FunctionDef) and n.name=='retention_namespace_snapshot')
        namespace=dict(file_snapshot=file_snapshot,frame=frame);exec(compile(ast.Module(body=[node],type_ignores=[]),str(source),'exec'),namespace)
        capture=namespace['retention_namespace_snapshot'];before=self.state();value=capture(self.f.base.root)
        self.assertEqual(value[self.f.base.leaf.name]['sha256'],self.f.q['plan']['archiveSha256']);self.assertEqual(value[self.f.private().name]['identity'][:2],self.f.record()['privateIdentity']);self.assertEqual(before,self.state())
        (self.f.private()/'foreign').write_bytes(b'preserve');before=self.state()
        with self.assertRaises(AssertionError):capture(self.f.base.root)
        self.assertEqual(before,self.state())
