#!/usr/bin/env python3
"""Independent read-only checks of candidate journals and actual attested HDD.

Writes only this gate's evidence report. No source mutations or container actions;
full source bytes were checked by the parent before exact fixture removal.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import sys

from validate_container_file_staging import load
from validate_container_directory_restart import preflight
from validate_container_file_publishing import common


def encode(value):return json.dumps(value,ensure_ascii=False,sort_keys=True,separators=(',',':'),allow_nan=False).encode()
def digest(domain,value):return hashlib.sha256(domain+encode(value)).hexdigest().upper()
def unsigned(value,key):return {k:v for k,v in value.items() if k!=key}


def private_json(path):
    info=path.lstat()
    assert stat.S_ISREG(info.st_mode) and info.st_uid==0 and info.st_nlink==1 and stat.S_IMODE(info.st_mode)==0o600
    return json.loads(path.read_bytes())


def main():
    assert os.geteuid()==0 and len(sys.argv)==2;os.umask(0o077)
    name=sys.argv[1];assert re.fullmatch('container-recursive-permissions-[a-f0-9]{32}',name)
    repo=Path(__file__).resolve().parents[2];evidence=repo/'deploy/artifacts'/name
    assert evidence.resolve(strict=True)==evidence and evidence.stat().st_uid==0 and stat.S_IMODE(evidence.stat().st_mode)==0o700
    baseline=private_json(evidence/'baseline.json');verified=private_json(evidence/'verified.json');rows=private_json(evidence/'results.json')
    assert re.fullmatch('[a-f0-9]{40}',baseline['revision']) and verified['revision']==baseline['revision']
    assert len(rows)==verified['operations']==4 and verified['actualCandidateSigkillCuts']==2
    assert verified['actualAttestedHddMetadata'] and verified['allFullBytesAndInodesOwnersMtimeAttributesVerified']
    assert verified['exactContainerAndSourceCleaned'] and verified['rootAndHddRetained'] and verified['coreUnchanged']
    assert verified['installedHelper'] is verified['signedRpc'] is verified['businessOrHumanUi'] is False
    assert baseline['candidateOnly'] and re.fullmatch('[a-f0-9]{64}',baseline['containerId'])
    os.environ['GIT_OPTIONAL_LOCKS']='0'
    for module,expected in baseline['sourceHashes'].items():
        assert re.fullmatch(r'whaledeck-[a-z-]+\.py',module)
        raw=subprocess.check_output(['git','-C',str(repo),'show',baseline['revision']+':deploy/host/'+module],timeout=10)
        assert hashlib.sha256(raw).hexdigest()==expected
    files=load('/usr/local/libexec/whaledeck-files.py','recursive_independent_files')
    storage=load('/usr/local/libexec/whaledeck-file-backups.py','recursive_independent_storage').FileBackups(files)
    previous=baseline['original'];ids=set();child_histories=0
    with storage.target(create_namespace=False,read_only=True) as (configuration,base):
        target=files.open_at(base,'container-recursive-permissions-v1',os.O_RDONLY|os.O_DIRECTORY)
        try:
            storage.attest(configuration,target,read_only=True);assert os.fstat(target).st_dev==baseline['hddDevice']
            for index,row in enumerate(rows):
                label=('complete','noop','partial','marker-only')[index];state=('Completed','Completed','Partial','Aborted')[index]
                assert row['label']==label and row['result']['state']==state
                request=row['request'];operation=request['operationId'];plan=request['plan']
                assert re.fullmatch('[a-f0-9]{32}',operation) and operation not in ids;ids.add(operation)
                root=evidence/'root-state'/(operation+'.json');receipt=private_json(root)
                assert hashlib.sha256(root.read_bytes()).hexdigest()==row['rootSha256']
                assert all(receipt[k]==request[k] for k in ('operationId','owner','plan')) and request['owner']=='B'*64
                assert receipt['state']==state and receipt['historyVerified'] and receipt['historyMayExist']
                assert receipt['historyDigest']==row['result']['historyDigest'] and receipt['outcomeDigest']==row['result']['outcomeDigest']
                assert digest(b'whaledeck-container-recursive-permission-plan-v1\n',unsigned(plan,'fingerprint'))==plan['fingerprint']
                assert plan['command']['containerId']==baseline['containerId'] and plan['command']['instanceId']==baseline['instanceId']
                assert plan['command']['path']=='work/recursive-tree' and (plan['uid'],plan['gid'])==(1000,1000)
                assert row['before']==previous and plan['rootIdentity']==previous['']
                saved=storage.read_json(target,operation+'.json',16*1024**2)
                assert saved==private_json(evidence/(operation+'.hdd.json')) and saved['plan']==plan and saved['owner']==request['owner']
                assert saved['resourceType']=='ContainerRecursivePermissionMetadata' and saved['operationId']==operation
                assert digest(b'whaledeck-container-recursive-permission-history-v1\n',unsigned(saved,'historyDigest'))==saved['historyDigest']==receipt['historyDigest']
                entries=saved['entries']
                assert digest(b'whaledeck-container-recursive-permission-manifest-v1\n',entries)==plan['manifestDigest']
                assert [e['relativePath'] for e in entries]==['sub/中文.bin','one.bin','sub','']
                assert plan['entryCount']==4 and plan['fileCount']==plan['directoryCount']==2
                outcomes=[];counts={k:0 for k in ('Completed','Aborted','NotStarted')};changes=0
                for n,entry in enumerate(entries):
                    child_id=hashlib.sha256(b'whaledeck-recursive-permission-entry-id-v1:'+operation.encode()+b':'+str(n).encode()).hexdigest()[:32]
                    child_path=evidence/'root-state'/(operation+'.entries')/(child_id+'.json')
                    child_plan=entry['plan'];relative=entry['relativePath'];old=row['before'][relative];actual=row['after'][relative]
                    assert child_plan['sourceIdentity']==old
                    assert digest(b'whaledeck-container-permission-plan-v1\n',unsigned(child_plan,'fingerprint'))==child_plan['fingerprint']
                    command=child_plan['command'];assert command['path']=='work/recursive-tree'+('/'+relative if relative else '')
                    assert command['containerId']==baseline['containerId'] and command['instanceId']==baseline['instanceId']
                    expected_version=hashlib.sha256(b'whaledeck-container-file-v1\n'+json.dumps([baseline['containerId'],baseline['instanceId'],command['path'],*old],ensure_ascii=False,separators=(',',':')).encode()).hexdigest().upper()
                    assert command['expectedVersion']==expected_version
                    completed=state=='Completed' or label=='partial' and n==0
                    aborted=label=='marker-only' and n==0
                    version='';changed=False
                    if completed or aborted:
                        child=private_json(child_path);assert child['operationId']==child_id and child['owner']==request['owner'] and child['plan']==child_plan
                        assert child['state']==('Completed' if completed else 'Aborted')
                        if completed:
                            assert child['modeConfirmed'] and child['historyVerified'] and child['actualIdentity']==actual
                            expected=list(old);expected[2]=old[2]&~0o777|int(command['mode'],8);assert actual[:8]==expected[:8]
                            version=hashlib.sha256(b'whaledeck-container-file-v1\n'+json.dumps([baseline['containerId'],baseline['instanceId'],command['path'],*actual],ensure_ascii=False,separators=(',',':')).encode()).hexdigest().upper()
                            assert child['version']==version;changed=not child_plan['noChange']
                            batch=files.open_at(target,operation+'/entries',os.O_RDONLY|os.O_DIRECTORY)
                            try:
                                storage.attest(configuration,batch,read_only=True);hdd=storage.read_json(batch,child_id+'.json')
                            finally:os.close(batch)
                            assert hdd['plan']==child_plan and hdd['parentOperationId']==operation and hdd['manifestDigest']==plan['manifestDigest']
                            assert hdd['resourceType']=='ContainerRecursivePermissionEntryMetadata' and hdd['operationId']==child_id and hdd['owner']==request['owner']
                            assert digest(b'whaledeck-container-recursive-permission-entry-history-v1\n',unsigned(hdd,'historyDigest'))==hdd['historyDigest']==child['historyDigest']
                            child_histories+=1
                    else:assert not os.path.lexists(child_path)
                    if not completed:assert actual==old
                    assert actual[:2]==old[:2] and actual[3:8]==old[3:8]
                    phase='Completed' if completed else 'Aborted' if aborted else 'NotStarted';counts[phase]+=1;changes+=changed
                    outcomes.append({'id':child_id,'state':phase,'version':version,'changed':changed})
                assert receipt['completedCount']==counts['Completed'] and receipt['abortedCount']==counts['Aborted'] and receipt['notStartedCount']==counts['NotStarted']
                assert receipt['changedCount']==changes and receipt['unknownCount']==0
                assert digest(b'whaledeck-recursive-permission-outcomes-v1\n',outcomes)==receipt['outcomeDigest']
                if label in ('noop','marker-only'):assert row['after']==row['before']
                assert row['crashPhase']==('AfterEntry' if label=='partial' else 'BeforeEntry' if label=='marker-only' else None)
                previous=row['after']
        finally:os.close(target)
    assert child_histories==9 and private_json(evidence/'physical-final.json')==previous
    source=Path(baseline['source']);assert source.parent==Path('/data/WhaleDeck/apps') and source.name==baseline['name']
    assert re.fullmatch('wdv-container-publish-[a-f0-9]{32}',source.name) and not os.path.lexists(source)
    absent=subprocess.run(['docker','inspect',baseline['containerId']],capture_output=True,timeout=15)
    assert absent.returncode!=0 and b'no such' in absent.stderr.lower()
    assert common.core_snapshot()=={k:tuple(v) for k,v in baseline['coreBefore'].items()};preflight()
    health=subprocess.check_output(['docker','ps','--format','{{.Names}}|{{.Status}}'],text=True,timeout=15).splitlines()
    assert len([line for line in health if line.startswith(('whaledeck-','database-platform-')) and '(healthy)' in line])==11
    proof={'revision':baseline['revision'],'operations':4,'actualCandidateSigkillCuts':2,'childHddMetadata':child_histories,
        'aggregateAndChildRootAndAttestedHddVerified':True,'chronologyAndNoReplayVerified':True,'exactFixtureAbsent':True,
        'independentReadOnlyCheck':True,'coreUnchangedHealthy':11,'installedOrBusinessOrUi':False}
    (evidence/'independent-final.json').write_text(json.dumps(proof))
    print(json.dumps({'recursiveCandidateIndependent':'passed',**proof}),flush=True)


if __name__=='__main__':main()
