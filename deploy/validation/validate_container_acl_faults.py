#!/usr/bin/env python3
"""Installed ACL pre-syscall cancellation/logout on exact disposable resources.

No installed hooks, arbitrary process signals, injected sessions, SQL/Root edits,
clock edits, replayed writes or automatic rollback. Root/HDD history is retained.
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
import threading
import time
import uuid

import requests
import validate_container_file_publishing as owned
from bound_container_acl_cut import ROOT, freeze_before_acl, freeze_after_acl, record_for
from validate_container_acl_browser import SOURCES as BROWSER_SOURCES, snapshot, managed_plan, no_active_work, read_metadata, inventory, PATHS, NAMES, remove_proved, version, expected_job_summary, validate_job_summary
from validate_container_directory_restart import Worker, command, preflight, agent_identity
from validate_container_file_staging import load
from validate_container_upload_bff import temporary_user, login, logout, request
from owned_authentik_revocation import OwnedAuthentikRevocation
from validate_metric_continuity import query
from validate_container_acl import unchanged_except_target

PHASE = 'startup'
SOURCES = tuple(dict.fromkeys((*BROWSER_SOURCES, 'bound_container_acl_cut.py', 'bound_container_copy_cut.py',
    'validate_container_acl_faults.py', 'validate_container_acl_faults.mjs', 'verify_container_acl_faults.py', 'owned_authentik_revocation.py')))


def write_signal(directory, name, value):
    assert re.fullmatch(r'[a-z-]+\.json', name)
    target = directory/name
    with target.with_suffix('.pending').open('x') as out:
        json.dump(value, out); out.flush(); os.fsync(out.fileno())
    target.with_suffix('.pending').replace(target)


def wait_signal(directory, name, browser, timeout=80):
    until = time.monotonic()+timeout
    while time.monotonic()<until:
        path = directory/name
        if path.exists():
            assert not path.is_symlink() and path.stat().st_size<=32768
            return json.loads(path.read_bytes())
        assert browser.poll() is None, 'ACTUAL_ACL_BROWSER_EXITED_BEFORE_SIGNAL'
        assert not (directory/'browser-failed.json').exists(), 'ACTUAL_ACL_BROWSER_REPORTED_FAILURE'
        time.sleep(.04)
    raise AssertionError('ACL_BROWSER_SIGNAL_DEADLINE')


def wait_settled(identifier):
    until = time.monotonic()+145
    while True:
        value=snapshot(identifier)
        if value['row']['state'] in ('Succeeded','Superseded','Failed','Canceled','Unknown','Acknowledged'): return value
        assert time.monotonic()<until,'ACL_TERMINAL_DEADLINE'; time.sleep(.5)


def terminal_state(kind):
    assert kind in ('cancel','logout','group-removal','account-disable','worker-crash','agent-restart','post-acl-cancel','post-acl-worker-crash','post-acl-group-removal','post-acl-account-disable','post-acl-agent-restart')
    if kind.startswith('post-acl-'):return 'Succeeded'
    return 'Failed' if kind in ('worker-crash','agent-restart') else 'Canceled'


def requested_kinds(selector):
    groups={'revocation':('group-removal','account-disable'),'restart':('worker-crash','agent-restart'),
        'post-change':('post-acl-cancel','post-acl-worker-crash'),'post-revocation':('post-acl-group-removal','post-acl-account-disable')}
    if selector in groups:return groups[selector]
    terminal_state(selector)
    return (selector,)


def expected_fault_acl(row,baseline,storage,module):
    if 'aclRestoreFaultVersion' in baseline:
        from acl_restore_fault_proof import require_restore_profile,verify_restore_binding
        require_restore_profile(baseline)
        return verify_restore_binding(row,baseline,storage,module)
    return __import__('validate_container_acl_browser').profile('access')


def verify_terminal(identifier, subject, baseline, storage, module, directory=None):
    """Read permanent facts; optionally archive to a NEW evidence directory."""
    data=snapshot(identifier);row=data['row'];plan=managed_plan(json.loads(row['plan_json']))
    assert row['actor_subject']==subject and row['container_id']==baseline['containerId'] and row['instance_id']==baseline['instanceId']
    assert row['relative_path']==plan['command']['path']=='work/acl-file.bin'
    expected=terminal_state(baseline['kind']);post=expected=='Succeeded'
    assert row['state']==data['executionJob']['state']==expected and data['planJob']['state']=='Succeeded'
    assert row['result_code']==('CONTAINER_ACL_COMPLETED' if post else 'CONTAINER_ACL_INTERRUPTED' if expected=='Failed' else 'CONTAINER_ACL_CANCELED')
    assert data['accepted']==data['confirmed']==1 and data['pending']==0
    assert row['root_started_at_utc'] and row['completed_at_utc'] and not row['publish_barrier'] and row['lease_token'] is row['lease_expires_at_utc'] is None
    assert row['acl_confirmed'] is post and row['history_may_exist'] and row['history_verified']
    assert bool(row['published_version']) is post
    assert plan['sourceIdentity']==baseline['original']['acl-file.bin']['identity'] and plan['accessAcl']==baseline['original']['acl-file.bin']['access'] and plan['defaultAcl']==baseline['original']['acl-file.bin']['default']
    desired=expected_fault_acl(row,baseline,storage,module)
    assert plan['command']['entries']==desired and plan['noChange'] is False
    record=record_for(row);operation=uuid.UUID(row['execution_job_id']).hex;raw=(ROOT/(operation+'.json')).read_bytes()
    assert json.loads(raw)==record and record['state']==('Completed' if post else 'Aborted') and record['aclConfirmed'] is post
    assert record['historyVerified'] and record['historyMayExist']
    if post:
        actual=baseline['cutPhysical']['acl-file.bin']['identity'];assert record['actualIdentity']==actual
        assert record['version']==row['published_version']==version(baseline,'work/acl-file.bin',actual)
    else:assert record['actualIdentity'] is None and record['version']==''
    receipt=json.loads(row['receipt_json']);assert receipt['Id']==row['execution_job_id'] and receipt['State']==record['state'] and receipt['AclConfirmed'] is post
    assert receipt['Version']==record['version'] and receipt['BindingCurrent'] is post
    assert receipt['Kind']==plan['kind'] and receipt['TargetMode']==plan['targetMode'] and receipt['NoChange'] is plan['noChange']
    assert re.fullmatch('[A-F0-9]{64}',row['receipt_hash'])
    assert receipt['HistoryMayExist'] and receipt['HistoryVerified'] and receipt['Fingerprint']==plan['fingerprint'] and receipt['TargetProof']==plan['targetProof']
    assert receipt['ContainerId']==row['container_id'] and receipt['InstanceId']==row['instance_id'] and receipt['AclType']=='access'
    with storage.target(create_namespace=False,read_only=True) as (configuration,base):
        fd=storage.f.open_at(base,'container-acl-v1',os.O_RDONLY|os.O_DIRECTORY)
        try:
            storage.attest(configuration,fd,read_only=True);hdd_raw=read_metadata(storage.f,storage,configuration,fd,operation+'.json',16384)
        finally:os.close(fd)
    hdd=json.loads(hdd_raw)
    assert hdd['resourceType']=='ContainerAclMetadata' and hdd['storageId']=='database-platform.backup.hdd'
    assert hdd['operationId']==operation and hdd['owner']==row['owner_hash'] and hdd['plan']==plan
    digest=hashlib.sha256(b'whaledeck-container-acl-history-v1\n'+module.encode({k:v for k,v in hdd.items() if k!='historyDigest'})).hexdigest().upper()
    assert digest==hdd['historyDigest']==record['historyDigest']==row['history_digest']==receipt['HistoryDigest']
    if directory:
        (directory/'final-sql.json').write_text(json.dumps(data));(directory/'final-root.json').write_bytes(raw);(directory/'final-hdd.json').write_bytes(hdd_raw)
    return data,record,raw,hdd_raw


def current_admin_terminal(identifier, original_subject, execution, version_value, directory, *, baseline=None):
    """Read existing facts with a different currently authorized BFF identity."""
    base='http://127.0.0.1:8080'
    with temporary_user(True) as (name,password):
        session,profile=login(base,name,password)
        try:
            assert profile['subject']!=original_subject and profile['isAdministrator'] is True
            response=request(session,'GET',base+'/api/v1/files/container-acls/recovery/'+identifier,200);intent=response.json()
            assert intent['id']==identifier and intent['executionJobId']==execution and intent['state']=='Succeeded' and intent['publishedVersion']==version_value
            extra={}
            if baseline and baseline.get('aclRestoreFaultVersion')==5:
                assert 'no-store' in response.headers.get('Cache-Control','')
                path='/api/v1/containers/'+baseline['containerId']+'/files/acls/restores/'+identifier
                denied=request(session,'GET',base+path,404)
                extra=dict(ownerRestoreStatus=denied.status_code,ownerRestorePath=path,recoveryNoStore=True)
            logout(base,session)
            proof={'subject':profile['subject'],'currentlyAdministrator':True,'readStatus':200,'intent':intent,'readOnlyOperation':True,'normalHttpDualLogout':True,**extra}
            (directory/'current-admin-read.json').write_text(json.dumps(proof));return proof
        finally:session.close()


def verify_task_results(data, baseline, ui, proof):
    assert baseline['kind']=='post-acl-agent-restart' and baseline['jobResultsVersion']==ui['jobResultsVersion']==1
    assert type(baseline['jobResultsVersion']) is type(ui['jobResultsVersion']) is int
    row=data['row'];plan=managed_plan(json.loads(row['plan_json']));assert proof['intentId']==row['id']
    assert ui['jobReadsOnly'] is True and len(proof['taskCenter'])==2
    for index,execution in enumerate((False,True)):
        identifier=row['execution_job_id'] if execution else row['id'];expected=expected_job_summary(row,plan,execution)
        sql=data['executionJob'] if execution else data['planJob'];api=proof['execution'] if execution else proof['plan']
        value=validate_job_summary(sql,expected,identifier);assert validate_job_summary(api,expected,identifier)==value
        text=json.dumps(value)
        for key in ('relative_path','instance_id','actor_subject','login_binding','owner_hash','request_hash','plan_hash','execution_hash','receipt_hash'):
            if row.get(key):assert row[key] not in text
        for secret in (plan['fingerprint'],plan['targetProof'],row['history_digest'],row['published_version']):assert secret not in text
        center=proof['taskCenter'][index];assert center['id']==identifier and center['intentId']==row['id'] and center['execution'] is execution
        assert center['screenshot']==('execution-job.png' if execution else 'plan-job.png') and center['noGenericWriteControls'] is True


def cleanup_original_after_failure(identifier, directory, folder, reader, module, content, original, *, browser_started):
    """A lost registration signal is uncertainty, never permission to delete."""
    if identifier is None:
        if browser_started: return False
    else:
        failed=wait_settled(identifier);(directory/'failed-gate-sql.json').write_text(json.dumps(failed));row=failed['row']
        if row['state'] not in ('Canceled','Failed','Succeeded','Superseded') or row['publish_barrier'] or row['lease_token'] is not None or row['lease_expires_at_utc'] is not None: return False
        if row['execution_job_id']:
            record=record_for(row);(directory/'failed-gate-root.json').write_text(json.dumps(record))
            if record['state'] not in ('Aborted','Completed'): return False
    if inventory(folder,reader,module,content)!=original: return False
    remove_proved(folder,reader,module,content,original);return True


def cleanup_post_acl_failure(identifier, baseline, directory, folder, reader, module, storage, content):
    """A missed cut stays FAILED; only proven own Completed data can be removed."""
    if identifier is None:return False
    failed=wait_settled(identifier);row=failed['row'];(directory/'failed-gate-sql.json').write_text(json.dumps(failed))
    if row['state']!='Succeeded' or row['publish_barrier'] or row['lease_token'] is not None or row['lease_expires_at_utc'] is not None:return False
    current=inventory(folder,reader,module,content);plan=managed_plan(json.loads(row['plan_json']))
    unchanged_except_target(baseline['original'],current,'acl-file.bin',plan['targetMode'])
    assert current['acl-file.bin']['access']==plan['command']['entries'] and current['acl-file.bin']['default']==plan['defaultAcl']
    _,record,raw,hdd=verify_terminal(identifier,row['actor_subject'],{**baseline,'cutPhysical':current},storage,module)
    assert record['actualIdentity']==current['acl-file.bin']['identity']
    (directory/'failed-gate-root.json').write_bytes(raw);(directory/'failed-gate-hdd.json').write_bytes(hdd)
    remove_proved(folder,reader,module,content,current);(directory/'failed-gate-cleanup.json').write_text(json.dumps({'gatePassed':False,'provedCompletedOwnSourceRemoved':True}))
    return True


RESTART_KEYS={'uploads','directories','copies','moves','permissions','ownerships','recursivePermissions','recursiveOwnerships','acls'}
TRANSIENT=Path('/run/systemd/transient')


def restart_allowed(held, row):
    checks=preflight([row['id'],row['execution_job_id']])
    tables={'uploads':'container_file_upload_operations','directories':'container_directory_operations','copies':'container_copy_intents',
        'moves':'container_move_intents','permissions':'container_permission_intents','ownerships':'container_ownership_intents',
        'recursivePermissions':'container_recursive_permission_intents','recursiveOwnerships':'container_recursive_ownership_intents','acls':'container_acl_intents'}
    counts=query('SELECT json_build_object('+','.join("'"+key+"',(SELECT count(*) FROM "+table+" WHERE (completed_at_utc IS NULL OR publish_barrier OR lease_token IS NOT NULL OR lease_expires_at_utc IS NOT NULL)"+(" AND id<>:'id'::uuid" if key=='acls' else '')+")" for key,table in tables.items())+');',id=row['id'])
    assert set(counts)==RESTART_KEYS and all(type(n) is int and n==0 for n in counts.values()),'OTHER_FILE_WORK_RESTART_REFUSED'
    active=[]
    for path in TRANSIENT.glob('whaledeck-*.service'):
        props=dict(line.split('=',1) for line in command('systemctl','show',path.name,'-p','ActiveState','-p','MainPID','-p','ControlPID','-p','ControlGroup').splitlines())
        if props['ActiveState'] in ('inactive','failed'):
            assert props['MainPID']==props['ControlPID']=='0'
            if props['ControlGroup']:
                processes=Path('/sys/fs/cgroup')/props['ControlGroup'].lstrip('/')/'cgroup.procs'
                assert not processes.exists() or not processes.read_text().split()
        else:active.append(path.name)
    assert set(active)<={held.cgroup},'OTHER_TRANSIENT_WORK_RESTART_REFUSED'
    return {**checks,**counts}


def host_snapshot(revision):
    result={}
    for unit,app in (('whaledeck-agent.service','WhaleDeck.Agent'),('whaledeck-maintenance.service','WhaleDeck.MaintenanceHost')):
        assert command('systemctl','show',unit,'-p','ActiveState','--value')=='active'
        pid=int(command('systemctl','show',unit,'-p','MainPID','--value'));assert pid>1
        exe=Path('/proc')/str(pid)/'exe';assert exe.readlink().name==app
        info=json.loads(command(str(exe),'--build-info'));assert info['sourceRevision']==revision
        result[unit]={'pid':pid,'sourceRevision':revision}
    return result


def require_host_restart(before, after, revision):
    assert before.keys()==after.keys()=={'whaledeck-agent.service','whaledeck-maintenance.service'}
    for value in (*before.values(),*after.values()):assert type(value['pid']) is int and value['pid']>1 and value['sourceRevision']==revision
    assert before['whaledeck-agent.service']['pid']!=after['whaledeck-agent.service']['pid']
    assert before['whaledeck-maintenance.service']==after['whaledeck-maintenance.service']


def maintenance_response(status, raw, cache):
    assert status==200 and 0<len(raw)<=2048 and 'no-store' in cache
    value=json.loads(raw);assert set(value)<={'state','phase','message','operationId','updatedAtUtc'} and value['state']=='healthy' and value['phase']=='Ready'
    return value


def maintenance_sample(client):
    began=time.monotonic()
    with client.get('http://127.0.0.1:8080/maintenance/status',timeout=(1,3),stream=True) as response:
        raw=response.raw.read(2049,decode_content=True);value=maintenance_response(response.status_code,raw,response.headers.get('Cache-Control',''))
    return {'atUtc':dt.datetime.now(dt.timezone.utc).isoformat(),'httpStatus':200,'state':value['state'],'phase':value['phase'],'noStore':True,'bytes':len(raw),'responseSeconds':round(time.monotonic()-began,4)}


def observe_maintenance(stop, ready):
    samples=[]
    with requests.Session() as client:
        client.trust_env=False
        while not stop.is_set():
            samples.append(maintenance_sample(client));ready.set();stop.wait(.2)
    return samples


def observation_expiry(row, now):
    assert row['lease_token'] and now.utcoffset() is not None
    expiry=dt.datetime.fromisoformat(row['lease_expires_at_utc'])
    assert expiry.utcoffset() is not None and dt.timedelta(seconds=25)<expiry-now<=dt.timedelta(seconds=95),'NATURAL_LEASE_OUTSIDE_BOUNDED_WINDOW'
    return expiry


def lease_held(before, current):
    for key in ('id','execution_job_id','container_id','instance_id','actor_subject','owner_hash','root_started_at_utc','plan_json','command_json'):
        assert current[key]==before[key]
    assert current['state']=='Applying' and current['publish_barrier'] and current['completed_at_utc'] is None and current['cancel_requested_at_utc'] is None
    assert current['lease_token']==before['lease_token'] and current['lease_expires_at_utc']==before['lease_expires_at_utc']
    if before.get('restore_source_json') is not None:
        for key in ('restore_source_json','request_hash','plan_hash','execution_hash','login_binding'):assert current[key]==before[key]


def natural_lease(row, folder, reader, module, content, original, directory, *, post=False):
    wall=dt.datetime.now(dt.timezone.utc);expiry=observation_expiry(row,wall);began=time.monotonic()
    path=ROOT/(uuid.UUID(row['execution_job_id']).hex+'.json');raw=path.read_bytes();root=json.loads(raw)
    assert root==record_for(row) and root['state'] in (('Applying',) if post else ('Preparing','Prepared')) and root['historyVerified']
    (directory/'after-helper-exit-root.json').write_bytes(raw);samples=[]
    while dt.datetime.now(dt.timezone.utc)<expiry:
        assert time.monotonic()-began<100,'NATURAL_LEASE_MONOTONIC_DEADLINE'
        current=snapshot(row['id']);observed=dt.datetime.now(dt.timezone.utc)
        if observed>=expiry:break
        lease_held(row,current['row']);assert current['executionJob']['state']=='Running' and current['executionJob']['cancel_requested_at_utc'] is None
        assert path.read_bytes()==raw and inventory(folder,reader,module,content)==original
        samples.append({'atUtc':observed.isoformat(),'state':'Applying','barrier':True,'leaseToken':row['lease_token'],'leaseExpiresAtUtc':row['lease_expires_at_utc'],
            'rootSha256':hashlib.sha256(raw).hexdigest(),'sourceFullBytesAclsAndAttributesUnchanged':True,
            **({'restoreSourceHash':hashlib.sha256(row['restore_source_json'].encode()).hexdigest().upper()} if row.get('restore_source_json') is not None else {})})
        time.sleep(min(2,max(.001,(expiry-observed).total_seconds())))
    ended=dt.datetime.now(dt.timezone.utc);elapsed=time.monotonic()-began
    assert len(samples)>=3 and abs((ended-wall).total_seconds()-elapsed)<3,'NATURAL_LEASE_NOT_OBSERVED'
    proof={'originalLeaseToken':row['lease_token'],'originalLeaseExpiresAtUtc':row['lease_expires_at_utc'],'waitBeganAtUtc':wall.isoformat(),
        'waitEndedAtUtc':ended.isoformat(),'monotonicWaitSeconds':round(elapsed,3),'rootSha256':hashlib.sha256(raw).hexdigest(),
        'rootStateAfterHelperExit':root['state'],'samples':samples,'clocksOrSqlModified':False}
    (directory/'natural-lease.json').write_text(json.dumps(proof));return proof


def run_case(kind, revision, evidence, source, reader, module, storage, *, restore=False):
    global PHASE
    post=kind.startswith('post-acl-');behavior=kind.removeprefix('post-acl-')
    restore_version=None
    if restore:
        from acl_restore_fault_proof import restore_fault_version
        restore_version=restore_fault_version((kind,))
    PHASE=kind+':preflight';no_active_work();worker=Worker();directory=evidence/kind;directory.mkdir(mode=0o700)
    held=browser=None;identifier=None;proved=False;current=None;agent_attempted=False;host=None
    with owned.fixture(restartable_core=('whaledeck-worker',)) as fixture, temporary_user(True) as (username,password):
        folder=fixture['folder'];(folder/'acl-directory').mkdir(mode=0o750)
        content=hashlib.shake_256(('acl-fault:'+fixture['identifier']).encode()).digest(2*1024**2+7)
        for relative,file_kind in PATHS.items():
            path=folder/relative
            if file_kind=='File':path.write_bytes(content);os.setxattr(path,'user.whaledeck-acl',b'preserve ordinary attribute')
            os.chown(path,2100,3100);path.chmod(0o640 if file_kind=='File' else 0o750)
        for relative in PATHS:os.utime(folder/relative,ns=(1700000000123456789,1700000000123456789))
        actual=json.loads(command('docker','inspect',fixture['identifier']))[0];original=inventory(folder,reader,module,content)
        baseline={'revision':revision,'validationSourceRevision':source,'kind':kind,'containerId':fixture['identifier'],'containerName':actual['Name'].lstrip('/'),
            'instanceId':str(actual['State']['Pid'])+':'+actual['State']['StartedAt'],'containerPid':actual['State']['Pid'],'source':str(folder),
            'original':original,'sha256':hashlib.sha256(content).hexdigest().upper(),'bytesPerFile':len(content),'coreBefore':owned.common.core_snapshot(),'evidence':str(directory)}
        if restore:baseline['aclRestoreFaultVersion']=restore_version
        if kind=='post-acl-agent-restart':baseline['jobResultsVersion']=1
        (directory/'baseline.json').write_text(json.dumps(baseline))
        try:
            PHASE=kind+':normal-visible-preflight'
            with (directory/'browser.stdout').open('wb') as out,(directory/'browser.stderr').open('wb') as err:
                browser=subprocess.Popen(['node',str(Path(__file__).with_suffix('.mjs')),revision],stdin=subprocess.PIPE,stdout=out,stderr=err,env={**os.environ,'VALIDATION_USERNAME':username,'VALIDATION_PASSWORD':password})
                browser.stdin.write(json.dumps(baseline).encode());browser.stdin.close();browser.stdin=None
                if restore:
                    from acl_restore_fault_proof import accept_source_capture
                    original=accept_source_capture(browser,directory,baseline,folder,reader,module,storage,content)
                    baseline['original']=original
                ready=wait_signal(directory,'ready.json',browser);identifier=ready['id'];assert snapshot(identifier)['row']['state']=='Ready'
                baseline['permissionExpiresAtUtc']=ready['permissionExpiresAtUtc'];(directory/'baseline.json').write_text(json.dumps(baseline))
                preflight([identifier]);worker.stop();write_signal(directory,'confirm.json',{'workerStopped':True})
                accepted=wait_signal(directory,'confirmed.json',browser,30);row=snapshot(identifier)['row']
                assert row['state']=='Pending' and row['root_started_at_utc'] is None and row['execution_job_id']==accepted['executionJobId'] and row['actor_subject']==ready['subject']
                PHASE=kind+(':exact-installed-post-acl-cut' if post else ':exact-installed-pre-syscall-history-cut')
                with ThreadPoolExecutor(max_workers=1) as pool:
                    watch=pool.submit(freeze_after_acl if post else freeze_before_acl,row,folder,baseline['containerPid'],'HistoryVerified');worker.start(wait_ready=False);held=watch.result(timeout=28)
                assert held is not None,'REAL_ACL_CUT_MISSED_NOT_COUNTED'
                applying=snapshot(identifier)['row'];assert applying['state']=='Applying' and applying['root_started_at_utc'] and applying['publish_barrier'] and held.record['historyVerified']
                cut=inventory(folder,reader,module,content)
                if post:
                    unchanged_except_target(original,cut,'acl-file.bin',held.record['plan']['targetMode'])
                    assert cut['acl-file.bin']['identity']==held.proof['changedIdentity'] and cut['acl-file.bin']['access']==held.record['plan']['command']['entries']
                    baseline['cutPhysical']=cut;(directory/'baseline.json').write_text(json.dumps(baseline))
                else:assert cut==original
                (directory/'cut-physical.json').write_text(json.dumps(cut))
                (directory/'cut-root.json').write_text(json.dumps(held.record));(directory/'bound-helper.json').write_text(json.dumps({'pid':held.pid,'unit':held.cgroup,'twoKernelFdProofs':True,'proof':held.proof}))
                PHASE=kind+':visible-action-and-product-helper-exit';authority=None;restart=None;lease=None
                if behavior in ('group-removal','account-disable'):
                    # Keep the original login and unexpired positive permission snapshot.
                    write_signal(directory,'action.json',{'kind':kind});action=wait_signal(directory,'action-done.json',browser,30)
                    assert dt.datetime.fromisoformat(ready['permissionExpiresAtUtc'])>dt.datetime.now(dt.timezone.utc)+dt.timedelta(hours=1)
                    with OwnedAuthentikRevocation(username,ready['subject']) as native:
                        assert not select.select([held.descriptor],[],[],0)[0],'BOUND_ACL_HELPER_ALREADY_EXITED'
                        assert inventory(folder,reader,module,content)==cut
                        began=time.monotonic()
                        with ThreadPoolExecutor(max_workers=1) as pool:
                            exit_observer=pool.submit(held.wait_exit_seconds,began,15);authority=native.change(behavior)
                            (directory/'authority-change.json').write_text(json.dumps(authority));elapsed=exit_observer.result(timeout=16)
                elif behavior in ('worker-crash','agent-restart'):
                    write_signal(directory,'action.json',{'kind':kind});action=wait_signal(directory,'action-done.json',browser,30)
                    checks=restart_allowed(held,applying);host=host_snapshot(revision)
                    start_frame=snapshot(identifier);applying=start_frame['row'];assert applying['state']=='Applying' and applying['publish_barrier']
                    (directory/'restart-start-sql.json').write_text(json.dumps(start_frame))
                    began=time.monotonic()
                    with ThreadPoolExecutor(max_workers=2) as pool:
                        if behavior=='worker-crash':
                            exit_observer=pool.submit(held.wait_exit_seconds,began,15)
                            before=worker.current();lease=applying['lease_expires_at_utc'];observation_expiry(applying,dt.datetime.now(dt.timezone.utc))
                            worker.stopped=True;command('docker','kill','--signal','KILL',worker.id)
                            crashed=snapshot(identifier);assert crashed['row']['lease_token']==applying['lease_token'] and crashed['row']['root_started_at_utc']==applying['root_started_at_utc']
                            assert crashed['row']['lease_expires_at_utc']==lease
                            applying=crashed['row'];lease=applying['lease_expires_at_utc'];(directory/'after-worker-crash-sql.json').write_text(json.dumps(crashed))
                            automatic=worker.current()['State']['Running'];worker.start(wait_ready=False);after=worker.current()
                            assert after['State']['StartedAt']!=before['State']['StartedAt']
                            (directory/'worker-inspect.json').write_text(json.dumps({'observedAtUtc':dt.datetime.now(dt.timezone.utc).isoformat(),
                                'containerId':after['Id'],'imageId':after['Image'],'startedAt':after['State']['StartedAt'],'running':after['State']['Running']}))
                            restart={'sameWorkerContainerAndImage':True,'workerId':worker.id,'signal':'SIGKILL','manuallyStarted':not automatic,
                                'beforeStartedAt':before['State']['StartedAt'],'afterStartedAt':after['State']['StartedAt'],'originalLeaseExpiresAtUtc':lease,
                                'otherActiveWork':checks,'hostsBefore':host,'hostsAfter':host_snapshot(revision)}
                            assert restart['hostsBefore']==restart['hostsAfter']
                        else:
                            stop=threading.Event();ready_probe=threading.Event();monitor=pool.submit(observe_maintenance,stop,ready_probe)
                            try:
                                assert ready_probe.wait(5),'MAINTENANCE_PRE_RESTART_PROBE_MISSING'
                                began=time.monotonic();started=dt.datetime.now(dt.timezone.utc);exit_observer=pool.submit(held.wait_exit_seconds,began,15)
                                agent_attempted=True;command('systemctl','restart','whaledeck-agent.service')
                                agent_identity(str(host['whaledeck-agent.service']['pid']),revision);after=host_snapshot(revision);require_host_restart(host,after,revision)
                                ended=dt.datetime.now(dt.timezone.utc)
                                with requests.Session() as client:
                                    client.trust_env=False;last=maintenance_sample(client)
                            finally:stop.set()
                            samples=monitor.result(timeout=5)+[last];samples.sort(key=lambda v:v['atUtc']);assert len(samples)>=3
                            (directory/'maintenance-observations.json').write_text(json.dumps(samples))
                            restart={'hostsBefore':host,'hostsAfter':after,'maintenancePidUnchanged':True,'otherActiveWork':checks,
                                'restartBeganAtUtc':started.isoformat(),'restartEndedAtUtc':ended.isoformat(),'totalRestartSeconds':round(time.monotonic()-began,3),'maintenanceSamples':len(samples)}
                        elapsed=exit_observer.result(timeout=16)
                else:
                    began=time.monotonic()
                    with ThreadPoolExecutor(max_workers=1) as pool:
                        exit_observer=pool.submit(held.wait_exit_seconds,began,15);write_signal(directory,'action.json',{'kind':kind})
                        action=wait_signal(directory,'action-done.json',browser,30);elapsed=exit_observer.result(timeout=16)
                assert elapsed is not None and elapsed<15,'PRODUCT_DID_NOT_STOP_BOUND_ACL_HELPER'
                held.release();held=None
                if lease:
                    PHASE=kind+':original-natural-lease';natural_lease(applying,folder,reader,module,content,cut,directory,post=post)
                PHASE=kind+':original-terminal-and-full-source-proof'
                final=wait_settled(identifier);data,record,_,_=verify_terminal(identifier,ready['subject'],baseline,storage,module,directory)
                assert final==data and bool(data['row']['cancel_requested_at_utc']) is (behavior=='cancel')
                if lease:assert dt.datetime.fromisoformat(data['row']['completed_at_utc'])>=dt.datetime.fromisoformat(lease)
                current=inventory(folder,reader,module,content);assert current==cut
                if post:
                    raw=(ROOT/(record['operationId']+'.json')).read_bytes()
                    late=subprocess.run(['/usr/local/libexec/whaledeck-privileged','container-file-acl','apply',uuid.uuid4().hex],
                        input=json.dumps({k:record[k] for k in ('operationId','owner','plan')})+'\n',capture_output=True,text=True,timeout=35)
                    assert late.returncode==0 and len(late.stdout.encode())<=32768 and len(late.stderr.encode())<=32768
                    replay=[json.loads(line) for line in late.stdout.splitlines() if line.strip()][-1]
                    assert replay['operationId']==record['operationId'] and replay['state']=='Completed' and replay['aclConfirmed'] is True and replay['replayed']
                    assert replay['version']==record['version'] and replay['historyDigest']==record['historyDigest']
                    assert (ROOT/(record['operationId']+'.json')).read_bytes()==raw and inventory(folder,reader,module,content)==cut
                    (directory/'late-apply.json').write_text(json.dumps(replay))
                (directory/'physical-final.json').write_text(json.dumps(current));write_signal(directory,'settled.json',{'id':identifier,'state':terminal_state(kind)})
                assert browser.wait(timeout=65)==0,'ACTUAL_ACL_TERMINAL_PAGE_NOT_VERIFIED'
                ui=json.loads((directory/'browser.json').read_bytes());assert ui['actualUi'] and ui['id']==identifier and ui['subject']==ready['subject']
                assert ui['authorityRevoked'] if authority else ui['normalDualLogout']
                assert ui['registrations']==ui['confirmations']==1 and ui['cancellations']==(1 if behavior=='cancel' else 0) and ui['bodies']==0
                if baseline.get('jobResultsVersion')==1:verify_task_results(data,baseline,ui,json.loads((directory/'task-results.json').read_bytes()))
                if post and authority:current_admin_terminal(identifier,ready['subject'],data['row']['execution_job_id'],record['version'],directory,baseline=baseline)
                result={'kind':kind,'id':identifier,'executionJobId':data['row']['execution_job_id'],'state':terminal_state(kind),'code':data['row']['result_code'],
                    'rootState':record['state'],'rootCutState':json.loads((directory/'cut-root.json').read_bytes())['state'],'productStoppedHelperSeconds':round(elapsed,3),
                    'validatorKilledHelper':False,'originalFullBytesFingerprintAclsAndAttributesUnchanged':not post,'postAclIdentityFullBytesAndAclsVerified':post,'actualUi':True,'normalDualLogout':authority is None,'authorityRevoked':authority is not None,'serviceRestart':restart}
                (directory/'verified.json').write_text(json.dumps(result));proved=True
        finally:
            if held is not None:held.release()
            try:
                if agent_attempted:
                    if command('systemctl','show','whaledeck-agent.service','-p','ActiveState','--value')!='active':command('systemctl','start','whaledeck-agent.service')
                    agent_identity(str(host['whaledeck-agent.service']['pid']),revision)
            finally:worker.start()
            if browser is not None and browser.poll() is None:
                try:browser.wait(timeout=235)
                except subprocess.TimeoutExpired:
                    (directory/'browser-timeout.json').write_text(json.dumps({'gatePassed':False,'sourcePreserved':True}))
                    browser.terminate()
                    try:browser.wait(timeout=15)
                    except subprocess.TimeoutExpired:browser.kill();browser.wait(timeout=10)
                    raise

            registered=directory/'registered.json'
            if identifier is None and registered.exists():identifier=json.loads(registered.read_bytes())['id']
            if proved:remove_proved(folder,reader,module,content,current)
            else:
                removed=cleanup_original_after_failure(identifier,directory,folder,reader,module,content,original,browser_started=browser is not None)
                if post and not removed:cleanup_post_acl_failure(identifier,baseline,directory,folder,reader,module,storage,content)

    assert not folder.exists() and subprocess.run(['docker','inspect',baseline['containerId']],capture_output=True,timeout=10).returncode!=0
    return result


def main(restore=False):
    global PHASE
    assert os.geteuid()==0 and len(sys.argv) in (2,3);os.umask(0o077)
    revision=sys.argv[1];assert re.fullmatch('[a-f0-9]{40}',revision);kinds=('cancel','logout')
    if len(sys.argv)==3:
        kinds=requested_kinds(sys.argv[2])
    restore_version=None
    if restore:
        from acl_restore_fault_proof import restore_fault_version
        restore_version=restore_fault_version(kinds)
    repo=Path(__file__).resolve().parents[2];assert not subprocess.check_output(['git','-C',str(repo),'status','--porcelain']).strip(),'FREEZE_VALIDATION_SOURCE_FIRST'
    source=subprocess.check_output(['git','-C',str(repo),'rev-parse','HEAD'],text=True).strip();hashes={}
    source_names=SOURCES
    if restore:
        from acl_restore_fault_proof import RESTORE_SOURCES
        source_names=(*SOURCES,*RESTORE_SOURCES)
    for name in source_names:
        raw=subprocess.check_output(['git','-C',str(repo),'show',source+':deploy/validation/'+name]);assert raw==(repo/'deploy/validation'/name).read_bytes();hashes[name]=hashlib.sha256(raw).hexdigest()
    installed={}
    installed_names=(*NAMES,'whaledeck-container-acl-transport.py','whaledeck-privileged')
    if restore:installed_names=(*installed_names,'whaledeck-container-acl-history.py','whaledeck-container-acl-history-transport.py')
    for name in installed_names:
        path=Path('/usr/local/libexec')/name;raw=subprocess.check_output(['git','-C',str(repo),'show',revision+':deploy/host/'+name]);assert path.read_bytes()==raw;installed[name]=hashlib.sha256(raw).hexdigest()
    with requests.Session() as session:
        session.trust_env=False;r=session.get('http://127.0.0.1:8080/api/v1/system/status',timeout=10);assert r.status_code==200 and r.json()['build']['sourceRevision']==revision,'TARGET_PRODUCT_NOT_DEPLOYED'
    no_active_work();evidence=repo/'deploy/artifacts'/('container-acl-faults-'+uuid.uuid4().hex);evidence.mkdir(mode=0o700)
    (evidence/'validation-source.json').write_text(json.dumps({'schemaVersion':6+restore_version if restore else 6,'revision':source,'sha256':hashes,'installedSha256':installed,'productRevision':revision,**({'aclRestoreFaultVersion':restore_version} if restore else {})}))
    reader=load('/usr/local/libexec/whaledeck-container-files.py','acl_fault_reader');module=load('/usr/local/libexec/whaledeck-container-acl.py','acl_fault_module');files=reader.trusted_module('whaledeck-files.py','acl_fault_files');storage=load('/usr/local/libexec/whaledeck-file-backups.py','acl_fault_hdd').FileBackups(files)
    print(json.dumps({'aclFaults':'started','evidence':str(evidence),'kinds':kinds}),flush=True);results=[]
    for kind in kinds:
        results.append(run_case(kind,revision,evidence,source,reader,module,storage,restore=restore));(evidence/'results.json').write_text(json.dumps(results));print(json.dumps({'aclFaults':'case-passed',**results[-1]}),flush=True)
    no_active_work();(evidence/'verified.json').write_text(json.dumps({'revision':revision,'cases':results,'coreHealthy':len(owned.common.core_snapshot())}))
    print(json.dumps({'aclFaults':'passed','evidence':str(evidence),'cases':len(results)}),flush=True)


if __name__=='__main__':
    try:main()
    except BaseException as error:
        import traceback
        print(json.dumps({'aclFaults':'failed','phase':PHASE,'failureType':type(error).__name__,'locations':[{'file':Path(f.filename).name,'line':f.lineno} for f in traceback.extract_tb(error.__traceback__)]}),flush=True)
        raise SystemExit(1) from None
