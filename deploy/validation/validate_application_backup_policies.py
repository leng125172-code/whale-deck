#!/usr/bin/env python3
"""Isolated application policy/retention acceptance. Never touches production archives or policies."""
import json
import os
import re
import runpy
import subprocess
import time
import uuid
from pathlib import Path
from validate_databases import Api
from validate_governance import request
from validate_oidc import authenticate, required_environment
from validate_metric_partition_runtime import query


def main():
    inspector = runpy.run_path(str(Path(__file__).resolve().parents[1] / 'host/whaledeck-application-backup.py'), run_name='validation')
    inspected = inspector['inspect_all']()
    assert inspected and all(set(item['Config']) == {'Labels'} and
        set(item['State']) == {'Running', 'Paused', 'Restarting', 'Dead', 'StartedAt'} for item in inspected)
    print(json.dumps({'rootDockerProjection': 'passed-without-env-cmd-or-health-output'}))
    base = os.environ.get('WHALEDECK_URL', 'http://192.168.100.13:8080').rstrip('/')
    session, _ = authenticate(base, required_environment('VALIDATION_USERNAME'), required_environment('VALIDATION_PASSWORD'))
    api = Api(base, session)
    slug = 'wdv-policy-' + uuid.uuid4().hex[:12]
    resource = 'application:' + slug
    installed = False
    policy = None
    tracked = []
    def records(): return request(api, 'GET', 'backups/records?instanceResourceId=' + resource).json()
    def state(job): return request(api, 'GET', 'jobs/' + job).json()
    def finish(job, timeout=160):
        end = time.monotonic() + timeout
        while time.monotonic() < end:
            value = state(job)
            if value['state'] in {'Succeeded', 'Failed', 'Canceled'}: return value
            time.sleep(2)
        raise RuntimeError('Original policy operation still nonterminal: ' + job)
    try:
        api.run('install', '', {'slug': slug, 'image': 'nginxinc/nginx-unprivileged:alpine', 'autoupdate': 'false'},
            area='applications', confirmed=True, requires_plan=True, timeout=160)
        installed = True
        time.sleep(7)
        first = api.run('backup', resource, {'consistency': 'live'}, area='applications', confirmed=True, requires_plan=True, timeout=160)
        second = api.run('backup', resource, {'consistency': 'live'}, area='applications', confirmed=True, requires_plan=True, timeout=160)
        tracked.extend([first['id'], second['id']])
        cleanup = api.run('cleanup-backups', resource, {'retentionDays': '14', 'retentionCount': '1'}, area='applications', confirmed=True, requires_plan=True, timeout=160)
        tracked.append(cleanup['id'])
        assert json.loads(cleanup['resultJson'])['removedBackupIds'] == [uuid.UUID(first['id']).hex]
        assert next(item for item in records() if item['jobId'] == first['id'])['status'] == 'Expired'
        assert next(item for item in records() if item['jobId'] == second['id'])['status'] == 'Succeeded'
        candidate = {'id': None, 'instanceResourceId': resource, 'isEnabled': True, 'scheduleExpression': '* * * * *',
            'timezone': 'Asia/Shanghai', 'retentionCount': 1, 'retentionDays': 14, 'targetDirectoryId': 'database-platform.backup.hdd',
            'compression': 'gzip', 'verifyAfterBackup': True, 'consistency': 'live', 'capacityWarningPercent': 80, 'capacityCriticalPercent': 90,
            'expectedVersion': None, 'confirmed': False}
        rejected = session.post(base + '/api/v1/backups/policies', headers=api.headers, json=candidate, timeout=25)
        assert rejected.status_code == 400, rejected.status_code
        candidate['confirmed'] = True
        policy = request(api, 'POST', 'backups/policies', payload=candidate).json()
        assert policy['consistency'] == 'live' and policy['instanceResourceId'] == resource
        end = time.monotonic() + 150
        automatic = None
        while time.monotonic() < end:
            automatic = next((item for item in records() if item['policyId'] == policy['id']), None)
            if automatic: break
            time.sleep(3)
        assert automatic is not None, 'Enabled application policy did not create a record'
        tracked.append(automatic['jobId'])
        assert finish(automatic['jobId'])['state'] == 'Succeeded'
        # Completed policy backups enqueue their own retention, not only daily cleanup.
        end = time.monotonic() + 80
        while time.monotonic() < end:
            values = records()
            if next(item for item in values if item['jobId'] == second['id'])['status'] == 'Expired': break
            time.sleep(2)
        assert next(item for item in records() if item['jobId'] == second['id'])['status'] == 'Expired'
        disabled = {**candidate, 'id': policy['id'], 'isEnabled': False, 'confirmed': False, 'expectedVersion': policy['version']}
        policy = request(api, 'POST', 'backups/policies', payload=disabled).json()
        assert not policy['isEnabled'] and policy['version'] > disabled['expectedVersion']
        print(json.dumps({'applicationBackupPolicies': 'passed', 'confirmation': '400-without-confirmation',
            'manualRetention': 'only-old-owned-archive-removed', 'recordStatus': 'Expired-preserved',
            'nativeScheduledBackup': 'Succeeded', 'retentionAfterAutomaticBackup': 'passed', 'disableAndVersion': 'passed'}))
    finally:
        if policy is not None and policy['isEnabled']:
            disabled = {**policy, 'expectedVersion': policy['version'], 'isEnabled': False, 'confirmed': False}
            policy = request(api, 'POST', 'backups/policies', payload=disabled).json()
        if installed:
            assert re.fullmatch(r'wdv-policy-[a-f0-9]{12}', slug)
            # Observe every job belonging to this generated resource, including automatic retention.
            own_jobs = query(f"SELECT coalesce(json_agg(id), '[]'::json) FROM operation_jobs WHERE request_json::jsonb->>'resourceId'='{resource}';")
            for job_id in own_jobs:
                value = state(job_id)
                if value['state'] not in {'Succeeded', 'Failed', 'Canceled'}:
                    session.post(base + '/api/v1/jobs/' + job_id + '/cancel', headers=api.headers, json={}, timeout=25)
                    finish(job_id)
                unit = 'whaledeck-application-backup-' + uuid.UUID(job_id).hex + '.service'
                deadline = time.monotonic() + 160
                while True:
                    active = subprocess.run(['systemctl', 'show', unit, '--property=ActiveState', '--value'], check=True,
                        text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=15).stdout.strip()
                    if active not in {'active', 'activating', 'deactivating'}: break
                    if time.monotonic() >= deadline: raise RuntimeError('Archive unit still active; test data retained')
                    time.sleep(2)
            api.run('uninstall', '', {'slug': slug, 'deleteVolumes': 'true'}, area='applications', confirmed=True, requires_plan=True, timeout=160)
            retained = '/data/WhaleDeck/apps/.retained'
            def docker(args): return subprocess.run(['docker', *args], check=True, text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=45).stdout
            names = docker(['run', '--rm', '--network', 'none', '--read-only', '--cap-drop', 'ALL', '--user', '997:975', '--mount',
                'type=bind,source=' + retained + ',target=/retained,readonly', 'alpine:3.22', 'find', '/retained', '-mindepth', '1', '-maxdepth', '1', '-name', slug + '-*']).splitlines()
            for path in names:
                name = path.rsplit('/', 1)[-1]
                assert re.fullmatch(re.escape(slug) + '-[a-f0-9]{32}', name)
                docker(['run', '--rm', '--network', 'none', '--read-only', '--cap-drop', 'ALL', '--cap-add', 'DAC_OVERRIDE', '--mount',
                    'type=bind,source=' + retained + ',target=/retained', 'alpine:3.22', 'rm', '-rf', '--', '/retained/' + name])
            docker(['run', '--rm', '--network', 'none', '--read-only', '--cap-drop', 'ALL', '--mount',
                'type=bind,source=/data/DockerData/whaledeck/application-backups,target=/backups', 'alpine:3.22', 'rm', '-rf', '--', '/backups/' + slug])
            # Remove only the policy created by this test; preserve immutable job/audit history.
            policy_id = str(uuid.UUID(policy['id'])) if policy is not None else None
            statement = f"BEGIN; UPDATE backup_records SET status='Expired' WHERE instance_resource_id IN (SELECT id FROM managed_resources WHERE external_id='{resource}');"
            if policy_id: statement += f"DELETE FROM backup_policies WHERE id='{policy_id}' AND instance_resource_id IN (SELECT id FROM managed_resources WHERE external_id='{resource}');"
            subprocess.run(['docker', 'exec', '-i', '-u', 'postgres', 'database-platform-postgres', 'psql', '-X', '-v', 'ON_ERROR_STOP=1', '-d', 'whaledeck'],
                input=statement + 'COMMIT;', check=True, text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=30)
        print(json.dumps({'temporaryApplicationPolicyCleanup': 'passed'}))


if __name__ == '__main__': main()
