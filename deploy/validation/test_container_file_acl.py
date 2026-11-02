"""Linux ACL syscalls and journal recovery in isolated temporary directories.

Mount/udev attestation and ContainerRoot are explicit fixtures, not installed
RPC, physical HDD, Authentik or UI evidence. Kernel ACL checks are real.
"""
import copy
import errno
import json
import os
import select
import signal
import stat
import struct
import subprocess
import unittest
import uuid
from unittest.mock import patch

import test_container_file_permissions as fixture
import test_container_file_staging as staging

acl = staging.load('whaledeck-container-acl.py', 'container_acl_candidate')


def named_acl(mask=4, uid=2201):
    return [{'tag':'user_obj','id':None,'permissions':6}, {'tag':'user','id':uid,'permissions':6},
            {'tag':'group_obj','id':None,'permissions':0}, {'tag':'mask','id':None,'permissions':mask},
            {'tag':'other','id':None,'permissions':0}]


class AclCodecTests(unittest.TestCase):
    def test_numeric_root_is_explicit_and_kernel_wire_is_little_endian(self):
        entries = named_acl(uid=0)
        raw = acl.encode_acl(entries)
        self.assertEqual(raw[:4], b'\x02\0\0\0'); self.assertEqual(acl.decode_acl(raw), entries)
        self.assertEqual(struct.unpack_from('<HHI',raw,12),(2,6,0)); self.assertEqual(acl.acl_mode(entries),0o640)

    def test_duplicate_unsorted_missing_base_or_mask_boolean_and_unbounded_ids_are_rejected(self):
        valid=named_acl()
        invalid=[[], valid+valid[-1:], list(reversed(valid)), valid[1:], [e for e in valid if e['tag']!='mask'],
                 valid+[{'tag':'other','id':None,'permissions':0}], valid*13]
        for tag,field,value in [('user','id',True),('user','id',-1),('user','id',4294967295),('user','id','2201'),
                                ('user_obj','id',0),('other','permissions',True),('mask','permissions',8),('mask','tag','url')]:
            bad=copy.deepcopy(valid);next(e for e in bad if e['tag']==tag)[field]=value;invalid.append(bad)
        for entries in invalid:
            with self.subTest(entries=entries),self.assertRaises(ValueError):acl.validate_entries(entries)
        acl.validate_entries([],empty=True)

    def test_binary_version_tags_ids_lengths_and_permissions_cannot_bypass_json_validation(self):
        raw=acl.encode_acl(named_acl())
        invalid=[b'',raw[:20],raw+b'\0',b'\x03'+raw[1:],raw[:4]+struct.pack('<HHI',3,6,0xffffffff)+raw[12:],
                 raw[:4]+struct.pack('<HHI',1,8,0xffffffff)+raw[12:],raw[:4]+struct.pack('<HHI',1,6,0)+raw[12:]]
        for value in invalid:
            with self.subTest(value=value),self.assertRaises(ValueError):acl.decode_acl(value)

    def test_only_enodata_means_absent_not_unsupported_denied_or_io_failure(self):
        for code in (errno.ENOTSUP,errno.EACCES,errno.EIO):
            with self.subTest(code=code),patch.object(os,'getxattr',side_effect=OSError(code,'refused')),self.assertRaises(OSError):acl.read_acl(1,'access',0o640)
        with patch.object(os,'getxattr',side_effect=OSError(errno.ENODATA,'absent')):
            self.assertEqual(acl.read_acl(1,'access',0o640),acl.base_acl(0o640));self.assertEqual(acl.read_acl(1,'default',0o640),[])


