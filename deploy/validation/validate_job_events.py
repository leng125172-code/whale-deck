"""Actual OIDC/Gateway SSE with a UUID-scoped inert job, not a host operation.

No outbox, Agent request or external notification is created. SQL receives only
validated fixture identifiers on stdin. Cleanup never targets existing jobs.
"""
import json
import os
import re
import subprocess
import uuid

import requests
from validate_oidc import authenticate, required_environment


def sql(text):
    result = subprocess.run(['docker', 'exec', '-i', '-u', 'postgres', 'database-platform-postgres',
                             'psql', '-X', '-v', 'ON_ERROR_STOP=1', '-d', 'whaledeck'],
                            input=text, text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=25)
    assert result.returncode == 0, 'Scoped event-stream fixture SQL failed'


def rows(response):
    response.raise_for_status()
    assert response.headers.get('Content-Type', '').startswith('text/event-stream')
    for line in response.iter_lines(decode_unicode=True):
        if line.startswith('data: '):
            yield json.loads(line[6:])


def main():
    base = os.environ.get('WHALEDECK_URL', 'http://192.168.100.13:8080').rstrip('/')
    username = required_environment('VALIDATION_USERNAME')
    assert re.fullmatch(r'whaledeck-validation-[a-f0-9]{10}', username)
    session, _ = authenticate(base, username, required_environment('VALIDATION_PASSWORD'))
    identity = session.get(base + '/api/v1/auth/me', timeout=15); identity.raise_for_status()
    subject = identity.json()['subject']
    assert re.fullmatch(r'[A-Za-z0-9_-]{1,256}', subject), 'Unsafe fixture actor identifier'
    job_id = str(uuid.uuid4())
    key = 'wdv-sse-' + uuid.uuid4().hex
    guard = f"id='{job_id}' AND job_type='validation.job-events' AND actor_subject='{subject}' AND idempotency_key='{key}'"
    created = False
    try:
        created = True
        sql(f"""BEGIN;
          INSERT INTO operation_jobs(id,job_type,actor_subject,idempotency_key,state,phase,request_json,created_at_utc,version)
          VALUES ('{job_id}','validation.job-events','{subject}','{key}','Running','Working','{{}}',now(),0);
          INSERT INTO operation_job_events(job_id,sequence,state,phase,message_code,detail_json,occurred_at_utc)
          SELECT '{job_id}',n,'Running','Working','STREAM_ACCEPTANCE','{{}}',now() FROM generate_series(1,1005) n;
          COMMIT;""")
        endpoint = base + '/api/v1/jobs/' + job_id + '/events'
        with session.get(endpoint, stream=True, timeout=(10, 20)) as response:
            assert 'no-store' in response.headers.get('Cache-Control', '')
            iterator = rows(response)
            first = next(iterator)
            assert first['sequence'] == 1 and first['state'] == 'Running'
            sql(f"""BEGIN;
              DO $$ BEGIN IF NOT EXISTS(SELECT 1 FROM operation_jobs WHERE {guard}) THEN RAISE EXCEPTION 'Fixture ownership mismatch'; END IF; END $$;
              UPDATE operation_jobs SET state='Succeeded',phase='Completed',completed_at_utc=now(),version=version+1 WHERE {guard};
              INSERT INTO operation_job_events(job_id,sequence,state,phase,message_code,detail_json,occurred_at_utc)
              VALUES ('{job_id}',1006,'Succeeded','Completed','STREAM_ACCEPTANCE_DONE','{{}}',now());
              COMMIT;""")
            events = [first, *iterator]
        assert [event['sequence'] for event in events] == list(range(1, 1007)), 'Live stream lost or duplicated history'
        assert events[-1]['state'] == 'Succeeded', 'Live terminal event missing'
        with session.get(endpoint, headers={'Last-Event-ID': '1005'}, stream=True, timeout=15) as response:
            assert [event['sequence'] for event in rows(response)] == [1006]
        with session.get(endpoint, headers={'Last-Event-ID': '1006'}, stream=True, timeout=15) as response:
            assert list(rows(response)) == []
        for cursor in ('-1', 'x', '9223372036854775808'):
            response = session.get(endpoint, headers={'Last-Event-ID': cursor}, timeout=15)
            assert response.status_code == 400 and response.json().get('code') == 'VALIDATION_FAILED'
        missing = session.get(base + '/api/v1/jobs/' + str(uuid.uuid4()) + '/events', timeout=15)
        assert missing.status_code == 404
        anonymous = requests.Session(); anonymous.trust_env = False
        response = anonymous.get(endpoint, allow_redirects=False, timeout=15)
        assert response.status_code == 401, 'Anonymous stream did not fail closed'
        print(json.dumps({'realGatewayJobSse': 'passed', 'liveTerminalAnd1006Events': 'passed',
                          'lastEventIdAndNoDuplicateTail': 'passed', 'invalidCursorMissingAnonymous': 'passed', 'agentActions': 0}))
    finally:
        if created:
            sql(f"""BEGIN;
              DO $$ BEGIN IF EXISTS(SELECT 1 FROM operation_jobs WHERE id='{job_id}' AND NOT ({guard})) THEN RAISE EXCEPTION 'Fixture ownership mismatch'; END IF; END $$;
              DELETE FROM operation_job_events WHERE job_id='{job_id}' AND EXISTS(SELECT 1 FROM operation_jobs WHERE {guard});
              DELETE FROM operation_jobs WHERE {guard};
              COMMIT;""")
            print(json.dumps({'jobEventTemporaryCleanup': 'passed', 'existingJobsUnchanged': True}))


if __name__ == '__main__':
    main()
