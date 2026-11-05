#!/usr/bin/env python3
"""Extend a NEW recursive ACL browser fixture with actual historical restores.

Only normal visible login and UI writes. Parent reads real SQL/Root/attested HDD
and physical bytes before acknowledging each case. Never replay an old fixture.
"""
import hashlib
import json
import os
from pathlib import Path
import time
import uuid

import validate_container_recursive_acl_browser as acl
from validate_container_recursive_acl_history_browser import immutable,acknowledge
from recursive_acl_restore_agent_io import installed,read_bundle,snapshot,hdd_snapshot
from recursive_acl_restore_business_proof import database_proof,records_bound_to_business,managed_receipt
from recursive_acl_restore_record_proof import records_proof
from recursive_acl_history_browser_proof import expected_summary,typed_equal,request_proof
from validate_metric_continuity import query

SPECS=(('restore-access','access',4),('restore-noop','access',0),('restore-default','clear-default',2),('restore-clear','default',2),('restore-cancel','root',4))
SOURCES=('validate_container_recursive_acl_restore_browser.py','validate_container_recursive_acl_restore_browser.mjs','verify_container_recursive_acl_restore_browser.py','test_recursive_acl_restore_browser_proof.py')


def expected_execution(intent):
    raw=hashlib.sha256(('whaledeck-container-recursive-acl-restore-execution-id-v1:'+uuid.UUID(intent).hex).encode()).digest()[:16]
    return str(uuid.UUID(bytes_le=raw))


def sql_snapshot(identifier):
    assert str(uuid.UUID(identifier))==identifier
    return query("""SELECT json_build_object('row',to_jsonb(c),'planJob',to_jsonb(p),'executionJob',to_jsonb(e),
      'pending',(SELECT count(*) FROM outbox_messages o WHERE o.payload_json->>'jobId' IN(c.id::text,c.execution_job_id::text) AND o.processed_at_utc IS NULL),
      'accepted',(SELECT count(*) FROM audit_events a WHERE a.job_id=c.id AND a.action='containers.files.recursive-acl-restore.plan-accepted'),
      'confirmed',(SELECT count(*) FROM audit_events a WHERE a.job_id=c.execution_job_id AND a.action='containers.files.recursive-acl-restore.confirmed'),
      'events',(SELECT count(*) FROM operation_job_events j WHERE j.job_id IN(c.id,c.execution_job_id)),
      'audits',(SELECT COALESCE(json_agg(to_jsonb(a) ORDER BY occurred_at_utc,id),'[]') FROM audit_events a WHERE a.job_id IN(c.id,c.execution_job_id) AND a.action LIKE 'containers.files.recursive-acl-restore.%'))
      FROM container_recursive_acl_restore_intents c JOIN operation_jobs p ON p.id=c.id LEFT JOIN operation_jobs e ON e.id=c.execution_job_id WHERE c.id=:'id'::uuid;""",id=identifier)


def wait_case(process,evidence,name):
    assert name in tuple(v[0] for v in SPECS)+('restore-ordinary',)
    path=evidence/(name+'-result.json');deadline=time.monotonic()+150
    while not path.exists():
        assert process.poll() is None,'RECURSIVE_RESTORE_BROWSER_EXITED'
        assert time.monotonic()<deadline,'RECURSIVE_RESTORE_BROWSER_DEADLINE'
        time.sleep(0.1)
    acl.trusted(path);assert path.stat().st_size<=262144
    return json.loads(path.read_bytes())


def source_proof(evidence,proof,io,create=False):
    item=proof['result'];data=acl.snapshot(item['id']);typed_equal(data,json.loads((evidence/(item['name']+'.sql.json')).read_bytes()))
    request=dict(operationId=uuid.UUID(item['executionJobId']).hex,owner=data['row']['owner_hash'],plan=acl.managed_plan(json.loads(data['row']['plan_json'])))
    bundle,frame=read_bundle(io,request,False,evidence/'restore-sources'/item['name'],create=create)
    source=dict(request=request,result=managed_receipt(item['receipt'],api=True),before=proof['before'],after=proof['after'],metadata=bundle['metadata'])
    attrs=attribute_digest()
    assert records_proof(source,bundle,restore=False,attribute_digest=attrs)==4
    return source,frame


def attribute_digest():
    import base64
    from recursive_acl_restore_rpc_proof import digest
    return digest(b'whaledeck-container-acl-attributes-v1\n',[['user.whaledeck-recursive',base64.b64encode(b'preserve ordinary attribute').decode()]])


