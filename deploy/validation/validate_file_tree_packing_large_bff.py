#!/usr/bin/env python3
"""Real >1 GiB TAR/HTTP proof with bounded buffers and exact owned fixture cleanup.

Runs on the workstation under the existing temporary identity supervisor.
Never extracts an archive, changes root policy, deletes receipts or uses user data.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import stat
import subprocess
import sys
import tarfile
import time
import traceback
import uuid

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'validation'))
from validate_databases import Api
from validate_oidc import authenticate, required_environment

PHASE = 'startup'
DATA = Path('/data/WhaleDeck/files')
ROOT = 'whaledeck.files.shared'
SIZE = 1024 ** 3 + 7


def main():
    global PHASE
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--expected-revision', required=True)
    args = parser.parse_args()
    assert re.fullmatch('[a-f0-9]{40}', args.expected_revision)
    assert os.geteuid() == 0 and DATA.resolve() == DATA and DATA.is_dir()
    assert shutil.disk_usage(DATA).free >= 6 * 1024 ** 3, 'insufficient-private-test-headroom'
    base = os.environ.get('WHALEDECK_URL', 'http://192.168.100.13:8080').rstrip('/')
    session, _ = authenticate(base, required_environment('VALIDATION_USERNAME'), required_environment('VALIDATION_PASSWORD'))
    api = Api(base, session)
    status = session.get(base + '/api/v1/system/status', timeout=20)
    assert status.status_code == 200 and status.json()['build']['sourceRevision'] == args.expected_revision
    marker = 'wdv-packing-large-bff-' + uuid.uuid4().hex[:16]
    folder = DATA / marker
    assert not os.path.lexists(folder)
    folder.mkdir(mode=0o700); identity = folder.stat()
    print(json.dumps({'ownedLargePackingFixture': marker, 'sourceBytes': SIZE}), flush=True)
    completed = False
    try:
        PHASE = 'write-full-source-with-bounded-buffer'
        source = folder / 'source'; source.mkdir()
        payload = source / 'payload.bin'; expected = hashlib.sha256(); block = os.urandom(1024 * 1024)
        with payload.open('xb') as stream:
            remaining = SIZE
            while remaining:
                piece = block[:min(len(block), remaining)]
                assert stream.write(piece) == len(piece)
                expected.update(piece); remaining -= len(piece)
            stream.flush(); os.fsync(stream.fileno())
        block = None; payload.chmod(0o640)
        def proof(path):
            value = path.stat()
            return value.st_dev, value.st_ino, value.st_uid, value.st_gid, value.st_mode, value.st_size, value.st_mtime_ns
        original = proof(payload)
        def get(path, **kwargs):
            value = session.get(base + '/api/v1/' + path, timeout=30, **kwargs)
            assert value.status_code == 200, 'get-status-mismatch'
            return value.json()
        def post(path, body, key):
            value = session.post(base + '/api/v1/' + path, json=body, headers={**api.headers, 'Idempotency-Key': key}, timeout=40)
            assert value.status_code == 202, 'post-status-mismatch'
            return value.json()
        def wait(identifier, wanted):
            deadline = time.monotonic() + 600
            while time.monotonic() < deadline:
                value = get('files/tree-packings/' + identifier)
                if value['state'] == wanted: return value
                assert value['state'] not in ('Unknown', 'Failed', 'Canceled', 'Expired', 'Acknowledged'), 'unexpected-packing-outcome'
                time.sleep(0.5)
            raise RuntimeError('packing-job-deadline')
        PHASE = 'real-directory-plan-job-confirmation-and-worker'
        entry = get('files/entry', params={'rootId': ROOT, 'path': marker + '/source'})
        command = {'source': {'rootId': ROOT, 'path': marker + '/source'}, 'expectedVersion': entry['version'],
            'target': {'rootId': ROOT, 'path': marker + '/large.tar'}, 'format': 'tar'}
        queued = post('files/tree-packings', command, str(uuid.uuid4()))
        ready = wait(queued['id'], 'Ready')
        assert ready['totalBytes'] == SIZE and ready['entryCount'] == 2 and ready['executionJobId'] is None
        executing = post('files/tree-packings/' + ready['id'] + '/confirm',
            {'planHash': ready['confirmation']['planHash'], 'confirmed': True, 'version': ready['version']}, ready['id'])
        assert executing['executionJobId'] and executing['executionJobId'] != ready['planningJobId']
        artifact = wait(ready['id'], 'Succeeded')
        assert artifact['archiveBytes'] > 1024 ** 3 and artifact['totalBytes'] == SIZE and not artifact['partialRetained']
        PHASE = 'independent-whole-source-tar-and-published-file-digests'
        archive_path = folder / 'large.tar'
        assert proof(payload) == original
        with payload.open('rb') as stream: assert hashlib.file_digest(stream, 'sha256').digest() == expected.digest()
        with tarfile.open(archive_path) as archive:
            assert set(archive.getnames()) == {'.', 'payload.bin'}
            member = archive.getmember('payload.bin'); assert member.isfile() and member.size == SIZE
            with archive.extractfile(member) as stream: assert hashlib.file_digest(stream, 'sha256').digest() == expected.digest()
        with archive_path.open('rb') as stream: disk_digest = hashlib.file_digest(stream, 'sha256').hexdigest().upper()
        assert archive_path.stat().st_size == artifact['archiveBytes'] and disk_digest == artifact['archiveSha256']
        PHASE = 'complete-large-http-stream-not-a-prefix'
        response = session.get(base + '/api/v1/files/tree-packings/' + artifact['id'] + '/download',
            params={'expectedVersion': artifact['publishedVersion']}, stream=True, timeout=(15, 120))
        received = hashlib.sha256(); count = 0
        with response:
            assert response.status_code == 200 and int(response.headers['Content-Length']) == artifact['archiveBytes']
            for chunk in response.iter_content(32768): received.update(chunk); count += len(chunk)
        assert count == artifact['archiveBytes'] and received.hexdigest().upper() == disk_digest
        PHASE = 'legacy-general-file-limit-still-denies-large-archive'
        denied = session.get(base + '/api/v1/files/download', params={'rootId': ROOT, 'path': marker + '/large.tar',
            'expectedVersion': artifact['publishedVersion']}, stream=True, timeout=30)
        with denied: assert denied.status_code in (400, 403, 409, 413), 'legacy-limit-was-not-proven'
        PHASE = 'real-audit-and-terminal-job-lock-observer'
        actor = get('auth/me')['subject']; audits = get('audit', params={'actorSubject': actor, 'take': 100})
        assert any(item['action'] == 'files.tree-pack.download' and item['result'] == 'Validated' and item.get('jobId') == artifact['executionJobId'] for item in audits)
        assert re.fullmatch('[a-f0-9-]{36}', artifact['id']) and re.fullmatch('[a-f0-9-]{36}', artifact['executionJobId'])
        sql = "SELECT json_build_object('active',(SELECT count(*) FROM file_tree_packing_operations WHERE id='" + artifact['id'] + "' AND completed_at_utc IS NULL),'locks',(SELECT count(*) FROM file_path_locks WHERE operation_id='" + artifact['executionJobId'] + "'))::text;"
        checked = subprocess.run(['docker', 'exec', '-u', 'postgres', 'database-platform-postgres', 'psql', '-X', '-v', 'ON_ERROR_STOP=1', '-d', 'whaledeck', '-At', '-c', sql], check=True, capture_output=True, text=True, timeout=20)
        assert json.loads(checked.stdout) == {'active': 0, 'locks': 0}
        PHASE = 'exact-owned-fixture-cleanup'
        assert folder.resolve() == folder and (folder.lstat().st_dev, folder.lstat().st_ino, folder.lstat().st_uid) == (identity.st_dev, identity.st_ino, 0)
        for line in Path('/proc/self/mountinfo').read_text().splitlines():
            mount = line.split()[4]; assert mount != str(folder) and not mount.startswith(str(folder) + '/')
        dirs = set(); files = set()
        for parent, names, entries in os.walk(folder, followlinks=False):
            for name in names + entries:
                path = Path(parent) / name; value = path.lstat()
                assert value.st_dev == identity.st_dev and value.st_uid in (0, 1000) and not stat.S_ISLNK(value.st_mode)
                relative = str(path.relative_to(folder))
                if stat.S_ISDIR(value.st_mode): dirs.add(relative)
                else: assert stat.S_ISREG(value.st_mode) and value.st_nlink == 1; files.add(relative)
        assert dirs == {'source', '.whaledeck-files'} and files == {'source/payload.bin', 'large.tar'}
        assert proof(payload) == original
        shutil.rmtree(folder); completed = True
        print(json.dumps({'realLargePackingBff': 'passed', 'sourceRevision': args.expected_revision, 'sourceBytes': SIZE,
            'archiveBytes': count, 'fullHttpAndDiskAndTarDigest': True, 'legacyGeneralDownloadStillLimited': True,
            'boundedValidationBuffers': True, 'exactOwnedFixtureRemoved': True, 'immutableRecordsRetained': True,
            'intentId': artifact['id'], 'executionJobId': artifact['executionJobId']}))
    finally:
        session.cookies.clear(); session.close()
        if not completed and folder.exists(): print(json.dumps({'ownedLargePackingFixturePreserved': marker, 'phase': PHASE}))


if __name__ == '__main__':
    try: main()
    except Exception as error:
        frames = traceback.extract_tb(error.__traceback__)
        print(json.dumps({'realLargePackingBff': 'failed', 'phase': PHASE, 'failureType': type(error).__name__, 'sourceLine': frames[-1].lineno if frames else None}))
        raise SystemExit(1) from None
