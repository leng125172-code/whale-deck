#!/usr/bin/env python3
"""Fresh visible identity -> Vue source/purge -> SQL/Root/original inode/HDD.

Read-only proof accepts each write before the next. An unsettled owned fixture
is retained. No native operation calls, database edits or old-operation replay.
"""
import hashlib,json,os,re,subprocess,sys,time,uuid
from pathlib import Path
import requests
import validate_container_file_publishing as owned
import validate_container_recycle_browser as source_ui
import container_recycle_agent_io as source_io
import container_recycle_purge_agent_io as purge_io
from recursive_acl_restore_agent_io import read_at
from container_recycle_purge_rpc_proof import digest
import stat
from validate_container_recycle_purge_agent import baseline,preserved,HOSTS
from validate_container_directory_restart import preflight
from validate_container_recursive_acl_browser import no_active_work
from validate_container_file_staging import load
from validate_container_upload_bff import temporary_user,login,logout,request
from validate_databases import Api
from validate_metric_continuity import query
from acl_source_apply_missing_proof import source_closure
from acl_history_browser_proof import typed_equal
from recycle_purge_browser_proof import execution,managed_plan,managed_receipt,job_summary,guid,stored_object,retained_history

SOURCES=('validate_container_recycle_purge_browser.py','validate_container_recycle_purge_browser.mjs','verify_container_recycle_purge_browser.py','recycle_purge_browser_proof.py','test_container_recycle_purge_browser_proof.py','test_container_recycle_purge_browser_scope.py')


def sql(identifier):
    guid(identifier)
    return query('''SELECT json_build_object('row',to_jsonb(c),'planJob',to_jsonb(p),'executionJob',to_jsonb(e),
    'pending',(SELECT count(*) FROM outbox_messages o WHERE o.payload_json->>'jobId' IN(c.id::text,c.execution_job_id::text) AND o.processed_at_utc IS NULL),
    'accepted',(SELECT count(*) FROM audit_events a WHERE a.job_id=c.id AND a.action='containers.files.recycle-purge.plan-accepted'),
    'confirmed',(SELECT count(*) FROM audit_events a WHERE a.job_id=c.execution_job_id AND a.action='containers.files.recycle-purge.confirmed'),
    'canceled',(SELECT count(*) FROM audit_events a WHERE a.job_id=c.id AND a.action='containers.files.recycle-purge.cancel-requested'))
    FROM container_recycle_purge_intents c JOIN operation_jobs p ON p.id=c.id LEFT JOIN operation_jobs e ON e.id=c.execution_job_id WHERE c.id=:'id'::uuid;''',id=identifier)


def idle():
    preflight();no_active_work()
    value=query('''SELECT json_build_object('recycles',(SELECT count(*) FROM container_recycle_intents WHERE completed_at_utc IS NULL OR publish_barrier OR lease_token IS NOT NULL),
    'restores',(SELECT count(*) FROM container_recycle_restore_intents WHERE completed_at_utc IS NULL OR publish_barrier OR lease_token IS NOT NULL));''')
    typed_equal(value,dict(recycles=0,restores=0))
    assert query("SELECT to_json(count(*)) FROM container_recycle_purge_intents WHERE completed_at_utc IS NULL OR publish_barrier OR lease_token IS NOT NULL")==0


def old_rows(container):
    """Hash every column of every other intent, keeping the read bounded.

    Full row JSON now exceeds the shared 64KiB SQL transport limit. SHA256 is
    computed over PostgreSQL's complete to_jsonb text, never a chosen subset.
    """
    assert re.fullmatch('[a-f0-9]{64}',container)
    result=query('''SELECT json_build_object('sources',(SELECT COALESCE(json_agg(json_build_object('id',id,'sha256',encode(sha256(convert_to(to_jsonb(c)::text,'UTF8')),'hex')) ORDER BY id),'[]'::json) FROM container_recycle_intents c WHERE container_id<>:'cid'),
    'restores',(SELECT COALESCE(json_agg(json_build_object('id',id,'sha256',encode(sha256(convert_to(to_jsonb(c)::text,'UTF8')),'hex')) ORDER BY id),'[]'::json) FROM container_recycle_restore_intents c WHERE container_id<>:'cid'));''',cid=container)
    result['purges']=query("SELECT COALESCE(json_agg(json_build_object('id',id,'sha256',encode(sha256(convert_to(to_jsonb(c)::text,'UTF8')),'hex')) ORDER BY id),'[]'::json) FROM container_recycle_purge_intents c WHERE container_id<>:'cid';",cid=container)
    return result


