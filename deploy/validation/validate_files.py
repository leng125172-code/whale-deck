#!/usr/bin/env python3
"""Real Cookie -> maintenance -> gateway -> API -> SQL/Worker -> UDS/files.

Root validation process only for inspecting/cleaning its exact owned fixtures.
No production file, credential, cookie, capability or payload is printed.
"""
import hashlib
import io
import json
import os
from pathlib import Path
import re
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
    assert os.geteuid() == 0, 'root-validation-required-for-owned-fixture-cleanup'
    base = os.environ.get('WHALEDECK_URL', 'http://192.168.100.13:8080').rstrip('/')
    session, _ = authenticate(base, required_environment('VALIDATION_USERNAME'), required_environment('VALIDATION_PASSWORD'))
    api = Api(base, session); operations = []; owned = {}
    def get(path, status=200, **kwargs):
        result = session.get(base + '/api/v1/' + path, timeout=25, **kwargs)
        assert result.status_code == status, 'unexpected-read-status'
        return result
    def post(path, body, status=200, key=None, target=session):
        result = target.post(base + '/api/v1/' + path, json=body, headers={**api.headers, **({'Idempotency-Key': key} if key else {})}, timeout=25)
        assert result.status_code == status, 'unexpected-write-status'
        return result
    def digest(content): return hashlib.sha256(content).hexdigest().upper()
    def name(content):
        value = 'wdv-bff-file-' + uuid.uuid4().hex + '.bin'; owned[value] = {digest(content)}; return value
    def plan(filename, content, expected='ABSENT', checksum=None):
        return post('files/uploads/plan', dict(rootId=ROOT, path=filename, sizeBytes=len(content), sha256=checksum or digest(content), expectedVersion=expected)).json()['planHash']
    def create(planned):
        key = str(uuid.uuid4()); body = dict(planHash=planned, confirmed=True, idempotencyKey=key)
        result = post('files/uploads', body, 202, key).json(); operations.append(result['id'])
        assert post('files/uploads', body, 202, key).json()['id'] == result['id']
        return result
    def wait(identifier, expected):
        deadline = time.monotonic() + 90
        while time.monotonic() < deadline:
            value = get('files/uploads/' + identifier).json()
            if value['state'] in ('Succeeded', 'Superseded', 'Failed', 'Canceled', 'Unknown'):
                assert value['state'] == expected, 'unexpected-intent-terminal'; return value
            time.sleep(0.5)
        raise RuntimeError('intent-terminal-timeout')
    def send(intent, content, expected=202, idle=False):
        class SlowBytes(io.BytesIO):
            calls = 0
            def read(self, *args):
                self.calls += 1
                if self.calls == 2: time.sleep(7)
                return super().read(*args)
        value = SlowBytes(content) if idle else content
        response = session.put(base + '/api/v1/files/uploads/' + intent['id'] + '/content', data=value,
            headers={**api.headers, 'Content-Type': 'application/octet-stream', 'Idempotency-Key': intent['id']}, timeout=120)
        if response.status_code != expected:
            try: code = response.json().get('code', '')
            except (ValueError, AttributeError): code = ''
            print(json.dumps(dict(binaryFailureStatus=response.status_code, expectedStatus=expected,
                problemCode=code if isinstance(code, str) and re.fullmatch(r'[A-Z0-9_]{1,80}', code) else 'withheld')), flush=True)
        assert response.status_code == expected, 'unexpected-binary-status'
        return response
    def inspect(filename): return get('files/entry', params=dict(rootId=ROOT, path=filename)).json()
    try:
        PHASE = 'roots-permission-origin-and-openapi'
        roots = get('files/roots').json(); assert any(item['id'] == ROOT for item in roots)
        assert all(set(item) == {'id', 'name', 'actions'} for item in roots)
        anonymous = requests.Session(); anonymous.trust_env = False
        assert anonymous.get(base + '/api/v1/files/roots', allow_redirects=False, timeout=15).status_code == 401
        for root, path in ((ROOT, '.env'), ('database-platform.repository', '.env')):
            get('files/entry', 403, params=dict(rootId=root, path=path))
        get('files/entries', 400, params=dict(rootId=ROOT, path='../escape'))
        dummy = dict(rootId=ROOT, path='wdv-never-uploaded.bin', sizeBytes=0, sha256='A' * 64, expectedVersion='ABSENT')
        assert session.post(base + '/api/v1/files/uploads/plan', json=dummy, timeout=15).status_code in (400, 403)
        assert session.post(base + '/api/v1/files/uploads/plan', json=dummy, headers={**api.headers, 'Origin': 'http://foreign.invalid'}, timeout=15).status_code == 403
        schema = session.get(base + '/openapi/v1.json', timeout=30); assert schema.status_code == 200
        binary = schema.json()['paths']['/api/v1/files/uploads/{id}/content']['put']['requestBody']['content']
        assert 'application/octet-stream' in binary

        PHASE = 'large-proxied-stage-worker-and-verified-download'
        content = os.urandom(33 * 1024 * 1024 + 7); filename = name(content)
        initial = create(plan(filename, content)); assert not (DIRECTORY / filename).exists()
        send(initial, content, idle=True); published = wait(initial['id'], 'Succeeded')
        assert digest((DIRECTORY / filename).read_bytes()) == digest(content)
        metadata = inspect(filename); assert metadata['sizeBytes'] == len(content) and metadata['version'] == published['publishedVersion']
        with get('files/download', params=dict(rootId=ROOT, path=filename, expectedVersion=metadata['version']), stream=True) as downloaded:
            checksum = hashlib.sha256(); size = 0
            assert downloaded.headers['Content-Length'] == str(len(content))
            for part in downloaded.iter_content(32768): checksum.update(part); size += len(part)
            assert size == len(content) and checksum.hexdigest().upper() == digest(content)
        get('files/download', 409, params=dict(rootId=ROOT, path=filename, expectedVersion='0' * 64))

        PHASE = 'login-bound-plan-and-intent'
        other_login, _ = authenticate(base, required_environment('VALIDATION_USERNAME'), required_environment('VALIDATION_PASSWORD'))
        other_api = Api(base, other_login)
        assert other_login.get(base + '/api/v1/files/uploads/' + initial['id'], timeout=15).status_code == 404
        replacement = b'owned-validation-replacement-only\n'; owned[filename].add(digest(replacement))
        planned = plan(filename, replacement, metadata['version'])
        other_key = str(uuid.uuid4())
        assert other_login.post(base + '/api/v1/files/uploads', json=dict(planHash=planned, confirmed=True, idempotencyKey=other_key),
            headers={**other_api.headers, 'Idempotency-Key': other_key}, timeout=15).status_code == 403
        changed = create(planned); send(changed, replacement); updated = wait(changed['id'], 'Succeeded')
        assert updated['previousRetained'] and (DIRECTORY / filename).read_bytes() == replacement
        previous = DIRECTORY / '.whaledeck-files' / (uuid.UUID(changed['id']).hex + '.upload')
        assert digest(previous.read_bytes()) == digest(content)
        assert get('files/uploads/' + initial['id']).json()['state'] == 'Succeeded'
        send(initial, content, expected=409)  # A second receive is never automatic replay/publication.
        assert (DIRECTORY / filename).read_bytes() == replacement

        PHASE = 'cancel-before-receive-and-checksum-rejection'
        canceled_name = name(b'cancel'); canceled = create(plan(canceled_name, b'cancel'))
        post('files/uploads/' + canceled['id'] + '/cancel', {}, 202, canceled['id']); wait(canceled['id'], 'Canceled')
        send(canceled, b'cancel', expected=409); assert not (DIRECTORY / canceled_name).exists()
        receipt = json.loads((Path('/var/lib/whaledeck-files') / (uuid.UUID(canceled['id']).hex + '.json')).read_text())
        assert receipt['state'] == 'Aborted' and receipt.get('canceledBeforeReceive')
        bad_name = name(b'bad'); bad = create(plan(bad_name, b'bad', checksum='F' * 64))
        send(bad, b'bad', expected=409); wait(bad['id'], 'Failed'); assert not (DIRECTORY / bad_name).exists()

        PHASE = 'stale-plan-and-durable-audit'
        stale = plan(filename, b'new', inspect(filename)['version'])
        own_change = b'owned-external-change-only\n'; owned[filename].add(digest(own_change)); (DIRECTORY / filename).write_bytes(own_change)
        key = str(uuid.uuid4()); post('files/uploads', dict(planHash=stale, confirmed=True, idempotencyKey=key), 409, key)
        assert (DIRECTORY / filename).read_bytes() == own_change
        subject = get('auth/me').json()['subject']
        audits = get('audit', params=dict(actorSubject=subject, take=500)).json()
        assert any(item['action'] == 'files.upload.accepted' for item in audits)
        assert any(item['action'] == 'files.upload.result' for item in audits)
        assert any(item['action'] == 'files.download' and item['result'] == 'Validated' for item in audits)
        for item in audits:
            detail = item.get('detailJson') or '{}'
            assert not any(value in detail for value in (filename, 'owned-validation-replacement-only', 'owned-external-change-only'))
        print(json.dumps(dict(realFileBff='passed', binaryBytes=33 * 1024 * 1024 + 7, idleSeconds=7,
            oidcMaintenanceGatewaySqlWorkerUds='passed', checksumDownloadOverwriteIdempotency='passed',
            loginOriginCsrfProtection='passed', canceledTombstoneAndWrongChecksum='passed', stalePlanAndAudit='passed')))
    finally:
        for identifier in operations:
            result = session.get(base + '/api/v1/files/uploads/' + identifier, timeout=15)
            if result.status_code == 200 and result.json()['state'] not in ('Succeeded', 'Superseded', 'Canceled', 'Failed'):
                post('files/uploads/' + identifier + '/cancel', {}, 202, identifier)
                wait(identifier, 'Canceled')
        allowed = set().union(*owned.values()) if owned else set()
        # 1.25 creates a separate HDD old-byte copy. Remove only copies from
        # this probe's exact terminal operation IDs and independently verify
        # owner, binding and full bytes first; never prune production history.
        for identifier in operations:
            operation = uuid.UUID(identifier).hex
            parent = Path('/data/DockerData/whaledeck/file-backups')
            data = parent / (operation + '.data'); metadata = parent / (operation + '.json')
            if data.exists() or metadata.exists():
                assert not data.is_symlink() and not metadata.is_symlink()
                data_info = data.stat(); metadata_info = metadata.stat()
                assert data_info.st_uid == metadata_info.st_uid == 0 and data_info.st_nlink == metadata_info.st_nlink == 1
                value = json.loads(metadata.read_bytes()); checksum = digest(data.read_bytes())
                assert value['state'] == 'Verified' and value['operationId'] == operation and value['rootId'] == ROOT
                assert value['path'] in owned and value['sha256'] == checksum and checksum in owned[value['path']]
                assert value['sizeBytes'] == data_info.st_size
                data.unlink(); metadata.unlink()
        for filename, checksums in owned.items():
            target = DIRECTORY / filename
            if target.exists():
                assert target.parent == DIRECTORY and filename.startswith('wdv-bff-file-') and not target.is_symlink() and digest(target.read_bytes()) in checksums
                target.unlink()
        for identifier in operations:
            previous = DIRECTORY / '.whaledeck-files' / (uuid.UUID(identifier).hex + '.upload')
            if previous.exists():
                assert not previous.is_symlink() and digest(previous.read_bytes()) in allowed
                previous.unlink()
        print(json.dumps(dict(ownedBffFilesRemoved=True, immutableIntentAuditAndRootReceiptsRetained=True)))


if __name__ == '__main__':
    try: main()
    except Exception as error:
        frames = traceback.extract_tb(error.__traceback__)
        print(json.dumps(dict(fileAcceptance='failed', phase=PHASE, failureType=type(error).__name__,
            sourceLine=frames[-1].lineno if frames else None)))
        raise SystemExit(1) from None
