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
from validate_container_ownership_browser import ROOT, snapshot, digest
from validate_container_upload_recovery_agent import trusted
from validate_metric_continuity import query


def verify(directory):
    repo = Path(__file__).resolve().parents[2]
    assert directory.parent == repo / 'deploy/artifacts'
    assert re.fullmatch('container-ownership-browser-[a-f0-9]{32}', directory.name)
    trusted(directory, True)
    baseline = json.loads((directory / 'baseline.json').read_bytes())
    results = json.loads((directory / 'browser.json').read_bytes())
    accepted = json.loads((directory / 'verified.json').read_bytes())
    assert baseline['revision'] == results['revision'] == accepted['revision']
    assert re.fullmatch('[a-f0-9]{40}', baseline['validationSourceRevision'])
    for relative, sha in baseline['validationSourceSha256'].items():
        assert relative in {'deploy/validation/validate_container_ownership_browser.py','deploy/validation/validate_container_ownership_browser.mjs','deploy/validation/verify_container_ownership_browser.py'}
        frozen = subprocess.check_output(['git','-C',str(repo),'show',baseline['validationSourceRevision']+':'+relative])
        assert hashlib.sha256(frozen).hexdigest() == sha

    assert results['actualUi'] and results['normalDualLogout']
    assert results['registrations'] == results['registrationRetries'] == 7
    assert results['confirmations'] == results['confirmationRetries'] == 6 and results['contentUploads'] == 0
    expected_sha = hashlib.sha256(hashlib.shake_256(('owned-container-original:' + baseline['containerName']).encode()).digest(common.SIZE)).hexdigest().upper()
    assert baseline['sha256'] == expected_sha and baseline['bytes'] == common.SIZE
    assert {item['name'] for item in results['completed']} == {'file-owner', 'file-noop', 'file-root', 'file-restore', 'directory-owner', 'directory-restore', 'file-cancel'}
    files = load(Path('/usr/local/libexec/whaledeck-files.py'), 'ownership_independent_files')
    storage = load(Path('/usr/local/libexec/whaledeck-file-backups.py'), 'ownership_independent_hdd').FileBackups(files)
    records = []
    with storage.target(create_namespace=False, read_only=True) as (configuration, base):
        hdd = Path(os.readlink('/proc/self/fd/' + str(base))) / 'container-ownership-v1'
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
                assert row['owner_confirmed'] is None and not row['history_may_exist'] and not row['history_verified'] and row['history_digest'] is None
                continue
            assert row['state'] == state['executionJob']['state'] == 'Succeeded' and state['confirmed'] == 1
            assert row['owner_confirmed'] and row['history_may_exist'] and row['history_verified']
            op = uuid.UUID(row['execution_job_id']).hex; path = ROOT / (op + '.json'); trusted(path)
            raw = path.read_bytes(); record = json.loads(raw)
            assert raw == (directory / (item['name'] + '.root.json')).read_bytes()
            assert record['operationId'] == op and record['owner'] == row['owner_hash'] and record['state'] == 'Completed'
            assert record['ownerConfirmed'] and record['historyMayExist'] and record['historyVerified']
            plan = record['plan']; managed = json.loads(row['plan_json'])
            assert plan['fingerprint'] == managed['Fingerprint'] == digest(b'whaledeck-container-ownership-plan-v1\n', {k: v for k, v in plan.items() if k != 'fingerprint'})
            assert [plan['command']['uid'], plan['command']['gid']] == [item['command']['uid'], item['command']['gid']] and plan['command']['path'] == row['relative_path']
            assert [plan['uid'], plan['gid']] == [managed['OwnerUid'], managed['OwnerGid']] == [1000, 1000]
            expected_source = {'file-owner':[1000,1000], 'file-noop':[2200,3200], 'file-root':[2200,3200], 'file-restore':[0,0], 'directory-owner':[2100,3100], 'directory-restore':[2200,3200]}[item['name']]
            expected_target = {'file-owner':[2200,3200], 'file-noop':[2200,3200], 'file-root':[0,0], 'file-restore':[1000,1000], 'directory-owner':[2200,3200], 'directory-restore':[2100,3100]}[item['name']]
            assert plan['sourceIdentity'][3:5] == [item['originalUid'],item['originalGid']] == expected_source
            assert [plan['command']['uid'],plan['command']['gid']] == expected_target
            assert [item['executorUid'],item['executorGid']] == [1000,1000]
            desired = list(plan['sourceIdentity']); desired[3:5] = expected_target
            assert record['actualIdentity'][:8] == desired[:8]
            expected_version = hashlib.sha256(b'whaledeck-container-file-v1\n' + json.dumps([baseline['containerId'], baseline['instanceId'], row['relative_path'], *record['actualIdentity']], ensure_ascii=False, separators=(',', ':')).encode()).hexdigest().upper()
            assert record['version'] == expected_version
            history = hdd / (op + '.json'); trusted(history)
            descriptor = os.open(history, os.O_RDONLY | os.O_NOFOLLOW)
            try:
                storage.attest(configuration, descriptor, read_only=True)
            finally:
                os.close(descriptor)
            saved = json.loads(history.read_bytes())
            assert history.read_bytes() == (directory / (item['name'] + '.hdd.json')).read_bytes()
            assert saved['operationId'] == op and saved['owner'] == row['owner_hash'] and saved['plan'] == plan
            assert saved['historyDigest'] == row['history_digest'] == record['historyDigest'] == digest(b'whaledeck-container-ownership-history-v1\n', {k: v for k, v in saved.items() if k != 'historyDigest'})
            assert record['version'] == row['published_version'] == item['publishedVersion']
            records.append({'id': row['id'], 'execution': row['execution_job_id'], 'rootSha256': hashlib.sha256(raw).hexdigest()})
    assert len(records) == 6 and len({item['id'] for item in records}) == 6
    source = Path(baseline['source'])
    assert source.parent == common.APPS and source.name == baseline['containerName'] and not source.exists()
    missing = subprocess.run(['docker', 'inspect', baseline['containerId']], capture_output=True, timeout=20)
    assert missing.returncode != 0 and b'no such' in missing.stderr.lower()
    preflight()
    active = query("SELECT json_build_object('active',count(*) FILTER(WHERE completed_at_utc IS NULL),'barriers',count(*) FILTER(WHERE publish_barrier),'leases',count(*) FILTER(WHERE lease_token IS NOT NULL)) FROM container_ownership_intents;")
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
    return {'readOnlyVerifier': True, 'revision': baseline['revision'], 'ownerships': records, 'canceled': 1,
        'exactSourceAndContainerAbsent': True, 'temporaryIdentitiesAbsent': True, 'activeOwnershipWork': active,
        'coreUnchanged': 11, 'physicalBytesVerifiedByParentBeforeCleanup': True}


if __name__ == '__main__':
    try:
        assert os.geteuid() == 0 and len(sys.argv) == 2
        print(json.dumps(verify(Path(sys.argv[1]))))
    except Exception as error:
        print(json.dumps({'ownershipIndependentVerification': 'failed', 'failureType': type(error).__name__}))
        raise SystemExit(1) from None
