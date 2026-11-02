#!/usr/bin/env python3
"""Installed real-page partial chmod cancellation and different-admin resolution.

No SQL/Root/clock edits or product hooks. All bytes and history are verified
before exact fixture removal. A missed cut or failed page remains a failed gate.
"""
from concurrent.futures import ThreadPoolExecutor
import contextlib
import datetime as dt
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
from bound_container_recursive_permission_cut import ROOT, child_id, freeze_partial, record_for
from bound_container_permission_cut import fingerprint
from validate_container_recursive_permission_browser import snapshot, managed_plan, read_metadata, version
from validate_container_recursive_permission_agent import no_active_work, host_identity
from validate_container_directory_restart import Worker, command, preflight
from validate_container_ownership_faults import write_signal, wait_signal
from validate_container_file_staging import load
from validate_container_upload_bff import temporary_user, login, logout, request
from validate_databases import Api
from validate_metric_continuity import query
from verify_container_recursive_permissions import digest, unsigned
from validate_container_directory_unknown import register as register_waiting, wait_settled as settle_waiting
from validate_container_directory_browser import snapshot as directory_snapshot, ROOT as DIRECTORY_ROOT, verify_directory
from validate_container_directory_restart import clean_success

PHASE = 'startup'
PATHS = {'': 'Directory', 'sub': 'Directory', **{f'sub/entry-{i:02}.bin': 'File' for i in range(16)}}
SOURCES = ('bound_container_recursive_permission_cut.py', 'validate_container_recursive_permission_faults.py',
    'validate_container_recursive_permission_faults.mjs', 'verify_container_recursive_permission_faults.py',
    'bound_container_copy_cut.py', 'bound_container_permission_cut.py', 'validate_container_directory_restart.py',
    'validate_container_ownership_faults.py', 'validate_container_file_publishing.py',
    'validate_container_recursive_permission_browser.py', 'validate_container_recursive_permission_agent.py',
    'validate_container_recursive_permissions.py', 'verify_container_recursive_permissions.py',
    'installed_ui_permission_session.mjs', 'installed_ui_logout.mjs', 'validate_container_directory_unknown.py',
    'validate_container_directory_browser.py', 'validate_container_upload_bff.py', 'validate_databases.py')


def inventory(root, content):
    result = {}
    for relative, kind in PATHS.items():
        path = root/relative; info = path.lstat(); assert not path.is_symlink()
        if kind == 'File':
            assert stat.S_ISREG(info.st_mode) and info.st_nlink == 1 and path.read_bytes() == content
            assert os.listxattr(path) == ['user.whaledeck-recursive']
            assert os.getxattr(path, 'user.whaledeck-recursive') == b'preserve ordinary attribute'
        else:
            assert stat.S_ISDIR(info.st_mode) and os.listxattr(path) == []
            assert {p.name for p in path.iterdir()} == {p.rsplit('/', 1)[-1] for p in PATHS if p and p.rpartition('/')[0] == relative}
        result[relative] = fingerprint(info)
    return result


def unchanged_except_modes(before, after):
    assert before.keys() == after.keys() == PATHS.keys()
    changed = 0
    for relative, old in before.items():
        actual = after[relative]; target = old[2] & ~0o777 | (0o600 if PATHS[relative] == 'File' else 0o700)
        assert actual[:2] == old[:2] and actual[3:8] == old[3:8]
        assert actual[2] in (old[2], target)
        if actual[2] == old[2]: assert actual == old
        else: assert actual[8] > old[8]; changed += 1
    return changed


def remove_proved(root, content, expected):
    assert inventory(root, content) == expected
    for relative in sorted(PATHS, key=lambda p: (p.count('/'), len(p)), reverse=True):
        path = root/relative; actual = fingerprint(path.lstat())
        if PATHS[relative] == 'File': assert actual == expected[relative]; path.unlink()
        else: assert actual[:5] == expected[relative][:5] and not list(path.iterdir()); path.rmdir()


def inactive_except(identifier=None, execution=None):
    assert execution is None or identifier is not None
    preflight(([identifier] if identifier else []) + ([execution] if execution else []))
    value = query('''SELECT json_build_object(
      'copies',(SELECT count(*) FROM container_copy_intents WHERE completed_at_utc IS NULL OR publish_barrier),
      'moves',(SELECT count(*) FROM container_move_intents WHERE completed_at_utc IS NULL OR publish_barrier),
      'permissions',(SELECT count(*) FROM container_permission_intents WHERE completed_at_utc IS NULL OR publish_barrier),
      'ownerships',(SELECT count(*) FROM container_ownership_intents WHERE completed_at_utc IS NULL OR publish_barrier),
      'recursive',(SELECT count(*) FROM container_recursive_permission_intents WHERE (completed_at_utc IS NULL OR publish_barrier) AND id::text<>:'id'));''', id=identifier or '')
    assert all(n == 0 for n in value.values()), 'OTHER_FILE_WORK_REFUSED'
    return value


def restart_allowed(held, row):
    checks = inactive_except(row['id'],row['execution_job_id'])
    active = []
    for path in Path('/run/systemd/transient').glob('whaledeck-*.service'):
        props = dict(line.split('=',1) for line in command('systemctl','show',path.name,'-p','ActiveState','-p','MainPID','-p','ControlPID','-p','ControlGroup').splitlines())
        if props['ActiveState'] in ('inactive','failed'):
            assert props['MainPID'] == props['ControlPID'] == '0'
            if props['ControlGroup']:
                processes = Path('/sys/fs/cgroup')/props['ControlGroup'].lstrip('/')/'cgroup.procs'
                assert not processes.exists() or not processes.read_text().split()
        else: active.append(path.name)
    assert set(active) <= {held.cgroup}, 'OTHER_TRANSIENT_WORK_RESTART_REFUSED'
    return checks


def same_operation(before, after):
    for key in ('id','execution_job_id','root_started_at_utc','actor_subject','owner_hash','container_id','instance_id','relative_path',
        'command_json','request_hash','plan_json','plan_hash','execution_hash','confirmed_at_utc','execution_expires_at_utc'):
        assert before[key] == after[key], 'RECURSIVE_RECOVERY_CHANGED_ORIGINAL_OPERATION'


