#!/usr/bin/env python3
"""Visible Authentik/Vue -> persistent recursive ACL workflow -> actual Root/HDD.

Only an owned non-root/no-network container and synthetic tree. Every write is
followed by full physical/SQL/Root/HDD proof before the next. No SQL/cache edits,
Cookie injection, broad cleanup, history deletion or automatic rollback.
"""
from contextlib import ExitStack
import base64
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
from validate_container_upload_bff import temporary_user
from validate_container_upload_recovery_agent import trusted
from validate_container_acl_browser import no_active_work as previous_idle
from validate_container_recursive_acl_agent import STATE,host_identity
from validate_container_recursive_acl import PATHS,NAMES,inventory,preserved,remove_proved
from verify_container_recursive_acl import digest,unsigned,acl_mode,file_version
from verify_container_recursive_acl_agent import expected_targets as native_targets
from validate_metric_continuity import query
from acl_source_apply_missing_proof import source_closure
from validate_container_recursive_ownership_browser import read_metadata,version

PHASE='startup'
CASES=('default','access','noop','mask','root','default-noop','clear-default','cancel')
CHANGES=(2,4,0,2,4,0,2,4)
SOURCES=('validate_container_recursive_acl_browser.py','validate_container_recursive_acl_browser.mjs','verify_container_recursive_acl_browser.py','test_recursive_acl_browser_probe_scope.py')


def expected_targets(index):
    files,directories=native_targets(index)
    if index==4:next(e for e in files if e['tag']=='mask')['permissions']=6
    return files,directories


def selected_cases(arguments):
    values=tuple(arguments)
    assert values in ((),('--remaining-defaults',),('--cancel-only',)),'UNKNOWN_REAL_GATE_SELECTION'
    return CASES[-1:] if values==('--cancel-only',) else CASES[5:] if values else CASES


def no_active_work():
    result=previous_idle()
    extra=query("SELECT json_build_object('recursiveAcl',(SELECT count(*) FROM container_recursive_acl_intents WHERE completed_at_utc IS NULL OR publish_barrier OR lease_token IS NOT NULL));")
    assert extra['recursiveAcl']==0,'OTHER_RECURSIVE_ACL_WORK_ACTIVE'
    return {**result,**extra}


def snapshot(identifier):
    assert str(uuid.UUID(identifier))==identifier
    return query('''SELECT json_build_object('row',to_jsonb(c),'planJob',to_jsonb(p),'executionJob',to_jsonb(e),
    'pending',(SELECT count(*) FROM outbox_messages o WHERE o.payload_json->>'jobId' IN(c.id::text,c.execution_job_id::text) AND o.processed_at_utc IS NULL),
    'accepted',(SELECT count(*) FROM audit_events a WHERE a.job_id=c.id AND a.action='containers.files.recursive-acl.plan-accepted'),
    'confirmed',(SELECT count(*) FROM audit_events a WHERE a.job_id=c.execution_job_id AND a.action='containers.files.recursive-acl.confirmed'),
    'events',(SELECT count(*) FROM operation_job_events j WHERE j.job_id IN(c.id,c.execution_job_id)))
    FROM container_recursive_acl_intents c JOIN operation_jobs p ON p.id=c.id LEFT JOIN operation_jobs e ON e.id=c.execution_job_id WHERE c.id=:'id'::uuid;''',id=identifier)


def managed_entries(values,empty=False):
    assert type(values) is list and len(values)<=64 and (values or empty)
    result=[];keys=[];tags=[];order={'user_obj':1,'user':2,'group_obj':4,'group':8,'mask':16,'other':32}
    for e in values:
        assert type(e) is dict and set(e)=={'Tag','Id','Permissions'}
        tag,identifier,bits=e['Tag'],e['Id'],e['Permissions']
        assert type(tag) is str and tag in order and type(bits) is int and 0<=bits<=7
        if tag in ('user','group'):assert type(identifier) is int and 0<=identifier<4294967295
        else:assert identifier is None
        keys.append((order[tag],-1 if identifier is None else identifier));tags.append(tag)
        result.append(dict(tag=tag,id=identifier,permissions=bits))
    assert keys==sorted(set(keys))
    if values:assert all(tags.count(t)==1 for t in ('user_obj','group_obj','other')) and (not any(t in tags for t in ('user','group')) or 'mask' in tags)
    return result


