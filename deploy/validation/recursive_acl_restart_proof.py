"""QA-only fixed Worker/Agent faults and read-only natural-lease witnesses."""
from concurrent.futures import ThreadPoolExecutor
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import re
import threading
import time

import requests
from bound_container_recursive_acl_cut import record_for
from validate_container_recursive_acl_agent import STATE
from validate_container_recursive_acl_browser import snapshot, managed_plan
from validate_metric_continuity import query
from validate_container_directory_restart import command, preflight, agent_identity
from validate_container_acl_faults import host_snapshot, require_host_restart, observation_expiry, maintenance_sample, observe_maintenance
from validate_container_recursive_permission_faults import same_operation

RESTART_KINDS=('worker-crash','agent-restart')
OWNER_PATTERN=r'(?:wd-qa-recursive-acl-restarts-[a-f0-9]{7}|wd-qa-recursive-acl-partial-restarts-[a-f0-9]{7}-(?:worker|agent)|wd-qa-recursive-acl-partial-instance-[a-f0-9]{7})\.service'
RESTART_KEYS={'uploads','directories','copies','moves','permissions','ownerships','recursivePermissions','recursiveOwnerships','acls','recursiveAcls'}


def require_only_transients(active, helper, owner):
    assert re.fullmatch('whaledeck-container-recursive-acl-[a-f0-9]{32}\\.service',helper)
    assert re.fullmatch(OWNER_PATTERN,owner)
    assert set(active)<={helper,owner},'OTHER_TRANSIENT_WORK_RESTART_REFUSED'


def owner_unit():
    group=Path('/proc/self/cgroup').read_text().strip();assert re.fullmatch('0::/system.slice/'+OWNER_PATTERN,group)
    unit=group.rsplit('/',1)[1];invocation=os.environ.get('INVOCATION_ID','');assert re.fullmatch('[a-f0-9]{32}',invocation)
    props=dict(v.split('=',1) for v in command('systemctl','show',unit,'-p','InvocationID','-p','MainPID','-p','ControlGroup').splitlines())
    assert props['InvocationID']==invocation and props['MainPID']==str(os.getpid()) and group=='0::'+props['ControlGroup']
    return unit


def restart_allowed(held,row):
    checks=preflight([row['id'],row['execution_job_id']])
    values=query('''SELECT json_build_object(
      'uploads',(SELECT count(*) FROM container_file_upload_operations WHERE completed_at_utc IS NULL OR publish_barrier OR lease_token IS NOT NULL OR lease_expires_at_utc IS NOT NULL),
      'directories',(SELECT count(*) FROM container_directory_operations WHERE completed_at_utc IS NULL OR publish_barrier OR lease_token IS NOT NULL OR lease_expires_at_utc IS NOT NULL),
      'copies',(SELECT count(*) FROM container_copy_intents WHERE completed_at_utc IS NULL OR publish_barrier OR lease_token IS NOT NULL OR lease_expires_at_utc IS NOT NULL),
      'moves',(SELECT count(*) FROM container_move_intents WHERE completed_at_utc IS NULL OR publish_barrier OR lease_token IS NOT NULL OR lease_expires_at_utc IS NOT NULL),
      'permissions',(SELECT count(*) FROM container_permission_intents WHERE completed_at_utc IS NULL OR publish_barrier OR lease_token IS NOT NULL OR lease_expires_at_utc IS NOT NULL),
      'ownerships',(SELECT count(*) FROM container_ownership_intents WHERE completed_at_utc IS NULL OR publish_barrier OR lease_token IS NOT NULL OR lease_expires_at_utc IS NOT NULL),
      'recursivePermissions',(SELECT count(*) FROM container_recursive_permission_intents WHERE completed_at_utc IS NULL OR publish_barrier OR lease_token IS NOT NULL OR lease_expires_at_utc IS NOT NULL),
      'recursiveOwnerships',(SELECT count(*) FROM container_recursive_ownership_intents WHERE completed_at_utc IS NULL OR publish_barrier OR lease_token IS NOT NULL OR lease_expires_at_utc IS NOT NULL),
      'acls',(SELECT count(*) FROM container_acl_intents WHERE completed_at_utc IS NULL OR publish_barrier OR lease_token IS NOT NULL OR lease_expires_at_utc IS NOT NULL),
      'recursiveAcls',(SELECT count(*) FROM container_recursive_acl_intents WHERE (completed_at_utc IS NULL OR publish_barrier OR lease_token IS NOT NULL OR lease_expires_at_utc IS NOT NULL) AND id<>:'id'::uuid));''',id=row['id'])
    assert set(values)==RESTART_KEYS and all(type(n) is int and n==0 for n in values.values()),'OTHER_FILE_WORK_RESTART_REFUSED'
    owner=owner_unit();active=[]
    for path in Path('/run/systemd/transient').glob('*.service'):
        if not path.name.startswith(('whaledeck-','wd-qa-')):continue
        props=dict(v.split('=',1) for v in command('systemctl','show',path.name,'-p','ActiveState','-p','MainPID','-p','ControlPID','-p','ControlGroup').splitlines())
        if props['ActiveState'] in ('inactive','failed'):
            assert props['MainPID']==props['ControlPID']=='0'
            if props['ControlGroup']:
                processes=Path('/sys/fs/cgroup')/props['ControlGroup'].lstrip('/')/'cgroup.procs'
                assert not processes.exists() or not processes.read_text().split()
        else:active.append(path.name)
    require_only_transients(active,held.cgroup,owner)
    return {**checks,**values}


