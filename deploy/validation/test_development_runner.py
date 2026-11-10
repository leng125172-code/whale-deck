import hashlib
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
            with patch.object(runner.subprocess, 'run', side_effect=[timeout, None]) as run:
                self.assertEqual(('TimedOut', None), runner.execute(Path(folder), identifier, run_id, 30))
            start, delete = (call.args[0] for call in run.call_args_list)
            name = f'whaledeck-dev-{identifier.replace("-", "")}-{run_id}'
            self.assertEqual(name, start[-1])
            self.assertEqual(start[:3] + ['delete', '--force', name], delete)

    def test_runtime_signal_or_uncertain_cleanup_never_returns_a_result(self):
        for first, second in ((subprocess.CompletedProcess('runc', -9), None),
                              (subprocess.TimeoutExpired('runc', 30), subprocess.CalledProcessError(1, 'runc'))):
            with tempfile.TemporaryDirectory() as folder, patch.object(runner.subprocess, 'run', side_effect=[first, second]):
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
