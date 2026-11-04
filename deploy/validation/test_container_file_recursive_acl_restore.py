"""Real temporary Linux ACL restore; HDD/config/container liveness are fixtures."""
import copy,json,os,unittest,uuid
from unittest.mock import patch
import test_container_file_recursive_acl as native
from test_container_file_staging import load

module=load('whaledeck-container-recursive-acl-restore.py','recursive_restore_candidate')
common=load('whaledeck-container-acl-history.py','recursive_restore_common')
catalog=load('whaledeck-container-recursive-acl-history.py','recursive_restore_catalog')

class RestoreKernelScopeTests(unittest.TestCase):
    def test_restore_type_is_trusted_only_and_cannot_mix_chown_or_disable_acl(self):
        for flags in ({'acl_restore':True},{'acl':True,'acl_restore':'yes'},{'acl':True,'ownership':True,'acl_restore':True}):
            with self.assertRaises(ValueError):native.recursive.recursive_permissions_type(None,None,None,**flags)

    def test_independent_artifact_selection_rejects_before_dependency_access(self):
        from verify_recursive_acl_restore_kernel import verify
        from recover_recursive_acl_restore_kernel import recover
        for name in ('../escape','recursive-acl-restore-kernel-'+'A'*32,'container-recursive-acl-'+'a'*32):
            with self.assertRaises(AssertionError):verify(name)
            with self.assertRaises(AssertionError):recover(name)

