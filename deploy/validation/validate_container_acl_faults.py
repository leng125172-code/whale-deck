#!/usr/bin/env python3
"""Installed ACL pre-syscall cancellation/logout on exact disposable resources.

No installed hooks, arbitrary process signals, injected sessions, SQL/Root edits,
clock edits, replayed writes or automatic rollback. Root/HDD history is retained.
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

import requests
import validate_container_file_publishing as owned
from bound_container_acl_cut import ROOT, freeze_before_acl, record_for
from validate_container_acl_browser import SOURCES as BROWSER_SOURCES, snapshot, managed_plan, no_active_work, read_metadata, inventory, PATHS, NAMES, remove_proved
from validate_container_directory_restart import Worker, command, preflight
from validate_container_file_staging import load
from validate_container_upload_bff import temporary_user

PHASE = 'startup'
SOURCES = tuple(dict.fromkeys((*BROWSER_SOURCES, 'bound_container_acl_cut.py', 'bound_container_copy_cut.py',
    'validate_container_acl_faults.py', 'validate_container_acl_faults.mjs', 'verify_container_acl_faults.py')))


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


def verify_terminal(identifier, subject, baseline, storage, module, directory=None):
    """Read permanent facts; optionally archive to a NEW evidence directory."""
    data=snapshot(identifier);row=data['row'];plan=managed_plan(json.loads(row['plan_json']))
    assert row['actor_subject']==subject and row['container_id']==baseline['containerId'] and row['instance_id']==baseline['instanceId']
    assert row['relative_path']==plan['command']['path']=='work/acl-file.bin'
    assert row['state']==data['executionJob']['state']=='Canceled' and data['planJob']['state']=='Succeeded'
    assert data['accepted']==data['confirmed']==1 and data['pending']==0
    assert row['root_started_at_utc'] and row['completed_at_utc'] and not row['publish_barrier'] and row['lease_token'] is row['lease_expires_at_utc'] is None
    assert row['acl_confirmed'] is False and row['history_may_exist'] and row['history_verified'] and not row['published_version']
    assert plan['sourceIdentity']==baseline['original']['acl-file.bin']['identity'] and plan['accessAcl']==baseline['original']['acl-file.bin']['access'] and plan['defaultAcl']==baseline['original']['acl-file.bin']['default']
    assert plan['command']['entries']==__import__('validate_container_acl_browser').profile('access') and plan['noChange'] is False
    record=record_for(row);operation=uuid.UUID(row['execution_job_id']).hex;raw=(ROOT/(operation+'.json')).read_bytes()
    assert json.loads(raw)==record and record['state']=='Aborted' and record['aclConfirmed'] is False
    assert record['actualIdentity'] is None and record['version']=='' and record['historyVerified'] and record['historyMayExist']
    receipt=json.loads(row['receipt_json']);assert receipt['Id']==row['execution_job_id'] and receipt['State']=='Aborted' and receipt['AclConfirmed'] is False
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


def run_case(kind, revision, evidence, source, reader, module, storage):
    global PHASE
    PHASE=kind+':preflight';no_active_work();worker=Worker();directory=evidence/kind;directory.mkdir(mode=0o700)
    held=browser=None;identifier=None;proved=False;current=None
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
        (directory/'baseline.json').write_text(json.dumps(baseline))
        try:
            PHASE=kind+':normal-visible-preflight'
            with (directory/'browser.stdout').open('wb') as out,(directory/'browser.stderr').open('wb') as err:
                browser=subprocess.Popen(['node',str(Path(__file__).with_suffix('.mjs')),revision],stdin=subprocess.PIPE,stdout=out,stderr=err,env={**os.environ,'VALIDATION_USERNAME':username,'VALIDATION_PASSWORD':password})
                browser.stdin.write(json.dumps(baseline).encode());browser.stdin.close();browser.stdin=None
                ready=wait_signal(directory,'ready.json',browser);identifier=ready['id'];assert snapshot(identifier)['row']['state']=='Ready'
                preflight([identifier]);worker.stop();write_signal(directory,'confirm.json',{'workerStopped':True})
                accepted=wait_signal(directory,'confirmed.json',browser,30);row=snapshot(identifier)['row']
                assert row['state']=='Pending' and row['root_started_at_utc'] is None and row['execution_job_id']==accepted['executionJobId'] and row['actor_subject']==ready['subject']
                PHASE=kind+':exact-installed-pre-syscall-history-cut'
                with ThreadPoolExecutor(max_workers=1) as pool:
                    watch=pool.submit(freeze_before_acl,row,folder,baseline['containerPid'],'HistoryVerified');worker.start(wait_ready=False);held=watch.result(timeout=28)
                assert held is not None,'REAL_ACL_CUT_MISSED_NOT_COUNTED'
                applying=snapshot(identifier)['row'];assert applying['state']=='Applying' and applying['root_started_at_utc'] and applying['publish_barrier'] and held.record['historyVerified']
                assert inventory(folder,reader,module,content)==original
                (directory/'cut-root.json').write_text(json.dumps(held.record));(directory/'bound-helper.json').write_text(json.dumps({'pid':held.pid,'unit':held.cgroup,'twoKernelFdProofs':True,'proof':held.proof}))
                PHASE=kind+':visible-action-and-product-helper-exit';began=time.monotonic()
                with ThreadPoolExecutor(max_workers=1) as pool:
                    exit_observer=pool.submit(held.wait_exit_seconds,began,15);write_signal(directory,'action.json',{'kind':kind})
                    action=wait_signal(directory,'action-done.json',browser,30);elapsed=exit_observer.result(timeout=16)
                assert elapsed is not None and elapsed<15,'PRODUCT_DID_NOT_STOP_BOUND_ACL_HELPER'
                held.release();held=None;PHASE=kind+':original-terminal-and-full-source-proof'
                final=wait_settled(identifier);data,record,_,_=verify_terminal(identifier,ready['subject'],baseline,storage,module,directory)
                assert final==data and bool(data['row']['cancel_requested_at_utc']) is (kind=='cancel')
                current=inventory(folder,reader,module,content);assert current==original
                (directory/'physical-final.json').write_text(json.dumps(current));write_signal(directory,'settled.json',{'id':identifier,'state':'Canceled'})
                assert browser.wait(timeout=65)==0,'ACTUAL_ACL_TERMINAL_PAGE_NOT_VERIFIED'
                ui=json.loads((directory/'browser.json').read_bytes());assert ui['actualUi'] and ui['normalDualLogout'] and ui['id']==identifier and ui['subject']==ready['subject']
                assert ui['registrations']==ui['confirmations']==1 and ui['cancellations']==(1 if kind=='cancel' else 0) and ui['bodies']==0
                result={'kind':kind,'id':identifier,'executionJobId':data['row']['execution_job_id'],'state':'Canceled','code':data['row']['result_code'],
                    'rootState':record['state'],'rootCutState':json.loads((directory/'cut-root.json').read_bytes())['state'],'productStoppedHelperSeconds':round(elapsed,3),
                    'validatorKilledHelper':False,'originalFullBytesFingerprintAclsAndAttributesUnchanged':True,'actualUi':True,'normalDualLogout':True}
                (directory/'verified.json').write_text(json.dumps(result));proved=True
        finally:
            if held is not None:held.release()
            worker.start()
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
            if proved:remove_proved(folder,reader,module,content,original)
            else:cleanup_original_after_failure(identifier,directory,folder,reader,module,content,original,browser_started=browser is not None)

    assert not folder.exists() and subprocess.run(['docker','inspect',baseline['containerId']],capture_output=True,timeout=10).returncode!=0
    return result


def main():
    global PHASE
    assert os.geteuid()==0 and len(sys.argv) in (2,3);os.umask(0o077)
    revision=sys.argv[1];assert re.fullmatch('[a-f0-9]{40}',revision);kinds=('cancel','logout')
    if len(sys.argv)==3:assert sys.argv[2] in kinds;kinds=(sys.argv[2],)
    repo=Path(__file__).resolve().parents[2];assert not subprocess.check_output(['git','-C',str(repo),'status','--porcelain']).strip(),'FREEZE_VALIDATION_SOURCE_FIRST'
    source=subprocess.check_output(['git','-C',str(repo),'rev-parse','HEAD'],text=True).strip();hashes={}
    for name in SOURCES:
        raw=subprocess.check_output(['git','-C',str(repo),'show',source+':deploy/validation/'+name]);assert raw==(repo/'deploy/validation'/name).read_bytes();hashes[name]=hashlib.sha256(raw).hexdigest()
    installed={}
    for name in (*NAMES,'whaledeck-container-acl-transport.py','whaledeck-privileged'):
        path=Path('/usr/local/libexec')/name;raw=subprocess.check_output(['git','-C',str(repo),'show',revision+':deploy/host/'+name]);assert path.read_bytes()==raw;installed[name]=hashlib.sha256(raw).hexdigest()
    with requests.Session() as session:
        session.trust_env=False;r=session.get('http://127.0.0.1:8080/api/v1/system/status',timeout=10);assert r.status_code==200 and r.json()['build']['sourceRevision']==revision,'TARGET_PRODUCT_NOT_DEPLOYED'
    no_active_work();evidence=repo/'deploy/artifacts'/('container-acl-faults-'+uuid.uuid4().hex);evidence.mkdir(mode=0o700)
    (evidence/'validation-source.json').write_text(json.dumps({'revision':source,'sha256':hashes,'installedSha256':installed,'productRevision':revision}))
    reader=load('/usr/local/libexec/whaledeck-container-files.py','acl_fault_reader');module=load('/usr/local/libexec/whaledeck-container-acl.py','acl_fault_module');files=reader.trusted_module('whaledeck-files.py','acl_fault_files');storage=load('/usr/local/libexec/whaledeck-file-backups.py','acl_fault_hdd').FileBackups(files)
    print(json.dumps({'aclFaults':'started','evidence':str(evidence),'kinds':kinds}),flush=True);results=[]
    for kind in kinds:
        results.append(run_case(kind,revision,evidence,source,reader,module,storage));(evidence/'results.json').write_text(json.dumps(results));print(json.dumps({'aclFaults':'case-passed',**results[-1]}),flush=True)
    no_active_work();(evidence/'verified.json').write_text(json.dumps({'revision':revision,'cases':results,'coreHealthy':len(owned.common.core_snapshot())}))
    print(json.dumps({'aclFaults':'passed','evidence':str(evidence),'cases':len(results)}),flush=True)


if __name__=='__main__':
    try:main()
    except BaseException as error:
        import traceback
        print(json.dumps({'aclFaults':'failed','phase':PHASE,'failureType':type(error).__name__,'locations':[{'file':Path(f.filename).name,'line':f.lineno} for f in traceback.extract_tb(error.__traceback__)]}),flush=True)
        raise SystemExit(1) from None
