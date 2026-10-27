#!/usr/bin/env python3
"""Installed human Cookie -> SQL/Worker -> UDS/Root/HDD acceptance.

Only an owned, non-root, no-network container is changed. No SQL/cache edits,
production data writes, generic retries, or automatic rollback. Immutable jobs,
audits and Root/HDD history are retained; uncertain fixture content is preserved.
This CLI does not claim browser acceptance or physical separate-disk recovery.
"""
import contextlib
import hashlib
import io
import json
import os
from pathlib import Path
import re
import secrets
import stat
import sys
import time
from urllib.parse import urljoin, urlparse
import uuid

import requests

import validate_container_file_publishing as owned
from validate_container_file_staging import load
from validate_container_upload_recovery_agent import digest, trusted
from validate_databases import Api
from validate_metric_continuity import query
from validate_oidc import authenticate, follow_redirects, required_environment
from run_with_ephemeral_admin import AUTHENTIK_API, container_environment, require

PHASE = 'startup'
LAST_HTTP = None
STATE = Path('/var/lib/whaledeck-container-uploads')


def request(session, method, url, expected, **options):
    global LAST_HTTP
    response = session.request(method, url, allow_redirects=False, timeout=45, **options)
    LAST_HTTP = {'method': method, 'expected': expected, 'actual': response.status_code}
    assert response.status_code == expected
    return response


def login(base, username, password, admin=True):
    with contextlib.redirect_stdout(io.StringIO()):
        return authenticate(base, username, password, require_administrator=admin)


def logout(base, session):
    headers = Api(base, session).headers
    response = request(session, 'POST', base+'/api/v1/auth/logout', 302, headers=headers)
    response = follow_redirects(session, response)
    for _ in range(8):
        if '/if/flow/' not in response.url: break
        parsed = urlparse(response.url); slug = parsed.path.rstrip('/').split('/')[-1]
        executor = f'{parsed.scheme}://{parsed.netloc}/api/v3/flows/executor/{slug}/'
        stage = session.get(executor, params={'query': parsed.query}, timeout=20); assert stage.status_code == 200
        challenge = stage.json(); assert challenge.get('component') == 'xak-flow-redirect'
        response = follow_redirects(session, session.get(urljoin(response.url, challenge['to']), allow_redirects=False, timeout=20))
    assert urlparse(response.url).path == '/login'
    assert session.get(base+'/api/v1/auth/me', timeout=20).status_code == 401
    identity = f'{urlparse(base).scheme}://{urlparse(base).hostname}:8081/api/v3/core/users/me/'
    assert session.get(identity, timeout=20).status_code in (401, 403)


@contextlib.contextmanager
def temporary_user(administrator):
    values = container_environment(); session = requests.Session(); session.trust_env = False
    session.headers.update({'Authorization': 'Bearer '+require(values, 'Authentication__Authentik__ApiToken')})
    username = 'whaledeck-upload-qa-'+uuid.uuid4().hex[:12]; password = 'Wd!'+secrets.token_urlsafe(24)+'9aA'
    identifier = None
    try:
        created = session.post(AUTHENTIK_API+'core/users/', json={'username': username, 'name': 'Temporary upload acceptance',
            'is_active': True, 'email': '', 'groups': [require(values, 'Authentication__Authentik__AdministratorGroupId')] if administrator else []}, timeout=20)
        assert created.status_code == 201; identifier = str(created.json()['pk'])
        changed = session.post(AUTHENTIK_API+f'core/users/{identifier}/set_password/', json={'password': password}, timeout=20)
        assert changed.status_code in (200, 204)
        yield username, password
    finally:
        if identifier is not None:
            current = session.get(AUTHENTIK_API+f'core/users/{identifier}/', timeout=20)
            assert current.status_code == 200 and current.json()['username'] == username
            assert session.delete(AUTHENTIK_API+f'core/users/{identifier}/', timeout=20).status_code == 204
            assert session.get(AUTHENTIK_API+f'core/users/{identifier}/', timeout=20).status_code == 404
        session.close()


