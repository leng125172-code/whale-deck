#!/usr/bin/env python3
"""Frozen internal history engine on an owned container and physical HDD.

No installed helper/RPC/API/UI is added. Policy is copied into an isolated
private HDD sandbox; existing captures and production settings stay intact.
Verify full binary source before/after an actual owned container restart.
"""
import base64
import hashlib
import json
import os
from pathlib import Path
import stat
import sys
import tempfile
import uuid

import validate_container_file_bff_extended as owned_fixture
from validate_container_file_staging import load

PHASE='startup'


def main():
    global PHASE
    assert os.geteuid()==0;os.umask(0o077)
    host=Path(__file__).resolve().parents[1]/'host'
    names=('whaledeck-container-files.py','whaledeck-files.py','whaledeck-container-terminal.py',
        'whaledeck-container-upload.py','whaledeck-file-backups.py','whaledeck-container-backups.py','whaledeck-container-history.py')
    frozen={name:(host/name).read_bytes() for name in names}
    hashes={name:hashlib.sha256(data).hexdigest() for name,data in frozen.items()}
    evidence=host.parent/'artifacts'/('container-history-native-'+uuid.uuid4().hex);evidence.mkdir(mode=0o700)
    def save(name,value):
        assert name in ('baseline.json','verified.json')
        path=evidence/name;path.write_text(json.dumps(value));path.chmod(0o600)
    with tempfile.TemporaryDirectory(prefix='wdv-container-history-',dir='/var/lib') as temporary:
        bundle=Path(temporary)
        for name,data in frozen.items():path=bundle/name;path.write_bytes(data);path.chmod(0o750)
        reader=load(bundle/names[0],'native_history_reader')
        files=reader.trusted_module(names[1],'native_history_files')
        docker=reader.trusted_module(names[2],'native_history_docker')
        uploads=load(bundle/names[3],'native_history_uploads')
        backups=load(bundle/names[4],'native_history_backups')
        captures=load(bundle/names[5],'native_history_captures')
        history=load(bundle/names[6],'native_history_catalog')
        production=backups.FileBackups(files);configuration=production.configuration()
        PHASE='attest-existing-hdd-before-private-sandbox'
        with production.target() as (_,parent):
            private_name='wdv-container-history-'+uuid.uuid4().hex
            os.mkdir(private_name,0o700,dir_fd=parent);os.fsync(parent)
            target=Path(configuration['mountPoint'])/configuration['targetRelative']/private_name
            target_identity=target.stat()
        policy=bundle/'policy.json';private_policy={**configuration,'targetRelative':configuration['targetRelative']+'/'+private_name}
        policy.write_text(json.dumps(private_policy));policy.chmod(0o600);backups.CONFIGURATION=str(policy)
        state=bundle/'state';state.mkdir(mode=0o700)
        upload=uploads.ContainerUploads(reader,files,state)
        storage=backups.FileBackups(files,namespace='container-files-v1')
        capture=captures.ContainerBackups(upload,storage);catalog=history.ContainerHistory(upload,storage)
        completed=False;operation=None;expected=None;binding=None
        try:
            with owned_fixture.fixture() as owned:
                identifier=owned['identifier'];location={'containerId':identifier,'path':'work/中文.bin'}
                inspected=docker.api('GET','/containers/'+identifier+'/json')
                target_info=docker.target(inspected,identifier,docker.registry());location['instanceId']=target_info['instanceId']
                old_instance=location['instanceId']
                save('baseline.json',{'sourceHashes':hashes,'containerId':identifier,'originalInstanceId':old_instance,
                    'source':str(owned['folder']),'privateHddTarget':str(target)})
                PHASE='real-private-stage-and-complete-old-version-capture'
                with reader.ContainerRoot(location,files,docker) as session:
                    original=reader.inspect(location,session,files)
                    parent=reader.inspect({**location,'path':'work'},session,files)
                    replacement=hashlib.shake_256(b'owned-container-history-private-staged').digest(owned_fixture.SIZE)
                    operation=uuid.uuid4().hex
                    request={**location,'operationId':operation,'owner':'B'*64,'expectedVersion':original['version'],
                        'expectedParentVersion':parent['version'],'sizeBytes':len(replacement),'sha256':hashlib.sha256(replacement).hexdigest().upper()}
                    binding={key:request[key] for key in ('containerId','instanceId','operationId','owner')}
                    with tempfile.TemporaryFile() as stream:
                        for offset in range(0,len(replacement),reader.CHUNK):
                            stream.write((json.dumps({'type':'chunk','offset':offset,'data':base64.b64encode(replacement[offset:offset+reader.CHUNK]).decode()})+'\n').encode())
                        stream.write(b'{"type":"commit"}\n');stream.seek(0)
                        assert upload.stage(request,session,files.InputLines(stream.fileno()))['state']=='Staged'
                    captured=capture.capture(binding,session)
                    expected=owned['proofs']['中文.bin'][1]
                    assert captured['sha256']==expected and captured['sizeBytes']==owned_fixture.SIZE
                    PHASE='physical-hdd-inventory-and-full-verified-source'
                    query={**location,'after':'','take':10,'expectedVersion':''}
                    listed=catalog.listing(query,session);assert listed['complete'] and listed['invalidRecords']==0 and len(listed['entries'])==1
                    item=listed['entries'][0];assert item['captureId']==operation and item['availability']=='Present'
                    assert item['originalInstanceId']==old_instance and item['sha256']==expected and 'owner' not in item and 'rootId' not in item
                    source={**location,'captureId':operation,'expectedCaptureHash':item['captureHash']}
                    with catalog.verified_source(source,session) as (meta,descriptor):
                        raw=bytearray()
                        while chunk:=os.read(descriptor,reader.CHUNK):raw.extend(chunk)
                        assert bytes(raw)==(owned['folder']/'中文.bin').read_bytes() and hashlib.sha256(raw).hexdigest().upper()==expected
                        assert meta['resourceType']=='ContainerFile' and meta['sizeBytes']==len(raw)
                    assert reader.inspect(location,session,files)['version']==original['version']
                    PHASE='wrong-physical-uuid-fails-without-empty-fallback'
                    policy.write_text(json.dumps({**private_policy,'filesystemUuid':str(uuid.uuid4())}))
                    try:
                        try:catalog.listing(query,session)
                        except files.Rejected:pass
                        else:raise AssertionError('WRONG_UUID_ACCEPTED')
                    finally:policy.write_text(json.dumps(private_policy))
                PHASE='actual-owned-restart-retains-historical-source-binding'
                owned_fixture.command(['docker','stop','--time','1',identifier]);owned_fixture.command(['docker','start',identifier])
                inspected=docker.api('GET','/containers/'+identifier+'/json')
                current=docker.target(inspected,identifier,docker.registry());assert current['instanceId']!=old_instance
                current_location={**location,'instanceId':current['instanceId']}
                with reader.ContainerRoot(current_location,files,docker) as session:
                    new_list=catalog.listing({**current_location,'after':'','take':10,'expectedVersion':''},session)
                    assert new_list['entries']==listed['entries'] and new_list['version']!=listed['version']
                    try:catalog.listing({**current_location,'after':'','take':10,'expectedVersion':listed['version']},session)
                    except reader.Rejected as error:assert str(error)=='CONTAINER_HISTORY_VERSION_CONFLICT'
                    else:raise AssertionError('OLD_INSTANCE_INVENTORY_ACCEPTED')
                    with catalog.verified_source({**current_location,'captureId':operation,'expectedCaptureHash':item['captureHash']},session) as (_,descriptor):
                        raw=bytearray()
                        while chunk:=os.read(descriptor,reader.CHUNK):raw.extend(chunk)
                        assert bytes(raw)==(owned['folder']/'中文.bin').read_bytes() and hashlib.sha256(raw).hexdigest().upper()==expected
                    try:
                        with catalog.verified_source(source,session):raise AssertionError('OLD_CURRENT_SCOPE_ACCEPTED')
                    except reader.Rejected as error:assert str(error)=='CONTAINER_HISTORY_SCOPE_MISMATCH'
                    assert reader.inspect(current_location,session,files)['sizeBytes']==owned_fixture.SIZE
                completed=True
                save('verified.json',{'sourceHashes':hashes,'physicalHddFullBytesBeforeAfterOwnedRestart':True,
                    'bytes':owned_fixture.SIZE,'originalSourceUnchanged':True,'wrongUuidAndOldCurrentScopeRejected':True,
                    'installedHelperRpcSqlApiOrUi':False,'sourceDifferentDevice':captured['differentSourceDevice']})
                print(json.dumps({'nativeContainerHistory':'passed','fullBytes':owned_fixture.SIZE,
                    'actualOwnedRestartAndOldCapture':True,'wrongUuidAndStaleCurrentScopeRejected':True,
                    'installedHelperRpcSqlApiOrUi':False,'sourceHashes':hashes,'evidence':str(evidence)}),flush=True)
        finally:
            assert target.resolve(strict=True)==target
            info=target.stat();assert (info.st_dev,info.st_ino,info.st_uid,stat.S_IMODE(info.st_mode))==(target_identity.st_dev,target_identity.st_ino,0,0o700)
            namespace=target/'container-files-v1'
            if completed and namespace.is_dir() and not namespace.is_symlink():
                assert {x.name for x in namespace.iterdir()}=={operation+'.data',operation+'.json'}
                data=namespace/(operation+'.data');meta_path=namespace/(operation+'.json');meta=json.loads(meta_path.read_bytes())
                for path in (data,meta_path):
                    current=path.lstat();assert stat.S_ISREG(current.st_mode) and current.st_uid==0 and current.st_nlink==1 and stat.S_IMODE(current.st_mode)==0o600
                assert data.stat().st_size==owned_fixture.SIZE and owned_fixture.file_digest(data)==expected
                assert all(meta[key]==binding[key] for key in ('containerId','instanceId','operationId','owner'))
                assert meta['resourceType']=='ContainerFile' and meta['state']=='Verified' and meta['sha256']==expected
                data.unlink();meta_path.unlink();namespace.rmdir()
            if not any(target.iterdir()):target.rmdir();print(json.dumps({'ownedHistoryHddSandboxRemoved':True}),flush=True)
            else:print(json.dumps({'ownedHistoryHddSandboxRemoved':False,'preservedForInspection':True}),flush=True)


if __name__=='__main__':
    try:main()
    except BaseException as error:
        import traceback
        print(json.dumps({'nativeContainerHistory':'failed','phase':PHASE,'failureType':type(error).__name__,
            'locations':[{'file':Path(frame.filename).name,'line':frame.lineno} for frame in traceback.extract_tb(error.__traceback__)]}),file=sys.stderr)
        raise SystemExit(1) from None