def managed_plan(value):
    c=value['Command'];loc=c['Location'];s=value['RootIdentity'];p=value['ParentIdentity'];kind=c['AclType']
    assert kind in ('access','default')
    if kind=='default':assert c['FileEntries'] is None;files=None
    else:files=managed_entries(c['FileEntries'])
    directories=managed_entries(c['DirectoryEntries'],kind=='default')
    identity=[s[k] for k in ('Device','Inode','Mode','OwnerUid','OwnerGid','LinkCount','SizeBytes','ModifiedAtUnixNs')]+[value['RootChangedAtUnixNs']]
    assert all(type(v) is int for v in identity)
    assert all(0<=identity[n]<=18446744073709551615 for n in (0,1)) and identity[1]>0
    assert 0<=identity[2]<=65535 and stat.S_ISDIR(identity[2]) and not identity[2]&0o7000
    assert all(0<=identity[n]<4294967295 for n in (3,4)) and identity[5]>=1 and 0<=identity[6]<=9223372036854775807
    assert all(-9223372036854775808<=identity[n]<=9223372036854775807 for n in (7,8))
    counts={k[0].lower()+k[1:]:value[k] for k in ('EntryCount','FileCount','DirectoryCount','ChangeCount')}
    assert all(type(v) is int and 0<=v<=10000 for v in counts.values())
    assert 0<counts['entryCount']==counts['fileCount']+counts['directoryCount'] and counts['changeCount']<=counts['entryCount']
    if kind=='default':assert counts['changeCount']<=counts['directoryCount']
    assert type(value['SchemaVersion']) is int and value['SchemaVersion']==1
    for k in ('OwnerUid','OwnerGid'):assert type(value[k]) is int and 0<=value[k]<4294967295
    parent=[p['Device'],p['Inode']];assert all(type(v) is int and 0<=v<=18446744073709551615 for v in parent) and parent[1]>0
    for v in (c['ExpectedVersion'],value['TargetProof'],value['ParentVersion'],value['Fingerprint'],value['ManifestDigest']):assert type(v) is str and re.fullmatch('[A-F0-9]{64}',v)
    return {'schemaVersion':1,'command':{'containerId':loc['ContainerId'],'instanceId':loc['InstanceId'],'path':loc['Path'],'expectedVersion':c['ExpectedVersion'],'aclType':kind,'fileEntries':files,'directoryEntries':directories},
        'targetProof':value['TargetProof'],'parentIdentity':parent,'parentVersion':value['ParentVersion'],'uid':value['OwnerUid'],'gid':value['OwnerGid'],'fingerprint':value['Fingerprint'],'rootIdentity':identity,'manifestDigest':value['ManifestDigest'],**counts}


def archive(evidence,name,raw,read_only):
    p=evidence/name
    if read_only:assert p.read_bytes()==raw,'ARCHIVED_PROOF_CHANGED'
    else:
        with p.open('xb') as f:f.write(raw)
        p.chmod(0o600)