def lease_held(before, current):
    same_operation(before,current)
    assert current['state'] == 'Applying' and current['publish_barrier'] and current['completed_at_utc'] is None
    assert current['lease_token'] == before['lease_token'] and current['lease_expires_at_utc'] == before['lease_expires_at_utc']
    assert current['cancel_requested_at_utc'] is None


def natural_lease(row, root, expected, directory):
    expiry = dt.datetime.fromisoformat(row['lease_expires_at_utc'])
    assert row['lease_token'] and expiry > dt.datetime.now(dt.timezone.utc)+dt.timedelta(seconds=25)
    raw = (ROOT/(uuid.UUID(row['execution_job_id']).hex+'.json')).read_bytes()
    root_state = json.loads(raw)['state']; assert root_state in ('Applying','Partial')
    # The disconnected RPC may cooperatively save Partial while stopping the
    # helper. Only SQL reclaim waits for the old lease; Root need not stay Applying.
    assert json.loads(raw) == record_for(row)
    (directory/'after-helper-exit-root.json').write_bytes(raw)
    samples = []; began = time.monotonic(); wall = dt.datetime.now(dt.timezone.utc)
    while dt.datetime.now(dt.timezone.utc) < expiry:
        current = snapshot(row['id']); observed = dt.datetime.now(dt.timezone.utc)
        if observed >= expiry: break
        lease_held(row,current['row'])
        assert current['executionJob']['state'] == 'Running' and current['executionJob']['cancel_requested_at_utc'] is None
        assert (ROOT/(uuid.UUID(row['execution_job_id']).hex+'.json')).read_bytes() == raw
        assert {relative:fingerprint((root/relative).lstat()) for relative in PATHS} == expected
        samples.append({'atUtc':observed.isoformat(),'state':current['row']['state'],'barrier':True,
            'leaseToken':current['row']['lease_token'],'leaseExpiresAtUtc':current['row']['lease_expires_at_utc'],
            'rootSha256':hashlib.sha256(raw).hexdigest(),'sourceFingerprintsUnchanged':True})
        time.sleep(min(2,max(0.001,(expiry-observed).total_seconds())))
    ended = dt.datetime.now(dt.timezone.utc); elapsed = time.monotonic()-began
    assert len(samples) >= 3 and abs((ended-wall).total_seconds()-elapsed) < 3, 'NATURAL_LEASE_NOT_OBSERVED'
    proof = {'originalLeaseToken':row['lease_token'],'originalLeaseExpiresAtUtc':row['lease_expires_at_utc'],
        'waitBeganAtUtc':wall.isoformat(),'waitEndedAtUtc':ended.isoformat(),'monotonicWaitSeconds':round(elapsed,3),
        'rootSha256':hashlib.sha256(raw).hexdigest(),'rootStateAfterHelperExit':root_state,'samples':samples,'clocksOrSqlModified':False}
    (directory/'natural-lease.json').write_text(json.dumps(proof))
    return proof


def wait_state(identifier, expected, timeout=115):
    deadline = time.monotonic()+timeout
    while True:
        value = snapshot(identifier)
        # Partial is deliberately unresolved: its original outbox messages stay
        # pending until explicit acknowledgement finishes the execution job.
        if value['row']['state'] == expected: return value
        assert value['row']['state'] in ('Pending', 'Applying', expected), 'UNEXPECTED_RECURSIVE_RESULT'
        assert time.monotonic() < deadline, 'RECURSIVE_RESULT_DEADLINE'
        time.sleep(0.5)


