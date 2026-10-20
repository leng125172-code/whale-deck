#!/usr/bin/env python3
"""Actual Cookie/BFF/SQL/Worker/installed-UDS overwrite acceptance.

Run through run_with_ephemeral_admin only after the supplied revision is
installed. Only newly owned payloads are changed. An uncertain write is never
retried; failures preserve the fixture. Durable jobs, captures, archives and
receipts are retained, not deleted behind the catalogue. No browser or physical
independent-disk disaster recovery is claimed.
"""
from datetime import datetime
import hashlib
import itertools
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
from validate_file_tree_restoring_bff import sql_identifiers
from validate_tree_overwrite_restoring_agent import preserve_evidence, REPOSITORY

ROOT = 'whaledeck.files.shared'
DATA = Path('/data/WhaleDeck/files')
STATE = Path('/var/lib/whaledeck-files')
KIND = 'tree-overwrite-restorings'
TERMINAL = ('Succeeded', 'Failed', 'Canceled', 'Expired', 'Acknowledged')
PHASE = 'startup'
PUBLIC_FIELDS = set('id planningJobId executionJobId state command entryCount totalBytes targetEntryCount targetTotalBytes readyExpiresAtUtc resultCode version publishedVersion partialRetained confirmation archiveBytes archiveSha256 overwrittenCaptureId backupMayBeRetained backupVerified originalsRetained targetMayHaveChanged backupArchiveBytes backupArchiveSha256'.split())


def identifier(value):
    assert type(value) is str and re.fullmatch('[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}', value)
    assert str(uuid.UUID(value)) == value and uuid.UUID(value).int
    return value


def hash_value(value):
    return type(value) is str and re.fullmatch('[A-F0-9]{64}', value) is not None


def location(value):
    assert type(value) is dict and set(value) == {'rootId', 'path'}
    assert type(value['rootId']) is str and 1 <= len(value['rootId']) <= 128
    path = value['path']
    assert type(path) is str and 1 <= len(path) <= 2048 and not any(character in path for character in ('\\', '\0', '\r', '\n'))
    assert all(part not in ('', '.', '..') for part in path.split('/'))


