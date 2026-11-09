"""Owned temporary native unlink/fsync/journals and actual fork/SIGKILL cuts.

Namespace/HDD hardware attestation and due clock are explicit doubles. These
are not installed RPC, natural-expiry or production HDD-retirement evidence.
"""
import copy
import json
import os
import select
import signal
import unittest
from unittest.mock import patch
import test_container_file_staging as staging
import test_container_recycle_archive_retirement_fences as fences
import test_container_recycle_retired_history as observations
from test_container_recycle_purge_plan import snapshot

candidate=staging.load('whaledeck-container-recycle-archive-retirements.py','archive_retirements_candidate')


@unittest.skipUnless(os.name=='posix' and os.geteuid()==0,'isolated Linux root required')
class ArchiveRetirementTests(unittest.TestCase):
    def setUp(self):
        self.f=fences.ArchiveRetirementFenceTests();self.f.setUp();self.addCleanup(self.f.doCleanups)
        self.engine=candidate.archive_retirements_type(type(self.f.engine))(self.f.preflight,self.f.state)
        self.q=self.f.q;self.session=self.f.session;self.native=self.f.native
        self.ref=self.engine.ref(self.q);self.data=self.native.base.target/(self.ref['operationId']+'.data')
        self.reader=observations.candidate.ContainerRecycleRetiredHistory(self.engine)
        self.due=self.f.f.due

    def protected(self):
        # Exclude exactly this data object, not its metadata or sibling history.
        values=self.f.protected();values=copy.deepcopy(values)
        values['files'][4].pop(self.data.name,None)
        return values

    @staticmethod
    def no_data_unlink():
        unlink=os.unlink
        def checked(name,*args,**kwargs):
            if str(name).endswith(('.data','.partial')):raise AssertionError('never repeat payload unlink')
            return unlink(name,*args,**kwargs)
        return patch.object(os,'unlink',side_effect=checked)

    def reserve(self):return self.engine.reserve(self.q,self.session)
    def apply(self):return self.engine.apply(self.q,self.session)
    def clock(self,ms=None):return self.f.f.clock(self.due+1 if ms is None else ms)
    def observe(self):return self.reader.observe(self.ref,self.native.q['plan']['command']['historyDigest'])
    def kill_at(self,phase):
        child=os.fork()
        if child==0:
            try:
                self.engine.checkpoint=lambda at:os.kill(os.getpid(),signal.SIGKILL) if at==phase else None
                self.apply()
            except BaseException:os._exit(2)
            os._exit(3)
        fd=os.pidfd_open(child)
        try:
            ready=select.select([fd],[],[],20)[0]
            if not ready:signal.pidfd_send_signal(fd,signal.SIGKILL)
            _,status=os.waitpid(child,0)
        finally:os.close(fd)
        self.assertTrue(ready);self.assertTrue(os.WIFSIGNALED(status));self.assertEqual(os.WTERMSIG(status),signal.SIGKILL)

    def test_due_execution_unlinks_only_selected_data_and_joins_real_certificate(self):
        self.reserve();before=self.protected();identity=self.engine.source.metadata(self.data.stat())
        with self.clock():
            result=self.apply();observed=self.observe()
        self.assertEqual(result['state'],'Completed');self.assertTrue(result['retirementApplied'] and result['sourceFenced'] and result['ownedFence'])
        self.assertEqual((observed['disposition'],observed['dataPresent']),('Retired',False));self.assertFalse(self.data.exists());self.assertEqual(before,self.protected())
        record=self.f.record();self.assertEqual(record['schemaVersion'],2);self.assertEqual(record['completion']['unlinkedIdentity'],identity);self.assertTrue(record['completion']['executionPlan']['retirementEligible']);self.assertFalse(self.q['plan']['retirementEligible']);self.assertEqual(json.loads(self.f.marker().read_bytes())['state'],'Retired')
        with self.assertRaises(FileNotFoundError):self.native.source.history.verify(self.ref,self.native.q['plan']['command']['historyDigest'])

    def test_no_implicit_reservation_and_abort_never_late_executes(self):
        before=self.protected()
        with self.clock(),self.assertRaisesRegex(Exception,'NOT_RESERVED'):self.apply()
        self.engine.discard(self.q,None)
        with self.clock():self.assertEqual(self.apply()['state'],'Aborted')
        self.assertTrue(self.data.exists());self.assertEqual(before,self.protected())

    def test_young_sessionless_and_forged_plans_do_not_enter_write_phase(self):
        self.reserve();before=snapshot(self.f.state);data=self.data.read_bytes()
        with self.assertRaisesRegex(Exception,'NOT_ELIGIBLE'):self.apply()
        with self.clock(),self.assertRaisesRegex(Exception,'SESSION_REQUIRED'):self.engine.apply(self.q,None)
        bad=copy.deepcopy(self.q);bad['plan']['notBeforeUnixMs']-=1;bad['plan']['fingerprint']=self.f.preflight.digest(bad['plan'])
        with self.clock(),self.assertRaises(Exception):self.engine.apply(bad,self.session)
        self.assertEqual(before,snapshot(self.f.state));self.assertEqual(data,self.data.read_bytes())

    def test_missing_or_changed_live_copy_cannot_delete_hdd(self):
        self.reserve();self.native.base.leaf.rename(self.native.base.leaf.with_suffix('.held'));before=self.protected()
        with self.clock(),self.assertRaisesRegex(Exception,'PLAN_CHANGED|NOT_ELIGIBLE'):self.apply()
        self.assertTrue(self.data.exists());self.assertEqual(self.f.record()['state'],'Reserved');self.assertEqual(before,self.protected())

    def test_live_copy_path_removed_during_last_hash_rejects_before_unlink(self):
        self.reserve();check=self.engine.copies.check_file;calls=0
        def move_after_hash(*args,**kwargs):
            nonlocal calls
            result=check(*args,**kwargs);calls+=1
            # The first two calls are preflight.binding; later calls are the
            # executor's guard. Mutation after a full FD check must be noticed.
            if calls==3:self.native.base.leaf.rename(self.native.base.leaf.with_suffix('.held'))
            return result
        with self.clock(),patch.object(self.engine.copies,'check_file',side_effect=move_after_hash),self.assertRaises(Exception):self.apply()
        self.assertTrue(self.data.exists());self.assertEqual(self.f.record()['state'],'Reserved')

    def test_missing_archive_claim_or_changed_history_never_gets_repaired(self):
        self.reserve();claim=self.native.f.claim();claim.unlink();before=self.protected()
        with self.clock(),self.assertRaisesRegex(Exception,'FENCE_CHANGED'):self.apply()
        self.assertTrue(self.data.exists());self.assertFalse(claim.exists());self.assertEqual(before,self.protected())

    def test_same_bytes_hdd_replacement_at_removing_is_not_deleted(self):
        self.reserve();held=self.data.with_suffix('.held')
        def replace(phase):
            if phase=='Removing':self.data.rename(held);self.data.write_bytes(held.read_bytes());self.data.chmod(0o600)
        with self.clock(),patch.object(self.engine,'checkpoint',side_effect=replace),self.assertRaisesRegex(Exception,'HISTORY_CHANGED'):self.apply()
        self.assertEqual(self.data.read_bytes(),held.read_bytes());self.assertEqual(self.f.record()['state'],'Unknown');self.assertIsNone(self.f.record()['completion'])

    def test_partial_object_appearing_before_unlink_is_preserved_and_blocks_execution(self):
        self.reserve();partial=self.data.with_suffix('.partial')
        def create(phase):
            if phase=='RetiringFenced':partial.write_bytes(b'foreign partial')
        with self.clock(),patch.object(self.engine,'checkpoint',side_effect=create),self.assertRaisesRegex(Exception,'HISTORY_CHANGED'):self.apply()
        self.assertTrue(self.data.exists());self.assertEqual(partial.read_bytes(),b'foreign partial');self.assertEqual(self.f.record()['state'],'Unknown')

    def test_storage_readonly_or_configuration_change_refuses_unlink(self):
        self.reserve();attest=self.engine.source.history.storage.attest
        def readonly(*args,**kwargs):
            if not kwargs.get('read_only',False):raise OSError('owned read-only mount')
            return attest(*args,**kwargs)
        with self.clock(),patch.object(self.engine.source.history.storage,'attest',side_effect=readonly),self.assertRaisesRegex(OSError,'read-only mount'):self.apply()
        self.assertTrue(self.data.exists());self.assertEqual(self.f.record()['state'],'Reserved')

    def test_unlink_errno_never_confirms_or_retries_deletion(self):
        self.reserve();before=self.protected();unlink=os.unlink;calls=[]
        def fail(name,*args,**kwargs):
            if name==self.data.name:calls.append(name);raise OSError('owned unlink failure')
            return unlink(name,*args,**kwargs)
        with self.clock(),patch.object(os,'unlink',side_effect=fail),self.assertRaisesRegex(OSError,'owned unlink'):self.apply()
        self.assertEqual(calls,[self.data.name]);self.assertTrue(self.data.exists());self.assertEqual(self.f.record()['state'],'Unknown');self.assertIsNone(self.engine.recover(self.q)['retirementApplied']);self.assertEqual(before,self.protected())

    def test_hdd_directory_fsync_failure_after_unlink_never_claims_retired(self):
        self.reserve();fsync=os.fsync
        def fail(fd):
            if os.readlink('/proc/self/fd/'+str(fd))==str(self.native.base.target):raise OSError('owned HDD fsync failure')
            return fsync(fd)
        with self.clock(),patch.object(os,'fsync',side_effect=fail),self.assertRaisesRegex(OSError,'owned HDD fsync'):self.apply()
        self.assertFalse(self.data.exists());self.assertEqual(self.f.record()['state'],'Unknown');self.assertIsNone(self.f.record()['completion']);self.assertEqual(self.observe()['disposition'],'Unavailable')

    def test_real_sigkill_cuts_are_unknown_and_never_resume_unlink_or_certificate(self):
        for phase,exists in (('Removing',True),('RetiringFenced',True),('UnlinkedNamespace',False),('Unlinked',False),('Removed',False),('RetiredFenced',False)):
            with self.subTest(phase=phase):
                f=ArchiveRetirementTests();f.setUp()
                try:
                    f.reserve();before=f.protected()
                    with f.clock():f.kill_at(phase)
                    cold=type(f.engine)(f.f.preflight,f.f.state)
                    with self.no_data_unlink(),patch.object(cold.preflight,'plan_locked',side_effect=AssertionError('never replan interrupted execution')):
                        for action in (cold.inspect,cold.apply,cold.recover,cold.discard):
                            result=action(f.q,None);self.assertEqual(result['state'],'Unknown');self.assertIsNone(result['retirementApplied']);self.assertTrue(result['sourceFenced'])
                    self.assertEqual(f.data.exists(),exists);self.assertEqual(before,f.protected());self.assertEqual(f.observe()['disposition'],'Unavailable')
                finally:f.doCleanups()

    def test_completed_sigkill_is_durable_and_replay_is_observation_only(self):
        self.reserve()
        with self.clock():self.kill_at('Completed')
        before=self.protected();state=snapshot(self.f.state)
        with patch.object(os,'unlink',side_effect=AssertionError('no replay')),patch.object(self.engine.preflight,'plan_locked',side_effect=AssertionError('no fresh delete')):
            for action in (self.engine.apply,self.engine.recover,self.engine.discard,self.engine.reserve):
                result=action(self.q,None);self.assertEqual(result['state'],'Completed');self.assertTrue(result['retirementApplied'])
        self.assertEqual(before,self.protected());self.assertEqual(state,snapshot(self.f.state))

    def test_lost_execution_marker_at_every_cut_still_blocks_competitors(self):
        for phase in ('Removing','RetiringFenced','UnlinkedNamespace','Unlinked','Removed','RetiredFenced'):
            with self.subTest(phase=phase):
                f=ArchiveRetirementTests();f.setUp()
                try:
                    f.reserve()
                    with f.clock():f.kill_at(phase)
                    f.f.marker().unlink();before=f.protected();present=f.data.exists()
                    with f.clock(),self.assertRaisesRegex(Exception,'SOURCE_BUSY'):f.engine.reserve(f.f.new(),f.session)
                    with self.no_data_unlink():
                        result=f.engine.apply(f.q,None)
                    self.assertEqual(result['state'],'Unknown');self.assertIsNone(result['retirementApplied']);self.assertFalse(f.f.marker().exists());self.assertEqual(f.data.exists(),present);self.assertEqual(before,f.protected())
                finally:f.doCleanups()

    def test_orphan_retiring_marker_reconstructs_only_versioned_unknown(self):
        self.reserve()
        with self.clock():self.kill_at('Unlinked')
        self.f.journal().unlink();marker=self.f.marker().read_bytes()
        with self.no_data_unlink():
            result=self.engine.apply(self.q,None)
        self.assertEqual(result['state'],'Unknown');self.assertIsNone(result['retirementApplied']);self.assertEqual(self.f.record()['schemaVersion'],2);self.assertEqual(marker,self.f.marker().read_bytes());self.assertEqual(self.observe()['disposition'],'Unavailable')

    def test_authority_loss_at_removing_does_not_delete(self):
        self.reserve()
        def cut(phase):
            if phase=='Removing':self.session.revalidate.side_effect=RuntimeError('owned authority loss')
        with self.clock(),patch.object(self.engine,'checkpoint',side_effect=cut),self.assertRaisesRegex(RuntimeError,'owned authority loss'):self.apply()
        self.assertTrue(self.data.exists());self.assertEqual(self.f.record()['state'],'Unknown')

    def test_clock_rollback_at_retiring_fence_blocks_unlink(self):
        self.reserve();clock=self.clock();clock.start();self.addCleanup(clock.stop)
        def cut(phase):
            if phase=='RetiringFenced':candidate.time.time_ns.return_value=(self.due-1)*1000000
        with patch.object(self.engine,'checkpoint',side_effect=cut),self.assertRaisesRegex(Exception,'NOT_DUE'):self.apply()
        self.assertTrue(self.data.exists());self.assertEqual(self.f.record()['state'],'Unknown')

    def test_postunlink_data_reappearance_is_never_success(self):
        self.reserve()
        def cut(phase):
            if phase=='Unlinked':self.data.write_bytes(b'foreign reappearance');self.data.chmod(0o600)
        with self.clock(),patch.object(self.engine,'checkpoint',side_effect=cut),self.assertRaisesRegex(Exception,'HISTORY_CHANGED'):self.apply()
        self.assertEqual(self.data.read_bytes(),b'foreign reappearance');self.assertEqual(self.f.record()['state'],'Unknown');self.assertEqual(self.observe()['disposition'],'Unavailable')

    def test_terminal_marker_failure_retains_certificate_but_unknown(self):
        self.reserve();write=self.engine.write_marker
        def fail(request,state):
            if state=='Retired':raise OSError('owned terminal marker failure')
            return write(request,state)
        with self.clock(),patch.object(self.engine,'write_marker',side_effect=fail),self.assertRaisesRegex(OSError,'terminal marker'):self.apply()
        self.assertEqual(self.f.record()['state'],'Unknown');self.assertIsNotNone(self.f.record()['completion']);self.assertFalse(self.data.exists());self.assertEqual(self.observe()['disposition'],'Unavailable')

    def test_completed_journal_directory_fsync_error_is_not_recovered_as_success(self):
        self.reserve();save=self.engine.save;fsync=os.fsync;committing=False;failed=False
        def saving(state,record):
            nonlocal committing
            committing=record['state']=='Completed'
            try:return save(state,record)
            finally:committing=False
        def fail(fd):
            nonlocal failed
            if committing and not failed and os.readlink('/proc/self/fd/'+str(fd))==str(self.f.state):
                failed=True;raise OSError('owned Completed directory fsync')
            return fsync(fd)
        with self.clock(),patch.object(self.engine,'save',side_effect=saving),patch.object(os,'fsync',side_effect=fail),self.assertRaisesRegex(OSError,'Completed directory fsync'):self.apply()
        self.assertTrue(failed);self.assertFalse(self.data.exists());self.assertEqual(self.f.record()['state'],'Unknown');self.assertEqual(json.loads(self.f.marker().read_bytes())['state'],'Unknown');self.assertIsNone(self.engine.recover(self.q)['retirementApplied']);self.assertEqual(self.observe()['disposition'],'Unavailable')

    def test_late_authority_loss_after_terminal_marker_cannot_commit_completed(self):
        self.reserve()
        def cut(phase):
            if phase=='RetiredFenced':self.session.revalidate.side_effect=RuntimeError('owned late authority loss')
        with self.clock(),patch.object(self.engine,'checkpoint',side_effect=cut),self.assertRaisesRegex(RuntimeError,'late authority loss'):self.apply()
        self.assertFalse(self.data.exists());self.assertEqual(self.f.record()['state'],'Unknown');self.assertIsNotNone(self.f.record()['completion']);self.assertEqual(self.observe()['disposition'],'Unavailable')

    def test_copy_loss_after_unlink_keeps_only_unknown_not_success(self):
        self.reserve()
        def cut(phase):
            if phase=='Unlinked':self.native.base.leaf.rename(self.native.base.leaf.with_suffix('.held'))
        with self.clock(),patch.object(self.engine,'checkpoint',side_effect=cut),self.assertRaises(Exception):self.apply()
        self.assertFalse(self.data.exists());self.assertEqual(self.f.record()['state'],'Unknown');self.assertIsNone(self.f.record()['completion'])

    def test_hdd_unexpected_attributes_before_unlink_are_not_discarded(self):
        self.reserve()
        def cut(phase):
            if phase=='RetiringFenced':os.setxattr(self.data,'user.foreign',b'preserve')
        with self.clock(),patch.object(self.engine,'checkpoint',side_effect=cut),self.assertRaisesRegex(Exception,'HISTORY_CHANGED'):self.apply()
        self.assertTrue(self.data.exists());self.assertEqual(os.getxattr(self.data,'user.foreign'),b'preserve')

    def test_foreign_marker_after_removing_is_never_poisoned_or_overwritten(self):
        self.reserve();raw=None
        def cut(phase):
            nonlocal raw
            if phase=='RetiringFenced':
                self.f.marker().write_text(json.dumps(self.engine.marker(self.f.new(),'Reserved')));raw=self.f.marker().read_bytes()
        with self.clock(),patch.object(self.engine,'checkpoint',side_effect=cut),self.assertRaisesRegex(Exception,'FENCE_CHANGED'):self.apply()
        self.assertTrue(self.data.exists());self.assertEqual(self.f.marker().read_bytes(),raw);self.assertEqual(self.f.record()['state'],'Unknown')

    def test_lost_marker_after_removing_is_never_recreated_by_failure_cleanup(self):
        self.reserve()
        def cut(phase):
            if phase=='RetiringFenced':self.f.marker().unlink()
        with self.clock(),patch.object(self.engine,'checkpoint',side_effect=cut),self.assertRaisesRegex(Exception,'FENCE_CHANGED'):self.apply()
        self.assertTrue(self.data.exists());self.assertFalse(self.f.marker().exists());self.assertEqual(self.f.record()['state'],'Unknown')

    def test_new_versioned_phase_shapes_are_strict(self):
        self.reserve()
        with self.clock():self.apply()
        completed=self.f.record()
        for state,completion in (('Removing',completed['completion']),('Removed',None),('Completed',None)):
            bad={**completed,'state':state,'completion':completion}
            with self.subTest(state=state),self.assertRaisesRegex(Exception,'RECEIPT_INVALID'):self.engine.validate_record(bad,self.q['operationId'])
