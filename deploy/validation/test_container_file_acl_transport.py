"""Fixed ACL transport: real temporary inode ACLs, synthetic Root/mount proof.

No installed RPC or production write; separate native gates attest real mounts.
"""
import ctypes
import errno
import struct
import os
from pathlib import Path
from types import SimpleNamespace
import unittest
from unittest.mock import Mock,patch
import test_container_file_acl as fixtures
import test_container_file_staging as common
transport=common.load('whaledeck-container-acl-transport.py','acl_transport_tests')

class AclMappingTests(unittest.TestCase):
    def test_unproven_or_idmapped_mount_never_passes_as_unmapped(self):
        with patch.object(transport,'mount_attributes',return_value=0):transport.require_unmapped(1)
        with patch.object(transport,'mount_attributes',return_value=0x100000),self.assertRaisesRegex(transport.Rejected,'MAPPING_UNPROVEN'):transport.require_unmapped(1)
        for error in (OSError(errno.ENOSYS,'missing'),transport.Rejected('CONTAINER_ACL_MAPPING_UNPROVEN')):
            with patch.object(transport,'mount_attributes',side_effect=error),self.assertRaises(type(error)):transport.require_unmapped(1)
    def test_kernel_fd_query_requires_exact_basic_result_and_bounded_size(self):
        for result,size,mask,expected in ((0,512,2,True),(0,79,2,False),(0,513,2,False),(0,512,0,False),(-1,512,2,False)):
            def query(_nr,request,output,_size,_flags):
                self.assertEqual(struct.unpack('<IIQQQ',request._obj.raw[:32]),(32,9,0,2,0));self.assertEqual(_flags.value,1)
                raw=bytearray(512);struct.pack_into('<I',raw,0,size);struct.pack_into('<Q',raw,8,mask);struct.pack_into('<Q',raw,64,0x26)
                ctypes.memmove(output,bytes(raw),512);return result
            library=SimpleNamespace(syscall=Mock(side_effect=query))
            with self.subTest(result=result,size=size,mask=mask),patch.object(transport.ctypes,'CDLL',return_value=library),patch.object(transport.os,'uname',return_value=SimpleNamespace(machine='x86_64')):
                if expected:self.assertEqual(transport.mount_attributes(9),0x26)
                else:
                    with self.assertRaisesRegex(transport.Rejected,'MAPPING_UNPROVEN'):transport.mount_attributes(9)
    def test_user_namespace_must_match_helper_and_pid_one_with_stable_session(self):
        session=SimpleNamespace(request={'containerId':'a'*64,'instanceId':'bound'},current=Mock());reader=SimpleNamespace(target_binding=Mock(return_value=123))
        for ids,accepted in (((1,1,1),True),((1,2,1),False),((1,1,2),False)):
            with patch.object(transport.os,'stat',side_effect=[SimpleNamespace(st_ino=i) for i in ids]):
                if accepted:transport.namespace_current(reader,session)
                else:
                    with self.assertRaisesRegex(transport.Rejected,'MAPPING_UNPROVEN'):transport.namespace_current(reader,session)
        with patch.object(transport.os,'stat',side_effect=FileNotFoundError),self.assertRaisesRegex(transport.Rejected,'MAPPING_UNPROVEN'):transport.namespace_current(reader,session)
    def test_errors_are_bounded_and_unrelated_missing_storage_is_not_not_found(self):
        self.assertEqual(transport.error_code(FileNotFoundError('private HDD missing')),'CONTAINER_ACL_UNAVAILABLE')
        self.assertEqual(transport.error_code(RuntimeError('password secret')),'CONTAINER_ACL_UNAVAILABLE')
        self.assertEqual(transport.error_code(OSError(errno.ENOTSUP,'ACL unavailable')),'CONTAINER_ACL_UNSUPPORTED')
        for code in ('CONTAINER_ACL_NOT_FOUND','CONTAINER_ACL_MAPPING_UNPROVEN','CONTAINER_FILES_TARGET_MISSING'):
            self.assertEqual(transport.error_code(RuntimeError(code)),code)
        with self.assertRaisesRegex(transport.Rejected,'MODULE_UNSAFE'):transport.load('not-approved.py')

