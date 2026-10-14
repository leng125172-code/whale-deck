#!/usr/bin/env python3
"""Real OIDC/admin/ordinary inbox, CSRF and Worker alert delivery; no external send.

Creates a temporary Info rule and an ordinary Authentik account. Rule is disabled
and event recovered; history/audit/messages remain deliberately, account removed.
"""
import json
import os
import secrets
import time
import uuid
import requests

from run_with_ephemeral_admin import AUTHENTIK_API, container_environment, require
from validate_databases import Api
from validate_governance import request
from validate_oidc import authenticate, required_environment


def main():
    base = os.environ.get('WHALEDECK_URL', 'http://192.168.100.13:8080').rstrip('/')
    admin_session, _ = authenticate(base, required_environment('VALIDATION_USERNAME'), required_environment('VALIDATION_PASSWORD'))
    admin = Api(base, admin_session)
    anonymous = requests.Session()
    anonymous.trust_env = False
    assert anonymous.get(base + '/api/v1/notifications', allow_redirects=False, timeout=20).status_code == 401
    values = container_environment()
    identity = requests.Session()
    identity.trust_env = False
    identity.headers.update({'Authorization': 'Bearer ' + require(values, 'Authentication__Authentik__ApiToken')})
    name = 'whaledeck-inbox-validation-' + uuid.uuid4().hex[:10]
    password = 'Wd!' + secrets.token_urlsafe(24) + '9aA'
    user_id = None
    rule = None
    event_id = None
    try:
        created = identity.post(AUTHENTIK_API + 'core/users/', json={'username': name, 'name': 'Ephemeral inbox acceptance', 'email': '', 'is_active': True, 'groups': []}, timeout=20)
        created.raise_for_status()
        user_id = str(created.json()['pk'])
        identity.post(AUTHENTIK_API + f'core/users/{user_id}/set_password/', json={'password': password}, timeout=20).raise_for_status()
        ordinary_session, _ = authenticate(base, name, password, require_administrator=False)
        ordinary = Api(base, ordinary_session)
        me = request(ordinary, 'GET', 'auth/me').json()
        assert me['isAdministrator'] is False
        empty = request(ordinary, 'GET', 'notifications/summary').json()
        assert empty['unreadCount'] == 0 and empty['totalCount'] == 0
        rule = request(admin, 'POST', 'alerts/rules', payload={'ruleType': 'LoadAverage',
            'resourceSelectorJson': '{"resourceIds":["local"]}', 'thresholdJson': '{"value":0,"recoveryValue":0}',
            'evaluationWindowSeconds': 12, 'severity': 'Info', 'isEnabled': True, 'expectedVersion': None}).json()
        deadline = time.monotonic() + 60
        notification = None
        while time.monotonic() < deadline:
            events = request(admin, 'GET', 'alerts?includeRecovered=true').json()
            match = next((item for item in events if item['ruleId'] == rule['id'] and item['state'] == 'Active'), None)
            if match:
                event_id = match['id']
                inbox = request(admin, 'GET', 'notifications?kind=Alert&take=100').json()
                notification = next((item for item in inbox['items'] if item['sourceId'] == event_id and item['messageCode'] == 'ALERT_ACTIVE'), None)
                if notification: break
            time.sleep(2)
        assert notification is not None, 'Real Worker alert did not produce inbox event'
        note_id = notification['id']
        assert notification['readAtUtc'] is None and notification['targetPath'] == '/alerts'
        assert request(ordinary, 'GET', 'notifications').json()['items'] == []
        request(ordinary, 'POST', f'notifications/{note_id}/read-state', payload={'isRead': True}, expected=404)
        request(ordinary, 'POST', f'notifications/{uuid.uuid4()}/read-state', payload={'isRead': True}, expected=404)
        no_csrf = admin_session.post(base + f'/api/v1/notifications/{note_id}/read-state', json={'isRead': True}, headers={'Origin': base}, allow_redirects=False, timeout=20)
        assert no_csrf.status_code in (400, 403)
        wrong_origin = admin_session.post(base + f'/api/v1/notifications/{note_id}/read-state', json={'isRead': True}, headers={**admin.headers, 'Origin': 'http://unapproved.invalid'}, allow_redirects=False, timeout=20)
        assert wrong_origin.status_code in (400, 403)
        first = request(admin, 'POST', f'notifications/{note_id}/read-state', payload={'isRead': True}).json()
        again = request(admin, 'POST', f'notifications/{note_id}/read-state', payload={'isRead': True}).json()
        assert first == again and first['isRead'] is True
        request(admin, 'POST', f'notifications/{note_id}/read-state', payload={'isRead': False})
        summary = request(admin, 'GET', 'notifications/summary')
        assert 'no-store' in summary.headers.get('Cache-Control', '')
        # Ordinary subjects cannot consume administrator snapshot ranges.
        request(ordinary, 'POST', 'notifications/read-all', payload={'throughSequence': summary.json()['snapshotSequence']}, expected=400)
        assert request(ordinary, 'GET', 'notifications/summary').json()['unreadCount'] == 0
    finally:
        try:
            if rule:
                request(admin, 'POST', 'alerts/rules', payload={**rule, 'isEnabled': False, 'expectedVersion': rule['version']})
        finally:
            # Identity cleanup must still execute if rule cleanup itself fails.
            if user_id:
                deleted = identity.delete(AUTHENTIK_API + f'core/users/{user_id}/', timeout=20)
                assert deleted.status_code in (204, 404)
    if event_id:
        deadline = time.monotonic() + 60
        while time.monotonic() < deadline:
            inbox = request(admin, 'GET', 'notifications?kind=Alert&take=100').json()['items']
            if any(item['sourceId'] == event_id and item['messageCode'] == 'ALERT_RECOVERED' for item in inbox): break
            time.sleep(2)
        else: raise RuntimeError('Disabled temporary rule did not produce recovery notification')
    print(json.dumps({'notificationInboxAcceptance': 'passed', 'realAuthentikAdminOrdinaryAnonymous': 'passed',
        'workerTriggerRecoveryCsrfOriginHiddenIdempotenceNoStore': 'passed',
        'temporaryRule': 'disabled-and-recovered', 'ordinaryAccount': 'deleted',
        'externalDeliveries': 0, 'historyAuditAndMessages': 'retained'}))


if __name__ == '__main__':
    main()
