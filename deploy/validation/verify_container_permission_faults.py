#!/usr/bin/env python3
"""Independent read-only post-cleanup fault evidence; no business writes."""
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys

import requests

from bound_container_permission_cut import ROOT, record_for
from run_with_ephemeral_admin import AUTHENTIK_API, container_environment, require
from validate_container_directory_restart import preflight
from validate_container_file_publishing import common
from validate_container_file_staging import load
from validate_container_permission_browser import snapshot
from validate_container_upload_recovery_agent import trusted
from validate_metric_continuity import query


def healthy_core():
    values = json.loads(subprocess.check_output(['docker', 'inspect', *common.CORE_NAMES], timeout=20))
    assert len(values) == 11 and {v['Name'].lstrip('/') for v in values} == set(common.CORE_NAMES)
    assert all(v['State']['Running'] and v['State'].get('Health', {}).get('Status') == 'healthy' for v in values)
    return common.core_snapshot()


def verify(directory):
    repo = Path(__file__).resolve().parents[2]
    assert directory.parent == repo / 'deploy/artifacts' and re.fullmatch('container-permission-faults-[a-f0-9]{32}', directory.name)
    trusted(directory, True)
    accepted = json.loads((directory / 'verified.json').read_bytes())
    assert accepted['cases'] and 1 <= len(accepted['cases']) <= 4
    files = load(Path('/usr/local/libexec/whaledeck-files.py'), 'permission_fault_verifier_files')
    storage = load(Path('/usr/local/libexec/whaledeck-file-backups.py'), 'permission_fault_verifier_hdd').FileBackups(files)
    records = []
    current_core = healthy_core()
    for result in accepted['cases']:
        kind = result['kind']; assert kind in ('cancel', 'logout', 'worker-crash', 'agent-restart')
        p = directory / kind; trusted(p, True)
        baseline = json.loads((p / 'baseline.json').read_bytes()); ui = json.loads((p / 'browser.json').read_bytes())
        assert baseline['revision'] == ui['revision'] == accepted['revision'] and ui['id'] == result['id']
        assert ui['registrations'] == ui['confirmations'] == 1 and ui['cancellations'] == (1 if kind == 'cancel' else 0) and ui['bodies'] == 0
        assert ui['actualUi'] and ui['normalDualLogout'] and result['actualUi'] and not result['validatorKilledHelper']
        assert 0 <= result['productStoppedHelperSeconds'] < 15 and result['originalFullBytesAndFingerprintUnchanged']
        expected_sha = hashlib.sha256(hashlib.shake_256(('owned-container-original:' + baseline['containerName']).encode()).digest(common.SIZE)).hexdigest().upper()
        assert baseline['sha256'] == expected_sha and baseline['bytes'] == common.SIZE
        value = snapshot(result['id']); row = value['row']
        assert value == json.loads((p / 'final-sql.json').read_bytes())
        assert row['actor_subject'] == ui['subject'] and row['execution_job_id'] == ui['executionJobId'] == result['executionJobId']
        assert row['state'] == ui['finalState'] == value['executionJob']['state'] == ('Canceled' if kind in ('cancel', 'logout') else 'Failed')
        assert value['planJob']['state'] == 'Succeeded' and value['accepted'] == value['confirmed'] == 1 and value['pending'] == 0
        assert row['mode_confirmed'] is False and row['history_may_exist'] and row['history_verified'] and row['published_version'] is None
        assert row['completed_at_utc'] and not row['publish_barrier'] and row['lease_token'] is None and row['lease_expires_at_utc'] is None
        audit = query("SELECT json_build_object('n',count(*),'actors',json_agg(actor_subject),'results',json_agg(result)) FROM audit_events WHERE job_id=:'id'::uuid AND action='containers.files.permission.result';", id=row['execution_job_id'])
        assert audit == {'n': 1, 'actors': [ui['subject']], 'results': [row['state']]}
        record = record_for(row); assert record['state'] == 'Aborted' and record['modeConfirmed'] is False
        raw = (ROOT / (record['operationId'] + '.json')).read_bytes(); assert raw == (p / 'final-root.json').read_bytes()
        cut = json.loads((p / 'cut-root.json').read_bytes()); bound = json.loads((p / 'bound-helper.json').read_bytes())
        assert cut['state'] in ('Preparing', 'Prepared') and cut['modeConfirmed'] is None and cut['historyVerified']
        assert cut['plan']['sourceIdentity'] == record['plan']['sourceIdentity'] == baseline['original'] == bound['proof']['originalIdentity']
        assert cut['owner'] == record['owner'] and cut['operationId'] == record['operationId'] and bound['twoKernelFdProofs']
        with storage.target(create_namespace=False, read_only=True) as (configuration, base):
            hdd = Path(os.readlink('/proc/self/fd/' + str(base))) / 'container-permissions-v1' / (record['operationId'] + '.json')
            trusted(hdd); descriptor = os.open(hdd, os.O_RDONLY | os.O_NOFOLLOW)
            try: storage.attest(configuration, descriptor, read_only=True)
            finally: os.close(descriptor)
            saved_raw = hdd.read_bytes(); assert saved_raw == (p / 'final-hdd.json').read_bytes(); history = json.loads(saved_raw)
            assert history['plan'] == record['plan'] and history['operationId'] == record['operationId'] and history['owner'] == row['owner_hash']
            digest = hashlib.sha256(b'whaledeck-container-permission-history-v1\n' + json.dumps({k: v for k, v in history.items() if k != 'historyDigest'},
                sort_keys=True, ensure_ascii=False, separators=(',', ':'), allow_nan=False).encode()).hexdigest().upper()
            assert digest == history['historyDigest'] == record['historyDigest'] == row['history_digest']
        if kind == 'worker-crash':
            service = result['serviceRestart']; assert service['sameWorkerContainerAndImage'] and service['signal'] == 'SIGKILL'
            assert dt.datetime.fromisoformat(row['completed_at_utc']) >= dt.datetime.fromisoformat(service['originalLeaseExpiresAtUtc'])
            samples = json.loads((p / 'natural-lease.json').read_bytes()); assert len(samples) == 3
            assert all(s['state'] == 'Applying' and s['lease'] == service['originalLeaseExpiresAtUtc'] for s in samples)
        if kind == 'agent-restart':
            service = result['serviceRestart']; assert service['oldAgentPid'] != service['newAgentPid'] and service['maintenancePidUnchanged']
            assert all(n == 0 for n in service['otherActiveWork'].values())
        source = Path(baseline['source']); assert source.parent == common.APPS and source.name == baseline['containerName'] and not source.exists()
        missing = subprocess.run(['docker', 'inspect', baseline['containerId']], capture_output=True, timeout=10)
        assert missing.returncode != 0 and b'no such' in missing.stderr.lower()
        assert current_core.keys() == baseline['coreBefore'].keys()
        for name, before in baseline['coreBefore'].items():
            assert list(current_core[name][:3] if name == '/whaledeck-worker' else current_core[name]) == (before[:3] if name == '/whaledeck-worker' else before)
        records.append({'kind': kind, 'id': row['id'], 'rootSha256': hashlib.sha256(raw).hexdigest(), 'historyDigest': digest})
    preflight()
    totals = query("SELECT json_build_object('active',count(*) FILTER(WHERE completed_at_utc IS NULL),'barriers',count(*) FILTER(WHERE publish_barrier),'leases',count(*) FILTER(WHERE lease_token IS NOT NULL)) FROM container_permission_intents;")
    assert all(n == 0 for n in totals.values())
    session = requests.Session(); session.trust_env = False
    session.headers['Authorization'] = 'Bearer ' + require(container_environment(), 'Authentication__Authentik__ApiToken')
    try:
        for prefix in ('whaledeck-validation-', 'whaledeck-upload-qa-'):
            reply = session.get(AUTHENTIK_API + 'core/users/', params={'search': prefix, 'page_size': 100}, timeout=20)
            assert reply.status_code == 200 and not reply.json().get('pagination', {}).get('next')
            assert not any(str(u['username']).startswith(prefix) for u in reply.json()['results'])
    finally: session.close()
    return {'readOnlyVerifier': True, 'revision': accepted['revision'], 'cases': records, 'permissionTotals': totals,
        'exactSourcesAndContainersAbsent': True, 'temporaryIdentitiesAbsent': True, 'coreHealthy': 11,
        'physicalFullBytesVerifiedBeforeCleanupByParent': True}


if __name__ == '__main__':
    try:
        assert os.geteuid() == 0 and len(sys.argv) == 2
        print(json.dumps(verify(Path(sys.argv[1]))))
    except BaseException as error:
        import traceback
        print(json.dumps({'permissionFaultIndependentVerifier': 'failed', 'failureType': type(error).__name__,
            'locations': [{'file': Path(f.filename).name, 'line': f.lineno} for f in traceback.extract_tb(error.__traceback__)]}))
        raise SystemExit(1) from None
