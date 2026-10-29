#!/usr/bin/env python3
"""Read-only independent SQL/Root/fixture checks after an inflight acceptance.

No authentication changes, task writes, container actions, or cleanup. Output is
limited to evidence counts; runtime credentials stay in memory.
"""
import datetime as dt
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import uuid

import requests
from bound_container_copy_cut import ROOT, record_for
from run_with_ephemeral_admin import AUTHENTIK_API, container_environment, require
from validate_container_copy_browser import snapshot
from validate_container_directory_restart import command, preflight
from validate_metric_continuity import query


def verify_case(directory, case):
    kind = case['kind']
    assert kind in ('cancel', 'logout', 'worker-crash', 'agent-restart')
    baseline = json.loads((directory / (kind + '-baseline.json')).read_bytes())
    cut = json.loads((directory / (kind + '-partial-cut.json')).read_bytes())
    value = snapshot(baseline['id']); row = value['row']
    expected = 'Canceled' if kind in ('cancel', 'logout') else 'Failed'
    code = 'CONTAINER_COPY_CANCELED' if expected == 'Canceled' else 'CONTAINER_COPY_INTERRUPTED'
    assert case['id'] == row['id'] == baseline['id']
    assert case['executionId'] == baseline['executionId'] == row['execution_job_id']
    assert row['id'] != row['execution_job_id']
    assert row['state'] == value['executionJob']['state'] == expected and row['result_code'] == code
    assert value['planJob']['state'] == 'Succeeded'
    assert row['completed_at_utc'] and row['root_started_at_utc']
    assert row['lease_token'] is None and row['lease_expires_at_utc'] is None
    assert not row['publish_barrier'] and not row['private_may_exist'] and row['published_version'] is None
    assert value['pending'] == 0 and value['accepted'] == value['confirmed'] == 1 and value['events'] >= 4
    assert row['container_id'] == baseline['containerId'] and row['instance_id'] == baseline['instanceId']
    assert row['target_path'] == baseline['target'] and row['source_path'] == 'work/large.bin'
    assert cut['twoKernelFdProofs'] is True and cut['outer']['state'] == 'Running'
    assert cut['outer']['operationId'] == uuid.UUID(row['execution_job_id']).hex
    assert 0 < cut['proof']['partialBytes'] < cut['proof']['totalBytes'] == baseline['bytes']
    assert cut['proof']['inner']['state'] == 'Preparing'
    assert 0 <= case['productStoppedHelperSeconds'] < 15 and case['validatorKilledHelper'] is False
    receipt = record_for(row)
    assert receipt['state'] == 'Aborted' and receipt['owner'] == row['owner_hash']
    archive = directory / (kind + '-root-final.json')
    original = ROOT / (uuid.UUID(row['execution_job_id']).hex + '.json')
    assert archive.read_bytes() == original.read_bytes()
    source = Path(baseline['source'])
    assert source.parent == Path('/data/WhaleDeck/apps') and re.fullmatch('wdv-container-publish-[a-f0-9]{32}', source.name)
    assert not os.path.lexists(source), 'OWNED_SOURCE_NOT_CLEANED'
    absent = subprocess.run(['docker', 'inspect', baseline['containerId']], capture_output=True, timeout=10)
    assert absent.returncode != 0 and b'no such' in absent.stderr.lower()
    if kind == 'worker-crash':
        proof = json.loads((directory / 'worker-crash-lease-wait.json').read_bytes())
        assert proof['sameContainerAndImage'] is True and proof['signal'] == 'SIGKILL'
        assert dt.datetime.fromisoformat(row['completed_at_utc']) >= dt.datetime.fromisoformat(proof['originalLeaseExpiresAtUtc'])
    if kind == 'agent-restart':
        proof = case['serviceRestart']
        assert proof['oldAgentPid'] != proof['newAgentPid'] and proof['maintenancePidUnchanged'] and proof['apiAvailable']
    return {'kind': kind, 'id': row['id'], 'executionId': row['execution_job_id'], 'state': expected,
            'partialBytes': cut['proof']['partialBytes'], 'plannedBytes': baseline['bytes'], 'rootOriginalUnchanged': True,
            'sourceAndExactContainerAbsent': True}


def main():
    assert os.geteuid() == 0 and len(sys.argv) >= 2
    os.umask(0o077); artifacts = Path(__file__).resolve().parents[1] / 'artifacts'; records = []
    directories = []
    for name in sys.argv[1:]:
        assert re.fullmatch('container-copy-inflight-[a-f0-9]{32}', name)
        directory = artifacts / name
        assert directory.resolve(strict=True) == directory
        result = json.loads((directory / 'verified.json').read_bytes())
        assert result['partialCopies'] == len(result['cases']) == 2 and result['rootAndBusinessHistoryRetained']
        verified = [verify_case(directory, case) for case in result['cases']]
        assert len({case['kind'] for case in verified}) == 2
        records.extend(verified); directories.append(directory)
    assert len({row['id'] for row in records}) == len(records)
    checks = preflight()
    copies = query("""SELECT json_build_object(
        'active',(SELECT count(*) FROM container_copy_intents WHERE completed_at_utc IS NULL),
        'barriers',(SELECT count(*) FROM container_copy_intents WHERE publish_barrier),
        'leases',(SELECT count(*) FROM container_copy_intents WHERE lease_token IS NOT NULL));""")
    assert all(value == 0 for value in copies.values())
    core = [json.loads(line) for line in command('docker', 'ps', '-a', '--format', 'json').splitlines()]
    core = [item for item in core if item['Names'].startswith(('whaledeck-', 'database-platform-'))]
    assert len(core) == 11 and all('(healthy)' in item['Status'] for item in core)
    transients = []
    for path in Path('/run/systemd/transient').glob('whaledeck-*.service'):
        if command('systemctl', 'show', path.name, '-p', 'ActiveState', '--value') not in ('inactive', 'failed'):
            transients.append(path.name)
    assert not transients
    session = requests.Session(); session.trust_env = False
    values = container_environment()
    session.headers['Authorization'] = 'Bearer ' + require(values, 'Authentication__Authentik__ApiToken')
    users = 0
    try:
        for prefix in ('whaledeck-validation-', 'whaledeck-upload-qa-'):
            reply = session.get(AUTHENTIK_API + 'core/users/', params={'search': prefix, 'page_size': 100}, timeout=20)
            assert reply.status_code == 200
            body = reply.json(); assert not body.get('pagination', {}).get('next')
            users += sum(str(row['username']).startswith(prefix) for row in body['results'])
    finally: session.close()
    assert users == 0
    proof = {'cases': records, 'preflight': checks, 'copyTotals': copies, 'coreHealthy': len(core),
             'activeTransients': 0, 'temporaryUsers': users, 'independentReadOnlyCheck': True}
    # Private evidence is the only write; product state stays read-only.
    for directory in directories: (directory / 'independent-final.json').write_text(json.dumps(proof))
    print(json.dumps({'containerCopyInflightIndependent': 'passed', 'cases': len(records),
                      'coreHealthy': len(core), 'temporaryUsers': users}), flush=True)


if __name__ == '__main__': main()
