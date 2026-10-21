#!/usr/bin/env python3
"""Real Cookie/BFF/Worker/SQL/installed UDS cleanup of newly owned captures.

Run through run_with_ephemeral_admin as root after the supplied application
revision is deployed. Covers both capture kinds and all three explicit scopes.
No production capture is selected, no lost write is retried, and no archive,
journal or catalogue is deleted behind the public workflow. Failures preserve
the fixture. This is not a browser or interrupted-deletion acceptance.
"""
from datetime import datetime
import hashlib
import itertools
import json
import os
from pathlib import Path
import re
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
from validate_tree_purging_agent import preserve_evidence, tree

ROOT = 'whaledeck.files.shared'
DATA = Path('/data/WhaleDeck/files')
STATE = Path('/var/lib/whaledeck-files')
REPOSITORY = Path('/data/GitRepos/whale-deck')
KIND = 'tree-purgings'
SCOPES = ('Archive', 'Originals', 'All')
TERMINAL = ('Succeeded', 'Failed', 'Canceled', 'Expired', 'Acknowledged')
PUBLIC_FIELDS = set('id planningJobId executionJobId state command entryCount totalBytes archiveBytes readyExpiresAtUtc resultCode version deletionStarted archiveRemoved originalsRemoved confirmation'.split())
PHASE = 'startup'


def identifier(value):
    assert type(value) is str and re.fullmatch('[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}', value)
    assert str(uuid.UUID(value)) == value and uuid.UUID(value).int
    return value


def public_response(row):
    assert type(row) is dict and set(row) == PUBLIC_FIELDS
    identifier(row['id']); assert row['planningJobId'] == row['id']
    command = row['command']
    assert type(command) is dict and set(command) == {'captureId', 'source', 'captureKind', 'scope'}
    identifier(command['captureId'])
    assert command['captureKind'] in ('Trash', 'Overwrite') and command['scope'] in SCOPES
    source = command['source']
    assert type(source) is dict and set(source) == {'rootId', 'path'} and source['rootId'] == ROOT
    assert type(source['path']) is str and 1 <= len(source['path']) <= 2048
    assert not any(value in source['path'] for value in ('\\', '\0', '\r', '\n'))
    assert all(part not in ('', '.', '..') for part in source['path'].split('/'))
    execution = row['executionJobId']
    if execution is not None:
        identifier(execution); assert execution not in (row['id'], command['captureId'])
    assert type(row['version']) is int and row['version'] >= 0
    assert row['state'] in ('Queued', 'Planning', 'Ready', 'PurgingQueued', 'Purging', 'Unknown', *TERMINAL)
    assert row['resultCode'] is None or type(row['resultCode']) is str and re.fullmatch('FILES_[A-Z_]{1,74}', row['resultCode'])
    plan = any(row[key] is not None for key in ('entryCount', 'totalBytes', 'archiveBytes', 'readyExpiresAtUtc'))
    if plan:
        assert type(row['entryCount']) is int and 1 <= row['entryCount'] <= 10000
        assert type(row['totalBytes']) is int and 0 <= row['totalBytes'] <= 16 * 1024**3
        assert type(row['archiveBytes']) is int and 1 <= row['archiveBytes'] <= 17 * 1024**3
        assert type(row['readyExpiresAtUtc']) is str
        assert datetime.fromisoformat(row['readyExpiresAtUtc'].replace('Z', '+00:00')).utcoffset() is not None
    for flag in ('deletionStarted', 'archiveRemoved', 'originalsRemoved'):
        assert row[flag] is None or type(row[flag]) is bool
    assert not row['archiveRemoved'] or command['scope'] != 'Originals' and row['deletionStarted'] is True
    assert not row['originalsRemoved'] or command['scope'] != 'Archive' and row['deletionStarted'] is True
    if command['scope'] == 'Originals': assert row['archiveRemoved'] is False
    if command['scope'] == 'Archive': assert row['originalsRemoved'] is False
    if execution is None: assert all(row[key] is False for key in ('deletionStarted', 'archiveRemoved', 'originalsRemoved'))
    if row['state'] in ('Ready', 'PurgingQueued', 'Purging', 'Unknown', 'Succeeded', 'Acknowledged'): assert plan
    if row['state'] in ('PurgingQueued', 'Purging', 'Unknown', 'Succeeded', 'Acknowledged'): assert execution is not None
    if row['state'] in ('Queued', 'Planning', 'Ready'): assert execution is None
    if row['state'] == 'Unknown': assert row['deletionStarted'] is not False
    if row['state'] == 'Succeeded':
        assert row['deletionStarted'] is True
        assert row['archiveRemoved'] is (command['scope'] != 'Originals')
        assert row['originalsRemoved'] is (command['scope'] != 'Archive')
    confirmation = row['confirmation']
    if row['state'] == 'Ready':
        assert type(confirmation) is dict and set(confirmation) == {'planHash', 'expiresAtUtc', 'warning'}
        assert type(confirmation['planHash']) is str and 1 <= len(confirmation['planHash']) <= 8192
        assert confirmation['expiresAtUtc'] == row['readyExpiresAtUtc']
        assert type(confirmation['warning']) is str and 1 <= len(confirmation['warning']) <= 2048
    else: assert confirmation is None
    return row


