"""Real private journals/FDs; kernel quota and cgroup state are explicit doubles."""
import errno
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import threading
import unittest
import uuid
from types import SimpleNamespace
from unittest.mock import patch

import test_development_archive_operations as shared_tests
import test_development_retirement as prerequisites

fixture = shared_tests.fixture
operations = fixture.load('retirement_operations_tests', 'whaledeck-development-retirement-operations.py')
execution = fixture.load('retirement_execution_tasks_tests', 'whaledeck-development-retirement-execution.py')
transport = fixture.load('retirement_transport_tests', 'whaledeck-development-retirement-transport.py')


@unittest.skipUnless(os.geteuid() == 0, 'Requires new private root fixtures')
class RetirementTaskTests(unittest.TestCase):
    write_config = prerequisites.RetirementPlanTests.write_config
    write_mountinfo = prerequisites.RetirementPlanTests.write_mountinfo
    enable_quota = prerequisites.RetirementPlanTests.enable_quota
    stop = prerequisites.RetirementPlanTests.stop
    command = prerequisites.RetirementPlanTests.command
    entry = prerequisites.RetirementPlanTests.entry
    prepare = prerequisites.RetirementPlanTests.prepare
    snapshot = prerequisites.RetirementPlanTests.snapshot
    disk_snapshot = prerequisites.RetirementPlanTests.disk_snapshot

    def setUp(self):
        shared_tests.captures.ArchiveTests.setUp(self)
        self.planner, p, self.archive, self.capture_receipt = self.prepare()
        self.executor = execution.DevelopmentRetirementExecution(self.capture, shared_tests.captures.archives.PayloadFiles)
        self.control = shared_tests.ExecutionDouble(self.engine)
        self.control.validator = shared_tests.control.Execution(self.engine, kind='retirement')
        self.control.proof['path'] = self.control.proof['path'].replace('-archive-run-', '-retirement-run-')
        home = tempfile.TemporaryDirectory(); self.addCleanup(home.cleanup)
        self.journal = Path(home.name)
        pch = patch.object(operations, 'BASE', self.journal); pch.start(); self.addCleanup(pch.stop)
        self.tasks = operations.create(shared_tests.operations, self.engine, self.planner, self.executor, self.control)
        self.base = dict(schemaVersion=1, taskId=str(uuid.uuid4()), phase='plan',
            retirementId=str(uuid.uuid4()), prerequisite=p, retirementPlanSha256='')
        self.releases = []
        def release(parent, project, size):
            self.assertFalse(self.target.exists())
            self.assertFalse((self.root / ('.retirement-' + self.request['workspaceId']) / 'source').exists())
            self.quota.require_enforcing(parent)
            self.quota.projects.pop(project); self.releases.append(project)
        self.quota.release_empty = release

    def finish(self, value):
        self.control.alive = True
        self.assertEqual('Finishing', self.tasks.run(value)['status'])
        self.control.alive = False
        result = self.tasks.observe(value)
        self.assertEqual('Succeeded', result['status'])
        return result

    def execute_request(self):
        plan = self.finish(self.base)
        return dict(self.base, taskId=str(uuid.uuid4()), phase='execute',
            retirementPlanSha256=plan['result']['retirementPlanSha256'])

    def test_plan_and_execution_are_separate_bound_tasks_and_keep_hdd_and_budget(self):
        hdd = self.disk_snapshot(self.hdd)
        value = self.execute_request()
        result = self.finish(value)
        self.assertTrue(result['result']['sourceRemoved'])
        self.assertFalse(result['result']['releasesApplicationBudget'])
        self.assertEqual(hdd, self.disk_snapshot(self.hdd)); self.assertEqual(1, len(self.releases))
        with patch.object(self.executor, 'run', side_effect=AssertionError('no redispatch')):
            self.assertEqual(result, self.tasks.run(value))
            self.assertEqual(result, self.tasks.cancel(value))
        self.assertEqual(self.capture_receipt, self.capture.verify(self.archive, self.capture_receipt['receiptSha256']))

    def test_prestart_cancel_blocks_late_dispatch_without_source_access(self):
        value = self.execute_request()
        self.assertEqual('Stopped', self.tasks.cancel(value)['status'])
        with patch.object(self.executor, 'run', side_effect=AssertionError('late work')):
            self.assertFalse(self.tasks.admit_dispatch(value))
            self.assertEqual('Stopped', self.tasks.run(value)['status'])
        self.assertTrue(self.target.exists()); self.assertEqual([], self.releases)

    def test_ambiguous_dispatch_is_not_repeated(self):
        with patch.object(transport.subprocess, 'run', side_effect=subprocess.TimeoutExpired('manager', 6)) as manager:
            with self.assertRaisesRegex(fixture.files.Rejected, 'DISPATCH_UNCONFIRMED'):
                transport.dispatch(self.tasks, self.base)
            self.assertEqual('Pending', transport.dispatch(self.tasks, self.base)['status'])
        self.assertEqual(1, manager.call_count)
        self.assertEqual('development-retirement-run', manager.call_args.args[0][1])

    def test_immutable_payload_and_disjoint_archive_schema_refuse_before_work(self):
        self.tasks.cancel(self.base)
        invalid = [dict(self.base, phase='capture'), dict(self.base, extra=True),
            dict(self.base, schemaVersion=True), dict(self.base, taskId=self.base['retirementId']),
            dict(self.base, retirementPlanSha256='a'*64),
            dict(schemaVersion=1, taskId=str(uuid.uuid4()), archiveId=str(uuid.uuid4()),
                phase='plan', storage=self.request, expectedEpoch=2, planSha256='', receiptSha256='')]
        for value in invalid:
            with self.subTest(value=value), self.assertRaises(fixture.files.Rejected):
                self.tasks.cancel(value)
        different = dict(self.base, prerequisite=dict(self.base['prerequisite'], expectedEpoch=4))
        with self.assertRaises(fixture.files.Rejected): self.tasks.cancel(different)
        archived = shared_tests.operations.ArchiveOperations(self.engine, self.capture, self.control)
        with self.assertRaisesRegex(fixture.files.Rejected, 'REQUEST_INVALID'): archived.validate(self.base)

    def test_dead_execution_never_reexecutes_but_new_authorized_task_can_resume_engine(self):
        value = self.execute_request(); self.control.alive = True
        with patch.object(self.executor, 'remove_tree', side_effect=RuntimeError('after durable detach')):
            with self.assertRaises(RuntimeError): self.tasks.run(value)
        self.control.alive = False
        with patch.object(self.executor, 'run', side_effect=AssertionError('automatic replay')):
            self.assertEqual('Interrupted', self.tasks.run(value)['status'])
        self.assertFalse(self.target.exists())
        result = self.finish(dict(value, taskId=str(uuid.uuid4())))
        self.assertTrue(result['result']['sourceRemoved']); self.assertEqual(1, len(self.releases))

    def test_corrupt_success_bindings_boolean_numbers_and_budget_release_are_refused(self):
        value = self.execute_request(); self.finish(value)
        path = self.journal / value['taskId'] / 'result.json'
        original = json.loads(path.read_bytes())
        for change in ({'releasesApplicationBudget':True}, {'sourceRemoved':1}, {'projectId':True},
                {'workspaceId':str(uuid.uuid4())}, {'requestSha256':'f'*64}, {'extra':True}):
            altered = dict(original, result=dict(original['result'], **change))
            path.write_bytes(self.engine.canonical(altered))
            with self.subTest(change=change), self.assertRaisesRegex(fixture.files.Rejected, 'RESULT_INVALID'):
                self.tasks.observe(value)
        path.write_bytes(self.engine.canonical(original))
        self.assertEqual(original, self.tasks.observe(value))

    def test_observe_cancel_do_not_acquire_source_or_hdd_locks(self):
        value = self.execute_request(); self.control.alive = True
        with patch.object(self.executor, 'run', side_effect=RuntimeError('cut')):
            with self.assertRaises(RuntimeError): self.tasks.run(value)
        with self.engine.locked_root(self.request['rootId'], exclusive=True), self.store.locked(exclusive=True):
            self.assertEqual('Running', self.tasks.observe(value)['status'])
            self.assertEqual('Stopped', self.tasks.cancel(value)['status'])
        self.assertTrue(self.target.exists()); self.assertEqual([], self.releases)

    def test_io_failure_is_retirement_scoped_cached_and_keeps_source_and_quota(self):
        value = self.execute_request(); self.control.alive = True
        with patch.object(self.executor, 'run', side_effect=OSError(errno.EACCES, 'private path')):
            self.assertEqual('Finishing', self.tasks.run(value)['status'])
        self.control.alive = False
        result = self.tasks.observe(value)
        self.assertEqual('Rejected', result['status'])
        self.assertEqual('WORKSPACE_STORAGE_RETIREMENT_ACCESS_DENIED', result['code'])
        self.assertNotIn('private path', json.dumps(result))
        with patch.object(self.executor, 'run', side_effect=AssertionError('no automatic retry')):
            self.assertEqual(result, self.tasks.run(value))
        self.assertTrue(self.target.exists()); self.assertEqual([], self.releases)

    def test_worker_admission_waits_for_short_observation_lock_before_execution_proof(self):
        self.tasks.admit_dispatch(self.base)
        ready, contended = threading.Event(), threading.Event()
        original_flock = shared_tests.operations.fcntl.flock
        main = threading.get_ident()
        def observer():
            with self.tasks.entry(self.base):
                ready.set(); contended.wait(3)
        def flock(fd, flags):
            try:
                return original_flock(fd, flags)
            except BlockingIOError:
                if threading.get_ident() == main: contended.set()
                raise
        thread = threading.Thread(target=observer); thread.start()
        try:
            self.assertTrue(ready.wait(3))
            with patch.object(shared_tests.operations.fcntl, 'flock', side_effect=flock):
                self.assertEqual('Finishing', self.tasks.run(self.base)['status'])
        finally:
            contended.set(); thread.join(3)
        self.assertFalse(thread.is_alive()); self.assertTrue(contended.is_set())
        self.assertEqual(1, self.control.identity_calls)
        self.control.alive = False
        self.assertEqual('Succeeded', self.tasks.observe(self.base)['status'])


