#!/usr/bin/env python3
"""Independent read-only installed archive join and live/current HDD reread."""
import hashlib,json,os,re,subprocess,sys
from pathlib import Path
from acl_history_browser_proof import typed_equal
from acl_source_apply_missing_proof import source_closure
from verify_container_recursive_acl import private_json
from recursive_acl_restore_agent_io import read_at
from validate_container_recycle_archive_agent import SOURCES,HOSTS,PROBE_SOURCES,baseline,preserved
from validate_container_recycle_purge_browser import old_rows
from validate_container_file_publishing import common,verify_container
import container_recycle_purge_rpc_proof as purge
import container_recycle_purge_agent_io as purge_io
import container_recycle_agent_io as source_io
import container_recycle_archive_agent_io as archive_io
import container_recycle_archive_rpc_proof as proof
import container_recycle_archive_workflow as workflow


def verify(name,live=False):
    assert type(live) is bool and re.fullmatch('container-recycle-archive-agent-[a-f0-9]{32}',name);repo=Path(__file__).resolve().parents[2];e=repo/'deploy/artifacts'/name
    src=private_json(e/'source.json');before=private_json(e/'baseline.json');f=private_json(e/'fixture.json');steps=private_json(e/'steps.json');source_before=private_json(e/'source-before.json');history_before=private_json(e/'history-before.json');purged_before=private_json(e/'purged-before.json')
    assert src['validationSourceSha256']==source_closure(repo,src['validationRevision'],seeds=SOURCES)
    for n,h in src['validationSourceSha256'].items():assert hashlib.sha256((repo/'deploy/validation'/n).read_bytes()).hexdigest()==h and hashlib.sha256((e/'source'/n).read_bytes()).hexdigest()==h
    git=lambda *a:subprocess.check_output(['git','-c','safe.directory='+str(repo),'-C',str(repo),*a]);assert set(src['probeSourceSha256'])==set(PROBE_SOURCES)
    for n,h in src['probeSourceSha256'].items():assert hashlib.sha256(git('show',src['probeRevision']+':'+n)).hexdigest()==h and hashlib.sha256((repo/n).read_bytes()).hexdigest()==h
    for n in HOSTS:assert (Path('/usr/local/libexec')/n).read_bytes()==git('show',src['revision']+':deploy/host/'+n)
    binary=Path(src['binary']);assert binary.parent.parent==repo/'deploy/artifacts' and re.fullmatch(r'terminal-probe\.[A-Za-z0-9]+',binary.parent.name) and binary.name=='TerminalProbe';assert hashlib.sha256(binary.read_bytes()).hexdigest()==src['binarySha256'];subprocess.run(['sha256sum','--quiet','-c','SHA256SUMS'],cwd=binary.parent,check=True,capture_output=True)
    assert f['header']['revision']==src['revision'] and f['header']['probeRevision']==src['probeRevision'];frames=[proof.unique_json(line) for line in (e/'rpc.jsonl').read_text().splitlines()]
    if live:
        summary=workflow.prefix_proof(frames,f['header']);typed_equal(summary,private_json(e/'live-summary.json'));assert summary['closed'] is False
        process=private_json(e/'probe-process.json');pid=process['pid'];assert type(pid) is int and pid>1;proc=Path('/proc')/str(pid);assert proc.stat().st_uid==0 and proc.joinpath('stat').read_text().rsplit(') ',1)[1].split()[19]==process['startedTick'];assert proc.joinpath('cmdline').read_bytes().split(b'\0')[:2]==[os.fsencode(binary),b'container-recycle-archives']
    else:
        summary=workflow.transcript_proof(frames,f['header']);typed_equal(summary,private_json(e/'summary.json'));assert summary['closed'] and summary['afterCleanupBindingObserved'] and len(frames)==36
    assert len(steps)==24 and (e/'rpc.stderr').read_bytes()==b'' and (e/'capability/rpc.stderr').read_bytes()==b''
    cap=[proof.unique_json(line) for line in (e/'capability/rpc.jsonl').read_text().splitlines()];assert len(cap)==2 and cap[-1]==dict(kind='done');proof.ready_proof(cap[0],f['header'])
    for at,n in ((1,'source-plan-frame.json'),(4,'plan-frame.json'),(5,'refusals.json'),(11,'fresh-plan-frame.json'),(18,'source-purged-frame.json'),(19,'archive-plan-frame.json'),(20,'archive-refusals.json'),(26,'archive-fresh-plan-frame.json'),(33,'source-after-archive-frame.json')):typed_equal(frames[at],private_json(e/n))
    final={}
    for index,step in enumerate(steps):
        folder=e/('records-'+str(index));records=step['records'];q=step['request'];receipt=step['receipt']
        if step['phase']=='source':
            assert index in (0,1);_,root,_=read_at(None,str(folder/'root.json'));_,history,_=read_at(None,str(folder/'hdd.json'));typed_equal(root,records['root']);typed_equal(history,records['history']);purge.source.record_proof(q,receipt,root,history,data_sha256=records['data']['sha256'],data_size=records['data']['sizeBytes']);source_io.physical_proof(step['physical'],f['original'],root,receipt)
            if index==1:typed_equal(root,source_before);typed_equal(history,history_before)
        elif step['phase']=='purge':
            assert 2<=index<=12
            for key,n in (('root','purge-root.json'),('marker','purge-marker.json'),('sourceRoot','source-root.json'),('history','source-hdd.json')):
                _,raw,_=read_at(None,str(folder/n));typed_equal(raw,records[key])
            purge.record_proof(q,receipt,records['root'],records['marker'],records['sourceRoot'],records['history'],source_before,history_before,records['data'],store_identity=records['storeIdentity'],storage_hash=records['storageHash']);purge_io.physical_proof(step['physical'],f['original'],q,records,receipt);typed_equal(step['frame'],frames[index+4 if index<7 else index+5])
            if index==12:typed_equal(records,purged_before)
        else:
            assert step['phase']=='archive' and 13<=index<=23
            for key,n in (('root','archive-root.json'),('claim','source-claim.json'),('sourceRoot','source-root.json'),('purgeRoot','purge-root.json'),('marker','purge-marker.json'),('history','source-hdd.json')):
                _,raw,_=read_at(None,str(folder/n));typed_equal(raw,records[key])
            proof.record_proof(q,receipt,records['root'],records['claim'],records['sourceRoot'],records['purgeRoot'],records['marker'],records['history'],records['data'],source_before=purged_before['sourceRoot'],purge_before=purged_before['root'],marker_before=purged_before['marker'],history_before=purged_before['history'],data_before=purged_before['data'],store_identity=records['storeIdentity'],storage_hash=records['storageHash']);archive_io.physical_proof(step['physical'],f['original'],q,records,receipt);typed_equal(step['frame'],frames[index+8 if index<18 else index+9]);final[q['operationId']]=step
    assert set(final)=={summary['archiveCancelOperationId'],summary['archiveOperationId']};cancel=final[summary['archiveCancelOperationId']];last=final[summary['archiveOperationId']];assert last is steps[23]
    for state,op,expected in ((archive_io.STATE,summary['archiveCancelOperationId'],cancel['records']['root']),(purge_io.STATE,summary['cancelOperationId'],steps[6]['records']['root'])):
        _,current,_=read_at(None,str(state/(op+'.json')));typed_equal(current,expected);assert current['state']=='Aborted'
    current_purge=purge_io.read_operation(steps[12]['request'],steps[12]['receipt'],source_before,history_before,e/'records-12');typed_equal(current_purge,purged_before)
    receipt=last['receipt'] if live else proof.receipt_frame(frames[34],last['request']);current=archive_io.read_operation(last['request'],receipt,purged_before,e/('records-23' if live else 'cleanup-records'));typed_equal(current,last['records'])
    now=baseline(src['revision']);preserved(before,now,summary['sourceOperationId'],(summary['cancelOperationId'],summary['purgeOperationId']),(summary['archiveCancelOperationId'],summary['archiveOperationId']));typed_equal(old_rows('0'*64),private_json(e/'business-before.json'))
    folder=Path(f['folder']);assert folder.parent==common.APPS and re.fullmatch('wdv-container-publish-[a-f0-9]{32}',folder.name) and folder.name==f['name']
    if live:
        typed_equal(archive_io.physical(folder,last['request']),last['physical']);c=json.loads(common.command(['docker','inspect',f['header']['containerId']]))[0];verify_container(c,f['header']['containerId'],f['name'],folder);assert str(c['State']['Pid'])+':'+c['State']['StartedAt']==f['header']['instanceId']
    else:
        assert not os.path.lexists(folder) and subprocess.run(['docker','inspect',f['header']['containerId']],capture_output=True).returncode!=0;typed_equal(now,private_json(e/'after.json'));assert private_json(e/'live-independent.json')['physicalReread'] is True
        accepted=private_json(e/'verified.json');assert accepted['signedRpc'] is accepted['historyRetained'] is accepted['sourceAndContainerAbsent'] is accepted['exactNewCopyRemoved'] is True
    return dict(independent=True,physicalReread=live,currentHddReread=True,signedTranscriptBound=True,historiesRetained=True,humanUi=False,**summary)


if __name__=='__main__':
    try:
        assert os.geteuid()==0 and (len(sys.argv)==2 or len(sys.argv)==3 and sys.argv[2]=='--live');os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0';live=sys.argv[2:]==['--live'];value=verify(sys.argv[1],live);p=Path(__file__).resolve().parents[2]/'deploy/artifacts'/sys.argv[1]/('live-independent.json' if live else 'final-independent.json')
        with p.open('x') as out:json.dump(value,out)
        print(json.dumps(dict(installedArchiveIndependent='passed',**value)))
    except BaseException as error:
        import traceback
        frame=traceback.extract_tb(error.__traceback__)[-1];print(json.dumps(dict(installedArchiveIndependent='failed',type=type(error).__name__,file=Path(frame.filename).name,line=frame.lineno)));raise SystemExit(1) from None
