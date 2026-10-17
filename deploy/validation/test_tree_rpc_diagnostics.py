import importlib.util
import json
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location('tree_rpc_validator', Path(__file__).with_name('validate_file_tree_rpc.py'))
validator = importlib.util.module_from_spec(spec)
spec.loader.exec_module(validator)


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


if __name__ == '__main__': unittest.main()
