"""Servicing-floor checks use synthetic artifact metadata, never install services."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

SPEC = importlib.util.spec_from_file_location('host_runtime', Path(__file__).resolve().parents[1] / 'deploy/host/validate-runtime.py')
runtime = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(runtime)


class HostRuntimeTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix='wd-runtime-test-')
        self.root = Path(self.temporary.name)
        self.application = 'WhaleDeck.Agent'
        self.config = self.root / (self.application + '.runtimeconfig.json')
        self.dll = self.root / 'System.Security.Cryptography.Pkcs.dll'
        self.write()

    def tearDown(self):
        self.temporary.cleanup()

    def write(self, framework='10.0.13', pkcs='10.0.13'):
        self.config.write_text(json.dumps({'runtimeOptions': {'includedFrameworks': [
            {'name': name, 'version': framework} for name in sorted(runtime.FRAMEWORKS)]}}))
        self.dll.write_bytes(b'MZ' + pkcs.encode('ascii') + b'+' + b'a' * 40)

    def validate(self):
        return runtime.validate_component(self.root, self.application)

    def test_fixed_bundle_passes(self):
        self.assertEqual(self.validate()['pkcsVersions'], ['10.0.13'])

    def test_future_servicing_patch_passes(self):
        self.write('10.0.14', '10.0.14')
        self.assertEqual(self.validate()['pkcsVersions'], ['10.0.14'])

    def test_old_framework_is_rejected_even_with_new_pkcs(self):
        self.write(framework='10.0.8')
        with self.assertRaisesRegex(ValueError, 'VERSION_UNSAFE'):
            self.validate()

    def test_old_pkcs_is_rejected_even_with_new_framework(self):
        self.write(pkcs='10.0.8')
        with self.assertRaisesRegex(ValueError, 'VERSION_UNSAFE'):
            self.validate()

    def test_future_major_requires_explicit_review(self):
        self.write(framework='11.0.0')
        with self.assertRaisesRegex(ValueError, 'VERSION_UNSUPPORTED'):
            self.validate()

    def test_missing_runtime_metadata_is_rejected(self):
        self.config.write_text('{}')
        with self.assertRaisesRegex(ValueError, 'FRAMEWORK_SET_INVALID'):
            self.validate()

    def test_duplicate_framework_is_rejected(self):
        self.config.write_text(json.dumps({'runtimeOptions': {'includedFrameworks': [
            {'name': 'Microsoft.NETCore.App', 'version': '10.0.13'}] * 2}}))
        with self.assertRaisesRegex(ValueError, 'FRAMEWORK_SET_INVALID'):
            self.validate()

    def test_missing_dll_is_rejected(self):
        self.dll.unlink()
        with self.assertRaisesRegex(ValueError, 'ARTIFACT_INVALID'):
            self.validate()

    def test_missing_product_version_is_rejected(self):
        self.dll.write_bytes(b'MZmetadata-with-no-product-version')
        with self.assertRaisesRegex(ValueError, 'PRODUCT_VERSION_MISSING'):
            self.validate()

    def test_non_pe_file_is_rejected(self):
        self.dll.write_bytes(b'10.0.13+' + b'a' * 40)
        with self.assertRaisesRegex(ValueError, 'PRODUCT_VERSION_MISSING'):
            self.validate()

    def test_oversized_metadata_is_rejected(self):
        self.config.write_text(' ' * (64 * 1024 + 1))
        with self.assertRaisesRegex(ValueError, 'ARTIFACT_INVALID'):
            self.validate()

    def test_corrupt_json_is_rejected(self):
        self.config.write_text('not valid JSON')
        with self.assertRaises(ValueError):
            self.validate()


if __name__ == '__main__':
    unittest.main()
