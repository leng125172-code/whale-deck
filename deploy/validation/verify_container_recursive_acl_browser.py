#!/usr/bin/env python3
"""Independent read-only recheck of permanent SQL/Root/attested HDD and cleanup.

Re-read every raw record; physical bytes are the parent's per-round evidence,
never claimed to have been reread after the exact source has been removed.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import struct
import sys

import requests
from run_with_ephemeral_admin import AUTHENTIK_API,container_environment,require
from validate_container_file_staging import load
from validate_container_file_publishing import common
from validate_container_recursive_acl_browser import CASES,SOURCES,verify_case,no_active_work
from validate_container_recursive_acl_agent import host_identity
from validate_container_recursive_acl import NAMES
from verify_container_recursive_permissions import private_json
from acl_source_apply_missing_proof import source_closure


def verify(name):
    assert re.fullmatch('container-recursive-acl-browser-[a-f0-9]{32}',name)
    repo=Path(__file__).resolve().parents[2];evidence=repo/'deploy/artifacts'/name
    from validate_container_upload_recovery_agent import trusted
    trusted(evidence,True)
    b=private_json(evidence/'baseline.json');accepted=private_json(evidence/'verified.json');browser=private_json(evidence/'browser.json');proofs=private_json(evidence/'results.json')
    assert accepted['revision']==browser['revision']==b['revision'] and re.fullmatch('[a-f0-9]{40}',b['revision'])
    labels=b['cases'];assert labels in (list(CASES),list(CASES[5:]));count=len(labels)
    assert accepted['cases']==len(proofs)==len(browser['completed'])==count and accepted['succeeded']==count-1 and accepted['canceledBeforeExecution']==1
    assert accepted['actualBrowserAndIdentity'] and browser['actualUi'] and browser['normalDualLogout'] and browser['logout']['normalUiLogout']
    assert browser['registrations']==browser['registrationRetries']==count and browser['confirmations']==browser['confirmationRetries']==count-1 and browser['contentUploads']==0
    assert browser['subject']!=browser['ordinary']['subject'] and browser['ordinary']['deniedGet']==browser['ordinary']['deniedPost']==4
    assert browser['ordinary']['pagePrivateRequests']==0 and browser['ordinary']['logout']['normalUiLogout'] and browser['anonymousDenied']==4
    for logout in (browser['logout'],browser['ordinary']['logout']):
        assert logout['providerNavigationObserved'] and logout['finalLoginPage'] and logout['postStatus'] in (302,303) and logout['bffStatus']==401 and logout['identityStatus'] in (401,403)
    assert b['validationSourceSha256']==source_closure(repo,b['validationSourceRevision'],seeds=SOURCES)
    for name,sha in b['validationSourceSha256'].items():
        raw=subprocess.check_output(['git','-C',str(repo),'show',b['validationSourceRevision']+':deploy/validation/'+name]);assert hashlib.sha256(raw).hexdigest()==sha
    for name,sha in b['sourceHashes'].items():
        raw=subprocess.check_output(['git','-C',str(repo),'show',b['revision']+':deploy/host/'+name]);assert hashlib.sha256(raw).hexdigest()==sha and (Path('/usr/local/libexec')/name).read_bytes()==raw
    assert host_identity(b['revision'])==b['hostIdentity']
    content=hashlib.shake_256(('recursive-browser:'+b['containerId']).encode()).digest(2*1024**2+7)
    assert len(content)==b['bytesPerFile'] and hashlib.sha256(content).hexdigest().upper()==b['sha256']
    reader=load('/usr/local/libexec/whaledeck-container-files.py','recursive_acl_ui_second_reader')
    files=reader.trusted_module('whaledeck-files.py','recursive_acl_ui_second_files')
    storage=load('/usr/local/libexec/whaledeck-file-backups.py','recursive_acl_ui_second_hdd').FileBackups(files)
    acl=load('/usr/local/libexec/whaledeck-container-acl.py','recursive_acl_ui_second_acl')
    previous=b['original'];children=0;aggregates=0
    for index,proof in enumerate(proofs):
        item=browser['completed'][index];assert item==proof['result'] and item['name']==labels[index] and proof['before']==previous
        assert private_json(evidence/(item['name']+'-result.json'))=={'subject':browser['subject'],'result':item}
        assert private_json(evidence/(item['name']+'-verified.json'))=={'id':item['id'],'physicalAndHistoryVerified':True}
        actual=verify_case(item,b,browser['subject'],evidence,reader,storage,proof['before'],proof['after'],acl,read_only=True)
        assert actual==proof;children+=actual['children'];aggregates+=item['name']!='cancel';previous=proof['after']
    assert children==accepted['childHddRecords']==4*(count-1) and aggregates==accepted['aggregateHddRecords']==count-1
    assert previous==private_json(evidence/'physical-final.json')
    target_shots=sum(1 if label in ('default','default-noop','clear-default') else 2 for label in labels)
    images=list(evidence.glob('*.png'));assert len(images)==2*count+target_shots+1 and len(list(evidence.glob('*-targets.png')))==target_shots
    assert len(list(evidence.glob('*-ready.png')))==count and len(list(evidence.glob('*-result.png')))==count and (evidence/'ordinary-denied.png').exists()
    for image in images:
        data=image.read_bytes();assert len(data)>1000 and data[:8]==b'\x89PNG\r\n\x1a\n'
        width,height=struct.unpack('>II',data[16:24]);assert height==600 and width in (320,1440)

    source=Path(b['source']);assert source.parent==common.APPS and source.name==b['containerName'] and re.fullmatch('wdv-container-publish-[a-f0-9]{32}',source.name) and not os.path.lexists(source)
    missing=subprocess.run(['docker','inspect',b['containerId']],capture_output=True,timeout=15);assert missing.returncode!=0 and b'no such' in missing.stderr.lower()
    no_active_work();assert common.core_snapshot()=={k:tuple(v) for k,v in b['coreBefore'].items()}
    api=requests.Session();api.trust_env=False;api.headers['Authorization']='Bearer '+require(container_environment(),'Authentication__Authentik__ApiToken')
    try:
        response=api.get(AUTHENTIK_API+'core/users/',params={'search':'whaledeck-upload-qa-','page_size':100},timeout=20)
        assert response.status_code==200 and not response.json().get('pagination',{}).get('next')
        assert not any(str(u['username']).startswith('whaledeck-upload-qa-') for u in response.json()['results'])
    finally:api.close()
    return dict(revision=b['revision'],independentReadOnlyVerifier=True,cases=count,caseLabels=labels,aggregateHddRecords=count-1,childHddRecords=4*(count-1),temporaryIdentitiesAbsent=True,exactSourceAndContainerAbsent=True,coreUnchanged=11,physicalBytesCheckedByParentPerCase=True,sourceBytesRereadAfterCleanup=False,normalDualLogoutFromOriginalContexts=True)


if __name__=='__main__':
    try:
        assert os.geteuid()==0 and len(sys.argv)==2;os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0'
        result=verify(sys.argv[1]);(Path(__file__).resolve().parents[2]/'deploy/artifacts'/sys.argv[1]/'independent-final.json').write_text(json.dumps(result))
        print(json.dumps({'independentRecursiveAclBrowser':'passed',**result}))
    except Exception as error:
        print(json.dumps({'independentRecursiveAclBrowser':'failed','failureType':type(error).__name__}));raise SystemExit(1) from None
