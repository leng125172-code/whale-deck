#!/usr/bin/env python3
"""Real visible cancellation at a four-FD-bound Root Reserved pre-delete cut.

The still-retained owned source is returned only through dedicated product
restore, independently proved before exact fixture cleanup. HDD is retained.
"""
import hashlib,json,os,re,subprocess,sys,time,uuid,select,signal
from pathlib import Path
from concurrent.futures import ThreadPoolExecutor
from bound_container_recycle_purge_cut import freeze_before_delete
import validate_container_file_publishing as owned
import validate_container_recycle_browser as source_ui
import container_recycle_agent_io as source_io
import container_recycle_restore_agent_io as restore_io
import container_recycle_purge_agent_io as purge_io
from validate_container_file_staging import load
from validate_container_upload_bff import temporary_user
from validate_container_recycle_purge_agent import baseline,HOSTS
from validate_container_recycle_purge_browser import sql,idle,old_rows,hdd_observation,prove_ready,denied,stop_browser
from validate_container_recycle_restore_browser import prove_result as prove_restore
from recycle_purge_browser_proof import execution,managed_plan,job_summary,stored_object,retained_history
from recycle_restore_browser_proof import execution as restore_execution
from acl_source_apply_missing_proof import source_closure
from acl_history_browser_proof import typed_equal
from validate_metric_continuity import query
from recycle_purge_browser_proof import managed_receipt
from validate_container_recycle_purge_ready_logout import restored_hdd

SOURCES=('validate_container_recycle_purge_inflight_cancel.py','validate_container_recycle_purge_inflight_cancel.mjs','verify_container_recycle_purge_inflight_cancel.py','test_container_recycle_purge_inflight_cancel.py','bound_container_recycle_purge_cut.py','test_container_recycle_purge_cut_scope.py')


def terminal_proof(value,browser,fixture):
    r=value['row'];assert r['actor_subject']==fixture['subject']
    if browser is not None:assert r['id']==browser['final']['id'] and r['actor_subject']==browser['subject']
    assert (r['container_id'],r['instance_id'],r['relative_path'])==(fixture['containerId'],fixture['instanceId'],'work/recycle-rpc.bin')
    assert r['state']==value['executionJob']['state']=='Canceled' and r['result_code']=='CONTAINER_RECYCLE_PURGE_CANCELED'
    assert r['cancel_requested_at_utc'] and r['confirmed_at_utc'] and r['root_started_at_utc'] and r['reservation_json'] and r['apply_started_at_utc'] and r['completed_at_utc']
    assert r['execution_job_id']==execution(r['id']) and r['root_state']=='Aborted'
    assert r['purge_applied'] is r['source_fenced'] is r['owned_fence'] is False
    assert not r['publish_barrier'] and r['lease_token'] is r['lease_expires_at_utc'] is None and value['pending']==0
    assert value['accepted']==value['confirmed']==1 and value['canceled']==0 and value['planJob']['state']=='Succeeded'
    job_summary(stored_object(value['planJob']['result_json']),r,execution_stage=False)
    job_summary(stored_object(value['executionJob']['result_json']),r,execution_stage=True)
    if browser is not None:assert browser['actualUi'] and browser['registrations']==browser['confirmations']==browser['cancellations']==1 and browser['normalDualLogouts']==2 and browser['reloginOriginalReferencesDenied']==4
    return dict(originalExecutionCanceled=True,rootAborted=True,noDeletedOutcome=True,durableApplyMarkerIsNotDeletion=True)


def prove_cancel(dto,fixture,source,e,create=False):
    value=sql(dto['id']);r=value['row'];terminal_proof(value,None,fixture);assert r['id']==dto['id']
    count=query("SELECT to_json(count(*)) FROM audit_events WHERE job_id=:'job'::uuid AND action='containers.files.recycle-purge.cancel-requested'",job=r['execution_job_id']);assert count==1
    p=managed_plan(stored_object(r['plan_json']));typed_equal(p['command']['source'],source['request']);assert p['command']['historyDigest']==source['records']['history']['historyDigest']
    q=dict(operationId=uuid.UUID(r['execution_job_id']).hex,owner=r['owner_hash'],plan=p);receipt=managed_receipt(dto['receipt'],q)
    assert receipt['state']=='Aborted' and receipt['purgeApplied'] is False
    records=purge_io.read_operation(q,receipt,source['records']['root'],source['records']['history'],e/'purge-originals',create=create)
    assert records['marker']['state']=='Released' and records['marker']['operationId']==q['operationId']
    physical=purge_io.physical(Path(fixture['folder']),q);purge_io.physical_proof(physical,fixture['original'],q,records,receipt)
    preserved_source=unchanged_source(fixture,source,e)
    return dict(sql=value,request=q,receipt=receipt,records=records,physical=physical,source=preserved_source,cancelAuditCount=count)