@unittest.skipUnless(os.name=='posix' and os.geteuid()==0,'isolated Linux root required')
class LinuxContainerAclTests(unittest.TestCase):
    def setUp(self):
        fixture.LinuxContainerPermissionTests.setUp(self)
        self.state=self.disk.disk/'acl-state';self.state.mkdir(mode=0o700)
        self.history=acl.acl_history_type(fixture.candidate)(self.disk.engine)
        self.engine=acl.container_acl_type(fixture.directories.ContainerDirectories)(self.uploads,self.publisher,self.state,self.history)
        self.backup=self.disk.target/self.history.NAMESPACE

    def command(self,path=None,kind='access',entries=None):
        path=self.leaf.name if path is None else path
        return {**self.session.request,'path':path,'aclType':kind,'entries':named_acl() if entries is None else entries,
                'expectedVersion':self.r.version(self.session.request,path,(self.root/path).lstat())}

    def request(self,command=None):return {'operationId':uuid.uuid4().hex,'owner':staging.OWNER,'plan':self.engine.plan(self.command() if command is None else command,self.session)}
    def record(self,request):return json.loads((self.state/(request['operationId']+'.json')).read_bytes())
    def history_file(self,request):return self.backup/(request['operationId']+'.json')
    def acl(self,path=None,kind='access'):
        path=self.leaf if path is None else path
        fd=os.open(path,os.O_RDONLY|os.O_NOFOLLOW)
        try:return acl.read_acl(fd,kind,os.fstat(fd).st_mode)
        finally:os.close(fd)

    def test_plan_reads_original_acl_and_effective_mask_without_writing_or_history(self):
        before=self.r.fingerprint(self.leaf.stat());plan=self.request()['plan']
        self.assertEqual(plan['accessAcl'],acl.base_acl(0o640));self.assertEqual(plan['defaultAcl'],[])
        self.assertEqual(plan['targetMode'],0o640);self.assertFalse(plan['noChange']);self.assertEqual(plan['fingerprint'],self.engine.digest(plan))
        self.assertEqual(before,self.r.fingerprint(self.leaf.stat()));self.assertFalse(self.backup.exists());self.assertEqual(list(self.state.iterdir()),[])

    def test_real_access_acl_keeps_full_bytes_inode_owner_mtime_user_attributes_and_history(self):
        os.setxattr(self.leaf,'user.test',b'preserve');before=self.leaf.stat();request=self.request();result=self.engine.apply(request,self.session)
        after=self.leaf.stat();self.assertEqual((result['state'],result['aclConfirmed'],result['bindingCurrent']),('Completed',True,True))
        self.assertEqual(self.acl(),named_acl());self.assertEqual(self.leaf.read_bytes(),self.content)
        self.assertEqual((after.st_dev,after.st_ino,after.st_mode,after.st_uid,after.st_gid,after.st_mtime_ns,after.st_size),(before.st_dev,before.st_ino,before.st_mode,before.st_uid,before.st_gid,before.st_mtime_ns,before.st_size))
        self.assertEqual(os.getxattr(self.leaf,'user.test'),b'preserve');self.assertGreater(after.st_ctime_ns,before.st_ctime_ns)
        saved=json.loads(self.history_file(request).read_text());self.assertEqual(saved['resourceType'],'ContainerAclMetadata');self.assertEqual(saved['plan'],request['plan'])
        self.assertEqual(saved['historyDigest'],result['historyDigest']);self.assertEqual(stat.S_IMODE(self.history_file(request).stat().st_mode),0o600)
        if os.path.exists('/usr/bin/getfacl'):
            output=subprocess.check_output(['/usr/bin/getfacl','--absolute-names','--numeric','--omit-header',str(self.leaf)],text=True)
            self.assertIn('user:2201:rw-\t#effective:r--',output);self.assertIn('mask::r--',output)

    def test_mask_controls_real_kernel_access_without_changing_named_entry(self):
        self.root.chmod(0o755)
        def allowed(flags):
            pid=os.fork()
            if pid==0:
                try:
                    os.setgroups([]);os.setgid(3301);os.setuid(2201)
                    fd=os.open(self.leaf.name,flags,dir_fd=self.folder);os.close(fd);os._exit(0)
                except PermissionError:os._exit(13)
                except BaseException:os._exit(2)
            _,status=os.waitpid(pid,0);return os.waitstatus_to_exitcode(status)
        self.engine.apply(self.request(),self.session);self.assertEqual(allowed(os.O_RDONLY),0);self.assertEqual(allowed(os.O_WRONLY),13)
        self.engine.apply(self.request(self.command(entries=named_acl(mask=6))),self.session)
        self.assertEqual(allowed(os.O_WRONLY),0);self.assertEqual(stat.S_IMODE(self.leaf.stat().st_mode),0o660)

    def test_default_acl_changes_only_directory_and_new_children_inherit_effective_permissions(self):
        folder=self.root/'folder';folder.mkdir(mode=0o750);folder.chmod(0o750);child=folder/'old';child.write_bytes(b'existing');old=self.r.fingerprint(child.stat());before=folder.stat()
        result=self.engine.apply(self.request(self.command('folder','default')),self.session)
        self.assertEqual(result['state'],'Completed');self.assertEqual(self.acl(folder,'default'),named_acl())
        self.assertEqual(folder.stat().st_mode,before.st_mode);self.assertEqual(self.r.fingerprint(child.stat()),old)
        fd=os.open(folder/'new',os.O_CREAT|os.O_WRONLY|os.O_EXCL,0o666);os.close(fd)
        self.assertEqual(self.acl(folder/'new'),named_acl());self.assertEqual(stat.S_IMODE((folder/'new').stat().st_mode),0o640)

    def test_access_edit_preserves_default_acl_and_default_edit_preserves_access_acl(self):
        folder=self.root/'folder';folder.mkdir(mode=0o750)
        self.engine.apply(self.request(self.command('folder','default')),self.session)
        self.engine.apply(self.request(self.command('folder',entries=named_acl(mask=7))),self.session)
        self.assertEqual(self.acl(folder),named_acl(mask=7));self.assertEqual(self.acl(folder,'default'),named_acl())
        self.engine.apply(self.request(self.command('folder','default',[])),self.session)
        self.assertEqual(self.acl(folder),named_acl(mask=7));self.assertEqual(self.acl(folder,'default'),[])

    def test_reduce_to_base_acl_removes_named_entries_and_no_change_does_not_write(self):
        self.engine.apply(self.request(),self.session)
        self.engine.apply(self.request(self.command(entries=acl.base_acl(0o600))),self.session)
        self.assertEqual(self.acl(),acl.base_acl(0o600));self.assertNotIn('system.posix_acl_access',os.listxattr(self.leaf))
        request=self.request(self.command(entries=acl.base_acl(0o600)));before=self.r.fingerprint(self.leaf.stat())
        with patch.object(os,'setxattr',side_effect=AssertionError('no write')):
            result=self.engine.apply(request,self.session)
        self.assertTrue(result['noChange']);self.assertEqual(result['state'],'Completed');self.assertEqual(self.r.fingerprint(self.leaf.stat()),before)

    def test_default_acl_on_file_and_extra_mode_recursion_owner_fields_rejected(self):
        with self.assertRaisesRegex(self.r.Rejected,'DEFAULT_REQUIRES_DIRECTORY'):self.request(self.command(kind='default'))
        for key,value in [('mode','0777'),('uid',0),('recursive',True),('hostPath','/etc/passwd')]:
            with self.subTest(key=key),self.assertRaisesRegex(self.r.Rejected,'REQUEST_INVALID'):self.request({**self.command(),key:value})

    def test_links_special_modes_and_protected_paths_fail_closed(self):
        link=self.root/'link';link.symlink_to(self.leaf)
        with self.assertRaises((OSError,self.r.Rejected)):self.request(self.command('link'))
        link.unlink();os.link(self.leaf,link)
        with self.assertRaisesRegex(self.r.Rejected,'SOURCE_UNSUPPORTED'):self.request()
        link.unlink();self.leaf.chmod(0o4640)
        with self.assertRaisesRegex(self.r.Rejected,'SOURCE_UNSUPPORTED'):self.request()
        command={**self.command(),'path':'etc/shadow'}
        with self.assertRaisesRegex(self.r.Rejected,'PROTECTED'):self.request(command)

    def test_stale_acl_version_and_resigned_lies_cannot_pass_plan_validation(self):
        request=self.request();fd=os.open(self.leaf,os.O_RDONLY)
        try:acl.write_acl(fd,'access',named_acl(mask=6))
        finally:os.close(fd)
        with self.assertRaisesRegex(self.r.Rejected,'VERSION_CONFLICT'):self.engine.apply(request,self.session)
        self.assertFalse(self.history_file(request).exists())
        for field,value in [('noChange',True),('targetMode',0o777),('accessAcl',[]),('defaultAcl',named_acl())]:
            bad=copy.deepcopy(request);bad['plan'][field]=value;bad['plan']['fingerprint']=self.engine.digest(bad['plan'])
            with self.subTest(field=field),self.assertRaises(self.r.Rejected):self.engine.apply(bad,self.session)

    def test_history_failure_prevents_acl_write_and_shared_history_domain_is_independent(self):
        request=self.request();before=self.r.fingerprint(self.leaf.stat())
        with patch.object(self.history,'capture',side_effect=OSError('unavailable')),self.assertRaises(OSError):self.engine.apply(request,self.session)
        self.assertEqual(self.r.fingerprint(self.leaf.stat()),before);self.assertEqual(self.acl(),acl.base_acl(0o640))
        self.assertEqual(self.engine.inspect(request,self.session)['state'],'Aborted')
        self.assertNotEqual(self.history.DOMAIN,fixture.candidate.ContainerPermissionHistory.DOMAIN)

    def test_actual_sigkill_before_and_after_acl_syscall_recovers_without_reapplying(self):
        for phase in ('Preparing','HistoryVerified','Prepared','Applying','AclWritten','AclChanged'):
            with self.subTest(phase=phase):
                self.leaf.unlink();self.leaf.write_bytes(self.content);self.leaf.chmod(0o640);os.chown(self.leaf,2100,3100)
                request=self.request();before=self.leaf.stat();pid=os.fork()
                if pid==0:
                    self.engine.checkpoint=lambda current:os.kill(os.getpid(),signal.SIGKILL) if current==phase else None
                    try:self.engine.apply(request,self.session)
                    except BaseException:os._exit(2)
                    os._exit(3)
                descriptor=os.pidfd_open(pid)
                try:
                    ready=bool(select.select([descriptor],[],[],15)[0])
                    if not ready:signal.pidfd_send_signal(descriptor,signal.SIGKILL)
                    _,status=os.waitpid(pid,0);self.assertTrue(ready,'child deadline')
                finally:os.close(descriptor)
                self.assertTrue(os.WIFSIGNALED(status));self.assertEqual(os.WTERMSIG(status),signal.SIGKILL)
                with patch.object(acl,'write_acl',side_effect=AssertionError('recovery cannot write')):
                    observed=self.engine.inspect(request,self.session);self.assertEqual(observed['state'],'Completed' if phase in ('AclWritten','AclChanged') else 'Aborted')
                    raw=(self.state/(request['operationId']+'.json')).read_bytes();self.assertTrue(self.engine.apply(request,self.session)['replayed']);self.assertEqual(self.engine.discard(request,self.session)['state'],observed['state'])
                self.assertEqual((self.state/(request['operationId']+'.json')).read_bytes(),raw);self.assertEqual(self.leaf.read_bytes(),self.content);self.assertEqual(self.leaf.stat().st_ino,before.st_ino)

    def test_external_nonselected_acl_change_is_unknown_and_never_overwritten(self):
        folder=self.root/'folder';folder.mkdir(mode=0o750);request=self.request(self.command('folder'))
        def change(phase):
            if phase=='Prepared':
                fd=os.open(folder,os.O_RDONLY|os.O_DIRECTORY)
                try:acl.write_acl(fd,'default',named_acl())
                finally:os.close(fd)
        self.engine.checkpoint=change
        with self.assertRaisesRegex(self.r.Rejected,'VERSION_CONFLICT'):self.engine.apply(request,self.session)
        with patch.object(acl,'write_acl',side_effect=AssertionError('cannot overwrite')):
            self.assertEqual(self.engine.inspect(request,self.session)['state'],'Unknown');self.assertEqual(self.engine.apply(request,self.session)['state'],'Unknown')
        self.assertEqual(self.acl(folder),acl.base_acl(0o750));self.assertEqual(self.acl(folder,'default'),named_acl())

    def test_missing_root_with_retained_history_is_unknown_not_repeat(self):
        request=self.request();self.engine.apply(request,self.session);(self.state/(request['operationId']+'.json')).unlink();before=self.r.fingerprint(self.leaf.stat())
        with patch.object(acl,'write_acl',side_effect=AssertionError('orphan must not write')):
            self.assertEqual(self.engine.apply(request,self.session)['state'],'Unknown');self.assertEqual(self.engine.discard(request,self.session)['state'],'Unknown')
        self.assertEqual(before,self.r.fingerprint(self.leaf.stat()));self.assertTrue(self.history_file(request).exists())

    def test_history_loss_or_replaced_inode_after_write_cannot_be_claimed_current(self):
        request=self.request();self.engine.checkpoint=lambda phase:self.history_file(request).unlink() if phase=='AclChanged' else None
        result=self.engine.apply(request,self.session);self.assertEqual(result['state'],'Unknown');self.assertFalse(result['bindingCurrent']);self.assertEqual(self.acl(),named_acl())
        self.engine.checkpoint=lambda _:None;request=self.request(self.command(entries=named_acl(mask=6)))
        def replace(phase):
            if phase=='AclChanged':self.leaf.rename(self.root/'old');self.leaf.write_bytes(b'foreign')
        self.engine.checkpoint=replace;self.assertEqual(self.engine.apply(request,self.session)['state'],'Unknown');self.assertEqual(self.leaf.read_bytes(),b'foreign')

    def test_completed_replay_keeps_original_history_but_does_not_claim_current_after_edit(self):
        request=self.request();result=self.engine.apply(request,self.session);raw=(self.state/(request['operationId']+'.json')).read_bytes()
        self.leaf.chmod(0o600)
        with patch.object(acl,'write_acl',side_effect=AssertionError('historical completion cannot reapply')):
            replay=self.engine.apply(request,self.session)
        self.assertEqual(replay['state'],'Completed');self.assertFalse(replay['bindingCurrent']);self.assertEqual(replay['version'],result['version']);self.assertEqual((self.state/(request['operationId']+'.json')).read_bytes(),raw)

    def test_source_fsync_eio_after_real_acl_write_remains_unknown_without_retrying_mutation(self):
        request=self.request();original_sync=os.fsync;identity=(self.leaf.stat().st_dev,self.leaf.stat().st_ino)
        def broken_sync(fd):
            info=os.fstat(fd)
            if (info.st_dev,info.st_ino)==identity:raise OSError(errno.EIO,'source sync unavailable')
            return original_sync(fd)
        with patch.object(os,'fsync',side_effect=broken_sync),self.assertRaises(OSError):self.engine.apply(request,self.session)
        self.assertEqual(self.acl(),named_acl());self.assertEqual(self.record(request)['state'],'Unknown')
        with patch.object(acl,'write_acl',side_effect=AssertionError('must not reapply')):
            self.assertEqual(self.engine.inspect(request,self.session)['state'],'Unknown');self.assertEqual(self.engine.apply(request,self.session)['state'],'Unknown')

    def test_named_group_is_enforced_by_kernel_supplementary_membership(self):
        self.root.chmod(0o755)
        entries=[{'tag':'user_obj','id':None,'permissions':6},{'tag':'group_obj','id':None,'permissions':0},
                 {'tag':'group','id':3301,'permissions':6},{'tag':'mask','id':None,'permissions':4},{'tag':'other','id':None,'permissions':0}]
        self.engine.apply(self.request(self.command(entries=entries)),self.session)
        for groups,expected in (([],13),([3301],0)):
            pid=os.fork()
            if pid==0:
                try:
                    os.setgroups(groups);os.setgid(4401);os.setuid(4401);fd=os.open(self.leaf.name,os.O_RDONLY,dir_fd=self.folder);os.close(fd);os._exit(0)
                except PermissionError:os._exit(13)
                except BaseException:os._exit(2)
            _,status=os.waitpid(pid,0);self.assertEqual(os.waitstatus_to_exitcode(status),expected)

    def test_maximum_acl_budget_roundtrips_real_filesystem_and_journal(self):
        entries=[{'tag':'user_obj','id':None,'permissions':6}]+[{'tag':'user','id':uid,'permissions':4} for uid in range(2200,2260)]+[
            {'tag':'group_obj','id':None,'permissions':0},{'tag':'mask','id':None,'permissions':4},{'tag':'other','id':None,'permissions':0}]
        self.assertEqual(len(entries),64);request=self.request(self.command(entries=entries));self.assertEqual(self.engine.apply(request,self.session)['state'],'Completed')
        self.assertEqual(self.acl(),entries);self.assertLess((self.state/(request['operationId']+'.json')).stat().st_size,self.engine.LIMIT)
