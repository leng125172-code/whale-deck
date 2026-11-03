#!/usr/bin/env python3
"""Independent read-only verification of actual candidate Root/HDD records.

Full source bytes were checked before exact cleanup by the parent. No mutation,
installed RPC or business/UI acceptance is inferred from candidate receipts.
"""
import base64
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import sys
from validate_container_file_staging import load
from validate_container_file_publishing import common
from validate_container_acl_browser import no_active_work
from validate_container_acl_faults import host_snapshot
from acl_source_apply_missing_proof import source_closure


def encode(value):return json.dumps(value,ensure_ascii=False,sort_keys=True,separators=(',',':'),allow_nan=False).encode()
def digest(domain,value):return hashlib.sha256(domain+encode(value)).hexdigest().upper()
def unsigned(value,key):return {k:v for k,v in value.items() if k!=key}


def private_json(path):
    info=path.lstat()
    assert stat.S_ISREG(info.st_mode) and info.st_uid==info.st_gid==0 and info.st_nlink==1 and stat.S_IMODE(info.st_mode)==0o600 and info.st_size<=16*1024**2
    def unique(pairs):
        value={}
        for key,item in pairs:
            assert key not in value;value[key]=item
        return value
    return json.loads(path.read_bytes(),object_pairs_hook=unique)


def verify_fixture_recipe(baseline):
    assert re.fullmatch('[a-f0-9]{64}',baseline['containerId']) and type(baseline['bytesPerFile']) is int and baseline['bytesPerFile']==2*1024**2+7
    content=hashlib.shake_256(('recursive-acl-candidate:'+baseline['containerId']).encode()).digest(baseline['bytesPerFile'])
    assert hashlib.sha256(content).hexdigest().upper()==baseline['sha256'];return content


def file_version(baseline,path,identity):
    return hashlib.sha256(b'whaledeck-container-file-v1\n'+json.dumps([baseline['containerId'],baseline['instanceId'],path,*identity],ensure_ascii=False,separators=(',',':')).encode()).hexdigest().upper()


def acl_mode(entries):
    values={e['tag']:e['permissions'] for e in entries if e['id'] is None}
    return values['user_obj']*64+values.get('mask',values['group_obj'])*8+values['other']


