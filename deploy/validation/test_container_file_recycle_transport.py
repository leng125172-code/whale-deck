"""Private transport with real temporary I/O; explicit namespace/HDD doubles, not RPC."""
import contextlib
import errno
import os
from pathlib import Path
import subprocess
import unittest
from unittest.mock import patch
import test_container_file_recycling as fixtures
import test_container_file_staging as common

transport=common.load('whaledeck-container-recycle-transport.py','recycle_transport_tests')


class RecycleTransportBoundaryTests(unittest.TestCase):
    def test_unknown_exception_details_are_never_protocol_codes(self):
        for error in (RuntimeError('/private secret'),FileNotFoundError('HDD/socket missing'),RuntimeError('CONTAINER_RECYCLE_NOT_FOUND extra')):
            self.assertEqual(transport.error_code(error),'CONTAINER_RECYCLE_UNAVAILABLE')
        for code in ('CONTAINER_RECYCLE_NOT_FOUND','CONTAINER_RECYCLE_RESTORE_UNPROVEN','CONTAINER_RECYCLE_RESTORE_CLAIMED','FILES_BACKUP_HDD_REQUIRED','FILES_BACKUP_CAPACITY_REQUIRED'):
            self.assertEqual(transport.error_code(RuntimeError(code)),code)
        self.assertEqual(transport.error_code(OSError(errno.EEXIST,'private path')),'CONTAINER_RECYCLE_TARGET_EXISTS')

    def test_modules_actions_and_installer_shells_are_fixed(self):
        with self.assertRaisesRegex(transport.Rejected,'MODULE_UNSAFE'):transport.load('../arbitrary.py')
        with self.assertRaisesRegex(transport.Rejected,'ACTION_INVALID'):transport.execute('shell',{},None,None,None,None)
        host=Path(__file__).resolve().parents[1]/'host'
        for name in ('whaledeck-privileged','install.sh','validate.sh'):subprocess.run(['bash','-n',str(host/name)],check=True,capture_output=True)
        shell=(host/'whaledeck-privileged').read_text();start=shell.index('  container-file-recycling)');end=shell.index('  container-file-permissions)',start);block=shell[start:end]
        self.assertIn('"$2" == restore',block);self.assertIn('RestrictAddressFamilies=AF_UNIX',block);self.assertIn('KillMode=control-group',block)
        self.assertIn('recycle_instance',block);self.assertIn('"$3"',block);self.assertNotIn('CAP_SYS_ADMIN',block);self.assertNotIn('CAP_CHOWN',block)
        self.assertIn('/var/lib/whaledeck-container-recycling',(host/'install.sh').read_text())
        self.assertIn('assert_metadata /var/lib/whaledeck-container-recycling',(host/'validate.sh').read_text())
        self.assertIn('/var/lib/whaledeck-container-recycle-restores/claims',(host/'install.sh').read_text())
        self.assertIn('/usr/local/libexec/whaledeck-container-recycle-restores.py',(host/'install.sh').read_text())
        self.assertIn('/usr/local/libexec/whaledeck-container-recycle-restores.py',(host/'validate.sh').read_text())
        self.assertIn('/var/lib/whaledeck-container-recycle-restores/claims',(host/'validate.sh').read_text())
        self.assertIn("restore_claims_path=Path('/var/lib/whaledeck-container-recycle-restores/claims')",(host/'whaledeck-container-recycle-transport.py').read_text())


