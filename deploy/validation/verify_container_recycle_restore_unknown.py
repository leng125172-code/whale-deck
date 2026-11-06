#!/usr/bin/env python3
"""Separate read-only Unknown/declined/ACK verification, including rebuilt fence."""
import hashlib,json,os,re,subprocess,sys
from pathlib import Path
from verify_container_recursive_acl import private_json
from acl_history_browser_proof import typed_equal
from acl_source_apply_missing_proof import source_closure
from validate_container_recycle_restore_unknown import SOURCES,prove,browser_proof
from restore_claim_loss_proof import stable_unknown,require_ack,preserve,names
from validate_container_recycle_restore_agent import baseline
from validate_container_recycle_restore_browser import old_rows
from validate_container_recycle_restore_postreturn_cancel import cut_binding
from restore_returned_cut_io import record_projection
from recursive_acl_restore_agent_io import read_at
from bound_container_ownership_cut import operation_lock
import container_recycle_agent_io as source_io
import container_recycle_restore_agent_io as restore_io
from validate_container_file_publishing import verify_container,common


def verify(name,live=False):
    assert type(live) is bool and re.fullmatch('container-recycle-restore-unknown-[a-f0-9]{32}',name)
    repo=Path(__file__).resolve().parents[2];e=repo/'deploy/artifacts'/name;read=lambda n:private_json(e/n)
    s,f,b,initial,p,source=[read(n) for n in ('source.json','fixture.json','browser.json','unknown-proof.json','live-proof.json','source-proof.json')]
    assert s['validationSourceSha256']==source_closure(repo,s['validationRevision'],seeds=SOURCES)
    for n,h in s['validationSourceSha256'].items():assert hashlib.sha256((repo/'deploy/validation'/n).read_bytes()).hexdigest()==h
    browser_proof(b);assert b['subject']==f['subject'];stable_unknown(initial,read('canceled-decision-proof.json'));require_ack(initial,p,b['recoverySubject'])
    record,proof,cut_sql,pending=[read(n) for n in ('cut-record.json','cut-proof.json','cut-sql.json','cut-source-proof.json')];cut_binding(cut_sql['row'],record,proof,f)
    assert record['state']=='Applying' and cut_sql['row']['state']=='Applying' and pending['verification']['hddDataReread'] is True
    assert proof['restoreLockIdentity']==list(operation_lock(restore_io.STATE/(record['operationId']+'.lock'))) and proof['sourceLockIdentity']==list(operation_lock(source_io.STATE/(source['request']['operationId']+'.lock')))
    archived={}
    for key,leaf in [('root','restore-root.json'),('claim','source-claim.json'),('sourceRoot','source-root.json'),('history','source-hdd.json')]:
        _,value,_=read_at(None,str(e/'cut-originals'/leaf));typed_equal(value,pending[key]);archived[key]=value
    typed_equal(record_projection(p['request'],archived['root'],archived['claim'],archived['sourceRoot'],archived['history'],p['records']['data']),pending['verification'])
    for key in ('claim','sourceRoot','history','data'):typed_equal(pending[key],p['records'][key])
    loss=read('claim-loss.json');assert loss['onlyExactOwnedClaimMoved'] and loss['originalHelperStillAlive'] and loss['sourceAndHddNotModified']
    source_name,hold=names(p['request']);assert (loss['sourceName'],loss['quarantineName'])==(source_name,hold);typed_equal(loss['claim'],pending['claim'])
    assert hashlib.sha256((e/'cut-originals/source-claim.json').read_bytes()).hexdigest()==loss['rawSha256'];typed_equal(loss['originalIdentity'][:8],loss['quarantineIdentity'][:8]);assert loss['quarantineIdentity'][8]>=loss['originalIdentity'][8]
    resumed=read('claim-loss-resume.json');assert resumed==dict(pid=proof['pid'],cgroup=proof['cgroup'],invocationId=proof['invocationId'],sameOriginalPidfd=True,claimAbsent=True)
    exited=read('helper-exit.json');assert exited['observedWithOriginalPidfd'] and exited['measurementBeginsBeforeOwnedClaimMove'] and exited['controlledOriginalResumed'] and 0<exited['elapsedSeconds']<20
    assert read('unknown-ready.json')==dict(id=b['result']['id'],executionJobId=b['result']['executionJobId'])
    current=prove(b['result'],f,source,e,live=live);typed_equal(current,p if live else {**p,'physical':None})
    folder=Path(f['folder']);assert folder.parent==common.APPS and folder.name==f['containerName'] and re.fullmatch('wdv-container-publish-[a-f0-9]{32}',folder.name)
    if live:
        c=json.loads(common.command(['docker','inspect',f['containerId']]))[0];verify_container(c,f['containerId'],f['containerName'],folder);assert str(c['State']['Pid'])+':'+c['State']['StartedAt']==f['instanceId']
    else:
        assert read('independent-live.json')['sourceReread'] and read('verified.json')['ownedFixtureRemoved'];assert not os.path.lexists(folder) and subprocess.run(['docker','inspect',f['containerId']],capture_output=True).returncode!=0
        fd=restore_io.private_directory(restore_io.STATE/'claims')
        try:
            raw,claim,identity=read_at(fd,source_name);typed_equal(claim,p['records']['claim']);typed_equal(identity,p['claimRebuild']['rebuiltIdentity']);assert hashlib.sha256(raw).hexdigest()==p['claimRebuild']['rebuiltRawSha256']
            try:os.stat(hold,dir_fd=fd,follow_symlinks=False);raise AssertionError('OWNED_QUARANTINE_NOT_CLEANED')
            except FileNotFoundError:pass
        finally:os.close(fd)
        typed_equal(read('after.json'),baseline(s['revision']))
    preserve(read('before.json'),baseline(s['revision']),source['request']['operationId'],p['request']['operationId'],loss if live else None)
    typed_equal(read('prior-business.json'),old_rows(f['containerId']))
    return dict(independent=True,sourceReread=live,hddReread=True,unknownIsNotNoRestore=True,nullAttributionPreserved=True,declinedDecisionUnchanged=True,onlyExplicitDifferentAdminAck=True,productRebuiltFenceRetained=True)


if __name__=='__main__':
    try:
        assert os.geteuid()==0 and (len(sys.argv)==2 or sys.argv[2:]==['--live']);os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0';live=sys.argv[2:]==['--live'];v=verify(sys.argv[1],live);p=Path(__file__).resolve().parents[2]/'deploy/artifacts'/sys.argv[1]/('independent-live.json' if live else 'independent-final.json')
        with p.open('x') as out:json.dump(v,out)
        print(json.dumps(dict(restoreUnknownIndependent='passed',**v)))
    except BaseException as error:
        import traceback
        t=traceback.extract_tb(error.__traceback__)[-1];print(json.dumps(dict(restoreUnknownIndependent='failed',type=type(error).__name__,file=Path(t.filename).name,line=t.lineno)));raise SystemExit(1) from None
