#!/usr/bin/env python3
"""Actual publication barrier, interrupted HDD capture and cross-admin sealing.

Stop only an owned ordinary container after positive partial HDD-copy proof.
No SQL/cache/time edits, service restarts or artificial journal states. A
second owned upload must defer behind the barrier and publish after sealing.
Permanent jobs/audit/Root/HDD evidence remains; uncertain sources are preserved.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import stat
import sys
import time
import uuid

from validate_container_file_staging import load
import validate_container_file_publishing as owned
from validate_container_upload_bff import login, logout, request, snapshot, wait_sql, terminal, root_receipt, temporary_user
from validate_container_upload_recovery_agent import digest, trusted
from validate_databases import Api

PHASE='startup'
STATE=Path('/var/lib/whaledeck-container-uploads')
OLD_SIZE=512*1024**2


def register(base,session,api,fixture,path,body,expected):
    endpoint=base+f'/api/v1/containers/{fixture["identifier"]}/files/'
    target=request(session,'GET',endpoint+'target',200).json()
    sha=hashlib.sha256(body).hexdigest().upper();key=str(uuid.uuid4())
    plan=request(session,'POST',endpoint+'uploads/plan',200,headers=api.headers,json={
        'instanceId':target['instanceId'],'path':path,'sizeBytes':len(body),'sha256':sha,'expectedVersion':expected}).json()
    intent=request(session,'POST',endpoint+'uploads',202,headers={**api.headers,'Idempotency-Key':key},
        json={'planHash':plan['planHash'],'confirmed':True,'idempotencyKey':key}).json()
    return endpoint,intent,sha


def send(session,api,endpoint,intent,body):
    value=request(session,'PUT',endpoint+f'uploads/{intent["id"]}/content',202,
        headers={**api.headers,'Idempotency-Key':intent['id'],'Content-Type':'application/octet-stream'},data=body).json()
    assert value['state']=='Ready'


def recover(base,session,profile,api,original):
    global PHASE
    PHASE='different-current-admin-plan-confirm-and-permanent-seal'
    row=original['row'];identifier=row['id']
    assert row['state']=='Unknown' and row['publish_barrier'] and row['root_started_at_utc'] is not None
    assert row['actor_subject']!=profile['subject']
    retained=root_receipt(row);assert retained['state']=='Staged' and 'originalCapture' in retained
    candidate=request(session,'GET',base+f'/api/v1/files/recovery/container-upload/{identifier}',200).json()
    assert candidate['integrity']=='Verified' and candidate['container']=={
        'containerId':row['container_id'],'instanceId':row['instance_id'],'path':row['relative_path']}
    key=str(uuid.uuid4())
    operation=request(session,'POST',base+'/api/v1/files/recovery/operations',202,
        headers={**api.headers,'Idempotency-Key':key},json={'kind':'container-upload','intentId':identifier,
            'originalVersion':candidate['version'],'disposition':'AcknowledgeUnknown','reasonCode':'OriginalSessionLost'}).json()
    deadline=time.monotonic()+100
    while operation['state']!='Ready':
        assert operation['state'] not in ('Failed','Canceled','Disposed','Expired') and time.monotonic()<deadline
        time.sleep(0.5);operation=request(session,'GET',base+f'/api/v1/files/recovery/operations/{operation["id"]}',200).json()
    assert operation['rootState']=='Unknown' and operation['confirmation']['planHash']
    operation=request(session,'POST',base+f'/api/v1/files/recovery/operations/{operation["id"]}/confirm',202,
        headers={**api.headers,'Idempotency-Key':operation['id']},json={
            'planHash':operation['confirmation']['planHash'],'confirmed':True,'version':operation['version']}).json()
    while operation['state']!='Disposed':
        assert operation['state'] not in ('Failed','Canceled','Expired') and time.monotonic()<deadline
        time.sleep(0.5);operation=request(session,'GET',base+f'/api/v1/files/recovery/operations/{operation["id"]}',200).json()
    assert operation['resultCode']=='FILES_RECOVERY_DISPOSED' and operation['originalResolution']=='Failed'
    final=snapshot(identifier);terminal(final,'Failed','CONTAINER_UPLOAD_ADMIN_ACKNOWLEDGED','Failed')
    mutable={'state','version','lease_token','lease_expires_at_utc','next_attempt_at_utc','publish_barrier','result_code','completed_at_utc'}
    assert {k:v for k,v in final['row'].items() if k not in mutable}=={k:v for k,v in row.items() if k not in mutable}
    sealed=root_receipt(final['row']);assert sealed['schemaVersion']==3 and sealed['state']=='Sealed'
    assert sealed['originalRecord']==retained and sealed['originalRecordHash']==digest('original',retained)
    assert sealed['actorHash']==digest('actor',profile['subject']) and sealed['recoveryId']==uuid.UUID(operation['executionJobId']).hex
    return operation


def main():
    global PHASE
    assert os.geteuid()==0 and len(sys.argv) in (2,4);os.umask(0o077)
    revision=sys.argv[1];assert re.fullmatch('[a-f0-9]{40}',revision)
    base=os.environ.get('WHALEDECK_URL','http://192.168.100.13:8080').rstrip('/')
    repository=Path(__file__).resolve().parents[2]
    if len(sys.argv)==4:
        assert sys.argv[2]=='--recover'
        path=Path(sys.argv[3]);assert path.parent.parent==repository/'deploy/artifacts' and path.name=='baseline.json'
        assert re.fullmatch('container-publish-interruption-[a-f0-9]{32}',path.parent.name)
        assert stat.S_IMODE(trusted(path.parent,True).st_mode)==0o700 and stat.S_IMODE(trusted(path).st_mode)==0o600
        prior=json.loads(path.read_bytes());original=snapshot(prior['intent'])
        assert prior['revision']==revision and original['row']['container_id']==prior['containerId']
        assert original['row']['instance_id']==prior['instanceId'] and original['row']['relative_path']=='work/original.bin'
        with temporary_user(True) as (username,password):
            session,profile=login(base,username,password);api=Api(base,session)
            recover(base,session,profile,api,original);logout(base,session);session.close()
        print(json.dumps({'publishInterruption':'recovery-passed','actualGlobalBarrierReleased':True,'durableEvidenceRetained':True}),flush=True)
        return
    evidence=repository/'deploy/artifacts'/('container-publish-interruption-'+uuid.uuid4().hex);evidence.mkdir(mode=0o700)
    def save(name,value):
        assert name in ('baseline.json','verified.json')
        path=evidence/name;path.write_text(json.dumps(value));path.chmod(0o600)
    backup=load(Path('/usr/local/libexec/whaledeck-file-backups.py'),'publish_interruption_hdd')
    files=load(Path('/usr/local/libexec/whaledeck-files.py'),'publish_interruption_files')
    storage=backup.FileBackups(files);configuration=storage.configuration()
    history=Path(configuration['mountPoint'])/configuration['targetRelative']/'container-files-v1'
    assert shutil.disk_usage(history).free>8*1024**3 and shutil.disk_usage(owned.common.APPS).free>8*1024**3
    body=hashlib.shake_256(b'owned-publish-interruption-v1').digest(owned.common.SIZE)
    with temporary_user(True) as (other_name,other_password):
        other,other_profile=login(base,other_name,other_password);other_api=Api(base,other)
        assert request(other,'GET',base+'/api/v1/system/status',200).json()['build']['sourceRevision']==revision
        with owned.fixture() as first,owned.fixture() as second:
            PHASE='prepare-owned-source-and-freeze-original-version'
            source=first['folder']/'original.bin';block=hashlib.shake_256(b'owned-512mib-old-version').digest(1024**2)
            checksum=hashlib.sha256()
            with source.open('wb') as stream:
                for _ in range(OLD_SIZE//len(block)):stream.write(block);checksum.update(block)
                stream.flush();os.fsync(stream.fileno())
            before=owned.common.fingerprint(source);old_sha=checksum.hexdigest().upper()
            first['proofs']['original.bin']=before,old_sha
            with temporary_user(True) as (owner_name,owner_password):
                owner,owner_profile=login(base,owner_name,owner_password);owner_api=Api(base,owner)
                endpoint=base+f'/api/v1/containers/{first["identifier"]}/files/'
                target=request(owner,'GET',endpoint+'target',200).json()
                query={'instanceId':target['instanceId'],'path':'work','take':100}
                page=request(owner,'GET',endpoint+'entries',200,params=query).json()
                entry=next(item for item in page['entries'] if item['name']=='original.bin')
                endpoint,intent,sha=register(base,owner,owner_api,first,'work/original.bin',body,entry['version'])
                identifier=intent['id'];operation=uuid.UUID(identifier).hex
                baseline={'revision':revision,'intent':identifier,'containerId':first['identifier'],'instanceId':target['instanceId'],
                    'source':str(first['folder']),'sourceFingerprint':before,'sourceSha256':old_sha,'oldBytes':OLD_SIZE,
                    'secondContainerId':second['identifier'],'secondSource':str(second['folder'])}
                save('baseline.json',baseline);send(owner,owner_api,endpoint,intent,body)
                PHASE='positive-root-and-partial-hdd-copy-before-owned-container-stop'
                deadline=time.monotonic()+90;partial=history/(operation+'.partial');metadata=history/(operation+'.json')
                while True:
                    assert time.monotonic()<deadline
                    try:
                        receipt=json.loads((STATE/(operation+'.json')).read_bytes())
                        capture=json.loads(metadata.read_bytes());info=partial.lstat()
                        if receipt['state']=='Staged' and 'originalCapture' in receipt and capture['state']=='Preparing' and 0<info.st_size<OLD_SIZE:
                            assert stat.S_IMODE(trusted(partial).st_mode)==0o600 and capture['operationId']==operation
                            live=snapshot(identifier);assert live['row']['state']=='Publishing' and live['row']['publish_barrier'] and live['row']['root_started_at_utc']
                            original_container=json.loads(owned.common.command(['docker','inspect',first['identifier']]))[0]
                            owned.verify_container(original_container,first['identifier'],first['folder'].name,first['folder'])
                            assert original_container['State']['Running'] is True
                            partial_before=info.st_size
                            owned.common.command(['docker','stop','--time','1',first['identifier']]);break
                    except FileNotFoundError:pass
                    time.sleep(0.005)
                PHASE='unknown-keeps-the-actual-global-publication-barrier'
                unknown=wait_sql(identifier,'Unknown');row=unknown['row']
                assert row['publish_barrier'] and row['root_started_at_utc'] and row['published_version'] is None
                assert row['lease_token'] is None and row['completed_at_utc'] is None
                retained=root_receipt(row);assert retained['state']=='Staged' and 'originalCapture' in retained and 'publication' not in retained
                assert owned.common.fingerprint(source)==before and owned.common.file_digest(source)==old_sha
                payload=STATE/(operation+'.data');assert stat.S_IMODE(trusted(payload).st_mode)==0o600 and payload.read_bytes()==body
                hdd_identity=owned.common.fingerprint(partial);hdd_sha=owned.common.file_digest(partial)
                capture=json.loads(metadata.read_bytes());assert capture['state']=='Preparing' and 0<partial.stat().st_size<OLD_SIZE
                PHASE='a-different-live-container-upload-defers-behind-the-barrier'
                second_endpoint,waiting,second_sha=register(base,other,other_api,second,'work/after-barrier.bin',body,'ABSENT')
                baseline['waitingIntent']=waiting['id'];save('baseline.json',baseline)
                send(other,other_api,second_endpoint,waiting,body)
                blocked=snapshot(waiting['id']);version=blocked['row']['version'];deferred=0;deadline=time.monotonic()+45
                while deferred<2:
                    assert time.monotonic()<deadline
                    current=snapshot(waiting['id']);candidate=current['row']
                    assert candidate['state']=='Ready' and candidate['root_started_at_utc'] is None and not candidate['publish_barrier']
                    assert snapshot(identifier)['row']['publish_barrier']
                    assert not (second['folder']/'after-barrier.bin').exists()
                    if candidate['version']>version and candidate['lease_token'] is None:
                        deferred+=1;version=candidate['version']
                    time.sleep(0.25)
                PHASE='normal-original-logout-then-exact-original-user-deletion'
                logout(base,owner);owner.close()
            # The original identity no longer exists; this is another real user.
            assert other_profile['subject']!=owner_profile['subject']
            recovery=recover(base,other,other_profile,other_api,unknown)
            PHASE='released-barrier-allows-previously-deferred-publication'
            completed=wait_sql(waiting['id'],'Succeeded');terminal(completed,'Succeeded','CONTAINER_UPLOAD_PUBLISHED','Succeeded')
            output=second['folder']/'after-barrier.bin';assert output.read_bytes()==body
            assert completed['row']['sha256']==second_sha and not completed['row']['previous_retained']
            second['proofs'][output.name]=owned.common.fingerprint(output),second_sha
            assert root_receipt(completed['row'])['state']=='Published'
            # Even a new-file publication creates an empty private staging
            # directory. Track and verify it instead of mistaking it for an
            # unknown extra object during exact fixture cleanup.
            second['private']='.whaledeck-container-files-v1'
            assert payload.read_bytes()==body and owned.common.fingerprint(source)==before and owned.common.file_digest(source)==old_sha
            assert owned.common.fingerprint(partial)==hdd_identity and owned.common.file_digest(partial)==hdd_sha
            assert json.loads(metadata.read_bytes())==capture
            save('verified.json',{'revision':revision,'originalHadRootStartedAndGlobalBarrier':True,'partialBytesBeforeStop':partial_before,
                'partialBytesRetained':partial.stat().st_size,'originalIdentityDeletedBeforeDifferentAdminSeal':True,
                'deferredWorkerClaims':deferred,'secondPublishedOnlyAfterSeal':True,'recoveryId':recovery['id'],
                'originalFullSourceUnchanged':True,'rootPayloadAndIncompleteHddCaptureRetained':True})
        logout(base,other);other.close()
    print(json.dumps({'publishInterruption':'passed','revision':revision,'actualGlobalBarrierAndPartialHddFault':True,
        'differentAdministratorAfterOriginalDeleted':True,'secondUploadDeferredThenPublished':True,
        'coreUnchangedAndOwnedSourcesCleaned':True,'durableEvidenceRetained':True,'evidence':str(evidence)}),flush=True)


if __name__=='__main__':
    try:main()
    except BaseException as error:
        import traceback
        print(json.dumps({'publishInterruption':'failed','phase':PHASE,'failureType':type(error).__name__,
            'locations':[{'file':Path(frame.filename).name,'line':frame.lineno} for frame in traceback.extract_tb(error.__traceback__)],
            'unverifiedDataPreserved':True}),file=sys.stderr)
        raise SystemExit(1) from None
