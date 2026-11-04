#!/usr/bin/env python3
"""Only the fixed failed logout UI run; prove zero changes and exact owned cleanup.

No identity/API writes, Apply, ACK, SQL mutation, rollback or process signals.
Incomplete new-login/ordinary/terminal-UI evidence is never reconstructed.
"""
import hashlib,json,os,sys,subprocess,uuid
from pathlib import Path
from collections import Counter
from acl_history_browser_proof import typed_equal
from acl_source_apply_missing_proof import source_closure
from recursive_acl_restore_agent_io import installed,read_bundle
from recursive_acl_restore_prewrite_proof import read_early,early_abort_proof,cancellation_cause
from recursive_acl_restore_record_proof import records_proof
from recursive_acl_restore_business_proof import managed_plan,managed_receipt,metadata,json_projection
from recursive_acl_history_paging_proof import content,inventory,remove_proved
from validate_container_recursive_acl_restore_browser import sql_snapshot,attribute_digest,expected_execution
from validate_recursive_acl_restore_paging import unchanged,restore_database
from validate_container_file_publishing import common
from validate_container_upload_recovery_agent import trusted

NAME='recursive-acl-restore-inflight-logout-049b18f87fcf49a7b9c01baf33313781'
SOURCES=('reconcile_recursive_acl_restore_logout.py','test_reconcile_recursive_acl_restore_logout.py')

def target(name):
    assert name==NAME
    return Path(__file__).resolve().parents[2]/'deploy/artifacts'/name

