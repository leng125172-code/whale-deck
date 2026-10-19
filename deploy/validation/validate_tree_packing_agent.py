#!/usr/bin/env python3
"""Installed UDS/helper probe with one exact owned disposable tree.

No human/BFF/UI claim. Unknown results keep artifacts and receipts for review;
successful cleanup touches only this invocation's known objects and IDs.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import sys
import tarfile
import uuid
from validate_tree_backup_engine import DIRECTORY,ROOT,proof

REPOSITORY=Path('/data/GitRepos/whale-deck')
STATE=Path('/var/lib/whaledeck-files/tree-packing')
PHASE='startup'
FIXTURE=None


def run(arguments):
    return subprocess.check_output(arguments,text=True,stderr=subprocess.PIPE,timeout=30).strip()


def main():
    global PHASE,FIXTURE
    assert os.geteuid()==0 and len(sys.argv)==3
    binary=Path(sys.argv[1]); revision=sys.argv[2]
    assert re.fullmatch('[a-f0-9]{40}',revision)
    assert binary.parent.parent==REPOSITORY/'deploy/artifacts' and re.fullmatch(r'terminal-probe\.[a-zA-Z0-9]+',binary.parent.name) and binary.name=='TerminalProbe'
    assert binary.resolve()==binary and binary.is_file() and stat.S_ISREG(binary.lstat().st_mode)
    git=['git','-c','safe.directory='+str(REPOSITORY),'-C',str(REPOSITORY)]
    assert run(git+['rev-parse','HEAD'])==revision and not run(git+['status','--porcelain'])
    build=json.loads(run(['/opt/whaledeck-agent/WhaleDeck.Agent','--build-info']))
    assert build['sourceRevision']==revision
    for name in ('whaledeck-files.py','whaledeck-file-tree.py','whaledeck-tree-archive.py','whaledeck-tree-packing.py','whaledeck-privileged'):
        installed=Path('/usr/local/libexec')/name; info=installed.lstat()
        assert stat.S_ISREG(info.st_mode) and info.st_uid==0 and info.st_nlink==1 and not info.st_mode&0o022
        assert hashlib.sha256(installed.read_bytes()).digest()==hashlib.sha256((REPOSITORY/'deploy/host'/name).read_bytes()).digest()
    policy=json.loads(Path('/etc/whaledeck/file-roots.json').read_bytes())
    root=next(item for item in policy if item['id']==ROOT)
    assert root['path']==str(DIRECTORY) and 'files-tree-pack' in root['allowedActions']
    os.umask(0o077)
    fixture=DIRECTORY/('wdv-agent-pack-'+uuid.uuid4().hex[:16]); fixture.mkdir(mode=0o700); FIXTURE=fixture
    before=proof(fixture)[:2]; print(json.dumps({'ownedAgentPackingFixture':fixture.name}),flush=True)
    source=fixture/'source'; source.mkdir(); (source/'空目录').mkdir()
    payload=bytes(range(256))*8192+b'TAIL\x00\xff!'; (source/'中文.bin').write_bytes(payload); (source/'中文.bin').chmod(0o751)
    large=fixture/'large-source'; large.mkdir(); (large/'空目录').mkdir()
    with (large/'large.bin').open('wb') as stream:
        stream.truncate(1024**3+7); stream.seek(1024**3); stream.write(b'TAIL\x00\xff!'); stream.flush(); os.fsync(stream.fileno())
    protected=fixture/'protected'; protected.mkdir(); (protected/'.env').write_bytes(b'synthetic-protected-fixture-not-a-credential')
    original={str(path.relative_to(fixture)):proof(path) for path in (source,source/'中文.bin',source/'空目录',large,large/'large.bin',large/'空目录',protected,protected/'.env')}
    PHASE='installed-signed-grpc-fixed-helper-and-large-download'
    environment=os.environ.copy(); environment['WHALEDECK_PACK_PROBE_RELATIVE_PATH']=fixture.name; environment['WHALEDECK_PACK_PROBE_LARGE']='1'
    completed=subprocess.run([str(binary),'tree-pack'],env=environment,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True,timeout=1800)
    rows=[json.loads(line) for line in completed.stdout.splitlines() if line.strip()]
    if completed.returncode or len(rows)!=1 or rows[0].get('installedTreePackingRpc')!='passed':
        # Persist only the probe's stable safe status, never raw stderr/body.
        for line in completed.stderr.splitlines():
            try: status=json.loads(line)
            except ValueError: continue
            if status.get('terminalProbe')=='failed': print(json.dumps(status),flush=True)
        raise RuntimeError('installed-probe-failed')
    result=rows[0]; assert result['largeBytes']==1024**3+7 and result['signedDownloadWholeArchiveChecksumMatched']
    assert {name:proof(fixture/name) for name in original}==original
    PHASE='independent-disk-tar-and-receipt-verification'
    operations=result['completedOperationIds']; canceled=result['canceledOperationIds']
    assert len(operations)==3 and len(canceled)==1 and len(set(operations+canceled))==4
    assert all(isinstance(value,str) and re.fullmatch('[a-f0-9]{32}',value) for value in operations+canceled)
    allowed={'small.tar','small.tar.gz','large.tar'}; targets=[]; receipts=[]
    for operation in operations+canceled:
        record=STATE/(operation+'.json'); info=record.lstat()
        assert stat.S_ISREG(info.st_mode) and info.st_uid==0 and info.st_nlink==1 and stat.S_IMODE(info.st_mode)==0o600
        value=json.loads(record.read_bytes()); assert value['operationId']==operation
        assert value['plan']['command']['source']['rootId']==ROOT and value['plan']['command']['source']['path'].startswith(fixture.name+'/')
        assert value['state']==('Completed' if operation in operations else 'Aborted')
        receipts.extend((record,STATE/(operation+'.lock')))
        if operation in canceled: assert not value.get('mayHaveStaged'); continue
        command=value['plan']['command']; target=Path(command['target']['path'])
        assert command['target']['rootId']==ROOT and target.parent==Path(fixture.name) and target.name in allowed
        path=fixture/target.name; metadata=proof(path)
        assert list(metadata[:8])==value['newIdentity'] and metadata[-1]==value['archiveSha256'] and path.stat().st_size==value['archiveBytes']
        assert stat.S_IMODE(path.stat().st_mode)==0o640 and (path.stat().st_uid,path.stat().st_gid)==(root.get('ownerUid',0),root.get('ownerGid',0))
        expected=large/'large.bin' if target.name=='large.tar' else source/'中文.bin'
        with tarfile.open(path) as archive:
            assert set(archive.getnames())=={'.',expected.name,'空目录'} and archive.getmember('空目录').isdir()
            with archive.extractfile(expected.name) as stream: assert hashlib.file_digest(stream,'sha256').hexdigest().upper()==proof(expected)[-1]
        targets.append(path)
    assert {item.name for item in targets}==allowed
    assert {name:proof(fixture/name) for name in original}==original
    PHASE='precise-owned-cleanup'
    assert proof(fixture)[:2]==before and fixture.resolve()==fixture
    assert {item.name for item in fixture.iterdir()}=={'source','large-source','protected','.whaledeck-files',*allowed}
    staging=fixture/'.whaledeck-files'; info=staging.lstat(); assert stat.S_ISDIR(info.st_mode) and info.st_uid==0 and stat.S_IMODE(info.st_mode)==0o700 and not any(staging.iterdir())
    fingerprints={str(path):proof(path) for path in receipts+targets}
    for path in receipts:
        info=path.lstat(); assert stat.S_ISREG(info.st_mode) and info.st_uid==0 and info.st_nlink==1 and stat.S_IMODE(info.st_mode)==0o600
    for path in receipts+targets: assert proof(path)==fingerprints[str(path)]
    for path in receipts+targets: path.unlink()
    for folder,children in ((source,{'中文.bin','空目录'}),(large,{'large.bin','空目录'}),(protected,{'.env'})):
        assert {item.name for item in folder.iterdir()}==children
    (source/'中文.bin').unlink(); (source/'空目录').rmdir(); source.rmdir()
    (large/'large.bin').unlink(); (large/'空目录').rmdir(); large.rmdir()
    (protected/'.env').unlink(); protected.rmdir(); staging.rmdir(); fixture.rmdir()
    print(json.dumps({'installedTreePackingAcceptance':'passed','sourceRevision':revision,'smallWholeBytes':len(payload),'largeWholeBytes':1024**3+7,
        'independentArchiveAndSourceProofs':True,'signedUdsRealHelperAndLargeDownload':True,'exactOwnedFixturesAndReceiptsRemoved':True,'bffWorkerHumanAuditAndUi':'not-covered'}),flush=True)


if __name__=='__main__':
    try: main()
    except BaseException as error:
        print(json.dumps({'installedTreePackingAcceptance':'failed','phase':PHASE,'failureType':type(error).__name__,'unknownObjectsPreserved':True,'privateDetailsWithheld':True}),flush=True)
        raise SystemExit(1) from None
