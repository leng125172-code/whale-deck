#!/usr/bin/env python3
"""Frozen internal restore kernel on an owned container and attested actual HDD.

No install/RPC/business/UI grant. Original namespaces and services are preserved.
"""
from contextlib import contextmanager
import hashlib,json,os,re,subprocess,sys,uuid
from pathlib import Path
from unittest.mock import patch
import validate_container_file_publishing as owned
from validate_container_file_staging import load
from recursive_acl_history_probe import engines,root_snapshot
from validate_container_recursive_acl import PATHS,inventory,remove_proved,kill_at,file_acl,directory_acl
from validate_container_recursive_acl_agent import host_identity
from validate_container_recursive_acl_browser import no_active_work
from validate_container_recursive_acl_faults import preservation_snapshot
from acl_source_apply_missing_proof import source_closure

PHASE='startup'
MODULE='whaledeck-container-recursive-acl-restore.py'
SOURCES=('validate_recursive_acl_restore_kernel.py','verify_recursive_acl_restore_kernel.py','test_container_file_recursive_acl_restore.py')

@contextmanager
def bundle(repo,revision,evidence):
    with engines(repo,revision,evidence/'source-state') as p:
        folder=Path(p.reader.__file__).parent;raw=subprocess.check_output(['git','-C',str(repo),'show',revision+':deploy/host/'+MODULE]);assert raw==(repo/'deploy/host'/MODULE).read_bytes();(folder/MODULE).write_bytes(raw);(folder/MODULE).chmod(0o750)
        module=load(folder/MODULE,'native_restore_kernel');recursive=load(folder/'whaledeck-container-recursive-permissions.py','native_restore_shared');permission=load(folder/'whaledeck-container-permissions.py','native_restore_permission');directory=load(folder/'whaledeck-container-directories.py','native_restore_directory');profile=load(folder/'whaledeck-container-recursive-acl.py','native_restore_profile')
        history=module.recursive_acl_restore_history_type(recursive)(p.storage);typ=module.recursive_acl_restore_type(p.acl,permission,directory.ContainerDirectories,recursive,profile,type(p.catalog));restore=typ(p.engine.u,p.engine.p,evidence/'restore-state',history)
        p.restore=restore;p.restoreModule=module;p.hashes[MODULE]=hashlib.sha256(raw).hexdigest();yield p

def metadata_snapshot(p):
    with p.storage.target(create_namespace=False,read_only=True) as (cfg,fd):
        p.storage.attest(cfg,fd,read_only=True);path=Path(os.readlink('/proc/self/fd/'+str(fd)))/p.restore.history.NAMESPACE
        return root_snapshot(path) if path.exists() else {}

def archive_hdd(evidence,p,request,restore):
    engine=p.restore if restore else p.engine;record=json.loads((engine.state_path/(request['operationId']+'.json')).read_bytes())
    if not record['historyVerified']:return
    with engine.history.target(False) as (_,fd):
        base=Path(os.readlink('/proc/self/fd/'+str(fd)));paths=[base/(request['operationId']+'.json')]
        child=base/request['operationId']/'entries'
        if child.exists():paths.extend(child.glob('*.json'))
        for path in paths:
            relative=path.relative_to(base);target=evidence/'hdd'/engine.history.NAMESPACE/relative;target.parent.mkdir(mode=0o700,parents=True,exist_ok=True);raw=path.read_bytes()
            if target.exists():assert target.read_bytes()==raw
            else:
                with target.open('xb') as f:f.write(raw)

