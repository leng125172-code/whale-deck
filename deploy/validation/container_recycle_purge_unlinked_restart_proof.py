"""Fixed service controls reused unchanged; post-unlink lease facts are distinct."""
import copy,hashlib,json,time
from datetime import datetime,timezone,timedelta
from pathlib import Path
from container_recycle_purge_restart_proof import KINDS,TABLES,IMMUTABLE,restart,same_operation,lease_held,require_transients,restart_allowed
from validate_container_acl_faults import observation_expiry,require_host_restart
from validate_container_recycle_purge_browser import sql,hdd_observation
from validate_metric_continuity import query
from recycle_purge_unlinked_logout_proof import preserved
from recursive_acl_restore_agent_io import read_at
from acl_history_browser_proof import typed_equal
import container_recycle_purge_agent_io as purge_io
import container_recycle_agent_io as source_io


def result_audit(row):
    return query("SELECT json_build_object('count',count(*),'atUtc',min(occurred_at_utc),'actors',coalesce(json_agg(actor_subject),'[]'::json)) FROM audit_events WHERE job_id=:'job'::uuid AND action='containers.files.recycle-purge.result' AND result='Unknown'",job=row['execution_job_id'])


def originals(row,record,marker,source_root,cut,cut_source):
    assert record['operationId']==row['execution_job_id'].replace('-','') and record['owner']==row['owner_hash'];typed_equal(record['plan'],cut['plan'])
    assert record['state'] in ('Removing','Unknown') and record['pendingIdentity']==cut['pendingIdentity'] and record['unlinkConfirmed'] is False
    assert marker['state'] in ('Deleting','Unknown') and marker['operationId']==record['operationId'] and marker['owner']==record['owner'] and marker['fingerprint']==record['plan']['fingerprint'];typed_equal(marker['source'],record['plan']['command']['source'])
    typed_equal(source_root,cut_source);assert source_root['state']=='Purging' and source_root['actualIdentity'] is None


def natural_lease(row,fixture,e):
    began=time.monotonic();wall=datetime.now(timezone.utc);expiry=observation_expiry(row,wall);source=json.loads((e/'source-proof.json').read_text());op=source['request']['operationId'];q={k:json.loads((e/'cut-record.json').read_text())[k] for k in ('operationId','owner','plan')}
    root_path=purge_io.STATE/(q['operationId']+'.json');marker_path=source_io.STATE/(op+'.purge.json');source_path=source_io.STATE/(op+'.json')
    raw,record,_=read_at(None,str(root_path));marker_raw,marker,_=read_at(None,str(marker_path));source_raw,source_root,_=read_at(None,str(source_path));originals(row,record,marker,source_root,json.loads((e/'cut-record.json').read_text()),json.loads((e/'cut-source-root.json').read_text()))
    for name,value in [('after-helper-exit-root.json',raw),('after-helper-exit-marker.json',marker_raw),('after-helper-exit-source.json',source_raw)]:(e/name).write_bytes(value)
    samples=[]
    while datetime.now(timezone.utc)<expiry:
        assert time.monotonic()-began<100
        data=sql(row['id']);a=root_path.read_bytes();b=marker_path.read_bytes();c=source_path.read_bytes();physical=purge_io.physical(Path(fixture['folder']),q);hdd=hdd_observation(op);at=datetime.now(timezone.utc)
        if at>=expiry:break
        lease_held(row,data);assert a==raw and b==marker_raw and c==source_raw;assert physical['source'] is physical['retained'] is physical['pending'] is None;typed_equal(hdd,source['hdd'])
        samples.append(dict(atUtc=at.isoformat(),state='Applying',barrier=True,leaseToken=row['lease_token'],leaseExpiresAtUtc=row['lease_expires_at_utc'],threeNamesAbsent=True,rootSha256=hashlib.sha256(raw).hexdigest(),markerSha256=hashlib.sha256(marker_raw).hexdigest(),sourceSha256=hashlib.sha256(source_raw).hexdigest(),hddDataReread=True))
        time.sleep(min(2,max(.001,(expiry-at).total_seconds())))
    proof=dict(waitBeganAtUtc=wall.isoformat(),waitEndedAtUtc=datetime.now(timezone.utc).isoformat(),monotonicSeconds=time.monotonic()-began,originalLeaseToken=row['lease_token'],originalLeaseExpiresAtUtc=row['lease_expires_at_utc'],samples=samples,clocksOrSqlModified=False);assert len(samples)>=3;(e/'natural-lease.json').write_text(json.dumps(proof))


