"""Private permission transport seams with real Linux metadata, synthetic disk attestation.

This is not installed signed-RPC or human business/UI acceptance.
"""
import contextlib
import errno
import json
import os
import stat
import signal
import unittest
from unittest.mock import patch
import test_container_file_recursive_acl as fixtures
import test_container_file_staging as common
transport = common.load('whaledeck-container-recursive-acl-transport.py', 'recursive_acl_transport_test')

class RecursiveAclErrorTests(unittest.TestCase):
    def test_only_exact_private_receipt_absence_means_not_found(self):
        for code in ('CONTAINER_RECURSIVE_ACL_NOT_FOUND', 'CONTAINER_FILES_TARGET_MISSING', 'CONTAINER_RECURSIVE_ACL_HISTORY_CHANGED'):
            self.assertEqual(code, transport.error_code(RuntimeError(code)))
        for error in (FileNotFoundError(errno.ENOENT, 'HDD/socket missing'), RuntimeError('/private password'), RuntimeError('CONTAINER_RECURSIVE_ACL_NOT_FOUND private')):
            self.assertEqual('CONTAINER_RECURSIVE_ACL_UNAVAILABLE', transport.error_code(error))
    def test_single_entry_error_is_bounded_under_recursive_domain(self):
        self.assertEqual('CONTAINER_RECURSIVE_ACL_HISTORY_CHANGED',transport.error_code(RuntimeError('CONTAINER_ACL_HISTORY_CHANGED')))
        self.assertEqual('CONTAINER_RECURSIVE_ACL_UNAVAILABLE',transport.error_code(RuntimeError('CONTAINER_ACL_secret /private')))
    def test_unsupported_acl_dependency_is_explicit_and_unknown_io_remains_unavailable(self):
        self.assertEqual('CONTAINER_RECURSIVE_ACL_UNSUPPORTED',transport.error_code(OSError(errno.ENOTSUP,'private')))
        self.assertEqual('CONTAINER_RECURSIVE_ACL_UNAVAILABLE',transport.error_code(OSError(errno.EIO,'private')))
    def test_readonly_source_and_hdd_failure_are_not_aliased_to_success(self):
        self.assertEqual('CONTAINER_RECURSIVE_ACL_PROTECTED', transport.error_code(PermissionError(errno.EACCES, 'private')))
        self.assertEqual('CONTAINER_RECURSIVE_ACL_CAPACITY_LOW', transport.error_code(OSError(errno.ENOSPC, 'private')))

