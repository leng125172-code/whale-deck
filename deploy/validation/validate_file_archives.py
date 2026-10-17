#!/usr/bin/env python3
"""Real OIDC/BFF/SQL/Worker/installed Agent archive acceptance.

Only this invocation's private test directory and exact, verified HDD test
copies may be removed. Unknown jobs preserve all evidence. No tokens, cookies,
credentials, file bodies or raw HTTP/SQL error text are printed.
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
TERMINAL = ('Succeeded', 'Superseded', 'Failed', 'Canceled', 'Acknowledged')


def digest(value): return hashlib.sha256(value).hexdigest().upper()


def main():
    global PHASE
    assert os.geteuid() == 0 and DIRECTORY.resolve() == DIRECTORY and DIRECTORY.is_dir()
    base = os.environ.get('WHALEDECK_URL', 'http://192.168.100.13:8080').rstrip('/')
    session, _ = authenticate(base, required_environment('VALIDATION_USERNAME'), required_environment('VALIDATION_PASSWORD'))
    api = Api(base, session)
    fixture = Path(tempfile.mkdtemp(prefix='wdv-archive-bff-', dir=DIRECTORY)); folder_identity = fixture.stat()
    source = fixture / 'source.bin'; relative = fixture.name + '/source.bin'
    original = bytes(range(256)) * 8192 + b'\x00owned-bff-archive\xff'
    changed = b'owned-bff-overwrite\x00\xff'
    operations = []; plans = []; allowed = {digest(original), digest(changed)}
    captured = {}; retained = {}; verified = False

    def get(path, expected=200, target=session, **params):
        result = target.get(base + '/api/v1/' + path, params=params, timeout=35)
        assert result.status_code == expected, 'unexpected-read-status'
        return result

    def post(path, body, expected=200, key=None, target=session, headers=None):
        result = target.post(base + '/api/v1/' + path, json=body,
            headers={**api.headers, **({'Idempotency-Key': key} if key else {}), **(headers or {})}, timeout=45)
        assert result.status_code == expected, 'unexpected-write-status'
        return result.json() if result.content else None

    def entry(): return get('files/entry', rootId=ROOT, path=relative).json()

    def plan(kind, version=None, expected=None):
        key = str(uuid.uuid4())
        command = dict(kind=kind, location=dict(rootId=ROOT, path=relative), versionId=version,
            expectedVersion=expected or ('ABSENT' if kind == 'purge' or not source.exists() else entry()['version']))
        result = post('files/archives/plan', command, key=key); plans.append(result)
        assert result['command'] == command and result['idempotencyKey'] == key and uuid.UUID(result['operationId'])
        registration = get('files/archives/registrations/' + result['operationId']).json()
        assert registration['state'] == 'Issued' and registration['intent'] is None and registration['plan'] == result
        return result

    def body(planned): return dict(planHash=planned['planHash'], confirmed=True, idempotencyKey=planned['idempotencyKey'])

    def submit(planned):
        identifier = planned['operationId']
        # Register the possible write BEFORE the HTTP request: a lost response
        # must not allow finally to mistake an accepted write for absence.
        operations.append(identifier)
        accepted = post('files/archives', body(planned), 202, planned['idempotencyKey'])
        assert accepted['id'] == identifier and accepted['jobId'] == identifier
        assert post('files/archives', body(planned), 202, planned['idempotencyKey'])['id'] == identifier
        registration = get('files/archives/registrations/' + identifier).json()
        assert registration['state'] == 'Registered' and registration['plan'] is None and registration['intent']['id'] == identifier
        deadline = time.monotonic() + 100
        while time.monotonic() < deadline:
            result = get('files/archives/' + identifier).json()
            if result['state'] in TERMINAL or result['state'] == 'Unknown':
                assert result['state'] == 'Succeeded', 'archive-result-not-success'
                return result
            time.sleep(0.5)
        raise RuntimeError('archive-result-timeout')

    def capture(identifier, content, version, kind):
        raw_id = uuid.UUID(identifier).hex
        data, metadata = BACKUPS / (raw_id + '.data'), BACKUPS / (raw_id + '.json')
        assert BACKUPS.resolve() == BACKUPS and BACKUPS.stat().st_uid == 0 and stat.S_IMODE(BACKUPS.stat().st_mode) == 0o700
        for path in (data, metadata):
            info = path.lstat(); assert stat.S_ISREG(info.st_mode) and info.st_uid == 0 and info.st_nlink == 1 and stat.S_IMODE(info.st_mode) == 0o600
        value = json.loads(metadata.read_bytes())
        assert data.read_bytes() == content and value['state'] == 'Verified' and value['operationId'] == raw_id
        assert value['rootId'] == ROOT and value['path'] == relative and value['sourceVersion'] == version
        assert value['sha256'] == digest(content) and value['sizeBytes'] == len(content) and value.get('captureKind', 'Overwrite') == kind
        assert value['differentSourceDevice'] == (folder_identity.st_dev != data.stat().st_dev)
        captured[identifier] = (digest(content), version)
        return value

    try:
        source.write_bytes(original); os.chown(source, 1000, 1000); source.chmod(0o640)
        old_identity = source.stat(); initial = entry()
        PHASE = 'openid-capabilities-origin-and-no-unconfirmed-write'
        roots = get('files/roots').json(); root = next(item for item in roots if item['id'] == ROOT)
        assert {'files-history', 'files-archive'} <= set(root['actions'])
        schema = session.get(base + '/openapi/v1.json', timeout=30); assert schema.status_code == 200
        assert '/api/v1/files/archives/registrations/{id}' in schema.json()['paths']
        anonymous = requests.Session(); anonymous.trust_env = False
        assert anonymous.get(base + '/api/v1/files/archives', params=dict(rootId=ROOT), timeout=15).status_code == 401
        command = dict(kind='trash', location=dict(rootId=ROOT, path=relative), versionId=None, expectedVersion=initial['version'])
        assert session.post(base + '/api/v1/files/archives/plan', json=command, timeout=15).status_code in (400, 403)
        post('files/archives/plan', command, 403, str(uuid.uuid4()), headers={'Origin': 'http://foreign.invalid'})
        post('files/archives/plan', command, 400)
        canceled = plan('trash'); assert source.read_bytes() == original
        canceled_id = canceled['operationId']; cancel = post('files/archives/registrations/' + canceled_id + '/cancel', {}, key=canceled_id)
        assert cancel['state'] == 'Canceled'
        post('files/archives', body(canceled), 409, canceled['idempotencyKey']); assert source.read_bytes() == original
        bad_path = {**command, 'location': dict(rootId=ROOT, path='../escape')}
        post('files/archives/plan', bad_path, 400, str(uuid.uuid4()))
        protected = {**command, 'location': dict(rootId=ROOT, path='.env')}
        post('files/archives/plan', protected, 403, str(uuid.uuid4()))

        PHASE = 'original-login-bound-plan-recovery-and-trash'
        trash = plan('trash'); other, _ = authenticate(base, required_environment('VALIDATION_USERNAME'), required_environment('VALIDATION_PASSWORD'))
        get('files/archives/registrations/' + trash['operationId'], 404, target=other)
        trashed = submit(trash); trash_id = trashed['id']; assert not source.exists() and trashed['sourceRetained']
        retained[trash_id] = digest(original)
        backup = capture(trash_id, original, initial['version'], 'Trash'); assert backup['captureState'] == 'Trashed'
        history = get('files/archives', rootId=ROOT, path=relative, take=50).json()
        assert 'no-store' in get('files/archives', rootId=ROOT, path=relative, take=1).headers.get('Cache-Control', '').lower()
        row = next(item for item in history['entries'] if item['id'] == trash_id)
        assert row['captureKind'] == 'Trash' and row['captureState'] == 'Trashed' and row['availability'] == 'Present'
        assert row['sizeBytes'] == len(original) and row['sha256'] == digest(original)

        PHASE = 'restore-absent-and-overwrite-with-new-private-hdd-capture'
        restored = submit(plan('restore', trash_id, 'ABSENT'))
        assert source.read_bytes() == original and not restored['sourceRetained']
        restored_info = source.stat()
        assert (restored_info.st_uid, restored_info.st_gid, stat.S_IMODE(restored_info.st_mode), restored_info.st_mtime_ns) == (old_identity.st_uid, old_identity.st_gid, stat.S_IMODE(old_identity.st_mode), old_identity.st_mtime_ns)
        source.write_bytes(changed); overwrite_version = entry()['version']
        overwrite = submit(plan('restore', trash_id, overwrite_version)); retained[overwrite['id']] = digest(changed)
        assert source.read_bytes() == original and overwrite['sourceRetained']
        capture(overwrite['id'], changed, overwrite_version, 'Overwrite')

        PHASE = 'purge-exact-hdd-copy-without-original-or-evidence-deletion'
        purge = submit(plan('purge', trash_id)); assert purge['command']['versionId'] == trash_id
        assert source.read_bytes() == original
        raw_id = uuid.UUID(trash_id).hex
        assert not (BACKUPS / (raw_id + '.data')).exists() and not (BACKUPS / (raw_id + '.json')).exists()
        assert (fixture / '.whaledeck-files' / (raw_id + '.archive')).read_bytes() == original
        history = get('files/archives', rootId=ROOT, path=relative, take=50).json()
        assert all(item['id'] != trash_id for item in history['entries'])
        assert any(item['id'] == overwrite['id'] for item in history['entries'])
        rejected = dict(kind='restore', location=dict(rootId=ROOT, path=relative), versionId=trash_id, expectedVersion=entry()['version'])
        post('files/archives/plan', rejected, 404, str(uuid.uuid4()))

        PHASE = 'real-human-audit-durable-jobs-catalog-and-lock-release'
        ids = ','.join("'" + str(uuid.UUID(value)) + "'::uuid" for value in operations)
        sql = "SELECT json_build_object('jobs',(SELECT json_agg(json_build_object('id',id,'actor',actor_subject,'state',state)) FROM operation_jobs WHERE id IN (" + ids + "))," + \
            "'audit',(SELECT json_agg(json_build_object('action',action,'result',result,'detail',detail_json)) FROM audit_events WHERE job_id IN (" + ids + "))," + \
            "'catalog',(SELECT json_agg(json_build_object('id',id,'source',source_job_id,'creator',created_by_subject,'availability',availability,'state',capture_state)) FROM file_versions WHERE id IN (" + ids + "))," + \
            "'locks',(SELECT count(*) FROM file_path_locks WHERE operation_id IN (" + ids + ")))::text;"
        process = subprocess.run(['docker', 'exec', '--user', 'postgres', '-i', 'database-platform-postgres', 'psql', '-X', '-v', 'ON_ERROR_STOP=1', '-U', 'postgres', '-d', 'whaledeck', '-At'], input=sql.encode(), stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, timeout=20, check=True)
        proof = json.loads(process.stdout); subject = get('auth/me').json()['subject']
        assert len(proof['jobs']) == 4 and all(item['actor'] == subject and item['state'] == 'Succeeded' for item in proof['jobs']) and proof['locks'] == 0
        assert len(proof['audit']) == 8 and all(set(item['detail']) == {'Kind', 'TargetHash', 'ArchiveVersionId'} for item in proof['audit'])
        assert sum(item['action'] == 'files.archive.result' and item['result'] == 'Succeeded' for item in proof['audit']) == 4
        catalog = next(item for item in proof['catalog'] if item['id'] == trash_id)
        assert catalog['state'] == 'Purged' and catalog['availability'] == 'Purged' and catalog['source'] == trash_id and catalog['creator'] == subject
        for identifier in operations:
            receipt = Path('/var/lib/whaledeck-files/archives') / (uuid.UUID(identifier).hex + '.json')
            value = json.loads(receipt.read_bytes()); assert value['state'] == 'Completed' and value['operationId'] == uuid.UUID(identifier).hex
        verified = True
        print(json.dumps(dict(realFileArchivesBff='passed', oidcApiSqlWorkerUdsInstalledHelper='passed', originalLoginAndCancelFence='passed',
            trashRestoreAbsentRestoreOverwritePurge='passed', privateHddBytesAndMetadata='passed', humanAuditAndImmutableCatalog='passed',
            differentSourceDevice=backup['differentSourceDevice'], ordinaryRoleAndRealBrowser='separate-evidence')))
    finally:
        # Any possible accepted write must be proven terminal before cleanup.
        # Never destroy evidence after a lost response or an uncertain outcome.
        for identifier in operations:
            result = get('files/archives/' + identifier).json()
            assert result['state'] in TERMINAL, 'uncertain-archive-preserve-all-fixtures'
        current = fixture.lstat()
        assert stat.S_ISDIR(current.st_mode) and (current.st_dev, current.st_ino) == (folder_identity.st_dev, folder_identity.st_ino)
        if source.exists():
            info = source.lstat(); assert stat.S_ISREG(info.st_mode) and info.st_nlink == 1 and digest(source.read_bytes()) in allowed; source.unlink()
        private = fixture / '.whaledeck-files'
        if private.exists():
            assert not private.is_symlink() and private.stat().st_uid == 0 and stat.S_IMODE(private.stat().st_mode) == 0o700
            expected = {uuid.UUID(identifier).hex + '.archive': checksum for identifier, checksum in retained.items()}
            for path in private.iterdir():
                info = path.lstat(); assert path.name in expected and stat.S_ISREG(info.st_mode) and info.st_nlink == 1 and digest(path.read_bytes()) == expected[path.name]; path.unlink()
            private.rmdir()
        for identifier, (checksum, version) in captured.items():
            raw_id = uuid.UUID(identifier).hex; data, metadata = BACKUPS / (raw_id + '.data'), BACKUPS / (raw_id + '.json')
            if not data.exists() and not metadata.exists(): continue
            # Both still exist or preserve both; no half-proof cleanup.
            info = data.lstat(); meta_info = metadata.lstat(); value = json.loads(metadata.read_bytes())
            assert all(stat.S_ISREG(item.st_mode) and item.st_uid == 0 and item.st_nlink == 1 and stat.S_IMODE(item.st_mode) == 0o600 for item in (info, meta_info))
            assert digest(data.read_bytes()) == checksum and value['operationId'] == raw_id and value['path'] == relative and value['sourceVersion'] == version and value['sha256'] == checksum
            data.unlink(); metadata.unlink()
        assert not any(fixture.iterdir()); fixture.rmdir()
        print(json.dumps(dict(exactOwnedArchiveFixturesRemoved=True, immutableJobAuditCatalogRootReceiptsRetained=True, acceptanceCompleted=verified)))


if __name__ == '__main__':
    try: main()
    except Exception as error:
        frames = traceback.extract_tb(error.__traceback__)
        print(json.dumps(dict(fileArchiveAcceptance='failed', phase=PHASE, failureType=type(error).__name__, sourceLine=frames[-1].lineno if frames else None)))
        raise SystemExit(1) from None
