#!/usr/bin/env python3
"""Actual visible Authentik/Vue -> persistent workflow -> installed Root/HDD.

Only one exactly owned no-network, non-root container and synthetic HDD tree.
Each case pauses until physical bytes/inodes/metadata and permanent proofs were
read. No SQL/cache writes, Cookie injection, broad cleanup or automatic rollback.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import sys
import time
import uuid

import validate_container_file_publishing as owned
from validate_container_file_staging import load
from validate_container_upload_bff import login, logout, request, temporary_user
from validate_container_upload_recovery_agent import trusted
from validate_container_recursive_ownership_agent import STATE
from validate_container_recursive_permission_agent import no_active_work as old_no_active_work
from validate_container_recursive_ownership import PATHS, inventory, remove_proved
from verify_container_recursive_permissions import digest, unsigned
from validate_databases import Api
from validate_metric_continuity import query

PHASE = 'startup'
CASES = ('complete', 'noop', 'alternate', 'zero', 'readable', 'cancel')
OWNERS = ((2200, 3200), (2200, 3200), (2300, 3300), (0, 0), (2200, 3200), (2400, 3400))
CHANGES = (3, 0, 4, 4, 4, 4)
SOURCES = ('validate_container_recursive_ownership_browser.py', 'validate_container_recursive_ownership_browser.mjs', 'verify_container_recursive_ownership_browser.py')


def no_active_work():
    result = old_no_active_work()
    extra = query("SELECT json_build_object('recursivePermission',(SELECT count(*) FROM container_recursive_permission_intents WHERE completed_at_utc IS NULL OR publish_barrier OR lease_token IS NOT NULL),'recursiveOwnership',(SELECT count(*) FROM container_recursive_ownership_intents WHERE completed_at_utc IS NULL OR publish_barrier OR lease_token IS NOT NULL));")
    assert all(value == 0 for value in extra.values()), 'OTHER_RECURSIVE_WORK_ACTIVE'
    return {**result, **extra}


def snapshot(identifier):
    assert str(uuid.UUID(identifier)) == identifier
    return query('''SELECT json_build_object('row',to_jsonb(c),'planJob',to_jsonb(p),'executionJob',to_jsonb(e),
      'pending',(SELECT count(*) FROM outbox_messages o WHERE o.payload_json->>'jobId' IN (c.id::text,c.execution_job_id::text) AND o.processed_at_utc IS NULL),
      'accepted',(SELECT count(*) FROM audit_events a WHERE a.job_id=c.id AND a.action='containers.files.recursive-ownership.plan-accepted'),
      'confirmed',(SELECT count(*) FROM audit_events a WHERE a.job_id=c.execution_job_id AND a.action='containers.files.recursive-ownership.confirmed'),
      'events',(SELECT count(*) FROM operation_job_events j WHERE j.job_id IN (c.id,c.execution_job_id)))
      FROM container_recursive_ownership_intents c JOIN operation_jobs p ON p.id=c.id LEFT JOIN operation_jobs e ON e.id=c.execution_job_id WHERE c.id=:'id'::uuid;''', id=identifier)


def managed_plan(value):
    cmd = value['Command']; loc = cmd['Location']; source = value['RootIdentity']; parent = value['ParentIdentity']
    assert all(type(cmd[key]) is int and 0 <= cmd[key] < 4294967295 for key in ('Uid','Gid'))
    return {'schemaVersion': value['SchemaVersion'], 'command': {'containerId': loc['ContainerId'], 'instanceId': loc['InstanceId'], 'path': loc['Path'], 'uid': cmd['Uid'], 'gid': cmd['Gid'], 'expectedVersion': cmd['ExpectedVersion']},
        'targetProof': value['TargetProof'], 'parentIdentity': [parent['Device'], parent['Inode']], 'parentVersion': value['ParentVersion'], 'uid': value['OwnerUid'], 'gid': value['OwnerGid'], 'fingerprint': value['Fingerprint'],
        'rootIdentity': [source[k] for k in ('Device', 'Inode', 'Mode', 'OwnerUid', 'OwnerGid', 'LinkCount', 'SizeBytes', 'ModifiedAtUnixNs')] + [value['RootChangedAtUnixNs']],
        **{key[0].lower()+key[1:]: value[key] for key in ('ManifestDigest', 'EntryCount', 'FileCount', 'DirectoryCount', 'ChangeCount')}}


def version(baseline, path, identity):
    return hashlib.sha256(b'whaledeck-container-file-v1\n' + json.dumps([baseline['containerId'], baseline['instanceId'], path, *identity], ensure_ascii=False, separators=(',', ':')).encode()).hexdigest().upper()


def read_metadata(files, storage, configuration, fd, name, limit=16*1024**2):
    descriptor = files.open_at(fd, name, os.O_RDONLY)
    try:
        storage.attest(configuration, descriptor, read_only=True); info = os.fstat(descriptor)
        assert stat.S_ISREG(info.st_mode) and info.st_nlink == 1 and info.st_uid == 0 and stat.S_IMODE(info.st_mode) == 0o600 and 0 < info.st_size <= limit
        chunks = []; total = 0
        while True:
            raw = os.read(descriptor, min(65536, limit + 1 - total))
            if not raw: break
            chunks.append(raw); total += len(raw); assert total <= limit
        assert total == info.st_size
        return b''.join(chunks)
    finally: os.close(descriptor)


def verify_case(result, baseline, subject, evidence, reader, storage, before, after):
    index = CASES.index(result['name']); data = snapshot(result['id']); row = data['row']; plan = managed_plan(json.loads(row['plan_json']))
    assert row['actor_subject'] == subject and row['container_id'] == baseline['containerId'] and row['instance_id'] == baseline['instanceId']
    assert row['relative_path'] == result['command']['location']['path'] == 'work/recursive-tree' and row['state'] == result['state']
    assert row['completed_at_utc'] and not row['publish_barrier'] and row['lease_token'] is None and row['lease_expires_at_utc'] is None
    assert data['pending'] == 0 and data['accepted'] == 1 and data['planJob']['state'] == 'Succeeded'
    assert plan['rootIdentity'] == before[''] and plan['command']['expectedVersion'] == version(baseline, 'work/recursive-tree', before[''])
    assert [plan['command']['uid'], plan['command']['gid']] == list(OWNERS[index])
    assert [plan['entryCount'], plan['fileCount'], plan['directoryCount'], plan['changeCount']] == [4, 2, 2, CHANGES[index]]
    assert [result['entryCount'], result['fileCount'], result['directoryCount'], result['changeCount']] == [4, 2, 2, CHANGES[index]]
    assert [result['rootUid'], result['rootGid'], result['executorUid'], result['executorGid']] == [*before[''][3:5], 1000, 1000]
    assert plan['manifestDigest'] == result['manifestDigest'] and [plan['uid'], plan['gid']] == [1000, 1000]
    (evidence/(result['name']+'.sql.json')).write_text(json.dumps(data))
    if result['name'] == 'cancel':
        assert row['state'] == 'Canceled' and row['execution_job_id'] is None and row['root_started_at_utc'] is None and data['executionJob'] is None and data['confirmed'] == 0
        assert row['receipt_json'] is None and row['root_state'] is None and not row['history_may_exist'] and not row['history_verified'] and row['history_digest'] is None
        assert result['receipt'] is None and after == before
        return {'result': result, 'before': before, 'after': after, 'children': 0}
    assert row['state'] == data['executionJob']['state'] == 'Succeeded' and data['confirmed'] == 1 and data['events'] >= 4
    assert row['execution_job_id'] == result['executionJobId'] and row['root_started_at_utc'] and row['history_verified'] and row['history_may_exist']
    operation = uuid.UUID(row['execution_job_id']).hex; path = STATE/(operation+'.json'); trusted(path)
    assert stat.S_IMODE(path.stat().st_mode) == 0o600 and path.stat().st_size <= 65536
    raw = path.read_bytes(); record = json.loads(raw)
    assert type(record['schemaVersion']) is int and record['schemaVersion'] == 1
    assert all(type(record[key]) is int for key in ('completedCount','changedCount','abortedCount','notStartedCount','unknownCount'))
    assert record['operationId'] == operation and record['owner'] == row['owner_hash'] and record['plan'] == plan and record['state'] == 'Completed'
    assert plan['fingerprint'] == digest(b'whaledeck-container-recursive-ownership-plan-v1\n', unsigned(plan, 'fingerprint'))
    result_receipt = result['receipt']; sql_receipt = json.loads(row['receipt_json'])
    assert sql_receipt['Id'] == result_receipt['id'] == row['execution_job_id'] and sql_receipt['State'] == result_receipt['state'] == row['root_state'] == 'Completed'
    for key in ('completedCount', 'changedCount', 'abortedCount', 'notStartedCount', 'unknownCount'):
        column = re.sub(r'(?<!^)(?=[A-Z])', '_', key).lower()
        assert record[key] == result_receipt[key] == sql_receipt[key[0].upper()+key[1:]] == row[column]
    assert [record[k] for k in ('completedCount', 'changedCount', 'abortedCount', 'notStartedCount', 'unknownCount')] == [4, CHANGES[index], 0, 0, 0]
    assert result_receipt['bindingCurrent'] and result_receipt['historyAvailable'] and result_receipt['historyVerified']
    assert record['historyVerified'] and record['historyMayExist'] and record['historyDigest'] == row['history_digest'] == result_receipt['historyDigest']
    (evidence/(result['name']+'.root.json')).write_bytes(raw)
    outcomes = []
    with storage.target(create_namespace=False, read_only=True) as (configuration, base):
        fd = storage.f.open_at(base, 'container-recursive-ownership-v1', os.O_RDONLY|os.O_DIRECTORY)
        try:
            storage.attest(configuration, fd, read_only=True)
            saved_raw = read_metadata(storage.f, storage, configuration, fd, operation+'.json'); saved = json.loads(saved_raw)
            assert saved['resourceType'] == 'ContainerRecursiveOwnershipMetadata' and saved['storageId'] == 'database-platform.backup.hdd'
            assert saved['operationId'] == operation and saved['owner'] == row['owner_hash'] and saved['plan'] == plan
            assert saved['historyDigest'] == record['historyDigest'] == digest(b'whaledeck-container-recursive-ownership-history-v1\n', unsigned(saved, 'historyDigest'))
            assert [entry['relativePath'] for entry in saved['entries']] == ['sub/中文.bin', 'one.bin', 'sub', '']
            assert plan['manifestDigest'] == digest(b'whaledeck-container-recursive-ownership-manifest-v1\n', saved['entries'])
            (evidence/(result['name']+'.hdd.json')).write_bytes(saved_raw)
            child_fd = storage.f.open_at(fd, operation+'/entries', os.O_RDONLY|os.O_DIRECTORY)
            try:
                storage.attest(configuration, child_fd, read_only=True)
                for n, entry in enumerate(saved['entries']):
                    child_id = hashlib.sha256(b'whaledeck-recursive-ownership-entry-id-v1:'+operation.encode()+b':'+str(n).encode()).hexdigest()[:32]
                    child_path = STATE/(operation+'.entries')/(child_id+'.json'); trusted(child_path)
                    child_raw = child_path.read_bytes(); assert len(child_raw) <= 16384; child = json.loads(child_raw)
                    relative = entry['relativePath']; child_plan = entry['plan']; old = before[relative]; actual = after[relative]
                    assert child['state'] == 'Completed' and child['ownerConfirmed'] is True and child['historyVerified'] is True
                    assert child['operationId'] == child_id and child['owner'] == row['owner_hash'] and child['plan'] == child_plan and child['actualIdentity'] == actual
                    assert child_plan['sourceIdentity'] == old and child_plan['command']['path'] == 'work/recursive-tree'+('/'+relative if relative else '')
                    assert child_plan['fingerprint'] == digest(b'whaledeck-container-ownership-plan-v1\n', unsigned(child_plan, 'fingerprint'))
                    wanted = list(old); wanted[3:5] = list(OWNERS[index])
                    assert actual[:8] == wanted[:8]
                    assert [child_plan['command']['uid'],child_plan['command']['gid']] == list(OWNERS[index])
                    assert child_plan['noChange'] == (old[3:5] == wanted[3:5])
                    if child_plan['noChange']: assert actual == old
                    hdd_raw = read_metadata(storage.f, storage, configuration, child_fd, child_id+'.json', 16384); hdd = json.loads(hdd_raw)
                    assert hdd['resourceType'] == 'ContainerRecursiveOwnershipEntryMetadata' and hdd['parentOperationId'] == operation and hdd['manifestDigest'] == plan['manifestDigest']
                    assert hdd['operationId'] == child_id and hdd['owner'] == row['owner_hash'] and hdd['plan'] == child_plan
                    assert hdd['historyDigest'] == child['historyDigest'] == digest(b'whaledeck-container-recursive-ownership-entry-history-v1\n', unsigned(hdd, 'historyDigest'))
                    published = version(baseline, child_plan['command']['path'], actual); assert published == child['version']
                    outcomes.append({'id': child_id, 'state': 'Completed', 'version': published, 'changed': not child_plan['noChange']})
                    (evidence/(result['name']+'.'+child_id+'.root.json')).write_bytes(child_raw); (evidence/(result['name']+'.'+child_id+'.hdd.json')).write_bytes(hdd_raw)
            finally: os.close(child_fd)
        finally: os.close(fd)
    assert record['outcomeDigest'] == row['outcome_digest'] == result_receipt['outcomeDigest'] == digest(b'whaledeck-recursive-ownership-outcomes-v1\n', outcomes)
    if result['name'] == 'noop': assert after == before
    return {'result': result, 'before': before, 'after': after, 'children': 4, 'rootSha256': hashlib.sha256(raw).hexdigest()}


def unprivileged(base, baseline, original):
    endpoint = base+'/api/v1/containers/'+baseline['containerId']+'/files/recursive-ownerships'; recovery = base+'/api/v1/files/container-recursive-ownerships/recovery'
    paths = [endpoint+'/'+original['id'], endpoint+'/by-key/'+str(uuid.uuid4()), recovery, recovery+'/'+original['id']]
    anonymous = __import__('requests').Session(); anonymous.trust_env = False
    try:
        for path in paths: request(anonymous, 'GET', path, 401)
    finally: anonymous.close()
    with temporary_user(False) as (username, password):
        session, _ = login(base, username, password, False)
        try:
            api = Api(base, session)
            for path in paths: request(session, 'GET', path, 403)
            body = {'instanceId': baseline['instanceId'], 'path': 'work/recursive-tree', 'uid': 2200, 'gid': 3200, 'expectedVersion': baseline['sourceVersion']}
            request(session, 'POST', endpoint, 403, headers={**api.headers, 'Idempotency-Key':str(uuid.uuid4())}, json=body)
            for action in ('confirm','cancel'):
                request(session, 'POST', endpoint+'/'+original['id']+'/'+action, 403, headers={**api.headers,'Idempotency-Key':original['id']}, json={'planHash':'not-authority','confirmed':True,'version':original['version']} if action=='confirm' else None)
            request(session, 'POST', recovery+'/'+original['id']+'/acknowledge', 403, headers={**api.headers,'Idempotency-Key':original['id']}, json={'version':original['version'],'confirmed':True})
            logout(base, session)
        finally: session.close()


def wait_case(process, evidence, name):
    assert name in CASES
    deadline = time.monotonic()+100
    path = evidence/(name+'-result.json')
    while not path.exists():
        assert process.poll() is None, 'ACTUAL_RECURSIVE_BROWSER_EXITED'
        assert time.monotonic() < deadline, 'RECURSIVE_BROWSER_CASE_DEADLINE'
        time.sleep(0.1)
    trusted(path); assert path.stat().st_size <= 32768
    return json.loads(path.read_bytes())


def main():
    global PHASE
    assert os.geteuid() == 0 and len(sys.argv) == 2; os.umask(0o077); os.environ['GIT_OPTIONAL_LOCKS']='0'
    revision = sys.argv[1]; assert re.fullmatch('[a-f0-9]{40}', revision)
    repo = Path(__file__).resolve().parents[2]; base = 'http://127.0.0.1:8080'
    assert not subprocess.check_output(['git','-C',str(repo),'status','--porcelain']).strip(), 'FREEZE_VALIDATION_SOURCE_FIRST'
    validation_revision = subprocess.check_output(['git','-C',str(repo),'rev-parse','HEAD'], text=True).strip()
    hashes = {}
    for name in SOURCES:
        frozen = subprocess.check_output(['git','-C',str(repo),'show',validation_revision+':deploy/validation/'+name])
        assert (repo/'deploy/validation'/name).read_bytes() == frozen; hashes[name] = hashlib.sha256(frozen).hexdigest()
    no_active_work(); active = query("SELECT json_build_object('n',count(*)) FROM container_recursive_ownership_intents WHERE completed_at_utc IS NULL OR publish_barrier;"); assert active['n'] == 0
    before = owned.common.core_snapshot(); assert len(before) == 11
    evidence = repo/'deploy/artifacts'/('container-recursive-ownership-browser-'+uuid.uuid4().hex); evidence.mkdir(mode=0o700)
    reader = load('/usr/local/libexec/whaledeck-container-files.py','recursive_browser_reader')
    files = reader.trusted_module('whaledeck-files.py','recursive_browser_files')
    storage = load('/usr/local/libexec/whaledeck-file-backups.py','recursive_browser_storage').FileBackups(files)
    with owned.fixture() as fixture:
        PHASE = 'owned-tree'; root = fixture['folder']/'recursive-tree'; root.mkdir(mode=0o750); (root/'sub').mkdir(mode=0o700)
        content = hashlib.shake_256(('recursive-browser:'+fixture['identifier']).encode()).digest(2*1024**2+7)
        owners = {'':(2100,3100),'one.bin':(2200,3200),'sub':(2101,3101),'sub/中文.bin':(2201,3201)}
        for relative, kind in PATHS.items():
            path = root/relative
            if kind == 'File': path.write_bytes(content); os.setxattr(path,'user.whaledeck-recursive',b'preserve ordinary attribute')
            os.chown(path,*owners[relative]); path.chmod(0o750 if relative=='' else 0o700 if kind=='Directory' else 0o640 if relative=='one.bin' else 0o600)
        for relative in PATHS: os.utime(root/relative, ns=(1700000000123456789,1700000000123456789))
        inspected = json.loads(owned.common.command(['docker','inspect',fixture['identifier']]))[0]
        original = inventory(root,reader,content)
        baseline = {'revision':revision,'validationSourceRevision':validation_revision,'validationSourceSha256':hashes,'containerId':fixture['identifier'],'containerName':inspected['Name'].lstrip('/'),'instanceId':str(inspected['State']['Pid'])+':'+inspected['State']['StartedAt'],
            'source':str(fixture['folder']),'root':str(root),'original':original,'sha256':hashlib.sha256(content).hexdigest().upper(),'bytesPerFile':len(content),'coreBefore':before,'evidence':str(evidence)}
        baseline['sourceVersion'] = version(baseline,'work/recursive-tree',original[''])
        with storage.target(create_namespace=False,read_only=True) as (configuration,base_fd):
            storage.attest(configuration,base_fd,read_only=True)
            baseline['sourceDevice'] = root.stat().st_dev; baseline['hddDevice'] = os.fstat(base_fd).st_dev
        (evidence/'baseline.json').write_text(json.dumps(baseline))
        results = []; current = original
        with temporary_user(True) as (username,password):
            environment = {**os.environ,'WHALEDECK_URL':base,'VALIDATION_USERNAME':username,'VALIDATION_PASSWORD':password}
            with (evidence/'browser.stdout').open('w') as out, (evidence/'browser.stderr').open('w') as err:
                process = subprocess.Popen(['node',str(repo/'deploy/validation/validate_container_recursive_ownership_browser.mjs'),revision],stdin=subprocess.PIPE,stdout=out,stderr=err,env=environment,text=True)
                try:
                    process.stdin.write(json.dumps(baseline)); process.stdin.close()
                    for name in CASES:
                        PHASE = 'actual-ui-physical-proof-'+name
                        signal = wait_case(process,evidence,name); result = signal['result']; assert result['name'] == name and result['confirmation'] is None
                        after = inventory(root,reader,content)
                        proof = verify_case(result,baseline,signal['subject'],evidence,reader,storage,current,after); results.append(proof); current = after
                        (evidence/'results.json').write_text(json.dumps(results)); (evidence/'physical-final.json').write_text(json.dumps(current))
                        target = evidence/(name+'-verified.json'); pending = evidence/(name+'-verified.pending')
                        with pending.open('x') as stream: stream.write(json.dumps({'id':result['id'],'physicalAndHistoryVerified':True}))
                        pending.replace(target)
                    assert process.wait(timeout=60) == 0, 'ACTUAL_RECURSIVE_BROWSER_FAILED'
                finally:
                    if process.poll() is None:
                        process.terminate()
                        try: process.wait(timeout=15)
                        except subprocess.TimeoutExpired: process.kill(); process.wait(timeout=10)
            browser = json.loads((evidence/'browser.json').read_bytes()); assert browser['normalDualLogout'] and browser['registrations'] == 6 and browser['confirmations'] == 5 and browser['contentUploads'] == 0
            assert browser['completed'] == [p['result'] for p in results]
            PHASE = 'ordinary-and-anonymous'; unprivileged(base,baseline,browser['completed'][0])
        PHASE = 'proved-source-cleanup'; assert inventory(root,reader,content) == current
        remove_proved(root,reader,content,current)
    PHASE = 'independent-cleanup-preconditions'
    assert owned.common.core_snapshot() == before and not fixture['folder'].exists()
    no_active_work(); active = query("SELECT json_build_object('n',count(*)) FROM container_recursive_ownership_intents WHERE completed_at_utc IS NULL OR publish_barrier OR lease_token IS NOT NULL;"); assert active['n'] == 0
    units = subprocess.check_output(['systemctl','list-units','--all','--no-legend','whaledeck-container-recursive-ownership-*'],text=True,timeout=15); assert not units.strip()
    verified = {'revision':revision,'actualBrowserAndIdentity':True,'cases':6,'succeeded':5,'canceledBeforeExecution':1,'aggregateHddRecords':5,'childHddRecords':20,'perCasePhysicalBytesAndMetadata':True,'ordinaryDenied':8,'anonymousDenied':4,'normalDualLogout':True,'exactSourceAndContainerAbsent':True,'coreUnchanged':11,'permanentHistoryRetained':True}
    (evidence/'verified.json').write_text(json.dumps(verified))
    print(json.dumps({'actualRecursiveOwnershipBrowser':'passed','evidence':str(evidence),'cases':6}),flush=True)


if __name__ == '__main__':
    try: main()
    except BaseException as error:
        print(json.dumps({'actualRecursiveOwnershipBrowser':'failed','phase':PHASE,'failureType':type(error).__name__}),flush=True)
        raise SystemExit(1) from None