def hdd_observation(operation):
    assert re.fullmatch('[a-f0-9]{32}',operation) and int(operation,16)>0
    io=source_io.installed()
    with io.storage.target(create_namespace=False,read_only=True) as (config,base):
        fd=io.files.open_at(base,source_io.NAMESPACE,os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW)
        try:
            info=os.fstat(fd);assert (info.st_uid,info.st_gid,stat.S_IMODE(info.st_mode))==(0,0,0o700);io.storage.attest(config,fd,read_only=True)
            raw,history,_=read_at(fd,operation+'.json');assert len(raw)<=65536
            data=source_io.file_snapshot(Path('/proc/self/fd/'+str(fd))/(operation+'.data'));assert data is not None
            io.storage.attest(config,fd,read_only=True);assert (info.st_dev,info.st_ino)==(os.fstat(fd).st_dev,os.fstat(fd).st_ino)
            return dict(history=history,data=data,storeIdentity=[info.st_dev,info.st_ino],storageHash=digest(b'',config))
        finally:os.close(fd)


def plan_join(value,fixture,source):
    data=sql(value['id']);r=data['row'];assert r['actor_subject']==fixture['subject']
    assert (r['container_id'],r['instance_id'],r['relative_path'])==(fixture['containerId'],fixture['instanceId'],'work/recycle-rpc.bin')
    assert r['source_intent_id']==source['sql']['row']['id']==value['sourceIntentId']
    op=execution(r['id']);source_op=execution(r['source_intent_id'],source=True)
    assert source_op==value['sourceOperationId']==source['sql']['row']['execution_job_id']
    plan=managed_plan(json.loads(r['plan_json']));typed_equal(plan['command']['source'],source['request'])
    assert plan['command']['historyDigest']==source['receipt']['historyDigest'] and plan['command']['scope']=='Originals'
    assert data['accepted']==1 and data['planJob']['state']=='Succeeded' and data['pending']==0
    job_summary(stored_object(data['planJob']['result_json']),r,execution_stage=False)
    typed_equal(source_ui.sql(r['source_intent_id']),source['sql'])
    return data,plan,op


def prove_ready(value,fixture,source,e):
    data,plan,op=plan_join(value,fixture,source);r=data['row']
    assert r['state']=='Ready' and r['version']==value['version'] and r['execution_job_id'] is r['root_started_at_utc'] is r['receipt_json'] is r['apply_started_at_utc'] is r['reservation_json'] is None
    assert not r['publish_barrier'] and data['executionJob'] is None and data['confirmed']==data['canceled']==0
    for suffix in ('.json','.lock'):assert not (purge_io.STATE/(uuid.UUID(op).hex+suffix)).exists()
    typed_equal(source_io.physical(Path(fixture['folder']),source['request']['operationId']),source['physical'])
    typed_equal(source_io.read_operation(source['request'],source['receipt'],e/'source-originals'),source['records'])
    hdd=hdd_observation(source['request']['operationId']);retained_history(source['hdd'],hdd)
    assert plan['sourceRecordDigest']==digest(b'whaledeck-container-recycle-purge-source-v1\n',source['records']['root'])
    assert plan['historyMetadataDigest']==digest(b'whaledeck-container-recycle-purge-history-v1\n',source['records']['history'])
    typed_equal(plan['storeIdentity'],hdd['storeIdentity']);assert plan['storageHash']==hdd['storageHash']
    return dict(sql=data,plan=plan,hdd=hdd,noExecutionRoot=True,originalStillRetained=True)


