#!/usr/bin/env python3
"""Independent read-only physical acquisition, before OR after explicit retirement.

Unlike the original aging observer, after acquisition does not assume .data
still exists. It does not declare success; the acceptance proof checks delta.
"""
import hashlib,json,os,re,subprocess,sys
from pathlib import Path
from acl_source_apply_missing_proof import source_closure
from acl_history_browser_proof import typed_equal
from recursive_acl_restore_agent_io import read_at,snapshot
from validate_container_recycle_archive_copies import NAMES,VALIDATION_SEEDS
from validate_container_recycle_archive_plan import payload_snapshot
from validate_container_recycle_archive_agent import baseline
from validate_container_recycle_archive_browser import old_rows,idle
from verify_container_recycle_archive_copies import retirement_namespace_reread,retirement_namespace_proof
from verify_container_recycle_archive_aging import configure_observer_git,aging_container_proof
from validate_container_recycle_archive_readiness import frozen_engine
from validate_container_file_staging import load
import validate_container_file_publishing as owned


def frozen_capture(e):
    repo=Path(__file__).resolve().parents[2];configure_observer_git(repo)
    assert e.resolve(strict=True)==e and (e.stat().st_uid,e.stat().st_gid,e.stat().st_mode&0o777)==(0,0,0o700)
    source=read_at(None,str(e/'source.json'))[1];sha=source['validationRevision'];assert re.fullmatch('[a-f0-9]{40}',sha)
    git=lambda n:subprocess.check_output(['git','-C',str(repo),'show',sha+':'+n])
    expected={n:hashlib.sha256(git('deploy/host/'+n)).hexdigest() for n in NAMES}
    typed_equal(source['sourceSha256'],expected);typed_equal(source['validationSourceSha256'],source_closure(repo,sha,seeds=VALIDATION_SEEDS))
    for folder,prefix,key in (('source','deploy/host/','sourceSha256'),('validation','deploy/validation/','validationSourceSha256')):
        for name,digest in source[key].items():
            assert re.fullmatch(r'[A-Za-z0-9_.-]+\.(py|mjs)',name)
            raw=(e/folder/name).read_bytes();assert hashlib.sha256(raw).hexdigest()==digest and raw==git(prefix+name) and raw==(repo/prefix/name).read_bytes()
    return source


def parent_identity_proof(folder,fixture,request):
    assert folder.is_absolute() and folder.resolve(strict=True)==folder
    info=folder.stat();identity=[info.st_dev,info.st_ino]
    typed_equal(identity,fixture['parentIdentity'])
    typed_equal(identity,request['plan']['command']['archive']['plan']['parentIdentity'])
    assert (info.st_uid,info.st_gid,info.st_mode&0o777)==(0,0,0o755)


def collect(name):
    assert os.geteuid()==0 and re.fullmatch('container-recycle-archive-copies-[a-f0-9]{32}',name)
    e=Path(__file__).resolve().parents[2]/'deploy/artifacts'/name;source=frozen_capture(e)
    read=lambda n:read_at(None,str(e/n))[1];handoff=read('aging-handoff.json');q=handoff['request'];fixture=read('fixture-initial.json');copied=read('archive-copy.json')
    assert q['owner']=='8'*64 and q['plan']['command']['retainDays']==1
    engine,_,_,_=frozen_engine(e);ref=engine.ref(q);folder=Path(fixture['folder'])
    container=json.loads(owned.common.command(['docker','inspect',fixture['containerId']]))[0];owned.verify_container(container,fixture['containerId'],fixture['containerName'],folder)
    import time
    aging_container_proof(container,q,handoff['preparedAtUnixMs'],time.time_ns()//1000000)
    parent_identity_proof(folder,fixture,q)
    namespace=retirement_namespace_reread(folder);retirement_namespace_proof(namespace,fixture,copied)
    with engine.source.history.target(False) as (_,fd):hdd=payload_snapshot(Path(os.readlink('/proc/self/fd/'+str(fd))))
    roots=snapshot(e/'retirement-state');protected={n:snapshot(e/n) for n in ('source-state','purge-state','restore-state','archive-state')}
    journal=read('retirement-state/'+q['operationId']+'.json');marker=read('retirement-state/claims/'+ref['operationId']+'.json')
    observer=load(e/'source'/NAMES[18],'completion_physical_history').ContainerRecycleRetiredHistory(engine)
    observation=observer.observe(ref,q['plan']['command']['archive']['plan']['command']['historyDigest'])
    global_state=baseline(source['revision']);business=old_rows('0'*64);idle()
    typed_equal(roots,snapshot(e/'retirement-state'));typed_equal(protected,{n:snapshot(e/n) for n in protected});typed_equal(namespace,retirement_namespace_reread(folder))
    with engine.source.history.target(False) as (_,fd):typed_equal(hdd,payload_snapshot(Path(os.readlink('/proc/self/fd/'+str(fd)))))
    typed_equal(global_state,baseline(source['revision']));typed_equal(business,old_rows('0'*64))
    current=json.loads(owned.common.command(['docker','inspect',fixture['containerId']]))[0]
    owned.verify_container(current,fixture['containerId'],fixture['containerName'],folder)
    aging_container_proof(current,q,handoff['preparedAtUnixMs'],time.time_ns()//1000000)
    parent_identity_proof(folder,fixture,q)
    return dict(request=q,roots=roots,hdd=hdd,protected=protected,namespace=namespace,journal=journal,marker=marker,observation=observation,baseline=global_state,business=business)


if __name__=='__main__':
    try:assert len(sys.argv)==2;print(json.dumps(collect(sys.argv[1])))
    except BaseException as error:print(json.dumps(dict(collection='failed',type=type(error).__name__)));raise SystemExit(1) from None