def main():
    global PHASE
    assert os.geteuid()==0 and len(sys.argv)==2;os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0';repo=Path(__file__).resolve().parents[2];revision=sys.argv[1];assert re.fullmatch('[a-f0-9]{40}',revision)
    assert subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip()==revision and not subprocess.check_output(['git','status','--porcelain']).strip(),'FREEZE_RESTORE_KERNEL_FIRST'
    hashes=source_closure(repo,revision,seeds=SOURCES);assert all(hashlib.sha256((repo/'deploy/validation'/n).read_bytes()).hexdigest()==sha for n,sha in hashes.items())
    evidence=repo/'deploy/artifacts'/('recursive-acl-restore-kernel-'+uuid.uuid4().hex);evidence.mkdir(mode=0o700)
    for name in ('source-state','restore-state'):(evidence/name).mkdir(mode=0o700)
    print(json.dumps(dict(recursiveAclRestoreKernel='started',evidence=evidence.name)),flush=True)
    try:
        with bundle(repo,revision,evidence) as p:
            old=preservation_snapshot(p.storage);hosts=host_identity('390889dad8c6cb42a881b5b05f6c70b5fda59c09');core=owned.common.core_snapshot();directories=root_snapshot(Path('/var/lib/whaledeck-container-directories'));no_active_work()
            with owned.fixture() as fixture:
                root=fixture['folder']/'recursive-tree';root.mkdir(mode=0o750);(root/'sub').mkdir(mode=0o700);data=hashlib.shake_256(('restore-kernel:'+fixture['identifier']).encode()).digest(65536+7)
                for index,(relative,kind) in enumerate(PATHS.items()):
                    path=root/relative
                    if kind=='File':path.write_bytes(data)
                    os.chown(path,2100+index,3100+index);path.chmod(0o750 if kind=='Directory' else 0o600 if '/' in relative else 0o640);os.setxattr(path,'user.whaledeck-recursive',b'preserve ordinary attribute')
                def write(relative,kind,entries):
                    fd=os.open(root/relative,os.O_RDONLY|os.O_NOFOLLOW)
                    try:p.acl.write_acl(fd,kind,entries)
                    finally:os.close(fd)
                named=file_acl(2);named[1].update(id=0,permissions=0);write('one.bin','access',named);write('sub/中文.bin','access',p.acl.base_acl(0o600));write('sub','default',file_acl(4))
                for relative in PATHS:os.utime(root/relative,ns=(1700000000123456789,1700000000123456789))
                inspected=p.docker.api('GET','/containers/'+fixture['identifier']+'/json');target=p.docker.target(inspected,fixture['identifier'],p.docker.registry());location=dict(containerId=fixture['identifier'],instanceId=target['instanceId'])
                b=dict(revision=revision,validationSourceRevision=revision,validationSourceSha256=hashes,hostHashes=p.hashes,installedRevision='390889dad8c6cb42a881b5b05f6c70b5fda59c09',hosts=hosts,core=core,directoryRoot=directories,protectedFiles=old,**location,containerName=inspected['Name'].lstrip('/'),source=str(fixture['folder']),root=str(root),bytesPerFile=len(data),sha256=hashlib.sha256(data).hexdigest(),sourceDevice=root.stat().st_dev)
                with p.storage.target(create_namespace=False,read_only=True) as (_,fd):b['hddDevice']=os.fstat(fd).st_dev
                (evidence/'baseline.json').write_text(json.dumps(b));sources=[];cases=[]
                with p.reader.ContainerRoot(location,p.files,p.docker) as session:
                    def capture(kind):
                        before=inventory(root,p.reader,data,p.acl);command={**location,'path':'work/recursive-tree','expectedVersion':p.reader.version(location,'work/recursive-tree',root.stat()),'aclType':kind,'fileEntries':file_acl() if kind=='access' else None,'directoryEntries':directory_acl() if kind=='access' else file_acl(6)}
                        request=dict(operationId=uuid.uuid4().hex,owner='B'*64,plan=p.engine.plan(command,session));reply=p.engine.apply(request,session);assert reply['state']=='Completed';sources.append(dict(request=request,reply=reply,before=before,after=inventory(root,p.reader,data,p.acl)));archive_hdd(evidence,p,request,False);(evidence/'sources.json').write_text(json.dumps(sources));return sources[-1]
                    def restore(label,source,mode='apply'):
                        global PHASE
                        PHASE=label;before=inventory(root,p.reader,data,p.acl);command={**location,'path':'work/recursive-tree','expectedVersion':p.reader.version(location,'work/recursive-tree',root.stat()),'captureId':source['request']['operationId'],'expectedHistoryDigest':source['reply']['historyDigest']}
                        request=dict(operationId=uuid.uuid4().hex,owner='B'*64,plan=p.restore.plan(command,session));p.restore.cancelled=None;p.restore.checkpoint=lambda phase,index:None
                        if mode=='discard':reply=p.restore.discard(request,session)
                        elif mode.startswith('kill-'):
                            kill_at(p.restore,request,session,'AfterEntry' if mode=='kill-after' else 'BeforeEntry');reply=p.restore.inspect(request,session)
                        elif mode=='cancel':
                            flag=[False];p.restore.cancelled=lambda:flag[0];p.restore.checkpoint=lambda phase,index:flag.__setitem__(0,True) if phase=='AfterEntry' and index==2 else None
                            try:p.restore.apply(request,session)
                            except p.reader.Rejected:pass
                            else:raise AssertionError('CANCEL_BOUNDARY_NOT_REACHED')
                            reply=p.restore.inspect(request,session)
                        else:reply=p.restore.apply(request,session)
                        p.restore.cancelled=None;after=inventory(root,p.reader,data,p.acl);raw=(p.restore.state_path/(request['operationId']+'.json')).read_bytes()
                        with patch.object(p.acl,'write_acl',side_effect=AssertionError('late restore must never replay')):late=p.restore.apply(request,session)
                        assert late['state']==reply['state'] and inventory(root,p.reader,data,p.acl)==after and (p.restore.state_path/(request['operationId']+'.json')).read_bytes()==raw
                        expected='Aborted' if mode in ('discard','kill-before') else 'Partial' if mode in ('cancel','kill-after') else 'Completed';assert reply['state']==expected
                        if expected=='Completed':
                            kind=source['request']['plan']['command']['aclType']
                            for rel in PATHS:
                                assert after[rel]['access' if kind=='access' else 'default']==source['before'][rel]['access' if kind=='access' else 'default']
                        cases.append(dict(label=label,request=request,reply=reply,before=before,after=after,mode=mode));archive_hdd(evidence,p,request,True);(evidence/'cases.json').write_text(json.dumps(cases))
                    access=capture('access');restore('access-complete',access);assert 'system.posix_acl_access' not in os.listxattr(root/'sub/中文.bin');restore('access-noop',access)
                    write('','default',[]);write('sub','default',named);default=capture('default');restore('default-complete',default);restore('discard',default,'discard')
                    for rel in ('','sub'):write(rel,'default',file_acl(6))
                    restore('partial-default-cancel',default,'cancel')
                    for rel,kind in PATHS.items():write(rel,'access',file_acl() if kind=='File' else directory_acl())
                    restore('killed-after-entry',access,'kill-after')
                    for rel,kind in PATHS.items():write(rel,'access',file_acl() if kind=='File' else directory_acl())
                    restore('killed-before-entry',access,'kill-before')
                final=inventory(root,p.reader,data,p.acl);(evidence/'physical-final.json').write_text(json.dumps(final));(evidence/'restore-hdd-snapshot.json').write_text(json.dumps(metadata_snapshot(p)))
                (evidence/'source-state-snapshot.json').write_text(json.dumps(root_snapshot(evidence/'source-state')));(evidence/'restore-state-snapshot.json').write_text(json.dumps(root_snapshot(evidence/'restore-state')))
                PHASE='independent-live-before-cleanup'
                with (evidence/'independent-live.log').open('x') as stream:subprocess.run([sys.executable,str(repo/'deploy/validation/verify_recursive_acl_restore_kernel.py'),evidence.name,'--live'],stdout=stream,stderr=subprocess.STDOUT,check=True,timeout=120)
                assert inventory(root,p.reader,data,p.acl)==final;remove_proved(root,p.reader,data,final,p.acl)
            protected=preservation_snapshot(p.storage);assert all(protected.get(n)==v for n,v in old.items()) and owned.common.core_snapshot()==core and host_identity(b['installedRevision'])==hosts and root_snapshot(Path('/var/lib/whaledeck-container-directories'))==directories;no_active_work()
            (evidence/'final-protected.json').write_text(json.dumps(protected));(evidence/'verified.json').write_text(json.dumps(dict(candidateOnly=True,installedOrRpcOrUi=False,cases=7,normalSourceCaptures=2,exactSourceAndContainerAbsent=True,oldProtectedFilesPreserved=len(old))))
        PHASE='independent-final-after-cleanup'
        with (evidence/'independent-final.log').open('x') as stream:subprocess.run([sys.executable,str(repo/'deploy/validation/verify_recursive_acl_restore_kernel.py'),evidence.name],stdout=stream,stderr=subprocess.STDOUT,check=True,timeout=120)
        print(json.dumps(dict(recursiveAclRestoreKernel='passed',evidence=evidence.name,cases=7)),flush=True)
    except BaseException as error:
        import traceback
        f=traceback.extract_tb(error.__traceback__)[-1];(evidence/'failure.json').write_text(json.dumps(dict(phase=PHASE,type=type(error).__name__,file=Path(f.filename).name,line=f.lineno)));print(json.dumps(dict(recursiveAclRestoreKernel='failed',phase=PHASE,evidence=evidence.name,type=type(error).__name__)),flush=True);raise SystemExit(1) from None

if __name__=='__main__':main()