def snapshot(identifier, action='upload'):
    assert str(uuid.UUID(identifier)) == identifier
    assert action in ('upload','restore')
    return query('''SELECT json_build_object('row',to_jsonb(u),'job',json_build_object('state',j.state,'code',j.error_code),
      'pending',(SELECT count(*) FROM outbox_messages o WHERE o.message_type IN
        ('ContainerFileUploadRequested','ContainerFileUploadCancellationRequested') AND o.payload_json->>'jobId'=u.id::text AND o.processed_at_utc IS NULL),
      'audits',(SELECT count(*) FROM audit_events a WHERE a.job_id=u.id AND a.action LIKE :'action'))
      FROM container_file_upload_operations u JOIN operation_jobs j ON j.id=u.id WHERE u.id=:'id'::uuid;''', id=identifier, action='containers.files.'+action+'.%')


def wait_sql(identifier, state):
    deadline = time.monotonic()+100
    while True:
        value = snapshot(identifier)
        if value['row']['state'] == state: return value
        assert value['row']['state'] not in ('Unknown', 'Failed', 'Canceled', 'Superseded') or value['row']['state'] == state
        assert time.monotonic() < deadline
        time.sleep(0.5)


def terminal(value, state, code, job):
    row = value['row']; assert row['state'] == state and row['result_code'] == code and row['completed_at_utc'] is not None
    assert not row['publish_barrier'] and row['lease_token'] is None and row['lease_expires_at_utc'] is None
    assert value['job']['state'] == job and value['pending'] == 0 and value['audits'] >= 2


def root_receipt(row):
    operation = uuid.UUID(row['id']).hex; metadata = STATE/(operation+'.json')
    assert stat.S_IMODE(trusted(metadata).st_mode) == 0o600 and metadata.stat().st_size <= 16384
    saved = json.loads(metadata.read_bytes())
    assert all(saved[key] == value for key,value in {'operationId': operation, 'containerId': row['container_id'], 'instanceId': row['instance_id'],
        'owner': row['owner_hash'], 'targetProof': row['target_proof']}.items())
    return saved

def recover(base, session, profile, api, unknown, unknown_value, source=None):
    global PHASE
    PHASE = 'different-current-administrator-permanent-unknown-recovery'
    assert unknown_value['row']['actor_subject'] != profile['subject']
    candidate = request(session, 'GET', base+f'/api/v1/files/recovery/container-upload/{unknown["id"]}', 200).json()
    assert candidate['container'] == unknown['location'] and candidate['source'] is None and candidate['target'] is None
    assert candidate['integrity'] == 'Verified'
    recovery_key = str(uuid.uuid4())
    recovery = request(session, 'POST', base+'/api/v1/files/recovery/operations', 202,
        headers={**api.headers, 'Idempotency-Key': recovery_key}, json={'kind': 'container-upload', 'intentId': unknown['id'],
        'originalVersion': candidate['version'], 'disposition': 'AcknowledgeUnknown', 'reasonCode': 'OriginalSessionLost'}).json()
    deadline = time.monotonic()+100
    while recovery['state'] != 'Ready':
        assert recovery['state'] not in ('Failed','Canceled','Disposed','Expired') and time.monotonic() < deadline
        time.sleep(0.5); recovery = request(session, 'GET', base+f'/api/v1/files/recovery/operations/{recovery["id"]}', 200).json()
    assert recovery['rootState'] == 'Unknown' and recovery['confirmation']['planHash']
    confirmed = request(session, 'POST', base+f'/api/v1/files/recovery/operations/{recovery["id"]}/confirm', 202,
        headers={**api.headers, 'Idempotency-Key': recovery['id']}, json={'planHash': recovery['confirmation']['planHash'], 'confirmed': True, 'version': recovery['version']}).json()
    while confirmed['state'] != 'Disposed':
        assert confirmed['state'] not in ('Failed','Canceled','Expired') and time.monotonic() < deadline
        time.sleep(0.5); confirmed = request(session, 'GET', base+f'/api/v1/files/recovery/operations/{confirmed["id"]}', 200).json()
    assert confirmed['resultCode'] == 'FILES_RECOVERY_DISPOSED' and confirmed['originalResolution'] == 'Failed'
    final = snapshot(unknown['id']); terminal(final, 'Failed', 'CONTAINER_UPLOAD_ADMIN_ACKNOWLEDGED', 'Failed')
    mutable = {'state','version','lease_token','lease_expires_at_utc','next_attempt_at_utc','publish_barrier','result_code','completed_at_utc'}
    assert {key:value for key,value in final['row'].items() if key not in mutable} == {key:value for key,value in unknown_value['row'].items() if key not in mutable}
    sealed = root_receipt(final['row']); assert sealed['schemaVersion'] == 3 and sealed['state'] == 'Sealed' and sealed['originalRecord'] is None
    assert sealed['recoveryId'] == uuid.UUID(confirmed['executionJobId']).hex
    assert sealed['actorHash'] == digest('actor',profile['subject']) and sealed['originalRecordHash'] == digest('original', None)
    assert not (STATE/(uuid.UUID(unknown['id']).hex+'.data')).exists()
    if source is not None: assert not (source/'unknown.bin').exists()
    return confirmed

