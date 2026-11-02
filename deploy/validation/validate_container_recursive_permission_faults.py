#!/usr/bin/env python3
"""Installed real-page partial chmod cancellation and different-admin resolution.

No SQL/Root/clock edits or product hooks. All bytes and history are verified
before exact fixture removal. A missed cut or failed page remains a failed gate.
"""
from concurrent.futures import ThreadPoolExecutor
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

PHASE = 'startup'
PATHS = {'': 'Directory', 'sub': 'Directory', **{f'sub/entry-{i:02}.bin': 'File' for i in range(16)}}
SOURCES = ('bound_container_recursive_permission_cut.py', 'validate_container_recursive_permission_faults.py',
    'validate_container_recursive_permission_faults.mjs', 'verify_container_recursive_permission_faults.py')


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


def inactive_except(identifier=None):
    preflight([identifier] if identifier else [])
    value = query('''SELECT json_build_object(
      'copies',(SELECT count(*) FROM container_copy_intents WHERE completed_at_utc IS NULL OR publish_barrier),
      'moves',(SELECT count(*) FROM container_move_intents WHERE completed_at_utc IS NULL OR publish_barrier),
      'permissions',(SELECT count(*) FROM container_permission_intents WHERE completed_at_utc IS NULL OR publish_barrier),
      'ownerships',(SELECT count(*) FROM container_ownership_intents WHERE completed_at_utc IS NULL OR publish_barrier),
      'recursive',(SELECT count(*) FROM container_recursive_permission_intents WHERE (completed_at_utc IS NULL OR publish_barrier) AND id::text<>:'id'));''', id=identifier or '')
    assert all(n == 0 for n in value.values()), 'OTHER_FILE_WORK_REFUSED'
    return value


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
    assert os.geteuid() == 0 and len(sys.argv) == 2; os.umask(0o077); os.environ['GIT_OPTIONAL_LOCKS']='0'
    revision = sys.argv[1]; assert re.fullmatch('[a-f0-9]{40}',revision)
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
    held = browser = identifier = baseline = None; passed = False
    with owned.fixture(restartable_core=('whaledeck-worker',)) as fixture:
        root = fixture['folder']/'recursive-tree'; root.mkdir(mode=0o750); (root/'sub').mkdir(mode=0o750)
        content = hashlib.shake_256(('recursive-fault:'+fixture['identifier']).encode()).digest(2*1024**2+7)
        for i,(relative,kind) in enumerate(PATHS.items()):
            path = root/relative
            if kind == 'File': path.write_bytes(content); os.setxattr(path,'user.whaledeck-recursive',b'preserve ordinary attribute')
            os.chown(path,2100+i,3100+i); path.chmod(0o640 if kind=='File' else 0o750)
        for relative in PATHS: os.utime(root/relative,ns=(1700000000123456789,1700000000123456789))
        before = inventory(root,content); actual = json.loads(command('docker','inspect',fixture['identifier']))[0]
        baseline = {'revision':revision,'validationSourceRevision':validation,'sourceSha256':hashes,'evidence':str(directory),
            'containerId':fixture['identifier'],'containerName':actual['Name'].lstrip('/'),'containerPid':actual['State']['Pid'],
            'instanceId':str(actual['State']['Pid'])+':'+actual['State']['StartedAt'],'root':str(root),'source':str(fixture['folder']),
            'original':before,'sha256':hashlib.sha256(content).hexdigest().upper(),'bytesPerFile':len(content),'coreBefore':owned.common.core_snapshot(),'hostBefore':host}
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
                PHASE = 'actual-cancel-and-product-helper-exit'; began = time.monotonic()
                with ThreadPoolExecutor(max_workers=1) as exits:
                    stopping = exits.submit(held.wait_exit_seconds,began,15)
                    write_signal(directory,'action.json',{'kind':'cancel'})
                    wait_signal(directory,'action-done.json',browser,30)
                    elapsed = stopping.result(timeout=16)
                assert elapsed is not None and elapsed < 15, 'PRODUCT_DID_NOT_STOP_BOUND_RECURSIVE_HELPER'
                held.release(); held = None
                PHASE = 'real-partial-proof'; partial = wait_state(identifier,'Partial')
                after = inventory(root,content); record, proof = verify_partial(partial,baseline,before,after,storage,directory)
                (directory/'partial-sql.json').write_text(json.dumps(partial)); (directory/'physical-partial.json').write_text(json.dumps(after))
                (directory/'history-proof.json').write_text(json.dumps(proof))
                write_signal(directory,'settled.json',{'id':identifier,'state':'Partial','changedCount':record['changedCount']})
                assert browser.wait(timeout=60) == 0, 'PARTIAL_PAGE_NOT_VERIFIED'; browser = None
                ui = json.loads((directory/'cancel-browser.json').read_bytes()); assert ui['subject'] == partial['row']['actor_subject'] == ready['subject']
            # The original temporary Authentik identity has actually been deleted.
            # A new, distinct human login owns the explicit recovery decision.
            PHASE = 'different-admin-real-recovery'
            with temporary_user(True) as (username,password):
                recovery_fixture = {**baseline,'id':identifier,'originalSubject':ready['subject'],'changedCount':record['changedCount']}
                browser = browser_start(recovery_fixture,'recovery',username,password,directory)
                canceled = wait_signal(directory,'recovery-canceled.json',browser)
                assert canceled['subject'] != ready['subject'] and canceled['acknowledgements'] == 0
                still = snapshot(identifier); assert still == partial, 'CANCELED_RECOVERY_CHANGED_ORIGINAL_TASK'
                assert inventory(root,content) == after and record_for(still['row']) == record
                write_signal(directory,'acknowledge.json',{'id':identifier,'originalStillProtected':True})
                assert browser.wait(timeout=65) == 0, 'CURRENT_ADMIN_ACK_PAGE_NOT_VERIFIED'; browser = None
                recovery = json.loads((directory/'recovery-browser.json').read_bytes())
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
            assert replay['state'] == 'Partial' and replay['operationId'] == record['operationId'] and replay['replayed']
            assert inventory(root,content) == after and (ROOT/(record['operationId']+'.json')).read_bytes() == (directory/'partial.root.json').read_bytes()
            (directory/'late-apply.json').write_text(json.dumps(replay)); (directory/'final-sql.json').write_text(json.dumps(final))
            (directory/'physical-final.json').write_text(json.dumps(after))
            assert host_identity(revision) == host; worker.start(); inactive_except()
            remove_proved(root,content,after); passed = True
            result = {'id':identifier,'operationId':record['operationId'],'productRevision':revision,'validationSourceRevision':validation,
                'partialCounts':{k:record[k] for k in ('completedCount','changedCount','abortedCount','notStartedCount','unknownCount')},
                'productStoppedHelperSeconds':round(elapsed,3),'validatorKilledHelper':False,'differentAdminAcknowledged':True,
                'originalIdentityDeleted':True,'rootHistoryAndPhysicalPreserved':True,'latePrivilegedHelperReplayNoChanges':True,
                'lateReplayWasSignedRpc':False,'actualUi':True,'workerRestartedSameContainer':True,'allFileBytesUidGidMtimeXattrPreserved':True}
        finally:
            if held is not None: held.release()
            worker.start()
            if browser is not None and browser.poll() is None:
                try: browser.wait(timeout=235)
                except subprocess.TimeoutExpired: browser.terminate(); browser.wait(timeout=10)
            registered = directory/'registered.json'
            if identifier is None and registered.exists(): identifier = json.loads(registered.read_bytes())['id']
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
    assert passed and baseline is not None
    assert not Path(baseline['source']).exists() and subprocess.run(['docker','inspect',baseline['containerId']],capture_output=True).returncode != 0
    no_active_work(); inactive_except(); (directory/'verified.json').write_text(json.dumps(result))
    print(json.dumps({'recursiveFaults':'passed',**result,'evidence':str(directory)}),flush=True)


if __name__ == '__main__':
    try: main()
    except BaseException as error:
        import traceback
        locations = [{'file':Path(f.filename).name,'line':f.lineno} for f in traceback.extract_tb(error.__traceback__)]
        print(json.dumps({'recursiveFaults':'failed','phase':PHASE,'failureType':type(error).__name__,'locations':locations}),flush=True)
        sys.exit(1)