@unittest.skipUnless(os.name=='posix' and os.geteuid()==0,'temporary Linux root required')
class LinuxRecycleTransportTests(unittest.TestCase):
    def setUp(self):
        self.case=fixtures.LinuxContainerRecycleTests();self.case.setUp();self.addCleanup(self.case.doCleanups)
        self.r,self.f,self.engine= self.case.r,self.case.f,self.case.engine
        changed=patch.object(self.r,'ContainerRoot',side_effect=lambda *_:contextlib.nullcontext(self.case.session));changed.start();self.addCleanup(changed.stop)

    def execute(self, action, request):return transport.execute(action,request,self.r,self.f,None,self.engine)

    def test_plan_apply_restore_inspect_and_discard_observe_same_original(self):
        q=self.case.request();self.assertEqual(self.execute('plan',q['plan']['command']),q['plan'])
        self.assertEqual(self.execute('apply',q)['state'],'Trashed');self.assertFalse(self.case.leaf.exists())
        self.assertEqual(self.execute('discard',q)['state'],'Trashed')
        self.assertEqual(self.execute('restore',q)['state'],'Restored');self.assertEqual(self.case.leaf.read_bytes(),self.case.content)
        self.assertEqual(self.execute('inspect',q)['state'],'Restored')

    def test_authoritative_target_loss_does_not_erase_an_original_trash_receipt(self):
        q=self.case.request();self.execute('apply',q)
        for code in ('CONTAINER_FILES_TARGET_MISSING','CONTAINER_FILES_INSTANCE_CHANGED','CONTAINER_FILES_TARGET_CHANGED','CONTAINER_FILES_ROOT_CHANGED'):
            with patch.object(self.r,'ContainerRoot',side_effect=self.r.Rejected(code)):
                for action in ('apply','inspect','discard'):
                    r=self.execute(action,q);self.assertEqual(r['state'],'Trashed');self.assertFalse(r['bindingCurrent'])
                with self.assertRaisesRegex(self.r.Rejected,'SESSION_REQUIRED'):self.execute('restore',q)
        self.assertEqual(self.case.retained(q).read_bytes(),self.case.content)

    def test_pre_apply_discard_fences_late_writes_even_when_target_disappeared(self):
        q=self.case.request();before=self.r.fingerprint(self.case.leaf.stat())
        with patch.object(self.r,'ContainerRoot',side_effect=self.r.Rejected('CONTAINER_FILES_TARGET_MISSING')):
            self.assertEqual(self.execute('discard',q)['state'],'Aborted')
        self.assertEqual(self.execute('apply',q)['state'],'Aborted');self.assertEqual(self.r.fingerprint(self.case.leaf.stat()),before)
        self.assertFalse(self.case.target.exists())

    def test_missing_receipt_and_generic_outages_are_not_authoritative_no_write_facts(self):
        q=self.case.request()
        with patch.object(self.r,'ContainerRoot',side_effect=self.r.Rejected('CONTAINER_FILES_TARGET_MISSING')):
            with self.assertRaisesRegex(self.r.Rejected,'NOT_FOUND'):self.execute('inspect',q)
            with self.assertRaisesRegex(self.r.Rejected,'SESSION_REQUIRED'):self.execute('apply',q)
        for error in (FileNotFoundError('missing socket or storage'),ConnectionError('docker unavailable'),PermissionError('registry unsafe')):
            with patch.object(self.r,'ContainerRoot',side_effect=error):
                for action in ('apply','inspect','discard','restore'):
                    with self.assertRaises(type(error)):self.execute(action,q)
        self.assertEqual(list(self.case.state.glob('*.json')),[]);self.assertFalse(self.case.target.exists())

    def test_extra_request_fields_cannot_become_root_paths(self):
        q=self.case.request()
        with self.assertRaises(self.r.Rejected):self.execute('apply',{**q,'hostPath':'/etc'})
        self.assertTrue(self.case.leaf.exists());self.assertFalse(self.case.target.exists())

    def test_missing_root_with_retained_history_is_unknown_not_not_found(self):
        q=self.case.request();self.execute('apply',q);(self.case.state/(q['operationId']+'.json')).unlink()
        with patch.object(self.r,'ContainerRoot',side_effect=self.r.Rejected('CONTAINER_FILES_TARGET_MISSING')):
            r=self.execute('inspect',q)
        self.assertEqual(r['state'],'Unknown');self.assertTrue(r['historyMayExist']);self.assertIsNone(r['sourceRetained'])
        self.assertEqual(self.case.retained(q).read_bytes(),self.case.content)


if __name__=='__main__':unittest.main()
