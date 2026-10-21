"""Validator regression tests; mocks do not establish installed helper proof."""
import importlib.util
import io
from pathlib import Path
import subprocess
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

spec = importlib.util.spec_from_file_location('container_file_probe', Path(__file__).with_name('validate_container_file_reads.py'))
probe = importlib.util.module_from_spec(spec); spec.loader.exec_module(probe)
INVOCATION = 'a' * 32


class ProbeTests(unittest.TestCase):
    def properties(self):
        return {'InvocationID': INVOCATION, 'User': 'root', 'Group': 'root', 'NoNewPrivileges': 'yes',
            'ProtectSystem': 'strict', 'ProtectHome': 'yes', 'PrivateTmp': 'yes', 'PrivateDevices': 'yes',
            'ProtectKernelTunables': 'yes', 'ProtectKernelModules': 'yes', 'ProtectControlGroups': 'yes',
            'KillMode': 'control-group', 'MemoryMax': str(128*1024**2), 'TasksMax': '8', 'LimitNOFILE': '64',
            'LimitFSIZE': str(1024**3), 'ReadWritePaths': '/var/lib/whaledeck-container-files',
            'CapabilityBoundingSet': 'cap_sys_ptrace cap_dac_read_search', 'RestrictAddressFamilies': 'AF_UNIX'}

    def test_sandbox_requires_every_exact_property(self):
        properties = self.properties(); probe.assert_helper_sandbox(properties, INVOCATION)
        for name in properties:
            with self.subTest(name=name), self.assertRaises(AssertionError):
                probe.assert_helper_sandbox({**properties, name: 'unexpected'}, INVOCATION)
        for name, value in (('CapabilityBoundingSet', 'cap_sys_ptrace cap_dac_read_search cap_sys_admin'),
                ('RestrictAddressFamilies', 'AF_UNIX AF_INET'), ('ReadWritePaths', '/var/lib/whaledeck-container-files /data')):
            with self.subTest(name=name), self.assertRaises(AssertionError):
                probe.assert_helper_sandbox({**properties, name: value}, INVOCATION)

    def test_partial_ready_is_still_bounded(self):
        stream = Mock(); stream.fileno.return_value = 42
        with patch.object(probe.time, 'monotonic', side_effect=[0, 0, 11]), \
             patch.object(probe.select, 'select', return_value=([stream], [], [])), \
             patch.object(probe.os, 'read', return_value=b'{'), self.assertRaisesRegex(AssertionError, 'ready-timeout'):
            probe.helper_ready(stream)

    def test_ready_reads_no_bytes_from_following_response(self):
        stream = Mock(); stream.fileno.return_value = 42
        wire = ('{"type":"driver","invocationId":"' + INVOCATION + '"}\n').encode()
        with patch.object(probe.select, 'select', return_value=([stream], [], [])), \
             patch.object(probe.os, 'read', side_effect=[bytes([value]) for value in wire]) as reading:
            self.assertEqual(INVOCATION, probe.helper_ready(stream)); self.assertEqual(len(wire), reading.call_count)

    def test_eof_and_unknown_ready_refuse_before_body(self):
        stream = Mock()
        for wire in (b'', b'{"type":"wrong"}\n', b'{"type":"driver","invocationId":"other"}\n'):
            with self.subTest(wire=wire), patch.object(probe.select, 'select', return_value=([stream], [], [])), \
                 patch.object(probe.os, 'read', side_effect=[bytes([value]) for value in wire] if wire else [b'']), \
                 self.assertRaises(AssertionError): probe.helper_ready(stream)

    def process(self):
        return Mock(stdin=io.BytesIO(), stdout=io.BytesIO(), stderr=io.BytesIO(), poll=Mock(return_value=None))

    def test_cleanup_uses_exact_invocation_and_always_closes_streams(self):
        process = self.process()
        with patch.object(probe.subprocess, 'run', return_value=SimpleNamespace(returncode=0)) as stop:
            probe.close_helper(process, ['helper'], 'b'*32, INVOCATION)
        self.assertEqual(['helper', 'container-file-transport-stop', 'b'*32, INVOCATION], stop.call_args.args[0])
        self.assertTrue(all(stream.closed for stream in (process.stdin, process.stdout, process.stderr)))
        process.wait.assert_called_once_with(timeout=15); process.kill.assert_not_called()

    def test_stop_failure_does_not_skip_bounded_wait_or_stream_cleanup(self):
        for failure in (SimpleNamespace(returncode=2), subprocess.TimeoutExpired('helper', 10)):
            process = self.process()
            behavior = {'side_effect': failure} if isinstance(failure, Exception) else {'return_value': failure}
            with patch.object(probe.subprocess, 'run', **behavior), self.assertRaisesRegex(AssertionError, 'cleanup-failed'):
                probe.close_helper(process, ['helper'], 'b'*32, INVOCATION)
            process.wait.assert_called_once_with(timeout=15)
            self.assertTrue(all(stream.closed for stream in (process.stdin, process.stdout, process.stderr)))

    def test_missing_ready_closes_input_without_any_stop_target(self):
        process = self.process()
        with patch.object(probe.subprocess, 'run') as stop:
            probe.close_helper(process, ['helper'], 'b'*32, None)
        stop.assert_not_called(); self.assertTrue(process.stdin.closed)

    def test_hung_caller_kills_only_owned_popen_and_reports_failed_cleanup(self):
        process = self.process(); process.wait.side_effect = [subprocess.TimeoutExpired('caller', 15), 0]
        with patch.object(probe.subprocess, 'run', return_value=SimpleNamespace(returncode=0)), \
             self.assertRaisesRegex(AssertionError, 'cleanup-failed'):
            probe.close_helper(process, ['helper'], 'b'*32, INVOCATION)
        process.kill.assert_called_once_with(); self.assertEqual(2, process.wait.call_count)
        self.assertTrue(all(stream.closed for stream in (process.stdin, process.stdout, process.stderr)))


if __name__ == '__main__': unittest.main()
