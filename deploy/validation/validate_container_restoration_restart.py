#!/usr/bin/env python3
"""Real installed restoration with queued and positive-partial process restarts.

Only the fixed Worker container and Agent service may restart, after a zero
active-upload preflight. No SQL/cache/time/journal edits, injected receipts,
deployment rollback, production file changes or broad cleanup. Historical
content, permanent jobs/audit/Root receipts remain. This is not browser evidence.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import stat
import subprocess
import sys
import time
import uuid

import validate_container_file_publishing as owned
from validate_container_file_staging import load
from validate_container_upload_bff import login, logout, request, root_receipt, snapshot, terminal, wait_sql, query
from validate_container_upload_recovery_agent import trusted
from validate_databases import Api
from validate_oidc import required_environment

PHASE = 'startup'
STATE = Path('/var/lib/whaledeck-container-uploads')
SIZE = 128 * 1024**2 + 7
DESIRED = (1001, 2002, 0o640, 1700000000123456789)
CURRENT = (1000, 1000, 0o600, 1750000000987654321)


def command(args, timeout=40):
    return subprocess.run(args, check=True, capture_output=True, timeout=timeout).stdout


def worker():
    value = json.loads(command(['docker', 'inspect', 'whaledeck-worker']))[0]
    assert value['Name'] == '/whaledeck-worker' and value['HostConfig']['RestartPolicy']['Name'] == 'unless-stopped'
    return {'id': value['Id'], 'image': value['Image'], 'running': value['State']['Running'],
        'pid': value['State']['Pid'], 'started': value['State']['StartedAt'], 'restarts': value['RestartCount'],
        'healthy': value['State'].get('Health', {}).get('Status') == 'healthy'}


def agent_pid():
    value = int(command(['systemctl', 'show', 'whaledeck-agent.service', '-p', 'MainPID', '--value']).strip())
    assert value > 1
    return value


def attributes(path):
    info = path.stat()
    return info.st_uid, info.st_gid, stat.S_IMODE(info.st_mode), info.st_mtime_ns


def set_attributes(path, values):
    os.chown(path, *values[:2]); path.chmod(values[2]); os.utime(path, ns=(values[3], values[3]))
    assert attributes(path) == values


def wait_worker(original, *, changed=False):
    deadline = time.monotonic() + 80
    while True:
        current = worker()
        assert (current['id'], current['image']) == (original['id'], original['image'])
        different = current['pid'] != original['pid'] and current['started'] != original['started']
        if current['running'] and current['healthy'] and (not changed or different): return current
        assert time.monotonic() < deadline
        time.sleep(0.3)


def main():
    global PHASE
    assert os.geteuid() == 0 and len(sys.argv) in (2,3); os.umask(0o077)
    remaining = len(sys.argv) == 3
    if remaining: assert sys.argv[2] == '--remaining'
    revision = sys.argv[1]; assert re.fullmatch('[a-f0-9]{40}', revision)
    base = os.environ.get('WHALEDECK_URL', 'http://192.168.100.13:8080').rstrip('/')
    repository = Path(__file__).resolve().parents[2]
    validator_revision = command(['git', '-C', str(repository), 'rev-parse', 'HEAD']).decode().strip()
    assert re.fullmatch('[a-f0-9]{40}', validator_revision)
    for name in ('whaledeck-container-file-write.py', 'whaledeck-container-upload.py', 'whaledeck-container-history.py',
            'whaledeck-container-restore.py', 'whaledeck-container-publish.py', 'whaledeck-container-backups.py'):
        assert Path('/usr/local/libexec', name).read_bytes() == command(['git', '-C', str(repository), 'show', revision + ':deploy/host/' + name])
    evidence = repository / 'deploy/artifacts' / ('container-restoration-restart-' + uuid.uuid4().hex)
    evidence.mkdir(mode=0o700)
    def save(name, value):
        assert re.fullmatch(r'(?:baseline|queued|worker-partial|agent-partial|verified)\.json', name)
        output = evidence / name; output.write_text(json.dumps(value)); output.chmod(0o600)
    status = query("SELECT json_build_object('active',count(*)) FROM container_file_upload_operations WHERE completed_at_utc IS NULL;")
    assert status['active'] == 0
    original_worker = worker(); assert original_worker['running'] and original_worker['healthy']
    original_agent = agent_pid()
    maintenance = command(['systemctl', 'show', 'whaledeck-maintenance.service', '-p', 'MainPID', '--value']).strip()
    assert int(maintenance) > 1
    session, _ = login(base, required_environment('VALIDATION_USERNAME'), required_environment('VALIDATION_PASSWORD'))
    api = Api(base, session)
    assert request(session, 'GET', base + '/api/v1/system/status', 200).json()['build']['sourceRevision'] == revision
    backup = load(Path('/usr/local/libexec/whaledeck-file-backups.py'), 'restart_hdd')
    files = load(Path('/usr/local/libexec/whaledeck-files.py'), 'restart_files')
    config = backup.FileBackups(files).configuration()
    history = Path(config['mountPoint']) / config['targetRelative'] / 'container-files-v1'
    assert shutil.disk_usage(history).free > 8 * 1024**3 and shutil.disk_usage(owned.common.APPS).free > 8 * 1024**3
    save('baseline.json', {'revision': revision, 'validatorRevision': validator_revision, 'worker': original_worker, 'agentPid': original_agent, 'maintenancePid': int(maintenance)})
    outcomes = []
    kinds = ('worker-partial', 'agent-partial') if remaining else ('queued', 'worker-partial', 'agent-partial')
    for kind in kinds:
        PHASE = kind + '-prepare-real-hdd-history'
        with owned.fixture(restartable_core=('whaledeck-worker',) if kind != 'agent-partial' else ()) as fixture:
            output = fixture['folder'] / 'original.bin'
            digest = hashlib.sha256(); block = hashlib.shake_256(('owned-restoration-' + kind).encode()).digest(1024**2)
            with output.open('wb') as stream:
                remaining = SIZE
                while remaining:
                    chunk = block[:min(remaining, len(block))]; stream.write(chunk); digest.update(chunk); remaining -= len(chunk)
                stream.flush(); os.fsync(stream.fileno())
            set_attributes(output, DESIRED); source_sha = digest.hexdigest().upper()
            old_identity = owned.common.fingerprint(output)
            fixture['proofs']['original.bin'] = old_identity, source_sha
            endpoint = base + f'/api/v1/containers/{fixture["identifier"]}/files/'
            target = request(session, 'GET', endpoint + 'target', 200).json()
            entry = request(session, 'GET', endpoint + 'entry', 200, params={'instanceId': target['instanceId'], 'path': 'work/original.bin'}).json()
            replacement = hashlib.shake_256(('owned-current-target-' + kind).encode()).digest(owned.common.SIZE)
            replacement_sha = hashlib.sha256(replacement).hexdigest().upper(); key = str(uuid.uuid4())
            plan = request(session, 'POST', endpoint + 'uploads/plan', 200, headers=api.headers, json={'instanceId': target['instanceId'],
                'path': 'work/original.bin', 'sizeBytes': len(replacement), 'sha256': replacement_sha, 'expectedVersion': entry['version']}).json()
            upload = request(session, 'POST', endpoint + 'uploads', 202, headers={**api.headers, 'Idempotency-Key': key},
                json={'planHash': plan['planHash'], 'confirmed': True, 'idempotencyKey': key}).json()
            request(session, 'PUT', endpoint + f'uploads/{upload["id"]}/content', 202,
                headers={**api.headers, 'Idempotency-Key': upload['id'], 'Content-Type': 'application/octet-stream'}, data=replacement)
            first = wait_sql(upload['id'], 'Succeeded'); terminal(first, 'Succeeded', 'CONTAINER_UPLOAD_PUBLISHED', 'Succeeded')
            capture = uuid.UUID(upload['id']).hex; fixture['private'] = '.whaledeck-container-files-v1'
            retained = fixture['folder'] / fixture['private'] / (capture + '.upload')
            assert retained.stat().st_ino == old_identity[1] and owned.common.file_digest(retained) == source_sha
            fixture['privateProofs'][retained.name] = owned.common.fingerprint(retained), source_sha
            assert owned.common.file_digest(history / (capture + '.data')) == source_sha
            source_metadata = (history / (capture + '.json')).read_bytes()
            source_data_identity = owned.common.fingerprint(history / (capture + '.data'))
            source = next(item for item in request(session, 'GET', endpoint + 'history', 200,
                params={'instanceId': target['instanceId'], 'path': 'work/original.bin'}).json()['entries'] if item['captureId'] == capture)
            set_attributes(output, CURRENT)
            current_identity = owned.common.fingerprint(output)
            fixture['proofs']['original.bin'] = current_identity, replacement_sha
            entry = request(session, 'GET', endpoint + 'entry', 200, params={'instanceId': target['instanceId'], 'path': 'work/original.bin'}).json()
            plan = request(session, 'POST', endpoint + 'restores/plan', 200, headers=api.headers, json={'instanceId': target['instanceId'],
                'path': 'work/original.bin', 'captureId': capture, 'expectedCaptureHash': source['captureHash'],
                'expectedVersion': entry['version'], 'restoreAttributes': True}).json()
            assert plan['restoreAttributes'] is True
            before_worker, before_agent = worker(), agent_pid()
            key = str(uuid.uuid4()); stopped = False; identifier = None; finished = False
            try:
                if kind == 'queued':
                    command(['docker', 'stop', '--time', '20', before_worker['id']]); stopped = True
                    assert not worker()['running']
                intent = request(session, 'POST', endpoint + 'restores', 202, headers={**api.headers, 'Idempotency-Key': key},
                    json={'planHash': plan['planHash'], 'confirmed': True, 'idempotencyKey': key}).json()
                assert intent['restoreAttributes'] is True
                identifier = intent['id']; operation = uuid.UUID(identifier).hex
                frozen = snapshot(identifier, 'restore')['row']
                save(kind + '.json', {'revision': revision, 'kind': kind, 'intent': identifier, 'containerId': fixture['identifier'],
                    'source': str(fixture['folder']), 'capture': capture, 'sourceSha256': source_sha, 'sourceBytes': SIZE,
                    'workerBefore': before_worker, 'agentBefore': before_agent})
                if kind == 'queued':
                    assert frozen['state'] == 'AwaitingUpload' and not frozen['lease_token'] and frozen['receive_token'] is None
                    assert not (STATE / (operation + '.json')).exists() and not (STATE / (operation + '.partial')).exists()
                    PHASE = 'queued-actual-worker-and-agent-restarts'
                    command(['systemctl', 'restart', 'whaledeck-agent.service']); assert agent_pid() != before_agent
                    command(['docker', 'start', before_worker['id']]); stopped = False
                    after_worker = wait_worker(before_worker, changed=True)
                    assert after_worker['started'] != before_worker['started'] and after_worker['pid'] != before_worker['pid']
                    partial_bytes = 0
                else:
                    PHASE = kind + '-positive-root-partial-before-process-restart'
                    partial = STATE / (operation + '.partial'); deadline = time.monotonic() + 100
                    while True:
                        assert time.monotonic() < deadline
                        try:
                            info = trusted(partial)
                            if 0 < info.st_size < SIZE:
                                record = json.loads((STATE / (operation + '.json')).read_bytes())
                                assert record['schemaVersion'] == 4 and record['state'] == 'Receiving' and record['request']['restoreSource']['captureId'] == capture
                                partial_bytes = info.st_size
                                if kind == 'worker-partial':
                                    # The host rejects direct pidfd signals.
                                    # Its authorized Docker API performs the
                                    # actual abrupt SIGKILL. Explicitly restart
                                    # the same object; do not claim automatic
                                    # Docker restart-policy evidence.
                                    assert worker() == before_worker
                                    command(['docker', 'kill', '--signal', 'KILL', before_worker['id']]); stopped = True
                                    command(['docker', 'start', before_worker['id']]); stopped = False
                                else: command(['systemctl', 'restart', 'whaledeck-agent.service'])
                                break
                        except FileNotFoundError: pass
                        time.sleep(0.003)
                    if kind == 'worker-partial':
                        after_worker = wait_worker(before_worker, changed=True)
                        assert after_worker['pid'] != before_worker['pid'] and after_worker['restarts'] == before_worker['restarts']
                    else:
                        assert agent_pid() != before_agent
                        after_worker = worker()
                        assert after_worker['running'] and all(after_worker[key] == before_worker[key] for key in ('id', 'image', 'pid', 'started', 'restarts'))
                PHASE = kind + '-independent-terminal-and-full-source-verification'
                expected = 'Succeeded' if kind == 'queued' else 'Failed'
                deadline = time.monotonic() + 180
                while True:
                    final = snapshot(identifier, 'restore')
                    if final['row']['state'] == expected: break
                    assert final['row']['state'] not in ('Unknown', 'Canceled', 'Succeeded', 'Superseded', 'Failed') and time.monotonic() < deadline
                    time.sleep(0.5)
                terminal(final, expected, 'CONTAINER_UPLOAD_PUBLISHED' if kind == 'queued' else 'CONTAINER_UPLOAD_INTERRUPTED', expected)
                row = final['row']
                immutable = ('id', 'idempotency_key', 'actor_subject', 'login_binding', 'trace_id', 'owner_hash', 'container_id', 'instance_id', 'relative_path',
                    'target_proof', 'expected_parent_version', 'expected_version', 'previous_size_bytes', 'plan_hash', 'request_hash', 'sha256',
                    'size_bytes', 'history_source_json', 'restore_attributes', 'created_at_utc', 'expires_at_utc')
                assert all(row[key] == frozen[key] for key in immutable) and row['restore_attributes'] is True
                assert not row['publish_barrier'] and row['lease_token'] is None
                record = root_receipt(row)
                if kind == 'queued':
                    assert record['schemaVersion'] == 4 and record['state'] == 'Published' and row['root_started_at_utc']
                    assert attributes(output) == DESIRED and owned.common.file_digest(output) == source_sha
                    assert record['restorationAttributes'] == dict(zip(('uid', 'gid', 'mode', 'modifiedNs'), DESIRED))
                    displaced = fixture['folder'] / fixture['private'] / (operation + '.upload')
                    assert displaced.stat().st_ino == current_identity[1] and displaced.read_bytes() == replacement and attributes(displaced) == CURRENT
                    fixture['privateProofs'][displaced.name] = owned.common.fingerprint(displaced), replacement_sha
                    fixture['proofs']['original.bin'] = owned.common.fingerprint(output), source_sha
                    assert owned.common.file_digest(STATE / (operation + '.data')) == source_sha
                    assert (history / (operation + '.data')).read_bytes() == replacement
                else:
                    assert record['state'] == 'Discarded' and record['schemaVersion'] == 4 and row['root_started_at_utc'] is None
                    assert not record.get('originalCapture') and owned.common.fingerprint(output) == current_identity
                    assert output.read_bytes() == replacement and attributes(output) == CURRENT
                    assert not (history / (operation + '.json')).exists()
                    assert not (history / (operation + '.data')).exists()
                    assert not (STATE / (operation + '.partial')).exists() and not (STATE / (operation + '.data')).exists()
                assert (history / (capture + '.json')).read_bytes() == source_metadata
                assert owned.common.fingerprint(history / (capture + '.data')) == source_data_identity
                assert owned.common.file_digest(history / (capture + '.data')) == source_sha
                public = request(session, 'GET', endpoint + f'restores/{identifier}', 200).json()
                assert public['restoreAttributes'] is True and public['state'] == expected and public['jobId'] == identifier
                assert command(['systemctl', 'show', 'whaledeck-maintenance.service', '-p', 'MainPID', '--value']).strip() == maintenance
                assert request(session, 'GET', base + '/api/v1/system/status', 200).json()['build']['sourceRevision'] == revision
                outcome = {'revision': revision, 'kind': kind, 'intent': identifier, 'state': expected, 'partialBytesBeforeRestart': partial_bytes,
                    'containerId': fixture['identifier'], 'source': str(fixture['folder']), 'capture': capture, 'sourceSha256': source_sha,
                    'workerBefore': before_worker, 'agentBefore': before_agent,
                    'fullHistoricalBytesVerified': SIZE, 'immutablePolicyUnchanged': True, 'targetAndHistoryVerified': True,
                    'maintenancePidUnchanged': True, 'newWorkerPid': worker()['pid'], 'newAgentPid': agent_pid()}
                if kind == 'worker-partial': outcome['restartMethod'] = 'docker-sigkill-explicit-start-not-automatic-restart'
                outcomes.append(outcome); save(kind + '.json', outcome)
                finished = True
                print(json.dumps({'restorationRestartCase': kind, 'state': expected, 'partialBytesBeforeRestart': partial_bytes}), flush=True)
            finally:
                if stopped or not worker()['running']:
                    command(['docker', 'start', before_worker['id']]); wait_worker(before_worker)
                if not finished and identifier is not None:
                    # While this exact fixture still exists, request normal
                    # cancellation and wait for facts before its context exits.
                    # Never rewrite the failed task or fabricate a root proof.
                    failure = snapshot(identifier, 'restore')
                    if failure['row']['state'] not in ('Succeeded', 'Superseded', 'Canceled', 'Failed', 'Unknown'):
                        request(session, 'POST', endpoint + f'restores/{identifier}/cancel', 202,
                            headers={**api.headers, 'Idempotency-Key': identifier})
                        deadline = time.monotonic() + 120
                        while snapshot(identifier, 'restore')['row']['state'] not in ('Succeeded', 'Superseded', 'Canceled', 'Failed', 'Unknown'):
                            assert time.monotonic() < deadline
                            time.sleep(0.5)
    assert worker()['healthy'] and agent_pid() > 1
    assert query("SELECT json_build_object('active',count(*)) FROM container_file_upload_operations WHERE completed_at_utc IS NULL;")['active'] == 0
    logout(base, session); session.close()
    save('verified.json', {'revision': revision, 'cases': outcomes, 'verifiedKinds': list(kinds),
        'sqlCacheTimeAndReceiptsUnmodified': True, 'noBrowserClaim': True, 'permanentEvidenceRetained': True})
    print(json.dumps({'installedRestorationRestart': 'passed', 'cases': len(outcomes), 'evidence': str(evidence)}), flush=True)


if __name__ == '__main__':
    try: main()
    except BaseException as error:
        import traceback
        print(json.dumps({'installedRestorationRestart': 'failed', 'phase': PHASE, 'failureType': type(error).__name__,
            'locations': [{'file': Path(frame.filename).name, 'line': frame.lineno} for frame in traceback.extract_tb(error.__traceback__)],
            'unverifiedDataPreserved': True}), file=sys.stderr)
        raise SystemExit(1) from None
