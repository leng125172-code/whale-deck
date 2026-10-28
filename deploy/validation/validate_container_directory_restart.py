#!/usr/bin/env python3
"""Real queued directory tasks across installed Worker/Agent restarts.

No database or clock edits. Only the existing Worker is explicitly stopped and
started; Agent is restarted only after proving no live terminal or file work.
Each write targets an exact no-network disposable container. Permanent business
and Root history stays; cleanup requires a proved, empty directory inode.
This is not an in-flight Root publication or Unknown-recovery gate.
"""
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
from validate_container_directory_browser import ROOT, snapshot, verify_directory
from validate_container_upload_bff import login, logout, request
from validate_container_upload_recovery_agent import trusted
from validate_databases import Api
from validate_metric_continuity import query
from validate_oidc import required_environment

PHASE = 'startup'


def command(*arguments):
    return subprocess.run(arguments, check=True, capture_output=True, text=True, timeout=60).stdout.strip()


def preflight(exempt=None):
    exempt = exempt or []
    assert all(str(uuid.UUID(value)) == value for value in exempt)
    value = query('''SELECT json_build_object(
      'otherJobs',(SELECT count(*) FROM operation_jobs WHERE completed_at_utc IS NULL AND id::text NOT IN
        (SELECT jsonb_array_elements_text(:'exempt'::jsonb))),
      'terminals',(SELECT count(*) FROM terminal_sessions WHERE state NOT IN ('Closed','Failed','Expired','Revoked')),
      'hostLocks',(SELECT count(*) FROM file_path_locks),
      'uploadBarriers',(SELECT count(*) FROM container_file_upload_operations WHERE publish_barrier),
      'directoryBarriers',(SELECT count(*) FROM container_directory_operations WHERE publish_barrier));''', exempt=json.dumps(exempt))
    assert all(count == 0 for count in value.values()), 'OTHER_ACTIVE_WORK_RESTART_REFUSED'
    return value


class Worker:
    def __init__(self):
        self.original = json.loads(command('docker', 'inspect', 'whaledeck-worker'))[0]
        self.id = self.original['Id']; self.stopped = False
        assert self.original['State']['Running'] and self.original['State']['Health']['Status'] == 'healthy'

    def current(self):
        value = json.loads(command('docker', 'inspect', self.id))[0]
        assert value['Id'] == self.id and value['Name'] == '/whaledeck-worker' and value['Image'] == self.original['Image']
        return value

    def stop(self):
        assert self.current()['State']['Running']
        self.stopped = True  # finally must repair even a lost stop response.
        command('docker', 'stop', '--time', '5', self.id)
        assert not self.current()['State']['Running']

    def start(self, wait_ready=True):
        assert type(wait_ready) is bool
        if not self.stopped: return
        if not self.current()['State']['Running']: command('docker', 'start', self.id)
        # A publication-cut observer must act while the short-lived Root
        # helper is paused, not wait for the next 30-second health probe.
        # Keep the restoration flag until a normal finally waits for health.
        if not wait_ready: return
        deadline = time.monotonic() + 90
        while True:
            value = self.current()
            assert value['State']['Running'], 'WORKER_DID_NOT_REMAIN_RUNNING'
            if value['State'].get('Health', {}).get('Status') == 'healthy': break
            assert time.monotonic() < deadline, 'WORKER_HEALTH_DEADLINE'
            time.sleep(1)
        assert value['State']['StartedAt'] != self.original['State']['StartedAt']
        self.stopped = False


def root_record(identifier):
    operation = uuid.UUID(identifier).hex; path = ROOT / (operation + '.json')
    info = trusted(path); assert stat.S_IMODE(info.st_mode) == 0o600 and info.st_size <= 16384
    return path, json.loads(path.read_bytes())


def wait_terminal(identifier, expected):
    deadline = time.monotonic() + 100
    while True:
        value = snapshot(identifier)
        if value['row']['state'] == expected: return value
        assert value['row']['state'] in ('Pending', 'Applying'), 'DIRECTORY_UNEXPECTED_TERMINAL_OR_UNKNOWN'
        assert time.monotonic() < deadline, 'DIRECTORY_TERMINAL_DEADLINE'
        time.sleep(1)


