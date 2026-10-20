#!/usr/bin/env python3
"""Installed real Cookie/BFF/SQL/Worker/UDS directory restoration acceptance.

Run only via run_with_ephemeral_admin after installing the exact revision.
An independently created synthetic directory is recycled through the public
workflow and restored through the public capture-ID-only contract. Unknown
results are never retried. Failure preserves evidence; success removes only
the exact inode/checksum-proven synthetic payloads, not jobs/audits/receipts.
No browser, cross-administrator interruption or physical disaster-recovery claim.
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
from validate_file_tree_recycling_bff import plain_file

ROOT = 'whaledeck.files.shared'
DATA = Path('/data/WhaleDeck/files')
STATE = Path('/var/lib/whaledeck-files')
TERMINAL = ('Succeeded', 'Failed', 'Canceled', 'Expired', 'Acknowledged')
PHASE = 'startup'


def sql_identifiers(values):
    assert values and all(isinstance(value, str) and re.fullmatch('[a-f0-9-]{36}', value)
        and str(uuid.UUID(value)) == value and uuid.UUID(value).int for value in values)
    return ','.join("'" + value + "'" for value in values)


def public_response(row):
    assert set(row['command']) == {'captureId', 'source', 'target', 'captureKind'}
    assert row['command']['captureKind'] == 'Trash'
    assert not any(key in row for key in ('ownerHash', 'captureOwner', 'loginBinding', 'planJson', 'commandJson'))
    return row


def main():
    global PHASE
    assert os.geteuid() == 0 and len(sys.argv) == 2 and re.fullmatch('[a-f0-9]{40}', sys.argv[1])
    revision = sys.argv[1]
    assert json.loads(subprocess.check_output(['/opt/whaledeck-agent/WhaleDeck.Agent', '--build-info'], text=True))['sourceRevision'] == revision
    assert DATA.resolve() == DATA and DATA.is_dir()
    registry = next(item for item in json.loads(Path('/etc/whaledeck/file-roots.json').read_bytes()) if item['id'] == ROOT)
    assert registry['path'] == str(DATA) and {'files-list', 'files-download', 'files-upload', 'files-mutate', 'files-history', 'files-archive', 'files-tree-trash', 'files-tree-restore'} <= set(registry['allowedActions'])
    base = os.environ.get('WHALEDECK_URL', 'http://192.168.100.13:8080').rstrip('/')
    session, _ = authenticate(base, required_environment('VALIDATION_USERNAME'), required_environment('VALIDATION_PASSWORD'))
    api = Api(base, session)
    marker = 'wdv-restoring-bff-' + uuid.uuid4().hex[:16]
    folder = DATA / marker
    assert not os.path.lexists(folder)
    os.umask(0o077)
    folder.mkdir(mode=0o700)
    identity = proof(folder)[:2]
    print(json.dumps({'ownedRestoringBffFixture': marker, 'runtimeRevision': revision}), flush=True)
    ids, executions = [], []

    def get(path, status=200, **kwargs):
        response = session.get(base + '/api/v1/' + path, timeout=30, **kwargs)
        assert response.status_code == status, 'read-status-mismatch'
        return response

    def post(path, body=None, status=202, key=None):
        response = session.post(base + '/api/v1/' + path, json=body,
            headers={**api.headers, **({'Idempotency-Key': key} if key else {})}, timeout=40)
        assert response.status_code == status, 'write-status-mismatch'
        return response

    def wait(kind, identifier, states):
        deadline = time.monotonic() + 210
        while time.monotonic() < deadline:
            row = get('files/' + kind + '/' + identifier).json()
            if kind == 'tree-restorings': public_response(row)
            if row['state'] in states: return row
            assert row['state'] not in TERMINAL + ('Unknown',), 'unexpected-terminal-result'
            time.sleep(0.5)
        raise RuntimeError('restoring-task-deadline')

    def queue(command):
        key = str(uuid.uuid4())
        response = post('files/tree-restorings', command, key=key)
        row = public_response(response.json())
        ids.append(row['id'])
        assert row['planningJobId'] == row['id'] and row['executionJobId'] is None
        assert response.headers.get('Location') == '/api/v1/files/tree-restorings/' + row['id']
        assert get('files/tree-restorings/by-key/' + key).json()['id'] == row['id']
        assert post('files/tree-restorings', command, key=key).json()['id'] == row['id']
        return row

    try:
        PHASE = 'owned-directory-and-real-recycling-capture'
        source = folder / 'original'; source.mkdir(mode=0o750)
        (source / '空目录').mkdir(mode=0o710)
        leaf = source / '中文完整字节.bin'
        payload = os.urandom(2 * 1024**2 + 7)
        leaf.write_bytes(payload); leaf.chmod(0o640)
        os.utime(leaf, ns=(1712345678123456789, 1712345678123456789))
        original = proof(leaf)
        entries = get('files/entries', params={'rootId': ROOT, 'path': marker}).json()['entries']
        entry = next(item for item in entries if item['name'] == source.name)
        recycle_command = {'source': {'rootId': ROOT, 'path': marker + '/original'}, 'expectedVersion': entry['version']}
        recycle = post('files/tree-recyclings', recycle_command, key=str(uuid.uuid4())).json()
        ready = wait('tree-recyclings', recycle['id'], ('Ready',))
        recycling = post('files/tree-recyclings/' + ready['id'] + '/confirm',
            {'planHash': ready['confirmation']['planHash'], 'confirmed': True, 'version': ready['version']}, key=ready['id']).json()
        captured = wait('tree-recyclings', ready['id'], TERMINAL + ('Unknown',))
        assert captured['state'] == 'Succeeded' and captured['sourceRemoved'] and not source.exists()
        capture_id = captured['captureId']; capture_operation = uuid.UUID(capture_id).hex
        assert capture_id == recycling['executionJobId']
        listing = get('files/tree-recyclings/captures', params={'rootId': ROOT, 'path': recycle_command['source']['path']}).json()
        assert len(listing['items']) == 1 and listing['next'] is None
        capture = listing['items'][0]
        assert capture['id'] == capture_id and capture['source'] == recycle_command
        config = json.loads(Path('/etc/whaledeck/file-backups.json').read_bytes())
        store = Path(config['mountPoint']) / config['targetRelative'] / 'directory-archives'
        assert store.resolve() == store and store.is_dir() and store.lstat().st_uid == 0
        archive_proofs = {}
        for suffix in ('.json', '.manifest.json', '.tar.gz', '.lock'):
            path = store / (capture_operation + suffix)
            plain_file(path, None if suffix == '.tar.gz' else 16 * 1024**2)
            archive_proofs[path] = proof(path)
        archive = store / (capture_operation + '.tar.gz')
        saved = json.loads((store / (capture_operation + '.json')).read_bytes())
        assert saved['state'] == 'Verified' and type(saved['differentSourceDevice']) is bool
        assert proof(archive)[-1] == capture['archiveSha256'] and proof(archive)[6] == capture['archiveBytes']
        with tarfile.open(archive, 'r:gz') as reader:
            members = reader.getmembers()
            assert {member.name.rstrip('/') for member in members} == {'.', '空目录', leaf.name}
            member = next(item for item in members if item.name == leaf.name)
            assert member.isfile() and member.size == len(payload)
            with reader.extractfile(member) as content:
                assert content.read(len(payload) + 1) == payload and not content.read(1)
        private = folder / '.whaledeck-files' / (capture_operation + '.tree-trash')
        assert proof(private / leaf.name)[:8] == original[:8] and proof(private / leaf.name)[-1] == original[-1]

        PHASE = 'csrf-origin-contract-and-absent-target-plan'
        command = {'captureId': capture_id, 'target': recycle_command['source']}
        anonymous = requests.Session(); anonymous.trust_env = False
        try:
            assert anonymous.get(base + '/api/v1/files/tree-restorings/' + str(uuid.uuid4()), timeout=20).status_code == 401
        finally: anonymous.close()
        assert session.post(base + '/api/v1/files/tree-restorings', json=command, timeout=20).status_code in (400, 403)
        assert session.post(base + '/api/v1/files/tree-restorings', json=command,
            headers={**api.headers, 'Origin': 'http://foreign.invalid', 'Idempotency-Key': str(uuid.uuid4())}, timeout=20).status_code == 403
        schema = session.get(base + '/openapi/v1.json', timeout=30)
        assert schema.status_code == 200 and all('/api/v1/files/tree-restorings' + suffix in schema.json()['paths']
            for suffix in ('', '/{id}', '/by-key/{key}', '/{id}/confirm', '/{id}/cancel', '/{id}/acknowledge'))
        post('files/tree-restorings', {**command, 'captureOwner': '0' * 64}, 400, str(uuid.uuid4()))
        post('files/tree-restorings', {**command, 'target': {'rootId': ROOT, 'path': '../escape'}}, 400, str(uuid.uuid4()))
        planned = wait('tree-restorings', queue(command)['id'], ('Ready',))
        assert planned['command'] == {**command, 'source': recycle_command['source'], 'captureKind': 'Trash'}
        assert all(planned[key] == capture[key] for key in ('entryCount', 'totalBytes', 'archiveBytes', 'archiveSha256'))
        assert not source.exists() and all(proof(path) == value for path, value in archive_proofs.items())
        confirmation = {'planHash': planned['confirmation']['planHash'], 'confirmed': True, 'version': planned['version']}
        other, _ = authenticate(base, required_environment('VALIDATION_USERNAME'), required_environment('VALIDATION_PASSWORD'))
        try:
            assert other.get(base + '/api/v1/files/tree-restorings/' + planned['id'], timeout=20).status_code == 404
            other_api = Api(base, other)
            assert other.post(base + '/api/v1/files/tree-restorings/' + planned['id'] + '/confirm', json=confirmation,
                headers={**other_api.headers, 'Idempotency-Key': planned['id']}, timeout=20).status_code == 403
        finally: other.cookies.clear(); other.close()

        PHASE = 'real-worker-installed-uds-and-full-restored-disk'
        started = post('files/tree-restorings/' + planned['id'] + '/confirm', confirmation, key=planned['id']).json()
        execution = started['executionJobId']; executions.append(execution)
        assert execution and execution not in (planned['id'], capture_id)
        assert post('files/tree-restorings/' + planned['id'] + '/confirm', confirmation, key=planned['id']).json()['executionJobId'] == execution
        completed = wait('tree-restorings', planned['id'], TERMINAL + ('Unknown',))
        assert completed['state'] == 'Succeeded' and not completed['partialRetained'] and re.fullmatch('[A-F0-9]{64}', completed['publishedVersion'])
        restored = proof(leaf)
        assert restored[:2] != original[:2] and restored[6] == len(payload) and restored[-1] == original[-1]
        assert (restored[3], restored[4], stat.S_IMODE(restored[2]), restored[7]) == (registry['ownerUid'], registry['ownerGid'], 0o640, original[7])
        assert leaf.read_bytes() == payload and proof(private / leaf.name)[:8] == original[:8]
        receipt_path = STATE / 'tree-restoring' / (uuid.UUID(execution).hex + '.json')
        plain_file(receipt_path, 32768); receipt = json.loads(receipt_path.read_bytes())
        assert receipt['state'] == 'Completed' and receipt['plan']['command']['captureId'] == capture_operation
        assert receipt['plan']['command']['captureOwner'] == saved['owner'] and tuple(receipt['newIdentity']) == proof(source)[:2]

        PHASE = 'cancel-and-existing-target-no-overwrite'
        canceled = wait('tree-restorings', queue({'captureId': capture_id, 'target': {'rootId': ROOT, 'path': marker + '/canceled'}})['id'], ('Ready',))
        post('files/tree-restorings/' + canceled['id'] + '/cancel', key=canceled['id'])
        assert wait('tree-restorings', canceled['id'], TERMINAL)['state'] == 'Canceled'
        post('files/tree-restorings/' + canceled['id'] + '/confirm', {'planHash': canceled['confirmation']['planHash'], 'confirmed': True, 'version': canceled['version']}, 409, canceled['id'])
        rejected = wait('tree-restorings', queue(command)['id'], TERMINAL)
        assert rejected['state'] == 'Failed' and proof(leaf) == restored and not (folder / 'canceled').exists()
        assert all(proof(path) == value for path, value in archive_proofs.items())
        subject = get('auth/me').json()['subject']
        audits = get('audit', params={'actorSubject': subject, 'take': 500}).json()
        assert all(any(item.get('jobId') == execution and item['action'] == action and item['actorSubject'] == subject for item in audits)
            for action in ('files.tree-restore.confirmed', 'files.tree-restore.result'))
        literals = sql_identifiers(ids); execution_literal = sql_identifiers(executions)
        sql = "SELECT json_build_object('intents',(SELECT count(*) FROM file_tree_restoring_operations WHERE id IN (" + literals + ")),'active',(SELECT count(*) FROM file_tree_restoring_operations WHERE id IN (" + literals + ") AND completed_at_utc IS NULL),'locks',(SELECT count(*) FROM file_path_locks WHERE operation_id IN (" + execution_literal + ")))::text;"
        result = json.loads(subprocess.check_output(['docker', 'exec', '-u', 'postgres', 'database-platform-postgres', 'psql', '-X', '-v', 'ON_ERROR_STOP=1', '-d', 'whaledeck', '-At', '-c', sql], text=True))
        assert result == {'intents': len(ids), 'active': 0, 'locks': 0}

        PHASE = 'ordinary-real-cookie-with-valid-csrf'
        helper = subprocess.Popen([sys.executable, '-u', str(Path(__file__).with_name('ephemeral_browser_user.py'))], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        name, password = 'wd-browser-' + uuid.uuid4().hex, 'Wd!' + secrets.token_hex(40)
        ordinary = None
        try:
            helper.stdin.write(json.dumps({'username': name, 'password': password, 'administrator': False}) + '\n'); helper.stdin.flush()
            assert json.loads(helper.stdout.readline())['ready'] is True
            ordinary, _ = authenticate(base, name, password, require_administrator=False)
            password = None; ordinary_api = Api(base, ordinary)
            assert ordinary.get(base + '/api/v1/files/tree-restorings/' + ids[0], timeout=20).status_code == 403
            assert ordinary.post(base + '/api/v1/files/tree-restorings', json=command, headers={**ordinary_api.headers, 'Idempotency-Key': str(uuid.uuid4())}, timeout=20).status_code == 403
        finally:
            password = None
            if ordinary is not None: ordinary.cookies.clear(); ordinary.close()
            helper.stdin.close(); helper.stdin = None
            cleanup, private_error = helper.communicate(timeout=45); private_error = None
            assert helper.returncode == 0 and any(json.loads(line).get('temporaryBrowserUserRemoved') is True for line in cleanup.splitlines())

        PHASE = 'exact-inode-and-checksum-proven-synthetic-cleanup'
        assert proof(folder)[:2] == identity and folder.resolve() == folder and folder.parent == DATA
        for line in Path('/proc/self/mountinfo').read_text().splitlines():
            mount = line.split()[4]; assert mount != str(folder) and not mount.startswith(str(folder) + '/')
        expected = {'original', 'original/' + leaf.name, 'original/空目录', '.whaledeck-files',
            '.whaledeck-files/' + private.name, '.whaledeck-files/' + private.name + '/' + leaf.name, '.whaledeck-files/' + private.name + '/空目录'}
        objects = {path: proof(path) for path in folder.rglob('*')}
        assert {str(path.relative_to(folder)) for path in objects} == expected
        assert all(stat.S_ISREG(value[2]) or stat.S_ISDIR(value[2]) for value in objects.values())
        assert all(proof(path) == value for path, value in {**objects, **archive_proofs}.items())
        assert proof(leaf) == restored and proof(private / leaf.name)[:8] == original[:8]
        for path, value in objects.items():
            if stat.S_ISREG(value[2]): path.unlink()
        for path in sorted((path for path, value in objects.items() if stat.S_ISDIR(value[2])), key=lambda path: len(path.parts), reverse=True): path.rmdir()
        folder.rmdir()
        for path in archive_proofs: path.unlink()
        print(json.dumps({'realDirectoryRestoringBff': 'passed', 'binaryBytes': len(payload),
            'originalLoginJobsWorkerInstalledUdsFullNewDiskAndOriginalInode': True,
            'archiveUnchangedCancelExistingTargetOrdinaryCsrfAuditAndZeroLocks': True,
            'differentSourceDevice': saved['differentSourceDevice'], 'exactOwnedSyntheticPayloadsRemoved': True,
            'immutableJobsCaptureCatalogueAuditAndRootReceiptsRetained': True,
            'browserCrossAdministratorInterruptionAndLargePayload': 'not-covered'}), flush=True)
    finally:
        if folder.exists(): print(json.dumps({'ownedRestoringBffFixturePreserved': marker, 'phase': PHASE}), flush=True)
        session.cookies.clear(); session.close()


if __name__ == '__main__':
    try: main()
    except Exception as error:
        frames = traceback.extract_tb(error.__traceback__)
        print(json.dumps({'realDirectoryRestoringBff': 'failed', 'phase': PHASE, 'failureType': type(error).__name__, 'sourceLine': frames[-1].lineno if frames else None}), flush=True)
        raise SystemExit(1) from None
