#!/usr/bin/env python3
"""Installed real-page ordinary chmod cancellation and process recovery gate.

Only exact disposable resources and pidfd-bound helpers are used. No SQL/Root
edits, injected browser sessions, product hooks, arbitrary PID or clock edits.
"""
from concurrent.futures import ThreadPoolExecutor
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import sys
import time
import uuid

import validate_container_file_publishing as owned
from bound_container_permission_cut import ROOT, freeze_before_mode, record_for, fingerprint
from validate_container_permission_browser import snapshot
from validate_container_directory_restart import Worker, command, preflight, agent_identity
from validate_container_upload_bff import login, logout, request, temporary_user
from validate_container_file_staging import load
from validate_databases import Api
from validate_metric_continuity import query

PHASE = 'startup'


def write_signal(directory, name, value):
    assert re.fullmatch(r'[a-z-]+\.json', name)
    path = directory / name; pending = path.with_name(path.name + '.pending')
    with pending.open('x') as out:
        json.dump(value, out); out.flush(); os.fsync(out.fileno())
    pending.replace(path)


def wait_signal(directory, name, browser, timeout=80):
    assert re.fullmatch(r'[a-z-]+\.json', name)
    until = time.monotonic() + timeout
    while time.monotonic() < until:
        path = directory / name
        if path.exists():
            assert not path.is_symlink() and path.stat().st_size <= 32768
            return json.loads(path.read_bytes())
        if browser.poll() is not None: raise AssertionError('ACTUAL_PERMISSION_BROWSER_EXITED_BEFORE_SIGNAL')
        if (directory / 'browser-failed.json').exists(): raise AssertionError('ACTUAL_PERMISSION_BROWSER_REPORTED_FAILURE')
        time.sleep(0.04)
    raise AssertionError('PERMISSION_BROWSER_SIGNAL_DEADLINE')


def wait_settled(identifier):
    until = time.monotonic() + 145
    while True:
        value = snapshot(identifier)
        if value['row']['state'] in ('Succeeded', 'Superseded', 'Failed', 'Canceled', 'Unknown', 'Acknowledged'): return value
        assert time.monotonic() < until, 'PERMISSION_TERMINAL_DEADLINE'
        time.sleep(0.5)


def save_history(row, directory, label):
    record = record_for(row); raw = (ROOT / (uuid.UUID(row['execution_job_id']).hex + '.json')).read_bytes()
    (directory / (label + '-root.json')).write_bytes(raw)
    assert record['modeConfirmed'] is (False if record['state'] == 'Aborted' else True if record['state'] == 'Completed' else None)
    assert record['historyMayExist'] == row['history_may_exist'] and record['historyVerified'] == row['history_verified']
    if record['historyVerified']:
        files = load(Path('/usr/local/libexec/whaledeck-files.py'), 'permission_fault_history_files')
        storage = load(Path('/usr/local/libexec/whaledeck-file-backups.py'), 'permission_fault_history_hdd').FileBackups(files)
        with storage.target(create_namespace=False, read_only=True) as (configuration, base):
            target = Path(os.readlink('/proc/self/fd/' + str(base))) / 'container-permissions-v1' / (record['operationId'] + '.json')
            descriptor = os.open(target, os.O_RDONLY | os.O_NOFOLLOW)
            try: storage.attest(configuration, descriptor, read_only=True)
            finally: os.close(descriptor)
            assert target.stat().st_uid == 0 and stat.S_IMODE(target.stat().st_mode) == 0o600
            raw_history = target.read_bytes(); history = json.loads(raw_history)
            assert history['plan'] == record['plan'] and history['operationId'] == record['operationId'] and history['owner'] == row['owner_hash']
            digest = hashlib.sha256(b'whaledeck-container-permission-history-v1\n' + json.dumps({k: v for k, v in history.items() if k != 'historyDigest'},
                ensure_ascii=False, sort_keys=True, separators=(',', ':'), allow_nan=False).encode()).hexdigest().upper()
            assert digest == history['historyDigest'] == record['historyDigest'] == row['history_digest']
            (directory / (label + '-hdd.json')).write_bytes(raw_history)
    return record


def prove_original(fixture, baseline, record, *, require_unchanged=True):
    source = fixture['folder'] / 'original.bin'
    assert source.parent == fixture['folder'] and not source.is_symlink()
    assert fingerprint(source.stat()) == (baseline['original'] if require_unchanged else record['actualIdentity'])
    assert hashlib.sha256(source.read_bytes()).hexdigest().upper() == baseline['sha256'] and source.stat().st_size == baseline['bytes']
    assert {p.name for p in fixture['folder'].iterdir()} == {'original.bin'}
    fixture['proofs']['original.bin'] = (owned.common.fingerprint(source), baseline['sha256'])


