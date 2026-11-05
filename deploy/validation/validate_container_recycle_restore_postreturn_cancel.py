#!/usr/bin/env python3
"""Fresh late cancellation after original inode return, before own final journal.

No retries of a missed Apply. Unsettled owned resources and all histories stay.
Only this invocation's independently proved synthetic trash may be removed.
"""
import hashlib,json,os,re,subprocess,sys,time,uuid,threading
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
import validate_container_file_publishing as owned
import validate_container_recycle_browser as source_ui
import container_recycle_agent_io as source_io
import container_recycle_restore_agent_io as restore_io
import bound_container_recycle_restore_cut as cut
import bound_container_recycle_restore_returned_cut as returned
from restore_returned_cut_io import read_pending
from validate_container_recycle_restore_browser import prove_result
from validate_container_recycle_restore_browser import sql,idle,old_rows,denied,stop_browser
from validate_container_recycle_restore_agent import baseline
from validate_container_recycle_agent import preserved as preserve_source
from validate_container_file_staging import load
from validate_container_upload_bff import temporary_user
from validate_metric_continuity import query
from acl_source_apply_missing_proof import source_closure
from acl_history_browser_proof import typed_equal
from recycle_restore_browser_proof import execution,managed_plan,managed_receipt,job_summary,stored_object

SOURCES=('validate_container_recycle_restore_postreturn_cancel.py','validate_container_recycle_restore_postreturn_cancel.mjs','verify_container_recycle_restore_postreturn_cancel.py','test_restore_postreturn_cancel_proof.py')


def preserved(before,after,source_op,restore_op):
    preserve_source({k:v for k,v in before.items() if k!='recycleRestoreRoot'},{k:v for k,v in after.items() if k!='recycleRestoreRoot'},[source_op])
    old=before['recycleRestoreRoot'];new=after['recycleRestoreRoot'];assert all(new.get(k)==v for k,v in old.items())
    assert set(new)-set(old)=={restore_op+'.json',restore_op+'.lock','claims/'+source_op+'.json'}


def cut_binding(row,record,proof,fixture):
    plan=managed_plan(stored_object(row['plan_json']));op=uuid.UUID(execution(row['id'])).hex
    assert uuid.UUID(row['execution_job_id']).hex==record['operationId']==proof['restoreOperationId']==op
    typed_equal(record['plan'],plan);assert record['owner']==row['owner_hash']
    assert record['state']=='Applying' and record['claimed'] is record['historyVerified'] is True
    assert proof['rootPhase']=='Applying' and proof['sourcePhase']=='Restored' and proof['sourceReturned'] is proof['sourceClaimMatches'] is proof['threeKernelFdProofs'] is True
    assert proof['sourceOperationId']==plan['command']['source']['operationId'] and proof['historyDigest']==plan['command']['historyDigest']
    typed_equal(proof['restoredIdentity'][:8],plan['retainedIdentity'][:8]);assert proof['restoredSha256']==fixture['original']['sha256']
    typed_equal(proof['containerRootIdentity'],fixture['rootIdentity'])
    identities=[tuple(proof[k]) for k in ('containerRootIdentity','restoreLockIdentity','sourceLockIdentity')];assert len(set(identities))==3 and all(len(v)==2 for v in identities)
    assert type(proof['pid']) is int and proof['pid']>1 and re.fullmatch('[a-f0-9]{32}',proof['invocationId']) and cut.GROUP.fullmatch(proof['cgroup'])


def prove(value,fixture,source,e,create=False,live=True):
    # The common completed projection joins actual terminal receipt, own claim, source and HDD.
    if live:
        proof=prove_result(value,fixture,source,e,create=create)
    else:
        from container_recycle_restore_agent_io import read_operation
        saved=json.loads((e/'live-proof.json').read_text());typed_equal(sql(value['id']),saved['sql']);typed_equal(read_operation(saved['request'],saved['receipt'],e/'restore-originals'),saved['records']);proof={**saved,'physical':None}
    row=proof['sql']['row'];assert row['state']=='Succeeded' and row['cancel_requested_at_utc'] and row['result_code']=='CONTAINER_RECYCLE_RESTORE_COMPLETED'
    audits=query("SELECT json_build_object('n',count(*)) FROM audit_events WHERE job_id=:'id'::uuid AND action='containers.files.recycle-restore.cancel-requested';",id=row['execution_job_id']);assert audits['n']==1
    pending=json.loads((e/'cut-source-proof.json').read_text());assert pending['verification']['hddDataReread'] is True and pending['root']['state']=='Applying'
    for key in ('claim','sourceRoot','history','data'):typed_equal(proof['records'][key],pending[key])
    proof['cancelExecutionAudits']=audits['n'];return proof


