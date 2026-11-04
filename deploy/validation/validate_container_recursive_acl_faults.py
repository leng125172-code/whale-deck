#!/usr/bin/env python3
"""Real installed recursive ACL cancellation/logout after aggregate HDD capture.

Only an owned tree and fresh human identity; no receipt/SQL/clock edits, product
hooks or arbitrary PID input. Full physical proof precedes exact source cleanup.
"""
from concurrent.futures import ThreadPoolExecutor
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import re
import select
import stat
import subprocess
import sys
import time
import uuid

import validate_container_file_publishing as owned
from bound_container_recursive_acl_cut import record_for, freeze_before_first_acl, child_id
from validate_container_recursive_acl_browser import snapshot, managed_plan, no_active_work, read_metadata, expected_targets, archive
from validate_container_recursive_acl_agent import STATE, host_identity
from validate_container_recursive_acl import PATHS, NAMES, inventory, remove_proved
from validate_container_directory_restart import Worker, command, preflight
from validate_container_acl_faults import write_signal, wait_signal
from validate_container_file_staging import load
from validate_container_upload_bff import temporary_user
from validate_container_upload_recovery_agent import trusted
from verify_container_recursive_permissions import digest, unsigned
from acl_source_apply_missing_proof import source_closure
from owned_authentik_revocation import OwnedAuthentikRevocation
from validate_container_recursive_permission_faults import require_authority_change
from validate_container_recursive_permission_faults import restore_fault_services
from recursive_acl_restart_proof import RESTART_KINDS, restart, natural_lease, require_host_restart, verify_restart

PHASE='startup'
REVOKED_KINDS=('group-removal','account-disable')
SOURCES=('bound_container_recursive_acl_cut.py','validate_container_recursive_acl_faults.py',
    'validate_container_recursive_acl_faults.mjs','verify_container_recursive_acl_faults.py','test_recursive_acl_fault_probe_scope.py')


def preservation_snapshot(storage):
    """Read-only pinned file digests; private contents never enter evidence."""
    result={}
    def add(path):
        info=path.lstat();assert stat.S_ISREG(info.st_mode) and info.st_nlink==1 and not path.is_symlink() and info.st_size<=16*1024**2
        fd=os.open(path,os.O_RDONLY|os.O_NOFOLLOW|os.O_CLOEXEC)
        try:
            before=os.fstat(fd);assert (before.st_dev,before.st_ino)==(info.st_dev,info.st_ino)
            sha=hashlib.sha256();size=0
            while True:
                part=os.read(fd,65536)
                if not part:break
                size+=len(part);assert size<=16*1024**2;sha.update(part)
            identity=lambda v:[v.st_dev,v.st_ino,v.st_mode,v.st_uid,v.st_gid,v.st_nlink,v.st_size,v.st_mtime_ns,v.st_ctime_ns]
            assert identity(before)==identity(os.fstat(fd))==identity(path.lstat()) and size==info.st_size
            result[str(path)]=dict(identity=identity(before),sha256=sha.hexdigest())
        finally:os.close(fd)
    for path in ('/data/GitRepos/whale-deck/.env','/data/GitRepos/whale-deck/.images.env','/etc/sudoers.d/whaledeck-agent',
        '/etc/sudoers.d/90-whaledeck-development-user','/etc/whaledeck/agent-capability.key','/etc/whaledeck/resources.json','/etc/whaledeck/agent.env',
        '/var/lib/whaledeck-agent/platform-maintenance/deployment-revisions.json'):add(Path(path))
    for path in (Path('/var/lib/whaledeck-container-acl'),STATE):
        trusted(path,True)
        for leaf in path.rglob('*.json'):add(leaf)
    with storage.target(create_namespace=False,read_only=True) as (configuration,fd):
        storage.attest(configuration,fd,read_only=True)
        base=Path(os.readlink('/proc/self/fd/'+str(fd)))
        for namespace in ('container-acl-v1','container-recursive-acl-v1'):
            path=base/namespace;trusted(path,True)
            for leaf in path.rglob('*.json'):add(leaf)
    return result


def selected_kinds(arguments):
    arguments=tuple(arguments)
    assert arguments in ((),('cancel',),('logout',),('revocation',),('group-removal',),('account-disable',),('restart',),('worker-crash',),('agent-restart',)), 'UNKNOWN_RECURSIVE_ACL_FAULT_SELECTION'
    return REVOKED_KINDS if arguments==('revocation',) else RESTART_KINDS if arguments==('restart',) else arguments or ('cancel','logout')


