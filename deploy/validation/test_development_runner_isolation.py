import errno
import importlib.util
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

path = Path(__file__).resolve().parents[1] / 'host' / 'whaledeck-development-runner-isolation.py'
spec = importlib.util.spec_from_file_location('runner_isolation', path)
isolation = importlib.util.module_from_spec(spec)
spec.loader.exec_module(isolation)
files_spec = importlib.util.spec_from_file_location('runner_test_files', path.with_name('whaledeck-files.py'))
files = importlib.util.module_from_spec(files_spec)
files_spec.loader.exec_module(files)


class RunnerIsolationTests(unittest.TestCase):
    def test_profile_blocks_quota_mutation_and_namespace_creation(self):
        profile = isolation.seccomp_profile()
        self.assertEqual('SCMP_ACT_ERRNO', profile['defaultAction'])
        self.assertEqual(errno.EPERM, profile['defaultErrnoRet'])
        rules = profile['syscalls']
        permitted = {name for rule in rules if rule['action'] == 'SCMP_ACT_ALLOW' for name in rule['names']}
        self.assertFalse(permitted & {
            'file_setattr', 'quotactl', 'quotactl_fd', 'io_uring_setup', 'io_uring_enter', 'io_uring_register',
            'mount', 'move_mount', 'open_tree', 'unshare', 'setns', 'bpf', 'ptrace'
        })
        for rule in rules:
            if 'ioctl' in rule['names']:
                self.assertEqual(1, rule['args'][0]['index'])
                self.assertEqual('SCMP_CMP_EQ', rule['args'][0]['op'])
                self.assertNotIn(rule['args'][0]['value'], (0x401c5820, 0x40086602, 0x40046602))
        clone = next(rule for rule in rules if 'clone' in rule['names'])
        self.assertEqual({'index': 0, 'value': 0x7e020080, 'valueTwo': 0, 'op': 'SCMP_CMP_MASKED_EQ'}, clone['args'][0])
        clone3 = next(rule for rule in rules if 'clone3' in rule['names'])
        self.assertEqual(errno.ENOSYS, clone3['errnoRet'])
        self.assertEqual([{'index': 1, 'value': 0, 'op': 'SCMP_CMP_EQ'}],
                         next(rule['args'] for rule in rules if 'sched_setscheduler' in rule['names']))
        for name, index in (('mknod', 1), ('mknodat', 2)):
            self.assertEqual([{'index': index, 'value': 0o170000, 'valueTwo': 0o010000, 'op': 'SCMP_CMP_MASKED_EQ'}],
                             next(rule['args'] for rule in rules if name in rule['names']))

    @unittest.skipUnless(os.geteuid() == 0, 'Requires root or an isolated one-UID user namespace')
    def test_real_private_projection_holds_inode_and_excludes_nested_mount(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source, bundle, unrelated = (root / name for name in ('data', 'bundle', 'unrelated'))
            for item in (source, bundle, unrelated):
                item.mkdir(mode=0o700)
            (source / 'sentinel').write_text('pinned')
            (source / 'nested').mkdir()
            (source / 'work').mkdir(mode=0o700)
            (source / 'nested' / 'marker').write_text('unmounted')
            (unrelated / 'marker').write_text('foreign mount')
            caller = os.stat('/proc/self/ns/mnt')
            pid = os.fork()
            if pid == 0:
                try:
                    self.check_projection(root, source, bundle, unrelated, caller)
                except BaseException:
                    import traceback
                    traceback.print_exc()
                    os._exit(1)
                os._exit(0)
            _, status = os.waitpid(pid, 0)
            self.assertEqual(0, os.waitstatus_to_exitcode(status))
            self.assertTrue(os.path.samestat(caller, os.stat('/proc/self/ns/mnt')))
            self.assertEqual([], list((bundle / 'workspace').iterdir()))
            self.assertEqual('unmounted', (root / 'original' / 'nested' / 'marker').read_text())

    def check_projection(self, root, source, bundle, unrelated, caller):
        namespace = isolation.RunnerMountNamespace()
        self.assertFalse(os.path.samestat(caller, namespace.namespace))
        subprocess.run(['mount', '--bind', unrelated, source / 'nested'], check=True, timeout=5)
        fd = os.open(source, isolation.DIR)
        bundle_fd = os.open(bundle, isolation.DIR)
        try:
            info = os.fstat(fd)
            with self.assertRaisesRegex(PermissionError, 'identity'), namespace.project(fd, bundle_fd, info.st_ino + 1):
                self.fail('Unattested inode mounted')
            source.rename(root / 'original')
            source.mkdir(mode=0o700)
            (source / 'sentinel').write_text('replacement')
            with namespace.project(fd, bundle_fd, info.st_ino) as projected:
                self.assertTrue(os.path.samestat(info, os.fstat(projected)))
                view = bundle / 'workspace'
                self.assertEqual('pinned', (view / 'sentinel').read_text())
                self.assertEqual('unmounted', (view / 'nested' / 'marker').read_text())
                self.assertEqual('foreign mount', (root / 'original' / 'nested' / 'marker').read_text())
                with self.assertRaisesRegex(PermissionError, 'wrong identity'):
                    isolation.oci_config(files, bundle_fd, projected, ['/bin/probe'], 10001, 500, 128)
                for argv, uid, cpu in ((['relative'], 10001, 500), (['/bin/probe'], 0, 500), (['/bin/probe'], 10001, 0)):
                    with self.assertRaises(ValueError):
                        isolation.oci_config(files, bundle_fd, projected, argv, uid, cpu, 128)
            with self.assertRaises(FileExistsError), namespace.project(fd, bundle_fd, info.st_ino):
                self.fail('Existing projection was overwritten')
        finally:
            os.close(fd)
            os.close(bundle_fd)


if __name__ == '__main__':
    unittest.main()
