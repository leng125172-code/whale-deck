#!/usr/bin/env python3
"""Frozen internal ordinary-file recycle on a new real container/attested HDD.

Not installed, signed RPC, permanent business, UI, or lost-private HDD restore.
Only this invocation's proved fixture file and empty private directory are removed.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import uuid
import validate_container_file_publishing as owned
from validate_container_file_staging import load
from validate_container_directory_restart import preflight
from validate_container_recursive_acl_faults import preservation_snapshot
from recursive_acl_restore_agent_io import installed,snapshot,hdd_snapshot
from validate_recursive_acl_restore_paging import restore_database,database
from validate_container_recursive_acl_agent import host_identity

NAMES=('whaledeck-files.py','whaledeck-container-files.py','whaledeck-container-terminal.py',
    'whaledeck-container-upload.py','whaledeck-container-publish.py','whaledeck-file-backups.py',
    'whaledeck-container-directories.py','whaledeck-container-permissions.py','whaledeck-container-recycling.py')


def preservation():
    io=installed()
    return dict(protected=preservation_snapshot(io.storage),sourceRoot=snapshot(io.sourceState),
        restoreRoot=snapshot(io.restoreState),restoreHdd=hdd_snapshot(io,True),core=owned.common.core_snapshot(),
        hosts=host_identity('34153d2c454942b2665902772b7d5e48226cf7db'),restoreDatabase=restore_database(),sourceDatabase=database())


def main(revision):
    assert os.geteuid()==0 and re.fullmatch('[a-f0-9]{40}',revision);os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0'
    repo=Path(__file__).resolve().parents[2]
    assert subprocess.check_output(['git','-C',str(repo),'rev-parse','HEAD'],text=True).strip()==revision
    assert not subprocess.check_output(['git','-C',str(repo),'status','--porcelain']).strip()
    preflight();before=preservation();e=repo/'deploy/artifacts'/('container-recycling-'+uuid.uuid4().hex);e.mkdir(mode=0o700)
    save=lambda n,v:(e/n).write_text(json.dumps(v,ensure_ascii=False))
    bundle=e/'source';bundle.mkdir(mode=0o700);hashes={}
    for n in NAMES:
        raw=subprocess.check_output(['git','-C',str(repo),'show',revision+':deploy/host/'+n]);assert raw==(repo/'deploy/host'/n).read_bytes()
        (bundle/n).write_bytes(raw);(bundle/n).chmod(0o750);hashes[n]=hashlib.sha256(raw).hexdigest()
    reader=load(bundle/NAMES[1],'recycle_native_reader');files=reader.trusted_module(NAMES[0],'recycle_native_files');docker=reader.trusted_module(NAMES[2],'recycle_native_docker')
    state=e/'root-state';state.mkdir(mode=0o700)
    u=load(bundle/NAMES[3],'recycle_native_upload').ContainerUploads(reader,files,state)
    publisher=load(bundle/NAMES[4],'recycle_native_publish').ContainerPublisher(u,type('UnusedCapture',(),{'u':u})())
    storage=load(bundle/NAMES[5],'recycle_native_storage').FileBackups(files)
    directory=load(bundle/NAMES[6],'recycle_native_state').ContainerDirectories;base=load(bundle/NAMES[7],'recycle_native_history').ContainerPermissionHistory
    candidate=load(bundle/NAMES[8],'recycle_native_candidate');history=candidate.history_type(base)(storage);engine=candidate.recycling_type(directory)(u,publisher,state,history)
    save('before.json',before);save('source.json',dict(revision=revision,sha256=hashes));print(json.dumps(dict(containerRecycleCandidate='started',evidence=e.name)),flush=True)
    with owned.fixture() as fixture:
        fixture['retain_unsettled']=True
        path=fixture['folder']/'recycle-candidate.bin';data=hashlib.shake_256(('recycle:'+fixture['identifier']).encode()).digest(1024**2+7)
        path.write_bytes(data);os.chown(path,1000,1000);path.chmod(0o640);os.setxattr(path,'user.recycle-native',b'preserved ordinary attribute');os.utime(path,ns=(1700000000123456789,1700000000123456789))
        container=json.loads(owned.common.command(['docker','inspect',fixture['identifier']]))[0]
        c=dict(containerId=fixture['identifier'],instanceId=str(container['State']['Pid'])+':'+container['State']['StartedAt'],path='work/'+path.name)
        first=list(reader.fingerprint(path.stat()));save('fixture.json',dict(**c,source=str(fixture['folder']),original=first,sha256=hashlib.sha256(data).hexdigest().upper()))
        with reader.ContainerRoot(c,files,docker) as session:
            command={**c,'expectedVersion':reader.version(c,c['path'],path.stat())};q=dict(operationId=uuid.uuid4().hex,owner='B'*64,plan=engine.plan(command,session));save('request.json',q)
            assert path.read_bytes()==data and list(reader.fingerprint(path.stat()))==first
            result=engine.apply(q,session);assert result['state']=='Trashed' and result['bindingCurrent'] and result['historyVerified'];assert not path.exists()
            retained=fixture['folder']/engine.PRIVATE/(q['operationId']+'.trash');assert retained.read_bytes()==data and list(reader.fingerprint(retained.stat()))[:8]==first[:8]
            save('trashed.json',result);save('retained-identity.json',list(reader.fingerprint(retained.stat())))
            with history.target(False) as (_,fd):
                m=history.read(fd,q);capture=os.open(q['operationId']+'.data',os.O_RDONLY|os.O_NOFOLLOW,dir_fd=fd)
                try:
                    checksum=hashlib.sha256()
                    while chunk:=os.read(capture,files.CHUNK):checksum.update(chunk)
                    assert checksum.hexdigest().upper()==hashlib.sha256(data).hexdigest().upper()
                finally:os.close(capture)
                assert m['captureKind']=='Trash' and m['captureState']=='Trashed';save('hdd-trashed.json',m)
            assert engine.apply(q,session)['replayed'] and not path.exists()
        # Reopen through a real new pidfd/root FD and reattest the actual HDD.
        with reader.ContainerRoot(c,files,docker) as session:
            assert engine.inspect(q,session)['state']=='Trashed'
            restored=engine.restore(q,session);assert restored['state']=='Restored' and restored['bindingCurrent'];save('restored.json',restored)
            assert path.read_bytes()==data and list(reader.fingerprint(path.stat()))[:8]==first[:8] and os.getxattr(path,'user.recycle-native')==b'preserved ordinary attribute'
            observed=list(reader.fingerprint(path.stat()));assert engine.restore(q,session)['replayed'] and list(reader.fingerprint(path.stat()))==observed
            with history.target(False) as (_,fd):m=history.read(fd,q);assert m['captureState']=='Restored';save('hdd-restored.json',m)
            save('final-file.json',observed)
        record=json.loads((state/(q['operationId']+'.json')).read_text());private=fixture['folder']/engine.PRIVATE
        assert list(reader.fingerprint(path.stat()))==observed and path.read_bytes()==data;path.unlink()
        assert not list(private.iterdir()) and [private.stat().st_dev,private.stat().st_ino]==record['privateIdentity'] and private.stat().st_uid==0 and private.stat().st_mode&0o777==0o700
        private.rmdir();fixture['retain_unsettled']=False
    after=preservation();assert after==before;save('after.json',after)
    assert not fixture['folder'].exists();save('verified.json',dict(revision=revision,realContainerAndFreshRootSession=True,actualHddAttested=True,
        originalInodeRestored=True,ordinaryUserXattrPreserved=True,sourceAndContainerAbsent=True,oldProductionUnchanged=True,
        installed=False,signedRpc=False,businessUi=False,differentSourceDevice=m['differentSourceDevice']))
    print(json.dumps(dict(containerRecycleCandidate='passed',evidence=e.name,installed=False,signedRpc=False,businessUi=False)),flush=True)


if __name__=='__main__':
    try:assert len(sys.argv)==2;main(sys.argv[1])
    except BaseException as error:
        import traceback
        f=traceback.extract_tb(error.__traceback__)[-1];print(json.dumps(dict(containerRecycleCandidate='failed',type=type(error).__name__,file=Path(f.filename).name,line=f.lineno)),flush=True);raise SystemExit(1) from None
