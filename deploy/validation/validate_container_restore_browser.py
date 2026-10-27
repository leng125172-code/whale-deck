#!/usr/bin/env python3
"""Installed browser restoration, independently checked SQL/Root/full HDD bytes.

One owned ordinary container only. Retain permanent jobs/audit/HDD; preserve
uncertain fixture bytes. No request mocking, production data, SQL/cache edits,
credentials/sessions export, source republishing or broad cleanup.
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

import validate_container_file_publishing as owned
from validate_container_file_staging import load
from validate_container_upload_bff import login,logout,request,snapshot,terminal,root_receipt,wait_sql,query
from validate_container_upload_recovery_agent import trusted
from validate_databases import Api
from validate_oidc import required_environment

PHASE='startup'


def main():
    global PHASE
    assert os.geteuid()==0 and len(sys.argv)==2;os.umask(0o077)
    revision=sys.argv[1];assert re.fullmatch('[a-f0-9]{40}',revision)
    repository=Path(__file__).resolve().parents[2]
    evidence=repository/'deploy/artifacts'/('container-restore-browser-'+uuid.uuid4().hex);evidence.mkdir(mode=0o700)
    def save(name,value):
        assert name in ('baseline.json','browser.stdout','browser.stderr','verified.json')
        p=evidence/name;p.write_text(value if isinstance(value,str) else json.dumps(value));p.chmod(0o600)
    base=os.environ.get('WHALEDECK_URL','http://192.168.100.13:8080').rstrip('/')
    session,_=login(base,required_environment('VALIDATION_USERNAME'),required_environment('VALIDATION_PASSWORD'));api=Api(base,session)
    assert request(session,'GET',base+'/api/v1/system/status',200).json()['build']['sourceRevision']==revision
    backups=load(Path('/usr/local/libexec/whaledeck-file-backups.py'),'restore_browser_hdd');files=load(Path('/usr/local/libexec/whaledeck-files.py'),'restore_browser_files')
    config=backups.FileBackups(files).configuration();history=Path(config['mountPoint'])/config['targetRelative']/'container-files-v1'
    with owned.fixture() as fixture:
        endpoint=base+f'/api/v1/containers/{fixture["identifier"]}/files/'
        target=request(session,'GET',endpoint+'target',200).json();original_instance=target['instanceId']
        original=request(session,'GET',endpoint+'entry',200,params={'instanceId':original_instance,'path':'work/original.bin'}).json()
        output=fixture['folder']/'original.bin';before=output.stat()
        replacement=hashlib.shake_256(b'owned-restoration-original-replacement-v1').digest(owned.common.SIZE);sha=hashlib.sha256(replacement).hexdigest().upper()
        key=str(uuid.uuid4());PHASE='real-bff-upload-generates-hdd-source'
        plan=request(session,'POST',endpoint+'uploads/plan',200,headers=api.headers,json={'instanceId':original_instance,'path':'work/original.bin','sizeBytes':len(replacement),'sha256':sha,'expectedVersion':original['version']}).json()
        created=request(session,'POST',endpoint+'uploads',202,headers={**api.headers,'Idempotency-Key':key},json={'planHash':plan['planHash'],'confirmed':True,'idempotencyKey':key}).json()
        initial_id=created['id'];initial_capture=uuid.UUID(initial_id).hex
        save('baseline.json',{'revision':revision,'containerId':fixture['identifier'],'containerName':fixture['folder'].name,'source':str(fixture['folder']),
            'initialUpload':initial_id,'initialCapture':initial_capture,'originalInstance':original_instance,'evidence':str(evidence)})
        request(session,'PUT',endpoint+f'uploads/{initial_id}/content',202,headers={**api.headers,'Idempotency-Key':initial_id,'Content-Type':'application/octet-stream'},data=replacement)
        value=wait_sql(initial_id,'Succeeded');terminal(value,'Succeeded','CONTAINER_UPLOAD_PUBLISHED','Succeeded');initial_record=root_receipt(value['row'])
        assert output.read_bytes()==replacement
        fixture['proofs']['original.bin']=owned.common.fingerprint(output),sha;fixture['private']='.whaledeck-container-files-v1'
        first=fixture['folder']/fixture['private']/(initial_capture+'.upload');assert first.stat().st_ino==before.st_ino and first.read_bytes()==fixture['old']
        fixture['privateProofs'][first.name]=owned.common.fingerprint(first),hashlib.sha256(fixture['old']).hexdigest().upper()
        source_meta=(history/(initial_capture+'.json')).read_bytes();source_data=owned.common.fingerprint(history/(initial_capture+'.data'))
        assert (history/(initial_capture+'.data')).read_bytes()==fixture['old']
        PHASE='actual-owned-restart-before-browser-old-source-restoration'
        owned.common.command(['docker','stop','--time','1',fixture['identifier']]);owned.common.command(['docker','start',fixture['identifier']])
        target=request(session,'GET',endpoint+'target',200).json();assert target['instanceId']!=original_instance
        payload={'revision':revision,'containerId':fixture['identifier'],'containerName':fixture['folder'].name,'initialCapture':initial_capture,'evidence':str(evidence)}
        PHASE='real-desktop-mobile-confirmed-restore-and-normal-cross-page-logout'
        result=subprocess.run(['node',str(Path(__file__).with_suffix('.mjs')),revision],input=json.dumps(payload),text=True,capture_output=True,timeout=420)
        assert len(result.stdout.encode())<=32768 and len(result.stderr.encode())<=32768
        save('browser.stdout',result.stdout);save('browser.stderr',result.stderr)
        if result.returncode:
            for line in result.stderr.splitlines():
                try:
                    v=json.loads(line)
                    if v.get('installedContainerRestoreBrowser')=='failed':print(json.dumps({k:v.get(k) for k in ('installedContainerRestoreBrowser','phase','failureType')}),flush=True)
                except ValueError:pass
            raise AssertionError('RESTORE_BROWSER_NOT_VERIFIED')
        results=evidence/'results.json';assert stat.S_IMODE(trusted(results).st_mode)==0o600
        observed=json.loads(results.read_bytes());assert observed['revision']==revision and observed['fullSsoLogout'] and observed['bodyRequests']==0 and len(observed['restores'])==2
        PHASE='independent-immutable-source-sql-root-inode-and-full-hdd'
        previous_identity=initial_record['publication']['pendingProof'];expected_source=initial_capture
        for index,restored in enumerate(observed['restores']):
            value=snapshot(restored['id'],'restore');terminal(value,'Succeeded','CONTAINER_UPLOAD_PUBLISHED','Succeeded');row=value['row']
            expected=fixture['old'] if index==0 else replacement;displaced=replacement if index==0 else fixture['old']
            assert row['container_id']==fixture['identifier'] and row['instance_id']==target['instanceId'] and row['relative_path']=='work/original.bin'
            assert row['size_bytes']==len(expected) and row['sha256']==hashlib.sha256(expected).hexdigest().upper() and row['expected_version']==restored['originalVersion'] and row['published_version']==restored['publishedVersion']
            if index:assert restored['originalVersion']==observed['restores'][index-1]['publishedVersion']
            source=json.loads(row['history_source_json']);assert source['CaptureId']==expected_source and source['Sha256']==row['sha256'] and source['Path']==row['relative_path'] and source['ContainerId']==row['container_id']
            if index==0:assert source['OriginalInstanceId']==original_instance and source['OriginalInstanceId']!=row['instance_id']
            capture_json=json.loads((history/(expected_source+'.json')).read_bytes())
            actual_hash=hashlib.sha256(b'whaledeck-container-history-v1\ncapture\n'+json.dumps(capture_json,sort_keys=True,separators=(',',':'),ensure_ascii=False,allow_nan=False).encode()).hexdigest().upper()
            assert source['CaptureHash']==actual_hash and (history/(expected_source+'.data')).read_bytes()==expected
            catalog=query("SELECT to_jsonb(v) FROM container_file_versions v WHERE id=:'id'::uuid;",id=str(uuid.UUID(expected_source)))
            assert catalog['container_id']==row['container_id'] and catalog['capture_hash']==source['CaptureHash'] and catalog['sha256']==source['Sha256']
            job=query("SELECT json_build_object('type',job_type) FROM operation_jobs WHERE id=:'id'::uuid;",id=row['id']);assert job['type']=='containers.files.restore'
            record=root_receipt(row);assert record['state']=='Published' and record['originalCapture']['originalIdentity']==previous_identity
            previous_identity=record['publication']['pendingProof'];capture=uuid.UUID(row['id']).hex
            staged=Path('/var/lib/whaledeck-container-uploads')/(capture+'.data');assert stat.S_IMODE(trusted(staged).st_mode)==0o600 and staged.read_bytes()==expected
            retained=fixture['folder']/fixture['private']/(capture+'.upload');assert retained.read_bytes()==displaced and retained.stat().st_nlink==1
            assert (retained.stat().st_uid,retained.stat().st_gid,stat.S_IMODE(retained.stat().st_mode))==(1000,1000,0o640)
            fixture['privateProofs'][retained.name]=owned.common.fingerprint(retained),hashlib.sha256(displaced).hexdigest().upper()
            metadata=json.loads((history/(capture+'.json')).read_bytes());assert metadata['sha256']==hashlib.sha256(displaced).hexdigest().upper() and (history/(capture+'.data')).read_bytes()==displaced
            expected_source=capture
        assert output.read_bytes()==replacement and (history/(initial_capture+'.json')).read_bytes()==source_meta and owned.common.fingerprint(history/(initial_capture+'.data'))==source_data
        assert output.stat().st_nlink==1 and (output.stat().st_uid,output.stat().st_gid,stat.S_IMODE(output.stat().st_mode))==(1000,1000,0o640)
        fixture['proofs']['original.bin']=owned.common.fingerprint(output),sha
        save('verified.json',{'revision':revision,'actualBrowserRestores':2,'fullBytesEach':owned.common.SIZE,'oldCaptureAfterActualRestart':True,
            'sourceCatalogSqlRootHddAndOldInodesVerified':True,'zeroBrowserBodies':True,'normalTwoPageLogout':True,'permanentEvidenceRetained':True})
    logout(base,session);session.close()
    print(json.dumps({'installedContainerRestoreBrowser':'passed','revision':revision,'restores':2,'fullBytesEach':owned.common.SIZE,
        'desktopLightMobileDark':True,'sourceAfterActualRestart':True,'ownedContainerAndSourceCleaned':True,'permanentEvidenceRetained':True,'evidence':str(evidence)}),flush=True)


if __name__=='__main__':
    try:main()
    except BaseException as error:
        import traceback
        print(json.dumps({'installedContainerRestoreBrowser':'failed','phase':PHASE,'failureType':type(error).__name__,
            'locations':[{'file':Path(f.filename).name,'line':f.lineno} for f in traceback.extract_tb(error.__traceback__)],'unverifiedDataPreserved':True}),file=sys.stderr)
        raise SystemExit(1) from None
