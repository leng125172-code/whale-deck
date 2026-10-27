#!/usr/bin/env python3
"""Installed signed attribute Stage/Publish on one exact owned fixture.

Initial capture is a real BFF upload; attribute publication is direct signed
UDS, not a claim of restored business jobs or UI. Retain Root/HDD evidence.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import sys
import uuid

import validate_container_file_publishing as owned
from validate_container_file_staging import load
from validate_container_upload_bff import login, logout, request, root_receipt, wait_sql, terminal
from validate_container_upload_recovery_agent import trusted
from validate_databases import Api
from validate_oidc import required_environment

PHASE = 'startup'


def main():
    global PHASE
    assert os.geteuid() == 0 and len(sys.argv) == 3; os.umask(0o077)
    revision = sys.argv[1]; assert re.fullmatch('[a-f0-9]{40}', revision)
    probe = Path(sys.argv[2]); repository = Path(__file__).resolve().parents[2]
    assert probe.name == 'TerminalProbe' and probe.parent.parent == repository / 'deploy/artifacts'
    assert re.fullmatch(r'terminal-probe\.[A-Za-z0-9]{6}', probe.parent.name)
    trusted(probe); trusted(probe.parent, True)
    subprocess.run(['sha256sum', '--quiet', '-c', 'SHA256SUMS'], cwd=probe.parent, capture_output=True, check=True, timeout=30)
    for component, binary in (('agent', 'WhaleDeck.Agent'), ('maintenance', 'WhaleDeck.MaintenanceHost')):
        build = json.loads(owned.common.command(['/opt/whaledeck-' + component + '/' + binary, '--build-info']))
        assert build['sourceRevision'] == revision
    names = ('whaledeck-container-file-write.py', 'whaledeck-container-upload.py', 'whaledeck-container-backups.py',
        'whaledeck-container-publish.py', 'whaledeck-container-history.py', 'whaledeck-container-restore.py')
    for name in names:
        expected = owned.common.command(['git', 'show', revision + ':deploy/host/' + name])
        assert Path('/usr/local/libexec', name).read_bytes() == expected
    evidence = repository / 'deploy/artifacts' / ('container-restoration-agent-' + uuid.uuid4().hex); evidence.mkdir(mode=0o700)
    def save(name, value):
        assert name in ('baseline.json', 'probe.stdout', 'probe.stderr', 'verified.json', 'journals.json')
        path = evidence / name; path.write_text(value if isinstance(value, str) else json.dumps(value)); path.chmod(0o600)
    base = os.environ.get('WHALEDECK_URL', 'http://192.168.100.13:8080').rstrip('/')
    session, _ = login(base, required_environment('VALIDATION_USERNAME'), required_environment('VALIDATION_PASSWORD')); api = Api(base, session)
    backups = load(Path('/usr/local/libexec/whaledeck-file-backups.py'), 'restoration_agent_hdd')
    files = load(Path('/usr/local/libexec/whaledeck-files.py'), 'restoration_agent_files')
    policy = backups.FileBackups(files).configuration(); history = Path(policy['mountPoint']) / policy['targetRelative'] / 'container-files-v1'
    with owned.fixture() as fixture:
        endpoint = base + f'/api/v1/containers/{fixture["identifier"]}/files/'
        output = fixture['folder'] / 'original.bin'; desired = (1001, 2002, 0o640, 1700000000123456789)
        os.chown(output, desired[0], desired[1]); output.chmod(desired[2]); os.utime(output, ns=(desired[3], desired[3]))
        old_sha = hashlib.sha256(fixture['old']).hexdigest().upper()
        fixture['proofs']['original.bin'] = owned.common.fingerprint(output), old_sha
        target = request(session, 'GET', endpoint + 'target', 200).json(); original_instance = target['instanceId']
        original = request(session, 'GET', endpoint + 'entry', 200, params={'instanceId': original_instance, 'path': 'work/original.bin'}).json()
        replacement = hashlib.shake_256(b'owned-installed-attribute-current-target').digest(owned.common.SIZE)
        new_sha = hashlib.sha256(replacement).hexdigest().upper(); key = str(uuid.uuid4())
        PHASE = 'actual-bff-legacy-upload-and-historical-attributes'
        plan = request(session, 'POST', endpoint + 'uploads/plan', 200, headers=api.headers, json={'instanceId': original_instance,
            'path': 'work/original.bin', 'sizeBytes': len(replacement), 'sha256': new_sha, 'expectedVersion': original['version']}).json()
        initial = request(session, 'POST', endpoint + 'uploads', 202, headers={**api.headers, 'Idempotency-Key': key},
            json={'planHash': plan['planHash'], 'confirmed': True, 'idempotencyKey': key}).json()
        initial_id = initial['id']; capture = uuid.UUID(initial_id).hex
        save('baseline.json', {'revision': revision, 'containerId': fixture['identifier'], 'source': str(fixture['folder']), 'initialJob': initial_id,
            'sourceCapture': capture, 'originalInstanceId': original_instance})
        old_inode = output.stat().st_ino
        request(session, 'PUT', endpoint + f'uploads/{initial_id}/content', 202, headers={**api.headers, 'Idempotency-Key': initial_id,
            'Content-Type': 'application/octet-stream'}, data=replacement)
        value = wait_sql(initial_id, 'Succeeded'); terminal(value, 'Succeeded', 'CONTAINER_UPLOAD_PUBLISHED', 'Succeeded'); initial_root = root_receipt(value['row'])
        assert initial_root['schemaVersion'] == 1 and output.read_bytes() == replacement
        fixture['private'] = '.whaledeck-container-files-v1'; retained = fixture['folder'] / fixture['private'] / (capture + '.upload')
        assert retained.stat().st_ino == old_inode and retained.read_bytes() == fixture['old']
        fixture['privateProofs'][retained.name] = owned.common.fingerprint(retained), old_sha
        source_metadata = (history / (capture + '.json')).read_bytes(); source_data = owned.common.fingerprint(history / (capture + '.data'))
        assert (history / (capture + '.data')).read_bytes() == fixture['old']
        listed = request(session, 'GET', endpoint + 'history', 200, params={'instanceId': original_instance, 'path': 'work/original.bin'}).json()
        source = next(item for item in listed['entries'] if item['captureId'] == capture)
        os.chown(output, 1000, 1000); output.chmod(0o600)
        current_identity = files.publication_identity(output.stat()); fixture['proofs']['original.bin'] = owned.common.fingerprint(output), new_sha
        PHASE = 'owned-restart-before-signed-original-and-other-path-restoration'
        owned.common.command(['docker', 'stop', '--time', '1', fixture['identifier']]); owned.common.command(['docker', 'start', fixture['identifier']])
        current = request(session, 'GET', endpoint + 'target', 200).json(); assert current['instanceId'] != original_instance
        restore_id, new_id, bad_id = (uuid.uuid4().hex for _ in range(3))
        parameters = {'revision': revision, 'containerId': fixture['identifier'], 'instanceId': current['instanceId'], 'captureId': capture,
            'captureHash': source['captureHash'], 'sha256': old_sha, 'sizeBytes': len(fixture['old']),
            'restoreOperationId': restore_id, 'newOperationId': new_id, 'badOperationId': bad_id}
        result = subprocess.run([str(probe), 'container-restoration'], input=json.dumps(parameters), text=True, capture_output=True, timeout=210)
        assert len(result.stdout.encode()) <= 32768 and len(result.stderr.encode()) <= 32768
        save('probe.stdout', result.stdout); save('probe.stderr', result.stderr)
        if result.returncode:
            for line in result.stderr.splitlines():
                try:
                    detail = json.loads(line)
                    if detail.get('terminalProbe') == 'failed': print(json.dumps({key: detail.get(key) for key in ('phase', 'rpcStatus', 'fileCode')}), flush=True)
                except ValueError: pass
            raise AssertionError('INSTALLED_RESTORATION_RPC_NOT_VERIFIED')
        observed = json.loads(result.stdout); assert observed['installedContainerRestoration'] == 'passed' and len(observed['results']) == 2
        PHASE = 'independent-root-journal-target-inode-full-bytes-and-hdd'
        records = {}
        for identifier, path in ((restore_id, 'work/original.bin'), (new_id, 'work/recovered.bin')):
            saved_path = Path('/var/lib/whaledeck-container-uploads', identifier + '.json'); trusted(saved_path)
            record = json.loads(saved_path.read_bytes()); assert record['schemaVersion'] == 4 and record['state'] == 'Published'
            assert record['request']['path'] == path and record['request']['restoreSource'] == {'captureId': capture, 'captureHash': source['captureHash'], 'path': 'work/original.bin'}
            assert record['restorationAttributes'] == {'uid': desired[0], 'gid': desired[1], 'mode': desired[2], 'modifiedNs': desired[3]}
            staged = Path('/var/lib/whaledeck-container-uploads', identifier + '.data'); trusted(staged); assert staged.read_bytes() == fixture['old']
            leaf = fixture['folder'] / path.split('/')[-1]; assert leaf.read_bytes() == fixture['old']
            info = leaf.stat(); assert (info.st_uid, info.st_gid, stat.S_IMODE(info.st_mode), info.st_mtime_ns) == desired
            assert files.publication_identity(info) == record['publication']['pendingProof']
            fixture['proofs'][leaf.name] = owned.common.fingerprint(leaf), old_sha; records[identifier] = record
        displaced = fixture['folder'] / fixture['private'] / (restore_id + '.upload')
        assert files.publication_identity(displaced.stat()) == current_identity and displaced.read_bytes() == replacement
        fixture['privateProofs'][displaced.name] = owned.common.fingerprint(displaced), new_sha
        assert (history / (restore_id + '.data')).read_bytes() == replacement
        assert (history / (capture + '.json')).read_bytes() == source_metadata and owned.common.fingerprint(history / (capture + '.data')) == source_data
        assert not (fixture['folder'] / 'rejected.bin').exists()
        for suffix in ('.json', '.partial', '.data'): assert not Path('/var/lib/whaledeck-container-uploads', bad_id + suffix).exists()
        save('journals.json', records)
        save('verified.json', {'revision': revision, 'actualSignedInstalledRpc': True, 'actualOwnedRestart': True,
            'fullBytesEach': owned.common.SIZE, 'originalAndOtherPathHistoricalAttributes': True, 'signatureSourceTamperAndReplayRefused': True,
            'validSignatureWrongHashRefused': True, 'rootHddInodeAndSourceUnchanged': True, 'newHumanRestorationJobOrUi': False})
    logout(base, session); session.close()
    print(json.dumps({'installedContainerRestoration': 'passed', 'revision': revision, 'fullBytesEach': owned.common.SIZE,
        'signedInstalledAttributePublications': 2, 'exactOwnedContainerAndSourceCleaned': True, 'permanentRootHddAndInitialJobRetained': True,
        'newHumanRestorationJobOrUi': False, 'evidence': str(evidence)}), flush=True)


if __name__ == '__main__':
    try: main()
    except BaseException as error:
        import traceback
        print(json.dumps({'installedContainerRestoration': 'failed', 'phase': PHASE, 'failureType': type(error).__name__,
            'locations': [{'file': Path(frame.filename).name, 'line': frame.lineno} for frame in traceback.extract_tb(error.__traceback__)]}), file=sys.stderr)
        raise SystemExit(1) from None
