#!/usr/bin/env python3
"""Real administrator Cookie/BFF/Agent reads from one immutable, private test container.

Run through run_with_ephemeral_admin. No production bind/volume/port, container
Exec, arbitrary command, file mutation, token output or browser signoff.
"""
import contextlib
import hashlib
import io
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tarfile
import uuid

import requests
from validate_databases import Api
from validate_oidc import authenticate, required_environment

IMAGE = 'sha256:884e0b7f42f303641b0f4e78755aea647521dca0b89ca3be7992ad78b5c07434'
PHASE = 'startup'


def command(arguments):
    return subprocess.run(arguments, check=True, capture_output=True, timeout=30).stdout


def main():
    global PHASE
    assert os.geteuid() == 0 and len(sys.argv) == 2 and re.fullmatch('[a-f0-9]{40}', sys.argv[1])
    revision = sys.argv[1]
    assert json.loads(command(['/opt/whaledeck-agent/WhaleDeck.Agent', '--build-info']))['sourceRevision'] == revision
    base = os.environ.get('WHALEDECK_URL', 'http://192.168.100.13:8080').rstrip('/')
    with contextlib.redirect_stdout(io.StringIO()):
        session, _ = authenticate(base, required_environment('VALIDATION_USERNAME'), required_environment('VALIDATION_PASSWORD'))
    api = Api(base, session)
    name = 'wdv-container-bff-' + uuid.uuid4().hex[:16]
    identifier = None
    image = json.loads(command(['docker', 'image', 'inspect', IMAGE]))[0]
    assert image['Id'] == IMAGE and not image['Config'].get('Volumes')
    completed = False
    try:
        PHASE = 'owned-immutable-container'
        identifier = command(['docker', 'run', '--detach', '--name', name, '--network', 'none', '--read-only',
            '--cap-drop', 'ALL', '--security-opt', 'no-new-privileges', '--user', '1000:1000', '--pids-limit', '16',
            '--memory', '32m', '--cpus', '0.25', '--log-driver', 'local', '--log-opt', 'max-size=1m', '--log-opt', 'max-file=2',
            '--label', 'io.whaledeck.validation=' + name, '--entrypoint', '/bin/sleep', IMAGE, '600']).decode().strip()
        assert re.fullmatch('[a-f0-9]{64}', identifier)
        # Independent Docker archive reads exact immutable image content; it
        # does not rely on the new Agent decoder to define expected bytes.
        archive = command(['docker', 'cp', identifier + ':/usr/share/nginx/html/index.html', '-'])
        assert len(archive) <= 1024**2
        with tarfile.open(fileobj=io.BytesIO(archive)) as bundle:
            members = bundle.getmembers(); assert len(members) == 1
            member = members[0]; assert member.isfile() and member.name == 'index.html' and 0 < member.size <= 65536
            expected = bundle.extractfile(member).read(); assert len(expected) == member.size
        endpoint = base + '/api/v1/containers/' + identifier + '/files/'
        def get(action, status=200, **kwargs):
            response = session.get(endpoint + action, allow_redirects=False, timeout=40, **kwargs)
            assert response.status_code == status, 'unexpected-read-status'
            return response
        PHASE = 'real-target-list-and-metadata'
        target = get('target').json(); assert target['containerId'] == identifier and target['imageId'] == IMAGE
        instance = target['instanceId']; assert re.fullmatch('[1-9][0-9]*:[0-9TZ:.-]+', instance)
        location = {'instanceId': instance, 'path': 'usr/share/nginx/html/index.html'}
        page = get('entries', params={'instanceId': instance, 'path': 'usr/share/nginx/html', 'take': 100}).json()
        assert any(row['name'] == 'index.html' and row['kind'] == 'File' for row in page['entries'])
        metadata = get('entry', params=location).json(); assert metadata['sizeBytes'] == len(expected) and not metadata['isProtected']
        version = metadata['version']; assert re.fullmatch('[A-F0-9]{64}', version)
        PHASE = 'real-complete-download-and-refusals'
        response = get('download', params={**location, 'expectedVersion': version})
        assert response.content == expected and response.headers['Content-Length'] == str(len(expected))
        assert 'no-store' in response.headers.get('Cache-Control', '') and response.headers.get('X-Content-Type-Options') == 'nosniff'
        assert 'attachment' in response.headers.get('Content-Disposition', '')
        get('download', 409, params={**location, 'expectedVersion': 'F'*64})
        get('download', 400, params={**location, 'expectedVersion': version}, headers={'Range': 'bytes=0-1'})
        get('entry', 400, params={**location, 'path': '../outside'})
        get('entry', 409, params={**location, 'instanceId': '2:' + instance.split(':', 1)[1]})
        get('entry', 403, params={**location, 'path': 'etc/shadow'})
        core_id = command(['docker', 'inspect', '--format', '{{.Id}}', 'whaledeck-api']).decode().strip()
        denied = session.get(base + '/api/v1/containers/' + core_id + '/files/target', allow_redirects=False, timeout=20)
        assert denied.status_code == 403
        anonymous = requests.Session(); anonymous.trust_env = False
        assert anonymous.get(endpoint + 'target', allow_redirects=False, timeout=20).status_code == 401
        PHASE = 'durable-private-audit'
        me = session.get(base + '/api/v1/auth/me', timeout=20); assert me.status_code == 200 and me.json()['isAdministrator']
        audit_response = session.get(base + '/api/v1/audit', params={'actorSubject': me.json()['subject'], 'take': 100}, timeout=20)
        assert audit_response.status_code == 200
        audits = [row for row in audit_response.json() if row['action'].startswith('containers.files.') and row['targetId'] == identifier]
        assert any(row['action'] == 'containers.files.download' and row['result'] == 'Validated' for row in audits)
        assert any(row['action'] == 'containers.files.download' and row['result'] == 'Incomplete' for row in audits)
        for row in audits:
            detail = row.get('detailJson') or '{}'
            assert 'index.html' not in detail and instance not in detail and expected.decode(errors='replace') not in detail
        assert get('entry', params=location).json()['version'] == version
        PHASE = 'normal-cookie-logout'
        logged_out = session.post(base + '/api/v1/auth/logout', headers=api.headers, allow_redirects=False, timeout=20)
        assert logged_out.status_code in (302, 303)
        assert session.get(endpoint + 'target', allow_redirects=False, timeout=20).status_code == 401
        completed = True
        print(json.dumps({'containerFileBff': 'passed', 'revision': revision, 'bytes': len(expected),
            'realCookieMaintenanceGatewaySqlAndUds': True, 'independentImageContentMatch': True,
            'coreAnonymousPathInstanceVersionAndRangeRefused': True, 'privateAuditAndCookieLogout': True,
            'ordinaryUserRevocationBrowserOrLargeTransfer': False}), flush=True)
    finally:
        if identifier is not None:
            PHASE = 'owned-container-cleanup'
            owned = json.loads(command(['docker', 'inspect', identifier]))[0]
            assert owned['Id'] == identifier and owned['Name'] == '/' + name and owned['Image'] == IMAGE
            assert owned['Config']['Labels'].get('io.whaledeck.validation') == name
            assert not owned['Mounts'] and not owned['HostConfig'].get('Binds') and owned['HostConfig']['NetworkMode'] == 'none'
            command(['docker', 'rm', '--force', identifier])
            absent = subprocess.run(['docker', 'inspect', identifier], capture_output=True, timeout=10)
            assert absent.returncode != 0
            print(json.dumps({'ownedContainerRemoved': True, 'validationPassed': completed}), flush=True)


if __name__ == '__main__':
    try: main()
    except BaseException as error:
        print(json.dumps({'containerFileBff': 'failed', 'phase': PHASE, 'failureType': type(error).__name__}), file=sys.stderr)
        raise SystemExit(1) from None
