"""Natural cross-container mkdir deferrals and exact release after recursive Unknown ACK."""
import datetime as dt
import hashlib,json,os,re,stat,time,uuid
from pathlib import Path

from validate_container_recursive_acl_browser import snapshot,STATE
from validate_container_directory_browser import snapshot as directory_snapshot,ROOT,verify_directory
from validate_container_upload_recovery_agent import trusted
from validate_container_directory_restart import command

WAITING_PATH='work/递归ACL保护后的目录'
STABLE=('id','idempotency_key','actor_subject','login_binding','trace_id','owner_hash','container_id','instance_id','relative_path','plan_json','plan_hash','request_hash','created_at_utc','expires_at_utc')


def selected_waiting(arguments):
    if len(arguments)<=1:return False
    assert tuple(arguments)==('target-removed','cross-directory'),'UNKNOWN_CROSS_DIRECTORY_SCENARIO'
    return True


def history_snapshot():
    trusted(ROOT,True);result={}
    for path in ROOT.glob('*.json'):
        info=trusted(path);assert stat.S_ISREG(info.st_mode) and info.st_nlink==1 and info.st_size<=16384
        identity=lambda s:[s.st_dev,s.st_ino,s.st_mode,s.st_uid,s.st_gid,s.st_nlink,s.st_size,s.st_mtime_ns,s.st_ctime_ns]
        fd=os.open(path,os.O_RDONLY|os.O_NOFOLLOW|os.O_CLOEXEC)
        try:
            raw=os.read(fd,16385);assert len(raw)==info.st_size and identity(os.fstat(fd))==identity(info)==identity(path.lstat())
        finally:os.close(fd)
        result[path.name]=dict(identity=identity(info),sha256=hashlib.sha256(raw).hexdigest())
    return result


def waiting_baseline(other,original):
    assert other['identifier']!=original['identifier'] and other['folder']!=original['folder']
    inspected=json.loads(command('docker','inspect',other['identifier']))[0]
    from validate_container_file_publishing import verify_container
    verify_container(inspected,other['identifier'],other['folder'].name,other['folder'])
    return dict(containerId=other['identifier'],containerName=other['folder'].name,source=str(other['folder']),instanceId=str(inspected['State']['Pid'])+':'+inspected['State']['StartedAt'],path=WAITING_PATH,originalFileProof=other['proofs']['original.bin'])


def same_waiting(initial,current):
    for key in STABLE:assert initial[key]==current[key],'WAITING_DIRECTORY_ORIGINAL_BINDING_CHANGED'


def require_blocked(data,baseline,subject,at):
    row=data['row'];assert data['job']['id']==row['id']
    assert row['container_id']==baseline['containerId'] and row['instance_id']==baseline['instanceId'] and row['relative_path']==baseline['path']==WAITING_PATH and row['actor_subject']==subject
    assert row['state']=='Pending' and row['root_started_at_utc'] is row['completed_at_utc'] is row['lease_token'] is row['lease_expires_at_utc'] is None and row['publish_barrier'] is False
    assert data['job']['state']=='Queued' and data['accepted']==1 and data['terminalAudits']==0 and data['pending']>0
    return type(row['version']) is int and row['version']>=2 and dt.datetime.fromisoformat(row['next_attempt_at_utc'])>at


def original_file_unchanged(baseline):
    path=Path(baseline['source'])/'original.bin';info=path.lstat();expected=baseline['originalFileProof']
    assert [info.st_dev,info.st_ino,info.st_mode,info.st_uid,info.st_gid,info.st_nlink,info.st_size,info.st_mtime_ns,info.st_ctime_ns]==list(expected[0])
    assert hashlib.sha256(path.read_bytes()).hexdigest().upper()==expected[1]


def require_registration(registered,initial,b):
    row=initial['row'];assert initial['job']['id']==row['id']==registered['id'] and row['actor_subject']==registered['subject']
    assert registered['intent']['id']==registered['intent']['jobId']==row['id'] and registered['intent']['command']==dict(location=dict(containerId=b['containerId'],instanceId=b['instanceId'],path=WAITING_PATH),mode='0750')
    assert registered['plans']==registered['registrations']==1 and registered['cancellations']==registered['uploads']==0 and registered['originAndIdempotencyObserved'] is True
    registration=registered['registration'];assert set(registration)=={'planTokenSha256','confirmed','idempotencyKey'} and registration['confirmed'] is True
    assert registration['planTokenSha256']==row['plan_hash'] and re.fullmatch('[A-F0-9]{64}',row['plan_hash']) and str(uuid.UUID(registration['idempotencyKey']))==row['idempotency_key']


