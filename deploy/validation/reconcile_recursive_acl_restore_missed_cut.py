#!/usr/bin/env python3
"""One fixed missed-cut run: prove original completion and clean only its owned tree.

No Apply, SQL updates, identity changes, ACK or process signals. The failed UI
gate remains failed; a completed original operation is not a cancellation pass.
"""
import hashlib,json,os,subprocess,sys,uuid
from pathlib import Path
from acl_history_browser_proof import typed_equal
from acl_source_apply_missing_proof import source_closure
from recursive_acl_restore_agent_io import installed,read_bundle
from recursive_acl_restore_record_proof import records_proof
from recursive_acl_restore_business_proof import managed_plan,managed_receipt
from recursive_acl_history_paging_proof import content,inventory,remove_proved
from validate_container_recursive_acl_restore_browser import sql_snapshot,attribute_digest
from validate_container_file_publishing import common
from validate_container_upload_recovery_agent import trusted
from validate_recursive_acl_restore_paging import unchanged,restore_database

NAME='recursive-acl-restore-prewrite-cancel-2c40b80c1c444bb9973e223664f7e7d6'
SOURCES=('reconcile_recursive_acl_restore_missed_cut.py','test_reconcile_recursive_acl_restore_missed_cut.py')

def target(name):
    assert name==NAME
    return Path(__file__).resolve().parents[2]/'deploy/artifacts'/name


def settled(value):
    r=value['row']
    assert r['state']=='Succeeded' and r['completed_at_utc'] and r['lease_token'] is None and r['publish_barrier'] is False
    assert [r[k] for k in ('completed_count','changed_count','aborted_count','not_started_count','unknown_count')]==[26,26,0,0,0]
    assert value['executionJob']['state']=='Succeeded'


def main(name):
    assert os.geteuid()==0;os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0'
    p=target(name).resolve(strict=True);trusted(p,True);repo=p.parents[2]
    assert not (p/'reconciled-missed-cut.json').exists() and not (p/'browser.json').exists() and not (p/'bound-helper.json').exists()
    assert not subprocess.check_output(['git','-C',str(repo),'status','--porcelain']).strip()
    revision=subprocess.check_output(['git','-C',str(repo),'rev-parse','HEAD'],text=True).strip();closure=source_closure(repo,revision,seeds=SOURCES)
    assert all(hashlib.sha256((repo/'deploy/validation'/n).read_bytes()).hexdigest()==sha for n,sha in closure.items())
    b=json.loads((p/'baseline.json').read_bytes());source=json.loads((p/'capture.json').read_bytes());saved=json.loads((p/'failure-drained.sql.json').read_bytes());settled(saved)
    current=sql_snapshot(saved['row']['id']);typed_equal(current,saved);r=current['row'];request=dict(operationId=uuid.UUID(r['execution_job_id']).hex,owner=r['owner_hash'],plan=managed_plan(r['plan_json']))
    io=installed();archive=p/'settled-records';source_bundle,frame=read_bundle(io,source['request'],False,archive/'source',create=True)
    assert records_proof(source,source_bundle,restore=False,attribute_digest=attribute_digest())==26
    source['metadata']=source_bundle['metadata'];root=Path(b['root']);folder=Path(b['source'])
    assert folder.parent==common.APPS and folder.name==b['containerName'] and folder.resolve(strict=True)==folder and root==folder/'recursive-tree'
    missing=subprocess.run(['docker','inspect',b['containerId']],capture_output=True,timeout=15);assert missing.returncode!=0 and b'no such' in missing.stderr.lower()
    actual=inventory(root,io.reader,content(b['containerId']),io.acl)
    restored=dict(request=request,result=managed_receipt(r['receipt_json']),before=source['after'],after=actual)
    bundle,_=read_bundle(io,request,True,archive/'restore',create=True)
    assert records_proof(restored,bundle,restore=True,source=source,frame=frame,attribute_digest=attribute_digest())==26
    (p/'settled-physical.json').write_text(json.dumps(actual))
    before=unchanged(b,io.storage);assert restore_database()==dict(intents=b['restoreDatabase']['intents']+1,executions=b['restoreDatabase']['executions']+1,active=0)
    # Reopen all original records and current entities before the only cleanup.
    assert read_bundle(io,source['request'],False,archive/'source')==(source_bundle,frame)
    assert read_bundle(io,request,True,archive/'restore')[0]==bundle
    assert inventory(root,io.reader,content(b['containerId']),io.acl)==actual
    typed_equal(sql_snapshot(r['id']),saved)
    remove_proved(root,io.reader,content(b['containerId']),io.acl,actual)
    original=folder/'original.bin';assert {f.name for f in folder.iterdir()}=={'original.bin'} and not original.is_symlink()
    info=original.stat();assert info.st_uid==info.st_gid==1000 and info.st_nlink==1
    assert original.read_bytes()==hashlib.shake_256(('owned-container-original:'+b['containerName']).encode()).digest(common.SIZE)
    original.unlink();folder.rmdir();assert not os.path.lexists(folder)
    assert read_bundle(io,source['request'],False,archive/'source')==(source_bundle,frame)
    assert read_bundle(io,request,True,archive/'restore')[0]==bundle
    typed_equal(sql_snapshot(r['id']),saved);assert unchanged(b,io.storage)==before
    result=dict(originalGatePassed=False,originalOperationReplayed=False,actualCompletedChildren=26,actualChangedChildren=26,
        originalNormalLogoutAccepted=False,cancellationAccepted=False,active=0,rawSourceAndRestoreFiles=108,
        sourceAndContainerAbsent=True,protectedBefore=len(b['protectedFiles']),protectedAfter=len(before),
        revision=revision,sourceSha256=closure,sourceBytesRereadAfterCleanup=False)
    (p/'reconciled-missed-cut.json').write_text(json.dumps(result));print(json.dumps({k:v for k,v in result.items() if k!='sourceSha256'}))

if __name__=='__main__':
    try:assert len(sys.argv)==2;main(sys.argv[1])
    except BaseException as error:
        import traceback
        f=traceback.extract_tb(error.__traceback__)[-1];print(json.dumps(dict(missedCutReconciliation='failed',type=type(error).__name__,file=Path(f.filename).name,line=f.lineno)));raise SystemExit(1) from None
