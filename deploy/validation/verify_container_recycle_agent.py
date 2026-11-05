#!/usr/bin/env python3
"""Separate read-only verifier of the installed recycle transcript and originals."""
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
from acl_history_browser_proof import typed_equal
from acl_source_apply_missing_proof import source_closure
from recursive_acl_restore_agent_io import read_at
from verify_container_recursive_acl import private_json
from validate_container_recycle_agent import SOURCES,HOSTS,PROBE_SOURCES,baseline,preserved
from container_recycle_rpc_proof import ready_proof,plan_frame,receipt_frame,record_proof,OPERATION_REFUSALS
from container_recycle_agent_io import physical,physical_proof,read_operation
from validate_container_file_publishing import common,verify_container


def verify(name,live=False):
    assert type(live) is bool and re.fullmatch('container-recycle-agent-[a-f0-9]{32}',name)
    repo=Path(__file__).resolve().parents[2];e=repo/'deploy/artifacts'/name;source=private_json(e/'source.json');b=private_json(e/'baseline.json');fixture=private_json(e/'fixture.json')
    assert source['validationSourceSha256']==source_closure(repo,source['validationRevision'],seeds=SOURCES)
    for n,h in source['validationSourceSha256'].items():assert hashlib.sha256((repo/'deploy/validation'/n).read_bytes()).hexdigest()==h
    assert set(source['probeSourceSha256'])==set(PROBE_SOURCES)
    for n,h in source['probeSourceSha256'].items():
        raw=subprocess.check_output(['git','-C',str(repo),'show',source['probeRevision']+':'+n]);assert hashlib.sha256(raw).hexdigest()==h and (repo/n).read_bytes()==raw
    assert fixture['header']['revision']==source['revision'] and fixture['header']['probeRevision']==source['probeRevision']
    for n in HOSTS:assert (Path('/usr/local/libexec')/n).read_bytes()==subprocess.check_output(['git','-C',str(repo),'show',source['revision']+':deploy/host/'+n])
    binary=Path(source['binary']);assert binary.parent.parent==repo/'deploy/artifacts' and re.fullmatch('terminal-probe.[A-Za-z0-9]+',binary.parent.name) and binary.name=='TerminalProbe'
    assert hashlib.sha256(binary.read_bytes()).hexdigest()==source['binarySha256'];subprocess.run(['sha256sum','--quiet','-c','SHA256SUMS'],cwd=binary.parent,check=True,capture_output=True)
    transcript=[json.loads(line) for line in (e/'rpc.jsonl').read_bytes().splitlines()];steps=private_json(e/'steps.json')
    assert len(steps)==13 and len(transcript)==17 and transcript[-1]==dict(kind='done') and (e/'rpc.stderr').read_bytes()==b''
    ready_proof(transcript[0],fixture['header']);plan=plan_frame(transcript[1],fixture['header'],boundaries=True);typed_equal(transcript[1],private_json(e/'plan-frame.json'))
    cancel=steps[0]['request']['operationId'];normal=steps[4]['request']['operationId'];assert cancel!=normal
    assert transcript[2]==private_json(e/'refusals.json')==dict(kind='rejected',operationId=cancel,refusals=OPERATION_REFUSALS)
    expected=[(cancel,a,'Aborted') for a in ('discard','inspect','apply','discard')]+[(normal,a,'Trashed' if i<4 else 'Restored') for i,a in enumerate(('apply','inspect','apply','discard','restore','restore','inspect','apply','discard'))]
    jobs=set();final={};previous=None
    for index,(step,(op,action,state),wire) in enumerate(zip(steps,expected,transcript[3:-1],strict=True)):
        assert step['request']==dict(operationId=op,owner='B'*64,plan=plan) and step['action']==action
        typed_equal(step['frame'],wire);receipt=receipt_frame(wire,step['request']);typed_equal(receipt,step['receipt']);assert receipt['state']==state and wire['action']==action
        if action=='restore':jobs.add(wire['jobId'])
        folder=e/('records-'+str(index));_,root,_=read_at(None,str(folder/'root.json'));typed_equal(root,step['records']['root'])
        history=step['records']['history'];data=step['records']['data']
        if history is not None:_,stored,_=read_at(None,str(folder/'hdd.json'));typed_equal(stored,history)
        record_proof(step['request'],receipt,root,history,data_sha256=None if data is None else data['sha256'],data_size=None if data is None else data['sizeBytes'])
        physical_proof(step['physical'],fixture['original'],root,receipt)
        if previous is not None and previous['request']['operationId']==op and previous['receipt']['state']==state:
            assert receipt['replayed'] is True;typed_equal(previous['records'],step['records']);typed_equal(previous['physical'],step['physical'])
        previous=step;final[op]=(index,step)
    assert len(jobs)==1 and normal not in jobs and cancel not in jobs
    for index,step in final.values():typed_equal(read_operation(step['request'],step['receipt'],e/('records-'+str(index))),step['records'])
    current=baseline(source['revision']);preserved(b,current,(cancel,normal))
    folder=Path(fixture['folder']);assert folder.parent==common.APPS and re.fullmatch('wdv-container-publish-[a-f0-9]{32}',folder.name) and fixture['name']==folder.name
    if live:
        typed_equal(physical(folder,normal),steps[-1]['physical'])
        c=json.loads(common.command(['docker','inspect',fixture['header']['containerId']]))[0];verify_container(c,fixture['header']['containerId'],fixture['name'],folder)
    else:
        assert not os.path.lexists(folder) and subprocess.run(['docker','inspect',fixture['header']['containerId']],capture_output=True).returncode!=0
        typed_equal(private_json(e/'after.json'),current);assert private_json(e/'live-independent.json')['physicalReread'] is True
        accepted=private_json(e/'verified.json');assert accepted['signedRpc'] is True and accepted['operations']==2 and accepted['receiptFrames']==13 and accepted['sourceAndContainerAbsent'] is True
    return dict(independent=True,physicalReread=live,sourceBytesRereadAfterCleanup=False,signedTranscriptBound=True,receiptFrames=13,restoreJobs=1,historyRetained=True,sourceAbsent=not live,humanUi=False)


if __name__=='__main__':
    try:
        assert os.geteuid()==0 and (len(sys.argv)==2 or len(sys.argv)==3 and sys.argv[2]=='--live');os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0'
        live=sys.argv[2:]==['--live'];result=verify(sys.argv[1],live);p=Path(__file__).resolve().parents[2]/'deploy/artifacts'/sys.argv[1]/('live-independent.json' if live else 'final-independent.json')
        with p.open('x') as f:json.dump(result,f)
        print(json.dumps(dict(installedRecycleIndependent='passed',**result)))
    except BaseException as error:
        import traceback
        f=traceback.extract_tb(error.__traceback__)[-1];print(json.dumps(dict(installedRecycleIndependent='failed',type=type(error).__name__,file=Path(f.filename).name,line=f.lineno)));raise SystemExit(1) from None
