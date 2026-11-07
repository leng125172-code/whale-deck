#!/usr/bin/env python3
"""Candidate installed visible archive chain; not accepted until independent live/final proof.

Fresh own fixture only. Credentials private stdin; no direct RPC/SQL writes.
"""
from validate_container_recycle_purge_browser import *
import validate_container_recycle_purge_browser as purging
import container_recycle_archive_agent_io as archive_io
from validate_container_recycle_archive_agent import baseline,cleanup_new_copy
from recycle_archive_browser_proof import managed_plan as archive_plan,public_receipt,receipt_join,job_summary as archive_summary,execution as archive_execution
from validate_container_recycle_archive_agent import HOSTS
from recycle_purge_browser_proof import managed_plan as old_plan

SOURCES=('validate_container_recycle_archive_published_logout.py','validate_container_recycle_archive_published_logout.mjs','verify_container_recycle_archive_published_logout.py','archive_published_logout_proof.py','validate_container_recycle_archive_inflight_logout.py','cleanup_owned_archive_published_target.py','test_container_recycle_archive_published_logout.py','bound_container_recycle_archive_published_cut.py','test_container_recycle_archive_published_cut.py')
from concurrent.futures import ThreadPoolExecutor
from bound_container_recycle_archive_published_cut import freeze_published_namespace
from cleanup_owned_archive_published_target import cleanup_owned_target
from archive_published_logout_proof import prove as prove_unknown,require_ack,browser_proof,logout_exit_proof,result_audit,result_order
from validate_container_recycle_archive_inflight_logout import observe_archive_logout_exit
from datetime import datetime,timezone,timedelta
from archive_inflight_cancel_proof import reservation_proof



def idle():
    purging.idle();assert query("SELECT to_json(count(*)) FROM container_recycle_archive_intents WHERE completed_at_utc IS NULL OR publish_barrier OR lease_token IS NOT NULL")==0


def old_rows(container):
    result=purging.old_rows(container)
    result['archives']=query("SELECT COALESCE(json_agg(json_build_object('id',id,'sha256',encode(sha256(convert_to(to_jsonb(c)::text,'UTF8')),'hex')) ORDER BY id),'[]'::json) FROM container_recycle_archive_intents c WHERE container_id<>:'cid'",cid=container)
    return result


def preserved(before,after,source_id,purges,archives=()):
    assert len(purges)==2 and len(archives) in (0,2) and len({source_id,*purges,*archives})==1+len(purges)+len(archives)
    allowed=dict(recycleRoot={source_id+n for n in ('.json','.lock','.purge.json')},recycleHdd={source_id+n for n in ('.json','.data')},purgeRoot={op+n for op in purges for n in ('.json','.lock')},archiveRoot={op+n for op in archives for n in ('.json','.lock')},recycleRestoreRoot={'claims/'+source_id+'.json'} if archives else set())
    for key in before:
        if key not in allowed:typed_equal(before[key],after[key])
        else:
            assert all(after[key].get(n)==value for n,value in before[key].items());assert set(after[key])-set(before[key])<=allowed[key]


def archive_sql(identifier):
    guid(identifier)
    return query("""SELECT json_build_object('row',to_jsonb(c),'planJob',to_jsonb(p),'executionJob',to_jsonb(e),
      'pending',(SELECT count(*) FROM outbox_messages o WHERE o.payload_json->>'jobId' IN(c.id::text,c.execution_job_id::text) AND o.processed_at_utc IS NULL),
      'accepted',(SELECT count(*) FROM audit_events a WHERE a.job_id=c.id AND a.action='containers.files.recycle-archive.plan-accepted'),
      'confirmed',(SELECT count(*) FROM audit_events a WHERE a.job_id=c.execution_job_id AND a.action='containers.files.recycle-archive.confirmed'),
      'canceled',(SELECT count(*) FROM audit_events a WHERE a.job_id=c.id AND a.action='containers.files.recycle-archive.cancel-requested'))
      FROM container_recycle_archive_intents c JOIN operation_jobs p ON p.id=c.id LEFT JOIN operation_jobs e ON e.id=c.execution_job_id WHERE c.id=:'id'::uuid;""",id=identifier)


