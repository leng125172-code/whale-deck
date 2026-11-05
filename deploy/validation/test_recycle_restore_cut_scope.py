"""Real temporary native Applying records; Proc positives are explicit doubles.

Owned test children may be killed/stopped. No installed helper or product data
is touched, and these tests do not establish real UI cancellation acceptance.
"""
import copy,json,os,select,signal,subprocess,unittest,uuid
from pathlib import Path
from unittest.mock import patch
import bound_container_recycle_restore_cut as cut
import test_container_recycle_restore_rpc_proof as native
from test_recycle_restore_browser_proof import managed
from recycle_restore_browser_proof import execution


@unittest.skipUnless(os.name=='posix' and os.geteuid()==0,'isolated Linux root files required')
class RestoreCutTests(unittest.TestCase):
    def setUp(self):
        self.f=native.RestoreOriginalProofTests();self.f.setUp();self.addCleanup(self.f.doCleanups)
        self.folder=self.f.base.root/'work';self.request=self.f.q
        self.retained=self.folder/'.whaledeck-container-recycling-v1'/(self.f.ref['operationId']+'.trash')
        child=os.fork()
        if child==0:
            try:
                self.f.engine.checkpoint=lambda phase:os.kill(os.getpid(),signal.SIGKILL) if phase=='Applying' else None
                self.f.engine.apply(self.request,self.f.base.session)
            except BaseException:os._exit(2)
            os._exit(3)
        descriptor=os.pidfd_open(child)
        try:
            ready=bool(select.select([descriptor],[],[],15)[0])
            if not ready:signal.pidfd_send_signal(descriptor,signal.SIGKILL)
            _,status=os.waitpid(child,0);self.assertTrue(ready);self.assertTrue(os.WIFSIGNALED(status));self.assertEqual(os.WTERMSIG(status),signal.SIGKILL)
        finally:os.close(descriptor)
        self.patch=patch.multiple(cut,STATE=self.f.state,SOURCE_STATE=self.f.base.state);self.patch.start();self.addCleanup(self.patch.stop)
        self.record_path=self.f.state/(self.request['operationId']+'.json');self.record=json.loads(self.record_path.read_text())

    def test_actual_applying_claim_and_original_namespace_are_proven_without_hdd_claim(self):
        before=self.f.base.record(self.f.ref);proof=cut.before_return_proof(self.record,self.folder)
        self.assertTrue(proof['sourceNotReturned']);self.assertTrue(proof['sourceClaimMatches']);self.assertFalse(proof['hddDataReread'])
        self.assertEqual(proof['retainedIdentity'],self.request['plan']['retainedIdentity']);self.assertEqual(before,self.f.base.record(self.f.ref))

    def test_unclaimed_preparing_terminal_and_after_return_are_not_counted(self):
        for state in ('Preparing','Completed','Aborted','Unknown'):
            with self.subTest(state=state):self.assertIsNone(cut.before_return_proof({**self.record,'state':state},self.folder))
        original=self.f.base.record(self.f.ref);path=self.f.base.state/(self.f.ref['operationId']+'.json')
        for state in ('Restoring','Restored','Unknown','Aborted'):
            path.write_text(json.dumps({**original,'state':state}));path.chmod(0o600)
            with self.subTest(sourceState=state):self.assertIsNone(cut.before_return_proof(self.record,self.folder))

    def test_missing_or_foreign_source_claim_never_authorizes_a_cut(self):
        path=self.f.state/'claims'/(self.f.ref['operationId']+'.json');claim=json.loads(path.read_text())
        for key,bad in [('restoreOperationId','d'*32),('restoreOwner','D'*64),('restoreFingerprint','D'*64)]:
            path.write_text(json.dumps({**claim,key:bad}));path.chmod(0o600)
            with self.subTest(key=key),self.assertRaises(AssertionError):cut.before_return_proof(self.record,self.folder)
        path.unlink()
        with self.assertRaises(AssertionError):cut.before_return_proof(self.record,self.folder)

    def test_wrong_parent_private_inode_and_original_receipt_cannot_borrow_the_claim(self):
        path=self.f.base.state/(self.f.ref['operationId']+'.json');old=self.f.base.record(self.f.ref)
        for change in (dict(schemaVersion=2),dict(schemaVersion=True),dict(owner='D'*64),dict(privateIdentity=[0,1]),dict(actualIdentity=[0]*9),dict(historyDigest='D'*64)):
            path.write_text(json.dumps({**old,**change}));path.chmod(0o600)
            with self.subTest(change=change),self.assertRaises(AssertionError):cut.before_return_proof(self.record,self.folder)
        path.write_text(json.dumps(old));path.chmod(0o600)
        with self.assertRaises(AssertionError):cut.before_return_proof(self.record,self.f.base.root)

    def test_target_presence_or_retained_absence_is_a_missed_boundary(self):
        target=self.folder/'recycle-rpc.bin';target.write_bytes(b'foreign target')
        self.assertIsNone(cut.before_return_proof(self.record,self.folder));target.unlink()
        self.retained.rename(self.folder/'missing-retained')
        self.assertIsNone(cut.before_return_proof(self.record,self.folder))

    def test_changed_retained_bytes_attributes_and_foreign_entries_fail_closed(self):
        retained=self.retained;os.setxattr(retained,'user.changed',b'new')
        with self.assertRaises(AssertionError):cut.before_return_proof(self.record,self.folder)
        os.removexattr(retained,'user.changed');(retained.parent/'foreign').write_bytes(b'unrelated')
        with self.assertRaises(AssertionError):cut.before_return_proof(self.record,self.folder)

    def test_reader_accepts_64k_format_but_refuses_unsafe_records_and_duplicates(self):
        path=self.f.state/'reader.json';value={'payload':'x'*40000};path.write_text(json.dumps(value));path.chmod(0o600)
        self.assertEqual(cut.private_record(path),value)
        for raw in ('{"a":1,"a":2}','{"a":NaN}'):
            path.write_text(raw)
            with self.subTest(raw=raw),self.assertRaises(AssertionError):cut.private_record(path)
        path.write_text('{}');path.chmod(0o640)
        with self.assertRaises(AssertionError):cut.private_record(path)
        path.chmod(0o600);linked=self.f.state/'linked.json';linked.symlink_to(path)
        with self.assertRaises(OSError):cut.private_record(linked)
        path.write_text(json.dumps({'payload':'x'*65536}))
        with self.assertRaises(AssertionError):cut.private_record(path)

    def test_atomic_replacement_sample_is_retried_not_authorized(self):
        path=self.f.state/'reader.json';path.write_text('{}');path.chmod(0o600);read=os.read
        def replace(fd,size):
            value=read(fd,size);other=self.f.state/'replacement';other.write_text('{}');other.chmod(0o600);other.replace(path);return value
        with patch.object(cut.os,'read',side_effect=replace):self.assertIsNone(cut.private_record(path))

    def test_database_execution_domains_owner_and_full_plan_are_required(self):
        identifier=str(uuid.uuid4());source_id=str(uuid.uuid4());q=copy.deepcopy(self.request)
        q['operationId']=uuid.UUID(execution(identifier)).hex
        q['plan']['command']['source']['operationId']=uuid.UUID(execution(source_id,source=True)).hex
        native.rehash(q['plan'])
        record={**self.record,**q};path=self.f.state/(q['operationId']+'.json');path.write_text(json.dumps(record));path.chmod(0o600)
        row=dict(id=identifier,source_intent_id=source_id,execution_job_id=execution(identifier),owner_hash=q['owner'],plan_json=json.dumps(managed(q['plan'])),container_id=q['plan']['command']['source']['plan']['command']['containerId'],instance_id=q['plan']['command']['source']['plan']['command']['instanceId'],relative_path='work/recycle-rpc.bin')
        self.assertEqual(cut.record_for(row),record)
        for key,bad in [('id',str(uuid.uuid4())),('source_intent_id',str(uuid.uuid4())),('execution_job_id',str(uuid.uuid4())),('owner_hash','D'*64),('container_id','b'*64),('relative_path','work/foreign.bin')]:
            with self.subTest(key=key),self.assertRaises(AssertionError):cut.record_for({**row,key:bad})
        bad=copy.deepcopy(record);bad['plan']['privateIdentity'][1]+=1;native.rehash(bad['plan']);path.write_text(json.dumps(bad));path.chmod(0o600)
        with self.assertRaises(AssertionError):cut.record_for(row)

    def test_exact_transport_invocation_cgroup_and_all_three_fds_are_required(self):
        proc=self.f.state/'proc';p=proc/'321';p.mkdir(parents=True);(p/'fd').mkdir();group='whaledeck-container-recycle-restores-'+('a'*32)+'.service';inv='b'*32
        (p/'cmdline').write_bytes(b'/usr/bin/python3\0-I\0'+cut.TRANSPORT+b'\0apply\0');(p/'cgroup').write_bytes(b'0::/system.slice/'+group.encode()+b'\n');(p/'environ').write_bytes(b'INVOCATION_ID='+inv.encode()+b'\0')
        locks=[]
        for index in (4,5):
            lock=self.f.state/('test-lock-'+str(index));lock.touch(mode=0o600);locks.append(lock);(p/'fd'/str(index)).symlink_to(lock)
        (p/'fd'/'3').symlink_to(self.f.base.root);args=(321,group,inv,cut.identity(self.f.base.root),*(cut.identity(p) for p in locks))
        with patch.object(cut,'PROC',proc):
            self.assertTrue(cut.bound_process(*args))
            for field,bad in [('cmdline',b'/usr/bin/python3\0-I\0'+cut.TRANSPORT+b'\0inspect\0'),('cgroup',b'0::/foreign\n'),('environ',b'INVOCATION_ID='+b'c'*32)]:
                path=p/field;old=path.read_bytes();path.write_bytes(bad)
                try:self.assertFalse(cut.bound_process(*args))
                finally:path.write_bytes(old)
            for index in (3,4,5):
                fd=p/'fd'/str(index);target=fd.readlink();fd.unlink();self.assertFalse(cut.bound_process(*args));fd.symlink_to(target)
            self.assertFalse(cut.bound_process(321,group,inv,args[3],args[4],args[4]))
            self.assertFalse(cut.bound_process(321,group.replace('recycle-restores','recycling'),inv,*args[3:]))

    def test_actual_unrelated_child_with_three_fds_is_never_a_restore_transport(self):
        lock1=self.f.state/'lock1';lock2=self.f.state/'lock2';lock1.touch(mode=0o600);lock2.touch(mode=0o600)
        fds=[os.open(self.f.base.root,os.O_RDONLY|os.O_DIRECTORY),os.open(lock1,os.O_RDONLY),os.open(lock2,os.O_RDONLY)]
        try:
            child=subprocess.Popen(['/bin/sleep','5'],pass_fds=fds)
            try:
                with patch.object(cut.signal,'pidfd_send_signal') as send:
                    self.assertFalse(cut.bound_process(child.pid,'whaledeck-container-recycle-restores-'+('a'*32)+'.service','b'*32,*(cut.identity(Path('/proc')/str(child.pid)/'fd'/str(fd)) for fd in fds)))
                    send.assert_not_called()
            finally:child.terminate();child.wait(timeout=2)
        finally:
            for fd in fds:os.close(fd)

    def test_foreign_scope_is_rejected_before_pidfd_or_signals(self):
        row=dict(container_id='a'*64,instance_id='777:original')
        with patch.object(cut.os,'pidfd_open') as opened,patch.object(cut.signal,'pidfd_send_signal') as send,self.assertRaises(AssertionError):cut.freeze_before_return(row,self.folder,777)
        opened.assert_not_called();send.assert_not_called()


if __name__=='__main__':unittest.main()
