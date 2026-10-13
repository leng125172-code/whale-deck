#!/usr/bin/env python3
"""Real Authentik/API/Worker alert acceptance using existing low-risk measurements.

Only temporary alert rules and ephemeral users are created. No metric rewrites,
host load generation, disk saturation or container lifecycle mutations.
"""
import json
import os
import time
from datetime import datetime, timedelta, timezone
import requests
from validate_databases import Api
from validate_governance import request
from validate_metrics_history import query
from validate_oidc import authenticate, required_environment


def main():
    base = os.environ.get('WHALEDECK_URL', 'http://192.168.100.13:8080').rstrip('/')
    session, _ = authenticate(base, required_environment('VALIDATION_USERNAME'), required_environment('VALIDATION_PASSWORD'))
    api = Api(base, session)
    endpoint = base + '/api/v1/alerts'
    anonymous = requests.Session(); anonymous.trust_env = False
    assert anonymous.get(endpoint, timeout=20, allow_redirects=False).status_code == 401
    ticket = api.stage_secret(required_environment('VALIDATION_MANAGED_PASSWORD'))
    api.run('create-user', '', {'username': required_environment('VALIDATION_MANAGED_USERNAME'),
        'name': 'Temporary ordinary alert-validation user', 'email': '', 'inputTicket': ticket, 'inputKind': 'password'}, area='identity')
    ordinary, profile = authenticate(base, required_environment('VALIDATION_MANAGED_USERNAME'), required_environment('VALIDATION_MANAGED_PASSWORD'), require_administrator=False)
    assert profile.get('isAdministrator') is False
    for path in ('alerts', 'alerts/rules', 'alerts/history'):
        assert ordinary.get(base + '/api/v1/' + path, timeout=20, allow_redirects=False).status_code == 403
    host_id = query("SELECT to_json(id) FROM managed_resources WHERE resource_type='Host' AND external_id='local'")
    metrics = request(api, 'GET', 'overview').json()['metrics']
    device = next(item['deviceId'] for item in metrics if item['kind'] == 'disk.utilization')
    payload = {'ruleType': 'DiskUtilization', 'resourceSelectorJson': json.dumps({'resourceIds': ['local'], 'deviceIds': [device]}),
        'thresholdJson': json.dumps({'value': 0, 'criticalValue': 100, 'recoveryValue': 0}), 'evaluationWindowSeconds': 30,
        'severity': 'Info', 'isEnabled': True, 'expectedVersion': None}
    for invalid in (dict(payload, thresholdJson='{"value":90,"operator":"<"}'),
                    dict(payload, resourceSelectorJson='{"arbitrary":true}')):
        rejected = session.post(base + '/api/v1/alerts/rules', json=invalid, headers=api.headers, timeout=20)
        assert rejected.status_code == 400 and rejected.json().get('traceId')
    current = request(api, 'POST', 'alerts/rules', payload=payload).json()
    rule_id = current['id']
    def wait_event(predicate):
        deadline = time.monotonic() + 75
        while time.monotonic() < deadline:
            events = [item for item in request(api, 'GET', 'alerts?includeRecovered=true').json() if item['ruleId'] == rule_id]
            if len(events) == 1 and predicate(events[0]): return events[0]
            time.sleep(3)
        raise RuntimeError('Temporary alert did not reach the required real Worker state within its budget')
    try:
        alarm = wait_event(lambda item: item['state'] == 'Active')
        assert alarm['resourceId'] == host_id
        detail = alarm['monitoring']
        assert detail['triggered'] is True and 0 <= detail['value'] <= 100 and detail['coveragePercent'] >= 80
        acknowledged = request(api, 'POST', f"alerts/{alarm['id']}/acknowledge", payload={}).json()
        assert acknowledged['state'] == 'Acknowledged'
        silenced = request(api, 'POST', f"alerts/{alarm['id']}/silence", payload={'untilUtc': (datetime.now(timezone.utc) + timedelta(hours=1)).isoformat()}).json()
        assert silenced['state'] == 'Silenced'
        current = request(api, 'POST', 'alerts/rules', payload=dict(current,
            thresholdJson='{"value":0,"recoveryValue":0,"criticalValue":0}', expectedVersion=current['version'])).json()
        alarm = wait_event(lambda item: item['severity'] == 'Critical' and item['state'] == 'Silenced')
        assert alarm['id'] == silenced['id']
        current = request(api, 'POST', 'alerts/rules', payload=dict(current, isEnabled=False, expectedVersion=current['version'])).json()
        recovered = wait_event(lambda item: item['state'] == 'Recovered')
        assert recovered['monitoring']['reason'] == 'RuleDisabled'
        states = [item['state'] for item in request(api, 'GET', f"alerts/history?alertEventId={alarm['id']}&take=100").json()]
        assert {'Active', 'Acknowledged', 'Silenced', 'SeverityChanged', 'Recovered'}.issubset(states)
        print('Live monitoring alerts passed: real scoped measurement, dedup, acknowledge, silence, escalation, explicit disable/recovery and history; anonymous 401/ordinary 403/admin cookie+CSRF')
    finally:
        if current['isEnabled']:
            request(api, 'POST', 'alerts/rules', payload=dict(current, isEnabled=False, expectedVersion=current['version']))


if __name__ == '__main__':
    main()
