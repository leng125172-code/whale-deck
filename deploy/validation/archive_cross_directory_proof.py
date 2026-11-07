"""Archive source refusal and global DB publish-barrier directory witnesses.

The native source claim and global directory scheduling barrier are distinct.
"""
import copy,datetime as dt,hashlib,json,os,re,subprocess,time,uuid
from pathlib import Path
from acl_history_browser_proof import typed_equal
from validate_metric_continuity import query
from validate_container_recycle_archive_browser import archive_sql,preserved
from container_recycle_archive_agent_io import STATE
from validate_container_directory_browser import snapshot as directory_snapshot,ROOT,verify_directory
from container_recycle_cross_directory_proof import WAITING_PATH,STABLE,waiting_baseline,history_snapshot,same_waiting,require_blocked,require_registration,require_completed,original_file_unchanged,clean_waiting
from container_recycle_archive_restart_proof import TABLES


def directory_rows(exclude=None):
    if exclude is None:exclude='00000000-0000-0000-0000-000000000000'
    assert str(uuid.UUID(exclude))==exclude
    return query("SELECT coalesce(json_agg(json_build_object('id',id,'sha256',encode(sha256(convert_to(to_jsonb(d)::text,'UTF8')),'hex')) ORDER BY id),'[]'::json) FROM container_directory_operations d WHERE id<>:'id'::uuid",id=exclude)


def barriers():
    parts=["'"+t+"',(SELECT count(*) FROM "+t+" WHERE publish_barrier)" for t in TABLES]
    return query("SELECT json_build_object("+','.join(parts)+",'file_path_locks',(SELECT count(*) FROM file_path_locks))")


def require_archive_only_barrier(value):
    assert set(value)==set(TABLES)|{'file_path_locks'}
    for key,count in value.items():assert type(count) is int and count==(1 if key=='container_recycle_archive_intents' else 0)


def rejected_key(row,key):
    assert str(uuid.UUID(key))==key and key!=row['idempotency_key']
    return query("SELECT json_build_object('intents',(SELECT count(*) FROM container_recycle_archive_intents WHERE actor_subject=:'actor' AND login_binding=:'binding' AND idempotency_key=:'key'),'jobs',(SELECT count(*) FROM operation_jobs WHERE actor_subject=:'actor' AND idempotency_key=:'key'))",actor=row['actor_subject'],binding=row['login_binding'],key=key)


def require_source_refusal(event,before,after,empty):
    row=before['row'];assert row['state']=='Ready' and row['execution_job_id'] is None and row['root_started_at_utc'] is None
    assert event['id']==row['id'] and event['sourceIntentId']==row['source_intent_id'] and event['status']==409 and event['code']=='OPERATION_CONFLICT'
    assert event['originAndIdempotencyObserved'] is True
    assert str(uuid.UUID(event['rejectedKey']))==event['rejectedKey'] and event['rejectedKey']!=row['idempotency_key']
    assert event['before']['id']==event['after']['id']==row['id'] and event['before']['version']==event['after']['version']==row['version']
    typed_equal(event['before'],event['after']);typed_equal(before,after);typed_equal(empty,dict(intents=0,jobs=0))


def observe_waiting(original,b,registered,current,e):
    initial=directory_snapshot(registered['id']);row=initial['row'];assert b['containerId']!=original['sql']['row']['container_id'] and registered['subject']!=original['sql']['row']['actor_subject']
    require_registration(registered,initial,b);(e/'waiting-initial.sql.json').write_text(json.dumps(initial));samples=[];versions=set();until=time.monotonic()+65
    root=STATE/(original['request']['operationId']+'.json');raw=root.read_bytes();(e/'cross-archive-root.json').write_bytes(raw);target=Path(b['source'])/WAITING_PATH[5:];op=uuid.UUID(row['id']).hex
    while len(samples)<4:
        assert time.monotonic()<until,'FOUR_NATURAL_ARCHIVE_DIRECTORY_DEFERRALS_NOT_OBSERVED'
        data=directory_snapshot(row['id']);at=dt.datetime.now(dt.timezone.utc);same_waiting(row,data['row']);deferred=require_blocked(data,b,registered['subject'],at)
        typed_equal(archive_sql(original['sql']['row']['id']),original['sql']);assert original['sql']['row']['state']=='Unknown' and original['sql']['row']['publish_barrier']
        assert not os.path.lexists(target) and not (ROOT/(op+'.json')).exists() and not (ROOT/(op+'.lock')).exists() and root.read_bytes()==raw
        typed_equal(current(),original);guard=barriers();require_archive_only_barrier(guard);original_file_unchanged(b);assert {p.name for p in Path(b['source']).iterdir()}=={'original.bin'}
        if deferred and data['row']['version'] not in versions:
            versions.add(data['row']['version']);samples.append(dict(observedAtUtc=at.isoformat(),sql=data,rootAbsent=True,targetAbsent=True,archiveUnknownProtected=True,originalPhysicalUnchanged=True,unknownRootSha256=hashlib.sha256(raw).hexdigest(),barriers=guard))
        if len(samples)<4:time.sleep(.5)
    (e/'waiting-blocked.json').write_text(json.dumps(samples));return samples