@unittest.skipUnless(os.name == 'posix' and os.geteuid() == 0, 'isolated Linux root required')
class RecursiveAclTransportTests(unittest.TestCase):
    def setUp(self):
        self.fixture = fixtures.RecursiveAclTests(); self.fixture.setUp(); self.addCleanup(self.fixture.doCleanups)
        self.engine, self.r, self.f, self.session = self.fixture.engine, self.fixture.base.r, self.fixture.base.f, self.fixture.base.session
        changed = patch.object(self.r, 'ContainerRoot', side_effect=lambda *_: contextlib.nullcontext(self.session))
        changed.start(); self.addCleanup(changed.stop)
    def execute(self, action, request):
        return transport.execute(action, request, self.r, self.f, None, self.engine)
    def test_full_plan_apply_inspect_discard_keep_original_metadata_and_bytes(self):
        request = self.fixture.request(); plan = self.execute('plan', request['plan']['command']); self.assertEqual(request['plan'], plan)
        result = self.execute('apply', request)
        self.assertEqual(('Completed', True, True), (result['state'], result['bindingCurrent'], result['historyVerified']))
        record = json.loads((self.fixture.state/(request['operationId']+'.json')).read_bytes())
        self.assertEqual(self.fixture.content, (self.fixture.tree/'one.bin').read_bytes())
        for action in ('inspect', 'discard', 'apply'):
            replay = self.execute(action, request); self.assertEqual(replay['replayed'], action!='discard'); self.assertEqual(result['outcomeDigest'], replay['outcomeDigest'])
            self.assertEqual(record, json.loads((self.fixture.state/(request['operationId']+'.json')).read_bytes()))
    def test_missing_or_restarted_target_apply_only_observes_original_receipt(self):
        request = self.fixture.request(); result = self.execute('apply', request); saved = json.loads((self.fixture.state/(request['operationId']+'.json')).read_bytes())
        for code in ('CONTAINER_FILES_TARGET_MISSING', 'CONTAINER_FILES_INSTANCE_CHANGED', 'CONTAINER_FILES_TARGET_CHANGED', 'CONTAINER_FILES_ROOT_CHANGED'):
            with patch.object(self.r, 'ContainerRoot', side_effect=self.r.Rejected(code)):
                for action in ('apply', 'inspect', 'discard'):
                    replay = self.execute(action, request)
                    self.assertEqual(('Completed', False, result['outcomeDigest']), (replay['state'], replay['bindingCurrent'], replay['outcomeDigest']))
                    self.assertEqual(saved, json.loads((self.fixture.state/(request['operationId']+'.json')).read_bytes()))
    def test_target_loss_discard_fence_prevents_delayed_chown_and_hdd_write(self):
        request = self.fixture.request(); before = self.r.fingerprint((self.fixture.tree/'one.bin').stat())
        with patch.object(self.r, 'ContainerRoot', side_effect=self.r.Rejected('CONTAINER_FILES_TARGET_MISSING')):
            self.assertEqual('Aborted', self.execute('discard', request)['state'])
        self.assertEqual('Aborted', self.execute('apply', request)['state'])
        self.assertEqual(before, self.r.fingerprint((self.fixture.tree/'one.bin').stat())); self.assertFalse((self.fixture.base.disk.target/fixtures.module.NAMESPACE).exists())
    def test_missing_target_new_apply_does_not_create_a_receipt_or_touch_owner(self):
        request = self.fixture.request(); before = self.r.fingerprint((self.fixture.tree/'one.bin').stat())
        with patch.object(self.r, 'ContainerRoot', side_effect=self.r.Rejected('CONTAINER_FILES_TARGET_MISSING')):
            with self.assertRaisesRegex(self.r.Rejected, 'SESSION_REQUIRED'): self.execute('apply', request)
        self.assertEqual(before, self.r.fingerprint((self.fixture.tree/'one.bin').stat()))
        self.assertEqual([], list(self.fixture.state.glob('*.json'))); self.assertFalse((self.fixture.base.disk.target/fixtures.module.NAMESPACE).exists())
    def test_generic_docker_storage_and_registry_failures_cannot_be_tombstoned(self):
        request = self.fixture.request()
        for error in (ConnectionError('docker unavailable'), FileNotFoundError('missing HDD/socket'), PermissionError('REGISTRY_UNSAFE')):
            with patch.object(self.r, 'ContainerRoot', side_effect=error):
                for action in ('apply', 'inspect', 'discard'):
                    with self.assertRaises(type(error)): self.execute(action, request)
        self.assertEqual([], list(self.fixture.state.glob('*.json')))
    def test_missing_original_receipt_is_not_target_absence(self):
        request = self.fixture.request()
        with patch.object(self.r, 'ContainerRoot', side_effect=self.r.Rejected('CONTAINER_FILES_TARGET_MISSING')):
            with self.assertRaisesRegex(self.r.Rejected, 'CONTAINER_RECURSIVE_ACL_NOT_FOUND'): self.execute('inspect', request)
    def test_real_sigterm_observes_partial_and_does_not_continue_remaining_tree(self):
        leaf=self.fixture.tree/'sub/中文.bin';leaf.chmod(0o644);request=self.fixture.request()
        pid=os.fork()
        if pid==0:
            transport.bind_cancellation(self.engine)
            self.engine.checkpoint=lambda phase,index:os.kill(os.getpid(),signal.SIGTERM) if phase=='AfterEntry' and index==0 else None
            try:self.engine.apply(request,self.session)
            except self.r.Rejected as error:os._exit(0 if str(error)=='CONTAINER_RECURSIVE_ACL_CANCELED' else 2)
            except BaseException:os._exit(3)
            os._exit(4)
        _,status=os.waitpid(pid,0);self.assertTrue(os.WIFEXITED(status));self.assertEqual(os.WEXITSTATUS(status),0)
        after=self.fixture.snapshot()
        result=self.execute('inspect',request);replay=self.execute('apply',request)
        self.assertEqual((result['state'],result['changedCount'],result['notStartedCount']),('Partial',1,3))
        self.assertEqual(replay['state'],'Partial');self.assertEqual(after,self.fixture.snapshot())
        self.assertEqual((self.fixture.tree/'one.bin').stat().st_mode&0o777,0o640)

    def test_unknown_action_extra_fields_and_unsafe_modules_refused(self):
        with self.assertRaisesRegex(transport.Rejected, 'ACTION_INVALID'): self.execute('shell', {})
        with self.assertRaises(self.r.Rejected): self.execute('apply', {**self.fixture.request(), 'hostPath': '/etc'})
        with self.assertRaisesRegex(transport.Rejected, 'MODULE_UNSAFE'): transport.load('not-an-approved-module.py')

