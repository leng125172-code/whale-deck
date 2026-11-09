"""Real temp FD/hash/xattr acquisition; namespace and HDD attestation are doubles.

No installed state changes, live container mutations, clock change or cleanup.
"""
import copy
import base64
import hashlib
import json
import subprocess
import tempfile
import fcntl
import os
import sys
import time
from pathlib import Path
from types import SimpleNamespace
import unittest
from unittest.mock import patch
import container_recycle_archive_retirement_agent_io as io
import container_recycle_archive_retirement_rpc_proof as proof
import test_container_recycle_archive_retirement_rpc_proof as fixtures
from recursive_acl_restore_agent_io import snapshot


@unittest.skipUnless(os.name=='posix' and os.geteuid()==0,'isolated Linux root required')
class RetirementReadOnlyCaptureTests(unittest.TestCase):
    def setUp(self):
        self.f=fixtures.RetirementRootJoinTests();self.f.setUp();self.addCleanup(self.f.doCleanups)
        f=self.f.f;self.q=self.f.q;self.folder=f.f.work;session=f.f.session
        class Manager:
            def __enter__(self):return session
            def __exit__(self,*_):pass
        self.hardware=SimpleNamespace(files=f.f.source.f,reader=SimpleNamespace(ContainerRoot=lambda *_:Manager()),docker=None,storage=f.f.source.history.storage)
        for name,value in dict(STATE=self.f.state,SOURCE=f.f.base.state,PURGE=f.f.state,ARCHIVE=f.state,CLAIMS=f.engine.source.restore_claims_path).items():
            patcher=patch.object(io,name,value);patcher.start();self.addCleanup(patcher.stop)
        patcher=patch.object(io,'installed_io',return_value=self.hardware);patcher.start();self.addCleanup(patcher.stop)
        patcher=patch.object(sys,'dont_write_bytecode',True);patcher.start();self.addCleanup(patcher.stop)

    def test_before_reserve_captures_absent_selected_records_without_creating_them(self):
        before=snapshot(self.f.state);value=io.capture(self.q,self.folder)
        self.assertEqual(value['registry']['records'],{});self.assertEqual(value['registry']['markers'],{});self.assertEqual(snapshot(self.f.state),before)
        source=proof.source_ref(self.q['plan'])['operationId'];self.assertIn(source+'.data',value['hdd']);self.assertIn(source+'.json',value['hdd']);self.assertEqual(value['namespace']['entries']['recycle-rpc.bin']['identity'],self.q['plan']['publishedIdentity'])

    def test_reserved_capture_joins_real_bytes_without_writable_opens(self):
        r=self.f.engine.reserve(self.q,self.f.f.f.session);before=snapshot(self.f.state);opened=os.open
        def readonly(path,flags,*args,**kwargs):
            self.assertFalse(flags&(os.O_CREAT|os.O_TRUNC|os.O_WRONLY|os.O_RDWR|os.O_APPEND));return opened(path,flags,*args,**kwargs)
        with patch.object(os,'open',side_effect=readonly):value=io.capture(self.q,self.folder)
        proof.record_proof(self.q,r,value['registry']['records'][self.q['operationId']],value['registry']['markers'][self.f.ref['operationId']],value['originals'],originals_before=self.f.before,original_frames=value['originalFrames'],frames_before=self.f.frames_before,registry=value['registry']['records'],root_frames=value['registry']['frames'],observed_at=value['clockAfterUnixMs'])
        self.assertEqual(before,snapshot(self.f.state));self.assertLessEqual(value['clockBeforeUnixMs'],value['clockAfterUnixMs'])

    def test_missing_state_refuses_before_any_provider_loading(self):
        missing=self.f.state.parent/'not-installed'
        with patch.object(io,'STATE',missing),patch.object(io,'installed_io') as provider,self.assertRaises(FileNotFoundError):io.capture(self.q,self.folder)
        provider.assert_not_called();self.assertFalse(missing.exists())

    def test_busy_history_lock_fails_without_waiting_or_modifying(self):
        lock=self.f.data.parent/'history.lock';fd=os.open(lock,os.O_RDONLY)
        try:
            fcntl.flock(fd,fcntl.LOCK_EX|fcntl.LOCK_NB);before=snapshot(self.f.state)
            with self.assertRaises(BlockingIOError):io.capture(self.q,self.folder)
            self.assertEqual(before,snapshot(self.f.state))
        finally:os.close(fd)

    def test_changed_between_passes_is_never_coherent(self):
        read=io._payloads;calls=[]
        def changed(fd,deadline):
            result=read(fd,deadline);calls.append(True)
            if len(calls)==1:self.f.data.write_bytes(b'owned temporary HDD changed after first pass')
            return result
        with patch.object(io,'_payloads',side_effect=changed),self.assertRaises(AssertionError):io.capture(self.q,self.folder)
        self.assertEqual(len(calls),2)

    def test_storage_configuration_switch_is_rejected(self):
        original=self.hardware.storage.configuration;calls=[]
        def switched():
            value=original();calls.append(True)
            if len(calls)>1:value={**value,'targetRelative':'changed'}
            return value
        with patch.object(self.hardware.storage,'configuration',side_effect=switched),self.assertRaises(AssertionError):io.capture(self.q,self.folder)

    def test_symlink_and_hardlink_in_registry_are_rejected(self):
        target=self.f.state/('a'*32+'.json');target.symlink_to(self.f.data)
        with self.assertRaises(OSError):io.registry_snapshot()
        target.unlink();r=self.f.engine.reserve(self.q,self.f.f.f.session);self.assertEqual(r['state'],'Reserved')
        target=self.f.state/('a'*32+'.json');os.link(self.f.state/(self.q['operationId']+'.json'),target)
        with self.assertRaises(AssertionError):io.registry_snapshot()

    def test_wrong_parent_or_mutated_live_inode_cannot_be_selected(self):
        wrong=self.folder.parent
        with self.assertRaises(AssertionError):io.capture(self.q,wrong)
        self.f.f.f.leaf.write_bytes(b'changed owned live copy')
        with self.assertRaises(AssertionError):io.capture(self.q,self.folder)

    def test_bytecode_must_be_disabled_before_any_capture(self):
        with patch.object(sys,'dont_write_bytecode',False),patch.object(io,'registry_snapshot') as registry,self.assertRaisesRegex(AssertionError,'BYTECODE_DISABLED'):io.capture(self.q,self.folder)
        registry.assert_not_called()

    def test_completed_collection_is_supported_without_recreating_payload(self):
        self.f.engine.reserve(self.q,self.f.f.f.session);due=self.q['plan']['notBeforeUnixMs']
        with patch('time.time_ns',return_value=(due+1000)*1000000):
            before=io.capture(self.q,self.folder);receipt=self.f.engine.apply(self.q,self.f.f.f.session);after=io.capture(self.q,self.folder)
        source=self.f.ref['operationId'];self.assertIn(source+'.data',before['hdd']);self.assertNotIn(source+'.data',after['hdd']);self.assertEqual(before['namespace'],after['namespace'])
        result=io.physical_delta_proof(before,after,receipt);self.assertTrue(result['selectedDataAbsent'])
        extra=copy.deepcopy(after);extra['hdd']['foreign.data']=next(iter(after['hdd'].values()))
        with self.assertRaises(AssertionError):io.physical_delta_proof(before,extra,receipt)
        extra=copy.deepcopy(after);extra['hdd'][source+'.data']=before['hdd'][source+'.data']
        with self.assertRaises(AssertionError):io.physical_delta_proof(before,extra,receipt)

    def test_registry_and_payload_budgets_reject_before_large_reads(self):
        self.f.engine.reserve(self.q,self.f.f.f.session)
        with patch.object(io,'MAX_REGISTRY_BYTES',1),self.assertRaises(AssertionError):io.registry_snapshot()
        with patch.object(io,'MAX_BYTES',1),self.assertRaises(AssertionError):io.capture(self.q,self.folder)
        fd=os.open(self.f.data.parent,os.O_RDONLY|os.O_DIRECTORY)
        try:
            for maximum,deadline in ((1,time.monotonic()+10),(16777216,time.monotonic()-1)):
                with self.subTest(maximum=maximum),patch.object(os,'read') as read,self.assertRaises(AssertionError):io._file(fd,self.f.data.name,deadline,maximum)
                read.assert_not_called()
        finally:os.close(fd)

    def test_physical_delta_rejects_unbound_before_bytes_or_extra_marker(self):
        self.f.engine.reserve(self.q,self.f.f.f.session);due=self.q['plan']['notBeforeUnixMs']
        with patch('time.time_ns',return_value=(due+1000)*1000000):
            before=io.capture(self.q,self.folder);receipt=self.f.engine.apply(self.q,self.f.f.f.session);after=io.capture(self.q,self.folder)
        wrong=copy.deepcopy(before);wrong['registry']['frames'][self.q['operationId']+'.json']['sha256']='0'*64
        with self.assertRaises(AssertionError):io.physical_delta_proof(wrong,after,receipt)
        wrong=copy.deepcopy(after);wrong['registry']['markers']['0'*32]=next(iter(after['registry']['markers'].values()))
        with self.assertRaises(AssertionError):io.physical_delta_proof(before,wrong,receipt)

    def test_combined_registry_entry_budget_is_checked_before_json_reads(self):
        self.f.engine.reserve(self.q,self.f.f.f.session)
        extra=self.f.state/'claims'/('f'*32+'.json');extra.write_bytes(b'never parse beyond the combined budget');extra.chmod(0o600)
        with patch.object(io,'MAX_ENTRIES',3),patch.object(io,'_json') as read,self.assertRaises(AssertionError):io.registry_snapshot()
        read.assert_not_called()

    def test_raw_request_bytes_are_retained_without_asserting_rpc_origin(self):
        raw=(json.dumps(self.q,separators=(',',':'))+'\n').encode();value=io.capture_native(raw,self.folder)
        self.assertEqual(base64.b64decode(value['nativeInput']['base64']),raw);self.assertEqual(value['nativeInput']['sha256'],hashlib.sha256(raw).hexdigest());self.assertFalse(value['nativeInput']['rpcOriginVerified'])
        for bad in (raw[:-1],raw+b'{}\n',b'{}\r\n'):
            with self.subTest(length=len(bad)),patch.object(io,'registry_snapshot') as registry,self.assertRaises(AssertionError):io.capture_native(bad,self.folder)
            registry.assert_not_called()

    def test_entry_rejects_missing_B_before_importing_any_collector(self):
        script=Path(__file__).with_name('collect_container_recycle_archive_retirement.py')
        with tempfile.TemporaryDirectory() as name:
            target=Path(name)/script.name;target.write_bytes(script.read_bytes());before=set(Path(name).iterdir())
            run=subprocess.run([sys.executable,'-I',str(target)],input=b'',capture_output=True,timeout=5)
            self.assertEqual(run.returncode,1);self.assertEqual(json.loads(run.stdout),dict(collection='failed',code='BYTECODE_MUST_BE_DISABLED'));self.assertEqual(run.stderr,b'');self.assertEqual(set(Path(name).iterdir()),before)

    def test_entry_rejects_trailing_packet_before_project_import(self):
        script=Path(__file__).with_name('collect_container_recycle_archive_retirement.py')
        with tempfile.TemporaryDirectory() as name:
            target=Path(name)/script.name;target.write_bytes(script.read_bytes());before=set(Path(name).iterdir())
            packet=dict(nativeRequest='{}\n',folder='/unused')
            run=subprocess.run([sys.executable,'-B','-I',str(target)],input=(json.dumps(packet)+'\n{}\n').encode(),capture_output=True,timeout=5)
            self.assertEqual(run.returncode,1);self.assertEqual(json.loads(run.stdout),dict(collection='failed',type='AssertionError'));self.assertEqual(run.stderr,b'');self.assertEqual(set(Path(name).iterdir()),before)

    def test_invalid_request_is_rejected_before_state_or_modules(self):
        q=copy.deepcopy(self.q);q['operationId']='../unsafe'
        with patch.object(io,'registry_snapshot') as registry,patch.object(io,'installed_io') as provider,self.assertRaises(AssertionError):io.capture(q,self.folder)
        registry.assert_not_called();provider.assert_not_called()


if __name__=='__main__':unittest.main()
