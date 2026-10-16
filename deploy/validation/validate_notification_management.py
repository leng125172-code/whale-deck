"""Real cookie-authorized metadata CRUD. Disabled temporary channels only; zero external sends."""
import json
import os
import secrets
import uuid

import requests

from run_with_ephemeral_admin import AUTHENTIK_API, container_environment, require
from validate_oidc import authenticate, required_environment


def main():
    base = os.environ.get('WHALEDECK_URL', 'http://192.168.100.13:8080').rstrip('/')
    admin, _ = authenticate(base, required_environment('VALIDATION_USERNAME'), required_environment('VALIDATION_PASSWORD'))
    endpoint = base + '/api/v1/notifications/manage/'
    csrf = admin.get(base + '/api/v1/auth/csrf', timeout=15).json()
    headers = {csrf['headerName']: csrf['token'], 'Origin': base}
    identity = requests.Session()
    identity.trust_env = False
    identity.headers.update({'Authorization': 'Bearer ' + require(container_environment(), 'Authentication__Authentik__ApiToken')})
    user_id = channel_id = route_id = None

    def write(method, path, data, key=None, extra=None):
        return admin.request(method, endpoint + path, json=data, headers={**headers, 'Idempotency-Key': key or str(uuid.uuid4()), **(extra or {})}, timeout=20, allow_redirects=False)

    try:
        marker = 'acceptance-' + uuid.uuid4().hex[:12]
        command = {'name': marker, 'provider': 'Webhook', 'destinationReference': 'approved-test', 'credentialReference': 'private-test-reference', 'isEnabled': False, 'confirmed': True}
        no_csrf = admin.post(endpoint + 'channels', json=command, headers={'Origin': base, 'Idempotency-Key': str(uuid.uuid4())}, timeout=15, allow_redirects=False)
        assert no_csrf.status_code in (400, 403), 'Write without CSRF succeeded'
        assert write('POST', 'channels', command, extra={'Origin': 'http://untrusted.invalid'}).status_code == 403, 'Origin check failed'
        assert write('POST', 'channels', {**command, 'confirmed': False}).status_code == 400
        assert write('POST', 'channels', command, extra={'Idempotency-Key': ''}).status_code == 400
        key = str(uuid.uuid4())
        response = write('POST', 'channels', command, key)
        assert response.status_code == 200, 'Disabled channel creation failed'
        channel = response.json(); channel_id = channel['id']
        assert 'no-store' in response.headers.get('Cache-Control', '')
        assert channel['hasCredential'] and not channel['isEnabled']
        assert 'credentialReference' not in channel and 'private-test-reference' not in response.text
        replay = write('POST', 'channels', command, key)
        assert replay.status_code == 200 and replay.json()['id'] == channel_id
        assert write('POST', 'channels', {**command, 'name': marker + '-changed'}, key).status_code == 409
        update = {**command, 'expectedVersion': channel['version'], 'credentialReference': None, 'keepCredential': True}
        changed = write('PUT', 'channels/' + channel_id, update)
        assert changed.status_code == 200 and changed.json()['hasCredential']
        assert write('PUT', 'channels/' + channel_id, update).status_code == 409
        route_command = {'channelId': channel_id, 'sourceKind': 'Alert', 'minimumSeverity': 'Warning', 'isEnabled': False, 'includeRecovery': True, 'confirmed': True}
        route_key = str(uuid.uuid4())
        added = write('POST', 'routes', route_command, route_key)
        assert added.status_code == 200
        route_id = added.json()['id']; route_version = added.json()['version']
        assert write('POST', 'routes', route_command, route_key).json()['id'] == route_id
        listed = admin.get(endpoint + 'routes', params={'channelId': channel_id, 'take': 100}, timeout=15)
        assert listed.status_code == 200 and len(listed.json()['items']) == 1
        delivered = admin.get(endpoint + 'deliveries', params={'channelId': channel_id}, timeout=15)
        assert delivered.status_code == 200 and delivered.json()['items'] == []
        assert all(token not in delivered.text for token in ('leaseToken', 'credentialReference', 'destinationReference'))
        # Check new write contracts without queuing an external send in production.
        test_path = 'channels/' + channel_id + '/test'
        assert write('POST', test_path, {'expectedChannelVersion': 0, 'confirmed': False}).status_code == 400
        assert write('POST', test_path, {'expectedChannelVersion': -1, 'confirmed': True}).status_code == 400
        assert write('POST', test_path, {'expectedChannelVersion': 0, 'confirmed': True}, extra={'Idempotency-Key': ''}).status_code == 400
        retry_path = 'deliveries/' + str(uuid.uuid4()) + '/retry-unknown'
        retry_body = {'expectedVersion': 0, 'expectedChannelVersion': 0, 'confirmed': True, 'acceptDuplicateRisk': False}
        assert write('POST', retry_path, retry_body).status_code == 400
        assert write('POST', retry_path, {**retry_body, 'acceptDuplicateRisk': True}).status_code == 404
        name = 'whaledeck-notification-validation-' + uuid.uuid4().hex[:10]
        password = 'Wd!' + secrets.token_urlsafe(24) + '9aA'
        created = identity.post(AUTHENTIK_API + 'core/users/', json={'username': name, 'name': 'Ephemeral notification acceptance', 'email': '', 'is_active': True, 'groups': []}, timeout=20)
        created.raise_for_status(); user_id = str(created.json()['pk'])
        identity.post(AUTHENTIK_API + f'core/users/{user_id}/set_password/', json={'password': password}, timeout=20).raise_for_status()
        ordinary, _ = authenticate(base, name, password, require_administrator=False)
        anonymous = requests.Session(); anonymous.trust_env = False
        for path in ('channels', 'routes', 'deliveries'):
            assert ordinary.get(endpoint + path, timeout=15, allow_redirects=False).status_code == 403
            assert anonymous.get(endpoint + path, timeout=15, allow_redirects=False).status_code == 401
        ordinary_csrf = ordinary.get(base + '/api/v1/auth/csrf', timeout=15).json()
        terminal_id = str(uuid.uuid4()); terminal_key = str(uuid.uuid4())
        terminal_headers = {ordinary_csrf['headerName']: ordinary_csrf['token'], 'Origin': base, 'Idempotency-Key': terminal_key}
        terminal_root = base + '/api/v1/terminals/'
        for suffix in (terminal_id, terminal_id + '/connect'):
            assert ordinary.get(terminal_root + suffix, headers={'Origin': base}, timeout=15, allow_redirects=False).status_code == 403
            assert anonymous.get(terminal_root + suffix, headers={'Origin': base}, timeout=15, allow_redirects=False).status_code == 401
        for suffix, body in (('host/grants', {'confirmed': True, 'idempotencyKey': terminal_key}), (terminal_id + '/close', {})):
            assert ordinary.post(terminal_root + suffix, json=body, headers=terminal_headers, timeout=15, allow_redirects=False).status_code == 403
            assert anonymous.post(terminal_root + suffix, json=body, headers={'Origin': base}, timeout=15, allow_redirects=False).status_code == 401
        for path, body in ((test_path, {'expectedChannelVersion': 0, 'confirmed': True}), (retry_path, {**retry_body, 'acceptDuplicateRisk': True})):
            ordinary_headers = {ordinary_csrf['headerName']: ordinary_csrf['token'], 'Origin': base, 'Idempotency-Key': str(uuid.uuid4())}
            assert ordinary.post(endpoint + path, json=body, headers=ordinary_headers, timeout=15, allow_redirects=False).status_code == 403
            assert anonymous.post(endpoint + path, json=body, headers={'Origin': base}, timeout=15, allow_redirects=False).status_code == 401
        for suffix, body in (('private/plan', {'expectedChannelVersion': 0, 'action': 'remove'}), ('private', {'planHash': 'invalid', 'confirmed': True}), ('private/acknowledge-unknown', {'operationId': str(uuid.uuid4()), 'expectedVersion': 0, 'confirmed': True, 'acceptUnknownOutcome': True})):
            private_path = endpoint + 'channels/' + channel_id + '/' + suffix
            assert ordinary.post(private_path, json=body, headers=ordinary_headers, timeout=15, allow_redirects=False).status_code == 403
            assert anonymous.post(private_path, json=body, headers={'Origin': base}, timeout=15, allow_redirects=False).status_code == 401
        assert ordinary.get(endpoint + 'channels/' + channel_id + '/private', timeout=15, allow_redirects=False).status_code == 403
        assert anonymous.get(endpoint + 'channels/' + channel_id + '/private/status', timeout=15, allow_redirects=False).status_code == 401
        doc = admin.get(base + '/openapi/v1.json', timeout=20)
        assert doc.status_code == 200
        assert all('/api/v1/notifications/manage/' + path in doc.json()['paths'] for path in ('channels', 'routes', 'deliveries'))
        assert '/api/v1/notifications/manage/channels/{id}/test' in doc.json()['paths']
        assert '/api/v1/notifications/manage/deliveries/{id}/retry-unknown' in doc.json()['paths']
        assert all('/api/v1/terminals/' + suffix in doc.json()['paths'] for suffix in ('host/grants', '{id}', '{id}/connect', '{id}/close'))
        delete_key = str(uuid.uuid4()); delete_body = {'expectedVersion': route_version, 'confirmed': True}
        assert write('DELETE', 'routes/' + route_id, delete_body, delete_key).status_code == 204
        assert write('DELETE', 'routes/' + route_id, delete_body, delete_key).status_code == 204
        route_id = None
        # Reload after routing changes, which intentionally increment the parent channel version.
        channels = admin.get(endpoint + 'channels', params={'take': 100}, timeout=15).json()['items']
        current = next(item for item in channels if item['id'] == channel_id)
        assert write('DELETE', 'channels/' + channel_id, {'expectedVersion': current['version'], 'confirmed': True}).status_code == 204
        channel_id = None
        print(json.dumps({'notificationManagementAcceptance': 'passed', 'realOidcCrudIdempotencyVersionsOriginCsrf': 'passed', 'adminOrdinaryAnonymousOpenApi': 'passed', 'terminalOrdinaryAnonymousReadGrantConnectClose': 'passed', 'externalSends': 0, 'temporaryConfigurationRemoved': True}))
    finally:
        try:
            if route_id is not None:
                response = admin.get(endpoint + 'routes', params={'channelId': channel_id, 'take': 100}, timeout=15)
                response.raise_for_status()
                for item in response.json()['items']:
                    if item['id'] == route_id:
                        assert write('DELETE', 'routes/' + route_id, {'expectedVersion': item['version'], 'confirmed': True}).status_code in (204, 404)
        finally:
            try:
                if channel_id is not None:
                    response = admin.get(endpoint + 'channels', params={'take': 100}, timeout=15)
                    response.raise_for_status()
                    for item in response.json()['items']:
                        if item['id'] == channel_id:
                            assert write('DELETE', 'channels/' + channel_id, {'expectedVersion': item['version'], 'confirmed': True}).status_code in (204, 404)
            finally:
                if user_id is not None:
                    assert identity.delete(AUTHENTIK_API + f'core/users/{user_id}/', timeout=20).status_code in (204, 404)


if __name__ == '__main__':
    main()
