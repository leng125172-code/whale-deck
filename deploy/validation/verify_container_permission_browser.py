#!/usr/bin/env python3
"""Read-only, separate-process SQL/Root/HDD and exact cleanup verification.

The parent read full physical bytes before cleanup. This observer cannot prove
deleted bytes, create identities, write SQL, operate containers or remove files.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import uuid

import requests

from run_with_ephemeral_admin import AUTHENTIK_API, container_environment, require
from validate_container_directory_restart import preflight
from validate_container_file_publishing import common
from validate_container_file_staging import load
from validate_container_permission_browser import ROOT, snapshot, digest
from validate_container_upload_recovery_agent import trusted
from validate_metric_continuity import query


def verify(directory):
    repo = Path(__file__).resolve().parents[2]
    assert directory.parent == repo / 'deploy/artifacts'
    assert re.fullmatch('container-permission-browser-[a-f0-9]{32}', directory.name)
    trusted(directory, True)
    baseline = json.loads((directory / 'baseline.json').read_bytes())
    results = json.loads((directory / 'browser.json').read_bytes())
    accepted = json.loads((directory / 'verified.json').read_bytes())
    assert baseline['revision'] == results['revision'] == accepted['revision']
    assert results['actualUi'] and results['normalDualLogout']
    assert results['registrations'] == results['registrationRetries'] == 6
    assert results['confirmations'] == results['confirmationRetries'] == 5 and results['contentUploads'] == 0
    expected_sha = hashlib.sha256(hashlib.shake_256(('owned-container-original:' + baseline['containerName']).encode()).digest(common.SIZE)).hexdigest().upper()
    assert baseline['sha256'] == expected_sha and baseline['bytes'] == common.SIZE
    assert {item['name'] for item in results['completed']} == {'file-mode', 'file-noop', 'file-restore', 'directory-mode', 'directory-restore', 'file-cancel'}
    files = load(Path('/usr/local/libexec/whaledeck-files.py'), 'permission_independent_files')
    storage = load(Path('/usr/local/libexec/whaledeck-file-backups.py'), 'permission_independent_hdd').FileBackups(files)
    records = []
    with storage.target(create_namespace=False, read_only=True) as (configuration, base):
        hdd = Path(os.readlink('/proc/self/fd/' + str(base))) / 'container-permissions-v1'
        trusted(hdd, True)
        for item in results['completed']:
            state = snapshot(item['id']); row = state['row']
            assert state == json.loads((directory / (item['name'] + '.sql.json')).read_bytes())
            assert row['actor_subject'] == results['subject'] and row['container_id'] == baseline['containerId'] and row['instance_id'] == baseline['instanceId']
            assert row['completed_at_utc'] and row['lease_token'] is None and row['lease_expires_at_utc'] is None and not row['publish_barrier']
            assert state['pending'] == 0 and state['accepted'] == 1 and state['planJob']['state'] == 'Succeeded'
            assert item['confirmation'] is None
            if item['name'] == 'file-cancel':
                assert row['state'] == 'Canceled' and row['execution_job_id'] is None and state['executionJob'] is None
                assert state['confirmed'] == 0 and row['root_started_at_utc'] is None
                assert row['mode_confirmed'] is None and not row['history_may_exist'] and not row['history_verified'] and row['history_digest'] is None
                continue
            assert row['state'] == state['executionJob']['state'] == 'Succeeded' and state['confirmed'] == 1
            assert row['mode_confirmed'] and row['history_may_exist'] and row['history_verified']
            op = uuid.UUID(row['execution_job_id']).hex; path = ROOT / (op + '.json'); trusted(path)
            raw = path.read_bytes(); record = json.loads(raw)
            assert raw == (directory / (item['name'] + '.root.json')).read_bytes()
            assert record['operationId'] == op and record['owner'] == row['owner_hash'] and record['state'] == 'Completed'
            assert record['modeConfirmed'] and record['historyMayExist'] and record['historyVerified']
            plan = record['plan']; managed = json.loads(row['plan_json'])
            assert plan['fingerprint'] == managed['Fingerprint'] == digest(b'whaledeck-container-permission-plan-v1\n', {k: v for k, v in plan.items() if k != 'fingerprint'})
            assert plan['command']['mode'] == item['command']['mode'] and plan['command']['path'] == row['relative_path']
            assert [plan['uid'], plan['gid']] == ([2100, 3100] if item['kind'] == 'Directory' else [1000, 1000])
            history = hdd / (op + '.json'); trusted(history)
            descriptor = os.open(history, os.O_RDONLY | os.O_NOFOLLOW)
            try:
                storage.attest(configuration, descriptor, read_only=True)
            finally:
                os.close(descriptor)
            saved = json.loads(history.read_bytes())
            assert history.read_bytes() == (directory / (item['name'] + '.hdd.json')).read_bytes()
            assert saved['operationId'] == op and saved['owner'] == row['owner_hash'] and saved['plan'] == plan
            assert saved['historyDigest'] == row['history_digest'] == record['historyDigest'] == digest(b'whaledeck-container-permission-history-v1\n', {k: v for k, v in saved.items() if k != 'historyDigest'})
            assert record['version'] == row['published_version'] == item['publishedVersion']
            records.append({'id': row['id'], 'execution': row['execution_job_id'], 'rootSha256': hashlib.sha256(raw).hexdigest()})
    assert len(records) == 5 and len({item['id'] for item in records}) == 5
    source = Path(baseline['source'])
    assert source.parent == common.APPS and source.name == baseline['containerName'] and not source.exists()
    missing = subprocess.run(['docker', 'inspect', baseline['containerId']], capture_output=True, timeout=20)
    assert missing.returncode != 0 and b'no such' in missing.stderr.lower()
    preflight()
    active = query("SELECT json_build_object('active',count(*) FILTER(WHERE completed_at_utc IS NULL),'barriers',count(*) FILTER(WHERE publish_barrier),'leases',count(*) FILTER(WHERE lease_token IS NOT NULL)) FROM container_permission_intents;")
    assert all(n == 0 for n in active.values())
    current = common.core_snapshot()
    assert len(current) == 11 and json.loads(json.dumps(current)) == baseline['coreBefore']
    api = requests.Session(); api.trust_env = False
    api.headers['Authorization'] = 'Bearer ' + require(container_environment(), 'Authentication__Authentik__ApiToken')
    try:
        for prefix in ('whaledeck-validation-', 'whaledeck-upload-qa-'):
            users = api.get(AUTHENTIK_API + 'core/users/', params={'search': prefix, 'page_size': 100}, timeout=20)
            assert users.status_code == 200 and not users.json().get('pagination', {}).get('next')
            assert not any(str(user['username']).startswith(prefix) for user in users.json()['results'])
    finally:
        api.close()
    return {'readOnlyVerifier': True, 'revision': baseline['revision'], 'permissions': records, 'canceled': 1,
        'exactSourceAndContainerAbsent': True, 'temporaryIdentitiesAbsent': True, 'activePermissionWork': active,
        'coreUnchanged': 11, 'physicalBytesVerifiedByParentBeforeCleanup': True}


if __name__ == '__main__':
    try:
        assert os.geteuid() == 0 and len(sys.argv) == 2
        print(json.dumps(verify(Path(sys.argv[1]))))
    except Exception as error:
        print(json.dumps({'permissionIndependentVerification': 'failed', 'failureType': type(error).__name__}))
        raise SystemExit(1) from None
