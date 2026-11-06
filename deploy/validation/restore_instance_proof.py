"""QA-only single restart of the exact owned returned-file container instance.

No arbitrary target/PID endpoint. Preserve native completed ownership while
requiring a new namespace and refusing all other active file work.
"""
import json,os,re,select,signal,stat
from pathlib import Path
from datetime import datetime,timezone
from acl_history_browser_proof import typed_equal
from container_recycle_agent_io import file_snapshot,physical
from container_recycle_unknown_proof import displaced_proof
from container_recycle_restore_restart_proof import TABLES,lease_held
from validate_container_recycle_restore_browser import sql
from validate_container_directory_restart import command,preflight
from validate_container_file_publishing import common
from recursive_acl_instance_proof import instance_frame,require_frame
from validate_metric_continuity import query
from validate_container_recycle_restore_postreturn_cancel import cut_binding
import bound_container_recycle_restore_cut as cut


def as_baseline(f):return dict(containerId=f['containerId'],containerName=f['containerName'],source=f['folder'])


def other_work(row):
    data=sql(row['id']);lease_held(row,data)
    checks=preflight([row['id'],row['execution_job_id']]);parts=[]
    assert set(checks)=={'otherJobs','terminals','hostLocks','uploadBarriers','directoryBarriers'} and all(type(v) is int and v==0 for v in checks.values())
    for table in TABLES:
        exclude=" AND id<>:'id'::uuid" if table=='container_recycle_restore_intents' else ''
        parts.append("'"+table+"',(SELECT count(*) FROM "+table+" WHERE (completed_at_utc IS NULL OR publish_barrier OR lease_token IS NOT NULL OR lease_expires_at_utc IS NOT NULL)"+exclude+')')
    counts=query('SELECT json_build_object('+','.join(parts)+');',id=row['id'])
    assert set(counts)==set(TABLES) and all(type(v) is int and v==0 for v in counts.values())
    return checks,counts


def returned_snapshot(original,current):
    assert current is not None;displaced_proof(original,current)
    assert type(current['identity'][8]) is int


def bound_returned(fixture,held,row,pending):
    cut_binding(row,held.record,held.proof,fixture);p=held.proof
    assert held.pid==p['pid'] and held.cgroup==p['cgroup'] and held.descriptor is not None and not select.select([held.descriptor],[],[],0)[0]
    assert row['state']=='Applying' and row['completed_at_utc'] is row['cancel_requested_at_utc'] is None and row['root_started_at_utc'] and row['publish_barrier']
    command=held.record['plan']['command']['source']['plan']['command'];assert (command['containerId'],command['instanceId'],command['path'])==(fixture['containerId'],fixture['instanceId'],'work/recycle-rpc.bin')
    typed_equal(pending['root'],held.record);assert pending['verification']['hddDataReread'] is True and pending['sourceRoot']['state']=='Restored'
    op=held.record['operationId'];source_op=held.record['plan']['command']['source']['operationId']
    locks=(cut.operation_lock(cut.STATE/(op+'.lock')),cut.operation_lock(cut.SOURCE_STATE/(source_op+'.lock')))
    assert cut.unit_identity(p['cgroup'])==(held.pid,p['invocationId']) and cut.bound_process(held.pid,held.cgroup,p['invocationId'],tuple(p['containerRootIdentity']),*locks)
    assert (cut.PROC/str(held.pid)/'stat').read_text().rsplit(')',1)[1].split()[0] in ('T','t')
    typed_equal(cut.record_for(row),held.record);typed_equal(cut.private_record(cut.SOURCE_STATE/(source_op+'.json')),pending['sourceRoot'])
    folder=Path(fixture['folder']);i=folder.lstat();assert [i.st_dev,i.st_ino]==held.record['plan']['parentIdentity'] and i.st_uid==0 and stat.S_IMODE(i.st_mode)==0o755
    current=file_snapshot(folder/'recycle-rpc.bin');returned_snapshot(fixture['original'],current);typed_equal(current['identity'],pending['sourceRoot']['actualIdentity'])
    return current


