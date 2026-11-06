#!/usr/bin/env python3
"""Fresh exact claim-index loss on a still-Applying return, Unknown and explicit ACK.

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

SOURCES=('validate_container_recycle_restore_unknown.py','validate_container_recycle_restore_unknown.mjs','verify_container_recycle_restore_unknown.py','test_container_recycle_restore_unknown.py','test_container_recycle_restore_claim_loss.py')


from validate_container_recycle_restore_postreturn_cancel import cut_binding
from restore_unknown_proof import facts,namespace
from restore_claim_loss_proof import isolate,resume,rebuilt,remove_quarantine,preserve,stable_unknown,require_ack,acknowledgement_audit
from validate_container_recycle_restore_postreturn_revocation import cleanup_returned_proof


import bound_container_recycle_restore_returned_cut as returned
from restore_returned_cut_io import read_pending


def browser_proof(b):
    assert b['actualUi'] and b['unknownHelperExitedBeforeLogout'] and b['subject']!=b['recoverySubject']
    assert b['registrations']==b['confirmations']==1 and b['cancellations']==0 and b['acknowledgements']==b['managementWrites']==1 and b['retryRequests']==[]
    assert b['differentAdminOldIdStatus']==404 and b['originalSessionReadStatus']==401 and b['hintsCleared'] and b['cancelledDecisionPreserved']
    assert len(b['logoutReceipts'])==2
    for r in b['logoutReceipts']:assert r['normalUiLogout'] and r['postStatus'] in (302,303) and r['providerNavigationObserved'] and r['bffStatus']==401 and r['identityStatus'] in (401,403) and r['finalLoginPage']


def prove(value,fixture,source,e,create=False,live=True):
    data=sql(value['id']);q,receipt=facts(value,data,fixture,source)
    typed_equal(source_ui.sql(data['row']['source_intent_id']),source['sql'])
    archive=e/('ack-originals' if value['state']=='Acknowledged' else 'unknown-originals');records=restore_io.read_operation(q,receipt,archive,create=create)
    if live:
        current=namespace(fixture,records)
    else:
        saved=json.loads((e/'live-proof.json').read_text());typed_equal(data,saved['sql']);typed_equal(records,saved['records']);current=None
    pending=json.loads((e/'cut-source-proof.json').read_text());assert pending['verification']['hddDataReread'] is True and pending['root']['state']=='Applying'
    for key in ('claim','sourceRoot','history','data'):typed_equal(records[key],pending[key])
    audits=query("SELECT json_build_object('n',count(*)) FROM audit_events WHERE job_id=:'id'::uuid AND action='containers.files.recycle-restore.cancel-requested';",id=data['row']['execution_job_id']);assert audits['n']==0
    loss=json.loads((e/'claim-loss.json').read_text());claim_rebuild=rebuilt(q,loss) if live else json.loads((e/'live-proof.json').read_text())['claimRebuild']
    audit=acknowledgement_audit(data['row']['execution_job_id']);ack=value['state']=='Acknowledged';assert audit['count']==(1 if ack else 0)
    if ack:
        assert audit['actors']==[data['row']['acknowledged_by']] and audit['entries']==[dict(result='Unknown',targetType='container',targetId=fixture['containerId'],tracePresent=True)]
    else:assert audit['actors']==audit['entries']==[]
    return dict(sql=data,request=q,receipt=receipt,records=records,physical=current,claimRebuild=claim_rebuild,acknowledgementAudit=audit,explicitCancelAudits=0)


def independent(repo,e,live):
    with (e/('independent-live.log' if live else 'independent-final.log')).open('x') as out:subprocess.run([sys.executable,str(repo/'deploy/validation/verify_container_recycle_restore_unknown.py'),e.name]+(['--live'] if live else []),check=True,stdout=out,stderr=subprocess.STDOUT,timeout=90)


def main(revision):
    assert os.geteuid()==0 and re.fullmatch('[a-f0-9]{40}',revision);os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0';repo=Path(__file__).resolve().parents[2]
    git=lambda *a:subprocess.check_output(['git','-c','safe.directory='+str(repo),'-C',str(repo),*a])
    assert not git('status','--porcelain').strip();sha=git('rev-parse','HEAD').decode().strip();closure=source_closure(repo,sha,seeds=SOURCES)
    for n,h in closure.items():assert hashlib.sha256((repo/'deploy/validation'/n).read_bytes()).hexdigest()==h
    for n in ('whaledeck-privileged','whaledeck-container-recycle-restore-transport.py'):assert (Path('/usr/local/libexec')/n).read_bytes()==git('show',revision+':deploy/host/'+n)
    idle();before=baseline(revision);e=repo/'deploy/artifacts'/('container-recycle-restore-unknown-'+uuid.uuid4().hex);e.mkdir(mode=0o700)
    def save(n,v):
        with (e/n).open('x') as out:json.dump(v,out)
    save('before.json',before);save('source.json',dict(revision=revision,validationRevision=sha,validationSourceSha256=closure));print(json.dumps(dict(restoreUnknown='started',evidence=e.name)),flush=True)
    held=None
    try:
        with owned.fixture() as f:
            f['retain_unsettled']=True;leaf=f['folder']/'recycle-rpc.bin';leaf.write_bytes(hashlib.shake_256(('inflight-independent-restore:'+f['identifier']).encode()).digest(1024**2+7));leaf.chmod(0o640);os.chown(leaf,1000,1000);os.setxattr(leaf,'user.recycle',b'claim loss before final restore attribution')
            c=json.loads(owned.common.command(['docker','inspect',f['identifier']]))[0];header=dict(containerId=f['identifier'],instanceId=str(c['State']['Pid'])+':'+c['State']['StartedAt']);reader=load('/usr/local/libexec/whaledeck-container-files.py','restore_logout_reader')
            fixture=dict(revision=revision,containerId=f['identifier'],containerName=c['Name'].lstrip('/'),instanceId=header['instanceId'],containerPid=c['State']['Pid'],rootIdentity=list(cut.identity(Path('/proc')/str(c['State']['Pid'])/'root')),header=header,folder=str(f['folder']),evidence=str(e),original=source_io.file_snapshot(leaf),sourceVersion=reader.version(header,'work/recycle-rpc.bin',leaf.stat()))
            save('fixture-initial.json',fixture);save('prior-business.json',old_rows(f['identifier']))
            with temporary_user(True) as (username,password),temporary_user(True) as (recovery_username,recovery_password):
                source=None;intent=None;watch=None;exit_future=None;exited=False;unknown=None;box={};abort=threading.Event();pool=ThreadPoolExecutor(max_workers=2)
                def observe(identifier):
                    deadline=time.monotonic()+100
                    while time.monotonic()<deadline and not abort.is_set():
                        row=sql(identifier)['row']
                        if row['execution_job_id']:
                            result=returned.freeze_after_return(row,f['folder'],fixture['containerPid']);box['held']=result;return result
                        time.sleep(.01)
                    return None
                with (e/'browser.stdout').open('x') as out,(e/'browser.stderr').open('x') as err:
                    process=subprocess.Popen(['node',str(repo/'deploy/validation/validate_container_recycle_restore_unknown.mjs'),revision],stdin=subprocess.PIPE,stdout=out,stderr=err,text=True,env={**os.environ,'WHALEDECK_URL':'http://127.0.0.1:8080','VALIDATION_USERNAME':username,'VALIDATION_PASSWORD':password,'RECOVERY_USERNAME':recovery_username,'RECOVERY_PASSWORD':recovery_password})
                    try:
                        process.stdin.write(json.dumps(fixture));process.stdin.close();deadline=time.monotonic()+300
                        while process.poll() is None:
                            if source is None and (e/'source-completed.json').exists():
                                signal=json.loads((e/'source-completed.json').read_text());fixture['subject']=signal['subject'];source=source_ui.verify_case(signal['result'],fixture,e,create=True);(e/'originals').rename(e/'source-originals');save('source-proof.json',source);save('source-accepted.json',dict(id=signal['result']['id']))
                            if intent is None and (e/'armed-intent.json').exists():
                                armed=json.loads((e/'armed-intent.json').read_text());intent=armed['id'];assert source and armed['subject']==fixture['subject'];row=sql(intent)['row'];assert row['state']=='Ready' and row['execution_job_id'] is None;save('ready-sql.json',sql(intent));watch=pool.submit(observe,intent);save('observer-armed.json',dict(id=intent))
                            if watch is not None and watch.done() and held is None:
                                held=watch.result();assert held is not None,'EXACT_RESTORE_CUT_MISSED_NO_REPLAY';cutrow=sql(intent)['row'];assert cutrow['state']=='Applying' and cutrow['root_started_at_utc'] and cutrow['publish_barrier'];cut_binding(cutrow,held.record,held.proof,fixture)
                                # This is a current independent HDD/data read while the exact helper is stopped.
                                request=dict(operationId=uuid.UUID(cutrow['execution_job_id']).hex,owner=cutrow['owner_hash'],plan=managed_plan(stored_object(cutrow['plan_json'])));pending=read_pending(request,e/'cut-originals',create=True);typed_equal(pending['root'],held.record)
                                save('cut-record.json',held.record);save('cut-proof.json',held.proof);save('cut-sql.json',sql(intent));save('cut-source-proof.json',pending)
                                began=time.monotonic();exit_future=pool.submit(held.wait_exit_seconds,began,20)
                                loss=isolate(f,fixture,held,cutrow,pending,e);resume(fixture,held,cutrow,pending,loss,e)
                                save('cut-held.json',dict(id=intent,executionJobId=cutrow['execution_job_id']))
                            if exit_future is not None and exit_future.done() and not exited:
                                elapsed=exit_future.result();assert elapsed is not None,'ORIGINAL_HELPER_EXIT_NOT_OBSERVED'
                                save('helper-exit.json',dict(elapsedSeconds=elapsed,observedWithOriginalPidfd=True,measurementBeginsBeforeOwnedClaimMove=True,controlledOriginalResumed=True));exited=True
                            if exited and not (e/'unknown-ready.json').exists():
                                current=sql(intent)['row'];assert current['state'] in ('Pending','Applying','Unknown')
                                if current['state']=='Unknown' and current['lease_token'] is None:save('unknown-ready.json',dict(id=intent,executionJobId=cutrow['execution_job_id']))
                            if (e/'unknown-value.json').exists() and unknown is None:
                                value=json.loads((e/'unknown-value.json').read_text());unknown=prove(value,fixture,source,e,create=True);save('unknown-proof.json',unknown);save('unknown-proved.json',dict(id=intent))
                            if (e/'recovery-canceled.json').exists() and not (e/'acknowledge.json').exists():
                                decision=json.loads((e/'recovery-canceled.json').read_text());assert decision['id']==intent and decision['subject']!=fixture['subject'] and decision['acknowledgements']==0
                                declined=prove(json.loads((e/'unknown-value.json').read_text()),fixture,source,e);stable_unknown(unknown,declined);save('canceled-decision-proof.json',declined);save('acknowledge.json',dict(id=intent))
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
                final=prove(b['result'],fixture,source,e,create=True);require_ack(unknown,final,b['recoverySubject']);save('live-proof.json',final)
                save('fixture.json',fixture);denied(fixture,b['result']);independent(repo,e,True)
            op=source['request']['operationId'];p=json.loads((e/'live-proof.json').read_text());private=f['folder']/source_io.PRIVATE
            cleanup_returned_proof(source_io.file_snapshot(leaf),fixture['original'],p)
            i=private.lstat();assert [i.st_dev,i.st_ino]==p['records']['sourceRoot']['privateIdentity'] and not private.is_symlink() and not list(private.iterdir()) and i.st_uid==i.st_gid==0 and stat.S_IMODE(i.st_mode)==0o700
            remove_quarantine(p['request'],json.loads((e/'claim-loss.json').read_text()),p['claimRebuild']);leaf.unlink();private.rmdir();f['retain_unsettled']=False
        after=baseline(revision);preserve(before,after,op,p['request']['operationId']);save('after.json',after);typed_equal(old_rows(fixture['containerId']),json.loads((e/'prior-business.json').read_text()));idle()
        save('verified.json',dict(realUiUnknown=True,exactHelperExit=True,returnedOriginalPreservedUntilIndependentProof=True,productAttributionUnconfirmed=True,onlyExplicitDifferentAdminAck=True,ownedFixtureRemoved=True,historiesRetained=True,ordinaryDenied=9,anonymousDenied=5,normalDualLogouts=2))
        independent(repo,e,False);print(json.dumps(dict(restoreUnknown='passed',evidence=e.name)),flush=True)
    except BaseException as error:
        import traceback
        save('failure.json',dict(type=type(error).__name__,frames=[dict(file=Path(t.filename).name,line=t.lineno) for t in traceback.extract_tb(error.__traceback__)]));raise


if __name__=='__main__':
    try:assert len(sys.argv)==2;main(sys.argv[1])
    except BaseException as error:print(json.dumps(dict(restoreUnknown='failed',type=type(error).__name__)),flush=True);raise SystemExit(1) from None
