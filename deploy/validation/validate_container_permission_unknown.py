#!/usr/bin/env python3
"""Real page -> HDD-attested paused helper -> owned target removal -> Unknown.

The product terminates the helper after visible SSO logout. A different real
administrator explicitly acknowledges from the installed mobile page. No Root,
SQL or clock edits, session injection, arbitrary kill, or automatic rollback.
"""
from concurrent.futures import ThreadPoolExecutor
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
from bound_container_permission_cut import ROOT, freeze_before_mode, fingerprint, record_for
from validate_container_permission_browser import snapshot
from validate_container_permission_faults import wait_signal, write_signal, wait_settled, save_history, prove_original, failure_acknowledge
from validate_container_directory_restart import Worker, preflight, command, clean_success
from validate_container_directory_unknown import register as register_waiting, wait_settled as settle_waiting
from validate_container_directory_browser import snapshot as directory_snapshot
from validate_container_upload_bff import login, logout, request, temporary_user
from validate_databases import Api
from validate_metric_continuity import query

PHASE = 'startup'


def spawn(mode, revision, baseline, username, password, directory):
    stdout = (directory / (mode + '.stdout')).open('wb'); stderr = (directory / (mode + '.stderr')).open('wb')
    try:
        browser = subprocess.Popen(['node', str(Path(__file__).with_suffix('.mjs')), revision, mode], stdin=subprocess.PIPE,
            stdout=stdout, stderr=stderr, env={**os.environ, 'VALIDATION_USERNAME': username, 'VALIDATION_PASSWORD': password})
        browser.stdin.write(json.dumps(baseline).encode()); browser.stdin.close(); browser.stdin = None
        return browser
    finally: stdout.close(); stderr.close()


