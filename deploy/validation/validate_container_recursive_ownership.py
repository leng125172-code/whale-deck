#!/usr/bin/env python3
"""Frozen internal recursive-ownership candidate on one owned container/real HDD.

No installed helper, RPC, business job, human identity or UI. Trusted disposable
candidate checkpoints generate real process crashes; they are not product fault
hooks. Exact source proofs are required before cleanup; receipts stay on HDD.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import select
import signal
import stat
import subprocess
import sys
import tempfile
import uuid

import validate_container_file_publishing as owned
from validate_container_file_staging import load
from validate_container_directory_restart import preflight
from validate_container_recursive_permission_faults import inactive_except

PHASE='startup'
NAMES=('whaledeck-files.py','whaledeck-container-files.py','whaledeck-container-terminal.py',
    'whaledeck-container-upload.py','whaledeck-container-publish.py','whaledeck-file-backups.py',
    'whaledeck-container-directories.py','whaledeck-container-ownership.py','whaledeck-container-recursive-permissions.py','whaledeck-container-recursive-ownership.py')
PATHS={'':'Directory','one.bin':'File','sub':'Directory','sub/中文.bin':'File'}


def inventory(root,reader,content):
    result={}
    for relative,kind in PATHS.items():
        path=root/relative;info=path.lstat();assert not path.is_symlink()
        if kind=='File':
            assert stat.S_ISREG(info.st_mode) and info.st_nlink==1 and path.read_bytes()==content
            assert os.getxattr(path,'user.whaledeck-recursive')==b'preserve ordinary attribute'
        else:
            assert stat.S_ISDIR(info.st_mode)
            assert {p.name for p in path.iterdir()}=={n.rsplit('/',1)[-1] for n in PATHS if n and n.rpartition('/')[0]==relative}
        result[relative]=list(reader.fingerprint(info))
    return result


def remove_proved(root,reader,content,expected):
    assert inventory(root,reader,content)==expected
    for relative in ('sub/中文.bin','one.bin','sub',''):
        path=root/relative;current=list(reader.fingerprint(path.lstat()))
        if PATHS[relative]=='File':assert current==expected[relative];path.unlink()
        else:
            assert current[:5]==expected[relative][:5] and not list(path.iterdir());path.rmdir()


def kill_at(engine,request,session,phase):
    pid=os.fork()
    if pid==0:
        engine.checkpoint=lambda point,index:os.kill(os.getpid(),signal.SIGKILL) if point==phase and index==0 else None
        try:engine.apply(request,session)
        except BaseException:os._exit(2)
        os._exit(3)
    fd=os.pidfd_open(pid)
    try:
        ready=select.select([fd],[],[],30)[0]
        if not ready:signal.pidfd_send_signal(fd,signal.SIGKILL)
        _,status=os.waitpid(pid,0)
        assert ready and os.WIFSIGNALED(status) and os.WTERMSIG(status)==signal.SIGKILL,'CANDIDATE_CUT_NOT_REACHED'
    finally:os.close(fd)


def main():
    global PHASE
    assert os.geteuid()==0 and len(sys.argv)==2;os.umask(0o077)
    revision=sys.argv[1];assert re.fullmatch('[a-f0-9]{40}',revision)
    repo=Path(__file__).resolve().parents[2]
    os.environ['GIT_OPTIONAL_LOCKS']='0'
    def blob(path):return subprocess.check_output(['git','-C',str(repo),'show',revision+':'+path],timeout=10)
    assert subprocess.check_output(['git','-C',str(repo),'rev-parse','HEAD'],text=True).strip()==revision
    assert not subprocess.check_output(['git','-C',str(repo),'status','--porcelain']).strip(),'FREEZE_CANDIDATE_SOURCE_FIRST'
    frozen={name:blob('deploy/host/'+name) for name in NAMES}
    assert all((repo/'deploy/host'/name).read_bytes()==raw for name,raw in frozen.items())
    assert Path(__file__).read_bytes()==blob('deploy/validation/'+Path(__file__).name)
    verifier=repo/'deploy/validation/verify_container_recursive_ownership.py'
    assert verifier.read_bytes()==blob('deploy/validation/'+verifier.name)
    inactive_except();before=owned.common.core_snapshot();assert len(before)==11
    evidence=repo/'deploy/artifacts'/('container-recursive-ownership-'+uuid.uuid4().hex);evidence.mkdir(mode=0o700)
    state=evidence/'root-state';state.mkdir(mode=0o700)
    (evidence/'scope.json').write_text(json.dumps({'revision':revision,'candidateOnly':True}))
    results=[];baseline=None
    try:
        with tempfile.TemporaryDirectory(prefix='wd-native-recursive-') as temporary:
            bundle=Path(temporary)
            for name,raw in frozen.items():(bundle/name).write_bytes(raw);(bundle/name).chmod(0o750)
            reader=load(bundle/NAMES[1],'recursive_native_reader')
            files=reader.trusted_module(NAMES[0],'recursive_native_files');docker=reader.trusted_module(NAMES[2],'recursive_native_docker')
            uploads=load(bundle/NAMES[3],'recursive_native_upload').ContainerUploads(reader,files,state)
            publisher=load(bundle/NAMES[4],'recursive_native_publish').ContainerPublisher(uploads,type('UnusedContentBackup',(),{'u':uploads})())
            storage=load(bundle/NAMES[5],'recursive_native_hdd').FileBackups(files)
            directories=load(bundle/NAMES[6],'recursive_native_directories').ContainerDirectories
            single=load(bundle/NAMES[7],'recursive_native_single')
            module=load(bundle/NAMES[8],'recursive_native_engine');profile=load(bundle/NAMES[9],'recursive_native_owner')
            history=profile.recursive_ownership_history_type(module)(storage)
            engine=profile.recursive_ownership_type(single.container_ownership_type(directories),directories,single.ContainerOwnershipHistory,module)(uploads,publisher,state,history)
            with owned.fixture() as fixture:
                PHASE='owned-tree'
                root=fixture['folder']/'recursive-tree';root.mkdir(mode=0o750);(root/'sub').mkdir(mode=0o700)
                content=hashlib.shake_256(('recursive-owner-candidate:'+fixture['identifier']).encode()).digest(2*1024**2+7)
                for relative,kind in PATHS.items():
                    path=root/relative
                    if kind=='File':path.write_bytes(content);os.setxattr(path,'user.whaledeck-recursive',b'preserve ordinary attribute')
                    os.chown(path,2100+len(relative),3100+len(relative))
                    path.chmod(0o750 if relative=='' else 0o700 if kind=='Directory' else 0o640 if relative=='one.bin' else 0o600)
                    os.utime(path,ns=(1700000000123456789,1700000000123456789))
                inspected=docker.api('GET','/containers/'+fixture['identifier']+'/json')
                target=docker.target(inspected,fixture['identifier'],docker.registry())
                location={'containerId':fixture['identifier'],'instanceId':target['instanceId']}
                baseline={'revision':revision,**location,'name':inspected['Name'].lstrip('/'),'source':str(fixture['folder']),
                    'root':str(root),'original':inventory(root,reader,content),'sha256':hashlib.sha256(content).hexdigest().upper(),
                    'bytesPerFile':len(content),'coreBefore':before,'candidateOnly':True,
                    'sourceHashes':{name:hashlib.sha256(raw).hexdigest() for name,raw in frozen.items()}}
                (evidence/'baseline.json').write_text(json.dumps(baseline))
                with reader.ContainerRoot(location,files,docker) as session:
                    for label,uid,gid,phase in [('complete',2200,3200,None),('noop',2200,3200,None),
                            ('partial',2300,3300,'AfterEntry'),('marker-only',2400,3400,'BeforeEntry')]:
                        PHASE=label;old=inventory(root,reader,content)
                        command={**location,'path':'work/recursive-tree','expectedVersion':reader.version(location,'work/recursive-tree',root.stat()),
                            'uid':uid,'gid':gid}
                        request={'operationId':uuid.uuid4().hex,'owner':'B'*64,'plan':engine.plan(command,session)}
                        assert [request['plan']['uid'],request['plan']['gid']]==[1000,1000]
                        assert request['plan']['entryCount']==4 and request['plan']['fileCount']==2
                        if phase:kill_at(engine,request,session,phase);result=engine.inspect(request,session)
                        else:result=engine.apply(request,session)
                        after=inventory(root,reader,content)
                        expected='Partial' if label=='partial' else 'Aborted' if phase else 'Completed'
                        assert result['state']==expected and result['historyAvailable'] and result['historyVerified']
                        if expected=='Completed':
                            assert result['bindingCurrent'] and result['completedCount']==4
                            for relative in PATHS:assert after[relative][3:5]==[uid,gid]
                        elif expected=='Partial':
                            assert result['changedCount']==1 and result['notStartedCount']==3
                            assert after['sub/中文.bin'][3:5]==[uid,gid]
                            assert all(after[path]==old[path] for path in PATHS if path!='sub/中文.bin')
                        if label in ('noop','marker-only'):assert after==old
                        for path in PATHS:assert after[path][:3]==old[path][:3] and after[path][5:8]==old[path][5:8]
                        receipt=state/(request['operationId']+'.json');receipt_hash=hashlib.sha256(receipt.read_bytes()).hexdigest()
                        replay=engine.apply(request,session)
                        assert replay['state']==expected and replay['replayed'] and inventory(root,reader,content)==after
                        assert hashlib.sha256(receipt.read_bytes()).hexdigest()==receipt_hash
                        saved=history.load(request);assert saved['historyDigest']==result['historyDigest']
                        (evidence/(request['operationId']+'.hdd.json')).write_text(json.dumps(saved))
                        results.append({'label':label,'request':request,'result':result,'before':old,'after':after,'crashPhase':phase,'rootSha256':receipt_hash})
                        (evidence/'results.json').write_text(json.dumps(results))
                    final=inventory(root,reader,content);(evidence/'physical-final.json').write_text(json.dumps(final))
                    with history.target(False) as (_,fd):
                        assert os.fstat(fd).st_dev==root.stat().st_dev
                        baseline['hddDevice']=os.fstat(fd).st_dev
                        (evidence/'baseline.json').write_text(json.dumps(baseline))
                    PHASE='proved-cleanup';remove_proved(root,reader,content,final)
            assert baseline and not os.path.lexists(baseline['source'])
        assert owned.common.core_snapshot()==before;inactive_except()
        verified={'revision':revision,'operations':4,'completed':2,'partial':1,'aborted':1,'actualCandidateSigkillCuts':2,
            'actualAttestedHddMetadata':True,'allFullBytesAndInodesModesMtimeAttributesVerified':True,
            'exactContainerAndSourceCleaned':True,'rootAndHddRetained':True,'coreUnchanged':True,
            'installedHelper':False,'signedRpc':False,'businessOrHumanUi':False}
        (evidence/'verified.json').write_text(json.dumps(verified))
        print(json.dumps({'recursiveOwnershipCandidate':'passed','evidence':evidence.name,**verified}),flush=True)
    except BaseException as error:
        (evidence/'failure.json').write_text(json.dumps({'phase':PHASE,'type':type(error).__name__,'completedOperations':len(results)}))
        print(json.dumps({'recursiveOwnershipCandidate':'failed','phase':PHASE,'evidence':evidence.name,'type':type(error).__name__}),flush=True)
        raise


if __name__=='__main__':main()