def completed_root_receipt(saved, execution, capture, scope):
    # The internal journal is intentionally sparse: an unselected deletion
    # flag can be absent. The PUBLIC DTO still requires all three nullable
    # facts. Selected positive facts must be explicitly present, never inferred.
    assert type(saved) is dict and saved['state'] == 'Completed'
    assert saved['operationId'] == uuid.UUID(identifier(execution)).hex
    assert type(saved['owner']) is str and re.fullmatch('[A-F0-9]{64}', saved['owner'])
    assert saved['plan']['command']['captureId'] == uuid.UUID(identifier(capture)).hex
    assert saved['plan']['command']['scope'] == scope and scope in SCOPES
    assert saved['deletionStarted'] is True
    assert saved.get('archiveRemoved', False) is (scope != 'Originals')
    assert saved.get('originalsRemoved', False) is (scope != 'Archive')


def metadata_components(saved):
    assert type(saved) is dict and saved['state'] in ('Verified', 'Purged')
    originals = saved.get('originalsPurged', False)
    assert type(originals) is bool
    # Native capture metadata marks its archive through state=Purged, not a
    # non-existent archivePurged property. It retains the original manifest.
    return saved['state'] == 'Purged', originals


def cleanup_live_fixture(folder, identity, expected):
    assert folder.parent == DATA and re.fullmatch('wdv-purge-bff-[a-f0-9]{16}', folder.name)
    assert folder.resolve() == folder and proof(folder)[:2] == identity
    assert folder.lstat().st_uid == 0 and stat.S_IMODE(folder.lstat().st_mode) == 0o700
    for line in Path('/proc/self/mountinfo').read_text().splitlines():
        mount = line.split()[4]; assert mount != str(folder) and not mount.startswith(str(folder) + '/')
    members = list(itertools.islice(folder.rglob('*'), 33))
    assert len(members) <= 32 and set(members) == set(expected)
    for path in members:
        info = path.lstat(); value = expected[path]
        assert path.resolve() == path and (stat.S_ISREG(info.st_mode) or stat.S_ISDIR(info.st_mode))
        assert (info.st_dev, info.st_ino, info.st_mode, info.st_uid, info.st_gid) == value[:5]
        if stat.S_ISREG(info.st_mode): assert info.st_nlink == 1 and info.st_size == value[6]
    observed = {path: proof(path) for path in members}
    for path, value in observed.items():
        assert path.resolve() == path and (stat.S_ISREG(value[2]) or stat.S_ISDIR(value[2]))
        if stat.S_ISREG(value[2]): assert value[5] == 1 and value == expected[path]
        else: assert value[:5] == expected[path][:5]
    for path, value in observed.items():
        assert proof(path) == value
    for path, value in observed.items():
        if stat.S_ISREG(value[2]): path.unlink()
    for path in sorted((path for path, value in observed.items() if stat.S_ISDIR(value[2])), key=lambda value: len(value.parts), reverse=True):
        assert proof(path)[:2] == observed[path][:2]; path.rmdir()
    folder.rmdir(); assert not os.path.lexists(folder)


