#!/usr/bin/env python3
"""Actual installed editor, with independent SQL/Root/inode/HDD verification.

Run after the editor is deployed, via run_with_ephemeral_admin. No mocked HTTP,
Cookie export, SQL/cache edits or product-data mutation. Preserve uncertain
fixture data; retain permanent jobs, audit, Root payloads and HDD history.
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
    evidence = Path(__file__).resolve().parents[1]/'artifacts'/('container-text-browser-'+uuid.uuid4().hex)
    evidence.mkdir(mode=0o700)
    def save(name, value):
        assert name in ('baseline.json','verified.json','browser.stdout','browser.stderr')
        path = evidence/name; path.write_text(value if isinstance(value,str) else json.dumps(value)); path.chmod(0o600)
    initial = '\ufeffWhale Deck O0Il\r\n原始配置=true\r\n'.encode('utf8')
    texts = ['桌面编辑 O0Il\n保留 BOM 与 CRLF\n', '移动端编辑 O0Il\n再次覆盖并留存旧版本\n']
    outputs = [('\ufeff'+text.replace('\n','\r\n')).encode('utf8') for text in texts]
    backup = load(Path('/usr/local/libexec/whaledeck-file-backups.py'), 'text_browser_hdd')
    files = load(Path('/usr/local/libexec/whaledeck-files.py'), 'text_browser_files')
    storage = backup.FileBackups(files)
    with storage.target(create_namespace=False): pass
    configuration = storage.configuration()
    history = Path(configuration['mountPoint'])/configuration['targetRelative']/'container-files-v1'
    with owned.fixture() as fixture:
        leaf = '编辑示例.txt'; source = fixture['folder']/leaf
        source.write_bytes(initial); os.chown(source,1000,1000); source.chmod(0o640)
        before = source.stat()
        fixture['proofs'][leaf] = owned.common.fingerprint(source), hashlib.sha256(initial).hexdigest().upper()
        baseline = {'revision':revision,'containerId':fixture['identifier'],'containerName':fixture['folder'].name,
            'source':str(fixture['folder']),'leaf':leaf,'initialText':initial.decode('utf8')[1:].replace('\r\n','\n'),
            'texts':texts,'evidence':str(evidence)}
        save('baseline.json',baseline)
        PHASE = 'actual-visible-editor-and-normal-authentik-login'
        result = subprocess.run(['node',str(Path(__file__).with_suffix('.mjs')),revision],input=json.dumps(baseline),
            text=True,capture_output=True,timeout=300,check=False)
        assert len(result.stdout.encode()) <= 32768 and len(result.stderr.encode()) <= 32768
        save('browser.stdout',result.stdout); save('browser.stderr',result.stderr)
        if result.returncode:
            for line in result.stderr.splitlines():
                try:
                    item=json.loads(line)
                    if item.get('installedContainerTextBrowser')=='failed':
                        print(json.dumps({key:item.get(key) for key in ('installedContainerTextBrowser','phase','failureType')}),flush=True)
                except ValueError: pass
            raise AssertionError('BROWSER_TEXT_NOT_VERIFIED')
        PHASE = 'independent-full-bytes-version-sql-root-and-hdd'
        result_path=evidence/'results.json'; assert stat.S_IMODE(trusted(result_path).st_mode)==0o600
        observed=json.loads(result_path.read_bytes())
        assert observed['revision']==revision and observed['fullSsoLogout'] and observed['bodyRequests']==2
        assert len(observed['edits'])==2
        previous_identity=None
        fixture['private']='.whaledeck-container-files-v1'
        for index,edit in enumerate(observed['edits']):
            value=snapshot(edit['id']); terminal(value,'Succeeded','CONTAINER_UPLOAD_PUBLISHED','Succeeded'); row=value['row']
            body=outputs[index]; old=initial if index==0 else outputs[index-1]
            assert row['container_id']==fixture['identifier'] and row['relative_path']=='work/'+leaf
            assert row['expected_version']==edit['originalVersion'] and row['published_version']==edit['publishedVersion']
            assert row['previous_retained'] and row['size_bytes']==len(body) and row['sha256']==hashlib.sha256(body).hexdigest().upper()
            if index: assert edit['originalVersion']==observed['edits'][index-1]['publishedVersion']
            record=root_receipt(row); assert record['state']=='Published'
            if previous_identity: assert record['originalCapture']['originalIdentity']==previous_identity
            previous_identity=record['publication']['pendingProof']
            operation=uuid.UUID(row['id']).hex
            payload=Path('/var/lib/whaledeck-container-uploads')/(operation+'.data')
            assert stat.S_IMODE(trusted(payload).st_mode)==0o600 and payload.read_bytes()==body
            retained=fixture['folder']/fixture['private']/(operation+'.upload')
            assert retained.read_bytes()==old and retained.stat().st_nlink==1
            if index==0: assert retained.stat().st_ino==before.st_ino
            assert (retained.stat().st_uid,retained.stat().st_gid,stat.S_IMODE(retained.stat().st_mode))==(1000,1000,0o640)
            fixture['privateProofs'][retained.name]=owned.common.fingerprint(retained),hashlib.sha256(old).hexdigest().upper()
            metadata=history/(operation+'.json'); archive=history/(operation+'.data')
            assert stat.S_IMODE(trusted(metadata).st_mode)==0o600 and stat.S_IMODE(trusted(archive).st_mode)==0o600
            capture=json.loads(metadata.read_bytes())
            assert archive.read_bytes()==old and capture['operationId']==operation and capture['containerId']==fixture['identifier']
            assert capture['sha256']==hashlib.sha256(old).hexdigest().upper() and capture['targetProof']==row['target_proof']
        assert source.read_bytes()==outputs[-1] and source.stat().st_nlink==1
        assert (source.stat().st_uid,source.stat().st_gid,stat.S_IMODE(source.stat().st_mode))==(1000,1000,0o640)
        fixture['proofs'][leaf]=owned.common.fingerprint(source),hashlib.sha256(outputs[-1]).hexdigest().upper()
        save('verified.json',{'revision':revision,'actualEditorAndIndependentSqlRootFullBytesHdd':True,
            'desktopLightMobileDark':True,'bothVersionsAndOldInodesRetained':True,'normalTwoPageLogout':True})
    print(json.dumps({'installedContainerTextBrowser':'passed','revision':revision,'edits':2,
        'desktopLightMobileDark':True,'cancelHasNoRegistrationOrBody':True,'singleBodyAfterConfirmation':True,
        'independentSqlRootFullBytesHdd':True,'normalTwoPageLogout':True,'ownedContainerAndSourceCleaned':True,
        'durableHistoryRetained':True,'evidence':str(evidence)}),flush=True)


if __name__=='__main__':
    try: main()
    except BaseException as error:
        import traceback
        print(json.dumps({'installedContainerTextBrowser':'failed','phase':PHASE,'failureType':type(error).__name__,
            'locations':[{'file':Path(frame.filename).name,'line':frame.lineno} for frame in traceback.extract_tb(error.__traceback__)],
            'unverifiedDataPreserved':True}),file=sys.stderr)
        raise SystemExit(1) from None
