#!/usr/bin/env python3
"""Fresh visible identity -> Vue source/restore -> SQL/Root/original inode/HDD.

Read-only proof accepts each write before the next. An unsettled owned fixture
is retained. No native operation calls, database edits or old-operation replay.
"""
import hashlib,json,os,re,subprocess,sys,time,uuid
from pathlib import Path
import requests
import validate_container_file_publishing as owned
import validate_container_recycle_browser as source_ui
import container_recycle_agent_io as source_io
import container_recycle_restore_agent_io as restore_io
from validate_container_recycle_restore_agent import baseline,preserved
from validate_container_directory_restart import preflight
from validate_container_recursive_acl_browser import no_active_work
from validate_container_file_staging import load
from validate_container_upload_bff import temporary_user,login,logout,request
from validate_databases import Api
from validate_metric_continuity import query
from acl_source_apply_missing_proof import source_closure
from acl_history_browser_proof import typed_equal
from recycle_restore_browser_proof import execution,managed_plan,managed_receipt,job_summary,guid,stored_object

SOURCES=('validate_container_recycle_restore_browser.py','validate_container_recycle_restore_browser.mjs','verify_container_recycle_restore_browser.py','recycle_restore_browser_proof.py','test_recycle_restore_browser_proof.py')


def sql(identifier):
    guid(identifier)
    return query('''SELECT json_build_object('row',to_jsonb(c),'planJob',to_jsonb(p),'executionJob',to_jsonb(e),
    'pending',(SELECT count(*) FROM outbox_messages o WHERE o.payload_json->>'jobId' IN(c.id::text,c.execution_job_id::text) AND o.processed_at_utc IS NULL),
    'accepted',(SELECT count(*) FROM audit_events a WHERE a.job_id=c.id AND a.action='containers.files.recycle-restore.plan-accepted'),
    'confirmed',(SELECT count(*) FROM audit_events a WHERE a.job_id=c.execution_job_id AND a.action='containers.files.recycle-restore.confirmed'),
    'canceled',(SELECT count(*) FROM audit_events a WHERE a.job_id=c.id AND a.action='containers.files.recycle-restore.cancel-requested'))
    FROM container_recycle_restore_intents c JOIN operation_jobs p ON p.id=c.id LEFT JOIN operation_jobs e ON e.id=c.execution_job_id WHERE c.id=:'id'::uuid;''',id=identifier)


def idle():
    preflight();no_active_work()
    value=query('''SELECT json_build_object('recycles',(SELECT count(*) FROM container_recycle_intents WHERE completed_at_utc IS NULL OR publish_barrier OR lease_token IS NOT NULL),
    'restores',(SELECT count(*) FROM container_recycle_restore_intents WHERE completed_at_utc IS NULL OR publish_barrier OR lease_token IS NOT NULL));''')
    typed_equal(value,dict(recycles=0,restores=0))


def old_rows(container):
    """Hash every column of every other intent, keeping the read bounded.

    Full row JSON now exceeds the shared 64KiB SQL transport limit. SHA256 is
    computed over PostgreSQL's complete to_jsonb text, never a chosen subset.
    """
    assert re.fullmatch('[a-f0-9]{64}',container)
    return query('''SELECT json_build_object('sources',(SELECT COALESCE(json_agg(json_build_object('id',id,'sha256',encode(sha256(convert_to(to_jsonb(c)::text,'UTF8')),'hex')) ORDER BY id),'[]'::json) FROM container_recycle_intents c WHERE container_id<>:'cid'),
    'restores',(SELECT COALESCE(json_agg(json_build_object('id',id,'sha256',encode(sha256(convert_to(to_jsonb(c)::text,'UTF8')),'hex')) ORDER BY id),'[]'::json) FROM container_recycle_restore_intents c WHERE container_id<>:'cid'));''',cid=container)