def unchanged_source(fixture,source,e):
    typed_equal(source_ui.sql(source['sql']['row']['id']),source['sql'])
    physical=source_io.physical(Path(fixture['folder']),source['request']['operationId']);typed_equal(physical,source['physical'])
    typed_equal(source_io.read_operation(source['request'],source['receipt'],e/'source-originals'),source['records'])
    hdd=hdd_observation(source['request']['operationId']);retained_history(source['hdd'],hdd)
    return dict(physical=physical,hdd=hdd)


def preserved(before,after,source_op,restore_op,purge_op):
    from validate_container_recycle_purge_ready_logout import preserved as source_restore_preserved
    a={k:v for k,v in after.items() if k!='purgeRoot'};b={k:v for k,v in before.items() if k!='purgeRoot'}
    marker=source_op+'.purge.json';assert marker not in b['recycleRoot'] and marker in a['recycleRoot']
    a['recycleRoot']={k:v for k,v in a['recycleRoot'].items() if k!=marker}
    source_restore_preserved(b,a,source_op,restore_op)
    old=before['purgeRoot'];new=after['purgeRoot'];assert all(new.get(k)==v for k,v in old.items());assert set(new)-set(old)<={purge_op+'.json',purge_op+'.lock'}


def settle_held(held):
    """An exception must not resume a confirmed destructive helper into deletion."""
    if held is None or held.descriptor is None:return
    if not select.select([held.descriptor],[],[],0)[0]:
        try:signal.pidfd_send_signal(held.descriptor,signal.SIGKILL)
        except ProcessLookupError:pass
        assert select.select([held.descriptor],[],[],5)[0],'OWNED_HELD_PURGE_DID_NOT_EXIT'
    held.release()


def independent(repo,e,live):
    with (e/('independent-live.log' if live else 'independent-final.log')).open('x') as out:
        subprocess.run([sys.executable,str(repo/'deploy/validation/verify_container_recycle_purge_inflight_cancel.py'),e.name]+(['--live'] if live else []),check=True,stdout=out,stderr=subprocess.STDOUT,timeout=90)