def lease_held(before,current):
    same_operation(before,current)
    for key in ('login_binding','lease_token','lease_expires_at_utc'):assert current[key]==before[key]
    assert current['state']=='Applying' and current['publish_barrier'] and current['completed_at_utc'] is current['cancel_requested_at_utc'] is None


def natural_lease(row,current_tree,original,directory):
    wall=dt.datetime.now(dt.timezone.utc);expiry=observation_expiry(row,wall);began=time.monotonic()
    path=STATE/(row['execution_job_id'].replace('-','')+'.json');raw=path.read_bytes();record=json.loads(raw)
    assert record==record_for(row) and record['state'] in ('Applying','Aborted') and record['historyVerified'] and record['changedCount']==0
    (directory/'after-helper-exit-root.json').write_bytes(raw);samples=[]
    while dt.datetime.now(dt.timezone.utc)<expiry:
        assert time.monotonic()-began<100,'NATURAL_LEASE_MONOTONIC_DEADLINE'
        data=snapshot(row['id']);observed=dt.datetime.now(dt.timezone.utc)
        if observed>=expiry:break
        lease_held(row,data['row']);assert data['executionJob']['state']=='Running' and data['executionJob']['cancel_requested_at_utc'] is None
        assert path.read_bytes()==raw and current_tree()==original
        samples.append(dict(atUtc=observed.isoformat(),state='Applying',barrier=True,leaseToken=row['lease_token'],leaseExpiresAtUtc=row['lease_expires_at_utc'],rootSha256=hashlib.sha256(raw).hexdigest(),sourceFullBytesAclsAndAttributesUnchanged=True))
        time.sleep(min(2,max(.001,(expiry-observed).total_seconds())))
    ended=dt.datetime.now(dt.timezone.utc);elapsed=time.monotonic()-began
    proof=dict(originalLeaseToken=row['lease_token'],originalLeaseExpiresAtUtc=row['lease_expires_at_utc'],waitBeganAtUtc=wall.isoformat(),waitEndedAtUtc=ended.isoformat(),monotonicWaitSeconds=round(elapsed,3),rootSha256=hashlib.sha256(raw).hexdigest(),rootStateAfterHelperExit=record['state'],samples=samples,clocksOrSqlModified=False)
    assert len(samples)>=3 and abs((ended-wall).total_seconds()-elapsed)<3
    (directory/'natural-lease.json').write_text(json.dumps(proof))
    return proof


