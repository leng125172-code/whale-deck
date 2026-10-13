"""Pure identity checks; no Docker, root helper, process signal or remote writes."""
import importlib.util
import unittest
import tempfile
from pathlib import Path
from unittest.mock import patch

SOURCE = Path(__file__).resolve().parents[1] / 'deploy/validation/kill_bound_backup.py'
SPEC = importlib.util.spec_from_file_location('capsule', SOURCE)
capsule = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(capsule)


class CapsuleTests(unittest.TestCase):
    def setUp(self):
        self.slug = 'wdv-backup-' + 'a' * 12
        self.job = 'b' * 32
        self.cgroup = '0::/system.slice/whaledeck-application-backup-' + self.job + '.service\n'
        self.fields = ['/usr/bin/python3', '/usr/local/libexec/whaledeck-application-backup.py',
                       'run', self.slug, self.job, 'quiesced', 'c' * 64, '90']

    def check(self, **changes):
        values = dict(cgroup=self.cgroup, command=('\0'.join(self.fields) + '\0').encode(),
                      start='12345', expected_start='12345', slug=self.slug, job=self.job)
        values.update(changes)
        return capsule.matches_process(**values)

    def test_exact_identity(self):
        self.assertTrue(self.check())

    def test_reused_pid(self):
        self.assertFalse(self.check(start='12346'))

    def test_other_unit_or_suffix(self):
        self.assertFalse(self.check(cgroup='0::/system.slice/whaledeck-agent.service'))
        self.assertFalse(self.check(cgroup=self.cgroup.strip() + '/other'))

    def test_non_temporary_application(self):
        self.assertFalse(self.check(slug='production'))

    def test_wrong_job_or_helper_or_mode(self):
        for index, value in [(1, '/tmp/helper.py'), (4, 'd' * 32), (5, 'live'), (6, 'invalid'), (7, '100')]:
            fields = self.fields.copy()
            fields[index] = value
            self.assertFalse(self.check(command=('\0'.join(fields) + '\0').encode()))

    def test_no_shell_or_extra_arguments(self):
        self.assertFalse(self.check(command=b'/bin/sh\0-c\0kill 1\0'))
        self.assertFalse(self.check(command=('\0'.join(self.fields + ['extra']) + '\0').encode()))

    def exercise_handle(self, valid):
        with tempfile.TemporaryDirectory(prefix='wd-capsule-unit-') as temporary:
            root = Path(temporary)
            process = root / '456'
            process.mkdir()
            (process / 'stat').write_text('456 (helper) ' + ' '.join(['S'] + ['0'] * 18 + ['12345']))
            (process / 'cgroup').write_text(self.cgroup if valid else '0::/system.slice/whaledeck-agent.service')
            (process / 'cmdline').write_bytes(('\0'.join(self.fields) + '\0').encode())
            with patch.object(capsule, 'Path', return_value=root), \
                 patch.object(capsule.os, 'geteuid', return_value=0, create=True), \
                 patch.object(capsule.os, 'pidfd_open', return_value=17, create=True) as opened, \
                 patch.object(capsule.os, 'close') as closed, \
                 patch.object(capsule.signal, 'SIGKILL', 9, create=True), \
                 patch.object(capsule.signal, 'pidfd_send_signal', create=True) as sent:
                if valid:
                    capsule.kill_bound_process(456, '12345', self.slug, self.job)
                    sent.assert_called_once_with(17, capsule.signal.SIGKILL, None, 0)
                else:
                    with self.assertRaises(RuntimeError):
                        capsule.kill_bound_process(456, '12345', self.slug, self.job)
                    sent.assert_not_called()
                opened.assert_called_once_with(456, 0)
                closed.assert_called_once_with(17)

    def test_valid_signal_uses_opened_handle_not_numeric_pid(self):
        self.exercise_handle(True)

    def test_failed_identity_sends_no_signal_and_closes_handle(self):
        self.exercise_handle(False)


if __name__ == '__main__':
    unittest.main()
