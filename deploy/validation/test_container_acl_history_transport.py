"""Read-only driver scope/deny rules. Mapping and ContainerRoot are explicit fixtures."""
import contextlib
import errno
import os
from pathlib import Path
import subprocess
import sys
from types import SimpleNamespace
import unittest
from unittest.mock import patch

import test_container_file_acl as single
from test_container_file_staging import load

transport=load('whaledeck-container-acl-history-transport.py','acl_history_transport_candidate')
catalog=load('whaledeck-container-acl-history.py','acl_history_transport_reader')

@unittest.skipUnless(os.geteuid()==0,'isolated Linux root required')
class AclHistoryTransportTests(unittest.TestCase):
    def setUp(self):
        self.base=single.LinuxContainerAclTests();self.base.setUp();self.addCleanup(self.base.doCleanups)
        self.reader=catalog.ContainerAclHistoryReader(self.base.engine)
        self.bridge=SimpleNamespace(namespace_current=lambda *_:None,require_unmapped=lambda _:None)
        @contextlib.contextmanager
        def root(*_):yield self.base.session
        self.patch=patch.object(self.base.r,'ContainerRoot',side_effect=root);self.patch.start();self.addCleanup(self.patch.stop)
    def listing(self):return {**self.base.session.request,'path':self.base.leaf.name,'after':'','take':10,'expectedVersion':''}
    def execute(self,action,request):return transport.execute(action,request,self.base.r,self.base.f,None,self.reader,self.bridge)
    def test_list_and_selected_capture_read_have_no_mutating_fallback(self):
        request=self.base.request();receipt=self.base.engine.apply(request,self.base.session)
        before=self.base.record(request)
        with patch.object(self.base.engine,'apply',side_effect=AssertionError('must not execute ACL')),patch.object(self.base.history,'capture',side_effect=AssertionError('must not write history')):
            page=self.execute('list',self.listing());self.assertEqual(len(page['entries']),1)
            result=self.execute('read',{**self.base.session.request,'path':self.base.leaf.name,'captureId':request['operationId'],'expectedHistoryDigest':receipt['historyDigest']})
        self.assertEqual(result['accessAcl'],single.acl.base_acl(0o640));self.assertEqual(before,self.base.record(request))
    def test_only_list_and_read_are_allowed(self):
        for action in ('apply','inspect','discard','source','list;reboot','../../read'):
            with self.subTest(action=action),self.assertRaisesRegex(transport.Rejected,'REQUEST_INVALID'):self.execute(action,self.listing())
    def test_extra_raw_path_fields_protected_paths_and_wrong_binding_are_denied(self):
        for fields in ({'hostPath':'/etc/shadow'},{'path':'proc/self'},{'path':'../x'},{'containerId':'invalid'}):
            with self.subTest(fields=fields),self.assertRaises((transport.Rejected,self.base.r.Rejected)):
                self.execute('list',{**self.listing(),**fields})
    def test_namespace_mapping_failure_never_returns_snapshot(self):
        def reject(*_):raise transport.Rejected('CONTAINER_ACL_MAPPING_UNPROVEN')
        self.bridge.namespace_current=reject
        with self.assertRaisesRegex(transport.Rejected,'MAPPING'):self.execute('list',self.listing())
    def test_idmapped_fd_failure_never_returns_snapshot(self):
        def reject(*_):raise transport.Rejected('CONTAINER_ACL_MAPPING_UNPROVEN')
        self.bridge.require_unmapped=reject
        with self.assertRaisesRegex(transport.Rejected,'MAPPING'):self.execute('list',self.listing())
    def test_mapping_rechecked_after_metadata_read(self):
        count=[0]
        def reject(*_):
            count[0]+=1
            if count[0]==2:raise transport.Rejected('CONTAINER_ACL_MAPPING_UNPROVEN')
        self.bridge.namespace_current=reject
        with self.assertRaisesRegex(transport.Rejected,'MAPPING'):self.execute('list',self.listing())
    def test_missing_current_target_is_explicit_not_an_empty_page(self):
        self.base.leaf.unlink()
        with self.assertRaisesRegex(transport.Rejected,'TARGET_MISSING'):self.execute('list',self.listing())
    def test_path_replacement_after_query_does_not_return_history(self):
        original=self.reader.listing
        def replace(*args):
            result=original(*args);self.base.leaf.unlink();self.base.leaf.write_bytes(b'new identity');return result
        with patch.object(self.reader,'listing',side_effect=replace),self.assertRaisesRegex(transport.Rejected,'VERSION_CONFLICT'):self.execute('list',self.listing())
    def test_lost_hdd_or_base_is_unavailable_not_missing_capture(self):
        self.assertEqual(transport.error_code(FileNotFoundError()),'CONTAINER_ACL_HISTORY_UNAVAILABLE')
        self.assertEqual(transport.error_code(OSError(errno.EIO,'not logged')),'CONTAINER_ACL_HISTORY_UNAVAILABLE')
    def test_errors_are_fixed_codes_without_exception_or_input_echo(self):
        self.assertEqual(transport.error_code(ValueError('secret-value')),'CONTAINER_ACL_HISTORY_UNAVAILABLE')
        self.assertEqual(transport.error_code(transport.Rejected('CONTAINER_ACL_HISTORY_unsafe-user-text')),'CONTAINER_ACL_HISTORY_UNAVAILABLE')
        self.assertEqual(transport.error_code(transport.Rejected('CONTAINER_ACL_MAPPING_UNPROVEN')),'CONTAINER_ACL_HISTORY_MAPPING_UNPROVEN')
    def test_missing_capture_is_not_found_but_lost_hdd_base_remains_unavailable(self):
        request=self.base.request();receipt=self.base.engine.apply(request,self.base.session)
        query={**self.base.session.request,'path':self.base.leaf.name,'captureId':'e'*32,'expectedHistoryDigest':receipt['historyDigest']}
        with self.assertRaisesRegex(self.base.r.Rejected,'HISTORY_NOT_FOUND'):self.execute('read',query)
        with patch.object(self.base.history.storage,'target',side_effect=FileNotFoundError()),self.assertRaises(FileNotFoundError):self.execute('read',query)
    def test_shared_wrapper_grants_only_read_capabilities_and_one_lease_directory(self):
        text=(Path(__file__).resolve().parents[1]/'host/whaledeck-privileged').read_text()
        block=text.split('  container-file-acl-history)',1)[1].split('  container-file-acl-history-stop)',1)[0]
        self.assertIn('RuntimeMaxSec=35',block);self.assertIn('CAP_DAC_READ_SEARCH CAP_SYS_PTRACE',block)
        for forbidden in ('CAP_FOWNER','CAP_CHOWN','CAP_DAC_OVERRIDE','CAP_SYS_ADMIN','ReadWritePaths=/data','ReadWritePaths=/var/lib/whaledeck-container-acl'):
            self.assertNotIn(forbidden,block)
        self.assertIn('ReadWritePaths=/var/lib/whaledeck-container-files',block)
        stop=text.split('  container-file-acl-history-stop)',1)[1].split('    ;;',1)[0]
        self.assertIn('InvocationID',stop);self.assertIn('== "$3"',stop)
    def test_invalid_action_cli_exits_before_loading_or_reading_input(self):
        result=subprocess.run([sys.executable,'-I',str(Path(transport.__file__)),'apply'],input='private input',text=True,capture_output=True,timeout=5)
        self.assertEqual(result.returncode,1);self.assertNotIn('private input',result.stdout+result.stderr)
        self.assertIn('CONTAINER_ACL_HISTORY_REQUEST_INVALID',result.stdout)

if __name__=='__main__':unittest.main()
