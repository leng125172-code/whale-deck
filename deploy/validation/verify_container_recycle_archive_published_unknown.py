#!/usr/bin/env python3
"""Separate read-only original Unknown/ACK, published inode and current HDD proof."""
import os,json,sys,re,hashlib,subprocess
from pathlib import Path
from verify_container_recursive_acl import private_json
from acl_source_apply_missing_proof import source_closure
from acl_history_browser_proof import typed_equal
from validate_container_recycle_archive_published_unknown import SOURCES,HOSTS,baseline,preserved,old_rows,browser_proof
from archive_published_unknown_proof import row_proof,published_proof,audits,require_ack
from archive_inflight_cancel_proof import reservation_proof,exit_proof
from validate_container_recycle_archive_browser import archive_sql
from validate_container_recycle_purge_browser import sql as purge_sql,hdd_observation
from validate_container_recycle_browser import sql as source_sql
from recycle_archive_browser_proof import execution,stored_object,managed_plan,receipt_join
from container_recycle_archive_rpc_proof import record_proof
from bound_container_ownership_cut import operation_lock
from bound_container_recycle_archive_cut import GROUP
from validate_container_file_publishing import verify_container,common
import container_recycle_archive_agent_io as io
import container_recycle_purge_agent_io as purge_io
import container_recycle_agent_io as source_io


