"""Real cookie identities behind one proxy; no anonymous flood or host mutation."""
import json
import os
import secrets
import time
import uuid

import requests

from run_with_ephemeral_admin import AUTHENTIK_API, container_environment, require
from validate_oidc import authenticate, required_environment


def main():
    base = os.environ.get('WHALEDECK_URL', 'http://192.168.100.13:8080').rstrip('/')
    admin, _ = authenticate(base, required_environment('VALIDATION_USERNAME'), required_environment('VALIDATION_PASSWORD'))
    identity = requests.Session()
    identity.trust_env = False
    identity.headers.update({'Authorization': 'Bearer ' + require(container_environment(), 'Authentication__Authentik__ApiToken')})
    user_id = None
    try:
        # The operations budget starts fresh: authentication uses a separate bounded partition.
        started = time.monotonic()
        accepted = 0
        for index in range(121):
            response = admin.get(base + '/api/v1/notifications/summary', timeout=15, allow_redirects=False)
            if index < 120:
                assert response.status_code == 200, 'Fresh authenticated operation quota was not independent'
                accepted += 1
            else:
                assert response.status_code == 429, 'Request 121 did not return explicit 429'
                assert response.json()['code'] == 'API_RATE_LIMITED'
                assert response.json()['traceId']
                assert 'no-store' in response.headers.get('Cache-Control', '')
                assert 1 <= int(response.headers['Retry-After']) <= 60
        assert time.monotonic() - started < 50, 'Boundary check crossed a possible window rollover'
        name = 'whaledeck-rate-validation-' + uuid.uuid4().hex[:10]
        password = 'Wd!' + secrets.token_urlsafe(24) + '9aA'
        created = identity.post(AUTHENTIK_API + 'core/users/', json={
            'username': name, 'name': 'Ephemeral rate acceptance', 'email': '', 'is_active': True, 'groups': [],
        }, timeout=20)
        created.raise_for_status()
        user_id = str(created.json()['pk'])
        identity.post(AUTHENTIK_API + f'core/users/{user_id}/set_password/', json={'password': password}, timeout=20).raise_for_status()
        ordinary, _ = authenticate(base, name, password, require_administrator=False)
        assert ordinary.get(base + '/api/v1/notifications/summary', timeout=15).status_code == 200
        assert ordinary.get(base + '/api/v1/containers', timeout=15, allow_redirects=False).status_code == 403
        anonymous = requests.Session()
        anonymous.trust_env = False
        assert anonymous.get(base + '/api/v1/notifications/summary', timeout=15, allow_redirects=False).status_code == 401
        assert admin.get(base + '/api/v1/notifications/summary', timeout=15, allow_redirects=False).status_code == 429, 'Quota rolled over before session independence was checked'
        csrf = admin.get(base + '/api/v1/auth/csrf', timeout=15)
        assert csrf.status_code == 200, 'Exhausted operations blocked session CSRF'
        token = csrf.json()
        logout = admin.post(base + '/api/v1/auth/logout', headers={
            token['headerName']: token['token'], 'Origin': base,
        }, timeout=20, allow_redirects=False)
        assert logout.status_code in (302, 303), 'Panel logout was blocked by operations quota'
        assert admin.get(base + '/api/v1/auth/me', timeout=15, allow_redirects=False).status_code == 401
        print(json.dumps({'apiRateLimitAcceptance': 'passed', 'realCookieIdentitiesSameProxyIndependent': 'passed',
            'freshOperationsAccepted': accepted, 'request121': 429, 'sessionCsrfAndPanelLogout': 'passed',
            'anonymous401OrdinaryManagement403': 'passed', 'anonymousFlood': False,
            'globalAuthentikLogoutBrowser': 'not-tested', 'hostMutations': 0}))
    finally:
        if user_id is not None:
            removed = identity.delete(AUTHENTIK_API + f'core/users/{user_id}/', timeout=20)
            assert removed.status_code in (204, 404), 'Temporary rate identity cleanup failed'


if __name__ == '__main__':
    main()
