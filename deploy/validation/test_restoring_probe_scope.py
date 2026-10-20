"""No host mutations: guard probe resource IDs and existing QA-only revision rules."""
from pathlib import Path
import unittest
from validate_tree_restoring_agent import identifiers


class RestoringProbeScopeTests(unittest.TestCase):
    def test_only_exact_distinct_nonzero_operation_identifiers_are_accepted(self):
        values = ['a' * 32, 'b' * 32]
        self.assertEqual(identifiers(values, 2), values)
        for bad in (None, [], ['a' * 32], ['a' * 32, 'a' * 32], ['0' * 32, 'b' * 32], ['../x', 'b' * 32], ['A' * 32, 'b' * 32], ['a' * 32 + '\n', 'b' * 32]):
            with self.subTest(value=bad), self.assertRaises(AssertionError): identifiers(bad, 2)

    def test_probe_is_in_solution_and_reports_its_own_failure_phase_without_private_output(self):
        source = Path(__file__).parent / 'TerminalProbe'
        text = (source / 'Program.cs').read_text(encoding='utf-8-sig')
        self.assertIn('args[0] == "tree-restore"', text)
        self.assertIn('args is ["tree-restore"] ? FileTreeRestoringProbe.Phase', text)
        probe = (source / 'FileTreeRestoringProbe.cs').read_text(encoding='utf-8-sig')
        self.assertIn('CryptographicOperations.ZeroMemory(key)', probe)
        self.assertNotIn('FileTreeRestoringPolicy', probe)
        self.assertNotIn('docker.sock', probe)


if __name__ == '__main__': unittest.main()
