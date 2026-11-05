#!/usr/bin/env python3
"""Only the named failed pre-confirmation QA run; wait expiry and prove exact cleanup."""
import hashlib,json,os,subprocess,sys,time
from pathlib import Path
from acl_history_browser_proof import typed_equal
from recursive_acl_restore_ready_logout import observation,timestamp,expired_proof
from recursive_acl_history_paging_proof import content,inventory,verify_capture,remove_proved
from recursive_acl_restore_agent_io import installed
from validate_container_recursive_acl_restore_browser import sql_snapshot
from validate_recursive_acl_restore_paging import unchanged,no_restore_records,restore_database
from validate_container_file_publishing import common
from validate_container_upload_recovery_agent import trusted

NAME='recursive-acl-restore-job-results-ui-04fff188d6ee4d6785f1ddfe1539f2cb'

def target(name):
    assert name==NAME
    return Path(__file__).resolve().parents[2]/'deploy/artifacts'/name

def failed_before_confirmation(p):
    saved=lambda n:json.loads((p/n).read_bytes())
    b=saved('baseline.json');s=saved('ready-result.json');initial=saved('ready.sql.json');failure=saved('failure.json')
    assert b['restoreJobResults'] is b['restoreInflightLogout'] is True and b['restorePrewriteCancel'] is False
    assert failure==dict(phase='confirmed-prewrite-ready',type='AssertionError',frame='acl_history_browser_proof.py',line=16)
    assert s['jobResults']['planning']['listRequest']['query']==dict(take='50')
    assert initial['row']['state']=='Ready' and initial['row']['id']==s['ready']['id']
    assert not any((p/n).exists() for n in ('pending-result.json','pending.sql.json','action-result.json','browser.json','verified.json','ready-verified.json'))
    observation(initial,0);no_restore_records(s['ready']['id'])
    return b,s,initial,saved('capture.json')

def main(name):
    assert os.geteuid()==0;os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0'
    p=target(name);trusted(p,True);b,s,initial,capture=failed_before_confirmation(p)
    assert not (p/'reconciled-ready-failure.json').exists(),'EXACT_CLEANUP_ALREADY_RECORDED'
    began=time.monotonic();deadline=timestamp(initial['row']['ready_expires_at_utc'])+60;samples=[]
    while True:
        d=sql_snapshot(s['ready']['id']);samples.append(observation(d,time.monotonic()-began))
        (p/'failure-expiry-observations.json').write_text(json.dumps(samples))
        no_restore_records(s['ready']['id'])
        if d['row']['state']=='Expired':break
        assert d['row']['state']=='Ready' and time.time()<deadline and len(samples)<240
        time.sleep(2)
    io=installed();data=content(b['containerId']);metadata=verify_capture(p,b,capture,io.storage)
    expired_proof(d,initial,b,capture,metadata);typed_equal(d['planJob'],initial['planJob'])
    assert restore_database()==dict(intents=b['restoreDatabase']['intents']+1,executions=b['restoreDatabase']['executions'],active=0)
    current=inventory(Path(b['root']),io.reader,data,io.acl);typed_equal(current,capture['after']);protected=unchanged(b,io.storage)
    folder=Path(b['source']);root=Path(b['root']);assert folder.parent==common.APPS and folder.name==b['containerName'] and folder.resolve(strict=True)==folder and root==folder/'recursive-tree'
    missing=subprocess.run(['docker','inspect',b['containerId']],capture_output=True,timeout=15);assert missing.returncode!=0 and b'no such' in missing.stderr.lower()
    original=folder/'original.bin';assert {f.name for f in folder.iterdir()}=={'original.bin','recursive-tree'} and not original.is_symlink()
    info=original.stat();assert info.st_uid==info.st_gid==1000 and info.st_nlink==1
    assert original.read_bytes()==hashlib.shake_256(('owned-container-original:'+b['containerName']).encode()).digest(common.SIZE)
    (p/'failed-expired.sql.json').write_text(json.dumps(d));(p/'failed-physical-before-cleanup.json').write_text(json.dumps(current));(p/'failed-preservation-before.json').write_text(json.dumps(protected))
    remove_proved(root,io.reader,data,io.acl,current);assert {f.name for f in folder.iterdir()}=={'original.bin'};original.unlink();folder.rmdir()
    typed_equal(sql_snapshot(s['ready']['id']),d);typed_equal(unchanged(b,io.storage),protected);verify_capture(p,b,capture,io.storage);no_restore_records(s['ready']['id'])
    result=dict(originalGatePassed=False,originalRestorationReplayed=False,restoreExecutionCreated=False,naturalExpired=True,planningSnapshotPreserved=True,sourceAndContainerAbsent=True,originalWholeTreeUnchanged=True,oldProtected=len(b['protectedFiles']),finalProtected=len(protected))
    (p/'reconciled-ready-failure.json').write_text(json.dumps(result));print(json.dumps(result),flush=True)

def verify(name):
    assert os.geteuid()==0;os.environ['GIT_OPTIONAL_LOCKS']='0'
    p=target(name);trusted(p,True);b,s,initial,capture=failed_before_confirmation(p);io=installed()
    saved=lambda n:json.loads((p/n).read_bytes())
    d=sql_snapshot(s['ready']['id']);typed_equal(d,saved('failed-expired.sql.json'))
    metadata=verify_capture(p,b,capture,io.storage);expired_proof(d,initial,b,capture,metadata)
    typed_equal(saved('failed-physical-before-cleanup.json'),capture['after']);protected=unchanged(b,io.storage);typed_equal(protected,saved('failed-preservation-before.json'))
    assert not Path(b['source']).exists();missing=subprocess.run(['docker','inspect',b['containerId']],capture_output=True,timeout=15);assert missing.returncode!=0 and b'no such' in missing.stderr.lower()
    accepted=saved('reconciled-ready-failure.json');assert accepted['originalGatePassed'] is accepted['originalRestorationReplayed'] is accepted['restoreExecutionCreated'] is False
    assert accepted['naturalExpired'] is accepted['sourceAndContainerAbsent'] is accepted['originalWholeTreeUnchanged'] is True
    assert accepted['oldProtected']==len(b['protectedFiles']) and accepted['finalProtected']==len(protected)
    assert restore_database()==dict(intents=b['restoreDatabase']['intents']+1,executions=b['restoreDatabase']['executions'],active=0)
    result=dict(independentReadOnly=True,originalGatePassed=False,sourceAndContainerAbsent=True,sourceBytesRereadAfterCleanup=False,originalRestorationReplayed=False,finalProtected=len(protected))
    with (p/'failure-cleanup-independent.json').open('x') as stream:json.dump(result,stream)
    print(json.dumps(result),flush=True)

if __name__=='__main__':
    try:
        assert len(sys.argv)==2 or len(sys.argv)==3 and sys.argv[2]=='--verify'
        (verify if len(sys.argv)==3 else main)(sys.argv[1])
    except BaseException as error:
        import traceback
        f=traceback.extract_tb(error.__traceback__)[-1];print(json.dumps(dict(readyFailureReconciliation='failed',type=type(error).__name__,file=Path(f.filename).name,line=f.lineno)),flush=True);raise SystemExit(1) from None