@unittest.skipUnless(os.name=='posix' and os.geteuid()==0,'isolated Linux root required')
class AclTransportTests(unittest.TestCase):
    def setUp(self):
        self.fixture=fixtures.LinuxContainerAclTests();self.fixture.setUp();self.addCleanup(self.fixture.doCleanups)
        self.r,self.f,self.session=self.fixture.r,self.fixture.f,self.fixture.session
        base=self.fixture.engine
        self.engine=transport.scoped_acl_type(type(base))(base.u,base.p,base.state_path,base.history)
        self.enter=patch.object(self.r,'ContainerRoot',side_effect=lambda *_:__import__('contextlib').nullcontext(self.session));self.enter.start();self.addCleanup(self.enter.stop)
        mapping=patch.object(transport,'mount_attributes',return_value=0);mapping.start();self.addCleanup(mapping.stop)
    def execute(self,action,request):return transport.execute(action,request,self.r,self.f,None,self.engine)
    def test_full_fixed_actions_keep_acl_history_and_bytes(self):
        request=self.fixture.request();self.assertEqual(self.execute('plan',request['plan']['command']),request['plan'])
        result=self.execute('apply',request);self.assertEqual((result['state'],result['aclConfirmed']),('Completed',True));record=self.fixture.record(request)
        for action in ('inspect','apply','discard'):
            self.assertEqual(self.execute(action,request)['state'],'Completed');self.assertEqual(record,self.fixture.record(request))
        self.assertEqual(self.fixture.leaf.read_bytes(),self.fixture.content)
    def test_mapping_rejection_before_preflight_never_creates_history_or_journal(self):
        command=self.fixture.command();before=self.r.fingerprint(self.fixture.leaf.stat())
        with patch.object(transport,'mount_attributes',return_value=0x100000),self.assertRaisesRegex(self.r.Rejected,'MAPPING_UNPROVEN'):self.execute('plan',command)
        self.assertEqual(before,self.r.fingerprint(self.fixture.leaf.stat()));self.assertFalse(self.fixture.backup.exists());self.assertEqual(list(self.fixture.state.iterdir()),[])
    def test_mapping_lost_after_written_is_unknown_not_a_new_acl_write(self):
        request=self.fixture.request()
        self.engine.checkpoint=lambda phase: setattr(self.engine,'snapshot',Mock(side_effect=self.r.Rejected('CONTAINER_ACL_MAPPING_UNPROVEN'))) if phase=='AclWritten' else None
        result=self.execute('apply',request);self.assertEqual(result['state'],'Unknown')
        with patch.object(fixtures.acl,'write_acl',side_effect=AssertionError('never reapply')):self.assertEqual(self.execute('apply',request)['state'],'Unknown')
    def test_exact_target_loss_observes_old_receipt_and_never_creates_false_absence(self):
        request=self.fixture.request();self.execute('apply',request);original=self.fixture.record(request)
        for code in ('CONTAINER_FILES_TARGET_MISSING','CONTAINER_FILES_INSTANCE_CHANGED','CONTAINER_FILES_TARGET_CHANGED','CONTAINER_FILES_ROOT_CHANGED'):
            with patch.object(self.r,'ContainerRoot',side_effect=self.r.Rejected(code)):
                for action in ('apply','inspect','discard'):
                    result=self.execute(action,request);self.assertEqual(result['state'],'Completed');self.assertFalse(result['bindingCurrent']);self.assertEqual(original,self.fixture.record(request))
    def test_no_root_target_and_delayed_apply_cannot_bypass_discard_fence(self):
        request=self.fixture.request();before=self.r.fingerprint(self.fixture.leaf.stat())
        with patch.object(self.r,'ContainerRoot',side_effect=self.r.Rejected('CONTAINER_FILES_TARGET_MISSING')):
            with self.assertRaisesRegex(self.r.Rejected,'SESSION_REQUIRED'):self.execute('apply',request)
            with self.assertRaisesRegex(self.r.Rejected,'NOT_FOUND'):self.execute('inspect',request)
            self.assertEqual(self.execute('discard',request)['state'],'Aborted')
        self.assertEqual(self.execute('apply',request)['state'],'Aborted');self.assertEqual(before,self.r.fingerprint(self.fixture.leaf.stat()));self.assertFalse(self.fixture.backup.exists())
    def test_unrelated_docker_registry_or_hdd_failure_never_fabricates_tombstone(self):
        request=self.fixture.request()
        for error in (ConnectionError('docker'),FileNotFoundError('storage'),PermissionError('REGISTRY_UNSAFE')):
            with patch.object(self.r,'ContainerRoot',side_effect=error):
                for action in ('apply','inspect','discard'):
                    with self.assertRaises(type(error)):self.execute(action,request)
        self.assertEqual(list(self.fixture.state.glob('*.json')),[])
