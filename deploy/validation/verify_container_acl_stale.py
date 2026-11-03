#!/usr/bin/env python3
"""Independent read-only observer of rejected real stale-page ACL preflight."""
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys

import requests
from run_with_ephemeral_admin import AUTHENTIK_API,container_environment,require
from validate_container_acl_stale import SOURCES,require_touch,require_failed,expected_summary,no_operation_history
from validate_container_acl_browser import NAMES,snapshot,no_active_work,validate_job_summary,owned,version
from validate_container_acl_faults import host_snapshot
from validate_container_file_staging import load
from validate_container_upload_recovery_agent import trusted


def verify(name):
    repo=Path(__file__).resolve().parents[2];assert re.fullmatch('container-acl-stale-[a-f0-9]{32}',name);evidence=repo/'deploy/artifacts'/name;trusted(evidence,True)
    def read(name):
        p=evidence/name;s=trusted(p);assert s.st_mode&0o777==0o600 and s.st_size<=262144;return json.loads(p.read_bytes())
    baseline=read('baseline.json');ui=read('browser.json');accepted=read('verified.json');result=read('result.json');identifier=result['intent']['id']
    assert type(baseline['proofVersion']) is int and baseline['proofVersion']==1
    assert baseline['revision']==ui['revision']==accepted['revision'] and identifier==ui['id']==accepted['id']
    assert re.fullmatch('[a-f0-9]{40}',baseline['validationSourceRevision']);subprocess.run(['git','-C',str(repo),'merge-base','--is-ancestor',baseline['revision'],baseline['validationSourceRevision']],check=True,capture_output=True,timeout=15)
    assert set(baseline['validationSourceSha256'])==set(SOURCES)
    for name,sha in baseline['validationSourceSha256'].items():
        assert '/' not in name
        raw=subprocess.check_output(['git','-C',str(repo),'show',baseline['validationSourceRevision']+':deploy/validation/'+name]);assert hashlib.sha256(raw).hexdigest()==sha
    assert set(baseline['installedSourceSha256'])=={*NAMES,'whaledeck-container-acl-transport.py','whaledeck-privileged'}
    for name,sha in baseline['installedSourceSha256'].items():
        assert '/' not in name
        path=Path('/usr/local/libexec')/name;trusted(path);raw=subprocess.check_output(['git','-C',str(repo),'show',baseline['revision']+':deploy/host/'+name]);assert path.read_bytes()==raw and hashlib.sha256(raw).hexdigest()==sha
    original=baseline['original'];mutated=read('physical-mutated.json');require_touch(original,mutated);assert read('physical-final.json')==mutated
    content=hashlib.shake_256(('acl-stale:'+baseline['containerId']).encode()).digest(2*1024**2+7)
    assert baseline['bytesPerFile']==len(content) and baseline['sha256']==hashlib.sha256(content).hexdigest().upper()
    assert baseline['sourceVersion']==version(baseline,'work/acl-file.bin',original['acl-file.bin']['identity'])
    assert baseline['sourceVersion']!=version(baseline,'work/acl-file.bin',mutated['acl-file.bin']['identity'])
    data=snapshot(identifier);assert read('failed-sql.json')==data;require_failed(data,result['intent'],baseline,ui['subject']);validate_job_summary(result['job'],expected_summary(identifier),identifier,'Failed')
    files=load('/usr/local/libexec/whaledeck-files.py','acl_stale_independent_files');storage=load('/usr/local/libexec/whaledeck-file-backups.py','acl_stale_independent_storage').FileBackups(files);no_operation_history(identifier,storage)
    assert read('opened.json')=={'subject':ui['subject'],'originalPageOpened':True};assert read('changed.json')=={'originalVersionStale':True};assert read('verified-row.json')=={'id':identifier,'noExecutionOrHistory':True}
    assert ui['actualUi'] is ui['normalDualLogout'] is True and ui['registrations']==ui['registrationRetries']==1 and ui['confirmations']==ui['contentWrites']==ui['jobWrites']==0
    logout=ui['logout'];assert logout['normalUiLogout'] and logout['providerNavigationObserved'] and logout['bffStatus']==401 and logout['identityStatus'] in (401,403)
    assert [(p['width'],p['colorScheme']) for p in ui['taskCenter']]==[(320,'dark'),(1440,'light')]
    for proof in ui['taskCenter']:
        assert proof['id']==identifier and proof['missingConfirmationVisible'] is proof['noGenericWriteControls'] is True
        for key,part in [('titleScreenshot','title'),('factsScreenshot','facts')]:assert proof[key]==f"failed-plan-{part}-{proof['width']}-{proof['colorScheme']}.png"
    screenshots=['failed-preflight-mobile-dark.png',*[p[key] for p in ui['taskCenter'] for key in ('titleScreenshot','factsScreenshot')]]
    for name in screenshots:
        path=evidence/name;info=trusted(path);assert info.st_mode&0o777==0o600 and 1000<info.st_size<4*1024**2 and path.read_bytes().startswith(b'\x89PNG\r\n\x1a\n')
    assert read('denials.json')=={'anonymousReadsDenied':2,'ordinaryReadsDenied':2,'ordinaryRegistrationDenied':1,'normalOrdinaryLogout':True}
    for key in ('executionJobs','rootRecords','hddRecords'):assert type(accepted[key]) is int and accepted[key]==0
    for key in ('actualUi','normalDualLogout','physicalBytesAndAclsPreserved','onlyExactOwnedMtimeChanged'):assert accepted[key] is True
    assert accepted['state']=='Failed' and accepted['code']=='CONTAINER_ACL_VERSION_CONFLICT'
    source=Path(baseline['source']);assert source.parent==owned.common.APPS and source.name==baseline['containerName'] and not os.path.lexists(source)
    assert re.fullmatch('[a-f0-9]{64}',baseline['containerId']);missing=subprocess.run(['docker','inspect',baseline['containerId']],capture_output=True,timeout=10);assert missing.returncode!=0 and b'no such' in missing.stderr.lower()
    no_active_work();assert host_snapshot(baseline['revision'])==baseline['hostsBefore'] and owned.common.core_snapshot()=={k:tuple(v) for k,v in baseline['coreBefore'].items()}
    core=json.loads(subprocess.check_output(['docker','inspect',*owned.common.CORE_NAMES],timeout=20));assert len(core)==11 and all(c['State']['Running'] and c['State'].get('Health',{}).get('Status')=='healthy' for c in core)
    with requests.Session() as api:
        api.trust_env=False;api.headers['Authorization']='Bearer '+require(container_environment(),'Authentication__Authentik__ApiToken')
        for prefix in ('whaledeck-validation-','whaledeck-upload-qa-'):
            r=api.get(AUTHENTIK_API+'core/users/',params={'search':prefix,'page_size':100},timeout=20);assert r.status_code==200 and not r.json().get('pagination',{}).get('next') and not any(str(u['username']).startswith(prefix) for u in r.json()['results'])
    return dict(independentReadOnlyVerifier=True,revision=baseline['revision'],id=identifier,stalePagePreflightFailed=True,executionJobs=0,rootRecords=0,hddRecords=0,actualTaskDrawers=2,
        originalContextNormalDualLogout=True,temporaryIdentitiesAbsent=True,exactSourceAndContainerAbsent=True,coreHealthy=11,physicalBytesAndAclsCheckedByParentBeforeCleanup=True,sourceBytesRereadAfterCleanup=False)


if __name__=='__main__':
    try:assert os.geteuid()==0 and len(sys.argv)==2;print(json.dumps(verify(sys.argv[1])))
    except BaseException as error:
        import traceback
        print(json.dumps({'aclStaleIndependent':'failed','failureType':type(error).__name__,'locations':[{'file':Path(f.filename).name,'line':f.lineno} for f in traceback.extract_tb(error.__traceback__)]}));raise SystemExit(1) from None
