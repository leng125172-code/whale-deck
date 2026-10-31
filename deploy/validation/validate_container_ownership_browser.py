#!/usr/bin/env python3
"""Real Authentik/browser -> BFF/SQL/Worker/Agent explicit UID/GID ownership gate.

Only an exactly owned non-root container and synthetic HDD content are changed.
No raw SQL/cache writes, session injection or broad cleanup. Permanent task,
Root and HDD metadata history remains; failed content proofs preserve fixtures.
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
from validate_container_file_staging import load
from validate_container_upload_bff import login, logout, request, temporary_user
from validate_container_upload_recovery_agent import trusted
from validate_container_directory_restart import preflight
from validate_databases import Api
from validate_metric_continuity import query

PHASE = 'startup'
ROOT = Path('/var/lib/whaledeck-container-ownership')


def digest(domain, value):
    return hashlib.sha256(domain + json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(',', ':')).encode()).hexdigest().upper()


def snapshot(identifier):
    assert str(uuid.UUID(identifier)) == identifier
    return query('''SELECT json_build_object('row',to_jsonb(c),'planJob',to_jsonb(p),'executionJob',to_jsonb(e),
      'pending',(SELECT count(*) FROM outbox_messages o WHERE o.payload_json->>'jobId' IN (c.id::text,c.execution_job_id::text) AND o.processed_at_utc IS NULL),
      'accepted',(SELECT count(*) FROM audit_events a WHERE a.job_id=c.id AND a.action='containers.files.ownership.plan-accepted'),
      'confirmed',(SELECT count(*) FROM audit_events a WHERE a.job_id=c.execution_job_id AND a.action='containers.files.ownership.confirmed'),
      'events',(SELECT count(*) FROM operation_job_events j WHERE j.job_id IN (c.id,c.execution_job_id)))
      FROM container_ownership_intents c JOIN operation_jobs p ON p.id=c.id LEFT JOIN operation_jobs e ON e.id=c.execution_job_id WHERE c.id=:'id'::uuid;''', id=identifier)


def verify(result, baseline, subject, evidence, reader, storage):
    value = snapshot(result['id']); row = value['row']
    assert row['actor_subject'] == subject and row['container_id'] == baseline['containerId'] and row['instance_id'] == baseline['instanceId']
    assert row['relative_path'] == result['command']['location']['path'] and row['state'] == result['state']
    assert row['completed_at_utc'] and not row['publish_barrier'] and row['lease_token'] is None and row['lease_expires_at_utc'] is None
    assert value['pending'] == 0 and value['accepted'] == 1 and value['planJob']['state'] == 'Succeeded'
    (evidence / (result['name'] + '.sql.json')).write_text(json.dumps(value))
    if result['name'] == 'file-cancel':
        assert row['state'] == 'Canceled' and row['execution_job_id'] is None and row['root_started_at_utc'] is None and value['executionJob'] is None and value['confirmed'] == 0
        assert row['owner_confirmed'] is None and not row['history_may_exist'] and not row['history_verified'] and row['history_digest'] is None
        return None
    assert row['state'] == value['executionJob']['state'] == 'Succeeded' and row['root_started_at_utc']
    assert row['execution_job_id'] == result['executionJobId'] and row['id'] != row['execution_job_id']
    assert row['owner_confirmed'] is result['ownerConfirmed'] is True and row['history_may_exist'] and row['history_verified'] and result['historyVerified']
    assert row['history_digest'] == result['historyDigest'] and row['published_version'] == result['publishedVersion']
    assert value['confirmed'] == 1 and value['events'] >= 4
    operation = uuid.UUID(row['execution_job_id']).hex; path = ROOT / (operation + '.json'); trusted(path)
    assert stat.S_IMODE(path.stat().st_mode) == 0o600 and path.stat().st_size <= 16384
    raw = path.read_bytes(); record = json.loads(raw); plan = record['plan']; managed = json.loads(row['plan_json'])
    assert record['operationId'] == operation and record['owner'] == row['owner_hash'] and record['state'] == 'Completed'
    assert record['ownerConfirmed'] is True and record['historyMayExist'] and record['historyVerified']
    assert record['historyDigest'] == row['history_digest'] and record['version'] == row['published_version']
    assert plan['fingerprint'] == managed['Fingerprint'] == digest(b'whaledeck-container-ownership-plan-v1\n', {k: v for k, v in plan.items() if k != 'fingerprint'})
    assert plan['targetProof'] == managed['TargetProof'] and plan['kind'] == managed['Kind'] == result['kind']
    assert plan['command'] == {'containerId': row['container_id'], 'instanceId': row['instance_id'], 'path': row['relative_path'],
        'uid': managed['Command']['Uid'], 'gid': managed['Command']['Gid'], 'expectedVersion': managed['Command']['ExpectedVersion']}
    assert [plan['command']['uid'], plan['command']['gid']] == [result['command']['uid'], result['command']['gid']] and plan['noChange'] == result['noChange'] == (result['name'] == 'file-noop')
    source = managed['SourceIdentity']
    assert plan['sourceIdentity'] == [source[k] for k in ('Device','Inode','Mode','OwnerUid','OwnerGid','LinkCount','SizeBytes','ModifiedAtUnixNs')] + [managed['SourceChangedAtUnixNs']]
    expected_owners = {'file-owner':[1000,1000], 'file-noop':[2200,3200], 'file-root':[2200,3200], 'file-restore':[0,0], 'directory-owner':[2100,3100], 'directory-restore':[2200,3200]}[result['name']]
    # OwnerUid/Gid bind the container execution identity; SourceIdentity binds
    # the actual inode's owner. Explicit chown changes only the target pair.
    assert plan['attributesDigest'] == managed['AttributesDigest'] and [plan['uid'], plan['gid']] == [managed['OwnerUid'], managed['OwnerGid']] == [1000,1000]
    assert plan['sourceIdentity'][3:5] == expected_owners
    assert [result['originalUid'], result['originalGid']] == expected_owners
    assert [result['executorUid'], result['executorGid']] == [1000,1000]
    target_owners = {'file-owner':[2200,3200], 'file-noop':[2200,3200], 'file-root':[0,0], 'file-restore':[1000,1000], 'directory-owner':[2200,3200], 'directory-restore':[2100,3100]}[result['name']]
    assert [plan['command']['uid'], plan['command']['gid']] == target_owners
    desired = list(plan['sourceIdentity']); desired[3:5] = target_owners
    assert record['actualIdentity'][:8] == desired[:8]
    expected_version = hashlib.sha256(b'whaledeck-container-file-v1\n' + json.dumps([row['container_id'], row['instance_id'], row['relative_path'], *record['actualIdentity']], ensure_ascii=False, separators=(',', ':')).encode()).hexdigest().upper()
    assert record['version'] == expected_version
    original_version = hashlib.sha256(b'whaledeck-container-file-v1\n' + json.dumps([row['container_id'], row['instance_id'], row['relative_path'], *plan['sourceIdentity']], ensure_ascii=False, separators=(',', ':')).encode()).hexdigest().upper()
    assert plan['command']['expectedVersion'] == original_version

    with storage.target(create_namespace=False, read_only=True) as (configuration, base):
        target = Path(os.readlink('/proc/self/fd/' + str(base))) / 'container-ownership-v1'; trusted(target, True)
        history = target / (operation + '.json'); trusted(history); assert stat.S_IMODE(history.stat().st_mode) == 0o600
        descriptor = os.open(history, os.O_RDONLY | os.O_NOFOLLOW)
        try: storage.attest(configuration, descriptor, read_only=True)
        finally: os.close(descriptor)
        saved = json.loads(history.read_bytes()); assert saved['resourceType'] == 'ContainerOwnershipMetadata' and saved['storageId'] == 'database-platform.backup.hdd'
        assert saved['operationId'] == operation and saved['owner'] == record['owner'] and saved['plan'] == plan
        assert saved['historyDigest'] == record['historyDigest'] == digest(b'whaledeck-container-ownership-history-v1\n', {k: v for k, v in saved.items() if k != 'historyDigest'})
        (evidence / (result['name'] + '.hdd.json')).write_bytes(history.read_bytes())
    (evidence / (result['name'] + '.root.json')).write_bytes(raw)
    return record


def unprivileged(base, baseline, original):
    endpoint = base + '/api/v1/containers/' + baseline['containerId'] + '/files/ownerships'
    recovery = base + '/api/v1/files/container-ownerships/recovery'
    paths = [endpoint+'/'+original['id'], endpoint+'/by-key/'+str(uuid.uuid4()), recovery, recovery+'/'+original['id']]
    anonymous = requests.Session(); anonymous.trust_env = False
    for path in paths: request(anonymous, 'GET', path, 401)
    anonymous.close()
    with temporary_user(False) as (username, password):
        session, _ = login(base, username, password, False)
        try:
            api = Api(base, session)
            for path in paths: request(session, 'GET', path, 403)
            body = {'instanceId': baseline['instanceId'], 'path': 'work/original.bin', 'uid': 2200, 'gid': 3200, 'expectedVersion': baseline['sourceVersion']}
            request(session, 'POST', endpoint, 403, headers={**api.headers, 'Idempotency-Key':str(uuid.uuid4())}, json=body)
            for action in ('confirm','cancel'):
                request(session, 'POST', endpoint+'/'+original['id']+'/'+action, 403, headers={**api.headers, 'Idempotency-Key':original['id']},
                    json={'planHash':'not-authority','confirmed':True,'version':original['version']} if action=='confirm' else None)
            request(session, 'POST', recovery+'/'+original['id']+'/acknowledge', 403, headers={**api.headers, 'Idempotency-Key':original['id']}, json={'version':original['version'],'confirmed':True})
            logout(base, session)
        finally: session.close()


def main():
    global PHASE
    assert os.geteuid() == 0 and len(sys.argv) == 2; os.umask(0o077)
    # Git status may otherwise refresh a user-owned index as root/0600.
    # Acceptance only reads the frozen source; it must never replace that index.
    os.environ['GIT_OPTIONAL_LOCKS'] = '0'
    revision = sys.argv[1]; assert re.fullmatch('[a-f0-9]{40}', revision)
    repo = Path(__file__).resolve().parents[2]
    base = os.environ.get('WHALEDECK_URL','http://127.0.0.1:8080'); assert base == 'http://127.0.0.1:8080'
    assert not subprocess.check_output(['git','-C',str(repo),'status','--porcelain']).strip(), 'FREEZE_VALIDATION_SOURCE_FIRST'
    validation_revision = subprocess.check_output(['git','-C',str(repo),'rev-parse','HEAD'],text=True).strip()
    validation_digests = {}
    for name in ('validate_container_ownership_browser.py','validate_container_ownership_browser.mjs','verify_container_ownership_browser.py'):
        relative = 'deploy/validation/' + name
        frozen = subprocess.check_output(['git','-C',str(repo),'show',validation_revision+':'+relative])
        assert (repo/relative).read_bytes() == frozen
        validation_digests[relative] = hashlib.sha256(frozen).hexdigest()
    preflight(); active = query("SELECT json_build_object('n',count(*)) FROM container_ownership_intents WHERE completed_at_utc IS NULL OR publish_barrier;"); assert active['n'] == 0
    before = owned.common.core_snapshot(); assert len(before) == 11
    evidence = repo / 'deploy/artifacts' / ('container-ownership-browser-' + uuid.uuid4().hex); evidence.mkdir(mode=0o700)
    reader = load(Path('/usr/local/libexec/whaledeck-container-files.py'),'browser_ownership_reader')
    files = reader.trusted_module('whaledeck-files.py','browser_ownership_files')
    storage = load(Path('/usr/local/libexec/whaledeck-file-backups.py'),'browser_ownership_hdd').FileBackups(files)
    records = {}; values = None
    with owned.fixture() as fixture:
        PHASE = 'owned-fixture'
        source = fixture['folder'] / 'original.bin'; content = source.read_bytes(); original = list(reader.fingerprint(source.stat()))
        directory = fixture['folder'] / '所有者目录'; directory.mkdir(mode=0o750); directory.chmod(0o750); os.chown(directory,2100,3100)
        child = directory / '子项.bin'; child.write_bytes(content); child.chmod(0o640); os.chown(child,2200,3200)
        folder_before = list(reader.fingerprint(directory.stat())); child_before = list(reader.fingerprint(child.stat()))
        inspected = json.loads(owned.common.command(['docker','inspect',fixture['identifier']]))[0]
        baseline = {'validationSourceRevision':validation_revision,'validationSourceSha256':validation_digests,'revision':revision,'containerId':fixture['identifier'],'containerName':inspected['Name'].lstrip('/'),
            'instanceId':str(inspected['State']['Pid'])+':'+inspected['State']['StartedAt'],'evidence':str(evidence),
            'original':original,'directory':folder_before,'child':child_before,'source':str(fixture['folder']),
            'sha256':hashlib.sha256(content).hexdigest().upper(),'bytes':len(content),'coreBefore':before}
        baseline['sourceVersion'] = reader.version(baseline,'work/original.bin',source.stat())
        (evidence/'baseline.json').write_text(json.dumps(baseline))
        PHASE = 'actual-browser'
        with temporary_user(True) as (username,password):
            environment = {**os.environ,'WHALEDECK_URL':base,'VALIDATION_USERNAME':username,'VALIDATION_PASSWORD':password}
            result = subprocess.run(['node',str(repo/'deploy/validation/validate_container_ownership_browser.mjs'),revision],input=json.dumps(baseline),
                env=environment,text=True,capture_output=True,timeout=480)
            assert len(result.stdout.encode())<=16384 and len(result.stderr.encode())<=16384
            (evidence/'browser.stdout').write_text(result.stdout);(evidence/'browser.stderr').write_text(result.stderr)
            if result.returncode:
                for line in result.stderr.splitlines():
                    try:
                        item=json.loads(line)
                        if item.get('ownershipBrowser')=='failed': print(json.dumps(item),flush=True)
                    except ValueError: pass
            assert result.returncode==0,'ACTUAL_OWNERSHIP_BROWSER_FAILED'
            values=json.loads((evidence/'browser.json').read_bytes()); assert values['revision']==revision and values['containerId']==baseline['containerId']
            assert len(values['completed'])==7 and values['normalDualLogout'] and values['registrations']==7 and values['confirmations']==6 and values['contentUploads']==0
            PHASE='sql-root-hdd-proof'
            for row in values['completed']:
                assert row['confirmation'] is None
                records[row['name']]=verify(row,baseline,values['subject'],evidence,reader,storage)
            PHASE='unprivileged-and-anonymous'
            unprivileged(base,baseline,values['completed'][0])
        PHASE='original-inodes-and-full-bytes'
        after_file=list(reader.fingerprint(source.stat())); after_dir=list(reader.fingerprint(directory.stat()))
        assert after_file[:8]==original[:8] and after_dir[:8]==folder_before[:8]
        assert after_file==records['file-restore']['actualIdentity'] and after_dir==records['directory-restore']['actualIdentity']
        assert records['file-noop']['actualIdentity']==records['file-owner']['actualIdentity']
        assert records['file-root']['plan']['sourceIdentity'] == records['file-noop']['actualIdentity']
        assert records['file-restore']['plan']['sourceIdentity'] == records['file-root']['actualIdentity']
        assert records['directory-restore']['plan']['sourceIdentity'] == records['directory-owner']['actualIdentity']
        assert list(reader.fingerprint(child.stat()))==child_before and source.read_bytes()==child.read_bytes()==content
        assert {p.name for p in directory.iterdir()}=={'子项.bin'}
        (evidence/'after.json').write_text(json.dumps({'file':after_file,'directory':after_dir,'child':child_before}))
        fixture['proofs']['original.bin']=(owned.common.fingerprint(source),hashlib.sha256(content).hexdigest().upper())
        assert list(reader.fingerprint(child.stat()))==child_before;child.unlink()
        assert list(reader.fingerprint(directory.stat()))[:5]==after_dir[:5] and not list(directory.iterdir());directory.rmdir()
    PHASE='final-cleanup-checks'
    assert owned.common.core_snapshot()==before and not fixture['folder'].exists()
    missing=subprocess.run(['docker','inspect',baseline['containerId']],capture_output=True,timeout=10);assert missing.returncode!=0 and b'no such' in missing.stderr.lower()
    preflight();active=query("SELECT json_build_object('n',count(*)) FROM container_ownership_intents WHERE completed_at_utc IS NULL OR publish_barrier;");assert active['n']==0
    units=subprocess.run(['systemctl','list-units','--all','--no-legend','whaledeck-container-ownerships-*'],capture_output=True,text=True,timeout=10);assert units.returncode==0 and not units.stdout.strip()
    verified={'revision':revision,'actualBrowserAndIdentity':True,'cases':7,'succeeded':6,'canceledBeforeExecution':1,'rootAndHddRecords':6,
        'originalInodesOwnersMtimeAndFullBytes':True,'childUnchanged':True,'coreUnchanged':True,'ownedFixtureRemoved':True,
        'ordinaryDenied':8,'anonymousDenied':4,'csrfOriginDenied':True,'normalDualLogout':True,'pendingOwnershipIntents':0,'rootRecordsRetained':True}
    (evidence/'verified.json').write_text(json.dumps(verified))
    for path in evidence.iterdir(): path.chmod(0o600)
    PHASE='done';print(json.dumps({'evidence':str(evidence),'actualOwnershipBrowser':'passed'}),flush=True)


if __name__=='__main__':
    try:main()
    except BaseException as error:
        print(json.dumps({'actualOwnershipBrowser':'failed','phase':PHASE,'failureType':type(error).__name__}),flush=True)
        raise
