import errno
import importlib.util
from pathlib import Path
import stat
import struct
import subprocess
import sys
from types import SimpleNamespace
import unittest
from unittest.mock import patch


path = Path(__file__).resolve().parents[1] / 'host' / 'whaledeck-development-project-quota.py'
spec = importlib.util.spec_from_file_location('workspace_project_quota', path)
quota = importlib.util.module_from_spec(spec)
spec.loader.exec_module(quota)


class GuardTests(unittest.TestCase):
    def test_kernel_filter_denies_other_ioctls_quota_commands_and_exec(self):
        script = """
import errno, fcntl, importlib.util, os, sys
from pathlib import Path
def load(name):
    spec = importlib.util.spec_from_file_location(name, Path(sys.argv[1]) / (name + '.py'))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module
quota = load('whaledeck-development-project-quota')
read_only = sys.argv[2] == 'attest'
load('whaledeck-development-quota-guard').install(quota, read_only=read_only)
fd = os.open('.', os.O_RDONLY | os.O_DIRECTORY)
def refused(action):
    try:
        action()
    except OSError as error:
        assert error.errno == errno.EPERM, error
    else:
        raise AssertionError('Forbidden operation was allowed')
refused(lambda: fcntl.ioctl(fd, 0x5413, bytes(8)))
refused(lambda: quota.quota_call(fd, 0x80000302, 0, bytearray(64)))
refused(lambda: os.execv('/bin/true', ['/bin/true']))
if read_only:
    refused(lambda: fcntl.ioctl(0x7fffffff, quota.FSSETXATTR, bytes(28)))
    refused(lambda: quota.quota_call(-1, quota.Q_SETQUOTA, 1, bytearray(72)))
assert os.fstat(fd).st_ino > 0
os.close(fd)
"""
        for mode in ('allocate', 'attest'):
            with self.subTest(mode=mode):
                result = subprocess.run([sys.executable, '-B', '-I', '-c', script, str(path.parent), mode],
                                        capture_output=True, text=True, timeout=10)
                self.assertEqual(0, result.returncode, result.stderr)