def independent(repo,e,live):
    with (e/('independent-live.log' if live else 'independent-final.log')).open('x') as out:subprocess.run([sys.executable,str(repo/'deploy/validation/verify_container_recycle_restore_postreturn_cancel.py'),e.name]+(['--live'] if live else []),check=True,stdout=out,stderr=subprocess.STDOUT,timeout=90)


def main(revision):
    assert os.geteuid()==0 and re.fullmatch('[a-f0-9]{40}',revision);os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0';repo=Path(__file__).resolve().parents[2]
    git=lambda *a:subprocess.check_output(['git','-c','safe.directory='+str(repo),'-C',str(repo),*a])
    assert not git('status','--porcelain').strip();sha=git('rev-parse','HEAD').decode().strip();closure=source_closure(repo,sha,seeds=SOURCES)
    for n,h in closure.items():assert hashlib.sha256((repo/'deploy/validation'/n).read_bytes()).hexdigest()==h
    for n in ('whaledeck-privileged','whaledeck-container-recycle-restore-transport.py'):assert (Path('/usr/local/libexec')/n).read_bytes()==git('show',revision+':deploy/host/'+n)
    idle();before=baseline(revision);e=repo/'deploy/artifacts'/('container-recycle-restore-postreturn-cancel-'+uuid.uuid4().hex);e.mkdir(mode=0o700)
    def save(n,v):
        with (e/n).open('x') as out:json.dump(v,out)
    save('before.json',before);save('source.json',dict(revision=revision,validationRevision=sha,validationSourceSha256=closure));print(json.dumps(dict(restorePostreturnCancel='started',evidence=e.name)),flush=True)
    held=None
    try:
        with owned.fixture() as f:
            f['retain_unsettled']=True;leaf=f['folder']/'recycle-rpc.bin';leaf.write_bytes(hashlib.shake_256(('inflight-independent-restore:'+f['identifier']).encode()).digest(1024**2+7));leaf.chmod(0o640);os.chown(leaf,1000,1000);os.setxattr(leaf,'user.recycle',b'cancel must keep original trash')
            c=json.loads(owned.common.command(['docker','inspect',f['identifier']]))[0];header=dict(containerId=f['identifier'],instanceId=str(c['State']['Pid'])+':'+c['State']['StartedAt']);reader=load('/usr/local/libexec/whaledeck-container-files.py','restore_cancel_reader')
            fixture=dict(revision=revision,containerId=f['identifier'],containerName=c['Name'].lstrip('/'),instanceId=header['instanceId'],containerPid=c['State']['Pid'],rootIdentity=list(cut.identity(Path('/proc')/str(c['State']['Pid'])/'root')),header=header,folder=str(f['folder']),evidence=str(e),original=source_io.file_snapshot(leaf),sourceVersion=reader.version(header,'work/recycle-rpc.bin',leaf.stat()))
            save('fixture-initial.json',fixture);save('prior-business.json',old_rows(f['identifier']))
            with temporary_user(True) as (username,password):
                source=None;intent=None;watch=None;exit_future=None;exited=False;box={};abort=threading.Event();pool=ThreadPoolExecutor(max_workers=2)
                def observe(identifier):
                    deadline=time.monotonic()+100
                    while time.monotonic()<deadline and not abort.is_set():
                        row=sql(identifier)['row']
                        if row['execution_job_id']:
                            result=returned.freeze_after_return(row,f['folder'],fixture['containerPid']);box['held']=result;return result
                        time.sleep(.01)
                    return None
                with (e/'browser.stdout').open('x') as out,(e/'browser.stderr').open('x') as err:
                    process=subprocess.Popen(['node',str(repo/'deploy/validation/validate_container_recycle_restore_postreturn_cancel.mjs'),revision],stdin=subprocess.PIPE,stdout=out,stderr=err,text=True,env={**os.environ,'WHALEDECK_URL':'http://127.0.0.1:8080','VALIDATION_USERNAME':username,'VALIDATION_PASSWORD':password})
                    try:
                        process.stdin.write(json.dumps(fixture));process.stdin.close();deadline=time.monotonic()+300
                        while process.poll() is None:
                            if source is None and (e/'source-completed.json').exists():
                                signal=json.loads((e/'source-completed.json').read_text());fixture['subject']=signal['subject'];source=source_ui.verify_case(signal['result'],fixture,e,create=True);(e/'originals').rename(e/'source-originals');save('source-proof.json',source);save('source-accepted.json',dict(id=signal['result']['id']))
                            if intent is None and (e/'armed-intent.json').exists():
                                armed=json.loads((e/'armed-intent.json').read_text());intent=armed['id'];assert source and armed['subject']==fixture['subject'];row=sql(intent)['row'];assert row['state']=='Ready' and row['execution_job_id'] is None;save('ready-sql.json',sql(intent));watch=pool.submit(observe,intent);save('observer-armed.json',dict(id=intent))
                            if watch is not None and watch.done() and held is None:
                                held=watch.result();assert held is not None,'EXACT_RESTORE_CUT_MISSED_NO_REPLAY';cutrow=sql(intent)['row'];assert cutrow['state']=='Applying' and cutrow['root_started_at_utc'] and cutrow['publish_barrier'];cut_binding(cutrow,held.record,held.proof,fixture)
                                began=time.monotonic();exit_future=pool.submit(held.wait_exit_seconds,began,20)
                                # This is a current independent HDD/data read while the exact helper is stopped.
                                request=dict(operationId=uuid.UUID(cutrow['execution_job_id']).hex,owner=cutrow['owner_hash'],plan=managed_plan(stored_object(cutrow['plan_json'])));pending=read_pending(request,e/'cut-originals',create=True);typed_equal(pending['root'],held.record)
                                save('cut-record.json',held.record);save('cut-proof.json',held.proof);save('cut-sql.json',sql(intent));save('cut-source-proof.json',pending);save('cut-held.json',dict(id=intent,executionJobId=cutrow['execution_job_id']))
                            if exit_future is not None and exit_future.done() and not exited:
                                elapsed=exit_future.result();assert elapsed is not None and (e/'cancel-request-began.json').exists(),'RESTORE_HELPER_EXIT_NOT_OBSERVED';save('helper-exit.json',dict(elapsedSeconds=elapsed,observedWithOriginalPidfd=True,measurementBeganBeforeUiAllowed=True));exited=True
                            if (e/'cancel-completed.json').exists() and not (e/'cancel-accepted.json').exists():
                                result=json.loads((e/'cancel-completed.json').read_text())['result'];proof=prove(result,fixture,source,e,create=True);save('live-proof.json',proof);save('cancel-accepted.json',dict(id=result['id']))
                            assert time.monotonic()<deadline,'RESTORE_CANCEL_BROWSER_DEADLINE';time.sleep(.02)
                        assert process.returncode==0 and held is not None and exited,'RESTORE_POSTRETURN_CANCEL_FAILED'
                    except BaseException as error:
                        import traceback
                        save('browser-primary-failure.json',dict(type=type(error).__name__,frames=[dict(file=Path(t.filename).name,line=t.lineno) for t in traceback.extract_tb(error.__traceback__)]));raise
                    finally:
                        abort.set()
                        if held is not None:held.release()
                        try:stop_browser(process)
                        finally:
                            pool.shutdown(wait=True,cancel_futures=True)
                            if box.get('held') is not None:box['held'].release()
                b=json.loads((e/'browser.json').read_text());assert b['actualUi'] and b['normalDualLogouts']==2 and b['oldReferencesDenied']==3
                save('fixture.json',fixture);denied(fixture,b['result']);independent(repo,e,True)
            op=source['request']['operationId'];p=json.loads((e/'live-proof.json').read_text());private=f['folder']/source_io.PRIVATE
            typed_equal(source_io.file_snapshot(leaf),p['physical']['source']);assert p['physical']['retained'] is None;leaf.unlink();i=private.lstat();assert [i.st_dev,i.st_ino]==p['records']['sourceRoot']['privateIdentity'] and not private.is_symlink() and not list(private.iterdir());private.rmdir();f['retain_unsettled']=False
        after=baseline(revision);preserved(before,after,op,p['request']['operationId']);save('after.json',after);typed_equal(old_rows(fixture['containerId']),json.loads((e/'prior-business.json').read_text()));idle()
        save('verified.json',dict(realUiCancellation=True,exactHelperExit=True,originalReturnedBeforeCancel=True,productRestorePerformed=True,ownedFixtureRemoved=True,historiesRetained=True,ordinaryDenied=9,anonymousDenied=5,normalDualLogouts=2))
        independent(repo,e,False);print(json.dumps(dict(restorePostreturnCancel='passed',evidence=e.name)),flush=True)
    except BaseException as error:
        import traceback
        save('failure.json',dict(type=type(error).__name__,frames=[dict(file=Path(t.filename).name,line=t.lineno) for t in traceback.extract_tb(error.__traceback__)]));raise


if __name__=='__main__':
    try:assert len(sys.argv)==2;main(sys.argv[1])
    except BaseException as error:print(json.dumps(dict(restorePostreturnCancel='failed',type=type(error).__name__)),flush=True);raise SystemExit(1) from None
