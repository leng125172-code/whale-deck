#!/usr/bin/env python3
"""Real post-unlink generated-identity revocation, Unknown and explicit different-admin decision."""
import hashlib,json,os,re,subprocess,sys,time,uuid
from pathlib import Path
from concurrent.futures import ThreadPoolExecutor
from owned_authentik_revocation import OwnedAuthentikRevocation
from validate_container_recycle_revocation import authority_proof
from datetime import datetime,timezone,timedelta
import select
from bound_container_recycle_purge_unlinked_cut import freeze_after_unlink
from bound_container_recycle_purge_cut import private_record,SOURCE_STATE
from validate_container_recycle_purge_inflight_cancel import settle_held
from validate_container_recycle_purge_agent import baseline,HOSTS
from validate_container_recycle_purge_browser import sql,idle,old_rows,hdd_observation,prove_ready,denied,stop_browser
from validate_container_file_staging import load
from validate_container_upload_bff import temporary_user
from acl_source_apply_missing_proof import source_closure
from acl_history_browser_proof import typed_equal
from recycle_purge_unlinked_logout_proof import prove,require_ack,preserved
import validate_container_file_publishing as owned
import validate_container_recycle_browser as source_ui
import container_recycle_agent_io as source_io

SOURCES=('bound_container_recycle_purge_unlinked_cut.py','recycle_purge_unlinked_logout_proof.py','validate_container_recycle_purge_unlinked_revocation.py','validate_container_recycle_purge_unlinked_revocation.mjs','verify_container_recycle_purge_unlinked_revocation.py','test_container_recycle_purge_unlinked_revocation.py')


def browser_proof(b,f):
    assert b['actualUi'] and b['kind']==f['kind'] and b['subject']==f['subject'] and b['recoverySubject']==f['recoverySubject'] and b['recoverySubject']!=b['subject']
    assert b['registrations']==b['confirmations']==b['acknowledgements']==b['managementWrites']==1 and b['cancellations']==b['ownerLogoutPosts']==0
    assert b['differentAdminOldIdStatus']==404 and b['hintsCleared'] and b['privatePanelsAbsent'] and b['cancelledDecisionPreserved'] and len(b['jobProofs'])==2
    if b['kind']=='group-removal':assert b['originalIntentStatus']==403 and b['currentIdentityStatus']==200 and b['currentSubject']==b['subject'] and b['currentlyAdministrator'] is False and b['renderedRevocationPage']=='forbidden'
    else:assert b['originalIntentStatus'] in (401,403) and b['currentIdentityStatus'] in (401,403) and b['renderedRevocationPage']=='login'
    r=b['recoveryLogout'];assert r['normalUiLogout'] and r['postStatus'] in (302,303) and r['providerNavigationObserved'] and r['bffStatus']==401 and r['identityStatus'] in (401,403) and r['finalLoginPage']


def independent(repo,e,live):
    with (e/('independent-live.log' if live else 'independent-final.log')).open('x') as out:
        subprocess.run([sys.executable,str(repo/'deploy/validation/verify_container_recycle_purge_unlinked_revocation.py'),e.name]+(['--live'] if live else []),check=True,stdout=out,stderr=subprocess.STDOUT,timeout=90)


