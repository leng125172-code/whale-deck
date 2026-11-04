#!/usr/bin/env python3
"""Independent read-only observer; never claims to reread cleaned source bytes."""
import json
import os
from pathlib import Path
import sys
import uuid

import verify_container_recursive_acl_browser as previous
from validate_container_recursive_acl_history_browser import immutable,audit_rows
from recursive_acl_history_browser_proof import prove_snapshot,prove_pages,prove_audits

def verify(name):
    result=previous.verify(name,history=True)
    evidence=Path(__file__).resolve().parents[2]/'deploy/artifacts'/name
    saved=lambda name:previous.private_json(evidence/name)
    baseline=saved('baseline.json');proofs=saved('results.json');browser=saved('browser.json');signals=saved('history-signals.json');accepted=saved('history-verified.json')
    reader=previous.load('/usr/local/libexec/whaledeck-container-files.py','recursive_history_independent_reader')
    files=reader.trusted_module('whaledeck-files.py','recursive_history_independent_files')
    storage=previous.load('/usr/local/libexec/whaledeck-file-backups.py','recursive_history_independent_storage').FileBackups(files)
    metadata=immutable(evidence,baseline,proofs,storage);expected={};selected=[]
    assert len(signals)==accepted['historyReads']==browser['recursiveHistory']['reads']==7
    for signal,proof in zip(signals,[p for p in proofs if p['result']['executionJobId']],strict=True):
        item=proof['result'];capture=uuid.UUID(item['executionJobId']).hex;assert signal['subject']==browser['subject']
        expected[capture]=prove_snapshot(signal,baseline,item,metadata[capture]);selected.append(signal)
        prove_audits(saved('history-'+item['name']+'.audit.json'),browser['subject'],baseline,signal['pages'],selected)
        assert saved('history-'+item['name']+'-verified.json')==dict(captureId=capture,physicalRootHddAndAuditVerified=True)
        p=evidence/signal['screenshot'];previous.private_json(evidence/('history-'+item['name']+'-result.json'))
        from validate_container_upload_recovery_agent import trusted
        trusted(p);assert p.name=='history-'+item['name']+'.png' and p.read_bytes().startswith(b'\x89PNG\r\n\x1a\n') and 1000<p.stat().st_size<4*1024**2
        assert saved('history-'+item['name']+'-result.json')==signal
    pages=browser['recursiveHistory']['pages'];assert all(v['pages']==pages for v in signals) and len(pages)==accepted['historyPages']
    requests=browser['recursiveHistory']['requests'];assert len(requests)==len(pages)+7 and all(r['method']=='GET' for r in requests) and browser['recursiveHistory']['mutations']==0
    assert requests==[dict(method=p['request']['method'],pathname=p['request']['pathname']) for p in pages]+[dict(method=s['request']['method'],pathname=s['request']['pathname']) for s in signals]
    prove_pages(pages,baseline,expected);audits=audit_rows(browser['subject']);prove_audits(audits,browser['subject'],baseline,pages,signals)
    ordinary=saved('history-ordinary-result.json');assert ordinary==browser['historyOrdinary'] and ordinary['subject']==browser['ordinary']['subject']!=browser['subject']
    assert ordinary['normalDualLogout']==browser['ordinary']['logout'] and ordinary['denied']==[403,403] and ordinary['anonymous']==[401,401] and ordinary['uiHistoryRequests']==0 and audit_rows(ordinary['subject'])==[]
    assert saved('history-ordinary-verified.json')==dict(ordinaryAndAnonymousDenied=True)
    assert accepted['snapshotEntries']==28 and accepted['realUiSnapshotPagination'] is browser['recursiveHistory']['realUiSnapshotPagination'] is False
    assert accepted['historyAudits']==len(audits) and accepted['fullSourceAndOriginalRootHddUnchanged'] is True
    result.update(independentRecursiveAclHistoryBrowser=True,historyReads=7,historyPages=len(pages),historyAuditRows=len(audits),
        originalAggregateAndChildrenUnchanged=True,ordinaryHistoryDenied=2,anonymousHistoryDenied=2,realUiSnapshotPagination=False)
    return result

if __name__=='__main__':
    try:
        assert os.geteuid()==0 and len(sys.argv)==2;os.umask(0o077);value=verify(sys.argv[1])
        p=Path(__file__).resolve().parents[2]/'deploy/artifacts'/sys.argv[1]/'history-independent.json';p.write_text(json.dumps(value))
        print(json.dumps(dict(actualRecursiveAclHistoryIndependent='passed',**value)))
    except BaseException as error:
        print(json.dumps(dict(actualRecursiveAclHistoryIndependent='failed',failureType=type(error).__name__)));raise SystemExit(1) from None