def restart(kind,held,worker,row,revision,directory,attempt):
    assert kind in RESTART_KINDS
    checks=restart_allowed(held,row);hosts=host_snapshot(revision);attempt['hostsBefore']=hosts
    data=snapshot(row['id']);row=data['row'];assert row['state']=='Applying' and row['publish_barrier']
    observation_expiry(row,dt.datetime.now(dt.timezone.utc));(directory/'restart-start-sql.json').write_text(json.dumps(data))
    if kind=='worker-crash':
        before=worker.current();began=time.monotonic()
        with ThreadPoolExecutor(max_workers=1) as pool:
            exiting=pool.submit(held.wait_exit_seconds,began,15)
            worker.stopped=True;command('docker','kill','--signal','KILL',worker.id)
            crash=snapshot(row['id']);lease_held(row,crash['row']);(directory/'after-worker-crash-sql.json').write_text(json.dumps(crash))
            automatic=worker.current()['State']['Running'];worker.start(wait_ready=False);after=worker.current()
            assert before['State']['StartedAt']!=after['State']['StartedAt']
            elapsed=exiting.result(timeout=16)
        witness=dict(observedAtUtc=dt.datetime.now(dt.timezone.utc).isoformat(),containerId=after['Id'],imageId=after['Image'],startedAt=after['State']['StartedAt'],running=after['State']['Running'])
        (directory/'worker-inspect.json').write_text(json.dumps(witness))
        service=dict(sameWorkerContainerAndImage=True,workerId=worker.id,signal='SIGKILL',manuallyStarted=not automatic,beforeStartedAt=before['State']['StartedAt'],afterStartedAt=after['State']['StartedAt'],originalLeaseExpiresAtUtc=row['lease_expires_at_utc'],otherActiveWork=checks,hostsBefore=hosts,hostsAfter=host_snapshot(revision))
        assert service['hostsBefore']==service['hostsAfter']
    else:
        stop=threading.Event();ready=threading.Event()
        with ThreadPoolExecutor(max_workers=2) as pool:
            monitor=pool.submit(observe_maintenance,stop,ready)
            try:
                assert ready.wait(5),'MAINTENANCE_PRE_RESTART_PROBE_MISSING'
                began=time.monotonic();started=dt.datetime.now(dt.timezone.utc);exiting=pool.submit(held.wait_exit_seconds,began,15)
                attempt['agentRestartAttempted']=True;command('systemctl','restart','whaledeck-agent.service')
                agent_identity(str(hosts['whaledeck-agent.service']['pid']),revision);after=host_snapshot(revision);require_host_restart(hosts,after,revision)
                ended=dt.datetime.now(dt.timezone.utc)
                with requests.Session() as client:
                    client.trust_env=False;last=maintenance_sample(client)
                elapsed=exiting.result(timeout=16)
            finally:stop.set()
            samples=monitor.result(timeout=5)+[last];samples.sort(key=lambda v:v['atUtc'])
        (directory/'maintenance-observations.json').write_text(json.dumps(samples))
        service=dict(hostsBefore=hosts,hostsAfter=after,maintenancePidUnchanged=True,otherActiveWork=checks,restartBeganAtUtc=started.isoformat(),restartEndedAtUtc=ended.isoformat(),totalRestartSeconds=round(time.monotonic()-began,3),maintenanceSamples=len(samples))
    return elapsed,service,row


def verify_lease(row,final,proof,raw):
    expiry=dt.datetime.fromisoformat(proof['originalLeaseExpiresAtUtc']);began=dt.datetime.fromisoformat(proof['waitBeganAtUtc']);ended=dt.datetime.fromisoformat(proof['waitEndedAtUtc'])
    assert all(v.utcoffset() is not None for v in (expiry,began,ended)) and dt.timedelta(seconds=25)<expiry-began<=dt.timedelta(seconds=95)
    assert ended>=expiry and abs((ended-began).total_seconds()-proof['monotonicWaitSeconds'])<3 and proof['monotonicWaitSeconds']<100
    assert dt.datetime.fromisoformat(final['completed_at_utc'])>=expiry and proof['clocksOrSqlModified'] is False
    lease_held(row,row);assert row['lease_token']==proof['originalLeaseToken'] and row['lease_expires_at_utc']==proof['originalLeaseExpiresAtUtc']
    record=json.loads(raw);sha=hashlib.sha256(raw).hexdigest();assert record['state']==proof['rootStateAfterHelperExit'] in ('Applying','Aborted') and record['historyVerified'] and record['changedCount']==0
    assert proof['rootSha256']==sha and len(proof['samples'])>=3
    previous=began
    for sample in proof['samples']:
        at=dt.datetime.fromisoformat(sample['atUtc']);assert previous<=at<expiry and (at-previous).total_seconds()<5;previous=at
        assert sample['state']=='Applying' and sample['barrier'] is True and sample['leaseToken']==row['lease_token'] and sample['leaseExpiresAtUtc']==row['lease_expires_at_utc']
        assert sample['rootSha256']==sha and sample['sourceFullBytesAclsAndAttributesUnchanged'] is True
    assert (ended-previous).total_seconds()<5