def prove_result(value,fixture,source,e,*,create=False):
    data=sql(value['id']);r=data['row'];assert r['actor_subject']==fixture['subject']
    assert (r['container_id'],r['instance_id'],r['relative_path'])==(fixture['containerId'],fixture['instanceId'],'work/recycle-rpc.bin')
    assert r['source_intent_id']==source['sql']['row']['id']==value['sourceIntentId']
    op=execution(r['id']);source_op=execution(r['source_intent_id'],source=True)
    assert source_op==value['sourceOperationId']==source['sql']['row']['execution_job_id']
    plan=managed_plan(json.loads(r['plan_json']));typed_equal(plan['command']['source'],source['request'])
    assert plan['command']['historyDigest']==value['sourceHistoryDigest']==source['receipt']['historyDigest']
    assert r['state']==value['state'] and r['completed_at_utc'] and not r['publish_barrier'] and r['lease_token'] is r['lease_expires_at_utc'] is None
    assert data['pending']==0 and data['accepted']==1 and data['planJob']['state']=='Succeeded' and value['confirmation'] is None
    typed_equal(stored_object(data['planJob']['result_json']),job_summary(stored_object(data['planJob']['result_json']),r,execution_stage=False))
    typed_equal(source_ui.sql(r['source_intent_id']),source['sql'])
    if r['state']=='Canceled':
        assert r['execution_job_id'] is r['root_started_at_utc'] is r['receipt_json'] is r['root_state'] is None
        assert r['cancel_requested_at_utc'] and not r['history_verified'] and r['history_digest'] is None
        assert data['executionJob'] is None and data['confirmed']==0 and data['canceled']==1
        for k in ('binding_current','source_state','source_retained','source_restored','restored_by_this_operation','history_available'):assert r[k] is None
        assert not (restore_io.STATE/(uuid.UUID(op).hex+'.json')).exists() and not (restore_io.STATE/(uuid.UUID(op).hex+'.lock')).exists()
        typed_equal(source_io.physical(Path(fixture['folder']),uuid.UUID(source_op).hex),source['physical'])
        typed_equal(source_io.read_operation(source['request'],source['receipt'],e/'source-originals'),source['records'])
        return dict(sql=data,physical=source_io.physical(Path(fixture['folder']),uuid.UUID(source_op).hex),noExecutionRoot=True)
    assert r['state']=='Succeeded' and r['execution_job_id']==value['executionJobId']==op and r['root_started_at_utc']
    assert r['root_state']=='Completed' and r['binding_current'] is r['history_verified'] is r['source_restored'] is r['restored_by_this_operation'] is r['history_available'] is True
    assert r['source_state']=='Restored' and r['source_retained'] is False and data['confirmed']==1 and data['canceled']==0
    assert data['executionJob']['state']=='Succeeded' and data['executionJob']['id']==op
    q=dict(operationId=uuid.UUID(op).hex,owner=r['owner_hash'],plan=plan);receipt=managed_receipt(value['receipt'],q)
    assert r['history_digest']==receipt['historyDigest']==plan['command']['historyDigest']
    stored=json.loads(r['receipt_json']);typed_equal({k[0].lower()+k[1:]:v for k,v in stored.items()},value['receipt'])
    job_summary(stored_object(data['executionJob']['result_json']),r,execution_stage=True)
    records=restore_io.read_operation(q,receipt,e/'restore-originals',create=create)
    physical=source_io.physical(Path(fixture['folder']),uuid.UUID(source_op).hex)
    restore_io.physical_proof(physical,fixture['original'],records,receipt)
    return dict(sql=data,request=q,receipt=receipt,records=records,physical=physical)


