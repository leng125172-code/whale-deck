"""Real Linux fchown and process-loss checks; HDD attestation is synthetic here."""
import copy
import hashlib
import json
import os
import signal
import stat
import struct
from types import SimpleNamespace
import unittest
import uuid
from unittest.mock import patch

import test_container_file_ownership as single
import test_container_file_permissions as modes
from test_container_file_staging import load

recursive = load('whaledeck-container-recursive-permissions.py','recursive_owner_shared')
ownership = load('whaledeck-container-recursive-ownership.py','recursive_owner_candidate')


@unittest.skipUnless(os.name == 'posix' and os.geteuid() == 0, 'isolated Linux root required')
class RecursiveOwnershipTests(unittest.TestCase):
    def setUp(self):
        self.base = single.LinuxContainerOwnershipTests(); self.base.setUp(); self.addCleanup(self.base.doCleanups)
        self.tree = self.base.root/'tree'; self.tree.mkdir(mode=0o750); (self.tree/'sub').mkdir(mode=0o700)
        self.paths = ('','one.bin','sub','sub/中文.bin')
        self.content = hashlib.shake_256(b'recursive-ownership-original').digest(2*1024**2+7)
        for i,rel in enumerate(self.paths):
            path = self.tree/rel
            if rel.endswith('.bin'):
                path.write_bytes(self.content); path.chmod(0o640); os.setxattr(path,'user.preserve',b'ordinary attribute')
            os.chown(path,2100+i,3100+i); os.utime(path,ns=(1700000000123456789,1700000000123456789))
        self.state = self.base.disk.disk/'recursive-owner-state'; self.state.mkdir(mode=0o700)
        self.history = ownership.recursive_ownership_history_type(recursive)(self.base.disk.engine)
        self.type = ownership.recursive_ownership_type(type(self.base.engine),single.directories.ContainerDirectories,single.candidate.ContainerOwnershipHistory,recursive)
        self.engine = self.type(self.base.uploads,self.base.publisher,self.state,self.history)

    def command(self,uid=2200,gid=3200):
        return {**self.base.session.request,'path':'tree','uid':uid,'gid':gid,'expectedVersion':self.base.r.version(self.base.session.request,'tree',self.tree.stat())}
    def request(self,uid=2200,gid=3200):
        return {'operationId':uuid.uuid4().hex,'owner':'A'*64,'plan':self.engine.plan(self.command(uid,gid),self.base.session)}
    def snapshot(self): return {rel:list(self.base.r.fingerprint((self.tree/rel).lstat())) for rel in self.paths}
    def assert_preserved(self,before,after):
        for rel,old in before.items():
            actual = after[rel]; self.assertEqual(actual[:3],old[:3]); self.assertEqual(actual[5:8],old[5:8])
            if rel.endswith('.bin'):
                self.assertEqual((self.tree/rel).read_bytes(),self.content)
                self.assertEqual(os.listxattr(self.tree/rel),['user.preserve']); self.assertEqual(os.getxattr(self.tree/rel,'user.preserve'),b'ordinary attribute')
    def killed(self,request,phase):
        pid = os.fork()
        if pid == 0:
            self.engine.checkpoint = lambda point,index: os.kill(os.getpid(),signal.SIGKILL) if point == phase and index == 0 else None
            try: self.engine.apply(request,self.base.session)
            except BaseException: os._exit(2)
            os._exit(3)
        _,status = os.waitpid(pid,0)
        self.assertTrue(os.WIFSIGNALED(status)); self.assertEqual(os.WTERMSIG(status),signal.SIGKILL)

    def test_plan_seals_full_tree_target_and_execution_identity_without_writes(self):
        before = self.snapshot(); plan = self.request()['plan']
        self.assertEqual((plan['entryCount'],plan['fileCount'],plan['directoryCount'],plan['changeCount']),(4,2,2,4))
        self.assertEqual((plan['uid'],plan['gid']),(0,0)); self.assertEqual((plan['command']['uid'],plan['command']['gid']),(2200,3200))
        self.assertEqual(plan['rootIdentity'],before['']); self.assertEqual(before,self.snapshot())
        self.assertEqual(list(self.state.iterdir()),[]); self.assertFalse((self.base.disk.target/ownership.NAMESPACE).exists())

    def test_full_fchown_preserves_content_modes_attrs_and_does_not_replay(self):
        before = self.snapshot(); request = self.request(); result = self.engine.apply(request,self.base.session); after = self.snapshot()
        self.assertEqual((result['state'],result['completedCount'],result['changedCount']),('Completed',4,4)); self.assertTrue(result['bindingCurrent'])
        self.assert_preserved(before,after)
        for identity in after.values(): self.assertEqual(identity[3:5],[2200,3200])
        saved = self.history.load(request); self.assertEqual(saved['resourceType'],'ContainerRecursiveOwnershipMetadata')
        self.assertEqual(saved['historyDigest'],result['historyDigest'])
        root = self.state/(request['operationId']+'.json'); raw = root.read_bytes()
        with patch.object(os,'fchown',side_effect=AssertionError('never redo ownership')),patch.object(os,'fchmod',side_effect=AssertionError('ownership must not chmod')):
            replay = self.engine.apply(request,self.base.session)
        self.assertEqual(replay['state'],'Completed'); self.assertTrue(replay['replayed']); self.assertEqual(root.read_bytes(),raw); self.assertEqual(after,self.snapshot())

    def test_independent_uid_gid_changes_and_explicit_root_then_noop(self):
        for uid,gid in ((2200,3100),(2200,3200),(0,0)):
            before = self.snapshot(); result = self.engine.apply(self.request(uid,gid),self.base.session)
            self.assertEqual(result['state'],'Completed'); self.assert_preserved(before,self.snapshot())
            self.assertTrue(all(value[3:5] == [uid,gid] for value in self.snapshot().values()))
        before = self.snapshot(); request = self.request(0,0)
        with patch.object(os,'fchown',side_effect=AssertionError('noop cannot chown')): result = self.engine.apply(request,self.base.session)
        self.assertEqual((result['state'],result['changedCount']),('Completed',0)); self.assertEqual(before,self.snapshot())

    def test_invalid_owner_values_and_permission_fields_are_refused(self):
        before = self.snapshot()
        for key in ('uid','gid'):
            for value in (True,False,-1,4294967295,'2200',1.0,None):
                command = {**self.command(),key:value}
                with self.subTest(key=key,value=value),self.assertRaisesRegex(self.base.r.Rejected,'UID_GID_INVALID'): self.engine.plan(command,self.base.session)
        with self.assertRaisesRegex(self.base.r.Rejected,'REQUEST_INVALID'): self.engine.plan({**self.command(),'fileMode':'0777'},self.base.session)
        self.assertEqual(before,self.snapshot()); self.assertFalse(list(self.state.iterdir()))

    def test_cross_history_and_child_engine_domains_are_rejected(self):
        wrong = recursive.RecursivePermissionHistory(self.base.disk.engine)
        with self.assertRaisesRegex(self.base.r.Rejected,'LIMITS_INVALID'): self.type(self.base.uploads,self.base.publisher,self.state,wrong)
        with self.assertRaisesRegex(ValueError,'CHILD_ENGINE_MISMATCH'):
            ownership.recursive_ownership_type(modes.candidate.container_permissions_type(single.directories.ContainerDirectories),single.directories.ContainerDirectories,single.candidate.ContainerOwnershipHistory,recursive)
        with self.assertRaises(ValueError): recursive.recursive_permissions_type(type(self.base.engine),single.directories.ContainerDirectories,single.candidate.ContainerOwnershipHistory,ownership='true')
        request = self.request(); mode_type = recursive.recursive_permissions_type(modes.candidate.container_permissions_type(single.directories.ContainerDirectories),single.directories.ContainerDirectories,modes.candidate.ContainerPermissionHistory)
        self.assertNotEqual(self.engine.manifest_digest([]),mode_type.manifest_digest([]))
        self.assertNotEqual(self.engine.child_request(request,0,{'plan':{}})['operationId'],mode_type.child_request(self.engine,request,0,{'plan':{}})['operationId'])

    def test_unconfirmed_child_owner_cannot_be_counted_completed(self):
        request = self.request(); original = type(self.base.engine).public
        def unconfirmed(child,*args,**kwargs): return {**original(child,*args,**kwargs),'ownerConfirmed':False}
        with patch.object(type(self.base.engine),'public',new=unconfirmed),self.assertRaisesRegex(self.base.r.Rejected,'ENTRY_RESULT_UNKNOWN'): self.engine.apply(request,self.base.session)
        record = json.loads((self.state/(request['operationId']+'.json')).read_bytes())
        self.assertEqual(record['state'],'Unknown'); self.assertEqual(record['completedCount'],0); self.assertGreater(record['unknownCount'],0)

    def test_boolean_schema_is_not_integer_version_even_with_recomputed_history_digest(self):
        request = self.request(); self.engine.apply(request,self.base.session)
        root = self.state/(request['operationId']+'.json'); record = json.loads(root.read_bytes()); record['schemaVersion'] = True; root.write_text(json.dumps(record))
        with self.assertRaisesRegex(self.base.r.Rejected,'RECEIPT_INVALID'): self.engine.inspect(request,self.base.session)
        path = self.base.disk.target/ownership.NAMESPACE/(request['operationId']+'.json'); saved = json.loads(path.read_bytes()); saved['schemaVersion'] = True
        saved['historyDigest'] = recursive.digest(self.history.DOMAIN,{k:v for k,v in saved.items() if k != 'historyDigest'}); path.write_text(json.dumps(saved))
        with self.assertRaisesRegex(self.base.f.Rejected,'HISTORY_CHANGED'): self.history.load(request)

    def test_link_special_file_special_mode_and_acl_are_rejected(self):
        bad = self.tree/'bad'
        for kind in ('symlink','hardlink','fifo'):
            if kind == 'symlink': bad.symlink_to(self.tree/'one.bin')
            elif kind == 'hardlink': os.link(self.tree/'one.bin',bad)
            else: os.mkfifo(bad)
            with self.subTest(kind=kind),self.assertRaises((self.base.r.Rejected,OSError)): self.request()
            bad.unlink()
        leaf = self.tree/'one.bin'; leaf.chmod(0o4640)
        with self.assertRaises(self.base.r.Rejected): self.request()
        leaf.chmod(0o640)
        acl = struct.pack('<I',2)+b''.join(struct.pack('<HHI',*entry) for entry in ((1,6,0xffffffff),(2,4,1234),(4,4,0xffffffff),(16,4,0xffffffff),(32,0,0xffffffff)))
        os.setxattr(leaf,'system.posix_acl_access',acl); before = self.snapshot()
        with self.assertRaisesRegex(self.base.r.Rejected,'EXTENDED_METADATA_REQUIRED'): self.request()
        self.assertEqual(os.getxattr(leaf,'system.posix_acl_access'),acl); self.assertEqual(before,self.snapshot()); self.assertFalse(list(self.state.iterdir()))

    def test_budget_and_tree_drift_fail_before_any_owner_write(self):
        for limits in ({'max_entries':2},{'max_depth':0}):
            limited = self.type(self.base.uploads,self.base.publisher,self.state,self.history,**limits)
            with self.subTest(limits=limits),self.assertRaises(self.base.r.Rejected): limited.plan(self.command(),self.base.session)
        request = self.request(); (self.tree/'one.bin').chmod(0o600); before = self.snapshot()
        with self.assertRaises(self.base.r.Rejected): self.engine.apply(request,self.base.session)
        self.assertEqual(before,self.snapshot())

    def test_partial_real_process_loss_never_restarts_remaining_fchown(self):
        request = self.request(); before = self.snapshot(); self.killed(request,'AfterEntry'); stopped = self.snapshot()
        self.assertEqual(stopped['sub/中文.bin'][3:5],[2200,3200]); self.assert_preserved(before,stopped)
        with patch.object(os,'fchown',side_effect=AssertionError('recovery cannot resume chown')):
            result = self.engine.inspect(request,self.base.session); replay = self.engine.apply(request,self.base.session)
        self.assertEqual((result['state'],result['changedCount'],result['notStartedCount']),('Partial',1,3))
        self.assertEqual(replay['state'],'Partial'); self.assertEqual(stopped,self.snapshot())

    def test_marker_only_crash_and_discard_are_permanent_without_owner_change(self):
        request = self.request(); before = self.snapshot(); self.killed(request,'BeforeEntry')
        with patch.object(os,'fchown',side_effect=AssertionError('marker cannot start chown')):
            result = self.engine.inspect(request,self.base.session); replay = self.engine.apply(request,self.base.session)
        self.assertEqual(result['state'],'Aborted'); self.assertEqual(replay['state'],'Aborted'); self.assertEqual(before,self.snapshot())
        request = self.request(); self.engine.discard(request,self.base.session)
        with patch.object(os,'fchown',side_effect=AssertionError('discard cannot be undone')): self.assertEqual(self.engine.apply(request,self.base.session)['state'],'Aborted')

    def test_finished_child_receipt_loss_stays_unknown(self):
        request = self.request(); self.killed(request,'AfterEntry'); before = self.snapshot(); saved = self.history.load(request)
        child = self.engine.child_request(request,0,saved['entries'][0]); (self.state/(request['operationId']+'.entries')/(child['operationId']+'.json')).unlink()
        with patch.object(os,'fchown',side_effect=AssertionError('receipt loss forbids redo')): result = self.engine.inspect(request,self.base.session)
        self.assertEqual(result['state'],'Unknown'); self.assertEqual(before,self.snapshot())

    def test_hdd_or_child_artifacts_fence_a_lost_aggregate(self):
        for remove_hdd in (False,True):
            request = self.request(); self.engine.apply(request,self.base.session); before = self.snapshot()
            (self.state/(request['operationId']+'.json')).unlink()
            if remove_hdd: (self.base.disk.target/ownership.NAMESPACE/(request['operationId']+'.json')).unlink()
            with patch.object(os,'fchown',side_effect=AssertionError('orphan artifacts forbid new execution')):
                result = self.engine.discard(request,self.base.session); replay = self.engine.apply(request,self.base.session)
            self.assertEqual(result['state'],'Unknown'); self.assertEqual(replay['state'],'Unknown'); self.assertEqual(before,self.snapshot())

    def test_rehashed_manifest_cannot_retarget_one_child_uid(self):
        request = self.request(); self.killed(request,'BeforeEntry'); before = self.snapshot()
        path = self.base.disk.target/ownership.NAMESPACE/(request['operationId']+'.json'); saved = json.loads(path.read_bytes())
        child = saved['entries'][0]['plan']; child['command']['uid'] = 1234; child['fingerprint'] = self.base.engine.digest(child)
        plan = request['plan']; plan['manifestDigest'] = self.engine.manifest_digest(saved['entries']); plan['fingerprint'] = self.engine.digest(plan)
        saved['plan'] = plan; saved['historyDigest'] = recursive.digest(self.history.DOMAIN,{k:v for k,v in saved.items() if k != 'historyDigest'}); path.write_text(json.dumps(saved))
        root = self.state/(request['operationId']+'.json'); record = json.loads(root.read_bytes()); record['plan'] = plan; root.write_text(json.dumps(record))
        with patch.object(os,'fchown',side_effect=AssertionError('retargeted manifest cannot execute')): result = self.engine.inspect(request,self.base.session)
        self.assertEqual(result['state'],'Unknown'); self.assertFalse(result['historyAvailable']); self.assertEqual(before,self.snapshot())

    def test_capacity_failure_is_aborted_and_unavailable_hdd_is_not_absence(self):
        request = self.request(); before = self.snapshot(); original = os.fstatvfs
        def no_capacity(fd):
            target = self.base.disk.target/ownership.NAMESPACE
            if target.exists() and os.fstat(fd).st_ino == target.stat().st_ino: return SimpleNamespace(f_bavail=0,f_frsize=4096,f_blocks=1000,f_flag=0)
            return original(fd)
        with patch.object(os,'fstatvfs',side_effect=no_capacity),self.assertRaisesRegex(self.base.f.Rejected,'HISTORY_CAPACITY'): self.engine.apply(request,self.base.session)
        self.assertEqual(before,self.snapshot()); self.assertEqual(self.engine.apply(request,self.base.session)['state'],'Aborted')
        fresh = self.request()
        with patch.object(self.history.storage,'configuration',side_effect=FileNotFoundError('synthetic dependency loss')):
            for action in ('apply','discard'):
                with self.subTest(action=action),self.assertRaises(FileNotFoundError): getattr(self.engine,action)(fresh,self.base.session)
        self.assertFalse((self.state/(fresh['operationId']+'.json')).exists()); self.assertEqual(before,self.snapshot())

    def test_completed_binding_drift_and_foreign_child_are_never_repaired(self):
        request = self.request(); self.engine.apply(request,self.base.session); os.chown(self.tree/'one.bin',1234,2345); before = self.snapshot()
        with patch.object(os,'fchown',side_effect=AssertionError('completed operation cannot repair drift')): result = self.engine.apply(request,self.base.session)
        self.assertEqual(result['state'],'Completed'); self.assertFalse(result['bindingCurrent']); self.assertEqual(before,self.snapshot())
        request = self.request(2300,3300); self.killed(request,'AfterEntry'); (self.tree/'foreign').write_bytes(b'not approved')
        with patch.object(os,'fchown',side_effect=AssertionError('foreign entry cannot be adopted')): result = self.engine.inspect(request,self.base.session)
        self.assertEqual(result['state'],'Unknown')