def require_transition(f,t):
    before,after=t['before'],t['after'];require_frame(as_baseline(f),before);require_frame(as_baseline(f),after)
    for key in ('Id','Name','Image','Mounts','Config','HostConfig'):typed_equal(after[key],before[key])
    a,z=before['State'],after['State'];assert a['Pid']==f['containerPid'] and a['Pid']!=z['Pid']
    times=[datetime.fromisoformat(v['StartedAt']) for v in (a,z)];assert all(v.utcoffset() is not None for v in times) and times[0]<times[1]
    assert t['oldInstanceId']==f['instanceId']==str(a['Pid'])+':'+a['StartedAt']
    assert t['newInstanceId']==str(z['Pid'])+':'+z['StartedAt'] and t['newInstanceId']!=t['oldInstanceId']
    assert t['restartAttemptedOnce'] is True and t['commandAcknowledgement']==f['containerId'] and t['helperAliveBeforeRestart'] is True and type(t['helperAliveAfterRestart']) is bool
    for key in ('checks','checksBeforeRestart'):assert t[key] and all(type(v) is int and v==0 for v in t[key].values())
    for key in ('counts','countsBeforeRestart'):assert set(t[key])==set(TABLES) and all(type(v) is int and v==0 for v in t[key].values())
    typed_equal(t['coreBefore'],t['coreAfter']);returned_snapshot(f['original'],f['returned'])
    for key in ('sourceBefore','sourceAfter','newInstanceSource'):typed_equal(t[key],f['returned'])


def read_new_source(f,frame):
    require_frame(as_baseline(f),frame);current=instance_frame(json.loads(command('docker','inspect',f['containerId']))[0]);typed_equal(current,frame)
    pid=frame['State']['Pid'];fd=os.pidfd_open(pid)
    try:
        assert not select.select([fd],[],[],0)[0]
        parent=Path(f['folder']).stat();folder=Path('/proc')/str(pid)/'root/work';i=folder.stat();assert (i.st_dev,i.st_ino)==(parent.st_dev,parent.st_ino)
        value=file_snapshot(folder/'recycle-rpc.bin');typed_equal(value,f['returned'])
        typed_equal(instance_frame(json.loads(command('docker','inspect',f['containerId']))[0]),frame);assert not select.select([fd],[],[],0)[0]
        return value
    finally:os.close(fd)


def prepare(f,fixture,held,row,pending):
    assert os.geteuid()==0 and f['identifier']==fixture['containerId'] and re.fullmatch('[a-f0-9]{64}',f['identifier'])
    folder=Path(fixture['folder']);assert folder==f['folder'] and folder.parent==common.APPS and folder.resolve(strict=True)==folder and not folder.is_symlink()
    assert re.fullmatch('wdv-container-publish-[a-f0-9]{32}',folder.name) and fixture['containerName']==folder.name and not f.get('instanceRestartAttempted',False)
    frame=instance_frame(json.loads(command('docker','inspect',fixture['containerId']))[0]);require_frame(as_baseline(fixture),frame)
    assert frame['State']['Pid']==fixture['containerPid'] and str(frame['State']['Pid'])+':'+frame['State']['StartedAt']==fixture['instanceId']
    value=bound_returned(fixture,held,row,pending);fixture['returned']=value;checks,counts=other_work(row)
    return dict(before=frame,sourceBefore=value,checks=checks,counts=counts,coreBefore=common.core_snapshot())


def restart(f,fixture,held,row,pending,prepared,e):
    assert not f.get('instanceRestartAttempted',False)
    # Re-run every scope/current-FD/work check immediately before the only mutation.
    current=prepare(f,fixture,held,row,pending);typed_equal(current,prepared)
    f['instanceRestartAttempted']=True
    with (e/'instance-restart-request.json').open('x') as out:json.dump(dict(containerId=fixture['containerId'],before=prepared['before'],restartAttemptedOnce=True),out)
    typed_equal(instance_frame(json.loads(command('docker','inspect',fixture['containerId']))[0]),prepared['before'])
    typed_equal(file_snapshot(Path(fixture['folder'])/'recycle-rpc.bin'),fixture['returned']);assert not select.select([held.descriptor],[],[],0)[0]
    reply=command('docker','restart','--timeout','1',fixture['containerId'])
    after=instance_frame(json.loads(command('docker','inspect',fixture['containerId']))[0]);require_frame(as_baseline(fixture),after)
    t=dict(**prepared,after=after,checksBeforeRestart=current['checks'],countsBeforeRestart=current['counts'],commandAcknowledgement=reply,restartAttemptedOnce=True,helperAliveBeforeRestart=True,helperAliveAfterRestart=not bool(select.select([held.descriptor],[],[],0)[0]),oldInstanceId=fixture['instanceId'],newInstanceId=str(after['State']['Pid'])+':'+after['State']['StartedAt'],sourceAfter=file_snapshot(Path(fixture['folder'])/'recycle-rpc.bin'),newInstanceSource=read_new_source(fixture,after),coreAfter=common.core_snapshot())
    require_transition(fixture,t)
    with (e/'instance-transition.json').open('x') as out:json.dump(t,out)
    return t


