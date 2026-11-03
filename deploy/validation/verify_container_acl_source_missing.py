#!/usr/bin/env python3
"""Independent product-read-only proof of the one Ready/confirm source-loss case."""
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
from validate_container_acl_source_missing import SOURCES, INSTALLED, KIND, require_ready, require_canceled, verify_audits
from validate_container_acl_stale import no_operation_history
from validate_container_acl_faults import host_snapshot
from validate_container_acl_history_browser import immutable
from validate_container_file_staging import load
from validate_container_upload_recovery_agent import trusted
from owned_acl_source_quarantine import installed_namespace, catalogue, OwnedSourceQuarantine
from run_with_ephemeral_admin import AUTHENTIK_API,container_environment,require


def verify(name):
    assert re.fullmatch('container-acl-source-missing-[a-f0-9]{32}',name)
    repo=Path(__file__).resolve().parents[2];evidence=repo/'deploy/artifacts'/name;trusted(evidence,True)
    def read(leaf):
        p=evidence/leaf;i=trusted(p);assert i.st_mode&0o777==0o600 and i.st_size<=4*1024**2
        return json.loads(p.read_bytes())
    baseline=read('baseline.json');proof=read('capture-proof.json');ui=read('browser.json');passed=read('verified.json')
    assert type(baseline['aclSourceFaultVersion']) is int and baseline['aclSourceFaultVersion']==ui['aclSourceFaultVersion']==1
    assert baseline['kind']==ui['kind']==passed['kind']==KIND and baseline['subject']==ui['subject']
    assert baseline['revision']==ui['revision']==passed['revision']
    assert set(baseline['validationSourceSha256'])==set(SOURCES) and set(baseline['installedSourceSha256'])==set(INSTALLED)
    for leaf,sha in baseline['validationSourceSha256'].items():
        raw=subprocess.check_output(['git','-C',str(repo),'show',baseline['validationSourceRevision']+':deploy/validation/'+leaf]);assert hashlib.sha256(raw).hexdigest()==sha
    for leaf,sha in baseline['installedSourceSha256'].items():
        raw=subprocess.check_output(['git','-C',str(repo),'show',baseline['revision']+':deploy/host/'+leaf]);assert (Path('/usr/local/libexec')/leaf).read_bytes()==raw and hashlib.sha256(raw).hexdigest()==sha
    ready=read('ready-sql.json');identifier=ready['row']['id'];require_ready(ready,baseline)
    assert read('rejected-sql.json')==ready
    final=acl.snapshot(identifier);assert final==read('canceled-sql.json');require_canceled(final,ready)
    rejected=read('rejected.json');signal=read('ready.json');canceled=read('canceled.json')
    assert identifier==ui['id']==passed['id']==signal['id']==rejected['id']==canceled['id']
    assert signal['original']==rejected['original']==rejected['originalByKey']==rejected['unchanged']
    assert rejected['sourceStatus']==ui['sourceMissingStatus']==404 and rejected['confirmStatus']==ui['confirmStatus']==409
    assert rejected['code']==ui['code']=='OPERATION_CONFLICT'
    assert (ui['registrations'],ui['confirmations'],ui['cancellations'],ui['contentWrites'])==(1,1,1,0)
    assert (rejected['registrations'],rejected['confirmations'],rejected['cancellations'],rejected['contentWrites'])==(1,1,0,0)
    assert ui['events']==['restore-registration','restore-confirm','restore-cancel']
    for key in ('actualUi','normalDualLogout','originalByKeyUnchanged','sourceReadableAgain','uncertainConfirmationDisabled','errorEntirelyInViewport','noHorizontalOverflow'):assert ui[key] is True
    logout=ui['logout'];assert logout['normalUiLogout'] and logout['providerNavigationObserved'] and logout['bffStatus']==401 and logout['identityStatus'] in (401,403)
    assert read('denials.json')==dict(anonymousReadsDenied=2,ordinaryReadsDenied=2,normalOrdinaryDualLogout=True)
    assert read('physical-final.json')==baseline['original']==proof['after']
    assert baseline['bytesPerFile']==2*1024**2+7
    content=hashlib.shake_256(('source-missing:'+baseline['containerId']).encode()).digest(baseline['bytesPerFile'])
    assert hashlib.sha256(content).hexdigest().upper()==baseline['sha256']
    files=load('/usr/local/libexec/whaledeck-files.py','source_missing_independent_files')
    module=load('/usr/local/libexec/whaledeck-container-acl.py','source_missing_independent_acl')
    storage=load('/usr/local/libexec/whaledeck-file-backups.py','source_missing_independent_hdd').FileBackups(files)
    observed=evidence/'independent-capture';observed.mkdir(mode=0o700,exist_ok=True)
    original_baseline={**baseline,'original':proof['before']}
    capture=acl.verify_case(proof['result'],original_baseline,baseline['subject'],observed,None,module,storage,proof['before'],proof['after'])
    assert capture==proof and baseline['captureIntentId']==proof['result']['id']
    immutable(evidence/'capture',baseline,[proof],storage);no_operation_history(identifier,storage)
    with installed_namespace(storage) as (fd,attest):
        current=catalogue(fd);old=read('old-hdd.json');after=read('restored-hdd.json');own=baseline['restoreSource']['entry']['captureId']+'.json'
        assert current==after and {n:v for n,v in current.items() if n!=own}==old
        journal=os.open(evidence,os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW)
        try:
            state=OwnedSourceQuarantine(fd,journal,attest).state()  # no restore mutation in verifier
            assert state['captureId']+'.json'==own and state['old']==old
            OwnedSourceQuarantine.same_original(current[own],state['original'])
            assert not os.path.lexists('/proc/self/fd/'+str(fd)+'/'+state['quarantine'])
        finally:os.close(journal)
    verify_audits(read('restore-audits.json'),final['row'])
    for leaf in ('source-confirm-rejected-mobile-dark.png','source-original-canceled-mobile-dark.png'):
        p=evidence/leaf;i=trusted(p);assert i.st_mode&0o777==0o600 and 1000<i.st_size<4*1024**2 and p.read_bytes().startswith(b'\x89PNG\r\n\x1a\n')
    assert passed['oldCapturesUnchanged']==len(old) and passed['sourceCaptureCount']==1
    for key in ('actualUi','normalDualLogout','noRestoreExecution','noRestoreRootOrHdd','originalSourceRestored','physicalCheckedBeforeCleanup','exactSourceAndContainerRemoved'):assert passed[key] is True
    folder=Path(baseline['source']);assert folder.parent==acl.owned.common.APPS and folder.name==baseline['containerName'] and not os.path.lexists(folder)
    missing=subprocess.run(['docker','inspect',baseline['containerId']],capture_output=True,timeout=10);assert missing.returncode!=0 and b'no such' in missing.stderr.lower()
    acl.no_active_work();assert host_snapshot(baseline['revision'])==baseline['hostsBefore']
    assert acl.owned.common.core_snapshot()=={k:tuple(v) for k,v in baseline['coreBefore'].items()}
    containers=json.loads(subprocess.check_output(['docker','inspect',*acl.owned.common.CORE_NAMES],timeout=20));assert len(containers)==11 and all(c['State'].get('Health',{}).get('Status')=='healthy' for c in containers)
    with requests.Session() as api:
        api.trust_env=False;api.headers['Authorization']='Bearer '+require(container_environment(),'Authentication__Authentik__ApiToken')
        for prefix in ('whaledeck-validation-','whaledeck-upload-qa-'):
            r=api.get(AUTHENTIK_API+'core/users/',params={'search':prefix,'page_size':100},timeout=20)
            assert r.status_code==200 and not r.json().get('pagination',{}).get('next') and not any(str(u['username']).startswith(prefix) for u in r.json()['results'])
    return dict(independentReadOnlyVerifier=True,kind=KIND,id=identifier,revision=baseline['revision'],actualConfirmation409=True,
        noRestoreExecutionRootOrHdd=True,oldCapturesUnchanged=len(old),originalCaptureRestored=True,coreHealthy=11,
        temporaryIdentitiesAbsent=True,physicalCheckedByParentBeforeCleanup=True,deletedSourceBytesReread=False)


if __name__=='__main__':
    try:assert os.geteuid()==0 and len(sys.argv)==2;os.umask(0o077);print(json.dumps(verify(sys.argv[1])))
    except BaseException as error:
        print(json.dumps(dict(independentSourceMissing='failed',failureType=type(error).__name__,
            locations=[dict(file=Path(f.filename).name,line=f.lineno) for f in traceback.extract_tb(error.__traceback__)])))
        raise SystemExit(1) from None
