#!/usr/bin/env python3
"""Independent read-only post-cleanup SQL/Root/identity acceptance check.

Physical bytes were independently read by the Python parent before deletion.
This process verifies retained evidence and exact cleanup, not deleted bytes.
"""
import hashlib
import datetime as dt
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import sys
import uuid

import requests
from bound_container_move_cut import ROOT, receipt_json, record_for
from run_with_ephemeral_admin import AUTHENTIK_API, container_environment, require
from validate_container_directory_restart import preflight
from validate_container_move_browser import snapshot
from validate_container_directory_browser import snapshot as directory_snapshot, ROOT as DIRECTORY_ROOT
from validate_metric_continuity import query


def canonical(value):
    return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(',', ':'), allow_nan=False).encode()


def main():
    assert os.geteuid() == 0 and len(sys.argv) == 2; os.umask(0o077)
    name = sys.argv[1]; assert re.fullmatch('container-move-unknown-[a-f0-9]{32}', name)
    directory = Path(__file__).resolve().parents[1] / 'artifacts' / name
    assert directory.resolve(strict=True) == directory
    baseline = json.loads((directory / 'baseline.json').read_bytes()); checked = json.loads((directory / 'verified.json').read_bytes())
    ui = json.loads((directory / 'browser-result.json').read_bytes()); cut = json.loads((directory / 'bound-cut.json').read_bytes())
    assert checked['revision'] == ui['revision'] == baseline['revision']
    assert checked['originalAccountDeleted'] and checked['differentAdminUi'] and checked['realPartialRetention']
    assert checked['oldJobFailedNotMoveSucceeded'] and checked['sourceAndTargetContentVerified']
    assert checked['exactSourcesAndContainersCleaned'] and checked['rootAndBusinessHistoryRetained']
    assert ui['fullSsoLogout'] and ui['confirmations'] == 1 and ui['registrations'] == ui['bodies'] == 0
    assert ui['originalState'] == 'Acknowledged' and ui['waitingState'] == 'Succeeded'
    assert ui['destinationPublished'] is True and ui['sourceRemoved'] is ui['sourceRetained'] is None
    state = snapshot(baseline['intent']); row = state['row']; operation = uuid.UUID(row['execution_job_id']).hex
    assert row['id'] == checked['intent'] and row['execution_job_id'] == baseline['executionId']
    assert row['actor_subject'] == baseline['originalSubject'] != ui['subject'] == row['acknowledged_by']
    assert row['container_id'] == baseline['containerId'] and row['instance_id'] == baseline['instanceId']
    assert row['state'] == 'Acknowledged' and state['planJob']['state'] == 'Succeeded' and state['executionJob']['state'] == 'Failed'
    assert row['result_code'] == 'CONTAINER_MOVE_UNKNOWN_ACKNOWLEDGED' and row['completed_at_utc'] and row['root_started_at_utc']
    assert row['destination_published'] is True and row['source_removed'] is row['source_retained'] is None
    assert row['published_version'] is None and row['private_may_exist'] and not row['publish_barrier']
    assert row['lease_token'] is row['lease_expires_at_utc'] is None and state['pending'] == 0 and state['accepted'] == state['confirmed'] == 1
    receipt = record_for(row); raw = (ROOT / (operation + '.json')).read_bytes()
    assert raw == (directory / 'unknown-root.json').read_bytes() and receipt['state'] == 'Unknown'
    assert receipt['destinationPublished'] is True and receipt['sourceRemoved'] is receipt['sourceRetained'] is None
    assert receipt['plan'] == cut['outer']['plan'] and receipt['operationId'] == cut['outer']['operationId'] == operation
    assert cut['twoKernelFdProofs'] and cut['outer']['state'] == 'Running' and cut['proof']['inner']['state'] == 'SourceRetaining'
    assert 0 < len(cut['proof']['retainedFiles']) < len(cut['proof']['publishedFiles']) == 16
    assert set(cut['proof']['remainingFiles']).isdisjoint(cut['proof']['retainedFiles'])
    assert set(cut['proof']['remainingFiles']) | set(cut['proof']['retainedFiles']) == set(cut['proof']['publishedFiles'])
    inner = receipt_json(ROOT / 'engines/tree-moves' / (operation + '.json'))
    assert inner['state'] == 'Unknown' and inner['owner'] == row['owner_hash'] and inner['plan'] == receipt['plan']['innerPlan']
    assert hashlib.sha256(canonical(inner)).hexdigest().upper() == receipt['innerReceiptHash']
    manifest_path = ROOT / 'engines/tree-moves' / (operation + '.manifest.json')
    info = manifest_path.lstat(); assert stat.S_ISREG(info.st_mode) and info.st_uid == 0 and stat.S_IMODE(info.st_mode) == 0o600 and info.st_nlink == 1 and info.st_size <= 16 * 1024**2
    manifest = json.loads(manifest_path.read_bytes())
    assert hashlib.sha256(b'whaledeck-tree-move-v1\n' + canonical(manifest)).hexdigest().upper() == inner['manifestDigest']
    assert manifest['manifestSha256'] == inner['plan']['copyPlan']['manifestSha256']
    at_cut = json.loads((directory / 'physical-at-cut.json').read_bytes())
    assert at_cut == json.loads((directory / 'physical-final.json').read_bytes())
    files = [entry for entry in manifest['entries'] if entry['kind'] == 'File']; assert len(files) == 16
    for entry in files:
        name = entry['path']; assert re.fullmatch(r'part-[0-9]{2}\.bin', name)
        expected = hashlib.sha256(hashlib.shake_256(('owned-move-tree:' + baseline['containerName'] + ':' + str(int(name[5:7]))).encode()).digest(2 * 1024**2 + 7)).hexdigest().upper()
        assert entry['sha256'] == expected == baseline['digests'][name] == at_cut['sha256'][name]
        assert entry['sourceIdentity'] == baseline['metadata'][name][:8]
        matches = [at_cut[key][name] for key in ('source', 'retained') if name in at_cut[key]]
        assert len(matches) == 1 and matches[0][:8] == entry['sourceIdentity']
    late = [json.loads(line) for line in (directory / 'late-root.stdout').read_text().splitlines()][-1]
    assert late['operationId'] == operation and late['state'] == 'Unknown' and late['destinationPublished'] is True
    assert late['sourceRemoved'] is late['sourceRetained'] is None
    acknowledgement = json.loads(row['acknowledgement_json'])
    assert acknowledgement['State'] == 'Unknown' and acknowledgement['DestinationPublished'] is True
    assert acknowledgement['SourceRemoved'] is acknowledgement['SourceRetained'] is None
    audit = query("SELECT json_build_object('count',count(*),'actor',min(actor_subject)) FROM audit_events WHERE job_id=:'id'::uuid AND action='containers.files.move.unknown-acknowledged';", id=row['execution_job_id'])
    assert audit['count'] == 1 and audit['actor'] == ui['subject']
    waiting = directory_snapshot(baseline['waitingIntent']); other = waiting['row']
    assert other['state'] == waiting['job']['state'] == 'Succeeded' and waiting['pending'] == 0 and waiting['accepted'] == 1
    assert dt.datetime.fromisoformat(other['root_started_at_utc']) >= dt.datetime.fromisoformat(row['completed_at_utc'])
    assert not other['publish_barrier'] and other['lease_token'] is None
    blocked = json.loads((directory / 'blocked.json').read_bytes()); assert blocked['waitingVersion'] >= 4 and blocked['originalBarrier'] and not blocked['rootStarted']
    assert (DIRECTORY_ROOT / (uuid.UUID(other['id']).hex + '.json')).read_bytes() == (directory / (uuid.UUID(other['id']).hex + '.root.json')).read_bytes()
    for location in (baseline, baseline['waiting']):
        source = Path(location['source']); assert source.parent == Path('/data/WhaleDeck/apps') and re.fullmatch('wdv-container-publish-[a-f0-9]{32}', source.name)
        assert not os.path.lexists(source)
        absent = subprocess.run(['docker', 'inspect', location['containerId']], capture_output=True, timeout=10)
        assert absent.returncode != 0 and b'no such' in absent.stderr.lower()
    preflight()
    active = query("SELECT json_build_object('active',(SELECT count(*) FROM container_move_intents WHERE completed_at_utc IS NULL),'barriers',(SELECT count(*) FROM container_move_intents WHERE publish_barrier),'leases',(SELECT count(*) FROM container_move_intents WHERE lease_token IS NOT NULL));")
    assert all(value == 0 for value in active.values())
    for unit in Path('/run/systemd/transient').glob('whaledeck-*.service'):
        status = subprocess.check_output(['systemctl', 'show', unit.name, '-p', 'ActiveState', '--value'], text=True, timeout=10).strip()
        assert status in ('inactive', 'failed'), 'OTHER_ACTIVE_TRANSIENT_WORK'
    core = subprocess.check_output(['docker', 'ps', '-a', '--format', '{{.Names}}|{{.Status}}'], text=True, timeout=20).splitlines()
    assert len([value for value in core if value.startswith(('whaledeck-', 'database-platform-')) and '(healthy)' in value]) == 11
    api = requests.Session(); api.trust_env = False; api.headers['Authorization'] = 'Bearer ' + require(container_environment(), 'Authentication__Authentik__ApiToken')
    try:
        for prefix in ('whaledeck-validation-', 'whaledeck-upload-qa-'):
            users = api.get(AUTHENTIK_API + 'core/users/', params={'search': prefix, 'page_size': 100}, timeout=20)
            assert users.status_code == 200 and not users.json().get('pagination', {}).get('next')
            assert not any(str(user['username']).startswith(prefix) for user in users.json()['results'])
    finally: api.close()
    result = {'revision': baseline['revision'], 'intent': row['id'], 'executionId': row['execution_job_id'], 'retainedFilesAtCut': len(cut['proof']['retainedFiles']),
        'files': len(files), 'state': row['state'], 'rootState': receipt['state'], 'threeFacts': [True, None, None], 'lateApplyStillUnknown': True,
        'originalAuditAndRootUnchanged': True, 'otherApplicationWaitedThenSucceeded': True, 'exactCleanupVerified': True,
        'activeMoveWork': active, 'temporaryIdentitiesAbsent': True, 'coreHealthy': 11, 'independentReadOnlyCheck': True}
    (directory / 'independent-final.json').write_text(json.dumps(result))
    print(json.dumps(result), flush=True)


if __name__ == '__main__': main()
