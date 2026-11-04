#!/usr/bin/env python3
"""Read-only separate process: live pre-cleanup or retained final SQL/Root/HDD.

Never issues a mutation RPC or infers cleaned file bytes were reread.
"""
import json
import os
from pathlib import Path
import sys

import verify_container_recursive_acl_browser as previous
from validate_container_recursive_acl_restore_browser import SPECS,source_proof,verify_case,retained,preserved_baseline
from recursive_acl_restore_agent_io import installed
from acl_history_browser_proof import typed_equal


def live_verify(name):
    import hashlib,re,subprocess
    from validate_container_recursive_acl import inventory
    assert re.fullmatch('container-recursive-acl-browser-[a-f0-9]{32}',name)
    evidence=Path(__file__).resolve().parents[2]/'deploy/artifacts'/name
    b=previous.private_json(evidence/'baseline.json');old=previous.private_json(evidence/'results.json');rows=previous.private_json(evidence/'restore-results.json');browser=previous.private_json(evidence/'browser.json');io=installed()
    assert b['recursiveRestoreBrowser'] is True and browser['actualUi'] and browser['normalDualLogout']
    assert previous.host_identity(b['revision'])==b['hostIdentity'] and previous.common.core_snapshot()=={k:tuple(v) for k,v in b['coreBefore'].items()}
    from validate_container_recursive_acl_browser import SOURCES,no_active_work
    from validate_container_recursive_acl_restore_browser import SOURCES as restore_sources
    from acl_source_apply_missing_proof import source_closure
    repo=evidence.parents[2];assert b['validationSourceSha256']==source_closure(repo,b['validationSourceRevision'],seeds=(*SOURCES,*restore_sources))
    for filename,sha in b['validationSourceSha256'].items():
        assert hashlib.sha256(subprocess.check_output(['git','-C',str(repo),'show',b['validationSourceRevision']+':deploy/validation/'+filename])).hexdigest()==sha
    by_name={p['result']['name']:p for p in old};current=previous.private_json(evidence/'source-physical-final.json')
    for index,(label,source_name,_) in enumerate(SPECS):
        item=rows[index];signal=previous.private_json(evidence/(label+'-result.json'));typed_equal(signal,item['signal']);assert item['before']==current
        source,frame=source_proof(evidence,by_name[source_name],io)
        typed_equal(verify_case(signal,b,by_name[source_name]['result'],source,frame,item['before'],item['after'],evidence,io,read_only=True),item);current=item['after']
    assert current==previous.private_json(evidence/'physical-final.json')
    root=Path(b['root']);assert root==Path(b['source'])/'recursive-tree' and Path(b['source']).parent==previous.common.APPS
    content=hashlib.shake_256(('recursive-browser:'+b['containerId']).encode()).digest(2*1024**2+7)
    assert len(content)==b['bytesPerFile'] and hashlib.sha256(content).hexdigest().upper()==b['sha256']
    module=previous.load('/usr/local/libexec/whaledeck-container-acl.py','restore_live_independent_acl')
    assert inventory(root,io.reader,content,module)==current
    retained(evidence,b,old,rows,io.storage,io);preserved_baseline(b,io);no_active_work()
    from validate_metric_continuity import query
    assert query("SELECT count(*) FROM container_recursive_acl_restore_intents WHERE completed_at_utc IS NULL OR publish_barrier OR lease_token IS NOT NULL;")==0
    return dict(revision=b['revision'],independentLiveRestoreProof=True,sourceBytesRereadBeforeCleanup=True,physicalObjects=4,restoreCases=5,oldSourcesAndNewRestoreSqlRootHddReopened=True)


