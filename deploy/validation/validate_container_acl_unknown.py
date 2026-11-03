#!/usr/bin/env python3
"""Installed real ACL Unknown, distinct-admin UI ack and cross-container write.

Only exact owned containers are removed. No Root/SQL/cache/time edits, injected
sessions, arbitrary kill or dev rollback. Unknown/HDD originals remain intact.
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

import requests
import validate_container_file_publishing as owned
from bound_container_acl_cut import ROOT, freeze_before_acl, freeze_after_acl, record_for
from validate_container_acl_faults import SOURCES as FAULT_SOURCES, wait_signal, write_signal, wait_settled, snapshot, managed_plan, no_active_work, read_metadata, inventory, PATHS, NAMES, remove_proved, host_snapshot, load
from validate_container_acl_browser import profile as acl_profile, version, expected_job_summary, validate_job_summary
from validate_container_acl import unchanged_except_target
from validate_container_directory_restart import Worker, preflight, command, clean_success
from validate_container_directory_unknown import register as register_waiting, wait_settled as settle_waiting
from validate_container_directory_browser import snapshot as directory_snapshot, ROOT as DIRECTORY_ROOT
from validate_container_upload_bff import login, logout, request, temporary_user
from validate_databases import Api

PHASE='startup'
SOURCES=tuple(dict.fromkeys((*FAULT_SOURCES,'validate_container_acl_unknown.py','validate_container_acl_unknown.mjs','verify_container_acl_unknown.py',
    'validate_container_directory_unknown.py','validate_container_directory_browser.py','bound_container_directory_cut.py')))
WAITING_PATH='work/ACL未知保护后的目录'


def expected_physical(baseline):
    return baseline['cutPhysical'] if baseline.get('postAcl',False) else baseline['original']


def require_cut(baseline,record,proof,physical):
    original=baseline['original'];post=baseline.get('postAcl',False)
    assert type(post) is bool and record['aclConfirmed'] is None and record['historyVerified']
    assert record['actualIdentity'] is None and record['version']==''
    assert proof['originalIdentity']==record['plan']['sourceIdentity']==original['acl-file.bin']['identity']
    if post:
        assert record['state']=='Applying';unchanged_except_target(original,physical,'acl-file.bin',record['plan']['targetMode'])
        assert physical['acl-file.bin']['identity']==proof['changedIdentity'] and physical['acl-file.bin']['identity'][8]>original['acl-file.bin']['identity'][8]
        assert physical['acl-file.bin']['access']==proof['accessAcl']==record['plan']['command']['entries']
    else:assert record['state'] in ('Preparing','Prepared') and physical==original and proof['accessAcl']==record['plan']['accessAcl']
    assert proof['defaultAcl']==physical['acl-file.bin']['default']==record['plan']['defaultAcl'] and proof['attributesDigest']==record['plan']['attributesDigest']


def verify_unknown_jobs(data,proof,stage):
    row=data['row'];assert stage in ('unknown','acknowledged') and row['state']==('Unknown' if stage=='unknown' else 'Acknowledged')
    assert proof['id']==row['id'] and proof['stage']==stage and proof['readOnly'] is True
    plan=managed_plan(json.loads(row['plan_json']))
    for execution in (False,True):
        expected=expected_job_summary(row,plan,execution);identifier=row['execution_job_id'] if execution else row['id'];state='Succeeded'
        if execution:
            state='Running' if stage=='unknown' else 'Failed'
            expected.update(state=row['state'],code=row['result_code'],aclConfirmed=None,receiptState='Unknown',bindingCurrent=False,hasPublishedVersion=False)
        sql=data['executionJob'] if execution else data['planJob'];api=proof['execution'] if execution else proof['plan']
        value=validate_job_summary(sql,expected,identifier,state);assert validate_job_summary(api,expected,identifier,state)==value
        text=json.dumps(value)
        for key in ('relative_path','instance_id','actor_subject','login_binding','owner_hash','request_hash','plan_hash','execution_hash','receipt_hash','history_digest'):
            if row.get(key):assert row[key] not in text
    center=proof['taskCenter'];assert center['id']==row['execution_job_id'] and center['nullConfirmationVisible'] is True and center['noGenericWriteControls'] is True


def spawn(mode, revision, baseline, username, password, directory):
    with (directory/(mode+'.stdout')).open('wb') as out,(directory/(mode+'.stderr')).open('wb') as err:
        child=subprocess.Popen(['node',str(Path(__file__).with_suffix('.mjs')),revision,mode],stdin=subprocess.PIPE,stdout=out,stderr=err,
            env={**os.environ,'VALIDATION_USERNAME':username,'VALIDATION_PASSWORD':password})
        child.stdin.write(json.dumps(baseline).encode());child.stdin.close();child.stdin=None;return child


def save_facts(data, baseline, storage, module, directory=None, label='unknown'):
    row=data['row'];assert row['state'] in ('Unknown','Acknowledged') and row['acl_confirmed'] is None and row['published_version'] is None
    assert row['actor_subject']==baseline['originalSubject'] and row['container_id']==baseline['containerId'] and row['instance_id']==baseline['instanceId']
    assert row['relative_path']=='work/acl-file.bin' and row['root_started_at_utc'] and row['history_verified'] and row['history_may_exist']
    assert data['planJob']['state']=='Succeeded' and data['accepted']==data['confirmed']==1
    assert row['lease_token'] is None and row['lease_expires_at_utc'] is None
    if row['state']=='Unknown':assert row['publish_barrier'] and row['completed_at_utc'] is None and data['executionJob']['state']=='Running'
    else:assert not row['publish_barrier'] and row['completed_at_utc'] and data['executionJob']['state']=='Failed' and data['pending']==0
    record=record_for(row);plan=record['plan'];operation=uuid.UUID(row['execution_job_id']).hex;raw=(ROOT/(operation+'.json')).read_bytes()
    assert json.loads(raw)==record and record['state']=='Unknown' and record['aclConfirmed'] is None and record['actualIdentity'] is None and record['version']==''
    assert record['historyVerified'] and record['historyMayExist'] and record['historyDigest']==row['history_digest']
    original=baseline['original']['acl-file.bin'];assert plan['sourceIdentity']==original['identity'] and plan['accessAcl']==original['access'] and plan['defaultAcl']==original['default']
    assert plan['command']['entries']==acl_profile('access') and plan['command']['aclType']=='access' and plan['noChange'] is False
    assert plan['command']['expectedVersion']==version(baseline,'work/acl-file.bin',original['identity']) and plan['uid']==plan['gid']==1000
    receipt=json.loads(row['receipt_json']);assert re.fullmatch('[A-F0-9]{64}',row['receipt_hash'])
    assert receipt['Id']==row['execution_job_id'] and receipt['State']=='Unknown' and receipt['AclConfirmed'] is None and receipt['Version']=='' and receipt['BindingCurrent'] is False
    for key,expected in {'ContainerId':row['container_id'],'InstanceId':row['instance_id'],'Fingerprint':plan['fingerprint'],'TargetProof':plan['targetProof'],
        'Kind':plan['kind'],'AclType':'access','TargetMode':plan['targetMode'],'NoChange':False,'HistoryDigest':row['history_digest'],'HistoryMayExist':True,'HistoryVerified':True}.items():assert receipt[key]==expected
    with storage.target(create_namespace=False,read_only=True) as (configuration,base):
        fd=storage.f.open_at(base,'container-acl-v1',os.O_RDONLY|os.O_DIRECTORY)
        try:
            storage.attest(configuration,fd,read_only=True);hdd=read_metadata(storage.f,storage,configuration,fd,operation+'.json',16384)
        finally:os.close(fd)
    history=json.loads(hdd);assert history['resourceType']=='ContainerAclMetadata' and history['storageId']=='database-platform.backup.hdd'
    assert history['plan']==plan and history['operationId']==operation and history['owner']==row['owner_hash']
    digest=hashlib.sha256(b'whaledeck-container-acl-history-v1\n'+module.encode({k:v for k,v in history.items() if k!='historyDigest'})).hexdigest().upper()
    assert digest==history['historyDigest']==record['historyDigest']==row['history_digest']
    if directory:
        assert label in ('unknown','final','failed')
        (directory/(label+'-sql.json')).write_text(json.dumps(data));(directory/(label+'-root.json')).write_bytes(raw);(directory/(label+'-hdd.json')).write_bytes(hdd)
    return record,raw,hdd


def require_blocked(value, waiting, actor, observed):
    row=value['row'];assert row['id']==waiting['id'] and row['actor_subject']==actor and row['state']=='Pending'
    assert row['root_started_at_utc'] is None and not row['publish_barrier'] and row['completed_at_utc'] is None
    assert row['container_id']==waiting['containerId'] and row['relative_path']==WAITING_PATH
    return type(row['version']) is int and row['version']>=2 and row['lease_token'] is None and row['lease_expires_at_utc'] is None and dt.datetime.fromisoformat(row['next_attempt_at_utc'])>observed


def observe_waiting(identifier, waiting, waiting_baseline, other, actor, original, baseline, reader, module, content, raw, directory):
    until=time.monotonic()+65;samples=[];versions=set();target=other['folder']/WAITING_PATH.removeprefix('work/');root=DIRECTORY_ROOT/(uuid.UUID(waiting['id']).hex+'.json')
    while len(samples)<4:
        assert time.monotonic()<until,'FOUR_REAL_DIRECTORY_DEFERRALS_NOT_OBSERVED'
        value=directory_snapshot(waiting['id']);observed=dt.datetime.now(dt.timezone.utc);deferred=require_blocked(value,{**waiting,'containerId':waiting_baseline['containerId']},actor,observed)
        unknown=snapshot(identifier)['row'];assert unknown['state']=='Unknown' and unknown['publish_barrier'] and unknown['completed_at_utc'] is None
        assert not os.path.lexists(target) and not root.exists() and (ROOT/(uuid.UUID(unknown['execution_job_id']).hex+'.json')).read_bytes()==raw
        assert inventory(original['folder'],reader,module,content)==expected_physical(baseline)
        assert set(p.name for p in other['folder'].iterdir())=={'original.bin'} and owned.common.fingerprint(other['folder']/'original.bin')==other['proofs']['original.bin'][0]
        assert owned.common.file_digest(other['folder']/'original.bin')==other['proofs']['original.bin'][1]
        if deferred and value['row']['version'] not in versions:
            versions.add(value['row']['version']);samples.append({'observedAtUtc':observed.isoformat(),'sql':value,'rootAbsent':True,'targetAbsent':True,'aclUnknownProtected':True,'sourceUnchanged':True})
        if len(samples)<4:time.sleep(.5)
    assert len({s['sql']['row']['next_attempt_at_utc'] for s in samples})==4
    (directory/'waiting-blocked.json').write_text(json.dumps(samples));return samples


def failure_ack(identifier):
    """Only ordinary current-admin BFF ack; a fallback never makes a gate pass."""
    with temporary_user(True) as (name,password):
        session,_=login('http://127.0.0.1:8080',name,password)
        try:
            api=Api('http://127.0.0.1:8080',session);endpoint='http://127.0.0.1:8080/api/v1/files/container-acls/recovery/'+identifier
            value=request(session,'GET',endpoint,200).json()
            if value['state']=='Unknown':request(session,'POST',endpoint+'/acknowledge',202,headers={**api.headers,'Idempotency-Key':identifier},json={'version':value['version'],'confirmed':True})
            logout('http://127.0.0.1:8080',session)
        finally:session.close()


def main():
    global PHASE
    assert os.geteuid()==0 and len(sys.argv) in (2,3);os.umask(0o077);revision=sys.argv[1];assert re.fullmatch('[a-f0-9]{40}',revision)
    post=len(sys.argv)==3
    if post:assert sys.argv[2]=='post-acl'
    repo=Path(__file__).resolve().parents[2];assert not subprocess.check_output(['git','-C',str(repo),'status','--porcelain']).strip(),'FREEZE_VALIDATION_SOURCE_FIRST'
    source_revision=subprocess.check_output(['git','-C',str(repo),'rev-parse','HEAD'],text=True).strip();hashes={}
    for name in SOURCES:
        frozen=subprocess.check_output(['git','-C',str(repo),'show',source_revision+':deploy/validation/'+name]);assert frozen==(repo/'deploy/validation'/name).read_bytes();hashes[name]=hashlib.sha256(frozen).hexdigest()
    installed={}
    for name in (*NAMES,'whaledeck-container-acl-transport.py','whaledeck-privileged'):
        raw=subprocess.check_output(['git','-C',str(repo),'show',revision+':deploy/host/'+name]);assert raw==(Path('/usr/local/libexec')/name).read_bytes();installed[name]=hashlib.sha256(raw).hexdigest()
    base='http://127.0.0.1:8080'
    with requests.Session() as public:
        public.trust_env=False;r=public.get(base+'/api/v1/system/status',timeout=10);assert r.status_code==200 and r.json()['build']['sourceRevision']==revision,'TARGET_PRODUCT_NOT_DEPLOYED'
    no_active_work();hosts=host_snapshot(revision);worker=Worker();directory=repo/'deploy/artifacts'/('container-acl-unknown-'+uuid.uuid4().hex);directory.mkdir(mode=0o700)
    (directory/'validation-source.json').write_text(json.dumps({'proofVersion':3 if post else 2,'revision':source_revision,'productRevision':revision,'sha256':hashes,'installedSha256':installed}))
    reader=load('/usr/local/libexec/whaledeck-container-files.py','acl_unknown_reader');module=load('/usr/local/libexec/whaledeck-container-acl.py','acl_unknown_module');files=reader.trusted_module('whaledeck-files.py','acl_unknown_files');storage=load('/usr/local/libexec/whaledeck-file-backups.py','acl_unknown_hdd').FileBackups(files)
    print(json.dumps({'aclUnknown':'started','evidence':str(directory)}),flush=True)
    held=owner_browser=recovery_browser=None;identifier=waiting=None;client=profile=None;original_proved=waiting_proved=False
    with owned.fixture(restartable_core=('whaledeck-worker',),removable=True) as original,owned.fixture(restartable_core=('whaledeck-worker',)) as other:
        folder=original['folder'];(folder/'acl-directory').mkdir(mode=0o750);content=hashlib.shake_256(('acl-unknown:'+original['identifier']).encode()).digest(2*1024**2+7)
        for relative,kind in PATHS.items():
            p=folder/relative
            if kind=='File':p.write_bytes(content);os.setxattr(p,'user.whaledeck-acl',b'preserve ordinary attribute')
            os.chown(p,2100,3100);p.chmod(0o640 if kind=='File' else 0o750)
        for relative in PATHS:os.utime(folder/relative,ns=(1700000000123456789,1700000000123456789))
        actual=json.loads(command('docker','inspect',original['identifier']))[0];other_actual=json.loads(command('docker','inspect',other['identifier']))[0]
        baseline={'revision':revision,'validationSourceRevision':source_revision,'containerId':original['identifier'],'containerName':actual['Name'].lstrip('/'),
            'instanceId':str(actual['State']['Pid'])+':'+actual['State']['StartedAt'],'containerPid':actual['State']['Pid'],'source':str(folder),'original':inventory(folder,reader,module,content),
            'bytesPerFile':len(content),'sha256':hashlib.sha256(content).hexdigest().upper(),'coreBefore':owned.common.core_snapshot(),'hostsBefore':hosts,'evidence':str(directory),'postAcl':post}
        (directory/'baseline.json').write_text(json.dumps(baseline))
        try:
            PHASE='original-real-page-preflight'
            with temporary_user(True) as (username,password):
                owner_browser=spawn('owner',revision,baseline,username,password,directory);ready=wait_signal(directory,'owner-ready.json',owner_browser);identifier=ready['id']
                preflight([identifier]);worker.stop();write_signal(directory,'confirm.json',{'workerStopped':True});accepted=wait_signal(directory,'owner-confirmed.json',owner_browser,30)
                row=snapshot(identifier)['row'];assert row['state']=='Pending' and row['execution_job_id']==accepted['executionJobId'];baseline.update(id=identifier,executionJobId=row['execution_job_id'],originalSubject=ready['subject']);(directory/'baseline.json').write_text(json.dumps(baseline))
                PHASE='actual-post-acl-cut' if post else 'actual-pre-acl-hdd-cut'
                with ThreadPoolExecutor(max_workers=1) as pool:
                    observing=pool.submit(freeze_after_acl if post else freeze_before_acl,row,folder,baseline['containerPid'],'HistoryVerified');worker.start(wait_ready=False);held=observing.result(timeout=28)
                assert held is not None and held.record['historyVerified'] and held.record['aclConfirmed'] is None,'REAL_ACL_UNKNOWN_CUT_MISSED'
                physical=inventory(folder,reader,module,content);require_cut(baseline,held.record,held.proof,physical)
                if post:baseline['cutPhysical']=physical;(directory/'baseline.json').write_text(json.dumps(baseline))
                (directory/'cut-root.json').write_text(json.dumps(held.record));(directory/'bound-helper.json').write_text(json.dumps({'pid':held.pid,'unit':held.cgroup,'twoKernelFdProofs':True,'proof':held.proof}))
                write_signal(directory,'held.json',{'rootPausedAfterAcl':True} if post else {'rootPausedBeforeAcl':True});wait_signal(directory,'owner-inflight.json',owner_browser,30)
                PHASE='remove-exact-owned-container-then-normal-logout';original['remove_owned']();assert not select.select([held.descriptor],[],[],0)[0]
                after_remove=inventory(folder,reader,module,content);assert after_remove==expected_physical(baseline)
                removed={'containerId':baseline['containerId'],'onlyExactOwnedContainerRemoved':True,'helperAliveBeforeLogout':True,'sourceFullInventoryUnchanged':True}
                if post:removed.update(sourceInventoryReference='post-acl-cut',sourceInventory=after_remove)
                (directory/'removed-owned.json').write_text(json.dumps(removed))
                began=time.monotonic()
                with ThreadPoolExecutor(max_workers=1) as pool:
                    observing=pool.submit(held.wait_exit_seconds,began,15);write_signal(directory,'logout.json',{'ownedContainerRemoved':True})
                    assert owner_browser.wait(timeout=35)==0,'ORIGINAL_NORMAL_LOGOUT_FAILED';elapsed=observing.result(timeout=16)
                assert elapsed is not None and elapsed<15,'PRODUCT_DID_NOT_STOP_UNKNOWN_ACL_HELPER';held.release();held=None
                owner_ui=json.loads((directory/'owner-browser.json').read_bytes());assert owner_ui['subject']==ready['subject'] and owner_ui['actualUi'] and owner_ui['normalDualLogout']
            PHASE='original-unknown-facts';unknown=wait_settled(identifier);record,raw_root,raw_hdd=save_facts(unknown,baseline,storage,module,directory)
            assert unknown['row']['state']=='Unknown' and inventory(folder,reader,module,content)==expected_physical(baseline)
            with temporary_user(True) as (username,password):
                client,profile=login(base,username,password);api=Api(base,client);assert profile['subject']!=baseline['originalSubject']
                PHASE='other-container-four-natural-deferrals';waiting,waiting_baseline=register_waiting(base,client,api,other,WAITING_PATH)
                waiting_baseline.update(subject=profile['subject'],containerName=other_actual['Name'].lstrip('/'),originalFileProof=other['proofs']['original.bin']);(directory/'waiting-baseline.json').write_text(json.dumps(waiting_baseline))
                observe_waiting(identifier,waiting,waiting_baseline,other,profile['subject'],original,baseline,reader,module,content,raw_root,directory)
                PHASE='distinct-admin-visible-recovery-and-dismiss';recovery_browser=spawn('recovery',revision,{**baseline,'waitingId':waiting['id'],'waitingContainerName':waiting_baseline['containerName']},username,password,directory)
                dismissed=wait_signal(directory,'recovery-dismissed.json',recovery_browser,80);assert dismissed['id']==identifier and dismissed['subject']==profile['subject'] and dismissed['confirmations']==0
                if post:verify_unknown_jobs(unknown,json.loads((directory/'unknown-jobs.json').read_bytes()),'unknown')
                still=snapshot(identifier);assert still['row']==unknown['row'] and still['executionJob']['state']=='Running'
                assert (ROOT/(record['operationId']+'.json')).read_bytes()==raw_root and inventory(folder,reader,module,content)==expected_physical(baseline)
                assert directory_snapshot(waiting['id'])['row']['state']=='Pending' and not (other['folder']/WAITING_PATH.removeprefix('work/')).exists()
                write_signal(directory,'allow-ack.json',{'originalStillUnknownProtected':True,'waitingStillDeferred':True})
                PHASE='distinct-admin-explicit-ack-and-visible-waiting-publication';assert recovery_browser.wait(timeout=150)==0,'REAL_ACL_UNKNOWN_RECOVERY_PAGE_FAILED'
                ui=json.loads((directory/'recovery-browser.json').read_bytes());assert ui['subject']==profile['subject'] and ui['normalDualLogout'] and ui['actualUi'] and ui['waitingVisibleInVue']
                final=snapshot(identifier);final_record,final_root,final_hdd=save_facts(final,baseline,storage,module,directory,'final');assert final['row']['state']=='Acknowledged' and final['row']['acknowledged_by']==profile['subject']
                if post:verify_unknown_jobs(final,json.loads((directory/'acknowledged-jobs.json').read_bytes()),'acknowledged')
                for key in ('receipt_json','receipt_hash','acl_confirmed','history_may_exist','history_verified','history_digest','published_version','actor_subject','login_binding','execution_job_id','plan_json','command_json'):assert final['row'][key]==unknown['row'][key]
                assert final_root==raw_root and final_hdd==raw_hdd and final_record==record
                waiting_final=settle_waiting(waiting['id']);assert waiting_final['row']['state']=='Succeeded' and waiting_final['row']['root_started_at_utc']>=final['row']['completed_at_utc'];(directory/'waiting-final-sql.json').write_text(json.dumps(waiting_final))
                PHASE='late-original-apply-remains-unknown';late=subprocess.run(['/usr/local/libexec/whaledeck-privileged','container-file-acl','apply',uuid.uuid4().hex],input=json.dumps({k:record[k] for k in ('operationId','owner','plan')})+'\n',capture_output=True,text=True,timeout=35)
                assert late.returncode==0 and len(late.stdout.encode())<=32768 and len(late.stderr.encode())<=32768
                replay=[json.loads(line) for line in late.stdout.splitlines() if line.strip()][-1];assert replay['state']=='Unknown' and replay['aclConfirmed'] is None and replay['operationId']==record['operationId'] and replay['replayed']
                assert (ROOT/(record['operationId']+'.json')).read_bytes()==raw_root and inventory(folder,reader,module,content)==expected_physical(baseline);(directory/'late-apply.json').write_text(json.dumps(replay))
                (directory/'physical-final.json').write_text(json.dumps(inventory(folder,reader,module,content)));remove_proved(folder,reader,module,content,expected_physical(baseline));original_proved=True
                clean_success(waiting,waiting_baseline,profile,directory,other);waiting_proved=True;logout(base,client);client.close();client=None
            worker.start();no_active_work();assert host_snapshot(revision)==hosts
            summary={'revision':revision,'id':identifier,'waitingId':waiting['id'],'originalSubject':baseline['originalSubject'],'recoverySubject':profile['subject'],
                'businessState':'Acknowledged','executionJobState':'Failed','rootState':'Unknown','aclConfirmed':None,'historyVerified':True,
                'actualOriginalAndRecoveryPages':True,'normalOriginalAndRecoveryLogout':True,'originalIdentityDeletedBeforeRecovery':True,
                'productStoppedHelperSeconds':round(elapsed,3),'validatorKilledHelper':False,'otherWriteDeferredUntilRelease':True,'actualDirectoryVisibleInVue':True,
                'originalFullBytesFingerprintAclsAndAttributesUnchanged':not post,'postAclPhysicalInventoryPreserved':post,'lateOriginalApplyRemainsUnknown':True,'rootSha256':hashlib.sha256(raw_root).hexdigest()}
            (directory/'verified.json').write_text(json.dumps(summary))
        finally:
            if held is not None:held.release()
            worker.start()
            for child in (owner_browser,recovery_browser):
                if child is not None and child.poll() is None:
                    try:child.wait(timeout=235)
                    except subprocess.TimeoutExpired:
                        child.terminate()
                        try:child.wait(timeout=15)
                        except subprocess.TimeoutExpired:child.kill();child.wait(timeout=10)
                        raise
            registered=directory/'owner-registered.json'
            if identifier is None and registered.exists():identifier=json.loads(registered.read_bytes())['id']
            if identifier is not None and not original_proved:
                failed=wait_settled(identifier)
                if failed['row']['state']=='Unknown':failure_ack(identifier);failed=snapshot(identifier);(directory/'failure-fallback.json').write_text(json.dumps({'normalBffAcknowledgement':True,'gatePassed':False}))
                row=failed['row'];(directory/'failed-gate-sql.json').write_text(json.dumps(failed))
                if (not post or 'cutPhysical' in baseline) and row['state'] in ('Acknowledged','Canceled','Failed','Succeeded','Superseded') and not row['publish_barrier'] and row['lease_token'] is None and row['lease_expires_at_utc'] is None and inventory(folder,reader,module,content)==expected_physical(baseline):
                    remove_proved(folder,reader,module,content,expected_physical(baseline));original_proved=True
            if waiting is not None and not waiting_proved:
                end=settle_waiting(waiting['id'])
                if end['row']['state']=='Succeeded':clean_success(waiting,waiting_baseline,profile,directory,other);waiting_proved=True
            if client is not None:
                try:logout(base,client)
                finally:client.close()
    assert not folder.exists() and not other['folder'].exists()
    print(json.dumps({'aclUnknown':'passed','evidence':str(directory),**summary}),flush=True)


if __name__=='__main__':
    try:main()
    except BaseException as error:
        import traceback
        print(json.dumps({'aclUnknown':'failed','phase':PHASE,'failureType':type(error).__name__,'locations':[{'file':Path(f.filename).name,'line':f.lineno} for f in traceback.extract_tb(error.__traceback__)]}),flush=True)
        raise SystemExit(1) from None
