#!/usr/bin/env python3
"""Real Ready logout + natural expiry; no purge confirmation/cancel or clock edits.

The still-retained owned source is returned only through dedicated product
restore, independently proved before exact fixture cleanup. HDD is retained.
"""
import hashlib,json,os,re,subprocess,sys,time,uuid
from pathlib import Path
from datetime import datetime
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

SOURCES=('validate_container_recycle_purge_ready_logout.py','validate_container_recycle_purge_ready_logout.mjs','verify_container_recycle_purge_ready_logout.py','test_container_recycle_purge_ready_logout.py')


def terminal_proof(value,browser,fixture):
    r=value['row'];original=browser['original']
    assert r['id']==original['id']==browser['final']['id'] and r['actor_subject']==browser['subject']
    assert (r['container_id'],r['instance_id'],r['relative_path'])==(fixture['containerId'],fixture['instanceId'],'work/recycle-rpc.bin')
    assert r['state']=='Expired' and r['result_code']=='CONTAINER_RECYCLE_PURGE_PLAN_EXPIRED' and r['completed_at_utc']
    for k in ('cancel_requested_at_utc','confirmed_at_utc','execution_job_id','execution_hash','root_started_at_utc','reservation_json','reservation_hash','apply_started_at_utc','receipt_json','receipt_hash','root_state','purge_applied','source_fenced','owned_fence','lease_token','lease_expires_at_utc'):assert r[k] is None,k
    assert not r['publish_barrier'] and value['executionJob'] is None and value['pending']==0
    assert value['accepted']==1 and value['confirmed']==0 and value['canceled']==0 and value['planJob']['state']=='Succeeded'
    job_summary(stored_object(value['planJob']['result_json']),r,execution_stage=False)
    assert datetime.fromisoformat(r['ready_expires_at_utc'])==datetime.fromisoformat(original['readyExpiresAtUtc'])
    late=(datetime.fromisoformat(r['completed_at_utc'])-datetime.fromisoformat(r['ready_expires_at_utc'])).total_seconds();assert 0<=late<=30
    assert browser['actualUi'] and browser['clockChanged'] is False and browser['registrations']==1 and browser['confirmations']==browser['cancellations']==0
    assert browser['anonymous']==[401]*4 and browser['oldConfirmStatus']==401 and browser['newLoginReads']==[404]*2 and browser['newConfirmStatus']==404
    assert browser['elapsedMs']>=200000 and browser['normalDualLogouts']==2
    return dict(sqlExpiryLatenessSeconds=late,planningJobHistoricalReady=True,purgeExecutionAbsent=True,cancellationAbsent=True)


def unchanged_source(fixture,source,e):
    typed_equal(source_ui.sql(source['sql']['row']['id']),source['sql'])
    physical=source_io.physical(Path(fixture['folder']),source['request']['operationId']);typed_equal(physical,source['physical'])
    typed_equal(source_io.read_operation(source['request'],source['receipt'],e/'source-originals'),source['records'])
    hdd=hdd_observation(source['request']['operationId']);retained_history(source['hdd'],hdd)
    return dict(physical=physical,hdd=hdd)


def restored_hdd(before,after):
    assert before['history']['captureState']=='Trashed' and after['history']['captureState']=='Restored'
    typed_equal(after,{**before,'history':{**before['history'],'captureState':'Restored'}})


def preserved(before,after,source_op,restore_op):
    for op in (source_op,restore_op):assert re.fullmatch('[a-f0-9]{32}',op) and int(op,16)>0
    assert source_op!=restore_op
    for k in set(before)-{'recycleRoot','recycleHdd','recycleRestoreRoot'}:typed_equal(before[k],after[k])
    additions={'recycleRoot':{source_op+s for s in ('.json','.lock')},'recycleHdd':{source_op+s for s in ('.json','.data')},'recycleRestoreRoot':{restore_op+s for s in ('.json','.lock')}|{'claims/'+source_op+'.json'}}
    for k,allowed in additions.items():
        assert all(after[k].get(n)==v for n,v in before[k].items());assert set(after[k])-set(before[k])<=allowed


def independent(repo,e,live):
    with (e/('independent-live.log' if live else 'independent-final.log')).open('x') as out:
        subprocess.run([sys.executable,str(repo/'deploy/validation/verify_container_recycle_purge_ready_logout.py'),e.name]+(['--live'] if live else []),check=True,stdout=out,stderr=subprocess.STDOUT,timeout=90)


