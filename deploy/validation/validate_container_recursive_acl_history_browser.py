#!/usr/bin/env python3
"""Extend a NEW owned recursive ACL workflow before exact source cleanup.

Credentials stay in the existing stdin handshake. No old capture replay, SQL
writes, response interception, history deletion or automatic rollback.
"""
import hashlib
import json
import os
from pathlib import Path
import time
import uuid

import validate_container_recursive_acl_browser as acl
from recursive_acl_history_browser_proof import expected_summary,prove_snapshot,prove_pages,prove_audits
from validate_container_recursive_ownership_browser import read_metadata
from validate_container_upload_recovery_agent import trusted
from validate_metric_continuity import query

SOURCES=('validate_container_recursive_acl_history_browser.py','validate_container_recursive_acl_history_browser.mjs',
         'recursive_acl_history_browser_proof.py','verify_container_recursive_acl_history_browser.py','test_recursive_acl_history_browser_proof.py')

def audit_rows(subject):
    return query("""SELECT COALESCE(json_agg(to_jsonb(a) ORDER BY occurred_at_utc,id),'[]')
      FROM audit_events a WHERE actor_subject=:'subject' AND action IN
      ('containers.files.recursive-acl-history-list','containers.files.recursive-acl-history-read');""",subject=subject)

def wait_case(process,evidence,name):
    assert name in tuple('history-'+case for case in acl.CASES[:-1])+('history-ordinary',)
    deadline=time.monotonic()+100;path=evidence/(name+'-result.json')
    while not path.exists():
        assert process.poll() is None,'ACTUAL_RECURSIVE_HISTORY_BROWSER_EXITED'
        assert time.monotonic()<deadline,'RECURSIVE_HISTORY_BROWSER_DEADLINE'
        time.sleep(0.1)
    trusted(path);assert path.stat().st_size<=256*1024
    return json.loads(path.read_bytes())

def immutable(evidence,baseline,proofs,storage):
    metadata={}
    with storage.target(create_namespace=False,read_only=True) as (configuration,base):
        fd=storage.f.open_at(base,'container-recursive-acl-v1',os.O_RDONLY|os.O_DIRECTORY)
        try:
            storage.attest(configuration,fd,read_only=True)
            for proof in proofs:
                item=proof['result'];assert acl.snapshot(item['id'])==json.loads((evidence/(item['name']+'.sql.json')).read_bytes())
                if not item['executionJobId']:continue
                operation=uuid.UUID(item['executionJobId']).hex;raw=(acl.STATE/(operation+'.json')).read_bytes()
                assert hashlib.sha256(raw).hexdigest()==proof['rootSha256'] and raw==(evidence/(item['name']+'.root.json')).read_bytes()
                raw=read_metadata(storage.f,storage,configuration,fd,operation+'.json')
                assert raw==(evidence/(item['name']+'.hdd.json')).read_bytes()
                metadata[operation]=json.loads(raw)
                # Recheck every immutable child record, not only its aggregate summary.
                child=storage.f.open_at(fd,operation+'/entries',os.O_RDONLY|os.O_DIRECTORY)
                try:
                    storage.attest(configuration,child,read_only=True)
                    for index in range(4):
                        identifier=hashlib.sha256(b'whaledeck-recursive-acl-entry-id-v1:'+operation.encode()+b':'+str(index).encode()).hexdigest()[:32]
                        original=(acl.STATE/(operation+'.entries')/(identifier+'.json'));trusted(original)
                        assert original.read_bytes()==(evidence/(item['name']+'.'+identifier+'.root.json')).read_bytes()
                        raw=read_metadata(storage.f,storage,configuration,child,identifier+'.json',16384)
                        assert raw==(evidence/(item['name']+'.'+identifier+'.hdd.json')).read_bytes()
                finally:os.close(child)
        finally:os.close(fd)
    return metadata

def acknowledge(evidence,name,value):
    pending=evidence/(name+'-verified.pending')
    with pending.open('x') as stream:stream.write(json.dumps(value))
    pending.replace(evidence/(name+'-verified.json'))

def exercise(process,evidence,baseline,proofs,reader,storage,root,content,module,current):
    metadata=immutable(evidence,baseline,proofs,storage);signals=[]
    subject=json.loads((evidence/'access.sql.json').read_bytes())['row']['actor_subject']
    expected={uuid.UUID(p['result']['executionJobId']).hex:expected_summary(p['result'],metadata[uuid.UUID(p['result']['executionJobId']).hex]) for p in proofs if p['result']['executionJobId']}
    assert len(expected)==7
    for proof in proofs:
        item=proof['result']
        if not item['executionJobId']:continue
        signal=wait_case(process,evidence,'history-'+item['name']);assert signal['subject']==subject
        capture=uuid.UUID(item['executionJobId']).hex;prove_snapshot(signal,baseline,item,metadata[capture]);signals.append(signal)
        prove_pages(signal['pages'],baseline,expected)
        assert acl.inventory(root,reader,content,module)==current and immutable(evidence,baseline,proofs,storage)==metadata
        audits=audit_rows(subject);prove_audits(audits,subject,baseline,signal['pages'],signals)
        acl.archive(evidence,'history-'+item['name']+'.audit.json',json.dumps(audits,sort_keys=True).encode(),False)
        (evidence/'history-signals.json').write_text(json.dumps(signals))
        acknowledge(evidence,'history-'+item['name'],dict(captureId=capture,physicalRootHddAndAuditVerified=True))
    ordinary=wait_case(process,evidence,'history-ordinary')
    assert ordinary['subject']!=subject and ordinary['denied']==[403,403] and ordinary['anonymous']==[401,401] and ordinary['uiHistoryRequests']==0
    logout=ordinary['normalDualLogout'];assert logout['normalUiLogout'] and logout['providerNavigationObserved'] and logout['bffStatus']==401 and logout['identityStatus'] in (401,403)
    assert audit_rows(ordinary['subject'])==[] and acl.inventory(root,reader,content,module)==current and immutable(evidence,baseline,proofs,storage)==metadata
    acknowledge(evidence,'history-ordinary',dict(ordinaryAndAnonymousDenied=True))
    result=dict(historyReads=7,historyPages=len(signals[-1]['pages']),historyAudits=len(audit_rows(subject)),
        fullSourceAndOriginalRootHddUnchanged=True,ordinaryDenied=2,anonymousDenied=2,ordinaryUiRequests=0,
        realUiSnapshotPagination=False,snapshotEntries=28)
    (evidence/'history-verified.json').write_text(json.dumps(result));return result

if __name__=='__main__':
    try:acl.main(history=True)
    except BaseException as error:
        print(json.dumps(dict(actualRecursiveAclHistoryBrowser='failed',phase=acl.PHASE,failureType=type(error).__name__)),flush=True)
        raise SystemExit(1) from None
