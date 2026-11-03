#!/usr/bin/env python3
"""Opt-in real ACL restoration UI; NEW captures, operations and per-case physical proof."""
import json
import os
import uuid
import validate_container_acl_browser as acl
from validate_container_acl_history_browser import immutable,SOURCES as HISTORY_SOURCES
from acl_history_browser_proof import expected_entry,typed_equal
from acl_restore_browser_proof import SPECS,prove_audits,expected_metadata,unique_json
from validate_metric_continuity import query

SOURCES=(*HISTORY_SOURCES,'validate_container_acl_restore_browser.py','validate_container_acl_restore_browser.mjs',
         'verify_container_acl_restore_browser.py','acl_restore_browser_proof.py')

def audit_rows(identifier):
    return query("""SELECT COALESCE(json_agg(to_jsonb(a) ORDER BY occurred_at_utc,id),'[]') FROM audit_events a
       WHERE job_id IN (SELECT id FROM container_acl_intents WHERE id=:'id'::uuid UNION ALL
         SELECT execution_job_id FROM container_acl_intents WHERE id=:'id'::uuid)
         AND action LIKE 'containers.files.acl.restore.%';""",id=identifier)

def selected_source(baseline,proof,metadata):
    item=proof['result'];capture=uuid.UUID(item['executionJobId']).hex
    return dict(location=dict(containerId=baseline['containerId'],instanceId=baseline['instanceId'],path=item['command']['location']['path']),
        entry=expected_entry(item,metadata[capture]),accessAcl=metadata[capture]['plan']['accessAcl'],defaultAcl=metadata[capture]['plan']['defaultAcl'])

def exercise(process,evidence,baseline,proofs,reader,module,storage,folder,content,current):
    metadata=immutable(evidence,baseline,proofs,storage);original={p['result']['name']:p for p in proofs};restored=[]
    subject=json.loads((evidence/'access.sql.json').read_bytes())['row']['actor_subject']
    for name,capture,relative,cancel,noop in SPECS:
        if restored:immutable(evidence,baseline,restored,storage)
        signal=acl.wait_case(process,evidence,name);assert signal['subject']==subject
        source=selected_source(baseline,original[capture],metadata);typed_equal(signal['source'],source)
        result=signal['result'];assert result['name']==name and result['confirmation'] is None
        after=acl.inventory(folder,reader,module,content)
        proof=acl.verify_case(result,baseline,subject,evidence,reader,module,storage,current,after,restore_source=source)
        assert immutable(evidence,baseline,proofs,storage)==metadata
        previous_ids={p['result']['id'] for p in [*proofs,*restored]}
        previous_ops={p['result']['executionJobId'] for p in [*proofs,*restored] if p['result']['executionJobId']}
        assert result['id'] not in previous_ids and (result['executionJobId'] is None or result['executionJobId'] not in previous_ops)
        row=acl.snapshot(result['id'])['row'];audits=audit_rows(row['id']);prove_audits(audits,row)
        data=acl.snapshot(row['id'])
        for job in (data['planJob'],data['executionJob']):
            if job:typed_equal(unique_json(job['request_json']),expected_metadata(row))
        proof['source']=source;proof['auditCount']=len(audits);restored.append(proof);current=after
        (evidence/(name+'.audit.json')).write_text(json.dumps(audits));(evidence/'restore-results.json').write_text(json.dumps(restored))
        (evidence/'physical-final.json').write_text(json.dumps(current))
        p=evidence/(name+'-verified.pending');p.write_text(json.dumps(dict(id=result['id'],selectedSourceAndNewCurrentAclBackupVerified=True)));p.replace(evidence/(name+'-verified.json'))
    ordinary=acl.wait_case(process,evidence,'restore-ordinary')
    assert ordinary['subject']!=subject and ordinary['denied']==[403]*6 and ordinary['anonymous']==[401]*3 and ordinary['uiRestoreRequests']==0
    logout=ordinary['normalDualLogout'];assert logout['normalUiLogout'] and logout['providerNavigationObserved'] and logout['bffStatus']==401 and logout['identityStatus'] in (401,403)
    immutable(evidence,baseline,restored,storage)
    assert immutable(evidence,baseline,proofs,storage)==metadata and acl.inventory(folder,reader,module,content)==current
    assert query("SELECT count(*) FROM audit_events WHERE actor_subject=:'subject' AND action LIKE 'containers.files.acl.restore.%';",subject=ordinary['subject'])==0
    p=evidence/'restore-ordinary-verified.pending';p.write_text(json.dumps(dict(ordinaryAndAnonymousDenied=True)));p.replace(evidence/'restore-ordinary-verified.json')
    result=dict(restoreCases=6,restoreCompleted=5,restoreCanceledBeforeExecution=1,restoreNewHddRecords=5,
        restoreAuditRows=sum(p['auditCount'] for p in restored),selectedOriginalRootHddUnchanged=True,currentAclSavedBeforeRestore=True,
        restoreOrdinaryDenied=6,restoreAnonymousDenied=3)
    (evidence/'restore-verified.json').write_text(json.dumps(result));return result,current

if __name__=='__main__':
    try:acl.main(restore=True)
    except BaseException as error:
        print(json.dumps(dict(actualAclRestoreBrowser='failed',phase=acl.PHASE,failureType=type(error).__name__)),flush=True)
        raise SystemExit(1) from None
