#!/usr/bin/env python3
"""Fresh owned returned-target displacement and normal logout; never replay Apply.

No retries of a missed Apply. Unsettled owned resources and all histories stay.
Only this invocation's independently proved returned original may be removed.
"""
import hashlib,json,os,re,stat,subprocess,sys,time,uuid,threading
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
import validate_container_file_publishing as owned
import validate_container_recycle_browser as source_ui
import container_recycle_agent_io as source_io
import container_recycle_restore_agent_io as restore_io
import bound_container_recycle_restore_cut as cut
from validate_container_recycle_restore_browser import sql,idle,old_rows,denied,stop_browser
from validate_container_recycle_restore_agent import baseline
from validate_container_recycle_agent import preserved as preserve_source
from validate_container_file_staging import load
from validate_container_upload_bff import temporary_user
from validate_metric_continuity import query
from acl_source_apply_missing_proof import source_closure
from acl_history_browser_proof import typed_equal
from recycle_restore_browser_proof import execution,managed_plan,managed_receipt,job_summary,stored_object

SOURCES=('validate_container_recycle_restore_superseded.py','validate_container_recycle_restore_superseded.mjs','verify_container_recycle_restore_superseded.py','test_container_recycle_restore_superseded.py')


from validate_container_recycle_restore_postreturn_cancel import preserved,cut_binding
from restore_superseded_proof import terminal,namespace,displace_returned,cleanup_displaced_proof,DISPLACED
from validate_container_recycle_restore_inflight_logout import browser_proof as logout_browser_proof
from validate_container_recycle_inflight_logout import observe_logout_exit
import bound_container_recycle_restore_returned_cut as returned
from restore_returned_cut_io import read_pending


def browser_proof(b):
    logout_browser_proof(b)
    assert b['retryRequests']==[] and b['result']['state']=='Superseded' and b['job']['state']=='Failed' and b['job']['phase']=='Superseded'


def prove(value,fixture,source,e,create=False,live=True):
    data=sql(value['id']);q,receipt=terminal(value,data,fixture,source)
    typed_equal(source_ui.sql(data['row']['source_intent_id']),source['sql'])
    records=restore_io.read_operation(q,receipt,e/'restore-originals',create=create)
    if live:
        current,moved=namespace(Path(fixture['folder']),fixture['original'],records,receipt,fixture['displaced'])
    else:
        saved=json.loads((e/'live-proof.json').read_text());typed_equal(data,saved['sql']);typed_equal(records,saved['records']);current=moved=None
    pending=json.loads((e/'cut-source-proof.json').read_text());assert pending['verification']['hddDataReread'] is True and pending['root']['state']=='Applying'
    for key in ('claim','sourceRoot','history','data'):typed_equal(records[key],pending[key])
    audits=query("SELECT json_build_object('n',count(*)) FROM audit_events WHERE job_id=:'id'::uuid AND action='containers.files.recycle-restore.cancel-requested';",id=data['row']['execution_job_id']);assert audits['n']==0
    return dict(sql=data,request=q,receipt=receipt,records=records,physical=current,displaced=moved,explicitCancelAudits=0)


def independent(repo,e,live):
    with (e/('independent-live.log' if live else 'independent-final.log')).open('x') as out:subprocess.run([sys.executable,str(repo/'deploy/validation/verify_container_recycle_restore_superseded.py'),e.name]+(['--live'] if live else []),check=True,stdout=out,stderr=subprocess.STDOUT,timeout=90)


