"""Private transport boundaries and real temporary native unlink, not installed RPC.

Docker namespace/storage attestation and due time are explicit test doubles.
Frozen aging fixture/source, configured HDD and host install are never touched.
"""
import errno
import io
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch
import test_container_file_staging as staging
import test_container_recycle_archive_retirements as native

transport=staging.load('whaledeck-container-recycle-archive-retirement-transport.py','retirement_transport_candidate')
files=staging.load('whaledeck-files.py','retirement_transport_files')


class Rejected(Exception):pass


class RetirementTransportBoundaries(unittest.TestCase):
    def setUp(self):
        self.original={'containerId':'a'*64,'instanceId':'pinned','path':'selected'}
        self.command={'archive':{'plan':{'command':{'purge':{'plan':{'command':{'source':{'plan':{'command':self.original}}}}}}}},'retainDays':30,'mode':'ArchiveData'}
        self.q={'plan':{'command':self.command}}
        self.calls=[];self.reader=SimpleNamespace(Rejected=Rejected,ContainerRoot=lambda *_:None)
        self.engine=SimpleNamespace(request=lambda _:None,preflight=SimpleNamespace(command=lambda _:None))
        for action in transport.ACTIONS:setattr(self.engine,action,lambda q,s,action=action:self.calls.append((action,q,s)))

    def test_all_five_exact_actions_bind_and_close_original_session(self):
        session=object();seen=[]
        class Manager:
            def __enter__(self):return session
            def __exit__(self,*_):seen.append('closed')
        def opened(command,*_):self.assertIs(command,self.original);return Manager()
        with patch.object(self.reader,'ContainerRoot',side_effect=opened):
            for action in transport.ACTIONS:
                request=self.command if action=='plan' else self.q
                transport.execute(action,request,self.reader,None,None,self.engine)
                self.assertEqual(self.calls[-1],(action,request,session))
        self.assertEqual(seen,['closed']*5)

    def test_validation_precedes_any_session_open(self):
        for action in transport.ACTIONS:
            hook=self.engine.preflight if action=='plan' else self.engine
            method='command' if action=='plan' else 'request'
            with self.subTest(action=action),patch.object(hook,method,side_effect=Rejected('bad request')),patch.object(self.reader,'ContainerRoot') as opened,self.assertRaises(Rejected):
                transport.execute(action,self.command if action=='plan' else self.q,self.reader,None,None,self.engine)
            opened.assert_not_called()
        self.assertFalse(self.calls)

    def test_exact_target_loss_passes_none_for_durable_native_decision(self):
        for code in ('CONTAINER_FILES_TARGET_MISSING','CONTAINER_FILES_INSTANCE_CHANGED','CONTAINER_FILES_TARGET_CHANGED','CONTAINER_FILES_ROOT_CHANGED'):
            for action in transport.ACTIONS[1:]:
                with self.subTest(code=code,action=action),patch.object(self.reader,'ContainerRoot',side_effect=Rejected(code)):
                    transport.execute(action,self.q,self.reader,None,None,self.engine)
                    self.assertEqual(self.calls[-1],(action,self.q,None))

    def test_known_unsafe_container_is_not_retargeted(self):
        for code in ('CONTAINER_PROTECTED','CONTAINER_NOT_RUNNING','CONTAINER_UNSAFE_ISOLATION','CONTAINER_UNSAFE_MOUNT'):
            with self.subTest(code=code),patch.object(self.reader,'ContainerRoot',side_effect=ValueError(code)):
                transport.execute('inspect',self.q,self.reader,None,None,self.engine);self.assertIsNone(self.calls[-1][2])

    def test_plan_target_loss_and_unrelated_failures_never_become_safe_absence(self):
        for error in (OSError(errno.EIO,'private path'),PermissionError('unexpected'),Rejected('CONTAINER_FILES_SOCKET_FAILED')):
            for action in transport.ACTIONS:
                with self.subTest(error=type(error).__name__,action=action),patch.object(self.reader,'ContainerRoot',side_effect=error),self.assertRaises(type(error)):
                    transport.execute(action,self.command if action=='plan' else self.q,self.reader,None,None,self.engine)
        with patch.object(self.reader,'ContainerRoot',side_effect=Rejected('CONTAINER_FILES_TARGET_MISSING')),self.assertRaises(Rejected):transport.execute('plan',self.command,self.reader,None,None,self.engine)
        self.assertFalse(self.calls)

    def test_exit_runs_even_if_engine_raises(self):
        closed=[]
        class Manager:
            def __enter__(self):return object()
            def __exit__(self,*_):closed.append(True)
        with patch.object(self.reader,'ContainerRoot',return_value=Manager()),patch.object(self.engine,'apply',side_effect=Rejected('unsafe')),self.assertRaises(Rejected):transport.execute('apply',self.q,self.reader,None,None,self.engine)
        self.assertEqual(closed,[True])

    def test_no_caller_actions_modules_or_paths(self):
        for action in ('restore','purge','shell','../apply','Apply','apply\n'):
            with self.subTest(action=action),self.assertRaises(transport.Rejected):transport.execute(action,self.q,self.reader,None,None,self.engine)
        for name in ('/etc/passwd','../whaledeck-files.py','whaledeck-tree-recycling.py'):
            with self.subTest(name=name),self.assertRaisesRegex(transport.Rejected,'MODULE_UNSAFE'):transport.load(name)
        self.assertFalse(self.calls)

    def test_input_exact_body_budget_matches_agent_excluding_one_lf(self):
        raw=b'{"x":"'+b'a'*(65536-8)+b'"}\n';self.assertEqual(len(raw),65537)
        self.assertEqual(len(transport.read_request(files,io.BytesIO(raw))['x']),65536-8)
        for bad in (raw[:-2]+b'a"}\n',b'{}',b'{}\r\n',b'[]\n',b'{"x":1,"x":2}\n',b'{"x":NaN}\n',b'{"x":Infinity}\n',b'{"x":1e999}\n',b'{"x":"\xff"}\n'):
            with self.subTest(raw=bad[:30]),self.assertRaises(transport.Rejected):transport.read_request(files,io.BytesIO(bad))

    def test_input_source_quota_and_timeout_have_distinct_stable_codes(self):
        for code,expected in (('FILES_INPUT_LIMIT','REQUEST_INVALID'),('FILES_INPUT_TIMEOUT','TIMEOUT')):
            source=SimpleNamespace(readline=lambda _maximum:None)
            with self.subTest(code=code),patch.object(source,'readline',side_effect=files.Rejected(code)),self.assertRaisesRegex(transport.Rejected,'RETIREMENT_'+expected):transport.read_request(files,source)

    def test_nested_retirement_plan_and_depth_are_bounded(self):
        value={'x':0}
        for _ in range(19):value={'x':value}
        raw=(json.dumps(value)+'\n').encode();self.assertEqual(transport.read_request(files,io.BytesIO(raw)),value)
        with self.assertRaisesRegex(transport.Rejected,'REQUEST_INVALID'):transport.read_request(files,io.BytesIO((json.dumps({'x':value})+'\n').encode()))

    def test_pipe_eof_commit_rejects_trailing_data_before_dispatch(self):
        for suffix in (b'x',b'\n',b'{"apply":true}'):
            read,write=os.pipe()
            try:
                os.write(write,b'{}\n'+suffix);os.close(write);write=None;source=files.InputLines(read)
                self.assertEqual(transport.read_request(files,source),{})
                with self.assertRaises(files.Rejected):source.finish()
            finally:
                os.close(read)
                if write is not None:os.close(write)

    def test_errors_are_fixed_and_ancestor_absence_stays_distinct(self):
        for code in ('CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_NOT_FOUND','CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_NOT_DUE','CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_TIMEOUT','CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_SESSION_REQUIRED','CONTAINER_RECYCLE_ARCHIVE_RETENTION_COPY_CHANGED','CONTAINER_RECYCLE_ARCHIVE_SOURCE_NOT_FOUND','FILES_BACKUP_NOT_HDD'):
            self.assertEqual(transport.error_code(Rejected(code)),code)
        for error in (Rejected('/private/path and secret'),OSError(errno.EIO,'/private/path'),KeyError('private')):
            self.assertEqual(transport.error_code(error),'CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_UNAVAILABLE')

    def test_main_requires_eof_before_dispatch_and_always_cancels_timer(self):
        for trailing in (b'',b'{}\n'):
            read,write=os.pipe();frames=[];timers=[]
            try:
                os.write(write,b'{}\n'+trailing);os.close(write);write=None
                with os.fdopen(read,'rb') as stream:
                    read=None
                    output=SimpleNamespace(InputLines=files.InputLines,unique_object=files.unique_object,Rejected=files.Rejected,emit=frames.append)
                    with patch.object(os,'geteuid',return_value=0),patch.object(sys,'argv',['driver','inspect']),patch.dict(os.environ,{'INVOCATION_ID':'a'*32}),patch.object(sys,'stdin',stream),patch.object(transport,'bootstrap',return_value=(output,None,None,None)),patch.object(transport.signal,'signal'),patch.object(transport.signal,'alarm',side_effect=timers.append),patch.object(transport,'execute',return_value={'state':'Unknown'}) as execute:
                        if trailing:
                            with self.assertRaises(files.Rejected):transport.main()
                            execute.assert_not_called()
                        else:
                            transport.main();execute.assert_called_once_with('inspect',{},None,output,None,None)
                self.assertEqual(frames[0],dict(type='driver',invocationId='a'*32));self.assertEqual(timers,[10,0] if trailing else [10,110,0])
                self.assertEqual(len(frames),1 if trailing else 2)
            finally:
                if read is not None:os.close(read)
                if write is not None:os.close(write)

    def test_bad_invocation_or_action_never_bootstraps_or_reads_stdin(self):
        for argv,invocation in ((['driver','shell'],'a'*32),(['driver','plan'],''),(['driver','apply'],'A'*32),(['driver','inspect','extra'],'a'*32)):
            with patch.object(os,'geteuid',return_value=0),patch.object(sys,'argv',argv),patch.dict(os.environ,{'INVOCATION_ID':invocation}),patch.object(transport,'bootstrap') as build,self.assertRaises(transport.Rejected):transport.main()
            build.assert_not_called()


