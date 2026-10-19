#!/usr/bin/env python3
"""Actual registered filesystem/direct engine only, not installed RPC/UI.

All writes stay inside one new root-private fixture. Its receipt store is also
isolated there; no live operation/audit/configuration is cleared. Delete only
known invocation-owned files and empty directories after independent proofs.
"""
import hashlib
import json
import os
from pathlib import Path
import stat
import tarfile
import tempfile
import uuid
from validate_tree_backup_engine import load,proof,DIRECTORY,ROOT

PHASE='startup'


def main():
    global PHASE
    assert os.geteuid()==0 and DIRECTORY.resolve()==DIRECTORY
    os.umask(0o077)
    files=load('packing_probe_files','whaledeck-files.py')
    tree=load('packing_probe_tree','whaledeck-file-tree.py')
    archive=load('packing_probe_codec','whaledeck-tree-archive.py')
    packing=load('packing_probe_engine','whaledeck-tree-packing.py')
    policy=files.roots()[ROOT]
    assert policy['path']==str(DIRECTORY) and {'files-list','files-download','files-upload','files-mutate','files-archive','files-tree-pack'}<=set(policy['allowedActions'])
    fixture=Path(tempfile.mkdtemp(prefix='wdv-tree-pack-',dir=DIRECTORY)); fixture_identity=proof(fixture)[:2]
    print(json.dumps({'ownedTreePackingFixture':fixture.name}),flush=True)
    PHASE='owned-source-and-isolated-receipts'
    source=fixture/'source'; source.mkdir(mode=0o750)
    empty=source/'空目录'; empty.mkdir(mode=0o710)
    filename=source/('中文'*40+'.bin'); assert len(filename.name.encode())<=255
    payload=bytes(range(256))*8192+b'TAIL\x00\xff!'; filename.write_bytes(payload); filename.chmod(0o751)
    receipts=fixture/'receipts'; receipts.mkdir(mode=0o700); files.STATE=str(receipts)
    original={name:proof(source/name) for name in ('',filename.name,empty.name)}
    engine=packing.FileTreePacking(files,tree.FileTreeManifest,archive.TreeArchive); requests=[]; outputs=[]
    for archive_format in ('tar','tar.gz'):
        PHASE='actual-'+archive_format+'-publication-full-stream-verification'
        target=fixture/('result.'+archive_format)
        command={'source':{'rootId':ROOT,'path':fixture.name+'/source','expectedVersion':files.version(source.stat())},
            'target':{'rootId':ROOT,'path':fixture.name+'/'+target.name},'format':archive_format}
        request={'operationId':uuid.uuid4().hex,'owner':'A'*64,'plan':engine.plan(command)}
        result=engine.pack(request); requests.append(request); outputs.append(target)
        assert result['state']=='Completed' and result['totalBytes']==len(payload) and result['entryCount']==3
        assert engine.pack(request)==result and engine.lookup({key:request[key] for key in ('operationId','owner')})==result
        assert hashlib.sha256(target.read_bytes()).hexdigest().upper()==result['archiveSha256']
        assert target.stat().st_size==result['archiveBytes'] and stat.S_IMODE(target.stat().st_mode)==0o640
        assert (target.stat().st_uid,target.stat().st_gid)==(policy.get('ownerUid',0),policy.get('ownerGid',0))
        with tarfile.open(target) as encoded:
            with encoded.extractfile(filename.name) as stream: assert stream.read()==payload
            assert encoded.getmember(empty.name).isdir() and encoded.getmember(filename.name).mode==0o751
        assert {name:proof(source/name) for name in original}==original
    PHASE='exact-invocation-owned-fixture-cleanup'
    assert proof(fixture)[:2]==fixture_identity
    expected={source.name,receipts.name,files.STAGING,*[target.name for target in outputs]}
    assert {item.name for item in fixture.iterdir()}==expected
    pending=fixture/files.STAGING
    assert proof(pending)[3]==0 and stat.S_IMODE(pending.stat().st_mode)==0o700 and not any(pending.iterdir())
    history=receipts/'tree-packing'
    names={request['operationId']+suffix for request in requests for suffix in ('.json','.lock')}
    assert {item.name for item in history.iterdir()}==names and {item.name for item in receipts.iterdir()}=={'namespace.lock','tree-packing'}
    owned=[history/name for name in names]+[receipts/'namespace.lock']+outputs
    fingerprints={str(path):proof(path) for path in owned}
    for path in owned:
        info=path.lstat(); assert stat.S_ISREG(info.st_mode) and info.st_nlink==1
        if path not in outputs: assert info.st_uid==0 and stat.S_IMODE(info.st_mode)==0o600
    assert {name:proof(source/name) for name in original}==original
    for path in owned: assert proof(path)==fingerprints[str(path)]
    for path in owned: path.unlink()
    history.rmdir(); receipts.rmdir(); pending.rmdir()
    assert {item.name for item in source.iterdir()}=={filename.name,empty.name} and not any(empty.iterdir())
    filename.unlink(); empty.rmdir(); source.rmdir(); fixture.rmdir()
    print(json.dumps({'actualRegisteredTreePackingEngine':'passed','bothTarAndTarGz':True,'fullSourceBytes':len(payload),
        'sourceBytesInodeMetadataUnchanged':True,'independentStandardTarReaderAndWholeArchiveSha256':True,
        'newTargetOnlyAndRegistryOwnership':True,'exactOwnedFixtureAndIsolatedReceiptCleanup':True,
        'installedAgentRpcOrUserInterface':False}),flush=True)


if __name__=='__main__':
    try: main()
    except BaseException as error:
        print(json.dumps({'actualRegisteredTreePackingEngine':'failed','phase':PHASE,'failureType':type(error).__name__,'unknownObjectsPreserved':True,'privateDetailsWithheld':True}),flush=True)
        raise SystemExit(1) from None