def main(revision):
    assert os.geteuid()==0 and re.fullmatch('[a-f0-9]{40}',revision);os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0';repo=Path(__file__).resolve().parents[2]
    git=lambda *a:subprocess.check_output(['git','-c','safe.directory='+str(repo),'-C',str(repo),*a])
    assert not git('status','--porcelain').strip();sha=git('rev-parse','HEAD').decode().strip();closure=source_closure(repo,sha,seeds=SOURCES)
    for n,h in closure.items():assert hashlib.sha256((repo/'deploy/validation'/n).read_bytes()).hexdigest()==h
    for n in ('whaledeck-privileged','whaledeck-container-recycle-restore-transport.py'):assert (Path('/usr/local/libexec')/n).read_bytes()==git('show',revision+':deploy/host/'+n)
    idle();before=baseline(revision);e=repo/'deploy/artifacts'/('container-recycle-restore-superseded-'+uuid.uuid4().hex);e.mkdir(mode=0o700)
    def save(n,v):
        with (e/n).open('x') as out:json.dump(v,out)
    save('before.json',before);save('source.json',dict(revision=revision,validationRevision=sha,validationSourceSha256=closure));print(json.dumps(dict(restoreSuperseded='started',evidence=e.name)),flush=True)
    held=None
    try:
        with owned.fixture() as f:
            f['retain_unsettled']=True;leaf=f['folder']/'recycle-rpc.bin';leaf.write_bytes(hashlib.shake_256(('inflight-independent-restore:'+f['identifier']).encode()).digest(1024**2+7));leaf.chmod(0o640);os.chown(leaf,1000,1000);os.setxattr(leaf,'user.recycle',b'superseded after actual original return')
            c=json.loads(owned.common.command(['docker','inspect',f['identifier']]))[0];header=dict(containerId=f['identifier'],instanceId=str(c['State']['Pid'])+':'+c['State']['StartedAt']);reader=load('/usr/local/libexec/whaledeck-container-files.py','restore_logout_reader')
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
                    process=subprocess.Popen(['node',str(repo/'deploy/validation/validate_container_recycle_restore_superseded.mjs'),revision],stdin=subprocess.PIPE,stdout=out,stderr=err,text=True,env={**os.environ,'WHALEDECK_URL':'http://127.0.0.1:8080','VALIDATION_USERNAME':username,'VALIDATION_PASSWORD':password})
                    try:
                        process.stdin.write(json.dumps(fixture));process.stdin.close();deadline=time.monotonic()+300
                        while process.poll() is None:
                            if source is None and (e/'source-completed.json').exists():
                                signal=json.loads((e/'source-completed.json').read_text());fixture['subject']=signal['subject'];source=source_ui.verify_case(signal['result'],fixture,e,create=True);(e/'originals').rename(e/'source-originals');save('source-proof.json',source);save('source-accepted.json',dict(id=signal['result']['id']))
                            if intent is None and (e/'armed-intent.json').exists():
                                armed=json.loads((e/'armed-intent.json').read_text());intent=armed['id'];assert source and armed['subject']==fixture['subject'];row=sql(intent)['row'];assert row['state']=='Ready' and row['execution_job_id'] is None;save('ready-sql.json',sql(intent));watch=pool.submit(observe,intent);save('observer-armed.json',dict(id=intent))
                            if watch is not None and watch.done() and held is None:
                                held=watch.result();assert held is not None,'EXACT_RESTORE_CUT_MISSED_NO_REPLAY';cutrow=sql(intent)['row'];assert cutrow['state']=='Applying' and cutrow['root_started_at_utc'] and cutrow['publish_barrier'];cut_binding(cutrow,held.record,held.proof,fixture)
                                # Keep the exact process stopped: real logout must trigger product-owned service teardown.
                                # The bounded pidfd observer must pass before QA's finally may send SIGCONT.
                                began=time.monotonic();exit_future=pool.submit(observe_logout_exit,held,began,e,20)
                                # This is a current independent HDD/data read while the exact helper is stopped.
                                request=dict(operationId=uuid.UUID(cutrow['execution_job_id']).hex,owner=cutrow['owner_hash'],plan=managed_plan(stored_object(cutrow['plan_json'])));pending=read_pending(request,e/'cut-originals',create=True);typed_equal(pending['root'],held.record)
                                save('cut-record.json',held.record);save('cut-proof.json',held.proof);save('cut-sql.json',sql(intent));save('cut-source-proof.json',pending)
                                displacement=displace_returned(f,fixture,held,cutrow,pending);fixture['displaced']=displacement['displaced'];save('displacement.json',displacement);save('cut-held.json',dict(id=intent,executionJobId=cutrow['execution_job_id']))
                            if exit_future is not None and exit_future.done() and not exited:
                                observation=exit_future.result();save('helper-exit.json',observation);exited=True
                            if (e/'logout-completed.json').exists() and not (e/'logout-accepted.json').exists():
                                result=json.loads((e/'logout-completed.json').read_text())['result'];proof=prove(result,fixture,source,e,create=True);save('live-proof.json',proof);save('logout-accepted.json',dict(id=result['id']))
                            assert time.monotonic()<deadline,'RESTORE_CANCEL_BROWSER_DEADLINE';time.sleep(.02)
                        assert process.returncode==0 and held is not None and exited,'RESTORE_POSTRETURN_LOGOUT_FAILED'
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
                b=json.loads((e/'browser.json').read_text());browser_proof(b)
                save('fixture.json',fixture);denied(fixture,b['result']);independent(repo,e,True)
            op=source['request']['operationId'];p=json.loads((e/'live-proof.json').read_text());private=f['folder']/source_io.PRIVATE
            moved=f['folder']/DISPLACED;cleanup_displaced_proof(source_io.file_snapshot(moved),fixture['original'],p);assert not os.path.lexists(leaf)
            i=private.lstat();assert [i.st_dev,i.st_ino]==p['records']['sourceRoot']['privateIdentity'] and not private.is_symlink() and not list(private.iterdir()) and i.st_uid==i.st_gid==0 and stat.S_IMODE(i.st_mode)==0o700
            moved.unlink();private.rmdir();f['retain_unsettled']=False
        after=baseline(revision);preserved(before,after,op,p['request']['operationId']);save('after.json',after);typed_equal(old_rows(fixture['containerId']),json.loads((e/'prior-business.json').read_text()));idle()
        save('verified.json',dict(realUiSuperseded=True,exactHelperExit=True,displacedReturnedOriginalPreservedUntilIndependentProof=True,productRestorePerformed=True,ownedFixtureRemoved=True,historiesRetained=True,ordinaryDenied=9,anonymousDenied=5,normalDualLogouts=2))
        independent(repo,e,False);print(json.dumps(dict(restoreSuperseded='passed',evidence=e.name)),flush=True)
    except BaseException as error:
        import traceback
        save('failure.json',dict(type=type(error).__name__,frames=[dict(file=Path(t.filename).name,line=t.lineno) for t in traceback.extract_tb(error.__traceback__)]));raise


if __name__=='__main__':
    try:assert len(sys.argv)==2;main(sys.argv[1])
    except BaseException as error:print(json.dumps(dict(restoreSuperseded='failed',type=type(error).__name__)),flush=True);raise SystemExit(1) from None
