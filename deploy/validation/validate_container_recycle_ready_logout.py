#!/usr/bin/env python3
"""Fresh real Ready logout, same-subject login separation and natural expiry.

Never confirms/cancels an intent, changes clocks, edits SQL, or writes an old
Root record. Temporary source is kept unless proven unchanged and terminal.
"""
import hashlib,json,os,re,subprocess,sys,time,uuid
from pathlib import Path
from datetime import datetime,timezone
import validate_container_file_publishing as owned
from validate_container_file_staging import load
from validate_container_directory_restart import preflight
from validate_container_upload_bff import temporary_user
from validate_container_recycle_browser import sql,denied
from validate_container_recycle_agent import baseline,preserved
from container_recycle_agent_io import file_snapshot,STATE
from acl_source_apply_missing_proof import source_closure
from acl_history_browser_proof import typed_equal

SOURCES=('validate_container_recycle_ready_logout.py','validate_container_recycle_ready_logout.mjs','verify_container_recycle_ready_logout.py','test_container_recycle_ready_logout.py')

def terminal_proof(value,browser,fixture):
    row=value['row'];original=browser['original'];assert row['id']==original['id']==browser['final']['id'] and row['actor_subject']==browser['subject'] and row['container_id']==fixture['containerId'] and row['instance_id']==fixture['instanceId'] and row['relative_path']=='work/recycle-rpc.bin'
    assert row['state']=='Expired' and row['result_code']=='CONTAINER_RECYCLE_PLAN_EXPIRED' and row['completed_at_utc'] and row['cancel_requested_at_utc'] is None and row['confirmed_at_utc'] is None and row['execution_job_id'] is None and row['execution_hash'] is None and row['root_started_at_utc'] is None and row['receipt_json'] is None and row['receipt_hash'] is None and not row['publish_barrier'] and row['lease_token'] is None and row['lease_expires_at_utc'] is None
    assert not row['history_may_exist'] and not row['history_verified'] and row['history_digest'] is None and row['source_retained'] is row['source_restored'] is row['binding_current'] is row['history_available'] is row['root_state'] is None
    assert datetime.fromisoformat(row['ready_expires_at_utc'])==datetime.fromisoformat(original['readyExpiresAtUtc']) and value['executionJob'] is None and value['pending']==0 and value['accepted']==1 and value['confirmed']==0 and value['planJob']['state']=='Succeeded'
    facts=value['planJob']['result_json'];facts=json.loads(facts) if isinstance(facts,str) else facts;assert len(facts)==14 and facts['stage']=='planning' and facts['state']=='Ready' and facts['receiptState'] is None and not facts['historyMayExist'] and not facts['historyVerified']
    expiry=datetime.fromisoformat(row['ready_expires_at_utc']);completed=datetime.fromisoformat(row['completed_at_utc']);late=(completed-expiry).total_seconds();assert 0<=late<=30
    assert browser['actualUi'] and browser['normalDualLogouts']==2 and browser['registrations']==1 and browser['confirmations']==browser['cancellations']==0 and browser['clockChanged'] is False and browser['anonymous']==[401]*4 and browser['oldConfirmStatus']==401 and browser['newLoginReads']==[404]*2 and browser['newConfirmStatus']==404 and browser['elapsedMs']>=200000
    return dict(sqlExpiryLatenessSeconds=late,planningJobHistoricalReady=True,executionAbsent=True,sourceFactsUnproved=True)