def public_response(row, command=None):
    assert type(row) is dict and set(row) == PUBLIC_FIELDS
    identifier(row['id']); assert row['planningJobId'] == row['id']
    current = row['command']
    assert type(current) is dict and set(current) == {'captureId', 'source', 'target', 'targetExpectedVersion', 'captureKind'}
    identifier(current['captureId']); location(current['source']); location(current['target'])
    assert current['captureKind'] in ('Trash', 'Overwrite') and hash_value(current['targetExpectedVersion'])
    if command is not None:
        assert type(command) is dict and set(command) == {'captureId', 'target', 'targetExpectedVersion'}
        assert all(current[key] == value for key, value in command.items())
    execution = row['executionJobId']
    if execution is not None:
        identifier(execution); assert execution not in (row['id'], current['captureId'])
    assert row['state'] in ('Queued', 'Planning', 'Ready', 'OverwriteRestoringQueued', 'OverwriteRestoring', 'Unknown', *TERMINAL)
    assert type(row['version']) is int and row['version'] >= 0
    assert row['resultCode'] is None or type(row['resultCode']) is str and re.fullmatch('FILES_[A-Z_]{1,74}', row['resultCode'])
    for flag in ('partialRetained', 'backupMayBeRetained', 'backupVerified', 'originalsRetained', 'targetMayHaveChanged'):
        assert type(row[flag]) is bool
    has_plan = row['entryCount'] is not None or row['totalBytes'] is not None
    if has_plan:
        for count, size in (('entryCount', 'totalBytes'), ('targetEntryCount', 'targetTotalBytes')):
            assert type(row[count]) is int and 1 <= row[count] <= 10000
            assert type(row[size]) is int and 0 <= row[size] <= 16 * 1024**3
        assert type(row['archiveBytes']) is int and 1 <= row['archiveBytes'] <= 17 * 1024**3 and hash_value(row['archiveSha256'])
        assert type(row['readyExpiresAtUtc']) is str
        assert datetime.fromisoformat(row['readyExpiresAtUtc'].replace('Z', '+00:00')).utcoffset() is not None
    else:
        assert all(row[key] is None for key in ('targetEntryCount', 'targetTotalBytes', 'archiveBytes', 'archiveSha256', 'readyExpiresAtUtc'))
    if row['state'] in ('Ready', 'OverwriteRestoringQueued', 'OverwriteRestoring', 'Unknown', 'Succeeded', 'Acknowledged'): assert has_plan
    if row['state'] in ('OverwriteRestoringQueued', 'OverwriteRestoring', 'Unknown', 'Succeeded', 'Acknowledged'): assert execution is not None
    if row['backupVerified']:
        assert execution is not None and row['overwrittenCaptureId'] == execution and row['backupMayBeRetained']
        assert type(row['backupArchiveBytes']) is int and 1 <= row['backupArchiveBytes'] <= 17 * 1024**3
        assert hash_value(row['backupArchiveSha256'])
    else:
        assert all(row[key] is None for key in ('overwrittenCaptureId', 'backupArchiveBytes', 'backupArchiveSha256'))
    assert not row['targetMayHaveChanged'] or row['backupVerified']
    assert not row['originalsRetained'] or row['targetMayHaveChanged']
    if row['state'] == 'Succeeded':
        assert hash_value(row['publishedVersion']) and not row['partialRetained']
        assert row['backupVerified'] and row['originalsRetained'] and row['targetMayHaveChanged']
    else: assert row['publishedVersion'] is None
    confirmation = row['confirmation']
    if row['state'] == 'Ready':
        assert confirmation is not None and execution is None and not row['backupMayBeRetained']
    if confirmation is not None:
        assert row['state'] == 'Ready' and type(confirmation) is dict and set(confirmation) == {'planHash', 'expiresAtUtc', 'warning'}
        assert type(confirmation['planHash']) is str and 1 <= len(confirmation['planHash']) <= 4096
        assert confirmation['expiresAtUtc'] == row['readyExpiresAtUtc'] and type(confirmation['warning']) is str
    return row


def cleanup_payloads(folder, identity, expected_paths, file_proofs):
    assert folder.parent == DATA and re.fullmatch('wdv-overwrite-bff-[a-f0-9]{16}', folder.name)
    assert folder.resolve() == folder and proof(folder)[:2] == identity
    assert folder.lstat().st_uid == 0 and stat.S_IMODE(folder.lstat().st_mode) == 0o700
    for line in Path('/proc/self/mountinfo').read_text().splitlines():
        mount = line.split()[4]; assert mount != str(folder) and not mount.startswith(str(folder) + '/')
    members = list(itertools.islice(folder.rglob('*'), 65)); assert len(members) <= 64
    assert {str(path.relative_to(folder)) for path in members} == expected_paths
    observed = {path: proof(path) for path in members}
    assert all(stat.S_ISREG(value[2]) or stat.S_ISDIR(value[2]) for value in observed.values())
    assert {path for path, value in observed.items() if stat.S_ISREG(value[2])} == set(file_proofs)
    assert all(observed[path] == value for path, value in file_proofs.items())
    assert all(proof(path) == value for path, value in observed.items())
    for path in file_proofs: path.unlink()
    for path in sorted((path for path, value in observed.items() if stat.S_ISDIR(value[2])), key=lambda item: len(item.parts), reverse=True): path.rmdir()
    folder.rmdir(); assert not folder.exists()


