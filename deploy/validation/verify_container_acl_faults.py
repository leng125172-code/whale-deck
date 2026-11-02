#!/usr/bin/env python3
"""Independent read-only permanent ACL fault evidence after exact cleanup."""
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys

import requests
from validate_container_acl_faults import SOURCES, verify_terminal, no_active_work, load, owned
from validate_metric_continuity import query
from validate_container_upload_recovery_agent import trusted
from run_with_ephemeral_admin import AUTHENTIK_API, container_environment, require


def verify(directory):
    repo=Path(__file__).resolve().parents[2]
    assert directory.parent==repo/'deploy/artifacts' and re.fullmatch('container-acl-faults-[a-f0-9]{32}',directory.name);trusted(directory,True)
    def read(path):
        info=trusted(path);assert info.st_size<=262144;return json.loads(path.read_bytes())
    accepted=read(directory/'verified.json');provenance=read(directory/'validation-source.json')
    assert accepted['revision']==provenance['productRevision'] and set(provenance['sha256'])==set(SOURCES)
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
        kind=result['kind'];assert kind in ('cancel','logout');p=directory/kind;trusted(p,True)
        baseline=read(p/'baseline.json');ui=read(p/'browser.json');cut=read(p/'cut-root.json');bound=read(p/'bound-helper.json')
        assert baseline['revision']==ui['revision']==accepted['revision'] and baseline['validationSourceRevision']==provenance['revision'] and baseline['kind']==ui['kind']==kind
        assert ui['logout']['normalUiLogout'] and ui['logout']['providerNavigationObserved'] and ui['logout']['bffStatus']==401 and ui['logout']['identityStatus'] in (401,403)
        assert ui['id']==result['id'] and ui['executionJobId']==result['executionJobId'] and ui['actualUi'] and ui['normalDualLogout']
        assert ui['registrations']==ui['confirmations']==1 and ui['cancellations']==(1 if kind=='cancel' else 0) and ui['bodies']==0
        assert 0<=result['productStoppedHelperSeconds']<15 and not result['validatorKilledHelper'] and result['originalFullBytesFingerprintAclsAndAttributesUnchanged']
        content=hashlib.shake_256(('acl-fault:'+baseline['containerId']).encode()).digest(2*1024**2+7)
        assert baseline['bytesPerFile']==len(content) and baseline['sha256']==hashlib.sha256(content).hexdigest().upper()
        data,record,raw,hdd_raw=verify_terminal(result['id'],ui['subject'],baseline,storage,module)
        assert data==read(p/'final-sql.json') and raw==(p/'final-root.json').read_bytes() and hdd_raw==(p/'final-hdd.json').read_bytes()
        assert read(p/'physical-final.json')==baseline['original']
        assert data['row']['execution_job_id']==ui['executionJobId'] and data['row']['state']==ui['finalState']==result['state']=='Canceled'
        assert bool(data['row']['cancel_requested_at_utc']) is (kind=='cancel')
        assert cut['state'] in ('Preparing','Prepared') and cut['aclConfirmed'] is None and cut['historyVerified'] and cut['historyMayExist']
        assert cut['plan']==record['plan'] and cut['operationId']==record['operationId'] and cut['owner']==record['owner'] and cut['historyDigest']==record['historyDigest']
        proof=bound['proof'];assert bound['twoKernelFdProofs'] and re.fullmatch(r'whaledeck-container-acl-[a-f0-9]{32}\.service',bound['unit'])
        assert proof['originalIdentity']==cut['plan']['sourceIdentity']==baseline['original']['acl-file.bin']['identity']
        assert proof['accessAcl']==cut['plan']['accessAcl'] and proof['defaultAcl']==cut['plan']['defaultAcl'] and proof['attributesDigest']==cut['plan']['attributesDigest']
        audit=query("SELECT json_build_object('n',count(*),'actors',json_agg(actor_subject),'results',json_agg(result)) FROM audit_events WHERE job_id=:'id'::uuid AND action='containers.files.acl.result';",id=ui['executionJobId'])
        assert audit=={'n':1,'actors':[ui['subject']],'results':['Canceled']}
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