def agent_identity(before, revision):
    # Type=simple may report MainPID before exec. Observe that exact new
    # process until it really is the installed apphost; do not execute systemd
    # itself through the transient /proc/<pid>/exe link.
    deadline = time.monotonic() + 20; selected = None
    while True:
        pid = command('systemctl', 'show', 'whaledeck-agent.service', '-p', 'MainPID', '--value')
        if pid.isdigit() and int(pid) > 0 and pid != before:
            if selected is not None: assert pid == selected, 'AGENT_RESTARTED_AGAIN_DURING_PROBE'
            selected = pid
            try:
                if Path('/proc/' + pid + '/exe').readlink().name == 'WhaleDeck.Agent':
                    identity = json.loads(command('/proc/' + pid + '/exe', '--build-info'))
                    assert identity['sourceRevision'] == revision
                    return pid
            except (OSError, subprocess.CalledProcessError): pass
        assert time.monotonic() < deadline, 'AGENT_EXEC_IDENTITY_DEADLINE'
        time.sleep(0.1)


def clean_success(intent, baseline, profile, evidence, fixture):
    row = snapshot(intent['id'])['row']
    output, inode, private_inode = verify_directory({'id': intent['id'], 'path': baseline['path'],
        'publishedVersion': row['published_version']}, baseline, profile['subject'], evidence)
    private = fixture['folder'] / '.whaledeck-container-directories-v1'; private_info = trusted(private, True)
    assert [private_info.st_dev, private_info.st_ino] == private_inode and not list(private.iterdir())
    info = output.lstat(); assert (info.st_dev, info.st_ino, info.st_uid, info.st_gid, stat.S_IMODE(info.st_mode)) == inode
    assert not list(output.iterdir()); output.rmdir(); private.rmdir()