def main():
    global PHASE
    assert os.geteuid() == 0 and len(sys.argv) == 2 and re.fullmatch('[a-f0-9]{40}', sys.argv[1])
    revision = sys.argv[1]
    assert json.loads(subprocess.check_output(['/opt/whaledeck-agent/WhaleDeck.Agent', '--build-info'], text=True))['sourceRevision'] == revision
    assert json.loads(subprocess.check_output(['docker', 'exec', 'whaledeck-worker', 'dotnet', 'WhaleDeck.Worker.dll', '--build-info'], text=True))['sourceRevision'] == revision
    assert DATA.resolve() == DATA and DATA.is_dir()
    registry = next(item for item in json.loads(Path('/etc/whaledeck/file-roots.json').read_bytes()) if item['id'] == ROOT)
    assert registry['path'] == str(DATA) and {'files-list', 'files-download', 'files-upload', 'files-mutate', 'files-history', 'files-archive', 'files-tree-trash', 'files-tree-restore', 'files-tree-overwrite-restore'} <= set(registry['allowedActions'])
    base = os.environ.get('WHALEDECK_URL', 'http://192.168.100.13:8080').rstrip('/')
    session, _ = authenticate(base, required_environment('VALIDATION_USERNAME'), required_environment('VALIDATION_PASSWORD'))
    api = Api(base, session)
    marker = 'wdv-overwrite-bff-' + uuid.uuid4().hex[:16]
    folder = DATA / marker; assert not os.path.lexists(folder)
    os.umask(0o077); folder.mkdir(mode=0o700); identity = proof(folder)[:2]
    evidence = REPOSITORY / 'deploy/artifacts' / ('overwrite-evidence-' + marker.removeprefix('wdv-overwrite-bff-'))
    evidence.mkdir(mode=0o700)
    print(json.dumps({'ownedOverwriteBffFixture': marker, 'runtimeRevision': revision}), flush=True)
    ids, executions, other_jobs = [], [], []
    archive_proofs, output_proofs = {}, {}

    def get(path, status=200, **kwargs):
        response = session.get(base + '/api/v1/' + path, timeout=30, **kwargs)
        assert response.status_code == status, 'read-status-mismatch'
        return response

    def post(path, body=None, status=202, key=None):
        response = session.post(base + '/api/v1/' + path, json=body, headers={**api.headers, **({'Idempotency-Key': key} if key else {})}, timeout=40)
        assert response.status_code == status, 'write-status-mismatch'
        return response

    def wait(kind, identifier, states):
        deadline = time.monotonic() + 240
        while time.monotonic() < deadline:
            row = get('files/' + kind + '/' + identifier).json()
            if kind == KIND: public_response(row)
            if row['state'] in states: return row
            assert row['state'] not in TERMINAL + ('Unknown',), 'unexpected-terminal-result'
            time.sleep(0.5)
        raise RuntimeError('overwrite-task-deadline')

    def queue(command):
        key = str(uuid.uuid4()); response = post('files/' + KIND, command, key=key)
        row = public_response(response.json(), command); ids.append(row['id'])
        assert row['planningJobId'] == row['id'] and row['executionJobId'] is None
        assert response.headers.get('Location') == '/api/v1/files/' + KIND + '/' + row['id']
        assert get('files/' + KIND + '/by-key/' + key).json()['id'] == row['id']
        assert post('files/' + KIND, command, key=key).json()['id'] == row['id']
        conflict = {**command, 'targetExpectedVersion': ('A' if command['targetExpectedVersion'][0] != 'A' else 'B') * 64}
        post('files/' + KIND, conflict, 409, key)
        return row

    def current_version(name):
        entries = get('files/entries', params={'rootId': ROOT, 'path': marker}).json()['entries']
        return next(item['version'] for item in entries if item['name'] == name)

    def capture_proof(capture, expected_kind, expected_location, original):
        operation = uuid.UUID(identifier(capture['id'])).hex
        saved_path = store / (operation + '.json'); plain_file(saved_path, 32768)
        saved = json.loads(saved_path.read_bytes())
        assert saved['operationId'] == operation and saved['state'] == 'Verified' and saved.get('captureKind', 'Trash') == expected_kind
        assert saved['source'] == expected_location and saved['totalBytes'] == original[6] and saved['entryCount'] == 3
        assert saved['archiveBytes'] == capture['archiveBytes'] and saved['archiveSha256'] == capture['archiveSha256']
        manifest_path = store / (operation + '.manifest.json'); plain_file(manifest_path, 16 * 1024**2)
        assert proof(manifest_path)[-1] == saved['manifestFileSha256']
        manifest = json.loads(manifest_path.read_bytes())
        assert manifest['manifestSha256'] == capture['manifestSha256'] and manifest['rootId'] == ROOT and manifest['path'] == expected_location['path']
        assert manifest['rootVersion'] == expected_location['expectedVersion']
        assert {entry['path'] for entry in manifest['entries']} == {'', '空目录', '中文.bin'}
        member = next(entry for entry in manifest['entries'] if entry['path'] == '中文.bin')
        assert tuple(member['sourceIdentity']) == original[:8] and member['sha256'] == original[-1]
        archive = store / (operation + '.tar.gz'); plain_file(archive)
        archive_proof = proof(archive)
        assert archive_proof[:2] == tuple(saved['archiveIdentity']) and archive_proof[6] == saved['archiveBytes'] and archive_proof[-1] == saved['archiveSha256']
        with tarfile.open(archive, 'r:gz') as reader:
            members = reader.getmembers()
            assert {item.name.rstrip('/') for item in members} == {'.', '空目录', '中文.bin'} and all(item.isfile() or item.isdir() for item in members)
            member = next(item for item in members if item.name == '中文.bin'); assert member.size == original[6]
            with reader.extractfile(member) as stream: assert hashlib.file_digest(stream, 'sha256').hexdigest().upper() == original[-1]
        for suffix in ('.json', '.manifest.json', '.tar.gz', '.lock'):
            path = store / (operation + suffix); plain_file(path, None if suffix == '.tar.gz' else 16 * 1024**2); archive_proofs[path] = proof(path)
        assert saved['differentSourceDevice'] == (original[0] != archive_proof[0])
        return saved

    def confirm(row):
        body = {'planHash': row['confirmation']['planHash'], 'confirmed': True, 'version': row['version']}
        first = public_response(post('files/' + KIND + '/' + row['id'] + '/confirm', body, key=row['id']).json())
        execution = identifier(first['executionJobId']); executions.append(execution)
        assert post('files/' + KIND + '/' + row['id'] + '/confirm', body, key=row['id']).json()['executionJobId'] == execution
        result = public_response(wait(KIND, row['id'], TERMINAL + ('Unknown',)))
        assert result['state'] == 'Succeeded' and result['overwrittenCaptureId'] == execution
        return result

    try:
        PHASE = 'runtime-and-owned-distinct-directories'
        assert get('system/status').json()['build']['sourceRevision'] == revision
        config = json.loads(Path('/etc/whaledeck/file-backups.json').read_bytes())
        store = Path(config['mountPoint']) / config['targetRelative'] / 'directory-archives'
        assert store.resolve() == store and store.is_dir() and store.lstat().st_uid == 0
        originals = {}
        for name in ('original', 'target'):
            tree = folder / name; tree.mkdir(mode=0o750); (tree / '空目录').mkdir(mode=0o710)
            data = tree / '中文.bin'; data.write_bytes(os.urandom(2 * 1024**2 + 7)); data.chmod(0o640)
            os.utime(data, ns=(1712345678123456789, 1712345678123456789))
            originals[name] = (proof(tree), proof(data))
        preserve_evidence(evidence, 'baseline.json', {'fixture': marker, 'fixtureIdentity': identity, 'runtimeRevision': revision, 'originals': originals})
        source = {'rootId': ROOT, 'path': marker + '/original'}; target = {'rootId': ROOT, 'path': marker + '/target'}
        recycle_command = {'source': source, 'expectedVersion': current_version('original')}
        recycling = post('files/tree-recyclings', recycle_command, key=str(uuid.uuid4())).json(); other_jobs.append(recycling['id'])
        ready = wait('tree-recyclings', recycling['id'], ('Ready',))
        started = post('files/tree-recyclings/' + ready['id'] + '/confirm', {'planHash': ready['confirmation']['planHash'], 'confirmed': True, 'version': ready['version']}, key=ready['id']).json()
        other_jobs.append(started['executionJobId'])
        recycled = wait('tree-recyclings', ready['id'], TERMINAL + ('Unknown',)); assert recycled['state'] == 'Succeeded' and recycled['sourceRemoved']
        capture_id = recycled['captureId']; assert capture_id == started['executionJobId'] and not (folder / 'original').exists()
        listing = get('files/tree-recyclings/captures', params=source).json(); assert len(listing['items']) == 1 and listing['next'] is None
        capture = listing['items'][0]; assert capture['id'] == capture_id and capture['source'] == recycle_command
        saved_source = capture_proof(capture, 'Trash', {**source, 'expectedVersion': recycle_command['expectedVersion']}, originals['original'][1])
        source_private = folder / '.whaledeck-files' / (uuid.UUID(capture_id).hex + '.tree-trash')
        assert proof(source_private / '中文.bin')[:8] == originals['original'][1][:8] and proof(source_private / '中文.bin')[-1] == originals['original'][1][-1]
        # Deliberately consume the historical capture from a new login binding.
        # Capture ownership remains the old login; the new operation is not
        # permitted to inherit that login's planning/execution authority.
        previous = session
        session, _ = authenticate(base, required_environment('VALIDATION_USERNAME'), required_environment('VALIDATION_PASSWORD'))
        previous.cookies.clear(); previous.close(); api = Api(base, session)

        PHASE = 'real-cookie-csrf-origin-contract-cancel-and-stale-plan'
        command = {'captureId': capture_id, 'target': target, 'targetExpectedVersion': current_version('target')}
        anonymous = requests.Session(); anonymous.trust_env = False
        try: assert anonymous.get(base + '/api/v1/files/' + KIND + '/' + str(uuid.uuid4()), timeout=20).status_code == 401
        finally: anonymous.close()
        assert session.post(base + '/api/v1/files/' + KIND, json=command, timeout=20).status_code in (400, 403)
        assert session.post(base + '/api/v1/files/' + KIND, json=command, headers={**api.headers, 'Origin': 'http://foreign.invalid', 'Idempotency-Key': str(uuid.uuid4())}, timeout=20).status_code == 403
        schema = session.get(base + '/openapi/v1.json', timeout=30); assert schema.status_code == 200
        assert all('/api/v1/files/' + KIND + suffix in schema.json()['paths'] for suffix in ('', '/{id}', '/by-key/{key}', '/{id}/confirm', '/{id}/cancel', '/{id}/acknowledge'))
        post('files/' + KIND, {**command, 'captureOwner': '0' * 64}, 400, str(uuid.uuid4()))
        post('files/' + KIND, {key: value for key, value in command.items() if key != 'targetExpectedVersion'}, 400, str(uuid.uuid4()))
        post('files/' + KIND, {**command, 'target': {'rootId': ROOT, 'path': '../escape'}}, 400, str(uuid.uuid4()))
        canceled = wait(KIND, queue(command)['id'], ('Ready',)); public_response(canceled, command)
        post('files/' + KIND + '/' + canceled['id'] + '/cancel', key=canceled['id'])
        assert wait(KIND, canceled['id'], TERMINAL)['state'] == 'Canceled'
        post('files/' + KIND + '/' + canceled['id'] + '/confirm', {'planHash': canceled['confirmation']['planHash'], 'confirmed': True, 'version': canceled['version']}, 409, canceled['id'])
        assert proof(folder / 'target/中文.bin') == originals['target'][1]
        stale = wait(KIND, queue({**command, 'targetExpectedVersion': '0' * 64})['id'], TERMINAL + ('Unknown',))
        assert stale['state'] == 'Failed' and not stale['backupMayBeRetained'] and proof(folder / 'target/中文.bin') == originals['target'][1]

        PHASE = 'ready-same-login-binding-and-real-first-overwrite'
        planned = public_response(wait(KIND, queue(command)['id'], ('Ready',)), command)
        assert planned['command']['source'] == source and planned['command']['captureKind'] == 'Trash'
        assert (planned['entryCount'], planned['totalBytes'], planned['targetEntryCount'], planned['targetTotalBytes']) == (3, originals['original'][1][6], 3, originals['target'][1][6])
        assert all(planned[key] == capture[key] for key in ('archiveBytes', 'archiveSha256'))
        assert proof(folder / 'target/中文.bin') == originals['target'][1] and all(proof(path) == value for path, value in archive_proofs.items())
        other, _ = authenticate(base, required_environment('VALIDATION_USERNAME'), required_environment('VALIDATION_PASSWORD'))
        try:
            assert other.get(base + '/api/v1/files/' + KIND + '/' + planned['id'], timeout=20).status_code == 404
            other_api = Api(base, other)
            assert other.post(base + '/api/v1/files/' + KIND + '/' + planned['id'] + '/confirm', json={'planHash': planned['confirmation']['planHash'], 'confirmed': True, 'version': planned['version']}, headers={**other_api.headers, 'Idempotency-Key': planned['id']}, timeout=20).status_code == 403
        finally: other.cookies.clear(); other.close()
        first = confirm(planned)
        first_output = proof(folder / 'target/中文.bin'); first_tree = proof(folder / 'target')
        assert first_output[-1] == originals['original'][1][-1] and first_output[:2] != originals['original'][1][:2]
        assert (first_output[3], first_output[4], stat.S_IMODE(first_output[2]), first_output[7]) == (registry['ownerUid'], registry['ownerGid'], 0o640, originals['original'][1][7])
        old_private = folder / '.whaledeck-files' / (uuid.UUID(first['executionJobId']).hex + '.tree-overwrite-restore')
        assert proof(old_private)[:2] == originals['target'][0][:2] and proof(old_private / '中文.bin')[:8] == originals['target'][1][:8] and proof(old_private / '中文.bin')[-1] == originals['target'][1][-1]
        target_captures = get('files/tree-recyclings/captures', params=target).json()
        overwritten = next(item for item in target_captures['items'] if item['id'] == first['overwrittenCaptureId'])
        assert overwritten['kind'] == 'Overwrite' and hash_value(overwritten['contentSha256'])
        saved_target = capture_proof(overwritten, 'Overwrite', {**target, 'expectedVersion': command['targetExpectedVersion']}, originals['target'][1])
        assert saved_target['owner'] != saved_source['owner'] and saved_target['archiveBytes'] == first['backupArchiveBytes'] and saved_target['archiveSha256'] == first['backupArchiveSha256']
        receipt_path = STATE / 'tree-overwrite-restoring' / (uuid.UUID(first['executionJobId']).hex + '.json'); plain_file(receipt_path, 32768)
        receipt = json.loads(receipt_path.read_bytes()); assert receipt['state'] == 'Completed' and tuple(receipt['newIdentity']) == first_tree[:2]
        assert receipt['plan']['command']['captureOwner'] == saved_source['owner'] and receipt['owner'] == saved_target['owner']
        assert overwritten['contentSha256'] == saved_target['contentSha256'] == receipt['plan']['targetContentSha256']
        assert receipt['plan']['targetContentSha256'] != receipt['plan']['targetTreeSha256']

        PHASE = 'overwrite-history-reuse-through-both-public-workflows'
        absent_command = {'captureId': overwritten['id'], 'target': {'rootId': ROOT, 'path': marker + '/restored-old'}}
        absent = post('files/tree-restorings', absent_command, key=str(uuid.uuid4())).json(); other_jobs.append(absent['id'])
        absent_ready = wait('tree-restorings', absent['id'], ('Ready',)); assert absent_ready['command']['captureKind'] == 'Overwrite'
        absent_started = post('files/tree-restorings/' + absent['id'] + '/confirm', {'planHash': absent_ready['confirmation']['planHash'], 'confirmed': True, 'version': absent_ready['version']}, key=absent['id']).json(); other_jobs.append(absent_started['executionJobId'])
        assert wait('tree-restorings', absent['id'], TERMINAL + ('Unknown',))['state'] == 'Succeeded'
        restored_old = proof(folder / 'restored-old/中文.bin'); assert restored_old[-1] == originals['target'][1][-1] and restored_old[:2] != originals['target'][1][:2]
        assert (restored_old[3], restored_old[4], stat.S_IMODE(restored_old[2]), restored_old[7]) == (registry['ownerUid'], registry['ownerGid'], 0o640, originals['target'][1][7])
        reuse_command = {'captureId': overwritten['id'], 'target': target, 'targetExpectedVersion': current_version('target')}
        reuse = wait(KIND, queue(reuse_command)['id'], ('Ready',)); assert reuse['command']['captureKind'] == 'Overwrite'
        second = confirm(reuse)
        final_output = proof(folder / 'target/中文.bin'); assert final_output[-1] == originals['target'][1][-1] and final_output[:2] != originals['target'][1][:2]
        assert (final_output[3], final_output[4], stat.S_IMODE(final_output[2]), final_output[7]) == (registry['ownerUid'], registry['ownerGid'], 0o640, originals['target'][1][7])
        new_private = folder / '.whaledeck-files' / (uuid.UUID(second['executionJobId']).hex + '.tree-overwrite-restore')
        assert proof(new_private)[:2] == first_tree[:2] and proof(new_private / '中文.bin')[:8] == first_output[:8] and proof(new_private / '中文.bin')[-1] == first_output[-1]
        second_capture = next(item for item in get('files/tree-recyclings/captures', params=target).json()['items'] if item['id'] == second['overwrittenCaptureId'])
        capture_proof(second_capture, 'Overwrite', {**target, 'expectedVersion': reuse_command['targetExpectedVersion']}, first_output)
        assert all(proof(path) == value for path, value in archive_proofs.items())

        PHASE = 'human-audit-durable-sql-outbox-jobs-and-zero-locks'
        subject = get('auth/me').json()['subject']; audits = get('audit', params={'actorSubject': subject, 'take': 500}).json()
        for execution in executions:
            assert all(any(item.get('jobId') == execution and item['action'] == action and item['actorSubject'] == subject for item in audits) for action in ('files.tree-overwrite-restore.confirmed', 'files.tree-overwrite-restore.result'))
        literals = sql_identifiers(ids); all_jobs = sql_identifiers(ids + executions + other_jobs)
        captures = sql_identifiers([capture_id, first['overwrittenCaptureId'], second['overwrittenCaptureId']])
        sql = "SELECT json_build_object('intents',(SELECT count(*) FROM file_tree_overwrite_restoring_operations WHERE id IN (" + literals + ")),'active',(SELECT count(*) FROM file_tree_overwrite_restoring_operations WHERE id IN (" + literals + ") AND completed_at_utc IS NULL),'locks',(SELECT count(*) FROM file_path_locks WHERE operation_id IN (" + all_jobs + ")),'jobs',(SELECT count(*) FROM operation_jobs WHERE id IN (" + all_jobs + ")),'outboxPending',(SELECT count(*) FROM outbox_messages WHERE payload_json->>'jobId' IN (" + all_jobs + ") AND processed_at_utc IS NULL),'captures',(SELECT count(*) FROM file_tree_captures WHERE id IN (" + captures + ")),'overwrites',(SELECT count(*) FROM file_tree_captures WHERE id IN (" + captures + ") AND kind='Overwrite'))::text;"
        result = json.loads(subprocess.check_output(['docker', 'exec', '-u', 'postgres', 'database-platform-postgres', 'psql', '-X', '-v', 'ON_ERROR_STOP=1', '-d', 'whaledeck', '-At', '-c', sql], text=True))
        assert result == {'intents': len(ids), 'active': 0, 'locks': 0, 'jobs': len(ids + executions + other_jobs), 'outboxPending': 0, 'captures': 3, 'overwrites': 2}

        PHASE = 'ordinary-real-cookie-and-valid-csrf-denied'
        helper = subprocess.Popen([sys.executable, '-u', str(Path(__file__).with_name('ephemeral_browser_user.py'))], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        name, password = 'wd-browser-' + uuid.uuid4().hex, 'Wd!' + secrets.token_hex(40); ordinary = None
        try:
            helper.stdin.write(json.dumps({'username': name, 'password': password, 'administrator': False}) + '\n'); helper.stdin.flush()
            assert json.loads(helper.stdout.readline())['ready'] is True
            ordinary, _ = authenticate(base, name, password, require_administrator=False); password = None; ordinary_api = Api(base, ordinary)
            assert ordinary.get(base + '/api/v1/files/' + KIND + '/' + ids[0], timeout=20).status_code == 403
            assert ordinary.post(base + '/api/v1/files/' + KIND, json=reuse_command, headers={**ordinary_api.headers, 'Idempotency-Key': str(uuid.uuid4())}, timeout=20).status_code == 403
        finally:
            password = None
            if ordinary is not None: ordinary.cookies.clear(); ordinary.close()
            helper.stdin.close(); helper.stdin = None
            cleaned, private_error = helper.communicate(timeout=45); private_error = None
            assert helper.returncode == 0 and any(json.loads(line).get('temporaryBrowserUserRemoved') is True for line in cleaned.splitlines())

        PHASE = 'independent-final-evidence-and-exact-owned-payload-cleanup'
        preserve_evidence(evidence, 'rpc-result.json', {'first': first, 'second': second, 'canceledId': canceled['id'], 'staleId': stale['id'], 'jobs': ids + executions + other_jobs, 'sql': result})
        trees = (folder / 'target', folder / 'restored-old', source_private, old_private, new_private)
        expected_paths = {'.whaledeck-files'}
        for tree in trees:
            relative = str(tree.relative_to(folder)); expected_paths.update((relative, relative + '/空目录', relative + '/中文.bin'))
            output_proofs[tree / '中文.bin'] = proof(tree / '中文.bin')
        assert output_proofs[folder / 'target/中文.bin'] == final_output and output_proofs[folder / 'restored-old/中文.bin'] == restored_old
        assert output_proofs[source_private / '中文.bin'][:8] == originals['original'][1][:8]
        assert all(proof(path) == value for path, value in archive_proofs.items())
        cleanup_payloads(folder, identity, expected_paths, output_proofs)
        print(json.dumps({'realDirectoryOverwriteRestoringBff': 'passed', 'runtimeRevision': revision, 'binaryBytesPerTree': originals['original'][1][6],
            'realCookieCsrfLoginBoundPlansJobsWorkerInstalledUdsSqlAuditAndZeroLocks': True,
            'wholeSourceDisplacedOutputArchivesAndOverwriteCaptureReuse': True,
            'ordinaryDeniedCancelStaleVersionAndIdempotency': True, 'exactOwnedPayloadsRemoved': True,
            'durableCatalogueArchivesReceiptsJobsAndAuditPreserved': True,
            'differentSourceDevice': saved_target['differentSourceDevice'], 'browserAndInterruptedSwap': 'not-covered'}), flush=True)
    finally:
        if folder.exists(): print(json.dumps({'ownedOverwriteBffFixturePreserved': marker, 'phase': PHASE}), flush=True)
        session.cookies.clear(); session.close()


if __name__ == '__main__':
    try: main()
    except Exception as error:
        frames = [frame for frame in traceback.extract_tb(error.__traceback__) if Path(frame.filename).resolve() == Path(__file__).resolve()]
        print(json.dumps({'realDirectoryOverwriteRestoringBff': 'failed', 'phase': PHASE, 'failureType': type(error).__name__, 'sourceLine': frames[-1].lineno if frames else None, 'privateDetailsWithheld': True}), flush=True)
        raise SystemExit(1) from None