def main():
    assert os.geteuid()==0 and len(sys.argv)==2;os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0'
    name=sys.argv[1];assert re.fullmatch('container-recursive-acl-[a-f0-9]{32}',name)
    repo=Path(__file__).resolve().parents[2];evidence=repo/'deploy/artifacts'/name
    assert evidence.resolve(strict=True)==evidence and evidence.stat().st_uid==0 and stat.S_IMODE(evidence.stat().st_mode)==0o700
    b=private_json(evidence/'baseline.json');accepted=private_json(evidence/'verified.json');rows=private_json(evidence/'results.json')
    assert re.fullmatch('[a-f0-9]{40}',b['revision']) and accepted['revision']==b['revision'] and b['candidateOnly']
    verify_fixture_recipe(b)
    assert b['validationSourceSha256']==source_closure(repo,b['revision'],seeds=('validate_container_recursive_acl.py','verify_container_recursive_acl.py','test_container_file_recursive_acl.py'))
    for module,sha in b['sourceHashes'].items():
        assert re.fullmatch(r'whaledeck-[a-z-]+\.py',module)
        raw=subprocess.check_output(['git','-C',str(repo),'show',b['revision']+':deploy/host/'+module]);assert hashlib.sha256(raw).hexdigest()==sha
    assert len(rows)==accepted['operations']==6 and accepted['actualCandidateSigkillCuts']==2
    for flag in ('actualAttestedHddMetadata','allFullBytesAndInodesOwnersMtimeAttributesAndOtherAclVerified','exactContainerAndSourceCleaned','rootAndHddRetained','coreUnchanged'):assert accepted[flag] is True
    assert accepted['installedHelper'] is accepted['signedRpc'] is accepted['businessOrHumanUi'] is False
    files=load('/usr/local/libexec/whaledeck-files.py','recursive_acl_independent_files')
    storage=load('/usr/local/libexec/whaledeck-file-backups.py','recursive_acl_independent_storage').FileBackups(files)
    previous=b['original'];ids=set();child_histories=0
    labels=['access-complete','access-noop','default-complete','default-clear','partial-access','marker-only']
    states=['Completed','Completed','Completed','Completed','Partial','Aborted']
    attrs=digest(b'whaledeck-container-acl-attributes-v1\n',[['user.whaledeck-recursive',base64.b64encode(b'preserve ordinary attribute').decode('ascii')]])
    with storage.target(create_namespace=False,read_only=True) as (configuration,base):
        fd=files.open_at(base,'container-recursive-acl-v1',os.O_RDONLY|os.O_DIRECTORY)
        try:
            storage.attest(configuration,fd,read_only=True);assert os.fstat(fd).st_dev==b['hddDevice']
            for number,row in enumerate(rows):
                label,state=labels[number],states[number];assert row['label']==label and row['result']['state']==state
                request=row['request'];operation=request['operationId'];plan=request['plan'];command=plan['command']
                assert re.fullmatch('[a-f0-9]{32}',operation) and operation not in ids;ids.add(operation)
                root=evidence/'root-state'/(operation+'.json');record=private_json(root)
                assert hashlib.sha256(root.read_bytes()).hexdigest()==row['rootSha256'] and all(record[k]==request[k] for k in ('operationId','owner','plan'))
                assert type(record['schemaVersion']) is int and record['schemaVersion']==1 and request['owner']=='B'*64 and record['state']==state
                assert record['historyVerified'] is record['historyMayExist'] is True and record['historyDigest']==row['result']['historyDigest']
                assert record['outcomeDigest']==row['result']['outcomeDigest']
                assert digest(b'whaledeck-container-recursive-acl-plan-v1\n',unsigned(plan,'fingerprint'))==plan['fingerprint']
                assert command['containerId']==b['containerId'] and command['instanceId']==b['instanceId'] and command['path']=='work/recursive-tree'
                assert set(command)=={'containerId','instanceId','path','expectedVersion','aclType','fileEntries','directoryEntries'}
                assert command['expectedVersion']==file_version(b,command['path'],row['before']['']['identity'])
                assert (plan['uid'],plan['gid'])==(1000,1000) and row['before']==previous and plan['rootIdentity']==previous['']['identity']
                kind=command['aclType'];assert kind==('default' if label.startswith('default') else 'access')
                if kind=='default':assert command['fileEntries'] is None
                saved=storage.read_json(fd,operation+'.json',16*1024**2)
                assert saved==private_json(evidence/(operation+'.hdd.json')) and saved['plan']==plan and saved['owner']==request['owner'] and saved['operationId']==operation
                assert saved['resourceType']=='ContainerRecursiveAclMetadata' and saved['storageId']=='database-platform.backup.hdd' and type(saved['schemaVersion']) is int and saved['schemaVersion']==1
                assert type(saved['createdAtUnixMs']) is int and saved['createdAtUnixMs']>=0
                assert digest(b'whaledeck-container-recursive-acl-history-v1\n',unsigned(saved,'historyDigest'))==saved['historyDigest']==record['historyDigest']
                entries=saved['entries'];assert digest(b'whaledeck-container-recursive-acl-manifest-v1\n',entries)==plan['manifestDigest']
                assert [e['relativePath'] for e in entries]==['sub/中文.bin','one.bin','sub',''] and plan['entryCount']==4 and plan['fileCount']==plan['directoryCount']==2
                outcomes=[];counts={k:0 for k in ('Completed','Aborted','NotStarted')};changes=0
                assert plan['changeCount']==sum(not e['plan']['noChange'] for e in entries)
                for n,entry in enumerate(entries):
                    child_id=hashlib.sha256(b'whaledeck-recursive-acl-entry-id-v1:'+operation.encode()+b':'+str(n).encode()).hexdigest()[:32]
                    child_path=evidence/'root-state'/(operation+'.entries')/(child_id+'.json')
                    child_plan=entry['plan'];relative=entry['relativePath'];old=row['before'][relative];actual=row['after'][relative];cmd=child_plan['command']
                    identity=old['identity'];current=actual['identity']
                    assert child_plan['sourceIdentity']==identity and child_plan['accessAcl']==old['access'] and child_plan['defaultAcl']==old['default']
                    assert child_plan['attributesDigest']==attrs and digest(b'whaledeck-container-acl-plan-v1\n',unsigned(child_plan,'fingerprint'))==child_plan['fingerprint']
                    path='work/recursive-tree'+('/'+relative if relative else '')
                    assert cmd['path']==path and cmd['containerId']==b['containerId'] and cmd['instanceId']==b['instanceId'] and cmd['expectedVersion']==file_version(b,path,identity)
                    file=child_plan['kind']=='File';expected_type='access' if file and kind=='default' else kind
                    target=old['access'] if file and kind=='default' else command['fileEntries' if file else 'directoryEntries']
                    assert cmd['aclType']==expected_type and cmd['entries']==target
                    assert child_plan['noChange']==(target==old['access' if expected_type=='access' else 'default'])
                    assert child_plan['targetMode']==(acl_mode(target) if expected_type=='access' else identity[2]&0o777)
                    completed=state=='Completed' or label=='partial-access' and n==0;aborted=label=='marker-only' and n==0
                    version='';changed=False
                    if completed or aborted:
                        child=private_json(child_path);assert child['operationId']==child_id and child['owner']==request['owner'] and child['plan']==child_plan
                        assert child['state']==('Completed' if completed else 'Aborted')
                        if completed:
                            assert child['aclConfirmed'] is child['historyVerified'] is True and child['actualIdentity']==current
                            desired=list(identity);desired[2]=desired[2]&~0o777|child_plan['targetMode'];assert current[:8]==desired[:8]
                            assert actual['access']==(target if expected_type=='access' else old['access']) and actual['default']==(target if expected_type=='default' else old['default'])
                            version=file_version(b,path,current);assert child['version']==version;changed=not child_plan['noChange']
                            batch=files.open_at(fd,operation+'/entries',os.O_RDONLY|os.O_DIRECTORY)
                            try:storage.attest(configuration,batch,read_only=True);hdd=storage.read_json(batch,child_id+'.json')
                            finally:os.close(batch)
                            assert hdd['plan']==child_plan and hdd['parentOperationId']==operation and hdd['manifestDigest']==plan['manifestDigest']
                            assert hdd['resourceType']=='ContainerRecursiveAclEntryMetadata' and hdd['operationId']==child_id and hdd['owner']==request['owner']
                            assert type(hdd['schemaVersion']) is int and hdd['schemaVersion']==1 and hdd['storageId']=='database-platform.backup.hdd'
                            assert type(hdd['createdAtUnixMs']) is int and hdd['createdAtUnixMs']>=0
                            assert digest(b'whaledeck-container-recursive-acl-entry-history-v1\n',unsigned(hdd,'historyDigest'))==hdd['historyDigest']==child['historyDigest'];child_histories+=1
                        else:assert child['aclConfirmed'] is False and child['historyVerified'] is False
                    else:assert not os.path.lexists(child_path)
                    if not completed:assert actual==old
                    assert current[:2]==identity[:2] and current[3:8]==identity[3:8] and current[2]&~0o777==identity[2]&~0o777
                    if kind=='default':
                        assert actual['access']==old['access'] and current[2]==identity[2]
                        if file:assert actual==old
                    else:assert actual['default']==old['default']
                    phase='Completed' if completed else 'Aborted' if aborted else 'NotStarted';counts[phase]+=1;changes+=changed
                    outcomes.append(dict(id=child_id,state=phase,version=version,changed=changed))
                assert all(type(record[k]) is int for k in ('completedCount','abortedCount','notStartedCount','changedCount','unknownCount'))
                assert record['completedCount']==counts['Completed'] and record['abortedCount']==counts['Aborted'] and record['notStartedCount']==counts['NotStarted']
                assert record['changedCount']==changes and record['unknownCount']==0 and digest(b'whaledeck-recursive-acl-outcomes-v1\n',outcomes)==record['outcomeDigest']
                if label in ('access-noop','marker-only'):assert row['after']==row['before']
                assert row['crashPhase']==('AfterEntry' if label=='partial-access' else 'BeforeEntry' if label=='marker-only' else None)
                previous=row['after']
        finally:os.close(fd)
    assert child_histories==17 and private_json(evidence/'physical-final.json')==previous
    source=Path(b['source']);assert source.parent==Path('/data/WhaleDeck/apps') and source.name==b['name'] and not os.path.lexists(source)
    absent=subprocess.run(['docker','inspect',b['containerId']],capture_output=True,timeout=15);assert absent.returncode!=0 and b'no such' in absent.stderr.lower()
    assert common.core_snapshot()=={k:tuple(v) for k,v in b['coreBefore'].items()};no_active_work();assert host_snapshot('28062da454fd579d6860391a88816dab0bfca143')==b['hostsBefore']
    health=json.loads(subprocess.check_output(['docker','inspect',*common.CORE_NAMES]));assert len(health)==11 and all(c['State'].get('Health',{}).get('Status')=='healthy' for c in health)
    proof=dict(revision=b['revision'],operations=6,actualCandidateSigkillCuts=2,childHddMetadata=17,aggregateAndChildRootAndAttestedHddVerified=True,
        chronologyAndNoReplayVerified=True,exactFixtureAbsent=True,independentReadOnlyCheck=True,coreUnchangedHealthy=11,installedOrBusinessOrUi=False,deletedSourceBytesReread=False)
    (evidence/'independent-final.json').write_text(json.dumps(proof));print(json.dumps(proof))


if __name__=='__main__':main()
