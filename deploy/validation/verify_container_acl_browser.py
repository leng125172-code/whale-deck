#!/usr/bin/env python3
"""Separate read-only SQL/Root/attested HDD observer of real ACL UI evidence.

No identity/SQL/source edits. Deleted physical bytes were checked by the parent
per case, never claimed to be reread here after exact cleanup.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import requests
from run_with_ephemeral_admin import AUTHENTIK_API,container_environment,require
from validate_container_file_staging import load
from validate_container_file_publishing import common
from validate_container_upload_recovery_agent import trusted
from verify_container_recursive_permissions import private_json
from validate_container_acl_browser import CASES,SOURCES,snapshot,verify_case,no_active_work


def verify(name):
    assert re.fullmatch('container-acl-browser-[a-f0-9]{32}',name);repo=Path(__file__).resolve().parents[2];evidence=repo/'deploy/artifacts'/name;trusted(evidence,True)
    baseline=private_json(evidence/'baseline.json');proofs=private_json(evidence/'results.json');browser=private_json(evidence/'browser.json');accepted=private_json(evidence/'verified.json')
    assert baseline['revision']==browser['revision']==accepted['revision'] and browser['actualUi'] and browser['normalDualLogout']
    assert accepted['actualBrowserAndIdentity'] and accepted['cases']==len(proofs)==len(browser['completed'])==9
    assert browser['registrations']==browser['registrationRetries']==9 and browser['confirmations']==browser['confirmationRetries']==8 and browser['contentUploads']==0
    assert browser['logout']['normalUiLogout'] and browser['logout']['providerNavigationObserved'] and browser['logout']['bffStatus']==401 and browser['logout']['identityStatus'] in (401,403)
    assert re.fullmatch('[a-f0-9]{40}',baseline['validationSourceRevision'])
    subprocess.run(['git','-C',str(repo),'merge-base','--is-ancestor',baseline['revision'],baseline['validationSourceRevision']],check=True,capture_output=True,timeout=15)
    for file,sha in baseline['installedSourceSha256'].items():
        assert '/' not in file and re.fullmatch('[a-z0-9.-]+',file)
        raw=subprocess.check_output(['git','-C',str(repo),'show',baseline['revision']+':deploy/host/'+file]);assert hashlib.sha256(raw).hexdigest()==sha and (Path('/usr/local/libexec')/file).read_bytes()==raw
    assert set(baseline['validationSourceSha256'])==set(SOURCES)
    for file,sha in baseline['validationSourceSha256'].items():assert hashlib.sha256(subprocess.check_output(['git','-C',str(repo),'show',baseline['validationSourceRevision']+':deploy/validation/'+file])).hexdigest()==sha
    content=hashlib.shake_256(('acl-browser:'+baseline['containerId']).encode()).digest(2*1024**2+7);assert baseline['bytesPerFile']==len(content) and baseline['sha256']==hashlib.sha256(content).hexdigest().upper()
    reader=load('/usr/local/libexec/whaledeck-container-files.py','acl_browser_independent_reader');files=reader.trusted_module('whaledeck-files.py','acl_browser_independent_files');module=load('/usr/local/libexec/whaledeck-container-acl.py','acl_browser_independent_acl');storage=load('/usr/local/libexec/whaledeck-file-backups.py','acl_browser_independent_storage').FileBackups(files)
    observed=evidence/'independent-observed';observed.mkdir(mode=0o700,exist_ok=True);previous=baseline['original'];count=0
    for index,proof in enumerate(proofs):
        item=browser['completed'][index];label=CASES[index];assert proof['result']==item and item['name']==label and proof['before']==previous
        assert private_json(evidence/(label+'-result.json'))=={'subject':browser['subject'],'result':item}
        assert private_json(evidence/(label+'-verified.json'))=={'id':item['id'],'physicalAndHistoryVerified':True}
        assert snapshot(item['id'])==private_json(evidence/(label+'.sql.json'))
        current=verify_case(item,baseline,browser['subject'],observed,reader,module,storage,proof['before'],proof['after']);assert current==proof
        assert (observed/(label+'.sql.json')).read_bytes()==(evidence/(label+'.sql.json')).read_bytes()
        if label!='cancel':
            for suffix in ('.root.json','.hdd.json'):assert (observed/(label+suffix)).read_bytes()==(evidence/(label+suffix)).read_bytes()
            count+=1
        previous=proof['after']
    assert count==8 and previous==private_json(evidence/'physical-final.json')
    if baseline.get('jobResultsVersion')==1:
        assert accepted['jobResultsVersion']==browser['jobResultsVersion']==1
        assert accepted['newJobSummaries']==17 and accepted['actualTaskDrawers']==18 and accepted['legacyJobUnchanged']
        expected=[(item['executionJobId'] if execution else item['planningJobId'],item['id'],execution) for item in browser['completed'] for execution in (False,True) if not execution or item['executionJobId']]
        assert [(t['id'],t['intentId'],t['execution']) for t in browser['taskCenter'][:-1]]==expected
        assert len(expected)==17 and len({t[0] for t in expected})==17
        assert browser['taskCenter'][-1]=={'id':baseline['legacyJob']['id'],'legacy':True,'resultJson':None,'screenshot':'legacy-job.png'}
        for item in browser['taskCenter']:
            name=item['screenshot'];assert '/' not in name and re.fullmatch('[a-z-]+(?:-job)?\\.png',name)
            path=evidence/name;trusted(path);assert 1000<path.stat().st_size<4*1024**2 and path.read_bytes().startswith(b'\x89PNG\r\n\x1a\n')
        assert __import__('validate_metric_continuity').query("SELECT to_jsonb(j) FROM operation_jobs j WHERE id=:'id'::uuid;",id=baseline['legacyJob']['id'])==baseline['legacyJob']
    source=Path(baseline['source']);assert source.parent==common.APPS and source.name==baseline['containerName'] and not os.path.lexists(source)
    missing=subprocess.run(['docker','inspect',baseline['containerId']],capture_output=True,timeout=15);assert missing.returncode!=0 and b'no such' in missing.stderr.lower()
    no_active_work();assert common.core_snapshot()=={k:tuple(v) for k,v in baseline['coreBefore'].items()}
    api=requests.Session();api.trust_env=False;api.headers['Authorization']='Bearer '+require(container_environment(),'Authentication__Authentik__ApiToken')
    try:
        for prefix in ('whaledeck-validation-','whaledeck-upload-qa-'):
            response=api.get(AUTHENTIK_API+'core/users/',params={'search':prefix,'page_size':100},timeout=20);assert response.status_code==200 and not response.json().get('pagination',{}).get('next');assert not any(str(u['username']).startswith(prefix) for u in response.json()['results'])
    finally:api.close()
    return {'revision':baseline['revision'],'independentReadOnlyVerifier':True,'cases':9,'hddRecords':8,'temporaryIdentitiesAbsent':True,'exactSourceAndContainerAbsent':True,'coreUnchanged':11,'physicalBytesCheckedByParentPerCase':True,'sourceBytesRereadAfterCleanup':False,'normalDualLogoutFromOriginalContext':True}


if __name__=='__main__':
    try:
        assert os.geteuid()==0 and len(sys.argv)==2;os.umask(0o077);proof=verify(sys.argv[1]);repo=Path(__file__).resolve().parents[2];(repo/'deploy/artifacts'/sys.argv[1]/'independent-final.json').write_text(json.dumps(proof));print(json.dumps({'independentAclBrowser':'passed',**proof}))
    except Exception as error:
        print(json.dumps({'independentAclBrowser':'failed','failureType':type(error).__name__}));raise SystemExit(1) from None
