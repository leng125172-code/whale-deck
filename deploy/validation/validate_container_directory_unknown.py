#!/usr/bin/env python3
"""Real installed pre-publication interruption -> permanent Unknown -> UI ack.

No fabricated SQL rows, Root receipts, capability, clock or product test hook.
An exact pidfd-bound helper is paused after its real receipt, its owned target
container is removed, then only that helper is killed. Another app must wait
behind the barrier until a different real administrator explicitly acknowledges
the original through the normal page. Failure cleanup uses the same BFF policy,
never SQL edits or deletion of unproved private objects.
"""
from concurrent.futures import ThreadPoolExecutor
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import sys
import time
import uuid

import requests

import validate_container_file_publishing as owned
from bound_container_directory_cut import freeze_prepublication, record_for
from validate_container_directory_browser import ROOT, snapshot
from validate_container_directory_restart import Worker, clean_success, command, preflight, wait_terminal
from validate_container_upload_bff import login, logout, request, temporary_user
from validate_databases import Api
from validate_oidc import required_environment

PHASE = 'startup'


def register(base, session, api, fixture, path):
    inspect = json.loads(command('docker', 'inspect', fixture['identifier']))[0]
    instance = str(inspect['State']['Pid']) + ':' + inspect['State']['StartedAt']
    endpoint = base + f'/api/v1/containers/{fixture["identifier"]}/files/directories'
    plan = request(session, 'POST', endpoint + '/plan', 200, headers=api.headers,
        json={'instanceId': instance, 'path': path, 'mode': '0750'}).json()
    key = str(uuid.uuid4())
    intent = request(session, 'POST', endpoint, 202, headers={**api.headers, 'Idempotency-Key': key},
        json={'planHash': plan['planHash'], 'confirmed': True, 'idempotencyKey': key}).json()
    return intent, {'containerId': fixture['identifier'], 'instanceId': instance, 'source': str(fixture['folder']),
        'path': path, 'containerPid': inspect['State']['Pid']}


def wait_unknown(identifier):
    deadline = time.monotonic() + 100
    while True:
        value = snapshot(identifier)
        if value['row']['state'] == 'Unknown':
            assert value['row']['publish_barrier'] and value['row']['completed_at_utc'] is None
            return value
        assert value['row']['state'] in ('Pending', 'Applying'), 'REAL_ROOT_CUT_DID_NOT_PRODUCE_UNKNOWN'
        assert time.monotonic() < deadline, 'DIRECTORY_UNKNOWN_DEADLINE'
        time.sleep(0.5)


def wait_settled(identifier):
    deadline = time.monotonic() + 100
    while True:
        value = snapshot(identifier)
        if value['row']['state'] not in ('Pending', 'Applying'): return value
        assert time.monotonic() < deadline, 'ORIGINAL_DIRECTORY_TASK_STILL_ACTIVE'
        time.sleep(0.5)


def private_snapshot(fixture, record):
    private = fixture['folder'] / '.whaledeck-container-directories-v1'
    if not private.exists(): return None
    pending = private / (record['operationId'] + '.directory')
    return {'private': owned.common.fingerprint(private),
        'pending': owned.common.fingerprint(pending) if pending.exists() else None}


def clean_private(fixture, record, proof):
    assert not os.path.lexists(fixture['folder'] / record['plan']['command']['path'].removeprefix('work/'))
    private = fixture['folder'] / '.whaledeck-container-directories-v1'
    if proof is None:
        assert not os.path.lexists(private); return
    assert owned.common.fingerprint(private) == tuple(proof['private'])
    assert stat.S_ISDIR(private.lstat().st_mode) and not private.is_symlink()
    pending = private / (record['operationId'] + '.directory')
    if proof['pending'] is not None:
        assert {path.name for path in private.iterdir()} == {pending.name}
        assert owned.common.fingerprint(pending) == tuple(proof['pending']) and pending.is_dir() and not pending.is_symlink()
        assert not list(pending.iterdir()); pending.rmdir()
    assert not list(private.iterdir()); private.rmdir()


def acknowledge_if_unknown(base, session, api, identifier):
    value = snapshot(identifier)
    if value['row']['state'] == 'Unknown':
        path = base + '/api/v1/files/container-directories/recovery/' + identifier
        response = request(session, 'GET', path, 200).json()
        request(session, 'POST', path + '/acknowledge', 202, headers={**api.headers, 'Idempotency-Key': identifier},
            json={'version': response['version'], 'confirmed': True})
    value = snapshot(identifier)
    assert value['row']['state'] == 'Acknowledged' and value['job']['state'] == 'Failed'
    assert not value['row']['publish_barrier'] and value['row']['lease_token'] is None
    return value


