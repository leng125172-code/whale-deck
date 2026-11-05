#!/usr/bin/env python3
"""Read-only separate verification of original cancellation, source and HDD history."""
import hashlib,json,os,re,subprocess,sys
from pathlib import Path
from validate_container_recycle_inflight_cancel import SOURCES,prove
from validate_container_recycle_agent import baseline,preserved
from container_recycle_agent_io import file_snapshot,read_operation
from acl_history_browser_proof import typed_equal
from acl_source_apply_missing_proof import source_closure
from verify_container_recursive_acl import private_json

def verify(name,live=False):
    assert re.fullmatch('container-recycle-inflight-cancel-[a-f0-9]{32}',name);repo=Path(__file__).resolve().parents[2];e=repo/'deploy/artifacts'/name;s=private_json(e/'source.json');fixture=private_json(e/'fixture.json');browser=private_json(e/'browser.json');p=private_json(e/'live-proof.json');cut=private_json(e/'cut-proof.json');record=private_json(e/'cut-record.json');exit=private_json(e/'helper-exit.json')
    assert s['validationSourceSha256']==source_closure(repo,s['validationRevision'],seeds=SOURCES)
    for n,h in s['validationSourceSha256'].items():assert hashlib.sha256((repo/'deploy/validation'/n).read_bytes()).hexdigest()==h
    assert browser['actualUi'] and browser['normalDualLogout'] and browser['registrations']==browser['confirmations']==browser['cancellations']==1
    assert cut['twoKernelFdProofs'] and cut['sourceNotRemoved'] and type(cut['pid']) is int and cut['pid']>1 and len(cut['containerRootIdentity'])==len(cut['operationLockIdentity'])==2 and re.fullmatch('[a-f0-9]{32}',cut['invocationId']) and re.fullmatch('whaledeck-container-recycling-[a-f0-9]{32}.service',cut['cgroup'])
    assert record['state'] in ('Preparing','Retaining') and record['historyVerified'] and record['operationId']==p['request']['operationId'] and record['owner']==p['request']['owner'] and record['plan']==p['request']['plan'] and cut['historyDigest']==p['receipt']['historyDigest']
    typed_equal(cut['sourceIdentity'],fixture['original']['identity']);assert cut['sourceSha256']==fixture['original']['sha256'] and exit['observedWithOriginalPidfd'] and 0<exit['elapsedSeconds']<20
    if live:typed_equal(prove(browser['result'],fixture,e),p)
    else:
        from validate_container_recycle_browser import sql
        typed_equal(sql(browser['result']['id']),p['sql']);typed_equal(read_operation(p['request'],p['receipt'],e/'originals'),p['records']);assert private_json(e/'independent-live.json')['sourceReread'] and private_json(e/'verified.json')['fixtureAndUsersRemoved'] and not os.path.lexists(fixture['folder']);assert subprocess.run(['docker','inspect',fixture['containerId']],capture_output=True).returncode!=0
    preserved(private_json(e/'before.json'),baseline(s['revision']),[p['request']['operationId']]);return dict(independent=True,sourceReread=live,originalHelperExit=True,originalAbortedAndCanceled=True,hddReread=True)

if __name__=='__main__':
    try:
        assert os.geteuid()==0 and (len(sys.argv)==2 or sys.argv[2:]==['--live']);os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0';live=sys.argv[2:]==['--live'];v=verify(sys.argv[1],live);p=Path(__file__).resolve().parents[2]/'deploy/artifacts'/sys.argv[1]/('independent-live.json' if live else 'independent-final.json')
        with p.open('x') as out:json.dump(v,out)
        print(json.dumps(dict(inflightIndependent='passed',**v)))
    except BaseException as error:
        import traceback
        t=traceback.extract_tb(error.__traceback__)[-1];print(json.dumps(dict(inflightIndependent='failed',type=type(error).__name__,file=Path(t.filename).name,line=t.lineno)));raise SystemExit(1) from None