def verify_lease(row,audit,proof,raw,marker,source):
    expiry=datetime.fromisoformat(row['lease_expires_at_utc']);began=datetime.fromisoformat(proof['waitBeganAtUtc']);ended=datetime.fromisoformat(proof['waitEndedAtUtc'])
    assert timedelta(seconds=25)<expiry-began<=timedelta(seconds=95) and ended>=expiry
    assert audit['count']==1 and audit['actors']==[row['actor_subject']] and datetime.fromisoformat(audit['atUtc'])>=expiry
    assert proof['originalLeaseToken']==row['lease_token'] and proof['originalLeaseExpiresAtUtc']==row['lease_expires_at_utc'] and proof['clocksOrSqlModified'] is False
    assert abs((ended-began).total_seconds()-proof['monotonicSeconds'])<3 and proof['monotonicSeconds']<100 and len(proof['samples'])>=3
    previous=began
    for s in proof['samples']:
        at=datetime.fromisoformat(s['atUtc']);assert previous<=at<expiry and (at-previous).total_seconds()<5;previous=at
        assert s['state']=='Applying' and s['barrier'] and s['leaseToken']==row['lease_token'] and s['leaseExpiresAtUtc']==row['lease_expires_at_utc'] and s['threeNamesAbsent'] and s['hddDataReread']
        for key,value in [('rootSha256',raw),('markerSha256',marker),('sourceSha256',source)]:assert s[key]==hashlib.sha256(value).hexdigest()
    assert (ended-previous).total_seconds()<5


def preserved_restart(before,after,source_op,purge_op,service,kind):
    assert kind in KINDS and service['kind']==kind and service['otherActiveWork']['transientScopeVerified'] and set(service['otherActiveWork']['counts'])==set(TABLES)
    assert all(type(v) is int and v==0 for key in ('checks','counts') for v in service['otherActiveWork'][key].values())
    typed_equal(service['hostsBefore'],before['hosts']);typed_equal(service['hostsAfter'],after['hosts']);expected=copy.deepcopy(before)
    if kind=='worker-crash':
        a,b=before['core']['/whaledeck-worker'],after['core']['/whaledeck-worker'];assert a[:3]==b[:3] and a[2]=='running' and a[3]==service['beforeStartedAt'] and b[3]==service['afterStartedAt'] and a[3]!=b[3]
        assert service['sameWorkerContainerAndImage'] and service['workerId']==a[0] and service['imageId']==a[1] and service['signal']=='SIGKILL';expected['core']['/whaledeck-worker']=b
    else:require_host_restart(before['hosts'],after['hosts'],before['hosts']['whaledeck-agent.service']['sourceRevision']);expected['hosts']=after['hosts']
    preserved(expected,after,source_op,purge_op)


def verify_restart(e,fixture,final,before,after):
    read=lambda n:json.loads((e/n).read_text());service=read('service-restart.json');start=read('restart-start-sql.json');row=start['row'];same_operation(read('cut-sql.json')['row'],row);same_operation(row,final);lease_held(row,start)
    assert read('service-fault-attempt.json')==dict(kind=fixture['kind'],id=row['id'],executionJobId=row['execution_job_id'],scopeVerified=True)
    source=read('source-proof.json');preserved_restart(before,after,source['request']['operationId'],final['execution_job_id'].replace('-',''),service,fixture['kind'])
    x=read('helper-exit.json');assert 0<x['elapsedSeconds']<20 and x['observedWithOriginalPidfd'] and x['measurementBeginsBeforeServiceFault']
    audit=result_audit(row);typed_equal(audit,read('unknown-result-audit.json'));assert audit['count']==1
    settled=read('restart-settled.json');assert settled['id']==row['id'] and settled['executionJobId']==row['execution_job_id'] and settled['state']=='Unknown' and datetime.fromisoformat(settled['observedAtUtc'])>=datetime.fromisoformat(audit['atUtc'])
    if fixture['kind']=='worker-crash':
        lease_held(row,read('after-worker-crash-sql.json'));raw=(e/'after-helper-exit-root.json').read_bytes();marker=(e/'after-helper-exit-marker.json').read_bytes();src=(e/'after-helper-exit-source.json').read_bytes();originals(row,json.loads(raw),json.loads(marker),json.loads(src),read('cut-record.json'),read('cut-source-root.json'));verify_lease(row,audit,read('natural-lease.json'),raw,marker,src)
    else:
        samples=read('maintenance-observations.json');times=[v['atUtc'] for v in samples];began=service['restartBeganAtUtc'];ended=service['restartEndedAtUtc']
        assert len(samples)==service['maintenanceSamples']>=3 and times==sorted(times) and times[0]<=began<ended<=times[-1] and any(began<t<ended for t in times)
        assert all(v['httpStatus']==200 and v['state']=='healthy' and v['phase']=='Ready' and v['noStore'] for v in samples)
