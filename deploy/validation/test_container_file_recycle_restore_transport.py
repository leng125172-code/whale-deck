"""Fixed restore transport against real temporary I/O; namespace/HDD explicit doubles."""
import contextlib
import os
from pathlib import Path
import subprocess
import unittest
from unittest.mock import patch
import test_container_file_recycle_restores as fixtures
import test_container_file_staging as staging

transport=staging.load('whaledeck-container-recycle-restore-transport.py','recycle_restore_transport_tests')


class RestoreTransportBoundaries(unittest.TestCase):
    def test_fixed_modules_actions_and_no_private_error_details(self):
        with self.assertRaisesRegex(transport.Rejected,'MODULE_UNSAFE'):transport.load('../arbitrary.py')
        with self.assertRaisesRegex(transport.Rejected,'ACTION_INVALID'):transport.execute('restore',{},None,None,None,None)
        for code in ('CONTAINER_RECYCLE_RESTORE_NOT_FOUND','CONTAINER_RECYCLE_RESTORE_SOURCE_BUSY','CONTAINER_RECYCLE_RESTORE_TARGET_EXISTS','CONTAINER_RECYCLE_RESTORE_STATE_UNSAFE'):
            self.assertEqual(transport.error_code(RuntimeError(code)),code)
        for error in (RuntimeError('/private secret'),FileNotFoundError('missing socket')):
            self.assertEqual(transport.error_code(error),'CONTAINER_RECYCLE_RESTORE_UNAVAILABLE')

    def test_installer_and_scope_are_fixed_without_extra_capabilities(self):
        root=Path(__file__).resolve().parents[1]/'host'
        for name in ('whaledeck-privileged','install.sh','validate.sh'):subprocess.run(['bash','-n',str(root/name)],check=True,capture_output=True)
        shell=(root/'whaledeck-privileged').read_text();cases=[]
        for action in ('container-file-recycle-restores','container-file-recycle-restores-stop'):
            start=shell.index('  '+action+')\n');end=shell.index('\n    ;;',start)+len('\n    ;;');cases.append(shell[start:end])
        scope='\n'.join(cases)
        self.assertNotIn('  container-file-recycle-archives)',scope)
        for required in ('whaledeck-container-recycle-restores-$3','KillMode=control-group','RestrictAddressFamilies=AF_UNIX','CapabilityBoundingSet=CAP_DAC_OVERRIDE CAP_DAC_READ_SEARCH CAP_FOWNER CAP_SYS_PTRACE','recycle_restore_instance','"$3"'):
            self.assertIn(required,scope)
        self.assertNotIn('CAP_CHOWN',scope);self.assertNotIn('CAP_SYS_ADMIN',scope);self.assertNotIn('"$2" == restore',scope)
        for name in ('install.sh','validate.sh'):self.assertIn('/usr/local/libexec/whaledeck-container-recycle-restore-transport.py',(root/name).read_text())


@unittest.skipUnless(os.name=='posix' and os.geteuid()==0,'temporary Linux root required')
class LinuxRestoreTransportTests(unittest.TestCase):
    def setUp(self):
        self.case=fixtures.LinuxContainerRecycleRestoreTests();self.case.setUp();self.addCleanup(self.case.doCleanups)
        self.r,self.f,self.engine=self.case.base.r,self.case.base.f,self.case.engine
        changed=patch.object(self.r,'ContainerRoot',side_effect=lambda *_:contextlib.nullcontext(self.case.session));changed.start();self.addCleanup(changed.stop)
    def execute(self,action,value):return transport.execute(action,value,self.r,self.f,None,self.engine)

    def test_plan_apply_inspect_discard_keep_new_journal_and_original_source(self):
        q=self.case.request();self.assertEqual(self.execute('plan',q['plan']['command']),q['plan'])
        result=self.execute('apply',q);self.assertEqual(result['state'],'Completed');self.assertEqual(result['operationId'],q['operationId']);self.assertEqual(result['sourceOperationId'],self.case.ref['operationId'])
        self.assertEqual(self.execute('inspect',q)['state'],'Completed');self.assertEqual(self.execute('discard',q)['state'],'Completed');self.assertEqual(self.case.base.leaf.read_bytes(),self.case.base.content)

    def test_target_disappearance_does_not_guess_a_restore_success_or_resume(self):
        q=self.case.request()
        for code in ('CONTAINER_FILES_TARGET_MISSING','CONTAINER_FILES_INSTANCE_CHANGED','CONTAINER_FILES_ROOT_CHANGED','CONTAINER_NOT_RUNNING'):
            error=self.r.Rejected(code) if code!='CONTAINER_NOT_RUNNING' else ValueError(code)
            with patch.object(self.r,'ContainerRoot',side_effect=error):
                with self.assertRaisesRegex(self.r.Rejected,'NOT_FOUND'):self.execute('inspect',q)
                result=self.execute('discard',q);self.assertEqual(result['state'],'Aborted');self.assertFalse(result['restoredByThisOperation'])
            self.assertEqual(self.execute('apply',q)['state'],'Aborted');self.assertFalse(self.case.base.leaf.exists())
            q=self.case.request()

    def test_generic_outages_fail_closed_without_writing_a_journal(self):
        q=self.case.request()
        for error in (FileNotFoundError('socket'),ConnectionError('docker'),PermissionError('registry')):
            with patch.object(self.r,'ContainerRoot',side_effect=error):
                for action in ('apply','inspect','discard'):
                    with self.assertRaises(type(error)):self.execute(action,q)
        self.assertEqual(list(self.case.state.glob('*.json')),[]);self.assertFalse(self.case.base.leaf.exists())

    def test_extra_input_cannot_select_host_paths_or_another_original_record(self):
        q=self.case.request()
        with self.assertRaises(self.r.Rejected):self.execute('apply',{**q,'hostPath':'/etc'})
        self.assertEqual(list(self.case.state.glob('*.json')),[]);self.assertEqual(self.case.retained().read_bytes(),self.case.base.content)


if __name__=='__main__':unittest.main()