def agent_restart_allowed(held, row):
    checks = preflight([row['id'], row['execution_job_id']])
    others = query("""SELECT json_build_object('permission',(SELECT count(*) FROM container_permission_intents WHERE publish_barrier AND id<>:'id'::uuid),
      'copies',(SELECT count(*) FROM container_copy_intents WHERE publish_barrier),'moves',(SELECT count(*) FROM container_move_intents WHERE publish_barrier));""", id=row['id'])
    assert all(n == 0 for n in others.values())
    active = []
    for path in Path('/run/systemd/transient').glob('whaledeck-*.service'):
        props = dict(line.split('=', 1) for line in command('systemctl', 'show', path.name, '-p', 'ActiveState', '-p', 'MainPID', '-p', 'ControlPID', '-p', 'ControlGroup').splitlines())
        if props['ActiveState'] in ('inactive', 'failed'):
            assert props['MainPID'] == props['ControlPID'] == '0'
            if props['ControlGroup']:
                processes = Path('/sys/fs/cgroup') / props['ControlGroup'].lstrip('/') / 'cgroup.procs'
                assert not processes.exists() or not processes.read_text().split()
        else: active.append(path.name)
    assert set(active) <= {held.cgroup}, 'OTHER_TRANSIENT_WORK_AGENT_RESTART_REFUSED'
    return {**checks, **others}


def failure_acknowledge(identifier):
    """Exact ordinary BFF recovery only, never convert a failed gate to passed."""
    with temporary_user(True) as (name, password):
        session, _ = login('http://127.0.0.1:8080', name, password)
        try:
            api = Api('http://127.0.0.1:8080', session)
            url = 'http://127.0.0.1:8080/api/v1/files/container-permissions/recovery/' + identifier
            value = request(session, 'GET', url, 200).json()
            if value['state'] == 'Unknown':
                request(session, 'POST', url + '/acknowledge', 202, headers={**api.headers, 'Idempotency-Key': identifier}, json={'version': value['version'], 'confirmed': True})
            logout('http://127.0.0.1:8080', session)
        finally: session.close()


