#!/usr/bin/env python3
"""Read-only independent ACK/physical/HDD proof for one failed-case disposal."""
import hashlib,json,os,re,subprocess,sys
from pathlib import Path
from recover_archive_published_restart_failure import inputs,current,failed_case,CASE,SEAL,SOURCES
from archive_published_restart_unknown_proof import require_ack
from verify_container_recursive_acl import private_json
from acl_source_apply_missing_proof import source_closure
from acl_history_browser_proof import typed_equal
from validate_container_recycle_archive_published_restart import baseline,old_rows
from validate_container_recycle_archive_browser import archive_sql
from validate_container_recycle_purge_browser import hdd_observation
import container_recycle_archive_agent_io as io


def verify(name,live=False):
    assert os.geteuid()==0 and re.fullmatch('archive-published-failure-disposal-[a-f0-9]{32}',name) and type(live) is bool
    repo=Path(__file__).resolve().parents[2];e=repo/'deploy/artifacts'/name;read=lambda n:private_json(e/n);s=read('source.json');assert s['original']==CASE and s['originalSeal']==SEAL and s['originalGatePassed'] is False
    failed_case(repo,CASE);typed_equal(source_closure(repo,s['validationRevision'],seeds=SOURCES),s['validationSourceSha256'])
    for n,h in s['validationSourceSha256'].items():assert hashlib.sha256((e/'source'/n).read_bytes()).hexdigest()==h and hashlib.sha256((repo/'deploy/validation'/n).read_bytes()).hexdigest()==h
    old,f,dto,source,purge,cut=inputs(repo);before=read('unknown-proof.json');ack=read('ack-proof.json');api=read('api-ack.json');assert api['status']==202 and api['actualAdministratorApi'] and api['actualUi'] is api['originalGatePassed'] is False
    require_ack(before,ack,api['subject']);typed_equal(archive_sql(dto['id']),ack['sql']);typed_equal(old_rows(f['containerId']),read('prior-business.json'))
    root=io.read_operation(ack['request'],ack['receipt'],purge['records'],old/'archive-originals');typed_equal(root,ack['records']);typed_equal(hdd_observation(source['request']['operationId']),ack['hdd'])
    if live:typed_equal(current(repo,True),ack);typed_equal(baseline(s['revision']),read('before.json'))
    else:
        assert read('independent-live.json')['independent'] and read('owned-target-cleanup.json')['onlyOwnedPublishedTargetAndEmptyStageRemoved'] and read('disposed.json')['originalGatePassed'] is False
        assert not os.path.lexists(f['folder']) and subprocess.run(['docker','inspect',f['containerId']],capture_output=True).returncode!=0
        typed_equal(baseline(s['revision']),read('after.json'));typed_equal(read('before.json'),read('after.json'));typed_equal(read('hdd-after-cleanup.json'),ack['hdd'])
    return dict(independent=True,live=live,originalGatePassed=False,actualUi=False,rootUnknownClaimHddPreserved=True,archiveCopied=None)


if __name__=='__main__':
    try:
        assert len(sys.argv) in (2,3) and (len(sys.argv)==2 or sys.argv[2]=='--live');os.umask(0o077);live=len(sys.argv)==3;v=verify(sys.argv[1],live);e=Path(__file__).resolve().parents[2]/'deploy/artifacts'/sys.argv[1]
        with (e/('independent-live.json' if live else 'independent-final.json')).open('x') as out:json.dump(v,out)
        print(json.dumps(v))
    except BaseException as error:
        import traceback
        print(json.dumps(dict(independent=False,errorType=type(error).__name__,frames=[dict(file=Path(t.filename).name,line=t.lineno) for t in traceback.extract_tb(error.__traceback__)])));raise SystemExit(1) from None
