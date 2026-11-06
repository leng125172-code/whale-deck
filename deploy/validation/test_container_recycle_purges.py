"""Temporary Linux file/journal/HDD I/O, with namespace/attestation doubles.

Destructive operations touch only this test's generated retained file. These
checks are not installation, RPC, physical HDD or permanent business acceptance.
"""
import copy
import json
import os
import re
import select
import signal
import unittest
import uuid
from unittest.mock import patch

import test_container_recycle_purge_fences as fences
import test_container_file_staging as staging

candidate=staging.load('whaledeck-container-recycle-purges.py','container_purges_candidate')


@unittest.skipUnless(os.name=='posix' and os.geteuid()==0,'isolated Linux root required')
class PurgeTests(unittest.TestCase):
    def setUp(self):
        self.f=fences.PurgeFenceTests();self.f.setUp();self.addCleanup(self.f.doCleanups)
        self.native=self.f.f;self.base=self.native.base;self.source=self.native.source;self.session=self.native.session
        self.engine=candidate.purges_type(type(self.f.engine))(self.f.p,self.f.state)

    def request(self):return self.f.request()
    def record(self,q):return json.loads(self.f.journal(q).read_text())
    def pending(self,q):return self.native.retained().parent/(q['operationId']+'.purging')
    def hdd(self):return self.base.target/(self.native.ref['operationId']+'.data')
    def kill_at(self,q,phase):
        child=os.fork()
        if child==0:
            try:
                self.engine.checkpoint=lambda at:os.kill(os.getpid(),signal.SIGKILL) if at==phase else None
                self.engine.apply(q,self.session)
            except BaseException:os._exit(2)
            os._exit(3)
        fd=os.pidfd_open(child)
        try:
            ready=select.select([fd],[],[],20)[0]
            if not ready:signal.pidfd_send_signal(fd,signal.SIGKILL)
            _,status=os.waitpid(child,0)
        finally:os.close(fd)
        self.assertTrue(ready);self.assertTrue(os.WIFSIGNALED(status));self.assertEqual(os.WTERMSIG(status),signal.SIGKILL)

    def test_success_removes_only_selected_original_preserves_archive_and_durable_tombstones(self):
        q=self.request();data=self.hdd().read_bytes();archive=self.source.metadata(self.hdd().stat());history=self.base.saved(self.native.ref);neighbor=self.native.retained().parent/'unrelated.bin';neighbor.write_bytes(b'preserve neighbor')
        self.engine.reserve(q,self.session);r=self.engine.apply(q,self.session)
        self.assertEqual(r['state'],'Completed');self.assertTrue(r['purgeApplied']);self.assertTrue(r['ownedFence']);self.assertFalse(self.native.retained().exists());self.assertFalse(self.pending(q).exists());self.assertFalse(self.base.leaf.exists());self.assertEqual(neighbor.read_bytes(),b'preserve neighbor')
        self.assertEqual(self.hdd().read_bytes(),data);self.assertEqual(self.source.metadata(self.hdd().stat()),archive)
        new=self.base.saved(self.native.ref);self.assertEqual(new,{**history,'captureState':'Purged'});self.assertEqual(self.base.record(self.native.ref)['state'],'Purged');self.assertEqual(json.loads(self.f.marker().read_text())['state'],'Purged')
        self.assertEqual(self.source.inspect(self.native.ref,self.session)['state'],'Purged')
        for fn in (lambda:self.source.restore(self.native.ref,self.session),lambda:self.native.engine.plan(self.native.command,self.session)):
            with self.assertRaises(Exception):fn()

    def test_no_implicit_reservation_or_apply_after_cancel(self):
        q=self.request()
        with self.assertRaisesRegex(Exception,'NOT_RESERVED'):self.engine.apply(q,self.session)
        self.assertTrue(self.native.retained().exists());self.engine.reserve(q,self.session);self.engine.discard(q,self.session)
        with patch.object(os,'unlink',side_effect=AssertionError('must not delete after cancel')):self.assertEqual(self.engine.apply(q,self.session)['state'],'Aborted')

    def test_replay_and_discard_after_success_never_repeat_unlink_or_rewrite_archive(self):
        q=self.request();self.engine.reserve(q,self.session);self.engine.apply(q,self.session);saved=self.f.marker().read_bytes();history=self.base.saved(self.native.ref)
        with patch.object(os,'unlink',side_effect=AssertionError('success may only be observed')):
            for action in (lambda:self.engine.apply(q,self.session),lambda:self.engine.inspect(q),lambda:self.engine.discard(q,self.session),lambda:self.engine.reserve(q,self.session)):
                self.assertEqual(action()['state'],'Completed')
        self.assertEqual(saved,self.f.marker().read_bytes());self.assertEqual(history,self.base.saved(self.native.ref))

    def test_hdd_corruption_after_reservation_rejects_before_irreversible_marker(self):
        q=self.request();self.engine.reserve(q,self.session);self.hdd().write_bytes(b'corrupt')
        with self.assertRaises(Exception):self.engine.apply(q,self.session)
        self.assertEqual(self.record(q)['state'],'Reserved');self.assertEqual(self.base.record(self.native.ref)['state'],'Trashed');self.assertEqual(self.native.retained().read_bytes(),self.base.content)

    def test_new_restore_claim_after_reservation_blocks_apply_without_touching_either_copy(self):
        q=self.request();r=self.native.request();self.engine.reserve(q,self.session)
        # Explicit disk corruption/lost-fence simulation, not a permitted producer.
        with self.native.engine.claims() as d:self.base.f.save_receipt(d,self.native.ref['operationId'],self.native.engine.claim(r))
        with self.assertRaisesRegex(Exception,'SOURCE_BUSY'):self.engine.apply(q,self.session)
        self.assertEqual(self.record(q)['state'],'Reserved');self.assertEqual(self.native.retained().read_bytes(),self.base.content)

    def test_atomic_quarantine_never_overwrites_an_existing_file(self):
        q=self.request();self.engine.reserve(q,self.session);self.pending(q).write_bytes(b'foreign collision')
        with self.assertRaises(OSError):self.engine.apply(q,self.session)
        self.assertEqual(self.pending(q).read_bytes(),b'foreign collision');self.assertEqual(self.native.retained().read_bytes(),self.base.content);self.assertEqual(self.engine.inspect(q)['state'],'Unknown')

    def test_replacement_quarantine_and_foreign_target_are_not_deleted(self):
        q=self.request();self.engine.reserve(q,self.session);held=self.pending(q).with_suffix('.held')
        def change(phase):
            if phase=='QuarantinedNamespace':self.pending(q).rename(held);self.pending(q).write_bytes(b'foreign pending');self.base.leaf.write_bytes(b'foreign target')
        with patch.object(self.engine,'checkpoint',side_effect=change),self.assertRaises(Exception):self.engine.apply(q,self.session)
        self.assertEqual(held.read_bytes(),self.base.content);self.assertEqual(self.pending(q).read_bytes(),b'foreign pending');self.assertEqual(self.base.leaf.read_bytes(),b'foreign target');self.assertIsNone(self.engine.inspect(q)['purgeApplied'])

    def test_every_interrupted_phase_stays_unknown_and_never_resumes_removal(self):
        # Separate fixtures per phase; these are real SIGKILLs, not exceptions.
        phases=('Applying','DeletingFenced','SourcePurging','QuarantinedNamespace','Quarantined','Removing','Unlinked','Removed','HistoryPurged','SourcePurged','PurgedFenced')
        for phase in phases:
            with self.subTest(phase=phase):
                other=PurgeTests();other.setUp()
                try:
                    q=other.request();other.engine.reserve(q,other.session);hdd=other.hdd().read_bytes();other.kill_at(q,phase)
                    before={p.name:p.read_bytes() for p in other.native.retained().parent.iterdir() if p.is_file()}
                    # A fresh engine rereads disk; inspect/apply/discard cannot delete.
                    engine=candidate.purges_type(type(other.f.engine))(other.f.p,other.f.state)
                    unlink=os.unlink
                    allowed={(p.stat().st_dev,p.stat().st_ino) for p in (other.f.state,other.base.state)}
                    def metadata_temporary_only(name,*,dir_fd=None):
                        if dir_fd is None or not re.fullmatch(r'\.[a-f0-9]{32}(?:\.purge)?-[a-f0-9]{32}',str(name)) or (os.fstat(dir_fd).st_dev,os.fstat(dir_fd).st_ino) not in allowed:raise AssertionError('interrupted purge must not resume payload unlink')
                        return unlink(name,dir_fd=dir_fd)
                    with patch.object(os,'unlink',side_effect=metadata_temporary_only):
                        for action in (lambda:engine.inspect(q),lambda:engine.apply(q,other.session),lambda:engine.discard(q,other.session)):
                            r=action();self.assertEqual(r['state'],'Unknown');self.assertIsNone(r['purgeApplied']);self.assertTrue(r['ownedFence'])
                    self.assertEqual(before,{p.name:p.read_bytes() for p in other.native.retained().parent.iterdir() if p.is_file()});self.assertEqual(other.hdd().read_bytes(),hdd)
                finally:other.doCleanups()

    def test_crash_after_completed_is_success_and_never_resumes(self):
        q=self.request();self.engine.reserve(q,self.session);self.kill_at(q,'Completed')
        with patch.object(os,'unlink',side_effect=AssertionError('no replay')):self.assertTrue(self.engine.inspect(q)['purgeApplied'])
        self.assertFalse(self.native.retained().exists());self.assertEqual(self.hdd().read_bytes(),self.base.content)

    def test_orphan_completed_marker_is_unknown_not_reconstructed_success(self):
        q=self.request();self.engine.reserve(q,self.session);self.engine.apply(q,self.session);self.f.journal(q).unlink()
        r=self.engine.inspect(q);self.assertEqual(r['state'],'Unknown');self.assertIsNone(r['purgeApplied']);self.assertTrue(r['ownedFence']);self.assertEqual(self.hdd().read_bytes(),self.base.content)

    def test_completed_journal_with_missing_fence_does_not_lose_protection(self):
        q=self.request();self.engine.reserve(q,self.session);self.engine.apply(q,self.session);self.f.marker().unlink()
        r=self.engine.inspect(q);self.assertEqual(r['state'],'Unknown');self.assertTrue(r['ownedFence']);self.assertEqual(self.base.record(self.native.ref)['state'],'Purged')

    def test_deletion_phases_cannot_be_canceled_by_old_reservation_reader(self):
        q=self.request();self.engine.reserve(q,self.session);self.kill_at(q,'Removing')
        with self.assertRaises(Exception):self.f.engine.discard(q,self.session)
        self.assertEqual(json.loads(self.f.marker().read_text())['state'],'Deleting');self.assertTrue(self.pending(q).exists())

    def test_unlink_error_keeps_pending_file_and_unknown_fence(self):
        q=self.request();self.engine.reserve(q,self.session);unlink=os.unlink
        def refuse(name,**kwargs):
            if name==q['operationId']+'.purging':raise OSError('owned test unlink fault')
            return unlink(name,**kwargs)
        with patch.object(os,'unlink',side_effect=refuse),self.assertRaises(OSError):self.engine.apply(q,self.session)
        self.assertEqual(self.pending(q).read_bytes(),self.base.content);self.assertIsNone(self.engine.inspect(q)['purgeApplied']);self.assertEqual(self.hdd().read_bytes(),self.base.content)

    def test_history_metadata_failure_after_unlink_is_unknown_and_does_not_repeat_delete(self):
        q=self.request();self.engine.reserve(q,self.session);save=self.base.f.save_receipt
        def fail(directory,operation,value):
            if value.get('captureState')=='Purged':raise OSError('owned test metadata fault')
            return save(directory,operation,value)
        with patch.object(self.base.f,'save_receipt',side_effect=fail),self.assertRaises(OSError):self.engine.apply(q,self.session)
        self.assertFalse(self.pending(q).exists());self.assertFalse(self.native.retained().exists());self.assertEqual(self.base.saved(self.native.ref)['captureState'],'Trashed')
        self.assertEqual(self.record(q)['state'],'Unknown');self.assertTrue(self.record(q)['unlinkConfirmed']);self.assertIsNone(self.engine.inspect(q)['purgeApplied']);self.assertEqual(self.hdd().read_bytes(),self.base.content)

    def test_quarantine_fsync_failure_is_unknown_and_keeps_original_bytes(self):
        q=self.request();self.engine.reserve(q,self.session);fsync=os.fsync;private=self.native.retained().parent.stat()
        def fail(fd):
            info=os.fstat(fd)
            if (info.st_dev,info.st_ino)==(private.st_dev,private.st_ino):raise OSError('owned test fsync fault')
            return fsync(fd)
        with patch.object(os,'fsync',side_effect=fail),self.assertRaises(OSError):self.engine.apply(q,self.session)
        self.assertEqual(self.pending(q).read_bytes(),self.base.content);self.assertEqual(self.engine.inspect(q)['state'],'Unknown');self.assertEqual(self.hdd().read_bytes(),self.base.content)

    def test_missing_session_and_changed_original_are_rejected_before_deletion_phase(self):
        q=self.request();self.engine.reserve(q,self.session)
        with self.assertRaisesRegex(Exception,'SESSION_REQUIRED'):self.engine.apply(q,None)
        os.setxattr(self.native.retained(),'user.changed',b'changed')
        with self.assertRaises(Exception):self.engine.apply(q,self.session)
        self.assertEqual(self.record(q)['state'],'Reserved');self.assertTrue(self.native.retained().exists());self.assertFalse(self.pending(q).exists())


if __name__=='__main__':unittest.main()
