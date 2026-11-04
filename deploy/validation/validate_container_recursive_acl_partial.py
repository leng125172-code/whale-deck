#!/usr/bin/env python3
"""Actual Partial cancellation and a different administrator's visible ACK."""
from concurrent.futures import ThreadPoolExecutor
from contextlib import ExitStack
import hashlib,json,os,re,subprocess,sys,time,uuid
import datetime as dt
from pathlib import Path

from bound_container_recursive_acl_partial_cut import freeze_partial_acl
from validate_container_recursive_acl_faults import owned,Worker,command,preflight,host_identity,no_active_work,load,temporary_user,preservation_snapshot,inventory,remove_proved,NAMES,PATHS,write_signal,wait_signal,snapshot
from recursive_acl_partial_proof import verify_records,require_ack,require_case
from acl_source_apply_missing_proof import source_closure
from validate_container_upload_recovery_agent import trusted
from owned_authentik_revocation import OwnedAuthentikRevocation
from validate_container_recursive_permission_faults import require_logout_authority
from validate_container_recursive_permission_faults import require_authority_change,restore_fault_services
from recursive_acl_restart_proof import RESTART_KINDS,restart
from recursive_acl_partial_restart_proof import natural_partial_lease,verify_partial_restart
from recursive_acl_partial_unknown_proof import capture_completed_child,remove_unknown_target,verify_unknown_target,UNKNOWN_KINDS
from recursive_acl_instance_proof import prepare_instance,restart_instance,attest_instance_after_ack
from recursive_acl_cross_directory_proof import selected_waiting,waiting_baseline,history_snapshot,observe_waiting,finish_waiting,clean_waiting

PHASE='startup'
SOURCES=('validate_container_recursive_acl_partial.py','validate_container_recursive_acl_partial.mjs','verify_container_recursive_acl_partial.py','recursive_acl_partial_proof.py','test_recursive_acl_partial_workflow_probe_scope.py')


def selected_case(arguments):
    assert tuple(arguments) in ((),('logout',),('group-removal',),('account-disable',),('worker-crash',),('agent-restart',),('target-removed',),('instance-changed',)), 'UNKNOWN_PARTIAL_ACL_SCENARIO'
    return arguments[0] if arguments else 'cancel'


def create_tree(root,content):
    root.mkdir(mode=0o750);(root/'sub').mkdir(mode=0o700)
    owners={'':(2100,3100),'one.bin':(2200,3200),'sub':(2101,3101),'sub/中文.bin':(2201,3201)}
    for relative,entry_kind in PATHS.items():
        path=root/relative
        if entry_kind=='File':path.write_bytes(content)
        os.setxattr(path,'user.whaledeck-recursive',b'preserve ordinary attribute');os.chown(path,*owners[relative]);path.chmod(0o750 if relative=='' else 0o700 if entry_kind=='Directory' else 0o640 if relative=='one.bin' else 0o600)
    for relative in PATHS:os.utime(root/relative,ns=(1700000000123456789,1700000000123456789))


def wait_state(identifier,state):
    deadline=time.monotonic()+100
    while True:
        value=snapshot(identifier)
        if value['row']['state']==state:return value
        assert value['row']['state'] in ('Pending','Applying'), 'PARTIAL_UNEXPECTED_TERMINAL'
        assert time.monotonic()<deadline,'PARTIAL_RESULT_DEADLINE';time.sleep(.5)


def browser(repo,revision,role,baseline,credentials):
    environment={**os.environ};environment.pop('VALIDATION_USERNAME',None);environment.pop('VALIDATION_PASSWORD',None)
    p=Path(baseline['evidence'])
    with (p/(role+'.stdout')).open('xb') as out,(p/(role+'.stderr')).open('xb') as err:
        process=subprocess.Popen(['node',str(repo/'deploy/validation/validate_container_recursive_acl_partial.mjs'),revision,role],stdin=subprocess.PIPE,stdout=out,stderr=err,env=environment)
        process.stdin.write(json.dumps(dict(fixture=baseline,credentials=dict(zip(('username','password'),credentials)))).encode());process.stdin.close();process.stdin=None
    return process


def stop(process):
    if process is not None and process.poll() is None:
        process.terminate()
        try:process.wait(timeout=15)
        except subprocess.TimeoutExpired:process.kill();process.wait(timeout=10)


