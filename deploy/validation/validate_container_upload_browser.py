#!/usr/bin/env python3
"""Real installed browser uploads; independent SQL/Root/full filesystem observer.

Only the owned fixture source is cleaned after exact proofs. Durable jobs,
audits, Root receipts/payloads and HDD history remain available as evidence.
Run through run_with_ephemeral_admin; no storageState/token/trace exports.
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
from validate_container_upload_bff import snapshot, terminal, root_receipt
from validate_container_upload_recovery_agent import trusted

PHASE = 'startup'


def main():
    global PHASE
    assert os.geteuid() == 0 and len(sys.argv) == 2; os.umask(0o077)
    revision = sys.argv[1]; assert re.fullmatch('[a-f0-9]{40}', revision)
    repository = Path(__file__).resolve().parents[2]
    evidence = repository/'deploy/artifacts'/('container-upload-browser-'+uuid.uuid4().hex); evidence.mkdir(mode=0o700)
    body = hashlib.shake_256(b'owned-browser-container-upload-v1').digest(owned.common.SIZE)
    digest = hashlib.sha256(body).hexdigest().upper()
    backup_module = load(Path('/usr/local/libexec/whaledeck-file-backups.py'), 'browser_container_hdd')
    files_module = load(Path('/usr/local/libexec/whaledeck-files.py'), 'browser_container_files')
    storage = backup_module.FileBackups(files_module)
    with storage.target(create_namespace=False): pass
    configuration = storage.configuration(); history = Path(configuration['mountPoint'])/configuration['targetRelative']/'container-files-v1'
    with owned.fixture() as fixture:
        before = (fixture['folder']/'original.bin').stat()
        baseline = {'revision':revision,'containerId':fixture['identifier'],'containerName':fixture['folder'].name,
                    'source':str(fixture['folder']),'bytes':len(body),'sha256':digest,'evidence':str(evidence)}
        path = evidence/'baseline.json'; path.write_text(json.dumps(baseline));path.chmod(0o600)
        PHASE = 'visible-authentik-and-browser-controls'
        completed = subprocess.run(['node', str(Path(__file__).with_suffix('.mjs')), revision], input=json.dumps(baseline),
            text=True, capture_output=True, timeout=240, check=False)
        assert len(completed.stdout.encode()) <= 32768 and len(completed.stderr.encode()) <= 32768
        for name,value in (('browser.stdout',completed.stdout),('browser.stderr',completed.stderr)):
            path = evidence/name;path.write_text(value);path.chmod(0o600)
        if completed.returncode:
            # Only sanitized child stages, never Playwright messages or form values.
            for line in completed.stderr.splitlines():
                try:
                    detail = json.loads(line)
                    if detail.get('installedContainerUploadBrowser') == 'failed':
                        print(json.dumps({key:detail.get(key) for key in ('installedContainerUploadBrowser','phase','failureType')}),flush=True)
                except ValueError: pass
            raise AssertionError('BROWSER_UPLOAD_NOT_VERIFIED')
        PHASE = 'independent-sql-full-bytes-inode-and-hdd-proof'
        result_path = evidence/'results.json';assert stat.S_IMODE(trusted(result_path).st_mode) == 0o600
        results = json.loads(result_path.read_bytes())
        assert results['revision'] == revision and results['fullSsoLogout'] and results['bodyRequests'] == 2
        assert len(results['uploads']) == 2
        for index,result in enumerate(results['uploads']):
            value = snapshot(result['id']);terminal(value,'Succeeded','CONTAINER_UPLOAD_PUBLISHED','Succeeded');row=value['row']
            leaf_name = 'original.bin' if index == 0 else '移动端新建.bin'
            assert row['container_id'] == fixture['identifier'] and row['relative_path'] == 'work/'+leaf_name
            assert row['size_bytes'] == len(body) and row['sha256'] == digest and row['published_version'] == result['publishedVersion']
            output = fixture['folder']/leaf_name
            assert output.read_bytes() == body and output.lstat().st_nlink == 1 and (output.stat().st_uid,output.stat().st_gid) == (1000,1000)
            assert stat.S_IMODE(output.stat().st_mode) == (0o640 if index == 0 else 0o600)
            fixture['proofs'][leaf_name] = owned.common.fingerprint(output),digest
            record=root_receipt(row);assert record['state']=='Published'
            operation=uuid.UUID(row['id']).hex;payload=Path('/var/lib/whaledeck-container-uploads')/(operation+'.data')
            assert stat.S_IMODE(trusted(payload).st_mode)==0o600 and payload.read_bytes()==body
            if index == 0:
                assert row['previous_retained']
                retained=fixture['folder']/'.whaledeck-container-files-v1'/(operation+'.upload')
                assert retained.read_bytes()==fixture['old'] and retained.stat().st_ino==before.st_ino
                fixture['private']='.whaledeck-container-files-v1'
                fixture['privateProofs'][retained.name]=owned.common.fingerprint(retained),hashlib.sha256(fixture['old']).hexdigest().upper()
                metadata=history/(operation+'.json');archive=history/(operation+'.data')
                for path in (metadata,archive):assert stat.S_IMODE(trusted(path).st_mode)==0o600
                capture=json.loads(metadata.read_bytes())
                assert archive.read_bytes()==fixture['old'] and capture['operationId']==operation and capture['containerId']==fixture['identifier']
                assert capture['sha256']==hashlib.sha256(fixture['old']).hexdigest().upper() and capture['targetProof']==row['target_proof']
            else:
                assert not row['previous_retained'] and not (history/(operation+'.json')).exists()
        path=evidence/'verified.json';path.write_text(json.dumps({'revision':revision,'actualBrowserAndIndependentSqlRootFullBytesHdd':True,
            'desktopLightMobileDark':True,'normalTwoPageLogout':True,'durableHistoryRetained':True}));path.chmod(0o600)
    print(json.dumps({'installedContainerUploadBrowser':'passed','revision':revision,'bytesPerUpload':len(body),'uploads':2,
        'desktopLightMobileDark':True,'singleBodyAfterExplicitConfirmation':True,'independentSqlRootFullBytesHdd':True,
        'normalTwoPageLogout':True,'ownedContainerAndSourceCleaned':True,'durableHistoryRetained':True,'evidence':str(evidence)}),flush=True)


if __name__ == '__main__':
    try: main()
    except BaseException as error:
        import traceback
        print(json.dumps({'installedContainerUploadBrowser':'failed','phase':PHASE,'failureType':type(error).__name__,
            'locations':[{'file':Path(frame.filename).name,'line':frame.lineno} for frame in traceback.extract_tb(error.__traceback__)],
            'unverifiedDataPreserved':True}),file=sys.stderr)
        raise SystemExit(1) from None
