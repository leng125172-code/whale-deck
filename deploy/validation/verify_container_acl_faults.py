#!/usr/bin/env python3
"""Independent read-only permanent ACL fault evidence after exact cleanup."""
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys

import requests
from validate_container_acl_faults import SOURCES, verify_terminal, no_active_work, load, owned, terminal_state, RESTART_KEYS, require_host_restart, observation_expiry, unchanged_except_target, managed_plan, verify_task_results
from validate_metric_continuity import query
from validate_container_upload_recovery_agent import trusted
from run_with_ephemeral_admin import AUTHENTIK_API, container_environment, require


def validate_source_profile(provenance, kinds):
    schema=provenance.get('schemaVersion',1)
    assert type(schema) is int and schema in (1,2,3,4,5,6,7,8,9,10,11,12)
    if schema<6:assert 'post-acl-agent-restart' not in kinds
    if schema<5:assert not set(kinds)&{'post-acl-group-removal','post-acl-account-disable'}
    if schema<4:assert not set(kinds)&{'post-acl-cancel','post-acl-worker-crash'}
    if schema<3:assert not set(kinds)&{'worker-crash','agent-restart'}
    expected=set(SOURCES)
    if schema in (7,8,9,10,11,12):
        from acl_restore_fault_proof import RESTORE_SOURCES,restore_fault_version
        assert type(provenance.get('aclRestoreFaultVersion')) is int and provenance['aclRestoreFaultVersion']==schema-6==restore_fault_version(kinds)
        expected.update(RESTORE_SOURCES)
    else:assert 'aclRestoreFaultVersion' not in provenance
    if schema==1:
        assert not set(kinds)&{'group-removal','account-disable'}
        expected.remove('owned_authentik_revocation.py')
    assert set(provenance['sha256'])==expected


def verify_authority(baseline, ui, row, result, proof):
    kind=result['kind'].removeprefix('post-acl-');assert kind in ('group-removal','account-disable')
    assert ui['authorityRevoked'] and result['authorityRevoked'] and ui['normalDualLogout'] is False and not ui.get('logout')
    assert ui['renderedRevocationPage']==('forbidden' if kind=='group-removal' else 'login') and ui['hintsCleared'] is True
    assert ui['originalIntentStatus'] in (401,403) and ui['currentIdentityStatus'] in ((200,) if kind=='group-removal' else (401,403))
    assert proof['kind']==kind and proof['onlyOwnedIdentityChanged'] is True and proof['subject']==ui['subject']==row['actor_subject']
    before,after=proof['before'],proof['after'];keys={'pk','uuid','username','is_active','groups'}
    assert set(before)==set(after)==keys and type(before['pk']) is int and before['pk']>0 and before['pk']==after['pk']
    assert before['uuid']==after['uuid']==proof['subject'] and before['username']==after['username'] and re.fullmatch('whaledeck-upload-qa-[a-f0-9]{12}',before['username'])
    assert before['is_active'] is True and type(before['groups']) is list and before['groups'] and all(type(g) is str for g in before['groups'])
    if kind=='group-removal':assert after['groups']==[] and after['is_active'] is True
    else:assert after['is_active'] is False and after['groups']==before['groups']
    expiry=dt.datetime.fromisoformat(baseline['permissionExpiresAtUtc']);changed=dt.datetime.fromisoformat(proof['confirmedAtUtc'])
    assert expiry.tzinfo is not None and changed.tzinfo is not None and expiry>changed+dt.timedelta(hours=1)
    assert row['cancel_requested_at_utc'] is None and ui['cancellations']==0
    if baseline.get('aclRestoreFaultVersion') in (2,5):
        assert ui['intentReadPath']=='/api/v1/containers/'+row['container_id']+'/files/acls/restores/'+row['id']


def verify_current_admin(proof, row, record, old_subject):
    assert proof['subject']!=old_subject==row['actor_subject'] and proof['currentlyAdministrator'] is True
    assert proof['readStatus']==200 and proof['readOnlyOperation'] is True and proof['normalHttpDualLogout'] is True
    intent=proof['intent'];assert intent['id']==row['id'] and intent['executionJobId']==row['execution_job_id'] and intent['state']=='Succeeded'
    assert intent['publishedVersion']==record['version'] and intent['aclConfirmed'] is True and intent['confirmation'] is None
    assert intent['historyVerified'] and intent['historyDigest']==record['historyDigest'] and intent['receipt']['state']=='Completed'
    assert intent['receipt']['id']==row['execution_job_id'] and intent['receipt']['bindingCurrent'] is True
    if row.get('restore_source_json') is not None:
        assert proof['ownerRestoreStatus']==404 and proof['recoveryNoStore'] is True
        assert proof['ownerRestorePath']=='/api/v1/containers/'+row['container_id']+'/files/acls/restores/'+row['id']
    return intent