def selected_snapshot(signal,baseline,source_item,source):
    expected=expected_summary(source_item,source['metadata']);value=signal['source']
    entries=[dict(relativePath=e['relativePath'],kind=e['plan']['kind'],originalMode=e['plan']['sourceIdentity'][2]&0o777,accessAcl=e['plan']['accessAcl'],defaultAcl=e['plan']['defaultAcl']) for e in source['metadata']['entries']]
    typed_equal(value,dict(location=dict(containerId=baseline['containerId'],instanceId=baseline['instanceId'],path='work/recursive-tree'),summary=expected,offset=0,entries=entries,complete=True,nextOffset=None))
    request_proof(signal['sourceRequest'],baseline,expected['captureId'],expected)
    typed_equal(signal['preview'],value);request_proof(signal['previewRequest'],baseline,expected['captureId'],expected)
    return expected


def verify_case(signal,baseline,source_item,source,frame,before,after,evidence,io,*,read_only=False):
    name=signal['name'];spec=next(v for v in SPECS if v[0]==name);result=signal['result'];assert signal['subject'] and result['name']==name
    summary=selected_snapshot(signal,baseline,source_item,source)
    data=sql_snapshot(result['id']);raw=json.dumps(data,sort_keys=True).encode();acl.archive(evidence,name+'.sql.json',raw,read_only)
    proof=database_proof(data,result,baseline,signal['subject']);plan=proof['plan']
    typed_equal(plan['restoreSource'],summary);assert plan['changeCount']==spec[2] and result['readyExpiresAtUtc']==signal['ready']['readyExpiresAtUtc']
    assert result['id']==signal['registered']['id']==signal['ready']['id'] and signal['key']==data['row']['idempotency_key']
    assert signal['body']==dict(instanceId=baseline['instanceId'],path='work/recursive-tree',captureId=summary['captureId'],expectedHistoryDigest=summary['historyDigest'],expectedVersion=plan['command']['expectedVersion'])
    assert signal['ready']['state']=='Ready' and signal['registered']['state']=='Queued' and signal['registered']['executionJobId'] is None
    assert signal['originalReference']==dict(subject=signal['subject'],containerId=baseline['containerId'],key=signal['key'],id=result['id'])
    for key in ('entryCount','fileCount','directoryCount','changeCount','manifestDigest','restoreSource','command'):typed_equal(signal['ready'][key],result[key])
    assert signal['duplicateRegistrationId']==result['id']
    if proof['canceled']:
        assert name=='restore-cancel' and before==after and signal['confirmationReplayId'] is None and signal['cancelDismissedWithoutWrite'] is True
        # No execution ID means neither a restore Root record nor child/HDD namespace may exist under the intent ID.
        operation=uuid.UUID(expected_execution(result['id'])).hex
        assert not os.path.lexists(io.restoreState/(operation+'.json')) and not os.path.lexists(io.restoreState/(operation+'.entries'))
        from recursive_acl_restore_agent_io import namespace,absent
        with namespace(io,True) as fd:absent(fd,operation+'.json');absent(fd,operation)
        children=0
    else:
        assert result['executionJobId']==expected_execution(result['id'])
        request=proof['request'];bundle,_=read_bundle(io,request,True,evidence/'restore-records'/name,create=not read_only)
        children=records_bound_to_business(proof,bundle,source,frame,before,after,attribute_digest())
        assert signal['confirmationReplayId']==result['executionJobId'] and children==4
        if name=='restore-noop':assert before==after
    if baseline.get('restoreJobResults',False):
        from recursive_acl_restore_success_jobs_proof import terminal_proof
        ready_signal=json.loads((evidence/f'job-{name}-ready-result.json').read_bytes());initial=json.loads((evidence/f'job-{name}-ready.sql.json').read_bytes())
        facts=terminal_proof(signal,ready_signal,initial,data)
        acl.archive(evidence,f'job-{name}-proof.json',json.dumps(facts,sort_keys=True).encode(),read_only)
    return dict(signal=signal,before=before,after=after,children=children)


def retained(evidence,baseline,old,restored,storage,io):
    immutable(evidence,baseline,old,storage)
    for proof in restored:
        signal=proof['signal'];assert sql_snapshot(signal['result']['id'])==json.loads((evidence/(signal['name']+'.sql.json')).read_bytes())
        if signal['result']['executionJobId']:
            from recursive_acl_restore_business_proof import managed_plan
            data=json.loads((evidence/(signal['name']+'.sql.json')).read_bytes());request=dict(operationId=uuid.UUID(data['row']['execution_job_id']).hex,owner=data['row']['owner_hash'],plan=managed_plan(data['row']['plan_json']))
            read_bundle(io,request,True,evidence/'restore-records'/signal['name'])


def preserved_baseline(baseline,io):
    from validate_container_recursive_acl_faults import preservation_snapshot
    values=[(preservation_snapshot(io.storage),baseline['protectedBefore']),(snapshot(io.sourceState),baseline['sourceRootBefore']),(snapshot(io.restoreState),baseline['restoreRootBefore']),(hdd_snapshot(io,True),baseline['restoreHddBefore'])]
    for current,old in values:
        assert all(current.get(key)==value for key,value in old.items()),'OLD_RESTORE_GATE_RECORD_CHANGED'
    assert snapshot(Path('/var/lib/whaledeck-container-directories'))==baseline['directoryRootBefore']
    return dict(protected=len(values[0][0]),sourceRoot=len(values[1][0]),restoreRoot=len(values[2][0]),restoreHdd=len(values[3][0]),oldProtectedPreserved=len(baseline['protectedBefore']))


