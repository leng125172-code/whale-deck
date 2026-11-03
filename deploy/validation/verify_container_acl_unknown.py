#!/usr/bin/env python3
"""Independent read-only ACL Unknown/explicit ack/shared barrier verification."""
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import uuid
import datetime as dt

import requests
from validate_container_acl_unknown import SOURCES, WAITING_PATH, save_facts, require_blocked, snapshot, directory_snapshot, no_active_work, load, owned, host_snapshot, expected_physical, require_cut, verify_unknown_jobs, require_instance_transition
from validate_metric_continuity import query
from validate_container_upload_recovery_agent import trusted
from run_with_ephemeral_admin import AUTHENTIK_API, container_environment, require
from validate_container_acl_faults import RESTART_KEYS

IMMUTABLE=('receipt_json','receipt_hash','acl_confirmed','history_may_exist','history_verified','history_digest','published_version','actor_subject','login_binding','execution_job_id','plan_json','command_json')


def require_source_version(source,baseline):
    version=source['proofVersion'];assert type(version) is int and version in (2,3,4,5)
    if 'aclRestoreFaultVersion' in baseline:
        from acl_restore_unknown_proof import require_restore_unknown
        require_restore_unknown(baseline)
        assert version==5 and type(source.get('aclRestoreFaultVersion')) is int and source['aclRestoreFaultVersion']==7
        return
    assert 'aclRestoreFaultVersion' not in source and version!=5
    if baseline.get('instanceChanged',False):assert baseline['instanceChanged'] is True and baseline['postAcl'] is True and version==4
    elif baseline.get('postAcl',False):assert baseline['postAcl'] is True and version==3
    else:assert version==2


def require_acknowledgement(original, final, old_subject, new_subject):
    old,row=original['row'],final['row'];assert old_subject!=new_subject and old['actor_subject']==row['actor_subject']==old_subject
    assert old['state']=='Unknown' and old['publish_barrier'] and old['completed_at_utc'] is None and old['acl_confirmed'] is None and original['executionJob']['state']=='Running'
    assert row['state']=='Acknowledged' and final['executionJob']['state']=='Failed' and final['planJob']['state']=='Succeeded' and row['acknowledged_by']==new_subject
    assert row['completed_at_utc'] and not row['publish_barrier'] and row['lease_token'] is None and row['lease_expires_at_utc'] is None and final['pending']==0
    assert row['result_code']=='CONTAINER_ACL_UNKNOWN_ACKNOWLEDGED'
    for key in IMMUTABLE:assert row[key]==old[key]
    assert row.get('restore_source_json')==old.get('restore_source_json')
    if old.get('restore_source_json') is not None:
        for key in ('restore_source_json','request_hash','plan_hash','execution_hash','owner_hash'):assert row[key]==old[key]
    assert row['acl_confirmed'] is None and row['published_version'] is None and row['history_verified'] and row['history_may_exist']


def require_deferrals(samples, waiting, actor, released):
    assert len(samples)==4;versions=[];attempts=[];times=[]
    for sample in samples:
        at=dt.datetime.fromisoformat(sample['observedAtUtc']);assert at.utcoffset() is not None and at<released
        assert require_blocked(sample['sql'],waiting,actor,at)
        assert sample['rootAbsent'] and sample['targetAbsent'] and sample['aclUnknownProtected'] and sample['sourceUnchanged']
        versions.append(sample['sql']['row']['version']);attempts.append(sample['sql']['row']['next_attempt_at_utc']);times.append(at)
    assert len(set(versions))==len(set(attempts))==4 and versions==sorted(versions) and times==sorted(times)
    assert all((dt.datetime.fromisoformat(b)-dt.datetime.fromisoformat(a)).total_seconds()>=5 for a,b in zip(attempts,attempts[1:]))
    assert all(later>=dt.datetime.fromisoformat(due) for later,due in zip(times[1:],attempts[:-1]))
    assert all((later-earlier).total_seconds()>=5 for earlier,later in zip(times,times[1:]))


