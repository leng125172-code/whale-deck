"""Exercise the installed maintenance binary on isolated loopback/temporary state.

Never changes the production status file, stops Docker, or uses an identity token.
"""
import json
from pathlib import Path
import socket
import subprocess
import tempfile
import time

import requests


def main():
    client = requests.Session()
    client.trust_env = False
    ready = client.get('http://127.0.0.1:18080/health/live', timeout=5)
    assert ready.status_code == 200, 'Live API is not ready; no isolated maintenance test started'
    with tempfile.TemporaryDirectory(prefix='wd-maintenance-status-') as directory:
        state = Path(directory) / 'status.json'
        with socket.socket() as reservation:
            reservation.bind(('127.0.0.1', 0))
            port = reservation.getsockname()[1]
        base = 'http://127.0.0.1:' + str(port)
        environment = {
            'PATH': '/usr/bin:/bin', 'HOME': directory, 'ASPNETCORE_ENVIRONMENT': 'Production',
            'Maintenance__Urls': base, 'Maintenance__StatusPath': str(state),
            'HTTP_PROXY': 'http://127.0.0.1:1', 'HTTPS_PROXY': 'http://127.0.0.1:1', 'NO_PROXY': ''
        }
        process = subprocess.Popen(['/opt/whaledeck-maintenance/WhaleDeck.MaintenanceHost'],
            env=environment, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        try:
            response = None
            for _ in range(40):
                assert process.poll() is None, 'Isolated maintenance binary stopped before readiness'
                try:
                    response = client.get(base + '/maintenance/status', timeout=3)
                    break
                except requests.ConnectionError:
                    time.sleep(.1)
            assert response is not None and response.status_code == 200, 'Missing runtime state was not resolved from live backend'
            assert response.json()['state'] == 'healthy', 'Missing state returned a false outage'
            assert response.headers.get('Cache-Control') == 'no-store'
            state.write_text('{malformed', encoding='utf-8')
            response = client.get(base + '/maintenance/status', timeout=3)
            assert response.status_code == 503 and response.json()['phase'] == 'InvalidMaintenanceState'
            state.write_text(json.dumps({'state': 'updating', 'message': 'synthetic-public-progress', 'extra': 'synthetic-private-value'}), encoding='utf-8')
            response = client.get(base + '/maintenance/status', timeout=3)
            assert response.status_code == 200 and response.json()['state'] == 'updating'
            assert 'synthetic-private-value' not in response.text, 'Unexpected state fields leaked'
            state.write_text(json.dumps({'state': 'updating', 'extra': 'x' * 9000}), encoding='utf-8')
            response = client.get(base + '/maintenance/status', timeout=3)
            assert response.status_code == 503 and len(response.content) < 1024
            state.write_text('{"state":"healthy"}', encoding='utf-8')
            response = client.get(base + '/maintenance/status', timeout=3)
            assert response.status_code == 200 and response.json()['state'] == 'healthy' and response.json()['phase'] == 'Ready'
            print(json.dumps({'installedMaintenanceStatus': 'passed', 'missingRunFileReadyProbeProxyBypass': 'passed',
                'corruptAndOversizeFailClosedUnknownFieldsDropped': 'passed', 'productionStateUnchanged': True}))
        finally:
            process.terminate()
            try:
                process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=5)
    print(json.dumps({'isolatedMaintenanceProcessAndFilesCleaned': True}))


if __name__ == '__main__':
    main()
