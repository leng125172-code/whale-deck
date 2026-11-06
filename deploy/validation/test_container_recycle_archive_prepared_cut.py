"""Real temporary Prepared disk I/O and owned child pidfds; Proc positives fake.

Native checkpoint is confined to isolated kernel tests, never installed helper
injection. These checks do not sign real UI/identity/Unknown acceptance.
"""
import copy,json,os,signal,subprocess,tempfile,unittest,uuid
from pathlib import Path
from contextlib import contextmanager
from unittest.mock import patch
import bound_container_recycle_archive_prepared_cut as cut
import test_container_recycle_archive_rpc_proof as native
from test_container_recycle_purge_plan import snapshot


class PreparedReached(Exception):pass


@unittest.skipUnless(os.name=='posix' and os.geteuid()==0,'temporary root Linux files required')
class PreparedDiskTests(unittest.TestCase):
    def setUp(self):
        self.f=native.ArchiveRecordJoinTests();self.f.setUp();self.addCleanup(self.f.doCleanups)
        self.engine=self.f.engine;self.q=self.f.q;self.folder=self.f.f.work;self.state=self.f.state;self.path=self.state/(self.q['operationId']+'.json')
        self.engine.reserve(self.q,self.f.f.session);self.reserved=json.loads(self.path.read_bytes())
        self.patch=patch.multiple(cut,SOURCE_STATE=self.f.f.base.state,PURGE_STATE=self.f.f.state,CLAIMS=self.engine.restore.claims_path);self.patch.start();self.addCleanup(self.patch.stop)

    def prepare(self):
        def reached(phase):
            if phase=='Prepared':self.record=json.loads(self.path.read_bytes());raise PreparedReached()
        with patch.object(self.engine,'checkpoint',side_effect=reached),self.assertRaises(PreparedReached):self.engine.apply(self.q,self.f.f.session)
        self.assertEqual(json.loads(self.path.read_bytes())['state'],'Unknown')
        return self.record

    def test_actual_fsynced_prepared_copy_has_full_bytes_metadata_and_no_target(self):
        before=[snapshot(p) for p in (self.f.f.base.state,self.f.f.state,self.f.f.base.target)]
        r=self.prepare();v=cut.prepared_proof(r,self.folder);self.assertTrue(v['stagedFullCopyVerified'] and v['publicationNotEntered'] and v['targetAbsent']);self.assertFalse(v['terminalCopyJournalSaved'] or v['hddDataReread'])
        self.assertEqual(v['physical']['staged']['sha256'],self.f.data['sha256']);self.assertEqual(v['physical']['staged']['attributes'],self.f.f.original['attributes']);self.assertFalse((self.folder/'recycle-rpc.bin').exists())
        self.assertEqual(before,[snapshot(p) for p in (self.f.f.base.state,self.f.f.state,self.f.f.base.target)])

    def test_reserved_copying_publishing_completed_or_unknown_cannot_pass(self):
        for state in ('Preparing','Reserved','Applying','Copying','Publishing','Published','Completed','Aborted','Unknown'):
            with self.subTest(state=state),patch.object(cut,'source_proof') as read:self.assertIsNone(cut.prepared_proof({**self.reserved,'state':state},self.folder));read.assert_not_called()

    def test_mismatched_prepared_pending_or_published_identity_is_refused(self):
        r=self.prepare()
        for key,bad in (('privateIdentity',[1,2]),('pendingIdentity',[1,3]),('preparedIdentity',[1]*9),('publishedIdentity',r['preparedIdentity']),('parentAfterStage','0'*64)):
            with self.subTest(key=key),self.assertRaises(AssertionError):cut.prepared_proof({**r,key:bad},self.folder)

    def test_foreign_target_staged_data_or_xattr_change_is_never_accepted(self):
        r=self.prepare();target=self.folder/'recycle-rpc.bin';target.write_bytes(b'foreign')
        try:
            with self.assertRaises(AssertionError):cut.prepared_proof(r,self.folder)
        finally:target.unlink()
        # Parent version changed: separate fresh fixtures for payload mutations.
        for change in ('bytes','attributes'):
            other=PreparedDiskTests();other.setUp()
            try:
                record=other.prepare();stage=other.folder/(cut.STAGE_PREFIX+other.q['operationId'])/'copy.partial'
                if change=='bytes':
                    with stage.open('r+b') as out:out.write(b'corrupt')
                else:os.setxattr(stage,'user.foreign',b'changed')
                with self.subTest(change=change),self.assertRaises(AssertionError):cut.prepared_proof(record,other.folder)
            finally:other.doCleanups()

    def test_claim_or_immutable_original_lifecycle_change_is_refused(self):
        r=self.prepare();op=self.f.f.ref['operationId'];source=self.f.f.base.state/(op+'.json');claim=self.engine.restore.claims_path/(op+'.json')
        for path,key,bad in ((source,'state','Restored'),(source,'owner','F'*64),(claim,'restoreOperationId','f'*32),(claim,'restoreOwner','F'*64)):
            raw=path.read_bytes();v=json.loads(raw);path.write_text(json.dumps({**v,key:bad}));path.chmod(0o600)
            try:
                with self.subTest(key=key),self.assertRaises(AssertionError):cut.prepared_proof(r,self.folder)
            finally:path.write_bytes(raw)

    def test_unexpected_stage_entry_symlink_or_neighbor_is_refused(self):
        r=self.prepare();stage=self.folder/(cut.STAGE_PREFIX+self.q['operationId']);extra=stage/'foreign';extra.write_bytes(b'preserve')
        with self.assertRaises(AssertionError):cut.prepared_proof(r,self.folder)
        extra.unlink();neighbor=self.folder/'foreign-neighbor';neighbor.write_bytes(b'preserve')
        with self.assertRaises(AssertionError):cut.prepared_proof(r,self.folder)

    def test_unknown_observation_keeps_stage_and_never_replays_copy_or_publish(self):
        self.prepare();before=snapshot(self.folder);protected=[snapshot(p) for p in (self.f.f.base.state,self.f.f.state,self.f.f.base.target)]
        with patch.object(self.engine.f,'exchange',side_effect=AssertionError('no publish replay')),patch.object(os,'write',side_effect=AssertionError('no copy replay')):
            for action in (self.engine.inspect,self.engine.discard,self.engine.apply):
                r=action(self.q,self.f.f.session);self.assertEqual(r['state'],'Unknown');self.assertIsNone(r['archiveCopied']);self.assertTrue(r['sourceFenced']);self.assertEqual(before,snapshot(self.folder))
        self.assertEqual(protected,[snapshot(p) for p in (self.f.f.base.state,self.f.f.state,self.f.f.base.target)])