def verify(directory):
    repo=Path(__file__).resolve().parents[2];assert directory.parent==repo/'deploy/artifacts' and re.fullmatch('container-acl-unknown-[a-f0-9]{32}',directory.name);trusted(directory,True)
    def read(path):
        info=trusted(path);assert info.st_size<=262144 and info.st_mode&0o777==0o600;return json.loads(path.read_bytes())
    baseline=read(directory/'baseline.json');accepted=read(directory/'verified.json');source=read(directory/'validation-source.json');owner=read(directory/'owner-browser.json');recovery=read(directory/'recovery-browser.json')
    assert accepted['revision']==baseline['revision']==owner['revision']==recovery['revision']==source['productRevision']
    require_source_version(source,baseline);post=baseline.get('postAcl',False)
    expected_sources=SOURCES
    if 'aclRestoreFaultVersion' in baseline:
        from acl_restore_unknown_proof import restore_unknown_sources
        expected_sources=restore_unknown_sources()
    assert baseline['validationSourceRevision']==source['revision'] and set(source['sha256'])==set(expected_sources)
    subprocess.run(['git','-C',str(repo),'merge-base','--is-ancestor',baseline['revision'],source['revision']],check=True,capture_output=True,timeout=15)
    for name,sha in source['sha256'].items():
        assert '/' not in name;raw=subprocess.check_output(['git','-C',str(repo),'show',source['revision']+':deploy/validation/'+name]);assert hashlib.sha256(raw).hexdigest()==sha
    for name,sha in source['installedSha256'].items():
        assert '/' not in name;raw=subprocess.check_output(['git','-C',str(repo),'show',baseline['revision']+':deploy/host/'+name]);assert (Path('/usr/local/libexec')/name).read_bytes()==raw and hashlib.sha256(raw).hexdigest()==sha
    for ui in (owner,recovery):
        assert ui['actualUi'] and ui['normalDualLogout'] and ui['logout']['normalUiLogout'] and ui['logout']['providerNavigationObserved']
        assert ui['logout']['bffStatus']==401 and ui['logout']['identityStatus'] in (401,403)
    assert owner['registrations']==owner['confirmations']==recovery['confirmations']==1 and owner['bodies']==recovery['registrations']==recovery['bodies']==0
    assert owner['subject']==accepted['originalSubject']==baseline['originalSubject']!=recovery['subject']==accepted['recoverySubject'] and recovery['waitingVisibleInVue'] and recovery['waitingDirectoryInViewport']
    if 'aclRestoreFaultVersion' in baseline:
        from acl_history_browser_proof import typed_equal
        typed_equal(owner['restoreSource'],baseline['restoreSource'])
        assert type(recovery['ownerRestoreStatus']) is int and recovery['ownerRestoreStatus']==404 and recovery['recoveryNoStore'] is True
    assert recovery['displayedContainerId']==baseline['containerId'] and recovery['displayedPath']=='/work/acl-file.bin'
    assert 0<=accepted['productStoppedHelperSeconds']<15 and accepted['validatorKilledHelper'] is False
    if baseline.get('instanceChanged',False):
        transition=read(directory/'instance-transition.json');require_instance_transition(baseline,transition)
        assert transition['commandAcknowledgement']==baseline['containerId']
        response=read(directory/'instance-restart-response.json');assert response=={k:v for k,v in transition.items() if k!='otherActiveWork'}
        assert read(directory/'instance-restart-request.json')=={'containerId':baseline['containerId'],'before':transition['before'],'restartAttemptedOnce':True}
        assert read(directory/'instance-after-ack.json')==transition['after']
        assert not (directory/'removed-owned.json').exists() and read(directory/'logout.json')=={'ownedContainerRestarted':True}
        assert RESTART_KEYS<=set(transition['otherActiveWork']) and all(type(n) is int and n==0 for n in transition['otherActiveWork'].values())
        assert accepted['instanceChanged'] is True
    else:
        removed=read(directory/'removed-owned.json');expected_removed={'containerId':baseline['containerId'],'onlyExactOwnedContainerRemoved':True,'helperAliveBeforeLogout':True,'sourceFullInventoryUnchanged':True}
        if post:expected_removed.update(sourceInventoryReference='post-acl-cut',sourceInventory=expected_physical(baseline))
        assert removed==expected_removed
    original=read(directory/'unknown-sql.json');final=snapshot(accepted['id']);assert final==read(directory/'final-sql.json')
    require_acknowledgement(original,final,owner['subject'],recovery['subject']);row=final['row'];assert row['id']==owner['id']==recovery['id']==baseline['id'] and row['execution_job_id']==owner['executionJobId']==baseline['executionJobId']
    if post:
        verify_unknown_jobs(original,read(directory/'unknown-jobs.json'),'unknown');verify_unknown_jobs(final,read(directory/'acknowledged-jobs.json'),'acknowledged')
        assert recovery['jobReadsOnly'] is True
        for stage in ('unknown','acknowledged'):
            for suffix in ('-job.png','-job-facts.png'):
                path=directory/(stage+suffix);info=trusted(path);assert 1000<info.st_size<4*1024**2 and path.read_bytes().startswith(b'\x89PNG\r\n\x1a\n')
    files=load('/usr/local/libexec/whaledeck-files.py','acl_unknown_independent_files');module=load('/usr/local/libexec/whaledeck-container-acl.py','acl_unknown_independent_module');storage=load('/usr/local/libexec/whaledeck-file-backups.py','acl_unknown_independent_hdd').FileBackups(files)
    record,raw,hdd=save_facts(final,baseline,storage,module);assert raw==(directory/'unknown-root.json').read_bytes()==(directory/'final-root.json').read_bytes() and hdd==(directory/'unknown-hdd.json').read_bytes()==(directory/'final-hdd.json').read_bytes()
    assert hashlib.sha256(raw).hexdigest()==accepted['rootSha256'] and record['state']=='Unknown' and record['aclConfirmed'] is None
    cut=read(directory/'cut-root.json');bound=read(directory/'bound-helper.json');assert cut['aclConfirmed'] is None and cut['historyVerified']
    assert cut['plan']==record['plan'] and cut['owner']==record['owner'] and cut['operationId']==record['operationId'] and cut['historyDigest']==record['historyDigest']
    proof=bound['proof'];assert bound['twoKernelFdProofs'] and re.fullmatch(r'whaledeck-container-acl-[a-f0-9]{32}\.service',bound['unit'])
    require_cut(baseline,cut,proof,expected_physical(baseline))
    assert read(directory/'physical-final.json')==expected_physical(baseline) and accepted['originalFullBytesFingerprintAclsAndAttributesUnchanged'] is (not post)
    if post:assert accepted['postAclPhysicalInventoryPreserved'] is True
    content=hashlib.shake_256(('acl-unknown:'+baseline['containerId']).encode()).digest(2*1024**2+7);assert baseline['bytesPerFile']==len(content) and baseline['sha256']==hashlib.sha256(content).hexdigest().upper()
    late=read(directory/'late-apply.json');assert late['operationId']==record['operationId'] and late['state']=='Unknown' and late['aclConfirmed'] is None and late['replayed'] and late['version']=='' and late['historyDigest']==row['history_digest']
    action='containers.files.acl.'+('restore.' if 'aclRestoreFaultVersion' in baseline else '')+'unknown-acknowledged'
    audit=query("SELECT json_build_object('n',count(*),'actors',json_agg(actor_subject),'results',json_agg(result),'targets',json_agg(target_id)) FROM audit_events WHERE job_id=:'id'::uuid AND action=:'action';",id=row['execution_job_id'],action=action)
    assert audit=={'n':1,'actors':[recovery['subject']],'results':['Unknown'],'targets':[baseline['containerId']]}
    dismissed=read(directory/'recovery-dismissed.json');assert dismissed=={'id':row['id'],'subject':recovery['subject'],'confirmations':0,'stillUnknown':True}
    assert read(directory/'allow-ack.json')=={'originalStillUnknownProtected':True,'waitingStillDeferred':True} and recovery['ackVersion']==original['row']['version']
    waiting_baseline=read(directory/'waiting-baseline.json');waiting=directory_snapshot(accepted['waitingId']);assert waiting==read(directory/'waiting-final-sql.json')
    w=waiting['row'];assert w['state']==waiting['job']['state']=='Succeeded' and w['root_started_at_utc']>=row['completed_at_utc'] and w['actor_subject']==recovery['subject'] and w['relative_path']==WAITING_PATH
    assert w['container_id']==waiting_baseline['containerId']!=baseline['containerId'] and waiting_baseline['subject']==recovery['subject']
    require_deferrals(read(directory/'waiting-blocked.json'),{'id':w['id'],'containerId':w['container_id']},recovery['subject'],dt.datetime.fromisoformat(row['completed_at_utc']))
    operation=uuid.UUID(w['id']).hex;directory_root=Path('/var/lib/whaledeck-container-directories')/(operation+'.json');trusted(directory_root)
    assert directory_root.read_bytes()==(directory/(operation+'.root.json')).read_bytes();root=json.loads(directory_root.read_bytes())
    assert root['state']=='Completed' and root['operationId']==operation and root['owner']==w['owner_hash'] and root['version']==w['published_version'] and root['plan']['command']=={'containerId':w['container_id'],'instanceId':w['instance_id'],'path':WAITING_PATH,'mode':'0750'}
    for b in (baseline,waiting_baseline):
        path=Path(b['source']);assert path.parent==owned.common.APPS and re.fullmatch('wdv-container-publish-[a-f0-9]{32}',path.name) and not os.path.lexists(path)
        missing=subprocess.run(['docker','inspect',b['containerId']],capture_output=True,timeout=10);assert missing.returncode!=0 and b'no such' in missing.stderr.lower()
    actual=json.loads(subprocess.check_output(['docker','inspect',*owned.common.CORE_NAMES],timeout=20));assert len(actual)==11 and all(v['State']['Running'] and v['State'].get('Health',{}).get('Status')=='healthy' for v in actual)
    current=owned.common.core_snapshot()
    for name,before in baseline['coreBefore'].items():assert list(current[name][:3] if name=='/whaledeck-worker' else current[name])==(before[:3] if name=='/whaledeck-worker' else before)
    no_active_work();assert host_snapshot(baseline['revision'])==baseline['hostsBefore']
    with requests.Session() as api:
        api.trust_env=False;api.headers['Authorization']='Bearer '+require(container_environment(),'Authentication__Authentik__ApiToken')
        for prefix in ('whaledeck-validation-','whaledeck-upload-qa-'):
            r=api.get(AUTHENTIK_API+'core/users/',params={'search':prefix,'page_size':100},timeout=20);assert r.status_code==200 and not r.json().get('pagination',{}).get('next') and not any(str(u['username']).startswith(prefix) for u in r.json()['results'])
    return {'independentReadOnlyVerifier':True,'revision':baseline['revision'],'id':row['id'],'aclConfirmed':None,'originalExecutionFailed':True,'rootUnknownUnchanged':True,'originalHddUnchanged':True,'sameCidInstanceChanged':baseline.get('instanceChanged',False),
        'differentAdministratorActualPage':True,'fourNaturalDeferrals':True,'actualWaitingDirectoryVisibleInVue':True,'lateApplyRemainsUnknown':True,'exactSourcesAndContainersAbsent':True,'temporaryIdentitiesAbsent':True,'coreHealthy':11,'physicalFullBytesAndAclsVerifiedByParentBeforeCleanup':True}


if __name__=='__main__':
    try:
        assert os.geteuid()==0 and len(sys.argv)==2;print(json.dumps(verify(Path(sys.argv[1]))))
    except BaseException as error:
        import traceback
        print(json.dumps({'aclUnknownIndependentVerifier':'failed','failureType':type(error).__name__,'locations':[{'file':Path(f.filename).name,'line':f.lineno} for f in traceback.extract_tb(error.__traceback__)]}));raise SystemExit(1) from None
