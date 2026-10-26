#!/usr/bin/env python3
"""Installed OIDC/BFF/Worker revocation, with only an owned never-sent intent.

Run through run_with_ephemeral_admin.py. Do not change SQL, cache, production
users, permissions, service state or files. Retain immutable intent/audit/root
tombstone evidence. Cookies and login bindings never enter output.
"""
import concurrent.futures
import contextlib
import hashlib
import io
import json
import os
from pathlib import Path
import re
import sys
import time
import uuid

import requests

from validate_databases import Api
from validate_metric_continuity import query
from validate_oidc import authenticate, required_environment

PHASE = 'startup'
ROOT = 'whaledeck.files.shared'
DIRECTORY = Path('/data/WhaleDeck/files')


def snapshot(identifier):
    return query('''SELECT json_build_object(
      'id',u.id,'actor',u.actor_subject,'root',u.root_id,'path',u.relative_path,
      'state',u.state,'code',u.result_code,'owner',u.owner_hash,
      'binding',u.login_binding,'completed',u.completed_at_utc IS NOT NULL,
      'rootStarted',u.root_started_at_utc IS NOT NULL,
      'jobState',j.state,'cancelRequested',j.cancel_requested_at_utc IS NOT NULL,
      'locks',(SELECT count(*) FROM file_path_locks l WHERE l.operation_id=u.id),
      'pending',(SELECT count(*) FROM outbox_messages o
        WHERE o.message_type IN ('FileUploadRequested','FileUploadCancellationRequested')
          AND o.payload_json->>'jobId'=u.id::text AND o.processed_at_utc IS NULL),
      'audit',(SELECT count(*) FROM audit_events a WHERE a.job_id=u.id
        AND a.action='files.upload.result' AND a.result='Canceled'))
      FROM file_upload_operations u JOIN operation_jobs j ON j.id=u.id
      WHERE u.id=:'id'::uuid;''', id=identifier)


def quiet_login(base):
    with contextlib.redirect_stdout(io.StringIO()):
        return authenticate(base, required_environment('VALIDATION_USERNAME'),
                            required_environment('VALIDATION_PASSWORD'))


def logout(base, session):
    headers = Api(base, session).headers
    response = session.post(base + '/api/v1/auth/logout', headers=headers,
                            allow_redirects=False, timeout=20)
    assert response.status_code in (302, 303), 'BFF_LOGOUT_FAILED'