def terminal_state(kind):
    assert kind in ('cancel','logout',*REVOKED_KINDS,*RESTART_KINDS)
    return 'Failed' if kind in RESTART_KINDS else 'Canceled'


def terminal_summary(row,record,children):
    assert type(row['state']) is str and row['state'] in ('Failed','Canceled')
    assert type(children) is int and 0<=children<=1 and record['state']=='Aborted'
    counts=[record[k] for k in ('completedCount','changedCount','abortedCount','notStartedCount','unknownCount')]
    assert all(type(n) is int and n>=0 for n in counts)
    return dict(id=row['id'],executionJobId=row['execution_job_id'],state=row['state'],rootState='Aborted',aggregateHddRecords=1,childHddRecords=children,counts=counts)


def verify_revoked_browser(ui, kind, subject):
    assert kind in REVOKED_KINDS and ui['kind']==kind and ui['subject']==subject
    assert ui['authorityRevoked'] is True and ui['normalDualLogout'] is False and ui['logout'] is None
    assert ui['anonymousGet401'] is False
    assert ui['originalIntentStatus'] in (401,403) and ui['hintsCleared'] is True and ui['privatePanelsAbsent'] is True
    assert ui['cancellations']==0 and ui['uploads']==0
    if kind=='group-removal':
        assert ui['currentIdentityStatus']==200 and ui['currentlyAdministrator'] is False and ui['currentSubject']==subject and ui['renderedRevocationPage']=='forbidden'
    else:
        assert ui['currentIdentityStatus'] in (401,403) and ui['currentlyAdministrator'] is None and ui['currentSubject'] is None and ui['renderedRevocationPage']=='login'


def wait_settled(identifier):
    deadline=time.monotonic()+145
    while True:
        value=snapshot(identifier)
        if value['row']['completed_at_utc'] is not None: return value
        assert time.monotonic()<deadline,'RECURSIVE_ACL_TERMINAL_DEADLINE'
        time.sleep(.5)


