#!/usr/bin/env python3
"""New own capture: registered Queued, selected source lost BEFORE asynchronous plan.

Stop/start ONLY the existing Worker. No Root plan, execution or discard record;
unchanged target and old history, exact metadata restoration; no product rollback.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import traceback
import uuid
import requests
import validate_container_acl_browser as acl
from validate_container_acl_faults import wait_signal,write_signal,wait_settled,host_snapshot
from validate_container_acl_source_missing import denials,INSTALLED
from acl_source_plan_missing_proof import KIND,manifest,require_queued,require_failed,verify_audits,summary
from validate_container_acl_restore_browser import selected_source,audit_rows
from validate_container_acl_history_browser import immutable
from owned_acl_source_quarantine import installed_namespace,catalogue,OwnedSourceQuarantine
from validate_container_acl_stale import no_operation_history
from validate_container_file_staging import load
from validate_container_upload_bff import temporary_user
from validate_container_directory_restart import Worker,preflight
from validate_container_upload_recovery_agent import trusted
PHASE='startup'


def main():
    global PHASE
    assert os.geteuid()==0 and len(sys.argv)==2;os.umask(0o077)
    revision=sys.argv[1];assert re.fullmatch('[a-f0-9]{40}',revision)
    repo=Path(__file__).resolve().parents[2]
    assert not subprocess.check_output(['git','-C',str(repo),'status','--porcelain']).strip(), 'FREEZE_VALIDATION_SOURCE_FIRST'
    source=subprocess.check_output(['git','-C',str(repo),'rev-parse','HEAD'],text=True).strip(); hashes=manifest(repo,source);installed={}
    for name,sha in hashes.items():assert hashlib.sha256((repo/'deploy/validation'/name).read_bytes()).hexdigest()==sha
    for name in INSTALLED:
        path=Path('/usr/local/libexec')/name;trusted(path);raw=subprocess.check_output(['git','-C',str(repo),'show',revision+':deploy/host/'+name]);assert path.read_bytes()==raw;installed[name]=hashlib.sha256(raw).hexdigest()
    with requests.Session() as api:
        api.trust_env=False;r=api.get('http://127.0.0.1:8080/api/v1/system/status',timeout=10);assert r.status_code==200 and r.json()['build']['sourceRevision']==revision
    acl.no_active_work();hosts=host_snapshot(revision)
    evidence=repo/'deploy/artifacts'/('container-acl-source-plan-missing-'+uuid.uuid4().hex);evidence.mkdir(mode=0o700)
    print(json.dumps(dict(sourcePlanMissing='started',evidence=str(evidence))),flush=True)
    reader=load('/usr/local/libexec/whaledeck-container-files.py','source_missing_reader');module=load('/usr/local/libexec/whaledeck-container-acl.py','source_missing_acl')
    files=reader.trusted_module('whaledeck-files.py','source_missing_files');storage=load('/usr/local/libexec/whaledeck-file-backups.py','source_missing_hdd').FileBackups(files)
    browser=None;proved=False;identifier=None;current=None;queued=None;worker=Worker()
    with installed_namespace(storage) as (namespace,attest), acl.owned.fixture(restartable_core=('whaledeck-worker',)) as fixture:
        old=catalogue(namespace);(evidence/'old-hdd.json').write_text(json.dumps(old))
        folder=fixture['folder'];(folder/'acl-directory').mkdir(mode=0o750)
        content=hashlib.shake_256(('source-plan-missing:'+fixture['identifier']).encode()).digest(2*1024**2+7)
        for relative,kind in acl.PATHS.items():
            p=folder/relative
            if kind=='File':p.write_bytes(content);os.setxattr(p,'user.whaledeck-acl',b'preserve ordinary attribute')
            os.chown(p,2100,3100);p.chmod(0o640 if kind=='File' else 0o750)
        for relative in acl.PATHS:os.utime(folder/relative,ns=(1700000000123456789,1700000000123456789))
        actual=json.loads(acl.owned.common.command(['docker','inspect',fixture['identifier']]))[0];original=acl.inventory(folder,reader,module,content)
        baseline=dict(revision=revision,validationSourceRevision=source,validationSourceSha256=hashes,installedSourceSha256=installed,
            aclSourceFaultVersion=3,dependencyManifestVersion=1,kind=KIND,containerId=fixture['identifier'],containerName=actual['Name'].lstrip('/'),
            instanceId=str(actual['State']['Pid'])+':'+actual['State']['StartedAt'],source=str(folder),original=original,
            sha256=hashlib.sha256(content).hexdigest().upper(),bytesPerFile=len(content),coreBefore=acl.owned.common.core_snapshot(),hostsBefore=hosts,evidence=str(evidence))
        (evidence/'baseline.json').write_text(json.dumps(baseline))
        journal=os.open(evidence,os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW)
        tool=OwnedSourceQuarantine(namespace,journal,attest)
        try:
            with temporary_user(True) as (username,password):
                with (evidence/'browser.stdout').open('wb') as out,(evidence/'browser.stderr').open('wb') as err:
                    browser=subprocess.Popen(['node',str(Path(__file__).with_suffix('.mjs')),revision],stdin=subprocess.PIPE,stdout=out,stderr=err,
                        env={**os.environ,'VALIDATION_USERNAME':username,'VALIDATION_PASSWORD':password})
                    browser.stdin.write(json.dumps(baseline).encode());browser.stdin.close();browser.stdin=None
                    PHASE='new-visible-source-capture';captured=wait_signal(evidence,'source-captured.json',browser,120)
                    baseline['subject']=captured['subject'];current=acl.inventory(folder,reader,module,content)
                    capture=evidence/'capture';capture.mkdir(mode=0o700)
                    proof=acl.verify_case(captured['result'],baseline,baseline['subject'],capture,reader,module,storage,original,current)
                    (evidence/'capture-proof.json').write_text(json.dumps(proof));metadata=immutable(capture,baseline,[proof],storage)
                    baseline['captureIntentId']=captured['result']['id'];baseline['restoreSource']=selected_source(baseline,proof,metadata)
                    baseline['original']=current;(evidence/'baseline.json').write_text(json.dumps(baseline))
                    capture_sql=acl.snapshot(baseline['captureIntentId']);root_raw=(capture/'access.root.json').read_bytes()
                    tool.arm(baseline,proof,capture_sql,root_raw,old)
                    write_signal(evidence,'source-verified.json',dict(source=baseline['restoreSource'],physicalAndOriginalHistoryVerified=True))
                    PHASE='selected-source-before-restore-registration'
                    selected=wait_signal(evidence,'selected.json',browser,120)
                    assert selected['subject']==baseline['subject'] and selected['source']==baseline['restoreSource']
                    acl.no_active_work();worker.stop();write_signal(evidence,'register.json',dict(workerStopped=True))
                    registered=wait_signal(evidence,'registered.json',browser,45);identifier=registered['id'];queued=acl.snapshot(identifier)
                    assert registered['subject']==baseline['subject'] and registered['original']['source']==baseline['restoreSource']
                    require_queued(queued,baseline);preflight([identifier])
                    no_operation_history(identifier,storage);(evidence/'queued-sql.json').write_text(json.dumps(queued))
                    (evidence/'worker-stopped.json').write_text(json.dumps(dict(id=worker.id,image=worker.original['Image'],running=worker.current()['State']['Running'])))
                    assert not worker.current()['State']['Running']
                    PHASE='selected-source-missing-before-asynchronous-plan'
                    with tool.hidden() as hidden:
                        (evidence/'hidden-proof.json').write_text(json.dumps(hidden));write_signal(evidence,'source-hidden.json',hidden)
                        observed=wait_signal(evidence,'hidden-observed.json',browser,45)
                        assert observed['id']==identifier and observed['sourceStatus']==404 and observed['byKeyStatus']==200
                        assert acl.snapshot(identifier)==queued and acl.inventory(folder,reader,module,content)==current and catalogue(namespace)==old
                        no_operation_history(identifier,storage)
                        worker.start(wait_ready=False)
                        final=wait_settled(identifier);require_failed(final,queued)
                        no_operation_history(identifier,storage)
                        assert acl.inventory(folder,reader,module,content)==current and catalogue(namespace)==old
                        (evidence/'failed-sql.json').write_text(json.dumps(final))
                    PHASE='source-restored-original-preflight-remains-failed'
                    restored=tool.restore();assert restored['originalRestored']
                    own=baseline['restoreSource']['entry']['captureId']+'.json';after=catalogue(namespace)
                    assert {name:item for name,item in after.items() if name!=own}==old
                    assert immutable(capture,baseline,[proof],storage)==metadata
                    no_operation_history(identifier,storage)
                    (evidence/'restored-hdd.json').write_text(json.dumps(after));(evidence/'source-restored.json').write_text(json.dumps(restored))
                    assert acl.inventory(folder,reader,module,content)==current and acl.snapshot(identifier)==final
                    audits=audit_rows(identifier);verify_audits(audits,final['row']);(evidence/'restore-audits.json').write_text(json.dumps(audits))
                    (evidence/'physical-final.json').write_text(json.dumps(current))
                    write_signal(evidence,'failed.json',dict(id=identifier,executionJobId=None,sourceRestored=True))
                    terminal=wait_signal(evidence,'terminal.json',browser,60)
                    assert terminal['id']==identifier and terminal['final']['intent']['state']=='Failed' and terminal['final']['source']==baseline['restoreSource']
                    acl.validate_job_summary(terminal['job'],summary(identifier),identifier,'Failed')
                    write_signal(evidence,'verified-row.json',dict(id=identifier,failedBeforeRootPlanOrExecution=True))
                    assert browser.wait(timeout=75)==0, 'REAL_SOURCE_PLAN_MISSING_BROWSER_FAILED'
                    ui=json.loads((evidence/'browser.json').read_bytes());assert ui['actualUi'] and ui['normalDualLogout'] and ui['id']==identifier
                    (evidence/'denials.json').write_text(json.dumps(denials(baseline,identifier)))
                    assert acl.inventory(folder,reader,module,content)==current and immutable(capture,baseline,[proof],storage)==metadata
                    assert catalogue(namespace)==after;proved=True
        finally:
            try:
                if (evidence/'source-quarantine.json').exists():
                    try:tool.restore()
                    except BaseException:
                        (evidence/'quarantine-recovery-required.json').write_text(json.dumps(dict(gatePassed=False,originalsPreserved=True)));raise
            finally:
                os.close(journal)
                try:worker.start()
                finally:
                    if browser is not None and browser.poll() is None:
                        try:browser.wait(timeout=50)
                        except subprocess.TimeoutExpired:browser.terminate();browser.wait(timeout=10)
            if proved:acl.remove_proved(folder,reader,module,content,current)
            else:
                # A failed check never authorizes deleting uncertain or changed source data.
                if identifier is not None and queued is not None:
                    stopped=wait_settled(identifier);(evidence/'failed-gate-sql.json').write_text(json.dumps(stopped))
                    if stopped['row']['state']=='Failed':
                        try:require_failed(stopped,queued)
                        except (AssertionError,ValueError,OSError):pass
                        else:
                            if current is not None and acl.inventory(folder,reader,module,content)==current:
                                acl.remove_proved(folder,reader,module,content,current)
    acl.no_active_work();assert host_snapshot(revision)==hosts
    assert not os.path.lexists(folder) and subprocess.run(['docker','inspect',baseline['containerId']],capture_output=True,timeout=10).returncode!=0
    result=dict(revision=revision,id=identifier,kind=KIND,actualUi=True,normalDualLogout=True,sourceMissingStatus=404,registrationStatus=202,
        noExecution=True,noRootOrHdd=True,noRootPlan=True,sourceCaptureCount=1,oldCapturesUnchanged=len(old),originalSourceRestored=True,
        physicalCheckedBeforeCleanup=True,exactSourceAndContainerRemoved=True,executionJobId=None,coreHealthy=len(acl.owned.common.core_snapshot()))
    (evidence/'verified.json').write_text(json.dumps(result));print(json.dumps(dict(sourcePlanMissing='passed',evidence=str(evidence),**result)),flush=True)


if __name__=='__main__':
    try:main()
    except BaseException as error:
        print(json.dumps(dict(sourcePlanMissing='failed',phase=PHASE,failureType=type(error).__name__,
            locations=[dict(file=Path(f.filename).name,line=f.lineno) for f in traceback.extract_tb(error.__traceback__)])),flush=True)
        raise SystemExit(1) from None
