#!/usr/bin/env python3
"""Fresh fixed Worker/Agent faults at the verified pre-removal boundary. No replay."""
import hashlib,json,os,re,subprocess,sys,time,uuid
from pathlib import Path
import validate_container_file_publishing as owned
from validate_container_file_staging import load
from validate_container_directory_restart import preflight,Worker,command
from validate_container_upload_bff import temporary_user
from validate_container_recycle_browser import sql,native_plan,denied
from validate_container_recycle_agent import baseline
from container_recycle_restart_proof import KINDS,restart,natural_lease,preserved_restart
from validate_container_acl_faults import host_snapshot
from container_recycle_agent_io import file_snapshot,physical,PRIVATE,read_operation
from container_recycle_rpc_proof import proto_receipt
from bound_container_recycle_cut import freeze_before_removal
from acl_source_apply_missing_proof import source_closure
from acl_history_browser_proof import typed_equal

SOURCES=('validate_container_recycle_restart.py','validate_container_recycle_restart.mjs','verify_container_recycle_restart.py','test_container_recycle_restart.py')

def browser_proof(b,fixture):
    assert b['kind']==fixture['kind'] and b['subject']==fixture['subject'] and b['actualUi'] and b['registrations']==b['confirmations']==1 and b['cancellations']==0 and b['originalOwnerStillAdministrator']
    r=b['logout'];assert r['normalUiLogout'] and r['postStatus'] in (302,303) and r['providerNavigationObserved'] and r['bffStatus']==401 and r['identityStatus'] in (401,403) and r['finalLoginPage']


def prove(value,fixture,e,create=False):
    data=sql(value['id']);r=data['row'];assert r['actor_subject']==fixture['subject'] and r['container_id']==fixture['containerId'] and r['instance_id']==fixture['instanceId'] and r['relative_path']=='work/recycle-rpc.bin'
    assert r['state']==data['executionJob']['state']==value['state']=='Failed' and r['cancel_requested_at_utc'] is None and r['root_started_at_utc'] and r['completed_at_utc'] and not r['publish_barrier'] and r['lease_token'] is None and r['lease_expires_at_utc'] is None and data['pending']==0 and data['accepted']==data['confirmed']==1 and data['planJob']['state']=='Succeeded'
    assert r['result_code']=='CONTAINER_RECYCLE_INTERRUPTED' and value['confirmation'] is None
    assert r['root_state']=='Aborted' and r['source_retained'] is r['source_restored'] is False and r['history_verified'] and r['history_may_exist']
    q=dict(operationId=uuid.UUID(r['execution_job_id']).hex,owner=r['owner_hash'],plan=native_plan(json.loads(r['plan_json'])));receipt={k:v for k,v in value['receipt'].items() if k!='id'};receipt['operationId']=q['operationId'];receipt=proto_receipt(receipt,q);assert receipt['state']=='Aborted' and receipt['historyDigest']==r['history_digest']
    records=read_operation(q,receipt,e/'originals',create=create);p=physical(Path(fixture['folder']),q['operationId']);typed_equal(p['source'],fixture['original']);assert p['retained'] is None
    assert records['history']['captureState']=='Canceled';job=data['executionJob']['result_json'];job=json.loads(job) if isinstance(job,str) else job;assert len(job)==14 and job['state']=='Failed' and job['receiptState']=='Aborted' and job['sourceRetained'] is False and not job['publishBarrier']
    return dict(sql=data,request=q,receipt=receipt,records=records,physical=p)


