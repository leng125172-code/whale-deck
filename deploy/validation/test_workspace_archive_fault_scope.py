"""Offline mutation-boundary tests; no real identity or service operations."""
import copy
import unittest

from validate_workspace_archive_faults_installed import AUTHENTIK_API, OwnedArchiveIdentityRevocation

NAME = 'whaledeck-platform-ui-' + 'a' * 32
SUBJECT = '11111111-2222-3333-4444-555555555555'
GROUP = 'synthetic-admin-group'


class Response:
    def __init__(self, value, status=200):
        self.value, self.status_code = copy.deepcopy(value), status
    def json(self):
        return copy.deepcopy(self.value)


class Client:
    def __init__(self):
        self.user = dict(pk=41, uuid=SUBJECT, username=NAME, is_active=True, groups=[GROUP])
        self.patches, self.gets = [], []
        self.patch_status = 200
        self.next = None
    def get(self, url, **kwargs):
        self.gets.append(url)
        if url == AUTHENTIK_API + 'core/users/':
            assert kwargs['params'] == dict(username=NAME, page_size=2)
            return Response(dict(results=[self.user], pagination=dict(next=self.next)))
        assert url == AUTHENTIK_API + 'core/users/41/'
        return Response(self.user)
    def patch(self, url, *, json, timeout):
        self.patches.append((url, copy.deepcopy(json)))
        self.user.update(json)
        return Response(self.user, self.patch_status)


class WorkspaceArchiveIdentityScope(unittest.TestCase):
    def action(self, client):
        return OwnedArchiveIdentityRevocation(client, NAME, SUBJECT, GROUP)
    def test_group_removal_changes_only_the_exact_generated_user_groups(self):
        client = Client(); proof = self.action(client).change('group-removal')
        self.assertEqual([(AUTHENTIK_API + 'core/users/41/', {'groups': []})], client.patches)
        self.assertTrue(proof['onlyOwnedIdentityChanged']); self.assertTrue(client.user['is_active'])
    def test_disable_preserves_existing_groups(self):
        client = Client(); self.action(client).change('account-disable')
        self.assertEqual([(AUTHENTIK_API + 'core/users/41/', {'is_active': False})], client.patches)
        self.assertEqual([GROUP], client.user['groups'])
    def test_unknown_username_or_subject_never_reads_or_mutates_identity(self):
        client = Client()
        for name in ('admin', 'whaledeck-platform-ui-other', NAME + '/41'):
            with self.subTest(name=name), self.assertRaises(AssertionError):
                OwnedArchiveIdentityRevocation(client, name, SUBJECT, GROUP)
        with self.assertRaises(ValueError):
            OwnedArchiveIdentityRevocation(client, NAME, 'not-a-uuid', GROUP)
        self.assertEqual([], client.gets); self.assertEqual([], client.patches)
    def test_wrong_server_identity_never_mutates(self):
        for key, value in (('username', 'someone-else'), ('uuid', 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee'),
                           ('groups', []), ('is_active', False), ('is_active', 'true'), ('pk', '41/../42')):
            with self.subTest(key=key, value=value):
                client = Client(); client.user[key] = value
                with self.assertRaises(AssertionError):
                    self.action(client)
                self.assertEqual([], client.patches)
    def test_paginated_or_ambiguous_lookup_cannot_authorize_mutation(self):
        client = Client(); client.next = 2
        with self.assertRaises(AssertionError):
            self.action(client)
        self.assertEqual([], client.patches)
    def test_change_between_lookup_and_patch_is_rechecked(self):
        client = Client(); action = self.action(client); client.user['groups'] = []
        with self.assertRaises(AssertionError):
            action.change('account-disable')
        self.assertEqual([], client.patches)
    def test_lost_reply_consumes_the_one_mutation_attempt(self):
        client = Client(); action = self.action(client); client.patch_status = 503
        with self.assertRaises(AssertionError):
            action.change('group-removal')
        with self.assertRaises(AssertionError):
            action.change('group-removal')
        self.assertEqual(1, len(client.patches))
    def test_unsupported_action_never_mutates(self):
        client = Client(); action = self.action(client)
        with self.assertRaises(AssertionError):
            action.change('delete-user')
        self.assertEqual([], client.patches)


if __name__ == '__main__':
    unittest.main()
