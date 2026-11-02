#!/usr/bin/env python3
"""Separate read-only observer: permanent SQL/Root/attested HDD and exact cleanup.

Physical bytes were read at each case by the parent, never claimed reread after
cleanup. This observer performs no SQL writes, identity creation or deletion.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import uuid

import requests
from run_with_ephemeral_admin import AUTHENTIK_API, container_environment, require
from validate_container_file_staging import load
from validate_container_file_publishing import common
from validate_container_upload_recovery_agent import trusted
from validate_container_recursive_ownership_agent import STATE
from validate_container_recursive_ownership import PATHS
from verify_container_recursive_permissions import digest, unsigned, private_json
from validate_container_recursive_ownership_browser import CASES, OWNERS, CHANGES, SOURCES, snapshot, managed_plan, version, read_metadata, no_active_work
from validate_metric_continuity import query


def verify(name):
    assert re.fullmatch('container-recursive-ownership-browser-[a-f0-9]{32}',name)
    repo = Path(__file__).resolve().parents[2]; evidence = repo/'deploy/artifacts'/name; trusted(evidence,True)
    baseline = private_json(evidence/'baseline.json'); results = private_json(evidence/'browser.json'); accepted = private_json(evidence/'verified.json'); proofs = private_json(evidence/'results.json')
    assert baseline['revision'] == results['revision'] == accepted['revision'] and results['actualUi'] and results['normalDualLogout']
    assert accepted['actualBrowserAndIdentity'] and accepted['cases'] == len(proofs) == len(results['completed']) == 6
    assert results['registrations'] == results['registrationRetries'] == 6 and results['confirmations'] == results['confirmationRetries'] == 5 and results['contentUploads'] == 0
    assert re.fullmatch('[a-f0-9]{40}',baseline['validationSourceRevision'])
    assert set(baseline['validationSourceSha256']) == set(SOURCES)
    for file, sha in baseline['validationSourceSha256'].items():
        raw = subprocess.check_output(['git','-C',str(repo),'show',baseline['validationSourceRevision']+':deploy/validation/'+file])
        assert hashlib.sha256(raw).hexdigest() == sha
    content = hashlib.shake_256(('recursive-browser:'+baseline['containerId']).encode()).digest(2*1024**2+7)
    assert hashlib.sha256(content).hexdigest().upper() == baseline['sha256'] and baseline['bytesPerFile'] == len(content)
    previous = baseline['original']; aggregates = 0; children = 0
    files = load('/usr/local/libexec/whaledeck-files.py','recursive_browser_independent_files')
    storage = load('/usr/local/libexec/whaledeck-file-backups.py','recursive_browser_independent_hdd').FileBackups(files)
    with storage.target(create_namespace=False,read_only=True) as (configuration,base):
        fd = files.open_at(base,'container-recursive-ownership-v1',os.O_RDONLY|os.O_DIRECTORY)
        try:
            storage.attest(configuration,fd,read_only=True)
            for index, proof in enumerate(proofs):
                item = results['completed'][index]; label = CASES[index]
                assert item == proof['result'] and item['name'] == label and item['confirmation'] is None and proof['before'] == previous
                data = snapshot(item['id']); row = data['row']; assert data == private_json(evidence/(label+'.sql.json'))
                assert row['actor_subject'] == results['subject'] and row['container_id'] == baseline['containerId'] and row['instance_id'] == baseline['instanceId']
                assert row['relative_path'] == item['command']['location']['path'] == 'work/recursive-tree' and row['completed_at_utc'] and not row['publish_barrier'] and row['lease_token'] is None and row['lease_expires_at_utc'] is None
                assert data['pending'] == 0 and data['accepted'] == 1 and data['planJob']['state'] == 'Succeeded'
                plan = managed_plan(json.loads(row['plan_json'])); assert plan['rootIdentity'] == previous['']
                assert plan['command']['expectedVersion'] == version(baseline,'work/recursive-tree',previous[''])
                assert [plan['command']['uid'],plan['command']['gid']] == list(OWNERS[index])
                assert [plan['entryCount'],plan['fileCount'],plan['directoryCount'],plan['changeCount']] == [4,2,2,CHANGES[index]]
                if label == 'cancel':
                    assert row['state'] == 'Canceled' and row['execution_job_id'] is None and row['root_started_at_utc'] is None and row['receipt_json'] is None
                    assert data['executionJob'] is None and data['confirmed'] == 0 and not row['history_may_exist'] and not row['history_verified'] and row['history_digest'] is None
                    assert proof['after'] == previous and proof['children'] == 0
                    continue
                assert row['state'] == data['executionJob']['state'] == 'Succeeded' and data['confirmed'] == 1 and row['root_state'] == 'Completed'
                operation = uuid.UUID(row['execution_job_id']).hex; path = STATE/(operation+'.json'); trusted(path); raw = path.read_bytes(); record = json.loads(raw)
                assert raw == (evidence/(label+'.root.json')).read_bytes() and hashlib.sha256(raw).hexdigest() == proof['rootSha256']
                assert type(record['schemaVersion']) is int and record['schemaVersion'] == 1
                assert all(type(record[key]) is int for key in ('completedCount','changedCount','abortedCount','notStartedCount','unknownCount'))
                assert record['operationId'] == operation and record['owner'] == row['owner_hash'] and record['plan'] == plan and record['state'] == 'Completed'
                assert plan['fingerprint'] == digest(b'whaledeck-container-recursive-ownership-plan-v1\n',unsigned(plan,'fingerprint'))
                saved_raw = read_metadata(files,storage,configuration,fd,operation+'.json'); saved = json.loads(saved_raw)
                assert saved_raw == (evidence/(label+'.hdd.json')).read_bytes() and saved['plan'] == plan and saved['owner'] == row['owner_hash'] and saved['operationId'] == operation
                assert saved['resourceType'] == 'ContainerRecursiveOwnershipMetadata' and saved['storageId'] == 'database-platform.backup.hdd'
                assert saved['historyDigest'] == row['history_digest'] == record['historyDigest'] == item['historyDigest'] == digest(b'whaledeck-container-recursive-ownership-history-v1\n',unsigned(saved,'historyDigest'))
                assert plan['manifestDigest'] == digest(b'whaledeck-container-recursive-ownership-manifest-v1\n',saved['entries'])
                assert [entry['relativePath'] for entry in saved['entries']] == ['sub/中文.bin','one.bin','sub','']
                outcomes = []; child_fd = files.open_at(fd,operation+'/entries',os.O_RDONLY|os.O_DIRECTORY)
                try:
                    storage.attest(configuration,child_fd,read_only=True)
                    for n,entry in enumerate(saved['entries']):
                        child_id = hashlib.sha256(b'whaledeck-recursive-ownership-entry-id-v1:'+operation.encode()+b':'+str(n).encode()).hexdigest()[:32]
                        child_path = STATE/(operation+'.entries')/(child_id+'.json'); trusted(child_path); child_raw = child_path.read_bytes(); child = json.loads(child_raw)
                        assert child_raw == (evidence/(label+'.'+child_id+'.root.json')).read_bytes()
                        history_raw = read_metadata(files,storage,configuration,child_fd,child_id+'.json',16384); history = json.loads(history_raw)
                        assert history_raw == (evidence/(label+'.'+child_id+'.hdd.json')).read_bytes()
                        relative = entry['relativePath']; old = previous[relative]; actual = proof['after'][relative]; child_plan = entry['plan']
                        assert child['state'] == 'Completed' and child['operationId'] == child_id and child['owner'] == row['owner_hash'] and child['plan'] == child_plan and child['actualIdentity'] == actual
                        assert child_plan['sourceIdentity'] == old and child_plan['fingerprint'] == digest(b'whaledeck-container-ownership-plan-v1\n',unsigned(child_plan,'fingerprint'))
                        desired = list(old); desired[3:5] = list(OWNERS[index])
                        assert child['ownerConfirmed'] is True
                        assert [child_plan['command']['uid'],child_plan['command']['gid']] == list(OWNERS[index])
                        assert actual[:8] == desired[:8] and child_plan['noChange'] == (old[3:5] == desired[3:5])
                        if child_plan['noChange']: assert actual == old
                        assert history['resourceType'] == 'ContainerRecursiveOwnershipEntryMetadata' and history['parentOperationId'] == operation and history['manifestDigest'] == plan['manifestDigest']
                        assert history['operationId'] == child_id and history['owner'] == row['owner_hash'] and history['plan'] == child_plan
                        assert history['historyDigest'] == child['historyDigest'] == digest(b'whaledeck-container-recursive-ownership-entry-history-v1\n',unsigned(history,'historyDigest'))
                        published = version(baseline,child_plan['command']['path'],actual); assert published == child['version']
                        outcomes.append({'id':child_id,'state':'Completed','version':published,'changed':not child_plan['noChange']}); children += 1
                finally: os.close(child_fd)
                assert record['outcomeDigest'] == row['outcome_digest'] == item['receipt']['outcomeDigest'] == digest(b'whaledeck-recursive-ownership-outcomes-v1\n',outcomes)
                assert record['completedCount'] == 4 and record['changedCount'] == CHANGES[index] and record['abortedCount'] == record['notStartedCount'] == record['unknownCount'] == 0
                assert item['receipt']['completedCount'] == 4 and item['receipt']['changedCount'] == CHANGES[index] and row['history_verified']
                if label == 'noop': assert proof['after'] == previous
                previous = proof['after']; aggregates += 1
        finally: os.close(fd)
    assert aggregates == accepted['aggregateHddRecords'] == 5 and children == accepted['childHddRecords'] == 20
    assert previous == private_json(evidence/'physical-final.json')
    source = Path(baseline['source']); assert source.parent == common.APPS and source.name == baseline['containerName'] and not os.path.lexists(source)
    missing = subprocess.run(['docker','inspect',baseline['containerId']],capture_output=True,timeout=15); assert missing.returncode != 0 and b'no such' in missing.stderr.lower()
    no_active_work(); active = query("SELECT json_build_object('n',count(*)) FROM container_recursive_ownership_intents WHERE completed_at_utc IS NULL OR publish_barrier OR lease_token IS NOT NULL;"); assert active['n'] == 0
    assert common.core_snapshot() == {key:tuple(v) for key,v in baseline['coreBefore'].items()}
    api = requests.Session(); api.trust_env = False; api.headers['Authorization'] = 'Bearer '+require(container_environment(),'Authentication__Authentik__ApiToken')
    try:
        for prefix in ('whaledeck-validation-','whaledeck-upload-qa-'):
            response = api.get(AUTHENTIK_API+'core/users/',params={'search':prefix,'page_size':100},timeout=20); assert response.status_code == 200 and not response.json().get('pagination',{}).get('next')
            assert not any(str(user['username']).startswith(prefix) for user in response.json()['results'])
    finally: api.close()
    return {'revision':baseline['revision'],'independentReadOnlyVerifier':True,'cases':6,'aggregateHddRecords':aggregates,'childHddRecords':children,'temporaryIdentitiesAbsent':True,'exactSourceAndContainerAbsent':True,'coreUnchanged':11,'physicalBytesCheckedByParentPerCase':True,'sourceBytesRereadAfterCleanup':False}


if __name__ == '__main__':
    try:
        assert os.geteuid() == 0 and len(sys.argv) == 2; os.umask(0o077); os.environ['GIT_OPTIONAL_LOCKS']='0'
        proof = verify(sys.argv[1]); repo = Path(__file__).resolve().parents[2]
        (repo/'deploy/artifacts'/sys.argv[1]/'independent-final.json').write_text(json.dumps(proof))
        print(json.dumps({'independentRecursiveOwnershipBrowser':'passed',**proof}))
    except Exception as error:
        print(json.dumps({'independentRecursiveOwnershipBrowser':'failed','failureType':type(error).__name__})); raise SystemExit(1) from None
