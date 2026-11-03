#!/usr/bin/env python3
"""Real stale-page ACL preflight on one exactly owned disposable container.

Only the pinned fixture file's mtime is changed. No Root/SQL/cache/clock edits,
injected browser identity, execution confirmation, service restart or rollback.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import uuid

from validate_container_acl_browser import SOURCES as PREVIOUS_SOURCES, NAMES, PATHS, inventory, no_active_work, snapshot, version, validate_job_summary, remove_proved, owned
from validate_container_acl_faults import wait_signal, write_signal, wait_settled, host_snapshot
from validate_container_file_staging import load
from validate_container_upload_bff import temporary_user, login, logout, request
from validate_container_upload_recovery_agent import trusted
from validate_databases import Api

SOURCES=tuple(dict.fromkeys((*PREVIOUS_SOURCES,'validate_container_acl_faults.py','bound_container_acl_cut.py','bound_container_copy_cut.py','owned_authentik_revocation.py',
    'validate_container_acl_stale.py','validate_container_acl_stale.mjs','verify_container_acl_stale.py')))
CODE='CONTAINER_ACL_VERSION_CONFLICT'
PHASE='startup'


def execution_id(identifier):
    raw=hashlib.sha256(('whaledeck-container-acl-execution-id-v1:'+uuid.UUID(identifier).hex).encode()).digest()[:16]
    return uuid.UUID(bytes_le=raw).hex


def require_touch(before,after):
    assert set(before)==set(after)==set(PATHS)
    for name,old in before.items():
        new=after[name]
        assert len(old['identity'])==len(new['identity'])==9 and all(type(n) is int for n in [*old['identity'],*new['identity']])
        if name!='acl-file.bin':assert new==old;continue
        assert set(new)==set(old)=={'identity','access','default'}
        a,b=old['identity'],new['identity'];assert len(a)==len(b)==9 and a[:7]==b[:7]
        assert b[7]==a[7]+17 and b[8]>a[8]
        assert new['access']==old['access'] and new['default']==old['default']


def expected_summary(identifier):
    return dict(schemaVersion=1,intentId=identifier,stage='planning',state='Failed',code=CODE,aclType='access',
        kind=None,targetMode=None,noChange=None,aclConfirmed=None,receiptState=None,bindingCurrent=None,
        historyMayExist=False,historyVerified=False,hasPublishedVersion=False)


def require_failed(data,api,baseline,subject):
    row=data['row'];identifier=row['id'];assert row['state']==api['state']=='Failed' and row['result_code']==api['resultCode']==CODE
    assert row['actor_subject']==subject and row['container_id']==baseline['containerId'] and row['instance_id']==baseline['instanceId']
    assert row['relative_path']=='work/acl-file.bin' and api['id']==api['planningJobId']==identifier
    assert row['completed_at_utc'] and not row['publish_barrier'] and row['lease_token'] is row['lease_expires_at_utc'] is None
    for key in ('execution_job_id','root_started_at_utc','plan_json','plan_hash','receipt_json','receipt_hash','acl_confirmed','history_digest','published_version'):
        assert row[key] is None,key
    assert row['history_may_exist'] is row['history_verified'] is False
    assert data['executionJob'] is None and data['pending']==data['confirmed']==0 and data['accepted']==1
    for key in ('executionJobId','confirmation','receipt','publishedVersion','kind','targetMode','noChange'):
        assert api[key] is None,key
    assert api['command']['expectedVersion']==baseline['sourceVersion']
    assert api['command']['location']=={'containerId':baseline['containerId'],'instanceId':baseline['instanceId'],'path':'work/acl-file.bin'}
    assert api['command']['aclType']=='access' and api['command']['entries']==[
        {'tag':'user_obj','id':None,'permissions':6},{'tag':'group_obj','id':None,'permissions':4},{'tag':'other','id':None,'permissions':0}]
    validate_job_summary(data['planJob'],expected_summary(identifier),identifier,'Failed')


def no_operation_history(identifier,storage):
    # The same deterministic .NET Guid byte order as the production execution ID.
    names={uuid.UUID(identifier).hex+'.json',execution_id(identifier)+'.json'}
    for name in names:assert not os.path.lexists(Path('/var/lib/whaledeck-container-acl')/name)
    with storage.target(create_namespace=False,read_only=True) as (configuration,fd):
        storage.attest(configuration,fd,read_only=True)
        try:child=storage.f.open_at(fd,'container-acl-v1',os.O_RDONLY|os.O_DIRECTORY)
        except FileNotFoundError:return
        try:
            storage.attest(configuration,child,read_only=True)
            assert not names.intersection(os.listdir(child))
        finally:os.close(child)


def touch_exact(fixture,baseline,reader,module,content):
    actual=json.loads(owned.common.command(['docker','inspect',fixture['identifier']]))[0]
    owned.verify_container(actual,baseline['containerId'],baseline['containerName'],fixture['folder'])
    assert str(actual['State']['Pid'])+':'+actual['State']['StartedAt']==baseline['instanceId']
    before=inventory(fixture['folder'],reader,module,content);assert before==baseline['original']
    parent=os.open(fixture['folder'],os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW)
    try:fd=os.open('acl-file.bin',os.O_RDONLY|os.O_NOFOLLOW,dir_fd=parent)
    finally:os.close(parent)
    try:
        info=os.fstat(fd);assert list(reader.fingerprint(info))==before['acl-file.bin']['identity']
        os.utime(fd,ns=(info.st_atime_ns,info.st_mtime_ns+17))
    finally:os.close(fd)
    after=inventory(fixture['folder'],reader,module,content);require_touch(before,after)
    assert version(baseline,'work/acl-file.bin',after['acl-file.bin']['identity'])!=baseline['sourceVersion']
    return after


def access_denials(base,baseline,result):
    endpoint=base+'/api/v1/containers/'+baseline['containerId']+'/files/acls';paths=[endpoint+'/'+result['id'],base+'/api/v1/jobs/'+result['planningJobId']]
    import requests
    with requests.Session() as anonymous:
        anonymous.trust_env=False
        for path in paths:request(anonymous,'GET',path,401)
    with temporary_user(False) as (username,password):
        session,_=login(base,username,password,False)
        try:
            api=Api(base,session)
            for path in paths:request(session,'GET',path,403)
            body={**result['command']['location'],'aclType':'access','entries':result['command']['entries'],'expectedVersion':baseline['sourceVersion']}
            request(session,'POST',endpoint,403,headers={**api.headers,'Idempotency-Key':str(uuid.uuid4())},json=body)
            logout(base,session)
        finally:session.close()
    return {'anonymousReadsDenied':2,'ordinaryReadsDenied':2,'ordinaryRegistrationDenied':1,'normalOrdinaryLogout':True}


def main():
    global PHASE
    assert os.geteuid()==0 and len(sys.argv)==2;os.umask(0o077);revision=sys.argv[1];assert re.fullmatch('[a-f0-9]{40}',revision)
    repo=Path(__file__).resolve().parents[2];assert not subprocess.check_output(['git','-C',str(repo),'status','--porcelain']).strip()
    source=subprocess.check_output(['git','-C',str(repo),'rev-parse','HEAD'],text=True).strip();hashes={};installed={}
    for name in SOURCES:
        raw=subprocess.check_output(['git','-C',str(repo),'show',source+':deploy/validation/'+name]);assert raw==(repo/'deploy/validation'/name).read_bytes();hashes[name]=hashlib.sha256(raw).hexdigest()
    for name in (*NAMES,'whaledeck-container-acl-transport.py','whaledeck-privileged'):
        path=Path('/usr/local/libexec')/name;trusted(path);raw=subprocess.check_output(['git','-C',str(repo),'show',revision+':deploy/host/'+name]);assert path.read_bytes()==raw;installed[name]=hashlib.sha256(raw).hexdigest()
    no_active_work();hosts=host_snapshot(revision);evidence=repo/'deploy/artifacts'/('container-acl-stale-'+uuid.uuid4().hex);evidence.mkdir(mode=0o700)
    print(json.dumps({'aclStale':'started','evidence':str(evidence)}),flush=True)
    reader=load('/usr/local/libexec/whaledeck-container-files.py','acl_stale_reader');module=load('/usr/local/libexec/whaledeck-container-acl.py','acl_stale_module');files=reader.trusted_module('whaledeck-files.py','acl_stale_files');storage=load('/usr/local/libexec/whaledeck-file-backups.py','acl_stale_storage').FileBackups(files)
    with owned.fixture() as fixture:
        folder=fixture['folder'];(folder/'acl-directory').mkdir(mode=0o750);content=hashlib.shake_256(('acl-stale:'+fixture['identifier']).encode()).digest(2*1024**2+7)
        for relative,kind in PATHS.items():
            path=folder/relative
            if kind=='File':path.write_bytes(content);os.setxattr(path,'user.whaledeck-acl',b'preserve ordinary attribute')
            os.chown(path,2100,3100);path.chmod(0o640 if kind=='File' else 0o750)
        for relative in PATHS:os.utime(folder/relative,ns=(1700000000123456789,1700000000123456789))
        actual=json.loads(owned.common.command(['docker','inspect',fixture['identifier']]))[0];original=inventory(folder,reader,module,content)
        baseline=dict(revision=revision,validationSourceRevision=source,validationSourceSha256=hashes,installedSourceSha256=installed,proofVersion=1,
            containerId=fixture['identifier'],containerName=actual['Name'].lstrip('/'),instanceId=str(actual['State']['Pid'])+':'+actual['State']['StartedAt'],
            source=str(folder),original=original,sha256=hashlib.sha256(content).hexdigest().upper(),bytesPerFile=len(content),coreBefore=owned.common.core_snapshot(),hostsBefore=hosts,evidence=str(evidence))
        baseline['sourceVersion']=version(baseline,'work/acl-file.bin',original['acl-file.bin']['identity'])
        (evidence/'baseline.json').write_text(json.dumps(baseline));current=original;browser=None;identifier=None;proved=False
        try:
            with temporary_user(True) as (username,password):
                with (evidence/'browser.stdout').open('wb') as out,(evidence/'browser.stderr').open('wb') as err:
                    browser=subprocess.Popen(['node',str(Path(__file__).with_suffix('.mjs')),revision],stdin=subprocess.PIPE,stdout=out,stderr=err,env={**os.environ,'VALIDATION_USERNAME':username,'VALIDATION_PASSWORD':password})
                    browser.stdin.write(json.dumps(baseline).encode());browser.stdin.close();browser.stdin=None
                    PHASE='visible-page-captured-original-version';opened=wait_signal(evidence,'opened.json',browser);assert opened=={'subject':opened['subject'],'originalPageOpened':True};no_active_work()
                    PHASE='one-exact-owned-mtime-change';current=touch_exact(fixture,baseline,reader,module,content);(evidence/'physical-mutated.json').write_text(json.dumps(current));write_signal(evidence,'changed.json',{'originalVersionStale':True})
                    PHASE='real-preflight-rejection';result=wait_signal(evidence,'result.json',browser);identifier=result['intent']['id'];data=wait_settled(identifier);require_failed(data,result['intent'],baseline,opened['subject']);no_operation_history(identifier,storage)
                    assert inventory(folder,reader,module,content)==current
                    validate_job_summary(result['job'],expected_summary(identifier),identifier,'Failed')
                    (evidence/'failed-sql.json').write_text(json.dumps(data));write_signal(evidence,'verified-row.json',{'id':identifier,'noExecutionOrHistory':True});proved=True
                    assert browser.wait(timeout=90)==0,'ACTUAL_ACL_STALE_BROWSER_FAILED'
                    ui=json.loads((evidence/'browser.json').read_bytes());assert ui['subject']==opened['subject'] and ui['actualUi'] and ui['normalDualLogout'] and ui['registrations']==ui['registrationRetries']==1 and ui['confirmations']==ui['contentWrites']==0
                    PHASE='actual-permission-denials';denials=access_denials('http://127.0.0.1:8080',baseline,result['intent']);assert snapshot(identifier)==data
                    (evidence/'denials.json').write_text(json.dumps(denials));assert inventory(folder,reader,module,content)==current;no_operation_history(identifier,storage)
                    (evidence/'physical-final.json').write_text(json.dumps(current))
        finally:
            if browser is not None and browser.poll() is None:
                browser.terminate()
                try:browser.wait(timeout=15)
                except subprocess.TimeoutExpired:browser.kill();browser.wait(timeout=10)
            # Only the browser child is stopped. No product helper or service is signaled.
            if proved and identifier is not None and snapshot(identifier)['row']['state']=='Failed' and inventory(folder,reader,module,content)==current:
                no_operation_history(identifier,storage);remove_proved(folder,reader,module,content,current)
    no_active_work();assert host_snapshot(revision)==hosts
    result=dict(revision=revision,id=identifier,state='Failed',code=CODE,executionJobs=0,rootRecords=0,hddRecords=0,actualUi=True,normalDualLogout=True,physicalBytesAndAclsPreserved=True,onlyExactOwnedMtimeChanged=True)
    (evidence/'verified.json').write_text(json.dumps(result));print(json.dumps({'aclStale':'passed','evidence':str(evidence),**result}),flush=True)


if __name__=='__main__':
    try:main()
    except BaseException as error:
        import traceback
        print(json.dumps({'aclStale':'failed','phase':PHASE,'failureType':type(error).__name__,'locations':[{'file':Path(f.filename).name,'line':f.lineno} for f in traceback.extract_tb(error.__traceback__)]}),flush=True);raise SystemExit(1) from None
