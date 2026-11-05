"""Post-return Worker fault: fixed service guard plus naturally expiring original lease."""
import copy,hashlib,json,time
from datetime import datetime,timezone
from pathlib import Path
from container_recycle_restore_restart_proof import restart,lease_held,same_operation,verify_lease,TABLES
from validate_container_acl_faults import observation_expiry,require_host_restart
from validate_container_recycle_restore_browser import sql
from validate_container_recycle_restore_postreturn_cancel import preserved
from acl_history_browser_proof import typed_equal
from recursive_acl_restore_agent_io import read_at
import container_recycle_agent_io as source_io
import container_recycle_restore_agent_io as restore_io
KINDS=('worker-crash',)


def current_source(source_op):
    _,root,_=read_at(None,str(source_io.STATE/(source_op+'.json')))
    with source_io.namespace(restore_io.installed().storage) as fd:
        _,history,_=read_at(fd,source_op+'.json')
        import os
        data=source_io.file_snapshot(Path(os.readlink('/proc/self/fd/'+str(fd)))/(source_op+'.data'))
    return dict(sourceRoot=root,history=history,data=data)


def natural_lease(row,fixture,e):
    began=time.monotonic();wall=datetime.now(timezone.utc);expiry=observation_expiry(row,wall)
    pending=json.loads((e/'cut-source-proof.json').read_text());source_op=pending['root']['plan']['command']['source']['operationId']
    path=restore_io.STATE/(row['execution_job_id'].replace('-','')+'.json');raw,record,_=read_at(None,str(path))
    assert record['state'] in ('Applying','Completed') and record['claimed'] is record['historyVerified'] is True
    claim_path=restore_io.STATE/'claims'/(source_op+'.json');claim_raw,_,_=read_at(None,str(claim_path))
    (e/'after-helper-exit-root.json').write_bytes(raw);(e/'after-helper-exit-claim.json').write_bytes(claim_raw)
    physical=source_io.physical(Path(fixture['folder']),source_op);assert physical['source'] is not None and physical['retained'] is None
    samples=[]
    while datetime.now(timezone.utc)<expiry:
        assert time.monotonic()-began<100
        data=sql(row['id']);at=datetime.now(timezone.utc)
        if at>=expiry:break
        lease_held(row,data);assert path.read_bytes()==raw and claim_path.read_bytes()==claim_raw
        typed_equal(source_io.physical(Path(fixture['folder']),source_op),physical)
        actual=current_source(source_op)
        for key in ('sourceRoot','history','data'):typed_equal(actual[key],pending[key])
        samples.append(dict(atUtc=at.isoformat(),state='Applying',barrier=True,leaseToken=row['lease_token'],leaseExpiresAtUtc=row['lease_expires_at_utc'],sourceUnchanged=True,rootSha256=hashlib.sha256(raw).hexdigest(),claimSha256=hashlib.sha256(claim_raw).hexdigest(),hddDataReread=True,originalStillReturned=True))
        time.sleep(min(2,max(.001,(expiry-at).total_seconds())))
    result=dict(waitBeganAtUtc=wall.isoformat(),waitEndedAtUtc=datetime.now(timezone.utc).isoformat(),monotonicSeconds=time.monotonic()-began,originalLeaseToken=row['lease_token'],originalLeaseExpiresAtUtc=row['lease_expires_at_utc'],samples=samples,clocksOrSqlModified=False)
    assert len(samples)>=3;(e/'natural-lease.json').write_text(json.dumps(result))


def preserved_restart(before, after, source_op, restore_op, service, kind):
    assert kind=='worker-crash' and service['kind'] == kind and service['otherActiveWork']['transientScopeVerified']
    assert all(type(v) is int and v == 0 for k in ('checks', 'counts') for v in service['otherActiveWork'][k].values())
    assert set(service['otherActiveWork']['counts']) == set(TABLES)
    expected = copy.deepcopy(before)
    typed_equal(service['hostsBefore'], before['hosts']); typed_equal(service['hostsAfter'], after['hosts'])
    if kind == 'worker-crash':
        a, b = before['core']['/whaledeck-worker'], after['core']['/whaledeck-worker']
        assert a[:3] == b[:3] and a[2] == 'running' and a[3] == service['beforeStartedAt'] and b[3] == service['afterStartedAt'] and a[3] != b[3]
        assert service['sameWorkerContainerAndImage'] and service['workerId'] == a[0] and service['imageId'] == a[1] and service['signal'] == 'SIGKILL'
        expected['core']['/whaledeck-worker'] = b
    else:
        revision = before['hosts']['whaledeck-agent.service']['sourceRevision']; require_host_restart(before['hosts'], after['hosts'], revision)
        expected['hosts'] = after['hosts']
    preserved(expected, after, source_op, restore_op)


def verify_restart(e,fixture,final,before,after):
    read=lambda n:json.loads((e/n).read_text());service=read('service-restart.json');row=read('restart-start-sql.json')['row'];same_operation(row,final);lease_held(row,read('restart-start-sql.json'))
    source=read('source-proof.json');preserved_restart(before,after,source['request']['operationId'],final['execution_job_id'].replace('-',''),service,fixture['kind'])
    exited=read('helper-exit.json');assert exited['observedWithOriginalPidfd'] and exited['measurementBeginsBeforeServiceFault'] and 0<exited['elapsedSeconds']<20
    lease_held(row,read('after-worker-crash-sql.json'));raw=(e/'after-helper-exit-root.json').read_bytes();record=json.loads(raw)
    assert record['operationId']==final['execution_job_id'].replace('-','') and record['owner']==final['owner_hash'] and record['plan']==read('cut-record.json')['plan'] and record['state'] in ('Applying','Completed')
    proof=read('natural-lease.json');verify_lease(row,final,proof,raw);claim=(e/'after-helper-exit-claim.json').read_bytes()
    assert all(v['claimSha256']==hashlib.sha256(claim).hexdigest() and v['hddDataReread'] is v['originalStillReturned'] is True for v in proof['samples'])
