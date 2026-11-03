#!/usr/bin/env python3
"""Independent read-only observer after exact cleanup; never rereads deleted bytes."""
import json
import os
from pathlib import Path
import sys
import uuid

import verify_container_acl_browser as previous
from validate_container_acl_history_browser import immutable,audit_rows
from acl_history_browser_proof import prove_snapshot,prove_pages,prove_audits,expected_entry

def verify(name):
    result=previous.verify(name,history=True)
    evidence=Path(__file__).resolve().parents[2]/'deploy/artifacts'/name
    saved=lambda name:json.loads((evidence/name).read_bytes())
    baseline=saved('baseline.json');proofs=saved('results.json');browser=saved('browser.json');signals=saved('history-signals.json');accepted=saved('history-verified.json')
    reader=previous.load('/usr/local/libexec/whaledeck-container-files.py','history_browser_independent_reader')
    files=reader.trusted_module('whaledeck-files.py','history_browser_independent_files')
    storage=previous.load('/usr/local/libexec/whaledeck-file-backups.py','history_browser_independent_storage').FileBackups(files)
    metadata=immutable(evidence,baseline,proofs,storage);expected={};selected=[]
    assert len(signals)==accepted['historyReads']==browser['aclHistory']['reads']==8
    for signal,proof in zip(signals,[p for p in proofs if p['result']['executionJobId']],strict=True):
        item=proof['result'];capture=uuid.UUID(item['executionJobId']).hex
        entry=prove_snapshot(signal,baseline,item,metadata[capture]);expected[capture]=(item['command']['location']['path'],entry)
        selected.append(signal['snapshot']);assert signal['subject']==browser['subject']
        prove_audits(saved('history-'+item['name']+'.audit.json'),browser['subject'],baseline,[p['page'] for p in signal['pages']],selected)
        assert saved('history-'+item['name']+'-verified.json')==dict(captureId=capture,physicalRootHddAndAuditVerified=True)
        p=evidence/signal['screenshot'];previous.trusted(p);assert p.name=='history-'+item['name']+'.png' and p.read_bytes().startswith(b'\x89PNG\r\n\x1a\n') and 1000<p.stat().st_size<4*1024**2
    pages=browser['aclHistory']['pages'];assert pages==signals[-1]['pages'] and len(pages)==accepted['historyPages']
    assert len(browser['aclHistory']['requests'])==len(pages)+8 and all(r['method']=='GET' for r in browser['aclHistory']['requests']) and browser['aclHistory']['mutations']==0
    prove_pages(pages,baseline,expected);audits=audit_rows(browser['subject']);prove_audits(audits,browser['subject'],baseline,[p['page'] for p in pages],selected)
    ordinary=saved('history-ordinary-result.json');assert ordinary==browser['historyOrdinary'] and ordinary['subject']!=browser['subject']
    assert ordinary['denied']==[403,403] and ordinary['anonymous']==[401,401] and ordinary['uiHistoryRequests']==0 and audit_rows(ordinary['subject'])==[]
    logout=ordinary['normalDualLogout'];assert logout['normalUiLogout'] and logout['providerNavigationObserved'] and logout['bffStatus']==401 and logout['identityStatus'] in (401,403)
    assert saved('history-ordinary-verified.json')==dict(ordinaryAndAnonymousDenied=True)
    result.update(independentAclHistoryBrowser=True,historyReads=8,historyPages=len(pages),historyAuditRows=len(audits),
                  exactOriginalMetadata=True,normalDualLogoutForTwoContexts=True,ordinaryHistoryDenied=2,anonymousHistoryDenied=2)
    return result

if __name__=='__main__':
    try:
        assert os.geteuid()==0 and len(sys.argv)==2;os.umask(0o077);value=verify(sys.argv[1])
        p=Path(__file__).resolve().parents[2]/'deploy/artifacts'/sys.argv[1]/'history-independent.json';p.write_text(json.dumps(value));print(json.dumps(dict(actualAclHistoryIndependent='passed',**value)))
    except BaseException as error:
        print(json.dumps(dict(actualAclHistoryIndependent='failed',failureType=type(error).__name__)));raise SystemExit(1) from None
