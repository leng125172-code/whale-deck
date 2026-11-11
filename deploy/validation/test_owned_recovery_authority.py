"""Offline mutation-scope and lost-response restoration safety checks."""
import copy
import unittest

from owned_recovery_authority import OwnedRecoveryAuthority, check_binding

SUBJECT = '11111111-2222-3333-4444-555555555555'
WORKSPACE = 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee'
RETIREMENT = '22222222-3333-4444-5555-666666666666'
TASK = '33333333-4444-5555-6666-777777777777'
NAME = 'whaledeck-platform-ui-'+'a'*32
GROUP = 'synthetic-group'


class Response:
    status_code = 200
    def __init__(self, value): self.value = copy.deepcopy(value)
    def json(self): return copy.deepcopy(self.value)


class Client:
    def __init__(self):
        self.user = dict(pk=41, uuid=SUBJECT, username=NAME, is_active=True, groups=[GROUP])
        self.patches = []
        self.lose_reply = False

    def get(self, url, **kwargs):
        return Response(dict(results=[self.user], pagination={'next': None}) if url.endswith('core/users/') else self.user)

    def patch(self, url, *, json, timeout):
        self.patches.append(copy.deepcopy(json)); self.user.update(json)
        if self.lose_reply:
            self.lose_reply = False
            raise TimeoutError('synthetic lost reply')
        return Response(self.user)


class ScopeTests(unittest.TestCase):
    def action(self, client): return OwnedRecoveryAuthority(client, NAME, SUBJECT, GROUP)

    def test_removes_and_restores_only_original_exact_group(self):
        client = Client(); action = self.action(client)
        action.remove(); action.restore(); action.restore()
        self.assertEqual(client.patches, [{'groups': []}, {'groups': [GROUP]}])
        self.assertTrue(client.user['is_active'])

    def test_lost_remove_reply_can_restore_but_cannot_remove_again(self):
        client = Client(); action = self.action(client); client.lose_reply = True
        with self.assertRaises(TimeoutError): action.remove()
        with self.assertRaises(AssertionError): action.remove()
        action.restore()
        self.assertEqual(client.patches, [{'groups': []}, {'groups': [GROUP]}])

    def test_unowned_name_wrong_subject_and_non_admin_never_patch(self):
        for key, value in [('username', 'another-user'), ('uuid', WORKSPACE), ('groups', []), ('is_active', False)]:
            with self.subTest(key=key):
                client = Client(); client.user[key] = value
                with self.assertRaises(AssertionError): self.action(client)
                self.assertEqual(client.patches, [])
        with self.assertRaises(AssertionError): OwnedRecoveryAuthority(Client(), 'admin', SUBJECT, GROUP)

    def test_identity_or_groups_changed_by_others_is_not_overwritten(self):
        for key, value in [('uuid', WORKSPACE), ('username', 'other'), ('groups', ['different']), ('is_active', False)]:
            with self.subTest(key=key):
                client = Client(); action = self.action(client); action.remove(); client.user[key] = value
                with self.assertRaises(AssertionError): action.restore()
                self.assertEqual(client.patches, [{'groups': []}])

    def binding(self):
        slug = 'qa-native-'+'a'*24
        p = dict(username=NAME, subject=SUBJECT, workspaceId=WORKSPACE, retirementId=RETIREMENT)
        s = dict(slug=slug, ownerSubject=SUBJECT, workspace={'id': WORKSPACE},
            retirementBusiness={'recovery': {'failedView': dict(retirementId=RETIREMENT, attentionRequired=True, executeTaskId=TASK)}})
        d = dict(slug=slug, originalWorkspaceIds=[])
        c = dict(slug=slug, subject=SUBJECT, workspaceId=WORKSPACE, retirementId=RETIREMENT, executeStepId=TASK,
            lifecycle='Retiring', attention=True, lease=None, finished=None, recoveries=0, completions=0, status='Rejected', settled='timestamp')
        return p, s, d, c

    def test_only_declared_new_owner_and_settled_attention_case_allowed(self):
        p, s, d, c = self.binding(); check_binding(p, s, d, c)
        for key, value in [('subject', WORKSPACE), ('workspaceId', RETIREMENT), ('retirementId', WORKSPACE),
            ('executeStepId', RETIREMENT), ('attention', False), ('lease', TASK), ('finished', 'timestamp'),
            ('recoveries', 1), ('completions', 1), ('status', 'Running'), ('settled', None)]:
            with self.subTest(key=key), self.assertRaises(AssertionError):
                check_binding(p, s, d, dict(c, **{key: value}))
        d['originalWorkspaceIds'] = [WORKSPACE]
        with self.assertRaises(AssertionError): check_binding(p, s, d, c)


if __name__ == '__main__': unittest.main()
