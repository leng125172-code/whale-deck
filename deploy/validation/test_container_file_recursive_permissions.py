"""Actual Linux recursive inode changes; HDD attestation is explicitly synthetic."""
import hashlib
import json
import os
import stat
import signal
import struct
from types import SimpleNamespace
import unittest
import uuid
from unittest.mock import patch

import test_container_file_permissions as single
from test_container_file_staging import load

recursive=load('whaledeck-container-recursive-permissions.py','recursive_permission_candidate')


@unittest.skipUnless(os.name=='posix' and os.geteuid()==0,'isolated Linux root required')
class RecursivePermissionTests(unittest.TestCase):
    def setUp(self):
        self.base=single.LinuxContainerPermissionTests();self.base.setUp();self.addCleanup(self.base.doCleanups)
        self.tree=self.base.root/'tree';self.tree.mkdir(mode=0o750);self.tree.chmod(0o750)
        (self.tree/'sub').mkdir(mode=0o700);(self.tree/'sub').chmod(0o700)
        self.content=hashlib.shake_256(b'recursive-original-content').digest(2*1024**2+7)
        for path,mode in [('one.bin',0o640),('sub/中文.bin',0o600)]:
            f=self.tree/path;f.write_bytes(self.content);f.chmod(mode);os.chown(f,2100,3100)
        self.state=self.base.disk.disk/'recursive-state';self.state.mkdir(mode=0o700)
        self.history=recursive.RecursivePermissionHistory(self.base.disk.engine)
        self.type=recursive.recursive_permissions_type(type(self.base.engine),single.directories.ContainerDirectories,single.candidate.ContainerPermissionHistory)
        self.engine=self.type(self.base.uploads,self.base.publisher,self.state,self.history)
    def command(self):
        return {**self.base.session.request,'path':'tree','fileMode':'0600','directoryMode':'0750','expectedVersion':self.base.r.version(self.base.session.request,'tree',self.tree.stat())}
    def request(self):return {'operationId':uuid.uuid4().hex,'owner':'A'*64,'plan':self.engine.plan(self.command(),self.base.session)}
    def snapshot(self):return {str(p.relative_to(self.tree)):list(self.base.r.fingerprint(p.stat())) for p in [self.tree,*self.tree.rglob('*')]}
    def test_plan_is_read_only_and_seals_all_descendants(self):
        before=self.snapshot();request=self.request();plan=request['plan']
        self.assertEqual(plan['entryCount'],4);self.assertEqual(plan['fileCount'],2);self.assertEqual(plan['directoryCount'],2);self.assertEqual(plan['changeCount'],2)
        self.assertEqual(before,self.snapshot());self.assertEqual(list(self.state.iterdir()),[])
        self.assertFalse((self.base.disk.target/recursive.NAMESPACE).exists())
    def test_complete_preserves_inodes_owners_content_and_mtime_and_never_replays_chmod(self):
        before=self.snapshot();request=self.request();result=self.engine.apply(request,self.base.session)
        self.assertEqual(result['state'],'Completed');self.assertTrue(result['bindingCurrent']);self.assertEqual(result['completedCount'],4)
        self.assertEqual(result['changedCount'],2)
        after=self.snapshot()
        for path,old in before.items():
            actual=after[path];self.assertEqual(actual[:2],old[:2]);self.assertEqual(actual[3:8],old[3:8])
            self.assertEqual(actual[2]&0o777,0o750 if (self.tree/path).is_dir() else 0o600)
        self.assertEqual((self.tree/'one.bin').read_bytes(),self.content)
        self.assertEqual((self.tree/'sub/中文.bin').read_bytes(),self.content)
        with patch.object(os,'fchmod',side_effect=AssertionError('must not repeat chmod')):
            replay=self.engine.apply(request,self.base.session)
        self.assertEqual(replay['state'],'Completed');self.assertTrue(replay['replayed']);self.assertEqual(after,self.snapshot())
    def test_changed_child_after_plan_rejects_before_any_write(self):
        request=self.request();(self.tree/'sub/中文.bin').chmod(0o644);before=self.snapshot()
        with self.assertRaises(self.base.r.Rejected):self.engine.apply(request,self.base.session)
        self.assertEqual(before,self.snapshot())
    def test_symlink_hardlink_fifo_is_refused_during_plan(self):
        bad=self.tree/'bad'
        for kind in ('symlink','hardlink','fifo'):
            with self.subTest(kind=kind):
                if kind=='symlink':bad.symlink_to(self.tree/'one.bin')
                elif kind=='hardlink':os.link(self.tree/'one.bin',bad)
                else:os.mkfifo(bad)
                with self.assertRaises((self.base.r.Rejected,OSError)):self.engine.plan(self.command(),self.base.session)
                bad.unlink()
    def test_entry_and_depth_budgets_refuse_without_mutation(self):
        before=self.snapshot()
        for limits in ({'max_entries':2},{'max_depth':0}):
            engine=self.type(self.base.uploads,self.base.publisher,self.state,self.history,**limits)
            with self.subTest(limits=limits),self.assertRaises(self.base.r.Rejected):engine.plan(self.command(),self.base.session)
        self.assertEqual(before,self.snapshot())
    def test_discard_before_apply_is_a_permanent_fence(self):
        request=self.request();before=self.snapshot();self.assertEqual(self.engine.discard(request,self.base.session)['state'],'Aborted')
        with patch.object(os,'fchmod',side_effect=AssertionError('must not apply after discard')):result=self.engine.apply(request,self.base.session)
        self.assertEqual(result['state'],'Aborted');self.assertEqual(before,self.snapshot())

    def test_cancellation_after_one_changed_inode_is_partial_and_never_resumes(self):
        (self.tree/'sub/中文.bin').chmod(0o644)
        request=self.request();flag=[False];self.engine.cancelled=lambda:flag[0]
        self.engine.checkpoint=lambda phase,index: flag.__setitem__(0,True) if phase=='AfterEntry' and index==0 else None
        with self.assertRaises(self.base.r.Rejected):self.engine.apply(request,self.base.session)
        changed=self.snapshot()
        with patch.object(os,'fchmod',side_effect=AssertionError('must not continue remaining entries')):
            result=self.engine.inspect(request,self.base.session);replay=self.engine.apply(request,self.base.session)
        self.assertEqual(result['state'],'Partial');self.assertEqual(result['changedCount'],1);self.assertEqual(result['notStartedCount'],3)
        self.assertEqual(replay['state'],'Partial');self.assertEqual(changed,self.snapshot())
        self.assertEqual((self.tree/'one.bin').stat().st_mode&0o777,0o640)

    def killed(self,request,phase):
        pid=os.fork()
        if pid==0:
            try:
                self.engine.checkpoint=lambda at,index: os.kill(os.getpid(),signal.SIGKILL) if at==phase and index==0 else None
                self.engine.apply(request,self.base.session)
            except BaseException:os._exit(2)
            os._exit(3)
        _,status=os.waitpid(pid,0);self.assertTrue(os.WIFSIGNALED(status));self.assertEqual(os.WTERMSIG(status),signal.SIGKILL)

    def test_real_sigkill_after_first_item_keeps_partial_history_and_never_reapplies(self):
        (self.tree/'sub/中文.bin').chmod(0o644);request=self.request();self.killed(request,'AfterEntry');after=self.snapshot()
        with patch.object(os,'fchmod',side_effect=AssertionError('inspect must not chmod')):result=self.engine.inspect(request,self.base.session)
        self.assertEqual(result['state'],'Partial');self.assertEqual(result['changedCount'],1);self.assertEqual(after,self.snapshot())
        self.assertTrue(result['historyAvailable'])

    def test_real_sigkill_after_marker_before_first_item_proves_no_mutation_and_seals(self):
        request=self.request();before=self.snapshot();self.killed(request,'BeforeEntry')
        with patch.object(os,'fchmod',side_effect=AssertionError('must not start from interrupted marker')):
            result=self.engine.inspect(request,self.base.session);again=self.engine.apply(request,self.base.session)
        self.assertEqual(result['state'],'Aborted');self.assertEqual(again['state'],'Aborted');self.assertEqual(before,self.snapshot())

    def test_real_sigkill_after_actual_fchmod_before_child_completion_recovers_without_chmod(self):
        (self.tree/'sub/中文.bin').chmod(0o644);request=self.request();real=os.fchmod;pid=os.fork()
        if pid==0:
            def changed(fd,mode):real(fd,mode);os.kill(os.getpid(),signal.SIGKILL)
            try:
                with patch.object(os,'fchmod',side_effect=changed):self.engine.apply(request,self.base.session)
            except BaseException:os._exit(2)
            os._exit(3)
        _,status=os.waitpid(pid,0);self.assertTrue(os.WIFSIGNALED(status));self.assertEqual(os.WTERMSIG(status),signal.SIGKILL)
        after=self.snapshot()
        with patch.object(os,'fchmod',side_effect=AssertionError('must reconcile already changed inode')):result=self.engine.inspect(request,self.base.session)
        self.assertEqual(result['state'],'Partial');self.assertEqual(result['changedCount'],1);self.assertEqual(after,self.snapshot())

    def test_manifest_tampering_cannot_claim_current_binding_or_repeat_writes(self):
        request=self.request();self.engine.apply(request,self.base.session);before=self.snapshot()
        path=self.base.disk.target/recursive.NAMESPACE/(request['operationId']+'.json')
        saved=json.loads(path.read_bytes());saved['entries'][0]['plan']['command']['mode']='0777';path.write_text(json.dumps(saved))
        with patch.object(os,'fchmod',side_effect=AssertionError('tampered history must never chmod')):result=self.engine.apply(request,self.base.session)
        self.assertEqual(result['state'],'Completed');self.assertFalse(result['bindingCurrent']);self.assertFalse(result['historyAvailable']);self.assertEqual(before,self.snapshot())

    def test_missing_aggregate_journal_never_resumes_from_hdd_snapshot(self):
        request=self.request();self.engine.apply(request,self.base.session);before=self.snapshot();(self.state/(request['operationId']+'.json')).unlink()
        with patch.object(os,'fchmod',side_effect=AssertionError('missing root journal must not restart')):result=self.engine.apply(request,self.base.session)
        self.assertEqual(result['state'],'Unknown');self.assertEqual(before,self.snapshot())

    def test_real_acl_in_descendant_is_rejected_without_masking_or_chmod(self):
        leaf=self.tree/'sub/中文.bin'
        entries=[(1,6,0xffffffff),(2,4,2300),(4,4,0xffffffff),(16,4,0xffffffff),(32,0,0xffffffff)]
        acl=struct.pack('<I',2)+b''.join(struct.pack('<HHI',*entry) for entry in entries)
        os.setxattr(leaf,'system.posix_acl_access',acl);before=self.snapshot()
        with self.assertRaisesRegex(self.base.r.Rejected,'EXTENDED_METADATA_REQUIRED'):self.request()
        self.assertEqual(os.getxattr(leaf,'system.posix_acl_access'),acl);self.assertEqual(before,self.snapshot())
        self.assertEqual(list(self.state.iterdir()),[])

    def test_noop_and_zero_modes_complete_without_content_owner_or_replay_changes(self):
        command=self.command();command.update(fileMode='0000',directoryMode='0000')
        request={'operationId':uuid.uuid4().hex,'owner':'A'*64,'plan':self.engine.plan(command,self.base.session)}
        result=self.engine.apply(request,self.base.session);self.assertEqual(result['state'],'Completed')
        before=self.snapshot();self.assertTrue(all(row[2]&0o777==0 for row in before.values()))
        command['expectedVersion']=self.base.r.version(self.base.session.request,'tree',self.tree.stat())
        noop={'operationId':uuid.uuid4().hex,'owner':'A'*64,'plan':self.engine.plan(command,self.base.session)}
        with patch.object(os,'fchmod',side_effect=AssertionError('no-op must not chmod')):result=self.engine.apply(noop,self.base.session)
        self.assertEqual((result['state'],result['changedCount']),('Completed',0));self.assertTrue(result['bindingCurrent'])
        self.assertEqual(before,self.snapshot());self.assertEqual((self.tree/'sub/中文.bin').read_bytes(),self.content)

    def test_capacity_failure_before_any_inode_change_is_permanently_aborted(self):
        request=self.request();before=self.snapshot();original=os.fstatvfs
        def no_capacity(fd):
            target=self.base.disk.target/recursive.NAMESPACE
            if target.exists() and os.fstat(fd).st_ino==target.stat().st_ino:
                return SimpleNamespace(f_bavail=0,f_frsize=4096,f_blocks=1000,f_flag=0)
            return original(fd)
        with patch.object(os,'fstatvfs',side_effect=no_capacity),self.assertRaisesRegex(self.base.f.Rejected,'HISTORY_CAPACITY'):
            self.engine.apply(request,self.base.session)
        self.assertEqual(before,self.snapshot())
        with patch.object(os,'fchmod',side_effect=AssertionError('capacity failure cannot restart original operation')):
            result=self.engine.apply(request,self.base.session)
        self.assertEqual(result['state'],'Aborted');self.assertFalse(result['historyAvailable'])

    def test_readonly_and_changed_mount_proof_fail_before_any_mode_write(self):
        self.base.session.container['HostConfig']['ReadonlyRootfs']=True;before=self.snapshot()
        with self.assertRaisesRegex(self.base.r.Rejected,'READ_ONLY'):self.request()
        self.base.session.container['HostConfig']['ReadonlyRootfs']=False
        request=self.request();self.base.session.mounts=b'N'*32
        with self.assertRaises(self.base.r.Rejected):self.engine.apply(request,self.base.session)
        self.assertEqual(before,self.snapshot())

    def test_malformed_consistently_hashed_manifest_is_fenced_unknown(self):
        # Root-owned corrupted history, with every checksum recomputed: digest
        # checking alone cannot substitute for strict structural validation.
        for malformed in ({'relativePath':[], 'plan':None},{'relativePath':'sub/中文.bin','plan':{}},
                {'relativePath':'sub/中文.bin','plan':{'command':None}}):
            with self.subTest(malformed=malformed):
                request=self.request();self.killed(request,'BeforeEntry');before=self.snapshot()
                path=self.base.disk.target/recursive.NAMESPACE/(request['operationId']+'.json')
                saved=json.loads(path.read_bytes());saved['entries'][0]=malformed
                plan=request['plan'];plan['manifestDigest']=self.engine.manifest_digest(saved['entries']);plan['fingerprint']=self.engine.digest(plan)
                saved['plan']=plan;saved['historyDigest']=recursive.digest(self.history.DOMAIN,{k:v for k,v in saved.items() if k!='historyDigest'})
                path.write_text(json.dumps(saved))
                receipt=self.state/(request['operationId']+'.json');record=json.loads(receipt.read_bytes());record['plan']=plan;receipt.write_text(json.dumps(record))
                with patch.object(os,'fchmod',side_effect=AssertionError('malformed history must never apply')):
                    result=self.engine.inspect(request,self.base.session);again=self.engine.apply(request,self.base.session)
                self.assertEqual(result['state'],'Unknown');self.assertEqual(again['state'],'Unknown')
                self.assertFalse(result['historyAvailable']);self.assertEqual(before,self.snapshot())

    def test_completed_receipt_does_not_claim_current_binding_after_external_mode_change(self):
        request=self.request();self.engine.apply(request,self.base.session);(self.tree/'one.bin').chmod(0o644);before=self.snapshot()
        with patch.object(os,'fchmod',side_effect=AssertionError('completed receipt must not repair drift')):
            result=self.engine.apply(request,self.base.session)
        self.assertEqual(result['state'],'Completed');self.assertFalse(result['bindingCurrent']);self.assertTrue(result['historyAvailable'])
        self.assertEqual(before,self.snapshot())

    def test_new_child_after_partial_cannot_be_adopted_or_auto_completed(self):
        (self.tree/'sub/中文.bin').chmod(0o644);request=self.request();self.killed(request,'AfterEntry');(self.tree/'foreign').write_bytes(b'outside approved inventory')
        with patch.object(os,'fchmod',side_effect=AssertionError('must not adopt foreign child')):result=self.engine.inspect(request,self.base.session)
        self.assertEqual(result['state'],'Unknown');self.assertEqual((self.tree/'foreign').stat().st_mode&0o777,0o644)

    def test_missing_previously_finished_child_journal_stays_unknown(self):
        (self.tree/'sub/中文.bin').chmod(0o644);request=self.request();self.killed(request,'AfterEntry');before=self.snapshot()
        saved=self.history.load(request);sub=self.engine.child_request(request,0,saved['entries'][0])
        (self.state/(request['operationId']+'.entries')/(sub['operationId']+'.json')).unlink()
        with patch.object(os,'fchmod',side_effect=AssertionError('lost child receipt must not repeat mutation')):result=self.engine.inspect(request,self.base.session)
        self.assertEqual(result['state'],'Unknown');self.assertEqual(before,self.snapshot())

    def test_discard_after_missing_aggregate_fences_existing_hdd_and_never_claims_aborted(self):
        request=self.request();self.engine.apply(request,self.base.session);before=self.snapshot();(self.state/(request['operationId']+'.json')).unlink()
        with patch.object(os,'fchmod',side_effect=AssertionError('missing root discard must never chmod')):
            fenced=self.engine.discard(request,self.base.session);replay=self.engine.apply(request,self.base.session)
        self.assertEqual((fenced['state'],fenced['unknownCount']),('Unknown',4));self.assertTrue(fenced['historyMayExist'])
        self.assertEqual(replay['state'],'Unknown');self.assertEqual(before,self.snapshot())

    def test_discard_after_missing_aggregate_and_hdd_uses_child_state_as_permanent_unknown_fence(self):
        (self.tree/'sub/中文.bin').chmod(0o644);request=self.request();self.killed(request,'AfterEntry');before=self.snapshot()
        (self.state/(request['operationId']+'.json')).unlink()
        (self.base.disk.target/recursive.NAMESPACE/(request['operationId']+'.json')).unlink()
        with patch.object(os,'fchmod',side_effect=AssertionError('remaining child receipts forbid fresh execution')):
            fenced=self.engine.discard(request,self.base.session);replay=self.engine.apply(request,self.base.session)
        self.assertEqual(fenced['state'],'Unknown');self.assertEqual(replay['state'],'Unknown');self.assertEqual(before,self.snapshot())

    def test_hdd_child_artifacts_fence_discard_when_aggregate_and_local_state_are_lost(self):
        request=self.request();self.engine.apply(request,self.base.session);before=self.snapshot()
        (self.state/(request['operationId']+'.json')).unlink()
        (self.base.disk.target/recursive.NAMESPACE/(request['operationId']+'.json')).unlink()
        children=self.state/(request['operationId']+'.entries')
        for path in children.iterdir():self.assertTrue(path.is_file());path.unlink()
        children.rmdir()
        with patch.object(os,'fchmod',side_effect=AssertionError('HDD child history forbids a fresh operation')):
            result=self.engine.discard(request,self.base.session);replay=self.engine.apply(request,self.base.session)
        self.assertEqual(result['state'],'Unknown');self.assertTrue(result['historyMayExist'])
        self.assertEqual(replay['state'],'Unknown');self.assertEqual(before,self.snapshot())

    def test_unavailable_hdd_cannot_be_treated_as_absence_during_fresh_apply_or_discard(self):
        request=self.request();before=self.snapshot()
        with patch.object(self.history.storage,'configuration',side_effect=FileNotFoundError('synthetic missing policy')):
            for action in ('apply','discard'):
                with self.subTest(action=action),self.assertRaises(FileNotFoundError):getattr(self.engine,action)(request,self.base.session)
        self.assertFalse((self.state/(request['operationId']+'.json')).exists());self.assertEqual(before,self.snapshot())