def observe_waiting(original,b,registered,current,physical,directory):
    identifier=registered['id'];initial=directory_snapshot(identifier);row=initial['row'];assert row['id']==identifier and b['containerId']!=original['row']['container_id']
    assert registered['intent']['id']==registered['intent']['jobId']==identifier and registered['subject']!=original['row']['actor_subject']
    require_registration(registered,initial,b)
    (directory/'waiting-initial.sql.json').write_text(json.dumps(initial));samples=[];versions=set();until=time.monotonic()+65
    root=STATE/(uuid.UUID(original['row']['execution_job_id']).hex+'.json');raw=root.read_bytes();target=Path(b['source'])/WAITING_PATH[5:];receipt=ROOT/(uuid.UUID(identifier).hex+'.json')
    while len(samples)<4:
        assert time.monotonic()<until,'FOUR_NATURAL_DIRECTORY_DEFERRALS_NOT_OBSERVED'
        data=directory_snapshot(identifier);at=dt.datetime.now(dt.timezone.utc);same_waiting(row,data['row']);deferred=require_blocked(data,b,registered['subject'],at)
        assert snapshot(original['row']['id'])==original and original['row']['state']=='Unknown' and original['row']['publish_barrier']
        assert not os.path.lexists(target) and not receipt.exists() and root.read_bytes()==raw and current()==physical
        original_file_unchanged(b);assert {p.name for p in Path(b['source']).iterdir()}=={'original.bin'}
        if deferred and data['row']['version'] not in versions:
            versions.add(data['row']['version']);samples.append(dict(observedAtUtc=at.isoformat(),sql=data,rootAbsent=True,targetAbsent=True,recursiveUnknownProtected=True,originalPhysicalUnchanged=True,unknownRootSha256=hashlib.sha256(raw).hexdigest()))
        if len(samples)<4:time.sleep(.5)
    (directory/'waiting-blocked.json').write_text(json.dumps(samples));return samples


def require_deferrals(samples,b,registered,initial,released,root_raw):
    assert len(samples)==4;versions=[];attempts=[];times=[];sha=hashlib.sha256(root_raw).hexdigest()
    for sample in samples:
        at=dt.datetime.fromisoformat(sample['observedAtUtc']);assert at.utcoffset() is not None and at<released
        same_waiting(initial['row'],sample['sql']['row']);assert require_blocked(sample['sql'],b,registered['subject'],at)
        assert all(sample[k] is True for k in ('rootAbsent','targetAbsent','recursiveUnknownProtected','originalPhysicalUnchanged')) and sample['unknownRootSha256']==sha
        versions.append(sample['sql']['row']['version']);attempts.append(dt.datetime.fromisoformat(sample['sql']['row']['next_attempt_at_utc']));times.append(at)
    assert len(set(versions))==len(set(attempts))==4 and versions==sorted(versions) and times==sorted(times)
    assert all((z-a).total_seconds()>=5 for a,z in zip(attempts,attempts[1:])) and all((z-a).total_seconds()>=5 for a,z in zip(times,times[1:]))
    assert all(at>=due for at,due in zip(times[1:],attempts[:-1]))


def require_completed(data,initial,b,registered,released,receipt):
    row=data['row'];same_waiting(initial['row'],row);assert data['job']['id']==row['id']
    assert row['id']==registered['id'] and row['state']==data['job']['state']=='Succeeded' and row['actor_subject']==registered['subject']
    assert row['container_id']==b['containerId'] and row['instance_id']==b['instanceId'] and row['relative_path']==b['path']==WAITING_PATH
    started=dt.datetime.fromisoformat(row['root_started_at_utc']);ended=dt.datetime.fromisoformat(row['completed_at_utc']);assert released<=started<=ended
    assert row['publish_barrier'] is False and row['lease_token'] is row['lease_expires_at_utc'] is None and row['result_code']=='CONTAINER_DIRECTORY_CREATED'
    assert data['pending']==0 and data['accepted']==1 and data['terminalAudits']==1 and data['events']>=2 and re.fullmatch('[A-F0-9]{64}',row['published_version'])
    plan=json.loads(row['plan_json']);r=receipt['plan'];assert receipt['state']=='Completed' and receipt['operationId']==uuid.UUID(row['id']).hex and receipt['owner']==row['owner_hash'] and receipt['version']==row['published_version']
    assert r['command']==dict(containerId=b['containerId'],instanceId=b['instanceId'],path=WAITING_PATH,mode='0750') and r['fingerprint']==plan['Fingerprint'] and r['targetProof']==plan['TargetProof'] and r['parentVersion']==plan['ParentVersion'] and (r['uid'],r['gid'])==(1000,1000)
    unsigned={k:v for k,v in r.items() if k!='fingerprint'};assert r['fingerprint']==hashlib.sha256(b'whaledeck-container-directory-plan-v1\n'+json.dumps(unsigned,sort_keys=True,separators=(',',':'),ensure_ascii=False).encode()).hexdigest().upper()


