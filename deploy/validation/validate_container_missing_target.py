#!/usr/bin/env python3
"""Actual queued upload target removal, prompt Unknown and a different admin UI seal.

Only one exact owned ordinary fixture is removed by its existing verified
context. No content bytes, SQL/cache/time edits or production resource removal.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import time
import uuid

import validate_container_file_publishing as owned
from validate_container_restoration_restart import worker, wait_worker
from validate_container_upload_bff import login, logout, request, snapshot, terminal, root_receipt, temporary_user, query
from validate_container_upload_recovery_agent import digest
from validate_databases import Api

PHASE = 'startup'


def main():
    global PHASE
    assert os.geteuid() == 0 and len(sys.argv) == 2; os.umask(0o077)
    revision = sys.argv[1]; assert re.fullmatch('[a-f0-9]{40}', revision)
    base = os.environ.get('WHALEDECK_URL', 'http://192.168.100.13:8080').rstrip('/')
    repository = Path(__file__).resolve().parents[2]
    evidence = repository / 'deploy/artifacts' / ('container-missing-target-' + uuid.uuid4().hex); evidence.mkdir(mode=0o700)
    assert query("SELECT json_build_object('active',count(*)) FROM container_file_upload_operations WHERE completed_at_utc IS NULL;")['active'] == 0
    baseline_worker = worker(); assert baseline_worker['running'] and baseline_worker['healthy']
    state = Path('/var/lib/whaledeck-container-uploads'); value = None; stopped = False
    with temporary_user(True) as (owner_name, owner_password):
        owner, owner_profile = login(base, owner_name, owner_password); api = Api(base, owner)
        assert request(owner, 'GET', base + '/api/v1/system/status', 200).json()['build']['sourceRevision'] == revision
        try:
            with owned.fixture(restartable_core=('whaledeck-worker',)) as fixture:
                PHASE = 'normal-plan-and-persistent-registration-without-content'
                endpoint = base + f'/api/v1/containers/{fixture["identifier"]}/files/'
                target = request(owner, 'GET', endpoint + 'target', 200).json()
                plan = request(owner, 'POST', endpoint + 'uploads/plan', 200, headers=api.headers, json={'instanceId': target['instanceId'],
                    'path': 'work/missing.bin', 'sizeBytes': 3, 'sha256': hashlib.sha256(b'abc').hexdigest().upper(), 'expectedVersion': 'ABSENT'}).json()
                owned.common.command(['docker', 'stop', '--time', '20', baseline_worker['id']]); stopped = True
                key = str(uuid.uuid4())
                intent = request(owner, 'POST', endpoint + 'uploads', 202, headers={**api.headers, 'Idempotency-Key': key},
                    json={'planHash': plan['planHash'], 'confirmed': True, 'idempotencyKey': key}).json()
                operation = uuid.UUID(intent['id']).hex
                assert not (state / (operation + '.json')).exists()
                value = {'revision': revision, 'intent': intent['id'], 'containerId': fixture['identifier'],
                    'source': str(fixture['folder']), 'evidence': str(evidence)}
                path = evidence / 'baseline.json'; path.write_text(json.dumps(value)); path.chmod(0o600)
                owned.common.command(['docker', 'start', baseline_worker['id']]); stopped = False
                # Exiting the exact fixture proves and removes only its known
                # container/files; the original permanent job remains intact.
            PHASE = 'real-docker-404-through-installed-agent-and-worker'
            began = time.monotonic(); wait_worker(baseline_worker, changed=True)
            missing = subprocess.run(['docker', 'inspect', value['containerId']], capture_output=True)
            assert missing.returncode and b'no such object' in missing.stderr.lower() and not Path(value['source']).exists()
            while True:
                unknown = snapshot(value['intent']); row = unknown['row']
                if row['state'] == 'Unknown': break
                assert row['state'] == 'AwaitingUpload' and time.monotonic() - began < 35
                time.sleep(0.25)
            elapsed = time.monotonic() - began
            assert elapsed < 40 and row['result_code'] == 'CONTAINER_UPLOAD_TARGET_UNAVAILABLE'
            assert row['root_started_at_utc'] is None and not row['publish_barrier'] and row['receive_token'] is None
            for suffix in ('.json', '.partial', '.data'): assert not (state / (operation + suffix)).exists()
            logout(base, owner); owner.close()
        finally:
            if stopped: owned.common.command(['docker', 'start', baseline_worker['id']]); wait_worker(baseline_worker)
    PHASE = 'different-admin-real-ui-seal-does-not-invent-missing-operation-success'
    with temporary_user(True) as (name, password):
        session, profile = login(base, name, password); assert profile['subject'] != owner_profile['subject']
        environment = dict(os.environ, VALIDATION_USERNAME=name, VALIDATION_PASSWORD=password)
        result = subprocess.run(['node', str(repository / 'deploy/validation/validate_container_restoration_publication.mjs'), revision],
            input=json.dumps(value), text=True, capture_output=True, timeout=240, env=environment)
        for leaf, content in (('browser.stdout', result.stdout), ('browser.stderr', result.stderr)):
            path = evidence / leaf; path.write_text(content); path.chmod(0o600)
        assert result.returncode == 0
        observed = json.loads((evidence / 'browser-result.json').read_bytes()); assert observed['fullSsoLogout'] and observed['confirms'] == observed['registrations'] == 1
        final = snapshot(value['intent']); terminal(final, 'Failed', 'CONTAINER_UPLOAD_ADMIN_ACKNOWLEDGED', 'Failed')
        seal = root_receipt(final['row']); assert seal['schemaVersion'] == 3 and seal['state'] == 'Sealed' and seal['originalRecord'] is None
        assert seal['originalRecordHash'] == digest('original', None) and seal['actorHash'] == digest('actor', profile['subject'])
        for suffix in ('.partial', '.data'): assert not (state / (operation + suffix)).exists()
        logout(base, session); session.close()
    report = {'revision': revision, 'realMissingTargetRecognizedSeconds': round(elapsed, 3), 'noContentOrPublication': True,
        'unknownNotMissingOperation': True, 'differentAdminUiSeal': True, 'normalLogout': True, 'exactFixtureRemoved': True}
    path = evidence / 'verified.json'; path.write_text(json.dumps(report)); path.chmod(0o600)
    print(json.dumps({'installedMissingTarget': 'passed', **report, 'evidence': str(evidence)}), flush=True)


if __name__ == '__main__':
    try: main()
    except BaseException as error:
        import traceback
        print(json.dumps({'installedMissingTarget': 'failed', 'phase': PHASE, 'failureType': type(error).__name__,
            'locations': [{'file': Path(frame.filename).name, 'line': frame.lineno} for frame in traceback.extract_tb(error.__traceback__)]}), file=sys.stderr)
        raise SystemExit(1) from None