if __name__ == '__main__': unittest.main()


@unittest.skipUnless(os.name=='posix' and os.geteuid()==0,'isolated Linux root required')
class RecursiveAclMappingTests(unittest.TestCase):
    def setUp(self):
        self.fixture=fixtures.RecursiveAclTests();self.fixture.setUp();self.addCleanup(self.fixture.doCleanups)
        self.base=common.load('whaledeck-container-acl-transport.py','recursive_acl_base_mapping')
        cls=transport.scoped_recursive_acl_type(self.fixture.type,self.base)
        self.engine=cls(self.fixture.base.uploads,self.fixture.base.publisher,self.fixture.state,self.fixture.history)
        self.r,self.session=self.fixture.base.r,self.fixture.base.session

    def request(self):
        return dict(operationId='f'*32,owner='B'*64,plan=self.engine.plan(self.fixture.command(),self.session))

    def test_each_real_tree_inode_and_final_confined_child_parent_have_mapping_guards(self):
        checked=set()
        def mapping(fd):
            info=os.fstat(fd);checked.add((info.st_dev,info.st_ino))
        with patch.object(self.base,'namespace_current') as namespace,patch.object(self.base,'require_unmapped',side_effect=mapping):
            request=self.request();result=self.engine.apply(request,self.session)
        self.assertEqual(result['state'],'Completed')
        self.assertGreater(namespace.call_count,4)
        for rel in self.fixture.paths:
            info=(self.fixture.tree/rel).stat();self.assertIn((info.st_dev,info.st_ino),checked)

    def test_unproved_namespace_or_fd_mapping_fails_before_any_mutation(self):
        before=self.fixture.snapshot()
        for method in ('namespace_current','require_unmapped'):
            with patch.object(self.base,'namespace_current'),patch.object(self.base,'require_unmapped'),patch.object(self.base,method,side_effect=self.base.Rejected('CONTAINER_ACL_MAPPING_UNPROVEN')):
                with self.assertRaisesRegex(self.r.Rejected,'MAPPING_UNPROVEN'):self.request()
        self.assertEqual(before,self.fixture.snapshot());self.assertFalse(list(self.fixture.state.iterdir()))

    def test_mapping_refusal_after_children_cannot_bypass_guard_for_root_last_write(self):
        refused=[False]
        def namespace(*_):
            if refused[0]:raise self.base.Rejected('CONTAINER_ACL_MAPPING_UNPROVEN')
        with patch.object(self.base,'namespace_current',side_effect=namespace),patch.object(self.base,'require_unmapped'):
            request=self.request();before=self.fixture.snapshot()['']
            self.engine.checkpoint=lambda phase,index:refused.__setitem__(0,True) if phase=='AfterEntry' and index==2 else None
            with self.assertRaisesRegex(self.r.Rejected,'MAPPING_UNPROVEN'):self.engine.apply(request,self.session)
            self.assertEqual(before,self.fixture.snapshot()[''])
            with patch.object(fixtures.single.acl,'write_acl',side_effect=AssertionError('must not resume')):
                result=self.engine.apply(request,self.session)
            self.assertEqual(result['state'],'Unknown');self.assertEqual(result['unknownCount'],4)


if __name__=='__main__':unittest.main()
