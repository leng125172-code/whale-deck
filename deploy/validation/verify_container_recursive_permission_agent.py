#!/usr/bin/env python3
"""Read-only second process verifies original installed Root/HDD and exact cleanup.

Full bytes were checked before source removal, not reread after deletion.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import sys

from validate_container_recursive_permissions import PATHS
from validate_container_recursive_permission_agent import STATE,no_active_work,host_identity
from verify_container_recursive_permissions import digest,unsigned,private_json
from validate_container_file_staging import load
from validate_container_file_publishing import common


def proto_plan(value):
    command=value['command'];location=command['location'];source=value['rootIdentity']
    return {'schemaVersion':value['schemaVersion'],'command':{'containerId':location['containerId'],'instanceId':location['instanceId'],'path':location['path'],
        'expectedVersion':command['expectedVersion'],'fileMode':command['fileMode'],'directoryMode':command['directoryMode']},
        'targetProof':value['targetProof'],'parentIdentity':[int(value['parentIdentity']['device']),int(value['parentIdentity']['inode'])],
        'parentVersion':value['parentVersion'],'uid':value['ownerUid'],'gid':value['ownerGid'],'fingerprint':value['fingerprint'],
        'rootIdentity':[int(source[key]) for key in ('device','inode','mode','ownerUid','ownerGid','linkCount','sizeBytes','modifiedAtUnixNs')]+[int(value['rootChangedAtUnixNs'])],
        **{key:value[key] for key in ('manifestDigest','entryCount','fileCount','directoryCount','changeCount')}}


def main():
    assert os.geteuid()==0 and len(sys.argv)==2;os.umask(0o077)
    name=sys.argv[1];assert re.fullmatch('container-recursive-permission-agent-[a-f0-9]{32}',name)
    repo=Path(__file__).resolve().parents[2];evidence=repo/'deploy/artifacts'/name
    assert evidence.resolve(strict=True)==evidence and evidence.stat().st_uid==0 and stat.S_IMODE(evidence.stat().st_mode)==0o700
    baseline=private_json(evidence/'baseline.json');verified=private_json(evidence/'verified.json');rows=private_json(evidence/'results.json')
    revision=baseline['revision'];assert re.fullmatch('[a-f0-9]{40}',revision) and verified['revision']==revision
    assert verified['operations']==len(rows)==4 and verified['completed']==3 and verified['aborted']==1
    assert verified['installedSignedUdsAndFixedHelper'] and not verified['humanBffOrUi'] and not baseline['candidateOnly']
    assert verified['fullBytesAndOriginalInodesOwnersMtimeAttributes'] and verified['rootAndHddRetained'] and verified['exactSourceAndContainerCleaned']
    assert verified['planBoundaryRefusals']==8 and verified['operationBoundaryRefusals']==3
    assert baseline['statePath']==str(STATE) and host_identity(revision)==baseline['hostIdentity']
    os.environ['GIT_OPTIONAL_LOCKS']='0'
    for module,sha in baseline['sourceHashes'].items():
        assert re.fullmatch(r'whaledeck-[a-z-]+(?:\.py)?',module)
        path=Path('/usr/local/libexec')/module;info=path.lstat();assert info.st_uid==0 and info.st_nlink==1 and stat.S_IMODE(info.st_mode)==0o750
        frozen=subprocess.check_output(['git','-C',str(repo),'show',revision+':deploy/host/'+module],timeout=10)
        assert path.read_bytes()==frozen and hashlib.sha256(frozen).hexdigest()==sha
    files=load('/usr/local/libexec/whaledeck-files.py','installed_recursive_second_files')
    storage=load('/usr/local/libexec/whaledeck-file-backups.py','installed_recursive_second_storage').FileBackups(files)
    previous=baseline['original'];ids=set();histories=0
    transcript=[json.loads(line) for line in (evidence/'rpc.jsonl').read_bytes().splitlines()]
    assert transcript[0]['kind']=='ready' and transcript[-1]['kind']=='done'
    plans=[proto_plan(json.loads(item['plan'])) for item in transcript if item['kind']=='plan']
    assert len(plans)==4
    with storage.target(create_namespace=False,read_only=True) as (configuration,base):
        fd=files.open_at(base,'container-recursive-permissions-v1',os.O_RDONLY|os.O_DIRECTORY)
        try:
            storage.attest(configuration,fd,read_only=True);assert os.fstat(fd).st_dev==baseline['hddDevice']
            for index,row in enumerate(rows):
                label=('complete','noop','alternate','cancel')[index];state='Aborted' if index==3 else 'Completed'
                assert row['label']==label and row['before']==previous
                request=row['request'];operation=request['operationId'];plan=request['plan'];result=row['result']
                assert re.fullmatch('[a-f0-9]{32}',operation) and operation not in ids;ids.add(operation)
                root=STATE/(operation+'.json');record=private_json(root)
                assert root.read_bytes()==(evidence/'root-state'/root.name).read_bytes() and hashlib.sha256(root.read_bytes()).hexdigest()==row['rootSha256']
                assert all(record[k]==request[k] for k in ('operationId','owner','plan')) and request['owner']=='B'*64
                assert record['state']==result['state']==state and plan==plans[index]
                assert plan['fingerprint']==digest(b'whaledeck-container-recursive-permission-plan-v1\n',unsigned(plan,'fingerprint'))
                command=plan['command'];assert command['containerId']==baseline['containerId'] and command['instanceId']==baseline['instanceId'] and command['path']=='work/recursive-tree'
                assert [plan['uid'],plan['gid']]==[1000,1000] and plan['rootIdentity']==previous['']
                version=hashlib.sha256(b'whaledeck-container-file-v1\n'+json.dumps([baseline['containerId'],baseline['instanceId'],command['path'],*previous['']],ensure_ascii=False,separators=(',',':')).encode()).hexdigest().upper()
                assert command['expectedVersion']==version
                assert plan['entryCount']==4 and plan['fileCount']==plan['directoryCount']==2
                assert result['operationId']==operation and result['fingerprint']==plan['fingerprint'] and result['targetProof']==plan['targetProof']
                assert result['completedCount']==record['completedCount'] and result['changedCount']==record['changedCount']
                actions=[item for item in transcript if item['kind']=='receipt' and item['operationId']==operation]
                assert len(actions)==4
                for item in actions:
                    response=json.loads(item['result']);assert response['state']==state and response['outcomeDigest']==result['outcomeDigest'] and response['historyDigest']==result['historyDigest']
                if state=='Aborted':
                    assert row['after']==row['before'] and record['changedCount']==record['completedCount']==record['unknownCount']==0 and record['notStartedCount']==4
                    assert not record['historyMayExist'] and not record['historyVerified'] and record['historyDigest']==record['outcomeDigest']==''
                    assert not result['bindingCurrent'] and not result['historyAvailable']
                    assert not os.path.lexists(STATE/(operation+'.entries'))
                    try:storage.read_json(fd,operation+'.json')
                    except FileNotFoundError:pass
                    else:raise AssertionError('CANCELED_OPERATION_HAS_HISTORY')
                    previous=row['after'];continue
                assert record['completedCount']==4 and record['changedCount']==plan['changeCount']==(2,0,4)[index]
                assert record['abortedCount']==record['notStartedCount']==record['unknownCount']==0 and record['historyMayExist'] and record['historyVerified']
                assert result['bindingCurrent'] and result['historyAvailable'] and result['historyVerified']
                saved=storage.read_json(fd,operation+'.json',16*1024**2)
                assert saved==private_json(evidence/(operation+'.hdd.json')) and saved['plan']==plan and saved['operationId']==operation and saved['owner']==request['owner']
                assert saved['resourceType']=='ContainerRecursivePermissionMetadata' and saved['storageId']=='database-platform.backup.hdd'
                assert saved['historyDigest']==record['historyDigest']==result['historyDigest']==digest(b'whaledeck-container-recursive-permission-history-v1\n',unsigned(saved,'historyDigest'))
                entries=saved['entries'];assert [entry['relativePath'] for entry in entries]==['sub/中文.bin','one.bin','sub','']
                assert plan['manifestDigest']==digest(b'whaledeck-container-recursive-permission-manifest-v1\n',entries)
                outcomes=[]
                child_fd=files.open_at(fd,operation+'/entries',os.O_RDONLY|os.O_DIRECTORY)
                try:
                    storage.attest(configuration,child_fd,read_only=True)
                    for n,entry in enumerate(entries):
                        sub=hashlib.sha256(b'whaledeck-recursive-permission-entry-id-v1:'+operation.encode()+b':'+str(n).encode()).hexdigest()[:32]
                        child_path=STATE/(operation+'.entries')/(sub+'.json');child=private_json(child_path)
                        assert child_path.read_bytes()==(evidence/'root-state'/(operation+'.entries')/child_path.name).read_bytes()
                        child_plan=entry['plan'];relative=entry['relativePath'];old=row['before'][relative];actual=row['after'][relative]
                        assert child['state']=='Completed' and child['modeConfirmed'] and child['historyVerified'] and child['actualIdentity']==actual
                        assert child['plan']==child_plan and child['owner']==request['owner'] and child['operationId']==sub and child_plan['sourceIdentity']==old
                        assert child_plan['fingerprint']==digest(b'whaledeck-container-permission-plan-v1\n',unsigned(child_plan,'fingerprint'))
                        wanted=list(old);wanted[2]=old[2]&~0o777|int(command['directoryMode'] if PATHS[relative]=='Directory' else command['fileMode'],8)
                        assert actual[:8]==wanted[:8]
                        if child_plan['noChange']:assert actual==old
                        hdd=storage.read_json(child_fd,sub+'.json');assert hdd['plan']==child_plan and hdd['parentOperationId']==operation and hdd['manifestDigest']==plan['manifestDigest']
                        assert hdd['resourceType']=='ContainerRecursivePermissionEntryMetadata' and hdd['owner']==request['owner'] and hdd['operationId']==sub
                        assert hdd['historyDigest']==child['historyDigest']==digest(b'whaledeck-container-recursive-permission-entry-history-v1\n',unsigned(hdd,'historyDigest'))
                        published=hashlib.sha256(b'whaledeck-container-file-v1\n'+json.dumps([baseline['containerId'],baseline['instanceId'],child_plan['command']['path'],*actual],ensure_ascii=False,separators=(',',':')).encode()).hexdigest().upper()
                        assert published==child['version'];outcomes.append({'id':sub,'state':'Completed','version':published,'changed':not child_plan['noChange']});histories+=1
                finally:os.close(child_fd)
                assert record['outcomeDigest']==result['outcomeDigest']==digest(b'whaledeck-recursive-permission-outcomes-v1\n',outcomes)
                previous=row['after']
        finally:os.close(fd)
    assert histories==verified['childHddMetadata']==12 and private_json(evidence/'physical-final.json')==previous
    source=Path(baseline['source']);assert source.parent==Path('/data/WhaleDeck/apps') and source.name==baseline['name']
    assert re.fullmatch('wdv-container-publish-[a-f0-9]{32}',source.name) and not os.path.lexists(source)
    absent=subprocess.run(['docker','inspect',baseline['containerId']],capture_output=True,timeout=15);assert absent.returncode!=0 and b'no such' in absent.stderr.lower()
    assert common.core_snapshot()=={key:tuple(v) for key,v in baseline['coreBefore'].items()};no_active_work()
    units=subprocess.check_output(['systemctl','list-units','--all','--no-legend','whaledeck-container-recursive-permissions-*'],text=True,timeout=10);assert not units.strip()
    health=subprocess.check_output(['docker','ps','--format','{{.Names}}|{{.Status}}'],text=True,timeout=15).splitlines()
    assert len([line for line in health if line.startswith(('whaledeck-','database-platform-')) and '(healthy)' in line])==11
    proof={'revision':revision,'operations':4,'completed':3,'aborted':1,'childHddMetadata':histories,'installedSignedRpc':True,
        'actualRootAndAttestedHddVerified':True,'chronologyAndExactCleanup':True,'independentReadOnlyCheck':True,'coreUnchangedHealthy':11,
        'sourceBytesRereadAfterCleanup':False,'humanBusinessOrUi':False}
    (evidence/'independent-final.json').write_text(json.dumps(proof));print(json.dumps({'independentInstalledRecursivePermissionRpc':'passed',**proof}),flush=True)


if __name__=='__main__':main()
