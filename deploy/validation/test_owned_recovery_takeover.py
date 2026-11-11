"""Do not confuse a legitimate new workflow with immutable old evidence."""
import copy
import unittest

from owned_recovery_takeover import require_takeover_window

OWNER = '11111111-1111-1111-1111-111111111111'
OTHER = '22222222-2222-2222-2222-222222222222'
OLD = '33333333-3333-3333-3333-333333333333'


class TakeoverWindowTests(unittest.TestCase):
    def fixture(self):
        before = dict(workspaceOwner=OWNER, jobActor=OWNER, oldTask=OLD, oldStepSha256='a'*64,
            planSha256='b'*64, snapshotSha256='c'*64, archiveId='fixed-archive', expectedEpoch=4,
            confirmedAtUtc='fixed-confirmation', planExpiresAtUtc='fixed-expiry')
        proof = dict(passed=True, rows=[dict(workspaceOwner=OWNER, jobActor=OWNER,
            recoveryActor=OTHER, executionActor=OTHER, previousTaskId=OLD)])
        return before, copy.deepcopy(before), OWNER, True, proof

    def test_new_actor_can_advance_current_workflow_while_old_evidence_stays_exact(self):
        require_takeover_window(*self.fixture())

    def test_restore_before_second_actor_success_does_not_count(self):
        args = list(self.fixture()); args[3] = False
        with self.assertRaises(AssertionError): require_takeover_window(*args)

    def test_old_step_plan_snapshot_confirmation_and_expiry_cannot_change(self):
        for key in ('oldStepSha256', 'planSha256', 'snapshotSha256', 'archiveId', 'expectedEpoch', 'confirmedAtUtc', 'planExpiresAtUtc'):
            args = list(self.fixture()); args[1][key] = 'changed'
            with self.subTest(key=key), self.assertRaises(AssertionError): require_takeover_window(*args)

    def test_owner_or_original_job_cannot_be_transferred(self):
        for key in ('workspaceOwner', 'jobActor'):
            args = list(self.fixture()); args[4]['rows'][0][key] = OTHER
            with self.subTest(key=key), self.assertRaises(AssertionError): require_takeover_window(*args)

    def test_same_actor_wrong_execution_or_previous_task_is_rejected(self):
        for key, value in [('recoveryActor', OWNER), ('executionActor', OWNER), ('previousTaskId', OTHER)]:
            args = list(self.fixture()); args[4]['rows'][0][key] = value
            with self.subTest(key=key), self.assertRaises(AssertionError): require_takeover_window(*args)

    def test_failed_or_ambiguous_administrator_proof_is_rejected(self):
        args = list(self.fixture()); args[4]['passed'] = False
        with self.assertRaises(AssertionError): require_takeover_window(*args)
        args = list(self.fixture()); args[4]['rows'] *= 2
        with self.assertRaises(AssertionError): require_takeover_window(*args)