def main():
    global PHASE
    assert os.geteuid() == 0 and len(sys.argv) == 2
    revision = sys.argv[1]
    assert re.fullmatch('[a-f0-9]{40}', revision)
    base = os.environ.get('WHALEDECK_URL', 'http://192.168.100.13:8080').rstrip('/')
    anonymous = requests.Session(); anonymous.trust_env = False
    first = second = None
    identifier = None
    logged_out = False
    filename = 'wdv-login-liveness-' + uuid.uuid4().hex + '.bin'
    target = DIRECTORY / filename
    assert not target.exists() and not target.is_symlink()
    try:
        PHASE = 'installed-runtime-and-independent-logins'
        runtime = anonymous.get(base + '/api/v1/system/status', timeout=20)
        assert runtime.status_code == 200 and runtime.json()['build']['sourceRevision'] == revision
        first, profile = quiet_login(base)
        second, other = quiet_login(base)
        assert profile['subject'] == other['subject'] and profile['isAdministrator'] and other['isAdministrator']
        first_api = Api(base, first)
        cookie = first.prepare_request(requests.Request('GET', base + '/api/v1/auth/me')).headers['Cookie']
        assert cookie != second.prepare_request(requests.Request('GET', base + '/api/v1/auth/me')).headers['Cookie']

        PHASE = 'owned-intent-with-no-content-and-login-isolation'
        content = b'owned-login-revocation-probe-never-sent\n'
        planned = first.post(base + '/api/v1/files/uploads/plan', headers=first_api.headers,
            json={'rootId': ROOT, 'path': filename, 'sizeBytes': len(content),
                  'sha256': hashlib.sha256(content).hexdigest().upper(), 'expectedVersion': 'ABSENT'}, timeout=20)
        assert planned.status_code == 200
        key = str(uuid.uuid4())
        accepted = first.post(base + '/api/v1/files/uploads',
            headers={**first_api.headers, 'Idempotency-Key': key},
            json={'planHash': planned.json()['planHash'], 'confirmed': True, 'idempotencyKey': key}, timeout=20)
        assert accepted.status_code == 202
        identifier = str(uuid.UUID(accepted.json()['id']))
        before = snapshot(identifier)
        assert before['actor'] == profile['subject'] and before['root'] == ROOT and before['path'] == filename
        assert before['state'] == 'AwaitingUpload' and not before['completed'] and not before['cancelRequested']
        assert not before['rootStarted'] and before['locks'] > 0 and re.fullmatch('[A-F0-9]{64}', before['binding'])
        assert second.get(base + '/api/v1/files/uploads/' + identifier, timeout=20).status_code == 404
        assert not target.exists()

        PHASE = 'normal-logout-old-cookie-replay-with-other-login-live'
        logout(base, first); logged_out = True
        def replay(_):
            with requests.Session() as stale:
                stale.trust_env = False
                return stale.get(base + '/api/v1/auth/me', headers={'Cookie': cookie},
                                 allow_redirects=False, timeout=20).status_code
        with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
            assert list(pool.map(replay, range(16))) == [401] * 16
        assert first.put(base + '/api/v1/files/uploads/' + identifier + '/content',
            headers={**first_api.headers, 'Cookie': cookie, 'Content-Type': 'application/octet-stream',
                     'Idempotency-Key': identifier}, data=content, allow_redirects=False, timeout=20).status_code == 401
        current = second.get(base + '/api/v1/auth/me', timeout=20)
        assert current.status_code == 200 and current.json()['isAdministrator']
        assert second.get(base + '/api/v1/containers', timeout=20).status_code == 200

        PHASE = 'actual-worker-reconciles-revoked-original-login'
        deadline = time.monotonic() + 90
        while True:
            actual = snapshot(identifier)
            if actual['completed']: break
            assert time.monotonic() < deadline, 'WORKER_REVOCATION_TIMEOUT'
            time.sleep(.5)
        assert actual['actor'] == before['actor'] and actual['binding'] == before['binding']
        assert actual['state'] == actual['jobState'] == 'Canceled' and actual['code'] == 'FILES_CANCELED'
        assert not actual['cancelRequested'] and not actual['rootStarted']
        assert actual['locks'] == actual['pending'] == 0 and actual['audit'] == 1
        receipt = Path('/var/lib/whaledeck-files') / (uuid.UUID(identifier).hex + '.json')
        assert not receipt.is_symlink()
        proof = json.loads(receipt.read_bytes())
        assert proof['operationId'] == uuid.UUID(identifier).hex and proof['owner'] == before['owner']
        assert proof['stagedOnly'] is True and proof['sizeBytes'] == 0 and proof['sha256'] == '0' * 64
        assert proof['state'] == 'Aborted' and proof['canceledBeforeReceive'] is True
        assert not target.exists() and not target.is_symlink()
        assert second.get(base + '/api/v1/auth/me', timeout=20).status_code == 200
        assert replay(0) == 401
        print(json.dumps({'bffLoginLiveness': 'passed', 'revision': revision,
            'realOidcCookieSqlWorkerAndRootTombstone': True,
            'sameSubjectOtherLoginRemainsAdministrator': True,
            'oldCookieReplaysRefused': 18, 'noExplicitJobCancellation': True,
            'ownedTargetNeverWritten': True, 'pathLocksReleasedAndResultAudited': True,
            'intentAuditAndCancellationTombstoneRetained': True}), flush=True)
    finally:
        # Logging out the original login lets the installed Worker reconcile its
        # own intent. Never forge terminal SQL or delete uncertain root evidence.
        if first is not None and not logged_out:
            try: logout(base, first)
            finally: first.close()
        elif first is not None: first.close()
        if second is not None:
            try: logout(base, second)
            finally: second.close()
        anonymous.close()


if __name__ == '__main__':
    try: main()
    except BaseException as error:
        print(json.dumps({'bffLoginLiveness': 'failed', 'phase': PHASE,
                          'failureType': type(error).__name__}), file=sys.stderr)
        raise SystemExit(1) from None
