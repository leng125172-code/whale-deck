"""QA-only fixed owned instance change and read-only original Unknown/ACK proofs."""
import json
import os
from pathlib import Path
import re
import select
import uuid

from acl_history_browser_proof import typed_equal
from container_recycle_agent_io import file_snapshot, physical, read_operation
from container_recycle_rpc_proof import proto_receipt
from validate_container_recycle_browser import sql, native_plan
from validate_metric_continuity import query
from container_recycle_restart_proof import same_operation

APPS = Path('/data/WhaleDeck/apps')
from datetime import datetime,timezone
from recursive_acl_instance_proof import instance_frame,require_frame
from container_recycle_restart_proof import TABLES
from validate_container_directory_restart import command,preflight
import validate_container_file_publishing as owned


def acknowledgement_audit(execution):
    assert str(uuid.UUID(execution)) == execution
    return query("SELECT json_build_object('count',count(*),'actors',coalesce(json_agg(actor_subject),'[]'::json),'entries',coalesce(json_agg(json_build_object('result',result,'targetType',target_type,'targetId',target_id,'tracePresent',length(trace_id)>0)),'[]'::json)) FROM audit_events WHERE job_id=:'id'::uuid AND action='containers.files.recycle.unknown-acknowledged';", id=execution)


def pending_proof(data, acknowledged):
    # Unknown is deliberately nonterminal; its original execution delivery remains pending.
    assert type(acknowledged) is bool and type(data['pending']) is int and data['pending']==(0 if acknowledged else 1)


def prove(value, fixture, e, create=False):
    data = sql(value['id']); r = data['row']; ack = value['state'] == 'Acknowledged';pending_proof(data,ack)
    assert r['state'] == value['state'] in ('Unknown', 'Acknowledged')
    assert r['actor_subject'] == fixture['subject'] and r['container_id'] == fixture['containerId'] and r['instance_id'] == fixture['instanceId'] and r['relative_path'] == 'work/recycle-rpc.bin'
    assert r['root_started_at_utc'] and r['execution_job_id'] == value['executionJobId'] and r['cancel_requested_at_utc'] is None
    assert r['lease_token'] is r['lease_expires_at_utc'] is None and data['accepted'] == data['confirmed'] == 1 and data['planJob']['state'] == 'Succeeded'
    assert r['root_state'] == 'Unknown' and r['source_retained'] is r['source_restored'] is None and r['history_verified'] and r['history_may_exist']
    assert r['publish_barrier'] is (not ack) and bool(r['completed_at_utc']) is ack and bool(r['acknowledged_by']) is ack and bool(r['acknowledgement_json']) is ack
    job = data['executionJob']; assert job['state'] == ('Failed' if ack else 'Running') and job['phase'] == ('Acknowledged' if ack else 'AwaitingContainerRecycleReceipt')
    assert bool(job['completed_at_utc']) is ack and job['cancel_requested_at_utc'] is None
    assert r['result_code'] == ('CONTAINER_RECYCLE_UNKNOWN_ACKNOWLEDGED' if ack else 'CONTAINER_RECYCLE_RESULT_UNKNOWN')
    q = dict(operationId=uuid.UUID(r['execution_job_id']).hex, owner=r['owner_hash'], plan=native_plan(json.loads(r['plan_json'])))
    receipt = {k:v for k,v in value['receipt'].items() if k != 'id' and not (k in ('sourceRetained','sourceRestored') and v is None)}
    receipt['operationId'] = q['operationId']; receipt = proto_receipt(receipt, q)
    assert receipt['state'] == 'Unknown' and receipt['sourceRetained'] is receipt['sourceRestored'] is None and receipt['historyDigest'] == r['history_digest']
    records = read_operation(q, receipt, e/'originals', create=create)
    current = physical(Path(fixture['folder']), q['operationId']);typed_equal(current['source'],fixture['original']);assert current['retained'] is None
    transition=json.loads((e/'instance-transition.json').read_text());require_transition(fixture,transition);new_source=read_new_source(fixture,transition['after']);typed_equal(new_source,fixture['original'])
    result = job['result_json']; result = json.loads(result) if isinstance(result, str) else result
    assert len(result) == 14 and result['state'] == value['state'] and result['receiptState'] == 'Unknown' and result['sourceRetained'] is result['sourceRestored'] is None and result['publishBarrier'] is (not ack)
    audit = acknowledgement_audit(r['execution_job_id']); assert audit['count'] == (1 if ack else 0)
    if ack:
        assert audit['actors'] == [r['acknowledged_by']] and r['acknowledged_by'] != r['actor_subject']
        assert audit['entries'] == [dict(result='Unknown',targetType='container',targetId=fixture['containerId'],tracePresent=True)]
    else:assert audit['actors']==audit['entries']==[]
    return dict(sql=data, request=q, receipt=receipt, records=records, physical=current, newInstanceSource=new_source, acknowledgementAudit=audit)


