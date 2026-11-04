#!/usr/bin/env python3
"""Read permanent SQL/Root/HDD again; never replay a fault or source mutation."""
import hashlib
import json
import os
from pathlib import Path
import re
import struct
import subprocess
import sys

import requests
from validate_container_recursive_acl_faults import SOURCES, REVOKED_KINDS, RESTART_KINDS, require_host_restart, verify_restart, verify_revoked_browser, require_authority_change, verify_terminal, no_active_work, host_identity, load, owned, preservation_snapshot
from verify_container_recursive_permissions import private_json
from validate_container_upload_recovery_agent import trusted
from acl_source_apply_missing_proof import source_closure
from run_with_ephemeral_admin import AUTHENTIK_API, container_environment, require


def target(name):
    assert re.fullmatch('container-recursive-acl-faults-[a-f0-9]{32}',name)
    return Path(__file__).resolve().parents[2]/'deploy/artifacts'/name


def verify(name):
    evidence=target(name);trusted(evidence,True);scope=private_json(evidence/'scope.json');accepted=private_json(evidence/'verified.json');proofs=private_json(evidence/'results.json')
    assert scope['kinds'] in (['cancel'],['logout'],['cancel','logout'],['group-removal'],['account-disable'],list(REVOKED_KINDS),list(RESTART_KINDS),['worker-crash'],['agent-restart']) and accepted['kinds']==scope['kinds'] and accepted['cases']==len(proofs)==len(scope['kinds'])
    assert accepted['actualVisibleIdentityAndActions'] and accepted['physicalBytesCheckedBeforeCleanup'] and not accepted['sourceBytesRereadAfterCleanup']
    repo=Path(__file__).resolve().parents[2]
    assert scope['validationSourceSha256']==source_closure(repo,scope['validationSourceRevision'],seeds=SOURCES)
    for name,sha in scope['validationSourceSha256'].items():
        raw=subprocess.check_output(['git','-C',str(repo),'show',scope['validationSourceRevision']+':deploy/validation/'+name]);assert hashlib.sha256(raw).hexdigest()==sha
    for name,sha in scope['sourceHashes'].items():
        raw=subprocess.check_output(['git','-C',str(repo),'show',scope['revision']+':deploy/host/'+name]);assert hashlib.sha256(raw).hexdigest()==sha and (Path('/usr/local/libexec')/name).read_bytes()==raw
    hosts=accepted.get('hostIdentityAfter',scope['hostIdentity']);assert host_identity(scope['revision'])==hosts
    if 'agent-restart' in scope['kinds']:require_host_restart(scope['hostIdentity'],hosts,scope['revision'])
    else:assert scope['hostIdentity']==hosts
    files=load('/usr/local/libexec/whaledeck-files.py','recursive_acl_fault_second_files');storage=load('/usr/local/libexec/whaledeck-file-backups.py','recursive_acl_fault_second_hdd').FileBackups(files)
    observed=preservation_snapshot(storage);previous=scope['preservedFiles'];assert all(observed.get(k)==v for k,v in previous.items())
    for kind,proof in zip(scope['kinds'],proofs):
        directory=evidence/kind;baseline=private_json(directory/'baseline.json');ui=private_json(directory/'browser.json');ready=private_json(directory/'ready.json');cut=private_json(directory/'cut-root.json');bound=private_json(directory/'bound-helper.json')
        assert baseline['kind']==proof['kind']==ui['kind']==kind and baseline['revision']==scope['revision'] and baseline['validationSourceSha256']==scope['validationSourceSha256']
        assert baseline['original']==private_json(directory/'cut-physical.json')==private_json(directory/'physical-final.json')
        assert cut['state']=='Applying' and cut['historyVerified'] is True and cut['historyMayExist'] is True and cut['finishedThrough']==-1 and cut['startedThrough'] in (-1,0)
        assert bound['proof']['twoKernelFdProofs'] and bound['proof']['originalFullTreeUnchanged']
        assert ui['actualUi'] and ui['subject']==proof['subject']==ready['subject'] and ui['id']==proof['id']==ready['id']
        assert ui['registrations']==ui['confirmations']==1 and ui['cancellations']==int(kind=='cancel') and ui['uploads']==0
        if kind in REVOKED_KINDS:
            require_authority_change(private_json(directory/'authority-change.json'),ready,kind);verify_revoked_browser(ui,kind,ready['subject'])
            denied=private_json(directory/'authority-denied.json');assert all(ui[k]==v for k,v in denied.items())
            assert proof['authorityRevoked'] is True and proof['normalDualLogout'] is False and not (directory/'logout.json').exists()
        else:
            assert ui['anonymousGet401'];logout=ui['logout'];assert logout==private_json(directory/'logout.json')
            assert logout['normalUiLogout'] and logout['providerNavigationObserved'] and logout['finalLoginPage'] and logout['postStatus'] in (302,303) and logout['bffStatus']==401 and logout['identityStatus'] in (401,403)
        actual=verify_terminal(proof['id'],baseline,ready['subject'],storage,directory,read_only=True)
        if kind in RESTART_KINDS:
            verify_restart(kind,proof,baseline,ui,private_json(directory/'final-sql.json')['row'],directory,private_json)
        assert all(proof[k]==v for k,v in actual.items()) and proof==private_json(directory/'verified.json')
        assert proof['validatorKilledHelper'] is False and 0<proof['productStoppedHelperSeconds']<15 and proof['originalFullTreeUnchanged']
        shots=list(directory.glob('*.png'));assert {p.name for p in shots}==({'inflight.png','authority-denied.png'} if kind in REVOKED_KINDS else {'inflight.png','terminal.png'} if kind in ('cancel',*RESTART_KINDS) else {'inflight.png'})
        for p in shots:
            raw=p.read_bytes();assert len(raw)>1000 and raw[:8]==b'\x89PNG\r\n\x1a\n';assert struct.unpack('>II',raw[16:24])==((320 if kind in ('cancel','group-removal') else 1440),600)
        source=Path(baseline['source']);assert source.parent==owned.common.APPS and source.name==baseline['containerName'] and not os.path.lexists(source)
        missing=subprocess.run(['docker','inspect',baseline['containerId']],capture_output=True,timeout=15);assert missing.returncode!=0 and b'no such' in missing.stderr.lower()
    no_active_work();after=owned.common.core_snapshot();assert after.keys()==scope['coreBefore'].keys()
    for name,value in scope['coreBefore'].items():assert after[name][:3]==tuple(value[:3]) if name.lstrip('/')=='whaledeck-worker' else after[name]==tuple(value)
    session=requests.Session();session.trust_env=False;session.headers['Authorization']='Bearer '+require(container_environment(),'Authentication__Authentik__ApiToken')
    try:
        r=session.get(AUTHENTIK_API+'core/users/',params=dict(search='whaledeck-upload-qa-',page_size=100),timeout=20)
        assert r.status_code==200 and not r.json().get('pagination',{}).get('next') and not any(u['username'].startswith('whaledeck-upload-qa-') for u in r.json()['results'])
    finally:session.close()
    return dict(independentReadOnlyVerifier=True,cases=len(proofs),kinds=scope['kinds'],permanentSqlRootAndHddReread=True,temporaryIdentitiesAbsent=True,
        revokedContexts=sum(k in REVOKED_KINDS for k in scope['kinds']),normalDualLogoutContexts=sum(k not in REVOKED_KINDS for k in scope['kinds']),
        physicalBytesCheckedByParentBeforeCleanup=True,sourceBytesRereadAfterCleanup=False,exactSourceAndContainerAbsent=True)


if __name__=='__main__':
    assert os.geteuid()==0 and len(sys.argv)==2;os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0'
    name=sys.argv[1];proof=verify(name);(target(name)/'independent.json').write_text(json.dumps(proof));print(json.dumps(proof))