def archive_join(value,fixture,source,purge):
    data=archive_sql(value['id']);r=data['row'];plan=archive_plan(json.loads(r['plan_json']));op=archive_execution(r['id'])
    assert r['actor_subject']==fixture['subject'] and (r['container_id'],r['instance_id'],r['relative_path'])==(fixture['containerId'],fixture['instanceId'],'work/recycle-rpc.bin')
    assert r['source_intent_id']==purge['sql']['row']['id']==value['sourceIntentId']
    typed_equal(plan['command']['purge'],purge['request']);assert plan['command']['historyDigest']==source['receipt']['historyDigest']
    assert data['accepted']==1 and data['planJob']['state']=='Succeeded' and data['pending']==0
    archive_summary(stored_object(data['planJob']['result_json']),r,purge['sql']['row'],execution_stage=False)
    typed_equal(purging.sql(r['source_intent_id']),purge['sql']);typed_equal(source_ui.sql(source['sql']['row']['id']),source['sql'])
    return data,plan,op


def prove_archive_ready(value,fixture,source,purge,e):
    data,plan,op=archive_join(value,fixture,source,purge);r=data['row']
    assert r['state']=='Ready' and r['version']==value['version'] and r['execution_job_id'] is r['root_started_at_utc'] is r['receipt_json'] is r['reservation_json'] is r['apply_started_at_utc'] is None
    assert not r['publish_barrier'] and data['executionJob'] is None and data['confirmed']==data['canceled']==0
    for suffix in ('.json','.lock'):assert not (archive_io.STATE/(uuid.UUID(op).hex+suffix)).exists()
    q=dict(operationId=uuid.UUID(op).hex,owner=r['owner_hash'],plan=plan);physical=archive_io.physical(Path(fixture['folder']),q);typed_equal(physical['oldPrivateIdentity'],plan['privateIdentity']);assert physical['source'] is physical['stageIdentity'] is physical['staged'] is physical['retained'] is physical['pending'] is None
    current_purge=purge_io.read_operation(purge['request'],purge['receipt'],source['records']['root'],source['records']['history'],e/'purge-originals');typed_equal(current_purge,purge['records'])
    hdd=hdd_observation(source['request']['operationId']);retained_history(source['hdd'],hdd,purged=True)
    return dict(sql=data,plan=plan,hdd=hdd,noExecutionRoot=True)


def prove_archive_result(value,fixture,source,purge,e,*,create=False):
    data,plan,op=archive_join(value,fixture,source,purge);r=data['row'];assert r['state']==value['state'] and r['completed_at_utc'] and not r['publish_barrier'] and r['lease_token'] is r['lease_expires_at_utc'] is None
    q=dict(operationId=uuid.UUID(op).hex,owner=r['owner_hash'],plan=plan)
    if r['state']=='Canceled':
        assert r['execution_job_id'] is r['root_started_at_utc'] is r['receipt_json'] is r['reservation_json'] is r['apply_started_at_utc'] is None
        assert r['archive_copied'] is r['binding_current'] is r['source_fenced'] is r['owned_fence'] is None
        assert data['confirmed']==0 and data['canceled']==1 and data['executionJob'] is None
        for suffix in ('.json','.lock'):assert not (archive_io.STATE/(uuid.UUID(op).hex+suffix)).exists()
        current=archive_io.physical(Path(fixture['folder']),q);assert current['source'] is current['stageIdentity'] is None
        hdd=hdd_observation(source['request']['operationId']);retained_history(source['hdd'],hdd,purged=True)
        return dict(sql=data,request=q,physical=current,hdd=hdd,noExecutionRoot=True)
    assert r['state']=='Succeeded' and r['execution_job_id']==value['executionJobId']==op and r['root_state']=='Completed' and r['archive_copied'] is True and r['binding_current'] is True
    assert r['root_started_at_utc'] and r['reservation_json'] and r['apply_started_at_utc'] and value['reservationRecorded'] is value['archiveCopied'] is True
    assert data['confirmed']==1 and data['canceled']==0 and data['executionJob']['state']=='Succeeded'
    receipt=receipt_join(r,value['receipt'],q);records=archive_io.read_operation(q,receipt,purge['records'],e/'archive-originals',create=create)
    physical=archive_io.physical(Path(fixture['folder']),q);archive_io.physical_proof(physical,fixture['original'],q,records,receipt)
    archive_summary(stored_object(data['executionJob']['result_json']),r,purge['sql']['row'],execution_stage=True)
    hdd=hdd_observation(source['request']['operationId']);retained_history(source['hdd'],hdd,purged=True)
    return dict(sql=data,request=q,receipt=receipt,records=records,physical=physical,hdd=hdd)


