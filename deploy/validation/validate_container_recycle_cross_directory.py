#!/usr/bin/env python3
"""Fresh recycle Unknown, cross-container mkdir deferrals, explicit ACK and same-task release."""
import hashlib,json,os,re,select,subprocess,sys,time,uuid
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
import validate_container_file_publishing as owned
from validate_container_file_staging import load
from validate_container_directory_restart import preflight
from validate_container_upload_bff import temporary_user
from validate_container_recycle_browser import sql,denied
from validate_container_recycle_agent import baseline
from container_recycle_agent_io import file_snapshot,PRIVATE
from container_recycle_cross_directory_proof import waiting_baseline,history_snapshot,observe_waiting,finish_waiting,clean_waiting,preserved_cross
from container_recycle_unknown_proof import DISPLACED,displace,displaced_proof,prove,require_ack
from validate_container_recycle_inflight_logout import observe_logout_exit
from bound_container_recycle_cut import freeze_before_removal
from acl_source_apply_missing_proof import source_closure
from acl_history_browser_proof import typed_equal

SOURCES=('validate_container_recycle_cross_directory.py','validate_container_recycle_cross_directory.mjs','verify_container_recycle_cross_directory.py','test_container_recycle_cross_directory.py')

def stop_child(process, timeout=10):
    if process.poll() is None:
        process.terminate()
        try:process.wait(timeout=timeout)
        except subprocess.TimeoutExpired:
            # This is exactly the Node child spawned by this gate, never a supplied PID.
            process.kill();process.wait(timeout=timeout)


def browser_proof(b,fixture):
    assert b['actualUi'] and b['subject']==fixture['subject'] and b['recoverySubject']!=b['subject'] and b['registrations']==b['confirmations']==1 and b['cancellations']==0 and b['acknowledgements']==1 and b['managementWrites']==3
    assert b['originalSessionReadStatus']==401 and b['differentAdminOldIdStatus']==404 and b['hintsCleared'] and b['cancelledDecisionPreserved']
    d=b['waitingDirectory'];assert d['plans']==d['registrations']==1 and d['cancellations']==d['uploads']==0 and d['state']=='Succeeded' and d['anonymousStatus']==401
    assert len(b['logoutReceipts'])==2
    for r in b['logoutReceipts']:
        assert r['normalUiLogout'] and r['postStatus'] in (302,303) and r['providerNavigationObserved'] and r['bffStatus']==401 and r['identityStatus'] in (401,403) and r['finalLoginPage']