def verify(name,live=False):
    assert os.geteuid()==0 and type(live) is bool and re.fullmatch('container-recycle-archive-published-unknown-[a-f0-9]{32}',name)
    repo=Path(__file__).resolve().parents[2];e=repo/'deploy/artifacts'/name;read=lambda n:private_json(e/n)
    s=read('source.json');f=read('fixture.json');b=read('browser.json');browser_proof(b,f);assert s['revision']==f['revision']==b['revision']
    typed_equal(source_closure(repo,s['validationRevision'],seeds=SOURCES),s['validationSourceSha256'])
    for n,h in s['validationSourceSha256'].items():assert hashlib.sha256((e/'source'/n).read_bytes()).hexdigest()==h and hashlib.sha256((repo/'deploy/validation'/n).read_bytes()).hexdigest()==h
    for n in HOSTS:assert (Path('/usr/local/libexec')/n).read_bytes()==subprocess.check_output(['git','-c','safe.directory='+str(repo),'show',s['revision']+':deploy/host/'+n])
    source=read('source-proof.json');purge=read('success-proof.json');unknown=read('archive-inflight-proof.json');ack=read('ack-proof.json');cut=read('cut-record.json');proof=read('cut-proof.json');cut_sql=read('cut-sql.json')['row']
    typed_equal(source_sql(source['sql']['row']['id']),source['sql']);typed_equal(purge_sql(purge['sql']['row']['id']),purge['sql']);typed_equal(archive_sql(ack['sql']['row']['id']),ack['sql'])
    row_proof(unknown['sql'],f,purge['sql']['row'],False);row_proof(ack['sql'],f,purge['sql']['row'],True);require_ack(unknown,ack,f['recoverySubject']);typed_equal(read('decision-proof.json'),unknown)
    q=ack['request'];r=ack['sql']['row'];typed_equal(managed_plan(stored_object(r['plan_json'])),q['plan']);typed_equal(receipt_join(r,b['final']['receipt'],q),ack['receipt']);typed_equal(audits(r),ack['audits'])
    typed_equal(read('archive-inflight-completed.json')['result'],{k:v for k,v in b['archives'][1].items() if k!='name'});typed_equal(read('ack-completed.json')['result'],b['final'])
    for phase in ('archive-inflight','decision','ack'):assert read(phase+'-accepted.json')['id']==r['id']
    assert cut['state']=='Publishing' and cut['publishedIdentity'] is None and cut_sql['state']=='Applying' and cut_sql['receipt_json'] is None and cut_sql['publish_barrier']
    assert cut_sql['id']==r['id'] and cut_sql['execution_job_id']==r['execution_job_id'];typed_equal(reservation_proof(cut_sql,q)['state'],'Reserved')
    for k in ('operationId','owner','plan'):typed_equal(cut[k],q[k])
    published_proof(unknown['physical'],q,unknown['records']['root'],cut);typed_equal(unknown['physical'],proof['physical']);typed_equal(read('cut-current.json')['physical'],proof['physical'])
    assert proof['eightKernelFdProofs'] and proof['rootPhase']=='Publishing' and proof['targetFullCopyVerified'] and proof['publishedNamespaceVerified'] and proof['stageEmpty'] and not proof['directoryFsyncProven'] and not proof['terminalCopyJournalSaved'] and not proof['durablePublicationJournalSaved'] and proof['hddDataReread'] and proof['hddMetadataJoined']
    identities=[proof[k] for k in ('containerRootIdentity','archiveLockIdentity','purgeLockIdentity','sourceLockIdentity','archiveFdIdentity','parentFdIdentity','stageFdIdentity','outputFdIdentity')];assert len({tuple(v) for v in identities})==8
    for key,path in (('archiveLockIdentity',io.STATE/(q['operationId']+'.lock')),('purgeLockIdentity',purge_io.STATE/(purge['request']['operationId']+'.lock')),('sourceLockIdentity',source_io.STATE/(source['request']['operationId']+'.lock'))):typed_equal(proof[key],list(operation_lock(path)))
    typed_equal(proof['archiveFdIdentity'],q['plan']['archiveIdentity'][:2]);typed_equal(proof['parentFdIdentity'],q['plan']['parentIdentity']);typed_equal(proof['stageFdIdentity'],cut['privateIdentity']);typed_equal(proof['outputFdIdentity'],cut['pendingIdentity']);assert GROUP.fullmatch(proof['cgroup']) and re.fullmatch('[a-f0-9]{32}',proof['invocationId'])
    exit_proof(read('helper-exit.json'),read('cancel-request-began.json'),r['id']);assert read('cut-held.json')['executionJobId']==r['execution_job_id']
    current=io.read_operation(q,ack['receipt'],purge['records'],e/'archive-originals');typed_equal(current,ack['records']);assert current['root']['state']=='Unknown' and current['claim'] is not None
    hdd=hdd_observation(source['request']['operationId']);typed_equal(hdd,ack['hdd']);typed_equal(hdd,read('cut-current.json')['hdd'])
    for http,job in zip(b['jobProofs'],(unknown['sql']['executionJob'],ack['sql']['executionJob']),strict=True):assert http['id']==job['id'] and http['state']==job['state'];typed_equal(stored_object(http['resultJson']),stored_object(job['result_json']))
    folder=Path(f['folder']);assert folder.parent==common.APPS and folder.name==f['containerName'] and re.fullmatch('wdv-container-publish-[a-f0-9]{32}',folder.name)
    if live:
        c=json.loads(common.command(['docker','inspect',f['containerId']]))[0];verify_container(c,f['containerId'],f['containerName'],folder);assert str(c['State']['Pid'])+':'+c['State']['StartedAt']==f['instanceId']
        typed_equal(source_io.file_snapshot(folder/'original.bin'),f['originalFixtureFile'])
        from bound_container_recycle_archive_prepared_cut import parent_version
        assert parent_version(cut,folder)==proof['parentVersionAfterPublication']!=cut['parentAfterStage']
        physical=io.physical(folder,q);typed_equal(physical,ack['physical']);published_proof(physical,q,current['root'],cut);typed_equal(physical,read('cut-current.json')['physical'])
    else:
        assert read('independent-live.json')['publishedInodePhysicallyReread'] and read('owned-target-cleanup.json')['onlyOwnedPublishedTargetAndEmptyStageRemoved'] and read('verified.json')['fixtureRemoved']
        assert not os.path.lexists(folder) and subprocess.run(['docker','inspect',f['containerId']],capture_output=True).returncode!=0;typed_equal(baseline(s['revision']),read('after.json'));typed_equal(hdd,read('hdd-after-cleanup.json'))
    preserved(read('before.json'),baseline(s['revision']),source['request']['operationId'],[execution(v['id'],'purge').replace('-','') for v in b['completed']],[execution(v['id']).replace('-','') for v in b['archives']]);typed_equal(read('prior-business.json'),old_rows(f['containerId']))
    return dict(independent=True,actualUi=True,actualIdentity=True,publishedInodePhysicallyReread=live,rootUnknownClaimPreserved=True,archiveCopied=None,explicitDifferentAdminAcknowledgement=True,hddRereadAfterCleanup=not live)


if __name__=='__main__':
    try:
        assert len(sys.argv) in (2,3) and (len(sys.argv)==2 or sys.argv[2]=='--live');os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0';live=len(sys.argv)==3;v=verify(sys.argv[1],live);p=Path(__file__).resolve().parents[2]/'deploy/artifacts'/sys.argv[1]/('independent-live.json' if live else 'independent-final.json')
        with p.open('x') as out:json.dump(v,out)
        print(json.dumps(v))
    except BaseException as e:
        import traceback
        frame=traceback.extract_tb(e.__traceback__)[-1];print(json.dumps(dict(independent=False,errorType=type(e).__name__,file=Path(frame.filename).name,line=frame.lineno)));raise SystemExit(1) from None