def verify_terminal(identifier, baseline, subject, storage, directory, *, read_only=False):
    """Permanent facts only; source bytes are separately proved by the parent."""
    expected=terminal_state(baseline['kind'])
    data=snapshot(identifier);row=data['row'];plan=managed_plan(json.loads(row['plan_json']))
    assert row['actor_subject']==subject and row['container_id']==baseline['containerId'] and row['instance_id']==baseline['instanceId']
    assert row['relative_path']==plan['command']['path']=='work/recursive-tree'
    assert row['state']==data['executionJob']['state']==expected and row['result_code']==('CONTAINER_RECURSIVE_ACL_INTERRUPTED' if expected=='Failed' else 'CONTAINER_RECURSIVE_ACL_CANCELED')
    assert data['accepted']==data['confirmed']==1 and data['pending']==0 and data['planJob']['state']=='Succeeded'
    assert row['root_started_at_utc'] and row['completed_at_utc'] and not row['publish_barrier'] and row['lease_token'] is row['lease_expires_at_utc'] is None
    assert bool(row['cancel_requested_at_utc']) is (baseline['kind']=='cancel')
    assert plan['rootIdentity']==baseline['original']['']['identity'] and (plan['command']['fileEntries'],plan['command']['directoryEntries'])==expected_targets(1)
    assert plan['entryCount']==plan['changeCount']==4 and plan['fileCount']==plan['directoryCount']==2
    assert row['history_may_exist'] and row['history_verified'] and re.fullmatch('[A-F0-9]{64}',row['receipt_hash'])
    record=record_for(row);operation=uuid.UUID(row['execution_job_id']).hex;receipt=json.loads(row['receipt_json'])
    assert record['state']==row['root_state']==receipt['State']=='Aborted'
    assert receipt['Id']==row['execution_job_id'] and receipt['BindingCurrent'] is False and receipt['HistoryAvailable'] is True
    assert receipt['ContainerId']==row['container_id'] and receipt['InstanceId']==row['instance_id'] and receipt['AclType']=='access'
    assert receipt['Fingerprint']==plan['fingerprint'] and receipt['TargetProof']==plan['targetProof']
    assert record['finishedThrough']==-1 and type(record['startedThrough']) is int and record['startedThrough'] in (-1,0)
    assert record['historyMayExist'] is record['historyVerified'] is receipt['HistoryMayExist'] is receipt['HistoryVerified'] is True
    for key in ('completedCount','changedCount','abortedCount','notStartedCount','unknownCount'):
        column=re.sub(r'(?<!^)(?=[A-Z])','_',key).lower()
        assert type(record[key]) is int and record[key]==row[column]==receipt[key[0].upper()+key[1:]]
    assert record['completedCount']==record['changedCount']==record['unknownCount']==0
    assert record['abortedCount'] in (0,1) and record['notStartedCount']==4-record['abortedCount']
    assert (record['startedThrough']==-1)==(record['abortedCount']==0)
    archive(directory,'final-sql.json',json.dumps(data,sort_keys=True).encode(),read_only)
    archive(directory,'final-root.json',(STATE/(operation+'.json')).read_bytes(),read_only)
    children=0
    with storage.target(create_namespace=False,read_only=True) as (configuration,base):
        fd=storage.f.open_at(base,'container-recursive-acl-v1',os.O_RDONLY|os.O_DIRECTORY)
        try:
            storage.attest(configuration,fd,read_only=True)
            raw=read_metadata(storage.f,storage,configuration,fd,operation+'.json');saved=json.loads(raw)
            assert saved['resourceType']=='ContainerRecursiveAclMetadata' and saved['storageId']=='database-platform.backup.hdd'
            assert saved['operationId']==operation and saved['owner']==row['owner_hash'] and saved['plan']==plan
            assert saved['historyDigest']==record['historyDigest']==row['history_digest']==receipt['HistoryDigest']==digest(b'whaledeck-container-recursive-acl-history-v1\n',unsigned(saved,'historyDigest'))
            assert plan['manifestDigest']==digest(b'whaledeck-container-recursive-acl-manifest-v1\n',saved['entries'])
            assert [e['relativePath'] for e in saved['entries']]==['sub/中文.bin','one.bin','sub','']
            for entry in saved['entries']:
                old=baseline['original'][entry['relativePath']];p=entry['plan']
                assert p['sourceIdentity']==old['identity'] and p['accessAcl']==old['access'] and p['defaultAcl']==old['default'] and p['noChange'] is False
                assert digest(b'whaledeck-container-acl-plan-v1\n',unsigned(p,'fingerprint'))==p['fingerprint']
            archive(directory,'final-hdd.json',raw,read_only)
            child_directory=STATE/(operation+'.entries');outcomes=[]
            if child_directory.exists():
                trusted(child_directory,True)
                child_names={child_id(operation,0)+'.json'} if record['startedThrough']==0 else set()
                actual={p.name for p in child_directory.iterdir() if p.name.endswith('.json')};assert actual==child_names
            else:assert record['startedThrough']==-1
            for index,entry in enumerate(saved['entries']):
                child=child_id(operation,index);phase='NotStarted'
                if index<=record['startedThrough']:
                    p=child_directory/(child+'.json');trusted(p);raw=p.read_bytes();r=json.loads(raw)
                    assert r['operationId']==child and r['owner']==row['owner_hash'] and r['plan']==entry['plan']
                    assert r['state']=='Aborted' and r['aclConfirmed'] is False and r['actualIdentity'] is None and r['version']==''
                    archive(directory,child+'.root.json',raw,read_only);phase='Aborted'
                    if r['historyVerified']:
                        child_fd=storage.f.open_at(fd,operation+'/entries',os.O_RDONLY|os.O_DIRECTORY)
                        try:storage.attest(configuration,child_fd,read_only=True);history_raw=read_metadata(storage.f,storage,configuration,child_fd,child+'.json',16384)
                        finally:os.close(child_fd)
                        history=json.loads(history_raw)
                        assert history['resourceType']=='ContainerRecursiveAclEntryMetadata' and history['parentOperationId']==operation and history['manifestDigest']==plan['manifestDigest']
                        assert history['operationId']==child and history['owner']==row['owner_hash'] and history['plan']==entry['plan']
                        assert history['historyDigest']==r['historyDigest']==digest(b'whaledeck-container-recursive-acl-entry-history-v1\n',unsigned(history,'historyDigest'))
                        archive(directory,child+'.hdd.json',history_raw,read_only);children+=1
                outcomes.append(dict(id=child,state=phase,version='',changed=False))
            expected_outcome='' if record['startedThrough']<0 else digest(b'whaledeck-recursive-acl-outcomes-v1\n',outcomes)
            assert record['outcomeDigest']==receipt['OutcomeDigest']==expected_outcome and (row['outcome_digest'] or '')==expected_outcome
        finally:os.close(fd)
    return terminal_summary(row,record,children)