def verify_partial(data, baseline, before, after, storage, directory):
    row = data['row']; record = record_for(row); plan = managed_plan(json.loads(row['plan_json']))
    assert row['state'] == record['state'] == row['root_state'] == 'Partial'
    assert row['root_started_at_utc'] and row['publish_barrier'] and row['completed_at_utc'] is None
    assert row['lease_token'] is None and row['lease_expires_at_utc'] is None
    assert data['planJob']['state'] == 'Succeeded' and data['executionJob']['state'] == 'Running'
    assert data['accepted'] == data['confirmed'] == 1
    assert data['executionJob']['completed_at_utc'] is None and row['history_verified'] and row['history_may_exist']
    assert row['container_id'] == baseline['containerId'] and row['instance_id'] == baseline['instanceId']
    assert plan['rootIdentity'] == before[''] and plan['entryCount'] == plan['changeCount'] == len(PATHS)
    changed = unchanged_except_modes(before, after)
    assert 0 < changed == record['changedCount'] == record['completedCount'] < len(PATHS)
    assert record['unknownCount'] == 0 and record['notStartedCount'] > 0
    receipt = json.loads(row['receipt_json'])
    for key in ('completedCount', 'changedCount', 'abortedCount', 'notStartedCount', 'unknownCount'):
        column = re.sub(r'(?<!^)(?=[A-Z])', '_', key).lower()
        assert receipt[key[0].upper()+key[1:]] == row[column] == record[key]
    assert sum(record[k] for k in ('completedCount','abortedCount','notStartedCount','unknownCount')) == len(PATHS)
    operation = record['operationId']; root_bytes = (ROOT/(operation+'.json')).read_bytes()
    assert plan['fingerprint'] == digest(b'whaledeck-container-recursive-permission-plan-v1\n', unsigned(plan, 'fingerprint'))
    outcomes = []; children = []; raw_files = {'partial.root.json': root_bytes}
    with storage.target(create_namespace=False, read_only=True) as (configuration, base):
        fd = storage.f.open_at(base, 'container-recursive-permissions-v1', os.O_RDONLY|os.O_DIRECTORY)
        try:
            raw = read_metadata(storage.f, storage, configuration, fd, operation+'.json'); saved = json.loads(raw)
            raw_files['partial.hdd.json'] = raw
            assert saved['operationId'] == operation and saved['owner'] == row['owner_hash'] and saved['plan'] == plan
            assert saved['resourceType'] == 'ContainerRecursivePermissionMetadata' and saved['storageId'] == 'database-platform.backup.hdd'
            assert record['historyDigest'] == row['history_digest'] == saved['historyDigest'] == digest(b'whaledeck-container-recursive-permission-history-v1\n', unsigned(saved, 'historyDigest'))
            assert plan['manifestDigest'] == digest(b'whaledeck-container-recursive-permission-manifest-v1\n', saved['entries'])
            assert {e['relativePath'] for e in saved['entries']} == PATHS.keys()
            child_fd = storage.f.open_at(fd, operation+'/entries', os.O_RDONLY|os.O_DIRECTORY)
            try:
                for index, entry in enumerate(saved['entries']):
                    cid = child_id(operation, index); path = ROOT/(operation+'.entries')/(cid+'.json')
                    relative = entry['relativePath']; cp = entry['plan']; actual = after[relative]
                    assert cp['sourceIdentity'] == before[relative] and cp['command']['path'] == 'work/recursive-tree'+('/'+relative if relative else '')
                    assert not cp['noChange'] and cp['fingerprint'] == digest(b'whaledeck-container-permission-plan-v1\n', unsigned(cp, 'fingerprint'))
                    phase = 'NotStarted'; published = ''; changed_entry = False
                    if index <= record['startedThrough']:
                        info = path.lstat(); assert stat.S_ISREG(info.st_mode) and info.st_uid == 0 and stat.S_IMODE(info.st_mode) == 0o600 and info.st_nlink == 1 and info.st_size <= 16384
                        child_raw = path.read_bytes(); child = json.loads(child_raw)
                        assert child['operationId'] == cid and child['owner'] == record['owner'] and child['plan'] == cp
                        phase = child['state']; assert phase in ('Completed', 'Aborted')
                        raw_files[cid+'.root.json'] = child_raw
                        if phase == 'Completed':
                            assert child['actualIdentity'] == actual and child['modeConfirmed'] and child['historyVerified']
                            assert actual[2] != before[relative][2]
                            assert child['version'] == version(baseline,cp['command']['path'],actual)
                            published = child['version']; changed_entry = True
                        else: assert actual == before[relative]
                        if child['historyVerified']:
                            hraw = read_metadata(storage.f, storage, configuration, child_fd, cid+'.json', 16384); hdd = json.loads(hraw)
                            assert hdd['operationId'] == cid and hdd['owner'] == record['owner'] and hdd['plan'] == cp
                            assert hdd['parentOperationId'] == operation and hdd['manifestDigest'] == plan['manifestDigest']
                            assert hdd['resourceType'] == 'ContainerRecursivePermissionEntryMetadata'
                            assert child['historyDigest'] == hdd['historyDigest'] == digest(b'whaledeck-container-recursive-permission-entry-history-v1\n', unsigned(hdd,'historyDigest'))
                            raw_files[cid+'.hdd.json'] = hraw
                        children.append({'id':cid,'state':phase,'historyVerified':child['historyVerified']})
                    else:
                        assert not path.exists() and actual == before[relative]
                    outcomes.append({'id':cid,'state':phase,'version':published if changed_entry else '', 'changed':changed_entry})
                assert {p.stem for p in (ROOT/(operation+'.entries')).glob('*.json')} == {c['id'] for c in children}
                assert {n for n in os.listdir(child_fd) if n.endswith('.json')} == {c['id']+'.json' for c in children if c['historyVerified']}
            finally: os.close(child_fd)
        finally: os.close(fd)
    assert digest(b'whaledeck-recursive-permission-outcomes-v1\n', outcomes) == record['outcomeDigest'] == row['outcome_digest']
    for name, raw in raw_files.items(): (directory/name).write_bytes(raw)
    return record, {'children':children, 'outcomes':outcomes, 'sha256':{k:hashlib.sha256(v).hexdigest() for k,v in raw_files.items()}}


def require_unknown_counts(record, count):
    assert type(count) is int and 1 <= count <= 10000
    assert all(type(record[k]) is int for k in ('completedCount','changedCount','abortedCount','notStartedCount','unknownCount'))
    assert record['state'] == 'Unknown' and [record[k] for k in ('completedCount','changedCount','abortedCount','notStartedCount','unknownCount')] == [0,0,0,0,count]
    assert record['outcomeDigest'] == ''


def require_waiting_deferred(value, identifier, subject):
    row = value['row']; job = value['job']
    assert row['id'] == job['id'] == identifier and row['actor_subject'] == job['actor_subject'] == subject
    assert row['state'] == 'Pending' and row['root_started_at_utc'] is None and not row['publish_barrier'] and row['completed_at_utc'] is None
    assert job['state'] in ('Pending','Running') and job['completed_at_utc'] is None and value['accepted'] == 1


def observe_waiting(waiting, baseline, fixture, subject, unknown, root, content, expected, directory, worker):
    samples = []; output = fixture['folder']/baseline['path'].removeprefix('work/')
    original_raw = (ROOT/(uuid.UUID(unknown['row']['execution_job_id']).hex+'.json')).read_bytes()
    deadline = time.monotonic()+95; previous_version = 0; previous_next = None
    while len(samples) < 4:
        value = directory_snapshot(waiting['id']); require_waiting_deferred(value,waiting['id'],subject)
        assert not output.exists() and not (DIRECTORY_ROOT/(uuid.UUID(waiting['id']).hex+'.json')).exists()
        assert not (fixture['folder']/'.whaledeck-container-directories-v1').exists()
        assert snapshot(unknown['row']['id']) == unknown and unknown['row']['publish_barrier']
        assert {p:fingerprint((root/p).lstat()) for p in PATHS} == expected and (ROOT/(uuid.UUID(unknown['row']['execution_job_id']).hex+'.json')).read_bytes() == original_raw
        row = value['row']; next_attempt = dt.datetime.fromisoformat(row['next_attempt_at_utc'])
        if previous_next is None: previous_next = dt.datetime.fromisoformat(row['created_at_utc'])
        if row['lease_token'] is None and row['lease_expires_at_utc'] is None and row['version'] >= previous_version+2 and (previous_next is None or next_attempt > previous_next):
            assert inventory(root,content) == expected
            running = worker.current(); assert running['State']['Running']
            checks = preflight([unknown['row']['id'],unknown['row']['execution_job_id'],waiting['id']])
            samples.append({'atUtc':dt.datetime.now(dt.timezone.utc).isoformat(),'sql':value,'actualTargetAbsent':True,'rootReceiptAbsent':True,'sourceTreeAndUnknownUnchanged':True,
                'workerId':worker.id,'workerImage':running['Image'],'workerStartedAt':running['State']['StartedAt'],'workerRunning':True,'otherWorkChecks':checks})
            previous_version = row['version']; previous_next = next_attempt
        assert time.monotonic() < deadline, 'FOUR_REAL_DIRECTORY_DEFERRALS_NOT_OBSERVED'
        if len(samples) < 4: time.sleep(1)
    assert samples[-1]['sql']['row']['version'] >= 8
    (directory/'waiting-blocked.json').write_text(json.dumps(samples))
    return samples