def verify_restart_service(kind,service,baseline,final,directory,read):
    """Shared fixed service identity/guard proof, independent of write outcome."""
    assert kind in RESTART_KINDS
    assert RESTART_KEYS<=service['otherActiveWork'].keys() and all(type(v) is int and v==0 for v in service['otherActiveWork'].values())
    start=read(directory/'restart-start-sql.json');row=start['row'];same_operation(row,final);assert final['login_binding']==row['login_binding']
    assert row['state']=='Applying' and row['publish_barrier'] and start['executionJob']['state']=='Running'
    if kind=='worker-crash':
        assert service['sameWorkerContainerAndImage'] and service['signal']=='SIGKILL' and type(service['manuallyStarted']) is bool
        witness=read(directory/'worker-inspect.json');original=baseline['coreBefore']['/whaledeck-worker']
        assert witness['containerId']==service['workerId']==original[0] and witness['imageId']==original[1] and witness['running'] is True
        assert witness['startedAt']==service['afterStartedAt'] and service['beforeStartedAt']!=service['afterStartedAt'] and service['hostsBefore']==service['hostsAfter']
        before,after,at=[dt.datetime.fromisoformat(v) for v in (service['beforeStartedAt'],service['afterStartedAt'],witness['observedAtUtc'])];assert dt.datetime.fromisoformat(original[3])<=before<after<=at
        crash=read(directory/'after-worker-crash-sql.json');lease_held(row,crash['row']);assert crash['executionJob']['state']=='Running'
        assert service['originalLeaseExpiresAtUtc']==row['lease_expires_at_utc']
    else:
        require_host_restart(service['hostsBefore'],service['hostsAfter'],baseline['revision']);assert service['maintenancePidUnchanged']
        began=dt.datetime.fromisoformat(service['restartBeganAtUtc']);ended=dt.datetime.fromisoformat(service['restartEndedAtUtc']);observation_expiry(row,began)
        samples=read(directory/'maintenance-observations.json');times=[dt.datetime.fromisoformat(v['atUtc']) for v in samples]
        assert len(samples)==service['maintenanceSamples']>=3 and times==sorted(times) and times[0]<=began<ended<=times[-1]
        assert any(began<t<ended for t in times) and all((b-a).total_seconds()<5 for a,b in zip(times,times[1:]))
        assert 0<=(ended-began).total_seconds()<=service['totalRestartSeconds']+1
        assert all(v['httpStatus']==200 and v['state']=='healthy' and v['phase']=='Ready' and v['noStore'] and 0<v['bytes']<=2048 and 0<=v['responseSeconds']<5 for v in samples)
    return row


def verify_restart(kind,result,baseline,ui,final,directory,read):
    assert kind in RESTART_KINDS and ui['normalDualLogout'] and ui['cancellations']==0 and final['cancel_requested_at_utc'] is None
    row=verify_restart_service(kind,result['serviceRestart'],baseline,final,directory,read)
    intent=ui['terminal'];assert intent['id']==final['id'] and intent['executionJobId']==final['execution_job_id'] and intent['state']=='Failed' and intent['resultCode']=='CONTAINER_RECURSIVE_ACL_INTERRUPTED'
    assert intent['confirmation'] is None and intent['receipt']['state']=='Aborted' and intent['receipt']['changedCount']==intent['receipt']['unknownCount']==0 and intent['receipt']['historyVerified'] is True
    if kind=='worker-crash':
        raw=(directory/'after-helper-exit-root.json').read_bytes();root=json.loads(raw)
        assert root['operationId']==final['execution_job_id'].replace('-','') and root['owner']==final['owner_hash']
        assert root['plan']==managed_plan(json.loads(final['plan_json'])) and root['historyDigest']==final['history_digest']
        verify_lease(row,final,read(directory/'natural-lease.json'),raw)
