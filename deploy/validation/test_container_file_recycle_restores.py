"""Real temporary Linux file/journal/HDD-format I/O with explicit namespace doubles.

Not an installed helper, physical HDD, Docker, RPC or business acceptance.
"""
import copy
import json
import os
import select
import signal
import stat
import unittest
import uuid
from unittest.mock import patch

import test_container_file_recycling as recycling
import test_container_file_staging as staging

candidate=staging.load('whaledeck-container-recycle-restores.py','recycle_restore_candidate')


@unittest.skipUnless(os.name=='posix' and os.geteuid()==0,'isolated Linux root required')
class LinuxContainerRecycleRestoreTests(unittest.TestCase):
    def setUp(self):
        self.base=recycling.LinuxContainerRecycleTests();self.base.setUp();self.addCleanup(self.base.doCleanups)
        self.source=self.base.engine;self.session=self.base.session;self.ref=self.base.request();self.source.apply(self.ref,self.session)
        self.state=self.base.disk.disk/'restore-state';self.state.mkdir(mode=0o700);(self.state/'claims').mkdir(mode=0o700)
        self.engine=candidate.restores_type(recycling.directories.ContainerDirectories)(self.source,self.state)
        self.command=dict(source=copy.deepcopy(self.ref),historyDigest=self.base.record(self.ref)['historyDigest'])

    def request(self):
        return dict(operationId=uuid.uuid4().hex,owner='F'*64,plan=self.engine.plan(self.command,self.session))

    def own_record(self,q):return json.loads((self.state/(q['operationId']+'.json')).read_bytes())
    def claim(self):return self.state/'claims'/(self.ref['operationId']+'.json')
    def retained(self):return self.base.retained(self.ref)

    def kill_at(self,q,phase,source=False):
        child=os.fork()
        if child==0:
            try:
                obj=self.source if source else self.engine
                obj.checkpoint=lambda at:os.kill(os.getpid(),signal.SIGKILL) if at==phase else None
                self.engine.apply(q,self.session)
            except BaseException:os._exit(2)
            os._exit(3)
        fd=os.pidfd_open(child)
        try:
            ready=select.select([fd],[],[],20)[0]
            if not ready:signal.pidfd_send_signal(fd,signal.SIGKILL)
            _,status=os.waitpid(child,0);self.assertTrue(ready,'owned test child exceeded deadline')
        finally:os.close(fd)
        self.assertTrue(os.WIFSIGNALED(status));self.assertEqual(os.WTERMSIG(status),signal.SIGKILL)

    def test_plan_reads_exact_retained_inode_hdd_and_target_without_creating_journal_or_claim(self):
        source=(self.base.state/(self.ref['operationId']+'.json')).read_bytes();hdd=self.base.saved(self.ref);inode=self.source.metadata(self.retained().stat())
        q=self.request();p=q['plan'];self.assertEqual(p['retainedIdentity'],inode);self.assertEqual(p['privateIdentity'],self.base.record(self.ref)['privateIdentity'])
        self.assertEqual(p['command'],self.command);self.assertEqual(p['fingerprint'],self.engine.digest(p));self.assertEqual(source,(self.base.state/(self.ref['operationId']+'.json')).read_bytes());self.assertEqual(hdd,self.base.saved(self.ref))
        self.assertEqual(list(self.state.iterdir()),[self.state/'claims']);self.assertEqual(list((self.state/'claims').iterdir()),[]);self.assertFalse(self.base.leaf.exists())
        self.command['source']['plan']['attributes'][0][1]='changed';self.assertNotEqual(p['command'],self.command)

    def test_restore_has_distinct_durable_id_and_preserves_original_inode_bytes_ownership_and_history(self):
        q=self.request();before=self.retained().stat();digest=self.command['historyDigest'];result=self.engine.apply(q,self.session)
        self.assertEqual((result['state'],result['operationId'],result['sourceOperationId']),('Completed',q['operationId'],self.ref['operationId']))
        self.assertTrue(result['bindingCurrent']);self.assertTrue(result['restoredByThisOperation']);self.assertEqual(result['sourceState'],'Restored');self.assertEqual(result['historyDigest'],digest)
        after=self.base.leaf.stat();self.assertEqual((after.st_dev,after.st_ino,after.st_mode,after.st_uid,after.st_gid,after.st_size,after.st_mtime_ns),(before.st_dev,before.st_ino,before.st_mode,before.st_uid,before.st_gid,before.st_size,before.st_mtime_ns))
        self.assertEqual(self.base.leaf.read_bytes(),self.base.content);self.assertEqual(os.getxattr(self.base.leaf,'user.recycle'),b'original metadata');self.assertFalse(self.retained().exists())
        self.assertEqual(self.base.record(self.ref)['state'],'Restored');self.assertEqual(self.base.saved(self.ref)['captureState'],'Restored');self.assertTrue(self.claim().exists())
        self.assertEqual(stat.S_IMODE(self.claim().stat().st_mode),0o600)

    def test_replay_is_observe_only_and_does_not_attribute_another_restore_to_an_aborted_job(self):
        q=self.request();self.engine.discard(q,self.session);self.assertEqual(self.engine.apply(q,self.session)['state'],'Aborted')
        other=self.request();self.engine.apply(other,self.session)
        with patch.object(self.base.f,'exchange',side_effect=AssertionError('must not rename twice')):
            self.assertEqual(self.engine.apply(other,self.session)['state'],'Completed');result=self.engine.apply(q,self.session)
        self.assertEqual(result['state'],'Aborted');self.assertFalse(result['restoredByThisOperation'])

    def test_discard_before_apply_fences_only_new_restore_and_keeps_original_trash(self):
        q=self.request();old=(self.base.state/(self.ref['operationId']+'.json')).read_bytes();inode=self.source.metadata(self.retained().stat())
        result=self.engine.discard(q)
        self.assertEqual(result['state'],'Aborted');self.assertFalse(result['restoredByThisOperation']);self.assertIsNone(result['sourceRestored'])
        self.assertEqual(old,(self.base.state/(self.ref['operationId']+'.json')).read_bytes());self.assertEqual(self.source.metadata(self.retained().stat()),inode);self.assertFalse(self.claim().exists())
        with patch.object(self.base.f,'exchange',side_effect=AssertionError('late apply must be fenced')):self.assertEqual(self.engine.apply(q,self.session)['state'],'Aborted')

    def test_target_created_after_plan_is_not_overwritten_and_failure_has_a_permanent_no_write_fence(self):
        q=self.request();self.base.leaf.write_bytes(b'late target');before=self.source.metadata(self.retained().stat())
        with self.assertRaisesRegex(Exception,'TARGET_EXISTS'):self.engine.apply(q,self.session)
        self.assertEqual(self.base.leaf.read_bytes(),b'late target');self.assertEqual(self.source.metadata(self.retained().stat()),before)
        self.assertEqual(self.engine.inspect(q,self.session)['state'],'Aborted');self.assertFalse(self.claim().exists())

    def test_atomic_noreplace_conflict_after_restoring_marker_is_unknown_not_false_zero(self):
        q=self.request()
        def conflict(phase):
            if phase=='Restoring':self.base.leaf.write_bytes(b'late after marker')
        with patch.object(self.source,'checkpoint',side_effect=conflict),self.assertRaises(OSError):self.engine.apply(q,self.session)
        result=self.engine.inspect(q,self.session);self.assertEqual(result['state'],'Unknown');self.assertIsNone(result['restoredByThisOperation']);self.assertIsNone(result['sourceRetained']);self.assertIsNone(result['sourceRestored'])
        self.assertEqual(self.base.leaf.read_bytes(),b'late after marker');self.assertEqual(self.retained().read_bytes(),self.base.content);self.assertTrue(self.claim().exists())
        with patch.object(self.base.f,'exchange',side_effect=AssertionError('unknown must not resume')):self.assertEqual(self.engine.apply(q,self.session)['state'],'Unknown')

    def test_retained_identity_bytes_or_parent_changes_reject_before_any_restore(self):
        q=self.request();os.setxattr(self.retained(),'user.changed',b'new')
        with self.assertRaisesRegex(Exception,'SOURCE_CHANGED'):self.engine.apply(q,self.session)
        self.assertFalse(self.base.leaf.exists());self.assertEqual(self.engine.inspect(q,self.session)['state'],'Aborted')

    def test_same_source_operation_id_or_changed_plan_is_rejected(self):
        q=self.request();q['operationId']=self.ref['operationId']
        with self.assertRaisesRegex(Exception,'NEW_OPERATION_REQUIRED'):self.engine.apply(q,self.session)
        q=self.request();q['plan']['retainedIdentity'][1]+=1
        with self.assertRaisesRegex(Exception,'PLAN_INVALID'):self.engine.apply(q,self.session)

    def test_preparing_journal_crash_before_claim_is_a_permanent_no_write_fence(self):
        q=self.request();before=(self.base.state/(self.ref['operationId']+'.json')).read_bytes();self.kill_at(q,'Preparing')
        result=self.engine.inspect(q,self.session);self.assertEqual(result['state'],'Aborted');self.assertFalse(result['restoredByThisOperation']);self.assertFalse(self.claim().exists())
        self.assertEqual(before,(self.base.state/(self.ref['operationId']+'.json')).read_bytes());self.assertEqual(self.retained().read_bytes(),self.base.content)
        with patch.object(self.base.f,'exchange',side_effect=AssertionError('late apply must not resume')):self.assertEqual(self.engine.apply(q,self.session)['state'],'Aborted')

    def test_claim_created_before_applying_crash_aborts_without_renaming_and_releases_only_its_claim(self):
        q=self.request();self.kill_at(q,'SourceClaimed');self.assertTrue(self.claim().exists());self.assertEqual(self.own_record(q)['state'],'Preparing')
        result=self.engine.inspect(q,self.session);self.assertEqual(result['state'],'Aborted');self.assertFalse(result['restoredByThisOperation']);self.assertFalse(self.claim().exists());self.assertFalse(self.base.leaf.exists());self.assertEqual(self.retained().read_bytes(),self.base.content)

    def test_applying_marker_crash_aborts_and_late_apply_never_resumes(self):
        q=self.request();self.kill_at(q,'Applying');self.assertEqual(self.engine.discard(q,self.session)['state'],'Aborted')
        with patch.object(self.base.f,'exchange',side_effect=AssertionError('must not resume')):self.assertEqual(self.engine.apply(q,self.session)['state'],'Aborted')
        self.assertFalse(self.claim().exists());self.assertEqual(self.base.saved(self.ref)['captureState'],'Trashed')

    def test_original_restoring_marker_crash_before_rename_recovers_aborted_new_job(self):
        q=self.request();self.kill_at(q,'Restoring',source=True);self.assertEqual(self.base.record(self.ref)['state'],'Restoring')
        result=self.engine.inspect(q,self.session);self.assertEqual(result['state'],'Aborted');self.assertFalse(self.base.leaf.exists());self.assertEqual(self.base.record(self.ref)['state'],'Trashed')

    def test_real_namespace_rename_before_reply_crash_is_recovered_completed_without_replaying(self):
        q=self.request();self.kill_at(q,'RestoredNamespace',source=True);self.assertTrue(self.base.leaf.exists());self.assertFalse(self.retained().exists())
        with patch.object(self.base.f,'exchange',side_effect=AssertionError('never replay namespace move')):result=self.engine.inspect(q,self.session)
        self.assertEqual(result['state'],'Completed');self.assertTrue(result['restoredByThisOperation']);self.assertEqual(self.base.leaf.read_bytes(),self.base.content);self.assertEqual(self.base.saved(self.ref)['captureState'],'Restored')

    def test_source_completed_before_own_journal_crash_is_attributed_only_through_exact_claim(self):
        q=self.request();self.kill_at(q,'SourceReturned');self.assertEqual(self.own_record(q)['state'],'Applying');self.assertEqual(self.base.record(self.ref)['state'],'Restored')
        self.assertEqual(self.engine.inspect(q,self.session)['state'],'Completed')

    def test_matching_claim_blocks_legacy_restore_and_competing_new_job(self):
        q=self.request();other=copy.deepcopy(q);other['operationId']=uuid.uuid4().hex;self.kill_at(q,'Applying')
        with self.assertRaisesRegex(Exception,'RESTORE_CLAIMED'):self.source.restore(self.ref,self.session)
        with self.assertRaisesRegex(Exception,'SOURCE_BUSY'):self.engine.plan(self.command,self.session)
        result=self.engine.apply(other,self.session);self.assertEqual(result['state'],'Aborted');self.assertEqual(json.loads(self.claim().read_bytes())['restoreOperationId'],q['operationId'])
        self.assertEqual(self.engine.inspect(q,self.session)['state'],'Aborted');self.assertFalse(self.claim().exists())

    def test_lost_own_journal_or_claim_never_becomes_success_and_restores_the_known_source_fence(self):
        q=self.request();self.kill_at(q,'Applying');self.claim().unlink()
        result=self.engine.inspect(q,self.session);self.assertEqual(result['state'],'Unknown');self.assertIsNone(result['restoredByThisOperation']);self.assertTrue(self.claim().exists())
        with self.assertRaisesRegex(Exception,'RESTORE_CLAIMED'):self.source.restore(self.ref,self.session)
        (self.state/(q['operationId']+'.json')).unlink();self.assertEqual(self.engine.inspect(q,self.session)['state'],'Unknown')

    def test_separate_configured_legacy_engine_also_obeys_the_durable_claim(self):
        q=self.request();self.kill_at(q,'Applying')
        replica=recycling.candidate.recycling_type(recycling.directories.ContainerDirectories)(self.source.u,self.source.p,self.source.state_path,self.source.history,restore_claims_path=self.engine.claims_path)
        with self.assertRaisesRegex(Exception,'RESTORE_CLAIMED'):replica.restore(self.ref,self.session)
        self.assertFalse(self.base.leaf.exists());self.assertEqual(self.retained().read_bytes(),self.base.content)

    def test_parent_version_change_or_hdd_corruption_is_not_a_fresh_restore_plan(self):
        q=self.request();(self.base.root/'sibling').write_bytes(b'other')
        with self.assertRaisesRegex(Exception,'PLAN_CHANGED'):self.engine.apply(q,self.session)
        self.assertEqual(self.engine.inspect(q,self.session)['state'],'Aborted');self.assertFalse(self.claim().exists())
        fresh=self.request();data=self.base.target/(self.ref['operationId']+'.data');data.write_bytes(b'corrupt')
        with self.assertRaises(Exception):self.engine.apply(fresh,self.session)
        self.assertFalse(self.base.leaf.exists());self.assertEqual(self.retained().read_bytes(),self.base.content)

    def test_source_owner_digest_and_typed_plan_fields_cannot_be_substituted(self):
        for changed in ('owner','historyDigest'):
            command=copy.deepcopy(self.command)
            if changed=='owner':command['source']['owner']='E'*64
            else:command['historyDigest']='7'*64
            with self.subTest(changed=changed),self.assertRaises(Exception):self.engine.plan(command,self.session)
        q=self.request();q['plan']['uid']=True;q['plan']['fingerprint']=self.engine.digest(q['plan'])
        with self.assertRaisesRegex(Exception,'PLAN_INVALID'):self.engine.apply(q,self.session)
        with self.assertRaisesRegex(ValueError,'INDEPENDENT'):candidate.restores_type(recycling.directories.ContainerDirectories)(self.source,self.source.state_path)

    def test_missing_configured_claim_directory_fails_closed_even_for_separate_legacy_engine(self):
        q=self.request();self.kill_at(q,'Applying');self.claim().unlink();self.engine.claims_path.rmdir()
        with self.assertRaisesRegex(Exception,'STATE_UNSAFE'):self.source.restore(self.ref,self.session)
        with self.assertRaisesRegex(Exception,'STATE_UNSAFE'):self.engine.inspect(q,self.session)
        self.assertFalse(self.base.leaf.exists());self.assertEqual(self.retained().read_bytes(),self.base.content)

    def test_incoherent_journal_and_unknown_source_booleans_are_not_reported_as_success_or_zero(self):
        q=self.request();self.engine.discard(q,self.session);record=self.own_record(q);record['sourceOutcome']='Restored'
        with self.assertRaisesRegex(Exception,'RECEIPT_INVALID'):self.engine.validate_record(record,q['operationId'])
        q=self.request();self.kill_at(q,'Applying');record=self.own_record(q);record.update(state='Unknown',sourceOutcome='Restored')
        self.engine.validate_record(record,q['operationId']);result=self.engine.public(record,self.base.record(self.ref),self.session,True)
        self.assertIsNone(result['restoredByThisOperation']);self.assertIsNone(result['sourceRetained']);self.assertIsNone(result['sourceRestored'])

    def test_unsafe_claim_permissions_and_owner_plan_collision_fail_closed(self):
        q=self.request();self.kill_at(q,'Applying');self.claim().chmod(0o644)
        with self.assertRaisesRegex(Exception,'STATE_UNSAFE'):self.engine.inspect(q,self.session)
        self.assertFalse(self.base.leaf.exists());self.claim().chmod(0o600)
        bad=copy.deepcopy(q);bad['owner']='E'*64
        with self.assertRaisesRegex(Exception,'NOT_FOUND'):self.engine.apply(bad,self.session)
        bad=copy.deepcopy(q);bad['plan']['parentVersion']='7'*64;bad['plan']['fingerprint']=self.engine.digest(bad['plan'])
        with self.assertRaisesRegex(Exception,'IDEMPOTENCY_CONFLICT'):self.engine.apply(bad,self.session)

    def test_retained_loss_before_any_source_restoring_marker_does_not_attribute_a_restore(self):
        q=self.request();self.kill_at(q,'Applying');self.retained().rename(self.base.root/'removed-retained')
        result=self.engine.inspect(q,self.session);self.assertEqual(result['state'],'Aborted')
        # Applying was durable, but the original source journal was never Restoring.
        self.assertFalse(result['restoredByThisOperation']);self.assertFalse(self.base.leaf.exists())


if __name__=='__main__':unittest.main()
