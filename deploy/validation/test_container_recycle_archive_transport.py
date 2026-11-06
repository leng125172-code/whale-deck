"""Transport fail-closed boundaries; explicit namespace and dispatch doubles."""
import errno
import io
import json
import os
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch
import test_container_file_staging as staging

transport=staging.load('whaledeck-container-recycle-archive-transport.py','archive_transport_tests')
files=staging.load('whaledeck-files.py','archive_transport_input_files')


class Rejected(Exception):pass


class ArchiveTransportTests(unittest.TestCase):
    def setUp(self):
        self.command={'purge':{'plan':{'command':{'source':{'plan':{'command':{'containerId':'a'*64}}}}}},'mode':'ArchiveCopy'}
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
        self.assertEqual(transport.error_code(OSError(errno.EIO,'/private/secret')),'CONTAINER_RECYCLE_ARCHIVE_UNAVAILABLE')
        for code in ('CONTAINER_RECYCLE_ARCHIVE_NOT_RESERVED','CONTAINER_RECYCLE_ARCHIVE_HISTORY_CHANGED','CONTAINER_RECYCLE_ARCHIVE_FENCE_CHANGED','CONTAINER_RECYCLE_ARCHIVE_STATE_UNSAFE','CONTAINER_RECYCLE_ARCHIVE_TIMEOUT'):
            self.assertEqual(transport.error_code(Rejected(code)),code)

    def test_source_dependency_absence_is_not_private_operation_absence(self):
        self.assertEqual(transport.error_code(Rejected('CONTAINER_RECYCLE_ARCHIVE_SOURCE_NOT_FOUND')),'CONTAINER_RECYCLE_ARCHIVE_SOURCE_NOT_FOUND')
        self.assertNotEqual(transport.error_code(Rejected('CONTAINER_RECYCLE_ARCHIVE_SOURCE_NOT_FOUND')),'CONTAINER_RECYCLE_ARCHIVE_NOT_FOUND')

    def test_plan_resolves_only_original_source_command_inside_pinned_purge(self):
        session=object()
        class Manager:
            def __enter__(self):return session
            def __exit__(self,*_):pass
        seen=[]
        with patch.object(self.reader,'ContainerRoot',side_effect=lambda command,*args:(seen.append(command),Manager())[1]):transport.execute('plan',self.command,self.reader,None,None,self.engine)
        self.assertEqual(seen,[self.command['purge']['plan']['command']['source']['plan']['command']]);self.assertEqual(self.actions,[('plan',session)])

    def test_input_preserves_exact_integer_and_rejects_duplicates_oversize_and_deep_payload(self):
        value={'plan':{'time':1722222222222222222},'name':'中文'}
        raw=(json.dumps(value,ensure_ascii=False)+'\n').encode();self.assertEqual(transport.read_request(files,io.BytesIO(raw)),value)
        for raw in (b'{"a":1,"a":2}\n',b'{"a":"'+b'x'*65536+b'"}\n'):
            with self.subTest(length=len(raw)),self.assertRaises(files.Rejected):transport.read_request(files,io.BytesIO(raw))
        value={}
        for _ in range(17):value={'a':value}
        with self.assertRaisesRegex(transport.Rejected,'REQUEST_INVALID'):transport.read_request(files,io.BytesIO((json.dumps(value)+'\n').encode()))

    def test_input_commit_requires_eof_and_rejects_extra_lines(self):
        import os
        for raw,accepted in ((b'{}\n',True),(b'{}\n{}\n',False)):
            read,write=os.pipe()
            try:
                os.write(write,raw);os.close(write);write=None;source=files.InputLines(read);self.assertEqual(transport.read_request(files,source),{})
                if accepted:source.finish()
                else:
                    with self.assertRaisesRegex(files.Rejected,'COMMIT_TRAILING_DATA'):source.finish()
            finally:
                os.close(read)
                if write is not None:os.close(write)

    def test_installation_and_stop_are_fixed_and_restore_registry_remains_readonly(self):
        host=Path(__file__).resolve().parents[1]/'host';helper=(host/'whaledeck-privileged').read_text();block=helper.split('  container-file-recycle-archives)',1)[1].split('  container-file-permissions)',1)[0]
        self.assertIn('"$2" == reserve',block);self.assertIn('"$recycle_archive_instance" == "$3"',block);self.assertIn('--property=Restart=no',block);self.assertIn('--property=KillMode=control-group',block)
        rw=next(line for line in block.splitlines() if 'ReadWritePaths=' in line)
        self.assertIn('/var/lib/whaledeck-container-recycle-archives',rw);self.assertIn('/var/lib/whaledeck-container-recycle-restores/claims',rw)
        self.assertNotIn('/var/lib/whaledeck-container-recycling',rw);self.assertNotIn('/var/lib/whaledeck-container-recycle-purges',rw);self.assertNotIn('/data/DockerData/whaledeck/file-backups',rw)
        self.assertIn('CAP_CHOWN',block);self.assertNotIn('CAP_SYS_ADMIN',block)
        install=(host/'install.sh').read_text()
        for name in ('archive-plan','archive-fences','archive-copies','archive-transport'):
            self.assertIn('install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-container-recycle-'+name+'.py"',install)


