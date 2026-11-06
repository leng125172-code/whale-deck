"""Temporary real journals/HDD and owned child pidfds; Proc positives are doubles.

These are cut candidate tests, never installed RPC or visible UI acceptance.
"""
import json,os,signal,subprocess,tempfile,unittest,uuid
from contextlib import contextmanager
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch
import bound_container_recycle_archive_cut as cut
import test_container_recycle_archive_rpc_proof as native
from test_container_recycle_archive_browser_proof import managed,rows
from test_container_recycle_purge_plan import snapshot


@unittest.skipUnless(os.name=='posix' and os.geteuid()==0,'isolated root Linux files/pidfds required')
class ArchiveCutRecordTests(unittest.TestCase):
    def setUp(self):
        self.f=native.ArchiveRecordJoinTests();self.f.setUp();self.addCleanup(self.f.doCleanups)
        self.engine=self.f.engine;self.q=self.f.q;self.base=self.f.f.base;self.folder=self.f.f.work
        self.engine.reserve(self.q,self.f.f.session)
        self.path=self.f.state/(self.q['operationId']+'.json');self.record=json.loads(self.path.read_bytes())
        self.source=self.base.state/(self.f.f.ref['operationId']+'.json')
        self.marker=self.base.state/(self.f.f.ref['operationId']+'.purge.json')
        self.purge=self.f.f.state/(self.f.f.q['operationId']+'.json')
        self.claim=self.engine.restore.claims_path/(self.f.f.ref['operationId']+'.json')
        self.history=self.base.target/(self.f.f.ref['operationId']+'.json')
        self.data=self.history.with_suffix('.data')
        for change in (patch.multiple(cut,STATE=self.f.state,SOURCE_STATE=self.base.state,PURGE_STATE=self.f.f.state,CLAIMS=self.engine.restore.claims_path),
                       patch.object(cut.source_io,'installed',return_value=SimpleNamespace(storage=self.base.disk.engine,files=self.base.f))):
            change.start();self.addCleanup(change.stop)

    def test_reserved_proves_no_stage_and_preserves_all_real_files(self):
        before=[snapshot(p) for p in (self.base.state,self.f.f.state,self.f.state,self.base.target,self.folder)]
        v=cut.before_copy_proof(self.record,self.folder)
        self.assertEqual(v['rootPhase'],'Reserved');self.assertTrue(v['sourceClaimMatches'] and v['targetAbsent'] and v['stageAbsent'] and v['copyingNotEntered'])
        self.assertFalse(v['hddDataReread']) # Only current_archive may assert HDD reread.
        self.assertEqual(before,[snapshot(p) for p in (self.base.state,self.f.f.state,self.f.state,self.base.target,self.folder)])

    def test_applying_and_every_later_phase_are_uncertain(self):
        for state in ('Preparing','Applying','Copying','Prepared','Publishing','Published','Completed','Unknown','Aborted'):
            with self.subTest(state=state):self.assertIsNone(cut.before_copy_proof({**self.record,'state':state},self.folder))
        for key,value in (('privateIdentity',[1,2]),('pendingIdentity',[1,3]),('preparedIdentity',[1]*9),('publishedIdentity',[1]*9),('parentAfterStage','A'*64)):
            with self.subTest(key=key),self.assertRaises(AssertionError):cut.before_copy_proof({**self.record,key:value},self.folder)

    def test_changed_source_purge_marker_or_claim_is_refused(self):
        for path,changes in ((self.source,dict(owner='F'*64,state='Restored',schemaVersion=True,historyDigest='0'*64)),
                             (self.purge,dict(owner='F'*64,state='Unknown',unlinkConfirmed=False,schemaVersion=True)),
                             (self.marker,dict(operationId='f'*32,owner='F'*64,fingerprint='0'*64,state='Reserved',schemaVersion=True)),
                             (self.claim,dict(restoreOperationId='f'*32,restoreOwner='F'*64,restoreFingerprint='0'*64,schemaVersion=True))):
            raw=path.read_bytes();good=json.loads(raw)
            try:
                for key,value in changes.items():
                    path.write_text(json.dumps({**good,key:value}));path.chmod(0o600)
                    with self.subTest(path=path.name,key=key),self.assertRaises(AssertionError):cut.before_copy_proof(self.record,self.folder)
            finally:path.write_bytes(raw)

    def test_missing_immutable_source_record_is_not_a_safe_cut(self):
        for path in (self.source,self.purge,self.marker):
            held=path.with_suffix('.held');path.rename(held)
            try:self.assertIsNone(cut.before_copy_proof(self.record,self.folder))
            finally:held.rename(path)

    def test_foreign_target_or_stage_is_never_adopted(self):
        target=self.folder/'recycle-rpc.bin';target.write_bytes(b'foreign')
        try:
            with self.assertRaises(AssertionError):cut.before_copy_proof(self.record,self.folder)
        finally:target.unlink()
        stage=self.folder/('.whaledeck-container-archive-'+self.q['operationId']);stage.mkdir(mode=0o700)
        try:
            with self.assertRaises(AssertionError):cut.before_copy_proof(self.record,self.folder)
        finally:stage.rmdir()

    def test_actual_hdd_data_and_metadata_are_read_on_attested_readonly_namespace(self):
        before=snapshot(self.base.target)
        with cut.current_archive(self.q['plan']) as (identity,v):
            self.assertEqual(identity,tuple(self.q['plan']['archiveIdentity'][:2]));self.assertTrue(v['hddDataReread'] and v['hddMetadataJoined'] and v['currentHddFdAttested'])
            self.assertEqual(v['hddMetadataDigest'],self.q['plan']['historyMetadataDigest']);self.assertEqual(v['hddSha256'],self.q['plan']['archiveSha256'])
        self.assertEqual(before,snapshot(self.base.target))

    def test_history_metadata_mismatch_or_mid_handoff_change_is_refused(self):
        raw=self.history.read_bytes();v=json.loads(raw)
        try:
            for key,value in (('captureState','Trashed'),('historyDigest','0'*64),('unexpected',True)):
                self.history.write_text(json.dumps({**v,key:value}));self.history.chmod(0o600)
                entered=False
                with self.subTest(key=key),self.assertRaises(AssertionError):
                    with cut.current_archive(self.q['plan']):entered=True
                self.assertFalse(entered)
            self.history.write_bytes(raw)
            with self.assertRaises(AssertionError):
                with cut.current_archive(self.q['plan']):self.history.write_text(json.dumps({**v,'unexpected':True}))
        finally:self.history.write_bytes(raw)

    def test_changed_hdd_payload_is_refused_before_handoff(self):
        with self.data.open('r+b') as stream:stream.write(b'corrupt')
        entered=False
        with self.assertRaises(AssertionError):
            with cut.current_archive(self.q['plan']):entered=True
        self.assertFalse(entered)

    def test_sql_execution_purge_domain_owner_and_full_plan_are_bound(self):
        row,_,_=rows();p=native.plan();p= cut.managed_plan(managed(p))
        # Build the public-source domain using an independently generated SQL row.
        p['command']['purge']['operationId']=uuid.UUID(cut.execution(row['source_intent_id'],'purge')).hex;native.rehash(p)
        op=cut.execution(row['id']);r={**self.record,'operationId':uuid.UUID(op).hex,'owner':'D'*64,'plan':p}
        path=self.f.state/(r['operationId']+'.json');path.write_text(json.dumps(r));path.chmod(0o600)
        c=p['command']['purge']['plan']['command']['source']['plan']['command']
        row.update(owner_hash='D'*64,plan_json=json.dumps(managed(p)),container_id=c['containerId'],instance_id=c['instanceId'],relative_path=c['path'])
        self.assertEqual(cut.record_for(row),r)
        for key,value in (('id',str(uuid.uuid4())),('source_intent_id',str(uuid.uuid4())),('execution_job_id',str(uuid.uuid4())),('owner_hash','F'*64),('container_id','f'*64),('relative_path','work/foreign')):
            with self.subTest(key=key),self.assertRaises((AssertionError,FileNotFoundError)):cut.record_for({**row,key:value})


