#!/usr/bin/env python3
"""Real candidate history reads; no installation, RPC, business job or UI claim."""
import hashlib,json,os,re,subprocess,sys,uuid
from pathlib import Path
from unittest.mock import patch

import validate_container_file_publishing as owned
from validate_container_recursive_acl import PATHS,fixture_bytes,inventory,remove_proved,file_acl,directory_acl,kill_at
from validate_container_recursive_acl_partial import create_tree,no_active_work,preflight,host_identity,preservation_snapshot
from acl_source_apply_missing_proof import source_closure
from recursive_acl_history_probe import engines,source_request,verify_page,root_snapshot,NAMES

SOURCES=('validate_container_recursive_acl_history.py','verify_container_recursive_acl_history.py','recursive_acl_history_probe.py','test_container_recursive_acl_history.py')
PHASE='startup'


def save(p,name,value):
    (p/name).open('x').write(json.dumps(value,ensure_ascii=False))


def main():
    global PHASE
    assert os.geteuid()==0 and len(sys.argv)==3;source,runtime=sys.argv[1:];assert all(re.fullmatch('[a-f0-9]{40}',v) for v in (source,runtime));os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0'
    repo=Path(__file__).resolve().parents[2];assert subprocess.check_output(['git','-C',str(repo),'rev-parse','HEAD'],text=True).strip()==source and not subprocess.check_output(['git','-C',str(repo),'status','--porcelain']).strip()
    hashes=source_closure(repo,source,seeds=SOURCES);assert all(hashlib.sha256((repo/'deploy/validation'/n).read_bytes()).hexdigest()==sha for n,sha in hashes.items())
    no_active_work();preflight();hosts=host_identity(runtime);core=owned.common.core_snapshot();p=repo/'deploy/artifacts'/('container-recursive-acl-history-'+uuid.uuid4().hex);p.mkdir(mode=0o700);state=p/'root-state';state.mkdir(mode=0o700)
    print(json.dumps(dict(recursiveAclHistoryCandidate='started',evidence=p.name)),flush=True)
    with engines(repo,source,state) as e:
        installed={}
        for n in NAMES[:-1]:
            path=Path('/usr/local/libexec')/n;raw=subprocess.check_output(['git','-C',str(repo),'show',runtime+':deploy/host/'+n]);assert path.read_bytes()==raw;installed[n]=hashlib.sha256(raw).hexdigest()
        assert not Path('/usr/local/libexec/'+NAMES[-1]).exists()
        old=preservation_snapshot(e.storage);save(p,'scope.json',dict(sourceRevision=source,runtimeRevision=runtime,candidateOnly=True,sourceHashes=e.hashes,validationSourceSha256=hashes,installedSourceHashes=installed,hostIdentity=hosts,coreBefore=core,preservedFiles=old))
        proved=False
        with owned.fixture() as fixture:
            root=fixture['folder']/'recursive-tree';content=fixture_bytes(fixture['identifier']);create_tree(root,content)
            # Heterogeneous historical entries, including an explicit numeric root ACL.
            for relative,uid in (('one.bin',2202),('sub/中文.bin',0)):
                fd=os.open(root/relative,os.O_RDONLY)
                try:
                    entries=file_acl(6);entries[1]['id']=uid;e.acl.write_acl(fd,'access',entries)
                finally:os.close(fd)
            for relative in ('','sub'):
                fd=os.open(root/relative,os.O_RDONLY|os.O_DIRECTORY)
                try:e.acl.write_acl(fd,'default',directory_acl())
                finally:os.close(fd)
            observed=e.docker.api('GET','/containers/'+fixture['identifier']+'/json');target=e.docker.target(observed,fixture['identifier'],e.docker.registry());location=dict(containerId=fixture['identifier'],instanceId=target['instanceId'])
            b=dict(**location,containerName=fixture['folder'].name,source=str(fixture['folder']),bytesPerFile=len(content),sha256=hashlib.sha256(content).hexdigest().upper(),original=inventory(root,e.reader,content,e.acl))
            with e.storage.target(create_namespace=False,read_only=True) as (_,fd):b.update(sourceDevice=root.stat().st_dev,hddDevice=os.fstat(fd).st_dev)
            save(p,'baseline.json',b);cases=[]
            try:
                with e.reader.ContainerRoot(location,e.files,e.docker) as session:
                    for label,kind,files,dirs,cut in (('access','access',file_acl(),directory_acl(),False),('default','default',None,file_acl(6),False),('clear','default',None,[],False),('partial','access',file_acl(6),directory_acl(),True)):
                        PHASE='capture-'+label;before=inventory(root,e.reader,content,e.acl)
                        command={**location,'path':'work/recursive-tree','expectedVersion':e.reader.version(location,'work/recursive-tree',root.stat()),'aclType':kind,'fileEntries':files,'directoryEntries':dirs}
                        request=dict(operationId=uuid.uuid4().hex,owner='B'*64,plan=e.engine.plan(command,session))
                        if cut:kill_at(e.engine,request,session,'AfterEntry');receipt=e.engine.inspect(request,session);assert receipt['state']=='Partial'
                        else:receipt=e.engine.apply(request,session);assert receipt['state']=='Completed'
                        after=inventory(root,e.reader,content,e.acl);cases.append(dict(label=label,request=request,receipt=receipt,before=before,after=after))
                    save(p,'cases.json',cases);final=inventory(root,e.reader,content,e.acl);save(p,'physical-before-read.json',final);pre_read=preservation_snapshot(e.storage)
                    root_before=root_snapshot(state);save(p,'root-before-read.json',root_before)
                    PHASE='readonly-catalog';pages=[];read_pages=[]
                    def forbidden(*args,**kwargs):raise AssertionError('HISTORY_READER_MUST_NOT_WRITE')
                    with patch.object(e.acl,'write_acl',side_effect=forbidden),patch.object(e.files,'save_receipt',side_effect=forbidden):
                        after='';version='';found=[]
                        while True:
                            page=e.catalog.listing({**location,'path':'work/recursive-tree','after':after,'take':1,'expectedVersion':version},session);pages.append(page);found.extend(v['captureId'] for v in page['entries']);version=page['version']
                            if page['complete']:break
                            assert page['nextAfter'] and page['nextAfter']>after;after=page['nextAfter'];assert len(pages)<100
                        assert found==sorted(c['request']['operationId'] for c in cases)
                        for case in cases:
                            offset=0;actual=[]
                            while True:
                                page=e.catalog.read(source_request(b,case,offset=offset,take=1),session);offset=verify_page(page,b,case,offset);actual.append(page)
                                if page['complete']:break
                            read_pages.append(actual)
                    assert inventory(root,e.reader,content,e.acl)==final and preservation_snapshot(e.storage)==pre_read and root_snapshot(state)==root_before
                    save(p,'listing-pages.json',pages);save(p,'entry-pages.json',read_pages);save(p,'read-preservation.json',pre_read)
                    PHASE='independent-live-read'
                    result=subprocess.run([sys.executable,str(repo/'deploy/validation/verify_container_recursive_acl_history.py'),p.name],capture_output=True,text=True,timeout=100)
                    (p/'independent.stdout').write_text(result.stdout);(p/'independent.stderr').write_text(result.stderr);assert result.returncode==0,'INDEPENDENT_LIVE_CATALOG_FAILED'
                    assert inventory(root,e.reader,content,e.acl)==final and preservation_snapshot(e.storage)==pre_read and root_snapshot(state)==root_before
                    proved=True
            finally:
                if proved:remove_proved(root,e.reader,content,final,e.acl)
        no_active_work();preflight();assert host_identity(runtime)==hosts and owned.common.core_snapshot()==core
        now=preservation_snapshot(e.storage);assert all(now.get(k)==v for k,v in old.items())
        save(p,'verified.json',dict(candidateOnly=True,actualHddAndPinnedContainer=True,captures=4,completedCaptures=3,partialCaptures=1,listingPages=len(pages),entryPages=sum(map(len,read_pages)),readerWrites=False,oldProtectedFilesUnchanged=len(old),exactSourceAndContainerAbsent=True,installedRpcOrBusinessOrUiAccepted=False))
        print(json.dumps(dict(recursiveAclHistoryCandidate='passed',evidence=p.name,captures=4,entryPages=sum(map(len,read_pages)))),flush=True)


if __name__=='__main__':
    try:main()
    except BaseException as error:
        import traceback
        frames=[dict(file=Path(f.filename).name,line=f.lineno) for f in traceback.extract_tb(error.__traceback__) if '/deploy/' in f.filename]
        print(json.dumps(dict(recursiveAclHistoryCandidate='failed',phase=PHASE,failureType=type(error).__name__,locations=frames)),flush=True);raise SystemExit(1) from None