def main(revision):
    assert os.geteuid()==0 and re.fullmatch('[a-f0-9]{40}',revision);os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0'
    repo=Path(__file__).resolve().parents[2];git=lambda *a:subprocess.check_output(['git','-c','safe.directory='+str(repo),'-C',str(repo),*a])
    assert not git('status','--porcelain').strip();sha=git('rev-parse','HEAD').decode().strip();closure=source_closure(repo,sha,seeds=SOURCES)
    for n,h in closure.items():assert hashlib.sha256((repo/'deploy/validation'/n).read_bytes()).hexdigest()==h
    for n in HOSTS:assert (Path('/usr/local/libexec')/n).read_bytes()==git('show',revision+':deploy/host/'+n)
    idle();before=baseline(revision);e=repo/'deploy/artifacts'/('container-recycle-purge-ready-logout-'+uuid.uuid4().hex);e.mkdir(mode=0o700)
    def save(n,v):
        with (e/n).open('x') as out:json.dump(v,out)
    frozen=e/'source';frozen.mkdir(mode=0o700)
    for n,h in closure.items():
        raw=git('show',sha+':deploy/validation/'+n);assert hashlib.sha256(raw).hexdigest()==h;(frozen/n).write_bytes(raw)
    save('before.json',before);save('source.json',dict(revision=revision,validationRevision=sha,validationSourceSha256=closure));print(json.dumps(dict(actualPurgeReadyLogout='started',evidence=e.name)),flush=True)
    try:
        with owned.fixture() as f:
            f['retain_unsettled']=True;leaf=f['folder']/'recycle-rpc.bin';leaf.write_bytes(hashlib.shake_256(('purge-ready-logout:'+f['identifier']).encode()).digest(1024**2+7));leaf.chmod(0o640);os.chown(leaf,1000,1000);os.setxattr(leaf,'user.recycle',b'purge ready logout original');original=source_io.file_snapshot(leaf)
            c=json.loads(owned.common.command(['docker','inspect',f['identifier']]))[0];header=dict(containerId=f['identifier'],instanceId=str(c['State']['Pid'])+':'+c['State']['StartedAt']);reader=load('/usr/local/libexec/whaledeck-container-files.py','purge_ready_reader')
            fixture=dict(revision=revision,containerId=f['identifier'],containerName=c['Name'].lstrip('/'),instanceId=header['instanceId'],header=header,folder=str(f['folder']),evidence=str(e),original=original,sourceVersion=reader.version(header,'work/recycle-rpc.bin',leaf.stat()))
            save('fixture-initial.json',fixture);save('prior-business.json',old_rows(f['identifier']))
            with temporary_user(True) as (username,password):
                with (e/'browser.stdout').open('x') as out,(e/'browser.stderr').open('x') as err:
                    process=subprocess.Popen(['node',str(repo/'deploy/validation/validate_container_recycle_purge_ready_logout.mjs'),revision],stdin=subprocess.PIPE,stdout=out,stderr=err,text=True,env={**os.environ,'WHALEDECK_URL':'http://127.0.0.1:8080','VALIDATION_USERNAME':username,'VALIDATION_PASSWORD':password})
                    source=ready=expired=restored=None
                    try:
                        process.stdin.write(json.dumps(fixture));process.stdin.close();deadline=time.monotonic()+620
                        while process.poll() is None:
                            for phase in ('source','ready','expiry','restore-ready','restored'):
                                marker=e/(phase+'-completed.json');accepted=e/(phase+'-accepted.json')
                                if not marker.exists() or accepted.exists():continue
                                signal=json.loads(marker.read_text());fixture['subject']=signal['subject'];value=signal['result']
                                if phase=='source':
                                    assert source is None;source=source_ui.verify_case(value,fixture,e,create=True);(e/'originals').rename(e/'source-originals');source['hdd']=hdd_observation(source['request']['operationId']);retained_history(source['hdd'],source['hdd']);typed_equal(source['hdd']['history'],source['records']['history']);typed_equal(source['hdd']['data'],source['records']['data']);save('source-proof.json',source)
                                elif phase=='ready':
                                    assert source and ready is None;ready=prove_ready(value,fixture,source,e);save('ready-proof.json',ready)
                                elif phase=='expiry':
                                    assert ready and expired is None;until=time.monotonic()+25
                                    while True:
                                        data=sql(value['id'])
                                        if data['row']['state']=='Expired':break
                                        assert data['row']['state']=='Ready' and time.monotonic()<until;time.sleep(.5)
                                    typed_equal(data['planJob'],ready['sql']['planJob'])
                                    for key in ('source_intent_id','source_proof_hash','command_json','request_hash','plan_json','plan_hash','ready_expires_at_utc','actor_subject','login_binding'):
                                        typed_equal(data['row'][key],ready['sql']['row'][key])
                                    expired=dict(sql=data,source=unchanged_source(fixture,source,e));save('expired-proof.json',expired)
                                elif phase=='restore-ready':
                                    assert expired and restored is None
                                    data=sql(ready['sql']['row']['id']);typed_equal(data,expired['sql']);unchanged_source(fixture,source,e)
                                    from validate_container_recycle_restore_browser import sql as restore_sql
                                    row=restore_sql(value['id'])['row'];assert row['state']=='Ready' and row['source_intent_id']==source['sql']['row']['id'] and row['version']==value['version'] and row['execution_job_id'] is row['root_started_at_utc'] is None;save('restore-ready-proof.json',dict(sql=restore_sql(value['id'])))
                                else:
                                    assert expired and restored is None;restored=prove_restore(value,fixture,source,e,create=True);save('restored-proof.json',restored)
                                save(phase+'-accepted.json',dict(id=value['id']))
                            assert time.monotonic()<deadline,'PURGE_READY_LOGOUT_DEADLINE';time.sleep(.1)
                        assert process.returncode==0,'PURGE_READY_LOGOUT_BROWSER_FAILED'
                    finally:stop_browser(process)
                assert source and ready and expired and restored and (e/'browser.stderr').read_bytes()==b''
                browser=json.loads((e/'browser.json').read_text());proof=terminal_proof(sql(browser['original']['id']),browser,fixture);save('terminal-proof.json',proof);save('fixture.json',fixture)
                denied(fixture,browser['final']);independent(repo,e,True)
            # Only the independently verified original inode returned by product restore.
            typed_equal(source_io.file_snapshot(leaf),restored['physical']['source']);leaf.unlink()
            private=f['folder']/source_io.PRIVATE;info=private.lstat();assert not private.is_symlink() and [info.st_dev,info.st_ino]==restored['records']['sourceRoot']['privateIdentity'] and not list(private.iterdir());private.rmdir();f['retain_unsettled']=False
        hdd=hdd_observation(source['request']['operationId']);restored_hdd(source['hdd'],hdd);save('hdd-after-cleanup.json',hdd)
        assert not Path(fixture['folder']).exists() and subprocess.run(['docker','inspect',fixture['containerId']],capture_output=True).returncode!=0
        after=baseline(revision);restore_op=uuid.UUID(restore_execution(browser['restored']['id'])).hex;preserved(before,after,source['request']['operationId'],restore_op);typed_equal(json.loads((e/'prior-business.json').read_text()),old_rows(fixture['containerId']));idle();save('after.json',after)
        save('verified.json',dict(actualUi=True,actualIdentity=True,normalReadyLogout=True,sameSubjectNewLoginSeparated=True,naturalExpiry=True,purgeConfirmations=0,purgeCancellations=0,purgeExecutionAbsent=True,dedicatedRestoreForQaCleanup=True,hddRereadAfterCleanup=True,fixtureRemoved=True,ordinaryDenied=9,anonymousDenied=5))
        independent(repo,e,False);print(json.dumps(dict(actualPurgeReadyLogout='passed',evidence=e.name)),flush=True)
    except BaseException as error:
        import traceback
        save('failure.json',dict(type=type(error).__name__,frames=[dict(file=Path(t.filename).name,line=t.lineno,function=t.name) for t in traceback.extract_tb(error.__traceback__)]));raise


if __name__=='__main__':
    try:assert len(sys.argv)==2;main(sys.argv[1])
    except BaseException as error:print(json.dumps(dict(actualPurgeReadyLogout='failed',type=type(error).__name__)),flush=True);raise SystemExit(1) from None
