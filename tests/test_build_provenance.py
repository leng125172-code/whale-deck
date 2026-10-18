"""Contract tests with explicit process doubles; actual runtime proof is separate."""
import importlib.util
import json
from pathlib import Path
import unittest

SPEC = importlib.util.spec_from_file_location('build_provenance', Path(__file__).resolve().parents[1] / 'deploy/validation/validate_build_provenance.py')
module = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(module)


class BuildProvenanceTests(unittest.TestCase):
    revision = 'a' * 40

    def setUp(self):
        self.calls = []

    def fake_command(self, arguments):
        self.calls.append(arguments)
        if arguments[:2] == ['docker', 'inspect']:
            return 'b' * 64 + '|sha256:' + 'c' * 64
        if arguments[:3] == ['docker', 'image', 'inspect']:
            return self.revision
        if 'cat' in arguments:
            component = 'WhaleDeck.Gateway'
        elif arguments[0].replace('\\', '/').startswith('/opt/'):
            component = Path(arguments[0]).name
        elif arguments[:2] == ['docker', 'run']:
            component = 'WhaleDeck.Migrator'
        else:
            component = arguments[-2].removesuffix('.dll')
        return json.dumps({'component': component, 'version': '0.1.0', 'sourceRevision': self.revision})

    def test_all_six_components_require_exact_binary_and_image_identity(self):
        result = module.verify(self.revision, run=self.fake_command)
        self.assertTrue(result['verified'])
        self.assertEqual(len(result['components']), 6)
        self.assertTrue(all('--build-info' in call for call in self.calls if call[:2] == ['docker', 'run']))
        self.assertFalse(any('Env' in str(call) for call in self.calls))
        self.assertIn('none', next(call for call in self.calls if call[:2] == ['docker', 'run']))

    def test_wrong_image_label_is_rejected(self):
        def changed(args):
            return 'd' * 40 if args[:3] == ['docker', 'image', 'inspect'] else self.fake_command(args)
        with self.assertRaisesRegex(ValueError, 'IMAGE_REVISION_MISMATCH'):
            module.verify(self.revision, run=changed)

    def test_container_replacement_during_inspection_is_not_a_pass(self):
        snapshots = 0
        def changed(args):
            nonlocal snapshots
            if args[:2] == ['docker', 'inspect']:
                snapshots += 1
                return ('b' if snapshots == 1 else 'd') * 64 + '|sha256:' + 'c' * 64
            return self.fake_command(args)
        with self.assertRaisesRegex(ValueError, 'CONTAINER_CHANGED'):
            module.verify(self.revision, run=changed)

    def test_missing_binary_revision_is_rejected_even_if_image_label_matches(self):
        with self.assertRaisesRegex(ValueError, 'IDENTITY_MISMATCH'):
            module.identity({'component': 'WhaleDeck.Api', 'version': '0.1.0', 'sourceRevision': None}, 'WhaleDeck.Api', self.revision)

    def test_metadata_does_not_accept_extra_environment_fields(self):
        with self.assertRaisesRegex(ValueError, 'METADATA_INVALID'):
            module.identity({'component': 'WhaleDeck.Api', 'version': '0.1.0', 'sourceRevision': self.revision, 'env': {}}, 'WhaleDeck.Api', self.revision)


if __name__ == '__main__':
    unittest.main()
