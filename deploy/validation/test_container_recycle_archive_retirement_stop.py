"""Actual owned child/pidfd signals; systemd and cgroup identity are doubles.

No host unit or non-fixture process is ever stopped. Replacement race confirms
that an old pidfd cannot signal a newly allocated process after the final check.
"""
import os
from pathlib import Path
import signal
import subprocess
import sys
import unittest
from unittest.mock import patch
import test_container_file_staging as staging

stop=staging.load('whaledeck-container-recycle-archive-retirement-stop.py','retirement_pidfd_stop_tests')
DRIVER='a'*32
INV='b'*32
CG='/system.slice/'+stop.PREFIX+DRIVER+'.service'


class RetirementStopPolicyTests(unittest.TestCase):
    def test_bad_id_never_queries_manager(self):
        for value in ('','../unit','A'*32,'0'*32):
            with self.subTest(value=value),patch.object(stop,'lookup') as lookup,self.assertRaises(stop.Rejected):stop.terminate(value,INV)
            lookup.assert_not_called()

    def test_missing_invocation_or_zero_mainpid_never_opens_a_process(self):
        for value in (('',0,''),(INV,0,'')):
            with patch.object(stop,'lookup',return_value=value),patch.object(stop,'PinnedProcess') as process:stop.terminate(DRIVER,INV)
            process.assert_not_called()

    def test_foreign_invocation_never_opens_a_process(self):
        with patch.object(stop,'lookup',return_value=('c'*32,500,CG)),patch.object(stop,'PinnedProcess') as process,self.assertRaises(stop.Rejected):stop.terminate(DRIVER,INV)
        process.assert_not_called()

    def test_changed_manager_binding_never_sends_a_signal(self):
        process=unittest.mock.Mock()
        with patch.object(stop,'lookup',side_effect=[(INV,500,CG),('c'*32,501,CG)]),patch.object(stop,'PinnedProcess',return_value=process),self.assertRaisesRegex(stop.Rejected,'BINDING_CHANGED'):stop.terminate(DRIVER,INV)
        process.send.assert_not_called();process.close.assert_called_once()

    def test_second_lookup_failure_is_not_a_successful_cancel(self):
        process=unittest.mock.Mock();process.exited.return_value=False
        with patch.object(stop,'lookup',side_effect=[(INV,500,CG),FileNotFoundError()]),patch.object(stop,'PinnedProcess',return_value=process),self.assertRaises(stop.Rejected):stop.terminate(DRIVER,INV)
        process.send.assert_not_called();process.close.assert_called_once()

    def test_lookup_is_fixed_bounded_and_rejects_malformed_properties(self):
        good=('InvocationID='+INV+'\nMainPID=500\nControlGroup='+CG+'\n').encode()
        with patch.object(stop.subprocess,'run',return_value=subprocess.CompletedProcess([],0,good,b'')) as run:
            self.assertEqual(stop.lookup(DRIVER),(INV,500,CG));args,kwargs=run.call_args
            self.assertEqual(args[0][0:3],['/usr/bin/systemctl','show',stop.PREFIX+DRIVER+'.service']);self.assertEqual(kwargs['timeout'],2);self.assertNotIn('shell',kwargs)
        for raw in (good+b'Extra=secret\n',good.replace(b'MainPID=500',b'MainPID=1'),good.replace(CG.encode(),b'/other-unit'),good.replace(INV.encode(),b''),good+b'MainPID=600\n'):
            with self.subTest(raw=raw[:30]),patch.object(stop.subprocess,'run',return_value=subprocess.CompletedProcess([],0,raw,b'')),self.assertRaises(stop.Rejected):stop.lookup(DRIVER)


@unittest.skipUnless(os.name=='posix' and os.geteuid()==0,'isolated Linux root required')
class RetirementRealPidfdTests(unittest.TestCase):
    def child(self,ignore=False,invocation=INV):
        source="import signal,time,sys;"+("signal.signal(signal.SIGTERM,signal.SIG_IGN);" if ignore else '')+"print('ready',flush=True);time.sleep(60)"
        child=subprocess.Popen([sys.executable,'-c',source],stdout=subprocess.PIPE,stderr=subprocess.PIPE,env=dict(PATH=os.defpath,INVOCATION_ID=invocation))
        def cleanup():
            if child.poll() is None:child.kill()
            child.communicate(timeout=5)
        self.addCleanup(cleanup);self.assertEqual(child.stdout.readline(),b'ready\n');return child

    def cgroup_double(self):
        actual=stop.PinnedProcess.read
        def read(process,name,limit):return bytearray(('0::'+CG+'\n').encode()) if name=='cgroup' else actual(process,name,limit)
        return patch.object(stop.PinnedProcess,'read',read)

    def test_real_owned_main_exits_via_its_pidfd(self):
        child=self.child()
        with self.cgroup_double(),patch.object(stop,'lookup',return_value=(INV,child.pid,CG)):stop.terminate(DRIVER,INV)
        self.assertEqual(child.wait(timeout=5),-signal.SIGTERM)

    def test_term_ignoring_owned_main_is_killed_via_same_pidfd(self):
        child=self.child(True)
        with self.cgroup_double(),patch.object(stop,'lookup',return_value=(INV,child.pid,CG)):stop.terminate(DRIVER,INV)
        self.assertEqual(child.wait(timeout=5),-signal.SIGKILL)

    def test_replacement_after_final_lookup_is_never_signaled(self):
        old=self.child();replacement=[];actual=stop.PinnedProcess.send
        def raced(process,number):
            if not replacement:
                old.kill();old.wait(timeout=5);replacement.append(self.child(invocation='c'*32))
            return actual(process,number)
        with self.cgroup_double(),patch.object(stop,'lookup',return_value=(INV,old.pid,CG)),patch.object(stop.PinnedProcess,'send',raced):stop.terminate(DRIVER,INV)
        self.assertIsNone(replacement[0].poll())

    def test_wrong_cgroup_or_initial_invocation_never_signals_owned_child(self):
        child=self.child(invocation='c'*32)
        with self.cgroup_double(),patch.object(stop,'lookup',return_value=(INV,child.pid,CG)),self.assertRaises(stop.Rejected):stop.terminate(DRIVER,INV)
        self.assertIsNone(child.poll())
        with patch.object(stop,'lookup',return_value=('c'*32,child.pid,CG)),self.assertRaises(stop.Rejected):stop.terminate(DRIVER,'c'*32)
        self.assertIsNone(child.poll())

    def test_duplicate_invocation_environment_is_rejected_and_buffer_zeroed(self):
        child=self.child();actual=stop.PinnedProcess.read;environment=bytearray(('INVOCATION_ID='+INV+'\0INVOCATION_ID=c\0').encode())
        def read(process,name,limit):
            if name=='environ':return environment
            if name=='cgroup':return bytearray(('0::'+CG+'\n').encode())
            return actual(process,name,limit)
        with patch.object(stop.PinnedProcess,'read',read),patch.object(stop,'lookup',return_value=(INV,child.pid,CG)),self.assertRaises(stop.Rejected):stop.terminate(DRIVER,INV)
        self.assertEqual(environment,bytearray(len(environment)));self.assertIsNone(child.poll())


if __name__=='__main__':unittest.main()