def verify_lease_proof(row, final, proof, raw, *, post=False):
    expiry=dt.datetime.fromisoformat(proof['originalLeaseExpiresAtUtc']);began=dt.datetime.fromisoformat(proof['waitBeganAtUtc']);ended=dt.datetime.fromisoformat(proof['waitEndedAtUtc'])
    assert all(t.utcoffset() is not None for t in (expiry,began,ended)) and dt.timedelta(seconds=25)<expiry-began<=dt.timedelta(seconds=95)
    assert ended>=expiry and abs((ended-began).total_seconds()-proof['monotonicWaitSeconds'])<3 and proof['monotonicWaitSeconds']<100
    assert dt.datetime.fromisoformat(final['completed_at_utc'])>=expiry and proof['clocksOrSqlModified'] is False
    assert row['state']=='Applying' and row['publish_barrier'] and row['completed_at_utc'] is None and row['cancel_requested_at_utc'] is None
    assert row['lease_token']==proof['originalLeaseToken'] and row['lease_expires_at_utc']==proof['originalLeaseExpiresAtUtc']
    digest=hashlib.sha256(raw).hexdigest();root=json.loads(raw);assert root['state']==proof['rootStateAfterHelperExit'] in (('Applying',) if post else ('Preparing','Prepared')) and root['historyVerified']
    assert digest==proof['rootSha256'] and len(proof['samples'])>=3
    previous=began
    for sample in proof['samples']:
        at=dt.datetime.fromisoformat(sample['atUtc']);assert previous<=at<expiry and (at-previous).total_seconds()<5;previous=at
        assert sample['state']=='Applying' and sample['barrier'] is True and sample['leaseToken']==row['lease_token'] and sample['leaseExpiresAtUtc']==row['lease_expires_at_utc']
        assert sample['rootSha256']==digest and sample['sourceFullBytesAclsAndAttributesUnchanged'] is True
        if row.get('restore_source_json') is not None:assert sample['restoreSourceHash']==hashlib.sha256(row['restore_source_json'].encode()).hexdigest().upper()
    assert (ended-previous).total_seconds()<5


def verify_worker_snapshot(service, baseline, observed):
    original=baseline['coreBefore']['/whaledeck-worker']
    assert service['workerId']==observed['containerId']==original[0] and observed['imageId']==original[1]
    assert observed['startedAt']==service['afterStartedAt'] and observed['running'] is True
    before=dt.datetime.fromisoformat(service['beforeStartedAt']);after=dt.datetime.fromisoformat(service['afterStartedAt']);at=dt.datetime.fromisoformat(observed['observedAtUtc'])
    assert dt.datetime.fromisoformat(original[3])<=before<after<=at


