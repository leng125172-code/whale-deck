#!/usr/bin/env python3
"""Finish exactly one failed UI gate's proved fixture; never repeat mutations.

Requires all six original physical signals and permanent SQL/Root/HDD proofs.
Original logout failure remains Failed; separate auth-only evidence is explicit.
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
from validate_container_recursive_ownership import inventory, remove_proved
from validate_container_recursive_ownership_browser import CASES, SOURCES, snapshot, managed_plan, verify_case, unprivileged, no_active_work
from verify_container_recursive_permissions import private_json


def recover(name):
    assert re.fullmatch('container-recursive-ownership-browser-[a-f0-9]{32}',name)
    repo=Path(__file__).resolve().parents[2];evidence=repo/'deploy/artifacts'/name
    info=evidence.lstat();assert evidence.resolve(strict=True)==evidence and info.st_uid==0 and stat.S_IMODE(info.st_mode)==0o700
    assert not (evidence/'verified.json').exists() and not (evidence/'recovered.json').exists()
    original=private_json(evidence/'baseline.json');proofs=private_json(evidence/'results.json')
    assert len(proofs)==6 and not (evidence/'browser.json').exists()
    failures=[json.loads(line) for line in (evidence/'browser.stderr').read_text().splitlines()]
    assert len(failures)==1 and failures[0]['recursiveOwnershipBrowser']=='failed' and failures[0]['phase']=='visible-dual-logout'
    for file in SOURCES:
        frozen=subprocess.check_output(['git','-C',str(repo),'show',original['validationSourceRevision']+':deploy/validation/'+file])
        assert hashlib.sha256(frozen).hexdigest()==original['validationSourceSha256'][file]
    logout=private_json(evidence/'logout-only.json')
    assert logout['normalUiLogout'] is True and logout['providerNavigationObserved'] is True and logout['authOnly'] is True and logout['fileMutations']==0
    assert logout['postStatus']==302 and logout['bffStatus']==401 and logout['identityStatus'] in (401,403) and logout['finalLoginPage'] is True
    no_active_work();assert common.core_snapshot()=={key:tuple(v) for key,v in original['coreBefore'].items()}
    absent=subprocess.run(['docker','inspect',original['containerId']],capture_output=True,timeout=15)
    assert absent.returncode!=0 and b'no such' in absent.stderr.lower()
    previous=original['original'];subject=None;completed=[]
    reader=load('/usr/local/libexec/whaledeck-container-files.py','ownership_recovery_reader')
    files=reader.trusted_module('whaledeck-files.py','ownership_recovery_files')
    storage=load('/usr/local/libexec/whaledeck-file-backups.py','ownership_recovery_storage').FileBackups(files)
    readback=evidence/('recovery-read-back-'+uuid.uuid4().hex);readback.mkdir(mode=0o700)
    for label,proof in zip(CASES,proofs,strict=True):
        signal=private_json(evidence/(label+'-result.json'));verified=private_json(evidence/(label+'-verified.json'))
        assert signal['result']==proof['result'] and proof['result']['name']==label and proof['before']==previous
        if subject is None:subject=signal['subject']
        assert signal['subject']==subject and verified=={'id':proof['result']['id'],'physicalAndHistoryVerified':True}
        assert verify_case(proof['result'],original,subject,readback,reader,storage,proof['before'],proof['after'])==proof
        for item in readback.glob(label+'.*'):
            assert item.read_bytes()==(evidence/item.name).read_bytes()
        completed.append(proof['result']);previous=proof['after']
    assert logout['subject']!=subject
    root=Path(original['root']);source=Path(original['source']);name_in_fixture=original['containerName']
    assert source.parent==common.APPS and source.name==name_in_fixture and re.fullmatch('wdv-container-publish-[a-f0-9]{32}',source.name)
    assert source.resolve(strict=True)==source and root==source/'recursive-tree'
    parent=source.lstat();plan=managed_plan(json.loads(snapshot(completed[0]['id'])['row']['plan_json']))
    assert [parent.st_dev,parent.st_ino]==plan['parentIdentity'] and parent.st_uid==0 and stat.S_IMODE(parent.st_mode)==0o755
    assert {p.name for p in source.iterdir()}=={'original.bin','recursive-tree'}
    content=hashlib.shake_256(('recursive-browser:'+original['containerId']).encode()).digest(original['bytesPerFile'])
    assert hashlib.sha256(content).hexdigest().upper()==original['sha256'] and inventory(root,reader,content)==previous==private_json(evidence/'physical-final.json')
    leaf=source/'original.bin';info=leaf.lstat();identity=common.fingerprint(leaf)
    expected=hashlib.shake_256(('owned-container-original:'+name_in_fixture).encode()).digest(common.SIZE)
    assert stat.S_ISREG(info.st_mode) and info.st_nlink==1 and (info.st_uid,info.st_gid,stat.S_IMODE(info.st_mode))==(1000,1000,0o640) and leaf.read_bytes()==expected
    # Only policy/metadata endpoints: original CID is absent, never recreated.
    unprivileged('http://127.0.0.1:8080',original,completed[0])
    remove_proved(root,reader,content,previous)
    assert common.fingerprint(leaf)==identity and leaf.read_bytes()==expected and {p.name for p in source.iterdir()}=={'original.bin'}
    leaf.unlink();assert not list(source.iterdir()) and (source.stat().st_dev,source.stat().st_ino)==(parent.st_dev,parent.st_ino);source.rmdir()
    no_active_work();assert common.core_snapshot()=={key:tuple(v) for key,v in original['coreBefore'].items()}
    recovered={'revision':original['revision'],'containerId':original['containerId'],'subject':subject,'completed':completed,
        'actualUi':True,'normalDualLogout':False,'separateAuthOnlyLogout':True,'registrations':6,'confirmations':5,'registrationRetries':6,'confirmationRetries':5,'contentUploads':0,
        'countersProvedByFrozenBrowserBeforeLogout':True}
    (evidence/'browser-recovered.json').write_text(json.dumps(recovered))
    accepted={'revision':original['revision'],'actualBrowserAndIdentity':True,'cases':6,'succeeded':5,'canceledBeforeExecution':1,
        'aggregateHddRecords':5,'childHddRecords':20,'originalGateFailedAtLogout':True,'originalFailurePreserved':True,'separateAuthOnlyLogout':True,
        'ordinaryAndAnonymousCheckedAfterOriginalCidRemoval':True,'noRepeatedFileMutation':True,'exactSourceAndContainerAbsent':True,'coreUnchanged':11,
        'recoverySourceRevision':subprocess.check_output(['git','-C',str(repo),'rev-parse','HEAD'],text=True).strip()}
    (evidence/'recovered.json').write_text(json.dumps(accepted));return accepted


if __name__=='__main__':
    assert os.geteuid()==0 and len(sys.argv)==2;os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0'
    print(json.dumps({'originalSixCaseEvidenceRecovered':True,**recover(sys.argv[1])}))
