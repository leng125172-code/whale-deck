"""Real partial-write restart witnesses; SQL, clocks and history stay read-only."""
import datetime as dt
import hashlib
import json
import time

from bound_container_recursive_acl_cut import STATE, record_for
from bound_container_recursive_acl_partial_cut import partial_boundary, partial_proof
from recursive_acl_restart_proof import lease_held, observation_expiry, verify_restart_service
from validate_container_recursive_acl_browser import snapshot, managed_plan


def partial_checkpoint(record,cut):
    for key in ('operationId','owner','plan','historyDigest'):
        assert record[key]==cut[key]
    assert record['historyVerified'] is record['historyMayExist'] is True
    if record['state']=='Applying':assert partial_boundary(record)
    else:
        assert record['state']=='Partial' and all(type(record[k]) is int and record[k]>=0 for k in ('completedCount','changedCount','abortedCount','notStartedCount','unknownCount'))
        assert record['completedCount']==record['changedCount']==1 and record['unknownCount']==0
        assert record['completedCount']+record['abortedCount']+record['notStartedCount']==4 and record['notStartedCount']>0


def inventory_digest(physical):
    return hashlib.sha256(json.dumps(physical,sort_keys=True,separators=(',',':')).encode()).hexdigest()


def natural_partial_lease(row,current,physical,cut,directory):
    wall=dt.datetime.now(dt.timezone.utc);expiry=observation_expiry(row,wall);began=time.monotonic()
    path=STATE/(row['execution_job_id'].replace('-','')+'.json');raw=path.read_bytes();record=json.loads(raw)
    assert record==record_for(row);partial_checkpoint(record,cut);assert current()==physical
    (directory/'after-helper-exit-root.json').write_bytes(raw);samples=[];sha=hashlib.sha256(raw).hexdigest();physical_sha=inventory_digest(physical)
    while dt.datetime.now(dt.timezone.utc)<expiry:
        assert time.monotonic()-began<100,'PARTIAL_NATURAL_LEASE_MONOTONIC_DEADLINE'
        data=snapshot(row['id']);observed=dt.datetime.now(dt.timezone.utc)
        if observed>=expiry:break
        lease_held(row,data['row']);assert data['executionJob']['state']=='Running' and data['executionJob']['cancel_requested_at_utc'] is None
        assert path.read_bytes()==raw and current()==physical
        samples.append(dict(atUtc=observed.isoformat(),state='Applying',barrier=True,leaseToken=row['lease_token'],leaseExpiresAtUtc=row['lease_expires_at_utc'],rootSha256=sha,partialInventorySha256=physical_sha))
        time.sleep(min(2,max(.001,(expiry-observed).total_seconds())))
    ended=dt.datetime.now(dt.timezone.utc);elapsed=time.monotonic()-began
    proof=dict(originalLeaseToken=row['lease_token'],originalLeaseExpiresAtUtc=row['lease_expires_at_utc'],waitBeganAtUtc=wall.isoformat(),waitEndedAtUtc=ended.isoformat(),monotonicWaitSeconds=round(elapsed,3),rootSha256=sha,partialInventorySha256=physical_sha,rootStateAfterHelperExit=record['state'],samples=samples,clocksOrSqlModified=False)
    assert len(samples)>=3 and abs((ended-wall).total_seconds()-elapsed)<3
    (directory/'natural-lease.json').write_text(json.dumps(proof))
    return proof


def verify_partial_lease(row,final,proof,raw,cut,physical,observed):
    expiry=dt.datetime.fromisoformat(proof['originalLeaseExpiresAtUtc']);began=dt.datetime.fromisoformat(proof['waitBeganAtUtc']);ended=dt.datetime.fromisoformat(proof['waitEndedAtUtc']);settled=dt.datetime.fromisoformat(observed['atUtc'])
    assert all(v.utcoffset() is not None for v in (expiry,began,ended,settled)) and dt.timedelta(seconds=25)<expiry-began<=dt.timedelta(seconds=95)
    assert expiry<=ended<=settled and settled-ended<dt.timedelta(seconds=100) and final['completed_at_utc'] is None and final['state']=='Partial'
    assert abs((ended-began).total_seconds()-proof['monotonicWaitSeconds'])<3 and proof['monotonicWaitSeconds']<100 and proof['clocksOrSqlModified'] is False
    lease_held(row,row);assert row['lease_token']==proof['originalLeaseToken'] and row['lease_expires_at_utc']==proof['originalLeaseExpiresAtUtc']
    record=json.loads(raw);partial_checkpoint(record,cut);sha=hashlib.sha256(raw).hexdigest();physical_sha=inventory_digest(physical)
    assert record['state']==proof['rootStateAfterHelperExit'] and proof['rootSha256']==sha and proof['partialInventorySha256']==physical_sha and len(proof['samples'])>=3
    assert record['operationId']==row['execution_job_id'].replace('-','') and record['owner']==row['owner_hash'] and record['plan']==managed_plan(json.loads(final['plan_json']))
    previous=began
    for sample in proof['samples']:
        at=dt.datetime.fromisoformat(sample['atUtc']);assert previous<=at<expiry and (at-previous).total_seconds()<5;previous=at
        assert sample['state']=='Applying' and sample['barrier'] is True and sample['leaseToken']==row['lease_token'] and sample['leaseExpiresAtUtc']==row['lease_expires_at_utc']
        assert sample['rootSha256']==sha and sample['partialInventorySha256']==physical_sha
    assert (ended-previous).total_seconds()<5


def verify_partial_restart(kind,scope,owner,partial,directory,read):
    service=read(directory/'service-restart.json');final=partial['row']
    row=verify_restart_service(kind,service,scope,final,directory,read)
    assert service['hostsBefore']==scope['hostIdentity'] and owner['normalDualLogout'] and owner['cancellations']==0 and final['cancel_requested_at_utc'] is None
    intent=owner['result'];assert intent['id']==final['id'] and intent['executionJobId']==final['execution_job_id']
    assert intent['state']=='Partial' and intent['resultCode']=='CONTAINER_RECURSIVE_ACL_PARTIAL_REQUIRES_ACKNOWLEDGEMENT' and intent['confirmation'] is None
    assert intent['receipt']['state']=='Partial' and intent['receipt']['changedCount']==intent['receipt']['completedCount']==1 and intent['receipt']['unknownCount']==0
    physical=read(directory/'cut-physical.json');assert physical==read(directory/'physical-partial.json')
    cut=read(directory/'cut-root.json');baseline=read(directory/'baseline.json');boundary=partial_proof(cut,baseline['original'],physical)
    bound=read(directory/'bound-helper.json');assert boundary is not None and all(bound['proof'][k]==v for k,v in boundary.items()) and bound['proof']['twoKernelFdProofs'] is True
    observed=read(directory/'partial-observed.json');assert observed['id']==final['id'] and observed['executionJobId']==final['execution_job_id'] and observed['state']=='Partial'
    if kind=='worker-crash':
        verify_partial_lease(row,final,read(directory/'natural-lease.json'),(directory/'after-helper-exit-root.json').read_bytes(),cut,physical,observed)
    return service['hostsAfter']
