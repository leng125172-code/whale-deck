"""Exercise cleanup ownership with fake identity responses; never calls a service."""
import importlib.util
from pathlib import Path
import sys
from types import SimpleNamespace
import unittest
from unittest.mock import patch


class Response:
    status_code = 204
    ok = True

    def __init__(self, value):
        self.value = value

    def json(self):
        return self.value

    def raise_for_status(self):
        pass


class Session:
    def __init__(self, applications):
        self.headers = {}
        self.applications = applications
        self.deleted = []

    def post(self, url, **kwargs):
        return Response({'pk': 50})

    def get(self, url, **kwargs):
        return Response({'results': self.applications if url.endswith('core/applications/') else []})

    def delete(self, url, **kwargs):
        self.deleted.append(url)
        return Response({})


class EphemeralAdminCleanupTests(unittest.TestCase):
    own_slug = 'whaledeck-validation-0123456789'
    unrelated_slug = 'whaledeck-validation-abcdefabcd'

    def run_wrapper(self, applications):
        session = Session(applications)
        fake_requests = SimpleNamespace(Session=lambda: session)
        source = Path(__file__).with_name('run_with_ephemeral_admin.py')
        specification = importlib.util.spec_from_file_location('ephemeral_cleanup_fixture', source)
        module = importlib.util.module_from_spec(specification)
        with patch.dict(sys.modules, {'requests': fake_requests}):
            specification.loader.exec_module(module)
        with patch.object(module, 'container_environment', return_value={
            'Authentication__Authentik__ApiToken': 'fixture-only',
            'Authentication__Authentik__AdministratorGroupId': 'fixture-group',
        }), patch.object(module.secrets, 'token_hex', return_value='0123456789'), \
                patch.object(module.secrets, 'token_urlsafe', return_value='fixture-only'), \
                patch.object(module.subprocess, 'run', return_value=SimpleNamespace(returncode=0)), \
                patch.object(sys, 'argv', ['wrapper', str(source)]):
            self.assertEqual(0, module.main())
        return session.deleted

    def test_only_exact_current_application_and_its_provider_are_removed(self):
        deleted = self.run_wrapper([
            {'slug': self.unrelated_slug, 'provider': 99},
            {'slug': self.own_slug, 'provider': 71},
        ])
        self.assertEqual([
            'http://127.0.0.1:8081/api/v3/core/applications/' + self.own_slug + '/',
            'http://127.0.0.1:8081/api/v3/providers/oauth2/71/',
            'http://127.0.0.1:8081/api/v3/core/users/50/',
        ], deleted)

    def test_another_live_validation_session_is_never_prefix_cleaned(self):
        deleted = self.run_wrapper([{'slug': self.unrelated_slug, 'provider': 99}])
        self.assertEqual(['http://127.0.0.1:8081/api/v3/core/users/50/'], deleted)

    def test_owned_application_without_provider_only_removes_owned_objects(self):
        deleted = self.run_wrapper([{'slug': self.own_slug, 'provider': None}])
        self.assertEqual([
            'http://127.0.0.1:8081/api/v3/core/applications/' + self.own_slug + '/',
            'http://127.0.0.1:8081/api/v3/core/users/50/',
        ], deleted)


if __name__ == '__main__':
    unittest.main()