@unittest.skipUnless(os.name=='posix' and os.geteuid()==0,'isolated Linux root required')
class RecursiveAclRestoreTests(unittest.TestCase):
    def setUp(self):
        self.b=native.RecursiveAclTests();self.b.setUp();self.addCleanup(self.b.doCleanups)
        self.state=self.b.base.disk.disk/'restore-state';self.state.mkdir(mode=0o700)
        self.history=module.recursive_acl_restore_history_type(native.recursive)(self.b.base.disk.engine)
        self.type=module.recursive_acl_restore_type(native.single.acl,native.single.fixture.candidate,native.single.fixture.directories.ContainerDirectories,native.recursive,native.module,catalog.recursive_acl_history_reader_type(common))
        self.engine=self.type(self.b.base.uploads,self.b.base.publisher,self.state,self.history)

    def seed(self,kind='access'):
        if kind=='access':
            self.write('one.bin','access',native.single.named_acl(mask=2,uid=0));self.write('sub/中文.bin','access',native.single.acl.base_acl(0o600))
        else:
            self.write('','default',[]);self.write('sub','default',native.single.named_acl(mask=4,uid=77))
        old=self.b.snapshot();source=self.b.request(kind);result=self.b.engine.apply(source,self.b.base.session);self.assertEqual('Completed',result['state'])
        return source,result,old

    def write(self,relative,kind,entries):
        fd=os.open(self.b.tree/relative,os.O_RDONLY|os.O_NOFOLLOW)
        try:native.single.acl.write_acl(fd,kind,entries)
        finally:os.close(fd)

    def command(self,source,result):
        return {**self.b.base.session.request,'path':'tree','expectedVersion':self.b.base.r.version(self.b.base.session.request,'tree',self.b.tree.stat()),'captureId':source['operationId'],'expectedHistoryDigest':result['historyDigest']}

    def request(self,source,result):return dict(operationId=uuid.uuid4().hex,owner='A'*64,plan=self.engine.plan(self.command(source,result),self.b.base.session))
    def source_file(self,source):return self.b.base.disk.target/native.module.NAMESPACE/(source['operationId']+'.json')
    def record(self,request):return json.loads((self.state/(request['operationId']+'.json')).read_bytes())
    def no_write(self):return patch.object(native.single.acl,'write_acl',side_effect=AssertionError('restore must not write or resume'))

    def test_plan_seals_heterogeneous_targets_source_frame_and_current_tree_without_writing(self):
        source,result,old=self.seed();before=self.b.snapshot();request=self.request(source,result);plan=request['plan']
        self.assertEqual(before,self.b.snapshot());self.assertFalse(list(self.state.iterdir()));self.assertFalse((self.b.base.disk.target/module.NAMESPACE).exists())
        self.assertEqual([plan[k] for k in ('entryCount','fileCount','directoryCount','changeCount')],[4,2,2,4]);self.assertEqual(source['plan']['manifestDigest'],plan['restoreSource']['manifestDigest']);self.assertEqual(result['historyDigest'],plan['restoreSource']['historyDigest']);self.assertEqual(9,len(plan['restoreSourceFrame']))
        metadata=self.engine.restore_source(plan['command'],self.b.base.session)
        for relative in self.b.paths:
            values=self.engine.restore_targets(plan['command'],'File' if relative.endswith('.bin') else 'Directory',before[relative]['access'],relative,metadata)
            self.assertEqual(old[relative]['access'],values['entries'])

    def test_access_restores_original_per_inode_keeps_current_default_and_captures_undo(self):
        source,result,old=self.seed();self.write('','default',[]);before=self.b.snapshot();request=self.request(source,result);reply=self.engine.apply(request,self.b.base.session);after=self.b.snapshot()
        self.assertEqual(('Completed',4,4),(reply['state'],reply['completedCount'],reply['changedCount']));self.assertTrue(reply['bindingCurrent']);self.b.preserved(before,after,'access')
        for relative in self.b.paths:self.assertEqual(old[relative]['access'],after[relative]['access'])
        saved=self.history.load(request);self.assertEqual('ContainerRecursiveAclRestoreMetadata',saved['resourceType'])
        for e in saved['entries']:self.assertEqual(before[e['relativePath']]['access'],e['plan']['accessAcl'])
        self.assertNotIn('system.posix_acl_access',os.listxattr(self.b.tree/'sub/中文.bin'))

    def test_default_restores_different_directory_defaults_and_files_current_access(self):
        source,result,old=self.seed('default');self.write('one.bin','access',native.single.named_acl(mask=6));before=self.b.snapshot();request=self.request(source,result);reply=self.engine.apply(request,self.b.base.session);after=self.b.snapshot()
        self.assertEqual(('Completed',2),(reply['state'],reply['changedCount']));self.b.preserved(before,after,'default')
        for relative in ('','sub'):self.assertEqual(old[relative]['default'],after[relative]['default'])
        self.assertNotIn('system.posix_acl_default',os.listxattr(self.b.tree))

    def test_completed_replay_and_inspect_do_not_require_source_or_rewrite(self):
        source,result,_=self.seed();request=self.request(source,result);self.engine.apply(request,self.b.base.session);after=self.b.snapshot();self.source_file(source).unlink()
        with self.no_write():
            for response in (self.engine.apply(request,self.b.base.session),self.engine.inspect(request,self.b.base.session)):
                self.assertEqual('Completed',response['state']);self.assertTrue(response['bindingCurrent']);self.assertTrue(response['replayed'])
        self.assertEqual(after,self.b.snapshot())

    def test_fresh_restore_after_first_is_noop_and_never_calls_writer(self):
        source,result,_=self.seed();self.engine.apply(self.request(source,result),self.b.base.session);request=self.request(source,result);self.assertEqual(0,request['plan']['changeCount']);before=self.b.snapshot()
        with self.no_write():reply=self.engine.apply(request,self.b.base.session)
        self.assertEqual(('Completed',0),(reply['state'],reply['changedCount']));self.assertEqual(before,self.b.snapshot())

    def test_source_missing_wrong_digest_or_uniform_entries_cannot_plan_write(self):
        source,result,_=self.seed();before=self.b.snapshot();command=self.command(source,result)
        invalid=[{**command,'captureId':'0'*32},{**command,'expectedHistoryDigest':'F'*64},{**command,'captureId':True},{**command,'expectedHistoryDigest':False},{**command,'fileEntries':native.single.named_acl()},{**command,'aclType':'access'},{**command,'uid':0}]
        for bad in invalid:
            with self.subTest(bad=bad),self.assertRaises((self.b.base.r.Rejected,self.engine.f.Rejected,ValueError)):self.engine.plan(bad,self.b.base.session)
        self.source_file(source).unlink()
        with self.assertRaises(self.b.base.r.Rejected):self.engine.plan(command,self.b.base.session)
        self.assertEqual(before,self.b.snapshot())

    def test_changed_or_same_byte_replaced_source_after_plan_is_zero_write(self):
        for replacement in (False,True):
            source,result,_=self.seed();request=self.request(source,result);path=self.source_file(source);raw=path.read_bytes();before=self.b.snapshot()
            if replacement:
                alternate=path.with_suffix('.new');alternate.write_bytes(raw);alternate.chmod(0o600);alternate.replace(path)
            else:
                value=json.loads(raw);value['createdAtUnixMs']+=1;value['historyDigest']=native.recursive.digest(self.b.history.DOMAIN,{k:v for k,v in value.items() if k!='historyDigest'});path.write_text(json.dumps(value))
            with self.no_write(),self.assertRaises(self.b.base.r.Rejected):self.engine.apply(request,self.b.base.session)
            self.assertEqual(before,self.b.snapshot());self.assertEqual('Aborted',self.record(request)['state'])

    def test_source_loss_after_first_write_is_partial_and_never_resumes(self):
        source,result,_=self.seed();request=self.request(source,result);path=self.source_file(source)
        self.engine.checkpoint=lambda phase,index:path.unlink() if phase=='AfterEntry' and index==0 else None
        with self.assertRaises(self.b.base.r.Rejected):self.engine.apply(request,self.b.base.session)
        after=self.b.snapshot()
        with self.no_write():reply=self.engine.inspect(request,self.b.base.session);replay=self.engine.apply(request,self.b.base.session)
        self.assertEqual(('Partial',1,3),(reply['state'],reply['changedCount'],reply['notStartedCount']));self.assertEqual('Partial',replay['state']);self.assertEqual(after,self.b.snapshot())

    def test_cancel_after_write_uses_original_restore_receipts_without_source(self):
        source,result,_=self.seed();request=self.request(source,result);flag=[False];self.engine.cancelled=lambda:flag[0];self.engine.checkpoint=lambda phase,index:flag.__setitem__(0,True) if phase=='AfterEntry' and index==0 else None
        with self.assertRaises(self.b.base.r.Rejected):self.engine.apply(request,self.b.base.session)
        self.source_file(source).unlink()
        with self.no_write():reply=self.engine.inspect(request,self.b.base.session)
        self.assertEqual('Partial',reply['state']);self.assertTrue(reply['historyAvailable'])

    def test_target_disappearing_after_write_is_unknown_and_late_apply_does_not_resume(self):
        source,result,_=self.seed();request=self.request(source,result)
        self.engine.checkpoint=lambda phase,index:(self.b.tree/'one.bin').rename(self.b.tree/'renamed.bin') if phase=='AfterEntry' and index==0 else None
        with self.assertRaises((OSError,self.b.base.r.Rejected)):self.engine.apply(request,self.b.base.session)
        with self.no_write():reply=self.engine.inspect(request,self.b.base.session);late=self.engine.apply(request,self.b.base.session)
        self.assertEqual(('Unknown',4),(reply['state'],reply['unknownCount']));self.assertEqual('Unknown',late['state']);self.assertTrue(reply['historyAvailable'])

    def test_extra_or_wrong_kind_target_tree_fails_before_restore_capture(self):
        source,result,_=self.seed();extra=self.b.tree/'extra';extra.write_bytes(b'extra')
        with self.assertRaises(self.b.base.r.Rejected):self.engine.plan(self.command(source,result),self.b.base.session)
        self.assertFalse((self.b.base.disk.target/module.NAMESPACE).exists());extra.unlink()
        leaf=self.b.tree/'one.bin';leaf.unlink();leaf.mkdir()
        with self.assertRaises(self.b.base.r.Rejected):self.engine.plan(self.command(source,result),self.b.base.session)

    def test_signed_plan_source_summary_frame_and_target_digest_tampering_rejects(self):
        source,result,_=self.seed();request=self.request(source,result);before=self.b.snapshot()
        for mutate in (lambda p:p['restoreSource'].update(historyDigest='F'*64),lambda p:p['restoreSource'].update(entryCount=True),lambda p:p['restoreSourceFrame'].__setitem__(3,1000),lambda p:p.update(restoreTargetsDigest='wrong')):
            changed=copy.deepcopy(request);mutate(changed['plan']);changed['plan']['fingerprint']=self.engine.digest(changed['plan'])
            with self.no_write(),self.assertRaises((self.b.base.r.Rejected,self.engine.f.Rejected)):self.engine.apply(changed,self.b.base.session)
        self.assertEqual(before,self.b.snapshot())

    def test_discard_fences_new_operation_even_when_source_disappears(self):
        source,result,_=self.seed();request=self.request(source,result);self.engine.discard(request,self.b.base.session);self.source_file(source).unlink();before=self.b.snapshot()
        with self.no_write():self.assertEqual('Aborted',self.engine.apply(request,self.b.base.session)['state'])
        self.assertEqual(before,self.b.snapshot())

if __name__=='__main__':unittest.main()