@unittest.skipUnless(os.name=='posix' and os.geteuid()==0,'temporary root Linux files/pidfds required')
class PreparedProcessTests(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory();self.addCleanup(self.tmp.cleanup);self.root=Path(self.tmp.name)

    def test_eight_distinct_actual_fd_identities_and_exact_invocation_required(self):
        proc=self.root/'proc';p=proc/'321';p.mkdir(parents=True);(p/'fd').mkdir();group='whaledeck-container-recycle-archives-'+'a'*32+'.service';inv='b'*32
        (p/'cmdline').write_bytes(b'/usr/bin/python3\0-I\0'+cut.before.TRANSPORT+b'\0apply\0');(p/'cgroup').write_bytes(b'0::/system.slice/'+group.encode()+b'\n');(p/'environ').write_bytes(b'INVOCATION_ID='+inv.encode()+b'\0');ids=[]
        for at in range(8):
            path=self.root/('fd-'+str(at));path.touch(mode=0o600);(p/'fd'/str(at+3)).symlink_to(path);ids.append(cut.identity(path))
        with patch.object(cut,'PROC',proc),patch.object(cut.before,'PROC',proc):
            args=(321,group,inv,*ids);self.assertTrue(cut.bound_prepared_process(*args))
            self.assertFalse(cut.bound_prepared_process(321,group,inv,*ids[:7],ids[6]))
            for at in range(8):
                path=p/'fd'/str(at+3);dest=path.readlink();path.unlink()
                try:self.assertFalse(cut.bound_prepared_process(*args))
                finally:path.symlink_to(dest)
            (p/'cmdline').write_bytes(b'/bin/sleep\0')
            self.assertFalse(cut.bound_prepared_process(*args))

    def test_unrelated_actual_child_with_all_eight_fds_cannot_borrow_apply(self):
        fds=[];ids=[]
        try:
            for at in range(8):
                p=self.root/('real-'+str(at));p.touch(mode=0o600);fds.append(os.open(p,os.O_RDONLY));ids.append(cut.identity(p))
            child=subprocess.Popen(['/bin/sleep','5'],pass_fds=fds)
            try:self.assertFalse(cut.bound_prepared_process(child.pid,'whaledeck-container-recycle-archives-'+'a'*32+'.service','b'*32,*ids))
            finally:child.terminate();child.wait(timeout=2)
        finally:
            for fd in fds:os.close(fd)

    def test_foreign_fixture_is_rejected_before_pidfd_storage_or_signal(self):
        with patch.object(cut.before,'current_archive') as storage,patch.object(cut.os,'pidfd_open') as opened,patch.object(cut.signal,'pidfd_send_signal') as sent,self.assertRaises(AssertionError):cut.freeze_prepared(dict(container_id='a'*64,instance_id='777:fixture'),self.root,777)
        storage.assert_not_called();opened.assert_not_called();sent.assert_not_called()

    def handoff(self,mode):
        # Actual signals only target this sleep child. Native/Proc/unit positives
        # are explicit doubles exercising descriptor ownership and final attest.
        folder=self.root/('wdv-container-publish-'+uuid.uuid4().hex);folder.mkdir();state=self.root/'state';state.mkdir();source=self.root/'source';source.mkdir();purges=self.root/'purges';purges.mkdir();groups=self.root/'groups';groups.mkdir()
        identifier=str(uuid.uuid4());op=cut.execution(identifier);src='a'*32;purge='c'*32;group='whaledeck-container-recycle-archives-'+uuid.uuid4().hex+'.service';(groups/group).mkdir();inv='b'*32
        for path in (state/(uuid.UUID(op).hex+'.lock'),source/(src+'.lock'),purges/(purge+'.lock')):path.touch(mode=0o600)
        child=subprocess.Popen(['/bin/sleep','20']);opened=[];sent=[];real_open=os.pidfd_open;real_send=signal.pidfd_send_signal;real_path=Path;diagnostics={};attested=[]
        def pidfd(pid):self.assertEqual(pid,child.pid);fd=real_open(pid);opened.append(fd);return fd
        def send(fd,sig):self.assertIn(fd,opened);sent.append(sig);return real_send(fd,sig)
        def path(value):return self.root if str(value)=='/data/WhaleDeck/apps' else real_path(value)
        @contextmanager
        def archive(_):
            yield (999,888),dict(currentHddFdAttested=True,hddDataReread=True,hddMetadataJoined=True)
            attested.append(True)
            if mode=='attest-failure':raise RuntimeError('synthetic final attest failure')
        plan=dict(parentIdentity=list(cut.identity(folder)),command=dict(purge=dict(operationId=purge,plan=dict(command=dict(source=dict(operationId=src))))))
        record=dict(state='Prepared',privateIdentity=[1,50001],pendingIdentity=[1,50002]);row=dict(id=identifier,execution_job_id=op,container_id='a'*64,instance_id=str(child.pid)+':fixture',plan_json={});calls=[]
        def read(_):calls.append(True);return {**record,'state':'Publishing'} if mode=='late-phase' and len(calls)>1 else record
        try:
            with patch.multiple(cut,STATE=state,SOURCE_STATE=source,PURGE_STATE=purges,CGROUP=groups),patch.object(cut,'Path',side_effect=path),patch.object(cut,'managed_plan',return_value=plan),patch.object(cut.before,'current_archive',side_effect=archive),patch.object(cut.before,'unit_identity',return_value=(child.pid,inv)),patch.object(cut.before,'bound_process',return_value=True),patch.object(cut,'bound_prepared_process',return_value=True),patch.object(cut.before,'record_for',side_effect=read),patch.object(cut,'prepared_proof',side_effect=lambda r,_:{} if r['state']=='Prepared' else None),patch.object(cut.os,'pidfd_open',side_effect=pidfd),patch.object(cut.signal,'pidfd_send_signal',side_effect=send):
                if mode=='attest-failure':
                    with self.assertRaisesRegex(RuntimeError,'attest failure'):cut.freeze_prepared(row,folder,child.pid,diagnostics)
                else:
                    held=cut.freeze_prepared(row,folder,child.pid,diagnostics)
                    if mode=='late-phase':self.assertIsNone(held)
                    else:self.assertIsNotNone(held);self.assertEqual(attested,[True]);self.assertTrue(held.proof['eightKernelFdProofs']);held.release();self.assertIsNone(held.descriptor)
            child.wait(timeout=2);self.assertEqual(child.returncode,-signal.SIGKILL);self.assertNotIn(signal.SIGCONT,sent);self.assertTrue(diagnostics['boundOriginal'] and diagnostics['stopSent']);self.assertEqual(diagnostics['cutAccepted'],mode=='success')
            with self.assertRaises(OSError):os.fstat(opened[0])
        finally:
            if child.poll() is None:child.kill();child.wait(timeout=2)

    def test_final_attestation_failure_kills_bound_child_and_closes_pidfd(self):self.handoff('attest-failure')
    def test_phase_race_after_stop_is_missed_and_never_resumes(self):self.handoff('late-phase')
    def test_success_handoff_only_after_final_attestation_and_kill_only_release(self):self.handoff('success')


if __name__=='__main__':unittest.main()
