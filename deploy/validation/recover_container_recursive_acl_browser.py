#!/usr/bin/env python3
"""Read-only proof and exact cleanup of the first failed default case only.

Never recreates its container, repeats any ACL request or upgrades the failed
whole gate to Passed. Original context logout was not proved and stays unproved.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import sys
import uuid
from validate_container_file_staging import load
from validate_container_file_publishing import common
from validate_container_recursive_acl import inventory,remove_proved
from validate_container_recursive_acl_browser import managed_plan,snapshot,verify_case,no_active_work,SOURCES
from verify_container_recursive_permissions import private_json
from acl_source_apply_missing_proof import source_closure


def recover(name):
    assert re.fullmatch('container-recursive-acl-browser-[a-f0-9]{32}',name)
    repo=Path(__file__).resolve().parents[2];evidence=repo/'deploy/artifacts'/name
    info=evidence.lstat();assert evidence.resolve(strict=True)==evidence and info.st_uid==0 and stat.S_IMODE(info.st_mode)==0o700
    assert not (evidence/'verified.json').exists() and not (evidence/'recovered-first-default.json').exists() and not (evidence/'browser.json').exists()
    assert sorted(p.name for p in evidence.glob('*-result.json'))==['default-result.json']
    b=private_json(evidence/'baseline.json');signal=private_json(evidence/'default-result.json');assert signal['result']['name']=='default' and signal['result']['state']=='Succeeded'
    assert b['validationSourceSha256']==source_closure(repo,b['validationSourceRevision'],seeds=SOURCES)
    no_active_work();assert common.core_snapshot()=={k:tuple(v) for k,v in b['coreBefore'].items()}
    absent=subprocess.run(['docker','inspect',b['containerId']],capture_output=True,timeout=15);assert absent.returncode!=0 and b'no such' in absent.stderr.lower()
    source=Path(b['source']);root=Path(b['root']);assert source.parent==common.APPS and source.name==b['containerName'] and re.fullmatch('wdv-container-publish-[a-f0-9]{32}',source.name)
    assert source.resolve(strict=True)==source and root==source/'recursive-tree' and {p.name for p in source.iterdir()}=={'original.bin','recursive-tree'}
    parent=source.lstat();data=snapshot(signal['result']['id']);assert data==private_json(evidence/'default.sql.json')
    plan=managed_plan(json.loads(data['row']['plan_json']));assert [parent.st_dev,parent.st_ino]==plan['parentIdentity'] and parent.st_uid==0 and stat.S_IMODE(parent.st_mode)==0o755
    reader=load('/usr/local/libexec/whaledeck-container-files.py','recursive_acl_preserved_reader');files=reader.trusted_module('whaledeck-files.py','recursive_acl_preserved_files')
    storage=load('/usr/local/libexec/whaledeck-file-backups.py','recursive_acl_preserved_hdd').FileBackups(files);acl=load('/usr/local/libexec/whaledeck-container-acl.py','recursive_acl_preserved_acl')
    content=hashlib.shake_256(('recursive-browser:'+b['containerId']).encode()).digest(b['bytesPerFile']);assert hashlib.sha256(content).hexdigest().upper()==b['sha256']
    after=inventory(root,reader,content,acl)
    out=evidence/('preserved-default-readback-'+uuid.uuid4().hex);out.mkdir(mode=0o700)
    proof=verify_case(signal['result'],b,signal['subject'],out,reader,storage,b['original'],after,acl)
    (out/'physical-readback.json').write_text(json.dumps(proof))
    leaf=source/'original.bin';info=leaf.lstat();identity=common.fingerprint(leaf);expected=hashlib.shake_256(('owned-container-original:'+source.name).encode()).digest(common.SIZE)
    assert stat.S_ISREG(info.st_mode) and info.st_nlink==1 and (info.st_uid,info.st_gid,stat.S_IMODE(info.st_mode))==(1000,1000,0o640) and leaf.read_bytes()==expected
    remove_proved(root,reader,content,after,acl)
    assert common.fingerprint(leaf)==identity and leaf.read_bytes()==expected and {p.name for p in source.iterdir()}=={'original.bin'}
    leaf.unlink();assert not list(source.iterdir()) and (source.stat().st_dev,source.stat().st_ino)==(parent.st_dev,parent.st_ino);source.rmdir()
    no_active_work();assert common.core_snapshot()=={k:tuple(v) for k,v in b['coreBefore'].items()}
    result=dict(revision=b['revision'],originalWholeGatePassed=False,firstDefaultCasePhysicallyAndHistoryVerified=True,aggregateHddRecords=1,childHddRecords=4,noRepeatedFileMutation=True,originalNormalDualLogoutAccepted=False,originalFailurePreserved=True,exactSourceAndContainerAbsent=True,coreUnchanged=11,readbackDirectory=out.name)
    (evidence/'recovered-first-default.json').write_text(json.dumps(result));return result

if __name__=='__main__':
    assert os.geteuid()==0 and len(sys.argv)==2;os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0'
    print(json.dumps({'preservedRecursiveAclDefault':'verified',**recover(sys.argv[1])}))
