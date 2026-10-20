#!/usr/bin/env python3
"""Installed Cookie/BFF/Worker/UDS/HDD acceptance using one owned fixture.

No simulated root transport, direct engine invocation, broad cleanup or retries
of uncertain writes. Failures preserve objects; only verified invocation-owned
payloads are removed on success. Permanent jobs/audits/root receipts remain.
Run through run_with_ephemeral_admin.py after the expected runtime is installed.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import secrets
import stat
import subprocess
import sys
import tarfile
import time
import traceback
import uuid

import requests
from validate_databases import Api
from validate_oidc import authenticate, required_environment
from validate_tree_backup_engine import proof

ROOT = 'whaledeck.files.shared'
DATA = Path('/data/WhaleDeck/files')
STATE = Path('/var/lib/whaledeck-files/tree-recycling')
VALIDATION = Path('/data/GitRepos/whale-deck/deploy/validation')
TERMINAL = ('Succeeded', 'Failed', 'Canceled', 'Expired', 'Acknowledged')
PHASE = 'startup'


def plain_file(path, maximum=None):
    info = path.lstat()
    assert path.resolve() == path and stat.S_ISREG(info.st_mode)
    assert info.st_uid == 0 and info.st_nlink == 1 and stat.S_IMODE(info.st_mode) == 0o600
    if maximum is not None:
        assert info.st_size <= maximum
    return info


def main():
    global PHASE
    assert os.geteuid() == 0 and len(sys.argv) == 2
    revision = sys.argv[1]
    assert re.fullmatch('[a-f0-9]{40}', revision)
    installed = json.loads(subprocess.check_output(['/opt/whaledeck-agent/WhaleDeck.Agent', '--build-info'], text=True))
    assert installed['sourceRevision'] == revision
    assert DATA.resolve() == DATA and DATA.is_dir()
    registry = next(item for item in json.loads(Path('/etc/whaledeck/file-roots.json').read_bytes()) if item['id'] == ROOT)
    assert registry['path'] == str(DATA)
    assert {'files-list', 'files-download', 'files-mutate', 'files-history', 'files-archive', 'files-tree-trash'} <= set(registry['allowedActions'])
    base = os.environ.get('WHALEDECK_URL', 'http://192.168.100.13:8080').rstrip('/')
    session, _ = authenticate(base, required_environment('VALIDATION_USERNAME'), required_environment('VALIDATION_PASSWORD'))
    api = Api(base, session)
    marker = 'wdv-recycling-bff-' + uuid.uuid4().hex[:16]
    folder = DATA / marker
    assert not os.path.lexists(folder)
    folder.mkdir(mode=0o700)
    identity = proof(folder)[:2]
    print(json.dumps({'ownedRecyclingBffFixture': marker, 'runtimeRevision': revision}), flush=True)
    payload = os.urandom(2 * 1024 * 1024 + 7)
    filename = '中文完整字节.bin'
    ids, execution_ids, original, owned_archives = [], [], {}, {}

    def get(path, status=200, **kwargs):
        response = session.get(base + '/api/v1/' + path, timeout=30, **kwargs)
        assert response.status_code == status, 'read-status-mismatch'
        return response

    def post(path, body=None, status=202, key=None):
        response = session.post(base + '/api/v1/' + path, json=body,
            headers={**api.headers, **({'Idempotency-Key': key} if key else {})}, timeout=40)
        assert response.status_code == status, 'write-status-mismatch'
        return response

    def command(name):
        listing = get('files/entries', params={'rootId': ROOT, 'path': marker}).json()['entries']
        entry = next(item for item in listing if item['name'] == name)
        assert entry['kind'] == 'Directory' and not entry['isProtected']
        return {'source': {'rootId': ROOT, 'path': marker + '/' + name}, 'expectedVersion': entry['version']}

    def queue(value):
        key = str(uuid.uuid4())
        response = post('files/tree-recyclings', value, key=key)
        row = response.json()
        ids.append(row['id'])
        assert response.headers.get('Location') == '/api/v1/files/tree-recyclings/' + row['id']
        assert row['planningJobId'] == row['id'] and row['executionJobId'] is None and not row['captureVerified']
        assert get('files/tree-recyclings/by-key/' + key).json()['id'] == row['id']
        assert post('files/tree-recyclings', value, key=key).json()['id'] == row['id']
        return row

    def wait(identifier, states):
        deadline = time.monotonic() + 210
        while time.monotonic() < deadline:
            row = get('files/tree-recyclings/' + identifier).json()
            if row['state'] in states:
                return row
            assert row['state'] not in TERMINAL + ('Unknown',), 'unexpected-terminal-result'
            time.sleep(0.5)
        raise RuntimeError('recycling-task-deadline')

    try:
        PHASE = 'owned-fixtures'
        for name in ('complete', 'cancel', 'protected'):
            source = folder / name
            source.mkdir(mode=0o750)
            (source / '空目录').mkdir(mode=0o710)
            leaf = source / filename
            leaf.write_bytes(payload)
            leaf.chmod(0o640)
            os.utime(leaf, ns=(1712345678123456789, 1712345678123456789))
            if name == 'protected':
                (source / '.env').write_bytes(b'fixture-not-a-secret')
            original[name] = proof(leaf)
        PHASE = 'csrf-origin-anonymous-and-openapi'
        value = command('complete')
        anonymous = requests.Session()
        anonymous.trust_env = False
        try:
            assert anonymous.get(base + '/api/v1/files/tree-recyclings/' + str(uuid.uuid4()), timeout=20).status_code == 401
        finally:
            anonymous.close()
        assert session.post(base + '/api/v1/files/tree-recyclings', json=value, timeout=20).status_code in (400, 403)
        assert session.post(base + '/api/v1/files/tree-recyclings', json=value,
            headers={**api.headers, 'Origin': 'http://foreign.invalid', 'Idempotency-Key': str(uuid.uuid4())}, timeout=20).status_code == 403
        schema = session.get(base + '/openapi/v1.json', timeout=30)
        assert schema.status_code == 200
        assert all('/api/v1/files/tree-recyclings' + suffix in schema.json()['paths']
            for suffix in ('', '/{id}', '/by-key/{key}', '/{id}/confirm', '/{id}/cancel', '/{id}/acknowledge', '/captures'))
        post('files/tree-recyclings', {**value, 'source': {'rootId': ROOT, 'path': ''}}, 400, str(uuid.uuid4()))
        post('files/tree-recyclings', {**value, 'source': {'rootId': ROOT, 'path': '../escape'}}, 400, str(uuid.uuid4()))

        PHASE = 'plan-and-original-login-binding'
        ready = wait(queue(value)['id'], ('Ready',))
        assert ready['entryCount'] == 3 and ready['totalBytes'] == len(payload) and ready['confirmation']
        assert not ready['captureVerified'] and ready['archiveSha256'] is None and ready['archiveBytes'] is None
        assert proof(folder / 'complete' / filename) == original['complete']
        confirmation = {'planHash': ready['confirmation']['planHash'], 'confirmed': True, 'version': ready['version']}
        other, _ = authenticate(base, required_environment('VALIDATION_USERNAME'), required_environment('VALIDATION_PASSWORD'))
        try:
            assert other.get(base + '/api/v1/files/tree-recyclings/' + ready['id'], timeout=20).status_code == 404
            other_api = Api(base, other)
            assert other.post(base + '/api/v1/files/tree-recyclings/' + ready['id'] + '/confirm', json=confirmation,
                headers={**other_api.headers, 'Idempotency-Key': ready['id']}, timeout=20).status_code == 403
        finally:
            other.cookies.clear()
            other.close()

        PHASE = 'worker-installed-agent-hdd-and-full-original-inode'
        queued = post('files/tree-recyclings/' + ready['id'] + '/confirm', confirmation, key=ready['id']).json()
        execution = queued['executionJobId']
        assert execution and execution != queued['planningJobId']
        execution_ids.append(execution)
        assert post('files/tree-recyclings/' + ready['id'] + '/confirm', confirmation, key=ready['id']).json()['executionJobId'] == execution
        completed = wait(ready['id'], TERMINAL + ('Unknown',))
        assert completed['state'] == 'Succeeded'
        assert post('files/tree-recyclings/' + ready['id'] + '/confirm', confirmation, key=ready['id']).json()['executionJobId'] == execution
        assert all(completed[field] is True for field in ('captureVerified', 'captureMayBeRetained', 'sourceMayHaveChanged', 'sourceRemoved', 'originalsRetained'))
        assert completed['captureId'] == execution and not os.path.lexists(folder / 'complete')
        operation = uuid.UUID(execution).hex
        receipt_path = STATE / (operation + '.json')
        plain_file(receipt_path, 32768)
        receipt = json.loads(receipt_path.read_bytes())
        assert receipt['state'] == 'Completed' and receipt['plan']['source']['path'] == value['source']['path']
        assert receipt['plan']['entryCount'] == 3 and receipt['plan']['totalBytes'] == len(payload)
        private = folder / '.whaledeck-files' / (operation + '.tree-trash')
        assert private.resolve() == private and proof(private)[:2] == tuple(receipt['retainedIdentity'])
        assert stat.S_IMODE(private.lstat().st_mode) == 0o700 and private.lstat().st_uid == 0
        assert {str(item.relative_to(private)) for item in private.rglob('*')} == {'空目录', filename}
        retained = proof(private / filename)
        assert retained[:8] == original['complete'][:8] and retained[-1] == original['complete'][-1]
        assert (private / filename).read_bytes() == payload

        PHASE = 'independent-hdd-archive-full-bytes-and-catalogue'
        config = json.loads(Path('/etc/whaledeck/file-backups.json').read_bytes())
        store = Path(config['mountPoint']) / config['targetRelative'] / 'directory-archives'
        assert store.is_absolute() and store.resolve() == store and store.lstat().st_uid == 0
        archive = store / (operation + '.tar.gz')
        plain_file(archive)
        archive_proof = proof(archive)
        assert archive_proof[6] == completed['archiveBytes'] and archive_proof[-1] == completed['archiveSha256']
        with tarfile.open(archive, 'r:gz') as reader:
            members = reader.getmembers()
            assert {member.name.rstrip('/') for member in members} == {'.', '空目录', filename}
            assert all(member.isdir() or member.isfile() for member in members)
            member = next(item for item in members if item.name == filename)
            assert member.size == len(payload)
            with reader.extractfile(member) as content:
                assert content.read(len(payload) + 1) == payload and not content.read(1)
        for suffix in ('.json', '.manifest.json', '.tar.gz', '.lock'):
            path = store / (operation + suffix)
            plain_file(path, 16 * 1024**2 if suffix != '.tar.gz' else None)
            owned_archives[path] = proof(path)
        saved_capture = json.loads((store / (operation + '.json')).read_bytes())
        assert saved_capture['state'] == 'Verified' and type(saved_capture['differentSourceDevice']) is bool
        assert saved_capture['archiveSha256'] == archive_proof[-1]
        catalogue = get('files/tree-recyclings/captures', params={'rootId': ROOT, 'path': value['source']['path']}).json()
        assert len(catalogue['items']) == 1 and catalogue['next'] is None
        capture = catalogue['items'][0]
        assert capture['id'] == execution and capture['intentId'] == ready['id'] and capture['source'] == value
        assert capture['archiveSha256'] == archive_proof[-1] and capture['totalBytes'] == len(payload)
        assert re.fullmatch('[A-F0-9]{64}', capture['manifestSha256'])
        root_page = get('files/tree-recyclings/captures', params={'rootId': ROOT, 'take': 1}).json()
        assert len(root_page['items']) == 1

        PHASE = 'cancel-protected-human-audit-and-zero-locks'
        canceled = wait(queue(command('cancel'))['id'], ('Ready',))
        post('files/tree-recyclings/' + canceled['id'] + '/cancel', key=canceled['id'])
        assert wait(canceled['id'], TERMINAL)['state'] == 'Canceled'
        post('files/tree-recyclings/' + canceled['id'] + '/confirm', {'planHash': canceled['confirmation']['planHash'],
            'confirmed': True, 'version': canceled['version']}, 409, canceled['id'])
        rejected = wait(queue(command('protected'))['id'], TERMINAL)
        assert rejected['state'] == 'Failed'
        assert proof(folder / 'cancel' / filename) == original['cancel']
        assert proof(folder / 'protected' / filename) == original['protected']
        subject = get('auth/me').json()['subject']
        audits = get('audit', params={'actorSubject': subject, 'take': 500}).json()
        assert all(any(row.get('jobId') == execution and row['action'] == action and row['actorSubject'] == subject for row in audits)
            for action in ('files.tree-trash.confirmed', 'files.tree-trash.result'))
        assert all(re.fullmatch('[a-f0-9-]{36}', identifier) for identifier in ids + execution_ids)
        literals = ','.join("'" + identifier + "'" for identifier in ids)
        sql = "SELECT json_build_object('intents',(SELECT count(*) FROM file_tree_recycling_operations WHERE id IN (" + literals + ")),'active',(SELECT count(*) FROM file_tree_recycling_operations WHERE id IN (" + literals + ") AND completed_at_utc IS NULL),'locks',(SELECT count(*) FROM file_path_locks WHERE operation_id='" + execution + "'))::text;"
        result = json.loads(subprocess.check_output(['docker', 'exec', '-u', 'postgres', 'database-platform-postgres', 'psql', '-X', '-v', 'ON_ERROR_STOP=1', '-d', 'whaledeck', '-At', '-c', sql], text=True))
        assert result == {'intents': len(ids), 'active': 0, 'locks': 0}

        PHASE = 'ordinary-real-cookie-and-valid-csrf-denied'
        helper = subprocess.Popen([sys.executable, '-u', str(VALIDATION / 'ephemeral_browser_user.py')],
            stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        name, password = 'wd-browser-' + uuid.uuid4().hex, 'Wd!' + secrets.token_hex(40)
        ordinary = None
        try:
            helper.stdin.write(json.dumps({'username': name, 'password': password, 'administrator': False}) + '\n')
            helper.stdin.flush()
            assert json.loads(helper.stdout.readline())['ready'] is True
            ordinary, _ = authenticate(base, name, password, require_administrator=False)
            password = None
            ordinary_api = Api(base, ordinary)
            assert ordinary.get(base + '/api/v1/files/tree-recyclings/' + ids[0], timeout=20).status_code == 403
            assert ordinary.get(base + '/api/v1/files/tree-recyclings/captures', params={'rootId': ROOT}, timeout=20).status_code == 403
            denied = ordinary.post(base + '/api/v1/files/tree-recyclings', json=command('cancel'),
                headers={**ordinary_api.headers, 'Idempotency-Key': str(uuid.uuid4())}, timeout=20)
            assert denied.status_code == 403
        finally:
            password = None
            if ordinary is not None:
                ordinary.cookies.clear()
                ordinary.close()
            helper.stdin.close()
            helper.stdin = None
            cleanup, private_error = helper.communicate(timeout=45)
            private_error = None
            assert helper.returncode == 0 and any(json.loads(line).get('temporaryBrowserUserRemoved') is True for line in cleanup.splitlines())

        PHASE = 'exact-owned-fixture-cleanup'
        assert proof(folder)[:2] == identity and folder.parent == DATA and folder.resolve() == folder
        for line in Path('/proc/self/mountinfo').read_text().splitlines():
            mount = line.split()[4]
            assert mount != str(folder) and not mount.startswith(str(folder) + '/')
        assert {item.name for item in folder.iterdir()} == {'cancel', 'protected', '.whaledeck-files'}
        assert {item.name for item in private.parent.iterdir()} == {private.name}
        assert proof(private / filename) == retained and proof(archive) == archive_proof
        assert all(proof(path) == value for path, value in owned_archives.items())
        for name in ('cancel', 'protected'):
            source = folder / name
            assert source.resolve() == source and stat.S_ISDIR(source.lstat().st_mode)
            assert {item.name for item in source.iterdir()} == {filename, '空目录'} | ({'.env'} if name == 'protected' else set())
            assert proof(source / filename) == original[name]
            assert stat.S_ISDIR((source / '空目录').lstat().st_mode) and not any((source / '空目录').iterdir())
            if name == 'protected':
                assert (source / '.env').is_file() and not (source / '.env').is_symlink()
                assert (source / '.env').read_bytes() == b'fixture-not-a-secret'
        # Only this invocation's known private payload and HDD objects. Historical
        # catalogue/immutable root receipt explicitly do not promise current storage.
        for path in owned_archives:
            path.unlink()
        (private / filename).unlink()
        (private / '空目录').rmdir()
        private.rmdir()
        (folder / '.whaledeck-files').rmdir()
        for name in ('cancel', 'protected'):
            source = folder / name
            assert {item.name for item in source.iterdir()} == {filename, '空目录'} | ({'.env'} if name == 'protected' else set())
            assert proof(source / filename) == original[name]
            (source / filename).unlink()
            if name == 'protected':
                assert (source / '.env').read_bytes() == b'fixture-not-a-secret'
                (source / '.env').unlink()
            (source / '空目录').rmdir()
            source.rmdir()
        folder.rmdir()
        print(json.dumps({'realDirectoryRecyclingBff': 'passed', 'binaryBytes': len(payload),
            'originalLoginTwoJobsWorkerInstalledUdsAndHdd': True, 'retainedOriginalFileInodeAndFullArchiveBytes': True,
            'cancelProtectedHumanAuditCatalogueAndOrdinaryCsrfDenied': True, 'exactOwnedPayloadCleanup': True,
            'differentSourceDevice': saved_capture['differentSourceDevice'],
            'historicalCaptureCatalogueAndImmutableJobsAuditRootReceiptsRetained': True,
            'crossAdministratorDispositionRealBrowserAndLargeTree': 'not-covered'}), flush=True)
    finally:
        if folder.exists():
            print(json.dumps({'ownedRecyclingBffFixturePreserved': marker, 'phase': PHASE}), flush=True)
        session.cookies.clear()
        session.close()


if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        frames = traceback.extract_tb(error.__traceback__)
        print(json.dumps({'realDirectoryRecyclingBff': 'failed', 'phase': PHASE,
            'failureType': type(error).__name__, 'sourceLine': frames[-1].lineno if frames else None}), flush=True)
        raise SystemExit(1) from None