@unittest.skipUnless(os.name=='posix' and os.geteuid()==0,'isolated Linux root required')
class ArchiveKernelTransportTests(unittest.TestCase):
    def setUp(self):
        import test_container_recycle_archive_copies as native
        self.f=native.ArchiveCopyTests();self.f.setUp();self.addCleanup(self.f.doCleanups);self.engine=self.f.engine;session=self.f.session
        class Manager:
            def __enter__(self):return session
            def __exit__(self,*_):pass
        self.reader=SimpleNamespace(Rejected=self.f.source.r.Rejected,ContainerRoot=lambda *args:Manager())

    def test_five_actions_use_actual_temporary_journals_hdd_and_new_inode(self):
        q=self.f.q;plan=transport.execute('plan',q['plan']['command'],self.reader,None,None,self.engine);self.assertEqual(plan,q['plan']);before=self.f.protected()
        self.assertEqual(transport.execute('reserve',q,self.reader,None,None,self.engine)['state'],'Reserved')
        for action in ('apply','inspect','discard'):
            result=transport.execute(action,q,self.reader,None,None,self.engine);self.assertEqual(result['state'],'Completed');self.assertTrue(result['archiveCopied']);self.assertTrue(result['bindingCurrent'])
        self.assertEqual(self.f.base.leaf.read_bytes(),self.f.base.content);self.assertNotEqual(self.f.source.metadata(self.f.base.leaf.stat())[:2],q['plan']['deletedIdentity'][:2]);self.assertEqual(self.f.protected(),before)

    def test_lost_dependency_journal_is_never_operation_not_found(self):
        q=self.f.q;self.engine.reserve(q,self.f.session);ref=self.engine.ref(q)
        for path in (self.engine.source.state_path/(ref['operationId']+'.json'),self.engine.preflight.purges.state_path/(q['plan']['command']['purge']['operationId']+'.json')):
            raw=path.read_bytes();path.unlink()
            try:
                with self.subTest(journal=path.name),self.assertRaisesRegex(self.reader.Rejected,'ARCHIVE_SOURCE_NOT_FOUND'):transport.execute('inspect',q,self.reader,None,None,self.engine)
            finally:path.write_bytes(raw);path.chmod(0o600)

    def test_module_loading_requires_root_regular_single_link_bounded_code(self):
        import tempfile
        with tempfile.TemporaryDirectory(prefix='wd-archive-module-') as temporary:
            root=Path(temporary);path=root/'whaledeck-files.py';path.write_text('VALUE = 1\n');path.chmod(0o600)
            with patch.object(transport,'__file__',str(root/'transport.py')):
                self.assertEqual(transport.load(path.name).VALUE,1)
                path.chmod(0o666)
                with self.assertRaisesRegex(transport.Rejected,'MODULE_UNSAFE'):transport.load(path.name)
                path.chmod(0o600);held=root/'held.py';path.rename(held);path.symlink_to(held)
                with self.assertRaisesRegex(transport.Rejected,'MODULE_UNSAFE'):transport.load(path.name)
                path.unlink();os.link(held,path)
                with self.assertRaisesRegex(transport.Rejected,'MODULE_UNSAFE'):transport.load(path.name)
                path.unlink();held.rename(path);os.chown(path,1000,1000)
                with self.assertRaisesRegex(transport.Rejected,'MODULE_UNSAFE'):transport.load(path.name)
                os.chown(path,0,0);path.write_bytes(b'#'+b'x'*131072)
                with self.assertRaisesRegex(transport.Rejected,'MODULE_UNSAFE'):transport.load(path.name)


if __name__=='__main__':unittest.main()
