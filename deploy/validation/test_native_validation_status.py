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

    def test_public_phase_markers_are_bounded_and_never_store_raw_output(self):
        with tempfile.TemporaryDirectory() as directory:
            report = NativeValidationStatus(Path(directory), 'dispatch', 'c' * 40)
            phase = 'container-acl-restore-source-mac-immutable-audited-new-jobs'
            report.update(nativePhases=[phase])
            self.assertEqual([phase], json.loads(report.path.read_text())['nativePhases'])
            original = report.path.read_bytes()
            for value in (phase, [None], ['Connection=secret'], ['bad\nvalue'],
                          ['x' * 161], ['ok'] * 129):
                with self.assertRaises(ValueError):
                    report.update(nativePhases=value)
                self.assertEqual(original, report.path.read_bytes())


if __name__ == '__main__':
    unittest.main()
