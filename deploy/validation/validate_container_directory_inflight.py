#!/usr/bin/env python3
"""Installed cancellation/logout while a real directory helper is Prepared.

Only pause the exact helper proved by pidfd/root FD/operation lock. The product,
not this validator, must terminate it after cancellation or login revocation.
No arbitrary PID input, fake Root/SQL state, clock edits, or automatic rollback.
"""
from concurrent.futures import ThreadPoolExecutor
import datetime as dt
import json
import os
from pathlib import Path
import re
import select
import stat
import sys
import time
import uuid

import validate_container_file_publishing as owned
from bound_container_directory_cut import freeze_prepublication, record_for
from validate_container_directory_browser import ROOT, snapshot
from validate_container_directory_restart import Worker, agent_identity, clean_success, command, preflight
from validate_container_directory_unknown import acknowledge_if_unknown, register, wait_settled
from validate_container_upload_bff import login, logout, request
from validate_databases import Api
from validate_oidc import required_environment
from validate_metric_continuity import query

PHASE = 'startup'


def clean_aborted(fixture, row):
    receipt = record_for(row); assert receipt['state'] == 'Aborted' and not row['publish_barrier']
    assert row['completed_at_utc'] is not None and row['published_version'] is None
    assert not os.path.lexists(fixture['folder'] / row['relative_path'].removeprefix('work/'))
    private = fixture['folder'] / '.whaledeck-container-directories-v1'
    if private.exists():
        info = private.lstat()
        assert stat.S_ISDIR(info.st_mode) and info.st_uid == 0 and stat.S_IMODE(info.st_mode) == 0o700
        assert [info.st_dev, info.st_ino] == receipt['privateIdentity'] and not list(private.iterdir())
        private.rmdir()


