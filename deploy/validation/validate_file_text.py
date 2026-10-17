#!/usr/bin/env python3
"""Installed OIDC/BFF/Worker/Agent text editing + actual HDD old-byte proof.

Only exact owned test files/copies are removed after confirmed terminal jobs.
No configuration, production files, credentials, bodies or raw errors printed.
"""
import hashlib
import json
import os
from pathlib import Path
import stat
import subprocess
import tempfile
import time
import traceback
import uuid
import requests
from validate_databases import Api
from validate_oidc import authenticate, required_environment

PHASE = 'startup'
ROOT = 'whaledeck.files.shared'
DIRECTORY = Path('/data/WhaleDeck/files')
BACKUPS = Path('/data/DockerData/whaledeck/file-backups')


def digest(value): return hashlib.sha256(value).hexdigest().upper()


def main():
    global PHASE
    assert os.geteuid() == 0 and DIRECTORY.resolve() == DIRECTORY and DIRECTORY.is_dir()
    base = os.environ.get('WHALEDECK_URL', 'http://192.168.100.13:8080').rstrip('/')
    session, _ = authenticate(base, required_environment('VALIDATION_USERNAME'), required_environment('VALIDATION_PASSWORD'))
    api = Api(base, session)
    fixture = Path(tempfile.mkdtemp(prefix='wdv-text-', dir=DIRECTORY)); original_directory = fixture.stat()
    old = b'\xef\xbb\xbf' + 'public class O0Il {\r\n  // 中文 English\r\n}\r\n'.encode()
    updated = b'\xef\xbb\xbf' + 'public class O0Il {\r\n  // 已编辑 English\r\n}\r\n'.encode()
    files = {'sample.txt': old, 'invalid.txt': b'\xff', 'binary.txt': b'a\x00b', 'too-large.txt': b'a' * (1024**2 + 1), 'empty.txt': b''}
    allowed = {name: {digest(value)} for name, value in files.items()}; allowed['sample.txt'].add(digest(updated))
    operation = None; expected_version = None; old_identity = None

    def relative(name): return fixture.name + '/' + name
    def get(route, expected=200, **params):
        response = session.get(base + '/api/v1/' + route, params=params, timeout=30)
        assert response.status_code == expected, 'unexpected-read-status'
        return response
    def post(route, body, expected=200, key=None):
        response = session.post(base + '/api/v1/' + route, json=body, headers={**api.headers, **({'Idempotency-Key': key} if key else {})}, timeout=40)
        assert response.status_code == expected, 'unexpected-write-status'
        return response.json()
    def entry(name): return get('files/entry', rootId=ROOT, path=relative(name)).json()
    def preview(name, expected=200, version=None):
        response = get('files/text', expected, rootId=ROOT, path=relative(name), expectedVersion=version or entry(name)['version'])
        assert 'no-store' in response.headers.get('Cache-Control', '').lower(), 'text-must-not-be-cached'
        return response
    def wait(identifier):
        deadline = time.monotonic() + 90
        while time.monotonic() < deadline:
            result = get('files/uploads/' + identifier).json()
            if result['state'] in ('Succeeded', 'Superseded', 'Failed', 'Canceled', 'Unknown'): return result
            time.sleep(0.4)
        raise RuntimeError('text-upload-terminal-timeout')

    try:
        for name, content in files.items(): (fixture / name).write_bytes(content)
        PHASE = 'real-preview-contract-bom-crlf-and-rejections'
        schema = session.get(base + '/openapi/v1.json', timeout=30); assert schema.status_code == 200
        assert '/api/v1/files/text' in schema.json()['paths']
        initial = entry('sample.txt'); expected_version = initial['version']; old_identity = (fixture / 'sample.txt').stat()
        response = preview('sample.txt'); text = response.json()
        assert text == dict(rootId=ROOT, path=relative('sample.txt'), version=expected_version, sizeBytes=len(old), sha256=digest(old),
            text=old[3:].decode(), utf8Bom=True, lineEnding='CRLF')
        empty = preview('empty.txt').json(); assert empty['text'] == '' and empty['sizeBytes'] == 0 and empty['lineEnding'] == 'None' and not empty['utf8Bom']
        for name in ('invalid.txt', 'binary.txt', 'too-large.txt'):
            rejected = preview(name, 400).json(); assert 'text' not in rejected and 'sha256' not in rejected
        preview('sample.txt', 409, '0' * 64)
        anonymous = requests.Session(); anonymous.trust_env = False
        assert anonymous.get(base + '/api/v1/files/text', params=dict(rootId=ROOT, path=relative('sample.txt'), expectedVersion=expected_version), timeout=15).status_code == 401
        get('files/text', 403, rootId=ROOT, path='.env', expectedVersion=expected_version)
        get('files/text', 400, rootId=ROOT, path='../escape', expectedVersion=expected_version)

        PHASE = 'confirmed-overwrite-through-worker-and-hdd'
        command = dict(rootId=ROOT, path=relative('sample.txt'), sizeBytes=len(updated), sha256=digest(updated), expectedVersion=expected_version)
        plan = post('files/uploads/plan', command); assert plan['overwrites'] and 'HDD' in plan['warning']
        key = str(uuid.uuid4()); body = dict(planHash=plan['planHash'], confirmed=True, idempotencyKey=key)
        operation = post('files/uploads', body, 202, key)['id']
        assert post('files/uploads', body, 202, key)['id'] == operation
        result = session.put(base + '/api/v1/files/uploads/' + operation + '/content', data=updated,
            headers={**api.headers, 'Content-Type': 'application/octet-stream', 'Idempotency-Key': operation}, timeout=90)
        assert result.status_code == 202
        completed = wait(operation); assert completed['state'] == 'Succeeded' and completed['previousRetained']
        assert (fixture / 'sample.txt').read_bytes() == updated
        current = preview('sample.txt').json(); assert current['text'] == updated[3:].decode() and current['sha256'] == digest(updated)
        assert current['version'] == completed['publishedVersion'] and current['version'] != expected_version
        preview('sample.txt', 409, expected_version)
        key = str(uuid.uuid4()); post('files/uploads', dict(planHash=plan['planHash'], confirmed=True, idempotencyKey=key), 409, key)

        PHASE = 'actual-private-hdd-data-and-metadata-only-audit'
        identifier = uuid.UUID(operation).hex
        data = BACKUPS / (identifier + '.data'); metadata = BACKUPS / (identifier + '.json')
        assert not BACKUPS.is_symlink() and BACKUPS.stat().st_uid == 0 and stat.S_IMODE(BACKUPS.stat().st_mode) == 0o700
        for path in (data, metadata):
            info = path.lstat(); assert stat.S_ISREG(info.st_mode) and info.st_uid == 0 and info.st_nlink == 1 and stat.S_IMODE(info.st_mode) == 0o600
        assert data.read_bytes() == old
        backup = json.loads(metadata.read_bytes())
        assert backup['state'] == 'Verified' and backup['operationId'] == identifier and backup['rootId'] == ROOT and backup['path'] == relative('sample.txt')
        assert backup['sourceVersion'] == expected_version and backup['sha256'] == digest(old) and backup['sizeBytes'] == len(old)
        assert backup['differentSourceDevice'] == (old_identity.st_dev != data.stat().st_dev)
        receipt = json.loads((Path('/var/lib/whaledeck-files') / (identifier + '.json')).read_bytes())
        assert receipt['state'] == 'Completed' and receipt['hddBackup']['sha256'] == digest(old)
        subject = get('auth/me').json()['subject']; quoted_subject = subject.replace("'", "''")
        sql = "SELECT COALESCE(json_agg(json_build_object('action',action,'result',result,'detail',detail_json)), '[]'::json)::text FROM audit_events WHERE action='files.text-preview' AND actor_subject='" + quoted_subject + "';"
        process = subprocess.run(['docker', 'exec', '--user', 'postgres', '-i', 'database-platform-postgres', 'psql', '-X', '-v', 'ON_ERROR_STOP=1', '-U', 'postgres', '-d', 'whaledeck', '-At'],
            input=sql.encode(), stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, timeout=20, check=True)
        audits = json.loads(process.stdout)
        hashes = {digest((ROOT + ':' + relative(name)).encode()) for name in files}
        # The protected read is correctly denied by the Agent AFTER the BFF
        # writes Started/Incomplete metadata. Its exact attempted target hash
        # is expected too; never treat an absent audit field as proof of safety.
        hashes.add(digest((ROOT + ':.env').encode()))
        assert any(item['result'] == 'Validated' for item in audits) and any(item['result'] == 'Incomplete' for item in audits)
        assert all(set(item['detail']) == {'targetHash'} and item['detail']['targetHash'] in hashes for item in audits)
        print(json.dumps(dict(realFileTextBff='passed', oidcApiSqlWorkerUdsInstalledHelper='passed', utf8BomCrLfEmptyLimitAndBinaryRejection='passed',
            confirmedVersionBoundOverwrite='passed', physicalHddOldBytesChecksumAndPrivateReceipt='passed', metadataOnlySqlAudit='passed',
            differentSourceDevice=backup['differentSourceDevice'], physicalHddFailureInjectionAndBrowser='separate-evidence')))
    finally:
        if operation is not None:
            result = get('files/uploads/' + operation).json()
            if result['state'] not in ('Succeeded', 'Superseded', 'Failed', 'Canceled'):
                post('files/uploads/' + operation + '/cancel', {}, 202, operation)
                result = wait(operation)
            assert result['state'] in ('Succeeded', 'Superseded', 'Failed', 'Canceled'), 'uncertain-write-preserve-all-fixtures'
        current = fixture.lstat(); assert stat.S_ISDIR(current.st_mode) and (current.st_dev, current.st_ino) == (original_directory.st_dev, original_directory.st_ino)
        for name, checksums in allowed.items():
            path = fixture / name
            if path.exists():
                info = path.lstat(); assert stat.S_ISREG(info.st_mode) and info.st_nlink == 1 and digest(path.read_bytes()) in checksums
                path.unlink()
        if operation is not None:
            identifier = uuid.UUID(operation).hex
            retained = fixture / '.whaledeck-files' / (identifier + '.upload')
            if retained.exists():
                assert not retained.is_symlink() and retained.read_bytes() == old; retained.unlink()
            for suffix in ('.data', '.json'):
                path = BACKUPS / (identifier + suffix)
                if path.exists():
                    info = path.lstat(); assert stat.S_ISREG(info.st_mode) and info.st_uid == 0 and info.st_nlink == 1
                    if suffix == '.data': assert path.read_bytes() == old
                    else:
                        value = json.loads(path.read_bytes()); assert value['state'] == 'Verified' and value['operationId'] == identifier and value['path'] == relative('sample.txt') and value['sha256'] == digest(old)
                    path.unlink()
        private = fixture / '.whaledeck-files'
        if private.exists(): assert not private.is_symlink() and not any(private.iterdir()); private.rmdir()
        assert not any(fixture.iterdir()); fixture.rmdir()
        print(json.dumps(dict(exactOwnedTextFixturesAndHddTestCopiesRemoved=True, immutableJobAuditAndRootReceiptsRetained=True)))


if __name__ == '__main__':
    try: main()
    except Exception as error:
        frames = traceback.extract_tb(error.__traceback__)
        print(json.dumps(dict(fileTextAcceptance='failed', phase=PHASE, failureType=type(error).__name__, sourceLine=frames[-1].lineno if frames else None)))
        raise SystemExit(1) from None
