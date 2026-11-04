#!/usr/bin/env python3
"""Independent read-only actual restore records, HDD and optional live tree."""
import hashlib,json,os,re,subprocess,sys
from pathlib import Path
from validate_recursive_acl_restore_kernel import bundle,SOURCES,metadata_snapshot
from verify_container_recursive_acl import private_json
from validate_container_recursive_acl import inventory,PATHS
from validate_container_recursive_acl_agent import host_identity
from validate_container_file_publishing import common
from recursive_acl_history_probe import root_snapshot
from validate_container_recursive_acl_faults import preservation_snapshot
from validate_container_recursive_acl_browser import no_active_work
from acl_source_apply_missing_proof import source_closure

def verify(name,live=False):
    assert re.fullmatch('recursive-acl-restore-kernel-[a-f0-9]{32}',name)
    repo=Path(__file__).resolve().parents[2];evidence=repo/'deploy/artifacts'/name
    from validate_container_upload_recovery_agent import trusted
    trusted(evidence,True);b=private_json(evidence/'baseline.json');sources=private_json(evidence/'sources.json');cases=private_json(evidence/'cases.json')
    assert len(sources)==2 and [c['label'] for c in cases]==['access-complete','access-noop','default-complete','discard','partial-default-cancel','killed-after-entry','killed-before-entry']
    assert b['validationSourceSha256']==source_closure(repo,b['revision'],seeds=SOURCES)
    for name,sha in b['hostHashes'].items():assert hashlib.sha256(subprocess.check_output(['git','-C',str(repo),'show',b['revision']+':deploy/host/'+name])).hexdigest()==sha
    assert host_identity(b['installedRevision'])==b['hosts'] and common.core_snapshot()=={k:tuple(v) for k,v in b['core'].items()} and root_snapshot(Path('/var/lib/whaledeck-container-directories'))==b['directoryRoot']
    data=hashlib.shake_256(('restore-kernel:'+b['containerId']).encode()).digest(b['bytesPerFile']);assert hashlib.sha256(data).hexdigest()==b['sha256'];hdd_children=0
    with bundle(repo,b['revision'],evidence) as p:
        original={}
        def history(engine,request):
            value=engine.history.load(request)
            with engine.history.target(False) as (_,fd):
                base=Path(os.readlink('/proc/self/fd/'+str(fd)));path=base/(request['operationId']+'.json');assert path.read_bytes()==(evidence/'hdd'/engine.history.NAMESPACE/path.name).read_bytes()
            return value
        for source in sources:
            request=source['request'];p.engine.request(request);record=private_json(p.engine.state_path/(request['operationId']+'.json'));p.engine.validate_record(record,request['operationId']);saved=history(p.engine,request);p.engine.validate_manifest(request,saved)
            assert record['state']==source['reply']['state']=='Completed' and saved['historyDigest']==source['reply']['historyDigest'];original[request['operationId']]=saved
        for case in cases:
            request=case['request'];p.restore.request(request);record=private_json(p.restore.state_path/(request['operationId']+'.json'));p.restore.validate_record(record,request['operationId']);plan=request['plan'];before=case['before'];after=case['after'];reply=case['reply'];source=original[plan['command']['captureId']]
            assert plan['restoreSource']==p.restore.sources.summary(source) and plan['rootIdentity']==before['']['identity']
            with p.restore.sources.namespace() as (_,_,fd):assert p.restore.sources.frame(os.stat(source['operationId']+'.json',dir_fd=fd,follow_symlinks=False))==plan['restoreSourceFrame']
            assert record['state']==reply['state'] and record['plan']==plan
            for key in ('completedCount','changedCount','abortedCount','notStartedCount','unknownCount','historyVerified','historyDigest','outcomeDigest'):assert record[key]==reply[key]
            if not record['historyVerified']:
                assert case['label']=='discard' and record['state']=='Aborted' and before==after;continue
            saved=history(p.restore,request);entries=p.restore.validate_manifest(request,saved);assert saved['historyDigest']==record['historyDigest'];source_entries={e['relativePath']:e['plan'] for e in source['entries']}
            state_fd=p.files.trusted_directory(p.restore.state_path)
            try:
                with p.restore.child_engine(state_fd,request,False) as (child,_):
                    for index,entry in enumerate(entries):
                        relative=entry['relativePath'];node=entry['plan'];old=before[relative];actual=after[relative];historical=source_entries[relative];kind=plan['restoreSource']['changedAclType'];selected='access' if PATHS[relative]=='File' and kind=='default' else kind
                        target=old['access'] if PATHS[relative]=='File' and kind=='default' else historical['accessAcl' if selected=='access' else 'defaultAcl']
                        assert node['sourceIdentity']==old['identity'] and node['accessAcl']==old['access'] and node['defaultAcl']==old['default'] and node['command']['aclType']==selected and node['command']['entries']==target
                        assert actual['identity'][:2]==old['identity'][:2] and actual['identity'][3:8]==old['identity'][3:8]
                        assert actual['default' if selected=='access' else 'access']==old['default' if selected=='access' else 'access']
                        sub=p.restore.child_request(request,index,entry);path=child.state_path/(sub['operationId']+'.json')
                        if path.exists():
                            cr=private_json(path);child.validate_record(cr,sub['operationId']);assert cr['plan']==node
                            if cr['state']=='Completed':assert actual['access' if selected=='access' else 'default']==target
                            if cr['historyVerified']:
                                ch=child.history.load(sub);assert ch['historyDigest']==cr['historyDigest'] and ch['plan']==node;hdd_children+=1
                                with child.history.target(False) as (_,fd):
                                    raw=(Path(os.readlink('/proc/self/fd/'+str(fd)))/(sub['operationId']+'.json')).read_bytes();assert raw==(evidence/'hdd'/p.restore.history.NAMESPACE/request['operationId']/'entries'/(sub['operationId']+'.json')).read_bytes()
                        else:assert actual==old
            finally:os.close(state_fd)
        assert root_snapshot(evidence/'source-state')==private_json(evidence/'source-state-snapshot.json') and root_snapshot(evidence/'restore-state')==private_json(evidence/'restore-state-snapshot.json') and metadata_snapshot(p)==private_json(evidence/'restore-hdd-snapshot.json')
        protected=preservation_snapshot(p.storage);assert all(protected.get(n)==v for n,v in b['protectedFiles'].items());no_active_work()
        source=Path(b['source']);assert source.parent==common.APPS and source.name==b['containerName'] and re.fullmatch('wdv-container-publish-[a-f0-9]{32}',source.name)
        if live:assert inventory(Path(b['root']),p.reader,data,p.acl)==private_json(evidence/'physical-final.json')
        else:
            assert not os.path.lexists(source);missing=subprocess.run(['docker','inspect',b['containerId']],capture_output=True);assert missing.returncode!=0 and b'no such' in missing.stderr.lower();assert private_json(evidence/'verified.json')['candidateOnly'] is True and private_json(evidence/'final-protected.json')==protected
    return dict(candidateOnly=True,installedOrRpcOrUi=False,cases=7,sources=2,restoreChildHdd=hdd_children,livePhysicalReread=live,sourceBytesRereadAfterCleanup=False,sourceAndContainerAbsent=not live,oldProtectedFilesPreserved=len(b['protectedFiles']),coreUnchanged=11)

if __name__=='__main__':
    try:
        assert os.geteuid()==0 and (len(sys.argv)==2 or len(sys.argv)==3 and sys.argv[2]=='--live');os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0';live=len(sys.argv)==3;value=verify(sys.argv[1],live)
        p=Path(__file__).resolve().parents[2]/'deploy/artifacts'/sys.argv[1]/('live-independent.json' if live else 'final-independent.json')
        with p.open('x') as stream:json.dump(value,stream)
        print(json.dumps(dict(recursiveRestoreIndependent='passed',**value)))
    except BaseException as error:
        import traceback
        f=traceback.extract_tb(error.__traceback__)[-1];print(json.dumps(dict(recursiveRestoreIndependent='failed',type=type(error).__name__,file=Path(f.filename).name,line=f.lineno)));raise SystemExit(1) from None
