#!/usr/bin/env python3
"""Real Cookie/BFF/SQL/Worker/installed Agent unpacking acceptance.

Execute only via root run_with_ephemeral_admin. Exact synthetic fixture only;
failure preserves evidence. Never replays a failed operation or deletes jobs,
audits, root receipts, production data, or unknown private objects. No UI claim.
"""
from decimal import Decimal
import hashlib
import io
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

PHASE = 'startup'
ROOT = 'whaledeck.files.shared'
DATA = Path('/data/WhaleDeck/files')
STATE = Path('/var/lib/whaledeck-files/tree-unpacking')
TERMINAL = ('Succeeded', 'Failed', 'Canceled', 'Expired', 'Acknowledged')


def main():
    global PHASE
    assert os.geteuid() == 0 and DATA.resolve() == DATA and DATA.is_dir()
    registry = next(item for item in json.loads(Path('/etc/whaledeck/file-roots.json').read_bytes()) if item['id'] == ROOT)
    assert registry['path'] == str(DATA) and {'files-list', 'files-download', 'files-archive', 'files-tree-unpack', 'files-upload', 'files-mutate'} <= set(registry['allowedActions'])
    base = os.environ.get('WHALEDECK_URL', 'http://192.168.100.13:8080').rstrip('/')
    session, _ = authenticate(base, required_environment('VALIDATION_USERNAME'), required_environment('VALIDATION_PASSWORD'))
    api = Api(base, session)
    marker = 'wdv-unpacking-bff-' + uuid.uuid4().hex[:16]
    folder = DATA / marker
    assert not os.path.lexists(folder)
    folder.mkdir(mode=0o700)
    identity = folder.stat()
    print(json.dumps({'ownedUnpackingBffFixture': marker}), flush=True)
    content = os.urandom(2 * 1024 * 1024 + 7)
    inputs, outputs, ids, execution_ids = {}, {}, [], []

    def archive(name, body=content, protected=False):
        path = folder / name
        with tarfile.open(path, 'w:gz' if name.endswith('.gz') else 'w', format=tarfile.PAX_FORMAT) as writer:
            for member, mode in (('.', 0o750), ('空', 0o710), ('代码', 0o750)):
                entry = tarfile.TarInfo(member)
                entry.type, entry.mode = tarfile.DIRTYPE, mode
                entry.uid, entry.gid = 12345, 23456
                entry.pax_headers['mtime'] = '1712345678.123456789'
                writer.addfile(entry)
            entry = tarfile.TarInfo('代码/.env' if protected else '代码/payload.bin')
            entry.size, entry.mode, entry.uid, entry.gid = len(body), 0o6751, 12345, 23456
            entry.pax_headers['mtime'] = '1712345678.123456789'
            writer.addfile(entry, io.BytesIO(body))
        inputs[name] = proof(path)
        return path

    def get(path, status=200, **kwargs):
        response = session.get(base + '/api/v1/' + path, timeout=30, **kwargs)
        assert response.status_code == status, 'read-status-mismatch'
        return response

    def post(path, body=None, status=202, key=None):
        response = session.post(base + '/api/v1/' + path, json=body,
            headers={**api.headers, **({'Idempotency-Key': key} if key else {})}, timeout=40)
        assert response.status_code == status, 'write-status-mismatch'
        return response

    def command(name, target):
        # List metadata is also the UI source for archives above general Inspect's 1 GiB cap.
        entries = get('files/entries', params={'rootId': ROOT, 'path': marker}).json()['entries']
        entry = next(item for item in entries if item['name'] == name)
        assert entry['kind'] == 'File' and not entry['isProtected']
        return {'source': {'rootId': ROOT, 'path': marker + '/' + name}, 'expectedVersion': entry['version'],
            'target': {'rootId': ROOT, 'path': marker + '/' + target}, 'format': 'tar.gz' if name.endswith('.gz') else 'tar'}

    def queue(value):
        key = str(uuid.uuid4())
        response = post('files/tree-unpackings', value, key=key)
        row = response.json()
        ids.append(row['id'])
        assert response.headers.get('Location') == '/api/v1/files/tree-unpackings/' + row['id']
        assert row['planningJobId'] == row['id'] and row['executionJobId'] is None and row['archiveBytes'] is None
        assert get('files/tree-unpackings/by-key/' + key).json()['id'] == row['id']
        assert post('files/tree-unpackings', value, key=key).json()['id'] == row['id']
        return row

    def wait(identifier, states, reconcile_unknown=False):
        deadline = time.monotonic() + 150
        while time.monotonic() < deadline:
            row = get('files/tree-unpackings/' + identifier).json()
            if row['state'] in states:
                return row
            assert row['state'] not in TERMINAL + (() if reconcile_unknown else ('Unknown',)), 'unexpected-terminal-result'
            time.sleep(0.4)
        raise RuntimeError('unpacking-task-deadline')

    def confirm(ready):
        body = {'planHash': ready['confirmation']['planHash'], 'confirmed': True, 'version': ready['version']}
        response = post('files/tree-unpackings/' + ready['id'] + '/confirm', body, key=ready['id'])
        row = response.json()
        assert response.headers.get('Location') == '/api/v1/files/tree-unpackings/' + row['id']
        assert row['executionJobId'] and row['executionJobId'] != row['planningJobId']
        execution_ids.append(row['executionJobId'])
        assert post('files/tree-unpackings/' + ready['id'] + '/confirm', body, key=ready['id']).json()['executionJobId'] == row['executionJobId']
        return row, body

    try:
        for name in ('input.tar', 'input.tar.gz', 'stale.tar'):
            archive(name)
        archive('protected.tar', b'fixture-not-secret', True)
        PHASE = 'csrf-origin-anonymous-openapi'
        value = command('input.tar', 'output-tar')
        anonymous = requests.Session()
        anonymous.trust_env = False
        assert anonymous.get(base + '/api/v1/files/tree-unpackings/' + str(uuid.uuid4()), timeout=15).status_code == 401
        assert session.post(base + '/api/v1/files/tree-unpackings', json=value, timeout=15).status_code in (400, 403)
        assert session.post(base + '/api/v1/files/tree-unpackings', json=value, headers={**api.headers,
            'Origin': 'http://foreign.invalid', 'Idempotency-Key': str(uuid.uuid4())}, timeout=15).status_code == 403
        paths = session.get(base + '/openapi/v1.json', timeout=30)
        assert paths.status_code == 200
        paths = paths.json()['paths']
        assert all('/api/v1/files/tree-unpackings' + suffix in paths for suffix in ('', '/{id}', '/by-key/{key}', '/{id}/confirm', '/{id}/cancel', '/{id}/acknowledge'))
        assert '/api/v1/files/tree-unpackings/{id}/download' not in paths
        post('files/tree-unpackings', {**value, 'target': {'rootId': ROOT, 'path': '../escape'}}, 400, str(uuid.uuid4()))

        PHASE = 'two-formats-original-plan-worker-independent-output'
        for name, target_name in (('input.tar', 'output-tar'), ('input.tar.gz', 'output-gzip')):
            ready = wait(queue(command(name, target_name))['id'], ('Ready',))
            assert ready['entryCount'] == 4 and ready['totalBytes'] == len(content) and ready['confirmation']
            assert ready['archiveBytes'] == inputs[name][6] and ready['archiveSha256'] == inputs[name][-1]
            assert not (folder / target_name).exists()
            assert get('files/tree-unpackings/' + ready['id']).json()['confirmation']['planHash'] == ready['confirmation']['planHash']
            other, _ = authenticate(base, required_environment('VALIDATION_USERNAME'), required_environment('VALIDATION_PASSWORD'))
            try:
                assert other.get(base + '/api/v1/files/tree-unpackings/' + ready['id'], timeout=15).status_code == 404
                other_api = Api(base, other)
                denied = other.post(base + '/api/v1/files/tree-unpackings/' + ready['id'] + '/confirm', json={
                    'planHash': ready['confirmation']['planHash'], 'confirmed': True, 'version': ready['version']},
                    headers={**other_api.headers, 'Idempotency-Key': ready['id']}, timeout=20)
                assert denied.status_code == 403
            finally:
                other.cookies.clear()
                other.close()
            queued, confirmation = confirm(ready)
            completed = wait(queued['id'], TERMINAL + ('Unknown',))
            assert completed['state'] == 'Succeeded' and not completed['partialRetained'] and re.fullmatch('[A-F0-9]{64}', completed['publishedVersion'])
            assert completed['archiveBytes'] == ready['archiveBytes'] and completed['archiveSha256'] == ready['archiveSha256']
            output = folder / target_name
            assert {''} | {str(item.relative_to(output)) for item in output.rglob('*')} == {'', '空', '代码', '代码/payload.bin'}
            with tarfile.open(folder / name) as reader:
                for entry in reader.getmembers():
                    relative = '' if entry.name == '.' else entry.name
                    path = output / relative
                    info = path.lstat()
                    assert (stat.S_ISDIR(info.st_mode) if entry.isdir() else stat.S_ISREG(info.st_mode)) and info.st_dev == identity.st_dev
                    assert (info.st_uid, info.st_gid, stat.S_IMODE(info.st_mode), info.st_mtime_ns) == (
                        registry.get('ownerUid', 0), registry.get('ownerGid', 0), entry.mode & 0o777,
                        int(Decimal(entry.pax_headers['mtime']) * 1000000000))
                    if entry.isfile():
                        assert info.st_nlink == 1 and info.st_size == len(content) and proof(path)[-1] == hashlib.sha256(content).hexdigest().upper()
                    outputs[str(path.relative_to(folder))] = proof(path)
            record_path = STATE / (uuid.UUID(queued['executionJobId']).hex + '.json')
            info = record_path.lstat()
            assert stat.S_ISREG(info.st_mode) and info.st_uid == 0 and stat.S_IMODE(info.st_mode) == 0o600 and info.st_nlink == 1 and info.st_size <= 32768
            record = json.loads(record_path.read_bytes())
            assert record['state'] == 'Completed' and record['plan']['archiveBytes'] == ready['archiveBytes'] and record['plan']['archiveSha256'] == ready['archiveSha256']
            assert tuple(record['newIdentity']) == proof(output)[:2]
            assert proof(folder / name) == inputs[name]
            assert post('files/tree-unpackings/' + ready['id'] + '/confirm', confirmation, key=ready['id']).json()['executionJobId'] == queued['executionJobId']

        PHASE = 'cancel-protected-and-changed-input'
        canceled = wait(queue(command('input.tar', 'canceled'))['id'], ('Ready',))
        post('files/tree-unpackings/' + canceled['id'] + '/cancel', {}, key=canceled['id'])
        post('files/tree-unpackings/' + canceled['id'] + '/confirm', {'planHash': canceled['confirmation']['planHash'],
            'confirmed': True, 'version': canceled['version']}, 409, canceled['id'])
        rejected = wait(queue(command('protected.tar', 'protected-output'))['id'], TERMINAL)
        assert rejected['state'] == 'Failed'
        stale = wait(queue(command('stale.tar', 'stale-output'))['id'], ('Ready',))
        archive('stale.tar', b'changed-after-plan')
        stale_job, _ = confirm(stale)
        assert wait(stale_job['id'], TERMINAL, reconcile_unknown=True)['state'] == 'Failed'
        assert not any((folder / name).exists() for name in ('canceled', 'protected-output', 'stale-output'))

        PHASE = 'permanent-sql-jobs-human-audit-and-zero-locks'
        subject = get('auth/me').json()['subject']
        audits = get('audit', params={'actorSubject': subject, 'take': 500}).json()
        for identifier in execution_ids:
            assert all(any(row.get('jobId') == identifier and row['action'] == action and row['actorSubject'] == subject for row in audits)
                for action in ('files.tree-unpack.confirmed', 'files.tree-unpack.result'))
        assert all(re.fullmatch('[a-f0-9-]{36}', identifier) for identifier in ids + execution_ids)
        literals = ','.join("'" + identifier + "'" for identifier in ids)
        executions = ','.join("'" + identifier + "'" for identifier in execution_ids)
        sql = "SELECT json_build_object('intents',(SELECT count(*) FROM file_tree_unpacking_operations WHERE id IN (" + literals + ")),'active',(SELECT count(*) FROM file_tree_unpacking_operations WHERE id IN (" + literals + ") AND completed_at_utc IS NULL),'locks',(SELECT count(*) FROM file_path_locks WHERE operation_id IN (" + executions + ")))::text;"
        result = json.loads(subprocess.check_output(['docker', 'exec', '-u', 'postgres', 'database-platform-postgres', 'psql', '-X', '-v', 'ON_ERROR_STOP=1', '-d', 'whaledeck', '-At', '-c', sql], text=True))
        assert result == {'intents': len(ids), 'active': 0, 'locks': 0}

        PHASE = 'ordinary-real-cookie-and-valid-csrf-denied'
        helper = subprocess.Popen([sys.executable, '-u', str(Path(__file__).with_name('ephemeral_browser_user.py'))],
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
            assert ordinary.get(base + '/api/v1/files/tree-unpackings/' + ids[0], timeout=20).status_code == 403
            denied = ordinary.post(base + '/api/v1/files/tree-unpackings', json=command('input.tar', 'ordinary-denied'),
                headers={**ordinary_api.headers, 'Idempotency-Key': str(uuid.uuid4())}, timeout=20)
            assert denied.status_code == 403 and not (folder / 'ordinary-denied').exists()
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

        PHASE = 'exact-owned-cleanup'
        assert (folder.stat().st_dev, folder.stat().st_ino, folder.stat().st_uid) == (identity.st_dev, identity.st_ino, 0) and folder.resolve() == folder
        for line in Path('/proc/self/mountinfo').read_text().splitlines():
            mount = line.split()[4]
            assert mount != str(folder) and not mount.startswith(str(folder) + '/')
        expected = set(inputs) | set(outputs) | {'.whaledeck-files'}
        assert {str(item.relative_to(folder)) for item in folder.rglob('*')} == expected
        assert {name: proof(folder / name) for name in inputs} == inputs
        assert {name: proof(folder / name) for name in outputs} == outputs
        private = folder / '.whaledeck-files'
        info = private.lstat()
        assert stat.S_ISDIR(info.st_mode) and info.st_uid == 0 and stat.S_IMODE(info.st_mode) == 0o700 and not any(private.iterdir())
        for name, value in outputs.items():
            if stat.S_ISREG(value[2]):
                (folder / name).unlink()
        for name in sorted((name for name, value in outputs.items() if stat.S_ISDIR(value[2])), key=lambda value: (value.count('/'), value), reverse=True):
            (folder / name).rmdir()
        for name in inputs:
            (folder / name).unlink()
        private.rmdir()
        folder.rmdir()
        print(json.dumps({'realDirectoryUnpackingBff': 'passed', 'binaryBytes': len(content),
            'bothFormatsOriginalLoginJobsWorkerInstalledUdsAndFullOutputDisk': True,
            'inputProofHumanAuditCancelProtectedChangedInput': True, 'exactOwnedFixtureRemoved': True,
            'immutableJobsAuditIntentsAndRootReceiptsRetained': True,
            'ordinaryCookieCsrfDenied': True, 'crossAdministratorDispositionAndRealBrowser': 'not-covered'}), flush=True)
    finally:
        if folder.exists():
            print(json.dumps({'ownedUnpackingBffFixturePreserved': marker, 'phase': PHASE}), flush=True)
        session.cookies.clear()
        session.close()


if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        frames = traceback.extract_tb(error.__traceback__)
        print(json.dumps({'realDirectoryUnpackingBff': 'failed', 'phase': PHASE,
            'failureType': type(error).__name__, 'sourceLine': frames[-1].lineno if frames else None}), flush=True)
        raise SystemExit(1) from None
