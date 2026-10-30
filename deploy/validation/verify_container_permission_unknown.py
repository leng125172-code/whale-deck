#!/usr/bin/env python3
"""Read-only independent Unknown/acknowledgement and exact cleanup evidence."""
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
from validate_container_directory_browser import snapshot as directory_snapshot
from validate_container_permission_browser import snapshot
from validate_container_file_publishing import common
from validate_container_file_staging import load
from validate_container_upload_recovery_agent import trusted
from validate_metric_continuity import query
from verify_container_permission_faults import healthy_core


def verify(directory):
    repo = Path(__file__).resolve().parents[2]
    assert directory.parent == repo / 'deploy/artifacts' and re.fullmatch('container-permission-unknown-[a-f0-9]{32}', directory.name)
    trusted(directory, True)
    baseline = json.loads((directory / 'baseline.json').read_bytes()); accepted = json.loads((directory / 'verified.json').read_bytes())
    owner = json.loads((directory / 'owner-browser.json').read_bytes()); recovery = json.loads((directory / 'recovery-browser.json').read_bytes())
    assert baseline['revision'] == accepted['revision'] == owner['revision'] == recovery['revision']
    assert accepted['actualOriginalAndRecoveryPages'] and owner['actualUi'] and recovery['actualUi']
    assert owner['normalDualLogout'] and recovery['normalDualLogout'] and owner['subject'] == accepted['originalSubject'] != recovery['subject'] == accepted['recoverySubject']
    assert owner['registrations'] == owner['confirmations'] == recovery['confirmations'] == 1 and owner['bodies'] == recovery['registrations'] == recovery['bodies'] == 0
    assert accepted['validatorKilledHelper'] is False and 0 <= accepted['productStoppedHelperSeconds'] < 15
    original = json.loads((directory / 'unknown-sql.json').read_bytes()); final = snapshot(accepted['id'])
    assert final == json.loads((directory / 'final-sql.json').read_bytes())
    row = final['row']; old = original['row']
    assert old['state'] == 'Unknown' and old['publish_barrier'] and old['completed_at_utc'] is None and old['mode_confirmed'] is None
    assert old['history_verified'] and old['history_may_exist'] and original['executionJob']['state'] == 'Running'
    assert row['state'] == 'Acknowledged' and final['executionJob']['state'] == 'Failed' and final['planJob']['state'] == 'Succeeded'
    assert row['actor_subject'] == owner['subject'] and row['acknowledged_by'] == recovery['subject']
    assert row['completed_at_utc'] and not row['publish_barrier'] and row['lease_token'] is None and row['lease_expires_at_utc'] is None and final['pending'] == 0
    assert row['mode_confirmed'] is None and row['published_version'] is None and row['history_verified'] and row['history_digest'] == old['history_digest']
    audit = query("SELECT json_build_object('n',count(*),'actors',json_agg(actor_subject),'results',json_agg(result),'targets',json_agg(target_id)) FROM audit_events WHERE job_id=:'id'::uuid AND action='containers.files.permission.unknown-acknowledged';", id=row['execution_job_id'])
    assert audit == {'n': 1, 'actors': [recovery['subject']], 'results': ['Unknown'], 'targets': [baseline['containerId']]}
    record = record_for(row); assert record['state'] == 'Unknown' and record['modeConfirmed'] is None
    raw = (ROOT / (record['operationId'] + '.json')).read_bytes()
    assert raw == (directory / 'unknown-root.json').read_bytes() and hashlib.sha256(raw).hexdigest() == accepted['rootSha256']
    cut = json.loads((directory / 'cut-root.json').read_bytes()); bound = json.loads((directory / 'bound-helper.json').read_bytes())
    assert cut['state'] in ('Preparing', 'Prepared') and cut['historyVerified'] and bound['twoKernelFdProofs']
    assert cut['plan']['sourceIdentity'] == record['plan']['sourceIdentity'] == baseline['original'] == bound['proof']['originalIdentity']
    files = load(Path('/usr/local/libexec/whaledeck-files.py'), 'permission_unknown_verifier_files')
    storage = load(Path('/usr/local/libexec/whaledeck-file-backups.py'), 'permission_unknown_verifier_hdd').FileBackups(files)
    with storage.target(create_namespace=False, read_only=True) as (configuration, base):
        path = Path(os.readlink('/proc/self/fd/' + str(base))) / 'container-permissions-v1' / (record['operationId'] + '.json')
        trusted(path); fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW)
        try: storage.attest(configuration, fd, read_only=True)
        finally: os.close(fd)
        raw_history = path.read_bytes(); assert raw_history == (directory / 'unknown-hdd.json').read_bytes(); history = json.loads(raw_history)
        digest = hashlib.sha256(b'whaledeck-container-permission-history-v1\n' + json.dumps({k: v for k, v in history.items() if k != 'historyDigest'},
            ensure_ascii=False, sort_keys=True, separators=(',', ':'), allow_nan=False).encode()).hexdigest().upper()
        assert digest == history['historyDigest'] == row['history_digest'] == record['historyDigest']
        assert history['plan'] == record['plan'] and history['operationId'] == record['operationId'] and history['owner'] == row['owner_hash']
    late = json.loads((directory / 'late-apply.json').read_bytes())
    assert late['operationId'] == record['operationId'] and late['state'] == 'Unknown' and late['modeConfirmed'] is None and late['replayed']
    waiting_baseline = json.loads((directory / 'waiting-baseline.json').read_bytes()); waiting = directory_snapshot(accepted['waitingId'])
    assert waiting == json.loads((directory / 'waiting-final-sql.json').read_bytes())
    assert waiting['row']['state'] == 'Succeeded' and waiting['row']['root_started_at_utc'] >= row['completed_at_utc']
    blocked = json.loads((directory / 'waiting-blocked.json').read_bytes()); assert len(blocked) == 3
    assert all(s['rootStarted'] is False for s in blocked) and any(s['nextAttemptAtUtc'] > s['createdAtUtc'] for s in blocked)
    expected_sha = hashlib.sha256(hashlib.shake_256(('owned-container-original:' + baseline['containerName']).encode()).digest(common.SIZE)).hexdigest().upper()
    assert expected_sha == baseline['sha256'] and baseline['bytes'] == common.SIZE and accepted['originalCompleteBytesAndFingerprintUnchanged']
    for b in (baseline, waiting_baseline):
        source = Path(b['source']); assert source.parent == common.APPS and not source.exists()
        cid = b['containerId']; assert re.fullmatch('[a-f0-9]{64}', cid)
        missing = subprocess.run(['docker', 'inspect', cid], capture_output=True, timeout=10)
        assert missing.returncode != 0 and b'no such' in missing.stderr.lower()
    current = healthy_core()
    for name, before in baseline['coreBefore'].items():
        assert list(current[name][:3] if name == '/whaledeck-worker' else current[name]) == (before[:3] if name == '/whaledeck-worker' else before)
    preflight()
    totals = query("SELECT json_build_object('active',count(*) FILTER(WHERE completed_at_utc IS NULL),'barriers',count(*) FILTER(WHERE publish_barrier),'leases',count(*) FILTER(WHERE lease_token IS NOT NULL)) FROM container_permission_intents;")
    assert all(n == 0 for n in totals.values())
    session = requests.Session(); session.trust_env = False
    session.headers['Authorization'] = 'Bearer ' + require(container_environment(), 'Authentication__Authentik__ApiToken')
    try:
        for prefix in ('whaledeck-validation-', 'whaledeck-upload-qa-'):
            response = session.get(AUTHENTIK_API + 'core/users/', params={'search': prefix, 'page_size': 100}, timeout=20)
            assert response.status_code == 200 and not response.json().get('pagination', {}).get('next')
            assert not any(str(u['username']).startswith(prefix) for u in response.json()['results'])
    finally: session.close()
    return {'readOnlyVerifier': True, 'revision': accepted['revision'], 'id': row['id'], 'modeConfirmed': None, 'originalExecutionFailed': True,
        'rootUnknownUnchanged': True, 'hddMetadataUnchanged': True, 'lateApplyRemainsUnknown': True,
        'differentAdministratorActualPage': True, 'uniqueCurrentAdministratorAudit': True, 'otherWriteOnlyAfterAcknowledgement': True, 'exactSourcesAndContainersAbsent': True,
        'temporaryIdentitiesAbsent': True, 'permissionTotals': totals, 'coreHealthy': 11, 'physicalFullBytesVerifiedByParentBeforeCleanup': True}


if __name__ == '__main__':
    try:
        assert os.geteuid() == 0 and len(sys.argv) == 2
        print(json.dumps(verify(Path(sys.argv[1]))))
    except BaseException as error:
        import traceback
        print(json.dumps({'permissionUnknownIndependentVerifier': 'failed', 'failureType': type(error).__name__,
            'locations': [{'file': Path(f.filename).name, 'line': f.lineno} for f in traceback.extract_tb(error.__traceback__)]}))
        raise SystemExit(1) from None