def require_ack(before, after, actor):
    old, new = before['sql']['row'], after['sql']['row']; same_operation(old, new)
    assert old['state'] == 'Unknown' and old['publish_barrier'] and new['state'] == 'Acknowledged' and not new['publish_barrier']
    assert actor != old['actor_subject'] and new['acknowledged_by'] == actor and new['version'] > old['version']
    for k in ('root_state','source_retained','source_restored','history_digest','history_may_exist','history_verified','cancel_requested_at_utc'): typed_equal(new[k], old[k])
    facts = lambda r: {k:v for k,v in r.items() if k not in ('replayed','bindingCurrent','historyAvailable')}
    typed_equal(facts(before['receipt']), facts(after['receipt']))
    assert json.loads(new['acknowledgement_json']) == json.loads(new['receipt_json'])
    for k in ('records','physical','newInstanceSource'): typed_equal(after[k], before[k])


def as_baseline(fixture):
    return dict(containerId=fixture['containerId'],containerName=fixture['containerName'],source=fixture['folder'])


def read_new_source(fixture,frame):
    require_frame(as_baseline(fixture),frame)
    current=instance_frame(json.loads(command('docker','inspect',fixture['containerId']))[0]);typed_equal(current,frame)
    pid=frame['State']['Pid'];fd=os.pidfd_open(pid)
    try:
        assert not select.select([fd],[],[],0)[0]
        path=Path('/proc')/str(pid)/'root/work';i=path.stat();parent=Path(fixture['folder']).stat();assert (i.st_dev,i.st_ino)==(parent.st_dev,parent.st_ino)
        result=file_snapshot(path/'recycle-rpc.bin');typed_equal(result,fixture['original'])
        typed_equal(instance_frame(json.loads(command('docker','inspect',fixture['containerId']))[0]),frame);assert not select.select([fd],[],[],0)[0]
        return result
    finally:os.close(fd)


def require_transition(fixture,t):
    before,after=t['before'],t['after'];require_frame(as_baseline(fixture),before);require_frame(as_baseline(fixture),after)
    for k in ('Id','Name','Image','Mounts','Config','HostConfig'):typed_equal(after[k],before[k])
    old,new=before['State'],after['State'];assert old['Pid']==fixture['containerPid'] and old['Pid']!=new['Pid']
    assert datetime.fromisoformat(old['StartedAt'])<datetime.fromisoformat(new['StartedAt'])
    assert t['oldInstanceId']==fixture['instanceId']==str(old['Pid'])+':'+old['StartedAt']
    assert t['newInstanceId']==str(new['Pid'])+':'+new['StartedAt'] and t['newInstanceId']!=t['oldInstanceId']
    assert t['restartAttemptedOnce'] is True and t['commandAcknowledgement']==fixture['containerId'] and t['helperAliveBeforeRestart'] is True and type(t['helperAliveAfterRestart']) is bool
    assert set(t['counts'])==set(TABLES) and all(type(v) is int and v==0 for k in ('checks','counts') for v in t[k].values())
    typed_equal(t['coreBefore'],t['coreAfter'])
    for k in ('sourceBefore','sourceAfter','newInstanceSource'):typed_equal(t[k],fixture['original'])


