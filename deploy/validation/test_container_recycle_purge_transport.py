"""Transport fail-closed boundaries; explicit namespace and dispatch doubles."""
import errno
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch
import test_container_file_staging as staging

transport=staging.load('whaledeck-container-recycle-purge-transport.py','purge_transport_tests')


class Rejected(Exception):pass


class PurgeTransportTests(unittest.TestCase):
    def setUp(self):
        self.command={'source':{'plan':{'command':{'containerId':'a'*64}}},'scope':'Originals'}
        self.request={'plan':{'command':self.command}}
        self.actions=[]
        self.reader=SimpleNamespace(Rejected=Rejected,ContainerRoot=lambda *args:None)
        self.engine=SimpleNamespace(request=lambda q:None,preflight=SimpleNamespace(command=lambda q:None,plan=lambda q,s:self.actions.append(('plan',s))))
        for action in ('reserve','apply','inspect','discard'):
            setattr(self.engine,action,lambda q,s,action=action:self.actions.append((action,s)))

    def test_target_disappearance_never_retargets_any_mutation_or_old_observation(self):
        for code in ('CONTAINER_FILES_TARGET_MISSING','CONTAINER_FILES_INSTANCE_CHANGED','CONTAINER_FILES_TARGET_CHANGED','CONTAINER_FILES_ROOT_CHANGED'):
            for action in ('reserve','apply','inspect','discard'):
                with self.subTest(code=code,action=action),patch.object(self.reader,'ContainerRoot',side_effect=Rejected(code)):
                    transport.execute(action,self.request,self.reader,None,None,self.engine)
                    self.assertEqual(self.actions[-1],(action,None))

    def test_unrelated_socket_permission_hdd_or_unknown_errors_do_not_become_safe_absence(self):
        for error in (OSError(errno.EIO,'private path'),PermissionError('unexpected'),Rejected('CONTAINER_FILES_SOCKET_FAILED')):
            with self.subTest(type=type(error).__name__),patch.object(self.reader,'ContainerRoot',side_effect=error),self.assertRaises(type(error)):
                transport.execute('inspect',self.request,self.reader,None,None,self.engine)
        self.assertFalse(self.actions)

    def test_success_closes_original_manager_and_passes_exact_pinned_session(self):
        class Manager:
            closed=False
            def __enter__(self):return session
            def __exit__(self,*_):self.closed=True
        session=object();manager=Manager()
        with patch.object(self.reader,'ContainerRoot',return_value=manager):transport.execute('apply',self.request,self.reader,None,None,self.engine)
        self.assertEqual(self.actions,[('apply',session)]);self.assertTrue(manager.closed)

    def test_no_archive_restore_shell_or_caller_module_actions(self):
        for action in ('restore','archive','purge','shell','../apply'):
            with self.subTest(action=action),self.assertRaises(transport.Rejected):transport.execute(action,self.request,self.reader,None,None,self.engine)
        for name in ('/etc/passwd','whaledeck-tree-recycling.py','../whaledeck-files.py'):
            with self.subTest(name=name),self.assertRaises(transport.Rejected):transport.load(name)

    def test_error_projection_never_exposes_paths_or_unbounded_exception_text(self):
        self.assertEqual(transport.error_code(OSError(errno.EIO,'/private/secret')),'CONTAINER_RECYCLE_PURGE_UNAVAILABLE')
        for code in ('CONTAINER_RECYCLE_PURGE_NOT_RESERVED','CONTAINER_RECYCLE_PURGE_HISTORY_CHANGED','CONTAINER_RECYCLE_PURGE_FENCE_CHANGED','CONTAINER_RECYCLE_PURGE_STATE_UNSAFE','CONTAINER_RECYCLE_PURGE_TIMEOUT'):
            self.assertEqual(transport.error_code(Rejected(code)),code)

    def test_installation_and_stop_are_fixed_and_restore_registry_remains_readonly(self):
        host=Path(__file__).resolve().parents[1]/'host';helper=(host/'whaledeck-privileged').read_text();block=helper.split('  container-file-recycle-purges)',1)[1].split('  container-file-permissions)',1)[0]
        self.assertIn('"$2" == reserve',block);self.assertIn('"$recycle_purge_instance" == "$3"',block);self.assertIn('--property=Restart=no',block);self.assertIn('--property=KillMode=control-group',block)
        rw=next(line for line in block.splitlines() if 'ReadWritePaths=' in line)
        self.assertIn('/var/lib/whaledeck-container-recycle-purges',rw);self.assertNotIn('/var/lib/whaledeck-container-recycle-restores',rw)
        install=(host/'install.sh').read_text()
        for name in ('purge-plan','purge-fences','purges','purge-transport'):
            self.assertIn('install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-container-recycle-'+name+'.py"',install)


if __name__=='__main__':unittest.main()
