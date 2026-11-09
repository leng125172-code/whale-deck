import hashlib
import importlib.util
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch


def load(filename):
    path = Path(__file__).resolve().parents[1] / 'host' / filename
    spec = importlib.util.spec_from_file_location(filename[:-3], path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


unpack = load('whaledeck-development-unpack.py')
isolation = load('whaledeck-development-runner-isolation.py')


class UnpackTests(unittest.TestCase):
    def test_sandbox_is_distinct_from_the_application_profile(self):
        config = unpack.configuration(isolation, '/private/staged', '/private/result')
        self.assertTrue(config['process']['noNewPrivileges'])
        self.assertTrue(config['root']['readonly'])
        self.assertEqual('/umoci', config['process']['args'][0])
        self.assertIn({'type': 'network'}, config['linux']['namespaces'])
        mounts = {mount['destination']: mount['options'] for mount in config['mounts']}
        self.assertIn('ro', mounts['/image'])
        self.assertIn('noexec', mounts['/output'])
        rules = config['linux']['seccomp']['syscalls']
        allowed = {name for rule in rules if rule['action'] == 'SCMP_ACT_ALLOW' for name in rule['names']}
        self.assertTrue({'fchown', 'lsetxattr'} <= allowed)
        self.assertFalse({'socket', 'socketpair', 'mknod', 'mknodat', 'mount', 'ioctl'} & allowed)
        self.assertNotIn('fchown', isolation.seccomp_profile()['syscalls'][0]['names'])

    @unittest.skipUnless(os.geteuid() == 0, 'Needs root or a private user namespace')
    def test_binary_identity_is_required_before_copy(self):
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory) / 'candidate'
            target = Path(directory) / 'umoci'
            payload = b'fixture bytes'
            source.write_bytes(payload)
            source.chmod(0o755)
            digest = hashlib.sha256(payload).hexdigest()
            with patch.object(unpack, 'UMOCI_PATH', source):
                with self.assertRaisesRegex(unpack.UnpackError, 'TOOL_DIGEST'):
                    unpack.copy_tool(target, '0' * 64)
                self.assertFalse(target.exists())
                unpack.copy_tool(target, digest)
                self.assertEqual(payload, target.read_bytes())
                self.assertEqual(0o500, target.stat().st_mode & 0o777)

    @unittest.skipUnless(os.geteuid() == 0, 'Needs root or a private user namespace')
    def test_install_atomically_replaces_the_previous_executable(self):
        with tempfile.TemporaryDirectory() as directory:
            parent = Path(directory)
            target = parent / 'umoci'
            target.write_bytes(b'old')
            with patch.object(unpack, 'UMOCI_PATH', target), target.open('rb') as previous:
                unpack.install_artifact(b'new')
                self.assertEqual(b'old', previous.read())
                self.assertEqual(b'new', target.read_bytes())
                self.assertEqual(0o755, target.stat().st_mode & 0o777)
                self.assertEqual([target], list(parent.iterdir()))

    def test_runtime_timeout_attempts_cleanup_of_only_its_container(self):
        with tempfile.TemporaryDirectory() as directory:
            extraction = Path(directory)
            timeout = subprocess.TimeoutExpired('runc', unpack.UNPACK_TIMEOUT)
            with patch.object(unpack.subprocess, 'run', side_effect=[timeout, None]) as execute:
                with self.assertRaisesRegex(unpack.UnpackError, 'UNPACK_FAILED'):
                    unpack.runtime(extraction, extraction)
            start, cleanup = (call.args[0] for call in execute.call_args_list)
            self.assertEqual(start[:3], cleanup[:3])
            self.assertEqual(['delete', '--force', start[-1]], cleanup[3:])
            self.assertEqual('/usr/bin/runc', start[0])

    def test_cleanup_failure_is_reported_separately(self):
        with tempfile.TemporaryDirectory() as directory:
            extraction = Path(directory)
            denied = subprocess.CalledProcessError(1, 'runc')
            with patch.object(unpack.subprocess, 'run', side_effect=[denied, denied]):
                with self.assertRaisesRegex(unpack.UnpackError, 'CLEANUP_UNCONFIRMED'):
                    unpack.runtime(extraction, extraction)
            self.assertTrue((extraction / unpack.STATE).is_dir())


if __name__ == '__main__':
    unittest.main()