def prepare_instance(f,fixture,held,row):
    assert os.geteuid()==0 and f['identifier']==fixture['containerId'] and re.fullmatch('[a-f0-9]{64}',f['identifier'])
    folder=Path(fixture['folder']);assert folder==f['folder'] and folder.parent==APPS and folder.resolve(strict=True)==folder and not folder.is_symlink()
    assert re.fullmatch('wdv-container-publish-[a-f0-9]{32}',folder.name) and fixture['containerName']==folder.name
    assert held.proof['twoKernelFdProofs'] and held.proof['sourceNotRemoved'] and not f.get('instanceRestartAttempted',False)
    assert held.record['plan']['parentIdentity']==[folder.stat().st_dev,folder.stat().st_ino]
    checks=preflight([row['id'],row['execution_job_id']]);parts=[]
    for table in TABLES:
        exclude=" AND id<>:'id'::uuid" if table=='container_recycle_intents' else ''
        parts.append("'"+table+"',(SELECT count(*) FROM "+table+" WHERE (completed_at_utc IS NULL OR publish_barrier OR lease_token IS NOT NULL OR lease_expires_at_utc IS NOT NULL)"+exclude+')')
    counts=query('SELECT json_build_object('+','.join(parts)+');',id=row['id']);assert set(counts)==set(TABLES) and all(type(v) is int and v==0 for v in counts.values())
    frame=instance_frame(json.loads(command('docker','inspect',fixture['containerId']))[0]);require_frame(as_baseline(fixture),frame)
    assert frame['State']['Pid']==fixture['containerPid'] and str(frame['State']['Pid'])+':'+frame['State']['StartedAt']==fixture['instanceId']
    typed_equal(file_snapshot(folder/'recycle-rpc.bin'),fixture['original']);assert not select.select([held.descriptor],[],[],0)[0]
    return dict(before=frame,checks=checks,counts=counts,coreBefore=owned.common.core_snapshot(),sourceBefore=fixture['original'])


def restart_instance(f,fixture,held,prepared,e):
    assert not f.get('instanceRestartAttempted',False);typed_equal(instance_frame(json.loads(command('docker','inspect',fixture['containerId']))[0]),prepared['before'])
    typed_equal(file_snapshot(Path(fixture['folder'])/'recycle-rpc.bin'),fixture['original']);assert not select.select([held.descriptor],[],[],0)[0]
    f['instanceRestartAttempted']=True
    with (e/'instance-restart-request.json').open('x') as out:json.dump(dict(containerId=fixture['containerId'],before=prepared['before'],restartAttemptedOnce=True),out)
    reply=command('docker','restart','--timeout','1',fixture['containerId']);after=instance_frame(json.loads(command('docker','inspect',fixture['containerId']))[0]);require_frame(as_baseline(fixture),after)
    t=dict(**prepared,after=after,commandAcknowledgement=reply,restartAttemptedOnce=True,helperAliveBeforeRestart=True,helperAliveAfterRestart=not bool(select.select([held.descriptor],[],[],0)[0]),oldInstanceId=fixture['instanceId'],newInstanceId=str(after['State']['Pid'])+':'+after['State']['StartedAt'],sourceAfter=file_snapshot(Path(fixture['folder'])/'recycle-rpc.bin'),newInstanceSource=read_new_source(fixture,after),coreAfter=owned.common.core_snapshot())
    require_transition(fixture,t)
    with (e/'instance-transition.json').open('x') as out:json.dump(t,out)
    return t


def attest_after_ack(fixture,final,e):
    t=json.loads((e/'instance-transition.json').read_text());require_transition(fixture,t);source=read_new_source(fixture,t['after']);assert final['state']=='Acknowledged'
    value=dict(observedAtUtc=datetime.now(timezone.utc).isoformat(),frame=t['after'],source=source);assert datetime.fromisoformat(value['observedAtUtc'])>=datetime.fromisoformat(final['completed_at_utc'])
    with (e/'instance-after-ack.json').open('x') as out:json.dump(value,out)


def verify_instance(fixture,e,final):
    t=json.loads((e/'instance-transition.json').read_text());require_transition(fixture,t);r=json.loads((e/'instance-restart-request.json').read_text());assert r==dict(containerId=fixture['containerId'],before=t['before'],restartAttemptedOnce=True)
    after=json.loads((e/'instance-after-ack.json').read_text());typed_equal(after['frame'],t['after']);typed_equal(after['source'],fixture['original']);assert datetime.fromisoformat(after['observedAtUtc'])>=datetime.fromisoformat(final['completed_at_utc'])