def main(revision,kind):
    assert kind in ('group-removal','account-disable')
    assert os.geteuid()==0 and re.fullmatch('[a-f0-9]{40}',revision);os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0'
    repo=Path(__file__).resolve().parents[2];git=lambda *a:subprocess.check_output(['git','-c','safe.directory='+str(repo),'-C',str(repo),*a])
    assert not git('status','--porcelain').strip();sha=git('rev-parse','HEAD').decode().strip();closure=source_closure(repo,sha,seeds=SOURCES)
    for n,h in closure.items():assert hashlib.sha256((repo/'deploy/validation'/n).read_bytes()).hexdigest()==h
    for n in HOSTS:assert (Path('/usr/local/libexec')/n).read_bytes()==git('show',revision+':deploy/host/'+n)
    idle();before=baseline(revision);e=repo/'deploy/artifacts'/('container-recycle-purge-unlinked-revocation-'+uuid.uuid4().hex);e.mkdir(mode=0o700)
    def save(n,v):
        with (e/n).open('x') as out:json.dump(v,out)
    frozen=e/'source';frozen.mkdir(mode=0o700)
    for n,h in closure.items():
        raw=git('show',sha+':deploy/validation/'+n);assert hashlib.sha256(raw).hexdigest()==h;(frozen/n).write_bytes(raw)
    save('before.json',before);save('source.json',dict(revision=revision,validationRevision=sha,validationSourceSha256=closure));print(json.dumps(dict(purgeUnlinkedRevocation='started',evidence=e.name)),flush=True)
    try:
        with owned.fixture() as f:
            f['retain_unsettled']=True;leaf=f['folder']/'recycle-rpc.bin';leaf.write_bytes(hashlib.shake_256(('purge-post-unlink:'+f['identifier']).encode()).digest(1024**2+7));leaf.chmod(0o640);os.chown(leaf,1000,1000);os.setxattr(leaf,'user.recycle',b'unlinked revocation original')
            c=json.loads(owned.common.command(['docker','inspect',f['identifier']]))[0];header=dict(containerId=f['identifier'],instanceId=str(c['State']['Pid'])+':'+c['State']['StartedAt']);reader=load('/usr/local/libexec/whaledeck-container-files.py','unlinked_reader')
            fixture=dict(kind=kind,revision=revision,containerId=f['identifier'],containerName=c['Name'].lstrip('/'),instanceId=header['instanceId'],header=header,containerPid=c['State']['Pid'],folder=str(f['folder']),evidence=str(e),original=source_io.file_snapshot(leaf),sourceVersion=reader.version(header,'work/recycle-rpc.bin',leaf.stat()))
            save('fixture-initial.json',fixture);save('prior-business.json',old_rows(f['identifier']))
            with temporary_user(True) as (username,password),temporary_user(True) as (recovery_username,recovery_password):
                with (e/'browser.stdout').open('x') as out,(e/'browser.stderr').open('x') as err:
                    process=subprocess.Popen(['node',str(repo/'deploy/validation/validate_container_recycle_purge_unlinked_revocation.mjs'),revision],stdin=subprocess.PIPE,stdout=out,stderr=err,text=True,env={**os.environ,'WHALEDECK_URL':'http://127.0.0.1:8080','VALIDATION_USERNAME':username,'VALIDATION_PASSWORD':password,'RECOVERY_USERNAME':recovery_username,'RECOVERY_PASSWORD':recovery_password})
                    source=armed=unknown=ack=None;held=None;exited=False;pool=ThreadPoolExecutor(max_workers=1);future=None
                    try:
                        process.stdin.write(json.dumps(fixture));process.stdin.close();deadline=time.monotonic()+620
                        while process.poll() is None:
                            for phase in ('source','armed','unknown','decision','ack'):
                                marker=e/(phase+'-completed.json');accepted=e/(phase+'-accepted.json')
                                if not marker.exists() or accepted.exists():continue
                                signal=json.loads(marker.read_text())
                                if 'subject' in fixture:assert signal['subject']==fixture['subject']
                                else:fixture['subject']=signal['subject']
                                dto=signal['result']
                                if phase=='source':
                                    source=source_ui.verify_case(dto,fixture,e,create=True);(e/'originals').rename(e/'source-originals');source['hdd']=hdd_observation(source['request']['operationId']);typed_equal(source['hdd']['history'],source['records']['history']);typed_equal(source['hdd']['data'],source['records']['data']);save('source-proof.json',source)
                                elif phase=='armed':
                                    assert source and armed is None;armed=prove_ready(dto,fixture,source,e);save('armed-proof.json',armed)
                                elif phase=='unknown':
                                    assert held and source and unknown is None and signal['recoverySubject']!=fixture['subject'];fixture['recoverySubject']=signal['recoverySubject'];unknown=prove(dto,fixture,source,e,create=True);typed_equal(armed['sql']['planJob'],unknown['sql']['planJob']);save('unknown-proof.json',unknown)
                                elif phase=='decision':
                                    assert unknown and signal['recoverySubject']==fixture['recoverySubject'];current=prove(dto,fixture,source,e);typed_equal(current,unknown);save('declined-proof.json',current)
                                else:
                                    assert unknown and signal['recoverySubject']==fixture['recoverySubject'];ack=prove(dto,fixture,source,e,acknowledged=True);require_ack(unknown,ack,fixture['recoverySubject']);save('ack-proof.json',ack)
                                save(phase+'-accepted.json',dict(id=dto['id']))
                            if armed and held is None:
                                row=sql(armed['sql']['row']['id'])['row']
                                if row['execution_job_id']:
                                    diagnostics={}
                                    try:held=freeze_after_unlink(row,f['folder'],fixture['containerPid'],diagnostics)
                                    finally:save('cut-observation.json',diagnostics)
                                    assert held is not None,'EXACT_POST_UNLINK_CUT_MISSED_NO_REPLAY'
                                    current=sql(row['id']);r=current['row'];assert r['state']=='Applying' and r['reservation_json'] and r['apply_started_at_utc'] and r['publish_barrier'] and r['receipt_json'] is None
                                    typed_equal(hdd_observation(source['request']['operationId']),source['hdd']);save('cut-sql.json',current);save('cut-record.json',held.record);save('cut-proof.json',held.proof)
                                    op=source['request']['operationId'];save('cut-source-root.json',private_record(SOURCE_STATE/(op+'.json')));save('cut-source-marker.json',private_record(SOURCE_STATE/(op+'.purge.json')))
                                    save('cut-held.json',dict(id=row['id'],executionJobId=row['execution_job_id']))
                                    with OwnedAuthentikRevocation(username,fixture['subject']) as identity:
                                        assert not select.select([held.descriptor],[],[],0)[0],'HELPER_EXIT_BEFORE_AUTHORITY_CHANGE'
                                        expiry=r['execution_expires_at_utc'];assert datetime.fromisoformat(expiry)>datetime.now(timezone.utc)+timedelta(minutes=10)
                                        began=time.monotonic();future=pool.submit(held.wait_exit_seconds,began,20);authority=identity.change(kind);authority['executionExpiresAtUtc']=expiry;authority_proof(authority,fixture)
                                        assert not select.select([held.descriptor],[],[],0)[0],'HELPER_EXIT_BEFORE_AUTHORITY_CONFIRMATION';save('authority-change.json',authority)
                                    elapsed=future.result(timeout=21);assert elapsed is not None,'PRODUCT_HELPER_EXIT_NOT_OBSERVED'
                                    save('helper-exit.json',dict(elapsedSeconds=elapsed,observedWithOriginalPidfd=True,measurementBeginsBeforeAuthorityMutation=True,originalAliveBeforeAndAfterAuthorityConfirmation=True));exited=True
                                    until=time.monotonic()+40
                                    while True:
                                        current=sql(row['id'])['row']
                                        if current['state']=='Unknown':break
                                        assert current['state'] in ('Pending','Reserving','Applying') and time.monotonic()<until;time.sleep(.1)
                                    save('revocation-settled.json',dict(id=row['id'],state='Unknown',executionJobId=row['execution_job_id']))
                            assert time.monotonic()<deadline;time.sleep(.05)
                        assert process.returncode==0 and held and exited and source and unknown and ack and (e/'browser.stderr').read_bytes()==b''
                    finally:
                        try:settle_held(held)
                        finally:stop_browser(process);pool.shutdown(wait=True,cancel_futures=True)
                b=json.loads((e/'browser.json').read_text());browser_proof(b,fixture);save('fixture.json',fixture);denied(fixture,b['final']);independent(repo,e,True)
            # All three source names are absent. Never recreate the deleted
            # inode from a proc FD or claim product archive recovery occurred.
            private=f['folder']/source_io.PRIVATE;info=private.lstat();assert [info.st_dev,info.st_ino]==ack['request']['plan']['privateIdentity'] and not private.is_symlink() and not list(private.iterdir());private.rmdir();f['retain_unsettled']=False
        after=baseline(revision);preserved(before,after,source['request']['operationId'],ack['request']['operationId']);typed_equal(old_rows(fixture['containerId']),json.loads((e/'prior-business.json').read_text()));typed_equal(hdd_observation(source['request']['operationId']),source['hdd']);idle();save('after.json',after);save('hdd-after-cleanup.json',hdd_observation(source['request']['operationId']))
        save('verified.json',dict(actualUi=True,postUnlinkCut=True,fiveFdBound=True,rootUnknownPreserved=True,purgeApplied=None,explicitDifferentAdminAcknowledgement=True,realAuthorityRevocation=True,ownerLogoutPosts=0,explicitCancelAbsent=True,fixtureRemoved=True,hddRereadAfterCleanup=True,archiveRestorePerformed=False,ordinaryDenied=9,anonymousDenied=5));independent(repo,e,False);print(json.dumps(dict(purgeUnlinkedRevocation='passed',evidence=e.name)),flush=True)
    except BaseException as error:
        import traceback
        save('failure.json',dict(type=type(error).__name__,frames=[dict(file=Path(t.filename).name,line=t.lineno,function=t.name) for t in traceback.extract_tb(error.__traceback__)]));raise


if __name__=='__main__':
    try:assert len(sys.argv)==3;main(sys.argv[1],sys.argv[2])
    except BaseException as error:print(json.dumps(dict(purgeUnlinkedRevocation='failed',type=type(error).__name__)),flush=True);raise SystemExit(1) from None