def verify_case(result,baseline,subject,evidence,reader,storage,before,after,acl,read_only=False):
    index=CASES.index(result['name']);data=snapshot(result['id']);row=data['row'];plan=managed_plan(json.loads(row['plan_json']));command=plan['command'];kind=command['aclType']
    archive(evidence,result['name']+'.sql.json',json.dumps(data,sort_keys=True).encode(),read_only)
    assert row['actor_subject']==subject and row['container_id']==baseline['containerId'] and row['instance_id']==baseline['instanceId']
    assert row['relative_path']==result['command']['location']['path']=='work/recursive-tree' and row['state']==result['state'] and result['id']==result['planningJobId']==row['id']
    assert row['completed_at_utc'] and not row['publish_barrier'] and row['lease_token'] is None and row['lease_expires_at_utc'] is None and data['pending']==0
    assert data['accepted']==1 and data['planJob']['state']=='Succeeded' and data['planJob']['job_type']=='containers.files.recursive-acl-plan'
    assert plan['rootIdentity']==before['']['identity'] and command['expectedVersion']==version(baseline,'work/recursive-tree',before['']['identity'])
    assert (command['fileEntries'],command['directoryEntries'])==expected_targets(index)
    assert [plan[k] for k in ('entryCount','fileCount','directoryCount','changeCount')]==[4,2,2,CHANGES[index]]
    assert [result[k] for k in ('entryCount','fileCount','directoryCount','changeCount')]==[4,2,2,CHANGES[index]]
    assert [result['rootUid'],result['rootGid'],result['rootMode'],result['executorUid'],result['executorGid']]==[*before['']['identity'][3:5],before['']['identity'][2],1000,1000]
    assert plan['uid']==plan['gid']==1000 and plan['manifestDigest']==result['manifestDigest']
    assert result['command']['aclType']==kind and result['command']['fileEntries']==command['fileEntries'] and result['command']['directoryEntries']==command['directoryEntries']
    preserved(before,after,kind)
    if result['name']=='cancel':
        assert row['state']=='Canceled' and row['execution_job_id'] is None and row['root_started_at_utc'] is None and data['executionJob'] is None and data['confirmed']==0
        assert row['receipt_json'] is row['receipt_hash'] is row['root_state'] is None and not row['history_may_exist'] and not row['history_verified'] and row['history_digest'] is None
        assert result['receipt'] is None and after==before
        return dict(result=result,before=before,after=after,children=0)
    assert row['state']==data['executionJob']['state']=='Succeeded' and data['executionJob']['job_type']=='containers.files.recursive-acl' and data['confirmed']==1 and data['events']>=4
    assert row['execution_job_id']==result['executionJobId'] and row['root_started_at_utc'] and row['history_verified'] and row['history_may_exist'] and re.fullmatch('[A-F0-9]{64}',row['receipt_hash'])
    operation=uuid.UUID(row['execution_job_id']).hex;path=STATE/(operation+'.json');trusted(path);raw=path.read_bytes();record=json.loads(raw)
    assert 0<len(raw)<=65536 and record['schemaVersion']==1 and type(record['schemaVersion']) is int
    assert record['operationId']==operation and record['owner']==row['owner_hash'] and record['plan']==plan and record['state']=='Completed'
    assert digest(b'whaledeck-container-recursive-acl-plan-v1\n',unsigned(plan,'fingerprint'))==plan['fingerprint']
    receipt=result['receipt'];sql_receipt=json.loads(row['receipt_json'])
    assert sql_receipt['Id']==receipt['id']==row['execution_job_id'] and sql_receipt['State']==receipt['state']==row['root_state']=='Completed'
    assert sql_receipt['AclType']==receipt['aclType']==kind
    for key in ('completedCount','changedCount','abortedCount','notStartedCount','unknownCount'):
        column=re.sub(r'(?<!^)(?=[A-Z])','_',key).lower()
        assert type(record[key]) is int and record[key]==receipt[key]==sql_receipt[key[0].upper()+key[1:]]==row[column]
    assert [record[k] for k in ('completedCount','changedCount','abortedCount','notStartedCount','unknownCount')]==[4,CHANGES[index],0,0,0]
    for field in ('bindingCurrent','historyAvailable','historyVerified','historyMayExist'):assert receipt[field] is True
    assert record['historyVerified'] and record['historyMayExist'] and record['historyDigest']==row['history_digest']==receipt['historyDigest']
    archive(evidence,result['name']+'.root.json',raw,read_only)
    attrs=digest(b'whaledeck-container-acl-attributes-v1\n',[['user.whaledeck-recursive',base64.b64encode(b'preserve ordinary attribute').decode('ascii')]])
    outcomes=[]
    with storage.target(create_namespace=False,read_only=True) as (configuration,base):
        fd=storage.f.open_at(base,'container-recursive-acl-v1',os.O_RDONLY|os.O_DIRECTORY)
        try:
            storage.attest(configuration,fd,read_only=True)
            saved_raw=read_metadata(storage.f,storage,configuration,fd,operation+'.json');saved=json.loads(saved_raw)
            assert saved['resourceType']=='ContainerRecursiveAclMetadata' and saved['storageId']=='database-platform.backup.hdd' and saved['schemaVersion']==1 and type(saved['createdAtUnixMs']) is int
            assert saved['operationId']==operation and saved['owner']==row['owner_hash'] and saved['plan']==plan
            assert saved['historyDigest']==record['historyDigest']==digest(b'whaledeck-container-recursive-acl-history-v1\n',unsigned(saved,'historyDigest'))
            assert [e['relativePath'] for e in saved['entries']]==['sub/中文.bin','one.bin','sub','']
            assert plan['manifestDigest']==digest(b'whaledeck-container-recursive-acl-manifest-v1\n',saved['entries']) and plan['changeCount']==sum(not e['plan']['noChange'] for e in saved['entries'])
            archive(evidence,result['name']+'.hdd.json',saved_raw,read_only)
            child_fd=storage.f.open_at(fd,operation+'/entries',os.O_RDONLY|os.O_DIRECTORY)
            try:
                storage.attest(configuration,child_fd,read_only=True)
                for n,entry in enumerate(saved['entries']):
                    sub=hashlib.sha256(b'whaledeck-recursive-acl-entry-id-v1:'+operation.encode()+b':'+str(n).encode()).hexdigest()[:32]
                    child_path=STATE/(operation+'.entries')/(sub+'.json');trusted(child_path);child_raw=child_path.read_bytes();assert 0<len(child_raw)<=16384;child=json.loads(child_raw)
                    child_plan=entry['plan'];relative=entry['relativePath'];old=before[relative];actual=after[relative];identity=old['identity'];current=actual['identity'];cmd=child_plan['command']
                    assert child_plan['sourceIdentity']==identity and child_plan['accessAcl']==old['access'] and child_plan['defaultAcl']==old['default'] and child_plan['attributesDigest']==attrs
                    assert digest(b'whaledeck-container-acl-plan-v1\n',unsigned(child_plan,'fingerprint'))==child_plan['fingerprint']
                    node_path='work/recursive-tree'+('/'+relative if relative else '');file=PATHS[relative]=='File';target_type='access' if file and kind=='default' else kind
                    target=old['access'] if file and kind=='default' else command['fileEntries' if file else 'directoryEntries']
                    assert cmd==dict(containerId=baseline['containerId'],instanceId=baseline['instanceId'],path=node_path,expectedVersion=file_version(baseline,node_path,identity),aclType=target_type,entries=target)
                    assert child_plan['kind']==PATHS[relative] and child_plan['noChange']==(target==old['access' if target_type=='access' else 'default'])
                    assert child_plan['targetMode']==(acl_mode(target) if target_type=='access' else identity[2]&0o777)
                    assert child['schemaVersion']==1 and child['operationId']==sub and child['owner']==row['owner_hash'] and child['plan']==child_plan
                    assert child['state']=='Completed' and child['aclConfirmed'] is child['historyVerified'] is True and child['actualIdentity']==current
                    wanted=list(identity);wanted[2]=wanted[2]&~0o777|child_plan['targetMode'];assert current[:8]==wanted[:8]
                    assert actual['access']==(target if target_type=='access' else old['access']) and actual['default']==(target if target_type=='default' else old['default'])
                    if child_plan['noChange']:assert actual==old
                    published=file_version(baseline,node_path,current);assert child['version']==published
                    history_raw=read_metadata(storage.f,storage,configuration,child_fd,sub+'.json',16384);history=json.loads(history_raw)
                    assert history['resourceType']=='ContainerRecursiveAclEntryMetadata' and history['parentOperationId']==operation and history['manifestDigest']==plan['manifestDigest']
                    assert history['operationId']==sub and history['owner']==row['owner_hash'] and history['plan']==child_plan and history['storageId']=='database-platform.backup.hdd'
                    assert history['schemaVersion']==1 and type(history['createdAtUnixMs']) is int
                    assert history['historyDigest']==child['historyDigest']==digest(b'whaledeck-container-recursive-acl-entry-history-v1\n',unsigned(history,'historyDigest'))
                    archive(evidence,result['name']+'.'+sub+'.root.json',child_raw,read_only);archive(evidence,result['name']+'.'+sub+'.hdd.json',history_raw,read_only)
                    outcomes.append(dict(id=sub,state='Completed',version=published,changed=not child_plan['noChange']))
            finally:os.close(child_fd)
        finally:os.close(fd)
    assert record['outcomeDigest']==row['outcome_digest']==receipt['outcomeDigest']==digest(b'whaledeck-recursive-acl-outcomes-v1\n',outcomes)
    if result['name'] in ('noop','default-noop'):assert after==before
    return dict(result=result,before=before,after=after,children=4,rootSha256=hashlib.sha256(raw).hexdigest())


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
    assert os.geteuid()==0 and len(sys.argv)>=2; os.umask(0o077); os.environ['GIT_OPTIONAL_LOCKS']='0'
    selected=selected_cases(sys.argv[2:])
    revision = sys.argv[1]; assert re.fullmatch('[a-f0-9]{40}', revision)
    repo = Path(__file__).resolve().parents[2]; base = 'http://127.0.0.1:8080'
    assert not subprocess.check_output(['git','-C',str(repo),'status','--porcelain']).strip(), 'FREEZE_VALIDATION_SOURCE_FIRST'
    validation_revision = subprocess.check_output(['git','-C',str(repo),'rev-parse','HEAD'], text=True).strip()
    hashes = source_closure(repo,validation_revision,seeds=SOURCES)
    assert all(hashlib.sha256((repo/'deploy/validation'/n).read_bytes()).hexdigest()==v for n,v in hashes.items())
    hosts=host_identity(revision)
    source_hashes={}
    for name in (*NAMES,'whaledeck-container-backups.py','whaledeck-container-acl-transport.py','whaledeck-container-recursive-acl-transport.py','whaledeck-privileged'):
        path=Path('/usr/local/libexec')/name;trusted(path)
        frozen=subprocess.check_output(['git','-C',str(repo),'show',revision+':deploy/host/'+name]);assert path.read_bytes()==frozen
        source_hashes[name]=hashlib.sha256(frozen).hexdigest()
    no_active_work(); active = query("SELECT json_build_object('n',count(*)) FROM container_recursive_acl_intents WHERE completed_at_utc IS NULL OR publish_barrier;"); assert active['n'] == 0
    before = owned.common.core_snapshot(); assert len(before) == 11
    evidence = repo/'deploy/artifacts'/('container-recursive-acl-browser-'+uuid.uuid4().hex); evidence.mkdir(mode=0o700)
    print(json.dumps({'actualRecursiveAclBrowser':'started','evidence':str(evidence)}),flush=True)
    reader = load('/usr/local/libexec/whaledeck-container-files.py','recursive_browser_reader')
    files = reader.trusted_module('whaledeck-files.py','recursive_browser_files')
    storage = load('/usr/local/libexec/whaledeck-file-backups.py','recursive_browser_storage').FileBackups(files)
    acl=load('/usr/local/libexec/whaledeck-container-acl.py','recursive_browser_acl')
    with owned.fixture() as fixture:
        PHASE = 'owned-tree'; root = fixture['folder']/'recursive-tree'; root.mkdir(mode=0o750); (root/'sub').mkdir(mode=0o700)
        content = hashlib.shake_256(('recursive-browser:'+fixture['identifier']).encode()).digest(2*1024**2+7)
        owners = {'':(2100,3100),'one.bin':(2200,3200),'sub':(2101,3101),'sub/中文.bin':(2201,3201)}
        for relative, kind in PATHS.items():
            path = root/relative
            if kind == 'File': path.write_bytes(content)
            os.setxattr(path,'user.whaledeck-recursive',b'preserve ordinary attribute')
            os.chown(path,*owners[relative]); path.chmod(0o750 if relative=='' else 0o700 if kind=='Directory' else 0o640 if relative=='one.bin' else 0o600)
        if selected==CASES[5:]:
            access_files,access_dirs=expected_targets(4);default=expected_targets(0)[1]
            for relative,kind in PATHS.items():
                fd=os.open(root/relative,os.O_RDONLY|os.O_NOFOLLOW)
                try:
                    acl.write_acl(fd,'access',access_files if kind=='File' else access_dirs)
                    if kind=='Directory':acl.write_acl(fd,'default',default)
                finally:os.close(fd)
        for relative in PATHS: os.utime(root/relative, ns=(1700000000123456789,1700000000123456789))
        inspected = json.loads(owned.common.command(['docker','inspect',fixture['identifier']]))[0]
        original = inventory(root,reader,content,acl)
        baseline = {'revision':revision,'validationSourceRevision':validation_revision,'validationSourceSha256':hashes,'containerId':fixture['identifier'],'containerName':inspected['Name'].lstrip('/'),'instanceId':str(inspected['State']['Pid'])+':'+inspected['State']['StartedAt'],
            'source':str(fixture['folder']),'root':str(root),'original':original,'sha256':hashlib.sha256(content).hexdigest().upper(),'bytesPerFile':len(content),'cases':list(selected),'seededFixtureOnly':selected!=CASES,'coreBefore':before,'hostIdentity':hosts,'sourceHashes':source_hashes,'evidence':str(evidence)}
        baseline['sourceVersion'] = version(baseline,'work/recursive-tree',original['']['identity'])
        with storage.target(create_namespace=False,read_only=True) as (configuration,base_fd):
            storage.attest(configuration,base_fd,read_only=True)
            baseline['sourceDevice'] = root.stat().st_dev; baseline['hddDevice'] = os.fstat(base_fd).st_dev
        (evidence/'baseline.json').write_text(json.dumps(baseline))
        results = []; current = original
        with ExitStack() as users:
            admin=users.enter_context(temporary_user(True));ordinary=users.enter_context(temporary_user(False))
            environment={**os.environ,'WHALEDECK_URL':base};environment.pop('VALIDATION_USERNAME',None);environment.pop('VALIDATION_PASSWORD',None)
            with (evidence/'browser.stdout').open('w') as out, (evidence/'browser.stderr').open('w') as err:
                process = subprocess.Popen(['node',str(repo/'deploy/validation/validate_container_recursive_acl_browser.mjs'),revision],stdin=subprocess.PIPE,stdout=out,stderr=err,env=environment,text=True)
                try:
                    process.stdin.write(json.dumps({'fixture':baseline,'credentials':{'admin':dict(zip(('username','password'),admin)),'ordinary':dict(zip(('username','password'),ordinary))}}));process.stdin.close()
                    for name in selected:
                        PHASE = 'actual-ui-physical-proof-'+name
                        signal = wait_case(process,evidence,name); result = signal['result']; assert result['name'] == name and result['confirmation'] is None
                        after = inventory(root,reader,content,acl)
                        (evidence/(name+'-physical-before.json')).write_text(json.dumps(current))
                        (evidence/(name+'-physical-after.json')).write_text(json.dumps(after))
                        proof = verify_case(result,baseline,signal['subject'],evidence,reader,storage,current,after,acl); results.append(proof); current = after
                        (evidence/'results.json').write_text(json.dumps(results)); (evidence/'physical-final.json').write_text(json.dumps(current))
                        target = evidence/(name+'-verified.json'); pending = evidence/(name+'-verified.pending')
                        with pending.open('x') as stream: stream.write(json.dumps({'id':result['id'],'physicalAndHistoryVerified':True}))
                        pending.replace(target)
                        print(json.dumps({'actualRecursiveAclBrowser':'case-verified','case':name,'state':result['state'],'physicalAndHistoryVerified':True}),flush=True)
                    assert process.wait(timeout=60) == 0, 'ACTUAL_RECURSIVE_BROWSER_FAILED'
                finally:
                    if process.poll() is None:
                        process.terminate()
                        try: process.wait(timeout=15)
                        except subprocess.TimeoutExpired: process.kill(); process.wait(timeout=10)
            browser = json.loads((evidence/'browser.json').read_bytes()); assert browser['normalDualLogout'] and browser['registrations']==len(selected) and browser['confirmations']==len(selected)-1 and browser['contentUploads'] == 0
            assert browser['completed'] == [p['result'] for p in results]
            assert browser['ordinary']['deniedGet']==4 and browser['ordinary']['deniedPost']==4 and browser['ordinary']['pagePrivateRequests']==0 and browser['ordinary']['logout']['normalUiLogout']
            assert browser['anonymousDenied']==4
        PHASE = 'proved-source-cleanup'; assert inventory(root,reader,content,acl) == current
        remove_proved(root,reader,content,current,acl)
    PHASE = 'independent-cleanup-preconditions'
    assert owned.common.core_snapshot() == before and not fixture['folder'].exists() and host_identity(revision)==hosts
    no_active_work(); active = query("SELECT json_build_object('n',count(*)) FROM container_recursive_acl_intents WHERE completed_at_utc IS NULL OR publish_barrier OR lease_token IS NOT NULL;"); assert active['n'] == 0
    units = subprocess.check_output(['systemctl','list-units','--all','--no-legend','whaledeck-container-recursive-acl-*'],text=True,timeout=15); assert not units.strip()
    verified = {'revision':revision,'actualBrowserAndIdentity':True,'cases':len(selected),'caseLabels':list(selected),'succeeded':len(selected)-1,'canceledBeforeExecution':1,'aggregateHddRecords':len(selected)-1,'childHddRecords':4*(len(selected)-1),'perCasePhysicalBytesAndMetadata':True,'ordinaryDenied':8,'anonymousDenied':4,'normalDualLogout':True,'exactSourceAndContainerAbsent':True,'coreUnchanged':11,'permanentHistoryRetained':True}
    (evidence/'verified.json').write_text(json.dumps(verified))
    print(json.dumps({'actualRecursiveAclBrowser':'passed','evidence':str(evidence),'cases':len(selected)}),flush=True)


if __name__ == '__main__':
    try: main()
    except BaseException as error:
        import traceback
        frames=traceback.extract_tb(error.__traceback__)
        frame=next((f for f in reversed(frames) if f.filename.endswith('validate_container_recursive_acl_browser.py')),None)
        print(json.dumps({'actualRecursiveAclBrowser':'failed','phase':PHASE,'failureType':type(error).__name__,'frame':frame.lineno if frame else None}),flush=True)
        raise SystemExit(1) from None