def verify_restart(kind, result, baseline, ui, final, start, read, directory, *, post=False):
    service=result['serviceRestart'];assert service and ui['normalDualLogout'] and ui['cancellations']==0 and final['cancel_requested_at_utc'] is None
    if baseline.get('aclRestoreFaultVersion') in (3,4,6):
        from acl_history_browser_proof import typed_equal
        assert ui['intentReadPath']=='/api/v1/containers/'+final['container_id']+'/files/acls/restores/'+final['id']
        typed_equal(ui['restoreTerminalSource'],baseline['restoreSource'])
    intent=ui['terminalIntent'];assert intent['id']==final['id'] and intent['executionJobId']==final['execution_job_id'] and intent['state']==('Succeeded' if post else 'Failed') and intent['resultCode']==('CONTAINER_ACL_COMPLETED' if post else 'CONTAINER_ACL_INTERRUPTED')
    assert intent['aclConfirmed'] is post and intent['confirmation'] is None and intent['historyVerified'] and intent['receipt']['state']==('Completed' if post else 'Aborted')
    checks=service['otherActiveWork'];assert RESTART_KEYS<=set(checks) and all(type(n) is int and n==0 for n in checks.values())
    row=start['row']
    for key in ('id','execution_job_id','container_id','instance_id','actor_subject','owner_hash','plan_json','command_json','root_started_at_utc'):assert row[key]==final[key]
    assert row['state']=='Applying' and row['publish_barrier'] and row['root_started_at_utc'] and start['executionJob']['state']=='Running'
    if row.get('restore_source_json') is not None:
        for key in ('restore_source_json','request_hash','plan_hash','execution_hash','login_binding'):assert row[key]==final[key]
    if kind=='worker-crash':
        assert service['sameWorkerContainerAndImage'] and service['signal']=='SIGKILL' and type(service['manuallyStarted']) is bool
        original=baseline['coreBefore']['/whaledeck-worker'];assert service['workerId']==original[0] and service['beforeStartedAt']!=service['afterStartedAt']
        assert service['hostsBefore']==service['hostsAfter']
        crashed=read(directory/'after-worker-crash-sql.json');crash=crashed['row'];assert crash['lease_token']==row['lease_token']
        assert crash['lease_expires_at_utc']==row['lease_expires_at_utc']
        witness=directory/'independent-worker-inspect.json'
        if not witness.exists():witness=directory/'worker-inspect.json'
        verify_worker_snapshot(service,baseline,read(witness))
        for key in ('id','execution_job_id','container_id','instance_id','actor_subject','owner_hash','root_started_at_utc','plan_json','command_json'):assert crash[key]==row[key]
        assert crashed['executionJob']['state']=='Running' and service['originalLeaseExpiresAtUtc']==crash['lease_expires_at_utc']
        raw=(directory/'after-helper-exit-root.json').read_bytes();original_root=json.loads(raw)
        assert original_root['operationId']==final['execution_job_id'].replace('-','') and original_root['owner']==final['owner_hash'] and original_root['plan']==managed_plan(json.loads(final['plan_json']))
        assert original_root['aclConfirmed'] is None and original_root['actualIdentity'] is None and original_root['version']=='' and original_root['historyDigest']==final['history_digest']
        verify_lease_proof(crash,final,read(directory/'natural-lease.json'),raw,post=post)
    else:
        require_host_restart(service['hostsBefore'],service['hostsAfter'],baseline['revision']);assert service['maintenancePidUnchanged']
        began=dt.datetime.fromisoformat(service['restartBeganAtUtc']);observation_expiry(row,began);ended=dt.datetime.fromisoformat(service['restartEndedAtUtc']);samples=read(directory/'maintenance-observations.json')
        assert 0<=(ended-began).total_seconds()<=service['totalRestartSeconds']+1 and len(samples)==service['maintenanceSamples']>=3
        times=[dt.datetime.fromisoformat(v['atUtc']) for v in samples];assert times[0]<=began<ended<=times[-1] and times==sorted(times)
        assert any(began<t<ended for t in times) and all((b-a).total_seconds()<5 for a,b in zip(times,times[1:]))
        assert all(v['httpStatus']==200 and v['state']=='healthy' and v['phase']=='Ready' and v['noStore'] and 0<v['bytes']<=2048 and 0<=v['responseSeconds']<5 for v in samples)