def prove_result(value,fixture,source,e,*,create=False):
    data,plan,op=plan_join(value,fixture,source);r=data['row']
    assert r['state']==value['state'] and r['completed_at_utc'] and not r['publish_barrier'] and r['lease_token'] is r['lease_expires_at_utc'] is None
    assert value['confirmation'] is None and value['sourceHistoryDigest']==source['receipt']['historyDigest']
    if r['state']=='Canceled':
        assert r['execution_job_id'] is r['root_started_at_utc'] is r['receipt_json'] is r['root_state'] is r['reservation_json'] is r['apply_started_at_utc'] is None
        assert r['purge_applied'] is r['source_fenced'] is r['owned_fence'] is None and r['cancel_requested_at_utc']
        assert data['executionJob'] is None and data['confirmed']==0 and data['canceled']==1 and not value['reservationRecorded'] and value['purgeApplied'] is None
        for suffix in ('.json','.lock'):assert not (purge_io.STATE/(uuid.UUID(op).hex+suffix)).exists()
        physical=source_io.physical(Path(fixture['folder']),source['request']['operationId']);typed_equal(physical,source['physical'])
        typed_equal(source_io.read_operation(source['request'],source['receipt'],e/'source-originals'),source['records'])
        hdd=hdd_observation(source['request']['operationId']);retained_history(source['hdd'],hdd)
        return dict(sql=data,physical=physical,hdd=hdd,noExecutionRoot=True)
    assert r['state']=='Succeeded' and r['execution_job_id']==value['executionJobId']==op and r['root_started_at_utc'] and r['apply_started_at_utc'] and r['reservation_json']
    assert r['root_state']=='Completed' and r['purge_applied'] is r['source_fenced'] is r['owned_fence'] is True
    assert value['reservationRecorded'] is value['purgeApplied'] is True and data['confirmed']==1 and data['canceled']==0
    assert data['executionJob']['state']=='Succeeded' and data['executionJob']['id']==op
    q=dict(operationId=uuid.UUID(op).hex,owner=r['owner_hash'],plan=plan);receipt=managed_receipt(value['receipt'],q)
    stored=json.loads(r['receipt_json']);typed_equal({k[0].lower()+k[1:]:v for k,v in stored.items()},value['receipt'])
    job_summary(stored_object(data['executionJob']['result_json']),r,execution_stage=True)
    records=purge_io.read_operation(q,receipt,source['records']['root'],source['records']['history'],e/'purge-originals',create=create)
    physical=purge_io.physical(Path(fixture['folder']),q);purge_io.physical_proof(physical,fixture['original'],q,records,receipt)
    hdd=hdd_observation(source['request']['operationId']);retained_history(source['hdd'],hdd,purged=True)
    return dict(sql=data,request=q,receipt=receipt,records=records,physical=physical,hdd=hdd)


def denied(fixture,value):
    base='http://127.0.0.1:8080';endpoint=base+'/api/v1/containers/'+fixture['containerId']+'/files/recycle-purges';recovery=base+'/api/v1/files/container-recycle-purges/recovery'
    paths=[endpoint+'/'+value['id'],endpoint+'/by-key/'+str(uuid.uuid4()),endpoint+'/sources',recovery,recovery+'/'+value['id']]
    anonymous=requests.Session();anonymous.trust_env=False
    try:
        for path in paths:request(anonymous,'GET',path,401)
    finally:anonymous.close()
    with temporary_user(False) as (username,password):
        session,_=login(base,username,password,False)
        try:
            api=Api(base,session)
            for path in paths:request(session,'GET',path,403)
            request(session,'POST',endpoint,403,headers={**api.headers,'Idempotency-Key':str(uuid.uuid4())},json=dict(sourceIntentId=value['sourceIntentId']))
            for action in ('confirm','cancel'):
                request(session,'POST',endpoint+'/'+value['id']+'/'+action,403,headers={**api.headers,'Idempotency-Key':value['id']},json=dict(planHash='not-authority',confirmed=True,version=value['version']) if action=='confirm' else None)
            request(session,'POST',recovery+'/'+value['id']+'/acknowledge',403,headers={**api.headers,'Idempotency-Key':value['id']},json=dict(version=value['version'],confirmed=True));logout(base,session)
        finally:session.close()


def independent(repo,e,live):
    with (e/('independent-live.log' if live else 'independent-final.log')).open('x') as out:
        subprocess.run([sys.executable,str(repo/'deploy/validation/verify_container_recycle_purge_browser.py'),e.name]+(['--live'] if live else []),check=True,stdout=out,stderr=subprocess.STDOUT,timeout=90)


def stop_browser(process,grace=3):
    """Stop only this owning Popen child; never signal a product process."""
    if process.poll() is not None:return
    try:process.terminate()
    except ProcessLookupError:pass
    try:process.wait(timeout=grace)
    except subprocess.TimeoutExpired:
        try:process.kill()
        except ProcessLookupError:pass
        process.wait(timeout=grace)