def observe_cut(identifier,folder,pid,diagnostics):
    deadline=time.monotonic()+105
    while time.monotonic()<deadline:
        row=archive_sql(identifier)['row']
        if row['execution_job_id']:return freeze_published_namespace(row,folder,pid,diagnostics)
        assert row['state']=='Ready';time.sleep(.005)
    diagnostics['missReason']='NO_ORIGINAL_EXECUTION';return None


def settle_cut(pool,future,held):
    # Joining already waits for the observer. Retrieve its result after joining,
    # including a late success; never drop custody due to a result timeout.
    try:
        if held is not None:held.release()
    finally:
        pool.shutdown(wait=True,cancel_futures=True)
        if held is None and future is not None and not future.cancelled():
            late=future.result()
            if late is not None:late.release()


def independent(repo,e,live):
    with (e/('independent-live.log' if live else 'independent-final.log')).open('x') as out:
        subprocess.run([sys.executable,str(repo/'deploy/validation/verify_container_recycle_archive_published_logout.py'),e.name]+(['--live'] if live else []),check=True,stdout=out,stderr=subprocess.STDOUT,timeout=120)


def denied_archive(fixture,value):
    base='http://127.0.0.1:8080';endpoint=base+'/api/v1/containers/'+fixture['containerId']+'/files/recycle-archives';recovery=base+'/api/v1/files/container-recycle-archives/recovery'
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



