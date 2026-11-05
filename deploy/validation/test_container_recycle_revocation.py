import copy
import unittest
from pathlib import Path
from unittest.mock import patch

import validate_container_recycle_revocation as gate

SUBJECT = '11111111-2222-3333-4444-555555555555'


class RecycleRevocationTests(unittest.TestCase):
    def authority(self, kind):
        before = dict(pk=41, uuid=SUBJECT, username='whaledeck-upload-qa-123456abcdef', is_active=True, groups=['admin'])
        return dict(kind=kind, subject=SUBJECT, before=before, after={**before, **(dict(groups=[]) if kind == 'group-removal' else dict(is_active=False))}, onlyOwnedIdentityChanged=True, confirmedAtUtc='2026-11-05T10:00:00+00:00', executionExpiresAtUtc='2026-11-05T10:14:00+00:00')

    def browser(self, kind):
        return dict(kind=kind, subject=SUBJECT, recoverySubject='different', actualUi=True, registrations=1, confirmations=1, cancellations=0, managementWrites=0, ownerLogoutPosts=0, privatePanelsAbsent=True, hintsCleared=True, originalIntentStatus=403, currentIdentityStatus=200 if kind == 'group-removal' else 403, currentSubject=SUBJECT if kind == 'group-removal' else None, currentlyAdministrator=False if kind == 'group-removal' else None, renderedRevocationPage='forbidden' if kind == 'group-removal' else 'login', recoveryLogout=dict(normalUiLogout=True, postStatus=302, providerNavigationObserved=True, bffStatus=401, identityStatus=403, finalLoginPage=True))

    def test_each_exact_delta_accepts_only_its_selected_scenario(self):
        for kind in ('group-removal', 'account-disable'):
            with self.subTest(kind=kind):
                gate.authority_proof(self.authority(kind), dict(kind=kind, subject=SUBJECT))
                other = 'account-disable' if kind == 'group-removal' else 'group-removal'
                with self.assertRaises(AssertionError): gate.authority_proof(self.authority(kind), dict(kind=other, subject=SUBJECT))

    def test_wrong_identity_or_extra_mutation_is_not_authority_evidence(self):
        for kind in ('group-removal', 'account-disable'):
            for field, bad in [('pk', 42), ('uuid', 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee'), ('username', 'admin'), ('unrelated', True)]:
                a = self.authority(kind); a['after'][field] = bad
                with self.subTest(kind=kind, field=field), self.assertRaises(AssertionError): gate.authority_proof(a, dict(kind=kind, subject=SUBJECT))

    def test_expired_or_nearly_expiring_execution_is_not_a_revocation_proof(self):
        a = self.authority('group-removal'); a['executionExpiresAtUtc'] = '2026-11-05T10:09:59+00:00'
        with self.assertRaises(AssertionError): gate.authority_proof(a, dict(kind='group-removal', subject=SUBJECT))

    def test_group_removal_and_disabled_ui_require_distinct_visible_outcomes(self):
        for kind in ('group-removal', 'account-disable'):
            b = self.browser(kind); f = dict(kind=kind, subject=SUBJECT); gate.browser_proof(b, f)
            for field, bad in [('originalIntentStatus', 200), ('renderedRevocationPage', 'containers'), ('hintsCleared', False), ('privatePanelsAbsent', False), ('recoverySubject', SUBJECT), ('currentIdentityStatus', 200 if kind == 'account-disable' else 403)]:
                with self.subTest(kind=kind, field=field), self.assertRaises(AssertionError): gate.browser_proof({**b, field: bad}, f)

    def test_cancellation_logout_or_recovery_write_cannot_be_counted(self):
        f = dict(kind='group-removal', subject=SUBJECT); b = self.browser(f['kind'])
        for field in ('cancellations', 'ownerLogoutPosts', 'managementWrites'):
            with self.subTest(field=field), self.assertRaises(AssertionError): gate.browser_proof({**b, field: 1}, f)
        altered = copy.deepcopy(b); altered['recoveryLogout']['identityStatus'] = 200
        with self.assertRaises(AssertionError): gate.browser_proof(altered, f)

    def test_missed_cut_cannot_authorize_identity_change_or_replay(self):
        code = Path(gate.__file__).read_text()
        self.assertLess(code.index("assert held is not None,'EXACT_CUT_MISSED_NO_REPLAY'"), code.index('identity.change(kind)'))
        self.assertIn("'HELPER_EXIT_BEFORE_AUTHORITY_CONFIRMED'", code)
        self.assertIn("f['retain_unsettled']=True", code)
        self.assertLess(code.index("e/'independent-live.log'"), code.index('leaf.unlink()'))
        for kind in ('delete-user', 'logout', 'worker-crash'):
            with self.subTest(kind=kind), patch.object(gate.os, 'geteuid') as uid, self.assertRaises(AssertionError): gate.main('a' * 40, kind)
            uid.assert_not_called()


if __name__ == '__main__': unittest.main()
