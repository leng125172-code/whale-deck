#!/usr/bin/env python3
"""Installed signed UDS history/source RPC against one real human BFF capture.

Only an owned ordinary container is changed/restarted. Independent SQL, Root,
full target/displaced inode and HDD bytes remain evidence. No raw key/secret,
SQL/cache mutation, deployment, restore/publication replay or broad cleanup.
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
from validate_container_upload_bff import login,logout,request,snapshot,terminal,root_receipt,wait_sql
from validate_container_upload_recovery_agent import trusted
from validate_databases import Api
from validate_oidc import required_environment

PHASE='startup'


def main():
    global PHASE
    assert os.geteuid()==0 and len(sys.argv)==3;os.umask(0o077)
    revision=sys.argv[1];assert re.fullmatch('[a-f0-9]{40}',revision)
    probe=Path(sys.argv[2]);repository=Path(__file__).resolve().parents[2]
    assert probe.parent.parent==repository/'deploy/artifacts' and re.fullmatch(r'terminal-probe\.[A-Za-z0-9]+',probe.parent.name) and probe.name=='TerminalProbe'
    trusted(probe);trusted(probe.parent,True)
    subprocess.run(['sha256sum','--quiet','-c','SHA256SUMS'],cwd=probe.parent,capture_output=True,check=True,timeout=30)
    installed=json.loads(owned.common.command(['/opt/whaledeck-agent/WhaleDeck.Agent','--build-info']));assert installed['sourceRevision']==revision
    base=os.environ.get('WHALEDECK_URL','http://192.168.100.13:8080').rstrip('/')
    session,profile=login(base,required_environment('VALIDATION_USERNAME'),required_environment('VALIDATION_PASSWORD'));api=Api(base,session)
    assert request(session,'GET',base+'/api/v1/system/status',200).json()['build']['sourceRevision']==revision
    evidence=repository/'deploy/artifacts'/('container-history-agent-'+uuid.uuid4().hex);evidence.mkdir(mode=0o700)
    def save(name,value):
        assert re.fullmatch('[a-z0-9.-]+',name)
        path=evidence/name;path.write_text(value if isinstance(value,str) else json.dumps(value));path.chmod(0o600)
    backups=load(Path('/usr/local/libexec/whaledeck-file-backups.py'),'history_agent_hdd')
    files=load(Path('/usr/local/libexec/whaledeck-files.py'),'history_agent_files')
    storage=backups.FileBackups(files);configuration=storage.configuration()
    history=Path(configuration['mountPoint'])/configuration['targetRelative']/'container-files-v1'
    with owned.fixture() as fixture:
        endpoint=base+f'/api/v1/containers/{fixture["identifier"]}/files/'
        target=request(session,'GET',endpoint+'target',200).json();original_instance=target['instanceId']
        original=request(session,'GET',endpoint+'entry',200,params={'instanceId':original_instance,'path':'work/original.bin'}).json()
        original_identity=(fixture['folder']/'original.bin').stat();old_sha=hashlib.sha256(fixture['old']).hexdigest().upper()
        body=hashlib.shake_256(b'owned-native-history-source-new-content-v1').digest(owned.common.SIZE);sha=hashlib.sha256(body).hexdigest().upper()
        key=str(uuid.uuid4())
        PHASE='real-bff-upload-and-independent-old-capture'
        planned=request(session,'POST',endpoint+'uploads/plan',200,headers=api.headers,json={'instanceId':original_instance,
            'path':'work/original.bin','sizeBytes':len(body),'sha256':sha,'expectedVersion':original['version']}).json()
        operation=request(session,'POST',endpoint+'uploads',202,headers={**api.headers,'Idempotency-Key':key},
            json={'planHash':planned['planHash'],'confirmed':True,'idempotencyKey':key}).json()
        identifier=operation['id'];capture=uuid.UUID(identifier).hex
        save('baseline.json',{'revision':revision,'containerId':fixture['identifier'],'instanceId':original_instance,
            'source':str(fixture['folder']),'intent':identifier,'captureId':capture,'originalSha256':old_sha})
        request(session,'PUT',endpoint+f'uploads/{identifier}/content',202,
            headers={**api.headers,'Idempotency-Key':identifier,'Content-Type':'application/octet-stream'},data=body)
        value=wait_sql(identifier,'Succeeded');terminal(value,'Succeeded','CONTAINER_UPLOAD_PUBLISHED','Succeeded');row=value['row']
        assert row['previous_retained'] and row['sha256']==sha and root_receipt(row)['state']=='Published'
        output=fixture['folder']/'original.bin';assert output.read_bytes()==body
        fixture['proofs']['original.bin']=owned.common.fingerprint(output),sha
        fixture['private']='.whaledeck-container-files-v1'
        retained=fixture['folder']/fixture['private']/(capture+'.upload')
        assert retained.stat().st_ino==original_identity.st_ino and retained.read_bytes()==fixture['old']
        fixture['privateProofs'][retained.name]=owned.common.fingerprint(retained),old_sha
        meta_path=history/(capture+'.json');data_path=history/(capture+'.data')
        before_meta=meta_path.read_bytes();before_data=owned.common.fingerprint(data_path)
        assert stat.S_IMODE(trusted(meta_path).st_mode)==0o600 and stat.S_IMODE(trusted(data_path).st_mode)==0o600
        meta=json.loads(before_meta);assert meta['state']=='Verified' and meta['sha256']==old_sha and data_path.read_bytes()==fixture['old']
        assert meta['containerId']==fixture['identifier'] and meta['instanceId']==original_instance and meta['sourceVersion']==original['version']
        core=owned.common.command(['docker','inspect','--format','{{.Id}}','whaledeck-worker']).decode().strip()
        runs=[]
        for stage in ('current','restarted'):
            PHASE=stage+'-installed-independent-signed-history-rpc'
            if stage=='restarted':
                owned.common.command(['docker','stop','--time','1',fixture['identifier']]);owned.common.command(['docker','start',fixture['identifier']])
                target=request(session,'GET',endpoint+'target',200).json();assert target['instanceId']!=original_instance
            parameters={'revision':revision,'containerId':fixture['identifier'],'instanceId':target['instanceId'],'originalInstanceId':original_instance,
                'path':'work/original.bin','captureId':capture,'sourceVersion':original['version'],'sha256':old_sha,'sizeBytes':owned.common.SIZE,'coreId':core}
            completed=subprocess.run([str(probe),'container-history'],input=json.dumps(parameters),text=True,capture_output=True,timeout=180)
            assert len(completed.stdout.encode())<=32768 and len(completed.stderr.encode())<=32768
            save(stage+'.stdout',completed.stdout);save(stage+'.stderr',completed.stderr)
            if completed.returncode:
                for line in completed.stderr.splitlines():
                    try:
                        detail=json.loads(line)
                        if detail.get('terminalProbe')=='failed':print(json.dumps({k:detail.get(k) for k in ('terminalProbe','phase','rpcStatus','fileCode')}),flush=True)
                    except ValueError:pass
                raise AssertionError('INSTALLED_HISTORY_RPC_NOT_VERIFIED')
            runs.append(stage)
            assert meta_path.read_bytes()==before_meta and owned.common.fingerprint(data_path)==before_data and data_path.read_bytes()==fixture['old']
            assert output.read_bytes()==body and retained.read_bytes()==fixture['old'] and retained.stat().st_ino==original_identity.st_ino
        save('verified.json',{'revision':revision,'runs':runs,'installedSignedRpcAndActualRootFullSource':True,
            'realBffSqlHddCapture':True,'fullOldBytes':owned.common.SIZE,'oldCaptureAfterActualRestart':True,
            'targetDisplacedInodeAndHddUnchanged':True,'permanentSqlRootHddEvidenceRetained':True})
    logout(base,session);session.close()
    print(json.dumps({'installedContainerHistory':'passed','revision':revision,'fullBytes':owned.common.SIZE,
        'signedRpcAndActualRoot':True,'afterActualOwnedRestart':True,'ownedContainerAndSourceCleaned':True,
        'permanentEvidenceRetained':True,'evidence':str(evidence)}),flush=True)


if __name__=='__main__':
    try:main()
    except BaseException as error:
        import traceback
        print(json.dumps({'installedContainerHistory':'failed','phase':PHASE,'failureType':type(error).__name__,
            'locations':[{'file':Path(frame.filename).name,'line':frame.lineno} for frame in traceback.extract_tb(error.__traceback__)]}),file=sys.stderr)
        raise SystemExit(1) from None
