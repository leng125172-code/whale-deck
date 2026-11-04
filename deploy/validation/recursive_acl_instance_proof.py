"""Fixed owned-container instance fault; no host/service restart or arbitrary target."""
import datetime as dt
import json
import select
from pathlib import Path

import validate_container_file_publishing as owned
from validate_container_directory_restart import command
from recursive_acl_restart_proof import restart_allowed,RESTART_KEYS
from recursive_acl_partial_unknown_proof import require_unknown_identity


def instance_frame(value):
    return {**{k:value[k] for k in ('Id','Name','Image','Mounts')},
        'State':{k:value['State'][k] for k in ('Running','Pid','StartedAt')},
        'Config':{'User':value['Config']['User'],'Labels':{'io.whaledeck.validation':value['Config']['Labels'].get('io.whaledeck.validation')}},
        'HostConfig':{k:value['HostConfig'].get(k) for k in ('NetworkMode','ReadonlyRootfs','Binds','Mounts','CapDrop','SecurityOpt','Privileged','PidMode','IpcMode','PidsLimit','Memory','NanoCpus','RestartPolicy')}}


def require_frame(baseline,frame):
    owned.verify_container(frame,baseline['containerId'],baseline['containerName'],Path(baseline['source']))
    host=frame['HostConfig'];state=frame['State']
    assert host['Privileged'] is False and host['CapDrop']==['ALL'] and host['SecurityOpt']==['no-new-privileges']
    assert host['PidMode']=='' and host['IpcMode']=='private' and host['PidsLimit']==16 and host['Memory']==32*1024**2 and host['NanoCpus']==250000000
    assert host['RestartPolicy']==dict(Name='no',MaximumRetryCount=0)
    assert state['Running'] is True and type(state['Pid']) is int and state['Pid']>1


def require_instance_transition(baseline,proof,cut):
    assert baseline['kind']=='instance-changed'
    before,after=proof['before'],proof['after'];require_frame(baseline,before);require_frame(baseline,after)
    for key in ('Id','Name','Image','Mounts','Config','HostConfig'):assert before[key]==after[key]
    a,z=before['State'],after['State'];assert a['Pid']==baseline['containerPid'] and a['Pid']!=z['Pid']
    times=[dt.datetime.fromisoformat(v['StartedAt']) for v in (a,z)];assert all(v.utcoffset() is not None for v in times) and times[0]<times[1]
    assert str(a['Pid'])+':'+a['StartedAt']==baseline['instanceId']==proof['oldInstanceId']
    assert proof['newInstanceId']==str(z['Pid'])+':'+z['StartedAt'] and proof['newInstanceId']!=proof['oldInstanceId']
    assert proof['commandAcknowledgement']==baseline['containerId'] and proof['restartAttemptedOnce'] is True and type(proof['helperAliveAfterRestart']) is bool
    assert proof['sourceInventory']==proof['newInstanceInventory']==cut and proof['coreBefore']==proof['coreAfter']
    checks=proof['otherActiveWork'];assert RESTART_KEYS<=checks.keys() and all(type(v) is int and v==0 for v in checks.values())


def prepare_instance(baseline,fixture,held,row,current,cut):
    assert baseline['kind']=='instance-changed';require_unknown_identity(baseline,fixture)
    assert not fixture.get('instanceRestartAttempted',False) and current()==cut and not select.select([held.descriptor],[],[],0)[0]
    checks=restart_allowed(held,row)
    frame=instance_frame(json.loads(command('docker','inspect',fixture['identifier']))[0]);require_frame(baseline,frame)
    assert frame['State']['Pid']==baseline['containerPid'] and str(frame['State']['Pid'])+':'+frame['State']['StartedAt']==baseline['instanceId']
    return dict(before=frame,otherActiveWork=checks,coreBefore=owned.common.core_snapshot())


def restart_instance(baseline,fixture,held,prepared,current,cut,new_inventory,directory):
    assert baseline['kind']=='instance-changed';require_unknown_identity(baseline,fixture)
    assert not fixture.get('instanceRestartAttempted',False) and current()==cut and not select.select([held.descriptor],[],[],0)[0]
    before=instance_frame(json.loads(command('docker','inspect',fixture['identifier']))[0]);assert before==prepared['before'];require_frame(baseline,before)
    fixture['instanceRestartAttempted']=True
    (directory/'instance-restart-request.json').open('x').write(json.dumps(dict(containerId=baseline['containerId'],before=before,restartAttemptedOnce=True)))
    acknowledgement=command('docker','restart','--timeout','1',fixture['identifier'])
    after=instance_frame(json.loads(command('docker','inspect',fixture['identifier']))[0]);require_frame(baseline,after)
    proof={**prepared,'after':after,'oldInstanceId':baseline['instanceId'],'newInstanceId':str(after['State']['Pid'])+':'+after['State']['StartedAt'],
        'commandAcknowledgement':acknowledgement,'restartAttemptedOnce':True,'helperAliveAfterRestart':not bool(select.select([held.descriptor],[],[],0)[0]),
        'sourceInventory':current(),'newInstanceInventory':new_inventory(after['State']['Pid']),'coreAfter':owned.common.core_snapshot()}
    (directory/'instance-transition.json').open('x').write(json.dumps(proof));require_instance_transition(baseline,proof,cut)
    return proof


def attest_instance_after_ack(baseline,row,current,new_inventory,directory):
    assert baseline['kind']=='instance-changed' and row['state']=='Acknowledged'
    proof=json.loads((directory/'instance-transition.json').read_bytes());physical=current();require_instance_transition(baseline,proof,physical)
    actual=instance_frame(json.loads(command('docker','inspect',baseline['containerId']))[0]);assert actual==proof['after']
    assert new_inventory(actual['State']['Pid'])==physical
    result=dict(observedAtUtc=dt.datetime.now(dt.timezone.utc).isoformat(),frame=actual,sourceInventory=physical,newInstanceInventory=physical)
    (directory/'instance-after-ack.json').open('x').write(json.dumps(result))


def verify_instance_evidence(baseline,row,directory,read,physical):
    proof=read(directory/'instance-transition.json');require_instance_transition(baseline,proof,physical)
    request=read(directory/'instance-restart-request.json');assert request==dict(containerId=baseline['containerId'],before=proof['before'],restartAttemptedOnce=True)
    actual=read(directory/'instance-after-ack.json');assert actual['frame']==proof['after'] and actual['sourceInventory']==actual['newInstanceInventory']==physical
    at=dt.datetime.fromisoformat(actual['observedAtUtc']);ended=dt.datetime.fromisoformat(row['completed_at_utc']);assert at.utcoffset() is not None and at>=ended
    assert not (directory/'removed-owned.json').exists()
