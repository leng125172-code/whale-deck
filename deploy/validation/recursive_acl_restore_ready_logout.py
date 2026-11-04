"""Ready logout: immutable plan, fresh-login isolation, natural expiry, no writes."""
import datetime as dt
import json
from pathlib import Path
import time
from collections import Counter

from acl_history_browser_proof import typed_equal
from recursive_acl_restore_business_proof import managed_plan,metadata,json_projection
from recursive_acl_restore_paging_proof import page_proof,ready_proof
from recursive_acl_history_paging_proof import summary,verify_capture,inventory
from recursive_acl_history_browser_proof import prove_pages,prove_audits
from validate_container_recursive_acl_history_browser import audit_rows,acknowledge
from validate_container_recursive_acl_restore_browser import sql_snapshot
from validate_container_upload_recovery_agent import trusted

SOURCES=('validate_recursive_acl_restore_ready_logout.py','validate_recursive_acl_restore_ready_logout.mjs','recursive_acl_restore_ready_logout.py','verify_recursive_acl_restore_ready_logout.py','test_recursive_acl_restore_ready_logout.py')
VOLATILE={'state','result_code','completed_at_utc','version','lease_token','lease_expires_at_utc','next_attempt_at_utc'}


def timestamp(value):
    assert type(value) is str and len(value)<=80
    parsed=dt.datetime.fromisoformat(value.replace('Z','+00:00'));assert parsed.tzinfo is not None
    return parsed.timestamp()


def wait_signal(process,evidence,name):
    assert name in ('ready','logout','final-auth')
    path=evidence/(name+'-result.json');deadline=time.monotonic()+150
    while not path.exists():
        assert process.poll() is None,'READY_LOGOUT_BROWSER_EXITED'
        assert time.monotonic()<deadline,'READY_LOGOUT_BROWSER_DEADLINE';time.sleep(.1)
    trusted(path);assert path.stat().st_size<=262144
    return json.loads(path.read_bytes())


def observation(data,elapsed):
    row=data['row'];assert all(type(data[k]) is int and data[k]>=0 for k in ('pending','accepted','confirmed','events'));assert data['confirmed']==0 and data['executionJob'] is None and row['execution_job_id'] is None
    for key in ('root_started_at_utc','cancel_requested_at_utc','confirmed_at_utc','execution_expires_at_utc','execution_hash','root_state','receipt_json','receipt_hash','history_digest','completed_count','changed_count','aborted_count','not_started_count','unknown_count','outcome_digest','binding_current','history_available'):
        assert row[key] is None,key
    assert row['history_may_exist'] is row['history_verified'] is row['publish_barrier'] is False
    assert type(row['version']) is int and type(elapsed) in (float,int) and elapsed>=0
    return dict(elapsedMs=round(elapsed*1000),observedUnixMs=round(time.time()*1000),state=row['state'],version=row['version'],completed=row['completed_at_utc'],executionJobId=None,confirmed=0,rootStarted=False,publishBarrier=False)


def expired_proof(data,initial,b,row,source_metadata):
    old=initial['row'];r=data['row'];assert old['state']=='Ready' and r['state']=='Expired' and r['result_code']=='CONTAINER_RECURSIVE_ACL_RESTORE_PLAN_EXPIRED'
    assert r.keys()==old.keys()
    for key in r.keys()-VOLATILE:typed_equal(r[key],old[key])
    observation(data,0)
    assert timestamp(r['completed_at_utc'])>=timestamp(old['ready_expires_at_utc'])
    assert r['lease_token'] is r['lease_expires_at_utc'] is None and r['version']>old['version']
    assert data['pending']==0 and data['accepted']==1 and data['events']>=initial['events']
    typed_equal(data['planJob'],initial['planJob']);assert data['planJob']['state']=='Succeeded' and data['planJob']['job_type']=='containers.files.recursive-acl-restore-plan'
    typed_equal(json_projection(data['planJob']['request_json']),metadata(r))
    plan=managed_plan(r['plan_json']);typed_equal(plan['restoreSource'],summary(row,source_metadata))
    assert r['actor_subject']==initial['row']['actor_subject'] and r['container_id']==b['containerId'] and r['instance_id']==b['instanceId'] and r['relative_path']=='work/recursive-tree'
    wanted=Counter({('plan-accepted','Accepted'):1,('plan-result','Ready'):1,('plan-result','Expired'):1});observed=Counter();ids=set()
    for audit in data['audits']:
        assert audit['id'] not in ids;ids.add(audit['id'])
        assert audit['actor_subject']==r['actor_subject'] and audit['target_type']=='container' and audit['target_id']==r['container_id'] and audit['job_id']==r['id']
        assert type(audit['trace_id']) is str and 0<len(audit['trace_id'])<=128
        action=audit['action'];prefix='containers.files.recursive-acl-restore.';assert action.startswith(prefix)
        observed[(action.removeprefix(prefix),audit['result'])]+=1;typed_equal(json_projection(audit['detail_json']),metadata(r))
    assert observed==wanted
    return plan


