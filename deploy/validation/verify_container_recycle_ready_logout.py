#!/usr/bin/env python3
"""Read-only independent original login expiry proof. Never mutates a task."""
import hashlib,json,os,re,subprocess,sys
from pathlib import Path
from validate_container_recycle_ready_logout import SOURCES,terminal_proof
from validate_container_recycle_browser import sql
from validate_container_recycle_agent import baseline,preserved
from container_recycle_agent_io import file_snapshot
from acl_source_apply_missing_proof import source_closure
from acl_history_browser_proof import typed_equal
from verify_container_recursive_acl import private_json

def verify(name,live=False):
    assert re.fullmatch('container-recycle-ready-logout-[a-f0-9]{32}',name);repo=Path(__file__).resolve().parents[2];e=repo/'deploy/artifacts'/name;s=private_json(e/'source.json');fixture=private_json(e/'fixture.json');browser=private_json(e/'browser.json');assert s['validationSourceSha256']==source_closure(repo,s['validationRevision'],seeds=SOURCES)
    for n,h in s['validationSourceSha256'].items():assert hashlib.sha256((repo/'deploy/validation'/n).read_bytes()).hexdigest()==h
    current=sql(browser['original']['id']);typed_equal(current,private_json(e/'sql.json'));terminal_proof(current,browser,fixture);preserved(private_json(e/'before.json'),baseline(s['revision']),())
    if live:typed_equal(file_snapshot(Path(fixture['folder'])/'recycle-rpc.bin'),fixture['original'])
    else:
        assert private_json(e/'independent-live.json')['sourceReread'] is True and private_json(e/'verified.json')['fixtureAndUsersRemoved'] is True and not os.path.lexists(fixture['folder']);assert subprocess.run(['docker','inspect',fixture['containerId']],capture_output=True).returncode!=0
    return dict(independent=True,sourceReread=live,permanentExpiryAndOriginalJobUnchanged=True,rootAndHddUnchanged=True,clockChanged=False)

if __name__=='__main__':
    try:
        assert os.geteuid()==0 and (len(sys.argv)==2 or sys.argv[2:]==['--live']);os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0';live=sys.argv[2:]==['--live'];v=verify(sys.argv[1],live);p=Path(__file__).resolve().parents[2]/'deploy/artifacts'/sys.argv[1]/('independent-live.json' if live else 'independent-final.json')
        with p.open('x') as out:json.dump(v,out)
        print(json.dumps(dict(actualReadyLogoutIndependent='passed',**v)))
    except BaseException as error:
        import traceback
        t=traceback.extract_tb(error.__traceback__)[-1];print(json.dumps(dict(actualReadyLogoutIndependent='failed',type=type(error).__name__,file=Path(t.filename).name,line=t.lineno)));raise SystemExit(1) from None