@unittest.skipUnless(os.name=='posix' and os.geteuid()==0,'isolated Linux root required')
class RetirementTransportNative(unittest.TestCase):
    def setUp(self):
        self.f=native.ArchiveRetirementTests();self.f.setUp();self.addCleanup(self.f.doCleanups)
        self.engine=self.f.engine;self.q=self.f.q;self.session=self.f.session
        self.reader=SimpleNamespace(Rejected=Rejected)
        session=self.session
        class Manager:
            def __enter__(self):return session
            def __exit__(self,*_):pass
        self.reader.ContainerRoot=lambda *_:Manager()

    def test_actual_nested_plan_readonly_reserve_cancel_and_current_claim(self):
        before=self.f.protected();plan=transport.execute('plan',self.q['plan']['command'],self.reader,None,None,self.engine)
        self.assertEqual(plan,self.q['plan']);raw=(json.dumps(self.q)+'\n').encode();self.assertEqual(transport.read_request(files,io.BytesIO(raw)),self.q)
        self.assertEqual(transport.execute('reserve',self.q,self.reader,None,None,self.engine)['state'],'Reserved')
        self.assertEqual(transport.execute('discard',self.q,self.reader,None,None,self.engine)['state'],'Aborted')
        self.assertEqual(self.f.protected(),before);self.assertTrue(self.f.data.exists())

    def test_due_temporary_unlink_and_historical_replay_never_unlink_again(self):
        self.engine.reserve(self.q,self.session);before=self.f.protected()
        with patch.object(native.candidate.time,'time_ns',return_value=(self.f.due+1000)*1000000):
            result=transport.execute('apply',self.q,self.reader,None,None,self.engine)
        self.assertEqual(result['state'],'Completed');self.assertIs(result['retirementApplied'],True);self.assertFalse(self.f.data.exists());self.assertEqual(self.f.protected(),before)
        with patch.object(self.reader,'ContainerRoot',side_effect=Rejected('CONTAINER_FILES_TARGET_MISSING')),self.f.no_data_unlink():
            for action in transport.ACTIONS[1:]:self.assertEqual(transport.execute(action,self.q,self.reader,None,None,self.engine)['state'],'Completed')

    def test_missing_session_on_reserved_never_deletes_or_releases(self):
        self.engine.reserve(self.q,self.session);before=self.f.protected();journal=(self.engine.state_path/(self.q['operationId']+'.json')).read_bytes()
        with patch.object(self.reader,'ContainerRoot',side_effect=Rejected('CONTAINER_FILES_INSTANCE_CHANGED')),self.f.no_data_unlink():
            for action in ('apply','discard'):
                with self.subTest(action=action),self.assertRaisesRegex(self.engine.r.Rejected,'SESSION_REQUIRED'):transport.execute(action,self.q,self.reader,None,None,self.engine)
            self.assertEqual(transport.execute('inspect',self.q,self.reader,None,None,self.engine)['state'],'Reserved')
        self.assertEqual(journal,(self.engine.state_path/(self.q['operationId']+'.json')).read_bytes());self.assertEqual(before,self.f.protected());self.assertTrue(self.f.data.exists())

    def test_real_cli_invalid_invocation_never_emits_ready_or_reads_body(self):
        for action,invocation,suffix in (('shell','a'*32,'ACTION_INVALID'),('apply','','INVOCATION_INVALID'),('plan','A'*32,'INVOCATION_INVALID')):
            run=subprocess.run([sys.executable,'-I',transport.__file__,action],input=b'{"untrustedOperation":true}\n',env=dict(PATH=os.defpath,INVOCATION_ID=invocation),capture_output=True,timeout=5)
            self.assertEqual(run.returncode,1);self.assertEqual(run.stderr,b'')
            self.assertEqual(json.loads(run.stdout),dict(type='error',code='CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_'+suffix))

    def test_module_symlink_hardlink_writable_and_nonroot_are_rejected(self):
        with tempfile.TemporaryDirectory() as name:
            root=Path(name);p=root/'whaledeck-files.py';p.write_text('VALUE=1\n');p.chmod(0o600)
            with patch.object(transport,'__file__',str(root/'transport.py')):
                self.assertEqual(transport.load(p.name).VALUE,1)
                p.chmod(0o666)
                with self.assertRaises(transport.Rejected):transport.load(p.name)
                p.chmod(0o600);os.link(p,root/'extra')
                with self.assertRaises(transport.Rejected):transport.load(p.name)
                (root/'extra').unlink()
                os.chown(p,1000,1000)
                with self.assertRaises(transport.Rejected):transport.load(p.name)
                os.chown(p,0,0);p.unlink();p.symlink_to(root/'other')
                with self.assertRaises((transport.Rejected,FileNotFoundError)):transport.load(p.name)


if __name__=='__main__':unittest.main()
