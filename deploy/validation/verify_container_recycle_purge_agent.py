#!/usr/bin/env python3
"""Separate read-only installed purge transcript, raw records and physical proof."""
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
from acl_history_browser_proof import typed_equal
from acl_source_apply_missing_proof import source_closure
from verify_container_recursive_acl import private_json
from recursive_acl_restore_agent_io import read_at
from container_recycle_purge_workflow import transcript_proof
import container_recycle_purge_rpc_proof as proof
import container_recycle_purge_agent_io as purge_io
import container_recycle_agent_io as source_io
from validate_container_recycle_purge_agent import SOURCES,HOSTS,PROBE_SOURCES,baseline,preserved
from validate_container_recycle_restore_browser import old_rows
from validate_container_file_publishing import common,verify_container


def verify(name,live=False):
    assert type(live) is bool and re.fullmatch('container-recycle-purge-agent-[a-f0-9]{32}',name);repo=Path(__file__).resolve().parents[2];e=repo/'deploy/artifacts'/name
    src=private_json(e/'source.json');before=private_json(e/'baseline.json');f=private_json(e/'fixture.json');steps=private_json(e/'steps.json');source_before=private_json(e/'source-before.json');history_before=private_json(e/'history-before.json')
    assert src['validationSourceSha256']==source_closure(repo,src['validationRevision'],seeds=SOURCES)
    for n,h in src['validationSourceSha256'].items():assert hashlib.sha256((repo/'deploy/validation'/n).read_bytes()).hexdigest()==h and hashlib.sha256((e/'source'/n).read_bytes()).hexdigest()==h
    git=lambda *a:subprocess.check_output(['git','-c','safe.directory='+str(repo),'-C',str(repo),*a]);assert set(src['probeSourceSha256'])==set(PROBE_SOURCES)
    for n,h in src['probeSourceSha256'].items():assert hashlib.sha256(git('show',src['probeRevision']+':'+n)).hexdigest()==h and hashlib.sha256((repo/n).read_bytes()).hexdigest()==h
    for n in HOSTS:assert (Path('/usr/local/libexec')/n).read_bytes()==git('show',src['revision']+':deploy/host/'+n)
    binary=Path(src['binary']);assert binary.parent.parent==repo/'deploy/artifacts' and re.fullmatch(r'terminal-probe\.[A-Za-z0-9]+',binary.parent.name) and binary.name=='TerminalProbe';assert hashlib.sha256(binary.read_bytes()).hexdigest()==src['binarySha256'];subprocess.run(['sha256sum','--quiet','-c','SHA256SUMS'],cwd=binary.parent,check=True,capture_output=True)
    assert f['header']['revision']==src['revision'] and f['header']['probeRevision']==src['probeRevision'];frames=[proof.unique_json(l) for l in (e/'rpc.jsonl').read_text().splitlines()];summary=transcript_proof(frames,f['header']);typed_equal(summary,private_json(e/'summary.json'))
    assert len(steps)==13 and (e/'rpc.stderr').read_bytes()==b'' and (e/'capability/rpc.stderr').read_bytes()==b''
    pre=[proof.unique_json(l) for l in (e/'capability/rpc.jsonl').read_text().splitlines()];assert len(pre)==2 and pre[-1]==dict(kind='done');proof.ready_proof(pre[0],f['header'])
    for at,n in ((1,'source-plan-frame.json'),(4,'plan-frame.json'),(5,'refusals.json'),(11,'fresh-plan-frame.json'),(18,'source-purged-frame.json')):typed_equal(frames[at],private_json(e/n))
    final={}
    for index,step in enumerate(steps):
        folder=e/('records-'+str(index));records=step['records'];q=step['request'];receipt=step['receipt']
        if step['phase']=='source':
            assert index in (0,1);_,root,_=read_at(None,str(folder/'root.json'));_,history,_=read_at(None,str(folder/'hdd.json'));typed_equal(root,records['root']);typed_equal(history,records['history']);proof.source.record_proof(q,receipt,root,history,data_sha256=records['data']['sha256'],data_size=records['data']['sizeBytes']);source_io.physical_proof(step['physical'],f['original'],root,receipt)
            if index==1:typed_equal(root,source_before);typed_equal(history,history_before)
        else:
            for key,n in (('root','purge-root.json'),('marker','purge-marker.json'),('sourceRoot','source-root.json'),('history','source-hdd.json')):
                _,raw,_=read_at(None,str(folder/n));typed_equal(raw,records[key])
            proof.record_proof(q,receipt,records['root'],records['marker'],records['sourceRoot'],records['history'],source_before,history_before,records['data'],store_identity=records['storeIdentity'],storage_hash=records['storageHash']);purge_io.physical_proof(step['physical'],f['original'],q,records,receipt);typed_equal(step['frame'],frames[(index+4) if index<7 else (index+5)])
            final[q['operationId']]=(index,step)
    assert set(final)=={summary['cancelOperationId'],summary['purgeOperationId']}
    # Cancellation is historical: its own journal must remain unchanged, while
    # the shared source marker now belongs to the completed independent purge.
    cancel_index,cancel=final[summary['cancelOperationId']];_,current_cancel,_=read_at(None,str(purge_io.STATE/(summary['cancelOperationId']+'.json')));typed_equal(current_cancel,cancel['records']['root']);assert current_cancel['state']=='Aborted'
    index,last=final[summary['purgeOperationId']];assert index==12 and last['receipt']['state']=='Completed';current=purge_io.read_operation(last['request'],last['receipt'],source_before,history_before,e/('records-'+str(index)));typed_equal(current,last['records']);assert frames[18]['result']
    last_source=proof.source_receipt_frame(frames[18],last['request']['plan']['command']['source']);assert last_source['historyDigest']==current['history']['historyDigest']
    now=baseline(src['revision']);preserved(before,now,summary['sourceOperationId'],(summary['cancelOperationId'],summary['purgeOperationId']));typed_equal(old_rows('0'*64),private_json(e/'business-before.json'))
    folder=Path(f['folder']);assert folder.parent==common.APPS and re.fullmatch('wdv-container-publish-[a-f0-9]{32}',folder.name) and folder.name==f['name']
    if live:
        typed_equal(purge_io.physical(folder,last['request']),last['physical']);c=json.loads(common.command(['docker','inspect',f['header']['containerId']]))[0];verify_container(c,f['header']['containerId'],f['name'],folder);assert str(c['State']['Pid'])+':'+c['State']['StartedAt']==f['header']['instanceId']
    else:
        assert not os.path.lexists(folder) and subprocess.run(['docker','inspect',f['header']['containerId']],capture_output=True).returncode!=0;typed_equal(now,private_json(e/'after.json'));assert private_json(e/'live-independent.json')['physicalReread'] is True;accepted=private_json(e/'verified.json');assert accepted['signedRpc'] is accepted['historyRetained'] is accepted['sourceAndContainerAbsent'] is True
    return dict(independent=True,physicalReread=live,sourceBytesRereadAfterCleanup=False,signedTranscriptBound=True,historiesRetained=True,humanUi=False,**summary)


if __name__=='__main__':
    try:
        assert os.geteuid()==0 and (len(sys.argv)==2 or len(sys.argv)==3 and sys.argv[2]=='--live');os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0';live=sys.argv[2:]==['--live'];value=verify(sys.argv[1],live);p=Path(__file__).resolve().parents[2]/'deploy/artifacts'/sys.argv[1]/('live-independent.json' if live else 'final-independent.json')
        with p.open('x') as out:json.dump(value,out)
        print(json.dumps(dict(installedPurgeIndependent='passed',**value)))
    except BaseException as error:
        import traceback
        f=traceback.extract_tb(error.__traceback__)[-1];print(json.dumps(dict(installedPurgeIndependent='failed',type=type(error).__name__,file=Path(f.filename).name,line=f.lineno)));raise SystemExit(1) from None
