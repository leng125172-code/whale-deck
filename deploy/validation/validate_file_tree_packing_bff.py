#!/usr/bin/env python3
"""Real original-login directory plans/jobs/SQL/Worker/installed UDS packing and receipt HTTP downloads.

Root-owned exact fixture only. Failure preserves uncertain files and all
permanent evidence. No simulated Root, production fault injection or UI claim.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import secrets
import shutil
import stat
import tarfile
import subprocess
import sys
import time
import traceback
import uuid

import requests
from validate_databases import Api
from validate_oidc import authenticate, required_environment

PHASE = 'startup'
ROOT = 'whaledeck.files.shared'
DATA = Path('/data/WhaleDeck/files')
TERMINAL = ('Succeeded', 'Failed', 'Canceled', 'Expired', 'Acknowledged')


def main():
    global PHASE
    assert os.geteuid() == 0 and DATA.resolve() == DATA and DATA.is_dir()
    base = os.environ.get('WHALEDECK_URL', 'http://192.168.100.13:8080').rstrip('/')
    session, _ = authenticate(base, required_environment('VALIDATION_USERNAME'), required_environment('VALIDATION_PASSWORD'))
    api = Api(base, session); marker = 'wdv-packing-bff-' + uuid.uuid4().hex[:16]; folder = DATA / marker
    assert not os.path.lexists(folder); folder.mkdir(mode=0o700); identity = folder.stat()
    print(json.dumps({'ownedTreeBffFixture': marker}), flush=True)
    source = folder / 'source'; source.mkdir(); (source / '空').mkdir(); (source / '代码').mkdir()
    content = os.urandom(2 * 1024 * 1024 + 7); original = source / '代码/payload.bin'; original.write_bytes(content); original.chmod(0o640)
    stale_source = folder / 'stale-source'; stale_source.mkdir(); stale_file = stale_source / 'data.bin'; stale_file.write_bytes(b'before-plan')
    protected = folder / 'protected'; protected.mkdir(); (protected / '.env').write_bytes(b'fixture-only-not-a-secret')
    ids = []; execution_ids = []; acceptance = None

    def get(path, status=200, **kwargs):
        result = session.get(base + '/api/v1/' + path, timeout=30, **kwargs); assert result.status_code == status, 'read-status-mismatch'; return result

    def post(path, body=None, status=202, key=None):
        result = session.post(base + '/api/v1/' + path, json=body, headers={**api.headers, **({'Idempotency-Key': key} if key else {})}, timeout=40)
        assert result.status_code == status, 'write-status-mismatch'; return result

    def command(name='source', target='packed.tar'):
        location = marker + '/' + name
        entry = get('files/entry', params={'rootId': ROOT, 'path': location}).json()
        return {'source': {'rootId': ROOT, 'path': location}, 'expectedVersion': entry['version'], 'target': {'rootId': ROOT, 'path': marker + '/' + target}, 'format': 'tar.gz' if target.endswith('.tar.gz') else 'tar'}

    def queue(value):
        key = str(uuid.uuid4()); response = post('files/tree-packings', value, key=key); row = response.json(); ids.append(row['id'])
        assert response.headers.get('Location') == '/api/v1/files/tree-packings/' + row['id']
        assert row['planningJobId'] == row['id'] and row['executionJobId'] is None
        assert get('files/tree-packings/by-key/' + key).json()['id'] == row['id']
        assert post('files/tree-packings', value, key=key).json()['id'] == row['id']
        return row

    def wait(identifier, states, reconcile_unknown=False):
        deadline = time.monotonic() + 120
        while time.monotonic() < deadline:
            row = get('files/tree-packings/' + identifier).json()
            if row['state'] in states: return row
            assert row['state'] not in TERMINAL + (() if reconcile_unknown else ('Unknown',)), 'unexpected-terminal-result'
            time.sleep(0.4)
        raise RuntimeError('directory-task-deadline')

    def confirm(ready):
        body = {'planHash': ready['confirmation']['planHash'], 'confirmed': True, 'version': ready['version']}
        response = post('files/tree-packings/' + ready['id'] + '/confirm', body, key=ready['id']); row = response.json()
        assert response.headers.get('Location') == '/api/v1/files/tree-packings/' + row['id']
        assert row['executionJobId'] and row['executionJobId'] != row['planningJobId']; execution_ids.append(row['executionJobId'])
        assert post('files/tree-packings/' + ready['id'] + '/confirm', body, key=ready['id']).json()['executionJobId'] == row['executionJobId']
        return row, body

    try:
        PHASE = 'origin-csrf-anonymous-and-openapi'
        roots = get('files/roots').json(); assert any(row['id'] == ROOT and 'files-tree-pack' in row['actions'] for row in roots)
        anonymous = requests.Session(); anonymous.trust_env = False
        assert anonymous.get(base + '/api/v1/files/tree-packings/' + str(uuid.uuid4()), timeout=15).status_code == 401
        value = command()
        assert session.post(base + '/api/v1/files/tree-packings', json=value, timeout=15).status_code in (400, 403)
        assert session.post(base + '/api/v1/files/tree-packings', json=value, headers={**api.headers, 'Origin': 'http://foreign.invalid', 'Idempotency-Key': str(uuid.uuid4())}, timeout=15).status_code == 403
        schema = session.get(base + '/openapi/v1.json', timeout=30); assert schema.status_code == 200
        assert all('/api/v1/files/tree-packings' + suffix in schema.json()['paths'] for suffix in ('', '/{id}', '/by-key/{key}', '/{id}/confirm', '/{id}/cancel', '/{id}/acknowledge', '/{id}/download'))
        post('files/tree-packings', {**value, 'target': {'rootId': ROOT, 'path': '../escape'}}, 400, str(uuid.uuid4()))

        PHASE = 'original-plan-recovery-and-login-binding'
        planned = queue(value); ready = wait(planned['id'], ('Ready',)); assert ready['executionJobId'] is None and not (folder / 'packed.tar').exists()
        assert ready['entryCount'] == 4 and ready['totalBytes'] == len(content) and ready['confirmation']
        assert get('files/tree-packings/' + ready['id']).json()['confirmation']['planHash'] == ready['confirmation']['planHash']
        other, _ = authenticate(base, required_environment('VALIDATION_USERNAME'), required_environment('VALIDATION_PASSWORD')); other_api = Api(base, other)
        assert other.get(base + '/api/v1/files/tree-packings/' + ready['id'], timeout=15).status_code == 404
        denied = other.post(base + '/api/v1/files/tree-packings/' + ready['id'] + '/confirm', json={'planHash': ready['confirmation']['planHash'], 'confirmed': True, 'version': ready['version']},
            headers={**other_api.headers, 'Idempotency-Key': ready['id']}, timeout=20)
        assert denied.status_code == 403

        PHASE = 'confirmed-two-jobs-worker-packing-and-independent-observer'
        original_proof = (original.stat().st_ino, original.stat().st_size, original.stat().st_mtime_ns, original.stat().st_mode, original.stat().st_uid, original.stat().st_gid)
        completed_artifacts = []
        for format, prepared, name in (('tar', ready, 'packed.tar'), ('tar.gz', None, 'packed.tar.gz')):
            if prepared is None: prepared = wait(queue(command(target=name))['id'], ('Ready',))
            copied, confirmation = confirm(prepared)
            completed = wait(copied['id'], TERMINAL + ('Unknown',))
            assert completed['state'] == 'Succeeded' and not completed['partialRetained']
            assert completed['command']['format'] == format and completed['archiveBytes'] > 0
            target = folder / name; proof = target.stat()
            assert proof.st_uid == proof.st_gid == 1000 and proof.st_nlink == 1 and stat.S_IMODE(proof.st_mode) == 0o640
            with target.open('rb') as stream: digest = hashlib.file_digest(stream, 'sha256').hexdigest().upper()
            assert completed['archiveBytes'] == proof.st_size and completed['archiveSha256'] == digest
            with tarfile.open(target) as archive:
                assert set(archive.getnames()) == {'.', '空', '代码', '代码/payload.bin'}
                assert archive.getmember('空').isdir() and archive.getmember('代码').isdir()
                with archive.extractfile('代码/payload.bin') as stream: assert hashlib.file_digest(stream, 'sha256').digest() == hashlib.sha256(content).digest()
            assert (original.stat().st_ino, original.stat().st_size, original.stat().st_mtime_ns, original.stat().st_mode, original.stat().st_uid, original.stat().st_gid) == original_proof
            assert original.read_bytes() == content
            assert post('files/tree-packings/' + prepared['id'] + '/confirm', confirmation, key=prepared['id']).json()['executionJobId'] == copied['executionJobId']
            PHASE = 'receipt-http-download-current-login-full-sha'
            for client in (session, other):
                response = client.get(base + '/api/v1/files/tree-packings/' + copied['id'] + '/download',
                    params={'expectedVersion': completed['publishedVersion']}, stream=True, timeout=45)
                assert response.status_code == 200 and int(response.headers['Content-Length']) == proof.st_size
                received = hashlib.sha256(); count = 0
                with response:
                    for chunk in response.iter_content(32768): received.update(chunk); count += len(chunk)
                assert count == proof.st_size and received.hexdigest().upper() == digest
            assert other.get(base + '/api/v1/files/tree-packings/' + copied['id'], timeout=15).status_code == 404
            get('files/tree-packings/' + copied['id'] + '/download', 400, params={'expectedVersion': completed['publishedVersion']}, headers={'Range': 'bytes=0-9'})
            completed_artifacts.append(completed)

        PHASE = 'different-current-admin-and-ordinary-real-cookie-csrf'
        original_subject = get('auth/me').json()['subject']
        for administrator in (True, False):
            helper = subprocess.Popen([sys.executable, '-u', str(Path(__file__).with_name('ephemeral_browser_user.py'))],
                stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
            name = 'wd-browser-' + uuid.uuid4().hex; password = 'Wd!' + secrets.token_hex(40); client = None
            try:
                helper.stdin.write(json.dumps({'username': name, 'password': password, 'administrator': administrator}) + '\n'); helper.stdin.flush()
                assert json.loads(helper.stdout.readline())['ready'] is True
                client, _ = authenticate(base, name, password, require_administrator=administrator); password = None
                current_api = Api(base, client); current = client.get(base + '/api/v1/auth/me', timeout=20).json()
                assert current['isAdministrator'] is administrator and current['subject'] != original_subject
                for artifact in completed_artifacts:
                    path = base + '/api/v1/files/tree-packings/' + artifact['id']
                    assert client.get(path, timeout=20).status_code == (404 if administrator else 403)
                    response = client.get(path + '/download', params={'expectedVersion': artifact['publishedVersion']}, stream=True, timeout=45)
                    with response:
                        assert response.status_code == (200 if administrator else 403)
                        if administrator:
                            digest = hashlib.sha256(); count = 0
                            for chunk in response.iter_content(32768): digest.update(chunk); count += len(chunk)
                            assert count == artifact['archiveBytes'] and digest.hexdigest().upper() == artifact['archiveSha256']
                if administrator:
                    current_audits = client.get(base + '/api/v1/audit', params={'actorSubject': current['subject'], 'take': 100}, timeout=20)
                    assert current_audits.status_code == 200
                    assert all(any(row['action'] == 'files.tree-pack.download' and row['result'] == 'Validated' and
                        row.get('jobId') == artifact['executionJobId'] and row['actorSubject'] == current['subject'] for row in current_audits.json()) for artifact in completed_artifacts)
                else:
                    denied = client.post(base + '/api/v1/files/tree-packings', json=command(target='ordinary-denied.tar'),
                        headers={**current_api.headers, 'Idempotency-Key': str(uuid.uuid4())}, timeout=20)
                    assert denied.status_code == 403
            finally:
                password = None
                if client is not None: client.cookies.clear(); client.close()
                helper.stdin.close(); cleanup = helper.stdout.read(); private_error = helper.stderr.read(); helper.wait(timeout=45); private_error = None
                assert helper.returncode == 0 and any(json.loads(line).get('temporaryBrowserUserRemoved') is True for line in cleanup.splitlines())

        PHASE = 'ready-cancel-and-protected-member'
        cancel_ready = wait(queue(command(target='canceled.tar'))['id'], ('Ready',))
        post('files/tree-packings/' + cancel_ready['id'] + '/cancel', {}, key=cancel_ready['id'])
        post('files/tree-packings/' + cancel_ready['id'] + '/confirm', {'planHash': cancel_ready['confirmation']['planHash'], 'confirmed': True, 'version': cancel_ready['version']}, 409, cancel_ready['id'])
        assert not (folder / 'canceled.tar').exists()
        rejected = queue(command('protected', 'protected.tar')); assert wait(rejected['id'], TERMINAL)['state'] == 'Failed'
        assert not (folder / 'protected.tar').exists()

        PHASE = 'changed-descendant-after-plan-never-replayed'
        stale_ready = wait(queue(command('stale-source', 'stale.tar'))['id'], ('Ready',)); stale_file.write_bytes(b'changed-after-plan')
        stale_copy, _ = confirm(stale_ready); assert wait(stale_copy['id'], TERMINAL, reconcile_unknown=True)['state'] == 'Failed'
        assert not (folder / 'stale.tar').exists() and stale_file.read_bytes() == b'changed-after-plan'

        PHASE = 'human-audit-and-real-sql-observer'
        subject = get('auth/me').json()['subject']; audits = get('audit', params={'actorSubject': subject, 'take': 500}).json()
        assert any(row.get('jobId') == copied['executionJobId'] and row['action'] == 'files.tree-pack.confirmed' for row in audits)
        assert any(row.get('jobId') == copied['executionJobId'] and row['action'] == 'files.tree-pack.result' for row in audits)
        assert all(re.fullmatch('[a-f0-9-]{36}', identifier) for identifier in ids + execution_ids)
        literals = ','.join("'" + identifier + "'" for identifier in ids); executions = ','.join("'" + identifier + "'" for identifier in execution_ids)
        sql = "SELECT json_build_object('intents',(SELECT count(*) FROM file_tree_packing_operations WHERE id IN (" + literals + ")),'active',(SELECT count(*) FROM file_tree_packing_operations WHERE id IN (" + literals + ") AND completed_at_utc IS NULL),'locks',(SELECT count(*) FROM file_path_locks WHERE operation_id IN (" + executions + ")))::text;"
        observed = json.loads(subprocess.check_output(['docker', 'exec', '-u', 'postgres', 'database-platform-postgres', 'psql', '-X', '-v', 'ON_ERROR_STOP=1', '-d', 'whaledeck', '-At', '-c', sql], text=True))
        assert observed == {'intents': len(ids), 'active': 0, 'locks': 0}
        assert any(row['action'] == 'files.tree-pack.download' and row['result'] == 'Validated' for row in audits)
        PHASE = 'cleanup-verified-membership'
        expected_dirs = {'source', 'source/空', 'source/代码', 'stale-source', 'protected', '.whaledeck-files'}
        expected_files = {'source/代码/payload.bin', 'packed.tar', 'packed.tar.gz', 'stale-source/data.bin', 'protected/.env'}
        dirs = set(); files = set(); current = folder.lstat()
        assert (current.st_dev, current.st_ino, current.st_uid) == (identity.st_dev, identity.st_ino, 0) and folder.resolve() == folder
        for line in Path('/proc/self/mountinfo').read_text().splitlines(): assert line.split()[4] != str(folder) and not line.split()[4].startswith(str(folder) + '/')
        for parent, names, entries in os.walk(folder, followlinks=False):
            for name in names + entries:
                path = Path(parent) / name; info = path.lstat(); assert info.st_dev == identity.st_dev and not stat.S_ISLNK(info.st_mode) and info.st_uid in (0, 1000)
                if stat.S_ISDIR(info.st_mode): dirs.add(str(path.relative_to(folder)))
                else: assert stat.S_ISREG(info.st_mode) and info.st_nlink == 1; files.add(str(path.relative_to(folder)))
        assert dirs == expected_dirs and files == expected_files
        shutil.rmtree(folder)
        acceptance = {'realDirectoryPackingBff': 'passed', 'binaryBytes': len(content), 'originalLoginPlansJobsSqlWorkerInstalledUdsHumanAudit': 'passed',
            'bothFormatsCurrentAndNewLoginHttpFullShaAndIndependentArchiveDisk': 'passed', 'cancelProtectedAndChangedDescendantNoReplay': 'passed',
            'differentCurrentAdminDownloadAndAuditOrdinaryCookieCsrfDenial': 'passed',
            'exactOwnedFixtureRemoved': True, 'immutableJobsAuditIntentsAndRootReceiptsRetained': True, 'realBrowserAndProductionFaultInjection': 'separate-evidence'}
    finally:
        if folder.exists(): print(json.dumps({'ownedTreeBffFixturePreserved': marker, 'phase': PHASE}))
    assert acceptance is not None; print(json.dumps(acceptance))


if __name__ == '__main__':
    try: main()
    except Exception as error:
        frames = traceback.extract_tb(error.__traceback__)
        print(json.dumps({'realDirectoryPackingBff': 'failed', 'phase': PHASE, 'failureType': type(error).__name__, 'sourceLine': frames[-1].lineno if frames else None}))
        raise SystemExit(1) from None