def denied(fixture,value):
    base='http://127.0.0.1:8080';endpoint=base+'/api/v1/containers/'+fixture['containerId']+'/files/recycle-restores';recovery=base+'/api/v1/files/container-recycle-restores/recovery'
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
        subprocess.run([sys.executable,str(repo/'deploy/validation/verify_container_recycle_restore_browser.py'),e.name]+(['--live'] if live else []),check=True,stdout=out,stderr=subprocess.STDOUT,timeout=90)


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
    idle();before=baseline(revision)
    e=repo/'deploy/artifacts'/('container-recycle-restore-browser-'+uuid.uuid4().hex);e.mkdir(mode=0o700)
    def save(name,value):
        with (e/name).open('x') as out:json.dump(value,out)
    save('before.json',before);save('source.json',dict(revision=revision,validationRevision=sha,validationSourceSha256=closure));print(json.dumps(dict(actualRecycleRestore='started',evidence=e.name)),flush=True)
    try:
        with owned.fixture() as f:
            f['retain_unsettled']=True;leaf=f['folder']/'recycle-rpc.bin';leaf.write_bytes(hashlib.shake_256(('real-independent-restore:'+f['identifier']).encode()).digest(1024**2+7))
            os.chown(leaf,1000,1000);leaf.chmod(0o640);os.setxattr(leaf,'user.recycle',b'actual independent restore original');original=source_io.file_snapshot(leaf)
            c=json.loads(owned.common.command(['docker','inspect',f['identifier']]))[0];header=dict(containerId=f['identifier'],instanceId=str(c['State']['Pid'])+':'+c['State']['StartedAt'])
            reader=load('/usr/local/libexec/whaledeck-container-files.py','real_independent_restore_reader')
            fixture=dict(revision=revision,containerId=f['identifier'],containerName=c['Name'].lstrip('/'),instanceId=header['instanceId'],header=header,folder=str(f['folder']),evidence=str(e),original=original,sourceVersion=reader.version(header,'work/recycle-rpc.bin',leaf.stat()))
            save('fixture-initial.json',fixture);save('prior-business.json',old_rows(f['identifier']))
            with temporary_user(True) as (username,password):
                with (e/'browser.stdout').open('x') as out,(e/'browser.stderr').open('x') as err:
                    process=subprocess.Popen(['node',str(repo/'deploy/validation/validate_container_recycle_restore_browser.mjs'),revision],stdin=subprocess.PIPE,stdout=out,stderr=err,text=True,env={**os.environ,'WHALEDECK_URL':'http://127.0.0.1:8080','VALIDATION_USERNAME':username,'VALIDATION_PASSWORD':password})
                    source=cancel=success=None
                    try:
                        process.stdin.write(json.dumps(fixture));process.stdin.close();deadline=time.monotonic()+510
                        while process.poll() is None:
                            for phase in ('source','cancel','success'):
                                marker=e/(phase+'-completed.json');accepted=e/(phase+'-accepted.json')
                                if not marker.exists() or accepted.exists():continue
                                signal=json.loads(marker.read_text());fixture['subject']=signal['subject'];value=signal['result']
                                if phase=='source':
                                    assert source is None and not (e/'source-originals').exists()
                                    source=source_ui.verify_case(value,fixture,e,create=True)
                                    # Rename only this invocation's new private archive, never installed records.
                                    (e/'originals').rename(e/'source-originals')
                                    save('source-proof.json',source)
                                elif phase=='cancel':assert source and cancel is None;cancel=prove_result(value,fixture,source,e);save('cancel-proof.json',cancel)
                                else:assert source and cancel and success is None;success=prove_result(value,fixture,source,e,create=True);save('success-proof.json',success)
                                save(phase+'-accepted.json',dict(id=value['id']))
                            assert time.monotonic()<deadline,'REAL_RESTORE_BROWSER_DEADLINE';time.sleep(.1)
                        assert process.returncode==0,'REAL_RESTORE_BROWSER_FAILED'
                    except BaseException as primary:
                        import traceback
                        save('browser-primary-failure.json',dict(type=type(primary).__name__,frames=[dict(file=Path(t.filename).name,line=t.lineno,function=t.name) for t in traceback.extract_tb(primary.__traceback__)]))
                        raise
                    finally:
                        try:stop_browser(process)
                        except BaseException as cleanup:save('browser-stop-failure.json',dict(type=type(cleanup).__name__,pid=process.pid))
                assert source and cancel and success and (e/'browser.stdout').stat().st_size<16384 and (e/'browser.stderr').stat().st_size<16384
                browser=json.loads((e/'browser.json').read_text());assert browser['normalDualLogout'] and browser['actualUi'] and browser['reloginOriginalReferencesDenied']==5
                save('fixture.json',fixture);denied(fixture,browser['completed'][1]);independent(repo,e,True)
            # Remove only this gate's restored, independently reopened inode after all business/identity checks.
            typed_equal(source_io.file_snapshot(leaf),success['physical']['source']);leaf.unlink()
            private=f['folder']/source_io.PRIVATE;i=private.lstat();assert not private.is_symlink() and [i.st_dev,i.st_ino]==success['records']['sourceRoot']['privateIdentity'] and not list(private.iterdir());private.rmdir();f['retain_unsettled']=False
        after=baseline(revision);ops=[uuid.UUID(execution(v['id'])).hex for v in browser['completed']];preserved(before,after,source['request']['operationId'],ops);save('after.json',after)
        typed_equal(json.loads((e/'prior-business.json').read_text()),old_rows(fixture['containerId']));idle();assert not Path(fixture['folder']).exists()
        save('verified.json',dict(actualUi=True,actualIdentity=True,sourceOperations=1,restoreIntents=2,readyCanceled=1,completedRestore=1,ordinaryDenied=9,anonymousDenied=5,normalDualLogouts=2,fixtureRemoved=True,historiesRetained=True))
        independent(repo,e,False);print(json.dumps(dict(actualRecycleRestore='passed',evidence=e.name)),flush=True)
    except BaseException as error:
        import traceback
        save('failure.json',dict(type=type(error).__name__,frames=[dict(file=Path(t.filename).name,line=t.lineno,function=t.name) for t in traceback.extract_tb(error.__traceback__)]));raise


if __name__=='__main__':
    try:assert len(sys.argv)==2;main(sys.argv[1])
    except BaseException as error:print(json.dumps(dict(actualRecycleRestore='failed',type=type(error).__name__)),flush=True);raise SystemExit(1) from None
