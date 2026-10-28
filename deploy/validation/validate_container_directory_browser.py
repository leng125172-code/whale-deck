#!/usr/bin/env python3
"""Installed directory UI -> real identity/BFF/SQL/Worker/Root/filesystem gate.

Uses only an exact disposable non-root/no-network container. Persistent jobs,
audit and Root receipts are retained. A directory is removed only after its
empty inode, owner, permission and original receipt have independently matched.
Unproved objects stay on disk for investigation; no recursive cleanup.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import sys
import uuid

import requests

import validate_container_file_publishing as owned
from validate_container_upload_bff import login, logout, request, temporary_user
from validate_container_upload_recovery_agent import trusted
from validate_databases import Api
from validate_metric_continuity import query

PHASE = 'startup'
ROOT = Path('/var/lib/whaledeck-container-directories')


def snapshot(identifier):
    assert str(uuid.UUID(identifier)) == identifier
    return query('''SELECT json_build_object('row',to_jsonb(d),'job',to_jsonb(j),
      'pending',(SELECT count(*) FROM outbox_messages o WHERE o.payload_json->>'jobId'=d.id::text AND o.processed_at_utc IS NULL),
      'accepted',(SELECT count(*) FROM audit_events a WHERE a.job_id=d.id AND a.action='containers.files.mkdir.accepted'),
      'events',(SELECT count(*) FROM operation_job_events e WHERE e.job_id=d.id),
      'terminalAudits',(SELECT count(*) FROM audit_events a WHERE a.job_id=d.id AND a.action<>'containers.files.mkdir.accepted'))
      FROM container_directory_operations d JOIN operation_jobs j ON j.id=d.id WHERE d.id=:'id'::uuid;''', id=identifier)


def verify_directory(result, baseline, subject, evidence):
    value = snapshot(result['id']); row = value['row']
    assert row['actor_subject'] == subject and row['container_id'] == baseline['containerId']
    assert row['instance_id'] == baseline['instanceId'] and row['relative_path'] == result['path']
    assert row['state'] == value['job']['state'] == 'Succeeded'
    assert row['root_started_at_utc'] is not None and row['completed_at_utc'] is not None
    assert row['result_code'] == 'CONTAINER_DIRECTORY_CREATED' and not row['publish_barrier']
    assert row['lease_token'] is None and row['lease_expires_at_utc'] is None
    assert row['published_version'] == result['publishedVersion'] and re.fullmatch('[A-F0-9]{64}', row['request_hash'])
    assert value['pending'] == 0 and value['accepted'] == 1 and value['terminalAudits'] >= 1 and value['events'] >= 2
    plan = json.loads(row['plan_json']); location = plan['Command']['Location']
    assert location == {'ContainerId': row['container_id'], 'InstanceId': row['instance_id'], 'Path': row['relative_path']}
    operation = uuid.UUID(row['id']).hex; metadata = ROOT / (operation + '.json')
    info = trusted(metadata); assert stat.S_IMODE(info.st_mode) == 0o600 and info.st_size <= 16384
    raw = metadata.read_bytes(); receipt = json.loads(raw)
    assert receipt['state'] == 'Completed' and receipt['operationId'] == operation and receipt['owner'] == row['owner_hash']
    root_plan = receipt['plan']
    assert root_plan['command'] == {'containerId': row['container_id'], 'instanceId': row['instance_id'], 'path': row['relative_path'], 'mode': plan['Command']['Mode']}
    assert root_plan['fingerprint'] == plan['Fingerprint'] and root_plan['targetProof'] == plan['TargetProof']
    assert root_plan['parentVersion'] == plan['ParentVersion'] and (root_plan['uid'], root_plan['gid']) == (1000, 1000)
    unsigned = {key: item for key, item in root_plan.items() if key != 'fingerprint'}
    assert root_plan['fingerprint'] == hashlib.sha256(b'whaledeck-container-directory-plan-v1\n' + json.dumps(unsigned,
        sort_keys=True, separators=(',', ':'), ensure_ascii=False).encode()).hexdigest().upper()
    leaf = row['relative_path'].removeprefix('work/'); assert leaf and '/' not in leaf and row['relative_path'] == 'work/' + leaf
    output = Path(baseline['source']) / leaf; info = output.lstat()
    assert stat.S_ISDIR(info.st_mode) and not output.is_symlink() and output.resolve(strict=True) == output
    assert [info.st_dev, info.st_ino] == receipt['pendingIdentity']
    assert (info.st_uid, info.st_gid, stat.S_IMODE(info.st_mode)) == (1000, 1000, int(plan['Command']['Mode'], 8))
    assert receipt['version'] == row['published_version'] and not list(output.iterdir())
    (evidence / (operation + '.root.json')).write_bytes(raw)
    return output, (info.st_dev, info.st_ino, info.st_uid, info.st_gid, stat.S_IMODE(info.st_mode)), receipt['privateIdentity']


def deny_unprivileged(base, baseline, completed):
    endpoint = base + '/api/v1/containers/' + baseline['containerId'] + '/files/directories'
    anonymous = requests.Session(); anonymous.trust_env = False
    paths = [endpoint + '/' + completed['id'], base + '/api/v1/files/container-directories/recovery',
             base + '/api/v1/files/container-directories/recovery/' + completed['id']]
    for path in paths: request(anonymous, 'GET', path, 401)
    anonymous.close()
    with temporary_user(False) as (username, password):
        session, profile = login(base, username, password, admin=False); assert not profile['isAdministrator']
        headers = {**Api(base, session).headers, 'Idempotency-Key': str(uuid.uuid4())}
        for path in paths: request(session, 'GET', path, 403)
        for path, body in [(endpoint + '/plan', {'instanceId': baseline['instanceId'], 'path': 'work/禁止创建', 'mode': '0750'}),
                           (endpoint, {'confirmed': True, 'planHash': 'denied', 'idempotencyKey': headers['Idempotency-Key']}),
                           (endpoint + '/resolve', {'confirmed': True, 'planHash': 'denied', 'idempotencyKey': headers['Idempotency-Key']}),
                           (endpoint + '/' + completed['id'] + '/cancel', {}),
                           (paths[-1] + '/acknowledge', {'version': 0, 'confirmed': True})]:
            request(session, 'POST', path, 403, headers=headers, json=body)
        logout(base, session); session.close()


def main():
    global PHASE
    assert os.geteuid() == 0 and len(sys.argv) == 2; os.umask(0o077)
    revision = sys.argv[1]; assert re.fullmatch('[a-f0-9]{40}', revision)
    repository = Path(__file__).resolve().parents[2]
    evidence = repository / 'deploy/artifacts' / ('container-directory-browser-' + uuid.uuid4().hex); evidence.mkdir(mode=0o700)
    with owned.fixture() as fixture:
        inspected = json.loads(owned.common.command(['docker', 'inspect', fixture['identifier']]))[0]
        baseline = {'revision': revision, 'containerId': fixture['identifier'], 'containerName': fixture['folder'].name,
                    'source': str(fixture['folder']), 'instanceId': str(inspected['State']['Pid']) + ':' + inspected['State']['StartedAt'], 'evidence': str(evidence)}
        (evidence / 'baseline.json').write_text(json.dumps(baseline))
        PHASE = 'visible-authentik-bff-worker-and-ui'
        child = subprocess.run(['node', str(Path(__file__).with_suffix('.mjs')), revision], input=json.dumps(baseline),
            text=True, capture_output=True, timeout=300, check=False)
        assert len(child.stdout.encode()) <= 32768 and len(child.stderr.encode()) <= 32768
        (evidence / 'browser.stdout').write_text(child.stdout); (evidence / 'browser.stderr').write_text(child.stderr)
        if child.returncode:
            for line in child.stderr.splitlines():
                try:
                    item = json.loads(line)
                    if item.get('installedContainerDirectoryBrowser') == 'failed':
                        print(json.dumps({key: item.get(key) for key in ('installedContainerDirectoryBrowser', 'phase', 'failureType', 'locations')}), flush=True)
                except ValueError: pass
            raise AssertionError('DIRECTORY_BROWSER_NOT_VERIFIED')
        results_path = evidence / 'results.json'; assert stat.S_IMODE(trusted(results_path).st_mode) == 0o600
        results = json.loads(results_path.read_bytes())
        assert results['revision'] == revision and results['fullSsoLogout'] and results['registrations'] == 4
        assert results['exactHttpRetries'] == 4 and results['csrfAndOriginDenials'] == 2
        assert len(results['directories']) == 4 and len({item['id'] for item in results['directories']}) == 4
        PHASE = 'independent-durable-sql-root-inode-owner-mode-proof'
        verified = [verify_directory(result, baseline, results['subject'], evidence) for result in results['directories']]
        assert len({tuple(item[2]) for item in verified}) == 1
        PHASE = 'actual-anonymous-ordinary-permissions'
        deny_unprivileged(os.environ.get('WHALEDECK_URL', 'http://192.168.100.13:8080').rstrip('/'), baseline, results['directories'][0])
        assert not (fixture['folder'] / '禁止创建').exists()
        PHASE = 'exact-empty-directory-cleanup'
        private = fixture['folder'] / '.whaledeck-container-directories-v1'; private_info = trusted(private, True)
        assert [private_info.st_dev, private_info.st_ino] == verified[0][2] and stat.S_IMODE(private_info.st_mode) == 0o700
        assert not list(private.iterdir())
        for output, expected, _ in verified:
            info = output.lstat()
            assert (info.st_dev, info.st_ino, info.st_uid, info.st_gid, stat.S_IMODE(info.st_mode)) == expected
            assert stat.S_ISDIR(info.st_mode) and not list(output.iterdir())
            output.rmdir()
        private.rmdir()
        (evidence / 'verified.json').write_text(json.dumps({'revision': revision, 'directories': results['directories'],
            'independentSqlRootInodeOwnerMode': True, 'normalTwoPageSsoLogout': True, 'ordinaryDeniedRoutes': 8,
            'rootRecordsAndBusinessHistoryRetained': True}))
    print(json.dumps({'installedContainerDirectoryBrowser': 'passed', 'revision': revision, 'directories': 4,
        'desktopMobileLightDark': True, 'ordinaryAndAnonymousDenied': True, 'normalTwoPageSsoLogout': True,
        'ownedContainerAndEmptyDirectoriesCleaned': True, 'evidence': str(evidence)}), flush=True)


if __name__ == '__main__':
    try: main()
    except BaseException as error:
        import traceback
        print(json.dumps({'installedContainerDirectoryBrowser': 'failed', 'phase': PHASE, 'failureType': type(error).__name__,
            'locations': [{'file': Path(frame.filename).name, 'line': frame.lineno} for frame in traceback.extract_tb(error.__traceback__)],
            'unverifiedDataPreserved': True}), file=sys.stderr)
        raise SystemExit(1) from None