def samples_proof(samples,initial,final):
    assert type(samples) is list and 2<=len(samples)<=240
    expires=timestamp(initial['row']['ready_expires_at_utc'])*1000
    assert samples[0]['state']=='Ready' and samples[0]['observedUnixMs']<expires-30000
    previous=-1
    for index,s in enumerate(samples):
        assert type(s['observedUnixMs']) is type(s['version']) is type(s['confirmed']) is int
        assert type(s['elapsedMs']) is int and s['elapsedMs']>=previous;previous=s['elapsedMs']
        assert s['state']==('Expired' if index==len(samples)-1 else 'Ready')
        assert s['executionJobId'] is None and s['confirmed']==0 and s['rootStarted'] is s['publishBarrier'] is False
        assert s['completed']==(final['row']['completed_at_utc'] if index==len(samples)-1 else None)
    assert samples[-1]['observedUnixMs']>=expires and samples[-1]['elapsedMs']>=30000


def authority_proof(logout,final,subject):
    assert logout['subject']==logout['reloginSubject']==final['subject']==subject
    assert logout['anonymous']==[401]*6 and logout['anonymousConfirmation']==401
    assert logout['newLoginLookups']==[404,404] and logout['newLoginConfirmation']==404
    assert logout['hintsClearedAfterLogout'] is logout['hintsAbsentAfterRelogin'] is True and logout['oldTaskButtons']==0
    assert final['registrations']==1 and final['confirmations']==final['cancellations']==final['uploads']==0
    assert final['ordinary']['subject']!=subject and final['ordinary']['denied']==[403]*6 and final['ordinary']['anonymous']==[401]*6 and final['ordinary']['confirmation']==403 and final['ordinary']['uiRequests']==0
    assert final['adminAnonymous']==[401]*6 and final['result']['state']=='Expired' and final['result']['executionJobId'] is final['result']['receipt'] is final['result']['confirmation'] is None
    for value in (logout['logout'],final['adminLogout'],final['ordinary']['logout']):
        assert value['normalUiLogout'] and value['providerNavigationObserved'] and value['bffStatus']==401 and value['identityStatus'] in (401,403) and value['finalLoginPage']


def exercise(process,evidence,b,row,m,root,reader,content,acl,storage):
    import validate_recursive_acl_restore_paging as gate
    gate.PHASE='real-ready-before-logout';signal=wait_signal(process,evidence,'ready')
    page_proof(signal,b,row,m,0);page_proof(signal['selected'],b,row,m,0,selection=True)
    initial=sql_snapshot(signal['ready']['id']);ready_proof(signal,b,row,m,initial);observation(initial,0);gate.no_restore_records(signal['ready']['id'])
    (evidence/'ready.sql.json').write_text(json.dumps(initial))
    prove_pages(signal['pages'],b,{row['request']['operationId']:summary(row,m)})
    audits=audit_rows(signal['subject']);prove_audits(audits,signal['subject'],b,signal['pages'],[signal['selected'],signal]);(evidence/'history-audits.json').write_text(json.dumps(audits))
    assert inventory(root,reader,content,acl)==row['after'] and verify_capture(evidence,b,row,storage)==m;gate.unchanged(b,storage)
    acknowledge(evidence,'ready',dict(readySqlPhysicalSourceVerified=True))
    gate.PHASE='same-subject-new-login-isolation';logout=wait_signal(process,evidence,'logout');assert logout['subject']==signal['subject']
    mid=sql_snapshot(signal['ready']['id']);typed_equal(mid,initial);(evidence/'post-logout.sql.json').write_text(json.dumps(mid))
    gate.PHASE='natural-ready-deadline';started=time.monotonic();samples=[];deadline=timestamp(initial['row']['ready_expires_at_utc'])+60
    while True:
        current=sql_snapshot(signal['ready']['id']);samples.append(observation(current,time.monotonic()-started));gate.no_restore_records(signal['ready']['id'])
        (evidence/'natural-expiry-observations.json').write_text(json.dumps(samples))
        if current['row']['completed_at_utc'] is not None:break
        assert process.poll() is None and len(samples)<240 and time.time()<deadline
        for key in initial['row'].keys()-VOLATILE:typed_equal(current['row'][key],initial['row'][key])
        assert current['row']['state']=='Ready'
        if len(samples)%5==0:
            assert inventory(root,reader,content,acl)==row['after'] and verify_capture(evidence,b,row,storage)==m;gate.unchanged(b,storage)
        time.sleep(2)
    expired_proof(current,initial,b,row,m);samples_proof(samples,initial,current);(evidence/'expired.sql.json').write_text(json.dumps(current))
    assert inventory(root,reader,content,acl)==row['after'];gate.unchanged(b,storage)
    acknowledge(evidence,'expired',dict(naturalExpiryWithoutExecution=True))
    gate.PHASE='final-identity-denials';final=wait_signal(process,evidence,'final-auth');authority_proof(logout,final,signal['subject'])
    assert final['readySignal']==signal and final['logoutSignal']==logout and final['result']['id']==signal['ready']['id'] and final['result']['version']==current['row']['version']
    typed_equal(final['result']['restoreSource'],summary(row,m));assert sql_snapshot(signal['ready']['id'])==current
    assert audit_rows(final['ordinary']['subject'])==[]
    from validate_metric_continuity import query
    assert query("SELECT count(*) FROM audit_events WHERE actor_subject=:'subject' AND action LIKE 'containers.files.recursive-acl-restore.%';",subject=final['ordinary']['subject'])==0
    assert gate.restore_database()==dict(intents=b['restoreDatabase']['intents']+1,executions=b['restoreDatabase']['executions'],active=0)
    assert inventory(root,reader,content,acl)==row['after'];verify_capture(evidence,b,row,storage);gate.unchanged(b,storage)
    acknowledge(evidence,'final-auth',dict(identitiesAndNaturalExpiryVerified=True));assert process.wait(timeout=90)==0