def verify_unknown(data, baseline, before, after, storage, directory):
    """Unknown aggregate does not erase independently observed physical changes.

    Keep incomplete original children verbatim; never finish/fence them by
    writing state or pretending lack of current binding proves no mutation.
    """
    row = data['row']; record = record_for(row); plan = managed_plan(json.loads(row['plan_json']))
    require_unknown_counts(record,len(PATHS))
    assert row['state'] == row['root_state'] == 'Unknown' and row['publish_barrier'] and row['root_started_at_utc'] and row['completed_at_utc'] is None
    assert row['lease_token'] is None and row['lease_expires_at_utc'] is None
    assert data['planJob']['state'] == 'Succeeded' and data['executionJob']['state'] == 'Running' and data['executionJob']['completed_at_utc'] is None
    assert data['accepted'] == data['confirmed'] == 1 and row['history_verified'] and row['history_may_exist']
    same_operation(json.loads((directory/'pre-disappearance-sql.json').read_bytes())['row'],row)
    assert plan['rootIdentity'] == before[''] and plan['entryCount'] == plan['changeCount'] == len(PATHS)
    physical_changed = unchanged_except_modes(before,after); assert 0 < physical_changed < len(PATHS)
    assert after == json.loads((directory/'physical-cut.json').read_bytes())
    receipt = json.loads(row['receipt_json'])
    for key in ('completedCount','changedCount','abortedCount','notStartedCount','unknownCount'):
        column = re.sub(r'(?<!^)(?=[A-Z])','_',key).lower()
        assert receipt[key[0].upper()+key[1:]] == row[column] == record[key]
    assert not row['binding_current'] and row['history_available']
    assert plan['fingerprint'] == digest(b'whaledeck-container-recursive-permission-plan-v1\n',unsigned(plan,'fingerprint'))
    operation = record['operationId']; raw_files = {'partial.root.json':(ROOT/(operation+'.json')).read_bytes()}; children = []
    with storage.target(create_namespace=False,read_only=True) as (configuration,base):
        fd = storage.f.open_at(base,'container-recursive-permissions-v1',os.O_RDONLY|os.O_DIRECTORY)
        try:
            raw = read_metadata(storage.f,storage,configuration,fd,operation+'.json'); saved = json.loads(raw); raw_files['partial.hdd.json'] = raw
            assert saved['operationId'] == operation and saved['owner'] == row['owner_hash'] and saved['plan'] == plan
            assert saved['resourceType'] == 'ContainerRecursivePermissionMetadata' and saved['storageId'] == 'database-platform.backup.hdd'
            assert saved['historyDigest'] == record['historyDigest'] == row['history_digest'] == digest(b'whaledeck-container-recursive-permission-history-v1\n',unsigned(saved,'historyDigest'))
            assert plan['manifestDigest'] == digest(b'whaledeck-container-recursive-permission-manifest-v1\n',saved['entries'])
            assert {e['relativePath'] for e in saved['entries']} == PATHS.keys()
            child_fd = storage.f.open_at(fd,operation+'/entries',os.O_RDONLY|os.O_DIRECTORY)
            try:
                names = {n for n in os.listdir(child_fd) if n.endswith('.json')}; expected_root = set(); expected_hdd = set()
                for index,entry in enumerate(saved['entries']):
                    cid = child_id(operation,index); relative = entry['relativePath']; cp = entry['plan']; path = ROOT/(operation+'.entries')/(cid+'.json')
                    assert cp['sourceIdentity'] == before[relative] and cp['command']['path'] == 'work/recursive-tree'+('/'+relative if relative else '')
                    assert cp['fingerprint'] == digest(b'whaledeck-container-permission-plan-v1\n',unsigned(cp,'fingerprint'))
                    child = None
                    if path.exists():
                        assert index <= record['startedThrough']; expected_root.add(cid)
                        info = path.lstat(); assert stat.S_ISREG(info.st_mode) and info.st_uid == 0 and stat.S_IMODE(info.st_mode) == 0o600 and info.st_nlink == 1 and info.st_size <= 16384
                        craw = path.read_bytes(); child = json.loads(craw); raw_files[cid+'.root.json'] = craw
                        assert child['operationId'] == cid and child['owner'] == record['owner'] and child['plan'] == cp
                        if child['state'] == 'Completed':
                            assert child['modeConfirmed'] and child['actualIdentity'] == after[relative] and child['historyVerified']
                            assert child['version'] == version(baseline,cp['command']['path'],after[relative])
                        children.append({'id':cid,'state':child['state'],'historyVerified':child['historyVerified']})
                    else: assert index > record['finishedThrough'] and after[relative] == before[relative]
                    if cid+'.json' in names:
                        assert index <= record['startedThrough']; expected_hdd.add(cid+'.json')
                        hraw = read_metadata(storage.f,storage,configuration,child_fd,cid+'.json',16384); hdd = json.loads(hraw)
                        assert hdd['operationId'] == cid and hdd['owner'] == record['owner'] and hdd['plan'] == cp
                        assert hdd['parentOperationId'] == operation and hdd['manifestDigest'] == plan['manifestDigest'] and hdd['resourceType'] == 'ContainerRecursivePermissionEntryMetadata'
                        assert hdd['historyDigest'] == digest(b'whaledeck-container-recursive-permission-entry-history-v1\n',unsigned(hdd,'historyDigest'))
                        if child and child['historyVerified']: assert child['historyDigest'] == hdd['historyDigest']
                        raw_files[cid+'.hdd.json'] = hraw
                    elif child: assert not child['historyVerified']
                assert {p.stem for p in (ROOT/(operation+'.entries')).glob('*.json')} == expected_root
                assert names == expected_hdd and children and children[0]['state'] == 'Completed'
            finally: os.close(child_fd)
        finally: os.close(fd)
    for name,raw in raw_files.items(): (directory/name).write_bytes(raw)
    return record, {'children':children,'outcomes':None,'physicalChangedCount':physical_changed,
        'unknownAggregateDoesNotMeanNoPhysicalChanges':True,'sha256':{k:hashlib.sha256(v).hexdigest() for k,v in raw_files.items()}}