def main(revision,kind):
    assert kind in KINDS
    assert os.geteuid()==0 and re.fullmatch('[a-f0-9]{40}',revision);os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0';repo=Path(__file__).resolve().parents[2];assert not subprocess.check_output(['git','-C',str(repo),'status','--porcelain']).strip();sha=subprocess.check_output(['git','-C',str(repo),'rev-parse','HEAD'],text=True).strip();closure=source_closure(repo,sha,seeds=SOURCES)
    for n,h in closure.items():assert hashlib.sha256((repo/'deploy/validation'/n).read_bytes()).hexdigest()==h
    preflight();before=baseline(revision);e=repo/'deploy/artifacts'/('container-recycle-restart-'+uuid.uuid4().hex);e.mkdir(mode=0o700);save=lambda n,v:(e/n).write_text(json.dumps(v));save('before.json',before);save('source.json',dict(revision=revision,validationRevision=sha,validationSourceSha256=closure));print(json.dumps(dict(actualRestart='started',evidence=e.name)),flush=True);held=None;worker=Worker()
    try:
        with owned.fixture(restartable_core=('whaledeck-worker',) if kind=='worker-crash' else ()) as f:
            f['retain_unsettled']=True;leaf=f['folder']/'recycle-rpc.bin'
            # Use the already validated 1 MiB+7 ordinary-file size; no large-file performance claim.
            assert os.statvfs(f['folder']).f_bavail*os.statvfs(f['folder']).f_frsize>2*1024**3
            with leaf.open('xb') as out:
                chunk=hashlib.shake_256(('inflight:'+f['identifier']).encode()).digest(1024**2)
                for _ in range(1):out.write(chunk)
                out.write(b'fixture')
            leaf.chmod(0o640);os.chown(leaf,1000,1000);os.setxattr(leaf,'user.recycle',b'pre-removal restart original');original=file_snapshot(leaf);c=json.loads(owned.common.command(['docker','inspect',f['identifier']]))[0];header=dict(containerId=f['identifier'],instanceId=str(c['State']['Pid'])+':'+c['State']['StartedAt']);reader=load('/usr/local/libexec/whaledeck-container-files.py','inflight_recycle_reader');fixture=dict(kind=kind,revision=revision,containerId=f['identifier'],containerName=c['Name'].lstrip('/'),containerPid=c['State']['Pid'],instanceId=header['instanceId'],header=header,folder=str(f['folder']),evidence=str(e),original=original,sourceVersion=reader.version(header,'work/recycle-rpc.bin',leaf.stat()));save('fixture.json',fixture)
            with temporary_user(True) as (username,password):
                with (e/'browser.stdout').open('x') as out,(e/'browser.stderr').open('x') as err:
                    process=subprocess.Popen(['node',str(repo/'deploy/validation/validate_container_recycle_restart.mjs'),revision],stdin=subprocess.PIPE,stdout=out,stderr=err,env={**os.environ,'WHALEDECK_URL':'http://127.0.0.1:8080','VALIDATION_USERNAME':username,'VALIDATION_PASSWORD':password},text=True)
                    try:
                        process.stdin.write(json.dumps(fixture));process.stdin.close();deadline=time.monotonic()+420;intent=None;exited=False
                        while process.poll() is None:
                            if intent is None and (e/'armed-intent.json').exists():
                                armed=json.loads((e/'armed-intent.json').read_text());intent=armed['id'];fixture['subject']=armed['subject'];save('fixture.json',fixture);r=sql(intent)['row'];assert r['state']=='Ready' and r['execution_job_id'] is None;typed_equal(file_snapshot(leaf),original);save('observer-armed.json',dict(id=intent))
                            if intent and held is None and (e/'observer-armed.json').exists():
                                r=sql(intent)['row']
                                if r['execution_job_id']:
                                    held=freeze_before_removal(r,f['folder'],fixture['containerPid']);assert held is not None,'EXACT_CUT_MISSED_NO_REPLAY';save('cut-record.json',held.record);save('cut-proof.json',held.proof)
                                    restart_row=restart(kind,held,worker,revision,e);exited=True
                                    if kind=='worker-crash':natural_lease(restart_row,fixture,e)
                                    until=time.monotonic()+80
                                    while True:
                                        settled=sql(intent)['row']
                                        if settled['state']=='Failed':break
                                        assert settled['state'] in ('Pending','Applying') and time.monotonic()<until;time.sleep(.2)
                                    save('settled.json',dict(id=intent,state='Failed',executionJobId=restart_row['execution_job_id']))
                            assert time.monotonic()<deadline;time.sleep(.02)
                        assert process.returncode==0 and held is not None and exited,'ACTUAL_RESTART_FAILED'
                    finally:
                        if held is not None:held.release()
                        if process.poll() is None:process.terminate();process.wait(timeout=10)
                        worker.start()
                        if kind=='agent-restart' and command('systemctl','show','whaledeck-agent.service','-p','ActiveState','--value')!='active':command('systemctl','start','whaledeck-agent.service')
                        host_snapshot(revision)
                b=json.loads((e/'browser.json').read_text());browser_proof(b,fixture);proof=prove(b['result'],fixture,e,create=True);save('live-proof.json',proof);denied('http://127.0.0.1:8080',fixture,b['result'])
                with (e/'independent-live.log').open('x') as out:subprocess.run([sys.executable,str(repo/'deploy/validation/verify_container_recycle_restart.py'),e.name,'--live'],stdout=out,stderr=subprocess.STDOUT,check=True,timeout=90)
            typed_equal(file_snapshot(leaf),original);leaf.unlink();private=f['folder']/PRIVATE
            if os.path.lexists(private):
                i=private.lstat();assert [i.st_dev,i.st_ino]==proof['records']['root']['privateIdentity'] and not private.is_symlink() and not list(private.iterdir());private.rmdir()
            f['retain_unsettled']=False
        after=baseline(revision);preserved_restart(before,after,[proof['request']['operationId']],json.loads((e/'service-restart.json').read_text()),kind);save('after.json',after);preflight();save('verified.json',dict(realUiRestart=True,sourceUnchanged=True,exactHelperExit=True,fixtureAndUsersRemoved=True,rootAndHddRetained=True,ordinaryDenied=8,anonymousDenied=4))
        with (e/'independent-final.log').open('x') as out:subprocess.run([sys.executable,str(repo/'deploy/validation/verify_container_recycle_restart.py'),e.name],stdout=out,stderr=subprocess.STDOUT,check=True,timeout=90)
        print(json.dumps(dict(actualRestart='passed',evidence=e.name)),flush=True)
    except BaseException as error:
        import traceback
        t=traceback.extract_tb(error.__traceback__)[-1];save('failure.json',dict(type=type(error).__name__,file=Path(t.filename).name,line=t.lineno));raise

if __name__=='__main__':
    try:assert len(sys.argv)==3;main(sys.argv[1],sys.argv[2])
    except BaseException as error:print(json.dumps(dict(actualRestart='failed',type=type(error).__name__)),flush=True);raise SystemExit(1) from None