def main():
    global PHASE
    assert os.geteuid() == 0 and (len(sys.argv) == 2 or len(sys.argv) == 3 and sys.argv[2] == '--restarts'); os.umask(0o077)
    revision = sys.argv[1]; assert re.fullmatch('[a-f0-9]{40}', revision)
    restarts = len(sys.argv) == 3
    base = os.environ.get('WHALEDECK_URL', 'http://192.168.100.13:8080').rstrip('/')
    username, password = required_environment('VALIDATION_USERNAME'), required_environment('VALIDATION_PASSWORD')
    evidence = Path(__file__).resolve().parents[1] / 'artifacts' / ('container-directory-inflight-' + uuid.uuid4().hex)
    evidence.mkdir(mode=0o700); results = []
    for kind in (('worker-crash', 'agent-restart') if restarts else ('cancel', 'logout')):
        PHASE = 'preflight-' + kind; preflight(); worker = Worker()
        client, profile = login(base, username, password); api = Api(base, client)
        assert request(client, 'GET', base + '/api/v1/system/status', 200).json()['build']['sourceRevision'] == revision
        intent = None; held = None; cleaned = False; logged_out = False
        with owned.fixture(restartable_core=('whaledeck-worker',)) as fixture:
            try:
                worker.stop()
                intent, baseline = register(base, client, api, fixture, 'work/进行中-' + kind)
                baseline.update(revision=revision, kind=kind, id=intent['id'], originalFileProof=fixture['proofs']['original.bin'])
                (evidence / (kind + '-baseline.json')).write_text(json.dumps(baseline))
                row = snapshot(intent['id'])['row']
                PHASE = 'real-prepared-helper-' + kind
                with ThreadPoolExecutor(max_workers=1) as observers:
                    watch = observers.submit(freeze_prepublication, row, fixture['folder'], baseline['containerPid'], 'Prepared')
                    worker.start(wait_ready=False); held = watch.result(timeout=25)
                assert held is not None, 'PREPARED_CUT_MISSED_NOT_COUNTED'
                applying = snapshot(intent['id'])['row']
                assert applying['state'] == 'Applying' and applying['publish_barrier'] and applying['root_started_at_utc'] is not None
                assert held.record['state'] == 'Prepared'
                (evidence / (kind + '-prepared-root.json')).write_text(json.dumps(held.record))
                (evidence / (kind + '-bound-driver.json')).write_text(json.dumps({'pid': held.pid, 'unit': held.cgroup, 'twoKernelFdProofs': True}))
                began = time.monotonic(); PHASE = 'product-must-stop-prepared-helper-' + kind
                service_proof = None; original_lease = None
                if kind == 'cancel':
                    request(client, 'POST', base + f'/api/v1/containers/{fixture["identifier"]}/files/directories/{intent["id"]}/cancel',
                        202, headers={**api.headers, 'Idempotency-Key': intent['id']})
                elif kind == 'logout':
                    logout(base, client); logged_out = True
                elif kind == 'worker-crash':
                    before = worker.current(); original_lease = applying['lease_expires_at_utc']
                    assert original_lease is not None
                    worker.stopped = True
                    command('docker', 'kill', '--signal', 'KILL', worker.id)
                    automatic = worker.current()['State']['Running']
                    worker.start(wait_ready=False)
                    after = worker.current(); assert after['State']['StartedAt'] != before['State']['StartedAt']
                    service_proof = {'workerId': worker.id, 'sameContainerAndImage': True, 'signal': 'SIGKILL',
                        'manuallyStarted': not automatic, 'originalLeaseExpiresAtUtc': original_lease}
                else:
                    isolated = query('''SELECT json_build_object(
                      'otherJobs',(SELECT count(*) FROM operation_jobs WHERE completed_at_utc IS NULL AND id<>:'id'::uuid),
                      'terminals',(SELECT count(*) FROM terminal_sessions WHERE state NOT IN ('Closed','Failed','Expired','Revoked')),
                      'hostLocks',(SELECT count(*) FROM file_path_locks),
                      'otherDirectoryBarriers',(SELECT count(*) FROM container_directory_operations WHERE publish_barrier AND id<>:'id'::uuid),
                      'uploadBarriers',(SELECT count(*) FROM container_file_upload_operations WHERE publish_barrier));''', id=intent['id'])
                    assert all(count == 0 for count in isolated.values())
                    transient = [path.name for path in Path('/run/systemd/transient').glob('whaledeck-*.service')]
                    assert set(transient) <= {held.cgroup}, 'OTHER_TRANSIENT_WORK_AGENT_RESTART_REFUSED'
                    before = command('systemctl', 'show', 'whaledeck-agent.service', '-p', 'MainPID', '--value')
                    maintenance = command('systemctl', 'show', 'whaledeck-maintenance.service', '-p', 'MainPID', '--value')
                    command('systemctl', 'restart', 'whaledeck-agent.service')
                    after = agent_identity(before, revision)
                    assert command('systemctl', 'show', 'whaledeck-maintenance.service', '-p', 'MainPID', '--value') == maintenance
                    service_proof = {'oldAgentPid': int(before), 'newAgentPid': int(after), 'maintenancePidUnchanged': True,
                        'otherActiveWork': isolated, 'onlyOwnedPreparedHelper': True}
                readable, _, _ = select.select([held.descriptor], [], [], max(0.01, 15 - (time.monotonic() - began)))
                assert readable, 'PRODUCT_DID_NOT_STOP_BOUND_HELPER_WITHIN_REVOCATION_WINDOW'
                elapsed = time.monotonic() - began; assert elapsed < 15
                held.release(); held = None
                if kind == 'worker-crash':
                    PHASE = 'restarted-worker-must-respect-original-natural-lease'
                    for _ in range(3):
                        waiting = snapshot(intent['id'])['row']
                        assert waiting['state'] == 'Applying' and waiting['publish_barrier']
                        assert waiting['lease_expires_at_utc'] == original_lease
                        assert dt.datetime.fromisoformat(original_lease) > dt.datetime.now(dt.timezone.utc)
                        assert record_for(waiting)['state'] == 'Prepared'
                        assert request(client, 'GET', base + '/api/v1/system/status', 200).json()['build']['sourceRevision'] == revision
                        time.sleep(1)
                    (evidence / 'worker-crash-lease-wait.json').write_text(json.dumps(service_proof))
                PHASE = 'natural-aborted-receipt-and-original-job-' + kind
                final = wait_settled(intent['id']); row = final['row']
                expected = 'Canceled' if kind == 'cancel' else 'Failed'
                assert row['state'] == final['job']['state'] == expected
                assert final['pending'] == 0 and final['accepted'] == 1 and row['lease_token'] is None and not row['publish_barrier']
                assert row['published_version'] is None and row['root_started_at_utc'] is not None
                if original_lease is not None:
                    assert dt.datetime.fromisoformat(row['completed_at_utc']) >= dt.datetime.fromisoformat(original_lease)
                receipt = record_for(row); assert receipt['state'] == 'Aborted'
                assert not os.path.lexists(fixture['folder'] / baseline['path'].removeprefix('work/'))
                raw = (ROOT / (uuid.UUID(intent['id']).hex + '.json')).read_bytes()
                (evidence / (kind + '-aborted-root.json')).write_bytes(raw)
                clean_aborted(fixture, row); cleaned = True
                result = {'kind': kind, 'id': intent['id'], 'state': row['state'], 'code': row['result_code'],
                    'rootCutState': 'Prepared', 'rootFinalState': 'Aborted', 'productStoppedHelperSeconds': round(elapsed, 3),
                    'validatorDidNotKillHelper': True, 'targetAbsent': True, 'serviceRestart': service_proof}
                results.append(result); (evidence / 'results.json').write_text(json.dumps({'revision': revision, 'cases': results}))
                if not logged_out: logout(base, client); logged_out = True
            finally:
                if held is not None: held.release()
                worker.start()
                if intent is not None and not cleaned:
                    final = wait_settled(intent['id']); row = final['row']
                    if row['state'] == 'Succeeded': clean_success(intent, baseline, profile, evidence, fixture)
                    elif row['state'] in ('Failed', 'Canceled') and record_for(row)['state'] == 'Aborted': clean_aborted(fixture, row)
                    elif row['state'] == 'Unknown':
                        # Explicit authorized fallback releases only the exact
                        # test barrier. It cannot make this failed gate pass.
                        fallback, _ = login(base, username, password)
                        try:
                            acknowledge_if_unknown(base, fallback, Api(base, fallback), intent['id'])
                            (evidence / (kind + '-fallback.json')).write_text(json.dumps({'realBffAcknowledgement': True, 'gatePassed': False}))
                        finally: logout(base, fallback); fallback.close()
                if not logged_out: logout(base, client)
                client.close()
    preflight()
    (evidence / 'verified.json').write_text(json.dumps({'revision': revision, 'cases': results, 'realPreparedCuts': 2,
        'noValidatorHelperKill': True, 'naturalRecoveryNotDatabaseOrClockEdits': True, 'restarts': restarts}))
    print(json.dumps({'installedContainerDirectoryInflight': 'passed', 'revision': revision, 'cases': results,
        'exactSourcesAndContainersCleaned': True, 'evidence': str(evidence)}), flush=True)


if __name__ == '__main__':
    try: main()
    except BaseException as error:
        import traceback
        print(json.dumps({'installedContainerDirectoryInflight': 'failed', 'phase': PHASE, 'failureType': type(error).__name__,
            'locations': [{'file': Path(frame.filename).name, 'line': frame.lineno} for frame in traceback.extract_tb(error.__traceback__)],
            'unverifiedDataPreserved': True}), file=sys.stderr)
        raise SystemExit(1) from None
