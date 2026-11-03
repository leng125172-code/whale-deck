#!/usr/bin/env python3
"""Independent product-read-only verifier of first-Apply valid source-replacement fencing."""
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import traceback
import requests
import validate_container_acl_browser as acl
from acl_source_apply_replaced_proof import KIND,CODE,manifest,require_pending,require_failed,summary,verify_audits,worker_unchanged_except_start,require_replacement_observed
from owned_acl_source_replacement import OwnedSourceReplacement,changed_metadata
from validate_container_acl_source_missing import require_ready,INSTALLED
from bound_container_acl_cut import record_for
from validate_container_acl_faults import host_snapshot
from validate_container_acl_history_browser import immutable
from validate_container_file_staging import load
from validate_container_upload_recovery_agent import trusted
from owned_acl_source_quarantine import installed_namespace,catalogue,OwnedSourceQuarantine
from run_with_ephemeral_admin import AUTHENTIK_API,container_environment,require


def verify_fixture_recipe(baseline):
    assert re.fullmatch('[a-f0-9]{64}',baseline['containerId'])
    assert type(baseline['bytesPerFile']) is int and baseline['bytesPerFile']==2*1024**2+7
    content=hashlib.shake_256(('source-apply-replaced:'+baseline['containerId']).encode()).digest(baseline['bytesPerFile'])
    assert hashlib.sha256(content).hexdigest().upper()==baseline['sha256']
    return content


