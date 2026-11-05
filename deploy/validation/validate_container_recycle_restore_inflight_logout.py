#!/usr/bin/env python3
"""Fresh visible normal logout of a single exactly bound pre-return restore.

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
from validate_container_recycle_restore_browser import sql,idle,old_rows,denied,stop_browser
from validate_container_recycle_restore_agent import baseline
from validate_container_recycle_agent import preserved as preserve_source
from validate_container_file_staging import load
from validate_container_upload_bff import temporary_user
from validate_metric_continuity import query
from acl_source_apply_missing_proof import source_closure
from acl_history_browser_proof import typed_equal
from recycle_restore_browser_proof import execution,managed_plan,managed_receipt,job_summary,stored_object

SOURCES=('validate_container_recycle_restore_inflight_logout.py','validate_container_recycle_restore_inflight_logout.mjs','verify_container_recycle_restore_inflight_logout.py','test_restore_inflight_logout_proof.py')


from validate_container_recycle_restore_inflight_cancel import preserved,cut_binding
from validate_container_recycle_inflight_logout import observe_logout_exit,browser_proof as prior_browser_proof

def browser_proof(value):
    prior_browser_proof(value)
    assert value['newSessionKeyStatus']==value['newSourceIdStatus']==404


def prove(value,fixture,source,e,create=False,live=True):
    data=sql(value['id']);r=data['row'];op=execution(r['id']);source_op=execution(r['source_intent_id'],source=True)
    assert r['actor_subject']==fixture['subject'] and (r['container_id'],r['instance_id'],r['relative_path'])==(fixture['containerId'],fixture['instanceId'],'work/recycle-rpc.bin')
    assert r['source_intent_id']==value['sourceIntentId']==source['sql']['row']['id'] and source_op==value['sourceOperationId']==source['sql']['row']['execution_job_id']
    assert r['execution_job_id']==value['executionJobId']==op and r['state']==value['state']==data['executionJob']['state']=='Canceled'
    assert r['cancel_requested_at_utc'] is None and r['root_started_at_utc'] and r['completed_at_utc'] and not r['publish_barrier'] and r['lease_token'] is r['lease_expires_at_utc'] is None
    assert data['pending']==0 and data['accepted']==data['confirmed']==1 and data['planJob']['state']=='Succeeded' and value['confirmation'] is None
    audits=query("SELECT json_build_object('n',count(*)) FROM audit_events WHERE job_id=:'id'::uuid AND action='containers.files.recycle-restore.cancel-requested';",id=op);assert audits['n']==0
    plan=managed_plan(stored_object(r['plan_json']));typed_equal(plan['command']['source'],source['request'])
    assert r['root_state']=='Aborted' and r['source_state']=='Trashed' and r['source_retained'] is r['history_verified'] is r['history_available'] is True
    assert r['binding_current'] is r['source_restored'] is r['restored_by_this_operation'] is False
    q=dict(operationId=uuid.UUID(op).hex,owner=r['owner_hash'],plan=plan);receipt=managed_receipt(value['receipt'],q)
    assert receipt['state']=='Aborted' and receipt['historyDigest']==r['history_digest']==source['receipt']['historyDigest']
    stored=stored_object(r['receipt_json']);typed_equal({k[0].lower()+k[1:]:v for k,v in stored.items()},value['receipt'])
    job_summary(stored_object(data['executionJob']['result_json']),r,execution_stage=True);job_summary(stored_object(data['planJob']['result_json']),r,execution_stage=False)
    typed_equal(source_ui.sql(r['source_intent_id']),source['sql'])
    records=restore_io.read_operation(q,receipt,e/'restore-originals',create=create);assert records['claim'] is None and records['root']['claimed'] is True
    typed_equal(records['sourceRoot'],source['records']['root']);typed_equal(records['history'],source['records']['history']);typed_equal(records['data'],source['records']['data'])
    physical=None
    if live:
        physical=source_io.physical(Path(fixture['folder']),uuid.UUID(source_op).hex);typed_equal(physical,source['physical']);restore_io.physical_proof(physical,fixture['original'],records,receipt)
    return dict(sql=data,explicitCancelAudits=audits['n'],request=q,receipt=receipt,records=records,physical=physical)


def independent(repo,e,live):
    with (e/('independent-live.log' if live else 'independent-final.log')).open('x') as out:subprocess.run([sys.executable,str(repo/'deploy/validation/verify_container_recycle_restore_inflight_logout.py'),e.name]+(['--live'] if live else []),check=True,stdout=out,stderr=subprocess.STDOUT,timeout=90)


def main(revision):
    assert os.geteuid()==0 and re.fullmatch('[a-f0-9]{40}',revision);os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0';repo=Path(__file__).resolve().parents[2]
    git=lambda *a:subprocess.check_output(['git','-c','safe.directory='+str(repo),'-C',str(repo),*a])
    assert not git('status','--porcelain').strip();sha=git('rev-parse','HEAD').decode().strip();closure=source_closure(repo,sha,seeds=SOURCES)
    for n,h in closure.items():assert hashlib.sha256((repo/'deploy/validation'/n).read_bytes()).hexdigest()==h
    for n in ('whaledeck-privileged','whaledeck-container-recycle-restore-transport.py'):assert (Path('/usr/local/libexec')/n).read_bytes()==git('show',revision+':deploy/host/'+n)
    idle();before=baseline(revision);e=repo/'deploy/artifacts'/('container-recycle-restore-inflight-logout-'+uuid.uuid4().hex);e.mkdir(mode=0o700)
    def save(n,v):
        with (e/n).open('x') as out:json.dump(v,out)
    save('before.json',before);save('source.json',dict(revision=revision,validationRevision=sha,validationSourceSha256=closure));print(json.dumps(dict(restoreInflightLogout='started',evidence=e.name)),flush=True)
    held=None
    try:
        with owned.fixture() as f:
            f['retain_unsettled']=True;leaf=f['folder']/'recycle-rpc.bin';leaf.write_bytes(hashlib.shake_256(('inflight-independent-restore:'+f['identifier']).encode()).digest(1024**2+7));leaf.chmod(0o640);os.chown(leaf,1000,1000);os.setxattr(leaf,'user.recycle',b'logout must keep original trash')
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
                            result=cut.freeze_before_return(row,f['folder'],fixture['containerPid']);box['held']=result;return result
                        time.sleep(.01)
                    return None
                with (e/'browser.stdout').open('x') as out,(e/'browser.stderr').open('x') as err:
                    process=subprocess.Popen(['node',str(repo/'deploy/validation/validate_container_recycle_restore_inflight_logout.mjs'),revision],stdin=subprocess.PIPE,stdout=out,stderr=err,text=True,env={**os.environ,'WHALEDECK_URL':'http://127.0.0.1:8080','VALIDATION_USERNAME':username,'VALIDATION_PASSWORD':password})
                    try:
                        process.stdin.write(json.dumps(fixture));process.stdin.close();deadline=time.monotonic()+300
                        while process.poll() is None:
                            if source is None and (e/'source-completed.json').exists():
                                signal=json.loads((e/'source-completed.json').read_text());fixture['subject']=signal['subject'];source=source_ui.verify_case(signal['result'],fixture,e,create=True);(e/'originals').rename(e/'source-originals');save('source-proof.json',source);save('source-accepted.json',dict(id=signal['result']['id']))
                            if intent is None and (e/'armed-intent.json').exists():
                                armed=json.loads((e/'armed-intent.json').read_text());intent=armed['id'];assert source and armed['subject']==fixture['subject'];row=sql(intent)['row'];assert row['state']=='Ready' and row['execution_job_id'] is None;save('ready-sql.json',sql(intent));watch=pool.submit(observe,intent);save('observer-armed.json',dict(id=intent))
                            if watch is not None and watch.done() and held is None:
                                held=watch.result();assert held is not None,'EXACT_RESTORE_CUT_MISSED_NO_REPLAY';cutrow=sql(intent)['row'];assert cutrow['state']=='Applying' and cutrow['root_started_at_utc'] and cutrow['publish_barrier'];cut_binding(cutrow,held.record,held.proof,fixture)
                                began=time.monotonic();exit_future=pool.submit(observe_logout_exit,held,began,e,20)
                                # This is a current independent HDD/data read while the exact helper is stopped.
                                typed_equal(source_io.read_operation(source['request'],source['receipt'],e/'source-originals'),source['records']);typed_equal(source_io.physical(f['folder'],source['request']['operationId']),source['physical'])
                                save('cut-record.json',held.record);save('cut-proof.json',held.proof);save('cut-sql.json',sql(intent));save('cut-source-proof.json',dict(source=source['records'],physical=source['physical'],hddDataReread=True));save('cut-held.json',dict(id=intent,executionJobId=cutrow['execution_job_id']))
                            if exit_future is not None and exit_future.done() and not exited:
                                observation=exit_future.result();save('helper-exit.json',observation);exited=True
                            if (e/'logout-completed.json').exists() and not (e/'logout-accepted.json').exists():
                                result=json.loads((e/'logout-completed.json').read_text())['result'];proof=prove(result,fixture,source,e,create=True);save('live-proof.json',proof);save('logout-accepted.json',dict(id=result['id']))
                            assert time.monotonic()<deadline,'RESTORE_CANCEL_BROWSER_DEADLINE';time.sleep(.02)
                        assert process.returncode==0 and held is not None and exited,'RESTORE_INFLIGHT_LOGOUT_FAILED'
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
            op=source['request']['operationId'];p=json.loads((e/'live-proof.json').read_text());private=f['folder']/source_io.PRIVATE;retained=private/(op+'.trash')
            typed_equal(source_io.file_snapshot(retained),p['physical']['retained']);assert not os.path.lexists(leaf);retained.unlink();i=private.lstat();assert [i.st_dev,i.st_ino]==p['records']['sourceRoot']['privateIdentity'] and not private.is_symlink() and not list(private.iterdir());private.rmdir();f['retain_unsettled']=False
        after=baseline(revision);preserved(before,after,op,p['request']['operationId']);save('after.json',after);typed_equal(old_rows(fixture['containerId']),json.loads((e/'prior-business.json').read_text()));idle()
        save('verified.json',dict(realUiLogout=True,exactHelperExit=True,sourceRetainedUntilIndependentProof=True,productRestorePerformed=False,ownedFixtureRemoved=True,historiesRetained=True,ordinaryDenied=9,anonymousDenied=5,normalDualLogouts=2))
        independent(repo,e,False);print(json.dumps(dict(restoreInflightLogout='passed',evidence=e.name)),flush=True)
    except BaseException as error:
        import traceback
        save('failure.json',dict(type=type(error).__name__,frames=[dict(file=Path(t.filename).name,line=t.lineno) for t in traceback.extract_tb(error.__traceback__)]));raise


if __name__=='__main__':
    try:assert len(sys.argv)==2;main(sys.argv[1])
    except BaseException as error:print(json.dumps(dict(restoreInflightLogout='failed',type=type(error).__name__)),flush=True);raise SystemExit(1) from None
