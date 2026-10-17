import importlib.util
import json
from pathlib import Path
import unittest
from unittest import mock

spec = importlib.util.spec_from_file_location('tree_rpc_validator', Path(__file__).with_name('validate_file_tree_rpc.py'))
validator = importlib.util.module_from_spec(spec)
spec.loader.exec_module(validator)
move_spec = importlib.util.spec_from_file_location('tree_move_rpc_validator', Path(__file__).with_name('validate_file_tree_move_rpc.py'))
move_validator = importlib.util.module_from_spec(move_spec)
move_spec.loader.exec_module(move_validator)


class TreeRpcDiagnosticsTests(unittest.TestCase):
    def test_real_probe_stderr_is_not_lost(self):
        value = json.dumps({'terminalProbe': 'failed', 'phase': 'tree-plan', 'fileCode': 'FILES_ROOT_FORBIDDEN',
            'rpcStatus': 'PermissionDenied', 'untrustedException': 'must-not-be-copied'})
        self.assertEqual(validator.client_failure('', value), [{'treeClientFailed': True, 'clientPhase': 'tree-plan',
            'fileCode': 'FILES_ROOT_FORBIDDEN', 'rpcStatus': 'PermissionDenied'}])

    def test_untrusted_shapes_fields_and_plain_output_are_not_exposed(self):
        lines = 'private plain text\n[]\nnull\n' + json.dumps({'terminalProbe': 'failed', 'phase': '/private/path',
            'fileCode': 'FILES_INVALID secret', 'rpcStatus': 'credential'})
        self.assertEqual(validator.client_failure(lines, ''), [{'treeClientFailed': True, 'clientPhase': None,
            'fileCode': None, 'rpcStatus': None}])

    def test_wrong_record_and_tail_are_bounded(self):
        self.assertEqual(validator.client_failure('{"phase":"tree-plan","secret":"hidden"}', ''), [])
        line = '{"terminalProbe":"failed","phase":"tree-plan"}'
        self.assertEqual(len(validator.client_failure('\n'.join([line] * 30), '')), 8)


class TreeMoveObserverTests(unittest.TestCase):
    identifier = 'a' * 64
    marker = 'wdv-agent-move-' + 'b' * 16

    def output(self, *, running=False, restarting=False, paused=False, status='exited', marker=None):
        return mock.Mock(returncode=0, stdout=json.dumps([{'Id': self.identifier,
            'Config': {'Labels': {'io.whaledeck.validation.marker': marker or self.marker}},
            'State': {'Running': running, 'Restarting': restarting, 'Paused': paused, 'Status': status}}]), stderr='')

    def test_live_restart_and_pause_are_observed_never_removed(self):
        for state in ({'running': True}, {'restarting': True}, {'paused': True}):
            with self.subTest(state=state), mock.patch.object(move_validator.subprocess, 'run', return_value=self.output(**state)), mock.patch.object(move_validator, 'call') as remove:
                self.assertEqual(move_validator.observe_owned_containers([self.identifier], self.marker), [self.identifier])
                remove.assert_not_called()

    def test_only_owned_terminal_container_is_removed_without_force(self):
        for state in ('created', 'exited', 'dead'):
            with self.subTest(state=state), mock.patch.object(move_validator.subprocess, 'run', return_value=self.output(status=state)), mock.patch.object(move_validator, 'call') as remove:
                self.assertEqual(move_validator.observe_owned_containers([self.identifier], self.marker), [])
                remove.assert_called_once_with(['docker', 'rm', self.identifier])

    def test_timeout_or_transport_failure_is_not_terminal(self):
        with mock.patch.object(move_validator.subprocess, 'run', side_effect=move_validator.subprocess.TimeoutExpired('inspect', 30)), mock.patch.object(move_validator, 'call') as remove:
            with self.assertRaises(move_validator.subprocess.TimeoutExpired): move_validator.observe_owned_containers([self.identifier], self.marker)
            remove.assert_not_called()
        with mock.patch.object(move_validator.subprocess, 'run', return_value=mock.Mock(returncode=1, stderr='daemon unavailable')), mock.patch.object(move_validator, 'call') as remove:
            with self.assertRaisesRegex(RuntimeError, 'state-unconfirmed'): move_validator.observe_owned_containers([self.identifier], self.marker)
            remove.assert_not_called()

    def test_foreign_or_malformed_state_never_removed(self):
        for value in (self.output(marker='foreign'), self.output(status='running'), self.output(running=0)):
            with self.subTest(value=value), mock.patch.object(move_validator.subprocess, 'run', return_value=value), mock.patch.object(move_validator, 'call') as remove:
                with self.assertRaises(AssertionError): move_validator.observe_owned_containers([self.identifier], self.marker)
                remove.assert_not_called()

    def test_absent_container_is_not_restarted_or_removed(self):
        with mock.patch.object(move_validator.subprocess, 'run', return_value=mock.Mock(returncode=1, stderr='No such object: ' + self.identifier)), mock.patch.object(move_validator, 'call') as remove:
            self.assertEqual(move_validator.observe_owned_containers([self.identifier], self.marker), [])
            remove.assert_not_called()


if __name__ == '__main__': unittest.main()