def main(revision):
    assert os.geteuid()==0 and re.fullmatch('[a-f0-9]{40}',revision);os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0';repo=Path(__file__).resolve().parents[2];assert not subprocess.check_output(['git','-C',str(repo),'status','--porcelain']).strip();sha=subprocess.check_output(['git','-C',str(repo),'rev-parse','HEAD'],text=True).strip();closure=source_closure(repo,sha,seeds=SOURCES)
    for n,h in closure.items():assert hashlib.sha256((repo/'deploy/validation'/n).read_bytes()).hexdigest()==h
    preflight();before=baseline(revision);e=repo/'deploy/artifacts'/('container-recycle-cross-directory-'+uuid.uuid4().hex);e.mkdir(mode=0o700)
    def save(n,v):
        tmp=e/(n+'.tmp')
        with tmp.open('x') as out:json.dump(v,out)
        os.replace(tmp,e/n)
    save('before.json',before);save('source.json',dict(revision=revision,validationRevision=sha,validationSourceSha256=closure));print(json.dumps(dict(actualCrossDirectory='started',evidence=e.name)),flush=True);held=None
    try:
        with owned.fixture() as f,owned.fixture() as other:
            other['retain_unsettled']=True;waiting=waiting_baseline(other,f);save('waiting-baseline.json',waiting);save('cross-directory-scope.json',dict(oldDirectoryHistory=history_snapshot()))
            f['retain_unsettled']=True;leaf=f['folder']/'recycle-rpc.bin'
            # Use the already validated 1 MiB+7 ordinary-file size; no large-file performance claim.
            assert os.statvfs(f['folder']).f_bavail*os.statvfs(f['folder']).f_frsize>2*1024**3
            with leaf.open('xb') as out:
                chunk=hashlib.shake_256(('inflight:'+f['identifier']).encode()).digest(1024**2)
                for _ in range(1):out.write(chunk)
                out.write(b'fixture')
            leaf.chmod(0o640);os.chown(leaf,1000,1000);os.setxattr(leaf,'user.recycle',b'pre-removal unknown original');original=file_snapshot(leaf);c=json.loads(owned.common.command(['docker','inspect',f['identifier']]))[0];header=dict(containerId=f['identifier'],instanceId=str(c['State']['Pid'])+':'+c['State']['StartedAt']);reader=load('/usr/local/libexec/whaledeck-container-files.py','inflight_recycle_reader');fixture=dict(waitingDirectory=waiting,revision=revision,containerId=f['identifier'],containerName=c['Name'].lstrip('/'),containerPid=c['State']['Pid'],instanceId=header['instanceId'],header=header,folder=str(f['folder']),evidence=str(e),original=original,sourceVersion=reader.version(header,'work/recycle-rpc.bin',leaf.stat()));save('fixture.json',fixture)
            with temporary_user(True) as (username,password),temporary_user(True) as (recovery_username,recovery_password):
                with (e/'browser.stdout').open('x') as out,(e/'browser.stderr').open('x') as err:
                    process=subprocess.Popen(['node',str(repo/'deploy/validation/validate_container_recycle_cross_directory.mjs'),revision],stdin=subprocess.PIPE,stdout=out,stderr=err,env={**os.environ,'WHALEDECK_URL':'http://127.0.0.1:8080','VALIDATION_USERNAME':username,'VALIDATION_PASSWORD':password,'RECOVERY_USERNAME':recovery_username,'RECOVERY_PASSWORD':recovery_password},text=True)
                    try:
                        process.stdin.write(json.dumps(fixture));process.stdin.close();deadline=time.monotonic()+500;intent=None;exited=False;unknown=None;blocked=False;waiting_verified=None;pool=ThreadPoolExecutor(max_workers=1);exit_future=None
                        while process.poll() is None:
                            if intent is None and (e/'armed-intent.json').exists():
                                armed=json.loads((e/'armed-intent.json').read_text());intent=armed['id'];fixture['subject']=armed['subject'];save('fixture.json',fixture);r=sql(intent)['row'];assert r['state']=='Ready' and r['execution_job_id'] is None;typed_equal(file_snapshot(leaf),original);save('observer-armed.json',dict(id=intent))
                            if intent and held is None and (e/'observer-armed.json').exists():
                                r=sql(intent)['row']
                                if r['execution_job_id']:
                                    held=freeze_before_removal(r,f['folder'],fixture['containerPid']);assert held is not None,'EXACT_CUT_MISSED_NO_REPLAY';save('cut-record.json',held.record);save('cut-proof.json',held.proof);moved=displace(f,fixture,held);fixture['displaced']=moved['displaced'];save('fixture.json',fixture);save('displacement.json',moved);began=time.monotonic();exit_future=pool.submit(observe_logout_exit,held,began,e,20);save('cut-held.json',dict(id=intent,executionJobId=r['execution_job_id']))
                            if exit_future is not None and exit_future.done() and not exited:
                                observation=exit_future.result();save('helper-exit.json',observation);exited=True
                            if exited and unknown is None:
                                r=sql(intent)['row']
                                assert r['state'] in ('Pending','Applying','Unknown')
                                if r['state']=='Unknown' and not (e/'unknown-ready.json').exists():save('unknown-ready.json',dict(id=intent,state='Unknown'))
                                if (e/'unknown-value.json').exists():
                                    value=json.loads((e/'unknown-value.json').read_text());unknown=prove(value,fixture,e,create=True);save('unknown-proof.json',unknown);save('unknown-proved.json',dict(id=intent))
                            if unknown is not None and (e/'waiting-registered.json').exists() and not blocked:
                                registered=json.loads((e/'waiting-registered.json').read_text());observe_waiting(unknown,waiting,registered,lambda:prove(json.loads((e/'unknown-value.json').read_text()),fixture,e),e);blocked=True
                            if blocked and (e/'recovery-canceled.json').exists() and not (e/'acknowledge.json').exists():
                                decision=json.loads((e/'recovery-canceled.json').read_text());assert decision['id']==intent and decision['subject']!=fixture['subject'] and decision['acknowledgements']==0
                                canceled=prove(json.loads((e/'unknown-value.json').read_text()),fixture,e);typed_equal(canceled,unknown);save('canceled-decision-proof.json',canceled);save('acknowledge.json',dict(id=intent,originalProtectionAndHistoryVerified=True))
                            if (e/'waiting-acknowledged.json').exists() and waiting_verified is None:
                                ack=sql(intent);assert ack['row']['state']=='Acknowledged';settled,waiting_verified=finish_waiting(waiting,registered,ack,e);save('waiting-finished.json',settled)
                            assert time.monotonic()<deadline;time.sleep(.02)
                        assert process.returncode==0 and held is not None and exited and blocked and waiting_verified is not None,'ACTUAL_UNKNOWN_FAILED'
                    finally:
                        if held is not None:held.release()
                        stop_child(process)
                        if 'pool' in locals():pool.shutdown(wait=True,cancel_futures=True)
                b=json.loads((e/'browser.json').read_text());browser_proof(b,fixture);proof=prove(b['result'],fixture,e);require_ack(unknown,proof,b['recoverySubject']);save('live-proof.json',proof);denied('http://127.0.0.1:8080',fixture,b['result'])
                with (e/'independent-live.log').open('x') as out:subprocess.run([sys.executable,str(repo/'deploy/validation/verify_container_recycle_cross_directory.py'),e.name,'--live'],stdout=out,stderr=subprocess.STDOUT,check=True,timeout=90)
            clean_waiting(waiting_verified);other['retain_unsettled']=False
            assert file_snapshot(leaf) is None;typed_equal(file_snapshot(f['folder']/DISPLACED),fixture['displaced']);(f['folder']/DISPLACED).unlink();private=f['folder']/PRIVATE
            if os.path.lexists(private):
                i=private.lstat();assert [i.st_dev,i.st_ino]==proof['records']['root']['privateIdentity'] and not private.is_symlink() and not list(private.iterdir());private.rmdir()
            f['retain_unsettled']=False
        after=baseline(revision);preserved_cross(before,after,[proof['request']['operationId']],registered['id']);save('after.json',after);preflight();save('verified.json',dict(realUiCrossDirectory=True,naturalDeferrals=4,originalInodeAndBytesPreservedUnderDisplacedName=True,exactHelperExit=True,fixtureAndUsersRemoved=True,rootAndHddRetained=True,ordinaryDenied=8,anonymousDenied=4))
        with (e/'independent-final.log').open('x') as out:subprocess.run([sys.executable,str(repo/'deploy/validation/verify_container_recycle_cross_directory.py'),e.name],stdout=out,stderr=subprocess.STDOUT,check=True,timeout=90)
        print(json.dumps(dict(actualCrossDirectory='passed',evidence=e.name)),flush=True)
    except BaseException as error:
        import traceback
        t=traceback.extract_tb(error.__traceback__)[-1];frames=[dict(file=Path(v.filename).name,line=v.lineno,function=v.name) for v in traceback.extract_tb(error.__traceback__)];save('failure.json',dict(type=type(error).__name__,file=Path(t.filename).name,line=t.lineno,frames=frames));raise

if __name__=='__main__':
    try:assert len(sys.argv)==2;main(sys.argv[1])
    except BaseException as error:print(json.dumps(dict(actualCrossDirectory='failed',type=type(error).__name__)),flush=True);raise SystemExit(1) from None