def main():
    global PHASE
    assert os.geteuid() == 0 and len(sys.argv) == 2; os.umask(0o077)
    revision = sys.argv[1]; assert re.fullmatch('[a-f0-9]{40}', revision)
    base = os.environ.get('WHALEDECK_URL', 'http://192.168.100.13:8080').rstrip('/')
    anonymous = requests.Session(); anonymous.trust_env = False
    assert request(anonymous, 'GET', base + '/api/v1/system/status', 200).json()['build']['sourceRevision'] == revision
    evidence = Path(__file__).resolve().parents[1] / 'artifacts' / ('container-directory-restart-' + uuid.uuid4().hex)
    evidence.mkdir(mode=0o700); cases = []
    session, profile = login(base, required_environment('VALIDATION_USERNAME'), required_environment('VALIDATION_PASSWORD'))
    api = Api(base, session)
    for kind in ('worker', 'agent-while-queued', 'cancel-while-queued', 'logout-while-queued'):
        PHASE = 'preflight-' + kind; baseline_checks = preflight(); worker = Worker()
        with owned.fixture(restartable_core=('whaledeck-worker',)) as fixture:
            inspect = json.loads(command('docker', 'inspect', fixture['identifier']))[0]
            instance = str(inspect['State']['Pid']) + ':' + inspect['State']['StartedAt']
            path = 'work/排队重启-' + kind
            endpoint = base + f'/api/v1/containers/{fixture["identifier"]}/files/directories'
            planned = request(session, 'POST', endpoint + '/plan', 200, headers=api.headers,
                json={'instanceId': instance, 'path': path, 'mode': '0750'}).json()
            baseline = {'revision': revision, 'containerId': fixture['identifier'], 'instanceId': instance,
                'source': str(fixture['folder']), 'path': path, 'kind': kind, 'preflight': baseline_checks,
                'sourceIdentity': list(owned.common.fingerprint(fixture['folder'])), 'originalProof': fixture['proofs']['original.bin']}
            key = str(uuid.uuid4()); parameters = {'planHash': planned['planHash'], 'confirmed': True, 'idempotencyKey': key}
            headers = {**api.headers, 'Idempotency-Key': key}; intent = None; agent_pids = None; cleaned = False
            try:
                PHASE = 'stopped-worker-durable-registration-' + kind; worker.stop()
                intent = request(session, 'POST', endpoint, 202, headers=headers, json=parameters).json()
                assert intent['state'] == 'Pending' and intent['jobId'] == intent['id']
                again = request(session, 'POST', endpoint, 202, headers=headers, json=parameters).json()
                assert again['id'] == intent['id'] and again['expiresAtUtc'] == intent['expiresAtUtc']
                baseline['id'] = intent['id']; (evidence / (kind + '-baseline.json')).write_text(json.dumps(baseline))
                for _ in range(3):
                    pending = snapshot(intent['id']); row = pending['row']
                    assert row['state'] == 'Pending' and row['version'] == 0 and row['root_started_at_utc'] is None
                    assert row['lease_token'] is None and not row['publish_barrier'] and pending['accepted'] == 1
                    assert not (ROOT / (uuid.UUID(intent['id']).hex + '.json')).exists()
                    assert not (fixture['folder'] / path.removeprefix('work/')).exists()
                    assert request(anonymous, 'GET', base + '/api/v1/system/status', 200).json()['build']['sourceRevision'] == revision
                    time.sleep(1)
                if kind == 'agent-while-queued':
                    PHASE = 'real-agent-restart-before-directory-apply'; preflight([intent['id']])
                    maintenance = command('systemctl', 'show', 'whaledeck-maintenance.service', '-p', 'MainPID', '--value')
                    before = command('systemctl', 'show', 'whaledeck-agent.service', '-p', 'MainPID', '--value')
                    assert before.isdigit() and int(before) > 0
                    command('systemctl', 'restart', 'whaledeck-agent.service')
                    after = agent_identity(before, revision)
                    assert command('systemctl', 'show', 'whaledeck-maintenance.service', '-p', 'MainPID', '--value') == maintenance
                    agent_pids = {'before': int(before), 'after': int(after), 'maintenancePidUnchanged': True}
                elif kind == 'cancel-while-queued':
                    request(session, 'POST', endpoint + '/' + intent['id'] + '/cancel', 202,
                        headers={**api.headers, 'Idempotency-Key': intent['id']})
                elif kind == 'logout-while-queued':
                    logout(base, session)
                PHASE = 'actual-worker-start-and-original-intent-' + kind; worker.start()
                expected = 'Canceled' if kind in ('cancel-while-queued', 'logout-while-queued') else 'Succeeded'
                value = wait_terminal(intent['id'], expected); row = value['row']
                assert value['job']['state'] == expected and value['pending'] == 0 and value['accepted'] == 1
                assert row['lease_token'] is None and row['lease_expires_at_utc'] is None and not row['publish_barrier']
                receipt_path, receipt = root_record(intent['id'])
                if expected == 'Succeeded':
                    clean_success(intent, baseline, profile, evidence, fixture)
                else:
                    assert row['root_started_at_utc'] is None and row['published_version'] is None
                    assert row['result_code'] == 'CONTAINER_DIRECTORY_CANCELED' and receipt['state'] == 'Aborted'
                    assert receipt['operationId'] == uuid.UUID(intent['id']).hex and receipt['owner'] == row['owner_hash']
                    assert receipt['plan']['fingerprint'] == json.loads(row['plan_json'])['Fingerprint']
                    assert not (fixture['folder'] / path.removeprefix('work/')).exists()
                    (evidence / (uuid.UUID(intent['id']).hex + '.root.json')).write_bytes(receipt_path.read_bytes())
                cleaned = True
                assert request(anonymous, 'GET', base + '/api/v1/system/status', 200).json()['build']['sourceRevision'] == revision
                cases.append({'kind': kind, 'id': intent['id'], 'state': expected, 'rootState': receipt['state'],
                    'workerContainerIdUnchanged': True, 'agentRestart': agent_pids})
                (evidence / 'results.json').write_text(json.dumps({'revision': revision, 'cases': cases}))
            finally:
                worker.start()  # Never leave the real Worker manually stopped.
                if intent is not None and not cleaned:
                    # An observer failure must not race fixture teardown with
                    # the already accepted task. Query it; never submit again.
                    deadline = time.monotonic() + 100
                    while True:
                        value = snapshot(intent['id']); state = value['row']['state']
                        if state not in ('Pending', 'Applying'): break
                        assert time.monotonic() < deadline, 'FAILED_GATE_ORIGINAL_TASK_STILL_ACTIVE'
                        time.sleep(1)
                    if state == 'Succeeded': clean_success(intent, baseline, profile, evidence, fixture)
    session.close(); anonymous.close(); preflight()
    (evidence / 'verified.json').write_text(json.dumps({'revision': revision, 'cases': cases, 'pendingOnly': True,
        'realWorkerRestarts': 4, 'realAgentRestarts': 1, 'businessAndRootHistoryRetained': True}))
    print(json.dumps({'installedContainerDirectoryRestart': 'passed', 'revision': revision, 'cases': cases,
        'ownedSourcesAndContainersCleaned': True, 'evidence': str(evidence)}), flush=True)


if __name__ == '__main__':
    try: main()
    except BaseException as error:
        import traceback
        print(json.dumps({'installedContainerDirectoryRestart': 'failed', 'phase': PHASE, 'failureType': type(error).__name__,
            'locations': [{'file': Path(frame.filename).name, 'line': frame.lineno} for frame in traceback.extract_tb(error.__traceback__)],
            'unverifiedDataPreserved': True}), file=sys.stderr)
        raise SystemExit(1) from None
