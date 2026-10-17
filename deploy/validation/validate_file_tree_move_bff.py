#!/usr/bin/env python3
"""Real original-login directory plans/jobs/SQL/Worker/installed UDS move.

Root-owned exact fixture only. Failure preserves uncertain files and all
permanent evidence. No simulated Root, production fault injection or UI claim.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import secrets
import stat
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
    api = Api(base, session); marker = 'wdv-move-bff-' + uuid.uuid4().hex[:16]; folder = DATA / marker
    assert not os.path.lexists(folder); folder.mkdir(mode=0o700); identity = folder.stat()
    print(json.dumps({'ownedMoveBffFixture': marker}), flush=True)
    source = folder / 'source'; source.mkdir(); (source / '空').mkdir(); (source / '代码').mkdir()
    content = os.urandom(2 * 1024 * 1024 + 7); original = source / '代码/payload.bin'; original.write_bytes(content); original.chmod(0o640)
    original_info = original.stat()
    policy = next(x for x in json.loads(Path('/etc/whaledeck/file-roots.json').read_bytes()) if x['id'] == ROOT)
    assert policy['path'] == str(DATA)
    stale_source = folder / 'stale-source'; stale_source.mkdir(); stale_file = stale_source / 'data.bin'; stale_file.write_bytes(b'before-plan')
    protected = folder / 'protected'; protected.mkdir(); (protected / '.env').write_bytes(b'fixture-only-not-a-secret')
    ids = []; execution_ids = []; acceptance = None

    def get(path, status=200, **kwargs):
        result = session.get(base + '/api/v1/' + path, timeout=30, **kwargs)
        if result.status_code != status:
            print(json.dumps({'moveProbeHttpStatus': result.status_code, 'expectedStatus': status, 'phase': PHASE}), flush=True)
        assert result.status_code == status, 'read-status-mismatch'; return result

    def post(path, body=None, status=202, key=None):
        result = session.post(base + '/api/v1/' + path, json=body, headers={**api.headers, **({'Idempotency-Key': key} if key else {})}, timeout=40)
        assert result.status_code == status, 'write-status-mismatch'; return result

    def command(name='source', target='moved'):
        location = marker + '/' + name
        entry = get('files/entry', params={'rootId': ROOT, 'path': location}).json()
        return {'source': {'rootId': ROOT, 'path': location}, 'expectedVersion': entry['version'], 'target': {'rootId': ROOT, 'path': marker + '/' + target}}

    def queue(value):
        key = str(uuid.uuid4()); row = post('files/tree-moves', value, key=key).json(); ids.append(row['id'])
        assert row['planningJobId'] == row['id'] and row['executionJobId'] is None
        assert get('files/tree-moves/by-key/' + key).json()['id'] == row['id']
        assert post('files/tree-moves', value, key=key).json()['id'] == row['id']
        return row

    def wait(identifier, states, reconcile_unknown=False):
        deadline = time.monotonic() + 120
        while time.monotonic() < deadline:
            row = get('files/tree-moves/' + identifier).json()
            if row['state'] in states: return row
            assert row['state'] not in TERMINAL + (() if reconcile_unknown else ('Unknown',)), 'unexpected-terminal-result'
            time.sleep(0.4)
        raise RuntimeError('directory-task-deadline')

    def confirm(ready):
        body = {'planHash': ready['confirmation']['planHash'], 'confirmed': True, 'version': ready['version']}
        row = post('files/tree-moves/' + ready['id'] + '/confirm', body, key=ready['id']).json()
        assert row['executionJobId'] and row['executionJobId'] != row['planningJobId']; execution_ids.append(row['executionJobId'])
        assert post('files/tree-moves/' + ready['id'] + '/confirm', body, key=ready['id']).json()['executionJobId'] == row['executionJobId']
        return row, body

    try:
        PHASE = 'origin-csrf-anonymous-and-openapi'
        roots = get('files/roots').json(); assert any(row['id'] == ROOT and 'files-tree-move' in row['actions'] for row in roots)
        anonymous = requests.Session(); anonymous.trust_env = False
        assert anonymous.get(base + '/api/v1/files/tree-moves/' + str(uuid.uuid4()), timeout=15).status_code == 401
        value = command()
        assert session.post(base + '/api/v1/files/tree-moves', json=value, timeout=15).status_code in (400, 403)
        assert session.post(base + '/api/v1/files/tree-moves', json=value, headers={**api.headers, 'Origin': 'http://foreign.invalid', 'Idempotency-Key': str(uuid.uuid4())}, timeout=15).status_code == 403
        schema = session.get(base + '/openapi/v1.json', timeout=30); assert schema.status_code == 200
        assert all('/api/v1/files/tree-moves' + suffix in schema.json()['paths'] for suffix in ('', '/{id}', '/by-key/{key}', '/{id}/confirm', '/{id}/cancel', '/{id}/acknowledge'))
        post('files/tree-moves', {**value, 'target': {'rootId': ROOT, 'path': '../escape'}}, 400, str(uuid.uuid4()))

        PHASE = 'original-plan-recovery-and-login-binding'
        planned = queue(value); ready = wait(planned['id'], ('Ready',)); assert ready['executionJobId'] is None and not (folder / 'moved').exists()
        assert ready['entryCount'] == 4 and ready['totalBytes'] == len(content) and ready['confirmation']
        assert get('files/tree-moves/' + ready['id']).json()['confirmation']['planHash'] == ready['confirmation']['planHash']
        other, _ = authenticate(base, required_environment('VALIDATION_USERNAME'), required_environment('VALIDATION_PASSWORD')); other_api = Api(base, other)
        assert other.get(base + '/api/v1/files/tree-moves/' + ready['id'], timeout=15).status_code == 404
        denied = other.post(base + '/api/v1/files/tree-moves/' + ready['id'] + '/confirm', json={'planHash': ready['confirmation']['planHash'], 'confirmed': True, 'version': ready['version']},
            headers={**other_api.headers, 'Idempotency-Key': ready['id']}, timeout=20)
        assert denied.status_code == 403

        PHASE = 'ordinary-identity-real-cookie-and-csrf-denied'
        helper = subprocess.Popen([sys.executable, '-u', str(Path(__file__).resolve().parents[2] / 'deploy/validation/ephemeral_browser_user.py')],
            stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        ordinary_name = 'wd-browser-' + uuid.uuid4().hex; ordinary_password = 'Wd!' + secrets.token_hex(40)
        try:
            helper.stdin.write(json.dumps({'username': ordinary_name, 'password': ordinary_password, 'administrator': False}) + '\n'); helper.stdin.flush()
            assert json.loads(helper.stdout.readline())['ready'] is True
            ordinary, _ = authenticate(base, ordinary_name, ordinary_password, require_administrator=False); ordinary_password = None
            ordinary_api = Api(base, ordinary)
            assert ordinary.get(base + '/api/v1/auth/me', timeout=20).json()['isAdministrator'] is False
            assert ordinary.get(base + '/api/v1/files/tree-moves/' + ready['id'], timeout=20).status_code == 403
            assert ordinary.post(base + '/api/v1/files/tree-moves', json=command('stale-source', 'ordinary-denied'),
                headers={**ordinary_api.headers, 'Idempotency-Key': str(uuid.uuid4())}, timeout=20).status_code == 403
            ordinary.close()
        finally:
            ordinary_password = None; helper.stdin.close()
            cleanup = helper.stdout.read(); private_error = helper.stderr.read(); helper.wait(timeout=45)
            assert helper.returncode == 0 and any(json.loads(line).get('temporaryBrowserUserRemoved') is True for line in cleanup.splitlines())
            private_error = None

        PHASE = 'confirmed-two-jobs-worker-move-and-independent-observer'
        copied, confirmation = confirm(ready); completed = wait(copied['id'], TERMINAL + ('Unknown',)); assert completed['state'] == 'Succeeded' and not completed['partialRetained']
        assert all(completed[key] is True for key in ('destinationPublished', 'sourceRemoved', 'sourceRetained'))
        target = folder / 'moved/代码/payload.bin'; assert hashlib.sha256(target.read_bytes()).digest() == hashlib.sha256(content).digest()
        before, after = original_info, target.stat()
        assert (before.st_size, before.st_mtime_ns, stat.S_IMODE(before.st_mode)) == (after.st_size, after.st_mtime_ns, stat.S_IMODE(after.st_mode))
        assert (after.st_uid, after.st_gid) == (policy['ownerUid'], policy['ownerGid']) and after.st_nlink == 1 and (before.st_dev, before.st_ino) != (after.st_dev, after.st_ino)
        assert not list((folder / 'moved/空').iterdir()) and not source.exists()
        operation_id = uuid.UUID(copied['executionJobId']).hex
        receipt_path = Path('/var/lib/whaledeck-files/tree-moves') / (operation_id + '.json')
        receipt_info = receipt_path.lstat(); assert stat.S_ISREG(receipt_info.st_mode) and receipt_info.st_uid == 0 and receipt_info.st_nlink == 1 and stat.S_IMODE(receipt_info.st_mode) == 0o600 and receipt_info.st_size <= 32768
        receipt = json.loads(receipt_path.read_bytes())
        assert receipt['operationId'] == operation_id and receipt['state'] == 'Completed' and receipt['sourceRemoved'] and receipt['sourceRetained'] and receipt['destinationPublished']
        root_command = receipt['plan']['copyPlan']['command']
        assert root_command['source']['rootId'] == root_command['target']['rootId'] == ROOT
        assert root_command['source']['path'] == marker + '/source' and root_command['target']['path'] == marker + '/moved'
        retained_relative = '.whaledeck-files/' + operation_id + '.tree-move'; retained = folder / retained_relative
        kept = (retained / '代码/payload.bin').stat()
        assert (kept.st_dev, kept.st_ino, kept.st_uid, kept.st_gid, kept.st_size, kept.st_mtime_ns, stat.S_IMODE(kept.st_mode), kept.st_nlink) == (
            before.st_dev, before.st_ino, before.st_uid, before.st_gid, before.st_size, before.st_mtime_ns, stat.S_IMODE(before.st_mode), 1)
        assert (retained / '代码/payload.bin').read_bytes() == content and not list((retained / '空').iterdir())
        assert [retained.stat().st_dev, retained.stat().st_ino] == receipt['retainedIdentity']
        manifest_path = receipt_path.with_name(operation_id + '.manifest.json'); manifest_info = manifest_path.lstat()
        assert stat.S_ISREG(manifest_info.st_mode) and manifest_info.st_uid == 0 and manifest_info.st_nlink == 1 and stat.S_IMODE(manifest_info.st_mode) == 0o600 and manifest_info.st_size <= 16 * 1024**2
        manifest = json.loads(manifest_path.read_bytes()); encoded = json.dumps(manifest, sort_keys=True, ensure_ascii=False, separators=(',', ':'), allow_nan=False).encode()
        assert hashlib.sha256(b'whaledeck-tree-move-v1\n' + encoded).hexdigest().upper() == receipt['manifestDigest']
        assert manifest['manifestSha256'] == receipt['plan']['copyPlan']['manifestSha256']
        assert post('files/tree-moves/' + ready['id'] + '/confirm', confirmation, key=ready['id']).json()['executionJobId'] == copied['executionJobId']

        PHASE = 'ready-cancel-and-protected-member'
        cancel_ready = wait(queue(command('stale-source', target='canceled'))['id'], ('Ready',))
        post('files/tree-moves/' + cancel_ready['id'] + '/cancel', {}, key=cancel_ready['id'])
        post('files/tree-moves/' + cancel_ready['id'] + '/confirm', {'planHash': cancel_ready['confirmation']['planHash'], 'confirmed': True, 'version': cancel_ready['version']}, 409, cancel_ready['id'])
        assert not (folder / 'canceled').exists()
        rejected = queue(command('protected', 'protected-copy')); assert wait(rejected['id'], TERMINAL)['state'] == 'Failed'
        assert not (folder / 'protected-copy').exists()

        PHASE = 'changed-descendant-after-plan-never-replayed'
        stale_ready = wait(queue(command('stale-source', 'stale-copy'))['id'], ('Ready',)); stale_file.write_bytes(b'changed-after-plan')
        stale_copy, _ = confirm(stale_ready)
        stale_result = wait(stale_copy['id'], TERMINAL + ('Unknown',), reconcile_unknown=True)
        assert not (folder / 'stale-copy').exists() and stale_file.read_bytes() == b'changed-after-plan'
        if stale_result['state'] == 'Unknown':
            # Missing child receipt after a durable start is deliberately unknown,
            # even if this observer can prove its own target absent. Never force
            # that permanent Root state to Aborted or wait for it to turn Failed.
            original_execution = stale_result['executionJobId']
            post('files/tree-moves/' + stale_copy['id'] + '/acknowledge', {'confirmed': True, 'version': stale_result['version']}, key=stale_copy['id'])
            stale_result = get('files/tree-moves/' + stale_copy['id']).json()
            assert stale_result['state'] == 'Acknowledged' and stale_result['executionJobId'] == original_execution
        else: assert stale_result['state'] == 'Failed'

        PHASE = 'human-audit-and-real-sql-observer'
        subject = get('auth/me').json()['subject']; audits = get('audit', params={'actorSubject': subject, 'take': 500}).json()
        assert any(row.get('jobId') == copied['executionJobId'] and row['action'] == 'files.tree-move.confirmed' for row in audits)
        assert any(row.get('jobId') == copied['executionJobId'] and row['action'] == 'files.tree-move.result' for row in audits)
        assert all(re.fullmatch('[a-f0-9-]{36}', identifier) for identifier in ids + execution_ids)
        literals = ','.join("'" + identifier + "'" for identifier in ids); executions = ','.join("'" + identifier + "'" for identifier in execution_ids)
        sql = "SELECT json_build_object('intents',(SELECT count(*) FROM file_tree_move_operations WHERE id IN (" + literals + ")),'active',(SELECT count(*) FROM file_tree_move_operations WHERE id IN (" + literals + ") AND completed_at_utc IS NULL),'locks',(SELECT count(*) FROM file_path_locks WHERE operation_id IN (" + executions + ")))::text;"
        observed = json.loads(subprocess.check_output(['docker', 'exec', '-u', 'postgres', 'database-platform-postgres', 'psql', '-X', '-v', 'ON_ERROR_STOP=1', '-d', 'whaledeck', '-At', '-c', sql], text=True))
        assert observed == {'intents': len(ids), 'active': 0, 'locks': 0}
        PHASE = 'cleanup-verified-membership'
        expected_dirs = {'moved', 'moved/空', 'moved/代码', 'stale-source', 'protected', '.whaledeck-files'}
        expected_files = {'moved/代码/payload.bin', 'stale-source/data.bin', 'protected/.env'}
        expected_dirs.update({retained_relative, retained_relative + '/空', retained_relative + '/代码'})
        expected_files.add(retained_relative + '/代码/payload.bin')
        dirs = set(); files = set(); current = folder.lstat()
        assert (current.st_dev, current.st_ino, current.st_uid) == (identity.st_dev, identity.st_ino, 0) and folder.resolve() == folder
        for line in Path('/proc/self/mountinfo').read_text().splitlines(): assert line.split()[4] != str(folder) and not line.split()[4].startswith(str(folder) + '/')
        for parent, names, entries in os.walk(folder, followlinks=False):
            for name in names + entries:
                path = Path(parent) / name; info = path.lstat(); assert info.st_dev == identity.st_dev and not stat.S_ISLNK(info.st_mode) and info.st_uid in (0, policy['ownerUid'])
                if stat.S_ISDIR(info.st_mode): dirs.add(str(path.relative_to(folder)))
                else: assert stat.S_ISREG(info.st_mode) and info.st_nlink == 1; files.add(str(path.relative_to(folder)))
        assert dirs == expected_dirs and files == expected_files
        shutil.rmtree(folder)
        acceptance = {'realDirectoryMoveBff': 'passed', 'binaryBytes': len(content), 'originalLoginPlansJobsSqlWorkerInstalledUdsHumanAudit': 'passed',
            'sourceAndTargetFullBytesModesMtimeAndOwnership': 'passed', 'retainedOriginalInodeFullBytesAndPrivateManifest': 'passed',
            'realOrdinaryCookieCsrfGetAndPostDenied': 'passed',
            'cancelProtectedAndChangedDescendantNoReplay': 'passed',
            'changedSourceOutcome': stale_result['state'],
            'exactOwnedFixtureRemoved': True, 'immutableJobsAuditIntentsAndRootReceiptsRetained': True, 'realBrowserAndProductionFaultInjection': 'separate-evidence'}
    finally:
        if folder.exists(): print(json.dumps({'ownedMoveBffFixturePreserved': marker, 'phase': PHASE}))
    assert acceptance is not None; print(json.dumps(acceptance))


if __name__ == '__main__':
    try: main()
    except Exception as error:
        frames = traceback.extract_tb(error.__traceback__)
        print(json.dumps({'realDirectoryMoveBff': 'failed', 'phase': PHASE, 'failureType': type(error).__name__, 'sourceLine': frames[-1].lineno if frames else None}))
        raise SystemExit(1) from None