def require_deferrals(samples,b,registered,initial,released,raw):
    assert len(samples)==4;versions=[];attempts=[];times=[];sha=hashlib.sha256(raw).hexdigest()
    for s in samples:
        at=dt.datetime.fromisoformat(s['observedAtUtc']);assert at.utcoffset() is not None and at<released;same_waiting(initial['row'],s['sql']['row']);assert require_blocked(s['sql'],b,registered['subject'],at)
        assert all(s[k] is True for k in ('rootAbsent','targetAbsent','archiveUnknownProtected','originalPhysicalUnchanged')) and s['unknownRootSha256']==sha;require_archive_only_barrier(s['barriers'])
        versions.append(s['sql']['row']['version']);attempts.append(dt.datetime.fromisoformat(s['sql']['row']['next_attempt_at_utc']));times.append(at)
    assert len(set(versions))==len(set(attempts))==4 and versions==sorted(versions) and times==sorted(times)
    assert all((z-a).total_seconds()>=5 for a,z in zip(attempts,attempts[1:])) and all((z-a).total_seconds()>=5 for a,z in zip(times,times[1:]))
    assert all(at>=due for at,due in zip(times[1:],attempts[:-1]))


def finish_waiting(b,registered,ack,e):
    until=time.monotonic()+100
    while True:
        data=directory_snapshot(registered['id'])
        if data['row']['state']=='Succeeded':break
        assert data['row']['state'] in ('Pending','Applying') and time.monotonic()<until,'DIRECTORY_DID_NOT_COMPLETE_AFTER_ARCHIVE_ACK';time.sleep(.5)
    result=dict(id=registered['id'],path=WAITING_PATH,publishedVersion=data['row']['published_version']);verified=verify_directory(result,b,registered['subject'],e);raw=(ROOT/(uuid.UUID(result['id']).hex+'.json')).read_bytes();released=dt.datetime.fromisoformat(ack['sql']['row']['completed_at_utc']);initial=json.loads((e/'waiting-initial.sql.json').read_text())
    require_completed(data,initial,b,registered,released,json.loads(raw));require_deferrals(json.loads((e/'waiting-blocked.json').read_text()),b,registered,initial,released,(e/'cross-archive-root.json').read_bytes());original_file_unchanged(b)
    (e/'waiting-final.sql.json').write_text(json.dumps(data));(e/'waiting-root.json').write_bytes(raw);(e/'waiting-physical.json').write_text(json.dumps(dict(inode=list(verified[1]),privateIdentity=verified[2],emptyDirectory=True,sourceOriginalFileUnchanged=True)))
    return dict(id=result['id'],state='Succeeded',publishedVersion=result['publishedVersion']),verified


def preserved_cross(before,after,source,purges,archives,directory_id):
    expected=copy.deepcopy(before);old,new=before['directoryRoot'],after['directoryRoot'];op=uuid.UUID(directory_id).hex
    assert all(new.get(k)==v for k,v in old.items()) and set(new)-set(old)=={op+'.json',op+'.lock'}
    expected['directoryRoot']=new;preserved(expected,after,source,purges,archives)


def verify_peer(e,ack,browser,live):
    read=lambda n:json.loads((e/n).read_text());b=read('waiting-baseline.json');registered=read('waiting-registered.json');initial=read('waiting-initial.sql.json');final=read('waiting-final.sql.json');ui=browser['waitingDirectory']
    assert registered['subject']==browser['recoverySubject'] and b['containerId']!=ack['sql']['row']['container_id'];require_registration(registered,initial,b);typed_equal(directory_snapshot(registered['id']),final)
    assert ui['id']==registered['id'] and ui['state']=='Succeeded' and ui['publishedVersion']==final['row']['published_version'] and ui['anonymousStatus']==401
    assert ui['plans']==ui['registrations']==1 and ui['cancellations']==ui['uploads']==0 and all(ui[k] is True for k in ('normalUiRegistration','originalTaskVisibleAfterRelease','visibleDirectory','originAndIdempotencyObserved'))
    released=dt.datetime.fromisoformat(ack['sql']['row']['completed_at_utc']);raw=(ROOT/(uuid.UUID(registered['id']).hex+'.json')).read_bytes();assert raw==(e/'waiting-root.json').read_bytes()
    require_deferrals(read('waiting-blocked.json'),b,registered,initial,released,(e/'cross-archive-root.json').read_bytes());require_completed(final,initial,b,registered,released,json.loads(raw))
    assert directory_rows(registered['id'])==read('cross-directory-scope.json')['oldDirectoryBusiness'];history=history_snapshot();assert all(history.get(k)==v for k,v in read('cross-directory-scope.json')['oldDirectoryHistory'].items())
    physical=read('waiting-physical.json');assert physical['inode'][:2]==json.loads(raw)['pendingIdentity'] and physical['inode'][2:]==[1000,1000,0o750] and physical['emptyDirectory'] and physical['sourceOriginalFileUnchanged']
    folder=Path(b['source']);assert folder.parent==Path('/data/WhaleDeck/apps') and folder.name==b['containerName'] and re.fullmatch('wdv-container-publish-[a-f0-9]{32}',folder.name)
    if live:
        from validate_container_file_publishing import verify_container,common
        c=json.loads(common.command(['docker','inspect',b['containerId']]))[0];verify_container(c,b['containerId'],b['containerName'],folder);assert str(c['State']['Pid'])+':'+c['State']['StartedAt']==b['instanceId']
        original_file_unchanged(b);actual=verify_directory(dict(id=registered['id'],path=WAITING_PATH,publishedVersion=final['row']['published_version']),b,registered['subject'],e);typed_equal(list(actual[1]),physical['inode']);typed_equal(actual[2],physical['privateIdentity'])
    else:assert not os.path.lexists(b['source']) and subprocess.run(['docker','inspect',b['containerId']],capture_output=True).returncode!=0
    return dict(actualArchiveDatabaseBarrier=True,naturalDeferrals=4,sameDirectoryIntentCompletedAfterAck=True,nativeSourceClaimIsNotDirectoryBarrier=True)
