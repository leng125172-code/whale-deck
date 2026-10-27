#!/usr/bin/env python3
"""Installed attribute publication interrupted after a positive private-copy proof.

No injected journal, SQL/cache/time edits or test hooks in production code.
Only the fixed Worker is abruptly stopped/started and two exact owned ordinary
containers are changed. A different real administrator uses the visible UI to
seal the original uncertainty. Preserve unverified bytes and all durable facts.
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
from validate_container_restoration_restart import SIZE, DESIRED, CURRENT, worker, wait_worker, set_attributes, attributes
from validate_container_file_staging import load
from validate_container_upload_bff import login, logout, request, snapshot, wait_sql, terminal, root_receipt, temporary_user, query
from validate_container_upload_publish_interruption import register, send
from validate_container_upload_recovery_agent import digest, trusted
from validate_databases import Api

PHASE = 'startup'
STATE = Path('/var/lib/whaledeck-container-uploads')


def main():
    global PHASE
    assert os.geteuid() == 0 and len(sys.argv) == 2; os.umask(0o077)
    revision = sys.argv[1]; assert re.fullmatch('[a-f0-9]{40}', revision)
    repository = Path(__file__).resolve().parents[2]
    validator_revision = owned.common.command(['git', '-C', str(repository), 'rev-parse', 'HEAD']).decode().strip()
    assert re.fullmatch('[a-f0-9]{40}', validator_revision)
    for name in ('whaledeck-container-file-write.py', 'whaledeck-container-upload.py', 'whaledeck-container-publish.py',
            'whaledeck-container-history.py', 'whaledeck-container-restore.py', 'whaledeck-container-backups.py'):
        assert Path('/usr/local/libexec', name).read_bytes() == owned.common.command(['git', '-C', str(repository), 'show', revision + ':deploy/host/' + name])
    evidence = repository / 'deploy/artifacts' / ('container-restoration-publication-' + uuid.uuid4().hex)
    evidence.mkdir(mode=0o700)
    def save(name, value):
        assert name in ('baseline.json', 'interrupted-root.json', 'browser.stdout', 'browser.stderr', 'verified.json')
        output = evidence / name; output.write_text(value if isinstance(value, str) else json.dumps(value)); output.chmod(0o600)
    base = os.environ.get('WHALEDECK_URL', 'http://192.168.100.13:8080').rstrip('/')
    assert query("SELECT json_build_object('active',count(*)) FROM container_file_upload_operations WHERE completed_at_utc IS NULL;")['active'] == 0
    initial_worker = worker(); assert initial_worker['healthy'] and initial_worker['running']
    maintenance = owned.common.command(['systemctl', 'show', 'whaledeck-maintenance.service', '-p', 'MainPID', '--value']).strip()
    assert int(maintenance) > 1
    backups = load(Path('/usr/local/libexec/whaledeck-file-backups.py'), 'publication_restore_hdd')
    files = load(Path('/usr/local/libexec/whaledeck-files.py'), 'publication_restore_files')
    config = backups.FileBackups(files).configuration()
    history = Path(config['mountPoint']) / config['targetRelative'] / 'container-files-v1'
    assert shutil.disk_usage(history).free > 8 * 1024**3 and shutil.disk_usage(owned.common.APPS).free > 8 * 1024**3
    with temporary_user(True) as (other_name, other_password):
        other, other_profile = login(base, other_name, other_password); other_api = Api(base, other)
        assert request(other, 'GET', base + '/api/v1/system/status', 200).json()['build']['sourceRevision'] == revision
        with owned.fixture(restartable_core=('whaledeck-worker',)) as first, owned.fixture(restartable_core=('whaledeck-worker',)) as second:
            PHASE = 'real-bff-history-and-attribute-restoration'
            output = first['folder'] / 'original.bin'; block = hashlib.shake_256(b'owned-publication-history-v1').digest(1024**2); checksum = hashlib.sha256()
            with output.open('wb') as stream:
                remaining = SIZE
                while remaining:
                    part = block[:min(remaining, len(block))]; stream.write(part); checksum.update(part); remaining -= len(part)
                stream.flush(); os.fsync(stream.fileno())
            set_attributes(output, DESIRED); source_sha = checksum.hexdigest().upper()
            source_identity = owned.common.fingerprint(output); first['proofs']['original.bin'] = source_identity, source_sha
            body = hashlib.shake_256(b'owned-publication-current-v1').digest(owned.common.SIZE); current_sha = hashlib.sha256(body).hexdigest().upper()
            with temporary_user(True) as (owner_name, owner_password):
                owner, owner_profile = login(base, owner_name, owner_password); owner_api = Api(base, owner)
                endpoint = base + f'/api/v1/containers/{first["identifier"]}/files/'
                target = request(owner, 'GET', endpoint + 'target', 200).json()
                original = request(owner, 'GET', endpoint + 'entry', 200, params={'instanceId': target['instanceId'], 'path': 'work/original.bin'}).json()
                _, initial, initial_sha = register(base, owner, owner_api, first, 'work/original.bin', body, original['version'])
                assert initial_sha == current_sha; send(owner, owner_api, endpoint, initial, body)
                initial_value = wait_sql(initial['id'], 'Succeeded'); terminal(initial_value, 'Succeeded', 'CONTAINER_UPLOAD_PUBLISHED', 'Succeeded')
                source_capture = uuid.UUID(initial['id']).hex; first['private'] = '.whaledeck-container-files-v1'
                old = first['folder'] / first['private'] / (source_capture + '.upload')
                assert old.stat().st_ino == source_identity[1] and owned.common.file_digest(old) == source_sha
                first['privateProofs'][old.name] = owned.common.fingerprint(old), source_sha
                source_metadata = (history / (source_capture + '.json')).read_bytes()
                source_data_identity = owned.common.fingerprint(history / (source_capture + '.data'))
                assert owned.common.file_digest(history / (source_capture + '.data')) == source_sha
                set_attributes(output, CURRENT); current_identity = owned.common.fingerprint(output)
                first['proofs']['original.bin'] = current_identity, current_sha
                source = next(item for item in request(owner, 'GET', endpoint + 'history', 200,
                    params={'instanceId': target['instanceId'], 'path': 'work/original.bin'}).json()['entries'] if item['captureId'] == source_capture)
                current = request(owner, 'GET', endpoint + 'entry', 200, params={'instanceId': target['instanceId'], 'path': 'work/original.bin'}).json()
                plan = request(owner, 'POST', endpoint + 'restores/plan', 200, headers=owner_api.headers, json={'instanceId': target['instanceId'],
                    'path': 'work/original.bin', 'captureId': source_capture, 'expectedCaptureHash': source['captureHash'], 'expectedVersion': current['version'], 'restoreAttributes': True}).json()
                assert plan['restoreAttributes'] is True; key = str(uuid.uuid4())
                intent = request(owner, 'POST', endpoint + 'restores', 202, headers={**owner_api.headers, 'Idempotency-Key': key},
                    json={'planHash': plan['planHash'], 'confirmed': True, 'idempotencyKey': key}).json()
                assert intent['restoreAttributes'] is True; identifier = intent['id']; operation = uuid.UUID(identifier).hex
                baseline = {'revision': revision, 'validatorRevision': validator_revision, 'intent': identifier, 'containerId': first['identifier'], 'source': str(first['folder']),
                    'secondContainerId': second['identifier'], 'secondSource': str(second['folder']), 'sourceCapture': source_capture,
                    'sourceSha256': source_sha, 'sourceBytes': SIZE, 'currentSha256': current_sha, 'currentFingerprint': current_identity,
                    'evidence': str(evidence), 'workerBefore': initial_worker}
                save('baseline.json', baseline)
                PHASE = 'positive-private-publication-copy-and-complete-hdd-before-worker-kill'
                pending = first['folder'] / first['private'] / (operation + '.upload'); deadline = time.monotonic() + 120
                while True:
                    assert time.monotonic() < deadline
                    try:
                        info = trusted(pending); record = json.loads((STATE / (operation + '.json')).read_bytes())
                        if 0 < info.st_size < SIZE and record['state'] == 'PublishPreparing':
                            assert record['schemaVersion'] == 4 and record['publication']['pendingIdentity'] == [info.st_dev, info.st_ino]
                            assert 'pendingProof' not in record['publication'] and 'attributePreparationProof' not in record['publication']
                            # This is later than the previous partial-HDD gate:
                            # current target's backup is already complete.
                            current_capture = json.loads((history / (operation + '.json')).read_bytes())
                            assert current_capture['state'] == 'Verified' and (history / (operation + '.data')).read_bytes() == body
                            live = snapshot(identifier, 'restore')['row']
                            assert live['state'] == 'Publishing' and live['root_started_at_utc'] and live['publish_barrier'] and live['restore_attributes']
                            assert owned.common.fingerprint(output) == current_identity and output.read_bytes() == body
                            before_worker = worker(); assert before_worker['running']
                            assert (before_worker['id'], before_worker['image']) == (initial_worker['id'], initial_worker['image'])
                            # Recheck after SQL and target verification: never
                            # call an earlier observation the actual cut point.
                            info = trusted(pending); record = json.loads((STATE / (operation + '.json')).read_bytes())
                            if not 0 < info.st_size < SIZE or record['state'] != 'PublishPreparing': continue
                            assert record['publication']['pendingIdentity'] == [info.st_dev, info.st_ino]
                            partial_before = info.st_size
                            save('interrupted-root.json', record)
                            owned.common.command(['docker', 'kill', '--signal', 'KILL', before_worker['id']])
                            owned.common.command(['docker', 'start', before_worker['id']])
                            after_worker = wait_worker(before_worker, changed=True)
                            assert after_worker['restarts'] == before_worker['restarts']
                            break
                    except FileNotFoundError: pass
                    time.sleep(0.003)
                PHASE = 'natural-lease-expiry-and-permanent-publication-barrier'
                unknown = wait_sql(identifier, 'Unknown'); row = unknown['row']
                assert row['root_started_at_utc'] and row['publish_barrier'] and row['completed_at_utc'] is None and row['published_version'] is None
                retained_record = root_receipt(row)
                assert retained_record['schemaVersion'] == 4 and retained_record['state'] == 'PublishInterrupted'
                assert retained_record['request']['restoreSource']['captureId'] == source_capture
                assert owned.common.fingerprint(output) == current_identity and output.read_bytes() == body and attributes(output) == CURRENT
                pending_identity = owned.common.fingerprint(pending); pending_sha = owned.common.file_digest(pending)
                assert pending_identity[:2] == tuple(retained_record['publication']['pendingIdentity']) and 0 < pending.stat().st_size <= SIZE
                with pending.open('rb') as left, (STATE / (operation + '.data')).open('rb') as right:
                    while part := left.read(1024**2): assert part == right.read(len(part))
                first['privateProofs'][pending.name] = pending_identity, pending_sha
                assert owned.common.file_digest(STATE / (operation + '.data')) == source_sha
                PHASE = 'different-application-remains-blocked-until-human-seal'
                second_endpoint, waiting, waiting_sha = register(base, other, other_api, second, 'work/after-barrier.bin', body, 'ABSENT')
                baseline['waitingIntent'] = waiting['id']; save('baseline.json', baseline)
                send(other, other_api, second_endpoint, waiting, body)
                version = snapshot(waiting['id'])['row']['version']; deferred = 0; deadline = time.monotonic() + 50
                while deferred < 2:
                    assert time.monotonic() < deadline
                    candidate = snapshot(waiting['id'])['row']
                    assert candidate['state'] == 'Ready' and candidate['root_started_at_utc'] is None and not candidate['publish_barrier']
                    assert snapshot(identifier, 'restore')['row']['publish_barrier'] and not (second['folder'] / 'after-barrier.bin').exists()
                    if candidate['lease_token'] is None and candidate['version'] > version:
                        deferred += 1; version = candidate['version']
                    time.sleep(0.25)
                logout(base, owner); owner.close()
            PHASE = 'actual-different-admin-visible-ui-plan-cancel-confirm-and-seal'
            assert other_profile['subject'] != owner_profile['subject']
            environment = dict(os.environ, VALIDATION_USERNAME=other_name, VALIDATION_PASSWORD=other_password)
            result = subprocess.run(['node', str(Path(__file__).with_suffix('.mjs')), revision], input=json.dumps(baseline),
                text=True, capture_output=True, timeout=240, env=environment)
            assert len(result.stdout.encode()) <= 32768 and len(result.stderr.encode()) <= 32768
            save('browser.stdout', result.stdout); save('browser.stderr', result.stderr)
            if result.returncode:
                for line in result.stderr.splitlines():
                    try:
                        value = json.loads(line)
                        if value.get('restorationPublicationUi') == 'failed': print(json.dumps({key: value.get(key) for key in ('phase', 'failureType', 'locations')}), flush=True)
                    except ValueError: pass
                raise AssertionError('PUBLICATION_RECOVERY_UI_NOT_VERIFIED')
            observed = json.loads((evidence / 'browser-result.json').read_bytes())
            assert observed['fullSsoLogout'] and observed['confirms'] == observed['registrations'] == 1 and observed['contentRequests'] == 0
            final = snapshot(identifier, 'restore'); terminal(final, 'Failed', 'CONTAINER_UPLOAD_ADMIN_ACKNOWLEDGED', 'Failed')
            mutable = {'state', 'version', 'lease_token', 'lease_expires_at_utc', 'next_attempt_at_utc', 'publish_barrier', 'result_code', 'completed_at_utc'}
            assert {k: v for k, v in final['row'].items() if k not in mutable} == {k: v for k, v in row.items() if k not in mutable}
            seal = root_receipt(final['row']); assert seal['schemaVersion'] == 3 and seal['state'] == 'Sealed'
            assert seal['originalRecord'] == retained_record and seal['originalRecordHash'] == digest('original', retained_record)
            assert seal['actorHash'] == digest('actor', other_profile['subject']) and seal['recoveryId'] == uuid.UUID(observed['executionJobId']).hex
            PHASE = 'released-barrier-and-independent-full-source-private-and-hdd-proofs'
            completed = wait_sql(waiting['id'], 'Succeeded'); terminal(completed, 'Succeeded', 'CONTAINER_UPLOAD_PUBLISHED', 'Succeeded')
            later = second['folder'] / 'after-barrier.bin'; assert later.read_bytes() == body and completed['row']['sha256'] == waiting_sha
            second['proofs'][later.name] = owned.common.fingerprint(later), waiting_sha; second['private'] = '.whaledeck-container-files-v1'
            assert not list((second['folder'] / second['private']).iterdir())
            assert owned.common.fingerprint(pending) == pending_identity and owned.common.file_digest(pending) == pending_sha
            assert owned.common.fingerprint(output) == current_identity and output.read_bytes() == body and attributes(output) == CURRENT
            assert (history / (source_capture + '.json')).read_bytes() == source_metadata
            assert owned.common.fingerprint(history / (source_capture + '.data')) == source_data_identity
            assert owned.common.file_digest(history / (source_capture + '.data')) == source_sha
            assert (history / (operation + '.data')).read_bytes() == body and owned.common.file_digest(STATE / (operation + '.data')) == source_sha
            assert owned.common.command(['systemctl', 'show', 'whaledeck-maintenance.service', '-p', 'MainPID', '--value']).strip() == maintenance
            save('verified.json', {'revision': revision, 'positivePrivateBytesBeforeWorkerRestart': partial_before,
                'privateBytesRetainedAfterSeal': pending.stat().st_size, 'fullHistoryBytes': SIZE, 'originalTargetUnchanged': True,
                'completeCurrentBackupBeforeFault': True, 'differentApplicationDeferredClaims': deferred, 'secondPublishedAfterSeal': True,
                'normalDifferentAdminUiPlanAndConfirmation': True, 'originalIdentityDeleted': True, 'rootSchema4SealedIntact': True,
                'maintenancePidUnchanged': True, 'recoveryId': observed['recoveryId'], 'durableEvidenceRetained': True})
        logout(base, other); other.close()
    assert worker()['healthy']
    print(json.dumps({'installedRestorationPublication': 'passed', 'revision': revision, 'evidence': str(evidence),
        'actualPrivateCopyFaultAndGlobalBarrier': True, 'differentAdminUiSealAndReleasedWaitingJob': True}), flush=True)


if __name__ == '__main__':
    try: main()
    except BaseException as error:
        import traceback
        print(json.dumps({'installedRestorationPublication': 'failed', 'phase': PHASE, 'failureType': type(error).__name__,
            'locations': [{'file': Path(frame.filename).name, 'line': frame.lineno} for frame in traceback.extract_tb(error.__traceback__)],
            'unverifiedDataPreserved': True}), file=sys.stderr)
        raise SystemExit(1) from None