def main(revision):
    assert os.geteuid()==0 and re.fullmatch('[a-f0-9]{40}',revision);os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0';repo=Path(__file__).resolve().parents[2];assert not subprocess.check_output(['git','-C',str(repo),'status','--porcelain']).strip();sha=subprocess.check_output(['git','-C',str(repo),'rev-parse','HEAD'],text=True).strip();closure=source_closure(repo,sha,seeds=SOURCES)
    for n,h in closure.items():assert hashlib.sha256((repo/'deploy/validation'/n).read_bytes()).hexdigest()==h
    preflight();before=baseline(revision);e=repo/'deploy/artifacts'/('container-recycle-ready-logout-'+uuid.uuid4().hex);e.mkdir(mode=0o700);save=lambda n,v:(e/n).write_text(json.dumps(v));save('before.json',before);save('source.json',dict(revision=revision,validationRevision=sha,validationSourceSha256=closure));print(json.dumps(dict(actualRecycleReadyLogout='started',evidence=e.name)),flush=True)
    try:
        with owned.fixture() as f:
            f['retain_unsettled']=True;leaf=f['folder']/'recycle-rpc.bin';leaf.write_bytes(hashlib.shake_256(('ready-logout:'+f['identifier']).encode()).digest(1024**2+7));leaf.chmod(0o640);os.chown(leaf,1000,1000);os.setxattr(leaf,'user.recycle',b'ready logout original');original=file_snapshot(leaf);c=json.loads(owned.common.command(['docker','inspect',f['identifier']]))[0];header=dict(containerId=f['identifier'],instanceId=str(c['State']['Pid'])+':'+c['State']['StartedAt']);reader=load('/usr/local/libexec/whaledeck-container-files.py','ready_logout_reader');fixture=dict(revision=revision,containerId=f['identifier'],containerName=c['Name'].lstrip('/'),instanceId=header['instanceId'],header=header,folder=str(f['folder']),evidence=str(e),original=original,sourceVersion=reader.version(header,'work/recycle-rpc.bin',leaf.stat()));save('fixture.json',fixture)
            with temporary_user(True) as (username,password):
                result=subprocess.run(['node',str(repo/'deploy/validation/validate_container_recycle_ready_logout.mjs'),revision],input=json.dumps(fixture),env={**os.environ,'WHALEDECK_URL':'http://127.0.0.1:8080','VALIDATION_USERNAME':username,'VALIDATION_PASSWORD':password},text=True,capture_output=True,timeout=470);(e/'browser.stdout').write_text(result.stdout);(e/'browser.stderr').write_text(result.stderr);assert result.returncode==0,'ACTUAL_READY_LOGOUT_FAILED';browser=json.loads((e/'browser.json').read_text());deadline=time.monotonic()+20
                while True:
                    value=sql(browser['original']['id'])
                    if value['row']['state']=='Expired':break
                    assert value['row']['state']=='Ready' and time.monotonic()<deadline;time.sleep(.5)
                proof=terminal_proof(value,browser,fixture);save('sql.json',value);save('terminal-proof.json',proof);typed_equal(file_snapshot(leaf),original);denied('http://127.0.0.1:8080',fixture,browser['final'])
                with (e/'independent-live.log').open('x') as out:subprocess.run([sys.executable,str(repo/'deploy/validation/verify_container_recycle_ready_logout.py'),e.name,'--live'],stdout=out,stderr=subprocess.STDOUT,check=True,timeout=90)
            typed_equal(file_snapshot(leaf),original);leaf.unlink();f['retain_unsettled']=False
        after=baseline(revision);preserved(before,after,());save('after.json',after);preflight();assert not Path(fixture['folder']).exists();save('verified.json',dict(actualReadyLogout=True,newLoginSeparated=True,naturalExpiry=True,clockChanged=False,rootAndHddUnchanged=True,fixtureAndUsersRemoved=True,normalDualLogouts=2,ordinaryDenied=8,anonymousDenied=4,executionAndCancelAbsent=True))
        with (e/'independent-final.log').open('x') as out:subprocess.run([sys.executable,str(repo/'deploy/validation/verify_container_recycle_ready_logout.py'),e.name],stdout=out,stderr=subprocess.STDOUT,check=True,timeout=90)
        print(json.dumps(dict(actualRecycleReadyLogout='passed',evidence=e.name)),flush=True)
    except BaseException as error:
        import traceback
        t=traceback.extract_tb(error.__traceback__)[-1];save('failure.json',dict(type=type(error).__name__,file=Path(t.filename).name,line=t.lineno));raise

if __name__=='__main__':
    try:assert len(sys.argv)==2;main(sys.argv[1])
    except BaseException as error:print(json.dumps(dict(actualRecycleReadyLogout='failed',type=type(error).__name__)),flush=True);raise SystemExit(1) from None
