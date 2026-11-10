import hashlib
import errno
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch


def load(filename):
    path = Path(__file__).resolve().parents[1] / 'host' / filename
    spec = importlib.util.spec_from_file_location(filename[:-3], path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


runner = load('whaledeck-development-runner.py')
rootfs = load('whaledeck-development-rootfs.py')
isolation = load('whaledeck-development-runner-isolation.py')
storage = load('whaledeck-development-storage.py')
files = load('whaledeck-files.py')
control = load('whaledeck-development-runner-control.py')


class RunnerTests(unittest.TestCase):
    def test_private_diagnostics_are_bound_to_original_run_and_never_overwrite(self):
        diagnostics = runner.ExecutionDiagnostics()
        diagnostics.consume(b'not-a-real-secret: sample-value; error CS0103: source text')
        binding = {'runId': '1' * 32, 'requestSha256': 'a' * 64}
        with tempfile.TemporaryDirectory() as folder:
            fd = os.open(folder, os.O_RDONLY | os.O_DIRECTORY)
            try:
                self.assertTrue(runner.persist_diagnostics(rootfs, fd, binding, 'Failed', 1, diagnostics))
                path = Path(folder) / runner.DIAGNOSTICS
                raw = path.read_bytes()
                value = json.loads(raw)
                self.assertEqual(binding['requestSha256'], value['requestSha256'])
                self.assertEqual(['CS0103'], value['untrustedErrorCodes'])
                self.assertEqual(0o600, path.stat().st_mode & 0o777)
                self.assertNotIn(b'sample-value', raw)
                self.assertFalse(runner.persist_diagnostics(rootfs, fd, binding, 'Succeeded', 0, diagnostics))
                self.assertEqual(raw, path.read_bytes())
                with patch.object(rootfs, 'write_metadata', side_effect=OSError(errno.EDQUOT, 'quota')):
                    self.assertFalse(runner.persist_diagnostics(rootfs, fd, binding, 'Failed', 1, diagnostics))
            finally:
                os.close(fd)

    def monitored(self, code, diagnostics, deadline=5):
        return runner.monitored_process([sys.executable, '-c', code], deadline, diagnostics, None,
            {'stdin': subprocess.DEVNULL, 'env': {'PATH': '/usr/bin:/bin', 'LANG': 'C'}})

    def test_real_output_flood_is_drained_with_bounded_summary_and_no_arbitrary_text(self):
        diagnostics = runner.ExecutionDiagnostics()
        code = (r'import os; os.write(1,b"private-key-value\nerror MSB3021: Permission denied\n");'
                'os.write(2,b"x"*(3*1024*1024));raise SystemExit(1)')
        result = self.monitored(code, diagnostics)
        summary = diagnostics.summary()
        self.assertEqual(1, result.returncode)
        self.assertGreater(summary['outputBytes'], 3 * 1024 * 1024)
        self.assertEqual(runner.DIAGNOSTIC_SCAN_LIMIT, summary['scannedBytes'])
        self.assertTrue(summary['scanLimited'])
        self.assertEqual(['MSB3021'], summary['untrustedErrorCodes'])
        self.assertEqual(['PermissionDenied'], summary['untrustedCategories'])
        self.assertNotIn('private-key-value', json.dumps(summary))
        self.assertLess(len(json.dumps(summary)), 4096)
        self.assertLessEqual(len(diagnostics.tail), 96)

    def test_split_errors_and_many_codes_remain_bounded(self):
        diagnostics = runner.ExecutionDiagnostics()
        diagnostics.consume(b'error MS')
        diagnostics.consume(b'B3021: No space left on device')
        diagnostics.consume(b' '.join(('error CS' + str(number)).encode() for number in range(1000, 1200)))
        summary = diagnostics.summary()
        self.assertIn('MSB3021', summary['untrustedErrorCodes'])
        self.assertEqual(32, len(summary['untrustedErrorCodes']))
        self.assertTrue(summary['errorCodesLimited'])
        self.assertEqual(['NoSpace'], summary['untrustedCategories'])

    def test_real_child_timeout_kills_and_reaps_the_launcher(self):
        with tempfile.TemporaryDirectory() as folder:
            pidfile = Path(folder) / 'pid'
            code = f'import os,time;open({str(pidfile)!r},"w").write(str(os.getpid()));time.sleep(10)'
            with self.assertRaises(subprocess.TimeoutExpired):
                self.monitored(code, runner.ExecutionDiagnostics(), deadline=0.5)
            pid = int(pidfile.read_text())
            with self.assertRaises(ProcessLookupError):
                os.kill(pid, 0)

    def test_resource_maxima_are_allowlisted_and_missing_samples_are_not_zero(self):
        diagnostics = runner.ExecutionDiagnostics()
        with tempfile.TemporaryDirectory() as folder:
            group = Path(folder)
            diagnostics.sample(group)
            self.assertEqual({}, diagnostics.summary()['resourceMaxima'])
            self.assertEqual(0, diagnostics.summary()['resourceSamples'])
            (group / 'memory.events').write_text('oom 2\nsecret 900\noom_kill 1\n')
            (group / 'memory.peak').write_text('123456')
            (group / 'pids.events').write_text('max 3\n')
            diagnostics.sample(group)
            (group / 'memory.peak').write_text('120000')
            diagnostics.sample(group)
            self.assertEqual({'memory.events:oom': 2, 'memory.events:oom_kill': 1,
                              'memory.peak:bytes': 123456, 'pids.events:max': 3}, diagnostics.summary()['resourceMaxima'])

    def test_diagnostic_sampling_cannot_select_an_arbitrary_cgroup(self):
        with tempfile.TemporaryDirectory() as folder, patch.object(runner, 'monitored_process') as process:
            with self.assertRaisesRegex(runner.RunError, 'DELEGATION_UNAVAILABLE'):
                runner.execute(Path(folder), self.request['storage']['workspaceId'], self.request['runId'],
                               1, cgroup_parent='/../../etc')
            process.assert_not_called()

    def test_dotnet_report_recognizes_restore_and_legacy_receipts(self):
        current = ['/usr/share/dotnet/dotnet', 'test', '--results-directory', runner.REPORT_SLOT,
                   '--logger', 'trx;LogFileName=tests.trx']
        legacy = current[:2] + ['--no-restore'] + current[2:]
        for command in (current, legacy):
            self.assertEqual(('Trx', 'tests.trx'), runner.report_spec({'runtime': 'dotnet', 'argv': command}))
        self.assertIsNone(runner.report_spec({'runtime': 'dotnet', 'argv': current + ['--logger', 'other']}))

    def setUp(self):
        self.driver = storage.WorkspaceStorage(files)
        self.image = 'registry.example/test/sdk@sha256:' + 'a' * 64
        environment = json.dumps({'version': 1, 'runtimes': [
            {'key': 'node', 'version': '22', 'image': self.image},
        ]}, separators=(',', ':'))
        self.request = {
            'runId': '1' * 32, 'storage': {
                'schemaVersion': 1, 'rootId': 'nvme', 'workspaceId': 'ae89d511-53c9-4fd7-9ea4-e7cd6a2a9d38',
                'environmentSha256': hashlib.sha256(environment.encode()).hexdigest(), 'diskMiB': 512,
            }, 'environment': environment, 'runtime': 'node', 'argv': ['/usr/local/bin/node', '--test'],
            'cpuMillis': 500, 'memoryMiB': 256, 'deadlineSeconds': 30,
        }

    def binding(self, request):
        return runner.request_binding(self.driver, isolation, rootfs, request)

    def test_request_pins_runtime_and_fingerprints_arguments_and_budgets(self):
        image, binding = self.binding(self.request)
        self.assertEqual(self.image, image)
        for change in ({'cpuMillis': 1000}, {'argv': ['/usr/local/bin/node', '--version']}, {'deadlineSeconds': 120}):
            with self.subTest(change=change):
                self.assertNotEqual(binding, self.binding(self.request | change)[1])

    def test_environment_and_process_rejections(self):
        with self.assertRaisesRegex(runner.RunError, 'ENVIRONMENT_CHANGED'):
            self.binding(self.request | {'environment': self.request['environment'] + ' '})
        with self.assertRaisesRegex(runner.RunError, 'RUNTIME_NOT_REGISTERED'):
            self.binding(self.request | {'runtime': 'dotnet'})
        with self.assertRaises(ValueError):
            self.binding(self.request | {'argv': ['relative-command']})

    @unittest.skipUnless(os.geteuid() == 0, 'Requires a privileged test fixture for workload ownership')
    def test_report_slot_refuses_indirection_and_seals_only_bounded_workload_output(self):
        with tempfile.TemporaryDirectory() as temporary:
            base = Path(temporary)
            output, run = base / 'output', base / 'run'
            output.mkdir()
            run.mkdir()
            sentinel = base / 'sentinel'
            sentinel.write_bytes(b'untouched')
            os.chown(sentinel, runner.RUN_UID, runner.RUN_UID)
            entry = output / 'tests.xml'
            with self.driver.absolute(output) as slot, self.driver.absolute(run) as destination:
                def capture():
                    return runner.seal_report(self.driver, slot, destination, ('JUnit', entry.name))

                self.assertEqual('Missing', capture()['state'])
                entry.symlink_to(sentinel)
                self.assertEqual('Rejected', capture()['state'])
                entry.unlink()
                os.link(sentinel, entry)
                self.assertEqual('Rejected', capture()['state'])
                entry.unlink()
                os.mkfifo(entry)
                self.assertEqual('Rejected', capture()['state'])
                entry.unlink()
                entry.touch()
                os.chown(entry, runner.RUN_UID, runner.RUN_UID)
                with entry.open('wb') as stream:
                    stream.truncate(runner.REPORT_LIMIT + 1)
                self.assertEqual('TooLarge', capture()['state'])
                self.assertEqual([], list(run.iterdir()))

                raw = b'<testsuite><testcase name="local"/></testsuite>'
                entry.write_bytes(raw)
                self.assertEqual({'format': 'JUnit', 'state': 'Present', 'size': len(raw),
                    'sha256': hashlib.sha256(raw).hexdigest()}, capture())
                sealed = run / runner.REPORT
                self.assertEqual([sealed], list(run.iterdir()))
                self.assertEqual(raw, sealed.read_bytes())
                self.assertEqual(0, sealed.stat().st_uid)
                self.assertEqual(0o600, sealed.stat().st_mode & 0o777)
                self.assertEqual(b'untouched', sentinel.read_bytes())

    @unittest.skipUnless(os.geteuid() == 0, 'Needs root or a private user namespace')
    def test_staging_cannot_regain_the_quota_override_after_exec(self):
        pid = os.fork()
        if pid == 0:
            try:
                runner.enforce_staging_quota()
                script = '''from pathlib import Path
for line in Path('/proc/self/status').read_text().splitlines():
    if line.startswith(('CapInh:', 'CapPrm:', 'CapEff:', 'CapBnd:', 'CapAmb:')):
        assert not int(line.split()[1], 16) & (1 << 24), line
'''
                os.execl(sys.executable, sys.executable, '-I', '-c', script)
            finally:
                os._exit(1)
        _, status = os.waitpid(pid, 0)
        self.assertEqual(0, os.waitstatus_to_exitcode(status))

    def test_timeout_cleanup_targets_the_exact_workspace_and_run(self):
        with tempfile.TemporaryDirectory() as folder:
            identifier = self.request['storage']['workspaceId']
            run_id = self.request['runId']
            timeout = subprocess.TimeoutExpired('runc', 30)
            with patch.object(runner, 'monitored_process', side_effect=timeout) as start_run, \
                    patch.object(runner.subprocess, 'run') as run:
                self.assertEqual(('TimedOut', None), runner.execute(Path(folder), identifier, run_id, 30))
            start, delete = start_run.call_args.args[0], run.call_args.args[0]
            name = f'whaledeck-dev-{identifier.replace("-", "")}-{run_id}'
            self.assertEqual(name, start[-1])
            self.assertEqual(start[:3] + ['delete', '--force', name], delete)

    def test_runtime_signal_or_uncertain_cleanup_never_returns_a_result(self):
        for first, second in ((subprocess.CompletedProcess('runc', -9), None),
                              (subprocess.TimeoutExpired('runc', 30), subprocess.CalledProcessError(1, 'runc'))):
            with tempfile.TemporaryDirectory() as folder, patch.object(runner, 'monitored_process', side_effect=
                    first if isinstance(first, BaseException) else None, return_value=first), \
                    patch.object(runner.subprocess, 'run', side_effect=second):
                with self.assertRaisesRegex(runner.RunError, 'CLEANUP_UNCONFIRMED'):
                    runner.execute(Path(folder), self.request['storage']['workspaceId'], self.request['runId'], 30)

    @unittest.skipUnless(os.geteuid() == 0, 'Needs root or a private user namespace')
    def test_reclaim_preserves_a_legacy_bundle_without_cgroup_proof(self):
        with tempfile.TemporaryDirectory() as folder:
            bundle = Path(folder) / 'bundle'
            bundle.mkdir()
            fd = os.open(folder, os.O_RDONLY | os.O_DIRECTORY)
            try:
                with self.assertRaisesRegex(runner.RunError, 'EXECUTION_PROOF_MISSING'):
                    control.settle(self.driver, fd, stop=False, refuse=runner.refuse)
                self.assertTrue(bundle.is_dir())
            finally:
                os.close(fd)

    @unittest.skipUnless(os.geteuid() == 0, 'Needs root or a private user namespace')
    def test_recreated_scope_path_never_receives_a_kill(self):
        with tempfile.TemporaryDirectory() as folder:
            group = Path(folder) / 'system.slice' / f'whaledeck-development-run-start-{"1" * 32}.service'
            group.mkdir(parents=True)
            kill = group / 'cgroup.kill'
            kill.write_bytes(b'0')
            fd = os.open(folder, os.O_RDONLY | os.O_DIRECTORY)
            try:
                rootfs.write_metadata(fd, control.EXECUTION, {'bootId': control.boot_identity(),
                    'path': '/' + str(group.relative_to(folder)), 'device': group.stat().st_dev, 'inode': group.stat().st_ino + 1})
                cancel = Path(folder) / control.CANCEL
                cancel.write_bytes(b'0')
                cancel.chmod(0o600)
                with patch.object(control, 'CGROUP_ROOT', Path(folder)), self.assertRaisesRegex(runner.RunError, 'EXECUTION_PROOF_CHANGED'):
                    control.settle(self.driver, fd, stop=True, refuse=runner.refuse)
                self.assertEqual(b'0', kill.read_bytes())
                self.assertEqual(b'0', cancel.read_bytes())
            finally:
                os.close(fd)

    def test_pinned_scope_is_not_empty_until_events_or_original_path_prove_it(self):
        with tempfile.TemporaryDirectory() as folder:
            group = Path(folder) / 'scope'
            group.mkdir()
            with self.driver.absolute(Path(folder)) as root, self.driver.opened(root, 'scope', os.O_RDONLY | os.O_DIRECTORY) as fd:
                identity = os.fstat(fd)
                self.assertFalse(control.scope_empty(self.driver, root, fd, 'scope', identity, runner.refuse))
                group.rmdir()
                self.assertTrue(control.scope_empty(self.driver, root, fd, 'scope', identity, runner.refuse))


if __name__ == '__main__':
    unittest.main()
