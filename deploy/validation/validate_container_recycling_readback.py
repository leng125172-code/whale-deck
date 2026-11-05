#!/usr/bin/env python3
"""Read latest candidate against two sealed, cleaned native cases; no namespace replay.

No live container is reconstructed and no claim is made about latest-code live
UID/namespace guards. The original normal/conflict provenance stays distinct.
"""
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import uuid
from validate_container_file_staging import load
from validate_container_recycling import NAMES,preservation

CASES=(('normal','container-recycling-369a84b8f34c4ecb95ce22ce6f15dc7e','Restored'),
       ('conflict','container-recycling-0627fc616bab4f99b16e2300b6bdaec6','Unknown'))


def seal(root):
    m=json.loads((root/'EVIDENCE_SHA256.json').read_text())
    assert set(m)=={str(p.relative_to(root)) for p in root.rglob('*') if p.is_file()}-{'EVIDENCE_SHA256.json'}
    for name,entry in m.items():
        relative=Path(name);assert not relative.is_absolute() and '..' not in relative.parts
        p=root/relative;assert not p.is_symlink();raw=p.read_bytes()
        assert len(raw)==entry['sizeBytes'] and hashlib.sha256(raw).hexdigest()==entry['sha256']
    return hashlib.sha256((root/'EVIDENCE_SHA256.json').read_bytes()).hexdigest()


def main():
    assert os.geteuid()==0;os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0'
    repo=Path(__file__).resolve().parents[2];sha=subprocess.check_output(['git','-C',str(repo),'rev-parse','HEAD'],text=True).strip()
    assert not subprocess.check_output(['git','-C',str(repo),'status','--porcelain']).strip()
    e=repo/'deploy/artifacts'/('container-recycling-readback-'+uuid.uuid4().hex);e.mkdir(mode=0o700)
    bundle=e/'source';bundle.mkdir(mode=0o700);hashes={}
    for name in NAMES:
        raw=subprocess.check_output(['git','-C',str(repo),'show',sha+':deploy/host/'+name]);assert raw==(repo/'deploy/host'/name).read_bytes()
        (bundle/name).write_bytes(raw);(bundle/name).chmod(0o750);hashes[name]=hashlib.sha256(raw).hexdigest()
    reader=load(bundle/NAMES[1],'recycle_readback_reader');f=reader.trusted_module(NAMES[0],'recycle_readback_files')
    storage=load(bundle/NAMES[5],'recycle_readback_storage').FileBackups(f)
    base=load(bundle/NAMES[7],'recycle_readback_history').ContainerPermissionHistory
    directory=load(bundle/NAMES[6],'recycle_readback_journal').ContainerDirectories
    candidate=load(bundle/NAMES[8],'recycle_readback_candidate')
    before=preservation();rows=[]
    for kind,name,wanted in CASES:
        original=repo/'deploy/artifacts'/name;digest=seal(original)
        q=json.loads((original/'request.json').read_text());old=json.loads((original/'verified.json').read_text())
        assert old['sourceAndContainerAbsent'] and old['actualHddAttested']
        fixture=json.loads((original/'fixture.json').read_text());assert not Path(fixture['source']).exists()
        assert subprocess.run(['docker','inspect',fixture['containerId']],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL).returncode!=0
        u=load(bundle/NAMES[3],'recycle_readback_upload_'+kind).ContainerUploads(reader,f,original/'root-state')
        p=load(bundle/NAMES[4],'recycle_readback_publisher_'+kind).ContainerPublisher(u,type('UnusedCapture',(),{'u':u})())
        history=candidate.history_type(base)(storage);engine=candidate.recycling_type(directory)(u,p,original/'root-state',history)
        result=engine.inspect(q,None)
        assert result['state']==wanted and result['historyVerified'] and result['historyAvailable'] and result['bindingCurrent'] is False
        if wanted=='Unknown':assert result['sourceRetained'] is result['sourceRestored'] is None
        else:assert result['sourceRestored'] is True and result['sourceRetained'] is False
        assert seal(original)==digest
        rows.append(dict(kind=kind,originalSourceRevision=old['revision'],originalSeal=digest,currentResult=result))
    after=preservation();assert after==before
    for name,value in [('before.json',before),('after.json',after),('verified.json',dict(sourceRevision=sha,sourceSha256=hashes,
            oldCases=rows,latestMetadataReadable=True,oldSealsUnchanged=True,namespaceReadback=False,
            lateExecutorGuardLiveAccepted=False,additionalTrashOrRestore=False))]:
        with (e/name).open('x') as stream:json.dump(value,stream,ensure_ascii=False,indent=2)
    print(json.dumps(dict(containerRecycleReadback='passed',evidence=e.name,sourceRevision=sha,oldSealsUnchanged=True,additionalTrashOrRestore=False)),flush=True)


if __name__=='__main__':
    try:main()
    except BaseException as error:
        print(json.dumps(dict(containerRecycleReadback='failed',type=type(error).__name__)),flush=True);raise SystemExit(1) from None