def verify(directory):
    repo=Path(__file__).resolve().parents[2]
    assert directory.parent==repo/'deploy/artifacts' and re.fullmatch('container-acl-faults-[a-f0-9]{32}',directory.name);trusted(directory,True)
    def read(path):
        info=trusted(path);assert info.st_size<=262144;return json.loads(path.read_bytes())
    accepted=read(directory/'verified.json');provenance=read(directory/'validation-source.json')
    assert accepted['revision']==provenance['productRevision']
    validate_source_profile(provenance,[r['kind'] for r in accepted['cases']])
    subprocess.run(['git','-C',str(repo),'merge-base','--is-ancestor',accepted['revision'],provenance['revision']],check=True,capture_output=True,timeout=15)
    for name,digest in provenance['sha256'].items():
        assert '/' not in name
        raw=subprocess.check_output(['git','-C',str(repo),'show',provenance['revision']+':deploy/validation/'+name]);assert hashlib.sha256(raw).hexdigest()==digest
    for name,digest in provenance['installedSha256'].items():
        assert '/' not in name
        raw=subprocess.check_output(['git','-C',str(repo),'show',accepted['revision']+':deploy/host/'+name]);assert hashlib.sha256(raw).hexdigest()==digest and (Path('/usr/local/libexec')/name).read_bytes()==raw
    files=load('/usr/local/libexec/whaledeck-files.py','acl_fault_independent_files');module=load('/usr/local/libexec/whaledeck-container-acl.py','acl_fault_independent_module');storage=load('/usr/local/libexec/whaledeck-file-backups.py','acl_fault_independent_hdd').FileBackups(files)
    actual=json.loads(subprocess.check_output(['docker','inspect',*owned.common.CORE_NAMES],timeout=20))
    assert len(actual)==11 and {v['Name'].lstrip('/') for v in actual}==set(owned.common.CORE_NAMES)
    assert all(v['State']['Running'] and v['State'].get('Health',{}).get('Status')=='healthy' for v in actual)
    current=owned.common.core_snapshot();assert len(current)==11;results=[]
    assert accepted['cases'] and len(accepted['cases'])<=2 and len({r['kind'] for r in accepted['cases']})==len(accepted['cases'])
    for result in accepted['cases']:
        kind=result['kind'];assert kind in ('cancel','logout','group-removal','account-disable','worker-crash','agent-restart','post-acl-cancel','post-acl-worker-crash','post-acl-group-removal','post-acl-account-disable','post-acl-agent-restart');p=directory/kind;trusted(p,True)
        baseline=read(p/'baseline.json');ui=read(p/'browser.json');cut=read(p/'cut-root.json');bound=read(p/'bound-helper.json')
        assert baseline['revision']==ui['revision']==accepted['revision'] and baseline['validationSourceRevision']==provenance['revision'] and baseline['kind']==ui['kind']==kind
        assert baseline.get('aclRestoreFaultVersion')==provenance.get('aclRestoreFaultVersion')
        if provenance['schemaVersion'] in (7,8,9,10,11,12):assert baseline['evidence']==str(p)
        post=kind.startswith('post-acl-');behavior=kind.removeprefix('post-acl-')
        revoked=behavior in ('group-removal','account-disable')
        if not revoked:
            assert ui['logout']['normalUiLogout'] and ui['logout']['providerNavigationObserved'] and ui['logout']['bffStatus']==401 and ui['logout']['identityStatus'] in (401,403)
            assert ui['normalDualLogout']
        assert ui['id']==result['id'] and ui['executionJobId']==result['executionJobId'] and ui['actualUi']
        assert ui['registrations']==ui['confirmations']==1 and ui['cancellations']==(1 if behavior=='cancel' else 0) and ui['bodies']==0
        assert 0<=result['productStoppedHelperSeconds']<15 and not result['validatorKilledHelper'] and result['originalFullBytesFingerprintAclsAndAttributesUnchanged'] is (not post)
        content=hashlib.shake_256(('acl-fault:'+baseline['containerId']).encode()).digest(2*1024**2+7)
        assert baseline['bytesPerFile']==len(content) and baseline['sha256']==hashlib.sha256(content).hexdigest().upper()
        data,record,raw,hdd_raw=verify_terminal(result['id'],ui['subject'],baseline,storage,module)
        if kind=='post-acl-agent-restart':
            assert provenance['schemaVersion']==(12 if baseline.get('aclRestoreFaultVersion')==6 else 6) and baseline['jobResultsVersion']==1
            proof=read(p/'task-results.json');verify_task_results(data,baseline,ui,proof)
            for name in ('plan-job.png','execution-job.png'):
                path=p/name;info=trusted(path);assert 1000<info.st_size<4*1024**2 and path.read_bytes().startswith(b'\x89PNG\r\n\x1a\n')
        assert data==read(p/'final-sql.json') and raw==(p/'final-root.json').read_bytes() and hdd_raw==(p/'final-hdd.json').read_bytes()
        physical=read(p/'physical-final.json')
        if baseline.get('aclRestoreFaultVersion')==4:
            from acl_history_browser_proof import typed_equal
            assert ui['intentReadPath']=='/api/v1/containers/'+data['row']['container_id']+'/files/acls/restores/'+data['row']['id']
            typed_equal(ui['restoreTerminalSource'],baseline['restoreSource'])
        if post:
            assert result['postAclIdentityFullBytesAndAclsVerified'] and physical==baseline['cutPhysical']==read(p/'cut-physical.json')
            unchanged_except_target(baseline['original'],physical,'acl-file.bin',record['plan']['targetMode'])
            target=physical['acl-file.bin'];assert target['identity']==record['actualIdentity']==bound['proof']['changedIdentity']
            assert target['identity'][8]>baseline['original']['acl-file.bin']['identity'][8] and target['access']==record['plan']['command']['entries'] and target['default']==record['plan']['defaultAcl']
            late=read(p/'late-apply.json');assert late['operationId']==record['operationId'] and late['state']=='Completed' and late['aclConfirmed'] is True and late['replayed']
            assert late['version']==record['version'] and late['historyDigest']==record['historyDigest']
            intent=verify_current_admin(read(p/'current-admin-read.json'),data['row'],record,ui['subject']) if revoked else ui['terminalIntent'];assert intent['id']==data['row']['id'] and intent['executionJobId']==data['row']['execution_job_id']
            assert intent['state']=='Succeeded' and intent['resultCode']=='CONTAINER_ACL_COMPLETED' and intent['publishedVersion']==record['version'] and intent['aclConfirmed'] is True and intent['confirmation'] is None
            assert intent['historyVerified'] and intent['historyDigest']==record['historyDigest'] and intent['receipt']['id']==data['row']['execution_job_id'] and intent['receipt']['state']=='Completed' and intent['receipt']['bindingCurrent']
            assert intent['receipt']['version']==record['version'] and intent['receipt']['historyDigest']==record['historyDigest']
        else:assert physical==baseline['original']
        assert data['row']['execution_job_id']==ui['executionJobId'] and data['row']['state']==ui['finalState']==result['state']==terminal_state(kind)
        assert bool(data['row']['cancel_requested_at_utc']) is (behavior=='cancel')
        if revoked:verify_authority(baseline,ui,data['row'],result,read(p/'authority-change.json'))
        if behavior in ('worker-crash','agent-restart'):verify_restart(behavior,result,baseline,ui,data['row'],read(p/'restart-start-sql.json'),read,p,post=post)
        assert cut['state'] in (('Applying',) if post else ('Preparing','Prepared')) and cut['aclConfirmed'] is None and cut['historyVerified'] and cut['historyMayExist']
        assert cut['plan']==record['plan'] and cut['operationId']==record['operationId'] and cut['owner']==record['owner'] and cut['historyDigest']==record['historyDigest']
        proof=bound['proof'];assert bound['twoKernelFdProofs'] and re.fullmatch(r'whaledeck-container-acl-[a-f0-9]{32}\.service',bound['unit'])
        assert proof['originalIdentity']==cut['plan']['sourceIdentity']==baseline['original']['acl-file.bin']['identity']
        assert proof['accessAcl']==(cut['plan']['command']['entries'] if post else cut['plan']['accessAcl']) and proof['defaultAcl']==cut['plan']['defaultAcl'] and proof['attributesDigest']==cut['plan']['attributesDigest']
        action='containers.files.acl.restore.result' if provenance['schemaVersion'] in (7,8,9,10,11,12) else 'containers.files.acl.result'
        audit=query("SELECT json_build_object('n',count(*),'actors',json_agg(actor_subject),'results',json_agg(result)) FROM audit_events WHERE job_id=:'id'::uuid AND action=:'action';",id=ui['executionJobId'],action=action)
        assert audit=={'n':1,'actors':[ui['subject']],'results':[terminal_state(kind)]}
        source=Path(baseline['source']);assert source.parent==owned.common.APPS and source.name==baseline['containerName'] and not os.path.lexists(source)
        missing=subprocess.run(['docker','inspect',baseline['containerId']],capture_output=True,timeout=10);assert missing.returncode!=0 and b'no such' in missing.stderr.lower()
        assert current.keys()==baseline['coreBefore'].keys()
        for name,prior in baseline['coreBefore'].items():assert list(current[name][:3] if name=='/whaledeck-worker' else current[name])==(prior[:3] if name=='/whaledeck-worker' else prior)
        results.append({'kind':kind,'id':ui['id'],'rootSha256':hashlib.sha256(raw).hexdigest(),'hddSha256':hashlib.sha256(hdd_raw).hexdigest()})
    no_active_work()
    with requests.Session() as api:
        api.trust_env=False;api.headers['Authorization']='Bearer '+require(container_environment(),'Authentication__Authentik__ApiToken')
        for prefix in ('whaledeck-validation-','whaledeck-upload-qa-'):
            r=api.get(AUTHENTIK_API+'core/users/',params={'search':prefix,'page_size':100},timeout=20);assert r.status_code==200 and not r.json().get('pagination',{}).get('next') and not any(str(u['username']).startswith(prefix) for u in r.json()['results'])
    return {'independentReadOnlyVerifier':True,'revision':accepted['revision'],'cases':results,'hddRecords':len(results),'exactSourcesAndContainersAbsent':True,'temporaryIdentitiesAbsent':True,'coreHealthy':11,'physicalFullBytesAndAclsVerifiedBeforeCleanupByParent':True}


if __name__=='__main__':
    try:
        assert os.geteuid()==0 and len(sys.argv)==2;print(json.dumps(verify(Path(sys.argv[1]))))
    except BaseException as error:
        import traceback
        print(json.dumps({'aclFaultIndependentVerifier':'failed','failureType':type(error).__name__,'locations':[{'file':Path(f.filename).name,'line':f.lineno} for f in traceback.extract_tb(error.__traceback__)]}));raise SystemExit(1) from None