def exercise(process,evidence,baseline,proofs,reader,storage,root,content,module,current):
    io=installed();preserved_baseline(baseline,io);old={p['result']['name']:p for p in proofs};sources={name:source_proof(evidence,old[name],io,create=True) for name in {v[1] for v in SPECS}}
    (evidence/'source-physical-final.json').write_text(json.dumps(current));restored=[];subject=json.loads((evidence/'access.sql.json').read_bytes())['row']['actor_subject']
    for name,selected,_ in SPECS:
        retained(evidence,baseline,proofs,restored,storage,io);preserved_baseline(baseline,io)
        if baseline.get('restoreJobResults',False):
            from recursive_acl_restore_success_jobs_proof import ready_proof
            path=evidence/f'job-{name}-ready-result.json';deadline=time.monotonic()+150
            while not path.exists():
                assert process.poll() is None and time.monotonic()<deadline,'SUCCESS_JOBS_READY_DEADLINE';time.sleep(.1)
            acl.trusted(path);assert path.stat().st_size<=65536;ready_signal=json.loads(path.read_bytes());initial=sql_snapshot(ready_signal['id']);ready_proof(ready_signal,initial,name)
            assert initial['row']['actor_subject']==subject and initial['row']['container_id']==baseline['containerId'] and initial['row']['instance_id']==baseline['instanceId']
            typed_equal(ready_signal['source'],expected_summary(old[selected]['result'],sources[selected][0]['metadata']))
            from validate_recursive_acl_restore_paging import no_restore_records
            no_restore_records(ready_signal['id']);assert acl.inventory(root,reader,content,module)==current
            (evidence/f'job-{name}-ready.sql.json').write_text(json.dumps(initial))
            acknowledge(evidence,f'job-{name}-ready',dict(readySqlAndSourceVerified=True))
        signal=wait_case(process,evidence,name);assert signal['subject']==subject
        after=acl.inventory(root,reader,content,module)
        checked=verify_case(signal,baseline,old[selected]['result'],*sources[selected],current,after,evidence,io)
        restored.append(checked);current=after;(evidence/'restore-results.json').write_text(json.dumps(restored));(evidence/'physical-final.json').write_text(json.dumps(current))
        acknowledge(evidence,name,dict(id=signal['result']['id'],physicalRootHddAndSqlVerified=True))
    ordinary=wait_case(process,evidence,'restore-ordinary');assert ordinary['subject']!=subject and ordinary['deniedGet']==[403]*4 and ordinary['deniedPost']==[403]*4 and ordinary['anonymous']==[401]*4 and ordinary['uiRestoreRequests']==0
    assert ordinary['normalDualLogout']['normalUiLogout'] and ordinary['normalDualLogout']['providerNavigationObserved'] and ordinary['normalDualLogout']['bffStatus']==401 and ordinary['normalDualLogout']['identityStatus'] in (401,403)
    assert query("SELECT count(*) FROM audit_events WHERE actor_subject=:'subject' AND action LIKE 'containers.files.recursive-acl-restore.%';",subject=ordinary['subject'])==0
    if baseline.get('restoreJobResults',False):
        from recursive_acl_restore_success_jobs_proof import authority_proof
        authority_proof(ordinary['jobsAuthority'],ordinary,[p['signal']['result'] for p in restored]);typed_equal(ordinary['completedJobs'],[p['signal']['result'] for p in restored])
    retained(evidence,baseline,proofs,restored,storage,io);assert acl.inventory(root,reader,content,module)==current
    acknowledge(evidence,'restore-ordinary',dict(ordinaryAndAnonymousDenied=True))
    summary=dict(cases=5,completed=4,canceledBeforeExecution=1,newChildHdd=16,actualSqlRootHddPerCase=True,sourceCapturesPreserved=True,ordinaryDenied=8,anonymousDenied=4,realSourceSnapshotPagination=False)
    (evidence/'restore-preservation.json').write_text(json.dumps(preserved_baseline(baseline,io)));(evidence/'restore-verified.json').write_text(json.dumps(summary));return current

if __name__=='__main__':
    try:acl.main(restore=True)
    except BaseException as error:
        import traceback
        frame=next((f for f in reversed(traceback.extract_tb(error.__traceback__)) if f.filename.endswith('validate_container_recursive_acl_restore_browser.py')),None)
        print(json.dumps(dict(actualRecursiveAclRestoreBrowser='failed',phase=acl.PHASE,failureType=type(error).__name__,frame=frame.lineno if frame else None)),flush=True)
        raise SystemExit(1) from None
