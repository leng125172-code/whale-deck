"""Real temporary journals, flock, fsync, inode I/O and owned child SIGKILL.

Container namespace/HDD attestation doubles; not installed or RPC acceptance.
No payload deletion is implemented or exercised by this reservation layer.
"""
import copy
import json
import os
import select
import signal
import unittest
import uuid
from unittest.mock import patch

import test_container_file_recycle_restores as native
import test_container_file_recycling as recycling
import test_container_file_staging as staging
import test_container_recycle_purge_plan as plans

candidate=staging.load('whaledeck-container-recycle-purge-fences.py','purge_fences_candidate')


@unittest.skipUnless(os.name=='posix' and os.geteuid()==0,'isolated Linux root required')
class PurgeFenceTests(unittest.TestCase):
    def setUp(self):
        self.f=native.LinuxContainerRecycleRestoreTests();self.f.setUp();self.addCleanup(self.f.doCleanups)
        self.p=plans.candidate.ContainerRecyclePurgePreflight(self.f.engine)
        self.state=self.f.base.disk.disk/'purge-state';self.state.mkdir(mode=0o700)
        self.engine=candidate.purge_fences_type(recycling.directories.ContainerDirectories)(self.p,self.state)
        self.command=dict(**self.f.command,scope='Originals')

    def request(self):return dict(operationId=uuid.uuid4().hex,owner='D'*64,plan=self.p.plan(self.command,self.f.session))
    def marker(self):return self.f.base.state/(self.f.ref['operationId']+'.purge.json')
    def journal(self,q):return self.state/(q['operationId']+'.json')
    def snapshot(self):return [plans.snapshot(p) for p in (self.f.retained().parent,self.f.base.target,self.f.state)]
    def kill_at(self,q,phase,discard=False):
        child=os.fork()
        if child==0:
            try:
                self.engine.checkpoint=lambda at:os.kill(os.getpid(),signal.SIGKILL) if at==phase else None
                (self.engine.discard if discard else self.engine.reserve)(q,self.f.session)
            except BaseException:os._exit(2)
            os._exit(3)
        fd=os.pidfd_open(child)
        try:
            ready=select.select([fd],[],[],20)[0]
            if not ready:signal.pidfd_send_signal(fd,signal.SIGKILL)
            _,status=os.waitpid(child,0)
        finally:os.close(fd)
        self.assertTrue(ready);self.assertTrue(os.WIFSIGNALED(status));self.assertEqual(os.WTERMSIG(status),signal.SIGKILL)

    def test_reservation_preserves_payloads_and_blocks_old_and_new_restore(self):
        q=self.request();restore=self.f.request();before=self.snapshot();source=(self.f.base.state/(self.f.ref['operationId']+'.json')).read_bytes()
        r=self.engine.reserve(q,self.f.session);self.assertEqual(r['state'],'Reserved');self.assertTrue(r['ownedFence']);self.assertFalse(r['purgeApplied'])
        self.assertEqual(before,self.snapshot());self.assertEqual(source,(self.f.base.state/(self.f.ref['operationId']+'.json')).read_bytes())
        for action in (lambda:self.f.source.restore(self.f.ref,self.f.session),lambda:self.f.engine.plan(self.f.command,self.f.session),lambda:self.f.engine.apply(restore,self.f.session),lambda:self.f.engine.write_claim(restore),lambda:self.p.plan(self.command,self.f.session)):
            with self.assertRaisesRegex(Exception,'PURGE_CLAIMED'):action()
        self.assertFalse(self.f.claim().exists());self.assertEqual(before[:2],self.snapshot()[:2]);self.assertEqual(self.f.own_record(restore)['state'],'Aborted')

    def test_explicit_discard_keeps_tombstone_and_allows_fresh_restore(self):
        q=self.request();self.engine.reserve(q,self.f.session);r=self.engine.discard(q,self.f.session)
        self.assertEqual(r['state'],'Aborted');self.assertFalse(r['sourceFenced']);self.assertEqual(json.loads(self.marker().read_text())['state'],'Released')
        with patch.object(self.p,'plan_locked',side_effect=AssertionError('late reserve must observe')):self.assertEqual(self.engine.reserve(q,self.f.session)['state'],'Aborted')
        self.assertEqual(self.f.engine.apply(self.f.request(),self.f.session)['state'],'Completed')

    def test_released_marker_fences_same_id_after_journal_loss(self):
        q=self.request();self.engine.reserve(q,self.f.session);self.engine.discard(q,self.f.session);self.journal(q).unlink()
        self.assertEqual(self.engine.reserve(q,self.f.session)['state'],'Aborted');self.assertEqual(json.loads(self.marker().read_text())['state'],'Released')

    def test_new_reservation_after_release_does_not_let_old_discard_release_it(self):
        q=self.request();self.engine.reserve(q,self.f.session);self.engine.discard(q,self.f.session)
        other=self.request();self.engine.reserve(other,self.f.session);raw=self.marker().read_bytes()
        self.engine.discard(q,self.f.session);self.assertEqual(raw,self.marker().read_bytes());self.assertEqual(self.engine.inspect(other)['state'],'Reserved')

    def test_second_id_or_owner_cannot_take_an_existing_reservation(self):
        q=self.request();other=copy.deepcopy(q);other['operationId']=uuid.uuid4().hex;self.engine.reserve(q,self.f.session);raw=self.marker().read_bytes()
        with self.assertRaisesRegex(Exception,'SOURCE_BUSY'):self.engine.reserve(other,self.f.session)
        bad=copy.deepcopy(q);bad['owner']='E'*64
        with self.assertRaisesRegex(Exception,'NOT_FOUND'):self.engine.discard(bad,self.f.session)
        self.assertEqual(raw,self.marker().read_bytes())

    def test_existing_restore_claim_and_completed_restore_win(self):
        q=self.request();r=self.f.request();self.f.engine.write_claim(r)
        with self.assertRaisesRegex(Exception,'SOURCE_BUSY'):self.engine.reserve(q,self.f.session)
        self.assertFalse(self.marker().exists());self.assertFalse(self.journal(q).exists());self.f.engine.clear_claim(r)
        self.f.engine.apply(r,self.f.session)
        with self.assertRaises(Exception):self.engine.reserve(q,self.f.session)
        self.assertFalse(self.marker().exists());self.assertEqual(self.f.base.leaf.read_bytes(),self.f.base.content)

    def test_crash_before_source_fence_aborts_and_never_late_reserves(self):
        q=self.request();before=self.snapshot();self.kill_at(q,'Preparing')
        self.assertEqual(self.engine.inspect(q)['state'],'Aborted');self.assertFalse(self.marker().exists())
        self.assertEqual(self.engine.reserve(q,self.f.session)['state'],'Aborted');self.assertEqual(before,self.snapshot())

    def test_crash_after_source_fence_before_journal_commit_stays_unknown(self):
        q=self.request();before=self.snapshot();self.kill_at(q,'SourceFenced')
        self.assertEqual(self.engine.inspect(q)['state'],'Unknown');self.assertEqual(self.engine.discard(q,self.f.session)['state'],'Unknown')
        self.assertEqual(json.loads(self.marker().read_text())['state'],'Unknown');self.assertEqual(before,self.snapshot())
        with self.assertRaisesRegex(Exception,'PURGE_CLAIMED'):self.f.source.restore(self.f.ref,self.f.session)

    def test_committed_reservation_survives_process_exit_without_resuming_work(self):
        q=self.request();self.kill_at(q,'Reserved');before=self.snapshot();raw=self.marker().read_bytes()
        with patch.object(self.p,'plan_locked',side_effect=AssertionError('inspect and replay only observe')):
            self.assertEqual(self.engine.inspect(q)['state'],'Reserved');self.assertEqual(self.engine.reserve(q,self.f.session)['state'],'Reserved')
        self.assertEqual(before,self.snapshot());self.assertEqual(raw,self.marker().read_bytes())

    def test_crash_after_durable_abort_finishes_only_release(self):
        q=self.request();self.engine.reserve(q,self.f.session);self.kill_at(q,'AbortedSaved',True)
        self.assertEqual(json.loads(self.marker().read_text())['state'],'Reserved')
        self.assertEqual(self.engine.inspect(q)['state'],'Aborted');self.assertEqual(json.loads(self.marker().read_text())['state'],'Released')
        self.assertEqual(self.f.source.restore(self.f.ref,self.f.session)['state'],'Restored')

    def test_crash_after_release_is_terminal_and_does_not_reacquire(self):
        q=self.request();self.engine.reserve(q,self.f.session);self.kill_at(q,'Released',True)
        self.assertEqual(self.engine.reserve(q,self.f.session)['state'],'Aborted');self.assertEqual(json.loads(self.marker().read_text())['state'],'Released')

    def test_missing_source_marker_becomes_unknown_and_reestablishes_protection(self):
        q=self.request();self.engine.reserve(q,self.f.session);self.marker().unlink()
        r=self.engine.inspect(q);self.assertEqual(r['state'],'Unknown');self.assertTrue(r['ownedFence']);self.assertEqual(self.engine.reserve(q,self.f.session)['state'],'Unknown')
        with self.assertRaisesRegex(Exception,'PURGE_CLAIMED'):self.f.source.restore(self.f.ref,self.f.session)

    def test_orphan_marker_never_reconstructs_success_and_discard_cannot_release_it(self):
        q=self.request();self.engine.reserve(q,self.f.session);self.journal(q).unlink()
        self.assertEqual(self.engine.discard(q,self.f.session)['state'],'Unknown');self.assertEqual(json.loads(self.marker().read_text())['state'],'Unknown')

    def test_inspect_released_orphan_preserves_durable_cancellation(self):
        q=self.request();self.engine.reserve(q,self.f.session);self.engine.discard(q,self.f.session);self.journal(q).unlink()
        self.assertEqual(self.engine.inspect(q)['state'],'Aborted');self.assertEqual(json.loads(self.marker().read_text())['state'],'Released')

    def test_conflicting_active_marker_is_preserved_while_missing_fence_is_not_silently_released(self):
        q=self.request();self.engine.reserve(q,self.f.session);marker=json.loads(self.marker().read_text());marker['operationId']=uuid.uuid4().hex;self.marker().write_text(json.dumps(marker));raw=self.marker().read_bytes()
        r=self.engine.inspect(q);self.assertEqual(r['state'],'Unknown');self.assertTrue(r['sourceFenced']);self.assertFalse(r['ownedFence']);self.assertEqual(raw,self.marker().read_bytes())
        marker['state']='Released';self.marker().write_text(json.dumps(marker))
        r=self.engine.inspect(q);self.assertEqual(r['state'],'Unknown');self.assertTrue(r['ownedFence']);self.assertEqual(json.loads(self.marker().read_text())['state'],'Unknown')

    def test_changed_hdd_or_payload_prevents_release(self):
        q=self.request();self.engine.reserve(q,self.f.session);data=self.f.base.target/(self.f.ref['operationId']+'.data');data.write_bytes(b'changed')
        with self.assertRaises(Exception):self.engine.discard(q,self.f.session)
        self.assertEqual(self.engine.inspect(q)['state'],'Reserved');self.assertEqual(json.loads(self.marker().read_text())['state'],'Reserved');self.assertTrue(self.f.retained().exists())

    def test_malformed_symlink_hardlink_and_permissive_marker_fail_closed(self):
        q=self.request();self.engine.reserve(q,self.f.session);raw=self.marker().read_bytes()
        for value in (b'{}',raw.replace(b'"schemaVersion":1',b'"schemaVersion":true'),raw[:-1]+b',"state":"Released"}'):
            self.marker().write_bytes(value)
            with self.assertRaises(Exception):self.f.source.restore(self.f.ref,self.f.session)
        self.marker().write_bytes(raw);self.marker().chmod(0o644)
        with self.assertRaises(Exception):self.f.source.restore(self.f.ref,self.f.session)
        self.marker().chmod(0o600);held=self.marker().with_suffix('.held');self.marker().rename(held);self.marker().symlink_to(held)
        with self.assertRaises(Exception):self.f.source.restore(self.f.ref,self.f.session)
        self.marker().unlink();os.link(held,self.marker())
        with self.assertRaises(Exception):self.f.source.restore(self.f.ref,self.f.session)

    def test_request_rejects_source_id_unknown_fields_and_changed_plan(self):
        q=self.request();bad=copy.deepcopy(q);bad['operationId']=self.f.ref['operationId']
        with self.assertRaisesRegex(Exception,'NEW_OPERATION_REQUIRED'):self.engine.reserve(bad,self.f.session)
        bad=copy.deepcopy(q);bad['extra']=True
        with self.assertRaises(Exception):self.engine.reserve(bad,self.f.session)
        bad=copy.deepcopy(q);bad['plan']['archiveBytes']=True;bad['plan']['fingerprint']=self.p.digest(bad['plan'])
        with self.assertRaisesRegex(Exception,'PLAN_CHANGED'):self.engine.reserve(bad,self.f.session)
        self.assertFalse(self.marker().exists());self.assertFalse(self.journal(q).exists())

    def test_own_journal_source_lock_excludes_concurrent_restore_and_recovers_after_sigkill(self):
        q=self.request();restore=self.f.request();read,write=os.pipe();child=os.fork()
        if child==0:
            os.close(read)
            try:
                def cut(phase):
                    if phase=='SourceFenced':os.write(write,b'1');signal.pause()
                self.engine.checkpoint=cut;self.engine.reserve(q,self.f.session)
            except BaseException:os._exit(2)
            os._exit(3)
        os.close(write);fd=os.pidfd_open(child)
        try:
            self.assertTrue(select.select([read],[],[],20)[0]);self.assertEqual(os.read(read,1),b'1')
            with self.assertRaisesRegex(Exception,'BUSY'):self.f.engine.apply(restore,self.f.session)
        finally:
            signal.pidfd_send_signal(fd,signal.SIGKILL);os.waitpid(child,0);os.close(fd);os.close(read)
        self.assertEqual(self.engine.inspect(q)['state'],'Unknown');self.assertFalse(self.f.claim().exists());self.assertTrue(self.f.retained().exists())


if __name__=='__main__':unittest.main()
