"""Bounded request/dispatch tests and real forked Landlock/seccomp proofs.

Task codec/FDs are real; the launch manager, storage attestation and cgroup
execution in dispatch tests are explicit doubles. Native systemd is separate.
"""
import errno
import io
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import tempfile
import threading
from types import SimpleNamespace
import unittest
from unittest.mock import patch

import test_development_archive_operations as task_tests
import validate_development_archive_helper_native as native_helper

fixture = task_tests.fixture
transport = fixture.load('archive_transport_tests', 'whaledeck-development-archive-transport.py')
guard = fixture.load('archive_guard_tests', 'whaledeck-development-archive-guard.py')


@unittest.skipUnless(os.geteuid() == 0, 'Disposable root-owned journal required')
class DispatchTests(unittest.TestCase):
    setUp = task_tests.OperationTests.setUp
    write_config = task_tests.OperationTests.write_config
    write_mountinfo = task_tests.OperationTests.write_mountinfo
    enable_quota = task_tests.OperationTests.enable_quota
    stop = task_tests.OperationTests.stop
    snapshot = task_tests.OperationTests.snapshot

    def test_dispatch_once(self):
        with patch.object(transport.subprocess, 'run', return_value=subprocess.CompletedProcess([], 0)) as manager:
            self.assertEqual('Pending', transport.dispatch(self.tasks, self.base)['status'])
            self.assertEqual('Pending', transport.dispatch(self.tasks, self.base)['status'])
        self.assertEqual(1, manager.call_count)
        args, kwargs = manager.call_args
        self.assertEqual([transport.PRIVILEGED, 'development-archive-run', self.base['taskId'].replace('-', '')], args[0])
        self.assertEqual(6, kwargs['timeout']); self.assertEqual(subprocess.DEVNULL, kwargs['stdin'])

    def test_ambiguous_launch_never_repeats_and_cancel_rejects_delayed_execution(self):
        with patch.object(transport.subprocess, 'run', side_effect=subprocess.TimeoutExpired('inert manager', 6)) as manager:
            with self.assertRaisesRegex(fixture.files.Rejected, 'DISPATCH_UNCONFIRMED'):
                transport.dispatch(self.tasks, self.base)
            self.assertEqual('Pending', transport.dispatch(self.tasks, self.base)['status'])
        self.assertEqual(1, manager.call_count)
        self.assertEqual('Stopped', self.tasks.cancel(self.base)['status'])
        with patch.object(self.capture, 'plan', side_effect=AssertionError('late work')):
            self.assertEqual('Stopped', self.tasks.run(self.base)['status'])

    def test_failed_manager_keeps_dispatch_fence(self):
        with patch.object(transport.subprocess, 'run', return_value=subprocess.CompletedProcess([], 1)) as manager:
            with self.assertRaisesRegex(fixture.files.Rejected, 'DISPATCH_UNCONFIRMED'):
                transport.dispatch(self.tasks, self.base)
            self.assertEqual('Pending', transport.dispatch(self.tasks, self.base)['status'])
        self.assertEqual(1, manager.call_count)

    def test_cancel_confinement_keeps_the_task_lock_until_the_tombstone_is_committed(self):
        outcomes = []
        def concurrent_run():
            try: self.tasks.run(self.base)
            except fixture.files.Rejected as error: outcomes.append(str(error))
        def install_guard(_directories):
            thread = threading.Thread(target=concurrent_run); thread.start(); thread.join(2)
            self.assertFalse(thread.is_alive())
        path_guard = SimpleNamespace(install=install_guard)
        quota_guard = SimpleNamespace(install=lambda *_args, **_kwargs: None)
        result = transport.cancel(self.tasks, self.base, path_guard, quota_guard, None, task_tests.operations, task_tests.control)
        self.assertEqual(['WORKSPACE_STORAGE_ARCHIVE_TASK_BUSY'], outcomes)
        self.assertEqual('Stopped', result['status'])
        self.assertEqual(0, self.execution.identity_calls)
        self.assertEqual('Stopped', self.tasks.run(self.base)['status'])

    def test_bounded_stdin_rejects_duplicates_malformed_and_extra_fields(self):
        raw = self.engine.canonical(self.base)
        self.assertEqual(self.base, transport.read_request(io.BytesIO(raw), self.tasks))
        for raw in (b'', b' ' * 4097, b'null', b'{', b'{"schemaVersion":1,"schemaVersion":1}',
                    self.engine.canonical(dict(self.base, path='/untrusted'))):
            with self.subTest(raw=raw[:30]), self.assertRaises(fixture.files.Rejected):
                transport.read_request(io.BytesIO(raw), self.tasks)


