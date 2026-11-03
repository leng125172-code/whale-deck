#!/usr/bin/env python3
"""Real history UI extension of the NEW owned ACL workflow; no repeated old writes."""
import hashlib
import json
import os
from pathlib import Path
import uuid

import validate_container_acl_browser as acl
from acl_history_browser_proof import expected_entry, prove_snapshot, prove_audits, prove_pages
from validate_container_recursive_ownership_browser import read_metadata
from validate_metric_continuity import query

SOURCES=('validate_container_acl_history_browser.py','validate_container_acl_history_browser.mjs',
         'acl_history_browser_proof.py','verify_container_acl_history_browser.py')

def audit_rows(subject):
    return query("""SELECT COALESCE(json_agg(to_jsonb(a) ORDER BY occurred_at_utc,id),'[]')
      FROM audit_events a WHERE actor_subject=:'subject' AND action IN
      ('containers.files.acl-history-list','containers.files.acl-history-read');""",subject=subject)

def immutable(evidence, baseline, proofs, storage):
    metadata={}
    with storage.target(create_namespace=False,read_only=True) as (configuration,base):
        fd=storage.f.open_at(base,'container-acl-v1',os.O_RDONLY|os.O_DIRECTORY)
        try:
            storage.attest(configuration,fd,read_only=True)
            for proof in proofs:
                item=proof['result'];assert acl.snapshot(item['id'])==json.loads((evidence/(item['name']+'.sql.json')).read_bytes())
                if not item['executionJobId']:continue
                operation=uuid.UUID(item['executionJobId']).hex
                root=(acl.STATE/(operation+'.json')).read_bytes();raw=read_metadata(storage.f,storage,configuration,fd,operation+'.json',16384)
                assert hashlib.sha256(root).hexdigest()==proof['rootSha256'] and root==(evidence/(item['name']+'.root.json')).read_bytes()
                assert hashlib.sha256(raw).hexdigest()==proof['hddSha256'] and raw==(evidence/(item['name']+'.hdd.json')).read_bytes()
                metadata[operation]=json.loads(raw)
        finally:os.close(fd)
    return metadata

def exercise(process,evidence,baseline,proofs,reader,module,storage,folder,content,current):
    metadata=immutable(evidence,baseline,proofs,storage);selected=[];signals=[]
    subject=json.loads((evidence/'access.sql.json').read_bytes())['row']['actor_subject']
    for proof in proofs:
        item=proof['result']
        if not item['executionJobId']:continue
        signal=acl.wait_case(process,evidence,'history-'+item['name'])
        assert signal['subject']==subject and signal['name']==item['name']
        capture=uuid.UUID(item['executionJobId']).hex;prove_snapshot(signal,baseline,item,metadata[capture]);selected.append(signal['snapshot'])
        assert acl.inventory(folder,reader,module,content)==current
        assert immutable(evidence,baseline,proofs,storage)==metadata
        audits=audit_rows(subject);prove_audits(audits,subject,baseline,[p['page'] for p in signal['pages']],selected)
        (evidence/('history-'+item['name']+'.audit.json')).write_text(json.dumps(audits))
        signals.append(signal);(evidence/'history-signals.json').write_text(json.dumps(signals))
        p=evidence/('history-'+item['name']+'-verified.pending');p.write_text(json.dumps(dict(captureId=capture,physicalRootHddAndAuditVerified=True)));p.replace(evidence/('history-'+item['name']+'-verified.json'))
    expected={uuid.UUID(p['result']['executionJobId']).hex:(p['result']['command']['location']['path'],expected_entry(p['result'],metadata[uuid.UUID(p['result']['executionJobId']).hex])) for p in proofs if p['result']['executionJobId']}
    prove_pages(signals[-1]['pages'],baseline,expected)
    ordinary=acl.wait_case(process,evidence,'history-ordinary')
    assert ordinary['subject']!=subject and ordinary['denied']==[403,403] and ordinary['anonymous']==[401,401] and ordinary['uiHistoryRequests']==0
    logout=ordinary['normalDualLogout'];assert logout['normalUiLogout'] and logout['providerNavigationObserved'] and logout['bffStatus']==401 and logout['identityStatus'] in (401,403)
    assert audit_rows(ordinary['subject'])==[] and acl.inventory(folder,reader,module,content)==current and immutable(evidence,baseline,proofs,storage)==metadata
    p=evidence/'history-ordinary-verified.pending';p.write_text(json.dumps(dict(ordinaryAndAnonymousDenied=True)));p.replace(evidence/'history-ordinary-verified.json')
    result=dict(historyReads=len(signals),historyPages=len(signals[-1]['pages']),historyAudits=len(audit_rows(subject)),
                fullSourceAndOriginalRootHddUnchanged=True,ordinaryDenied=2,anonymousDenied=2,ordinaryUiRequests=0)
    (evidence/'history-verified.json').write_text(json.dumps(result));return result

if __name__=='__main__':
    try:acl.main(history=True)
    except BaseException as error:
        print(json.dumps(dict(actualAclHistoryBrowser='failed',phase=acl.PHASE,failureType=type(error).__name__)),flush=True)
        raise SystemExit(1) from None