def main(revision):
    assert os.geteuid()==0 and re.fullmatch('[a-f0-9]{40}',revision);os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0'
    repo=Path(__file__).resolve().parents[2];git=lambda *a:subprocess.check_output(['git','-c','safe.directory='+str(repo),'-C',str(repo),*a])
    assert not git('status','--porcelain').strip();sha=git('rev-parse','HEAD').decode().strip();closure=source_closure(repo,sha,seeds=SOURCES)
    for n,h in closure.items():assert hashlib.sha256((repo/'deploy/validation'/n).read_bytes()).hexdigest()==h
    for n in HOSTS:assert (Path('/usr/local/libexec')/n).read_bytes()==git('show',revision+':deploy/host/'+n)
    idle();before=baseline(revision);e=repo/'deploy/artifacts'/('container-recycle-purge-inflight-cancel-'+uuid.uuid4().hex);e.mkdir(mode=0o700)
    def save(n,v):
        with (e/n).open('x') as out:json.dump(v,out)
    frozen=e/'source';frozen.mkdir(mode=0o700)
    for n,h in closure.items():
        raw=git('show',sha+':deploy/validation/'+n);assert hashlib.sha256(raw).hexdigest()==h;(frozen/n).write_bytes(raw)
    save('before.json',before);save('source.json',dict(revision=revision,validationRevision=sha,validationSourceSha256=closure));print(json.dumps(dict(actualPurgeInflightCancel='started',evidence=e.name)),flush=True)
    try:
        with owned.fixture() as f:
            f['retain_unsettled']=True;leaf=f['folder']/'recycle-rpc.bin';leaf.write_bytes(hashlib.shake_256(('purge-inflight-cancel:'+f['identifier']).encode()).digest(1024**2+7));leaf.chmod(0o640);os.chown(leaf,1000,1000);os.setxattr(leaf,'user.recycle',b'purge cancellation original');original=source_io.file_snapshot(leaf)
            c=json.loads(owned.common.command(['docker','inspect',f['identifier']]))[0];header=dict(containerId=f['identifier'],instanceId=str(c['State']['Pid'])+':'+c['State']['StartedAt']);reader=load('/usr/local/libexec/whaledeck-container-files.py','purge_ready_reader')
            fixture=dict(revision=revision,containerId=f['identifier'],containerName=c['Name'].lstrip('/'),instanceId=header['instanceId'],header=header,containerPid=c['State']['Pid'],folder=str(f['folder']),evidence=str(e),original=original,sourceVersion=reader.version(header,'work/recycle-rpc.bin',leaf.stat()))
            save('fixture-initial.json',fixture);save('prior-business.json',old_rows(f['identifier']))
            with temporary_user(True) as (username,password):
                with (e/'browser.stdout').open('x') as out,(e/'browser.stderr').open('x') as err:
                    process=subprocess.Popen(['node',str(repo/'deploy/validation/validate_container_recycle_purge_inflight_cancel.mjs'),revision],stdin=subprocess.PIPE,stdout=out,stderr=err,text=True,env={**os.environ,'WHALEDECK_URL':'http://127.0.0.1:8080','VALIDATION_USERNAME':username,'VALIDATION_PASSWORD':password})
                    source=armed=canceled=restored=None;held=None;pool=ThreadPoolExecutor(max_workers=1);exit_future=None;exited=False;missed=False
                    try:
                        process.stdin.write(json.dumps(fixture));process.stdin.close();deadline=time.monotonic()+620
                        while process.poll() is None:
                            for phase in ('source','armed','canceled','restore-ready','restored'):
                                marker=e/(phase+'-completed.json');accepted=e/(phase+'-accepted.json')
                                if not marker.exists() or accepted.exists():continue
                                signal=json.loads(marker.read_text());fixture['subject']=signal['subject'];value=signal['result']
                                if phase=='source':
                                    assert source is None;source=source_ui.verify_case(value,fixture,e,create=True);(e/'originals').rename(e/'source-originals');source['hdd']=hdd_observation(source['request']['operationId']);retained_history(source['hdd'],source['hdd']);typed_equal(source['hdd']['history'],source['records']['history']);typed_equal(source['hdd']['data'],source['records']['data']);save('source-proof.json',source)
                                elif phase=='armed':
                                    assert source and armed is None;armed=prove_ready(value,fixture,source,e);save('armed-proof.json',armed)
                                elif phase=='canceled':
                                    assert held and source and canceled is None;canceled=prove_cancel(value,fixture,source,e,True);save('canceled-proof.json',canceled)
                                elif phase=='restore-ready':
                                    assert canceled and restored is None;typed_equal(sql(canceled['sql']['row']['id']),canceled['sql']);unchanged_source(fixture,source,e)
                                    from validate_container_recycle_restore_browser import sql as restore_sql
                                    row=restore_sql(value['id'])['row'];assert row['state']=='Ready' and row['source_intent_id']==source['sql']['row']['id'] and row['version']==value['version'] and row['execution_job_id'] is row['root_started_at_utc'] is None;save('restore-ready-proof.json',dict(sql=restore_sql(value['id'])))
                                else:
                                    assert canceled and restored is None;restored=prove_restore(value,fixture,source,e,create=True);save('restored-proof.json',restored)
                                save(phase+'-accepted.json',dict(id=value['id']))
                            if armed and held is None and not missed:
                                row=sql(armed['sql']['row']['id'])['row']
                                if row['execution_job_id']:
                                    diagnostics={}
                                    try:held=freeze_before_delete(row,f['folder'],fixture['containerPid'],diagnostics)
                                    finally:save('cut-observation.json',diagnostics)
                                    if held is None:
                                        missed=True;save('cut-missed.json',dict(id=row['id'],noReplay=True));save('cut-held.json',dict(id=row['id'],executionJobId=row['execution_job_id'],missed=True));continue
                                    current=sql(row['id']);actual=current['row'];assert actual['state']=='Applying' and actual['root_started_at_utc'] and actual['reservation_json'] and actual['apply_started_at_utc'] and actual['publish_barrier'] and actual['receipt_json'] is None
                                    preservation=unchanged_source(fixture,source,e);save('cut-source-preservation.json',preservation)
                                    from bound_container_recycle_purge_cut import private_record,SOURCE_STATE
                                    save('cut-source-root.json',private_record(SOURCE_STATE/(source['request']['operationId']+'.json')));save('cut-source-marker.json',private_record(SOURCE_STATE/(source['request']['operationId']+'.purge.json')))
                                    save('cut-sql.json',current);save('cut-record.json',held.record);save('cut-proof.json',held.proof)
                                    began=time.monotonic();began_ns=time.monotonic_ns();exit_future=pool.submit(held.wait_exit_seconds,began,20);save('cut-held.json',dict(id=row['id'],executionJobId=row['execution_job_id']))
                            if exit_future is not None and exit_future.done() and not exited:
                                elapsed=exit_future.result();assert elapsed is not None and (e/'cancel-request-began.json').exists(),'ORIGINAL_PURGE_HELPER_EXIT_NOT_OBSERVED'
                                request=json.loads((e/'cancel-request-began.json').read_text());action_ns=int(request['monotonicNs']);exit_ns=int((began+elapsed)*1e9)
                                assert began_ns<=action_ns<=exit_ns,'PURGE_EXIT_PRECEDED_UI_CANCEL_ACTION'
                                save('helper-exit.json',dict(elapsedSeconds=elapsed,observedWithOriginalPidfd=True,measurementBeginsBeforeAllowingUiAction=True,beganMonotonicNs=began_ns,actionMonotonicNs=action_ns,exitObservedMonotonicNs=exit_ns));exited=True
                            assert time.monotonic()<deadline,'PURGE_INFLIGHT_CANCEL_DEADLINE';time.sleep(.1)
                        assert process.returncode==0 and held is not None and exited,'PURGE_INFLIGHT_CANCEL_BROWSER_FAILED'
                    finally:
                        try:settle_held(held)
                        finally:stop_browser(process);pool.shutdown(wait=True,cancel_futures=True)
                assert source and armed and canceled and restored and exited and (e/'browser.stderr').read_bytes()==b''
                browser=json.loads((e/'browser.json').read_text());proof=terminal_proof(sql(browser['final']['id']),browser,fixture);save('terminal-proof.json',proof);save('fixture.json',fixture)
                denied(fixture,browser['final']);independent(repo,e,True)
            # Only the independently verified original inode returned by product restore.
            typed_equal(source_io.file_snapshot(leaf),restored['physical']['source']);leaf.unlink()
            private=f['folder']/source_io.PRIVATE;info=private.lstat();assert not private.is_symlink() and [info.st_dev,info.st_ino]==restored['records']['sourceRoot']['privateIdentity'] and not list(private.iterdir());private.rmdir();f['retain_unsettled']=False
        hdd=hdd_observation(source['request']['operationId']);restored_hdd(source['hdd'],hdd);save('hdd-after-cleanup.json',hdd)
        assert not Path(fixture['folder']).exists() and subprocess.run(['docker','inspect',fixture['containerId']],capture_output=True).returncode!=0
        after=baseline(revision);restore_op=uuid.UUID(restore_execution(browser['restored']['id'])).hex;preserved(before,after,source['request']['operationId'],restore_op,canceled['request']['operationId']);typed_equal(json.loads((e/'prior-business.json').read_text()),old_rows(fixture['containerId']));idle();save('after.json',after)
        save('verified.json',dict(actualUi=True,actualIdentity=True,originalPidfdExit=True,predeleteCutRootReserved=True,rootAborted=True,purgeApplied=False,visibleCancellation=True,dedicatedRestoreForQaCleanup=True,hddRereadAfterCleanup=True,fixtureRemoved=True,ordinaryDenied=9,anonymousDenied=5))
        independent(repo,e,False);print(json.dumps(dict(actualPurgeInflightCancel='passed',evidence=e.name)),flush=True)
    except BaseException as error:
        import traceback
        save('failure.json',dict(type=type(error).__name__,frames=[dict(file=Path(t.filename).name,line=t.lineno,function=t.name) for t in traceback.extract_tb(error.__traceback__)]));raise


if __name__=='__main__':
    try:assert len(sys.argv)==2;main(sys.argv[1])
    except BaseException as error:print(json.dumps(dict(actualPurgeInflightCancel='failed',type=type(error).__name__)),flush=True);raise SystemExit(1) from None
