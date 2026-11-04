"""Isolated Linux ACL filesystem + explicit ContainerRoot/mapping fixtures; not installed RPC evidence."""
import contextlib
import errno
import os
from pathlib import Path
import subprocess
import sys
from types import SimpleNamespace
import unittest
from unittest.mock import patch

import test_container_recursive_acl_history as history
from test_container_file_staging import load

transport=load('whaledeck-container-recursive-acl-history-transport.py','recursive_history_transport_candidate')

@unittest.skipUnless(os.geteuid()==0,'isolated Linux root required')
class RecursiveAclHistoryTransportTests(unittest.TestCase):
    def setUp(self):
        self.h=history.RecursiveAclHistoryTests();self.h.setUp();self.addCleanup(self.h.doCleanups)
        self.b=self.h.base.base
        self.bridge=SimpleNamespace(namespace_current=lambda *_:None,require_unmapped=lambda _:None)
        @contextlib.contextmanager
        def root(*_):yield self.h.session
        self.scope=patch.object(self.b.r,'ContainerRoot',side_effect=root);self.scope.start();self.addCleanup(self.scope.stop)
    def execute(self,action,request):return transport.execute(action,request,self.b.r,self.b.f,None,self.h.catalog,self.bridge)
    def test_full_manifest_pagination_has_no_write_or_old_operation_fallback(self):
        request,receipt=self.h.capture();before=self.h.snapshot()
        with self.h.base.no_write(),patch.object(self.b.f,'save_receipt',side_effect=AssertionError('read journal write')):
            page=self.execute('list',self.h.listing());self.assertEqual(len(page['entries']),1)
            first=self.execute('read',self.h.read(request,receipt,take=1));self.assertFalse(first['complete']);self.assertEqual(first['nextOffset'],1)
            last=self.execute('read',self.h.read(request,receipt,offset=3,take=1));self.assertTrue(last['complete']);self.assertIsNone(last['nextOffset']);self.assertEqual(last['entries'][0]['relativePath'],'')
        self.assertEqual(before,self.h.snapshot())
    def test_only_list_read_and_exact_request_fields_are_allowed(self):
        for action in ('apply','inspect','discard','restore','source','list;reboot','../../read'):
            with self.subTest(action=action),self.assertRaisesRegex(transport.Rejected,'REQUEST_INVALID'):self.execute(action,self.h.listing())
        for extra in ({'hostPath':'/etc/shadow'},{'operationId':'1'*32},{'offset':0}):
            with self.subTest(extra=extra),self.assertRaisesRegex(transport.Rejected,'REQUEST_INVALID'):self.execute('list',{**self.h.listing(),**extra})
    def test_binding_paths_and_non_directory_targets_are_denied(self):
        for extra in ({'path':'proc/self'},{'path':'../x'},{'path':'tree/one.bin'},{'containerId':'bad'}):
            with self.subTest(extra=extra),self.assertRaises((transport.Rejected,self.b.r.Rejected)):self.execute('list',{**self.h.listing(),**extra})
    def test_mapping_and_namespace_are_checked_before_and_after_read(self):
        for method in ('namespace_current','require_unmapped'):
            for reject_at in (1,2):
                count=[0]
                def check(*_):
                    count[0]+=1
                    if count[0]==reject_at:raise transport.Rejected('CONTAINER_ACL_MAPPING_UNPROVEN')
                with self.subTest(method=method,reject_at=reject_at),patch.object(self.bridge,method,side_effect=check),self.assertRaisesRegex(transport.Rejected,'MAPPING_UNPROVEN'):self.execute('list',self.h.listing())
    def test_replaced_root_is_pinned_and_result_discarded(self):
        original=self.h.catalog.listing;tree=self.h.base.tree
        def replace(*args):
            result=original(*args);tree.rename(tree.with_name('old-tree'));tree.mkdir();return result
        with patch.object(self.h.catalog,'listing',side_effect=replace),self.assertRaisesRegex(transport.Rejected,'VERSION_CONFLICT'):self.execute('list',self.h.listing())
    def test_changed_root_attributes_in_same_inode_invalidate_both_queries(self):
        request,receipt=self.h.capture()
        for action in ('list','read'):
            method='listing' if action=='list' else 'read';original=getattr(self.h.catalog,method)
            def change(*args):
                result=original(*args);os.setxattr(self.h.base.tree,'user.history-query',os.urandom(8));return result
            with self.subTest(action=action),patch.object(self.h.catalog,method,side_effect=change),self.assertRaisesRegex(transport.Rejected,'VERSION_CONFLICT'):
                self.execute(action,self.h.listing() if action=='list' else self.h.read(request,receipt))
    def test_missing_target_and_outage_do_not_become_empty_history(self):
        with self.assertRaisesRegex(transport.Rejected,'TARGET_MISSING'):self.execute('list',{**self.h.listing(),'path':'absent'})
        self.assertEqual(transport.error_code(FileNotFoundError()),'CONTAINER_RECURSIVE_ACL_HISTORY_UNAVAILABLE')
        self.assertEqual(transport.error_code(OSError(errno.EIO,'private')),'CONTAINER_RECURSIVE_ACL_HISTORY_UNAVAILABLE')
    def test_failed_query_and_second_fd_check_close_all_descriptors(self):
        before=set(os.listdir('/proc/self/fd'))
        with patch.object(self.h.catalog,'listing',side_effect=OSError(errno.EIO,'private')),self.assertRaises(OSError):self.execute('list',self.h.listing())
        self.assertEqual(before,set(os.listdir('/proc/self/fd')))
        calls=[0]
        def check(_):
            calls[0]+=1
            if calls[0]==2:raise transport.Rejected('CONTAINER_ACL_MAPPING_UNPROVEN')
        with patch.object(self.bridge,'require_unmapped',side_effect=check),self.assertRaises(transport.Rejected):self.execute('list',self.h.listing())
        self.assertEqual(before,set(os.listdir('/proc/self/fd')))
    def test_error_codes_are_bounded_and_never_echo_exception_or_user_input(self):
        for error in (ValueError('private input'),transport.Rejected('CONTAINER_RECURSIVE_ACL_HISTORY_private'),transport.Rejected('CONTAINER_RECURSIVE_ACL_apply_private')):
            self.assertEqual(transport.error_code(error),'CONTAINER_RECURSIVE_ACL_HISTORY_UNAVAILABLE')
        for code in ('CONTAINER_ACL_PLAN_INVALID','CONTAINER_ACL_ENTRIES_INVALID','CONTAINER_RECURSIVE_ACL_HISTORY_CHANGED'):
            self.assertEqual(transport.error_code(ValueError(code)),'CONTAINER_RECURSIVE_ACL_HISTORY_INVALID')
        self.assertEqual(transport.error_code(ValueError('CONTAINER_ACL_MAPPING_UNPROVEN')),'CONTAINER_RECURSIVE_ACL_HISTORY_MAPPING_UNPROVEN')
    def test_invalid_cli_action_exits_without_loading_modules_or_input(self):
        result=subprocess.run([sys.executable,'-I',transport.__file__,'apply'],input='private input',text=True,capture_output=True,timeout=5)
        self.assertEqual(result.returncode,1);self.assertNotIn('private input',result.stdout+result.stderr);self.assertIn('REQUEST_INVALID',result.stdout)
    def test_privileged_wrapper_has_read_capabilities_and_invocation_bound_stop_only(self):
        text=(Path(__file__).resolve().parents[1]/'host/whaledeck-privileged').read_text()
        block=text.split('  container-file-recursive-acl-history)',1)[1].split('  container-file-recursive-acl-history-stop)',1)[0]
        self.assertIn('CAP_DAC_READ_SEARCH CAP_SYS_PTRACE',block);self.assertIn('MemoryMax=256M',block);self.assertIn('RuntimeMaxSec=35',block)
        self.assertIn('ReadWritePaths=/var/lib/whaledeck-container-files',block)
        for value in ('CAP_FOWNER','CAP_CHOWN','CAP_DAC_OVERRIDE','CAP_SYS_ADMIN','ReadWritePaths=/data','ReadWritePaths=/var/lib/whaledeck-container-recursive-acl'):
            self.assertNotIn(value,block)
        stop=text.split('  container-file-recursive-acl-history-stop)',1)[1].split('    ;;',1)[0];self.assertIn('InvocationID',stop);self.assertIn('== "$3"',stop)
    def test_module_allowlist_and_installer_have_no_arbitrary_load(self):
        with self.assertRaisesRegex(transport.Rejected,'MODULE_UNSAFE'):transport.load('../../private.py')
        for filename in ('whaledeck-container-recursive-acl-history.py','whaledeck-container-recursive-acl-history-transport.py'):
            for script in ('install.sh','validate.sh'):
                self.assertIn(filename,(Path(__file__).resolve().parents[1]/'host'/script).read_text())

if __name__=='__main__':unittest.main()
