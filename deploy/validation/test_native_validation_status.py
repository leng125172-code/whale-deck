import json
from pathlib import Path
import tempfile
import unittest
from native_validation_status import NativeValidationStatus


class NativeValidationStatusTests(unittest.TestCase):
    def test_durable_acceptance_requires_executed_pass_and_cleanup(self):
        with tempfile.TemporaryDirectory() as directory:
            report = NativeValidationStatus(Path(directory), 'notifications', 'a' * 40)
            with self.assertRaises(ValueError):
                report.update(state='Passed')
            report.update(state='Running', executedTests=1, passedTests=1)
            with self.assertRaises(ValueError):
                report.update(state='Passed')
            report.update(cleanupConfirmed=True, state='Passed')
            self.assertEqual('Passed', json.loads(report.path.read_text())['state'])
            self.assertEqual([report.path], list(Path(directory).iterdir()))

    def test_rejects_output_and_invalid_metadata_without_replacing_status(self):
        with tempfile.TemporaryDirectory() as directory:
            report = NativeValidationStatus(Path(directory), 'alerts', 'b' * 40)
            original = report.path.read_bytes()
            for fields in ({'stdout': 'private'}, {'stage': 'bad\nvalue'}, {'state': 'Unknown'}):
                with self.assertRaises(ValueError):
                    report.update(**fields)
                self.assertEqual(original, report.path.read_bytes())


if __name__ == '__main__':
    unittest.main()