def browser_start(baseline, role, username, password, directory):
    out = (directory/(role+'.stdout')).open('wb'); err = (directory/(role+'.stderr')).open('wb')
    try:
        process = subprocess.Popen(['node', str(Path(__file__).with_suffix('.mjs')), baseline['revision'], role], stdin=subprocess.PIPE,
            stdout=out, stderr=err, env={**os.environ,'VALIDATION_USERNAME':username,'VALIDATION_PASSWORD':password})
        process.stdin.write(json.dumps(baseline).encode()); process.stdin.close(); return process
    finally: out.close(); err.close()


def failure_acknowledge(identifier, directory):
    value = snapshot(identifier)['row']
    if value['state'] not in ('Partial','Unknown'): return
    with temporary_user(True) as (username,password):
        session, _ = login('http://127.0.0.1:8080',username,password)
        try:
            api = Api('http://127.0.0.1:8080',session); url = 'http://127.0.0.1:8080/api/v1/files/container-recursive-permissions/recovery/'+identifier
            current = request(session,'GET',url,200).json()
            request(session,'POST',url+'/acknowledge',202,headers={**api.headers,'Idempotency-Key':identifier},json={'version':current['version'],'confirmed':True})
            logout('http://127.0.0.1:8080',session)
        finally: session.close()
    (directory/'failure-acknowledgement.json').write_text(json.dumps({'id':identifier,'gatePassed':False,'normalBffCleanup':True}))