def verify(name):
    result=previous.verify(name,restore=True);evidence=Path(__file__).resolve().parents[2]/'deploy/artifacts'/name
    saved=lambda filename:previous.private_json(evidence/filename)
    b=saved('baseline.json');browser=saved('browser.json');old=saved('results.json');rows=saved('restore-results.json');accepted=saved('restore-verified.json');io=installed()
    assert b['recursiveRestoreBrowser'] is True and len(rows)==len(browser['recursiveRestore']['completed'])==5
    by_name={p['result']['name']:p for p in old};current=saved('source-physical-final.json');children=0
    assert any(e['tag']=='user' and e['id']==0 for e in b['original']['one.bin']['access'])
    for index,(label,source_name,_) in enumerate(SPECS):
        row=rows[index];signal=saved(label+'-result.json');typed_equal(signal,row['signal']);typed_equal(signal['result'],browser['recursiveRestore']['completed'][index]);typed_equal(signal['body'],browser['recursiveRestore']['bodies'][index])
        assert row['before']==current and signal['subject']==browser['subject']
        source,frame=source_proof(evidence,by_name[source_name],io)
        value=verify_case(signal,b,by_name[source_name]['result'],source,frame,row['before'],row['after'],evidence,io,read_only=True);typed_equal(value,row);children+=value['children'];current=row['after']
        assert saved(label+'-verified.json')==dict(id=signal['result']['id'],physicalRootHddAndSqlVerified=True)
    live=saved('restore-live-independent.json');assert live['revision']==b['revision'] and live['independentLiveRestoreProof'] and live['sourceBytesRereadBeforeCleanup'] and live['physicalObjects']==4
    assert current==saved('physical-final.json') and children==accepted['newChildHdd']==16
    typed_equal(accepted,dict(cases=5,completed=4,canceledBeforeExecution=1,newChildHdd=16,actualSqlRootHddPerCase=True,sourceCapturesPreserved=True,ordinaryDenied=8,anonymousDenied=4,realSourceSnapshotPagination=False))
    restored=browser['recursiveRestore'];assert restored['registrations']==5 and restored['confirmations']==4 and restored['cancellations']==1 and restored['uploads']==0 and restored['realSourceSnapshotPagination'] is False and restored['securityChecks']==[400,403,400]
    ordinary=saved('restore-ordinary-result.json');typed_equal(ordinary,browser['restoreOrdinary']);assert ordinary['subject']==browser['ordinary']['subject']!=browser['subject']
    assert ordinary['deniedGet']==ordinary['deniedPost']==[403]*4 and ordinary['anonymous']==[401]*4 and ordinary['uiRestoreRequests']==0
    typed_equal(ordinary['normalDualLogout'],browser['ordinary']['logout']);assert saved('restore-ordinary-verified.json')==dict(ordinaryAndAnonymousDenied=True)
    retained(evidence,b,old,rows,io.storage,io);preserved_baseline(b,io)
    result.update(independentRecursiveAclRestoreBrowser=True,restoreCases=5,restoreCompleted=4,restoreCanceled=1,restoreChildHdd=16,restoreOrdinaryDenied=8,restoreAnonymousDenied=4,sourceBytesRereadAfterCleanup=False,actualRestoreUiSourcePagination=False)
    return result

if __name__=='__main__':
    try:
        assert os.geteuid()==0 and len(sys.argv) in (2,3);os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0'
        live=len(sys.argv)==3;assert not live or sys.argv[2]=='--live'
        value=live_verify(sys.argv[1]) if live else verify(sys.argv[1]);p=Path(__file__).resolve().parents[2]/'deploy/artifacts'/sys.argv[1]/('restore-live-independent.json' if live else 'restore-independent.json');p.write_text(json.dumps(value));print(json.dumps(dict(actualRecursiveAclRestoreIndependent='passed',**value)))
    except BaseException as error:
        import traceback
        frame=next((f for f in reversed(traceback.extract_tb(error.__traceback__)) if f.filename.endswith('verify_container_recursive_acl_restore_browser.py')),None)
        print(json.dumps(dict(actualRecursiveAclRestoreIndependent='failed',failureType=type(error).__name__,frame=frame.lineno if frame else None)));raise SystemExit(1) from None