def main(revision):
    assert os.geteuid()==0 and re.fullmatch('[a-f0-9]{40}',revision);os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0';repo=Path(__file__).resolve().parents[2]
    git=lambda *args:subprocess.check_output(['git','-c','safe.directory='+str(repo),'-C',str(repo),*args])
    assert not git('status','--porcelain').strip();sha=git('rev-parse','HEAD').decode().strip();closure=source_closure(repo,sha,seeds=SOURCES)
    for n,h in closure.items():assert hashlib.sha256((repo/'deploy/validation'/n).read_bytes()).hexdigest()==h
    for n in HOSTS:assert (Path('/usr/local/libexec')/n).read_bytes()==git('show',revision+':deploy/host/'+n)
    idle();before=baseline(revision)
    e=repo/'deploy/artifacts'/('container-recycle-archive-published-logout-'+uuid.uuid4().hex);e.mkdir(mode=0o700)
    def save(name,value):
        with (e/name).open('x') as out:json.dump(value,out)
    frozen=e/'source';frozen.mkdir(mode=0o700)
    for n,h in closure.items():
        raw=git('show',sha+':deploy/validation/'+n);assert hashlib.sha256(raw).hexdigest()==h;(frozen/n).write_bytes(raw)
    save('before.json',before);save('source.json',dict(revision=revision,validationRevision=sha,validationSourceSha256=closure));print(json.dumps(dict(actualArchivePublishedLogout='started',evidence=e.name)),flush=True)
    try:
        with owned.fixture() as f:
            f['retain_unsettled']=True;leaf=f['folder']/'recycle-rpc.bin';leaf.write_bytes(hashlib.shake_256(('real-independent-purge:'+f['identifier']).encode()).digest(1024**2+7))
            os.chown(leaf,1000,1000);leaf.chmod(0o640);os.setxattr(leaf,'user.recycle',b'actual originals purge original');original=source_io.file_snapshot(leaf)
            c=json.loads(owned.common.command(['docker','inspect',f['identifier']]))[0];header=dict(containerId=f['identifier'],instanceId=str(c['State']['Pid'])+':'+c['State']['StartedAt'])
            reader=load('/usr/local/libexec/whaledeck-container-files.py','real_purge_reader')
            fixture=dict(revision=revision,containerId=f['identifier'],containerName=c['Name'].lstrip('/'),instanceId=header['instanceId'],header=header,containerPid=c['State']['Pid'],folder=str(f['folder']),evidence=str(e),original=original,sourceVersion=reader.version(header,'work/recycle-rpc.bin',leaf.stat()))
            fixture['originalFixtureFile']=source_io.file_snapshot(f['folder']/'original.bin');save('fixture-initial.json',fixture);save('prior-business.json',old_rows(f['identifier']))
            with temporary_user(True) as (username,password),temporary_user(True) as (recovery_username,recovery_password):
                with (e/'browser.stdout').open('x') as out,(e/'browser.stderr').open('x') as err:
                    process=subprocess.Popen(['node',str(repo/'deploy/validation/validate_container_recycle_archive_published_logout.mjs'),revision],stdin=subprocess.PIPE,stdout=out,stderr=err,text=True,env={**os.environ,'WHALEDECK_URL':'http://127.0.0.1:8080'})
                    source=cancel=ready=final=success=arc_cancel=arc_ready=arc_final=arc_success=ack=None
                    held=None;pool=ThreadPoolExecutor(max_workers=2);cut_future=exit_future=None;exited=False;diagnostics={}
                    try:
                        process.stdin.write(json.dumps({**fixture,'credentials':dict(owner=dict(username=username,password=password),recovery=dict(username=recovery_username,password=recovery_password))}));process.stdin.close();deadline=time.monotonic()+720
                        while process.poll() is None:
                            for phase in ('source','cancel','ready-to-purge','final-confirm','success','archive-cancel','archive-ready','archive-final-confirm','archive-inflight','decision','ack'):
                                marker=e/(phase+'-completed.json');accepted=e/(phase+'-accepted.json')
                                if not marker.exists() or accepted.exists():continue
                                signal=json.loads(marker.read_text());fixture['subject']=signal['subject'];value=signal['result']
                                if signal.get('recoverySubject'):
                                    assert signal['recoverySubject']!=fixture['subject'];fixture['recoverySubject']=signal['recoverySubject']
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
                                elif phase=='success':assert source and cancel and ready and final and success is None;success=prove_result(value,fixture,source,e,create=True);save('success-proof.json',success)
                                elif phase=='archive-cancel':assert success and arc_cancel is None;arc_cancel=prove_archive_result(value,fixture,source,success,e);save('archive-cancel-proof.json',arc_cancel)
                                elif phase=='archive-ready':
                                    assert success and arc_cancel and arc_ready is None;arc_ready=prove_archive_ready(value,fixture,source,success,e)
                                    preserved(before,baseline(revision),source['request']['operationId'],[uuid.UUID(execution(v['sql']['row']['id'])).hex for v in (cancel,success)],[uuid.UUID(archive_execution(v)).hex for v in (arc_cancel['sql']['row']['id'],value['id'])])
                                    typed_equal(json.loads((e/'prior-business.json').read_text()),old_rows(fixture['containerId']));save('archive-ready-proof.json',arc_ready)
                                    cut_future=pool.submit(observe_cut,value['id'],f['folder'],fixture['containerPid'],diagnostics)
                                elif phase=='archive-final-confirm':
                                    assert arc_ready and arc_final is None;arc_final=prove_archive_ready(value,fixture,source,success,e);typed_equal(arc_final,arc_ready)
                                    preserved(before,baseline(revision),source['request']['operationId'],[uuid.UUID(execution(v['sql']['row']['id'])).hex for v in (cancel,success)],[uuid.UUID(archive_execution(v)).hex for v in (arc_cancel['sql']['row']['id'],value['id'])])
                                    typed_equal(json.loads((e/'prior-business.json').read_text()),old_rows(fixture['containerId']));save('archive-final-proof.json',arc_final)
                                elif phase=='archive-inflight':
                                    assert arc_final and held and arc_success is None;arc_success=prove_unknown(value,fixture,source,success,held.record,e,create=True);save('archive-inflight-proof.json',arc_success)
                                elif phase=='decision':
                                    assert arc_success and ack is None;current=prove_unknown(value,fixture,source,success,held.record,e);typed_equal(current,arc_success);save('decision-proof.json',current)
                                else:
                                    assert arc_success and fixture['recoverySubject'];ack=prove_unknown(value,fixture,source,success,held.record,e,acknowledged=True);require_ack(arc_success,ack,fixture['recoverySubject']);save('ack-proof.json',ack)
                                save(phase+'-accepted.json',dict(id=value['id']))
                            if cut_future is not None and cut_future.done() and held is None and not (e/'cut-held.json').exists():
                                try:held=cut_future.result()
                                finally:save('cut-observation.json',diagnostics)
                                if held is None:
                                    row=archive_sql(arc_ready['sql']['row']['id'])['row'];save('cut-missed.json',dict(id=row['id'],noReplay=True));save('cut-held.json',dict(id=row['id'],executionJobId=row['execution_job_id'],missed=True));continue
                                current=archive_sql(arc_ready['sql']['row']['id']);q={k:held.record[k] for k in ('operationId','owner','plan')};r=current['row']
                                assert r['state']=='Applying' and r['publish_barrier'] and r['receipt_json'] is None and r['apply_started_at_utc'];reservation_proof(r,q)
                                assert held.record['state']=='Publishing' and held.proof['eightKernelFdProofs'] and held.proof['hddDataReread'];physical=archive_io.physical(f['folder'],q);typed_equal(physical,held.proof['physical'])
                                hdd=hdd_observation(source['request']['operationId']);retained_history(source['hdd'],hdd,purged=True)
                                save('cut-current.json',dict(physical=physical,hdd=hdd));save('cut-sql.json',current);save('cut-record.json',held.record);save('cut-proof.json',held.proof)
                                assert datetime.fromisoformat(r['lease_expires_at_utc'])>datetime.now(timezone.utc)+timedelta(seconds=45)
                                assert datetime.fromisoformat(r['execution_expires_at_utc'])>datetime.now(timezone.utc)+timedelta(minutes=10)
                                began=time.monotonic();began_ns=time.monotonic_ns();began_at=datetime.now(timezone.utc).isoformat();exit_future=pool.submit(observe_archive_logout_exit,held,began,began_ns,e,20)
                                save('cut-held.json',dict(id=r['id'],executionJobId=r['execution_job_id']))
                            if exit_future is not None and exit_future.done() and not exited:
                                observation=exit_future.result();observation.update(beganAtUtc=began_at,exitObservedAtUtc=datetime.now(timezone.utc).isoformat());action=json.loads((e/'logout-request-began.json').read_text());r=json.loads((e/'cut-sql.json').read_text())['row'];logout_exit_proof(observation,action,r);save('helper-exit.json',observation);exited=True
                                until=time.monotonic()+120
                                while True:
                                    settled=archive_sql(r['id'])['row']
                                    if settled['state']=='Unknown':break
                                    assert settled['state'] in ('Pending','Reserving','Applying') and time.monotonic()<until;time.sleep(.2)
                                settlement=dict(id=r['id'],executionJobId=r['execution_job_id'],state='Unknown',observedAtUtc=datetime.now(timezone.utc).isoformat());audit=result_audit(r);result_order(r,action,settlement,audit);save('unknown-result-audit.json',audit);save('settled.json',settlement)
                            assert time.monotonic()<deadline,'ARCHIVE_PUBLISHED_LOGOUT_DEADLINE';time.sleep(.02)
                        assert process.returncode==0 and held is not None and exited,'ARCHIVE_PUBLISHED_LOGOUT_BROWSER_FAILED'
                    except BaseException as primary:
                        import traceback
                        save('browser-primary-failure.json',dict(type=type(primary).__name__,frames=[dict(file=Path(t.filename).name,line=t.lineno,function=t.name) for t in traceback.extract_tb(primary.__traceback__)]))
                        raise
                    finally:
                        try:
                            settle_cut(pool,cut_future,held)
                        finally:
                            stop_browser(process)
                assert source and cancel and ready and final and success and arc_cancel and arc_ready and arc_final and arc_success and (e/'browser.stdout').stat().st_size<16384 and (e/'browser.stderr').stat().st_size<16384
                browser=json.loads((e/'browser.json').read_text());browser_proof(browser,fixture);assert ack is not None
                save('fixture.json',fixture);denied_archive(fixture,browser['archives'][1]);independent(repo,e,True)
            # Only the already-proved empty private directory of this fresh fixture.
            typed_equal(source_io.file_snapshot(f['folder']/'original.bin'),fixture['originalFixtureFile'])
            current=archive_io.physical(f['folder'],ack['request']);typed_equal(current,ack['physical'])
            save('owned-target-cleanup.json',cleanup_owned_target(f['folder'],ack['request'],ack['records']['root'],held.record,current,held.proof['parentVersionAfterPublication'],ack['sql']['row']))
            private=f['folder']/source_io.PRIVATE;i=private.lstat();assert not private.is_symlink() and [i.st_dev,i.st_ino]==success['request']['plan']['privateIdentity'] and not list(private.iterdir())
            private.rmdir();f['retain_unsettled']=False
        after_hdd=hdd_observation(source['request']['operationId']);retained_history(source['hdd'],after_hdd,purged=True);save('hdd-after-cleanup.json',after_hdd)
        after=baseline(revision);ops=[uuid.UUID(execution(v['id'])).hex for v in browser['completed']];preserved(before,after,source['request']['operationId'],ops,[uuid.UUID(archive_execution(v['id'])).hex for v in browser['archives']]);save('after.json',after)
        typed_equal(json.loads((e/'prior-business.json').read_text()),old_rows(fixture['containerId']));idle();assert not Path(fixture['folder']).exists()
        save('verified.json',dict(actualUi=True,actualIdentity=True,sourceOperations=1,purgeIntents=2,readyCanceled=1,completedOriginalsPurge=1,hddRereadAfterCleanup=True,ordinaryDenied=9,anonymousDenied=5,archiveReadyCanceled=1,archivePublishedUnknown=1,realUiLogout=True,explicitCancelAbsent=True,explicitDifferentAdminAcknowledgement=True,originalPidfdExit=True,archiveCopied=None,normalDualLogouts=2,fixtureRemoved=True,historiesRetained=True))
        independent(repo,e,False);print(json.dumps(dict(actualArchivePublishedLogout='passed',evidence=e.name)),flush=True)
    except BaseException as error:
        import traceback
        save('failure.json',dict(type=type(error).__name__,frames=[dict(file=Path(t.filename).name,line=t.lineno,function=t.name) for t in traceback.extract_tb(error.__traceback__)]));raise



if __name__=='__main__':
    try:assert len(sys.argv)==2;main(sys.argv[1])
    except BaseException as error:print(json.dumps(dict(actualArchivePublishedLogout='failed',type=type(error).__name__)),flush=True);raise SystemExit(1) from None