def run_case(kind, revision, source_revision, hashes, evidence, reader, storage, acl):
    global PHASE
    PHASE=kind+':idle-check';no_active_work();preflight();worker=Worker();directory=evidence/kind;directory.mkdir(mode=0o700)
    held=browser=None;identifier=None;proved=False;root=None;attempt=dict(agentRestartAttempted=False)
    with owned.fixture(restartable_core=('whaledeck-worker',)) as fixture, temporary_user(True) as credentials:
        root=fixture['folder']/'recursive-tree';root.mkdir(mode=0o750);(root/'sub').mkdir(mode=0o700)
        content=hashlib.shake_256(('recursive-acl-fault:'+fixture['identifier']).encode()).digest(2*1024**2+7)
        owners={'':(2100,3100),'one.bin':(2200,3200),'sub':(2101,3101),'sub/中文.bin':(2201,3201)}
        for relative,entry_kind in PATHS.items():
            path=root/relative
            if entry_kind=='File':path.write_bytes(content)
            os.setxattr(path,'user.whaledeck-recursive',b'preserve ordinary attribute');os.chown(path,*owners[relative]);path.chmod(0o750 if relative=='' else 0o700 if entry_kind=='Directory' else 0o640 if relative=='one.bin' else 0o600)
        for relative in PATHS:os.utime(root/relative,ns=(1700000000123456789,1700000000123456789))
        inspect=json.loads(command('docker','inspect',fixture['identifier']))[0]
        current=lambda:inventory(root,reader,content,acl)
        original=current();baseline=dict(revision=revision,validationSourceRevision=source_revision,validationSourceSha256=hashes,kind=kind,
            containerId=fixture['identifier'],containerName=inspect['Name'].lstrip('/'),containerPid=inspect['State']['Pid'],
            instanceId=str(inspect['State']['Pid'])+':'+inspect['State']['StartedAt'],source=str(fixture['folder']),original=original,
            sha256=hashlib.sha256(content).hexdigest().upper(),bytesPerFile=len(content),evidence=str(directory),coreBefore=owned.common.core_snapshot())
        with storage.target(create_namespace=False,read_only=True) as (configuration,fd):
            storage.attest(configuration,fd,read_only=True);baseline.update(sourceDevice=root.stat().st_dev,hddDevice=os.fstat(fd).st_dev)
        (directory/'baseline.json').write_text(json.dumps(baseline))
        try:
            environment={**os.environ};environment.pop('VALIDATION_USERNAME',None);environment.pop('VALIDATION_PASSWORD',None)
            with (directory/'browser.stdout').open('xb') as out,(directory/'browser.stderr').open('xb') as err:
                browser=subprocess.Popen(['node',str(Path(__file__).with_suffix('.mjs')),revision],stdin=subprocess.PIPE,stdout=out,stderr=err,env=environment)
                browser.stdin.write(json.dumps(dict(fixture=baseline,credentials=dict(zip(('username','password'),credentials)))).encode());browser.stdin.close();browser.stdin=None
                PHASE=kind+':visible-preflight';ready=wait_signal(directory,'ready.json',browser);identifier=ready['id']
                assert snapshot(identifier)['row']['state']=='Ready';preflight([identifier]);worker.stop()
                write_signal(directory,'confirm.json',dict(workerStopped=True))
                accepted=wait_signal(directory,'confirmed.json',browser,30);row=snapshot(identifier)['row']
                assert row['state']=='Pending' and row['root_started_at_utc'] is None and row['execution_job_id']==accepted['executionJobId'] and row['actor_subject']==ready['subject']
                PHASE=kind+':bound-first-entry-cut'
                with ThreadPoolExecutor(max_workers=1) as pool:
                    watch=pool.submit(freeze_before_first_acl,row,fixture['folder'],baseline['containerPid'],original,current)
                    worker.start(wait_ready=False);held=watch.result(timeout=28)
                assert held is not None,'REAL_RECURSIVE_ACL_CUT_MISSED_NOT_COUNTED'
                applying=snapshot(identifier);assert applying['row']['state']=='Applying' and applying['row']['publish_barrier'] and applying['row']['root_started_at_utc']
                assert current()==original
                (directory/'cut-sql.json').write_text(json.dumps(applying));(directory/'cut-root.json').write_text(json.dumps(held.record))
                (directory/'cut-physical.json').write_text(json.dumps(current()));(directory/'bound-helper.json').write_text(json.dumps(dict(pid=held.pid,unit=held.cgroup,proof=held.proof)))
                PHASE=kind+':actual-visible-action';authority=None;service=None;restart_row=None
                if kind in REVOKED_KINDS:
                    write_signal(directory,'action.json',dict(kind=kind));action=wait_signal(directory,'action-done.json',browser,40)
                    assert dt.datetime.fromisoformat(ready['permissionExpiresAtUtc'])>dt.datetime.now(dt.timezone.utc)+dt.timedelta(hours=1)
                    with OwnedAuthentikRevocation(credentials[0],ready['subject']) as native:
                        assert current()==original and not select.select([held.descriptor],[],[],0)[0],'BOUND_RECURSIVE_ACL_HELPER_ALREADY_EXITED'
                        began=time.monotonic()
                        with ThreadPoolExecutor(max_workers=1) as pool:
                            exit_watch=pool.submit(held.wait_exit_seconds,began,15)
                            authority=native.change(kind)
                            (directory/'authority-change.json').write_text(json.dumps(authority))
                            require_authority_change(authority,ready,kind)
                            elapsed=exit_watch.result(timeout=16)
                elif kind in RESTART_KINDS:
                    write_signal(directory,'action.json',dict(kind=kind));action=wait_signal(directory,'action-done.json',browser,40)
                    elapsed,service,restart_row=restart(kind,held,worker,applying['row'],revision,directory,attempt)
                    (directory/'service-restart.json').write_text(json.dumps(service))
                else:
                    began=time.monotonic()
                    with ThreadPoolExecutor(max_workers=1) as pool:
                        exit_watch=pool.submit(held.wait_exit_seconds,began,15)
                        write_signal(directory,'action.json',dict(kind=kind));action=wait_signal(directory,'action-done.json',browser,40)
                        elapsed=exit_watch.result(timeout=16)
                assert elapsed is not None and elapsed<15,'PRODUCT_DID_NOT_STOP_BOUND_RECURSIVE_ACL_HELPER'
                (directory/'product-helper-exit.json').write_text(json.dumps(dict(productStoppedHelperSeconds=elapsed,validatorKilledHelper=False)))
                held.release();held=None
                assert action['id']==identifier and action['subject']==ready['subject']
                if kind=='worker-crash':
                    PHASE=kind+':original-natural-lease';natural_lease(restart_row,current,original,directory)
                PHASE=kind+':physical-and-permanent-facts';wait_settled(identifier);proof=verify_terminal(identifier,baseline,ready['subject'],storage,directory)
                assert current()==original;(directory/'physical-final.json').write_text(json.dumps(current()))
                write_signal(directory,'settled.json',dict(id=identifier,state=terminal_state(kind)))
                assert browser.wait(timeout=100)==0,'REAL_RECURSIVE_ACL_TERMINAL_PAGE_FAILED'
                ui=json.loads((directory/'browser.json').read_bytes());assert ui['actualUi'] and ui['id']==identifier and ui['subject']==ready['subject']
                assert ui['registrations']==ui['confirmations']==1 and ui['cancellations']==int(kind=='cancel') and ui['uploads']==0
                if authority:verify_revoked_browser(ui,kind,ready['subject'])
                else:assert ui['normalDualLogout'] and ui['anonymousGet401']
                proof.update(kind=kind,subject=ready['subject'],productStoppedHelperSeconds=round(elapsed,3),validatorKilledHelper=False,originalFullTreeUnchanged=True,normalDualLogout=authority is None,authorityRevoked=authority is not None)
                if service:
                    proof['serviceRestart']=service
                    verify_restart(kind,proof,baseline,ui,snapshot(identifier)['row'],directory,lambda p:json.loads(p.read_bytes()))
                (directory/'verified.json').write_text(json.dumps(proof));proved=True
        finally:
            if held is not None:held.release()
            try:restore_fault_services(worker,attempt['agentRestartAttempted'],attempt.get('hostsBefore'),revision)
            finally:
                if browser and browser.poll() is None:
                    browser.terminate()
                    try:browser.wait(timeout=15)
                    except subprocess.TimeoutExpired:browser.kill();browser.wait(timeout=10)
            # A failed or missed cut retains its source and receipts for inspection.
            if proved:remove_proved(root,reader,content,original,acl)
    assert not fixture['folder'].exists()
    return proof


