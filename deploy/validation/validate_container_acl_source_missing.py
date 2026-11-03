#!/usr/bin/env python3
"""One real Ready -> missing selected HDD source -> rejected original confirmation.

NEW own capture only; no SQL/time/cache/session injection, Worker stop, installed
hook, replacement metadata or retry of confirmation. Exact history is restored,
not deleted. Failed/uncertain sources stay. This is NOT other source-fault stages.
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
from validate_container_acl_faults import wait_signal, write_signal, wait_settled, host_snapshot
from acl_restore_fault_proof import RESTORE_SOURCES
from validate_container_acl_restore_browser import selected_source, audit_rows
from validate_container_acl_history_browser import immutable
from acl_restore_browser_proof import source_json, expected_metadata, unique_json
from acl_history_browser_proof import typed_equal
from owned_acl_source_quarantine import installed_namespace, catalogue, OwnedSourceQuarantine
from validate_container_acl_stale import no_operation_history
from validate_container_file_staging import load
from validate_container_upload_bff import temporary_user, login, logout, request
from validate_container_directory_restart import preflight
from validate_container_upload_recovery_agent import trusted
from validate_metric_continuity import query

PHASE = 'startup'
KIND = 'source-confirm-missing'
SOURCES = tuple(dict.fromkeys((*acl.SOURCES, *RESTORE_SOURCES,
    'owned_acl_source_quarantine.py','validate_container_acl_source_missing.py',
    'validate_container_acl_source_missing.mjs','verify_container_acl_source_missing.py',
    'validate_container_acl_stale.py','validate_container_acl_stale.mjs','verify_container_acl_stale.py',
    'test_acl_source_missing_proof.py')))
INSTALLED = tuple(dict.fromkeys((*acl.NAMES,'whaledeck-container-acl-transport.py','whaledeck-privileged',
    'whaledeck-container-acl-history.py','whaledeck-container-acl-history-transport.py')))


def own_ready_only(identifier):
    preflight([identifier]); acl.previous_idle()
    value=query("SELECT to_json(count(*)) FROM container_acl_intents WHERE id<>:'id'::uuid AND (completed_at_utc IS NULL OR publish_barrier OR lease_token IS NOT NULL OR lease_expires_at_utc IS NOT NULL);",id=identifier)
    assert type(value) is int and value==0, 'OTHER_ACL_WORK_ACTIVE'


def require_ready(data, baseline):
    row=data['row'];assert row['id']!=baseline['captureIntentId']
    assert row['actor_subject']==baseline['subject'] and row['container_id']==baseline['containerId'] and row['instance_id']==baseline['instanceId']
    assert row['relative_path']=='work/acl-file.bin' and row['state']=='Ready' and data['planJob']['state']=='Succeeded'
    assert row['execution_job_id'] is row['root_started_at_utc'] is row['completed_at_utc'] is None
    assert row['lease_token'] is row['lease_expires_at_utc'] is None and not row['publish_barrier']
    assert row['acl_confirmed'] is row['history_digest'] is row['receipt_json'] is row['receipt_hash'] is row['published_version'] is None
    assert row['history_may_exist'] is row['history_verified'] is False
    assert data['executionJob'] is None and data['pending']==data['confirmed']==0 and data['accepted']==1
    typed_equal(unique_json(row['restore_source_json']),source_json(baseline['restoreSource']))
    plan=acl.managed_plan(unique_json(row['plan_json']))
    assert plan['command']['entries']==baseline['restoreSource']['accessAcl'] and plan['command']['aclType']=='access' and plan['noChange'] is False
    assert plan['sourceIdentity']==baseline['original']['acl-file.bin']['identity']


def require_canceled(data, ready):
    row=data['row'];old=ready['row']
    assert row['state']=='Canceled' and row['result_code']=='CONTAINER_ACL_CANCELED' and row['cancel_requested_at_utc'] and row['completed_at_utc']
    assert row['execution_job_id'] is row['root_started_at_utc'] is row['receipt_json'] is row['receipt_hash'] is row['acl_confirmed'] is None
    assert row['lease_token'] is row['lease_expires_at_utc'] is None and not row['publish_barrier']
    assert row['history_may_exist'] is row['history_verified'] is False and row['history_digest'] is row['published_version'] is None
    assert row['version']==old['version']+1 and data['executionJob'] is None and data['pending']==data['confirmed']==0 and data['accepted']==1
    assert data['planJob']==ready['planJob'] and data['events']==ready['events']
    for key in ('id','actor_subject','login_binding','container_id','instance_id','relative_path','owner_hash','request_hash','command_json',
                'plan_json','plan_hash','restore_source_json','execution_hash','planning_expires_at_utc','ready_expires_at_utc'):
        assert row[key]==old[key], key


def verify_audits(rows,row):
    expected={'plan-accepted':'Accepted','plan-result':'Ready','cancel-requested':'Accepted'}
    assert len(rows)==len(expected);seen=set()
    for value in rows:
        name=value['action'].removeprefix('containers.files.acl.restore.')
        assert name in expected and name not in seen;seen.add(name)
        assert value['actor_subject']==row['actor_subject'] and value['target_id']==row['container_id'] and value['target_type']=='container'
        assert value['job_id']==row['id'] and value['result']==expected[name] and value['trace_id']
        typed_equal(unique_json(value['detail_json']),expected_metadata(row))
    assert seen==expected.keys()


def denials(baseline,identifier):
    base='http://127.0.0.1:8080'; endpoint=base+'/api/v1/containers/'+baseline['containerId']+'/files/acls'
    source=baseline['restoreSource']['entry']
    params=dict(instanceId=baseline['instanceId'],path='work/acl-file.bin',expectedHistoryDigest=source['historyDigest'])
    paths=[(endpoint+'/restores/'+identifier,None),(endpoint+'/history/'+source['captureId'],params)]
    with requests.Session() as anonymous:
        anonymous.trust_env=False
        for url,parameters in paths:request(anonymous,'GET',url,401,params=parameters)
    with temporary_user(False) as (name,password):
        session,_=login(base,name,password,False)
        try:
            for url,parameters in paths:request(session,'GET',url,403,params=parameters)
            logout(base,session)
        finally:session.close()
    return dict(anonymousReadsDenied=2,ordinaryReadsDenied=2,normalOrdinaryDualLogout=True)


def main():
    global PHASE
    assert os.geteuid()==0 and len(sys.argv)==2;os.umask(0o077)
    revision=sys.argv[1];assert re.fullmatch('[a-f0-9]{40}',revision)
    repo=Path(__file__).resolve().parents[2]
    assert not subprocess.check_output(['git','-C',str(repo),'status','--porcelain']).strip(), 'FREEZE_VALIDATION_SOURCE_FIRST'
    source=subprocess.check_output(['git','-C',str(repo),'rev-parse','HEAD'],text=True).strip(); hashes={};installed={}
    for name in SOURCES:
        raw=subprocess.check_output(['git','-C',str(repo),'show',source+':deploy/validation/'+name]);assert raw==(repo/'deploy/validation'/name).read_bytes();hashes[name]=hashlib.sha256(raw).hexdigest()
    for name in INSTALLED:
        path=Path('/usr/local/libexec')/name;trusted(path);raw=subprocess.check_output(['git','-C',str(repo),'show',revision+':deploy/host/'+name]);assert path.read_bytes()==raw;installed[name]=hashlib.sha256(raw).hexdigest()
    with requests.Session() as api:
        api.trust_env=False;r=api.get('http://127.0.0.1:8080/api/v1/system/status',timeout=10);assert r.status_code==200 and r.json()['build']['sourceRevision']==revision
    acl.no_active_work();hosts=host_snapshot(revision)
    evidence=repo/'deploy/artifacts'/('container-acl-source-missing-'+uuid.uuid4().hex);evidence.mkdir(mode=0o700)
    print(json.dumps(dict(aclSourceMissing='started',evidence=str(evidence))),flush=True)
    reader=load('/usr/local/libexec/whaledeck-container-files.py','source_missing_reader');module=load('/usr/local/libexec/whaledeck-container-acl.py','source_missing_acl')
    files=reader.trusted_module('whaledeck-files.py','source_missing_files');storage=load('/usr/local/libexec/whaledeck-file-backups.py','source_missing_hdd').FileBackups(files)
    browser=None;proved=False;identifier=None;current=None
    with installed_namespace(storage) as (namespace,attest), acl.owned.fixture() as fixture:
        old=catalogue(namespace);(evidence/'old-hdd.json').write_text(json.dumps(old))
        folder=fixture['folder'];(folder/'acl-directory').mkdir(mode=0o750)
        content=hashlib.shake_256(('source-missing:'+fixture['identifier']).encode()).digest(2*1024**2+7)
        for relative,kind in acl.PATHS.items():
            p=folder/relative
            if kind=='File':p.write_bytes(content);os.setxattr(p,'user.whaledeck-acl',b'preserve ordinary attribute')
            os.chown(p,2100,3100);p.chmod(0o640 if kind=='File' else 0o750)
        for relative in acl.PATHS:os.utime(folder/relative,ns=(1700000000123456789,1700000000123456789))
        actual=json.loads(acl.owned.common.command(['docker','inspect',fixture['identifier']]))[0];original=acl.inventory(folder,reader,module,content)
        baseline=dict(revision=revision,validationSourceRevision=source,validationSourceSha256=hashes,installedSourceSha256=installed,
            aclSourceFaultVersion=1,kind=KIND,containerId=fixture['identifier'],containerName=actual['Name'].lstrip('/'),
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
                    PHASE='original-ready-plan';ready=wait_signal(evidence,'ready.json',browser,100);identifier=ready['id']
                    data=acl.snapshot(identifier);require_ready(data,baseline);own_ready_only(identifier)
                    no_operation_history(identifier,storage);(evidence/'ready-sql.json').write_text(json.dumps(data))
                    PHASE='only-new-source-quarantined'
                    with tool.hidden() as hidden:
                        (evidence/'hidden-proof.json').write_text(json.dumps(hidden))
                        write_signal(evidence,'source-hidden.json',hidden)
                        rejected=wait_signal(evidence,'rejected.json',browser,60)
                        assert rejected['id']==identifier and rejected['subject']==baseline['subject'] and rejected['sourceStatus']==404 and rejected['confirmStatus']==409 and rejected['code']=='OPERATION_CONFLICT'
                        assert (rejected['registrations'],rejected['confirmations'],rejected['cancellations'],rejected['contentWrites'])==(1,1,0,0)
                        after=acl.snapshot(identifier);assert after==data
                        require_ready(after,baseline);no_operation_history(identifier,storage)
                        assert acl.inventory(folder,reader,module,content)==current and catalogue(namespace)==old
                        assert (capture/'access.root.json').read_bytes()==root_raw and (acl.STATE/(proof['result']['executionJobId'].replace('-','')+'.json')).read_bytes()==root_raw
                        (evidence/'rejected-sql.json').write_text(json.dumps(after))
                    PHASE='original-source-restored'
                    restored=tool.restore();assert restored['originalRestored']
                    own=baseline['restoreSource']['entry']['captureId']+'.json';after=catalogue(namespace)
                    assert {name:item for name,item in after.items() if name!=own}==old
                    assert immutable(capture,baseline,[proof],storage)==metadata
                    (evidence/'restored-hdd.json').write_text(json.dumps(after));write_signal(evidence,'source-restored.json',restored)
                    canceled=wait_signal(evidence,'canceled.json',browser,60);final=wait_settled(identifier);require_canceled(final,data)
                    assert canceled['id']==identifier and canceled['final']['intent']['state']=='Canceled' and canceled['final']['source']==baseline['restoreSource']
                    no_operation_history(identifier,storage);assert acl.inventory(folder,reader,module,content)==current
                    audits=audit_rows(identifier);verify_audits(audits,final['row']);(evidence/'restore-audits.json').write_text(json.dumps(audits))
                    (evidence/'canceled-sql.json').write_text(json.dumps(final));(evidence/'physical-final.json').write_text(json.dumps(current))
                    write_signal(evidence,'verified-row.json',dict(id=identifier,noExecutionOrNewHistory=True))
                    assert browser.wait(timeout=75)==0, 'REAL_SOURCE_MISSING_BROWSER_FAILED'
                    ui=json.loads((evidence/'browser.json').read_bytes());assert ui['actualUi'] and ui['normalDualLogout'] and ui['id']==identifier
                    (evidence/'denials.json').write_text(json.dumps(denials(baseline,identifier)))
                    assert acl.inventory(folder,reader,module,content)==current and immutable(capture,baseline,[proof],storage)==metadata
                    assert catalogue(namespace)==after;proved=True
        finally:
            if (evidence/'source-quarantine.json').exists():
                try:tool.restore()
                except BaseException:
                    (evidence/'quarantine-recovery-required.json').write_text(json.dumps(dict(gatePassed=False,originalsPreserved=True)));raise
            os.close(journal)
            if browser is not None and browser.poll() is None:
                try:browser.wait(timeout=50)
                except subprocess.TimeoutExpired:browser.terminate();browser.wait(timeout=10)
            if proved:acl.remove_proved(folder,reader,module,content,current)
            else:
                # No unproved deletion. A failed own browser may have logged out;
                # only a proved terminal without execution permits fixture cleanup.
                safe=False
                if identifier is not None:
                    stopped=wait_settled(identifier);(evidence/'failed-gate-sql.json').write_text(json.dumps(stopped))
                    safe=stopped['row']['state'] in ('Canceled','Expired','Failed') and stopped['row']['execution_job_id'] is None
                if safe and current is not None and acl.inventory(folder,reader,module,content)==current:
                    no_operation_history(identifier,storage);acl.remove_proved(folder,reader,module,content,current)
    acl.no_active_work();assert host_snapshot(revision)==hosts
    assert not os.path.lexists(folder) and subprocess.run(['docker','inspect',baseline['containerId']],capture_output=True,timeout=10).returncode!=0
    result=dict(revision=revision,id=identifier,kind=KIND,actualUi=True,normalDualLogout=True,sourceMissingStatus=404,confirmationStatus=409,
        noRestoreExecution=True,noRestoreRootOrHdd=True,sourceCaptureCount=1,oldCapturesUnchanged=len(old),originalSourceRestored=True,
        physicalCheckedBeforeCleanup=True,exactSourceAndContainerRemoved=True,coreHealthy=len(acl.owned.common.core_snapshot()))
    (evidence/'verified.json').write_text(json.dumps(result));print(json.dumps(dict(aclSourceMissing='passed',evidence=str(evidence),**result)),flush=True)


if __name__=='__main__':
    try:main()
    except BaseException as error:
        print(json.dumps(dict(aclSourceMissing='failed',phase=PHASE,failureType=type(error).__name__,
            locations=[dict(file=Path(f.filename).name,line=f.lineno) for f in traceback.extract_tb(error.__traceback__)])),flush=True)
        raise SystemExit(1) from None