def main():
    global PHASE
    assert os.geteuid()==0 and len(sys.argv) in (2,3,4);kind=selected_case(sys.argv[2:3]);cross_directory=selected_waiting(sys.argv[2:]);os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0'
    revision=sys.argv[1];assert re.fullmatch('[a-f0-9]{40}',revision);repo=Path(__file__).resolve().parents[2]
    assert not subprocess.check_output(['git','-C',str(repo),'status','--porcelain']).strip(),'FREEZE_VALIDATION_SOURCE_FIRST'
    source=subprocess.check_output(['git','-C',str(repo),'rev-parse','HEAD'],text=True).strip();hashes=source_closure(repo,source,seeds=SOURCES)
    assert all(hashlib.sha256((repo/'deploy/validation'/n).read_bytes()).hexdigest()==sha for n,sha in hashes.items())
    no_active_work();preflight();hosts=host_identity(revision);core=owned.common.core_snapshot();installed={}
    for name in (*NAMES,'whaledeck-container-backups.py','whaledeck-container-acl-transport.py','whaledeck-container-recursive-acl-transport.py','whaledeck-privileged'):
        path=Path('/usr/local/libexec')/name;trusted(path);raw=subprocess.check_output(['git','-C',str(repo),'show',revision+':deploy/host/'+name]);assert path.read_bytes()==raw;installed[name]=hashlib.sha256(raw).hexdigest()
    evidence=repo/'deploy/artifacts'/('container-recursive-acl-partial-'+uuid.uuid4().hex);evidence.mkdir(mode=0o700)
    print(json.dumps(dict(recursiveAclPartial='started',evidence=str(evidence))),flush=True)
    reader=load('/usr/local/libexec/whaledeck-container-files.py','partial_acl_reader');files=reader.trusted_module('whaledeck-files.py','partial_acl_files')
    storage=load('/usr/local/libexec/whaledeck-file-backups.py','partial_acl_hdd').FileBackups(files);acl=load('/usr/local/libexec/whaledeck-container-acl.py','partial_acl_acl')
    old=preservation_snapshot(storage);scope=dict(revision=revision,validationSourceRevision=source,validationSourceSha256=hashes,sourceHashes=installed,hostIdentity=hosts,coreBefore=core,preservedFiles=old,kind=kind,crossDirectory=cross_directory)
    (evidence/'scope.json').write_text(json.dumps(scope));worker=Worker();owner_process=recovery_process=held=None;proved=False;attempt=dict(agentRestartAttempted=False);hosts_after=hosts
    unknown=kind in UNKNOWN_KINDS;expected_state='Unknown' if unknown else 'Partial'
    with owned.fixture(restartable_core=('whaledeck-worker',),removable=(kind=='target-removed')) as fixture,ExitStack() as users:
        other=users.enter_context(owned.fixture(restartable_core=('whaledeck-worker',))) if cross_directory else None
        waiting=waiting_baseline(other,fixture) if cross_directory else None
        if cross_directory:
            (evidence/'waiting-baseline.json').write_text(json.dumps(waiting));(evidence/'cross-directory-scope.json').write_text(json.dumps(dict(oldDirectoryHistory=history_snapshot())))
        owner=users.enter_context(temporary_user(True));recovery=users.enter_context(temporary_user(True))
        root=fixture['folder']/'recursive-tree'
        content=hashlib.shake_256(('recursive-acl-partial:'+fixture['identifier']).encode()).digest(2*1024**2+7)
        create_tree(root,content)
        observed=json.loads(command('docker','inspect',fixture['identifier']))[0];current=lambda:inventory(root,reader,content,acl);new_inventory=lambda pid:inventory(Path('/proc')/str(pid)/'root/work/recursive-tree',reader,content,acl);original=current()
        baseline=dict(revision=revision,validationSourceRevision=source,containerId=fixture['identifier'],containerName=observed['Name'].lstrip('/'),containerPid=observed['State']['Pid'],instanceId=str(observed['State']['Pid'])+':'+observed['State']['StartedAt'],source=str(fixture['folder']),original=original,evidence=str(evidence),bytesPerFile=len(content),sha256=hashlib.sha256(content).hexdigest().upper(),kind=kind,crossDirectory=cross_directory,waitingDirectory=waiting)
        with storage.target(create_namespace=False,read_only=True) as (configuration,fd):storage.attest(configuration,fd,read_only=True);baseline.update(sourceDevice=root.stat().st_dev,hddDevice=os.fstat(fd).st_dev)
        (evidence/'baseline.json').write_text(json.dumps(baseline))
        try:
            PHASE='actual-owner-preflight';owner_process=browser(repo,revision,'owner',baseline,owner);ready=wait_signal(evidence,'ready.json',owner_process);identifier=ready['id']
            assert snapshot(identifier)['row']['state']=='Ready';preflight([identifier]);worker.stop();write_signal(evidence,'confirm.json',dict(workerStopped=True))
            confirmed=wait_signal(evidence,'confirmed.json',owner_process,30);row=snapshot(identifier)['row'];assert row['state']=='Pending' and row['execution_job_id']==confirmed['executionJobId'] and row['root_started_at_utc'] is None
            PHASE='actual-partial-cut'
            with ThreadPoolExecutor(max_workers=1) as pool:
                watch=pool.submit(freeze_partial_acl,row,fixture['folder'],baseline['containerPid'],original,current);worker.start(wait_ready=False);held=watch.result(timeout=28)
            assert held is not None,'ACTUAL_PARTIAL_ACL_CUT_MISSED_NOT_COUNTED'
            cut_physical=current();(evidence/'cut-root.json').write_text(json.dumps(held.record));(evidence/'cut-physical.json').write_text(json.dumps(cut_physical));(evidence/'bound-helper.json').write_text(json.dumps(dict(pid=held.pid,unit=held.cgroup,proof=held.proof)))
            if unknown:capture_completed_child(row,evidence)
            PHASE='actual-visible-partial-'+kind
            revoked=kind in ('group-removal','account-disable');identity=None
            if unknown:
                with OwnedAuthentikRevocation(owner[0],ready['subject']) as identity:
                    assert dt.datetime.fromisoformat(ready['permissionExpiresAtUtc'])>dt.datetime.now(dt.timezone.utc)+dt.timedelta(hours=1)
                    write_signal(evidence,'action.json',dict(partialCut=True));action=wait_signal(evidence,'action-done.json',owner_process,40)
                    assert action['id']==identifier and action['subject']==ready['subject']
                    if kind=='target-removed':remove_unknown_target(baseline,fixture,held,current,cut_physical,evidence)
                    else:prepared=prepare_instance(baseline,fixture,held,snapshot(identifier)['row'],current,cut_physical)
                    began=time.monotonic()
                    with ThreadPoolExecutor(max_workers=1) as pool:
                        exiting=pool.submit(held.wait_exit_seconds,began,15)
                        if kind=='instance-changed':restart_instance(baseline,fixture,held,prepared,current,cut_physical,new_inventory,evidence)
                        write_signal(evidence,'logout.json',dict(onlyExactOwnedContainerRemoved=kind=='target-removed',onlyExactOwnedInstanceRestarted=kind=='instance-changed'))
                        wait_signal(evidence,'logout-done.json',owner_process,40);elapsed=exiting.result(timeout=16)
                    response=identity.client.get(identity.url,timeout=5);assert response.status_code==200 and identity.changed is False
                    authority=dict(kind='logout',subject=ready['subject'],before=identity.before,after=identity.snapshot(response.json()),noIdentityMutation=True,confirmedAtUtc=dt.datetime.now(dt.timezone.utc).isoformat(),permissionExpiresAtUtc=ready['permissionExpiresAtUtc'])
                    require_logout_authority(authority,ready['subject']);assert dt.datetime.fromisoformat(authority['permissionExpiresAtUtc'])>dt.datetime.fromisoformat(authority['confirmedAtUtc'])+dt.timedelta(hours=1)
                    (evidence/'logout-authority.json').write_text(json.dumps(authority))
            elif kind in RESTART_KINDS:
                write_signal(evidence,'action.json',dict(partialCut=True));action=wait_signal(evidence,'action-done.json',owner_process,40)
                assert action['id']==identifier and action['subject']==ready['subject'] and current()==cut_physical
                elapsed,service,restart_row=restart(kind,held,worker,snapshot(identifier)['row'],revision,evidence,attempt)
                (evidence/'service-restart.json').write_text(json.dumps(service));hosts_after=service['hostsAfter']
            else:
                with ExitStack() as checks:
                    identity=checks.enter_context(OwnedAuthentikRevocation(owner[0],ready['subject'])) if kind=='logout' or revoked else None
                    if identity:assert dt.datetime.fromisoformat(ready['permissionExpiresAtUtc'])>dt.datetime.now(dt.timezone.utc)+dt.timedelta(hours=1)
                    if revoked:
                        write_signal(evidence,'action.json',dict(partialCut=True));wait_signal(evidence,'action-done.json',owner_process,40)
                    began=time.monotonic()
                    with ThreadPoolExecutor(max_workers=1) as pool:
                        exiting=pool.submit(held.wait_exit_seconds,began,15)
                        if revoked:
                            authority=identity.change(kind);authority['permissionExpiresAtUtc']=ready['permissionExpiresAtUtc'];require_authority_change(authority,ready,kind)
                            (evidence/'authority-change.json').write_text(json.dumps(authority))
                        else:
                            write_signal(evidence,'action.json',dict(partialCut=True));wait_signal(evidence,'action-done.json',owner_process,40)
                        elapsed=exiting.result(timeout=16)
                    if identity and kind=='logout':
                        response=identity.client.get(identity.url,timeout=5);assert response.status_code==200 and identity.changed is False
                        authority=dict(kind='logout',subject=ready['subject'],before=identity.before,after=identity.snapshot(response.json()),noIdentityMutation=True,confirmedAtUtc=dt.datetime.now(dt.timezone.utc).isoformat(),permissionExpiresAtUtc=ready['permissionExpiresAtUtc'])
                        require_logout_authority(authority,ready['subject']);assert dt.datetime.fromisoformat(authority['permissionExpiresAtUtc'])>dt.datetime.fromisoformat(authority['confirmedAtUtc'])+dt.timedelta(hours=1)
                        (evidence/'logout-authority.json').write_text(json.dumps(authority))
            assert elapsed is not None and elapsed<15,'PRODUCT_DID_NOT_STOP_PARTIAL_ACL_HELPER'
            (evidence/'product-helper-exit.json').write_text(json.dumps(dict(productStoppedHelperSeconds=elapsed,validatorKilledHelper=False)))
            held.release();held=None
            if kind=='worker-crash':
                PHASE='actual-partial-original-natural-lease';natural_partial_lease(restart_row,current,cut_physical,json.loads((evidence/'cut-root.json').read_bytes()),evidence)
            PHASE='actual-partial-permanent-proof';partial=wait_state(identifier,expected_state);physical=current();(evidence/'physical-partial.json').write_text(json.dumps(physical))
            (evidence/'partial-observed.json').write_text(json.dumps(dict(id=identifier,executionJobId=partial['row']['execution_job_id'],state=partial['row']['state'],atUtc=dt.datetime.now(dt.timezone.utc).isoformat())))
            if unknown or kind in RESTART_KINDS:assert physical==cut_physical
            assert bool(partial['row']['cancel_requested_at_utc']) is (kind=='cancel') and bool(partial['executionJob']['cancel_requested_at_utc']) is (kind=='cancel')
            facts=verify_records(partial,baseline,original,physical,storage,evidence,'partial')
            public_receipt={k[0].lower()+k[1:]:v for k,v in json.loads(partial['row']['receipt_json']).items()}
            write_signal(evidence,'settled.json',dict(id=identifier,state=expected_state,receipt=public_receipt))
            assert owner_process.wait(timeout=100)==0,'PARTIAL_OWNER_PAGE_FAILED';owner_ui=json.loads((evidence/'owner-browser.json').read_bytes());assert owner_ui['subject']==ready['subject']
            require_case(kind,partial,owner_ui,json.loads((evidence/('authority-change.json' if revoked else 'logout-authority.json')).read_bytes()) if identity else None)
            if kind in RESTART_KINDS:assert verify_partial_restart(kind,scope,owner_ui,partial,evidence,lambda p:json.loads(p.read_bytes()))==hosts_after
            if kind=='logout' or unknown:assert owner_ui['result'] is None and owner_ui['hintsCleared'] is True and owner_ui['cancellations']==0
            baseline.update(id=identifier,originalSubject=ready['subject'],receipt=public_receipt);(evidence/'recovery-fixture.json').write_text(json.dumps(baseline))
            PHASE='actual-different-admin-recovery';recovery_process=browser(repo,revision,'recovery',baseline,recovery)
            if cross_directory:
                registered=wait_signal(evidence,'waiting-registered.json',recovery_process);observe_waiting(partial,waiting,registered,current,physical,evidence)
            canceled=wait_signal(evidence,'recovery-canceled.json',recovery_process)
            assert canceled['subject']!=ready['subject'] and canceled['acknowledgements']==0 and snapshot(identifier)==partial and current()==physical
            verify_records(partial,baseline,original,physical,storage,evidence,'partial',read_only=True);write_signal(evidence,'acknowledge.json',dict(originalProtectionAndHistoryVerified=True))
            if cross_directory:
                PHASE='actual-directory-after-explicit-ack';wait_signal(evidence,'waiting-acknowledged.json',recovery_process,50)
                released=snapshot(identifier);require_ack(partial,released,registered['subject'],unknown=True)
                completed,waiting_verified=finish_waiting(waiting,registered,released,evidence);write_signal(evidence,'waiting-finished.json',completed)
            assert recovery_process.wait(timeout=100)==0,'PARTIAL_RECOVERY_PAGE_FAILED';recovery_ui=json.loads((evidence/'recovery-browser.json').read_bytes());ack=snapshot(identifier);require_ack(partial,ack,recovery_ui['subject'],unknown=unknown)
            assert current()==physical;ack_facts=verify_records(ack,baseline,original,physical,storage,evidence,'ack');assert facts['counts']==ack_facts['counts'] and facts['outcomes']==ack_facts['outcomes']
            (evidence/'physical-final.json').write_text(json.dumps(current()));(evidence/'facts.json').write_text(json.dumps(dict(partial=facts,ack=ack_facts)))
            if kind=='instance-changed':attest_instance_after_ack(baseline,ack['row'],current,new_inventory,evidence)
            if unknown:verify_unknown_target(baseline,ack['row'],evidence,lambda p:json.loads(p.read_bytes()))
            if cross_directory:clean_waiting(waiting_verified)
            proved=True
        finally:
            if held is not None:held.release()
            try:restore_fault_services(worker,attempt['agentRestartAttempted'],attempt.get('hostsBefore'),revision)
            finally:stop(owner_process);stop(recovery_process)
            if proved:remove_proved(root,reader,content,physical,acl)
    no_active_work();preflight();assert host_identity(revision)==hosts_after and not fixture['folder'].exists()
    after=owned.common.core_snapshot()
    for name,value in core.items():assert after[name][:3]==value[:3] if name.lstrip('/')=='whaledeck-worker' else after[name]==value
    preserved=preservation_snapshot(storage);assert all(preserved.get(k)==v for k,v in old.items());(evidence/'preservation-after.json').write_text(json.dumps(preserved))
    (evidence/'verified.json').write_text(json.dumps(dict(actualPartialAndDifferentAdminAck=not unknown,actualUnknownAfterPartialAndDifferentAdminAck=unknown,id=identifier,productStoppedHelperSeconds=round(elapsed,3),partialCounts=facts['counts'],childHddRecords=facts['childHddRecords'],oldFilesUnchanged=len(old),normalDualLogoutContexts=1 if revoked else 2,revokedContexts=int(revoked),exactSourceAndContainerAbsent=True,originalRootAndHddRetained=True,kind=kind,hostsAfter=hosts_after,crossDirectory=cross_directory)))
    print(json.dumps(dict(recursiveAclPartial='passed',evidence=evidence.name,counts=facts['counts'])),flush=True)


if __name__=='__main__':
    try:main()
    except BaseException as error:
        import traceback
        frames=[dict(file=Path(f.filename).name,line=f.lineno) for f in traceback.extract_tb(error.__traceback__) if '/deploy/validation/' in f.filename]
        print(json.dumps(dict(recursiveAclPartial='failed',phase=PHASE,failureType=type(error).__name__,locations=frames)),flush=True);raise SystemExit(1) from None
