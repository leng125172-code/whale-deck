#!/usr/bin/env python3
"""Separate read-only SQL/Root/HDD verifier, never adopts or repeats an operation."""
import hashlib,json,os,re,subprocess,sys
from pathlib import Path
from validate_container_recycle_browser import SOURCES,sql
from validate_container_recycle_agent import baseline,preserved
from container_recycle_agent_io import physical,physical_proof,read_operation
from acl_source_apply_missing_proof import source_closure
from acl_history_browser_proof import typed_equal
from verify_container_recursive_acl import private_json

def verify(name,live=False):
    assert re.fullmatch('container-recycle-browser-[a-f0-9]{32}',name);repo=Path(__file__).resolve().parents[2];e=repo/'deploy/artifacts'/name;s=private_json(e/'source.json');fixture=private_json(e/'fixture.json');b=private_json(e/'browser.json');proof=private_json(e/'live-proof.json')
    assert s['validationSourceSha256']==source_closure(repo,s['validationRevision'],seeds=SOURCES)
    for n,h in s['validationSourceSha256'].items():assert hashlib.sha256((repo/'deploy/validation'/n).read_bytes()).hexdigest()==h
    assert b['actualUi'] and b['normalDualLogout'] and b['registrations']==2 and b['confirmations']==1 and b['cancellations']==1 and b['contentUploads']==0 and len(b['completed'])==2
    canceled,success=b['completed'];assert canceled['name']=='cancel' and success['name']=='trash'
    cancel=sql(canceled['id']);typed_equal(cancel,private_json(e/'cancel-sql.json'));assert cancel['row']['execution_job_id'] is None and cancel['row']['receipt_json'] is None
    cancel_live=private_json(e/'cancel-live-proof.json');typed_equal(cancel_live['sql'],cancel);typed_equal(cancel_live['source'],fixture['original']);assert private_json(e/'cancel-proof-accepted.json')['id']==canceled['id']
    typed_equal(sql(success['id']),proof['sql']);typed_equal(read_operation(proof['request'],proof['receipt'],e/'originals'),proof['records'])
    if live:
        value=physical(Path(fixture['folder']),proof['request']['operationId']);typed_equal(value,proof['physical']);physical_proof(value,fixture['original'],proof['records']['root'],proof['receipt'])
    else:
        assert private_json(e/'independent-live.json')['physicalReread'] is True and not os.path.lexists(fixture['folder']);assert subprocess.run(['docker','inspect',fixture['containerId']],capture_output=True).returncode!=0
        assert private_json(e/'verified.json')['fixtureRemoved'] is True
    preserved(private_json(e/'before.json'),baseline(s['revision']),[proof['request']['operationId']])
    return dict(independent=True,physicalReread=live,hddDataReread=True,canceledBeforeExecution=True,sourceRemovedAfterProof=not live,restoreBusiness=False)

if __name__=='__main__':
    try:
        assert os.geteuid()==0 and (len(sys.argv)==2 or sys.argv[2:]==['--live']);os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0';live=sys.argv[2:]==['--live'];v=verify(sys.argv[1],live);p=Path(__file__).resolve().parents[2]/'deploy/artifacts'/sys.argv[1]/('independent-live.json' if live else 'independent-final.json')
        with p.open('x') as out:json.dump(v,out)
        print(json.dumps(dict(actualRecycleIndependent='passed',**v)))
    except BaseException as error:
        import traceback
        t=traceback.extract_tb(error.__traceback__)[-1];print(json.dumps(dict(actualRecycleIndependent='failed',type=type(error).__name__,file=Path(t.filename).name,line=t.lineno)));raise SystemExit(1) from None
