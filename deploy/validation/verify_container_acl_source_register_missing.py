#!/usr/bin/env python3
"""Independent product-read-only verifier of first-registration source-loss refusal."""
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
from acl_source_register_missing_proof import KIND,manifest,intent_id,no_registration,require_rejected
from validate_container_acl_source_missing import INSTALLED
from validate_container_acl_stale import no_operation_history
from validate_container_acl_faults import host_snapshot
from validate_container_acl_history_browser import immutable
from validate_container_file_staging import load
from validate_container_upload_recovery_agent import trusted
from owned_acl_source_quarantine import installed_namespace,catalogue,OwnedSourceQuarantine
from run_with_ephemeral_admin import AUTHENTIK_API,container_environment,require


def verify(name):
    assert re.fullmatch('container-acl-source-register-missing-[a-f0-9]{32}',name)
    repo=Path(__file__).resolve().parents[2];evidence=repo/'deploy/artifacts'/name;trusted(evidence,True)
    def read(leaf):
        p=evidence/leaf;i=trusted(p);assert i.st_mode&0o777==0o600 and i.st_size<=4*1024**2
        return json.loads(p.read_bytes())
    b=read('baseline.json');proof=read('capture-proof.json');ui=read('browser.json');accepted=read('verified.json')
    assert type(b['aclSourceFaultVersion']) is int and b['aclSourceFaultVersion']==ui['aclSourceFaultVersion']==4
    assert type(b['dependencyManifestVersion']) is int and b['dependencyManifestVersion']==1
    assert b['kind']==ui['kind']==accepted['kind']==KIND and b['revision']==ui['revision']==accepted['revision']
    assert b['validationSourceSha256']==manifest(repo,b['validationSourceRevision'])
    assert set(b['installedSourceSha256'])==set(INSTALLED)
    for leaf,sha in b['installedSourceSha256'].items():
        raw=subprocess.check_output(['git','-C',str(repo),'show',b['revision']+':deploy/host/'+leaf]);assert (Path('/usr/local/libexec')/leaf).read_bytes()==raw and hashlib.sha256(raw).hexdigest()==sha
    capture_sql=read('capture/access.sql.json');row=capture_sql['row']
    assert b['subject']==row['actor_subject']==ui['subject'] and row['id']==b['captureIntentId']
    assert intent_id(row['actor_subject'],row['login_binding'],row['idempotency_key'])==row['id']
    rejected=read('rejected.json');require_rejected(b,rejected)
    assert rejected['key']==ui['key']
    absent=no_registration(b['subject'],row['login_binding'],ui['key']);identifier=absent['expectedIntentId']
    assert absent==read('absent-after-rejection.json')==read('absent-after-query.json')
    assert accepted['expectedIntentId']==identifier and accepted['executionJobId'] is None
    assert read('selected.json')==dict(subject=b['subject'],source=b['restoreSource'])
    assert read('queried.json')==dict(key=ui['key'],sourceReadableAgain=True,byKeyStill404=True,jobStatuses=[404,404])
    assert (ui['registrations'],ui['confirmations'],ui['cancellations'],ui['contentWrites'],ui['jobWrites'])==(1,0,0,0,0)
    assert ui['events']==['restore-registration'] and ui['sourceMissingStatus']==404 and ui['registrationStatus']==409 and ui['code']=='OPERATION_CONFLICT'
    for key in ('actualUi','normalDualLogout','sourceReadableAgain','originalStillMissing','hintsCleared'):assert ui[key] is True
    logout=ui['logout'];assert logout['normalUiLogout'] and logout['providerNavigationObserved'] and logout['bffStatus']==401 and logout['identityStatus'] in (401,403)
    assert read('denials.json')==dict(anonymousReadsDenied=2,ordinaryReadsDenied=2,normalOrdinaryDualLogout=True)
    assert [(t['width'],t['colorScheme']) for t in ui['themes']]==[(320,'dark'),(1440,'light')]
    screenshots=['registration-rejected-320-dark.png']
    for case in ui['themes']:
        assert case['originalMissingVisible'] is case['noHorizontalOverflow'] is True
        assert case['screenshot']==f"registration-not-found-{case['width']}-{case['colorScheme']}.png";screenshots.append(case['screenshot'])
    for leaf in screenshots:
        p=evidence/leaf;i=trusted(p);assert i.st_mode&0o777==0o600 and 1000<i.st_size<4*1024**2 and p.read_bytes().startswith(b'\x89PNG\r\n\x1a\n')
    assert read('physical-final.json')==b['original']==proof['after']
    content=hashlib.shake_256(('source-register-missing:'+b['containerId']).encode()).digest(2*1024**2+7)
    assert b['bytesPerFile']==len(content) and b['sha256']==hashlib.sha256(content).hexdigest().upper()
    files=load('/usr/local/libexec/whaledeck-files.py','apply_missing_independent_files');module=load('/usr/local/libexec/whaledeck-container-acl.py','apply_missing_independent_acl')
    storage=load('/usr/local/libexec/whaledeck-file-backups.py','apply_missing_independent_storage').FileBackups(files)
    observed_capture=evidence/'independent-capture';observed_capture.mkdir(mode=0o700,exist_ok=True)
    actual=acl.verify_case(proof['result'],{**b,'original':proof['before']},b['subject'],observed_capture,None,module,storage,proof['before'],proof['after'])
    assert actual==proof and b['captureIntentId']==proof['result']['id'];immutable(evidence/'capture',b,[proof],storage)
    with installed_namespace(storage) as (fd,attest):
        after=read('restored-hdd.json');old=read('old-hdd.json');own=b['restoreSource']['entry']['captureId']+'.json'
        assert catalogue(fd)==after and {n:v for n,v in after.items() if n!=own}==old
        journal=os.open(evidence,os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW)
        try:
            state=OwnedSourceQuarantine(fd,journal,attest).state();assert state['old']==old and state['captureId']+'.json'==own
            OwnedSourceQuarantine.same_original(after[own],state['original'])
            assert not os.path.lexists('/proc/self/fd/'+str(fd)+'/'+state['quarantine'])
        finally:os.close(journal)
    no_operation_history(identifier,storage)
    assert acl.owned.common.core_snapshot()=={k:tuple(v) for k,v in b['coreBefore'].items()}
    assert host_snapshot(b['revision'])==b['hostsBefore'];acl.no_active_work()
    folder=Path(b['source']);assert folder.parent==acl.owned.common.APPS and folder.name==b['containerName'] and not os.path.lexists(folder)
    missing=subprocess.run(['docker','inspect',b['containerId']],capture_output=True,timeout=10);assert missing.returncode!=0 and b'no such' in missing.stderr.lower()
    containers=json.loads(subprocess.check_output(['docker','inspect',*acl.owned.common.CORE_NAMES],timeout=20));assert len(containers)==11 and all(c['State']['Running'] and c['State'].get('Health',{}).get('Status')=='healthy' for c in containers)
    assert accepted['oldCapturesUnchanged']==len(old) and accepted['sourceCaptureCount']==1
    for key in ('actualUi','normalDualLogout','noRegistration','noExecution','noRootOrHdd','originalSourceRestored','physicalCheckedBeforeCleanup','exactSourceAndContainerRemoved'):assert accepted[key] is True
    with requests.Session() as api:
        api.trust_env=False;api.headers['Authorization']='Bearer '+require(container_environment(),'Authentication__Authentik__ApiToken')
        for prefix in ('whaledeck-validation-','whaledeck-upload-qa-'):
            r=api.get(AUTHENTIK_API+'core/users/',params={'search':prefix,'page_size':100},timeout=20)
            assert r.status_code==200 and not r.json().get('pagination',{}).get('next') and not any(str(u['username']).startswith(prefix) for u in r.json()['results'])
    return dict(independentReadOnlyVerifier=True,kind=KIND,expectedIntentId=identifier,revision=b['revision'],registration409=True,
        noRegistration=True,noRestoreJobRootOrHdd=True,oldCapturesUnchanged=len(old),originalCaptureRestored=True,actualErrorScreenshots=3,coreHealthy=11,
        temporaryIdentitiesAbsent=True,physicalCheckedByParentBeforeCleanup=True,deletedSourceBytesReread=False)




if __name__=='__main__':
    try:assert os.geteuid()==0 and len(sys.argv)==2;os.umask(0o077);print(json.dumps(verify(sys.argv[1])))
    except BaseException as error:
        print(json.dumps(dict(independentSourceRegisterMissing='failed',failureType=type(error).__name__,locations=[dict(file=Path(f.filename).name,line=f.lineno) for f in traceback.extract_tb(error.__traceback__)])))
        raise SystemExit(1) from None
