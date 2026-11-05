#!/usr/bin/env python3
"""Separate read-only verifier for the exact canceled restore and retained source."""
import hashlib,json,os,re,subprocess,sys
from pathlib import Path
from verify_container_recursive_acl import private_json
from acl_source_apply_missing_proof import source_closure
from acl_history_browser_proof import typed_equal
from validate_container_recycle_restore_revocation import SOURCES,prove,preserved,cut_binding,browser_proof,authority_proof
from validate_container_recycle_restore_agent import baseline
from validate_container_recycle_restore_browser import old_rows
from recycle_restore_browser_proof import stored_object
from bound_container_ownership_cut import operation_lock
import container_recycle_agent_io as source_io
import container_recycle_restore_agent_io as restore_io
from validate_container_file_publishing import verify_container,common


def verify(name,live=False):
    assert type(live) is bool and re.fullmatch('container-recycle-restore-revocation-[a-f0-9]{32}',name)
    repo=Path(__file__).resolve().parents[2];e=repo/'deploy/artifacts'/name;s=private_json(e/'source.json');f=private_json(e/'fixture.json');b=private_json(e/'browser.json');p=private_json(e/'live-proof.json');source=private_json(e/'source-proof.json')
    assert s['validationSourceSha256']==source_closure(repo,s['validationRevision'],seeds=SOURCES)
    for n,h in s['validationSourceSha256'].items():assert hashlib.sha256((repo/'deploy/validation'/n).read_bytes()).hexdigest()==h
    browser_proof(b,f);authority=private_json(e/'authority-change.json');authority_proof(authority,f);assert authority['executionExpiresAtUtc']==p['sql']['row']['execution_expires_at_utc'];assert b['subject']==f['subject']
    record=private_json(e/'cut-record.json');proof=private_json(e/'cut-proof.json');cut_sql=private_json(e/'cut-sql.json');cut_binding(cut_sql['row'],record,proof,f)
    assert cut_sql['row']['state']=='Applying' and cut_sql['row']['root_started_at_utc'] and cut_sql['row']['publish_barrier']
    assert proof['restoreLockIdentity']==list(operation_lock(restore_io.STATE/(record['operationId']+'.lock')))
    assert proof['sourceLockIdentity']==list(operation_lock(source_io.STATE/(source['request']['operationId']+'.lock')))
    hdd=private_json(e/'cut-source-proof.json');assert hdd['hddDataReread'] is True;typed_equal(hdd['source'],source['records']);typed_equal(hdd['physical'],source['physical'])
    exited=private_json(e/'helper-exit.json');assert exited['observedWithOriginalPidfd'] and exited['measurementBeginsBeforeAuthorityMutation'] and exited['originalAliveBeforeAndAfterAuthorityConfirmation'] and 0<exited['elapsedSeconds']<20
    assert private_json(e/'settled.json')==dict(id=b['result']['id'],state='Canceled',executionJobId=b['result']['executionJobId'])
    assert private_json(e/'revocation-accepted.json')['id']==b['result']['id'];typed_equal(private_json(e/'revocation-completed.json')['result'],b['result'])
    current=prove(b['result'],f,source,e,live=live)
    typed_equal(current,p if live else {**p,'physical':None})
    assert b['job']['id']==p['sql']['row']['execution_job_id'] and b['job']['state']=='Canceled'
    typed_equal(json.loads(b['job']['resultJson']),stored_object(p['sql']['executionJob']['result_json']))
    folder=Path(f['folder']);assert folder.parent==common.APPS and folder.name==f['containerName'] and re.fullmatch('wdv-container-publish-[a-f0-9]{32}',folder.name)
    if live:
        c=json.loads(common.command(['docker','inspect',f['containerId']]))[0];verify_container(c,f['containerId'],f['containerName'],folder);assert str(c['State']['Pid'])+':'+c['State']['StartedAt']==f['instanceId']
    else:
        assert private_json(e/'independent-live.json')['sourceReread'] is True and private_json(e/'verified.json')['ownedFixtureRemoved'] is True
        assert not os.path.lexists(folder) and subprocess.run(['docker','inspect',f['containerId']],capture_output=True).returncode!=0
        typed_equal(private_json(e/'after.json'),baseline(s['revision']))
    preserved(private_json(e/'before.json'),baseline(s['revision']),source['request']['operationId'],p['request']['operationId'])
    typed_equal(private_json(e/'prior-business.json'),old_rows(f['containerId']))
    return dict(independent=True,sourceReread=live,hddReread=True,exactHelperExit=True,restoreCanceledAndAborted=True,sourceTrashedAndNotRestored=True)


if __name__=='__main__':
    try:
        assert os.geteuid()==0 and (len(sys.argv)==2 or sys.argv[2:]==['--live']);os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0';live=sys.argv[2:]==['--live'];v=verify(sys.argv[1],live);p=Path(__file__).resolve().parents[2]/'deploy/artifacts'/sys.argv[1]/('independent-live.json' if live else 'independent-final.json')
        with p.open('x') as out:json.dump(v,out)
        print(json.dumps(dict(restoreRevocationIndependent='passed',**v)))
    except BaseException as error:
        import traceback
        t=traceback.extract_tb(error.__traceback__)[-1];print(json.dumps(dict(restoreRevocationIndependent='failed',type=type(error).__name__,file=Path(t.filename).name,line=t.lineno)));raise SystemExit(1) from None