def main():
    global PHASE
    assert os.geteuid() == 0 and len(sys.argv) == 2; os.umask(0o077)
    revision = sys.argv[1]; assert re.fullmatch('[a-f0-9]{40}', revision)
    base = 'http://127.0.0.1:8080'; preflight(); worker = Worker()
    directory = Path(__file__).resolve().parents[1] / 'artifacts' / ('container-permission-unknown-' + uuid.uuid4().hex)
    directory.mkdir(mode=0o700)
    print(json.dumps({'permissionUnknown': 'started', 'evidence': str(directory)}), flush=True)
    held = owner_browser = recovery_browser = None; identifier = waiting = None; client = api = profile = None; original_proved = waiting_proved = False
    with owned.fixture(restartable_core=('whaledeck-worker',), removable=True) as original, owned.fixture(restartable_core=('whaledeck-worker',)) as other:
        actual = json.loads(command('docker', 'inspect', original['identifier']))[0]
        source = original['folder'] / 'original.bin'
        baseline = {'revision': revision, 'containerId': original['identifier'], 'containerName': actual['Name'].lstrip('/'),
            'instanceId': str(actual['State']['Pid']) + ':' + actual['State']['StartedAt'], 'containerPid': actual['State']['Pid'],
            'source': str(original['folder']), 'original': fingerprint(source.stat()), 'sha256': owned.common.file_digest(source),
            'bytes': source.stat().st_size, 'coreBefore': owned.common.core_snapshot(), 'evidence': str(directory)}
        (directory / 'baseline.json').write_text(json.dumps(baseline))
        try:
            PHASE = 'original-real-page-preflight-and-confirmation'
            with temporary_user(True) as (username, password):
                owner_browser = spawn('owner', revision, baseline, username, password, directory)
                ready = wait_signal(directory, 'owner-ready.json', owner_browser); identifier = ready['id']
                worker.stop(); write_signal(directory, 'confirm.json', {'workerStopped': True})
                accepted = wait_signal(directory, 'owner-confirmed.json', owner_browser, 30)
                row = snapshot(identifier)['row']; assert row['state'] == 'Pending' and row['execution_job_id'] == accepted['executionJobId']
                baseline.update(id=identifier, executionJobId=row['execution_job_id'], originalSubject=ready['subject'])
                (directory / 'baseline.json').write_text(json.dumps(baseline))
                PHASE = 'real-hdd-attested-pre-mode-root-cut'
                with ThreadPoolExecutor(max_workers=1) as observers:
                    observing = observers.submit(freeze_before_mode, row, original['folder'], baseline['containerPid'], 'HistoryVerified')
                    worker.start(wait_ready=False); held = observing.result(timeout=28)
                assert held is not None, 'REAL_PERMISSION_UNKNOWN_CUT_MISSED'
                assert held.record['historyVerified'] and held.record['modeConfirmed'] is None
                (directory / 'cut-root.json').write_text(json.dumps(held.record))
                (directory / 'bound-helper.json').write_text(json.dumps({'pid': held.pid, 'unit': held.cgroup, 'twoKernelFdProofs': True, 'proof': held.proof}))
                write_signal(directory, 'held.json', {'rootPausedBeforeMode': True})
                wait_signal(directory, 'owner-inflight.json', owner_browser, 30)
                PHASE = 'remove-only-exact-owned-container-and-normal-page-logout'
                original['remove_owned']()
                assert fingerprint(source.stat()) == baseline['original']
                began = time.monotonic()
                with ThreadPoolExecutor(max_workers=1) as exits:
                    exited = exits.submit(held.wait_exit_seconds, began, 15)
                    write_signal(directory, 'logout.json', {'ownedContainerRemoved': True})
                    assert owner_browser.wait(timeout=35) == 0, 'ORIGINAL_VISIBLE_LOGOUT_FAILED'
                    elapsed = exited.result(timeout=16)
                assert elapsed is not None and elapsed < 15, 'PRODUCT_DID_NOT_STOP_UNKNOWN_PERMISSION_HELPER'
                held.release(); held = None
                owner_ui = json.loads((directory / 'owner-browser.json').read_bytes())
                assert owner_ui['actualUi'] and owner_ui['normalDualLogout'] and owner_ui['subject'] == ready['subject']
                assert owner_ui['registrations'] == owner_ui['confirmations'] == 1 and owner_ui['bodies'] == 0
            # The original Authentik identity really has been deleted here.
            PHASE = 'real-permanent-unknown-and-original-history'
            unknown = wait_settled(identifier); row = unknown['row']
            assert row['state'] == 'Unknown' and row['completed_at_utc'] is None and row['publish_barrier'] and row['lease_token'] is None
            assert row['mode_confirmed'] is None and row['history_verified'] and row['history_may_exist'] and row['published_version'] is None
            assert unknown['executionJob']['state'] == 'Running' and unknown['planJob']['state'] == 'Succeeded'
            record = save_history(row, directory, 'unknown'); assert record['state'] == 'Unknown'
            prove_original(original, baseline, record)
            raw_root = (ROOT / (record['operationId'] + '.json')).read_bytes()
            (directory / 'unknown-sql.json').write_text(json.dumps(unknown))
            with temporary_user(True) as (username, password):
                client, profile = login(base, username, password); api = Api(base, client)
                assert profile['subject'] != baseline['originalSubject']
                PHASE = 'other-container-real-directory-write-must-be-deferred'
                waiting, waiting_baseline = register_waiting(base, client, api, other, 'work/权限未知保护后的目录')
                waiting_baseline.update(subject=profile['subject'], originalFileProof=other['proofs']['original.bin'])
                (directory / 'waiting-baseline.json').write_text(json.dumps(waiting_baseline))
                checks = []
                for _ in range(3):
                    value = directory_snapshot(waiting['id']); blocked = value['row']
                    assert blocked['state'] == 'Pending' and blocked['root_started_at_utc'] is None and not blocked['publish_barrier']
                    assert not (other['folder'] / '权限未知保护后的目录').exists() and snapshot(identifier)['row']['publish_barrier']
                    checks.append({'nextAttemptAtUtc': blocked['next_attempt_at_utc'], 'createdAtUtc': blocked['created_at_utc'], 'rootStarted': False})
                    time.sleep(2)
                assert any(s['nextAttemptAtUtc'] > s['createdAtUtc'] for s in checks)
                (directory / 'waiting-blocked.json').write_text(json.dumps(checks))
                PHASE = 'different-administrator-real-mobile-unknown-recovery'
                recovery_baseline = {**baseline, 'waitingId': waiting['id']}
                recovery_browser = spawn('recovery', revision, recovery_baseline, username, password, directory)
                assert recovery_browser.wait(timeout=180) == 0, 'REAL_PERMISSION_UNKNOWN_RECOVERY_PAGE_FAILED'
                ui = json.loads((directory / 'recovery-browser.json').read_bytes())
                assert ui['actualUi'] and ui['normalDualLogout'] and ui['subject'] == profile['subject']
                assert ui['confirmations'] == 1 and ui['registrations'] == ui['bodies'] == 0
                PHASE = 'independent-original-facts-and-waiting-publication'
                final = snapshot(identifier); row = final['row']
                assert row['state'] == 'Acknowledged' and final['executionJob']['state'] == 'Failed' and row['acknowledged_by'] == profile['subject']
                assert row['mode_confirmed'] is None and row['published_version'] is None and row['history_verified'] and row['history_digest'] == unknown['row']['history_digest']
                assert row['completed_at_utc'] and not row['publish_barrier'] and row['lease_token'] is None and final['pending'] == 0
                assert (ROOT / (record['operationId'] + '.json')).read_bytes() == raw_root
                waiting_final = settle_waiting(waiting['id']); assert waiting_final['row']['state'] == 'Succeeded'
                assert waiting_final['row']['root_started_at_utc'] >= row['completed_at_utc']
                (directory / 'final-sql.json').write_text(json.dumps(final)); (directory / 'waiting-final-sql.json').write_text(json.dumps(waiting_final))
                PHASE = 'late-original-fixed-helper-cannot-repeat-chmod'
                original_request = {k: record[k] for k in ('operationId', 'owner', 'plan')}
                late = subprocess.run(['/usr/local/libexec/whaledeck-privileged', 'container-file-permissions', 'apply', uuid.uuid4().hex],
                    input=json.dumps(original_request) + '\n', capture_output=True, text=True, timeout=35)
                (directory / 'late-apply.stdout').write_text(late.stdout)
                (directory / 'late-apply.stderr').write_text(late.stderr)
                assert late.returncode == 0 and len(late.stdout.encode()) <= 32768 and len(late.stderr.encode()) <= 32768
                messages = [json.loads(line) for line in late.stdout.splitlines() if line.strip()]
                result = messages[-1]; assert result['operationId'] == record['operationId'] and result['state'] == 'Unknown' and result['modeConfirmed'] is None and result['replayed']
                assert (ROOT / (record['operationId'] + '.json')).read_bytes() == raw_root
                (directory / 'late-apply.json').write_text(json.dumps(result))
                prove_original(original, baseline, record); original_proved = True
                clean_success(waiting, waiting_baseline, profile, directory, other); waiting_proved = True
                logout(base, client); client.close(); client = None
            worker.start(); preflight()
            summary = {'revision': revision, 'id': identifier, 'originalSubject': baseline['originalSubject'], 'recoverySubject': profile['subject'],
                'modeConfirmed': None, 'rootState': 'Unknown', 'businessState': 'Acknowledged', 'executionJobState': 'Failed',
                'historyVerified': True, 'normalOriginalAndRecoveryLogout': True, 'originalIdentityDeletedBeforeRecovery': True,
                'actualOriginalAndRecoveryPages': True, 'productStoppedHelperSeconds': round(elapsed, 3), 'validatorKilledHelper': False,
                'lateOriginalApplyRemainsUnknown': True, 'rootSha256': hashlib.sha256(raw_root).hexdigest(),
                'otherWriteDeferredUntilRelease': True, 'waitingId': waiting['id'], 'originalCompleteBytesAndFingerprintUnchanged': True}
            (directory / 'verified.json').write_text(json.dumps(summary))
        finally:
            if held is not None: held.release()
            worker.start()
            for child in (owner_browser, recovery_browser):
                if child is not None and child.poll() is None: child.wait(timeout=235)
            registered = directory / 'owner-registered.json'
            if identifier is None and registered.exists(): identifier = json.loads(registered.read_bytes())['id']
            if identifier is not None and not original_proved:
                failed = wait_settled(identifier)
                if failed['row']['state'] == 'Unknown':
                    failure_acknowledge(identifier); failed = snapshot(identifier)
                    (directory / 'failure-fallback.json').write_text(json.dumps({'normalBffAcknowledgement': True, 'gatePassed': False}))
                row = failed['row']
                if row['execution_job_id']:
                    record = save_history(row, directory, 'failed-gate')
                    if record['state'] in ('Unknown', 'Aborted', 'Completed'): prove_original(original, baseline, record, require_unchanged=record['state'] != 'Completed')
            if waiting is not None and not waiting_proved:
                end = settle_waiting(waiting['id'])
                if end['row']['state'] == 'Succeeded': clean_success(waiting, waiting_baseline, profile, directory, other)
            if client is not None:
                try: logout(base, client)
                finally: client.close()
    assert not Path(baseline['source']).exists() and not Path(waiting_baseline['source']).exists()
    print(json.dumps({'permissionUnknown': 'passed', 'evidence': str(directory), **summary}), flush=True)


if __name__ == '__main__':
    try: main()
    except BaseException as error:
        import traceback
        print(json.dumps({'permissionUnknown': 'failed', 'phase': PHASE, 'failureType': type(error).__name__,
            'locations': [{'file': Path(f.filename).name, 'line': f.lineno} for f in traceback.extract_tb(error.__traceback__)]}), flush=True)
        raise SystemExit(1) from None
