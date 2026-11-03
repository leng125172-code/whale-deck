#!/usr/bin/env python3
"""Independent installed Root/HDD inspection, with no local mutation engine.

Physical bytes are checked by the parent before cleanup, never reread afterward.
Host, probe and validation revisions are separate and independently attested.
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

from verify_container_recursive_acl import digest,unsigned,private_json,file_version,acl_mode
from validate_container_file_staging import load
from validate_container_file_publishing import common
from acl_source_apply_missing_proof import source_closure


def number(value,low=0,high=18446744073709551615):
    assert type(value) is int or type(value) is str and re.fullmatch('-?(0|[1-9][0-9]*)',value)
    result=int(value);assert low<=result<=high;return result


def entries(value,empty=False):
    assert type(value) is dict and set(value)<= {'entries'}
    values=value.get('entries',[]);assert type(values) is list and len(values)<=64
    assert values or empty
    output=[];keys=[];tags=[];order={'user_obj':1,'user':2,'group_obj':4,'group':8,'mask':16,'other':32}
    for item in values:
        assert type(item) is dict and set(item) in ({'tag','permissions'},{'tag','permissions','id'})
        tag=item['tag'];identifier=item.get('id');permission=item['permissions']
        assert type(tag) is str and tag in order and type(permission) is int and 0<=permission<=7
        if tag in ('user','group'):assert type(identifier) is int and 0<=identifier<4294967295
        else:assert 'id' not in item
        keys.append((order[tag],-1 if identifier is None else identifier));tags.append(tag)
        output.append({'tag':tag,'id':identifier,'permissions':permission})
    assert keys==sorted(set(keys))
    if values:assert all(tags.count(tag)==1 for tag in ('user_obj','group_obj','other')) and (not any(tag in tags for tag in ('user','group')) or 'mask' in tags)
    return output


def proto_plan(value):
    assert type(value) is dict
    command=value['command'];location=command['location'];source=value['rootIdentity'];kind=command['aclType']
    assert kind in ('access','default') and type(command['directoryEntries']) is dict
    directory=entries(command['directoryEntries'],empty=kind=='default')
    if kind=='access':file=entries(command['fileEntries'])
    else:assert command.get('fileEntries') is None;file=None
    root=[number(source[key]) for key in ('device','inode','mode','ownerUid','ownerGid','linkCount','sizeBytes')]
    root.extend((number(source['modifiedAtUnixNs'],-9223372036854775808,9223372036854775807),number(value['rootChangedAtUnixNs'],-9223372036854775808,9223372036854775807)))
    assert root[1]>0 and stat.S_ISDIR(root[2]) and not root[2]&0o7000 and root[2]<=65535
    assert root[3]<4294967295 and root[4]<4294967295 and root[5]>=1 and root[6]<=9223372036854775807
    assert value['schemaVersion']==1 and type(value['schemaVersion']) is int
    for key in ('ownerUid','ownerGid','entryCount','fileCount','directoryCount','changeCount'):
        assert type(value[key]) is int and 0<=value[key]<4294967295
    assert 0<value['entryCount']<=10000 and value['fileCount']+value['directoryCount']==value['entryCount'] and value['changeCount']<=value['entryCount']
    if kind=='default':assert value['changeCount']<=value['directoryCount']
    for item in (command['expectedVersion'],value['targetProof'],value['parentVersion'],value['fingerprint'],value['manifestDigest']):assert type(item) is str and re.fullmatch('[A-F0-9]{64}',item)
    assert re.fullmatch('[a-f0-9]{64}',location['containerId']) and type(location['instanceId']) is str and location['instanceId']
    assert location['path']=='work/recursive-tree'
    parent=[number(value['parentIdentity'][key]) for key in ('device','inode')];assert parent[1]>0
    return {'schemaVersion':1,'command':{'containerId':location['containerId'],'instanceId':location['instanceId'],'path':location['path'],
        'expectedVersion':command['expectedVersion'],'aclType':kind,'fileEntries':file,'directoryEntries':directory},
        'targetProof':value['targetProof'],'parentIdentity':parent,'parentVersion':value['parentVersion'],
        'uid':value['ownerUid'],'gid':value['ownerGid'],'fingerprint':value['fingerprint'],'rootIdentity':root,
        **{key:value[key] for key in ('manifestDigest','entryCount','fileCount','directoryCount','changeCount')}}


def verify_fixture_recipe(baseline):
    assert re.fullmatch('[a-f0-9]{64}',baseline['containerId']) and type(baseline['bytesPerFile']) is int and baseline['bytesPerFile']==2*1024**2+7
    content=hashlib.shake_256(('recursive-acl-installed:'+baseline['containerId']).encode()).digest(baseline['bytesPerFile'])
    assert hashlib.sha256(content).hexdigest().upper()==baseline['sha256'];return content


def expected_targets(index):
    def acl(mask,directory=False,root=False):
        return [dict(tag='user_obj',id=None,permissions=7 if directory else 6),dict(tag='user',id=0 if root else 2201,permissions=0 if root else 7 if directory else 6),
            dict(tag='group_obj',id=None,permissions=0),dict(tag='mask',id=None,permissions=mask),dict(tag='other',id=None,permissions=0)]
    if index in (0,5):return None,acl(6)
    if index==6:return None,[]
    return acl(6 if index in (3,7) else 4,root=index==4),acl(7,directory=True,root=index==4)


def verify_records(evidence,b,accepted,rows,files,storage):
    state=Path('/var/lib/whaledeck-container-recursive-acl');assert b['statePath']==str(state)
    previous=b['original'];ids=set();histories=0
    transcript=[json.loads(line) for line in (evidence/'rpc.jsonl').read_bytes().splitlines()]
    assert len(transcript)==43 and transcript[0]==dict(kind='ready',revision=b['revision'],probeRevision=b['probeRevision'],protocolMinor=53) and transcript[-1]==dict(kind='done')
    plans=[item for item in transcript if item['kind']=='plan'];assert len(plans)==8
    denied=[item for item in transcript if item['kind']=='rejected'];assert len(denied)==1 and denied[0]['refusals']==8
    labels=('default','access','noop','mask','root','default-noop','clear-default','cancel')
    changes=(2,4,0,2,4,0,2,0)
    expected_kinds=['ready']
    for index in range(8):expected_kinds+=['plan']+(['rejected'] if index==1 else [])+['receipt']*4
    expected_kinds+=['done'];assert [item['kind'] for item in transcript]==expected_kinds
    attrs=digest(b'whaledeck-container-acl-attributes-v1\n',[['user.whaledeck-recursive',base64.b64encode(b'preserve ordinary attribute').decode('ascii')]])
    with storage.target(create_namespace=False,read_only=True) as (configuration,base):
        fd=files.open_at(base,'container-recursive-acl-v1',os.O_RDONLY|os.O_DIRECTORY)
        try:
            storage.attest(configuration,fd,read_only=True);assert os.fstat(fd).st_dev==b['hddDevice']
            for index,row in enumerate(rows):
                label=labels[index];expected='Aborted' if index==7 else 'Completed';kind='default' if index in (0,5,6) else 'access'
                assert row['label']==label and row['before']==previous
                request=row['request'];operation=request['operationId'];plan=request['plan'];result=row['result'];command=plan['command']
                assert re.fullmatch('[a-f0-9]{32}',operation) and operation not in ids;ids.add(operation)
                root=state/(operation+'.json');record=private_json(root)
                assert root.read_bytes()==(evidence/'root-state'/root.name).read_bytes() and hashlib.sha256(root.read_bytes()).hexdigest()==row['rootSha256']
                assert type(record['schemaVersion']) is int and record['schemaVersion']==1 and all(record[k]==request[k] for k in ('operationId','owner','plan')) and request['owner']=='B'*64
                assert record['state']==result['state']==expected and result['aclType']==kind and plan==proto_plan(json.loads(plans[index]['plan']))
                assert plans[index]['boundaryRefusals']==(18 if index==1 else 0)
                if index==1:assert denied[0]['operationId']==operation
                assert digest(b'whaledeck-container-recursive-acl-plan-v1\n',unsigned(plan,'fingerprint'))==plan['fingerprint']
                assert set(command)=={'containerId','instanceId','path','expectedVersion','aclType','fileEntries','directoryEntries'}
                assert command['containerId']==b['containerId'] and command['instanceId']==b['instanceId'] and command['path']=='work/recursive-tree' and command['aclType']==kind
                assert (command['fileEntries'],command['directoryEntries'])==expected_targets(index)
                assert command['expectedVersion']==file_version(b,command['path'],previous['']['identity']) and plan['rootIdentity']==previous['']['identity']
                assert (plan['uid'],plan['gid'])==(1000,1000) and plan['entryCount']==4 and plan['fileCount']==plan['directoryCount']==2
                assert result['operationId']==operation and result['fingerprint']==plan['fingerprint'] and result['targetProof']==plan['targetProof']
                responses=[json.loads(item['result']) for item in transcript if item['kind']=='receipt' and item['operationId']==operation];assert len(responses)==4 and responses[0]==result
                for n,response in enumerate(responses):
                    assert response['state']==expected and response['aclType']==kind and response['operationId']==operation and response['fingerprint']==plan['fingerprint'] and response['targetProof']==plan['targetProof']
                    assert response['outcomeDigest']==result['outcomeDigest'] and response['historyDigest']==result['historyDigest'] and response['replayed']==(n in (1,2))
                    for key in ('completedCount','abortedCount','unknownCount','notStartedCount','changedCount'):assert type(response[key]) is int and response[key]==record[key]
                if expected=='Aborted':
                    assert row['after']==row['before'] and record['changedCount']==record['completedCount']==record['abortedCount']==record['unknownCount']==0 and record['notStartedCount']==4 and plan['changeCount']==4
                    assert record['historyMayExist'] is record['historyVerified'] is False and record['historyDigest']==record['outcomeDigest']==''
                    assert not result['bindingCurrent'] and not result['historyAvailable'] and not os.path.lexists(state/(operation+'.entries'))
                    try:storage.read_json(fd,operation+'.json')
                    except FileNotFoundError:pass
                    else:raise AssertionError('CANCELED_OPERATION_HAS_HISTORY')
                    previous=row['after'];continue
                assert record['historyVerified'] is record['historyMayExist'] is True and record['completedCount']==4 and record['changedCount']==changes[index]==plan['changeCount']
                assert record['abortedCount']==record['notStartedCount']==record['unknownCount']==0 and result['bindingCurrent'] and result['historyAvailable'] and result['historyVerified']
                saved=storage.read_json(fd,operation+'.json',16*1024**2)
                assert saved==private_json(evidence/(operation+'.hdd.json')) and all(saved[k]==request[k] for k in ('operationId','owner','plan'))
                assert saved['resourceType']=='ContainerRecursiveAclMetadata' and saved['storageId']=='database-platform.backup.hdd' and type(saved['schemaVersion']) is int and saved['schemaVersion']==1
                assert type(saved['createdAtUnixMs']) is int and saved['createdAtUnixMs']>=0
                assert digest(b'whaledeck-container-recursive-acl-history-v1\n',unsigned(saved,'historyDigest'))==saved['historyDigest']==record['historyDigest']==result['historyDigest']
                entries_saved=saved['entries'];assert digest(b'whaledeck-container-recursive-acl-manifest-v1\n',entries_saved)==plan['manifestDigest']
                assert [entry['relativePath'] for entry in entries_saved]==['sub/中文.bin','one.bin','sub',''] and plan['changeCount']==sum(not entry['plan']['noChange'] for entry in entries_saved)
                outcomes=[];batch=files.open_at(fd,operation+'/entries',os.O_RDONLY|os.O_DIRECTORY)
                try:
                    storage.attest(configuration,batch,read_only=True)
                    for n,entry in enumerate(entries_saved):
                        sub=hashlib.sha256(b'whaledeck-recursive-acl-entry-id-v1:'+operation.encode()+b':'+str(n).encode()).hexdigest()[:32]
                        child_path=state/(operation+'.entries')/(sub+'.json');child=private_json(child_path)
                        assert child_path.read_bytes()==(evidence/'root-state'/(operation+'.entries')/child_path.name).read_bytes()
                        child_plan=entry['plan'];relative=entry['relativePath'];old=row['before'][relative];actual=row['after'][relative];identity=old['identity'];current=actual['identity'];cmd=child_plan['command']
                        assert child_plan['sourceIdentity']==identity and child_plan['accessAcl']==old['access'] and child_plan['defaultAcl']==old['default'] and child_plan['attributesDigest']==attrs
                        assert digest(b'whaledeck-container-acl-plan-v1\n',unsigned(child_plan,'fingerprint'))==child_plan['fingerprint']
                        path='work/recursive-tree'+('/'+relative if relative else '');file=relative in ('one.bin','sub/中文.bin');target_type='access' if file and kind=='default' else kind
                        target=old['access'] if file and kind=='default' else command['fileEntries' if file else 'directoryEntries']
                        assert cmd==dict(containerId=b['containerId'],instanceId=b['instanceId'],path=path,expectedVersion=file_version(b,path,identity),aclType=target_type,entries=target)
                        assert child_plan['kind']==('File' if file else 'Directory') and child_plan['noChange']==(target==old['access' if target_type=='access' else 'default'])
                        assert child_plan['targetMode']==(acl_mode(target) if target_type=='access' else identity[2]&0o777)
                        assert type(child['schemaVersion']) is int and child['schemaVersion']==1 and child['operationId']==sub and child['owner']==request['owner'] and child['plan']==child_plan
                        assert child['state']=='Completed' and child['aclConfirmed'] is child['historyVerified'] is True and child['actualIdentity']==current
                        wanted=list(identity);wanted[2]=wanted[2]&~0o777|child_plan['targetMode'];assert current[:8]==wanted[:8]
                        assert actual['access']==(target if target_type=='access' else old['access']) and actual['default']==(target if target_type=='default' else old['default'])
                        if child_plan['noChange']:assert actual==old
                        assert current[:2]==identity[:2] and current[3:8]==identity[3:8] and current[2]&~0o777==identity[2]&~0o777
                        published=file_version(b,path,current);assert child['version']==published
                        hdd=storage.read_json(batch,sub+'.json');assert hdd['plan']==child_plan and hdd['parentOperationId']==operation and hdd['manifestDigest']==plan['manifestDigest']
                        assert hdd['resourceType']=='ContainerRecursiveAclEntryMetadata' and hdd['operationId']==sub and hdd['owner']==request['owner'] and hdd['storageId']=='database-platform.backup.hdd'
                        assert type(hdd['schemaVersion']) is int and hdd['schemaVersion']==1 and type(hdd['createdAtUnixMs']) is int and hdd['createdAtUnixMs']>=0
                        assert digest(b'whaledeck-container-recursive-acl-entry-history-v1\n',unsigned(hdd,'historyDigest'))==hdd['historyDigest']==child['historyDigest'];histories+=1
                        outcomes.append(dict(id=sub,state='Completed',version=published,changed=not child_plan['noChange']))
                finally:os.close(batch)
                assert digest(b'whaledeck-recursive-acl-outcomes-v1\n',outcomes)==record['outcomeDigest']==result['outcomeDigest']
                if index in (2,5):assert row['after']==row['before']
                previous=row['after']
        finally:os.close(fd)
    assert histories==accepted['childHddMetadata']==28 and private_json(evidence/'physical-final.json')==previous
    return histories


def main():
    from validate_container_recursive_acl_agent import host_identity,no_active_work
    assert os.geteuid()==0 and len(sys.argv)==2;os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0'
    name=sys.argv[1];assert re.fullmatch('container-recursive-acl-agent-[a-f0-9]{32}',name)
    repo=Path(__file__).resolve().parents[2];evidence=repo/'deploy/artifacts'/name
    assert evidence.resolve(strict=True)==evidence and evidence.stat().st_uid==0 and stat.S_IMODE(evidence.stat().st_mode)==0o700
    b=private_json(evidence/'baseline.json');accepted=private_json(evidence/'verified.json');rows=private_json(evidence/'results.json')
    for key in ('revision','probeRevision','validationSourceRevision'):assert re.fullmatch('[a-f0-9]{40}',b[key])
    assert accepted['revision']==b['revision'] and accepted['operations']==len(rows)==8 and accepted['completed']==7 and accepted['aborted']==1
    for flag in ('installedSignedUdsAndFixedHelper','fullBytesAndOriginalInodesOwnersMtimeAttributesAndOtherAcl','rootAndHddRetained','exactSourceAndContainerCleaned','coreUnchanged'):assert accepted[flag] is True
    assert accepted['humanBffOrUi'] is b['candidateOnly'] is False and accepted['planBoundaryRefusals']==18 and accepted['operationBoundaryRefusals']==8 and accepted['aggregateHddMetadata']==7 and accepted['activeTransientUnits']==0
    verify_fixture_recipe(b)
    assert b['validationSourceSha256']==source_closure(repo,b['validationSourceRevision'],seeds=('validate_container_recursive_acl_agent.py','verify_container_recursive_acl_agent.py','test_recursive_acl_agent_probe_scope.py'))
    for filename,sha in b['validationSourceSha256'].items():assert hashlib.sha256((repo/'deploy/validation'/filename).read_bytes()).hexdigest()==sha
    probe_source=subprocess.check_output(['git','-C',str(repo),'show',b['probeRevision']+':deploy/validation/TerminalProbe/ContainerRecursiveAclProbe.cs'])
    assert hashlib.sha256(probe_source).hexdigest()==b['probeSourceSha256'] and host_identity(b['revision'])==b['hostIdentity']
    for filename,sha in b['sourceHashes'].items():
        assert re.fullmatch(r'whaledeck-[a-z-]+(?:\.py)?',filename)
        path=Path('/usr/local/libexec')/filename;info=path.lstat();assert stat.S_ISREG(info.st_mode) and info.st_uid==info.st_gid==0 and info.st_nlink==1 and stat.S_IMODE(info.st_mode)==0o750
        raw=subprocess.check_output(['git','-C',str(repo),'show',b['revision']+':deploy/host/'+filename]);assert path.read_bytes()==raw and hashlib.sha256(raw).hexdigest()==sha
    files=load('/usr/local/libexec/whaledeck-files.py','recursive_acl_installed_second_files')
    storage=load('/usr/local/libexec/whaledeck-file-backups.py','recursive_acl_installed_second_hdd').FileBackups(files)
    histories=verify_records(evidence,b,accepted,rows,files,storage)
    source=Path(b['source']);assert source.parent==Path('/data/WhaleDeck/apps') and source.name==b['name'] and re.fullmatch('wdv-container-publish-[a-f0-9]{32}',source.name) and not os.path.lexists(source)
    assert Path(b['root'])==source/'recursive-tree'
    absent=subprocess.run(['docker','inspect',b['containerId']],capture_output=True,timeout=15);assert absent.returncode!=0 and b'no such' in absent.stderr.lower()
    assert common.core_snapshot()=={key:tuple(value) for key,value in b['coreBefore'].items()};no_active_work()
    units=subprocess.check_output(['systemctl','list-units','--all','--no-legend','whaledeck-container-recursive-acl-*'],text=True,timeout=10);assert not units.strip()
    health=json.loads(subprocess.check_output(['docker','inspect',*common.CORE_NAMES]));assert len(health)==11 and all(item['State'].get('Health',{}).get('Status')=='healthy' for item in health)
    proof=dict(revision=b['revision'],probeRevision=b['probeRevision'],validationSourceRevision=b['validationSourceRevision'],operations=8,completed=7,aborted=1,childHddMetadata=histories,
        installedSignedRpc=True,actualRootAndAttestedHddVerified=True,chronologyAndExactCleanup=True,independentReadOnlyCheck=True,coreUnchangedHealthy=11,sourceBytesRereadAfterCleanup=False,humanBusinessOrUi=False)
    (evidence/'independent-final.json').write_text(json.dumps(proof));print(json.dumps({'independentInstalledRecursiveAclRpc':'passed',**proof}),flush=True)


if __name__=='__main__':main()
