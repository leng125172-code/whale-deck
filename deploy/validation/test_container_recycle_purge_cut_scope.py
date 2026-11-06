"""Temporary native journals/files and owned child pidfds; Proc positives are doubles."""
import copy,json,os,select,signal,subprocess,tempfile,time,unittest,uuid
from pathlib import Path
from types import SimpleNamespace
from contextlib import contextmanager
from unittest.mock import patch
import bound_container_recycle_purge_cut as cut
import test_container_recycle_restore_rpc_proof as native
import test_container_recycle_purge_fences as fences
import test_container_recycle_purge_plan as plans
import test_container_recycle_purges as purges
from test_container_recycle_purge_browser_proof import managed
from test_container_recycle_purge_rpc_proof import plan,rehash


@unittest.skipUnless(os.name=='posix' and os.geteuid()==0,'isolated root Linux files/pidfds required')
class PurgeCutTests(unittest.TestCase):
    def setUp(self):
        self.f=native.RestoreOriginalProofTests();self.f.setUp();self.addCleanup(self.f.doCleanups)
        self.folder=self.f.base.root/'work';self.state=self.f.base.disk.disk/'purge-cut-state';self.state.mkdir(mode=0o700)
        preflight=plans.candidate.ContainerRecyclePurgePreflight(self.f.engine)
        engine_type=purges.candidate.purges_type(fences.candidate.purge_fences_type(native.native.recycling.directories.ContainerDirectories))
        self.engine=engine_type(preflight,self.state);self.command={**self.f.command,'scope':'Originals'}
        self.q=dict(operationId=uuid.uuid4().hex,owner='D'*64,plan=preflight.plan(self.command,self.f.base.session))
        self.engine.reserve(self.q,self.f.base.session);self.record=json.loads((self.state/(self.q['operationId']+'.json')).read_text())
        self.patch=patch.multiple(cut,STATE=self.state,SOURCE_STATE=self.f.base.state);self.patch.start();self.addCleanup(self.patch.stop)

    def test_reserved_namespace_is_read_without_claiming_deletion(self):
        v=cut.before_delete_proof(self.record,self.folder)
        self.assertTrue(v['originalNotQuarantined']);self.assertFalse(v['unlinkConfirmed']);self.assertEqual(v['rootPhase'],'Reserved')

    def test_applying_itself_and_later_journals_are_never_safe_abort_cuts(self):
        for state in ('Preparing','Applying','Quarantined','Removing','Removed','Completed','Unknown','Aborted'):
            with self.subTest(state=state):self.assertIsNone(cut.before_delete_proof({**self.record,'state':state},self.folder))
        marker=self.f.base.state/(self.f.ref['operationId']+'.purge.json');old=json.loads(marker.read_text())
        marker.write_text(json.dumps({**old,'state':'Deleting'}));marker.chmod(0o600)
        self.assertIsNone(cut.before_delete_proof(self.record,self.folder))

    def test_another_marker_operation_owner_fingerprint_or_source_is_refused(self):
        path=self.f.base.state/(self.f.ref['operationId']+'.purge.json');old=json.loads(path.read_text())
        for key,value in [('operationId','a'*32),('owner','A'*64),('fingerprint','A'*64),('source',{})]:
            path.write_text(json.dumps({**old,key:value}));path.chmod(0o600)
            with self.subTest(key=key),self.assertRaises(AssertionError):cut.before_delete_proof(self.record,self.folder)

    def test_source_root_digest_and_actual_retained_identity_must_match(self):
        path=self.f.base.state/(self.f.ref['operationId']+'.json');old=json.loads(path.read_text())
        for change in (dict(owner='A'*64),dict(actualIdentity=[0]*9),dict(historyDigest='0'*64),dict(schemaVersion=True)):
            path.write_text(json.dumps({**old,**change}));path.chmod(0o600)
            with self.subTest(change=change),self.assertRaises(AssertionError):cut.before_delete_proof(self.record,self.folder)

    def test_foreign_target_missing_retained_and_changed_attributes_fail_closed(self):
        target=self.folder/'recycle-rpc.bin';target.write_bytes(b'foreign');self.assertIsNone(cut.before_delete_proof(self.record,self.folder));target.unlink()
        retained=self.folder/cut.source_io.PRIVATE/(self.f.ref['operationId']+'.trash')
        os.setxattr(retained,'user.foreign',b'changed')
        with self.assertRaises(AssertionError):cut.before_delete_proof(self.record,self.folder)
        os.removexattr(retained,'user.foreign');retained.rename(self.folder/'missing-retained-fixture');self.assertIsNone(cut.before_delete_proof(self.record,self.folder))

    def test_sql_execution_source_domains_and_full_plan_are_bound(self):
        identifier,source=str(uuid.uuid4()),str(uuid.uuid4());p=plan();p['command']['source']['operationId']=uuid.UUID(cut.execution(source,source=True)).hex;rehash(p)
        op=cut.execution(identifier);r=dict(schemaVersion=1,operationId=uuid.UUID(op).hex,owner='D'*64,plan=p,state='Reserved',pendingIdentity=None,unlinkConfirmed=False)
        path=self.state/(r['operationId']+'.json');path.write_text(json.dumps(r));path.chmod(0o600)
        c=p['command']['source']['plan']['command'];row=dict(id=identifier,source_intent_id=source,execution_job_id=op,owner_hash='D'*64,plan_json=json.dumps(managed(p)),container_id=c['containerId'],instance_id=c['instanceId'],relative_path=c['path'])
        self.assertEqual(cut.record_for(row),r)
        for key,value in [('id',str(uuid.uuid4())),('source_intent_id',str(uuid.uuid4())),('execution_job_id',str(uuid.uuid4())),('owner_hash','A'*64),('container_id','b'*64),('relative_path','work/foreign')]:
            with self.subTest(key=key),self.assertRaises((AssertionError,FileNotFoundError)):cut.record_for({**row,key:value})

    def test_four_distinct_proc_fds_and_exact_apply_invocation_are_required(self):
        proc=self.state/'proc';p=proc/'321';p.mkdir(parents=True);(p/'fd').mkdir();group='whaledeck-container-recycle-purges-'+'a'*32+'.service';inv='b'*32
        (p/'cmdline').write_bytes(b'/usr/bin/python3\0-I\0'+cut.TRANSPORT+b'\0apply\0');(p/'cgroup').write_bytes(b'0::/system.slice/'+group.encode()+b'\n');(p/'environ').write_bytes(b'INVOCATION_ID='+inv.encode()+b'\0')
        identities=[]
        for at in range(4):
            item=self.state/('fd-target-'+str(at));item.touch(mode=0o600);(p/'fd'/str(at+3)).symlink_to(item);identities.append(cut.identity(item))
        with patch.object(cut,'PROC',proc):
            args=(321,group,inv,*identities);self.assertTrue(cut.bound_process(*args))
            self.assertFalse(cut.bound_process(321,group,inv,identities[0],identities[1],identities[2],identities[2]))
            for key,bad in [('cmdline',b'/usr/bin/python3\0-I\0'+cut.TRANSPORT+b'\0reserve\0'),('cgroup',b'0::/foreign\n'),('environ',b'INVOCATION_ID='+b'c'*32)]:
                path=p/key;old=path.read_bytes();path.write_bytes(bad)
                try:self.assertFalse(cut.bound_process(*args))
                finally:path.write_bytes(old)
            for at in range(4):
                path=p/'fd'/str(at+3);dest=path.readlink();path.unlink();self.assertFalse(cut.bound_process(*args));path.symlink_to(dest)

    def test_actual_unrelated_child_cannot_borrow_all_four_fd_identities(self):
        files=[];fds=[]
        try:
            for at in range(4):
                path=self.state/('real-'+str(at));path.touch(mode=0o600);files.append(path);fds.append(os.open(path,os.O_RDONLY))
            child=subprocess.Popen(['/bin/sleep','5'],pass_fds=fds)
            try:self.assertFalse(cut.bound_process(child.pid,'whaledeck-container-recycle-purges-'+'a'*32+'.service','b'*32,*(cut.identity(p) for p in files)))
            finally:child.terminate();child.wait(timeout=2)
        finally:
            for fd in fds:os.close(fd)

    def test_foreign_fixture_scope_is_rejected_before_pidfd_or_storage_access(self):
        with patch.object(cut.os,'pidfd_open') as opened,patch.object(cut,'current_archive') as storage,self.assertRaises(AssertionError):cut.freeze_before_delete(dict(container_id='a'*64,instance_id='777:fixture'),self.folder,777)
        opened.assert_not_called();storage.assert_not_called()

    def test_current_archive_uses_attested_readonly_fd_and_exact_bytes(self):
        p=self.q['plan'];base=self.f.base.target.parent;storage=self.f.source.history.storage;calls=[]
        @contextmanager
        def target(**kwargs):
            self.assertEqual(kwargs,dict(create_namespace=False,read_only=True));fd=os.open(base,os.O_RDONLY|os.O_DIRECTORY)
            try:yield storage.configuration(),fd
            finally:os.close(fd)
        def attest(config,fd,**kwargs):
            self.assertEqual(kwargs,dict(read_only=True));self.assertEqual(cut.identity(Path('/proc/self/fd/'+str(fd))),tuple(p['storeIdentity']));calls.append(fd)
        io=SimpleNamespace(storage=SimpleNamespace(target=target,attest=attest),files=self.f.base.f)
        with patch.object(cut.source_io,'installed',return_value=io):
            with cut.current_archive(p) as (ident,v):
                self.assertEqual(ident,tuple(p['archiveIdentity'][:2]));self.assertTrue(v['currentHddFdAttested']);self.assertEqual(v['hddSha256'],p['archiveSha256'])
        self.assertEqual(len(calls),2)

    def handoff_fixture(self,fail_exit):
        # Only this owned sleep child is stopped; path/unit/proc positives below
        # are explicit doubles to exercise post-attest descriptor ownership.
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp);folder=root/('wdv-container-publish-'+uuid.uuid4().hex);folder.mkdir();state=root/'state';state.mkdir();source=root/'sources';source.mkdir()
            ident=str(uuid.uuid4());op=cut.execution(ident);src='a'*32;group='whaledeck-container-recycle-purges-'+uuid.UUID(op).hex+'.service';inv='b'*32
            cg=root/'groups';cg.mkdir();(cg/group).mkdir()
            for path in (state/(uuid.UUID(op).hex+'.lock'),source/(src+'.lock')):path.touch(mode=0o600)
            child=subprocess.Popen(['/bin/sleep','20']);opened=[];real_open=os.pidfd_open;real_path=Path;checked=[]
            def opened_pidfd(pid):
                self.assertEqual(pid,child.pid);fd=real_open(pid);opened.append(fd);return fd
            def path(value):
                if str(value)=='/data/WhaleDeck/apps':return root
                if str(value)=='/data/WhaleDeck/apps/'+folder.name:return folder
                return real_path(value)
            @contextmanager
            def archive(_):
                yield (999,888),{'currentHddFdAttested':True}
                checked.append(True)
                if fail_exit:raise RuntimeError('synthetic final attest failure')
            row=dict(id=ident,execution_job_id=op,container_id='a'*64,instance_id=str(child.pid)+':fixture',plan_json={})
            plan=dict(parentIdentity=list(cut.identity(folder)),command=dict(source=dict(operationId=src)))
            try:
                with patch.multiple(cut,STATE=state,SOURCE_STATE=source,CGROUP=cg),patch.object(cut,'Path',side_effect=path),patch.object(cut,'managed_plan',return_value=plan),patch.object(cut,'current_archive',side_effect=archive),patch.object(cut,'unit_identity',return_value=(child.pid,inv)),patch.object(cut,'bound_process',return_value=True),patch.object(cut,'record_for',return_value={'state':'Reserved'}),patch.object(cut,'before_delete_proof',return_value={}),patch.object(cut.os,'pidfd_open',side_effect=opened_pidfd):
                    if fail_exit:
                        with self.assertRaisesRegex(RuntimeError,'attest failure'):cut.freeze_before_delete(row,'/data/WhaleDeck/apps/'+folder.name,child.pid)
                    else:
                        held=cut.freeze_before_delete(row,'/data/WhaleDeck/apps/'+folder.name,child.pid);self.assertIsNotNone(held);self.assertEqual(checked,[True]);self.assertIsNotNone(held.descriptor);held.release()
                self.assertEqual(len(opened),1)
                with self.assertRaises(OSError):os.fstat(opened[0])
                until=time.monotonic()+2
                while Path(f'/proc/{child.pid}/stat').read_text().rsplit(')',1)[1].split()[0] in ('T','t'):
                    self.assertLess(time.monotonic(),until);time.sleep(.001)
                if fail_exit:
                    child.wait(timeout=2);self.assertEqual(child.returncode,-signal.SIGKILL)
                else:self.assertIsNone(child.poll())
            finally:
                if child.poll() is None:child.terminate();child.wait(timeout=2)

    def test_final_hdd_attestation_failure_stops_unheld_child_and_closes_pidfd(self):
        self.handoff_fixture(True)

    def test_successful_handoff_occurs_only_after_context_attestation(self):
        self.handoff_fixture(False)


if __name__=='__main__':unittest.main()