def main():
    global PHASE
    assert os.geteuid() == 0 and (len(sys.argv) == 2 or len(sys.argv) == 4 and sys.argv[2] == '--recover'); os.umask(0o077)
    revision = sys.argv[1]; assert re.fullmatch('[a-f0-9]{40}', revision)
    base = os.environ.get('WHALEDECK_URL', 'http://192.168.100.13:8080').rstrip('/')
    anonymous = requests.Session(); anonymous.trust_env = False
    runtime = request(anonymous, 'GET', base+'/api/v1/system/status', 200).json(); assert runtime['build']['sourceRevision'] == revision
    session, profile = login(base, required_environment('VALIDATION_USERNAME'), required_environment('VALIDATION_PASSWORD'))
    api = Api(base, session)
    evidence = Path(__file__).resolve().parents[1]/'artifacts'/('container-upload-bff-'+uuid.uuid4().hex); evidence.mkdir(mode=0o700)
    def save(name, value):
        assert name in ('baseline.json', 'verified.json')
        path = evidence/name; path.write_text(json.dumps(value, ensure_ascii=False)); path.chmod(0o600)
    if len(sys.argv) == 4:
        path = Path(sys.argv[3]); repository = Path(__file__).resolve().parents[2]
        assert path.parent.parent == repository/'deploy/artifacts' and path.name == 'baseline.json'
        assert re.fullmatch('container-upload-bff-[a-f0-9]{32}', path.parent.name)
        assert stat.S_IMODE(trusted(path.parent,True).st_mode) == 0o700 and stat.S_IMODE(trusted(path).st_mode) == 0o600
        original = json.loads(path.read_bytes()); identifier = original['intents'][-1]
        value = snapshot(identifier); row = value['row']
        assert row['container_id'] == original['containerId'] and row['instance_id'] == original['instanceId']
        assert row['state'] == 'Unknown' and row['relative_path'] == 'work/unknown.bin' and row['root_started_at_utc'] is None
        unknown = {'id':identifier,'location':{'containerId':row['container_id'],'instanceId':row['instance_id'],'path':row['relative_path']}}
        confirmed = recover(base,session,profile,api,unknown,value)
        save('verified.json',{'recoveredOriginal':identifier,'recoveryId':confirmed['id'],'revision':revision})
        logout(base,session);session.close();anonymous.close()
        print(json.dumps({'containerUploadBff':'recovery-passed','revision':revision,'differentAdministratorAfterOriginalDeleted':True,
                          'originalResultFailedNotPublished':True,'durableHistoryRetained':True}),flush=True)
        return
    body = hashlib.shake_256(b'owned-complete-human-container-upload-v1').digest(owned.common.SIZE)
    sha = hashlib.sha256(body).hexdigest().upper(); intents = []
    storage_module = load(Path('/usr/local/libexec/whaledeck-file-backups.py'), 'bff_container_hdd')
    files_module = load(Path('/usr/local/libexec/whaledeck-files.py'), 'bff_container_files')
    storage = storage_module.FileBackups(files_module)
    with storage.target(create_namespace=False): pass
    configuration = storage.configuration(); history = Path(configuration['mountPoint'])/configuration['targetRelative']/'container-files-v1'
    with owned.fixture() as fixture:
        cid = fixture['identifier']; endpoint = base+f'/api/v1/containers/{cid}/files/'
        PHASE = 'current-admin-target-and-original-full-content'
        target = request(session, 'GET', endpoint+'target', 200).json(); instance = target['instanceId']
        original = request(session, 'GET', endpoint+'entry', 200, params={'instanceId': instance, 'path': 'work/original.bin'}).json()
        assert original['sizeBytes'] == len(fixture['old'])
        baseline = {'revision': revision, 'containerId': cid, 'instanceId': instance, 'fixture': str(fixture['folder']),
                    'originalSha256': hashlib.sha256(fixture['old']).hexdigest().upper(), 'bytes': len(body), 'intents': intents}
        save('baseline.json', baseline)
        def register(client, path, version):
            headers = Api(base, client).headers
            planned = request(client, 'POST', endpoint+'uploads/plan', 200, headers=headers,
                json={'instanceId': instance, 'path': path, 'sizeBytes': len(body), 'sha256': sha, 'expectedVersion': version}).json()
            key = str(uuid.uuid4()); parameters = {'planHash': planned['planHash'], 'confirmed': True, 'idempotencyKey': key}
            headers = {**headers, 'Idempotency-Key': key}
            first = request(client, 'POST', endpoint+'uploads', 202, headers=headers, json=parameters).json()
            again = request(client, 'POST', endpoint+'uploads', 202, headers=headers, json=parameters).json()
            assert first['id'] == first['jobId'] == again['id'] and first['expiresAtUtc'] == again['expiresAtUtc']
            assert first['location'] == {'containerId': cid, 'instanceId': instance, 'path': path}
            intents.append(first['id']); save('baseline.json', baseline)
            return first
        def receive(client, intent):
            headers = {**Api(base, client).headers, 'Idempotency-Key': intent['id'], 'Content-Type': 'application/octet-stream'}
            response = request(client, 'PUT', endpoint+f'uploads/{intent["id"]}/content', 202, headers=headers, data=body)
            assert 'no-store' in response.headers.get('Cache-Control', '')
            result = response.json(); assert result['id'] == intent['id'] and result['state'] == 'Ready'
        def read_back(path, version):
            response = request(session, 'GET', endpoint+'download', 200,
                params={'instanceId': instance, 'path': path, 'expectedVersion': version})
            assert len(response.content) == len(body) and response.content == body and hashlib.sha256(response.content).hexdigest().upper() == sha
        PHASE = 'overwrite-single-raw-receive-and-worker-publication'
        intent = register(session, 'work/original.bin', original['version']); before = (fixture['folder']/'original.bin').stat()
        receive(session, intent); complete = wait_sql(intent['id'], 'Succeeded'); terminal(complete, 'Succeeded', 'CONTAINER_UPLOAD_PUBLISHED', 'Succeeded')
        row = complete['row']; assert row['previous_retained'] and row['published_version']
        public = request(session, 'GET', endpoint+f'uploads/{intent["id"]}', 200).json(); assert public['state'] == 'Succeeded' and public['publishedVersion'] == row['published_version']
        output = fixture['folder']/'original.bin'; assert output.read_bytes() == body and (output.stat().st_uid, output.stat().st_gid, stat.S_IMODE(output.stat().st_mode)) == (1000,1000,0o640)
        operation = uuid.UUID(intent['id']).hex; retained = fixture['folder']/'.whaledeck-container-files-v1'/(operation+'.upload')
        assert retained.read_bytes() == fixture['old'] and retained.stat().st_ino == before.st_ino
        fixture['proofs']['original.bin'] = owned.common.fingerprint(output), sha
        fixture['private'] = '.whaledeck-container-files-v1'
        fixture['privateProofs'][retained.name] = owned.common.fingerprint(retained), baseline['originalSha256']
        root = root_receipt(row); assert root['state'] == 'Published'
        payload = STATE/(operation+'.data'); assert stat.S_IMODE(trusted(payload).st_mode) == 0o600 and payload.read_bytes() == body
        capture = history/(operation+'.json'); archive = history/(operation+'.data')
        for path in (capture, archive): assert stat.S_IMODE(trusted(path).st_mode) == 0o600
        captured = json.loads(capture.read_bytes())
        assert archive.read_bytes() == fixture['old'] and captured['sha256'] == baseline['originalSha256']
        assert all(captured[key] == value for key,value in {'operationId': operation, 'containerId': cid, 'instanceId': instance,
            'owner': row['owner_hash'], 'targetProof': row['target_proof'], 'state': 'Verified', 'captureState': 'Available'}.items())
        read_back('work/original.bin', row['published_version'])
        PHASE = 'new-file-publication-and-independent-complete-readback'
        created = register(session, 'work/new.bin', 'ABSENT'); receive(session, created)
        created_value = wait_sql(created['id'], 'Succeeded'); terminal(created_value, 'Succeeded', 'CONTAINER_UPLOAD_PUBLISHED', 'Succeeded')
        created_row = created_value['row']; assert not created_row['previous_retained']
        leaf = fixture['folder']/'new.bin'; assert leaf.read_bytes() == body and stat.S_ISREG(leaf.lstat().st_mode) and leaf.lstat().st_nlink == 1
        fixture['proofs'][leaf.name] = owned.common.fingerprint(leaf), sha
        assert root_receipt(created_row)['state'] == 'Published'
        assert not (history/(uuid.UUID(created['id']).hex+'.json')).exists()
        read_back('work/new.bin', created_row['published_version'])
        PHASE = 'explicit-never-sent-cancel-and-durable-root-fence'
        canceled = register(session, 'work/canceled.bin', 'ABSENT')
        request(session, 'POST', endpoint+f'uploads/{canceled["id"]}/cancel', 202,
            headers={**api.headers, 'Idempotency-Key': canceled['id']})
        canceled_value = wait_sql(canceled['id'], 'Canceled'); terminal(canceled_value, 'Canceled', 'CONTAINER_UPLOAD_CANCELED', 'Canceled')
        assert root_receipt(canceled_value['row'])['state'] == 'Prevented' and not (fixture['folder']/'canceled.bin').exists()
        PHASE = 'ordinary-and-anonymous-management-denial'
        request(anonymous, 'GET', endpoint+f'uploads/{intent["id"]}', 401)
        with temporary_user(False) as (username,password):
            ordinary, ordinary_profile = login(base, username, password, False); assert not ordinary_profile['isAdministrator']
            request(ordinary, 'GET', endpoint+f'uploads/{intent["id"]}', 403)
            request(ordinary, 'POST', endpoint+'uploads/plan', 403, headers=Api(base,ordinary).headers,
                json={'instanceId': instance, 'path': 'work/ordinary.bin', 'sizeBytes': len(body), 'sha256': sha, 'expectedVersion': 'ABSENT'})
            logout(base, ordinary); ordinary.close()
        PHASE = 'revoked-original-cookie-and-actual-worker-cancellation'
        with temporary_user(True) as (username,password):
            old, old_profile = login(base, username, password)
            revoked = register(old, 'work/revoked.bin', 'ABSENT')
            original_cookie = old.prepare_request(requests.Request('GET', endpoint+'target')).headers['Cookie']
            logout(base, old)
            request(anonymous, 'GET', endpoint+f'uploads/{revoked["id"]}', 401, headers={'Cookie': original_cookie})
            revoked_value = wait_sql(revoked['id'], 'Canceled'); terminal(revoked_value, 'Canceled', 'CONTAINER_UPLOAD_CANCELED', 'Canceled')
            assert root_receipt(revoked_value['row'])['state'] == 'Prevented' and not (fixture['folder']/'revoked.bin').exists()
            old.close()
            PHASE = 'stopped-container-unknown-and-deleted-original-identity'
            old, old_profile = login(base, username, password)
            unknown = register(old, 'work/unknown.bin', 'ABSENT')
            owned.common.command(['docker','stop','--time','2',cid])
            unknown_value = wait_sql(unknown['id'], 'Unknown'); assert unknown_value['row']['result_code'] == 'CONTAINER_UPLOAD_TARGET_UNAVAILABLE'
            logout(base, old); old.close()
        confirmed = recover(base, session, profile, api, unknown, unknown_value, fixture['folder'])
        owned.common.command(['docker','start',cid])
        save('verified.json', {'revision':revision, 'intents':intents, 'recoveryId':confirmed['id'], 'bytes':len(body),
            'fullOverwriteCreateReadbackHdd':True, 'explicitCancelAndLogoutWorkerFence':True, 'differentAdminAfterOriginalDeleted':True,
            'durableSqlAuditRootHddRetained':True, 'browser':False})
    PHASE = 'final-full-sso-logout'
    logout(base, session); session.close(); anonymous.close()
    print(json.dumps({'containerUploadBff':'passed','revision':revision,'bytes':len(body),'intents':len(intents),
        'fullOverwriteCreateReadbackHdd':True,'explicitCancelAndLogoutWorkerFence':True,'differentAdminAfterOriginalDeleted':True,
        'durableSqlAuditRootHddRetained':True,'ownedContainerAndSourceCleaned':True,'browser':False}),flush=True)


if __name__ == '__main__':
    try: main()
    except BaseException as error:
        import traceback
        print(json.dumps({'containerUploadBff':'failed','phase':PHASE,'failureType':type(error).__name__,'http':LAST_HTTP,
            'locations':[{'file':Path(frame.filename).name,'line':frame.lineno} for frame in traceback.extract_tb(error.__traceback__)],
            'unverifiedDataPreserved':True}),file=sys.stderr)
        raise SystemExit(1) from None
