#!/usr/bin/env python3
"""Explicit owned-aging acceptance; defaults to observation, never a scheduler.

Execution needs --execute-owned-retirement, natural due readiness, independent
physical before evidence and native fresh guards. Errors preserve the fixture;
no automatic retry, rollback or cleanup is provided.
"""
import json,os,re,subprocess,sys,time,uuid
from pathlib import Path
from acl_history_browser_proof import typed_equal
from recursive_acl_restore_agent_io import read_at
from validate_container_recycle_archive_readiness import frozen_engine,inspect_readiness,readiness_proof
from verify_container_recycle_archive_aging import configure_observer_git
from archive_retirement_completion_proof import completion_acceptance_proof


def may_execute(readiness,requested):
    assert type(requested) is bool
    readiness_proof(readiness)
    assert readiness['retirementExecuted'] is False
    if readiness['status']=='NotDue':return False
    assert readiness['status']=='ReadyForExplicitAcceptance' and readiness['currentPlan']['retirementEligible'] is True
    return requested


def completion_bundle(before,after,ready,receipt,clock_before,clock_after):
    typed_equal(before['request'],after['request']);q=before['request'];source=q['plan']['command']['archive']['plan']['command']['purge']['plan']['command']['source']['operationId'];data=source+'.data'
    typed_equal(before['business'],after['business'])
    b=before['baseline'];a=after['baseline'];typed_equal({k:v for k,v in b.items() if k!='recycleHdd'},{k:v for k,v in a.items() if k!='recycleHdd'})
    assert set(b['recycleHdd'])-set(a['recycleHdd'])=={data} and not set(a['recycleHdd'])-set(b['recycleHdd'])
    for name in a['recycleHdd']:typed_equal(b['recycleHdd'][name],a['recycleHdd'][name])
    value=dict(request=q,readiness=ready,clockBeforeUnixMs=clock_before,clockAfterUnixMs=clock_after,journalBefore=before['journal'],journalAfter=after['journal'],markerBefore=before['marker'],markerAfter=after['marker'],rootsBefore=before['roots'],rootsAfter=after['roots'],hddBefore=before['hdd'],hddAfter=after['hdd'],protectedBefore=before['protected'],protectedAfter=after['protected'],namespaceBefore=before['namespace'],namespaceAfter=after['namespace'],receipt=receipt,observation=after['observation'])
    independent={k:before[k] for k in ('request','roots','hdd','protected','namespace')}
    completion_acceptance_proof(value,independent);return value


def main(name,execute=False):
    assert os.geteuid()==0 and re.fullmatch('container-recycle-archive-copies-[a-f0-9]{32}',name);os.umask(0o077)
    repo=Path(__file__).resolve().parents[2];configure_observer_git(repo);e=repo/'deploy/artifacts'/name
    revision=subprocess.check_output(['git','-C',str(repo),'rev-parse','HEAD'],text=True).strip();assert re.fullmatch('[a-f0-9]{40}',revision)
    sources={}
    for filename in (Path(__file__).name,'collect_container_recycle_archive_completion.py','archive_retirement_completion_proof.py','validate_container_recycle_archive_readiness.py'):
        raw=(repo/'deploy/validation'/filename).read_bytes();assert raw==subprocess.check_output(['git','-C',str(repo),'show',revision+':deploy/validation/'+filename]);sources[filename]=raw
    # Capture success/failure receipts outside the immutable original evidence.
    out=repo/'deploy/artifacts'/('archive-retirement-acceptance-'+uuid.uuid4().hex);out.mkdir(mode=0o700)
    def save(n,v):
        with (out/n).open('x') as f:json.dump(v,f)
    def child(script,label):
        with (out/(label+'.json')).open('x') as log:r=subprocess.run([sys.executable,str(repo/'deploy/validation'/script),name],stdout=log,stderr=subprocess.STDOUT,timeout=240)
        assert r.returncode==0,label
        return read_at(None,str(out/(label+'.json')))[1]
    try:
        save('gate-revision.json',dict(revision=revision,capture=name,executionRequested=execute))
        for filename,raw in sources.items():(out/filename).write_bytes(raw)
        observed=child('verify_container_recycle_archive_aging.py','observer-before');assert observed['archiveAgingIndependent']=='passed'
        q=read_at(None,str(e/'aging-handoff.json'))[1]['request'];engine,reader,files,docker=frozen_engine(e)
        with reader.ContainerRoot(engine.ref(q)['plan']['command'],files,docker) as session:
            ready=inspect_readiness(engine,q,session);save('readiness.json',ready)
            if not may_execute(ready,execute):
                child('verify_container_recycle_archive_aging.py','observer-after')
                save('result.json',dict(status='NotDue' if ready['status']=='NotDue' else 'ExecutionNotRequested',retirementExecuted=False));return out
            before=child('collect_container_recycle_archive_completion.py','independent-before')
            # Revalidate after acquisition. Source locks and actual syscall
            # guards inside Apply remain authoritative through the write.
            ready=inspect_readiness(engine,q,session);assert may_execute(ready,execute);typed_equal(before['roots'],ready['rootsAfter']);save('execution-readiness.json',ready)
            clock_before=time.time_ns()//1000000;receipt=engine.apply(q,session);clock_after=time.time_ns()//1000000
            save('native-receipt.json',dict(receipt=receipt,clockBeforeUnixMs=clock_before,clockAfterUnixMs=clock_after))
            after=child('collect_container_recycle_archive_completion.py','independent-after')
            value=completion_bundle(before,after,ready,receipt,clock_before,clock_after);save('completion.json',value)
            current=child('collect_container_recycle_archive_completion.py','independent-current');typed_equal(current,after)
            save('result.json',dict(status='Accepted',retirementExecuted=True,onlyOwnedDataRemoved=True,fixtureRetainedForSeparateCleanup=True));return out
    except BaseException as error:
        save('failure.json',dict(type=type(error).__name__,executionRequested=execute,retirementOutcome='Unproven',fixtureRetained=True));raise


if __name__=='__main__':
    try:
        assert len(sys.argv) in (2,3) and (len(sys.argv)==2 or sys.argv[2]=='--execute-owned-retirement')
        out=main(sys.argv[1],len(sys.argv)==3);print(json.dumps(dict(evidence=out.name,**json.loads((out/'result.json').read_bytes()))))
    except BaseException as error:print(json.dumps(dict(acceptance='failed',type=type(error).__name__)));raise SystemExit(1) from None