def verify(name):
    assert re.fullmatch('container-acl-source-apply-replaced-[a-f0-9]{32}',name)
    repo=Path(__file__).resolve().parents[2];evidence=repo/'deploy/artifacts'/name;trusted(evidence,True)
    def read(leaf):
        p=evidence/leaf;i=trusted(p);assert i.st_mode&0o777==0o600 and i.st_size<=4*1024**2
        return json.loads(p.read_bytes())
    b=read('baseline.json');proof=read('capture-proof.json');ui=read('browser.json');accepted=read('verified.json')
    assert type(b['aclSourceFaultVersion']) is int and b['aclSourceFaultVersion']==ui['aclSourceFaultVersion']==6
    assert type(b['dependencyManifestVersion']) is int and b['dependencyManifestVersion']==1
    assert b['kind']==ui['kind']==accepted['kind']==KIND and b['revision']==ui['revision']==accepted['revision']
    assert b['validationSourceSha256']==manifest(repo,b['validationSourceRevision'])
    assert set(b['installedSourceSha256'])==set(INSTALLED)
    for leaf,sha in b['installedSourceSha256'].items():
        raw=subprocess.check_output(['git','-C',str(repo),'show',b['revision']+':deploy/host/'+leaf]);assert (Path('/usr/local/libexec')/leaf).read_bytes()==raw and hashlib.sha256(raw).hexdigest()==sha
    ready=read('ready-sql.json');pending=read('pending-sql.json');final=acl.snapshot(ready['row']['id'])
    require_ready(ready,b);require_pending(pending,ready);record=record_for(final['row']);require_failed(final,pending,record)
    assert final==read('failed-sql.json') and record==read('failed-root.json')
    root_raw=(acl.STATE/(record['operationId']+'.json')).read_bytes();assert root_raw==(evidence/'failed-root.json').read_bytes()
    assert final['row']['id']==ui['id']==accepted['id'] and final['row']['execution_job_id']==ui['executionJobId']==accepted['executionJobId']
    assert b['subject']==ui['subject']==final['row']['actor_subject']
    observed=read('replaced-observed.json');confirmed=read('confirmed.json');terminal=read('terminal.json')
    assert confirmed['id']==observed['id']==ui['id'] and confirmed['status']==202
    require_replacement_observed(b['restoreSource'],read('replacement-proof.json'),observed,ui['id'])
    assert observed['pending']==observed['byKey']['intent']==confirmed['pending'] and observed['byKey']['source']==b['restoreSource']
    assert terminal['id']==ui['id'] and terminal['final']['source']==b['restoreSource'] and terminal['final']['intent']['aclConfirmed'] is False
    acl.validate_job_summary(terminal['job'],summary(final['row']),final['row']['execution_job_id'],'Failed')
    assert (ui['registrations'],ui['confirmations'],ui['cancellations'],ui['contentWrites'],ui['jobWrites'])==(1,1,0,0,0)
    assert ui['events']==['restore-registration','restore-confirm'] and ui['originalSourceReadStatus']==409 and ui['replacementReadStatus']==200 and ui['finalState']=='Failed' and ui['finalCode']==CODE
    assert ui['actualUi'] is ui['normalDualLogout'] is ui['sourceReadableAgain'] is True
    logout=ui['logout'];assert logout['normalUiLogout'] and logout['providerNavigationObserved'] and logout['bffStatus']==401 and logout['identityStatus'] in (401,403)
    assert read('denials.json')==dict(anonymousReadsDenied=2,ordinaryReadsDenied=2,normalOrdinaryDualLogout=True)
    assert [(t['width'],t['colorScheme']) for t in ui['taskCenter']]==[(320,'dark'),(1440,'light')]
    screenshots=['source-apply-failed-mobile-dark.png']
    for case in ui['taskCenter']:
        assert case['id']==final['row']['execution_job_id'] and case['confirmedFalseVisible'] is case['noGenericWriteControls'] is True
        for key,part in [('titleScreenshot','title'),('factsScreenshot','facts')]:
            assert case[key]==f"failed-execution-{part}-{case['width']}-{case['colorScheme']}.png";screenshots.append(case[key])
    for leaf in screenshots:
        p=evidence/leaf;i=trusted(p);assert i.st_mode&0o777==0o600 and 1000<i.st_size<4*1024**2 and p.read_bytes().startswith(b'\x89PNG\r\n\x1a\n')
    assert read('physical-final.json')==b['original']==proof['after']
    verify_fixture_recipe(b)
    files=load('/usr/local/libexec/whaledeck-files.py','apply_missing_independent_files');module=load('/usr/local/libexec/whaledeck-container-acl.py','apply_missing_independent_acl')
    storage=load('/usr/local/libexec/whaledeck-file-backups.py','apply_missing_independent_storage').FileBackups(files)
    observed_capture=evidence/'independent-capture';observed_capture.mkdir(mode=0o700,exist_ok=True)
    actual=acl.verify_case(proof['result'],{**b,'original':proof['before']},b['subject'],observed_capture,None,module,storage,proof['before'],proof['after'])
    assert actual==proof and b['captureIntentId']==proof['result']['id'];immutable(evidence/'capture',b,[proof],storage)
    with installed_namespace(storage) as (fd,attest):
        after=read('restored-hdd.json');old=read('old-hdd.json');own=b['restoreSource']['entry']['captureId']+'.json'
        assert catalogue(fd)==after and {n:v for n,v in after.items() if n!=own}==old and record['operationId']+'.json' not in after
        journal=os.open(evidence,os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW)
        try:
            state=OwnedSourceQuarantine(fd,journal,attest).state();assert state['old']==old and state['captureId']+'.json'==own
            OwnedSourceQuarantine.same_original(after[own],state['original'])
            assert not os.path.lexists('/proc/self/fd/'+str(fd)+'/'+state['quarantine'])
            tool=OwnedSourceReplacement(OwnedSourceQuarantine(fd,journal,attest));_,replacement=tool.state()
            assert not os.path.lexists('/proc/self/fd/'+str(fd)+'/'+replacement['stage'])
            tool.verify_archive(replacement)
        finally:os.close(journal)
    assert (evidence/'replacement-metadata.json').read_bytes()==changed_metadata((evidence/'capture/access.hdd.json').read_bytes())
    late=read('late-apply.json');assert late['operationId']==record['operationId'] and late['state']=='Aborted' and late['replayed'] is True
    assert late['aclConfirmed'] is late['historyMayExist'] is late['historyVerified'] is False and late['historyDigest']==late['version']==''
    verify_audits(read('restore-audits.json'),final['row'])
    stopped=read('worker-stopped.json');assert stopped['running'] is False and stopped['id']==b['coreBefore']['/whaledeck-worker'][0] and stopped['image']==b['coreBefore']['/whaledeck-worker'][1]
    worker_unchanged_except_start(b['coreBefore'],acl.owned.common.core_snapshot());assert host_snapshot(b['revision'])==b['hostsBefore'];acl.no_active_work()
    folder=Path(b['source']);assert folder.parent==acl.owned.common.APPS and folder.name==b['containerName'] and not os.path.lexists(folder)
    missing=subprocess.run(['docker','inspect',b['containerId']],capture_output=True,timeout=10);assert missing.returncode!=0 and b'no such' in missing.stderr.lower()
    containers=json.loads(subprocess.check_output(['docker','inspect',*acl.owned.common.CORE_NAMES],timeout=20));assert len(containers)==11 and all(c['State']['Running'] and c['State'].get('Health',{}).get('Status')=='healthy' for c in containers)
    assert accepted['oldCapturesUnchanged']==len(old) and accepted['sourceCaptureCount']==1
    for key in ('actualUi','normalDualLogout','originalExecutionAborted','noRestoreCurrentHdd','rootStartedNeverMarked','originalSourceRestored','physicalCheckedBeforeCleanup','exactSourceAndContainerRemoved'):assert accepted[key] is True
    with requests.Session() as api:
        api.trust_env=False;api.headers['Authorization']='Bearer '+require(container_environment(),'Authentication__Authentik__ApiToken')
        for prefix in ('whaledeck-validation-','whaledeck-upload-qa-'):
            r=api.get(AUTHENTIK_API+'core/users/',params={'search':prefix,'page_size':100},timeout=20)
            assert r.status_code==200 and not r.json().get('pagination',{}).get('next') and not any(str(u['username']).startswith(prefix) for u in r.json()['results'])
    return dict(independentReadOnlyVerifier=True,kind=KIND,id=ui['id'],executionJobId=ui['executionJobId'],revision=b['revision'],originalRootAborted=True,originalDigestRead409=True,replacementDigestRead200=True,
        rootStartedNeverMarked=True,noCurrentHddBeforeImage=True,oldCapturesUnchanged=len(old),originalCaptureRestored=True,lateApplyPermanentlyAborted=True,
        actualTaskDrawers=2,coreHealthy=11,temporaryIdentitiesAbsent=True,physicalCheckedByParentBeforeCleanup=True,deletedSourceBytesReread=False)


if __name__=='__main__':
    try:assert os.geteuid()==0 and len(sys.argv)==2;os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0';print(json.dumps(verify(sys.argv[1])))
    except BaseException as error:
        print(json.dumps(dict(independentSourceApplyReplaced='failed',failureType=type(error).__name__,locations=[dict(file=Path(f.filename).name,line=f.lineno) for f in traceback.extract_tb(error.__traceback__)])))
        raise SystemExit(1) from None