class KernelGuardTests(unittest.TestCase):
    def test_runtime_caps_are_exact_and_resource_bypass_or_missing_seccomp_is_refused(self):
        def status(caps):
            return ''.join(name + ': ' + format(caps, '016x') + chr(10) for name in ('CapEff', 'CapPrm', 'CapBnd')) + 'NoNewPrivs: 1' + chr(10) + 'Seccomp: 2' + chr(10)
        allowed = (1 << 21) | (1 << 2)
        transport.attest_runtime('start', status(0)); transport.attest_runtime('run', status(allowed))
        for mode, text in (('run', status(allowed | (1 << 24))), ('start', status(allowed)),
                           ('run', status(allowed).replace('Seccomp: 2', 'Seccomp: 0')),
                           ('run', status(allowed).replace('NoNewPrivs: 1', 'NoNewPrivs: 0')),
                           ('run', status(allowed) + 'CapEff: 0000000000200004' + chr(10))):
            with self.subTest(mode=mode, text=text), self.assertRaisesRegex(RuntimeError, 'ISOLATION_UNCONFIRMED'):
                transport.attest_runtime(mode, text)

    def test_native_fixture_rewrite_never_rewrites_an_inserted_path_or_disk_root(self):
        text = '/var/lib/whaledeck-development /var/lib/whaledeck-development-disks /data/DevSpace/file'
        actual = native_helper.isolate_text(text, {
            '/var/lib/whaledeck-development': '/var/lib/whaledeck-development-disks/qa/base',
            '/data/DevSpace': '/run/qa/hdd'})
        self.assertEqual('/var/lib/whaledeck-development-disks/qa/base /var/lib/whaledeck-development-disks /run/qa/hdd/file', actual)

    def test_actual_landlock_confines_new_file_and_truncate_unlink_rename_hardlink(self):
        with tempfile.TemporaryDirectory() as home:
            root = Path(home); allowed = root / 'allowed'; allowed.mkdir()
            source = root / 'source'; source.write_bytes(b'unchanged')
            code = '''
import errno,importlib.util,json,os,pathlib,subprocess,sys
spec=importlib.util.spec_from_file_location('guard',sys.argv[1]);guard=importlib.util.module_from_spec(spec);spec.loader.exec_module(guard)
allowed=pathlib.Path(sys.argv[2]);source=pathlib.Path(sys.argv[3]);fd=os.open(allowed,os.O_RDONLY|os.O_DIRECTORY);sink=os.open('/dev/null',os.O_RDWR|os.O_CLOEXEC)
guard.install([fd]);os.close(fd)
(allowed/'owned').write_bytes(b'allowed');assert source.read_bytes()==b'unchanged'
os.symlink(source,allowed/'alias')
checks=[]
for action in (lambda:source.write_bytes(b'bad'),lambda:source.unlink(),lambda:source.rename(allowed/'moved'),lambda:os.link(source,allowed/'hardlink'),lambda:(source.parent/'outside').write_bytes(b'bad'),lambda:(allowed/'alias').write_bytes(b'bad')):
    try:action();raise AssertionError('Source mutation allowed')
    except OSError as error:assert error.errno in (errno.EACCES,errno.EXDEV,errno.EPERM);checks.append(error.errno)
assert subprocess.run(['/usr/bin/true'],stdin=sink,stdout=sink,stderr=sink,timeout=3).returncode==0;os.close(sink)
print(json.dumps(checks))
'''
            result = subprocess.run([sys.executable, '-B', '-c', code, str(Path(guard.__file__)), str(allowed), str(source)],
                check=True, capture_output=True, text=True, timeout=10)
            self.assertEqual(6, len(json.loads(result.stdout)))
            self.assertEqual(b'unchanged', source.read_bytes())
            self.assertEqual(b'allowed', (allowed / 'owned').read_bytes())

    def test_actual_archive_quota_guard_allows_getflags_and_denies_setflags_and_execution(self):
        code = '''
import errno,fcntl,importlib.util,os,sys,tempfile
def load(name):
    spec=importlib.util.spec_from_file_location(name,sys.argv[1]+'/'+name);m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m);return m
quota=load('whaledeck-development-project-quota.py');guard=load('whaledeck-development-quota-guard.py')
with tempfile.TemporaryFile() as f:
    guard.install(quota,read_only=True,archive_flags=True)
    flags=fcntl.ioctl(f.fileno(),0x80086601,bytes(8))
    try:fcntl.ioctl(f.fileno(),0x40086602,flags);raise AssertionError('SETFLAGS allowed')
    except OSError as error:assert error.errno==errno.EPERM
    try:os.execv('/usr/bin/true',['true']);raise AssertionError('Execution allowed')
    except OSError as error:assert error.errno==errno.EPERM
print('read-only-archive-ioctl-and-exec-guard-passed')
'''
        result = subprocess.run([sys.executable, '-B', '-c', code, str(Path(guard.__file__).parent)],
            check=True, capture_output=True, text=True, timeout=10)
        self.assertIn('read-only-archive-ioctl-and-exec-guard-passed', result.stdout)


if __name__ == '__main__':
    unittest.main()
