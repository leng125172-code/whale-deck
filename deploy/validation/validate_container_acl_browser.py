#!/usr/bin/env python3
"""Real Authentik/Vue ACL workflows; per-case physical/SQL/Root/HDD handshakes.

Only a fresh exactly owned container/HDD fixture. Never inject Cookie/SQL/cache,
repeat an unproved write, remove Root/HDD history or automatically roll back dev.
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
from validate_container_recursive_ownership_browser import no_active_work as previous_idle, read_metadata, version
from validate_container_acl import inventory, unchanged_except_target, PATHS, NAMES
from validate_container_acl_agent import STATE
from validate_databases import Api
from validate_metric_continuity import query

PHASE='startup'
CASES=('access','noop','mask','root','base','default','default-noop','clear-default','cancel')
SOURCES=('validate_container_acl_browser.py','validate_container_acl_browser.mjs','verify_container_acl_browser.py',
    'installed_ui_permission_session.mjs','installed_ui_logout.mjs','validate_container_acl.py','validate_container_acl_agent.py',
    'validate_container_file_publishing.py','validate_container_file_bff_extended.py','validate_container_file_staging.py',
    'validate_container_upload_bff.py','validate_container_upload_recovery_agent.py','validate_container_recursive_ownership_browser.py',
    'validate_container_recursive_ownership_agent.py','validate_container_recursive_permission_agent.py','validate_container_directory_restart.py',
    'validate_databases.py','validate_oidc.py','validate_metric_continuity.py','run_with_ephemeral_admin.py','verify_container_recursive_permissions.py')


def no_active_work():
    result=previous_idle()
    extra=query("SELECT json_build_object('acl',(SELECT count(*) FROM container_acl_intents WHERE completed_at_utc IS NULL OR publish_barrier OR lease_token IS NOT NULL));")
    assert extra['acl']==0,'OTHER_ACL_WORK_ACTIVE';return {**result,**extra}


def snapshot(identifier):
    assert str(uuid.UUID(identifier))==identifier
    return query('''SELECT json_build_object('row',to_jsonb(c),'planJob',to_jsonb(p),'executionJob',to_jsonb(e),
      'pending',(SELECT count(*) FROM outbox_messages o WHERE o.payload_json->>'jobId' IN(c.id::text,c.execution_job_id::text) AND o.processed_at_utc IS NULL),
      'accepted',(SELECT count(*) FROM audit_events a WHERE a.job_id=c.id AND a.action='containers.files.acl.plan-accepted'),
      'confirmed',(SELECT count(*) FROM audit_events a WHERE a.job_id=c.execution_job_id AND a.action='containers.files.acl.confirmed'),
      'events',(SELECT count(*) FROM operation_job_events j WHERE j.job_id IN(c.id,c.execution_job_id)))
      FROM container_acl_intents c JOIN operation_jobs p ON p.id=c.id LEFT JOIN operation_jobs e ON e.id=c.execution_job_id WHERE c.id=:'id'::uuid;''',id=identifier)


def managed_plan(value):
    c=value['Command'];loc=c['Location'];s=value['SourceIdentity'];p=value['ParentIdentity']
    def entries(values):return [{'tag':e['Tag'],'id':e['Id'],'permissions':e['Permissions']} for e in values]
    return {'schemaVersion':value['SchemaVersion'],'command':{'containerId':loc['ContainerId'],'instanceId':loc['InstanceId'],'path':loc['Path'],
        'aclType':c['AclType'],'entries':entries(c['Entries']),'expectedVersion':c['ExpectedVersion']},'targetProof':value['TargetProof'],
        'parentIdentity':[p['Device'],p['Inode']],'parentVersion':value['ParentVersion'],'uid':value['OwnerUid'],'gid':value['OwnerGid'],
        'fingerprint':value['Fingerprint'],'kind':value['Kind'],'sourceIdentity':[s[k] for k in ('Device','Inode','Mode','OwnerUid','OwnerGid','LinkCount','SizeBytes','ModifiedAtUnixNs')]+[value['SourceChangedAtUnixNs']],
        'attributesDigest':value['AttributesDigest'],'noChange':value['NoChange'],'accessAcl':entries(value['AccessAcl']),'defaultAcl':entries(value['DefaultAcl']),'targetMode':value['TargetMode']}


def profile(label):
    if label=='clear-default':return []
    if label=='base':return [{'tag':'user_obj','id':None,'permissions':6},{'tag':'group_obj','id':None,'permissions':4},{'tag':'other','id':None,'permissions':0}]
    mask=6 if label in ('mask','root') else 7 if label=='cancel' else 4
    return [{'tag':'user_obj','id':None,'permissions':6},{'tag':'user','id':0 if label=='root' else 2201,'permissions':0 if label=='root' else 6},
            {'tag':'group_obj','id':None,'permissions':0},{'tag':'mask','id':None,'permissions':mask},{'tag':'other','id':None,'permissions':0}]


def verify_case(result,baseline,subject,evidence,reader,module,storage,before,after):
    label=result['name'];assert label in CASES;relative='acl-directory' if label in ('default','default-noop','clear-default') else 'acl-file.bin'
    kind='default' if relative=='acl-directory' else 'access';data=snapshot(result['id']);row=data['row'];plan=managed_plan(json.loads(row['plan_json']))
    assert type(plan['schemaVersion']) is int and plan['schemaVersion']==1 and type(plan['targetMode']) is int and 0<=plan['targetMode']<=511 and type(plan['noChange']) is bool
    module.validate_entries(plan['command']['entries'],empty=kind=='default');module.validate_entries(plan['accessAcl']);module.validate_entries(plan['defaultAcl'],empty=True)
    assert row['actor_subject']==subject and row['container_id']==baseline['containerId'] and row['instance_id']==baseline['instanceId']
    assert row['relative_path']==plan['command']['path']==result['command']['location']['path']=='work/'+relative
    assert row['state']==result['state'] and row['completed_at_utc'] and not row['publish_barrier'] and row['lease_token'] is None and row['lease_expires_at_utc'] is None
    assert data['pending']==0 and data['accepted']==1 and data['planJob']['state']=='Succeeded'
    assert plan['command']['aclType']==kind and plan['command']['entries']==profile(label)
    assert plan['sourceIdentity']==before[relative]['identity'] and plan['accessAcl']==before[relative]['access'] and plan['defaultAcl']==before[relative]['default']
    assert plan['command']['expectedVersion']==version(baseline,'work/'+relative,before[relative]['identity'])
    assert plan['fingerprint']==hashlib.sha256(b'whaledeck-container-acl-plan-v1\n'+module.encode({k:v for k,v in plan.items() if k!='fingerprint'})).hexdigest().upper()
    assert plan['uid']==plan['gid']==result['executorUid']==result['executorGid']==1000
    assert [result['originalUid'],result['originalGid']]==before[relative]['identity'][3:5]
    assert plan['noChange']==result['noChange']==(label in ('noop','default-noop')) and plan['targetMode']==result['targetMode']
    (evidence/(label+'.sql.json')).write_text(json.dumps(data))
    if label=='cancel':
        assert row['state']=='Canceled' and row['execution_job_id'] is None and row['root_started_at_utc'] is None and data['executionJob'] is None and data['confirmed']==0
        assert row['receipt_json'] is None and row['receipt_hash'] is None and row['acl_confirmed'] is None and not row['history_may_exist'] and not row['history_verified'] and row['history_digest'] is None
        assert result['receipt'] is None and after==before;return {'result':result,'before':before,'after':after,'hddRecords':0}
    assert row['state']==data['executionJob']['state']=='Succeeded' and data['confirmed']==1 and data['events']>=4
    assert row['execution_job_id']==result['executionJobId'] and row['root_started_at_utc'] and row['acl_confirmed'] is True and row['history_verified'] and row['history_may_exist']
    assert re.fullmatch('[A-F0-9]{64}',row['receipt_hash'])
    operation=uuid.UUID(row['execution_job_id']).hex;path=STATE/(operation+'.json');info=trusted(path);assert stat.S_IMODE(info.st_mode)==0o600 and info.st_size<=16384
    raw=path.read_bytes();record=json.loads(raw);assert record['operationId']==operation and record['owner']==row['owner_hash'] and record['plan']==plan
    assert type(record['schemaVersion']) is int and record['schemaVersion']==1
    assert record['state']=='Completed' and record['aclConfirmed'] is True and record['historyVerified'] and record['historyMayExist']
    unchanged_except_target(before,after,relative,plan['targetMode']);assert after[relative][kind]==plan['command']['entries']
    assert after[relative]['default' if kind=='access' else 'access']==before[relative]['default' if kind=='access' else 'access']
    assert record['actualIdentity']==after[relative]['identity'] and record['version']==row['published_version']==result['publishedVersion']==version(baseline,'work/'+relative,after[relative]['identity'])
    receipt=result['receipt'];sql_receipt=json.loads(row['receipt_json']);assert receipt['id']==sql_receipt['Id']==row['execution_job_id'] and receipt['state']==sql_receipt['State']=='Completed'
    bindings={'containerId':baseline['containerId'],'instanceId':baseline['instanceId'],'fingerprint':plan['fingerprint'],'targetProof':plan['targetProof'],'kind':plan['kind'],'noChange':plan['noChange']}
    for key,expected in bindings.items():assert receipt[key]==sql_receipt[key[0].upper()+key[1:]]==expected
    assert receipt['historyVerified'] is sql_receipt['HistoryVerified'] is True and receipt['historyMayExist'] is sql_receipt['HistoryMayExist'] is True
    assert receipt['aclConfirmed'] is sql_receipt['AclConfirmed'] is True and receipt['bindingCurrent'] and receipt['aclType']==kind and receipt['targetMode']==plan['targetMode']
    assert record['historyDigest']==receipt['historyDigest']==sql_receipt['HistoryDigest']==row['history_digest']
    with storage.target(create_namespace=False,read_only=True) as (configuration,base_fd):
        fd=storage.f.open_at(base_fd,'container-acl-v1',os.O_RDONLY|os.O_DIRECTORY)
        try:
            storage.attest(configuration,fd,read_only=True);hdd_raw=read_metadata(storage.f,storage,configuration,fd,operation+'.json',16384)
        finally:os.close(fd)
    hdd=json.loads(hdd_raw);assert hdd['resourceType']=='ContainerAclMetadata' and hdd['storageId']=='database-platform.backup.hdd'
    assert hdd['operationId']==operation and hdd['owner']==row['owner_hash'] and hdd['plan']==plan
    assert hdd['historyDigest']==record['historyDigest']==hashlib.sha256(b'whaledeck-container-acl-history-v1\n'+module.encode({k:v for k,v in hdd.items() if k!='historyDigest'})).hexdigest().upper()
    if label in ('noop','default-noop'):assert before==after
    (evidence/(label+'.root.json')).write_bytes(raw);(evidence/(label+'.hdd.json')).write_bytes(hdd_raw)
    return {'result':result,'before':before,'after':after,'hddRecords':1,'rootSha256':hashlib.sha256(raw).hexdigest(),'hddSha256':hashlib.sha256(hdd_raw).hexdigest()}


def unprivileged(base,baseline,original):
    endpoint=base+'/api/v1/containers/'+baseline['containerId']+'/files/acls';recovery=base+'/api/v1/files/container-acls/recovery'
    paths=[endpoint+'/'+original['id'],endpoint+'/by-key/'+str(uuid.uuid4()),recovery,recovery+'/'+original['id']]
    anonymous=__import__('requests').Session();anonymous.trust_env=False
    try:
        for path in paths:request(anonymous,'GET',path,401)
    finally:anonymous.close()
    with temporary_user(False) as (username,password):
        session,_=login(base,username,password,False)
        try:
            api=Api(base,session)
            for path in paths:request(session,'GET',path,403)
            body={'instanceId':baseline['instanceId'],'path':'work/acl-file.bin','aclType':'access','entries':profile('access'),'expectedVersion':baseline['sourceVersion']}
            request(session,'POST',endpoint,403,headers={**api.headers,'Idempotency-Key':str(uuid.uuid4())},json=body)
            for action in ('confirm','cancel'):
                request(session,'POST',endpoint+'/'+original['id']+'/'+action,403,headers={**api.headers,'Idempotency-Key':original['id']},json={'planHash':'not-authority','confirmed':True,'version':original['version']} if action=='confirm' else None)
            request(session,'POST',recovery+'/'+original['id']+'/acknowledge',403,headers={**api.headers,'Idempotency-Key':original['id']},json={'version':original['version'],'confirmed':True});logout(base,session)
        finally:session.close()


def wait_case(process,evidence,name):
    deadline=time.monotonic()+110;path=evidence/(name+'-result.json')
    while not path.exists():
        assert process.poll() is None,'ACTUAL_ACL_BROWSER_EXITED';assert time.monotonic()<deadline,'ACL_BROWSER_CASE_DEADLINE';time.sleep(0.1)
    trusted(path);assert path.stat().st_size<=32768;return json.loads(path.read_bytes())


def remove_proved(folder,reader,module,content,current):
    assert folder.parent==owned.common.APPS and re.fullmatch('wdv-container-publish-[a-f0-9]{32}',folder.name) and not folder.is_symlink()
    # All identities, full content, ACLs and ordinary attributes must agree BEFORE any deletion.
    assert inventory(folder,reader,module,content)==current
    for relative in ('acl-directory/existing.bin','acl-file.bin'):
        assert list(reader.fingerprint((folder/relative).lstat()))==current[relative]['identity'];(folder/relative).unlink()
    directory=folder/'acl-directory';info=directory.lstat();prior=current['acl-directory']
    assert not directory.is_symlink() and stat.S_ISDIR(info.st_mode) and not list(directory.iterdir())
    assert list(reader.fingerprint(info))[:6]==prior['identity'][:6]
    fd=os.open(directory,os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW)
    try:assert module.read_acl(fd,'access',info.st_mode)==prior['access'] and module.read_acl(fd,'default',info.st_mode)==prior['default']
    finally:os.close(fd)
    directory.rmdir()


def main():
    global PHASE
    assert os.geteuid()==0 and len(sys.argv)==2;os.umask(0o077);revision=sys.argv[1];assert re.fullmatch('[a-f0-9]{40}',revision)
    repo=Path(__file__).resolve().parents[2];base='http://127.0.0.1:8080';assert not subprocess.check_output(['git','-C',str(repo),'status','--porcelain']).strip(),'FREEZE_VALIDATION_SOURCE_FIRST'
    source=subprocess.check_output(['git','-C',str(repo),'rev-parse','HEAD'],text=True).strip();hashes={}
    for name in SOURCES:
        raw=subprocess.check_output(['git','-C',str(repo),'show',source+':deploy/validation/'+name]);assert raw==(repo/'deploy/validation'/name).read_bytes();hashes[name]=hashlib.sha256(raw).hexdigest()
    installed_hashes={}
    for name in (*NAMES,'whaledeck-container-acl-transport.py','whaledeck-privileged'):
        path=Path('/usr/local/libexec')/name;trusted(path);raw=subprocess.check_output(['git','-C',str(repo),'show',revision+':deploy/host/'+name]);assert raw==path.read_bytes();installed_hashes[name]=hashlib.sha256(raw).hexdigest()
    public=__import__('requests').Session();public.trust_env=False
    try:
        response=public.get(base+'/api/v1/system/status',timeout=10);assert response.status_code==200 and response.json()['build']['sourceRevision']==revision,'TARGET_PRODUCT_NOT_DEPLOYED'
    finally:public.close()
    no_active_work();core=owned.common.core_snapshot();evidence=repo/'deploy/artifacts'/('container-acl-browser-'+uuid.uuid4().hex);evidence.mkdir(mode=0o700)
    print(json.dumps({'actualAclBrowser':'started','evidence':str(evidence)}),flush=True)
    reader=load('/usr/local/libexec/whaledeck-container-files.py','acl_browser_reader');module=load('/usr/local/libexec/whaledeck-container-acl.py','acl_browser_module');files=reader.trusted_module('whaledeck-files.py','acl_browser_files');storage=load('/usr/local/libexec/whaledeck-file-backups.py','acl_browser_storage').FileBackups(files)
    with owned.fixture() as fixture:
        folder=fixture['folder'];(folder/'acl-directory').mkdir(mode=0o750);content=hashlib.shake_256(('acl-browser:'+fixture['identifier']).encode()).digest(2*1024**2+7)
        for relative,kind in PATHS.items():
            p=folder/relative
            if kind=='File':p.write_bytes(content);os.setxattr(p,'user.whaledeck-acl',b'preserve ordinary attribute')
            os.chown(p,2100,3100);p.chmod(0o640 if kind=='File' else 0o750)
        for relative in PATHS:os.utime(folder/relative,ns=(1700000000123456789,1700000000123456789))
        inspected=json.loads(owned.common.command(['docker','inspect',fixture['identifier']]))[0];original=inventory(folder,reader,module,content)
        baseline={'revision':revision,'validationSourceRevision':source,'validationSourceSha256':hashes,'installedSourceSha256':installed_hashes,'containerId':fixture['identifier'],'containerName':inspected['Name'].lstrip('/'),'instanceId':str(inspected['State']['Pid'])+':'+inspected['State']['StartedAt'],'source':str(folder),'original':original,'sha256':hashlib.sha256(content).hexdigest().upper(),'bytesPerFile':len(content),'coreBefore':core,'evidence':str(evidence),'fixtureOriginal':{'identity':list(fixture['proofs']['original.bin'][0]),'sha256':fixture['proofs']['original.bin'][1]}}
        baseline['sourceVersion']=version(baseline,'work/acl-file.bin',original['acl-file.bin']['identity'])
        with storage.target(create_namespace=False,read_only=True) as (configuration,fd):storage.attest(configuration,fd,read_only=True);baseline['sourceDevice']=folder.stat().st_dev;baseline['hddDevice']=os.fstat(fd).st_dev
        (evidence/'baseline.json').write_text(json.dumps(baseline));results=[];current=original
        with temporary_user(True) as (username,password):
            env={**os.environ,'WHALEDECK_URL':base,'VALIDATION_USERNAME':username,'VALIDATION_PASSWORD':password}
            with (evidence/'browser.stdout').open('w') as out,(evidence/'browser.stderr').open('w') as err:
                process=subprocess.Popen(['node',str(repo/'deploy/validation/validate_container_acl_browser.mjs'),revision],stdin=subprocess.PIPE,stdout=out,stderr=err,env=env,text=True)
                try:
                    process.stdin.write(json.dumps(baseline));process.stdin.close()
                    for name in CASES:
                        PHASE='physical-proof-'+name;signal=wait_case(process,evidence,name);result=signal['result'];assert result['name']==name and result['confirmation'] is None
                        after=inventory(folder,reader,module,content);proof=verify_case(result,baseline,signal['subject'],evidence,reader,module,storage,current,after);results.append(proof);current=after
                        (evidence/'results.json').write_text(json.dumps(results));(evidence/'physical-final.json').write_text(json.dumps(current))
                        pending=evidence/(name+'-verified.pending');pending.write_text(json.dumps({'id':result['id'],'physicalAndHistoryVerified':True}));pending.replace(evidence/(name+'-verified.json'))
                    assert process.wait(timeout=75)==0,'ACTUAL_ACL_BROWSER_FAILED'
                finally:
                    if process.poll() is None:
                        process.terminate()
                        try:process.wait(timeout=15)
                        except subprocess.TimeoutExpired:process.kill();process.wait(timeout=10)
            browser=json.loads((evidence/'browser.json').read_bytes());assert browser['normalDualLogout'] and browser['registrations']==9 and browser['confirmations']==8 and browser['contentUploads']==0
            assert browser['completed']==[r['result'] for r in results];PHASE='unprivileged';unprivileged(base,baseline,browser['completed'][0])
        PHASE='proved-cleanup';remove_proved(folder,reader,module,content,current)
    no_active_work();assert owned.common.core_snapshot()==core and not fixture['folder'].exists()
    assert not subprocess.check_output(['systemctl','list-units','--all','--no-legend','whaledeck-container-acl-*'],text=True).strip()
    verified={'revision':revision,'actualBrowserAndIdentity':True,'cases':9,'completed':8,'canceledBeforeExecution':1,'hddRecords':8,'perCasePhysicalBytesAndMetadata':True,'ordinaryDenied':8,'anonymousDenied':4,'normalDualLogout':True,'exactSourceAndContainerAbsent':True,'coreUnchanged':11,'permanentHistoryRetained':True}
    (evidence/'verified.json').write_text(json.dumps(verified));print(json.dumps({'actualAclBrowser':'passed','evidence':str(evidence),'cases':9}),flush=True)


if __name__=='__main__':
    try:main()
    except BaseException as error:
        print(json.dumps({'actualAclBrowser':'failed','phase':PHASE,'failureType':type(error).__name__}),flush=True);raise SystemExit(1) from None
