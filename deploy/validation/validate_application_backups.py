#!/usr/bin/env python3
"""Native application backup/verify/cancel/reconnect acceptance using one isolated installation."""
import json
import os
import re
import subprocess
import time
import uuid

import requests
from validate_databases import Api
from validate_governance import request
from validate_oidc import authenticate, required_environment
from validate_metric_partition_runtime import query


def native(arguments, timeout=60):
    return subprocess.run(['docker', *arguments], check=True, text=True,
        stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=timeout).stdout


def main():
    base = os.environ.get('WHALEDECK_URL', 'http://192.168.100.13:8080').rstrip('/')
    session, _ = authenticate(base, required_environment('VALIDATION_USERNAME'), required_environment('VALIDATION_PASSWORD'))
    api = Api(base, session)
    slug = 'wdv-backup-' + uuid.uuid4().hex[:12]
    resource = 'application:' + slug
    container = 'whaledeck-app-' + slug
    volume = container + '-cache'
    marker = 'SYNTHETIC_' + uuid.uuid4().hex
    archive_root = '/data/DockerData/whaledeck/application-backups/' + slug
    installed = False
    ever_installed = False
    configured = False
    jobs = []

    def enqueue(action, parameters):
        plan = api.plan(action, resource, parameters, area='applications')
        job = api.post('applications/' + action, {'resourceId': resource, 'parameters': parameters,
            'confirmed': True, 'planHash': plan, 'idempotencyKey': 'backup-validation:' + uuid.uuid4().hex}, 202).json()
        jobs.append(job['id'])
        return job['id']

    def observe(job_id):
        value = request(api, 'GET', 'jobs/' + job_id).json()
        assert marker not in json.dumps(value)
        return value

    def terminal(job_id, timeout=360):
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            value = observe(job_id)
            if value['state'] in {'Succeeded', 'Failed', 'Canceled', 'RolledBack'}:
                return value
            time.sleep(2)
        raise RuntimeError('Original application backup job is still nonterminal: ' + job_id)

    def reader(arguments):
        return native(['run', '--rm', '--network', 'none', '--read-only', '--cap-drop', 'ALL',
            '--security-opt', 'no-new-privileges', '--memory', '128m', '--cpus', '0.5', '--mount',
            'type=bind,source=' + archive_root + ',target=/backup,readonly', 'alpine:3.22', *arguments])

    try:
        api.run('install', '', {'slug': slug, 'image': 'nginxinc/nginx-unprivileged:alpine', 'autoupdate': 'false'},
            area='applications', confirmed=True, requires_plan=True, timeout=180)
        installed = True
        ever_installed = True
        snapshot = request(api, 'GET', 'applications/configuration?resourceId=' + resource).json()
        patch = {'services': {'app': {'volumes': [
            {'type': 'bind', 'source': snapshot['ownedDataDirectory'], 'target': '/tmp/probe-data', 'readOnly': False},
            {'type': 'volume', 'source': volume, 'target': '/tmp/probe-volume', 'readOnly': False}]}},
            'environment': {'set': {'SYNTHETIC_VALUE': marker}, 'remove': []}, 'apply': True}
        ticket = api.stage_secret(json.dumps(patch))
        plan = api.post('applications/configuration/plan', {'resourceId': resource,
            'expectedFingerprint': snapshot['fingerprint'], 'inputTicket': ticket}, 200).json()['planHash']
        job = api.post('applications/configure', {'resourceId': resource,
            'parameters': {'slug': slug, 'inputKind': 'environment', 'inputTicket': ticket, 'expectedFingerprint': snapshot['fingerprint']},
            'idempotencyKey': 'backup-config:' + uuid.uuid4().hex, 'planHash': plan, 'confirmed': True}, 202).json()
        jobs.append(job['id'])
        assert terminal(job['id'])['state'] == 'Succeeded'
        configured = True
        native(['exec', '--user', '0', container, '/bin/sh', '-c',
            'printf bind-proof > /tmp/probe-data/proof.txt; printf volume-proof > /tmp/probe-volume/proof.txt'])
        time.sleep(7)  # Allow the existing six-second discovery cycle to persist the new application resource.
        original = json.loads(native(['inspect', '--type', 'container', container]))[0]
        first_id = enqueue('backup', {'slug': slug, 'consistency': 'quiesced'})
        first = terminal(first_id)
        assert first['state'] == 'Succeeded', first.get('errorCode')
        result = json.loads(first['resultJson'])
        assert result['dataIncluded'] and result['namedVolumes'] == [volume] and result['transactionConsistent'] is False
        archive_id = uuid.UUID(first_id).hex
        assert result['backupId'] == archive_id and result['consistency'] == 'quiesced'
        current = json.loads(native(['inspect', '--type', 'container', container]))[0]
        assert current['Id'] == original['Id'] and current['State']['StartedAt'] == original['State']['StartedAt'] and not current['State']['Paused']
        assert reader(['tar', '-xOzf', '/backup/' + archive_id + '.tar.gz', 'configuration/.env']).find(marker) >= 0
        assert reader(['tar', '-xOzf', '/backup/' + archive_id + '.tar.gz', 'data/proof.txt']) == 'bind-proof'
        assert reader(['tar', '-xOzf', '/backup/' + archive_id + '.tar.gz', 'volumes/' + volume + '/proof.txt']) == 'volume-proof'
        modes = reader(['stat', '-c', '%a', '/backup/' + archive_id + '.tar.gz', '/backup/' + archive_id + '.json'])
        assert modes == '600\n600\n'
        records = request(api, 'GET', 'backups/records?instanceResourceId=' + resource).json()
        record = next(item for item in records if item['jobId'] == first_id)
        assert record['status'] == 'Succeeded' and record['checksum'] == result['checksum'] and record['verifiedAtUtc']
        verify_id = enqueue('verify-backup', {'slug': slug, 'backupId': archive_id})
        assert terminal(verify_id)['state'] == 'Succeeded'

        # One bounded incompressible file makes the pause and cancellation/restart windows observable.
        native(['exec', '--user', '0', container, '/bin/sh', '-c',
            'dd if=/dev/urandom of=/tmp/probe-data/large.bin bs=1M count=256 2>/dev/null'], timeout=90)
        cancellation_id = enqueue('backup', {'slug': slug, 'consistency': 'quiesced'})
        deadline = time.monotonic() + 60
        while observe(cancellation_id)['state'] == 'Queued' and time.monotonic() < deadline:
            time.sleep(1)
        response = session.post(base + '/api/v1/jobs/' + cancellation_id + '/cancel', headers=api.headers, json={}, timeout=30)
        assert response.status_code in {200, 202, 204, 409}, response.status_code
        canceled = terminal(cancellation_id)
        assert canceled['state'] in {'Canceled', 'Succeeded'}, canceled.get('errorCode')
        assert not json.loads(native(['inspect', '--type', 'container', container]))[0]['State']['Paused']

        reconnect_id = enqueue('backup', {'slug': slug, 'consistency': 'quiesced'})
        unit = 'whaledeck-application-backup-' + uuid.UUID(reconnect_id).hex + '.service'
        deadline = time.monotonic() + 60
        live = False
        while time.monotonic() < deadline:
            check = subprocess.run(['systemctl', 'is-active', unit], check=False, text=True,
                stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=10)
            if check.stdout.strip() == 'active':
                live = True
                break
            if observe(reconnect_id)['state'] in {'Failed', 'Succeeded', 'Canceled'}: break
            time.sleep(1)
        assert live, 'The exact independent backup unit was not observed live'
        properties = ['ActiveState', 'MemoryMax', 'CPUQuotaPerSecUSec', 'IOWeight', 'NoNewPrivileges',
            'ProtectSystem', 'ProtectHome', 'PrivateTmp', 'PrivateDevices', 'RestrictAddressFamilies']
        output = subprocess.run(['systemctl', 'show', unit, *['--property=' + name for name in properties]],
            check=True, text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=15).stdout
        sandbox = dict(line.split('=', 1) for line in output.splitlines() if '=' in line)
        assert sandbox['ActiveState'] == 'active' and sandbox['MemoryMax'] == str(256 * 1024**2)
        assert sandbox['CPUQuotaPerSecUSec'] == '500ms' and sandbox['IOWeight'] == '50'
        assert all(sandbox[name] == 'yes' for name in ['NoNewPrivileges', 'ProtectHome', 'PrivateTmp', 'PrivateDevices'])
        assert sandbox['ProtectSystem'] == 'strict' and sandbox['RestrictAddressFamilies'] == 'AF_UNIX'
        api.run('systemd-restart', 'whaledeck.agent', {}, area='host', confirmed=True, timeout=120)
        reconnected = terminal(reconnect_id)
        assert reconnected['state'] == 'Succeeded', reconnected.get('errorCode')
        assert not json.loads(native(['inspect', '--type', 'container', container]))[0]['State']['Paused']
        count = query(f"SELECT json_build_object('jobs',(SELECT count(*) FROM operation_jobs WHERE request_json::text LIKE '%{marker}%'), 'audit',(SELECT count(*) FROM audit_events WHERE detail_json::text LIKE '%{marker}%'));")
        assert count == {'jobs': 0, 'audit': 0}
        anonymous = requests.Session(); anonymous.trust_env = False
        assert anonymous.get(base + '/api/v1/backups/records?instanceResourceId=' + resource, timeout=20).status_code == 401
        print(json.dumps({'applicationBackups': 'passed', 'archiveConfigurationAndDataAndVolume': 'native-exact',
            'archivePrivateModes': '0600', 'runtimeIdentity': 'retained', 'recordAndChecksum': 'passed',
            'verify': 'passed', 'cancel': canceled['state'], 'agentRestart': 'same-backup-job-completed', 'liveSandbox': sandbox,
            'nativePostgresMarkerCounts': count, 'anonymous': '401'}))
    finally:
        # Never tear down an application while its independent task is still running.
        for job_id in jobs:
            value = observe(job_id)
            if value['state'] not in {'Succeeded', 'Failed', 'Canceled', 'RolledBack'}:
                session.post(base + '/api/v1/jobs/' + job_id + '/cancel', headers=api.headers, json={}, timeout=30)
                terminal(job_id, timeout=360)
            # The acceptance script must not trust a faulty DB projection enough
            # to remove files while the original independent archive still runs.
            unit = 'whaledeck-application-backup-' + uuid.UUID(job_id).hex + '.service'
            deadline = time.monotonic() + 360
            while True:
                active = subprocess.run(['systemctl', 'show', unit, '--property=ActiveState', '--value'],
                    check=True, text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=10).stdout.strip()
                if active not in {'active', 'activating', 'deactivating'}: break
                if time.monotonic() >= deadline: raise RuntimeError('Original archive service is still active; application data retained')
                time.sleep(2)
        if installed:
            api.run('uninstall', '', {'slug': slug, 'deleteVolumes': 'true'}, area='applications', confirmed=True, requires_plan=True, timeout=120)
            installed = False
        if ever_installed:
            # Both paths are generated by this script and validated against their exact namespace.
            assert re.fullmatch(r'wdv-backup-[a-f0-9]{12}', slug)
            retained = '/data/WhaleDeck/apps/.retained'
            names = native(['run', '--rm', '--network', 'none', '--read-only', '--cap-drop', 'ALL', '--user', '997:975', '--mount',
                'type=bind,source=' + retained + ',target=/retained,readonly', 'alpine:3.22',
                'find', '/retained', '-mindepth', '1', '-maxdepth', '1', '-name', slug + '-*']).splitlines()
            for path in names:
                name = path.rsplit('/', 1)[-1]
                assert re.fullmatch(re.escape(slug) + '-[a-f0-9]{32}', name)
                native(['run', '--rm', '--network', 'none', '--read-only', '--cap-drop', 'ALL', '--cap-add', 'DAC_OVERRIDE',
                    '--mount', 'type=bind,source=' + retained + ',target=/retained', 'alpine:3.22', 'rm', '-rf', '--', '/retained/' + name])
            native(['run', '--rm', '--network', 'none', '--read-only', '--cap-drop', 'ALL', '--mount',
                'type=bind,source=/data/DockerData/whaledeck/application-backups,target=/backups',
                'alpine:3.22', 'rm', '-rf', '--', '/backups/' + slug])
        print(json.dumps({'temporaryApplicationBackupCleanup': 'passed'}))


if __name__ == '__main__': main()
