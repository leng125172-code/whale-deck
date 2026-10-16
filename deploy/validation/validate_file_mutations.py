#!/usr/bin/env python3
"""Real OIDC/BFF/SQL/Worker/UDS file mutations, exact owned fixtures only.

Run only after the 1.23 Agent, API migration and Worker have been deployed.
Never prints cookies, capabilities, plans, private metadata or exception text.
"""
import hashlib
import json
import os
from pathlib import Path
import stat
import time
import traceback
import uuid
import requests
from validate_databases import Api
from validate_oidc import authenticate, required_environment

PHASE = 'startup'
ROOT = 'whaledeck.files.shared'
DIRECTORY = Path('/data/WhaleDeck/files')


def main():
    global PHASE
    assert os.geteuid() == 0 and DIRECTORY.is_dir() and not DIRECTORY.is_symlink(), 'owned-fixture-validation-required'
    base = os.environ.get('WHALEDECK_URL', 'http://192.168.100.13:8080').rstrip('/')
    session, _ = authenticate(base, required_environment('VALIDATION_USERNAME'), required_environment('VALIDATION_PASSWORD'))
    api = Api(base, session); marker = 'wdv-bff-mutation-' + uuid.uuid4().hex
    content = os.urandom(65543); digest = hashlib.sha256(content).hexdigest().upper()
    folder = marker + '.dir'; original = marker + '.source'; copied = folder + '/' + marker + '.copy'; moved = folder + '/' + marker + '.moved'
    operations = []; upload_id = None; created_directory_identity = None
    assert all(not os.path.lexists(DIRECTORY / relative) for relative in (folder, original)), 'fixture-name-conflict'

    def get(path, expected=200, **kwargs):
        response = session.get(base + '/api/v1/' + path, timeout=30, **kwargs)
        assert response.status_code == expected, 'unexpected-read-status'
        return response

    def post(path, body, expected=200, key=None):
        response = session.post(base + '/api/v1/' + path, json=body, headers={**api.headers, **({'Idempotency-Key': key} if key else {})}, timeout=40)
        assert response.status_code == expected, 'unexpected-write-status'
        return response

    def inspect(path): return get('files/entry', params=dict(rootId=ROOT, path=path)).json()

    def command(kind, source=None, target=None, mode='', version='ABSENT'):
        return dict(kind=kind, source=None if source is None else dict(rootId=ROOT, path=source), target=None if target is None else dict(rootId=ROOT, path=target), expectedVersion=version, mode=mode)

    def enqueue(value):
        planned = post('files/operations/plan', value).json(); key = str(uuid.uuid4())
        body = dict(planHash=planned['planHash'], confirmed=True, idempotencyKey=key)
        created = post('files/operations', body, 202, key).json(); operations.append(created['id'])
        assert post('files/operations', body, 202, key).json()['id'] == created['id']
        assert post('files/operations/resolve', body, 200, key).json()['id'] == created['id']
        return created

    def wait(identifier):
        deadline = time.monotonic() + 90
        while time.monotonic() < deadline:
            row = get('files/operations/' + identifier).json()
            if row['state'] in ('Succeeded', 'Superseded', 'Failed', 'Canceled', 'Unknown', 'Acknowledged'): return row
            time.sleep(0.4)
        raise RuntimeError('mutation-terminal-timeout')

    try:
        PHASE = 'capabilities-csrf-origin-protection-and-contract'
        assert any(item['id'] == ROOT and 'files-mutate' in item['actions'] for item in get('files/roots').json())
        anonymous = requests.Session(); anonymous.trust_env = False
        assert anonymous.get(base + '/api/v1/files/operations/' + str(uuid.uuid4()), timeout=15).status_code == 401
        normal = command('mkdir', target=folder, mode='0750')
        assert session.post(base + '/api/v1/files/operations/plan', json=normal, timeout=15).status_code in (400, 403)
        assert session.post(base + '/api/v1/files/operations/plan', json=normal, headers={**api.headers, 'Origin': 'http://foreign.invalid'}, timeout=15).status_code == 403
        post('files/operations/plan', command('mkdir', target='.env', mode='0750'), 403)
        post('files/operations/plan', command('mkdir', target='../escape', mode='0750'), 400)
        post('files/operations/plan', command('mkdir', target=folder, mode='4755'), 400)
        schema = session.get(base + '/openapi/v1.json', timeout=30); assert schema.status_code == 200
        assert all('/api/v1/files/operations' + suffix in schema.json()['paths'] for suffix in ('', '/plan', '/resolve', '/{id}', '/{id}/cancel', '/{id}/acknowledge'))

        PHASE = 'directory-and-upload-through-durable-worker'
        created = enqueue(normal); assert wait(created['id'])['state'] == 'Succeeded'
        directory_info = (DIRECTORY / folder).lstat(); created_directory_identity = (directory_info.st_dev, directory_info.st_ino)
        assert stat.S_IMODE((DIRECTORY / folder).stat().st_mode) == 0o750
        upload_plan = post('files/uploads/plan', dict(rootId=ROOT, path=original, sizeBytes=len(content), sha256=digest, expectedVersion='ABSENT')).json()
        key = str(uuid.uuid4()); upload = post('files/uploads', dict(planHash=upload_plan['planHash'], confirmed=True, idempotencyKey=key), 202, key).json(); upload_id = upload['id']
        response = session.put(base + '/api/v1/files/uploads/' + upload_id + '/content', data=content, headers={**api.headers, 'Content-Type': 'application/octet-stream', 'Idempotency-Key': upload_id}, timeout=90)
        assert response.status_code == 202
        deadline = time.monotonic() + 60
        while time.monotonic() < deadline:
            result = get('files/uploads/' + upload_id).json()
            if result['state'] == 'Succeeded': break
            assert result['state'] not in ('Failed', 'Canceled', 'Unknown'); time.sleep(0.4)
        else: raise RuntimeError('upload-terminal-timeout')

        PHASE = 'copy-chmod-move-full-bytes-and-retained-source'
        copy = enqueue(command('copy', original, copied, version=inspect(original)['version']))
        assert wait(copy['id'])['state'] == 'Succeeded'; assert (DIRECTORY / copied).read_bytes() == content
        permission = enqueue(command('chmod', copied, mode='0640', version=inspect(copied)['version']))
        assert wait(permission['id'])['state'] == 'Succeeded'; assert stat.S_IMODE((DIRECTORY / copied).stat().st_mode) == 0o640
        move = enqueue(command('move', copied, moved, version=inspect(copied)['version']))
        result = wait(move['id']); assert result['state'] == 'Succeeded' and result['sourceRetained']
        assert not (DIRECTORY / copied).exists() and (DIRECTORY / moved).read_bytes() == content and (DIRECTORY / original).read_bytes() == content
        retained = DIRECTORY / folder / '.whaledeck-files' / (uuid.UUID(move['id']).hex + '.mutation')
        assert retained.read_bytes() == content
        # Repeating the original HTTP intent cannot revive its moved target.
        assert get('files/operations/' + copy['id']).json()['state'] == 'Succeeded'
        assert not (DIRECTORY / copied).exists()

        PHASE = 'other-login-stale-plan-and-human-audit'
        other, _ = authenticate(base, required_environment('VALIDATION_USERNAME'), required_environment('VALIDATION_PASSWORD'))
        other_api = Api(base, other)
        assert other.get(base + '/api/v1/files/operations/' + move['id'], timeout=15).status_code == 404
        planned = post('files/operations/plan', command('chmod', original, mode='0600', version=inspect(original)['version'])).json()
        key = str(uuid.uuid4())
        assert other.post(base + '/api/v1/files/operations', json=dict(planHash=planned['planHash'], confirmed=True, idempotencyKey=key), headers={**other_api.headers, 'Idempotency-Key': key}, timeout=20).status_code == 403
        # Only this owned fixture is changed to prove source-version rejection.
        current_mode = stat.S_IMODE((DIRECTORY / original).stat().st_mode); (DIRECTORY / original).chmod(current_mode ^ 0o040)
        post('files/operations', dict(planHash=planned['planHash'], confirmed=True, idempotencyKey=key), 409, key)
        subject = get('auth/me').json()['subject']; audits = get('audit', params=dict(actorSubject=subject, take=500)).json()
        for identifier in operations:
            assert any(item.get('jobId') == identifier and item['action'] == 'files.mutation.accepted' for item in audits)
            assert any(item.get('jobId') == identifier and item['action'] == 'files.mutation.result' for item in audits)
        assert all(marker not in (item.get('detailJson') or '') for item in audits)
        print(json.dumps(dict(realFileMutationBff='passed', binaryBytes=len(content), directoryCopyMovePermissions='passed',
            oidcSqlWorkerUdsHumanAudit='passed', idempotencyLoginCsrfOriginVersionProtection='passed',
            unknownFaultInjection='not-performed-on-production', directoryTreesHddAndTenantIsolation='not-covered')))
    finally:
        # Never erase evidence while the Worker may still be applying it.
        for identifier in operations:
            row = get('files/operations/' + identifier).json()
            if row['state'] not in ('Succeeded', 'Superseded', 'Failed', 'Canceled', 'Acknowledged'):
                post('files/operations/' + identifier + '/cancel', {}, 202, identifier)
                assert wait(identifier)['state'] in ('Succeeded', 'Superseded', 'Failed', 'Canceled'), 'owned-operation-still-uncertain'
        if upload_id is not None:
            row = get('files/uploads/' + upload_id).json()
            if row['state'] not in ('Succeeded', 'Superseded', 'Failed', 'Canceled'):
                post('files/uploads/' + upload_id + '/cancel', {}, 202, upload_id)
                raise RuntimeError('owned-upload-cleanup-awaiting-receipt')
        for relative in (original, copied, moved):
            target = DIRECTORY / relative
            if target.exists():
                assert target.is_file() and not target.is_symlink() and target.read_bytes() == content and marker in target.name
                target.unlink()
        private = DIRECTORY / folder / '.whaledeck-files'
        for identifier in operations:
            target = private / (uuid.UUID(identifier).hex + '.mutation')
            if target.exists():
                assert not target.is_symlink() and target.read_bytes() == content; target.unlink()
        if private.exists():
            assert not private.is_symlink() and not any(private.iterdir()); private.rmdir()
        directory = DIRECTORY / folder
        if directory.exists():
            current = directory.lstat(); assert (current.st_dev, current.st_ino) == created_directory_identity
            assert directory.name == folder and not directory.is_symlink() and not any(directory.iterdir()); directory.rmdir()
        print(json.dumps(dict(ownedFileMutationFixturesRemoved=True, immutableIntentAuditAndRootReceiptsRetained=True)))


if __name__ == '__main__':
    try: main()
    except Exception as error:
        frames = traceback.extract_tb(error.__traceback__)
        print(json.dumps(dict(fileMutationAcceptance='failed', phase=PHASE, failureType=type(error).__name__, sourceLine=frames[-1].lineno if frames else None)))
        raise SystemExit(1) from None