def main(name):
    assert os.geteuid()==0;os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0'
    p=target(name).resolve(strict=True);trusted(p,True);repo=p.parents[2]
    assert not (p/'reconciled-logout.json').exists() and not (p/'browser.json').exists()
    assert not subprocess.check_output(['git','-C',str(repo),'status','--porcelain']).strip()
    revision=subprocess.check_output(['git','-C',str(repo),'rev-parse','HEAD'],text=True).strip();closure=source_closure(repo,revision,seeds=SOURCES)
    assert all(hashlib.sha256((repo/'deploy/validation'/n).read_bytes()).hexdigest()==h for n,h in closure.items())
    b=json.loads((p/'baseline.json').read_bytes());source=json.loads((p/'capture.json').read_bytes());saved=json.loads((p/'terminal.sql.json').read_bytes());r=saved['row']
    typed_equal(sql_snapshot(r['id']),saved);assert r['state']==saved['executionJob']['state']=='Canceled' and r['completed_at_utc'] and not r['publish_barrier'] and r['lease_token'] is r['lease_expires_at_utc'] is None
    assert cancellation_cause(r,logout=True)=={} and r['result_code']=='CONTAINER_RECURSIVE_ACL_RESTORE_CANCELED' and r['execution_job_id']==expected_execution(r['id'])
    old=json.loads((p/'ready.sql.json').read_bytes())['row']
    for k in ('id','actor_subject','login_binding','owner_hash','command_json','request_hash','plan_json','plan_hash','ready_expires_at_utc'):typed_equal(r[k],old[k])
    wanted=Counter({('plan-accepted','Accepted',r['id']):1,('plan-result','Ready',r['id']):1,('confirmed','Accepted',r['execution_job_id']):1,('result','Canceled',r['execution_job_id']):1});seen=Counter()
    for audit in saved['audits']:
        assert audit['actor_subject']==r['actor_subject'] and audit['target_id']==r['container_id'] and audit['target_type']=='container' and audit['trace_id']
        typed_equal(json_projection(audit['detail_json']),metadata(r));prefix='containers.files.recursive-acl-restore.';assert audit['action'].startswith(prefix)
        seen[(audit['action'].removeprefix(prefix),audit['result'],audit['job_id'])]+=1
    assert seen==wanted
    action=json.loads((p/'action-result.json').read_bytes());logout=action['logout']
    assert action['id']==r['id'] and action['subject']==r['actor_subject'] and action['logoutDuringExecution'] is True and action['cancellations']==0
    assert logout['normalUiLogout'] is logout['providerNavigationObserved'] is logout['finalLoginPage'] is True and logout['bffStatus']==401 and logout['identityStatus'] in (401,403)
    io=installed();source_bundle,frame=read_bundle(io,source['request'],False,p/'bound-source')
    assert records_proof(source,source_bundle,restore=False,attribute_digest=attribute_digest())==26
    source['metadata']=source_bundle['metadata']
    request=dict(operationId=uuid.UUID(r['execution_job_id']).hex,owner=r['owner_hash'],plan=managed_plan(r['plan_json']));receipt=managed_receipt(r['receipt_json'])
    records=read_early(io,request,p/'reconciled-records',create=True);actual=inventory(Path(b['root']),io.reader,content(b['containerId']),io.acl)
    proof=early_abort_proof(request,receipt,records,source,frame,source['after'],actual,attribute_digest())
    assert r['root_state']==receipt['state']=='Aborted'
    for native,column in (('historyMayExist','history_may_exist'),('historyVerified','history_verified'),('historyDigest','history_digest'),('outcomeDigest','outcome_digest'),('bindingCurrent','binding_current'),('historyAvailable','history_available')):typed_equal(receipt[native],r[column])
    assert proof['counts']==[r[k] for k in ('completed_count','changed_count','aborted_count','not_started_count','unknown_count')]==[0,0,1,25,0]
    assert (p/'cut-hdd.json').read_bytes()==(p/'reconciled-records/hdd.json').read_bytes()
    before=unchanged(b,io.storage);assert restore_database()==dict(intents=b['restoreDatabase']['intents']+1,executions=b['restoreDatabase']['executions']+1,active=0)
    folder=Path(b['source']);root=Path(b['root']);assert folder.parent==common.APPS and folder.resolve(strict=True)==folder and folder.name==b['containerName'] and root==folder/'recursive-tree'
    missing=subprocess.run(['docker','inspect',b['containerId']],capture_output=True,timeout=15);assert missing.returncode!=0 and b'no such' in missing.stderr.lower()
    assert read_early(io,request,p/'reconciled-records')==records and inventory(root,io.reader,content(b['containerId']),io.acl)==actual
    remove_proved(root,io.reader,content(b['containerId']),io.acl,actual)
    original=folder/'original.bin';assert {f.name for f in folder.iterdir()}=={'original.bin'} and not original.is_symlink()
    info=original.stat();assert info.st_uid==info.st_gid==1000 and info.st_nlink==1
    assert original.read_bytes()==hashlib.shake_256(('owned-container-original:'+b['containerName']).encode()).digest(common.SIZE)
    original.unlink();folder.rmdir()
    assert read_early(io,request,p/'reconciled-records')==records and read_bundle(io,source['request'],False,p/'bound-source')==(source_bundle,frame)
    typed_equal(sql_snapshot(r['id']),saved);assert unchanged(b,io.storage)==before
    result=dict(originalGatePassed=False,originalRestorationReplayed=False,terminalUiAndRemainingIdentityChecksAccepted=False,
        originalCounts=proof['counts'],currentTreeUnchanged=True,sourceAndContainerAbsent=True,sourceBytesRereadAfterCleanup=False,
        oldProtected=len(b['protectedFiles']),finalProtected=len(before),revision=revision,sourceSha256=closure)
    (p/'reconciled-logout.json').write_text(json.dumps(result));print(json.dumps({k:v for k,v in result.items() if k!='sourceSha256'}))

if __name__=='__main__':
    try:assert len(sys.argv)==2;main(sys.argv[1])
    except BaseException as error:
        import traceback
        f=traceback.extract_tb(error.__traceback__)[-1];print(json.dumps(dict(logoutReconciliation='failed',type=type(error).__name__,file=Path(f.filename).name,line=f.lineno)));raise SystemExit(1) from None
