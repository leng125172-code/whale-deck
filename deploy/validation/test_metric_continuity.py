import unittest
from validate_metric_continuity import KINDS, evaluate


class MetricContinuityTests(unittest.TestCase):
    def rows(self):
        return [dict(kind=kind, device='fixture', count=60, maxGapSeconds=6.01,
                     leadingSeconds=2, trailingSeconds=4, notGood=0) for kind in KINDS]

    def test_complete_real_window_passes(self):
        result = evaluate(self.rows(), {(kind, 'fixture') for kind in KINDS}, 360)
        self.assertEqual(result['series'], 4)
        self.assertEqual(result['syntheticSamples'], 0)

    def test_rejects_regular_missing_batches_even_with_good_values(self):
        rows = self.rows()
        rows[0]['maxGapSeconds'] = 12
        with self.assertRaises(ValueError):
            evaluate(rows, {(kind, 'fixture') for kind in KINDS}, 360)

    def test_rejects_truncated_windows_missing_devices_bad_quality_and_duplicate_series(self):
        expected = {(kind, 'fixture') for kind in KINDS}
        for field, value in [('count', 40), ('leadingSeconds', 12), ('trailingSeconds', 12), ('notGood', 1), ('maxGapSeconds', None)]:
            with self.subTest(field=field):
                rows = self.rows()
                rows[0][field] = value
                with self.assertRaises(ValueError):
                    evaluate(rows, expected, 360)
        with self.assertRaises(ValueError):
            evaluate(self.rows()[:-1], expected, 360)
        with self.assertRaises(ValueError):
            evaluate(self.rows() + [self.rows()[0]], expected, 360)
        with self.assertRaises(ValueError):
            evaluate([], set(), 360)


if __name__ == '__main__':
    unittest.main()