class FixedKindTests(unittest.TestCase):
    @unittest.skipUnless(os.geteuid() == 0, 'Installation metadata requires root')
    def test_installer_creates_only_private_journal_and_refuses_unsafe_existing_state(self):
        host = Path(transport.__file__).parent
        install = (host / 'install.sh').read_text()
        fragment = install.split('retirement_operations=', 1)[1].split('for development_module in ', 1)[0]
        fragment = 'retirement_operations=' + fragment
        with tempfile.TemporaryDirectory() as home:
            root = Path(home); journal = root / 'retirement'
            for name in ('archive', 'development'):
                (root / name).mkdir(mode=0o700)
            env = dict(os.environ, archive_operations=str(root / 'archive'), development_base=str(root / 'development'))
            script = fragment.replace('/var/lib/whaledeck-development-retirement-operations', str(journal))
            def run():
                return subprocess.run(['bash', '-euc', script], env=env, capture_output=True, timeout=5).returncode
            self.assertEqual(0, run())
            info = journal.stat()
            self.assertEqual((0, 0, 0o700), (info.st_uid, info.st_gid, info.st_mode & 0o7777))
            kept = journal / 'retained'; kept.write_bytes(b'original journal')
            self.assertEqual(0, run()); self.assertEqual(b'original journal', kept.read_bytes())
            journal.chmod(0o750); self.assertNotEqual(0, run())
            self.assertEqual(0o750, journal.stat().st_mode & 0o7777)
            journal.chmod(0o700); kept.unlink(); journal.rmdir()
            journal.symlink_to(root / 'archive', target_is_directory=True)
            self.assertNotEqual(0, run()); self.assertTrue(journal.is_symlink())
            self.assertEqual([], list((root / 'archive').iterdir()))

    @unittest.skipUnless(os.geteuid() == 0, 'Private metadata requires root')
    def test_explicit_hdd_read_only_attestation_keeps_device_checks_and_default_write_requirement(self):
        archives=shared_tests.captures.archives
        driver=fixture.storage.WorkspaceStorage(fixture.files)
        filesystem=str(uuid.uuid4())
        config=dict(mountPoint='/data/DevSpace',filesystemUuid=filesystem,
            diskImage=dict(path='/data/.whaledeck-development-disks/archive.ext4'))
        with tempfile.TemporaryDirectory() as home,driver.absolute(Path(home)) as fd, \
                patch.object(driver,'mount_entry',return_value=('1','0','7:1','/','/data/DevSpace')), \
                patch.object(driver,'device_records',return_value=['E:ID_FS_UUID='+filesystem,'E:ID_FS_USAGE=filesystem']), \
                patch.object(driver,'require_reserved_image') as physical, \
                patch.object(archives.os,'fstatvfs',return_value=SimpleNamespace(f_flag=os.ST_RDONLY)) as status:
            readonly=archives.HddArchiveStorage(driver,read_only=True)
            readonly.attest(fd,config);self.assertEqual(1,physical.call_count)
            with self.assertRaisesRegex(fixture.files.Rejected,'READ_ONLY'):
                archives.HddArchiveStorage(driver).attest(fd,config)
            status.return_value=SimpleNamespace(f_flag=0)
            with self.assertRaisesRegex(fixture.files.Rejected,'READ_ONLY_REQUIRED'):readonly.attest(fd,config)
            self.assertEqual(1,physical.call_count)

    def test_retirement_worker_requires_exact_caps_and_refuses_resource_bypass(self):
        def status(caps):
            return ''.join(name+': '+format(caps, '016x')+chr(10) for name in ('CapEff','CapPrm','CapBnd'))+'NoNewPrivs: 1'+chr(10)+'Seccomp: 2'+chr(10)
        allowed=(1<<21)|(1<<1)
        transport.attest_runtime('run',status(allowed));transport.attest_runtime('observe',status(0))
        for text in (status(allowed|(1<<24)),status((1<<21)|(1<<2)),
                     status(allowed).replace('NoNewPrivs: 1','NoNewPrivs: 0')):
            with self.assertRaisesRegex(RuntimeError,'ISOLATION_UNCONFIRMED'):transport.attest_runtime('run',text)

    def test_actual_retirement_seccomp_permits_quota_limit_call_but_denies_tag_changes_and_exec(self):
        script = '''
import errno,fcntl,importlib.util,os,sys,tempfile
def load(name):
 s=importlib.util.spec_from_file_location(name,sys.argv[1]+'/'+name);m=importlib.util.module_from_spec(s);s.loader.exec_module(m);return m
q=load('whaledeck-development-project-quota.py');g=load('whaledeck-development-quota-guard.py')
with tempfile.TemporaryFile() as f:
 g.install(q,release_only=True,archive_flags=True)
 fcntl.ioctl(f.fileno(),0x80086601,bytes(8))
 for action in (lambda:fcntl.ioctl(f.fileno(),q.FSSETXATTR,bytes(28)),lambda:fcntl.ioctl(f.fileno(),0x40086602,bytes(8)),lambda:os.execv('/usr/bin/true',['true'])):
  try:action();raise AssertionError('Forbidden mutation allowed')
  except OSError as e:assert e.errno==errno.EPERM
 try:q.quota_call(-1,q.Q_SETQUOTA,1,bytearray(q.QUOTA.size));raise AssertionError('Invalid descriptor accepted')
 except OSError as e:assert e.errno==errno.EBADF,e
print('retirement-quota-kernel-filter-passed')
'''
        result=subprocess.run([sys.executable,'-B','-W','error','-c',script,str(Path(transport.__file__).parent)],capture_output=True,text=True,timeout=10)
        self.assertEqual(0,result.returncode,result.stderr)
        self.assertIn('retirement-quota-kernel-filter-passed',result.stdout)

    def test_cgroup_kinds_never_accept_each_others_execution_proof(self):
        driver = fixture.storage.WorkspaceStorage(fixture.files)
        for kind in ('archive', 'retirement'):
            control = shared_tests.control.Execution(driver, kind=kind)
            proof = dict(bootId=str(uuid.uuid4()), device=1, inode=1,
                path='/system.slice/whaledeck-development-'+kind+'-run-'+uuid.uuid4().hex+'.service')
            control.validate(proof)
            other = 'retirement' if kind == 'archive' else 'archive'
            with self.assertRaises(fixture.files.Rejected):
                shared_tests.control.Execution(driver, kind=other).validate(proof)
        with self.assertRaises(ValueError): shared_tests.control.Execution(driver, kind='../any')


if __name__ == '__main__': unittest.main()
