#!/usr/bin/env python3
"""Independent post-cleanup SQL/Root/HDD restoration observer; never redoes writes."""
import json
import os
from pathlib import Path
import sys
import uuid
import verify_container_acl_browser as previous
from validate_container_acl_browser import verify_case,snapshot
from validate_container_acl_history_browser import immutable
from validate_container_acl_restore_browser import selected_source,audit_rows
from acl_restore_browser_proof import SPECS,prove_audits,expected_metadata,unique_json
from acl_history_browser_proof import typed_equal


def verify(name):
    result=previous.verify(name,restore=True);root=Path(__file__).resolve().parents[2]/'deploy/artifacts'/name
    saved=lambda n:previous.private_json(root/n)
    baseline=saved('baseline.json');base_proofs=saved('results.json');proofs=saved('restore-results.json');browser=saved('browser.json');accepted=saved('restore-verified.json')
    assert len(proofs)==len(browser['aclRestores']['completed'])==accepted['restoreCases']==6
    assert browser['aclRestores']['realUi'] and browser['aclRestores']['registrationRetries']==6 and browser['aclRestores']['confirmationRetries']==5
    reader=previous.load('/usr/local/libexec/whaledeck-container-files.py','restore_independent_reader');files=reader.trusted_module('whaledeck-files.py','restore_independent_files')
    module=previous.load('/usr/local/libexec/whaledeck-container-acl.py','restore_independent_acl');storage=previous.load('/usr/local/libexec/whaledeck-file-backups.py','restore_independent_storage').FileBackups(files)
    metadata=immutable(root,baseline,base_proofs,storage);immutable(root,baseline,proofs,storage)
    base={p['result']['name']:p for p in base_proofs};current=saved('physical-base-final.json');observed=root/'restore-independent-observed';observed.mkdir(mode=0o700,exist_ok=True)
    ids={p['result']['id'] for p in base_proofs};ops={p['result']['executionJobId'] for p in base_proofs if p['result']['executionJobId']};hdd=audits=0
    for spec,proof,visible in zip(SPECS,proofs,browser['aclRestores']['completed'],strict=True):
        name,capture,relative,cancel,noop=spec;item=proof['result'];assert item['name']==name and proof['before']==current
        signal=saved(name+'-result.json');typed_equal(visible,dict(source=signal['source'],result=signal['result'],key=signal['key']));assert signal['result']==item and signal['subject']==browser['subject']
        source=selected_source(baseline,base[capture],metadata);typed_equal(source,proof['source']);typed_equal(source,signal['source'])
        assert item['id'] not in ids;ids.add(item['id'])
        if item['executionJobId']:assert item['executionJobId'] not in ops;ops.add(item['executionJobId'])
        actual=verify_case(item,baseline,browser['subject'],observed,reader,module,storage,current,proof['after'],job_evidence=root,restore_source=source)
        row=snapshot(item['id'])['row'];rows=audit_rows(item['id']);prove_audits(rows,row);typed_equal(saved(name+'.audit.json'),rows)
        data=snapshot(item['id'])
        for job in (data['planJob'],data['executionJob']):
            if job:typed_equal(unique_json(job['request_json']),expected_metadata(row))
        actual.update(source=source,auditCount=len(rows));typed_equal(actual,proof);hdd+=actual['hddRecords'];audits+=len(rows);current=proof['after']
        assert saved(name+'-verified.json')==dict(id=item['id'],selectedSourceAndNewCurrentAclBackupVerified=True)
        for suffix in ('ready','result'):
            p=root/(name+'-'+suffix+'.png');previous.trusted(p);assert p.read_bytes().startswith(b'\x89PNG\r\n\x1a\n') and 1000<p.stat().st_size<4*1024**2
    assert current==saved('physical-final.json') and hdd==accepted['restoreNewHddRecords']==5 and audits==accepted['restoreAuditRows']==23
    writes=browser['aclRestores']['writes'];endpoint='/api/v1/containers/'+baseline['containerId']+'/files/acls'
    assert len(writes)==12 and all(v['method']=='POST' for v in writes)
    assert sum(v['path']==endpoint+'/restores' for v in writes)==6 and sum(v['path'].endswith('/confirm') for v in writes)==5 and sum(v['path'].endswith('/cancel') for v in writes)==1
    ordinary=saved('restore-ordinary-result.json');assert ordinary==browser['restoreOrdinary'] and ordinary['subject']!=browser['subject']
    assert ordinary['denied']==[403]*6 and ordinary['anonymous']==[401]*3 and ordinary['uiRestoreRequests']==0
    logout=ordinary['normalDualLogout'];assert logout['normalUiLogout'] and logout['providerNavigationObserved'] and logout['bffStatus']==401 and logout['identityStatus'] in (401,403)
    assert saved('restore-ordinary-verified.json')==dict(ordinaryAndAnonymousDenied=True)
    result.update(independentAclRestoration=True,restoreCases=6,restoreCompleted=5,restoreCanceledBeforeExecution=1,
        restoreNewHddRecords=hdd,restoreAuditRows=audits,originalAndCurrentAclPreimagesDistinct=True,normalDualLogoutForTwoContexts=True,
        sourceBytesRereadAfterCleanup=False,restoreOrdinaryDenied=6,restoreAnonymousDenied=3)
    return result

if __name__=='__main__':
    try:
        assert os.geteuid()==0 and len(sys.argv)==2;os.umask(0o077);result=verify(sys.argv[1]);root=Path(__file__).resolve().parents[2]/'deploy/artifacts'/sys.argv[1]
        (root/'restore-independent.json').write_text(json.dumps(result));print(json.dumps(dict(actualAclRestoreIndependent='passed',**result)))
    except BaseException as error:
        print(json.dumps(dict(actualAclRestoreIndependent='failed',failureType=type(error).__name__)));raise SystemExit(1) from None
