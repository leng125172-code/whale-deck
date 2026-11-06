"""Actual temporary archive-to-new-inode I/O and owned fork/SIGKILL cuts.

Explicit container namespace/hardware-attestation doubles; not installed RPC.
"""
import copy
import hashlib
import json
import os
import select
import signal
import stat
import unittest
import uuid
from types import SimpleNamespace
from unittest.mock import patch
import test_container_recycle_archive_fences as fences
import test_container_file_staging as staging
from test_container_recycle_purge_plan import snapshot

candidate=staging.load('whaledeck-container-recycle-archive-copies.py','archive_copies_candidate')


@unittest.skipUnless(os.name=='posix' and os.geteuid()==0,'isolated Linux root required')
class ArchiveCopyTests(unittest.TestCase):
    def setUp(self):
        self.f=fences.ArchiveFenceTests();self.f.setUp();self.addCleanup(self.f.doCleanups)
        self.engine=candidate.archive_copies_type(type(self.f.engine))(self.f.p,self.f.state)
        self.q=self.f.q;self.session=self.f.session;self.base=self.f.base;self.source=self.f.p.source

    def protected(self):return [snapshot(p) for p in (self.base.state,self.f.f.f.f.state,self.base.target)]
    def private(self):return self.base.root/self.engine.stage_name(self.q)
    def record(self):return json.loads(self.f.journal().read_bytes())
    def reserve(self):return self.engine.reserve(self.q,self.session)
    def kill_at(self,phase):
        child=os.fork()
        if child==0:
            try:
                self.engine.checkpoint=lambda at:os.kill(os.getpid(),signal.SIGKILL) if at==phase else None
                self.engine.apply(self.q,self.session)
            except BaseException:os._exit(2)
            os._exit(3)
        fd=os.pidfd_open(child)
        try:
            ready=select.select([fd],[],[],20)[0]
            if not ready:signal.pidfd_send_signal(fd,signal.SIGKILL)
            _,status=os.waitpid(child,0)
        finally:os.close(fd)
        self.assertTrue(ready);self.assertTrue(os.WIFSIGNALED(status));self.assertEqual(os.WTERMSIG(status),signal.SIGKILL)

    def test_success_copies_new_inode_full_original_metadata_and_keeps_purge_hdd_unchanged(self):
        self.reserve();before=self.protected();result=self.engine.apply(self.q,self.session);old=self.engine.ref(self.q)['plan']['sourceIdentity'];actual=self.source.metadata(self.base.leaf.stat())
        self.assertEqual(result['state'],'Completed');self.assertTrue(result['archiveCopied']);self.assertTrue(result['bindingCurrent']);self.assertNotEqual(actual[:2],old[:2]);self.assertNotEqual(actual[:2],self.q['plan']['archiveIdentity'][:2]);self.assertEqual(actual[2:8],old[2:8]);self.assertEqual(self.base.leaf.read_bytes(),self.base.content);self.assertEqual(os.getxattr(self.base.leaf,'user.recycle'),b'original metadata');self.assertEqual(before,self.protected());self.assertTrue(self.f.claim().exists());self.assertEqual(list(self.private().iterdir()),[])
        private=self.private().stat();self.assertEqual((private.st_uid,private.st_gid,stat.S_IMODE(private.st_mode)),(0,0,0o700));self.assertEqual([private.st_dev,private.st_ino],self.record()['privateIdentity'])

    def test_no_implicit_reservation_or_apply_after_explicit_abort(self):
        with self.assertRaisesRegex(Exception,'NOT_RESERVED'):self.engine.apply(self.q,self.session)
        self.engine.discard(self.q);self.assertEqual(self.engine.apply(self.q,self.session)['state'],'Aborted');self.assertFalse(self.private().exists());self.assertFalse(self.base.leaf.exists())

    def test_success_replay_and_discard_never_copy_rename_or_delete_again(self):
        self.reserve();self.engine.apply(self.q,self.session);before=snapshot(self.base.root);protected=self.protected()
        with patch.object(self.engine.f,'exchange',side_effect=AssertionError('no publication replay')),patch.object(os,'write',side_effect=AssertionError('no payload rewrite')):
            for action in (self.engine.apply,self.engine.inspect,self.engine.discard):self.assertTrue(action(self.q,self.session)['archiveCopied'])
        self.assertEqual(before,snapshot(self.base.root));self.assertEqual(protected,self.protected())

    def test_existing_target_and_preoccupied_stage_are_preserved(self):
        self.reserve();self.base.leaf.write_bytes(b'foreign destination')
        with self.assertRaisesRegex(Exception,'TARGET_EXISTS'):self.engine.apply(self.q,self.session)
        self.assertEqual(self.base.leaf.read_bytes(),b'foreign destination');self.assertEqual(self.record()['state'],'Reserved');self.base.leaf.unlink()
        # Namespace changed: require a new plan rather than suppress its version.
        with self.assertRaisesRegex(Exception,'PLAN_CHANGED'):self.engine.apply(self.q,self.session)
        self.assertFalse(self.private().exists())

    def test_missing_session_does_not_enter_write_phase(self):
        self.reserve()
        with self.assertRaisesRegex(Exception,'SESSION_REQUIRED'):self.engine.apply(self.q,None)
        self.assertEqual(self.record()['state'],'Reserved');self.assertFalse(self.private().exists())

    def test_every_interrupted_write_phase_is_unknown_and_observation_never_resumes(self):
        for phase in ('Applying','PrivateCreated','PrivateBound','Copying','CopyChunk','Prepared','Publishing','PublishedNamespace','Published'):
            with self.subTest(phase=phase):
                other=ArchiveCopyTests();other.setUp()
                try:
                    other.reserve();protected=other.protected();other.kill_at(phase);namespace=snapshot(other.base.root)
                    with patch.object(other.engine.f,'exchange',side_effect=AssertionError('never resume publish')),patch.object(os,'write',side_effect=AssertionError('never resume copy')):
                        for action in (other.engine.inspect,other.engine.discard,other.engine.apply):
                            r=action(other.q,None);self.assertEqual(r['state'],'Unknown');self.assertIsNone(r['archiveCopied']);self.assertIsNone(r['bindingCurrent']);self.assertEqual(namespace,snapshot(other.base.root))
                    self.assertEqual(protected,other.protected());self.assertTrue(other.f.claim().exists())
                finally:other.doCleanups()

    def test_completed_survives_sigkill_and_cold_engine_reads_new_inode(self):
        self.reserve();self.kill_at('Completed');engine=candidate.archive_copies_type(type(self.f.engine))(self.f.p,self.f.state)
        r=engine.inspect(self.q,self.session);self.assertEqual(r['state'],'Completed');self.assertTrue(r['archiveCopied']);self.assertTrue(r['bindingCurrent'])

    def test_foreign_target_appearing_at_publish_is_never_overwritten(self):
        self.reserve()
        def cut(phase):
            if phase=='Publishing':self.base.leaf.write_bytes(b'late foreign target')
        with patch.object(self.engine,'checkpoint',side_effect=cut),self.assertRaises(Exception):self.engine.apply(self.q,self.session)
        self.assertEqual(self.base.leaf.read_bytes(),b'late foreign target');self.assertEqual(self.record()['state'],'Unknown');self.assertTrue((self.private()/'copy.partial').is_file())

    def test_noreplace_errno_keeps_stage_bytes_and_unknown_protection(self):
        self.reserve()
        with patch.object(self.engine.f,'exchange',side_effect=FileExistsError('owned publication fault')),self.assertRaises(FileExistsError):self.engine.apply(self.q,self.session)
        self.assertEqual(self.record()['state'],'Unknown');self.assertFalse(self.base.leaf.exists());self.assertEqual((self.private()/'copy.partial').read_bytes(),self.base.content);self.assertTrue(self.f.claim().exists())

    def test_namespace_change_during_mkdir_is_not_hidden_by_own_parent_version(self):
        self.reserve()
        def cut(phase):
            if phase=='PrivateCreated':(self.base.root/'foreign-neighbor').write_bytes(b'preserve')
        with patch.object(self.engine,'checkpoint',side_effect=cut),self.assertRaisesRegex(Exception,'PARENT_CHANGED'):self.engine.apply(self.q,self.session)
        self.assertEqual((self.base.root/'foreign-neighbor').read_bytes(),b'preserve');self.assertFalse(self.base.leaf.exists());self.assertEqual(self.record()['state'],'Unknown')

    def test_namespace_change_after_stage_is_rejected_before_publish(self):
        self.reserve()
        def cut(phase):
            if phase=='Prepared':(self.base.root/'foreign-neighbor').mkdir()
        with patch.object(self.engine,'checkpoint',side_effect=cut),self.assertRaisesRegex(Exception,'PARENT_CHANGED'):self.engine.apply(self.q,self.session)
        self.assertTrue((self.base.root/'foreign-neighbor').is_dir());self.assertFalse(self.base.leaf.exists());self.assertEqual(self.record()['state'],'Unknown')

    def test_same_data_archive_replacement_mid_copy_is_rejected(self):
        self.reserve();data=self.f.f.f.hdd();held=data.with_suffix('.held')
        def cut(phase):
            if phase=='Copying':data.rename(held);data.write_bytes(held.read_bytes());data.chmod(0o600)
        with patch.object(self.engine,'checkpoint',side_effect=cut),self.assertRaisesRegex(Exception,'HISTORY_CHANGED'):self.engine.apply(self.q,self.session)
        self.assertEqual(self.record()['state'],'Unknown');self.assertFalse(self.base.leaf.exists());self.assertEqual(data.read_bytes(),self.base.content)

    def test_short_writes_are_completed_but_write_failure_retains_partial_stage(self):
        self.reserve();write=os.write
        def short(fd,data):return write(fd,data[:max(1,len(data)//2)])
        with patch.object(os,'write',side_effect=short):self.assertTrue(self.engine.apply(self.q,self.session)['archiveCopied'])

    def test_write_error_and_fsync_error_do_not_delete_or_publish(self):
        for syscall in ('write','fsync'):
            other=ArchiveCopyTests();other.setUp()
            try:
                other.reserve()
                with patch.object(os,syscall,side_effect=OSError('owned '+syscall+' failure')),self.assertRaises(OSError):other.engine.apply(other.q,other.session)
                # fsync may fail while committing the initial Applying journal;
                # no recovery path may synthesize success or publish a payload.
                self.assertFalse(other.base.leaf.exists());self.assertTrue(other.f.claim().exists())
            finally:other.doCleanups()

    def test_mutated_staged_bytes_or_attributes_are_not_published(self):
        self.reserve()
        def cut(phase):
            if phase=='Prepared':os.setxattr(self.private()/'copy.partial','user.unexpected',b'changed')
        with patch.object(self.engine,'checkpoint',side_effect=cut),self.assertRaisesRegex(Exception,'COPY_CHANGED'):self.engine.apply(self.q,self.session)
        self.assertFalse(self.base.leaf.exists());self.assertEqual(self.record()['state'],'Unknown')

    def test_completed_binding_loss_does_not_recopy_or_erase_historical_result(self):
        self.reserve();self.engine.apply(self.q,self.session);moved=self.base.leaf.with_suffix('.moved');self.base.leaf.rename(moved)
        r=self.engine.inspect(self.q,self.session);self.assertTrue(r['archiveCopied']);self.assertFalse(r['bindingCurrent']);self.assertEqual(r['state'],'Completed');self.assertFalse(self.base.leaf.exists());self.assertEqual(moved.read_bytes(),self.base.content)

    def test_inspect_without_session_never_claims_current_binding(self):
        self.reserve();self.engine.apply(self.q,self.session);before=snapshot(self.base.root);r=self.engine.inspect(self.q);self.assertTrue(r['archiveCopied']);self.assertIsNone(r['bindingCurrent']);self.assertEqual(before,snapshot(self.base.root))

    def test_capacity_refusal_does_not_create_stage(self):
        self.reserve()
        with patch.object(os,'fstatvfs',return_value=SimpleNamespace(f_bavail=1,f_frsize=4096,f_flag=0)),self.assertRaisesRegex(Exception,'COPY_CAPACITY'):self.engine.apply(self.q,self.session)
        self.assertFalse(self.private().exists());self.assertEqual(self.record()['state'],'Reserved')

    def test_structurally_invalid_publication_receipt_cannot_claim_success(self):
        self.reserve();self.engine.apply(self.q,self.session);good=self.record()
        for key,value in (('publishedIdentity',None),('pendingIdentity',[1,2]),('preparedIdentity',good['plan']['deletedIdentity']),('parentAfterStage','')):
            bad=copy.deepcopy(good);bad[key]=value
            with self.subTest(key=key),self.assertRaisesRegex(Exception,'RECEIPT_INVALID'):self.engine.validate_record(bad,bad['operationId'])

    def test_stage_hardlink_before_copy_is_rejected_without_writing_alias(self):
        self.reserve();alias=self.base.disk.disk/'foreign-alias'
        def cut(phase):
            if phase=='Copying':os.link(self.private()/'copy.partial',alias)
        with patch.object(self.engine,'checkpoint',side_effect=cut),self.assertRaisesRegex(Exception,'COPY_CHANGED'):self.engine.apply(self.q,self.session)
        self.assertEqual(alias.read_bytes(),b'');self.assertFalse(self.base.leaf.exists());self.assertEqual(self.record()['state'],'Unknown')

    def test_pinned_storage_configuration_change_before_publish_is_rejected(self):
        self.reserve()
        def cut(phase):
            if phase=='Prepared':
                path=self.base.disk.configuration;value=json.loads(path.read_bytes());value['maxVersions']-=1;path.write_text(json.dumps(value))
        with patch.object(self.engine,'checkpoint',side_effect=cut),self.assertRaisesRegex(Exception,'HISTORY_CHANGED'):self.engine.apply(self.q,self.session)
        self.assertFalse(self.base.leaf.exists());self.assertEqual(self.record()['state'],'Unknown')

    def test_completed_copy_claim_loss_repairs_protection_without_republishing(self):
        self.reserve();self.engine.apply(self.q,self.session);self.f.claim().unlink();before=snapshot(self.base.root)
        r=self.engine.inspect(self.q,self.session);self.assertEqual(r['state'],'Completed');self.assertTrue(r['archiveCopied']);self.assertTrue(r['bindingCurrent']);self.assertEqual(before,snapshot(self.base.root));self.assertTrue(self.f.claim().exists())

    def test_unknown_no_session_repairs_only_exact_lost_claim_not_payload(self):
        self.reserve();self.kill_at('Prepared');self.f.claim().unlink();before=snapshot(self.base.root)
        r=self.engine.inspect(self.q);self.assertEqual(r['state'],'Unknown');self.assertIsNone(r['bindingCurrent']);self.assertIsNone(r['archiveCopied']);self.assertEqual(json.loads(self.f.claim().read_bytes()),self.engine.claim(self.q));self.assertEqual(before,snapshot(self.base.root))

    def test_completed_no_session_repairs_only_exact_claim_and_keeps_historical_result(self):
        self.reserve();self.engine.apply(self.q,self.session);self.f.claim().unlink();before=snapshot(self.base.root);record=self.record()
        r=self.engine.inspect(self.q);self.assertEqual(r['state'],'Completed');self.assertTrue(r['archiveCopied']);self.assertIsNone(r['bindingCurrent']);self.assertEqual(record,self.record());self.assertEqual(before,snapshot(self.base.root));self.assertEqual(json.loads(self.f.claim().read_bytes()),self.engine.claim(self.q))

    def test_completed_with_foreign_claim_keeps_history_but_never_verifies_current_binding(self):
        self.reserve();self.engine.apply(self.q,self.session);record=self.record();before=snapshot(self.base.root)
        claim=json.loads(self.f.claim().read_bytes());claim.update(restoreOperationId=uuid.uuid4().hex,restoreOwner='B'*64,restoreFingerprint='B'*64);self.f.claim().write_text(json.dumps(claim));raw=self.f.claim().read_bytes()
        result=self.engine.inspect(self.q,self.session);self.assertEqual(result['state'],'Completed');self.assertTrue(result['archiveCopied']);self.assertTrue(result['sourceFenced']);self.assertFalse(result['ownedFence']);self.assertFalse(result['bindingCurrent']);self.assertEqual(record,self.record());self.assertEqual(before,snapshot(self.base.root));self.assertEqual(raw,self.f.claim().read_bytes())


if __name__=='__main__':unittest.main()