def finish_waiting(b,registered,ack,directory):
    until=time.monotonic()+100
    while True:
        data=directory_snapshot(registered['id'])
        if data['row']['state']=='Succeeded':break
        assert data['row']['state'] in ('Pending','Applying') and time.monotonic()<until,'WAITING_DIRECTORY_DID_NOT_COMPLETE_AFTER_ACK';time.sleep(.5)
    result=dict(id=registered['id'],path=WAITING_PATH,publishedVersion=data['row']['published_version']);verified=verify_directory(result,b,registered['subject'],directory)
    raw=(ROOT/(uuid.UUID(result['id']).hex+'.json')).read_bytes();initial=json.loads((directory/'waiting-initial.sql.json').read_bytes());released=dt.datetime.fromisoformat(ack['row']['completed_at_utc'])
    require_completed(data,initial,b,registered,released,json.loads(raw));require_deferrals(json.loads((directory/'waiting-blocked.json').read_bytes()),b,registered,initial,released,(directory/'partial.root.json').read_bytes())
    original_file_unchanged(b);(directory/'waiting-final.sql.json').write_text(json.dumps(data));(directory/'waiting-root.json').write_bytes(raw)
    (directory/'waiting-physical.json').write_text(json.dumps(dict(inode=list(verified[1]),privateIdentity=verified[2],emptyDirectory=True,sourceOriginalFileUnchanged=True)))
    return dict(id=result['id'],state='Succeeded',publishedVersion=result['publishedVersion']),verified


def clean_waiting(verified):
    output,expected,private_identity=verified
    assert output.parent.parent==Path('/data/WhaleDeck/apps') and re.fullmatch('wdv-container-publish-[a-f0-9]{32}',output.parent.name) and output.name==WAITING_PATH[5:] and output.parent.resolve(strict=True)==output.parent
    private=output.parent/'.whaledeck-container-directories-v1';info=trusted(private,True)
    assert [info.st_dev,info.st_ino]==private_identity and not list(private.iterdir())
    info=output.lstat();assert stat.S_ISDIR(info.st_mode) and (info.st_dev,info.st_ino,info.st_uid,info.st_gid,stat.S_IMODE(info.st_mode))==expected and not list(output.iterdir())
    output.rmdir();private.rmdir()


def verify_cross_directory(directory,original_ack,recovery,read):
    b=read(directory/'waiting-baseline.json');registered=read(directory/'waiting-registered.json');initial=read(directory/'waiting-initial.sql.json');final=read(directory/'waiting-final.sql.json');ui=recovery['waitingDirectory']
    assert b['containerId']!=original_ack['row']['container_id'] and b['source']!=read(directory/'baseline.json')['source'] and registered['subject']==recovery['subject']
    require_registration(registered,initial,b)
    assert directory_snapshot(registered['id'])==final and ui['id']==registered['id'] and ui['state']=='Succeeded' and ui['publishedVersion']==final['row']['published_version']
    assert all(ui[k] is True for k in ('normalUiRegistration','originalTaskVisibleAfterRelease','visibleDirectory','originAndIdempotencyObserved')) and ui['plans']==ui['registrations']==1 and ui['cancellations']==ui['uploads']==0 and ui['anonymousStatus']==401
    released=dt.datetime.fromisoformat(original_ack['row']['completed_at_utc']);raw=(ROOT/(uuid.UUID(registered['id']).hex+'.json')).read_bytes();assert raw==(directory/'waiting-root.json').read_bytes()
    require_deferrals(read(directory/'waiting-blocked.json'),b,registered,initial,released,(directory/'partial.root.json').read_bytes());require_completed(final,initial,b,registered,released,json.loads(raw))
    physical=read(directory/'waiting-physical.json');assert physical['inode'][:2]==json.loads(raw)['pendingIdentity'] and physical['inode'][2:]==[1000,1000,0o750] and physical['emptyDirectory'] and physical['sourceOriginalFileUnchanged']
    source=Path(b['source']);assert source.parent==Path('/data/WhaleDeck/apps') and source.name==b['containerName'] and not os.path.lexists(source)
    import subprocess
    missing=subprocess.run(['docker','inspect',b['containerId']],capture_output=True);assert missing.returncode!=0 and b'no such' in missing.stderr.lower()
    before=read(directory/'cross-directory-scope.json')['oldDirectoryHistory'];after=history_snapshot();assert all(after.get(k)==v for k,v in before.items())
    return dict(actualCrossContainerDirectoryFence=True,naturalDeferrals=4,sameDirectoryIntentCompletedAfterAck=True,normalUiRegistrationAndResult=True,exactOtherSourceAndContainerAbsent=True,oldDirectoryRecordsUnchanged=len(before))