def main(revision):
    assert os.geteuid()==0 and re.fullmatch('[a-f0-9]{40}',revision);os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0';repo=Path(__file__).resolve().parents[2]
    git=lambda *args:subprocess.check_output(['git','-c','safe.directory='+str(repo),'-C',str(repo),*args])
    assert not git('status','--porcelain').strip();sha=git('rev-parse','HEAD').decode().strip();closure=source_closure(repo,sha,seeds=SOURCES)
    for n,h in closure.items():assert hashlib.sha256((repo/'deploy/validation'/n).read_bytes()).hexdigest()==h
    for n in HOSTS:assert (Path('/usr/local/libexec')/n).read_bytes()==git('show',revision+':deploy/host/'+n)
    idle();before=baseline(revision)
    e=repo/'deploy/artifacts'/('container-recycle-purge-browser-'+uuid.uuid4().hex);e.mkdir(mode=0o700)
    def save(name,value):
        with (e/name).open('x') as out:json.dump(value,out)
    frozen=e/'source';frozen.mkdir(mode=0o700)
    for n,h in closure.items():
        raw=git('show',sha+':deploy/validation/'+n);assert hashlib.sha256(raw).hexdigest()==h;(frozen/n).write_bytes(raw)
    save('before.json',before);save('source.json',dict(revision=revision,validationRevision=sha,validationSourceSha256=closure));print(json.dumps(dict(actualRecyclePurge='started',evidence=e.name)),flush=True)
    try:
        with owned.fixture() as f:
            f['retain_unsettled']=True;leaf=f['folder']/'recycle-rpc.bin';leaf.write_bytes(hashlib.shake_256(('real-independent-purge:'+f['identifier']).encode()).digest(1024**2+7))
            os.chown(leaf,1000,1000);leaf.chmod(0o640);os.setxattr(leaf,'user.recycle',b'actual originals purge original');original=source_io.file_snapshot(leaf)
            c=json.loads(owned.common.command(['docker','inspect',f['identifier']]))[0];header=dict(containerId=f['identifier'],instanceId=str(c['State']['Pid'])+':'+c['State']['StartedAt'])
            reader=load('/usr/local/libexec/whaledeck-container-files.py','real_purge_reader')
            fixture=dict(revision=revision,containerId=f['identifier'],containerName=c['Name'].lstrip('/'),instanceId=header['instanceId'],header=header,folder=str(f['folder']),evidence=str(e),original=original,sourceVersion=reader.version(header,'work/recycle-rpc.bin',leaf.stat()))
            save('fixture-initial.json',fixture);save('prior-business.json',old_rows(f['identifier']))
            with temporary_user(True) as (username,password):
                with (e/'browser.stdout').open('x') as out,(e/'browser.stderr').open('x') as err:
                    process=subprocess.Popen(['node',str(repo/'deploy/validation/validate_container_recycle_purge_browser.mjs'),revision],stdin=subprocess.PIPE,stdout=out,stderr=err,text=True,env={**os.environ,'WHALEDECK_URL':'http://127.0.0.1:8080','VALIDATION_USERNAME':username,'VALIDATION_PASSWORD':password})
                    source=cancel=ready=final=success=None
                    try:
                        process.stdin.write(json.dumps(fixture));process.stdin.close();deadline=time.monotonic()+510
                        while process.poll() is None:
                            for phase in ('source','cancel','ready-to-purge','final-confirm','success'):
                                marker=e/(phase+'-completed.json');accepted=e/(phase+'-accepted.json')
                                if not marker.exists() or accepted.exists():continue
                                signal=json.loads(marker.read_text());fixture['subject']=signal['subject'];value=signal['result']
                                if phase=='source':
                                    assert source is None and not (e/'source-originals').exists()
                                    source=source_ui.verify_case(value,fixture,e,create=True)
                                    # Rename only this invocation's new private archive, never installed records.
                                    (e/'originals').rename(e/'source-originals')
                                    source['hdd']=hdd_observation(source['request']['operationId']);retained_history(source['hdd'],source['hdd']);typed_equal(source['hdd']['history'],source['records']['history']);typed_equal(source['hdd']['data'],source['records']['data']);save('source-proof.json',source)
                                elif phase=='cancel':assert source and cancel is None;cancel=prove_result(value,fixture,source,e);save('cancel-proof.json',cancel)
                                elif phase=='ready-to-purge':
                                    assert source and cancel and ready is None;ready=prove_ready(value,fixture,source,e)
                                    ops=(uuid.UUID(execution(cancel['sql']['row']['id'])).hex,uuid.UUID(execution(value['id'])).hex)
                                    preserved(before,baseline(revision),source['request']['operationId'],ops)
                                    typed_equal(json.loads((e/'prior-business.json').read_text()),old_rows(fixture['containerId']));save('ready-proof.json',ready)
                                elif phase=='final-confirm':
                                    assert ready and final is None;final=prove_ready(value,fixture,source,e)
                                    typed_equal(final['plan'],ready['plan']);assert final['sql']['row']['id']==ready['sql']['row']['id'] and final['sql']['row']['version']==ready['sql']['row']['version']
                                    ops=(uuid.UUID(execution(cancel['sql']['row']['id'])).hex,uuid.UUID(execution(value['id'])).hex)
                                    preserved(before,baseline(revision),source['request']['operationId'],ops)
                                    typed_equal(json.loads((e/'prior-business.json').read_text()),old_rows(fixture['containerId']));save('final-confirm-proof.json',final)
                                else:assert source and cancel and ready and final and success is None;success=prove_result(value,fixture,source,e,create=True);save('success-proof.json',success)
                                save(phase+'-accepted.json',dict(id=value['id']))
                            assert time.monotonic()<deadline,'REAL_PURGE_BROWSER_DEADLINE';time.sleep(.1)
                        assert process.returncode==0,'REAL_PURGE_BROWSER_FAILED'
                    except BaseException as primary:
                        import traceback
                        save('browser-primary-failure.json',dict(type=type(primary).__name__,frames=[dict(file=Path(t.filename).name,line=t.lineno,function=t.name) for t in traceback.extract_tb(primary.__traceback__)]))
                        raise
                    finally:
                        try:stop_browser(process)
                        except BaseException as cleanup:save('browser-stop-failure.json',dict(type=type(cleanup).__name__,pid=process.pid))
                assert source and cancel and ready and final and success and (e/'browser.stdout').stat().st_size<16384 and (e/'browser.stderr').stat().st_size<16384
                browser=json.loads((e/'browser.json').read_text());assert browser['normalDualLogout'] and browser['actualUi'] and browser['reloginOriginalReferencesDenied']==5
                save('fixture.json',fixture);denied(fixture,browser['completed'][1]);independent(repo,e,True)
            # Only the already-proved empty private directory of this fresh fixture.
            typed_equal(purge_io.physical(f['folder'],success['request']),success['physical'])
            private=f['folder']/source_io.PRIVATE;i=private.lstat();assert not private.is_symlink() and [i.st_dev,i.st_ino]==success['request']['plan']['privateIdentity'] and not list(private.iterdir())
            private.rmdir();f['retain_unsettled']=False
        after_hdd=hdd_observation(source['request']['operationId']);retained_history(source['hdd'],after_hdd,purged=True);save('hdd-after-cleanup.json',after_hdd)
        after=baseline(revision);ops=[uuid.UUID(execution(v['id'])).hex for v in browser['completed']];preserved(before,after,source['request']['operationId'],ops);save('after.json',after)
        typed_equal(json.loads((e/'prior-business.json').read_text()),old_rows(fixture['containerId']));idle();assert not Path(fixture['folder']).exists()
        save('verified.json',dict(actualUi=True,actualIdentity=True,sourceOperations=1,purgeIntents=2,readyCanceled=1,completedOriginalsPurge=1,hddRereadAfterCleanup=True,ordinaryDenied=9,anonymousDenied=5,normalDualLogouts=2,fixtureRemoved=True,historiesRetained=True))
        independent(repo,e,False);print(json.dumps(dict(actualRecyclePurge='passed',evidence=e.name)),flush=True)
    except BaseException as error:
        import traceback
        save('failure.json',dict(type=type(error).__name__,frames=[dict(file=Path(t.filename).name,line=t.lineno,function=t.name) for t in traceback.extract_tb(error.__traceback__)]));raise


if __name__=='__main__':
    try:assert len(sys.argv)==2;main(sys.argv[1])
    except BaseException as error:print(json.dumps(dict(actualRecyclePurge='failed',type=type(error).__name__)),flush=True);raise SystemExit(1) from None