def main():
    global PHASE
    assert os.geteuid()==0 and len(sys.argv)>=2;os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0'
    kinds=selected_kinds(sys.argv[2:]);revision=sys.argv[1];assert re.fullmatch('[a-f0-9]{40}',revision)
    repo=Path(__file__).resolve().parents[2]
    assert not subprocess.check_output(['git','-C',str(repo),'status','--porcelain']).strip(),'FREEZE_VALIDATION_SOURCE_FIRST'
    source=subprocess.check_output(['git','-C',str(repo),'rev-parse','HEAD'],text=True).strip();hashes=source_closure(repo,source,seeds=SOURCES)
    assert all(hashlib.sha256((repo/'deploy/validation'/name).read_bytes()).hexdigest()==sha for name,sha in hashes.items())
    hosts=host_identity(revision);no_active_work();preflight();before=owned.common.core_snapshot();installed={}
    for name in (*NAMES,'whaledeck-container-backups.py','whaledeck-container-acl-transport.py','whaledeck-container-recursive-acl-transport.py','whaledeck-privileged'):
        path=Path('/usr/local/libexec')/name;trusted(path)
        raw=subprocess.check_output(['git','-C',str(repo),'show',revision+':deploy/host/'+name]);assert path.read_bytes()==raw;installed[name]=hashlib.sha256(raw).hexdigest()
    evidence=repo/'deploy/artifacts'/('container-recursive-acl-faults-'+uuid.uuid4().hex);evidence.mkdir(mode=0o700)
    print(json.dumps(dict(recursiveAclFaults='started',evidence=str(evidence))),flush=True)
    scope=dict(revision=revision,validationSourceRevision=source,validationSourceSha256=hashes,hostIdentity=hosts,sourceHashes=installed,coreBefore=before,kinds=kinds)
    (evidence/'scope.json').write_text(json.dumps(scope))
    reader=load('/usr/local/libexec/whaledeck-container-files.py','recursive_acl_fault_reader');files=reader.trusted_module('whaledeck-files.py','recursive_acl_fault_files')
    storage=load('/usr/local/libexec/whaledeck-file-backups.py','recursive_acl_fault_storage').FileBackups(files);acl=load('/usr/local/libexec/whaledeck-container-acl.py','recursive_acl_fault_acl')
    preserved=preservation_snapshot(storage);scope['preservedFiles']=preserved;(evidence/'scope.json').write_text(json.dumps(scope))
    results=[]
    for kind in kinds:
        results.append(run_case(kind,revision,source,hashes,evidence,reader,storage,acl));(evidence/'results.json').write_text(json.dumps(results))
    no_active_work();preflight();hosts_after=host_identity(revision)
    if 'agent-restart' in kinds:require_host_restart(hosts,hosts_after,revision)
    else:assert hosts_after==hosts
    after=owned.common.core_snapshot();assert before.keys()==after.keys()
    for name,value in before.items():assert after[name][:3]==value[:3] if name.lstrip('/')=='whaledeck-worker' else after[name]==value
    observed=preservation_snapshot(storage);assert all(observed.get(k)==v for k,v in preserved.items())
    (evidence/'preservation-after.json').write_text(json.dumps(observed))
    (evidence/'verified.json').write_text(json.dumps(dict(actualVisibleIdentityAndActions=True,cases=len(results),kinds=kinds,rootAndHddRetained=True,physicalBytesCheckedBeforeCleanup=True,sourceBytesRereadAfterCleanup=False,onlyWorkerRestarted='agent-restart' not in kinds,hostsUnchanged=hosts_after==hosts,hostIdentityAfter=hosts_after)))
    print(json.dumps(dict(recursiveAclFaults='passed',evidence=evidence.name,cases=len(results))),flush=True)


if __name__=='__main__':
    try:main()
    except BaseException as error:
        import traceback
        frames=[dict(file=Path(f.filename).name,line=f.lineno) for f in traceback.extract_tb(error.__traceback__) if 'deploy/validation/' in f.filename]
        print(json.dumps(dict(recursiveAclFaults='failed',phase=PHASE,failureType=type(error).__name__,locations=frames)),flush=True)
        raise SystemExit(1) from None
