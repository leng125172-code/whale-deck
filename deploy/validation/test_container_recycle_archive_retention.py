"""Native temporary retention boundaries; no installed RPC or physical HDD.

The stage replacement case uses an actual uid/gid 1000 child, not a mocked
rename. Namespace and storage attestation remain the explicit test doubles.
"""
import os
import select
import signal
import stat
import unittest
import uuid
from unittest.mock import patch
import test_container_recycle_archive_copies as copies
from test_container_recycle_purge_plan import snapshot


@unittest.skipUnless(os.name=='posix' and os.geteuid()==0,'isolated Linux root required')
class ArchiveRetentionTests(unittest.TestCase):
    def setUp(self):
        self.f=copies.ArchiveCopyTests();self.f.setUp();self.addCleanup(self.f.doCleanups)
        self.engine,self.q,self.session=self.f.engine,self.f.q,self.f.session

    def retained(self):
        return dict(claim=snapshot(self.engine.restore.claims_path),journal={n:v for n,v in snapshot(self.f.f.state).items() if n.endswith('.json')},
                    namespace=snapshot(self.f.base.root),directories={p.name:self.f.source.metadata(p.stat()) for p in self.f.base.root.iterdir() if p.is_dir()},protected=self.f.protected())

    def rejected_lock_only(self,other):
        self.assertFalse(self.f.f.journal(other).exists())
        lock=self.f.f.state/(other['operationId']+'.lock');info=lock.stat()
        self.assertEqual((info.st_uid,info.st_gid,stat.S_IMODE(info.st_mode),info.st_nlink,info.st_size),(0,0,0o600,1,0))

    def test_success_and_cold_replays_keep_exact_claim_stage_and_history(self):
        self.f.reserve();claim=snapshot(self.engine.restore.claims_path)
        with patch.object(os,'rmdir',side_effect=AssertionError('publication is not stage retirement')):
            r=self.engine.apply(self.q,self.session)
        self.assertTrue(r['archiveCopied'] and r['bindingCurrent']);self.assertEqual(claim,snapshot(self.engine.restore.claims_path))
        stage=self.f.private();self.assertEqual([stage.stat().st_dev,stage.stat().st_ino],self.f.record()['privateIdentity']);self.assertEqual(list(stage.iterdir()),[])
        before=self.retained();cold=type(self.engine)(self.f.f.p,self.f.f.state)
        with patch.object(cold,'clear_claim',side_effect=AssertionError('no implicit source release')),patch.object(os,'rmdir',side_effect=AssertionError('no stage retirement')):
            for action in (cold.apply,cold.inspect,cold.discard):
                r=action(self.q,self.session);self.assertEqual(r['state'],'Completed');self.assertTrue(r['archiveCopied'] and r['bindingCurrent']);self.assertEqual(before,self.retained())

    def test_completed_source_rejects_new_id_and_keeps_old_claim_and_copy(self):
        self.f.reserve();self.engine.apply(self.q,self.session);before=self.retained();other={**self.q,'operationId':uuid.uuid4().hex}
        with self.assertRaisesRegex(Exception,'SOURCE_BUSY'):self.engine.reserve(other,self.session)
        self.rejected_lock_only(other);self.assertEqual(before,self.retained())
        with self.assertRaisesRegex(Exception,'SOURCE_BUSY'):self.engine.plan(self.q['plan']['command'],self.session)
        for action in (self.engine.apply,self.engine.inspect,self.engine.discard):
            self.assertTrue(action(self.q,self.session)['archiveCopied']);self.assertEqual(before,self.retained())

    def test_absent_copy_is_not_source_release_or_implicit_republication(self):
        self.f.reserve();self.engine.apply(self.q,self.session);held=self.f.base.leaf.with_suffix('.held');self.f.base.leaf.rename(held)
        before=self.retained();other={**self.q,'operationId':uuid.uuid4().hex}
        for action in (self.engine.apply,self.engine.inspect,self.engine.discard):
            r=action(self.q,self.session);self.assertTrue(r['archiveCopied']);self.assertFalse(r['bindingCurrent']);self.assertEqual(before,self.retained())
        with self.assertRaisesRegex(Exception,'SOURCE_BUSY'):self.engine.reserve(other,self.session)
        self.rejected_lock_only(other);self.assertEqual(before,self.retained());self.assertEqual(held.read_bytes(),self.f.base.content)

    def test_unknown_cut_observers_never_release_or_rewrite_exact_claim(self):
        for phase in ('Prepared','PublishedNamespace'):
            with self.subTest(phase=phase):
                f=copies.ArchiveCopyTests();f.setUp()
                try:
                    f.reserve();claim=snapshot(f.engine.restore.claims_path);f.kill_at(phase)
                    r=f.engine.inspect(f.q,None);self.assertEqual(r['state'],'Unknown');self.assertEqual(claim,snapshot(f.engine.restore.claims_path))
                    before=dict(namespace=snapshot(f.base.root),journal=snapshot(f.f.state),protected=f.protected())
                    with patch.object(f.engine,'clear_claim',side_effect=AssertionError('no Unknown release')),patch.object(os,'rmdir',side_effect=AssertionError('no Unknown cleanup')):
                        for action in (f.engine.apply,f.engine.inspect,f.engine.discard):
                            self.assertIsNone(action(f.q,None)['archiveCopied']);self.assertEqual(claim,snapshot(f.engine.restore.claims_path));self.assertEqual(before,dict(namespace=snapshot(f.base.root),journal=snapshot(f.f.state),protected=f.protected()))
                finally:f.doCleanups()

    def test_lost_session_cannot_cancel_reserved_claim(self):
        self.f.reserve();before=self.retained()
        for action in (self.engine.apply,self.engine.discard):
            with self.subTest(action=action.__name__),self.assertRaisesRegex(Exception,'SESSION_REQUIRED'):action(self.q,None)
            self.assertEqual(before,self.retained())
        self.assertEqual(self.engine.inspect(self.q,None)['state'],'Reserved');self.assertEqual(before,self.retained())

    def test_real_unprivileged_parent_writer_can_replace_root_stage_but_no_cleanup_deletes_it(self):
        self.f.reserve();held_name='held-original-stage';parent=self.f.base.root;old_mode=stat.S_IMODE(parent.stat().st_mode);seen={}
        def completed(phase):
            if phase!='Completed':return
            stage=self.f.private();seen['old']=[stage.stat().st_dev,stage.stat().st_ino]
            self.assertEqual((stage.stat().st_uid,stat.S_IMODE(stage.stat().st_mode)),(0,0o700));self.assertEqual(list(stage.iterdir()),[])
            # Only the test's own parent is made writable. Its fd avoids the
            # private outer tempfile ancestors; no production paths are used.
            parent.chmod(0o777);fd=os.open(parent,os.O_RDONLY|os.O_DIRECTORY)
            try:
                child=os.fork()
                if child==0:
                    try:
                        os.setgroups([]);os.setgid(1000);os.setuid(1000)
                        os.rename(stage.name,held_name,src_dir_fd=fd,dst_dir_fd=fd)
                        os.mkdir(stage.name,0o700,dir_fd=fd)
                    except BaseException:os._exit(2)
                    os._exit(0)
                pidfd=os.pidfd_open(child)
                try:
                    ready=select.select([pidfd],[],[],10)[0]
                    if not ready:signal.pidfd_send_signal(pidfd,signal.SIGKILL)
                    _,status=os.waitpid(child,0)
                finally:os.close(pidfd)
                self.assertTrue(ready);self.assertTrue(os.WIFEXITED(status));self.assertEqual(os.WEXITSTATUS(status),0)
            finally:os.close(fd);parent.chmod(old_mode)
            seen['replacement']=stage.stat().st_ino;self.assertEqual(stage.stat().st_uid,1000)
        with patch.object(self.engine,'checkpoint',side_effect=completed),patch.object(os,'rmdir',side_effect=AssertionError('must not delete swapped directory')):
            r=self.engine.apply(self.q,self.session)
        self.assertTrue(r['archiveCopied'] and r['bindingCurrent']);self.assertNotEqual(seen['replacement'],seen['old'][1])
        held=parent/held_name;self.assertEqual([held.stat().st_dev,held.stat().st_ino],seen['old']);self.assertEqual(list(held.iterdir()),[])
        before=self.retained()
        with patch.object(os,'rmdir',side_effect=AssertionError('must preserve replacement on replay')):
            for action in (self.engine.apply,self.engine.inspect,self.engine.discard):
                self.assertTrue(action(self.q,self.session)['archiveCopied']);self.assertEqual(before,self.retained())
