"""Real isolated Linux ACL syscalls; container/HDD attestation is a fixture.

No installed grant/RPC, real workstation HDD, business identity or UI proof.
"""
import copy
import errno
import hashlib
import json
import os
import signal
import stat
import unittest
import uuid
from unittest.mock import patch
import test_container_file_acl as single
from test_container_file_staging import load

recursive=load('whaledeck-container-recursive-permissions.py','recursive_acl_shared_candidate')
module=load('whaledeck-container-recursive-acl.py','recursive_acl_candidate')


def directories_acl():
    value=single.named_acl(mask=7);value[0]['permissions']=7;value[1]['permissions']=7
    return value


@unittest.skipUnless(os.name=='posix' and os.geteuid()==0,'isolated Linux root required')
class RecursiveAclTests(unittest.TestCase):
    def setUp(self):
        self.base=single.LinuxContainerAclTests();self.base.setUp();self.addCleanup(self.base.doCleanups)
        self.tree=self.base.root/'tree';self.tree.mkdir(mode=0o750);(self.tree/'sub').mkdir(mode=0o700)
        self.paths=('','one.bin','sub','sub/中文.bin')
        self.content=hashlib.shake_256(b'recursive-acl-content').digest(2*1024**2+7)
        for i,rel in enumerate(self.paths):
            path=self.tree/rel
            if rel.endswith('.bin'):path.write_bytes(self.content);path.chmod(0o640)
            os.chown(path,2100+i,3100+i);os.setxattr(path,'user.preserve',b'ordinary attribute')
            os.utime(path,ns=(1700000000123456789,1700000000123456789))
        for rel in ('','sub'):
            fd=os.open(self.tree/rel,os.O_RDONLY|os.O_DIRECTORY)
            try:single.acl.write_acl(fd,'default',directories_acl())
            finally:os.close(fd)
        self.state=self.base.disk.disk/'recursive-acl-state';self.state.mkdir(mode=0o700)
        self.history=module.recursive_acl_history_type(recursive)(self.base.disk.engine)
        self.type=module.recursive_acl_type(single.acl,single.fixture.candidate,single.fixture.directories.ContainerDirectories,recursive)
        self.engine=self.type(self.base.uploads,self.base.publisher,self.state,self.history)

    def command(self,kind='access',directory=None):
        return {**self.base.session.request,'path':'tree','aclType':kind,'fileEntries':single.named_acl() if kind=='access' else None,
            'directoryEntries':directories_acl() if directory is None else directory,
            'expectedVersion':self.base.r.version(self.base.session.request,'tree',self.tree.stat())}

    def request(self,kind='access',directory=None):return dict(operationId=uuid.uuid4().hex,owner='A'*64,plan=self.engine.plan(self.command(kind,directory),self.base.session))

    def snapshot(self):
        result={}
        for rel in self.paths:
            path=self.tree/rel;info=path.lstat();fd=os.open(path,os.O_RDONLY|os.O_NOFOLLOW)
            try:result[rel]=dict(identity=list(self.base.r.fingerprint(info)),access=single.acl.read_acl(fd,'access',info.st_mode),default=single.acl.read_acl(fd,'default',info.st_mode))
            finally:os.close(fd)
            self.assertEqual(os.getxattr(path,'user.preserve'),b'ordinary attribute')
            if rel.endswith('.bin'):self.assertEqual(path.read_bytes(),self.content)
        return result

    def preserved(self,before,after,kind):
        self.assertEqual(before.keys(),after.keys())
        for rel,old in before.items():
            new=after[rel];a,b=old['identity'],new['identity']
            self.assertEqual(a[:2],b[:2]);self.assertEqual(a[3:8],b[3:8]);self.assertEqual(a[2]&~0o777,b[2]&~0o777)
            if kind=='access':self.assertEqual(old['default'],new['default'])
            else:
                self.assertEqual(old['access'],new['access']);self.assertEqual(a[2],b[2])
                if rel.endswith('.bin'):self.assertEqual(old,new)

    def no_write(self):
        return patch.object(single.acl,'write_acl',side_effect=AssertionError('must never write/replay ACL'))

    def test_plan_is_read_only_seals_originals_and_separates_file_directory_targets(self):
        before=self.snapshot();command=self.command();plan=self.engine.plan(command,self.base.session)
        self.assertEqual((plan['entryCount'],plan['fileCount'],plan['directoryCount'],plan['changeCount']),(4,2,2,4))
        self.assertEqual(before,self.snapshot());self.assertFalse(list(self.state.iterdir()));self.assertFalse((self.base.disk.target/module.NAMESPACE).exists())
        command['fileEntries'][0]['permissions']=0
        self.assertEqual(plan['command']['fileEntries'][0]['permissions'],6)
        self.assertEqual(plan['command']['directoryEntries'][0]['permissions'],7)

    def test_access_completes_real_acls_preserves_original_default_owner_bytes_and_never_replays(self):
        before=self.snapshot();request=self.request();result=self.engine.apply(request,self.base.session);after=self.snapshot()
        self.assertEqual((result['state'],result['completedCount'],result['changedCount']),('Completed',4,4));self.assertEqual(result['aclType'],'access');self.assertTrue(result['bindingCurrent'])
        self.preserved(before,after,'access')
        for rel,value in after.items():self.assertEqual(value['access'],single.named_acl() if rel.endswith('.bin') else directories_acl())
        raw=(self.state/(request['operationId']+'.json')).read_bytes()
        with self.no_write():replay=self.engine.apply(request,self.base.session)
        self.assertTrue(replay['replayed']);self.assertEqual(replay['state'],'Completed');self.assertEqual(self.snapshot(),after);self.assertEqual((self.state/(request['operationId']+'.json')).read_bytes(),raw)
        saved=self.history.load(request);self.assertEqual(saved['resourceType'],'ContainerRecursiveAclMetadata');self.assertEqual(saved['historyDigest'],result['historyDigest'])
        children=list((self.base.disk.target/module.NAMESPACE/request['operationId']/'entries').glob('*.json'));self.assertEqual(len(children),4)
        for p in children:
            value=json.loads(p.read_bytes());self.assertEqual(value['resourceType'],'ContainerRecursiveAclEntryMetadata');self.assertEqual(value['parentOperationId'],request['operationId']);self.assertEqual(value['manifestDigest'],request['plan']['manifestDigest'])

    def test_default_changes_only_directories_files_are_verified_unchanged_then_clear(self):
        for target in (single.named_acl(mask=6),[]):
            before=self.snapshot();request=self.request('default',target);self.assertEqual(request['plan']['changeCount'],2)
            result=self.engine.apply(request,self.base.session);after=self.snapshot();self.preserved(before,after,'default')
            self.assertEqual((result['state'],result['changedCount'],result['completedCount']),('Completed',2,4));self.assertTrue(result['bindingCurrent'])
            for rel in ('','sub'):self.assertEqual(after[rel]['default'],target)
            with self.no_write():self.assertEqual(self.engine.apply(request,self.base.session)['state'],'Completed')

    def test_noop_access_and_default_never_call_acl_write(self):
        self.engine.apply(self.request(),self.base.session)
        for kind in ('access','default'):
            before=self.snapshot();request=self.request(kind);self.assertEqual(request['plan']['changeCount'],0)
            with self.no_write():result=self.engine.apply(request,self.base.session)
            self.assertEqual((result['state'],result['changedCount']),('Completed',0));self.assertEqual(before,self.snapshot())

    def test_default_file_entries_and_extra_permissions_fields_are_refused(self):
        before=self.snapshot()
        for bad in ({**self.command('default'),'fileEntries':[]},{**self.command('default'),'fileEntries':single.named_acl()},
                    {**self.command(),'fileMode':'0777'},{**self.command(),'uid':0},{**self.command(),'aclType':'both'}):
            with self.subTest(bad=bad),self.assertRaises(self.base.r.Rejected):self.engine.plan(bad,self.base.session)
        self.assertEqual(before,self.snapshot());self.assertFalse(list(self.state.iterdir()))

    def test_invalid_mask_explicit_id_permission_zero_and_boolean_values(self):
        for entries in ([],[v for v in single.named_acl() if v['tag']!='mask'],single.named_acl()*13):
            with self.assertRaises(self.base.r.Rejected):self.engine.plan({**self.command(),'fileEntries':entries},self.base.session)
        for key,value in [('id',True),('id',4294967295),('permissions',True),('permissions',8)]:
            entries=single.named_acl();entries[1][key]=value
            with self.subTest(key=key,value=value),self.assertRaises(self.base.r.Rejected):self.engine.plan({**self.command(),'directoryEntries':entries},self.base.session)
        command=self.command();command['fileEntries']=single.named_acl(uid=0);command['fileEntries'][1]['permissions']=0
        result=self.engine.apply(dict(operationId=uuid.uuid4().hex,owner='A'*64,plan=self.engine.plan(command,self.base.session)),self.base.session)
        self.assertEqual(result['state'],'Completed');self.assertEqual(self.snapshot()['one.bin']['access'][1],dict(tag='user',id=0,permissions=0))

    def test_symlink_hardlink_fifo_and_special_modes_are_refused_without_writes(self):
        bad=self.tree/'bad'
        for kind in ('symlink','hardlink','fifo','special'):
            if kind=='symlink':bad.symlink_to(self.tree/'one.bin')
            elif kind=='hardlink':os.link(self.tree/'one.bin',bad)
            elif kind=='fifo':os.mkfifo(bad)
            else:bad.write_bytes(b'special');bad.chmod(0o4640)
            with self.subTest(kind=kind),self.assertRaises((self.base.r.Rejected,OSError)):self.engine.plan(self.command(),self.base.session)
            bad.unlink()
        self.assertFalse(list(self.state.iterdir()))

    def test_tree_entry_depth_and_time_budgets_fail_before_writing(self):
        before=self.snapshot()
        for limits in ({'max_entries':2},{'max_depth':0}):
            engine=self.type(self.base.uploads,self.base.publisher,self.state,self.history,**limits)
            with self.subTest(limits=limits),self.assertRaises(self.base.r.Rejected):engine.plan(self.command(),self.base.session)
        engine=self.type(self.base.uploads,self.base.publisher,self.state,self.history,timeout_seconds=.001)
        with patch.object(recursive.time,'monotonic',side_effect=[1,2]),self.assertRaisesRegex(self.base.r.Rejected,'TIMEOUT'):engine.plan(self.command(),self.base.session)
        self.assertEqual(before,self.snapshot())

    def test_changed_acl_after_plan_and_mutated_target_arrays_fail_before_mutation(self):
        request=self.request();fd=os.open(self.tree/'one.bin',os.O_RDONLY)
        try:single.acl.write_acl(fd,'access',single.named_acl(mask=6))
        finally:os.close(fd)
        before=self.snapshot()
        with self.no_write(),self.assertRaises(self.base.r.Rejected):self.engine.apply(request,self.base.session)
        self.assertEqual(before,self.snapshot())
        changed=self.request();changed['plan']['command']['fileEntries'][1]['permissions']=0
        with self.no_write(),self.assertRaises(self.base.r.Rejected):self.engine.apply(changed,self.base.session)

    def test_discard_is_a_permanent_fence_even_when_hdd_has_no_history(self):
        request=self.request();before=self.snapshot();result=self.engine.discard(request,self.base.session)
        self.assertEqual(result['state'],'Aborted')
        with self.no_write():self.assertEqual(self.engine.apply(request,self.base.session)['state'],'Aborted')
        self.assertEqual(before,self.snapshot());self.assertFalse((self.base.disk.target/module.NAMESPACE).exists())

    def test_partial_after_changed_file_and_default_directory_never_resumes(self):
        for kind,index in [('access',0),('default',2)]:
            # A distinct original target ensures each case actually changes an inode.
            target=single.named_acl(mask=6) if kind=='default' else None
            request=self.request(kind,target);flag=[False];self.engine.cancelled=lambda:flag[0]
            self.engine.checkpoint=lambda phase,i:flag.__setitem__(0,True) if phase=='AfterEntry' and i==index else None
            with self.assertRaises(self.base.r.Rejected):self.engine.apply(request,self.base.session)
            after=self.snapshot()
            with self.no_write():result=self.engine.inspect(request,self.base.session);replay=self.engine.apply(request,self.base.session)
            self.assertEqual((result['state'],result['changedCount']),('Partial',1));self.assertEqual(replay['state'],'Partial');self.assertEqual(after,self.snapshot())
            self.engine.cancelled=None;self.engine.checkpoint=lambda phase,i:None

    def killed(self,request,phase):
        pid=os.fork()
        if pid==0:
            try:
                self.engine.checkpoint=lambda point,index:os.kill(os.getpid(),signal.SIGKILL) if point==phase and index==0 else None
                self.engine.apply(request,self.base.session)
            except BaseException:os._exit(2)
            os._exit(3)
        _,status=os.waitpid(pid,0);self.assertTrue(os.WIFSIGNALED(status));self.assertEqual(os.WTERMSIG(status),signal.SIGKILL)

    def test_real_sigkill_after_first_entry_recovers_partial_without_writing(self):
        request=self.request();self.killed(request,'AfterEntry');after=self.snapshot()
        with self.no_write():result=self.engine.inspect(request,self.base.session);again=self.engine.apply(request,self.base.session)
        self.assertEqual((result['state'],result['changedCount']),('Partial',1));self.assertEqual(again['state'],'Partial');self.assertEqual(after,self.snapshot());self.assertTrue(result['historyAvailable'])

    def test_real_sigkill_at_marker_aborts_without_starting_acl_mutation(self):
        request=self.request();before=self.snapshot();self.killed(request,'BeforeEntry')
        with self.no_write():result=self.engine.inspect(request,self.base.session);again=self.engine.apply(request,self.base.session)
        self.assertEqual(result['state'],'Aborted');self.assertEqual(again['state'],'Aborted');self.assertEqual(before,self.snapshot())

    def test_real_sigkill_after_acl_syscall_recovers_actual_changed_entry_without_rewrite(self):
        request=self.request();real=single.acl.write_acl;pid=os.fork()
        if pid==0:
            def cut(fd,kind,entries):real(fd,kind,entries);os.kill(os.getpid(),signal.SIGKILL)
            try:
                with patch.object(single.acl,'write_acl',side_effect=cut):self.engine.apply(request,self.base.session)
            except BaseException:os._exit(2)
            os._exit(3)
        _,status=os.waitpid(pid,0);self.assertTrue(os.WIFSIGNALED(status));self.assertEqual(os.WTERMSIG(status),signal.SIGKILL)
        after=self.snapshot()
        with self.no_write():result=self.engine.inspect(request,self.base.session)
        self.assertEqual((result['state'],result['changedCount']),('Partial',1));self.assertEqual(after,self.snapshot())

    def test_replaced_inode_after_partial_loss_is_unknown_and_never_resumes(self):
        request=self.request();self.killed(request,'AfterEntry');p=self.tree/'one.bin';original=self.snapshot()['one.bin']
        pinned=os.open(p,os.O_RDONLY|os.O_NOFOLLOW)
        try:
            p.unlink();p.write_bytes(self.content);os.chown(p,*original['identity'][3:5]);p.chmod(original['identity'][2]&0o777)
            os.setxattr(p,'user.preserve',b'ordinary attribute');fd=os.open(p,os.O_RDONLY|os.O_NOFOLLOW)
            try:single.acl.write_acl(fd,'access',original['access'])
            finally:os.close(fd)
            os.utime(p,ns=(1700000000123456789,original['identity'][7]));self.assertNotEqual(list(self.base.r.fingerprint(p.stat()))[:2],original['identity'][:2])
        finally:os.close(pinned)
        after=self.snapshot()
        with self.no_write():result=self.engine.inspect(request,self.base.session);again=self.engine.apply(request,self.base.session)
        self.assertEqual(result['state'],'Unknown');self.assertEqual(result['unknownCount'],4);self.assertEqual(again['state'],'Unknown');self.assertEqual(after,self.snapshot())

    def test_acl_hdd_manifest_is_separate_and_tampering_prevents_writes(self):
        request=self.request();plan,entries=self.engine.inventory(request['plan']['command'],self.base.session)
        saved=self.history.capture(request,entries);self.assertEqual(saved['resourceType'],'ContainerRecursiveAclMetadata')
        changed=copy.deepcopy(saved);changed['entries'][0]['plan']['command']['entries'][1]['permissions']=0
        # A self-consistent storage digest still cannot replace the frozen manifest.
        changed['historyDigest']=recursive.digest(self.history.DOMAIN,{k:v for k,v in changed.items() if k!='historyDigest'})
        with patch.object(self.history,'load',return_value=changed),self.assertRaises(self.base.r.Rejected):self.engine.validate_manifest(request,changed)
        with self.no_write():self.assertEqual(self.engine.apply(request,self.base.session)['state'],'Unknown')

    def test_missing_aggregate_root_record_with_hdd_artifacts_permanently_fences_reapply(self):
        request=self.request();self.engine.apply(request,self.base.session);before=self.snapshot()
        (self.state/(request['operationId']+'.json')).unlink()
        with self.no_write():result=self.engine.apply(request,self.base.session)
        self.assertEqual(result['state'],'Unknown');self.assertEqual(result['unknownCount'],4);self.assertEqual(before,self.snapshot())

    def test_lost_root_and_aggregate_hdd_cannot_ignore_surviving_entry_histories(self):
        request=self.request();self.engine.apply(request,self.base.session);before=self.snapshot()
        (self.state/(request['operationId']+'.json')).unlink()
        (self.base.disk.target/module.NAMESPACE/(request['operationId']+'.json')).unlink()
        with self.no_write():result=self.engine.apply(request,self.base.session)
        self.assertEqual(result['state'],'Unknown');self.assertTrue(result['historyMayExist']);self.assertEqual(before,self.snapshot())

    def test_shared_chmod_chown_profiles_and_history_cannot_be_selected_via_request(self):
        with self.assertRaises(ValueError):recursive.recursive_permissions_type(type(self.base.engine),single.fixture.directories.ContainerDirectories,type(self.base.history),ownership=True,acl=True)
        with self.assertRaises(ValueError):recursive.recursive_permissions_type(single.fixture.candidate.container_permissions_type(single.fixture.directories.ContainerDirectories),single.fixture.directories.ContainerDirectories,type(self.base.history),acl=True)
        wrong=recursive.RecursivePermissionHistory(self.base.disk.engine)
        with self.assertRaises(self.base.r.Rejected):self.type(self.base.uploads,self.base.publisher,self.state,wrong)
        for change in ({'ownership':True},{'acl':True},{'mode':'0777'}):
            with self.assertRaises(self.base.r.Rejected):self.engine.plan({**self.command(),**change},self.base.session)


class RecursiveAclFixtureRecipeTests(unittest.TestCase):
    def test_parent_and_independent_content_recipe_match_and_reject_old_mutation_scope(self):
        from validate_container_recursive_acl import fixture_bytes
        from verify_container_recursive_acl import verify_fixture_recipe
        identifier='a'*64;content=fixture_bytes(identifier)
        b=dict(containerId=identifier,bytesPerFile=len(content),sha256=hashlib.sha256(content).hexdigest().upper())
        self.assertEqual(verify_fixture_recipe(b),content)
        old=hashlib.shake_256(('recursive-owner-candidate:'+identifier).encode()).digest(len(content))
        with self.assertRaises(AssertionError):verify_fixture_recipe({**b,'sha256':hashlib.sha256(old).hexdigest().upper()})
        with self.assertRaises(AssertionError):verify_fixture_recipe({**b,'bytesPerFile':True})


if __name__=='__main__':unittest.main()