def main():
    global PHASE
    assert os.geteuid() == 0 and len(sys.argv) == 3
    application, agent = sys.argv[1:]
    assert all(re.fullmatch('[a-f0-9]{40}', value) for value in (application, agent))
    git = ['git', '-c', 'safe.directory=' + str(REPOSITORY), '-C', str(REPOSITORY)]
    validation = subprocess.check_output(git + ['rev-parse', 'HEAD'], text=True).strip()
    assert re.fullmatch('[a-f0-9]{40}', validation)
    assert not subprocess.check_output(git + ['status', '--porcelain'], text=True).strip()
    changes = subprocess.check_output(git + ['diff', '--name-only', application, validation], text=True).splitlines()
    assert all(path.startswith(('deploy/validation/', 'docs/')) or path == '.project-atlas.json' for path in changes)
    assert json.loads(subprocess.check_output(['/opt/whaledeck-agent/WhaleDeck.Agent', '--build-info'], text=True))['sourceRevision'] == agent
    assert json.loads(subprocess.check_output(['docker', 'exec', 'whaledeck-worker', 'dotnet', 'WhaleDeck.Worker.dll', '--build-info'], text=True))['sourceRevision'] == application
    assert DATA.resolve() == DATA and DATA.is_dir()
    registry = next(row for row in json.loads(Path('/etc/whaledeck/file-roots.json').read_bytes()) if row['id'] == ROOT)
    assert registry['path'] == str(DATA) and {'files-list', 'files-download', 'files-mutate', 'files-history', 'files-archive', 'files-tree-trash', 'files-tree-overwrite-restore', 'files-tree-purge'} <= set(registry['allowedActions'])
    base = os.environ.get('WHALEDECK_URL', 'http://192.168.100.13:8080').rstrip('/')
    session, _ = authenticate(base, required_environment('VALIDATION_USERNAME'), required_environment('VALIDATION_PASSWORD'))
    api = Api(base, session)
    marker = 'wdv-purge-bff-' + uuid.uuid4().hex[:16]
    folder = DATA / marker; assert not os.path.lexists(folder)
    os.umask(0o077); folder.mkdir(mode=0o700); identity = proof(folder)[:2]
    evidence = REPOSITORY / 'deploy/artifacts' / ('purge-evidence-' + marker.removeprefix('wdv-purge-bff-'))
    evidence.mkdir(mode=0o700)
    jobs, purges, executions, captures, live = [], [], [], [], {}
    print(json.dumps({'ownedPurgeBffFixture': marker, 'applicationRevision': application, 'agentRevision': agent, 'validationRevision': validation}), flush=True)

    def get(path, status=200, **kwargs):
        result = session.get(base + '/api/v1/' + path, timeout=30, **kwargs)
        assert result.status_code == status, 'read-status-mismatch'
        return result

    def post(path, body=None, status=202, key=None):
        result = session.post(base + '/api/v1/' + path, json=body, headers={**api.headers, **({'Idempotency-Key': key} if key else {})}, timeout=40)
        assert result.status_code == status, 'write-status-mismatch'
        return result

    def wait(kind, operation, states):
        deadline = time.monotonic() + 240
        while time.monotonic() < deadline:
            row = get('files/' + kind + '/' + identifier(operation)).json()
            if kind == KIND: public_response(row)
            if row['state'] in states: return row
            assert row['state'] not in (*TERMINAL, 'Unknown'), 'unexpected-terminal-result'
            time.sleep(0.5)
        raise RuntimeError('purging-task-deadline')

    def execute(kind, command):
        row = post('files/' + kind, command, key=str(uuid.uuid4())).json(); jobs.append(identifier(row['id']))
        row = wait(kind, row['id'], ('Ready',))
        body = {'planHash': row['confirmation']['planHash'], 'confirmed': True, 'version': row['version']}
        started = post('files/' + kind + '/' + row['id'] + '/confirm', body, key=row['id']).json()
        jobs.append(identifier(started['executionJobId']))
        result = wait(kind, row['id'], (*TERMINAL, 'Unknown'))
        assert result['state'] == 'Succeeded'
        return result

    def current_version(name):
        entries = get('files/entries', params={'rootId': ROOT, 'path': marker}).json()['entries']
        return next(row['version'] for row in entries if row['name'] == name)

    def catalogue(source, capture_id):
        result = get('files/tree-recyclings/captures', params=source).json()
        assert result['next'] is None
        return next(row for row in result['items'] if row['id'] == capture_id)

    def capture_material(row, kind, source, original):
        operation = uuid.UUID(identifier(row['id'])).hex
        metadata = store / (operation + '.json'); plain_file(metadata, 32768)
        saved = json.loads(metadata.read_bytes())
        assert saved['operationId'] == operation and saved.get('captureKind', 'Trash') == kind
        assert row.get('kind', 'Trash') == kind and saved['state'] == 'Verified'
        assert row['source'] == source and saved['source'] == {**source['source'], 'expectedVersion': source['expectedVersion']}
        assert saved['totalBytes'] == original[6]
        manifest = store / (operation + '.manifest.json'); plain_file(manifest, 16 * 1024**2)
        assert proof(manifest)[-1] == saved['manifestFileSha256']
        raw = json.loads(manifest.read_bytes())
        assert raw['rootId'] == ROOT and raw['path'] == source['source']['path'] and raw['rootVersion'] == source['expectedVersion']
        assert raw['manifestSha256'] == row['manifestSha256'] and {item['path'] for item in raw['entries']} == {'', '空目录', '中文.bin'}
        member = next(item for item in raw['entries'] if item['path'] == '中文.bin')
        assert tuple(member['sourceIdentity']) == original[:8] and member['sha256'] == original[-1]
        archive = store / (operation + '.tar.gz'); plain_file(archive)
        archive_proof = proof(archive)
        assert archive_proof[:2] == tuple(saved['archiveIdentity']) and archive_proof[6] == row['archiveBytes'] == saved['archiveBytes']
        assert archive_proof[-1] == row['archiveSha256'] == saved['archiveSha256']
        with tarfile.open(archive, 'r:gz') as reader:
            members = reader.getmembers(); assert len(members) == 3 and {item.name.rstrip('/') for item in members} == {'.', '空目录', '中文.bin'}
            member = next(item for item in members if item.name == '中文.bin'); assert member.isfile() and member.size == original[6]
            with reader.extractfile(member) as stream: assert hashlib.file_digest(stream, 'sha256').hexdigest().upper() == original[-1]
        private = folder / '.whaledeck-files' / (operation + ('.tree-trash' if kind == 'Trash' else '.tree-overwrite-restore'))
        retained = tree(private); assert retained['中文.bin'][:8] == original[:8] and retained['中文.bin'][-1] == original[-1]
        return {'row': row, 'archive': archive, 'archiveProof': archive_proof, 'manifest': manifest, 'manifestProof': proof(manifest), 'metadata': metadata, 'private': private, 'retained': retained, 'differentSourceDevice': saved['differentSourceDevice']}

    def assert_material(material):
        row = catalogue(material['row']['source']['source'], material['row']['id'])
        assert not row['cleanupActive'] and row['archiveState'] in ('Retained', 'Purged') and row['originalsState'] in ('Retained', 'Purged')
        if row['archiveState'] == 'Purged': assert not os.path.lexists(material['archive'])
        else: assert proof(material['archive']) == material['archiveProof']
        if row['originalsState'] == 'Purged': assert not os.path.lexists(material['private'])
        else: assert tree(material['private']) == material['retained']
        assert proof(material['manifest']) == material['manifestProof']
        plain_file(material['metadata'], 32768)
        saved = json.loads(material['metadata'].read_bytes())
        assert metadata_components(saved) == (row['archiveState'] == 'Purged', row['originalsState'] == 'Purged')
        for path, expected in live.items(): assert tree(path) == expected
        return row

    def purge(material, scope):
        command = {'captureId': material['row']['id'], 'scope': scope}
        key = str(uuid.uuid4()); response = post('files/' + KIND, command, key=key)
        row = public_response(response.json()); purges.append(identifier(row['id'])); jobs.append(row['id'])
        assert response.headers.get('Location') == '/api/v1/files/' + KIND + '/' + row['id']
        assert get('files/' + KIND + '/by-key/' + key).json()['id'] == row['id']
        assert post('files/' + KIND, command, key=key).json()['id'] == row['id']
        ready = public_response(wait(KIND, row['id'], ('Ready',)))
        assert ready['command'] == {'captureId': command['captureId'], 'scope': scope, 'source': material['row']['source']['source'], 'captureKind': material['row'].get('kind', 'Trash')}
        assert ready['totalBytes'] == material['row']['totalBytes'] and ready['archiveBytes'] == material['row']['archiveBytes']
        other, _ = authenticate(base, required_environment('VALIDATION_USERNAME'), required_environment('VALIDATION_PASSWORD'))
        try: assert other.get(base + '/api/v1/files/' + KIND + '/' + row['id'], timeout=20).status_code == 404
        finally: other.cookies.clear(); other.close()
        body = {'planHash': ready['confirmation']['planHash'], 'version': ready['version'], 'confirmed': True}
        started = public_response(post('files/' + KIND + '/' + row['id'] + '/confirm', body, key=row['id']).json())
        execution = identifier(started['executionJobId']); jobs.append(execution); executions.append(execution)
        assert post('files/' + KIND + '/' + row['id'] + '/confirm', body, key=row['id']).json()['executionJobId'] == execution
        result = public_response(wait(KIND, row['id'], (*TERMINAL, 'Unknown'))); assert result['state'] == 'Succeeded'
        receipt = STATE / 'tree-purging' / (uuid.UUID(execution).hex + '.json'); plain_file(receipt, 32768)
        saved = json.loads(receipt.read_bytes())
        completed_root_receipt(saved, execution, command['captureId'], scope)
        assert_material(material)
        return result

    try:
        assert get('system/status').json()['build']['sourceRevision'] == application
        configuration = json.loads(Path('/etc/whaledeck/file-backups.json').read_bytes())
        store = Path(configuration['mountPoint']) / configuration['targetRelative'] / 'directory-archives'
        assert store.resolve() == store and store.is_dir() and store.lstat().st_uid == 0
        PHASE = 'create-six-owned-captures-through-real-recycle-and-overwrite'
        for index in range(3):
            originals = {}
            for name in ('source-' + str(index), 'target-' + str(index)):
                path = folder / name; path.mkdir(mode=0o750); (path / '空目录').mkdir(mode=0o710)
                (path / '中文.bin').write_bytes(os.urandom(2 * 1024**2 + 7)); (path / '中文.bin').chmod(0o640)
                originals[name] = proof(path / '中文.bin')
            source = {'rootId': ROOT, 'path': marker + '/source-' + str(index)}
            target = {'rootId': ROOT, 'path': marker + '/target-' + str(index)}
            source_command = {'source': source, 'expectedVersion': current_version('source-' + str(index))}
            recycled = execute('tree-recyclings', source_command)
            source_row = catalogue(source, recycled['captureId'])
            captures.append(capture_material(source_row, 'Trash', source_command, originals['source-' + str(index)]))
            target_version = current_version('target-' + str(index))
            overwritten = execute('tree-overwrite-restorings', {'captureId': source_row['id'], 'target': target, 'targetExpectedVersion': target_version})
            target_row = catalogue(target, overwritten['overwrittenCaptureId'])
            captures.append(capture_material(target_row, 'Overwrite', {'source': target, 'expectedVersion': target_version}, originals['target-' + str(index)]))
            # Current paths are live, unrelated objects, even at the old source
            # pathname. A historical purge must not delete these replacements.
            replacement = folder / ('source-' + str(index)); replacement.mkdir(mode=0o750)
            (replacement / 'current.bin').write_bytes(os.urandom(257))
            live[replacement] = tree(replacement); live[folder / ('target-' + str(index))] = tree(folder / ('target-' + str(index)))
        assert len({item['row']['id'] for item in captures}) == 6
        preserve_evidence(evidence, 'baseline.json', {'fixture': marker, 'identity': identity, 'application': application, 'agent': agent, 'validation': validation, 'captures': [item['row'] for item in captures]})
        PHASE = 'csrf-origin-schema-and-canceled-plan'
        command = {'captureId': captures[0]['row']['id'], 'scope': 'All'}
        with requests.Session() as anonymous:
            anonymous.trust_env = False
            assert anonymous.get(base + '/api/v1/files/' + KIND + '/' + str(uuid.uuid4()), timeout=20).status_code == 401
        assert session.post(base + '/api/v1/files/' + KIND, json=command, timeout=20).status_code in (400, 403)
        assert session.post(base + '/api/v1/files/' + KIND, json=command, headers={**api.headers, 'Origin': 'http://foreign.invalid', 'Idempotency-Key': str(uuid.uuid4())}, timeout=20).status_code == 403
        for invalid in ({**command, 'captureOwner': '0' * 64}, {'captureId': command['captureId']}, {**command, 'scope': 'all'}):
            post('files/' + KIND, invalid, 400, str(uuid.uuid4()))
        canceled = post('files/' + KIND, command, key=str(uuid.uuid4())).json(); jobs.append(canceled['id']); purges.append(canceled['id'])
        ready = public_response(wait(KIND, canceled['id'], ('Ready',)))
        post('files/' + KIND + '/' + ready['id'] + '/cancel', key=ready['id'])
        assert wait(KIND, ready['id'], TERMINAL)['state'] == 'Canceled'
        post('files/' + KIND + '/' + ready['id'] + '/confirm', {'planHash': ready['confirmation']['planHash'], 'version': ready['version'], 'confirmed': True}, 409, ready['id'])
        assert_material(captures[0])
        PHASE = 'both-capture-kinds-all-three-scopes-independent-full-disk-proof'
        results = []
        for index, scope in enumerate(SCOPES):
            for material in captures[index * 2:index * 2 + 2]: results.append(purge(material, scope))
        # Leave no test backup payload behind: the remaining component is
        # removed by a NEW explicitly scoped public operation, never unlink().
        PHASE = 'explicit-complement-plans-and-permanent-catalogue-facts'
        for material in captures:
            row = assert_material(material)
            if row['archiveState'] != 'Purged': results.append(purge(material, 'Archive'))
            elif row['originalsState'] != 'Purged': results.append(purge(material, 'Originals'))
            row = assert_material(material); assert row['archiveState'] == row['originalsState'] == 'Purged'
            post('files/' + KIND, {'captureId': row['id'], 'scope': 'All'}, 409, str(uuid.uuid4()))
        PHASE = 'durable-sql-jobs-audit-and-zero-locks'
        subject = get('auth/me').json()['subject']; audits = get('audit', params={'actorSubject': subject, 'take': 500}).json()
        assert all(any(item.get('jobId') == execution and item['action'] == action and item['actorSubject'] == subject for item in audits) for execution in executions for action in ('files.tree-purge.confirmed', 'files.tree-purge.result'))
        all_jobs = sql_identifiers(jobs); all_purges = sql_identifiers(purges); all_captures = sql_identifiers([item['row']['id'] for item in captures])
        sql = "SELECT json_build_object('purges',(SELECT count(*) FROM file_tree_purging_operations WHERE id IN (" + all_purges + ")),'active',(SELECT count(*) FROM file_tree_purging_operations WHERE id IN (" + all_purges + ") AND completed_at_utc IS NULL),'locks',(SELECT count(*) FROM file_path_locks WHERE operation_id IN (" + all_jobs + ")),'jobs',(SELECT count(*) FROM operation_jobs WHERE id IN (" + all_jobs + ")),'outboxPending',(SELECT count(*) FROM outbox_messages WHERE payload_json->>'jobId' IN (" + all_jobs + ") AND processed_at_utc IS NULL),'captures',(SELECT count(*) FROM file_tree_captures WHERE id IN (" + all_captures + ")))::text;"
        saved_sql = json.loads(subprocess.check_output(['docker', 'exec', '-u', 'postgres', 'database-platform-postgres', 'psql', '-X', '-v', 'ON_ERROR_STOP=1', '-d', 'whaledeck', '-At', '-c', sql], text=True))
        assert saved_sql == {'purges': len(purges), 'active': 0, 'locks': 0, 'jobs': len(jobs), 'outboxPending': 0, 'captures': 6}
        preserve_evidence(evidence, 'rpc-result.json', {'results': results, 'jobs': jobs, 'sql': saved_sql})
        PHASE = 'exact-owned-live-payload-cleanup-only'
        expected = {folder / '.whaledeck-files': proof(folder / '.whaledeck-files')}
        assert not any((folder / '.whaledeck-files').iterdir())
        for path, entries in live.items():
            for relative, value in entries.items(): expected[path / relative] = value
        cleanup_live_fixture(folder, identity, expected)
        print(json.dumps({'realDirectoryPurgingBff': 'passed', 'applicationRevision': application, 'agentRevision': agent, 'validationRevision': validation,
            'captureKinds': ['Trash', 'Overwrite'], 'scopes': list(SCOPES), 'mainExecutions': 6, 'complementExecutions': 4,
            'completeBytesPerHistoricalTree': 2 * 1024**2 + 7, 'currentReplacementPathsPreserved': True,
            'sqlAuditJobsAndZeroLocks': True, 'catalogueManifestRootJournalAuditRetained': True, 'ownedLivePayloadRemoved': True,
            'differentSourceDevice': sorted({item['differentSourceDevice'] for item in captures}),
            'browserOrdinaryRevocationAndInterruptedDeletion': 'not-covered'}), flush=True)
    finally:
        if folder.exists(): print(json.dumps({'ownedPurgeBffFixturePreserved': marker, 'phase': PHASE}), flush=True)
        session.cookies.clear(); session.close()


if __name__ == '__main__':
    try: main()
    except BaseException as error:
        lines = [frame.lineno for frame in traceback.extract_tb(error.__traceback__) if Path(frame.filename).name == Path(__file__).name]
        print(json.dumps({'realDirectoryPurgingBff': 'failed', 'phase': PHASE, 'failureType': type(error).__name__, 'sourceLines': lines[-8:], 'privateOutput': 'withheld'}), flush=True)
        raise SystemExit(1) from None