def run_case(kind, revision, evidence):
    global PHASE
    PHASE = kind + ':preflight'; preflight(); worker = Worker()
    directory = evidence / kind; directory.mkdir(mode=0o700)
    baseline = None; identifier = None; held = None; browser = None; proved = False
    with owned.fixture(restartable_core=('whaledeck-worker',)) as fixture, temporary_user(True) as (username, password):
        source = fixture['folder'] / 'original.bin'
        actual = json.loads(command('docker', 'inspect', fixture['identifier']))[0]
        baseline = {'revision': revision, 'kind': kind, 'containerId': fixture['identifier'], 'containerName': actual['Name'].lstrip('/'),
            'instanceId': str(actual['State']['Pid']) + ':' + actual['State']['StartedAt'], 'containerPid': actual['State']['Pid'],
            'source': str(fixture['folder']), 'original': fingerprint(source.stat()), 'sha256': owned.common.file_digest(source),
            'bytes': source.stat().st_size, 'coreBefore': owned.common.core_snapshot(), 'evidence': str(directory)}
        (directory / 'baseline.json').write_text(json.dumps(baseline))
        try:
            PHASE = kind + ':normal-visible-preflight'
            with (directory / 'browser.stdout').open('wb') as stdout, (directory / 'browser.stderr').open('wb') as stderr:
                browser = subprocess.Popen(['node', str(Path(__file__).with_suffix('.mjs')), revision], stdin=subprocess.PIPE,
                    stdout=stdout, stderr=stderr, env={**os.environ, 'VALIDATION_USERNAME': username, 'VALIDATION_PASSWORD': password})
                browser.stdin.write(json.dumps(baseline).encode()); browser.stdin.close(); browser.stdin = None
                ready = wait_signal(directory, 'ready.json', browser); identifier = ready['id']
                assert snapshot(identifier)['row']['state'] == 'Ready'
                worker.stop(); write_signal(directory, 'confirm.json', {'workerStopped': True})
                accepted = wait_signal(directory, 'confirmed.json', browser, 30)
                row = snapshot(identifier)['row']; assert row['state'] == 'Pending' and row['root_started_at_utc'] is None
                assert row['execution_job_id'] == accepted['executionJobId'] and row['actor_subject'] == ready['subject']
                PHASE = kind + ':exact-installed-helper-before-mode'
                with ThreadPoolExecutor(max_workers=1) as observers:
                    watching = observers.submit(freeze_before_mode, row, fixture['folder'], baseline['containerPid'], 'HistoryVerified')
                    worker.start(wait_ready=False); held = watching.result(timeout=28)
                assert held is not None, 'REAL_PERMISSION_CUT_MISSED_NOT_COUNTED'
                applying = snapshot(identifier)['row']
                assert applying['state'] == 'Applying' and applying['root_started_at_utc'] and applying['publish_barrier'] and held.record['historyVerified']
                (directory / 'cut-root.json').write_text(json.dumps(held.record))
                (directory / 'bound-helper.json').write_text(json.dumps({'pid': held.pid, 'unit': held.cgroup, 'twoKernelFdProofs': True, 'proof': held.proof}))
                restart = None; lease = None; elapsed = None
                PHASE = kind + ':real-action-and-product-helper-exit'
                # Observe exit concurrently with actual page actions and service drain.
                if kind in ('cancel', 'logout'):
                    began = time.monotonic()
                    with ThreadPoolExecutor(max_workers=1) as exits:
                        observing = exits.submit(held.wait_exit_seconds, began, 15)
                        write_signal(directory, 'action.json', {'kind': kind})
                        action = wait_signal(directory, 'action-done.json', browser, 30)
                        elapsed = observing.result(timeout=16)
                else:
                    write_signal(directory, 'action.json', {'kind': kind})
                    action = wait_signal(directory, 'action-done.json', browser, 30)
                    if kind == 'agent-restart': checks = agent_restart_allowed(held, applying)
                    began = time.monotonic()
                    with ThreadPoolExecutor(max_workers=1) as exits:
                        observing = exits.submit(held.wait_exit_seconds, began, 15)
                        if kind == 'worker-crash':
                            before = worker.current(); lease = applying['lease_expires_at_utc']; assert lease
                            worker.stopped = True; command('docker', 'kill', '--signal', 'KILL', worker.id)
                            automatic = worker.current()['State']['Running']; worker.start(wait_ready=False)
                            assert worker.current()['State']['StartedAt'] != before['State']['StartedAt']
                            restart = {'sameWorkerContainerAndImage': True, 'workerId': worker.id, 'signal': 'SIGKILL', 'manuallyStarted': not automatic, 'originalLeaseExpiresAtUtc': lease}
                        else:
                            before = command('systemctl', 'show', 'whaledeck-agent.service', '-p', 'MainPID', '--value')
                            maintenance = command('systemctl', 'show', 'whaledeck-maintenance.service', '-p', 'MainPID', '--value')
                            command('systemctl', 'restart', 'whaledeck-agent.service')
                            after = agent_identity(before, revision)
                            assert command('systemctl', 'show', 'whaledeck-maintenance.service', '-p', 'MainPID', '--value') == maintenance
                            restart = {'oldAgentPid': before, 'newAgentPid': after, 'maintenancePidUnchanged': True, 'otherActiveWork': checks, 'totalRestartSeconds': round(time.monotonic() - began, 3)}
                        elapsed = observing.result(timeout=16)
                assert elapsed is not None and elapsed < 15, 'PRODUCT_DID_NOT_STOP_BOUND_PERMISSION_HELPER'
                held.release(); held = None
                if lease:
                    PHASE = kind + ':original-natural-lease'
                    samples = []
                    for _ in range(3):
                        waiting = snapshot(identifier)['row']
                        assert waiting['state'] == 'Applying' and waiting['publish_barrier'] and waiting['lease_expires_at_utc'] == lease
                        assert dt.datetime.fromisoformat(lease) > dt.datetime.now(dt.timezone.utc)
                        assert record_for(waiting)['state'] in ('Preparing', 'Prepared')
                        samples.append({'state': waiting['state'], 'lease': lease}); time.sleep(1)
                    (directory / 'natural-lease.json').write_text(json.dumps(samples))
                PHASE = kind + ':original-business-and-root-terminal'
                final = wait_settled(identifier); row = final['row']; expected = 'Canceled' if kind in ('cancel', 'logout') else 'Failed'
                assert row['state'] == final['executionJob']['state'] == expected and final['planJob']['state'] == 'Succeeded'
                assert row['root_started_at_utc'] and row['completed_at_utc'] and not row['publish_barrier'] and row['lease_token'] is None and row['lease_expires_at_utc'] is None
                assert row['mode_confirmed'] is False and row['history_verified'] and row['history_may_exist'] and row['published_version'] is None
                assert final['accepted'] == final['confirmed'] == 1 and final['pending'] == 0
                if lease: assert dt.datetime.fromisoformat(row['completed_at_utc']) >= dt.datetime.fromisoformat(lease)
                record = save_history(row, directory, 'final'); assert record['state'] == 'Aborted'
                prove_original(fixture, baseline, record)
                (directory / 'final-sql.json').write_text(json.dumps(final))
                write_signal(directory, 'settled.json', {'id': identifier, 'state': expected})
                assert browser.wait(timeout=60) == 0, 'ACTUAL_PERMISSION_TERMINAL_PAGE_NOT_VERIFIED'
                ui = json.loads((directory / 'browser.json').read_bytes()); assert ui['actualUi'] and ui['normalDualLogout'] and ui['id'] == identifier
                assert ui['registrations'] == ui['confirmations'] == 1 and ui['cancellations'] == (1 if kind == 'cancel' else 0) and ui['bodies'] == 0
                result = {'kind': kind, 'id': identifier, 'executionJobId': row['execution_job_id'], 'state': expected, 'code': row['result_code'],
                    'rootState': 'Aborted', 'rootCutState': json.loads((directory / 'cut-root.json').read_bytes())['state'],
                    'productStoppedHelperSeconds': round(elapsed, 3), 'validatorKilledHelper': False, 'originalFullBytesAndFingerprintUnchanged': True,
                    'actualUi': True, 'serviceRestart': restart}
                (directory / 'verified.json').write_text(json.dumps(result)); proved = True
        finally:
            if held is not None: held.release()
            worker.start()
            if browser is not None and browser.poll() is None:
                # Allow the browser's own timeout/cancellation cleanup first.
                browser.wait(timeout=235)
            registered = directory / 'registered.json'
            if identifier is None and registered.exists(): identifier = json.loads(registered.read_bytes())['id']
            if identifier is not None and not proved:
                failed = wait_settled(identifier); row = failed['row']
                if row['state'] == 'Unknown':
                    failure_acknowledge(identifier); failed = snapshot(identifier); row = failed['row']
                    (directory / 'fallback.json').write_text(json.dumps({'normalBffAcknowledgement': True, 'gatePassed': False}))
                if row['execution_job_id']:
                    record = save_history(row, directory, 'failed-gate')
                    if record['state'] in ('Aborted', 'Completed'):
                        prove_original(fixture, baseline, record, require_unchanged=record['state'] == 'Aborted')
                (directory / 'failed-gate-sql.json').write_text(json.dumps(failed))
    assert not Path(baseline['source']).exists()
    assert subprocess.run(['docker', 'inspect', baseline['containerId']], capture_output=True, timeout=10).returncode != 0
    return result


