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


class RunnerTests(unittest.TestCase):
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


if __name__ == '__main__':
    unittest.main()