class ProjectQuotaTests(unittest.TestCase):
    def setUp(self):
        self.fd = 11
        self.project = 1400000061
        self.disk_mib = 256
        self.expected = (262144, 65536)
        self.state = (0,) * 8
        self.attrs = (0, 0, 0, 0, 0)
        self.flags = quota.PROJECT_ENFORCING
        self.writes = []
        self.cut = False
        self.contents = []
        for target, replacements in (
            (quota, {'quota_call': self.quota_call}),
            (quota.fcntl, {'ioctl': self.ioctl}),
            (quota.os, {'fstat': lambda _: SimpleNamespace(st_uid=0, st_mode=stat.S_IFDIR | 0o700),
                        'fsync': lambda _: None, 'listdir': lambda _: self.contents}),
        ):
            patcher = patch.multiple(target, **replacements)
            patcher.start()
            self.addCleanup(patcher.stop)

    def quota_call(self, fd, command, project, data):
        self.assertEqual(self.fd, fd)
        if command == quota.Q_GETSTATE:
            self.assertEqual(0, project)
            self.assertEqual(1, data[0])
            struct.pack_into('=H', data, 2, self.flags)
            return
        self.assertEqual(self.project, project)
        if command == quota.Q_GETQUOTA:
            data[:] = quota.QUOTA.pack(*self.state, quota.QIF_ALL)
            return
        self.assertEqual(quota.Q_SETQUOTA, command)
        *values, valid = quota.QUOTA.unpack(data)
        self.assertEqual(quota.QIF_LIMITS, valid)
        self.state = tuple(values)
        self.writes.append('limits')

    def ioctl(self, fd, command, data):
        self.assertEqual(self.fd, fd)
        if command == quota.FSGETXATTR:
            return quota.FILE_ATTR.pack(*self.attrs)
        self.assertEqual(quota.FSSETXATTR, command)
        if self.cut:
            # ext4 may commit the inherit flag before a failed project update.
            self.attrs = (quota.PROJINHERIT, 0, 0, 0, 0)
            raise OSError(errno.EIO, 'injected before project change')
        self.attrs = quota.FILE_ATTR.unpack(data)
        self.writes.append('attributes')

    def attach(self):
        quota.attach(self.fd, self.project, self.disk_mib)

    def test_sets_hard_blocks_and_inodes_then_verifies_exact_binding(self):
        quota.require_unused(self.fd, self.project)
        self.attach()
        self.assertEqual(self.expected, (self.state[0], self.state[3]))
        self.assertEqual((quota.PROJINHERIT, 0, 0, self.project, 0), self.attrs)
        self.assertEqual(['limits', 'attributes'], self.writes)
        self.attach()
        self.assertEqual(['limits', 'attributes'], self.writes)

    def test_replays_stopped_attach_without_overwriting_a_limit(self):
        self.cut = True
        with self.assertRaisesRegex(quota.QuotaError, 'QUOTA_ATTACH_FAILED'):
            self.attach()
        self.cut = False
        self.attach()
        self.assertEqual(['limits', 'attributes'], self.writes)

    def test_rejects_accounting_without_enforcement(self):
        self.flags = 0x10
        with self.assertRaisesRegex(quota.QuotaError, 'QUOTA_NOT_ENFORCING'):
            self.attach()
        self.assertEqual([], self.writes)

    def test_rejects_invalid_project_or_budget_before_any_kernel_query(self):
        with patch.object(quota, 'require_enforcing') as require:
            for value in (0, -1, True, 0x80000000):
                with self.subTest(project=value), self.assertRaisesRegex(quota.QuotaError, 'QUOTA_REQUEST_INVALID'):
                    quota.require_unused(self.fd, value)
            for value in (255, True, 1048577):
                with self.subTest(disk_mib=value), self.assertRaisesRegex(quota.QuotaError, 'QUOTA_REQUEST_INVALID'):
                    quota.attach(self.fd, self.project, value)
            require.assert_not_called()

    def test_rejects_existing_usage_or_different_limits(self):
        for state in ((0, 0, 4096, 0, 0, 1, 0, 0), (65536, 0, 0, 65536, 0, 0, 0, 0),
                      (262144, 0, 4096, 65536, 0, 1, 0, 0)):
            with self.subTest(state=state):
                self.state = state
                with self.assertRaisesRegex(quota.QuotaError, 'QUOTA_ID_IN_USE'):
                    quota.require_unused(self.fd, self.project)
                with self.assertRaisesRegex(quota.QuotaError, 'QUOTA_ID_IN_USE'):
                    self.attach()
        self.assertEqual([], self.writes)

    def test_rejects_prepopulated_directory_foreign_project_or_realtime(self):
        for attributes, contents in (((0, 0, 0, 0, 0), ['foreign']), ((0, 0, 0, 7, 0), []),
                                     ((quota.REALTIME_FLAGS, 0, 0, 0, 0), [])):
            with self.subTest(attributes=attributes):
                self.attrs, self.contents = attributes, contents
                with self.assertRaisesRegex(quota.QuotaError, 'QUOTA_DIRECTORY_UNSAFE'):
                    self.attach()
        self.assertEqual([], self.writes)

    def test_refuses_to_repair_drift_on_an_already_tagged_directory(self):
        self.attach()
        self.state = (0,) * 8
        with self.assertRaisesRegex(quota.QuotaError, 'QUOTA_BINDING_CHANGED'):
            self.attach()
        self.assertEqual(['limits', 'attributes'], self.writes)

    def test_release_empty_zeros_only_exact_unreferenced_limits_and_syncs(self):
        self.state = (262144, 0, 0, 65536, 0, 0, 0, 0)
        with patch.object(quota.LIBC, 'syncfs', return_value=0) as sync:
            quota.release_empty(self.fd, self.project, self.disk_mib)
            quota.release_empty(self.fd, self.project, self.disk_mib)
        self.assertEqual((0,) * 8, self.state)
        self.assertEqual(['limits'], self.writes)
        self.assertEqual(2, sync.call_count)

    def test_release_refuses_even_one_open_unlinked_inode_or_remaining_byte(self):
        for used_bytes, used_inodes in ((1, 0), (0, 1), (4096, 2)):
            self.state = (262144, 0, used_bytes, 65536, 0, used_inodes, 0, 0)
            with self.subTest(bytes=used_bytes, inodes=used_inodes), self.assertRaisesRegex(quota.QuotaError, 'QUOTA_STILL_REFERENCED'):
                quota.release_empty(self.fd, self.project, self.disk_mib)
        self.assertEqual([], self.writes)

    def test_release_refuses_foreign_limits_soft_limits_timers_and_disabled_enforcement(self):
        for state in ((65536, 0, 0, 65536, 0, 0, 0, 0),
                      (262144, 1, 0, 65536, 0, 0, 0, 0),
                      (262144, 0, 0, 65536, 0, 0, 1, 0)):
            self.state = state
            with self.subTest(state=state), self.assertRaisesRegex(quota.QuotaError, 'QUOTA_BINDING_CHANGED'):
                quota.release_empty(self.fd, self.project, self.disk_mib)
        self.flags = 0x10
        with self.assertRaisesRegex(quota.QuotaError, 'QUOTA_NOT_ENFORCING'):
            quota.release_empty(self.fd, self.project, self.disk_mib)
        self.assertEqual([], self.writes)

    def test_failed_sync_keeps_release_unknown_and_can_recheck_zero_without_rewriting(self):
        self.state = (262144, 0, 0, 65536, 0, 0, 0, 0)
        with patch.object(quota.LIBC, 'syncfs', return_value=-1):
            with self.assertRaisesRegex(quota.QuotaError, 'QUOTA_RELEASE_FAILED'):
                quota.release_empty(self.fd, self.project, self.disk_mib)
        with patch.object(quota.LIBC, 'syncfs', return_value=0):
            quota.release_empty(self.fd, self.project, self.disk_mib)
        self.assertEqual(['limits'], self.writes)


if __name__ == '__main__':
    unittest.main()