def main():
    assert os.geteuid() == 0 and len(sys.argv) in (2, 3); os.umask(0o077)
    revision = sys.argv[1]; assert re.fullmatch('[a-f0-9]{40}', revision)
    kinds = ('cancel', 'logout', 'worker-crash', 'agent-restart')
    if len(sys.argv) == 3: assert sys.argv[2] in kinds; kinds = (sys.argv[2],)
    evidence = Path(__file__).resolve().parents[1] / 'artifacts' / ('container-permission-faults-' + uuid.uuid4().hex)
    evidence.mkdir(mode=0o700); results = []
    print(json.dumps({'permissionFaults': 'started', 'evidence': str(evidence), 'kinds': kinds}), flush=True)
    for kind in kinds:
        result = run_case(kind, revision, evidence); results.append(result)
        (evidence / 'results.json').write_text(json.dumps(results)); print(json.dumps({'permissionFaults': 'case-passed', **result}), flush=True)
    preflight()
    totals = query("SELECT json_build_object('active',count(*) FILTER(WHERE completed_at_utc IS NULL),'barriers',count(*) FILTER(WHERE publish_barrier),'leases',count(*) FILTER(WHERE lease_token IS NOT NULL)) FROM container_permission_intents;")
    assert all(n == 0 for n in totals.values())
    (evidence / 'verified.json').write_text(json.dumps({'revision': revision, 'cases': results, 'permissionTotals': totals, 'coreHealthy': len(owned.common.core_snapshot())}))
    print(json.dumps({'permissionFaults': 'passed', 'evidence': str(evidence), 'cases': len(results)}), flush=True)


if __name__ == '__main__':
    try: main()
    except BaseException as error:
        import traceback
        print(json.dumps({'permissionFaults': 'failed', 'phase': PHASE, 'failureType': type(error).__name__,
            'locations': [{'file': Path(f.filename).name, 'line': f.lineno} for f in traceback.extract_tb(error.__traceback__)]}), flush=True)
        raise SystemExit(1) from None
