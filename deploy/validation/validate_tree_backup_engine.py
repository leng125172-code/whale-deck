#!/usr/bin/env python3
"""Direct engine/actual configured HDD probe, not an installed RPC/UI proof.

Only a newly created, exact owned fixture and its checksum/inode-proven capture
may be removed. No production source, archive, job, audit or configuration is
modified. Failures preserve unknown objects for explicit inspection.
"""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import stat
import sys
import tempfile
import uuid

DIRECTORY = Path('/data/WhaleDeck/files')
ROOT = 'whaledeck.files.shared'
PHASE = 'startup'


def load(name,filename):
    spec = importlib.util.spec_from_file_location(name,Path(__file__).resolve().parents[1]/'host'/filename)
    module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module); return module


def proof(path):
    value = path.lstat()
    digest = None
    if stat.S_ISREG(value.st_mode):
        with path.open('rb') as file: digest = hashlib.file_digest(file,'sha256').hexdigest().upper()
    else: assert stat.S_ISDIR(value.st_mode)
    return (value.st_dev,value.st_ino,value.st_mode,value.st_uid,value.st_gid,value.st_nlink,value.st_size,value.st_mtime_ns,value.st_ctime_ns,digest)


def main():
    global PHASE
    assert os.geteuid()==0 and DIRECTORY.resolve()==DIRECTORY and DIRECTORY.is_dir()
    os.umask(0o077)
    files = load('tree_backup_probe_files','whaledeck-files.py')
    ordinary = load('tree_backup_probe_hdd','whaledeck-file-backups.py')
    tree = load('tree_backup_probe_tree','whaledeck-file-tree.py')
    archive = load('tree_backup_probe_archive','whaledeck-tree-archive.py')
    backups = load('tree_backup_probe_capture','whaledeck-tree-backups.py')
    registry = files.roots()[ROOT]
    assert registry['path']==str(DIRECTORY) and {'files-list','files-download'}<=set(registry['allowedActions'])
    filename = '中文长文件名'*13+'.bin'; empty = '空目录'
    assert len(filename.encode('utf-8'))<=255
    source = Path(tempfile.mkdtemp(prefix='wdv-tree-backup-',dir=DIRECTORY))
    restored = Path(tempfile.mkdtemp(prefix='wdv-tree-restore-',dir=DIRECTORY))
    source_identity = proof(source)[:2]; restored_identity = proof(restored)[:2]
    assert re.fullmatch('wdv-tree-backup-[a-zA-Z0-9_-]+',source.name)
    assert re.fullmatch('wdv-tree-restore-[a-zA-Z0-9_-]+',restored.name)
    print(json.dumps({'ownedTreeBackupFixture':source.name,'ownedTreeRestoreFixture':restored.name}),flush=True)
    PHASE = 'owned-source-fixtures'
    payload = bytes(range(256))*8192+b'TAIL\0\xff!'
    (source/filename).write_bytes(payload); (source/filename).chmod(0o640); (source/empty).mkdir(mode=0o750)
    original = {name:proof(source/name) for name in ('',filename,empty)}
    command = {'rootId':ROOT,'path':source.name,'expectedVersion':files.version(source.stat())}
    manifest = tree.FileTreeManifest(files).scan(command)
    request = {'operationId':uuid.uuid4().hex,'owner':'A'*64,'source':command,
        'manifestSha256':manifest['manifestSha256'],'captureKind':'Trash'}
    engine = backups.TreeBackups(files,ordinary.FileBackups(files),tree.FileTreeManifest,archive.TreeArchive)
    PHASE = 'actual-configured-hdd-attestation-and-full-directory-capture'
    result = engine.ensure(request)
    assert result['state']=='Verified' and result['entryCount']==3 and result['totalBytes']==len(payload)
    assert engine.ensure(request)==result
    assert {name:proof(source/name) for name in original}==original
    PHASE = 'actual-hdd-archive-private-fd-restore-and-independent-disk-proof'
    with engine.b.target() as (configuration,base):
        store = files.open_at(base,engine.FOLDER,os.O_RDONLY | os.O_DIRECTORY)
        try:
            saved = engine.read(store,request['operationId'],engine.seed(request,registry,configuration,store))
            assert saved['state']=='Verified'
            descriptor = files.open_at(store,request['operationId']+'.tar.gz',os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW)
            destination = os.open(restored,os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
            try:
                codec = archive.TreeArchive(files,tree.FileTreeManifest)
                plan = codec.inspect(descriptor)
                assert plan['archiveSha256']==result['archiveSha256'] and plan['contentSha256']==result['contentSha256']
                codec.extract(descriptor,destination,plan)
            finally: os.close(destination); os.close(descriptor)
            assert (restored/filename).read_bytes()==payload and (restored/empty).is_dir()
            assert stat.S_IMODE((restored/filename).stat().st_mode)==0o640
            assert (restored/filename).stat().st_mtime_ns==(source/filename).stat().st_mtime_ns
            assert {name:proof(source/name) for name in original}==original
            PHASE = 'exact-owned-capture-and-fixture-cleanup'
            names = {request['operationId']+suffix for suffix in ('.json','.manifest.json','.tar.gz','.lock')}
            with os.scandir(store) as entries:
                assert {entry.name for entry in entries if entry.name.startswith(request['operationId'])}==names
            before = {}
            for name in names:
                value = os.stat(name,dir_fd=store,follow_symlinks=False)
                assert stat.S_ISREG(value.st_mode) and value.st_uid==0 and value.st_nlink==1 and stat.S_IMODE(value.st_mode)==0o600
                before[name]=(value.st_dev,value.st_ino,value.st_size,value.st_mtime_ns,value.st_ctime_ns)
            engine.verify(store,saved,codec)
            # Exact, invocation-owned objects only, with identities checked again.
            for name in names:
                value = os.stat(name,dir_fd=store,follow_symlinks=False)
                assert before[name]==(value.st_dev,value.st_ino,value.st_size,value.st_mtime_ns,value.st_ctime_ns)
            for name in names: os.unlink(name,dir_fd=store)
            os.fsync(store)
        finally: os.close(store)
    assert proof(source)[:2]==source_identity and proof(restored)[:2]==restored_identity
    assert {name:proof(source/name) for name in original}==original
    for folder in (source,restored):
        assert folder.parent==DIRECTORY and folder.resolve()==folder
        assert {entry.name for entry in folder.iterdir()}=={filename,empty}
        assert stat.S_ISDIR((folder/empty).lstat().st_mode) and not any((folder/empty).iterdir())
        assert stat.S_ISREG((folder/filename).lstat().st_mode) and (folder/filename).read_bytes()==payload
        (folder/filename).unlink(); (folder/empty).rmdir(); folder.rmdir()
    print(json.dumps({'directActualHddTreeEngine':'passed','archiveAndRestoreWholeBytes':len(payload),
        'mountedHddPolicyAttested':True,'differentSourceDevice':result['differentSourceDevice'],
        'sourceWholeBytesInodeModeAndTimesUnchanged':True,'independentRestoredDiskBytesVerified':True,
        'exactOwnedCaptureAndFixtureCleanup':True,'installedAgentRpcOrUserInterface':False}),flush=True)


if __name__=='__main__':
    try: main()
    except BaseException as error:
        print(json.dumps({'directActualHddTreeEngine':'failed','phase':PHASE,'failureType':type(error).__name__,
            'unknownObjectsPreserved':True,'privateDetailsWithheld':True}),file=sys.stderr)
        raise SystemExit(1) from None
