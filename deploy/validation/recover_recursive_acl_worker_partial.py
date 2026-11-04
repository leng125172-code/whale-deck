#!/usr/bin/env python3
"""Recover only the named failed recorder run. Never repeat the Worker fault.

The original whole gate stays failed; missing structured pre-kill inspect and
elapsed-duration receipts are not invented. Only proven own files are removed.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import sys

from acl_source_apply_missing_proof import source_closure
from validate_container_recursive_acl_faults import SOURCES, verify_terminal, no_active_work, host_identity, preservation_snapshot, load, owned, inventory, remove_proved
from verify_container_recursive_permissions import private_json
from validate_container_recursive_acl_browser import managed_plan
from recursive_acl_restart_proof import verify_lease, lease_held

NAME='container-recursive-acl-faults-c78f9daecb5c443cb2eb3694474409f3'
UNIT='wd-qa-recursive-acl-restarts-b510cfb.service'
INVOCATION='5cb84e9913304237be3b59381535c551'


def recover(name):
    assert name==NAME and os.geteuid()==0;os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0'
    repo=Path(__file__).resolve().parents[2];p=repo/'deploy/artifacts'/name;d=p/'worker-crash'
    assert not (p/'verified.json').exists() and not (d/'verified.json').exists() and not (p/'agent-restart').exists() and not (p/'recovered-worker-partial.json').exists()
    scope=private_json(p/'scope.json');b=private_json(d/'baseline.json');ui=private_json(d/'browser.json');ready=private_json(d/'ready.json')
    assert scope['kinds']==['worker-crash','agent-restart'] and b['kind']==ui['kind']=='worker-crash'
    assert re.fullmatch('b510cfb[a-f0-9]{33}',scope['validationSourceRevision'])
    assert scope['validationSourceSha256']==source_closure(repo,scope['validationSourceRevision'],seeds=SOURCES)
    assert b['validationSourceSha256']==scope['validationSourceSha256'] and b['revision']==scope['revision']
    current=subprocess.check_output(['git','-C',str(repo),'rev-parse','HEAD'],text=True).strip()
    assert not subprocess.check_output(['git','-C',str(repo),'status','--porcelain']).strip()
    closure=source_closure(repo,current,seeds=('recover_recursive_acl_worker_partial.py',))
    assert all(hashlib.sha256((repo/'deploy/validation'/n).read_bytes()).hexdigest()==sha for n,sha in closure.items())
    props=dict(v.split('=',1) for v in subprocess.check_output(['systemctl','show',UNIT,'-p','InvocationID','-p','MainPID','-p','Result','-p','ExecMainCode','-p','ExecMainStatus'],text=True).splitlines())
    assert props['InvocationID']==INVOCATION and props['MainPID']=='0' and props['Result']=='exit-code' and props['ExecMainCode']=='1' and props['ExecMainStatus']=='1'
    (p/'failed-unit.status').write_text(json.dumps(props));(p/'failed-unit.journal').write_bytes(subprocess.check_output(['journalctl','_SYSTEMD_INVOCATION_ID='+INVOCATION,'--no-pager','-o','cat']))
    assert host_identity(scope['revision'])==scope['hostIdentity'];no_active_work()
    reader=load('/usr/local/libexec/whaledeck-container-files.py','recover_acl_worker_reader');files=reader.trusted_module('whaledeck-files.py','recover_acl_worker_files')
    storage=load('/usr/local/libexec/whaledeck-file-backups.py','recover_acl_worker_hdd').FileBackups(files);acl=load('/usr/local/libexec/whaledeck-container-acl.py','recover_acl_worker_acl')
    observed=preservation_snapshot(storage);assert all(observed.get(k)==v for k,v in scope['preservedFiles'].items())
    result=verify_terminal(ready['id'],b,ready['subject'],storage,d,read_only=True);assert result['state']=='Failed' and result['counts']==[0,0,0,4,0]
    final=private_json(d/'final-sql.json')['row'];start=private_json(d/'restart-start-sql.json')['row'];crash=private_json(d/'after-worker-crash-sql.json')['row'];lease_held(start,crash)
    raw=(d/'after-helper-exit-root.json').read_bytes();record=json.loads(raw);assert record['operationId']==final['execution_job_id'].replace('-','') and record['owner']==final['owner_hash'] and record['plan']==managed_plan(json.loads(final['plan_json']))
    verify_lease(crash,final,private_json(d/'natural-lease.json'),raw)
    assert ui['actualUi'] and ui['normalDualLogout'] and ui['id']==final['id'] and ui['executionJobId']==final['execution_job_id'] and ui['registrations']==ui['confirmations']==1 and ui['cancellations']==ui['uploads']==0 and ui['anonymousGet401']
    logout=private_json(d/'logout.json');assert ui['logout']==logout and logout['normalUiLogout'] and logout['providerNavigationObserved'] and logout['postStatus'] in (302,303) and logout['bffStatus']==401 and logout['identityStatus'] in (401,403)
    assert ui['terminal']['state']=='Failed' and ui['terminal']['receipt']['state']=='Aborted' and ui['terminal']['executionJobId']==final['execution_job_id']
    witness=private_json(d/'worker-inspect.json');before=b['coreBefore']['/whaledeck-worker'];assert witness['containerId']==before[0] and witness['imageId']==before[1] and witness['running'] is True
    folder=Path(b['source']);assert folder.parent==owned.common.APPS and folder.name==b['containerName'] and re.fullmatch('wdv-container-publish-[a-f0-9]{32}',folder.name) and folder.resolve(strict=True)==folder
    plan=managed_plan(json.loads(final['plan_json']));info=folder.lstat();assert [info.st_dev,info.st_ino]==plan['parentIdentity'] and info.st_uid==0 and stat.S_IMODE(info.st_mode)==0o755
    assert {v.name for v in folder.iterdir()}=={'original.bin','recursive-tree'}
    content=hashlib.shake_256(('recursive-acl-fault:'+b['containerId']).encode()).digest(b['bytesPerFile']);assert hashlib.sha256(content).hexdigest().upper()==b['sha256']
    physical=inventory(folder/'recursive-tree',reader,content,acl);assert physical==b['original']==private_json(d/'cut-physical.json')==private_json(d/'physical-final.json')
    missing=subprocess.run(['docker','inspect',b['containerId']],capture_output=True,timeout=15);assert missing.returncode!=0 and b'no such' in missing.stderr.lower()
    original=folder/'original.bin';s=original.lstat();expected=hashlib.shake_256(('owned-container-original:'+folder.name).encode()).digest(owned.common.SIZE)
    assert stat.S_ISREG(s.st_mode) and not original.is_symlink() and s.st_nlink==1 and s.st_uid==s.st_gid==1000 and stat.S_IMODE(s.st_mode)==0o640 and original.read_bytes()==expected
    proof=dict(originalWholeGatePassed=False,originalFailure='TypeError serializing a set-valued summary state',workerPermanentAndPhysicalSubcaseVerified=True,
        originalStructuredServiceRestartReceiptSaved=False,exactHelperExitDurationPersisted=False,noWorkerFaultRepeated=True,agentRestartNotRun=True,
        state='Failed',rootState='Aborted',counts=result['counts'],naturalLeaseSamples=len(private_json(d/'natural-lease.json')['samples']),sourceFullBytesVerifiedBeforeCleanup=True,
        recoveryRevision=current,recoverySourceSha256=closure,oldProtectedFilesUnchanged=len(scope['preservedFiles']))
    (p/'recovery-before-cleanup.json').write_text(json.dumps(proof))
    remove_proved(folder/'recursive-tree',reader,content,b['original'],acl)
    assert {v.name for v in folder.iterdir()}=={'original.bin'} and original.lstat()==s and original.read_bytes()==expected
    original.unlink();folder.rmdir();assert not folder.exists()
    proof['exactSourceAndContainerAbsent']=True
    (p/'recovered-worker-partial.json').write_text(json.dumps(proof));return proof


if __name__=='__main__':
    assert len(sys.argv)==2;result=recover(sys.argv[1]);print(json.dumps({k:v for k,v in result.items() if k!='recoverySourceSha256'}))
