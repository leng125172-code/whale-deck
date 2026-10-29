"""Container move plan/receipt checks on real isolated Linux filesystems.

Kernel Docker identity is modeled here. SIGKILL targets only the test's own
forked candidate process, never an installed service. No SQL or UI claim.
"""
import contextlib
import copy
import hashlib
import json
import os
import select
import signal
import stat
import unittest
from unittest.mock import patch
import uuid

import test_container_copy_intents as copied
import test_container_move_scope as scoped

module = scoped.copied.common.common.load('whaledeck-container-moves.py', 'container_move_intents')
move_type = module.container_moves_type(copied.intents.ContainerCopies)


@unittest.skipUnless(os.name == 'posix' and os.geteuid() == 0, 'isolated Linux root required')
class LinuxContainerMoveIntentsTests(unittest.TestCase):
    def setUp(self):
        self.base=copied.LinuxContainerCopyIntentsTests();self.base.setUp();self.addCleanup(self.base.doCleanups)
        self.fixture,self.root,self.session,self.f,self.r=self.base.fixture,self.base.root,self.base.session,self.base.f,self.base.r
        self.state=self.fixture.fixture.fixture.base/'move-intents';self.state.mkdir(mode=0o700)
        parent=self.fixture.fixture
        self.engine=move_type(parent.u,parent.publisher,scoped.scope_type,scoped.copied.operations.FileOperations,
            scoped.copied.copies.FileTreeCopies,scoped.copied.trees.FileTreeManifest,scoped.moves.FileTreeMoves,self.state)

    def request(self,command=None):
        return {'operationId':uuid.uuid4().hex,'owner':'B'*64,'plan':self.engine.plan(self.fixture.command() if command is None else command,self.session)}

    def record_path(self,request):return self.state/(request['operationId']+'.json')
    def record(self,request):return json.loads(self.record_path(request).read_bytes())
    def retained(self,request):return self.root/scoped.scope_type.PRIVATE/(request['operationId']+('.mutation' if request['plan']['kind']=='File' else '.tree-move'))

    def kill_at(self,request,point):
        ready_read,ready_write=os.pipe();continue_read,continue_write=os.pipe();pid=os.fork()
        if pid==0:
            os.close(ready_read);os.close(continue_write)
            def pause():os.write(ready_write,b'R');os.read(continue_read,1)
            self.engine.checkpoint=lambda phase:pause() if phase==point else None
            factory=self.engine.engine
            def instrument(scope,kind):
                engine=factory(scope,kind)
                if point.startswith('File:'):
                    save=engine.save
                    def saved(folder,record):
                        save(folder,record)
                        if record['state']==point.split(':',1)[1]:pause()
                    engine.save=saved
                elif point.startswith('Tree:'):
                    engine.checkpoint=lambda phase:pause() if phase==point.split(':',1)[1] else None
                return engine
            self.engine.engine=instrument
            try:self.engine.apply(request,self.session)
            except BaseException:os._exit(2)
            os._exit(3)
        os.close(ready_write);os.close(continue_read)
        try:
            self.assertTrue(select.select([ready_read],[],[],12)[0],'child missed exact fence')
            self.assertEqual(b'R',os.read(ready_read,1));os.kill(pid,signal.SIGKILL)
            _,status=os.waitpid(pid,0);pid=None;self.assertTrue(os.WIFSIGNALED(status));self.assertEqual(signal.SIGKILL,os.WTERMSIG(status))
        finally:
            os.close(ready_read);os.close(continue_write)
            if pid is not None:
                with contextlib.suppress(ProcessLookupError):os.kill(pid,signal.SIGKILL)
                os.waitpid(pid,0)

    def test_plan_is_read_only_and_has_distinct_move_domain_and_kind(self):
        names=set(self.root.iterdir());before=self.r.fingerprint(self.root.stat());request=self.request();plan=request['plan']
        self.assertEqual('Move',plan['operationKind']);self.assertEqual('move',plan['innerPlan']['command']['kind'])
        self.assertEqual([],list(self.state.iterdir()));self.assertEqual(names,set(self.root.iterdir()));self.assertEqual(before,self.r.fingerprint(self.root.stat()))
        copy_plan=self.base.engine.plan(self.fixture.command(),self.session)
        self.assertNotEqual(plan['fingerprint'],copy_plan['fingerprint'])
        with self.assertRaises(self.f.Rejected):self.engine.discard({**request,'plan':copy_plan})
        with self.assertRaises(self.f.Rejected):self.base.engine.discard(request)

    def test_file_completion_proves_original_retention_and_container_etag_without_repeating(self):
        request=self.request();before=self.fixture.source.stat();result=self.engine.apply(request,self.session);target=self.root/'复制.bin'
        self.assertEqual('Completed',result['state']);self.assertTrue(result['bindingCurrent'])
        self.assertTrue(all(result[k] is True for k in self.engine.FACTS));self.assertFalse(result['privateMayExist'])
        self.assertFalse(self.fixture.source.exists());self.assertEqual(self.fixture.content,target.read_bytes());self.assertEqual(before.st_ino,self.retained(request).stat().st_ino)
        self.assertEqual(self.r.version(request['plan']['command'],'复制.bin',target.stat()),result['version']);self.assertNotEqual(self.f.version(target.stat()),result['version'])
        raw=self.record_path(request).read_bytes()
        with patch.object(scoped.copied.operations.FileOperations,'apply',side_effect=AssertionError('no replay')):
            self.assertEqual('Completed',self.engine.apply(request,self.session)['state'])
        self.assertEqual(raw,self.record_path(request).read_bytes());self.assertEqual(0o600,stat.S_IMODE(self.record_path(request).stat().st_mode))

    def test_directory_completion_preserves_mixed_owners_and_detects_descendant_changes(self):
        source=self.fixture.tree();os.chown(source,2100,3100);os.chown(source/'模块/代码.txt',2101,3101)
        request=self.request(self.fixture.command('项目','项目副本'));result=self.engine.apply(request,self.session)
        self.assertEqual('Completed',result['state']);self.assertTrue(result['bindingCurrent'])
        self.assertEqual((2100,3100),((self.root/'项目副本').stat().st_uid,(self.root/'项目副本').stat().st_gid))
        file=self.root/'项目副本/模块/代码.txt';self.assertEqual((2101,3101),(file.stat().st_uid,file.stat().st_gid))
        raw=self.record_path(request).read_bytes();file.write_bytes(b'changed descendant')
        current=self.engine.inspect(request,self.session);self.assertEqual('Completed',current['state']);self.assertFalse(current['bindingCurrent'])
        self.assertEqual(raw,self.record_path(request).read_bytes())

    def test_changed_retained_original_does_not_rewrite_historical_completed_facts(self):
        request=self.request();result=self.engine.apply(request,self.session);raw=self.record_path(request).read_bytes()
        self.retained(request).write_bytes(b'changed retained original')
        current=self.engine.inspect(request,self.session)
        self.assertEqual('Completed',current['state']);self.assertFalse(current['bindingCurrent']);self.assertEqual(result['version'],current['version'])
        self.assertEqual(raw,self.record_path(request).read_bytes())

    def test_cancel_before_apply_is_permanent_even_after_container_and_source_loss(self):
        request=self.request();self.fixture.source.unlink();result=self.engine.discard(request,None)
        self.assertEqual('Aborted',result['state']);self.assertTrue(all(result[k] is False for k in self.engine.FACTS))
        raw=self.record_path(request).read_bytes()
        with patch.object(self.engine,'plan',side_effect=AssertionError('no replan')):
            self.assertEqual('Aborted',self.engine.apply(request,None)['state'])
        self.assertEqual(raw,self.record_path(request).read_bytes())

    def test_owner_and_changed_plan_cannot_alias_original_operation(self):
        request=self.request();self.engine.discard(request)
        with self.assertRaisesRegex(self.f.Rejected,'NOT_FOUND'):self.engine.inspect({**request,'owner':'C'*64})
        other=self.request(self.fixture.command(target='other'))
        with self.assertRaisesRegex(self.f.Rejected,'IDEMPOTENCY_CONFLICT'):self.engine.apply({**other,'operationId':request['operationId']},self.session)

    def test_copy_chmod_or_tampered_container_plan_cannot_become_a_move(self):
        request=self.request()
        for key,value in (('operationKind','Copy'),('uid',True),('sourceParent',[0,True]),('sourceIdentity',[0]*9),('extra',1)):
            plan={**request['plan'],key:value};plan['fingerprint']=self.engine.digest(plan)
            with self.subTest(key=key),self.assertRaises(self.f.Rejected):self.engine.discard({**request,'plan':plan})
        for kind in ('copy','chmod'):
            plan=copy.deepcopy(request['plan']);plan['innerPlan']['command']['kind']=kind
            inner=plan['innerPlan'];inner['fingerprint']=hashlib.sha256(b'whaledeck-file-operation-v1:'+json.dumps(
                {k:v for k,v in inner.items() if k!='fingerprint'},sort_keys=True,separators=(',',':')).encode()).hexdigest().upper()
            plan['fingerprint']=self.engine.digest(plan)
            with self.assertRaises(self.f.Rejected):self.engine.discard({**request,'plan':plan})
        self.assertEqual([],list(self.state.iterdir()))

    def test_tree_move_cannot_accept_copy_policy_or_changed_source_parent(self):
        self.fixture.tree();request=self.request(self.fixture.command('项目','项目副本'))
        plan=copy.deepcopy(request['plan']);plan['innerPlan']['sourceParent'][1]+=1;plan['fingerprint']=self.engine.digest(plan)
        with self.assertRaises(self.f.Rejected):self.engine.discard({**request,'plan':plan})

    def test_preparing_crash_aborts_without_a_physical_move(self):
        request=self.request();self.kill_at(request,'Preparing');result=self.engine.inspect(request,self.session)
        self.assertEqual('Aborted',result['state']);self.assertTrue(all(result[k] is False for k in self.engine.FACTS))
        self.assertEqual(self.fixture.content,self.fixture.source.read_bytes());self.assertFalse((self.root/'复制.bin').exists())

    def test_running_without_inner_receipt_is_unknown_and_never_reapplied(self):
        request=self.request();self.kill_at(request,'Running');result=self.engine.inspect(request,self.session)
        self.assertEqual('Unknown',result['state']);self.assertTrue(all(result[k] is None for k in self.engine.FACTS))
        with patch.object(scoped.copied.operations.FileOperations,'apply',side_effect=AssertionError('no repeat')):
            self.assertEqual('Unknown',self.engine.apply(request,self.session)['state'])
        self.assertEqual(self.fixture.content,self.fixture.source.read_bytes())

    def test_prepublication_file_crash_aborts_and_cleans_receipt_owned_preparation(self):
        request=self.request();self.kill_at(request,'File:DestinationApplying');result=self.engine.inspect(request,self.session)
        self.assertEqual('Aborted',result['state']);self.assertFalse(result['privateMayExist']);self.assertFalse(self.retained(request).exists())
        self.assertFalse((self.root/'复制.bin').exists());self.assertEqual(self.fixture.content,self.fixture.source.read_bytes())

    def test_published_file_before_source_move_is_unknown_with_honest_nullable_facts(self):
        request=self.request();self.kill_at(request,'File:SourceApplying');result=self.engine.inspect(request,self.session)
        self.assertEqual('Unknown',result['state']);self.assertIs(result['destinationPublished'],True)
        self.assertIsNone(result['sourceRemoved']);self.assertIsNone(result['sourceRetained'])
        self.assertEqual(self.fixture.content,(self.root/'复制.bin').read_bytes());self.assertEqual(self.fixture.content,self.fixture.source.read_bytes())

    def test_completed_inner_after_outer_crash_is_observed_without_a_second_move(self):
        request=self.request();self.kill_at(request,'InnerFinished')
        with patch.object(scoped.copied.operations.FileOperations,'apply',side_effect=AssertionError('no replay')):
            result=self.engine.inspect(request,self.session)
        self.assertEqual('Completed',result['state']);self.assertTrue(result['bindingCurrent']);self.assertFalse(self.fixture.source.exists())
        self.assertEqual(self.fixture.content,self.retained(request).read_bytes())

    def test_directory_published_before_source_retention_is_unknown_without_source_mutation(self):
        self.fixture.tree();request=self.request(self.fixture.command('项目','项目副本'));self.kill_at(request,'Tree:DestinationCompleted')
        with patch.object(scoped.moves.FileTreeMoves,'retain_source',side_effect=AssertionError('no resume')):
            result=self.engine.inspect(request,self.session);self.assertEqual('Unknown',self.engine.apply(request,self.session)['state'])
        self.assertIs(result['destinationPublished'],True);self.assertIsNone(result['sourceRemoved']);self.assertIsNone(result['sourceRetained'])
        self.assertEqual(self.fixture.content,(self.root/'项目/模块/代码.txt').read_bytes());self.assertEqual(self.fixture.content,(self.root/'项目副本/模块/代码.txt').read_bytes())

    def test_partial_directory_source_retention_crash_keeps_unknown_and_original_bytes(self):
        self.fixture.tree();second=self.root/'项目/模块/第二.bin';second.write_bytes(b'second original');second.chmod(0o640)
        request=self.request(self.fixture.command('项目','项目副本'));before=(self.root/'项目/模块/代码.txt').stat()
        self.kill_at(request,'Tree:SourceRetained');result=self.engine.inspect(request,self.session)
        self.assertEqual('Unknown',result['state']);self.assertIs(result['destinationPublished'],True)
        self.assertIsNone(result['sourceRetained']);self.assertEqual(b'second original',second.read_bytes())
        retained=self.retained(request)/'模块/代码.txt';self.assertEqual(before.st_ino,retained.stat().st_ino);self.assertEqual(self.fixture.content,retained.read_bytes())
        self.assertTrue((self.root/'项目').exists())

    def test_completed_inner_with_missing_target_keeps_source_facts_but_stays_unknown(self):
        request=self.request();self.kill_at(request,'InnerFinished');(self.root/'复制.bin').rename(self.root/'externally-relocated')
        result=self.engine.inspect(request,self.session);self.assertEqual('Unknown',result['state']);self.assertIsNone(result['destinationPublished'])
        self.assertIs(result['sourceRemoved'],True);self.assertIs(result['sourceRetained'],True)
        self.assertEqual(self.fixture.content,self.retained(request).read_bytes());self.assertEqual(self.fixture.content,(self.root/'externally-relocated').read_bytes())

    def test_container_loss_does_not_rebind_or_guess_terminal_source_facts(self):
        request=self.request();self.kill_at(request,'InnerFinished');result=self.engine.inspect(request,None)
        self.assertEqual('Unknown',result['state']);self.assertTrue(all(result[k] is None for k in self.engine.FACTS))
        self.assertEqual('Completed',self.record(request)['innerState']);self.assertEqual(self.fixture.content,self.retained(request).read_bytes())

    def test_tampered_inner_receipt_hash_cannot_keep_current_binding(self):
        request=self.request();self.engine.apply(request,self.session);raw=self.record_path(request).read_bytes()
        path=self.state/'engines/operations'/(request['operationId']+'.json');value=json.loads(path.read_bytes());value['unexpectedField']=True;path.write_text(json.dumps(value));path.chmod(0o600)
        result=self.engine.inspect(request,self.session);self.assertEqual('Completed',result['state']);self.assertFalse(result['bindingCurrent'])
        self.assertEqual(raw,self.record_path(request).read_bytes())

    def test_invalid_fact_type_and_lock_symlink_are_rejected(self):
        request=self.request();lock=self.state/(request['operationId']+'.lock');lock.symlink_to(self.fixture.source)
        with self.assertRaises(OSError):self.engine.discard(request)
        lock.unlink();self.engine.discard(request);value=self.record(request);value['sourceRemoved']=0;self.record_path(request).write_text(json.dumps(value));self.record_path(request).chmod(0o600)
        with self.assertRaisesRegex(self.f.Rejected,'RECEIPT_INVALID'):self.engine.inspect(request)


if __name__=='__main__':unittest.main()
