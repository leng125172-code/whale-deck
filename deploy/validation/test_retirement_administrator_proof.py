"""Reject attribution, ownership and completion substitutions in real receipts."""
import copy
import unittest

from retirement_administrator_proof import require_administrator_recovery

OWNER = '11111111-1111-1111-1111-111111111111'
ACTOR = '22222222-2222-2222-2222-222222222222'
WORKSPACE = '33333333-3333-3333-3333-333333333333'
OLD = '44444444-4444-4444-4444-444444444444'
NEW = '55555555-5555-5555-5555-555555555555'


class AdministratorProofTests(unittest.TestCase):
    def fixture(self):
        state = dict(ownerSubject=OWNER, workspace=dict(id=WORKSPACE), retirementBusiness=dict(recovery=dict(
            administrator=dict(subject=ACTOR, ownerSubject=OWNER, passed=True, independentVisibleLogin=True,
                resourceOwnerPreserved=True, recoveryCompletedByIndependentSession=True,
                originalOwnerSessionPreserved=True, logout=dict(normalUiLogout=True, bffStatus=401, identityStatus=403)),
            attemptsBefore=[dict(taskId=OLD)], attemptsAfter=[dict(taskId=OLD), dict(taskId=NEW)])))
        rows = [dict(workspaceId=WORKSPACE, workspaceOwner=OWNER, jobActor=OWNER, executionActor=ACTOR,
            recoveryActor=ACTOR, previousTaskId=OLD, currentTaskId=NEW, recoveryTaskId=NEW, completionTaskId=NEW,
            bindingMatches=True, recoveryCount=1, independentActorAudit=True)]
        return state, rows

    def test_second_actor_and_unchanged_owner_pair_is_accepted(self):
        require_administrator_recovery(*self.fixture())

    def test_disabled_owner_requires_live_denial_instead_of_claiming_session_access(self):
        state, rows = self.fixture(); rec = state['retirementBusiness']['recovery']
        rec['takeover'] = dict(kind='account-disable')
        proof = rec['administrator']; del proof['originalOwnerSessionPreserved']
        proof.update(originalOwnerDeniedWhileDisabled=True, ownerStatusWhileDisabled=403)
        require_administrator_recovery(state, rows)
        for status in (200, 401):
            proof['ownerStatusWhileDisabled'] = status
            with self.subTest(status=status), self.assertRaises(AssertionError): require_administrator_recovery(state, rows)
        proof['ownerStatusWhileDisabled'] = 403; proof['originalOwnerDeniedWhileDisabled'] = False
        with self.assertRaises(AssertionError): require_administrator_recovery(state, rows)

    def test_owner_cannot_be_substituted_for_recovery_actor(self):
        for key in ('executionActor', 'recoveryActor'):
            state, rows = self.fixture(); rows[0][key] = OWNER
            with self.subTest(key=key), self.assertRaises(AssertionError): require_administrator_recovery(state, rows)
        state, rows = self.fixture(); state['retirementBusiness']['recovery']['administrator']['subject'] = OWNER
        with self.assertRaises(AssertionError): require_administrator_recovery(state, rows)

    def test_ownership_or_original_job_attribution_cannot_be_transferred(self):
        for key in ('workspaceOwner', 'jobActor'):
            state, rows = self.fixture(); rows[0][key] = ACTOR
            with self.subTest(key=key), self.assertRaises(AssertionError): require_administrator_recovery(state, rows)

    def test_stale_completion_or_missing_current_binding_audit_is_rejected(self):
        for key, value in [('completionTaskId', OLD), ('currentTaskId', OLD), ('recoveryTaskId', OLD),
                ('previousTaskId', NEW), ('bindingMatches', False), ('independentActorAudit', False), ('recoveryCount', 2)]:
            state, rows = self.fixture(); rows[0][key] = value
            with self.subTest(key=key), self.assertRaises(AssertionError): require_administrator_recovery(state, rows)
        state, rows = self.fixture()
        with self.assertRaises(AssertionError): require_administrator_recovery(state, rows+copy.deepcopy(rows))

    def test_missing_session_isolation_or_normal_logout_never_passes(self):
        for key in ('independentVisibleLogin', 'resourceOwnerPreserved', 'recoveryCompletedByIndependentSession', 'originalOwnerSessionPreserved', 'passed'):
            state, rows = self.fixture(); state['retirementBusiness']['recovery']['administrator'][key] = False
            with self.subTest(key=key), self.assertRaises(AssertionError): require_administrator_recovery(state, rows)
        state, rows = self.fixture(); state['retirementBusiness']['recovery']['administrator']['logout']['bffStatus'] = 200
        with self.assertRaises(AssertionError): require_administrator_recovery(state, rows)
