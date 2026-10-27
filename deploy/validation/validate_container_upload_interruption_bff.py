#!/usr/bin/env python3
"""Actual partially received HTTP uploads: cancel, logout, transport reset.

Private Cookie/CSRF stay in memory. Before each trigger, attest SQL Receiving,
Root Receiving and a nonempty owned partial inode; no artificial SQL states,
clock/cache edits, production fault injection or file-publication replay.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import socket
import stat
import struct
import sys
import time
from urllib.parse import urlsplit
import uuid

import requests
import validate_container_file_publishing as owned
from validate_container_upload_bff import request, login, logout, snapshot, terminal, root_receipt, wait_sql
from validate_container_upload_recovery_agent import trusted
from validate_databases import Api
from validate_oidc import required_environment

PHASE = 'startup'
STATE = Path('/var/lib/whaledeck-container-uploads')
SIZE = 64*1024**2+7
PREFIX_BYTES = 65536


def receiving(identifier):
    operation = uuid.UUID(identifier).hex; deadline = time.monotonic()+20
    while True:
        value = snapshot(identifier); row = value['row']; partial = STATE/(operation+'.partial')
        assert row['state'] in ('AwaitingUpload','Receiving') and row['root_started_at_utc'] is None and not row['publish_barrier']
        try:
            record = root_receipt(row); info = partial.lstat()
            if row['state'] == 'Receiving' and record['state'] == 'Receiving' and info.st_size > 0:
                assert info.st_size <= PREFIX_BYTES and stat.S_IMODE(trusted(partial).st_mode) == 0o600
                assert record['payloadIdentity'] == [info.st_dev, info.st_ino]
                assert record['request']['sizeBytes'] == SIZE and 'publication' not in record
                return value, info.st_size
        except FileNotFoundError: pass
        assert time.monotonic() < deadline
        time.sleep(0.1)


def open_partial(base, session, api, cid, identifier, body):
    parsed = urlsplit(base); assert parsed.scheme == 'http' and parsed.hostname in ('127.0.0.1','192.168.100.13','192.168.22.19') and parsed.port == 8080
    path = f'/api/v1/containers/{cid}/files/uploads/{identifier}/content'
    cookie = session.prepare_request(requests.Request('PUT',base+path)).headers['Cookie']
    headers = {**api.headers, 'Host':parsed.netloc, 'Cookie':cookie, 'Connection':'close',
               'Content-Type':'application/octet-stream','Content-Length':str(SIZE),'Idempotency-Key':identifier}
    for key,value in headers.items(): assert '\r' not in key+value and '\n' not in key+value
    packet = ('PUT '+path+' HTTP/1.1\r\n'+''.join(key+': '+value+'\r\n' for key,value in headers.items())+'\r\n').encode('ascii')
    wire = socket.create_connection((parsed.hostname,parsed.port),timeout=10)
    try:
        wire.setsockopt(socket.SOL_SOCKET,socket.SO_SNDBUF,32768)
        wire.sendall(packet);wire.sendall(body[:PREFIX_BYTES]);return wire,cookie
    except BaseException:wire.close();raise


def main():
    global PHASE
    assert os.geteuid()==0 and len(sys.argv)==2;os.umask(0o077)
    revision=sys.argv[1];assert re.fullmatch('[a-f0-9]{40}',revision)
    base=os.environ.get('WHALEDECK_URL','http://192.168.100.13:8080').rstrip('/')
    session,profile=login(base,required_environment('VALIDATION_USERNAME'),required_environment('VALIDATION_PASSWORD'))
    api=Api(base,session)
    assert request(session,'GET',base+'/api/v1/system/status',200).json()['build']['sourceRevision']==revision
    evidence=Path(__file__).resolve().parents[1]/'artifacts'/('container-upload-interruption-'+uuid.uuid4().hex);evidence.mkdir(mode=0o700)
    def save(name,value):
        assert name in ('baseline.json','verified.json')
        path=evidence/name;path.write_text(json.dumps(value));path.chmod(0o600)
    body=hashlib.shake_256(b'owned-partial-container-upload-bff-v1').digest(SIZE);sha=hashlib.sha256(body).hexdigest().upper()
    cases=[];intents=[]
    with owned.fixture() as fixture:
        cid=fixture['identifier'];endpoint=base+f'/api/v1/containers/{cid}/files/'
        target=request(session,'GET',endpoint+'target',200).json();instance=target['instanceId']
        baseline={'revision':revision,'containerId':cid,'instanceId':instance,'source':str(fixture['folder']),'declaredBytes':SIZE,'intents':intents}
        save('baseline.json',baseline)
        for scenario in ('cancel','logout','disconnect'):
            PHASE=scenario+'-register-and-partial-root-reception'
            path='work/partial-'+scenario+'.bin';key=str(uuid.uuid4())
            planned=request(session,'POST',endpoint+'uploads/plan',200,headers=api.headers,
                json={'instanceId':instance,'path':path,'sizeBytes':SIZE,'sha256':sha,'expectedVersion':'ABSENT'}).json()
            intent=request(session,'POST',endpoint+'uploads',202,headers={**api.headers,'Idempotency-Key':key},
                json={'planHash':planned['planHash'],'confirmed':True,'idempotencyKey':key}).json()
            identifier=intent['id'];intents.append(identifier);save('baseline.json',baseline)
            wire,old_cookie=open_partial(base,session,api,cid,identifier,body)
            try:
                observed,partial_bytes=receiving(identifier)
                PHASE=scenario+'-trigger-after-positive-partial-proof'
                if scenario=='cancel':
                    request(session,'POST',endpoint+f'uploads/{identifier}/cancel',202,headers={**api.headers,'Idempotency-Key':identifier})
                elif scenario=='logout':
                    logout(base,session)
                    replay=requests.Session();replay.trust_env=False
                    request(replay,'GET',endpoint+f'uploads/{identifier}',401,headers={'Cookie':old_cookie});replay.close()
                    session.close()
                    session,again=login(base,required_environment('VALIDATION_USERNAME'),required_environment('VALIDATION_PASSWORD'))
                    assert again['subject']==profile['subject'] and again['isAdministrator'];api=Api(base,session)
                else:
                    wire.setsockopt(socket.SOL_SOCKET,socket.SO_LINGER,struct.pack('ii',1,0));wire.close()
                # An incomplete HTTP/1 body can remain at nginx until its idle
                # budget ends. Prove backend termination while the client has
                # not closed its connection; a response timeout is no proof.
                expected='Failed' if scenario=='disconnect' else 'Canceled'
                result=wait_sql(identifier,expected)
                if scenario!='disconnect':
                    before=root_receipt(result['row']);assert before['state']=='Discarded'
                    try:wire.sendall(body[PREFIX_BYTES:PREFIX_BYTES+32768])
                    except (BrokenPipeError,ConnectionResetError):pass
                    time.sleep(0.2)
                    assert root_receipt(result['row'])==before
                    assert not (STATE/(uuid.UUID(identifier).hex+'.partial')).exists()
                    wire.setsockopt(socket.SOL_SOCKET,socket.SO_LINGER,struct.pack('ii',1,0))
            finally:wire.close()
            PHASE=scenario+'-worker-terminal-and-independent-cleanup-proof'
            expected='Failed' if scenario=='disconnect' else 'Canceled'
            code='CONTAINER_UPLOAD_INTERRUPTED' if scenario=='disconnect' else 'CONTAINER_UPLOAD_CANCELED'
            result=wait_sql(identifier,expected);terminal(result,expected,code,expected)
            row=result['row'];assert row['root_started_at_utc'] is None and row['receive_token'] is not None
            assert row['published_version'] is None and not row['previous_retained']
            root=root_receipt(row);assert root['state']=='Discarded' and 'publication' not in root
            operation=uuid.UUID(identifier).hex
            assert not (STATE/(operation+'.partial')).exists() and not (STATE/(operation+'.data')).exists()
            assert not (fixture['folder']/('partial-'+scenario+'.bin')).exists()
            # No implicit byte retry/publication after observing the terminal job.
            events=owned.common.command(['docker','exec','-u','postgres','database-platform-postgres','psql','-X','-At','-d','whaledeck','-c',
                "SELECT count(*) FROM operation_job_events WHERE job_id='"+identifier+"'::uuid AND message_code='CONTAINER_UPLOAD_PUBLISHING';"])
            assert events.strip()==b'0'
            cases.append({'scenario':scenario,'partialBytes':partial_bytes,'terminalBeforeClientClose':scenario!='disconnect',
                          'lateBytesCannotResumeRoot':scenario!='disconnect','state':expected,'rootState':root['state']})
        save('verified.json',{'revision':revision,'cases':cases,'noTargetPublication':True,'partialPayloadsRemoved':True,
            'independentNewLoginAfterLogout':True,'durableJobsAuditAndRootReceiptsRetained':True})
    PHASE='final-full-sso-logout'
    logout(base,session);session.close()
    print(json.dumps({'containerUploadInterruptionBff':'passed','revision':revision,'declaredBytes':SIZE,'cases':cases,
        'originalSourceAndCoreUnchanged':True,'ownedContainerAndSourceCleaned':True,'browserOrPublishingFaults':False}),flush=True)


if __name__=='__main__':
    try:main()
    except BaseException as error:
        import traceback
        print(json.dumps({'containerUploadInterruptionBff':'failed','phase':PHASE,'failureType':type(error).__name__,
            'locations':[{'file':Path(frame.filename).name,'line':frame.lineno} for frame in traceback.extract_tb(error.__traceback__)],
            'unverifiedDataPreserved':True}),file=sys.stderr)
        raise SystemExit(1) from None
