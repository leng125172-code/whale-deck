"""Real temporary files and child pidfd checks; Proc binding positives are explicit doubles."""
import copy,json,os,select,signal,subprocess,time,unittest,uuid
from pathlib import Path
from unittest.mock import patch
import bound_container_recycle_cut as cut
import test_container_file_recycling as native
from test_container_recycle_browser_scope import managed

@unittest.skipUnless(os.name=='posix' and os.geteuid()==0,'temporary root Linux files required')
class RecycleCutProofTests(unittest.TestCase):
    def setUp(self):
        self.f=native.LinuxContainerRecycleTests();self.f.setUp();self.addCleanup(self.f.doCleanups);self.source=self.f.root/'work';self.source.mkdir();self.f.leaf=self.f.leaf.rename(self.source/'recycle-rpc.bin');c={**self.f.session.request,'path':'work/recycle-rpc.bin','expectedVersion':self.f.r.version(self.f.session.request,'work/recycle-rpc.bin',self.f.leaf.stat())};self.plan=self.f.engine.plan(c,self.f.session);self.op=uuid.uuid4().hex
        self.record=dict(schemaVersion=1,operationId=self.op,owner='B'*64,plan=self.plan,state='Preparing',historyMayExist=True,historyVerified=True,historyDigest='A'*64,actualIdentity=None,privateIdentity=None)
        self.row=dict(execution_job_id=str(uuid.UUID(self.op)),owner_hash='B'*64,container_id=c['containerId'],instance_id=c['instanceId'],relative_path=c['path'],plan_json=json.dumps(managed(self.plan)))
        self.state=self.f.state;self.path=self.state/(self.op+'.json');self.path.write_text(json.dumps(self.record));self.path.chmod(0o600)
    def test_full_sql_and_root_plan_are_bound(self):
        with patch.object(cut,'STATE',self.state):
            self.assertEqual(cut.record_for(self.row),self.record)
            for key,value in [('owner_hash','C'*64),('container_id','b'*64),('instance_id','555:other'),('relative_path','work/foreign')]:
                with self.subTest(key=key),self.assertRaises(AssertionError):cut.record_for({**self.row,key:value})
    def test_rehashed_or_mutated_source_is_not_original(self):
        self.path.write_text(json.dumps({**self.record,'plan':{**self.plan,'sha256':'F'*64}}))
        with patch.object(cut,'STATE',self.state),self.assertRaises(AssertionError):cut.record_for(self.row)
        self.f.leaf.write_bytes(b'changed')
        with self.assertRaisesRegex(AssertionError,'SOURCE_CHANGED'):cut.pre_removal_proof(self.record,self.source)
    def test_preparing_verified_cut_reads_full_bytes_and_metadata(self):
        proof=cut.pre_removal_proof(self.record,self.source);self.assertTrue(proof['sourceNotRemoved']);self.assertEqual(proof['sourceSha256'],self.plan['sha256'])
        private=self.source/cut.PRIVATE;private.mkdir(mode=0o700);r={**self.record,'privateIdentity':list(cut.identity(private)),'state':'Retaining'};self.assertTrue(cut.pre_removal_proof(r,self.source)['sourceNotRemoved'])
    def test_terminal_or_unverified_states_are_not_counted(self):
        for state in ('Trashed','Restored','Unknown','Aborted','Restoring'):
            with self.subTest(state=state):self.assertIsNone(cut.pre_removal_proof({**self.record,'state':state},self.source))
        self.assertIsNone(cut.pre_removal_proof({**self.record,'historyVerified':False},self.source))
    def test_unbound_private_mkdir_or_missing_source_is_missed_not_counted(self):
        (self.source/cut.PRIVATE).mkdir(mode=0o700);self.assertIsNone(cut.pre_removal_proof(self.record,self.source));self.f.leaf.unlink();self.assertIsNone(cut.pre_removal_proof(self.record,self.source))
    def test_wrong_private_inode_or_unrelated_entries_fail_closed(self):
        private=self.source/cut.PRIVATE;private.mkdir(mode=0o700);r={**self.record,'privateIdentity':[1,2]}
        with self.assertRaises(AssertionError):cut.pre_removal_proof(r,self.source)
        r['privateIdentity']=list(cut.identity(private));(private/'foreign').touch()
        with self.assertRaises(AssertionError):cut.pre_removal_proof(r,self.source)
    def test_foreign_scope_is_rejected_before_opening_pidfd(self):
        with patch.object(cut.os,'pidfd_open') as opened,self.assertRaises(AssertionError):cut.freeze_before_removal(self.row,self.source,100)
        opened.assert_not_called()
    def test_source_symlink_and_changed_parent_do_not_pass(self):
        old=self.source/'old';self.f.leaf.rename(old);self.f.leaf.symlink_to(old)
        with self.assertRaises(OSError):cut.pre_removal_proof(self.record,self.source)
        with self.assertRaises(AssertionError):cut.pre_removal_proof(self.record,self.f.root)
    def test_proc_command_cgroup_invocation_and_both_fds_are_required(self):
        proc=self.f.disk.disk/'proc';p=proc/'321';p.mkdir(parents=True);(p/'fd').mkdir();group='whaledeck-container-recycling-'+('a'*32)+'.service';inv='b'*32
        (p/'cmdline').write_bytes(b'/usr/bin/python3\0-I\0'+cut.TRANSPORT+b'\0apply\0');(p/'cgroup').write_bytes(b'0::/system.slice/'+group.encode()+b'\n');(p/'environ').write_bytes(b'INVOCATION_ID='+inv.encode()+b'\0');lock=self.f.disk.disk/'lock';lock.touch(mode=0o600);(p/'fd'/'3').symlink_to(self.f.root);(p/'fd'/'4').symlink_to(lock)
        with patch.object(cut,'PROC',proc):
            args=(321,group,inv,cut.identity(self.f.root),cut.identity(lock));self.assertTrue(cut.bound_process(*args))
            for field,bad in [('cmdline',b'/bin/sleep\0'),('cgroup',b'0::/foreign\n'),('environ',b'INVOCATION_ID='+b'c'*32)]:
                path=p/field;old=path.read_bytes();path.write_bytes(bad)
                try:self.assertFalse(cut.bound_process(*args))
                finally:path.write_bytes(old)
            (p/'fd'/'4').unlink();self.assertFalse(cut.bound_process(*args))
    def test_unrelated_actual_child_is_never_authorized_even_with_both_fds(self):
        lock=self.f.disk.disk/'held';lock.touch(mode=0o600);root=os.open(self.f.root,os.O_RDONLY|os.O_DIRECTORY);fd=os.open(lock,os.O_RDONLY)
        try:
            child=subprocess.Popen(['/bin/sleep','5'],pass_fds=(root,fd))
            try:self.assertFalse(cut.bound_process(child.pid,'whaledeck-container-recycling-'+('a'*32)+'.service','b'*32,cut.identity(self.f.root),cut.identity(lock)))
            finally:child.terminate();child.wait(timeout=2)
        finally:os.close(root);os.close(fd)
    def test_unstopped_unheld_descriptor_is_closed_without_sending_any_signal(self):
        child=subprocess.Popen(['/bin/sleep','5']);fd=os.pidfd_open(child.pid)
        try:
            with patch.object(cut.signal,'pidfd_send_signal') as send:cut.release_unheld(fd,False)
            send.assert_not_called()
            with self.assertRaises(OSError):os.fstat(fd)
            self.assertIsNone(child.poll())
        finally:child.terminate();child.wait(timeout=2)
    def test_pidfd_resume_and_exit_measurement_use_only_owned_child(self):
        child=subprocess.Popen(['/bin/sleep','5']);fd=os.pidfd_open(child.pid);held=cut.HeldCopy(child.pid,fd,'test',{}, {})
        try:
            signal.pidfd_send_signal(fd,signal.SIGSTOP);deadline=time.monotonic()+2
            while Path(f'/proc/{child.pid}/stat').read_text().rsplit(')',1)[1].split()[0] not in ('T','t'):self.assertLess(time.monotonic(),deadline);time.sleep(.001)
            held.release();began=time.monotonic();watch=os.pidfd_open(child.pid)
            try:child.terminate();self.assertTrue(select.select([watch],[],[],2)[0]);child.wait(timeout=2)
            finally:os.close(watch)
            self.assertLess(time.monotonic()-began,2)
        finally:
            if child.poll() is None:child.kill();child.wait(timeout=2)
            held.release()

if __name__=='__main__':unittest.main()