def main():
    global PHASE
    assert os.geteuid() == 0 and len(sys.argv) == 2; os.umask(0o077)
    revision = sys.argv[1]; assert re.fullmatch('[a-f0-9]{40}', revision)
    base = os.environ.get('WHALEDECK_URL', 'http://192.168.100.13:8080').rstrip('/')
    anonymous = requests.Session(); anonymous.trust_env = False
    assert request(anonymous, 'GET', base + '/api/v1/system/status', 200).json()['build']['sourceRevision'] == revision
    preflight(); worker = Worker()
    repository = Path(__file__).resolve().parents[2]
    evidence = repository / 'deploy/artifacts' / ('container-directory-unknown-' + uuid.uuid4().hex); evidence.mkdir(mode=0o700)
    admin_name, admin_password = required_environment('VALIDATION_USERNAME'), required_environment('VALIDATION_PASSWORD')
    administrator, profile = login(base, admin_name, admin_password); api = Api(base, administrator)
    intent = None; waiting = None; held = None; original_profile = None; physical = None; frozen = None; waiting_clean = False; original_clean = False
    with owned.fixture(restartable_core=('whaledeck-worker',), removable=True) as original, owned.fixture(restartable_core=('whaledeck-worker',)) as other:
        try:
            with temporary_user(True) as (name, password):
                owner, original_profile = login(base, name, password); owner_api = Api(base, owner)
                assert original_profile['subject'] != profile['subject']
                PHASE = 'real-confirmed-original-with-worker-stopped'; worker.stop()
                intent, baseline = register(base, owner, owner_api, original, 'work/发布中断未知目录')
                baseline.update(revision=revision, intent=intent['id'], originalSubject=original_profile['subject'], evidence=str(evidence),
                    originalFileProof=original['proofs']['original.bin'])
                (evidence / 'baseline.json').write_text(json.dumps(baseline))
                row = snapshot(intent['id'])['row']; assert row['state'] == 'Pending' and row['root_started_at_utc'] is None
                PHASE = 'pidfd-bound-real-root-prepublication-cut'
                with ThreadPoolExecutor(max_workers=1) as observers:
                    watch = observers.submit(freeze_prepublication, row, original['folder'], baseline['containerPid'])
                    worker.start(wait_ready=False); held = watch.result(timeout=25)
                if held is None: raise AssertionError('REAL_PREPUBLICATION_CUT_MISSED_NOT_COUNTED')
                applying = snapshot(intent['id'])['row']
                assert applying['state'] == 'Applying' and applying['publish_barrier'] and applying['root_started_at_utc'] is not None
                frozen = held.record; physical = private_snapshot(original, frozen)
                (evidence / 'frozen-root.json').write_text(json.dumps(frozen))
                (evidence / 'physical-before.json').write_text(json.dumps(physical))
                (evidence / 'bound-driver.json').write_text(json.dumps({'pid': held.pid, 'unit': held.cgroup,
                    'twoKernelFdProofs': True, 'rootState': frozen['state'], 'targetAbsent': True}))
                original['remove_owned'](); held.kill(); held = None
                PHASE = 'natural-worker-observation-keeps-unknown-barrier'
                unknown = wait_unknown(intent['id']); root_unknown = record_for(unknown['row'])
                assert root_unknown['state'] == 'Unknown' and root_unknown['operationId'] == frozen['operationId']
                immutable = {key: unknown['row'][key] for key in ('actor_subject', 'login_binding', 'request_hash', 'plan_json', 'owner_hash')}
                raw_unknown = (ROOT / (uuid.UUID(intent['id']).hex + '.json')).read_bytes()
                (evidence / 'unknown-root.json').write_bytes(raw_unknown)
                logout(base, owner); owner.close()
            # The context proved the original Authentik account was deleted.
            PHASE = 'other-container-is-blocked-by-original-permanent-barrier'
            waiting, wait_baseline = register(base, administrator, api, other, 'work/屏障解除后目录')
            wait_baseline['revision'] = revision
            baseline.update(waitingIntent=waiting['id'], originalIdentityDeleted=True, waiting=wait_baseline)
            (evidence / 'baseline.json').write_text(json.dumps(baseline))
            deadline = time.monotonic() + 15
            while True:
                blocked = snapshot(waiting['id'])['row']
                assert blocked['state'] == 'Pending' and blocked['root_started_at_utc'] is None and not blocked['publish_barrier']
                assert snapshot(intent['id'])['row']['publish_barrier']
                assert not os.path.lexists(other['folder'] / '屏障解除后目录')
                if blocked['version'] >= 4: break  # At least two claim/defer cycles.
                assert time.monotonic() < deadline, 'SECOND_APPLICATION_NOT_OBSERVED_WAITING'
                time.sleep(0.5)
            (evidence / 'blocked.json').write_text(json.dumps({'waitingVersion': blocked['version'], 'rootStarted': False, 'originalBarrier': True}))
            PHASE = 'different-administrator-real-mobile-recovery-page'
            browser = subprocess.run(['node', str(Path(__file__).with_suffix('.mjs')), revision], input=json.dumps(baseline),
                capture_output=True, text=True, timeout=240)
            assert len(browser.stdout.encode()) <= 32768 and len(browser.stderr.encode()) <= 32768
            (evidence / 'browser.stdout').write_text(browser.stdout); (evidence / 'browser.stderr').write_text(browser.stderr)
            if browser.returncode:
                for line in browser.stderr.splitlines():
                    try:
                        value = json.loads(line)
                        if value.get('installedContainerDirectoryUnknownUi') == 'failed': print(json.dumps(value), flush=True)
                    except ValueError: pass
                raise AssertionError('REAL_UNKNOWN_PAGE_NOT_VERIFIED')
            ui = json.loads((evidence / 'browser-result.json').read_bytes())
            assert ui['fullSsoLogout'] and ui['confirmations'] == 1 and ui['registrations'] == ui['bodies'] == 0
            assert ui['subject'] == profile['subject'] and ui['originalState'] == 'Acknowledged' and ui['waitingState'] == 'Succeeded'
            PHASE = 'independent-old-evidence-and-waiting-publication-proof'
            final = acknowledge_if_unknown(base, administrator, api, intent['id']); row = final['row']
            assert {key: row[key] for key in immutable} == immutable and row['acknowledged_by'] == profile['subject']
            assert row['published_version'] is None and final['pending'] == 0 and final['accepted'] == 1
            assert (ROOT / (uuid.UUID(intent['id']).hex + '.json')).read_bytes() == raw_unknown
            (evidence / 'acknowledged.json').write_text(json.dumps({'id': intent['id'], 'state': row['state'], 'jobState': final['job']['state'],
                'newAdministrator': row['acknowledged_by'], 'originalBindingUnchanged': True, 'rootEvidenceUnchanged': True}))
            clean_success(waiting, wait_baseline, profile, evidence, other); waiting_clean = True
            clean_private(original, frozen, physical); original_clean = True
            logout(base, administrator)
        finally:
            if held is not None: held.release()
            worker.start()
            # Never leave a deliberate test Unknown globally blocking writes
            # when a page observer fails. The same real authorized endpoint
            # can seal it; this does NOT make the failed UI gate pass.
            if intent is not None and not original_clean or waiting is not None and not waiting_clean:
                fallback, fallback_profile = login(base, admin_name, admin_password); fallback_api = Api(base, fallback)
                try:
                    if intent is not None:
                        current = wait_settled(intent['id'])['row']
                        if current['state'] == 'Unknown':
                            acknowledge_if_unknown(base, fallback, fallback_api, intent['id'])
                            (evidence / 'fallback-acknowledgement.json').write_text(json.dumps({'id': intent['id'], 'realAuthorizedBff': True, 'uiGatePassed': False}))
                        if not original_clean:
                            current = snapshot(intent['id'])['row']
                            if current['state'] == 'Succeeded':
                                clean_success(intent, baseline, original_profile, evidence, original); original_clean = True
                            elif current['state'] == 'Acknowledged' and frozen is not None:
                                clean_private(original, frozen, physical); original_clean = True
                    if waiting is not None and not waiting_clean:
                        value = wait_settled(waiting['id'])
                        if value['row']['state'] == 'Unknown': acknowledge_if_unknown(base, fallback, fallback_api, waiting['id'])
                        elif value['row']['state'] == 'Succeeded':
                            clean_success(waiting, wait_baseline, profile, evidence, other); waiting_clean = True
                finally: logout(base, fallback); fallback.close()
    administrator.close(); anonymous.close(); preflight()
    (evidence / 'verified.json').write_text(json.dumps({'revision': revision, 'intent': intent['id'], 'waitingIntent': waiting['id'],
        'realPidfdCutAndDeletedTarget': True, 'originalAccountDeleted': True, 'otherApplicationWaited': True,
        'differentAdministratorUi': True, 'oldJobFailedNotCreated': True, 'rootAndBusinessEvidenceRetained': True}))
    print(json.dumps({'installedContainerDirectoryUnknown': 'passed', 'revision': revision, 'intent': intent['id'],
        'waitingIntent': waiting['id'], 'ownedSourcesAndContainersCleaned': True, 'evidence': str(evidence)}), flush=True)


if __name__ == '__main__':
    try: main()
    except BaseException as error:
        import traceback
        print(json.dumps({'installedContainerDirectoryUnknown': 'failed', 'phase': PHASE, 'failureType': type(error).__name__,
            'locations': [{'file': Path(frame.filename).name, 'line': frame.lineno} for frame in traceback.extract_tb(error.__traceback__)],
            'unverifiedObjectsPreserved': True}), file=sys.stderr)
        raise SystemExit(1) from None
