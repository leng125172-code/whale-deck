#!/usr/bin/env python3
"""Separate read-only original cut, SQL, Unknown/ACK, namespace and HDD proof."""
import hashlib,json,os,re,subprocess,sys
from pathlib import Path
from verify_container_recursive_acl import private_json
from acl_source_apply_missing_proof import source_closure
from acl_history_browser_proof import typed_equal
from validate_container_recycle_purge_unlinked_unknown import SOURCES,browser_proof
from recycle_purge_unlinked_proof import prove,require_ack,row_proof,preserved
from validate_container_recycle_purge_agent import baseline,HOSTS
from validate_container_recycle_purge_browser import old_rows,sql,hdd_observation
from validate_container_recycle_browser import sql as source_sql
from container_recycle_purge_rpc_proof import record_proof
from bound_container_recycle_purge_unlinked_cut import unlinked_frame
from bound_container_recycle_purge_cut import GROUP
from bound_container_ownership_cut import operation_lock
from validate_container_file_publishing import verify_container,common
from recursive_acl_restore_agent_io import read_at
import container_recycle_purge_agent_io as purge_io
import container_recycle_agent_io as source_io


def verify(name,live=False):
    assert type(live) is bool and re.fullmatch('container-recycle-purge-unlinked-unknown-[a-f0-9]{32}',name)
    repo=Path(__file__).resolve().parents[2];e=repo/'deploy/artifacts'/name;s=private_json(e/'source.json');f=private_json(e/'fixture.json');b=private_json(e/'browser.json')
    assert s['validationSourceSha256']==source_closure(repo,s['validationRevision'],seeds=SOURCES)
    for n,h in s['validationSourceSha256'].items():assert hashlib.sha256((repo/'deploy/validation'/n).read_bytes()).hexdigest()==hashlib.sha256((e/'source'/n).read_bytes()).hexdigest()==h
    for n in HOSTS:assert (Path('/usr/local/libexec')/n).read_bytes()==subprocess.check_output(['git','-c','safe.directory='+str(repo),'show',s['revision']+':deploy/host/'+n])
    assert s['revision']==f['revision']==b['revision'] and f['containerId']==b['containerId'];browser_proof(b,f)
    source=private_json(e/'source-proof.json');armed=private_json(e/'armed-proof.json');unknown=private_json(e/'unknown-proof.json');ack=private_json(e/'ack-proof.json')
    assert source['sql']['row']['actor_subject']==f['subject'];typed_equal(source_sql(b['source']['id']),source['sql']);row_proof(unknown['sql'],f,False);row_proof(ack['sql'],f,True)
    typed_equal(armed['sql']['planJob'],unknown['sql']['planJob']);typed_equal(armed['sql']['planJob'],ack['sql']['planJob']);typed_equal(private_json(e/'declined-proof.json'),unknown)
    require_ack(unknown,ack,b['recoverySubject']);current=prove(b['final'],f,source,e,acknowledged=True,live=live);typed_equal(current,ack if live else {**ack,'physical':None})
    q=ack['request'];p=q['plan'];cut=private_json(e/'cut-record.json');proof=private_json(e/'cut-proof.json');r=private_json(e/'cut-sql.json')['row']
    for key in ('operationId','owner','plan'):typed_equal(cut[key],q[key])
    assert cut['state']=='Removing' and cut['pendingIdentity'] is not None and cut['unlinkConfirmed'] is False
    assert r['id']==ack['sql']['row']['id'] and r['execution_job_id']==ack['sql']['row']['execution_job_id'] and r['state']=='Applying' and r['reservation_json'] and r['apply_started_at_utc'] and r['publish_barrier'] and r['receipt_json'] is None
    assert proof['fiveKernelFdProofs'] and proof['namespaceUnlinked'] and proof['rootPhase']=='Removing' and proof['sourcePhase']=='Purging' and proof['sourceMarkerPhase']=='Deleting' and proof['durableUnlinkConfirmation'] is False
    unlinked_frame(proof['unlinkedOriginal']['identity'],cut['pendingIdentity']);assert proof['unlinkedOriginal']['sha256']==f['original']['sha256'] and proof['unlinkedOriginal']['sizeBytes']==f['original']['sizeBytes'];typed_equal(proof['unlinkedOriginal']['attributes'],f['original']['attributes'])
    assert proof['physical']['source'] is proof['physical']['retained'] is proof['physical']['pending'] is None
    assert proof['currentHddFdAttested'] and proof['hddSha256']==p['archiveSha256'];typed_equal(proof['hddIdentity'],p['archiveIdentity']);typed_equal(proof['archiveFdIdentity'],p['archiveIdentity'][:2])
    typed_equal(proof['purgeLockIdentity'],list(operation_lock(purge_io.STATE/(q['operationId']+'.lock'))));typed_equal(proof['sourceLockIdentity'],list(operation_lock(source_io.STATE/(source['request']['operationId']+'.lock'))))
    assert len({tuple(proof[k]) for k in ('containerRootIdentity','purgeLockIdentity','sourceLockIdentity','archiveFdIdentity')}|{tuple(proof['unlinkedOriginal']['identity'][:2])})==5
    assert GROUP.fullmatch(proof['cgroup']) and re.fullmatch('[a-f0-9]{32}',proof['invocationId'])
    typed_equal(private_json(e/'cut-source-root.json'),ack['records']['sourceRoot']);marker=private_json(e/'cut-source-marker.json');typed_equal(marker,{**ack['records']['marker'],'state':'Deleting'})
    record_proof(q,unknown['receipt'],unknown['records']['root'],unknown['records']['marker'],unknown['records']['sourceRoot'],unknown['records']['history'],source['records']['root'],source['records']['history'],unknown['records']['data'],store_identity=unknown['records']['storeIdentity'],storage_hash=unknown['records']['storageHash'])
    exited=private_json(e/'helper-exit.json');request=private_json(e/'cancel-request-began.json');assert 0<=exited['elapsedSeconds']<20 and exited['observedWithOriginalPidfd'] and exited['measurementBeginsBeforeUiAction']
    assert request['id']==r['id'] and request['executionJobId']==r['execution_job_id'] and exited['beganMonotonicNs']<=exited['actionMonotonicNs']==int(request['monotonicNs'])<=exited['exitObservedMonotonicNs']
    for job,data in zip(b['jobProofs'],(unknown['sql'],ack['sql']),strict=True):assert job['id']==r['execution_job_id'];typed_equal(json.loads(job['resultJson']),json.loads(data['executionJob']['result_json']) if isinstance(data['executionJob']['result_json'],str) else data['executionJob']['result_json'])
    folder=Path(f['folder']);assert folder.parent==common.APPS and folder.name==f['containerName']
    if live:
        c=json.loads(common.command(['docker','inspect',f['containerId']]))[0];verify_container(c,f['containerId'],f['containerName'],folder);assert str(c['State']['Pid'])+':'+c['State']['StartedAt']==f['instanceId']
    else:
        assert private_json(e/'independent-live.json')['physicalReread'] and private_json(e/'verified.json')['fixtureRemoved'] and not os.path.lexists(folder) and subprocess.run(['docker','inspect',f['containerId']],capture_output=True).returncode!=0
        typed_equal(hdd_observation(source['request']['operationId']),private_json(e/'hdd-after-cleanup.json'));typed_equal(baseline(s['revision']),private_json(e/'after.json'))
    preserved(private_json(e/'before.json'),baseline(s['revision']),source['request']['operationId'],q['operationId']);typed_equal(old_rows(f['containerId']),private_json(e/'prior-business.json'))
    return dict(independent=True,actualUi=True,physicalReread=live,realPostUnlinkCut=True,fiveFdProof=True,unknownPreserved=True,purgeApplied=None,explicitDifferentAdminAck=True,hddReread=True,archiveRestorePerformed=False)


if __name__=='__main__':
    try:
        assert os.geteuid()==0 and (len(sys.argv)==2 or sys.argv[2:]==['--live']);os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0';live=sys.argv[2:]==['--live'];v=verify(sys.argv[1],live);e=Path(__file__).resolve().parents[2]/'deploy/artifacts'/sys.argv[1]
        with (e/('independent-live.json' if live else 'independent-final.json')).open('x') as out:json.dump(v,out)
        print(json.dumps(dict(purgeUnlinkedUnknownIndependent='passed',**v)))
    except BaseException as error:
        import traceback
        t=traceback.extract_tb(error.__traceback__)[-1];print(json.dumps(dict(purgeUnlinkedUnknownIndependent='failed',type=type(error).__name__,file=Path(t.filename).name,line=t.lineno)));raise SystemExit(1) from None
