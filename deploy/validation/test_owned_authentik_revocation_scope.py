"""Mutation boundaries for QA's exact temporary Authentik user, without real HTTP."""
import copy
import unittest
from unittest.mock import patch

import owned_authentik_revocation as owned

SUBJECT = '11111111-2222-3333-4444-555555555555'
NAME = 'whaledeck-upload-qa-123456abcdef'
SETTINGS = {'Authentication__Authentik__ApiToken':'synthetic-token','Authentication__Authentik__AdministratorGroupId':'synthetic-admin-group'}


class Response:
    def __init__(self, value, status=200): self.value, self.status_code = copy.deepcopy(value), status
    def json(self): return copy.deepcopy(self.value)


class Client:
    def __init__(self):
        self.user={'pk':41,'uuid':SUBJECT,'username':NAME,'is_active':True,'groups':['synthetic-admin-group']}
        self.headers={};self.trust_env=True;self.patches=[];self.closed=False;self.patch_status=200
    def get(self,url,**_kwargs):
        if url==owned.AUTHENTIK_API+'core/users/':return Response({'results':[self.user],'pagination':{'next':None}})
        assert url==owned.AUTHENTIK_API+'core/users/41/'
        return Response(self.user)
    def patch(self,url,*,json,timeout):
        self.patches.append((url,copy.deepcopy(json)));self.user.update(json)
        return Response(self.user,self.patch_status)
    def close(self): self.closed=True


class OwnedIdentityScopeTests(unittest.TestCase):
    def context(self, client): return owned.OwnedAuthentikRevocation(NAME,SUBJECT,client=client,settings=SETTINGS)

    def test_remove_only_groups_from_the_exact_owned_pk(self):
        c=Client()
        with self.context(c) as action:
            proof=action.change('group-removal')
            self.assertEqual(c.patches,[(owned.AUTHENTIK_API+'core/users/41/',{'groups':[]})])
            self.assertEqual(proof['subject'],SUBJECT);self.assertTrue(proof['onlyOwnedIdentityChanged'])
            self.assertTrue(proof['after']['is_active']);self.assertFalse(c.trust_env)
        self.assertTrue(c.closed)

    def test_disable_changes_only_active_flag_and_keeps_memberships(self):
        c=Client()
        with self.context(c) as action:
            proof=action.change('account-disable')
            self.assertEqual(c.patches,[(owned.AUTHENTIK_API+'core/users/41/',{'is_active':False})])
            self.assertEqual(proof['before']['groups'],proof['after']['groups'])

    def test_non_owned_username_is_rejected_before_loading_credentials_or_http(self):
        with patch.object(owned,'container_environment',side_effect=AssertionError('must not load configuration')) as env:
            for name in ('admin','whaledeck-upload-qa-other','whaledeck-upload-qa-123456abcdef/41'):
                with self.subTest(name=name), self.assertRaises(AssertionError): owned.OwnedAuthentikRevocation(name,SUBJECT)
            env.assert_not_called()

    def test_wrong_uuid_cannot_authorize_a_mutation(self):
        c=Client();c.user['uuid']='aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee'
        with self.assertRaises(AssertionError):self.context(c)
        self.assertFalse(c.patches);self.assertTrue(c.closed)

    def test_wrong_native_username_cannot_authorize_a_mutation(self):
        c=Client();c.user['username']='someone-else'
        with self.assertRaises(AssertionError):self.context(c)
        self.assertFalse(c.patches)

    def test_non_administrator_cannot_be_a_revocation_fixture(self):
        c=Client();c.user['groups']=[]
        with self.assertRaises(AssertionError):self.context(c)
        self.assertFalse(c.patches)

    def test_already_disabled_identity_is_not_a_live_revocation_case(self):
        c=Client();c.user['is_active']=False
        with self.assertRaises(AssertionError):self.context(c)
        self.assertFalse(c.patches)

    def test_native_identity_change_between_lookup_and_patch_is_refused(self):
        c=Client()
        with self.context(c) as action:
            c.user['uuid']='aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee'
            with self.assertRaises(AssertionError):action.change('group-removal')
        self.assertFalse(c.patches)

    def test_unsupported_action_never_sends_patch(self):
        c=Client()
        with self.context(c) as action:
            with self.assertRaises(AssertionError):action.change('delete-user')
        self.assertFalse(c.patches)

    def test_lost_reply_cannot_resubmit_a_second_mutation(self):
        c=Client();c.patch_status=503
        with self.context(c) as action:
            with self.assertRaises(AssertionError):action.change('account-disable')
            with self.assertRaises(AssertionError):action.change('account-disable')
        self.assertEqual(len(c.patches),1)

    def test_resource_id_cannot_select_another_path(self):
        c=Client();c.user['pk']='41/../42'
        with self.assertRaises(AssertionError):self.context(c)
        self.assertFalse(c.patches)

    def test_truthy_active_string_is_not_an_active_account(self):
        c=Client();c.user['is_active']='true'
        with self.assertRaises(AssertionError):self.context(c)
        self.assertFalse(c.patches)


if __name__=='__main__':unittest.main()
