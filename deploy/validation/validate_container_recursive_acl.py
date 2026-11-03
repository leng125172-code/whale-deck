#!/usr/bin/env python3
"""Frozen internal recursive-acl candidate on one owned container/real HDD.

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
from validate_container_acl_browser import no_active_work
from validate_container_acl_faults import host_snapshot
from acl_source_apply_missing_proof import source_closure
from unittest.mock import patch

PHASE='startup'
NAMES=('whaledeck-files.py','whaledeck-container-files.py','whaledeck-container-terminal.py',
    'whaledeck-container-upload.py','whaledeck-container-publish.py','whaledeck-file-backups.py',
    'whaledeck-container-directories.py','whaledeck-container-permissions.py','whaledeck-container-acl.py','whaledeck-container-recursive-permissions.py','whaledeck-container-recursive-acl.py')
PATHS={'':'Directory','one.bin':'File','sub':'Directory','sub/中文.bin':'File'}


def file_acl(mask=4):
    return [{'tag':'user_obj','id':None,'permissions':6},{'tag':'user','id':2201,'permissions':6},
            {'tag':'group_obj','id':None,'permissions':0},{'tag':'mask','id':None,'permissions':mask},{'tag':'other','id':None,'permissions':0}]


def fixture_bytes(identifier):
    assert re.fullmatch('[a-f0-9]{64}',identifier)
    return hashlib.shake_256(('recursive-acl-candidate:'+identifier).encode()).digest(2*1024**2+7)


def directory_acl():
    value=file_acl(7);value[0]['permissions']=7;value[1]['permissions']=7;return value


def inventory(root,reader,content,acl):
    result={}
    for relative,kind in PATHS.items():
        path=root/relative;info=path.lstat();assert not path.is_symlink()
        if kind=='File':assert stat.S_ISREG(info.st_mode) and info.st_nlink==1 and path.read_bytes()==content
        else:
            assert stat.S_ISDIR(info.st_mode)
            assert {p.name for p in path.iterdir()}=={n.rsplit('/',1)[-1] for n in PATHS if n and n.rpartition('/')[0]==relative}
        assert os.getxattr(path,'user.whaledeck-recursive')==b'preserve ordinary attribute'
        assert set(os.listxattr(path))<={'user.whaledeck-recursive',*acl.XATTRS.values()}
        fd=os.open(path,os.O_RDONLY|os.O_NOFOLLOW)
        try:result[relative]=dict(identity=list(reader.fingerprint(info)),access=acl.read_acl(fd,'access',info.st_mode),default=acl.read_acl(fd,'default',info.st_mode))
        finally:os.close(fd)
    return result


def preserved(before,after,kind):
    assert before.keys()==after.keys()==PATHS.keys()
    for relative,old in before.items():
        new=after[relative];a,b=old['identity'],new['identity']
        assert a[:2]==b[:2] and a[3:8]==b[3:8] and a[2]&~0o777==b[2]&~0o777
        if kind=='access':assert new['default']==old['default']
        else:
            assert new['access']==old['access'] and b[2]==a[2]
            if PATHS[relative]=='File':assert new==old


def remove_proved(root,reader,content,expected,acl):
    assert inventory(root,reader,content,acl)==expected
    for relative in ('sub/中文.bin','one.bin','sub',''):
        path=root/relative;current=list(reader.fingerprint(path.lstat()))
        if PATHS[relative]=='File':assert current==expected[relative]['identity'];path.unlink()
        else:
            assert current[:5]==expected[relative]['identity'][:5] and not list(path.iterdir());path.rmdir()


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
    verifier=repo/'deploy/validation/verify_container_recursive_acl.py'
    assert verifier.read_bytes()==blob('deploy/validation/'+verifier.name)
    validation=source_closure(repo,revision,seeds=('validate_container_recursive_acl.py','verify_container_recursive_acl.py','test_container_file_recursive_acl.py'))
    assert all(hashlib.sha256((repo/'deploy/validation'/name).read_bytes()).hexdigest()==sha for name,sha in validation.items())
    no_active_work();before=owned.common.core_snapshot();assert len(before)==11
    hosts=host_snapshot('28062da454fd579d6860391a88816dab0bfca143')
    evidence=repo/'deploy/artifacts'/('container-recursive-acl-'+uuid.uuid4().hex);evidence.mkdir(mode=0o700)
    print(json.dumps(dict(recursiveAclCandidate='started',evidence=evidence.name)),flush=True)
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
            single=load(bundle/NAMES[7],'recursive_native_single');acl=load(bundle/NAMES[8],'recursive_native_acl')
            module=load(bundle/NAMES[9],'recursive_native_engine');profile=load(bundle/NAMES[10],'recursive_native_acl_profile')
            history=profile.recursive_acl_history_type(module)(storage)
            engine=profile.recursive_acl_type(acl,single,directories,module)(uploads,publisher,state,history)
            with owned.fixture() as fixture:
                PHASE='owned-tree'
                root=fixture['folder']/'recursive-tree';root.mkdir(mode=0o750);(root/'sub').mkdir(mode=0o700)
                content=fixture_bytes(fixture['identifier'])
                for relative,kind in PATHS.items():
                    path=root/relative
                    if kind=='File':path.write_bytes(content)
                    os.setxattr(path,'user.whaledeck-recursive',b'preserve ordinary attribute')
                    os.chown(path,2100+len(relative),3100+len(relative))
                    path.chmod(0o750 if relative=='' else 0o700 if kind=='Directory' else 0o640 if relative=='one.bin' else 0o600)
                    os.utime(path,ns=(1700000000123456789,1700000000123456789))
                for relative in ('','sub'):
                    fd=os.open(root/relative,os.O_RDONLY|os.O_DIRECTORY)
                    try:acl.write_acl(fd,'default',directory_acl())
                    finally:os.close(fd)
                inspected=docker.api('GET','/containers/'+fixture['identifier']+'/json')
                target=docker.target(inspected,fixture['identifier'],docker.registry())
                location={'containerId':fixture['identifier'],'instanceId':target['instanceId']}
                baseline={'revision':revision,**location,'name':inspected['Name'].lstrip('/'),'source':str(fixture['folder']),
                    'root':str(root),'original':inventory(root,reader,content,acl),'sha256':hashlib.sha256(content).hexdigest().upper(),
                    'bytesPerFile':len(content),'coreBefore':before,'candidateOnly':True,
                    'sourceHashes':{name:hashlib.sha256(raw).hexdigest() for name,raw in frozen.items()},'validationSourceSha256':validation,'hostsBefore':hosts}
                (evidence/'baseline.json').write_text(json.dumps(baseline))
                with reader.ContainerRoot(location,files,docker) as session:
                    cases=[('access-complete','access',file_acl(),directory_acl(),None),('access-noop','access',file_acl(),directory_acl(),None),
                        ('default-complete','default',None,file_acl(6),None),('default-clear','default',None,[],None),
                        ('partial-access','access',file_acl(6),directory_acl(),'AfterEntry'),('marker-only','access',file_acl(7),directory_acl(),'BeforeEntry')]
                    for label,kind,file_entries,directory_entries,phase in cases:
                        PHASE=label;old=inventory(root,reader,content,acl)
                        command={**location,'path':'work/recursive-tree','expectedVersion':reader.version(location,'work/recursive-tree',root.stat()),
                            'aclType':kind,'fileEntries':file_entries,'directoryEntries':directory_entries}
                        request={'operationId':uuid.uuid4().hex,'owner':'B'*64,'plan':engine.plan(command,session)}
                        assert [request['plan']['uid'],request['plan']['gid']]==[1000,1000]
                        assert request['plan']['entryCount']==4 and request['plan']['fileCount']==2
                        if phase:kill_at(engine,request,session,phase);result=engine.inspect(request,session)
                        else:result=engine.apply(request,session)
                        after=inventory(root,reader,content,acl)
                        expected='Partial' if label=='partial-access' else 'Aborted' if phase else 'Completed'
                        assert result['state']==expected and result['aclType']==kind and result['historyAvailable'] and result['historyVerified']
                        if expected=='Completed':
                            assert result['bindingCurrent'] and result['completedCount']==4
                            assert result['changedCount']==(4 if label=='access-complete' else 0 if label=='access-noop' else 2)
                            for relative,node in after.items():
                                if kind=='access':assert node['access']==(file_entries if PATHS[relative]=='File' else directory_entries)
                                elif PATHS[relative]=='Directory':assert node['default']==directory_entries
                        elif expected=='Partial':
                            assert result['changedCount']==1 and result['notStartedCount']==3
                            assert after['sub/中文.bin']['access']==file_entries
                            assert all(after[path]==old[path] for path in PATHS if path!='sub/中文.bin')
                        if label in ('access-noop','marker-only'):assert after==old
                        preserved(old,after,kind)
                        receipt=state/(request['operationId']+'.json');receipt_hash=hashlib.sha256(receipt.read_bytes()).hexdigest()
                        with patch.object(acl,'write_acl',side_effect=AssertionError('candidate replay must never write ACL')):replay=engine.apply(request,session)
                        assert replay['state']==expected and replay['replayed'] and inventory(root,reader,content,acl)==after
                        assert hashlib.sha256(receipt.read_bytes()).hexdigest()==receipt_hash
                        saved=history.load(request);assert saved['historyDigest']==result['historyDigest']
                        (evidence/(request['operationId']+'.hdd.json')).write_text(json.dumps(saved))
                        results.append({'label':label,'request':request,'result':result,'before':old,'after':after,'crashPhase':phase,'rootSha256':receipt_hash})
                        (evidence/'results.json').write_text(json.dumps(results))
                    final=inventory(root,reader,content,acl);(evidence/'physical-final.json').write_text(json.dumps(final))
                    with history.target(False) as (_,fd):
                        assert os.fstat(fd).st_dev==root.stat().st_dev
                        baseline['hddDevice']=os.fstat(fd).st_dev
                        (evidence/'baseline.json').write_text(json.dumps(baseline))
                    PHASE='proved-cleanup';remove_proved(root,reader,content,final,acl)
            assert baseline and not os.path.lexists(baseline['source'])
        assert owned.common.core_snapshot()==before;no_active_work();assert host_snapshot('28062da454fd579d6860391a88816dab0bfca143')==hosts
        verified={'revision':revision,'operations':6,'completed':4,'partial':1,'aborted':1,'actualCandidateSigkillCuts':2,
            'actualAttestedHddMetadata':True,'allFullBytesAndInodesOwnersMtimeAttributesAndOtherAclVerified':True,
            'exactContainerAndSourceCleaned':True,'rootAndHddRetained':True,'coreUnchanged':True,
            'installedHelper':False,'signedRpc':False,'businessOrHumanUi':False}
        (evidence/'verified.json').write_text(json.dumps(verified))
        print(json.dumps({'recursiveAclCandidate':'passed','evidence':evidence.name,**verified}),flush=True)
    except BaseException as error:
        (evidence/'failure.json').write_text(json.dumps({'phase':PHASE,'type':type(error).__name__,'completedOperations':len(results)}))
        print(json.dumps({'recursiveAclCandidate':'failed','phase':PHASE,'evidence':evidence.name,'type':type(error).__name__}),flush=True)
        raise


if __name__=='__main__':main()
