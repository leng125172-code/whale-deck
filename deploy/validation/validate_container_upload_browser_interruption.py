#!/usr/bin/env python3
"""Real Chrome controls/transport + independent partial Root and Worker proof.

The browser uses native CDP upload throttling, not HTTP interception. A private
observer handshake only permits the UI action after actual nonempty Root
reception. Credentials remain in memory, uncertain test data is preserved.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import sys
import time
import uuid

import validate_container_file_publishing as owned
from validate_container_upload_bff import snapshot, terminal, root_receipt
from validate_container_upload_recovery_agent import trusted

PHASE='startup'
SIZE=64*1024**2+7
STATE=Path('/var/lib/whaledeck-container-uploads')


def main():
    global PHASE
    assert os.geteuid()==0 and len(sys.argv)==2;os.umask(0o077)
    revision=sys.argv[1];assert re.fullmatch('[a-f0-9]{40}',revision)
    evidence=Path(__file__).resolve().parents[1]/'artifacts'/('container-browser-interruption-'+uuid.uuid4().hex);evidence.mkdir(mode=0o700)
    def save(name,value):
        assert re.fullmatch('[a-z0-9.-]+',name)
        path=evidence/name;temporary=evidence/(name+'.tmp');temporary.write_text(json.dumps(value));temporary.chmod(0o600);temporary.replace(path)
    body=hashlib.shake_256(b'owned-real-browser-partial-upload-v1').digest(SIZE)
    sha=hashlib.sha256(body).hexdigest().upper();cases=[]
    for scenario in ('cancel','logout'):
        path=evidence/('browser-'+scenario+'.bin');path.write_bytes(body);path.chmod(0o600)
    with owned.fixture() as fixture:
        baseline={'revision':revision,'containerId':fixture['identifier'],'containerName':fixture['folder'].name,
            'source':str(fixture['folder']),'sizeBytes':SIZE,'sha256':sha,'evidence':str(evidence),
            'rootHelperSha256':hashlib.sha256(Path('/usr/local/libexec/whaledeck-privileged').read_bytes()).hexdigest()}
        save('baseline.json',baseline)
        child=subprocess.Popen(['node',str(Path(__file__).with_suffix('.mjs')),revision],stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True)
        child.stdin.write(json.dumps(baseline));child.stdin.close();child.stdin=None
        try:
            for scenario in ('cancel','logout'):
                PHASE=scenario+'-await-actual-browser-registration-and-partial-root'
                deadline=time.monotonic()+100
                while True:
                    assert child.poll() is None and time.monotonic()<deadline
                    try:
                        progress=json.loads((evidence/'progress.json').read_bytes())
                        if progress['scenario']==scenario and progress['phase']=='await-root-partial':break
                    except FileNotFoundError:pass
                    time.sleep(0.1)
                identifier=progress['id'];assert str(uuid.UUID(identifier))==identifier
                operation=uuid.UUID(identifier).hex;partial=STATE/(operation+'.partial')
                while True:
                    assert child.poll() is None and time.monotonic()<deadline
                    value=snapshot(identifier);row=value['row']
                    assert row['container_id']==fixture['identifier'] and row['relative_path']=='work/browser-'+scenario+'.bin'
                    assert row['size_bytes']==SIZE and row['sha256']==sha and row['root_started_at_utc'] is None and not row['publish_barrier']
                    assert row['state'] in ('AwaitingUpload','Receiving')
                    try:
                        root=root_receipt(row);info=partial.lstat()
                        if row['state']=='Receiving' and root['state']=='Receiving' and 0<info.st_size<SIZE:
                            assert stat.S_IMODE(trusted(partial).st_mode)==0o600 and root['payloadIdentity']==[info.st_dev,info.st_ino]
                            with partial.open('rb') as stream:prefix=stream.read(min(65536,info.st_size))
                            assert prefix==body[:len(prefix)] and prefix
                            break
                    except FileNotFoundError:pass
                    time.sleep(0.1)
                partial_bytes=info.st_size
                save(scenario+'-partial-attested.json',{'id':identifier,'partialBytes':partial_bytes,'actualSqlRootAndPrefix':True})
                PHASE=scenario+'-independent-backend-terminal-after-ui-action'
                deadline=time.monotonic()+100
                while True:
                    assert time.monotonic()<deadline
                    value=snapshot(identifier);state=value['row']['state']
                    if state in ('Failed','Canceled'):break
                    assert state not in ('Succeeded','Superseded','Unknown')
                    time.sleep(0.2)
                code='CONTAINER_UPLOAD_INTERRUPTED' if state=='Failed' else 'CONTAINER_UPLOAD_CANCELED'
                terminal(value,state,code,state);row=value['row'];root=root_receipt(row)
                assert root['state']=='Discarded' and 'publication' not in root
                assert row['root_started_at_utc'] is None and row['published_version'] is None
                assert not partial.exists() and not (STATE/(operation+'.data')).exists()
                assert not (fixture['folder']/('browser-'+scenario+'.bin')).exists()
                cases.append({'scenario':scenario,'id':identifier,'partialBytes':partial_bytes,'state':state,'resultCode':code})
                save(scenario+'-backend-verified.json',{'id':identifier,'noPublicationAndNoPartialPayload':True})
            PHASE='actual-cdp-terminals-and-full-two-page-sso-logout'
            stdout,stderr=child.communicate(timeout=60)
            assert len(stdout.encode())<=32768 and len(stderr.encode())<=32768
            for name,value in (('browser.stdout',stdout),('browser.stderr',stderr)):
                path=evidence/name;path.write_text(value);path.chmod(0o600)
            assert child.returncode==0
            results_path=evidence/'results.json';assert stat.S_IMODE(trusted(results_path).st_mode)==0o600
            results=json.loads(results_path.read_bytes());assert results['revision']==revision and results['fullSsoLogout']
            assert len(results['cases'])==2 and results['bodyRequests']==2
            for local,observed in zip(cases,results['cases'],strict=True):
                assert observed['id']==local['id'] and observed['scenario']==local['scenario']
                network=observed['network']
                assert network['seen'] and network['terminal']=='failed' and network['canceled'] and network['networkError']=='net::ERR_ABORTED'
            save('verified.json',{'revision':revision,'cases':cases,'realBrowserControlsAndCdpCanceled':True,
                'independentSqlRootPartialAndNoPublication':True,'normalTwoPageLogout':True,'durableHistoryRetained':True})
            for scenario in ('cancel','logout'):
                path=evidence/('browser-'+scenario+'.bin')
                assert stat.S_IMODE(trusted(path).st_mode)==0o600 and path.read_bytes()==body
                path.unlink()
        finally:
            if child.poll() is None:child.terminate()
            try:stdout,stderr=child.communicate(timeout=10)
            except subprocess.TimeoutExpired:child.kill();stdout,stderr=child.communicate(timeout=10)
            for name,value in (('browser.stdout',stdout),('browser.stderr',stderr)):
                if not (evidence/name).exists():
                    path=evidence/name;path.write_text(value[:32768]);path.chmod(0o600)
    print(json.dumps({'browserUploadInterruption':'passed','revision':revision,'declaredBytes':SIZE,
        'actualPartialRootBeforeActions':True,'actualCdpCanceledAfterControls':True,'independentWorkerRootTerminal':True,
        'normalTwoPageLogout':True,'ownedContainerAndSourceCleaned':True,'durableHistoryRetained':True,'evidence':str(evidence)}),flush=True)


if __name__=='__main__':
    try:main()
    except BaseException as error:
        import traceback
        print(json.dumps({'browserUploadInterruption':'failed','phase':PHASE,'failureType':type(error).__name__,
            'locations':[{'file':Path(frame.filename).name,'line':frame.lineno} for frame in traceback.extract_tb(error.__traceback__)],
            'unverifiedDataPreserved':True}),file=sys.stderr)
        raise SystemExit(1) from None
