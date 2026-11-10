"""Offline failure recovery gate; never invokes an identity, socket or helper."""
import json
import unittest

from validate_workspace_retirement_plan_installed import failed_probe_is_settled


class RetirementPlanRecoveryScope(unittest.TestCase):
    def process(self, cleanup, **changes):
        return dict(returncode=1, stderr=json.dumps(dict(passed=False, cleanup=cleanup)), **changes)

    def test_failure_before_first_rpc_allows_supported_ui_recovery(self):
        self.assertTrue(failed_probe_is_settled(self.process([])))

    def test_all_original_tasks_must_have_a_confirmed_terminal_receipt(self):
        for status in ('Succeeded', 'Rejected', 'Stopped', 'Interrupted'):
            with self.subTest(status=status):
                self.assertTrue(failed_probe_is_settled(self.process([dict(settled=True, Status=status)])))
        self.assertTrue(failed_probe_is_settled(self.process([dict(settled=True, Status='Succeeded'),
            dict(settled=True, Status='Stopped')])))

    def test_one_unsettled_task_blocks_recovery_even_if_another_is_terminal(self):
        for row in (dict(settled=False, Status='Stopped'), dict(settled=True, Status='Running'),
                    dict(settled=True, Status='Pending'), dict(settled=True, Status='Finishing'),
                    dict(settled=True, Status='NotFound'), dict(settled=1, Status='Stopped'),
                    dict(failureType='RpcException', settled=False)):
            with self.subTest(row=row):
                self.assertFalse(failed_probe_is_settled(self.process([dict(settled=True, Status='Stopped'), row])))

    def test_missing_timeout_or_malformed_output_cannot_prove_settlement(self):
        for value in ({}, None, dict(returncode=1, stderr=''), dict(returncode=1, stderr='timeout'),
                      dict(returncode=1, stderr='null'), dict(returncode=1, stderr='{}')):
            with self.subTest(value=value):
                self.assertFalse(failed_probe_is_settled(value))

    def test_cleanup_must_be_a_list_and_each_receipt_must_be_complete(self):
        for cleanup in (None, {}, False, [None], [dict(settled=True)], [dict(Status='Stopped')]):
            with self.subTest(cleanup=cleanup):
                self.assertFalse(failed_probe_is_settled(self.process(cleanup)))

    def test_success_or_crash_exit_cannot_be_treated_as_a_sanitized_failure(self):
        for code in (0, -9, True, '1'):
            value = self.process([]); value['returncode'] = code
            self.assertFalse(failed_probe_is_settled(value))
        self.assertFalse(failed_probe_is_settled(dict(returncode=1, stderr=json.dumps(dict(passed=True, cleanup=[])))))


if __name__ == '__main__':
    unittest.main()