def main():
    global PHASE
    assert os.geteuid() == 0 and len(sys.argv) in (2,3); os.umask(0o077); os.environ['GIT_OPTIONAL_LOCKS']='0'
    revision = sys.argv[1]; assert re.fullmatch('[a-f0-9]{40}',revision)
    kind = sys.argv[2] if len(sys.argv) == 3 else 'cancel'; assert kind in ('cancel','worker-crash','target-disappearance','target-disappearance-barrier')
    barrier_case = kind == 'target-disappearance-barrier'
    unknown_case = kind in ('target-disappearance','target-disappearance-barrier'); expected_state = 'Unknown' if unknown_case else 'Partial'
    repo = Path(__file__).resolve().parents[2]
    assert not subprocess.check_output(['git','-C',str(repo),'status','--porcelain']).strip(), 'FREEZE_VALIDATION_SOURCE_FIRST'
    validation = subprocess.check_output(['git','-C',str(repo),'rev-parse','HEAD'],text=True).strip()
    hashes = {}
    for name in SOURCES:
        raw = subprocess.check_output(['git','-C',str(repo),'show',validation+':deploy/validation/'+name]); assert raw == (repo/'deploy/validation'/name).read_bytes()
        hashes[name] = hashlib.sha256(raw).hexdigest()
    no_active_work(); inactive_except(); host = host_identity(revision); worker = Worker()
    directory = repo/'deploy/artifacts'/('container-recursive-permission-faults-'+uuid.uuid4().hex); directory.mkdir(mode=0o700)
    print(json.dumps({'recursiveFaults':'started','evidence':str(directory)}),flush=True)
    files = load('/usr/local/libexec/whaledeck-files.py','recursive_fault_files')
    storage = load('/usr/local/libexec/whaledeck-file-backups.py','recursive_fault_storage').FileBackups(files)
    held = browser = identifier = baseline = waiting = waiting_baseline = client = profile = None; passed = waiting_clean = False
    with owned.fixture(restartable_core=('whaledeck-worker',),removable=unknown_case) as fixture, contextlib.ExitStack() as extras:
        other = extras.enter_context(owned.fixture(restartable_core=('whaledeck-worker',))) if barrier_case else None
        root = fixture['folder']/'recursive-tree'; root.mkdir(mode=0o750); (root/'sub').mkdir(mode=0o750)
        content = hashlib.shake_256(('recursive-fault:'+fixture['identifier']).encode()).digest(2*1024**2+7)
        for i,(relative,entry_kind) in enumerate(PATHS.items()):
            path = root/relative
            if entry_kind == 'File': path.write_bytes(content); os.setxattr(path,'user.whaledeck-recursive',b'preserve ordinary attribute')
            os.chown(path,2100+i,3100+i); path.chmod(0o640 if entry_kind=='File' else 0o750)
        for relative in PATHS: os.utime(root/relative,ns=(1700000000123456789,1700000000123456789))
        before = inventory(root,content); actual = json.loads(command('docker','inspect',fixture['identifier']))[0]
        baseline = {'revision':revision,'kind':kind,'validationSourceRevision':validation,'sourceSha256':hashes,'evidence':str(directory),
            'containerId':fixture['identifier'],'containerName':actual['Name'].lstrip('/'),'containerPid':actual['State']['Pid'],
            'instanceId':str(actual['State']['Pid'])+':'+actual['State']['StartedAt'],'root':str(root),'source':str(fixture['folder']),
            'original':before,'sha256':hashlib.sha256(content).hexdigest().upper(),'bytesPerFile':len(content),'coreBefore':owned.common.core_snapshot(),'hostBefore':host,
            'fixtureOriginal':{'identity':owned.common.fingerprint(fixture['folder']/'original.bin'),'sha256':fixture['proofs']['original.bin'][1]}}
        (directory/'baseline.json').write_text(json.dumps(baseline))
        try:
            with temporary_user(True) as (username,password):
                PHASE = 'visible-preflight'; browser = browser_start(baseline,'cancel',username,password,directory)
                ready = wait_signal(directory,'ready.json',browser); identifier = ready['id']
                assert snapshot(identifier)['row']['state'] == 'Ready'
                inactive_except(identifier); worker.stop(); write_signal(directory,'confirm.json',{'workerStopped':True})
                accepted = wait_signal(directory,'confirmed.json',browser,30); row = snapshot(identifier)['row']
                assert row['state'] == 'Pending' and row['root_started_at_utc'] is None and row['execution_job_id'] == accepted['executionJobId']
                PHASE = 'exact-installed-partial-cut'
                with ThreadPoolExecutor(max_workers=1) as observers:
                    watching = observers.submit(freeze_partial,row,fixture['folder'],baseline['containerPid'])
                    worker.start(wait_ready=False); held = watching.result(timeout=28)
                assert held is not None, 'REAL_RECURSIVE_PARTIAL_CUT_MISSED_NOT_COUNTED'
                applying = snapshot(identifier)['row']; assert applying['state'] == 'Applying' and applying['publish_barrier'] and applying['root_started_at_utc']
                (directory/'cut-root.json').write_text(json.dumps(held.record))
                (directory/'bound-helper.json').write_text(json.dumps({'pid':held.pid,'unit':held.cgroup,'twoKernelFdProofs':True,'proof':held.proof}))
                PHASE = 'actual-'+kind+'-and-product-helper-exit'; crash = lease_proof = None; began = time.monotonic()
                write_signal(directory,'action.json',{'kind':kind})
                if kind == 'worker-crash' or unknown_case:
                    action = wait_signal(directory,'action-done.json',browser,30); assert action['cancellations'] == 0
                    checks = restart_allowed(held,applying)
                    applying = snapshot(identifier)['row']; lease_held(applying,applying)
                    assert applying['lease_token'] and applying['lease_expires_at_utc']
                    cut_tree = inventory(root,content); cut_raw = (ROOT/(uuid.UUID(applying['execution_job_id']).hex+'.json')).read_bytes()
                    assert json.loads(cut_raw) == held.record
                    (directory/('pre-disappearance-sql.json' if unknown_case else 'pre-crash-sql.json')).write_text(json.dumps(snapshot(identifier)))
                    (directory/'physical-cut.json').write_text(json.dumps(cut_tree)); (directory/'cut-root-original.json').write_bytes(cut_raw)
                if unknown_case:
                    PHASE = 'exact-owned-container-disappearance'
                    fixture['remove_owned'](); assert inventory(root,content) == cut_tree
                    (directory/'target-disappearance.json').write_text(json.dumps({'containerId':fixture['identifier'],'exactOwnedContainerRemoved':True,'sourcePreserved':True,'otherActiveWork':checks}))
                if kind == 'worker-crash' or unknown_case: began = time.monotonic()
                with ThreadPoolExecutor(max_workers=1) as exits:
                    stopping = exits.submit(held.wait_exit_seconds,began,15)
                    if kind == 'worker-crash':
                        prior = worker.current(); worker.stopped = True
                        command('docker','kill','--signal','KILL',worker.id)
                        stopped = worker.current(); automatic = stopped['State']['Running']
                        crashed = snapshot(identifier); same_operation(applying,crashed['row']); applying = crashed['row']
                        lease_held(applying,applying); (directory/'crash-sql.json').write_text(json.dumps(crashed))
                        worker.start(wait_ready=False)
                        started = worker.current(); assert started['State']['StartedAt'] != prior['State']['StartedAt']
                        crash = {'workerId':worker.id,'imageId':prior['Image'],'oldStartedAt':prior['State']['StartedAt'],'newStartedAt':started['State']['StartedAt'],
                            'signal':'SIGKILL','automaticRestartObserved':automatic,'exitCodeObservedBeforeExplicitStart':stopped['State']['ExitCode'],
                            'sameContainerAndImage':True,'otherActiveWork':checks,'originalLeaseExpiresAtUtc':applying['lease_expires_at_utc']}
                        (directory/'worker-crash.json').write_text(json.dumps(crash))
                    elif unknown_case:
                        write_signal(directory,'logout.json',{'exactOwnedContainerRemoved':True})
                        assert browser.wait(timeout=35) == 0, 'REMOVED_TARGET_ORIGINAL_LOGOUT_FAILED'
                    else: wait_signal(directory,'action-done.json',browser,30)
                    elapsed = stopping.result(timeout=16)
                assert elapsed is not None and elapsed < 15, 'PRODUCT_DID_NOT_STOP_BOUND_RECURSIVE_HELPER'
                held.release(); held = None
                if kind == 'worker-crash':
                    PHASE = 'original-natural-lease'; lease_proof = natural_lease(applying,root,cut_tree,directory)
                PHASE = 'real-'+expected_state.lower()+'-proof'; partial = wait_state(identifier,expected_state)
                if kind == 'worker-crash':
                    same_operation(applying,partial['row'])
                    assert dt.datetime.now(dt.timezone.utc) >= dt.datetime.fromisoformat(applying['lease_expires_at_utc'])
                    assert inventory(root,content) == cut_tree, 'RECOVERY_RESUMED_CHMOD'
                after = inventory(root,content)
                record,proof = (verify_unknown if unknown_case else verify_partial)(partial,baseline,before,after,storage,directory)
                (directory/'partial-sql.json').write_text(json.dumps(partial)); (directory/'physical-partial.json').write_text(json.dumps(after))
                (directory/'history-proof.json').write_text(json.dumps(proof))
                if not unknown_case:
                    write_signal(directory,'settled.json',{'id':identifier,'state':expected_state,
                        **{k:record[k] for k in ('completedCount','changedCount','abortedCount','notStartedCount','unknownCount')}})
                    assert browser.wait(timeout=60) == 0, 'PARTIAL_PAGE_NOT_VERIFIED'
                browser = None
                ui = json.loads((directory/'cancel-browser.json').read_bytes()); assert ui['subject'] == partial['row']['actor_subject'] == ready['subject']
            # The original temporary Authentik identity has actually been deleted.
            # A new, distinct human login owns the explicit recovery decision.
            PHASE = 'different-admin-real-recovery'
            with temporary_user(True) as (username,password):
                recovery_fixture = {**baseline,'id':identifier,'originalSubject':ready['subject'],'changedCount':record['changedCount'],'expectedState':expected_state,
                    'counts':{k:record[k] for k in ('completedCount','changedCount','abortedCount','notStartedCount','unknownCount')}}
                if barrier_case:
                    PHASE = 'actual-other-container-directory-deferred'
                    client,profile = login('http://127.0.0.1:8080',username,password); assert profile['subject'] != ready['subject']
                    waiting,waiting_baseline = register_waiting('http://127.0.0.1:8080',client,Api('http://127.0.0.1:8080',client),other,'work/递归未知保护解除后的目录')
                    inspected_other = json.loads(command('docker','inspect',other['identifier']))[0]
                    waiting_baseline.update(id=waiting['id'],subject=profile['subject'],containerName=inspected_other['Name'].lstrip('/'),originalFileProof=other['proofs']['original.bin'])
                    (directory/'waiting-baseline.json').write_text(json.dumps(waiting_baseline))
                    observe_waiting(waiting,waiting_baseline,other,profile['subject'],partial,root,content,after,directory,worker)
                    recovery_fixture.update(waitingId=waiting['id'],waitingContainerId=other['identifier'],waitingContainerName=waiting_baseline['containerName'],waitingPath=waiting_baseline['path'])
                browser = browser_start(recovery_fixture,'recovery',username,password,directory)
                canceled = wait_signal(directory,'recovery-canceled.json',browser)
                assert canceled['subject'] != ready['subject'] and canceled['acknowledgements'] == 0
                if barrier_case:
                    assert canceled['subject'] == profile['subject']
                    require_waiting_deferred(directory_snapshot(waiting['id']),waiting['id'],profile['subject'])
                    assert not (other['folder']/waiting_baseline['path'].removeprefix('work/')).exists()
                still = snapshot(identifier); assert still == partial, 'CANCELED_RECOVERY_CHANGED_ORIGINAL_TASK'
                assert inventory(root,content) == after and record_for(still['row']) == record
                write_signal(directory,'acknowledge.json',{'id':identifier,'originalStillProtected':True})
                assert browser.wait(timeout=105 if barrier_case else 65) == 0, 'CURRENT_ADMIN_ACK_PAGE_NOT_VERIFIED'; browser = None
                recovery = json.loads((directory/'recovery-browser.json').read_bytes())
                if barrier_case:
                    PHASE = 'same-original-waiting-task-published-after-acknowledgement'
                    waiting_final = settle_waiting(waiting['id']); released = snapshot(identifier)['row']
                    assert waiting_final['row']['state'] == waiting_final['job']['state'] == 'Succeeded'
                    assert dt.datetime.fromisoformat(waiting_final['row']['root_started_at_utc']) >= dt.datetime.fromisoformat(released['completed_at_utc'])
                    assert waiting_final['accepted'] == 1 and waiting_final['pending'] == 0 and recovery['waitingState'] == 'Succeeded'
                    proof_directory = directory/'waiting-proof'; proof_directory.mkdir(mode=0o700)
                    output,inode,private_inode = verify_directory({'id':waiting['id'],'path':waiting_baseline['path'],'publishedVersion':waiting_final['row']['published_version']},waiting_baseline,profile['subject'],proof_directory)
                    assert owned.common.fingerprint(other['folder']/'original.bin') == other['proofs']['original.bin'][0] and owned.common.file_digest(other['folder']/'original.bin') == other['proofs']['original.bin'][1]
                    (directory/'waiting-final-sql.json').write_text(json.dumps(waiting_final))
                    (directory/'waiting-physical.json').write_text(json.dumps({'inode':inode,'privateIdentity':private_inode,'originalFileUnchanged':True,'actualUiDirectoryObserved':recovery['waitingVisibleDirectory']}))
                    clean_success(waiting,waiting_baseline,profile,proof_directory,other); waiting_clean = True
                    logout('http://127.0.0.1:8080',client); client.close(); client = None
            PHASE = 'final-independent-physical-and-permanent-proof'
            final = snapshot(identifier); row = final['row']
            assert row['state'] == 'Acknowledged' and final['executionJob']['state'] == 'Failed'
            assert row['acknowledged_by'] == recovery['subject'] != row['actor_subject'] and row['actor_subject'] == ready['subject']
            assert row['completed_at_utc'] and not row['publish_barrier'] and row['lease_token'] is None and row['lease_expires_at_utc'] is None
            assert final['pending'] == 0 and final['accepted'] == final['confirmed'] == 1
            for key in ('id','container_id','instance_id','actor_subject','owner_hash','request_hash','plan_json','execution_job_id','root_started_at_utc','completed_count','changed_count','aborted_count','not_started_count','unknown_count','outcome_digest','history_digest','root_state'):
                assert row[key] == partial['row'][key], 'ACK_REWROTE_ORIGINAL_FACTS'
            # Discard reports Replayed=false, Inspect reports true. Availability
            # and binding are observations too; permanent facts remain equal.
            def facts(raw): return {k:v for k,v in json.loads(raw).items() if k not in ('Replayed','BindingCurrent','HistoryAvailable')}
            assert facts(row['receipt_json']) == facts(partial['row']['receipt_json'])
            assert (ROOT/(record['operationId']+'.json')).read_bytes() == (directory/'partial.root.json').read_bytes()
            assert inventory(root,content) == after
            late = subprocess.run(['/usr/local/libexec/whaledeck-privileged','container-file-recursive-permissions','apply',uuid.uuid4().hex],
                input=json.dumps({k:record[k] for k in ('operationId','owner','plan')})+'\n',capture_output=True,text=True,timeout=35)
            assert late.returncode == 0 and len(late.stdout.encode()) <= 65536 and len(late.stderr.encode()) <= 32768
            replay = [json.loads(line) for line in late.stdout.splitlines() if line.strip()][-1]
            assert replay['state'] == expected_state and replay['operationId'] == record['operationId'] and replay['replayed']
            assert inventory(root,content) == after and (ROOT/(record['operationId']+'.json')).read_bytes() == (directory/'partial.root.json').read_bytes()
            (directory/'late-apply.json').write_text(json.dumps(replay)); (directory/'final-sql.json').write_text(json.dumps(final))
            (directory/'physical-final.json').write_text(json.dumps(after))
            assert host_identity(revision) == host; worker.start(); inactive_except()
            remove_proved(root,content,after); passed = True
            result = {'id':identifier,'kind':kind,'operationId':record['operationId'],'productRevision':revision,'validationSourceRevision':validation,
                'partialCounts':{k:record[k] for k in ('completedCount','changedCount','abortedCount','notStartedCount','unknownCount')},
                'productStoppedHelperSeconds':round(elapsed,3),'validatorKilledHelper':False,'differentAdminAcknowledged':True,
                'originalIdentityDeleted':True,'rootHistoryAndPhysicalPreserved':True,'latePrivilegedHelperReplayNoChanges':True,
                'lateReplayWasSignedRpc':False,'actualUi':True,'workerRestartedSameContainer':True,'allFileBytesUidGidMtimeXattrPreserved':True,
                'workerCrash':crash,'naturalLeaseSamples':len(lease_proof['samples']) if lease_proof else 0,
                'rootState':expected_state,'physicalChangedCount':unchanged_except_modes(before,after),'targetRemovedDuringOperation':unknown_case,
                'waitingId':waiting['id'] if waiting else None,'otherWriteDeferredUntilExplicitRelease':barrier_case,'waitingActualUi':recovery.get('waitingVisibleDirectory',False)}
        finally:
            if held is not None: held.release()
            worker.start()
            if browser is not None and browser.poll() is None:
                try: browser.wait(timeout=235)
                except subprocess.TimeoutExpired: browser.terminate(); browser.wait(timeout=10)
            registered = directory/'registered.json'
            if identifier is None and registered.exists(): identifier = json.loads(registered.read_bytes())['id']
            if identifier is None and not passed:
                # No browser reference is not proof that HTTP registration did
                # not arrive. Require actual SQL absence and no active work.
                no_active_work(); inactive_except()
                unregistered = query("SELECT json_build_object('n',count(*)) FROM container_recursive_permission_intents WHERE container_id=:'cid';",cid=baseline['containerId'])
                assert unregistered['n'] == 0, 'UNREFERENCED_REGISTRATION_PRESERVED'
                assert inventory(root,content) == before
                remove_proved(root,content,before)
                (directory/'failure-before-registration.json').write_text(json.dumps({'gatePassed':False,'actualIntentRows':0,'exactUnchangedTreeRemoved':True}))
            if identifier is not None and not passed:
                current = snapshot(identifier)['row']; until = time.monotonic()+115
                while current['state'] in ('Pending','Applying') and time.monotonic()<until:
                    time.sleep(1); current = snapshot(identifier)['row']
                failure_acknowledge(identifier,directory)
                (directory/'failed-gate-sql.json').write_text(json.dumps(snapshot(identifier)))
                current = snapshot(identifier)['row']
                if current['completed_at_utc'] and not current['publish_barrier']:
                    observed = inventory(root,content); unchanged_except_modes(before,observed)
                    (directory/'failed-gate-physical.json').write_text(json.dumps(observed)); remove_proved(root,content,observed)
            if waiting is not None and not waiting_clean:
                end = settle_waiting(waiting['id'])
                if end['row']['state'] == 'Unknown':
                    with temporary_user(True) as (cleanup_name,cleanup_password):
                        cleanup_session,_ = login('http://127.0.0.1:8080',cleanup_name,cleanup_password)
                        try:
                            api = Api('http://127.0.0.1:8080',cleanup_session)
                            url = 'http://127.0.0.1:8080/api/v1/files/container-directories/recovery/'+waiting['id']
                            value = request(cleanup_session,'GET',url,200).json()
                            request(cleanup_session,'POST',url+'/acknowledge',202,headers={**api.headers,'Idempotency-Key':waiting['id']},json={'version':value['version'],'confirmed':True})
                            logout('http://127.0.0.1:8080',cleanup_session)
                        finally: cleanup_session.close()
                    end = directory_snapshot(waiting['id'])
                    (directory/'failed-waiting-acknowledgement.json').write_text(json.dumps({'id':waiting['id'],'state':end['row']['state'],'gatePassed':False,'normalBffCleanup':True}))
                if end['row']['state'] == 'Succeeded':
                    proof_directory = directory/'failed-waiting-proof'; proof_directory.mkdir(mode=0o700)
                    clean_success(waiting,waiting_baseline,profile,proof_directory,other)
                    (directory/'failed-waiting-cleanup.json').write_text(json.dumps({'id':waiting['id'],'gatePassed':False,'exactCompletedDirectoryRemoved':True}))
            if client is not None:
                try: logout('http://127.0.0.1:8080',client)
                except Exception:
                    # The temporary identity may already have been deleted by
                    # context unwinding. This never counts as a passing logout.
                    (directory/'failed-client-logout.json').write_text(json.dumps({'gatePassed':False,'identityMayAlreadyBeDeleted':True}))
                finally: client.close()
    assert passed and baseline is not None
    assert not Path(baseline['source']).exists() and subprocess.run(['docker','inspect',baseline['containerId']],capture_output=True).returncode != 0
    if barrier_case:
        assert waiting_clean and not Path(waiting_baseline['source']).exists()
        assert subprocess.run(['docker','inspect',waiting_baseline['containerId']],capture_output=True).returncode != 0
    no_active_work(); inactive_except(); (directory/'verified.json').write_text(json.dumps(result))
    print(json.dumps({'recursiveFaults':'passed',**result,'evidence':str(directory)}),flush=True)


if __name__ == '__main__':
    try: main()
    except BaseException as error:
        import traceback
        locations = [{'file':Path(f.filename).name,'line':f.lineno} for f in traceback.extract_tb(error.__traceback__)]
        print(json.dumps({'recursiveFaults':'failed','phase':PHASE,'failureType':type(error).__name__,'locations':locations}),flush=True)
        sys.exit(1)