@unittest.skipUnless(os.name=='posix' and os.geteuid()==0,'isolated root Linux files/pidfds required')
class ArchiveCutProcessTests(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory();self.addCleanup(self.tmp.cleanup);self.root=Path(self.tmp.name)

    def test_five_distinct_fds_exact_argv_cgroup_invocation_and_root_uid_required(self):
        proc=self.root/'proc';p=proc/'321';p.mkdir(parents=True);(p/'fd').mkdir();group='whaledeck-container-recycle-archives-'+'a'*32+'.service';inv='b'*32
        (p/'cmdline').write_bytes(b'/usr/bin/python3\0-I\0'+cut.TRANSPORT+b'\0apply\0');(p/'cgroup').write_bytes(b'0::/system.slice/'+group.encode()+b'\n');(p/'environ').write_bytes(b'INVOCATION_ID='+inv.encode()+b'\0')
        identities=[]
        for at in range(5):
            item=self.root/('fd-'+str(at));item.touch(mode=0o600);(p/'fd'/str(at+3)).symlink_to(item);identities.append(cut.identity(item))
        with patch.object(cut,'PROC',proc):
            args=(321,group,inv,*identities);self.assertTrue(cut.bound_process(*args))
            self.assertFalse(cut.bound_process(321,group,inv,*identities[:4],identities[3]))
            for key,bad in (('cmdline',b'/usr/bin/python3\0-I\0'+cut.TRANSPORT+b'\0reserve\0'),('cgroup',b'0::/foreign\n'),('environ',b'INVOCATION_ID='+b'c'*32)):
                path=p/key;old=path.read_bytes();path.write_bytes(bad)
                try:self.assertFalse(cut.bound_process(*args))
                finally:path.write_bytes(old)
            os.chown(p,1000,1000)
            try:self.assertFalse(cut.bound_process(*args))
            finally:os.chown(p,0,0)
            for at in range(5):
                path=p/'fd'/str(at+3);dest=path.readlink();path.unlink()
                try:self.assertFalse(cut.bound_process(*args))
                finally:path.symlink_to(dest)

    def test_unrelated_real_child_with_all_fds_still_cannot_borrow_invocation(self):
        fds=[]
        try:
            for at in range(5):
                path=self.root/('real-'+str(at));path.touch(mode=0o600);fds.append(os.open(path,os.O_RDONLY))
            child=subprocess.Popen(['/bin/sleep','5'],pass_fds=fds)
            try:self.assertFalse(cut.bound_process(child.pid,'whaledeck-container-recycle-archives-'+'a'*32+'.service','b'*32,*(cut.identity(self.root/('real-'+str(at))) for at in range(5))))
            finally:child.terminate();child.wait(timeout=2)
        finally:
            for fd in fds:os.close(fd)

    def test_foreign_fixture_is_rejected_before_storage_pidfd_or_signal(self):
        with patch.object(cut,'current_archive') as storage,patch.object(cut.os,'pidfd_open') as opened,patch.object(cut.signal,'pidfd_send_signal') as sent,self.assertRaises(AssertionError):
            cut.freeze_before_copy(dict(container_id='a'*64,instance_id='777:fixture'),self.root,777)
        storage.assert_not_called();opened.assert_not_called();sent.assert_not_called()

    def handoff(self,mode):
        # Only our sleep child receives real signals. Proc/unit/Root positives
        # below are explicit doubles exercising pidfd ownership and cleanup.
        folder=self.root/('wdv-container-publish-'+uuid.uuid4().hex);folder.mkdir();state=self.root/'state';state.mkdir();source=self.root/'source';source.mkdir();purges=self.root/'purges';purges.mkdir()
        identifier=str(uuid.uuid4());op=cut.execution(identifier);src='a'*32;purge='c'*32;group='whaledeck-container-recycle-archives-'+uuid.uuid4().hex+'.service';inv='b'*32
        groups=self.root/'groups';groups.mkdir();(groups/group).mkdir()
        for path in (state/(uuid.UUID(op).hex+'.lock'),source/(src+'.lock'),purges/(purge+'.lock')):path.touch(mode=0o600)
        child=subprocess.Popen(['/bin/sleep','20']);opened=[];diagnostics={};real_open=os.pidfd_open;real_path=Path;checked=[];sent=[];real_send=signal.pidfd_send_signal
        def pidfd(pid):self.assertEqual(pid,child.pid);fd=real_open(pid);opened.append(fd);return fd
        def send(fd,sig):self.assertIn(fd,opened);sent.append(sig);return real_send(fd,sig)
        def path(value):return self.root if str(value)=='/data/WhaleDeck/apps' else real_path(value)
        @contextmanager
        def archive(_):
            yield (999,888),dict(currentHddFdAttested=True,hddDataReread=True,hddMetadataJoined=True)
            checked.append(True)
            if mode=='attest-failure':raise RuntimeError('synthetic final attest failure')
        row=dict(id=identifier,execution_job_id=op,container_id='a'*64,instance_id=str(child.pid)+':fixture',plan_json={})
        plan=dict(parentIdentity=list(cut.identity(folder)),command=dict(purge=dict(operationId=purge,plan=dict(command=dict(source=dict(operationId=src))))))
        try:
            with patch.multiple(cut,STATE=state,SOURCE_STATE=source,PURGE_STATE=purges,CGROUP=groups),patch.object(cut,'Path',side_effect=path),patch.object(cut,'managed_plan',return_value=plan),patch.object(cut,'current_archive',side_effect=archive),patch.object(cut,'unit_identity',return_value=(child.pid,inv)),patch.object(cut,'bound_process',return_value=True),patch.object(cut,'record_for',return_value={'state':'Applying' if mode=='past-reserved' else 'Reserved'}),patch.object(cut,'before_copy_proof',return_value=None if mode=='past-reserved' else {}),patch.object(cut.os,'pidfd_open',side_effect=pidfd),patch.object(cut.signal,'pidfd_send_signal',side_effect=send):
                if mode=='attest-failure':
                    with self.assertRaisesRegex(RuntimeError,'attest failure'):cut.freeze_before_copy(row,folder,child.pid,diagnostics)
                else:
                    held=cut.freeze_before_copy(row,folder,child.pid,diagnostics)
                    if mode=='past-reserved':self.assertIsNone(held)
                    else:
                        self.assertIsNotNone(held);self.assertEqual(checked,[True]);self.assertTrue(held.proof['fiveKernelFdProofs']);held.release();held.release();self.assertIsNone(held.descriptor)
            child.wait(timeout=2);self.assertEqual(child.returncode,-signal.SIGKILL);self.assertNotIn(signal.SIGCONT,sent)
            self.assertTrue(diagnostics['boundOriginal'] and diagnostics['stopSent']);self.assertEqual(diagnostics['cutAccepted'],mode=='success');self.assertEqual(len(opened),1)
            with self.assertRaises(OSError):os.fstat(opened[0])
        finally:
            if child.poll() is None:child.kill();child.wait(timeout=2)

    def test_final_attestation_failure_kills_only_bound_child_and_closes_pidfd(self):self.handoff('attest-failure')
    def test_late_root_phase_rejects_cut_without_resuming_apply(self):self.handoff('past-reserved')
    def test_handoff_waits_for_attestation_and_cleanup_never_resumes_apply(self):self.handoff('success')


if __name__=='__main__':unittest.main()
