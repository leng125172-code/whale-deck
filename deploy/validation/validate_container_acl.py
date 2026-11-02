#!/usr/bin/env python3
"""Frozen internal ACL candidate on one exactly owned container and real HDD.

Not an installed helper, signed RPC, human authorization, business job or UI.
Candidate-only trusted checkpoints exercise real child SIGKILL and recovery.
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
from validate_container_recursive_ownership_browser import no_active_work
from validate_container_recursive_ownership_agent import host_identity

NAMES=('whaledeck-files.py','whaledeck-container-files.py','whaledeck-container-terminal.py','whaledeck-container-upload.py',
       'whaledeck-container-publish.py','whaledeck-file-backups.py','whaledeck-container-directories.py','whaledeck-container-permissions.py','whaledeck-container-acl.py')
PATHS={'acl-file.bin':'File','acl-directory':'Directory','acl-directory/existing.bin':'File'}
PHASE='startup'


def entries(mask=4):
    return [{'tag':'user_obj','id':None,'permissions':6},{'tag':'user','id':2201,'permissions':6},
            {'tag':'group_obj','id':None,'permissions':0},{'tag':'mask','id':None,'permissions':mask},{'tag':'other','id':None,'permissions':0}]


def inventory(folder,reader,module,content):
    result={}
    for relative,kind in PATHS.items():
        path=folder/relative;info=path.lstat();assert not path.is_symlink()
        if kind=='File':
            assert stat.S_ISREG(info.st_mode) and info.st_nlink==1 and path.read_bytes()==content
            assert os.getxattr(path,'user.whaledeck-acl')==b'preserve ordinary attribute'
            assert set(os.listxattr(path))<= {'user.whaledeck-acl',*module.XATTRS.values()}
        else:assert stat.S_ISDIR(info.st_mode) and {p.name for p in path.iterdir()}=={'existing.bin'}
        fd=os.open(path,os.O_RDONLY|os.O_NOFOLLOW)
        try:result[relative]={'identity':list(reader.fingerprint(info)),'access':module.read_acl(fd,'access',info.st_mode),'default':module.read_acl(fd,'default',info.st_mode)}
        finally:os.close(fd)
    return result


def unchanged_except_target(before,after,target,mode):
    assert before.keys()==after.keys()==PATHS.keys()
    for relative,old in before.items():
        current=after[relative]
        if relative!=target:assert current==old;continue
        a,b=old['identity'],current['identity'];assert a[:2]==b[:2] and a[3:8]==b[3:8] and b[2]&0o777==mode
        assert a[2]&~0o777==b[2]&~0o777 and b[8]>=a[8]


def kill_at(engine,request,session,phase):
    pid=os.fork()
    if pid==0:
        engine.checkpoint=lambda point:os.kill(os.getpid(),signal.SIGKILL) if point==phase else None
        try:engine.apply(request,session)
        except BaseException:os._exit(2)
        os._exit(3)
    fd=os.pidfd_open(pid)
    try:
        ready=bool(select.select([fd],[],[],30)[0])
        if not ready:signal.pidfd_send_signal(fd,signal.SIGKILL)
        _,status=os.waitpid(pid,0);assert ready and os.WIFSIGNALED(status) and os.WTERMSIG(status)==signal.SIGKILL,'CANDIDATE_CUT_MISSED'
    finally:os.close(fd)


def main():
    global PHASE
    assert os.geteuid()==0 and len(sys.argv)==2;os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0'
    revision=sys.argv[1];assert re.fullmatch('[a-f0-9]{40}',revision);repo=Path(__file__).resolve().parents[2]
    assert not subprocess.check_output(['git','-C',str(repo),'status','--porcelain']).strip(),'FREEZE_SOURCE_FIRST'
    blobs={n:subprocess.check_output(['git','-C',str(repo),'show',revision+':deploy/host/'+n]) for n in NAMES}
    assert all((repo/'deploy/host'/n).read_bytes()==raw for n,raw in blobs.items())
    assert Path(__file__).read_bytes()==subprocess.check_output(['git','-C',str(repo),'show',revision+':deploy/validation/'+Path(__file__).name])
    no_active_work();core=owned.common.core_snapshot();hosts=host_identity('b1fafedddfa1767d2d573bd94d2748f3cb5e7b44')
    evidence=repo/'deploy/artifacts'/('container-acl-'+uuid.uuid4().hex);evidence.mkdir(mode=0o700)
    state=evidence/'root-state';state.mkdir(mode=0o700);rows=[]
    print(json.dumps({'candidateAcl':'started','evidence':str(evidence)}),flush=True)
    try:
        with tempfile.TemporaryDirectory(prefix='wd-native-acl-') as tmp:
            bundle=Path(tmp)
            for n,raw in blobs.items():p=bundle/n;p.write_bytes(raw);p.chmod(0o750)
            reader=load(bundle/NAMES[1],'acl_native_reader');files=reader.trusted_module(NAMES[0],'acl_native_files');docker=reader.trusted_module(NAMES[2],'acl_native_docker')
            upload=load(bundle/NAMES[3],'acl_native_upload').ContainerUploads(reader,files,state)
            publisher=load(bundle/NAMES[4],'acl_native_publisher').ContainerPublisher(upload,type('UnusedContentBackup',(),{'u':upload})())
            storage=load(bundle/NAMES[5],'acl_native_hdd').FileBackups(files);directories=load(bundle/NAMES[6],'acl_native_directories').ContainerDirectories
            permissions=load(bundle/NAMES[7],'acl_native_permissions');module=load(bundle/NAMES[8],'acl_native_engine')
            history=module.acl_history_type(permissions)(storage);engine=module.container_acl_type(directories)(upload,publisher,state,history)
            with owned.fixture() as fixture:
                folder=fixture['folder'];(folder/'acl-directory').mkdir(mode=0o750)
                content=hashlib.shake_256(('candidate-acl:'+fixture['identifier']).encode()).digest(2*1024**2+7)
                for relative,kind in PATHS.items():
                    p=folder/relative
                    if kind=='File':p.write_bytes(content);os.setxattr(p,'user.whaledeck-acl',b'preserve ordinary attribute')
                    os.chown(p,2100,3100);p.chmod(0o640 if kind=='File' else 0o750)
                for relative in PATHS:os.utime(folder/relative,ns=(1700000000123456789,1700000000123456789))
                inspected=docker.api('GET','/containers/'+fixture['identifier']+'/json');target=docker.target(inspected,fixture['identifier'],docker.registry())
                location={'containerId':fixture['identifier'],'instanceId':target['instanceId']}
                baseline={'revision':revision,**location,'source':str(folder),'coreBefore':core,'hostBefore':hosts,'original':inventory(folder,reader,module,content),
                          'sha256':hashlib.sha256(content).hexdigest().upper(),'bytesPerFile':len(content),'sourceSha256':{n:hashlib.sha256(raw).hexdigest() for n,raw in blobs.items()},'candidateOnly':True}
                with storage.target(create_namespace=False,read_only=True) as (_,fd):baseline['hddDevice']=os.fstat(fd).st_dev
                (evidence/'baseline.json').write_text(json.dumps(baseline))
                operations=[('access','acl-file.bin','access',entries(),None),('noop','acl-file.bin','access',entries(),None),
                            ('mask','acl-file.bin','access',entries(6),None),('base','acl-file.bin','access',module.base_acl(0o600),None),
                            ('default','acl-directory','default',entries(),None),('clear-default','acl-directory','default',[],None),
                            ('cancel','acl-file.bin','access',entries(7),'discard'),('before-syscall','acl-file.bin','access',entries(),'Applying'),
                            ('after-syscall','acl-file.bin','access',entries(),'AclWritten')]
                for label,relative,kind,desired,cut in operations:
                    PHASE=label;before=inventory(folder,reader,module,content);path='work/'+relative
                    command={**location,'path':path,'aclType':kind,'entries':desired,'expectedVersion':reader.version(location,path,(folder/relative).stat())}
                    with reader.ContainerRoot(location,files,docker) as session:
                        request={'operationId':uuid.uuid4().hex,'owner':'A'*64,'plan':engine.plan(command,session)}
                        assert inventory(folder,reader,module,content)==before
                        if cut=='discard':result=engine.discard(request,session)
                        else:
                            if cut:kill_at(engine,request,session,cut);result=engine.inspect(request,session)
                            else:result=engine.apply(request,session)
                        expected='Aborted' if cut in ('discard','Applying') else 'Completed';assert result['state']==expected
                        after=inventory(folder,reader,module,content);mode=request['plan']['targetMode'] if expected=='Completed' else before[relative]['identity'][2]&0o777
                        unchanged_except_target(before,after,relative,mode)
                        if expected=='Completed':
                            assert after[relative][kind]==desired and result['aclConfirmed'] and result['bindingCurrent']
                            assert after[relative]['default' if kind=='access' else 'access']==before[relative]['default' if kind=='access' else 'access']
                        if label in ('noop','cancel','before-syscall'):assert after==before
                        raw=(state/(request['operationId']+'.json')).read_bytes();record=json.loads(raw);hdd=None
                        if record['historyVerified']:
                            with history.target(False) as (_,fd):
                                history.read(fd,request);hdd=storage.read_json(fd,request['operationId']+'.json')
                            assert hdd['plan']==request['plan'] and hdd['resourceType']=='ContainerAclMetadata'
                            (evidence/(request['operationId']+'.hdd.json')).write_text(json.dumps(hdd))
                        for action in (engine.inspect,engine.apply,engine.discard):
                            replay=action(request,session);assert replay['state']==expected and replay['replayed']
                            assert inventory(folder,reader,module,content)==after and (state/(request['operationId']+'.json')).read_bytes()==raw
                        rows.append({'label':label,'request':request,'result':result,'before':before,'after':after,'rootSha256':hashlib.sha256(raw).hexdigest(),'history':hdd is not None})
                        (evidence/'results.json').write_text(json.dumps(rows))
                final=inventory(folder,reader,module,content);(evidence/'physical-final.json').write_text(json.dumps(final))
                (evidence/'getfacl-final.txt').write_text(subprocess.check_output(['/usr/bin/getfacl','--absolute-names','--numeric',str(folder/'acl-file.bin'),str(folder/'acl-directory')],text=True))
                PHASE='proved-cleanup';assert set(p.name for p in folder.iterdir())=={'original.bin','acl-file.bin','acl-directory'}
                for relative in ('acl-directory/existing.bin','acl-file.bin'):
                    assert list(reader.fingerprint((folder/relative).lstat()))==final[relative]['identity'];(folder/relative).unlink()
                p=folder/'acl-directory';assert not list(p.iterdir()) and list(reader.fingerprint(p.lstat()))[:5]==final['acl-directory']['identity'][:5];p.rmdir()
            assert not folder.exists() and owned.common.core_snapshot()==core and host_identity('b1fafedddfa1767d2d573bd94d2748f3cb5e7b44')==hosts
        no_active_work();assert len(rows)==9 and sum(r['history'] for r in rows)==8
        result={'candidateAcl':'passed','operations':9,'completed':7,'aborted':2,'actualSigkillCuts':2,'hddMetadata':8,'allBytesInodesOwnersMtimeOrdinaryAttributesPreserved':True,
                'exactSourceAndContainerRemoved':True,'coreAndHostsUnchanged':True,'candidateOnly':True,'signedRpcOrHumanUi':False,'evidence':str(evidence)}
        (evidence/'verified.json').write_text(json.dumps(result));print(json.dumps(result),flush=True)
    except BaseException as error:
        (evidence/'failure.json').write_text(json.dumps({'phase':PHASE,'type':type(error).__name__,'completedOperations':len(rows)}));raise


if __name__=='__main__':main()