def namespace(fixture,records,receipt,t):
    require_transition(fixture,t);assert receipt['state']=='Completed' and receipt['bindingCurrent'] is False and receipt['restoredByThisOperation'] is True
    value=physical(Path(fixture['folder']),records['sourceRoot']['operationId']);assert value['retained'] is None
    typed_equal(value['source'],fixture['returned']);typed_equal(value['source']['identity'],records['sourceRoot']['actualIdentity']);typed_equal(value['privateIdentity'],records['sourceRoot']['privateIdentity'])
    return value,read_new_source(fixture,t['after'])


def attest(fixture,row,e):
    assert row['state']=='Superseded';t=json.loads((e/'instance-transition.json').read_text());require_transition(fixture,t)
    proof=dict(observedAtUtc=datetime.now(timezone.utc).isoformat(),frame=t['after'],source=read_new_source(fixture,t['after']))
    assert datetime.fromisoformat(proof['observedAtUtc'])>=datetime.fromisoformat(row['completed_at_utc'])
    with (e/'instance-after-result.json').open('x') as out:json.dump(proof,out)


def verify_attestation(fixture,row,e):
    t=json.loads((e/'instance-transition.json').read_text());require_transition(fixture,t)
    typed_equal(json.loads((e/'instance-restart-request.json').read_text()),dict(containerId=fixture['containerId'],before=t['before'],restartAttemptedOnce=True))
    p=json.loads((e/'instance-after-result.json').read_text());typed_equal(p['frame'],t['after']);typed_equal(p['source'],fixture['returned'])
    assert datetime.fromisoformat(p['observedAtUtc'])>=datetime.fromisoformat(row['completed_at_utc'])
    return t


def resume_after_restart(f,fixture,held,row,t,e):
    require_transition(fixture,t);assert f.get('instanceRestartAttempted') is True and not f.get('instancePauseDecisionMade',False)
    p=held.proof;assert held.pid==p['pid'] and held.cgroup==p['cgroup'] and held.descriptor is not None
    already=bool(select.select([held.descriptor],[],[],0)[0])
    if not already:
        op=held.record['operationId'];source_op=held.record['plan']['command']['source']['operationId']
        locks=(cut.operation_lock(cut.STATE/(op+'.lock')),cut.operation_lock(cut.SOURCE_STATE/(source_op+'.lock')))
        assert cut.unit_identity(p['cgroup'])==(held.pid,p['invocationId']) and cut.bound_process(held.pid,held.cgroup,p['invocationId'],tuple(p['containerRootIdentity']),*locks)
        assert (cut.PROC/str(held.pid)/'stat').read_text().rsplit(')',1)[1].split()[0] in ('T','t')
        typed_equal(cut.record_for(row),held.record);typed_equal(read_new_source(fixture,t['after']),fixture['returned'])
    request=dict(pid=held.pid,cgroup=held.cgroup,invocationId=p['invocationId'],newInstanceId=t['newInstanceId'],alreadyExited=already,resumeExactOriginal=not already)
    with (e/'instance-resume-request.json').open('x') as out:json.dump(request,out)
    f['instancePauseDecisionMade']=True
    if not already:signal.pidfd_send_signal(held.descriptor,signal.SIGCONT)
    with (e/'instance-resume-proof.json').open('x') as out:json.dump({**request,'originalPidfdKeptOpen':True,'pauseDecisionRecordedOnce':True},out)
    return {**request,'originalPidfdKeptOpen':True,'pauseDecisionRecordedOnce':True}


def verify_resume(fixture,cut_proof,e):
    t=json.loads((e/'instance-transition.json').read_text());require_transition(fixture,t)
    request=json.loads((e/'instance-resume-request.json').read_text());p=json.loads((e/'instance-resume-proof.json').read_text())
    typed_equal(p,{**request,'originalPidfdKeptOpen':True,'pauseDecisionRecordedOnce':True})
    assert request['pid']==cut_proof['pid'] and request['cgroup']==cut_proof['cgroup'] and request['invocationId']==cut_proof['invocationId'] and request['newInstanceId']==t['newInstanceId']
    assert type(request['alreadyExited']) is bool and request['resumeExactOriginal'] is (not request['alreadyExited'])
